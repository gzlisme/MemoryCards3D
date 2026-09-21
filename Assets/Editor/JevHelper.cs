using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// TypeSafe Jev API 编辑器封装
/// 三个判断原语的同步调用 + .env 密钥管理 + 用量日志
///
/// ⚠️ 限制：使用同步 WebClient，调用时会阻塞主线程最多 10 秒。
/// 仅限编辑器工具（batchmode / Editor 脚本）使用，禁止在运行时（游戏逻辑）调用。
/// 如未来需要在运行时使用，请改为 UnityWebRequest 协程版本。
/// </summary>
public static class JevHelper
{
    private const string Endpoint = "https://api.typesafe.ai/v1/systemone";
    private const string ModelId = "jev-latest";
    private const int TimeoutMs = 10000; // 10 秒
    private const string UsageLogPath = "D:\\work\\unity\\_jev_usage.log";
    private static string _apiKey;

    // ================= 公开方法 =================

    /// <summary>
    /// Noul：是/否概率判断，返回 0.0(否)~1.0(是) 的概率值
    /// </summary>
    public static float? AskNoul(string state, string question)
    {
        var questions = new Dictionary<string, object>
        {
            { "q", new Dictionary<string, object>
                {
                    { "type", "noul" },
                    { "instructions", question }
                }
            }
        };
        string response = CallJev(state, questions);
        if (response == null) return null;
        float? result = ExtractFloat(response, "noul");
        LogUsage("AskNoul", question, response);
        return result;
    }

    /// <summary>
    /// Choice：从给定选项中选择一个，返回选中的选项 key
    /// </summary>
    public static string AskChoice(string state, string question, Dictionary<string, string> criteria)
    {
        var questions = new Dictionary<string, object>
        {
            { "q", new Dictionary<string, object>
                {
                    { "type", "choice" },
                    { "instructions", question },
                    { "criteria", criteria }
                }
            }
        };
        string response = CallJev(state, questions);
        if (response == null) return null;
        string result = ExtractString(response, "choice");
        LogUsage("AskChoice", question, response);
        return result;
    }

    /// <summary>
    /// Score：量表评分判断，返回选中的等级编号（1-based）
    /// </summary>
    public static int? AskScore(string state, string question, string[] criteria)
    {
        // 修复问题1：类型从 Dictionary&lt;string,object&gt; 改为 Dictionary&lt;string,string&gt;
        // 确保 BuildQuestionJson 能正确序列化（匹配 Dictionary&lt;string,string&gt; 分支）
        var criteriaDict = new Dictionary<string, string>();
        for (int i = 0; i < criteria.Length; i++)
            criteriaDict[(i + 1).ToString()] = criteria[i];

        var questions = new Dictionary<string, object>
        {
            { "q", new Dictionary<string, object>
                {
                    { "type", "score" },
                    { "instructions", question },
                    { "criteria", criteriaDict }
                }
            }
        };
        string response = CallJev(state, questions);
        if (response == null) return null;
        int? result = ExtractInt(response, "score");
        LogUsage("AskScore", question, response);
        return result;
    }

    // ================= 内部方法 =================

    /// <summary>
    /// 从 .env 读取 API Key（缓存，只读一次）
    /// </summary>
    private static string ApiKey
    {
        get
        {
            if (_apiKey != null) return _apiKey;
            var root = Directory.GetParent(Application.dataPath).FullName;
            var envPath = Path.Combine(root, ".env");
            if (!File.Exists(envPath)) return null;
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("TYPESAFE_API_KEY="))
                    _apiKey = line.Substring("TYPESAFE_API_KEY=".Length).Trim();
            }
            return _apiKey;
        }
    }

    /// <summary>
    /// 核心 HTTP 调用：构建请求体 → POST → 返回响应文本
    /// 修复问题3：加入 529 过载重试逻辑（最多 3 次，间隔 10 秒）
    /// </summary>
    private static string CallJev(string state, Dictionary<string, object> questions)
    {
        var key = ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("JevHelper: API key not found in .env");
            return null;
        }

        // state 长度保护：不超过 8000 字符
        if (state != null && state.Length > 8000)
            state = state.Substring(0, 8000);

        // 构建 JSON 请求体
        string stateJson = EscapeJson(state);
        var qParts = new List<string>();
        foreach (var kvp in questions)
        {
            string qJson = BuildQuestionJson(kvp.Value);
            qParts.Add("\"" + kvp.Key + "\":" + qJson);
        }
        string questionsJson = string.Join(",", qParts);
        string body = "{\"model\":\"" + ModelId + "\",\"state\":\"" + stateJson + "\",\"questions\":{" + questionsJson + "}}";

        // 529 重试：最多 3 次，每次间隔 10 秒
        const int maxRetry = 3;
        for (int attempt = 1; attempt <= maxRetry; attempt++)
        {
            try
            {
                using (var wc = new TimeoutWebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers["Authorization"] = "Bearer " + key;
                    wc.Headers["Content-Type"] = "application/json; charset=utf-8";
                    return wc.UploadString(Endpoint, "POST", body);
                }
            }
            catch (System.Net.WebException wex)
            {
                string msg = wex.Message;
                bool is529 = msg.Contains("529") || msg.Contains("system_overloaded");
                if (is529 && attempt < maxRetry)
                {
                    Debug.LogWarning(string.Format(
                        "JevHelper: 529 overloaded, retry {0}/{1} after 10s", attempt, maxRetry));
                    System.Threading.Thread.Sleep(10000);
                    continue;
                }
                Debug.LogWarning("JevHelper: CallJev failed (WebException) - " + msg);
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("JevHelper: CallJev failed - " + ex.Message);
                return null;
            }
        }
        return null; // 所有重试都失败了
    }

    /// <summary>
    /// 构建单个问题的 JSON（按 type 分发）
    /// </summary>
    private static string BuildQuestionJson(object questionData)
    {
        var dict = questionData as Dictionary<string, object>;
        if (dict == null) return "{}";

        var sb = new StringBuilder();
        sb.Append("{");
        bool first = true;
        foreach (var kvp in dict)
        {
            if (!first) sb.Append(",");
            first = false;
            sb.Append("\"").Append(kvp.Key).Append("\":");
            if (kvp.Value is string)
                sb.Append("\"").Append(EscapeJson(kvp.Value.ToString())).Append("\"");
            else if (kvp.Value is Dictionary<string, string>)
            {
                // criteria 字典：key→description
                sb.Append("{");
                bool cFirst = true;
                foreach (var ckvp in (Dictionary<string, string>)kvp.Value)
                {
                    if (!cFirst) sb.Append(",");
                    cFirst = false;
                    sb.Append("\"").Append(ckvp.Key).Append("\":\"").Append(EscapeJson(ckvp.Value)).Append("\"");
                }
                sb.Append("}");
            }
            else
                sb.Append(EscapeJson(kvp.Value.ToString()));
        }
        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>
    /// JSON 字符串转义：处理引号、反斜杠、控制字符
    /// </summary>
    private static string EscapeJson(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从 JSON 响应中提取指定 key 的浮点值。
    /// ⚠️ 使用正则匹配，仅适用于扁平结构。若响应变复杂，应改用 JSON 库。
    /// </summary>
    private static float? ExtractFloat(string json, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(json,
            "\"" + key + "\"\\s*:\\s*([0-9]+\\.?[0-9]*)");
        if (match.Success)
        {
            float val;
            if (float.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out val))
                return val;
        }
        return null;
    }

    /// <summary>
    /// 从 JSON 响应中提取指定 key 的字符串值。
    /// ⚠️ 使用正则匹配，仅适用于扁平结构。若响应变复杂，应改用 JSON 库。
    /// </summary>
    private static string ExtractString(string json, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(json,
            "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 从 JSON 响应中提取指定 key 的整数值。
    /// ⚠️ 使用正则匹配，仅适用于扁平结构。若响应变复杂，应改用 JSON 库。
    /// </summary>
    private static int? ExtractInt(string json, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(json,
            "\"" + key + "\"\\s*:\\s*(-?[0-9]+)");
        if (match.Success)
        {
            int val;
            if (int.TryParse(match.Groups[1].Value, out val))
                return val;
        }
        return null;
    }

    /// <summary>
    /// 写用量日志：时间戳 | 方法名 | 问题(前50字符) | input_tokens | 估算费用
    /// </summary>
    private static void LogUsage(string method, string question, string response)
    {
        try
        {
            int tokens = 0;
            var match = System.Text.RegularExpressions.Regex.Match(response,
                "\"input_tokens\"\\s*:\\s*(\\d+)");
            if (match.Success) tokens = int.Parse(match.Groups[1].Value);

            string q = question != null && question.Length > 50
                ? question.Substring(0, 50) : question;
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss} | {1} | {2} | {3} tokens",
                DateTime.Now, method, q, tokens);
            File.AppendAllText(UsageLogPath, line + Environment.NewLine);
        }
        catch { /* 日志写入失败不阻塞 */ }
    }

    /// <summary>
    /// 带超时的 WebClient 子类（10 秒）
    /// </summary>
    private class TimeoutWebClient : System.Net.WebClient
    {
        protected override System.Net.WebRequest GetWebRequest(Uri address)
        {
            var req = base.GetWebRequest(address);
            req.Timeout = TimeoutMs;
            return req;
        }
    }
}
