using UnityEngine;
using UnityEngine.UI;

/// UI管理器：
/// 1) 顶部信息栏：当前玩家 / 请找数字 / 连对计数
/// 2) 胜利面板：遮罩 + 获胜玩家大字 + “再来一局”按钮
/// 所有 UI 都在运行时用代码创建——不依赖场景里的序列化引用，重建场景也不怕“丢线”。
/// 中文显示：引擎内置字体只有拉丁字符，所以加载操作系统字体（微软雅黑）做动态字体。
public class UIManager : MonoBehaviour
{
    private Text _turnText;            // “玩家1回合”
    private Text _expectText;          // “请找：3”
    private Text _countText;           // “连对：2/9”
    private Text _victoryText;         // 胜利面板大字
    private GameObject _victoryPanel;  // 胜利面板整体（默认隐藏）
    private Button _restartButton;     // 再来一局按钮

    // 系统中文字体只加载一次，全局复用
    private static Font _cnFont;
    private static Font CnFont
    {
        get
        {
            if (_cnFont == null)
            {
                // Windows 自带微软雅黑；万一加载失败，退回内置字体（仅保证英文可显示）
                _cnFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 28);
                if (_cnFont == null)
                    _cnFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _cnFont;
        }
    }

    void Awake()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        // ===== 根画布：铺在3D画面之上，随分辨率自适应缩放 =====
        var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); // 以1080p为基准，任何分辨率等比缩放

        // ===== 顶部信息底板（半透明压暗，保证文字可读性） =====
        var bar = CreateBar(canvas.transform);

        // 三行文字：回合（最大最显眼） / 期待数字 / 连对计数
        _turnText = CreateText(bar.transform, "玩家1回合", 46, new Vector2(0, -46), FontStyle.Bold);
        _expectText = CreateText(bar.transform, "请找：1", 34, new Vector2(0, -104), FontStyle.Normal);
        _countText = CreateText(bar.transform, "连对：0/9", 30, new Vector2(0, -146), FontStyle.Normal);

        // ===== 胜利面板：全屏遮罩 + 大字 + 再来一局按钮（默认隐藏） =====
        BuildVictoryPanel(canvas.transform);
    }

    /// 刷新顶部三行信息（回合状态一变就由 GameManager 调用）
    public void RefreshAll(GameManager gm)
    {
        if (_turnText == null) return;
        _turnText.text = "玩家" + gm.CurrentPlayer + "回合";
        _expectText.text = "请找：" + gm.ExpectedNumber;
        _countText.text = "连对：" + gm.CorrectCount + "/" + GameManager.TotalCards;
    }

    /// 显示胜利画面（GameManager 判定胜利时调用，把自己传进来给按钮接线）
    public void ShowVictory(int winnerPlayer, GameManager gm)
    {
        _victoryText.text = "玩家" + winnerPlayer + " 获胜！";
        _victoryPanel.SetActive(true);

        // 先清空旧监听，防止重复注册；按钮点击 = 重开一局
        _restartButton.onClick.RemoveAllListeners();
        _restartButton.onClick.AddListener(gm.RestartGame);
    }

    public void HideVictory()
    {
        _victoryPanel.SetActive(false);
    }

    /// 测试/调试钩子：胜利面板当前是否显示
    public bool IsVictoryVisible => _victoryPanel != null && _victoryPanel.activeSelf;

    // ================= 私有搭建工具方法 =================

    private GameObject CreateBar(Transform parent)
    {
        var go = new GameObject("TopBar", typeof(Image));
        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.35f); // 半透明黑
        img.raycastTarget = false;                // 底板绝不挡3D场景的卡片点击（重要！）

        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);   // 顶边中点向右上展开
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0, -10);
        rect.sizeDelta = new Vector2(0, 210);     // 宽=锚点区间撑满，高210
        return go;
    }

    private Text CreateText(Transform parent, string initial, int size, Vector2 pos, FontStyle style)
    {
        var go = new GameObject("Txt_" + initial, typeof(Text));
        var t = go.GetComponent<Text>();
        t.font = CnFont;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.text = initial;
        t.raycastTarget = false;                   // 文字也不挡点击

        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f); // 顶边中点锚定
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(1600, size + 24);
        return t;
    }

    private void BuildVictoryPanel(Transform parent)
    {
        // ---- 遮罩 ----
        var panel = new GameObject("VictoryPanel", typeof(Image));
        var img = panel.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.78f);  // 深色遮罩盖住场景，突出胜利信息
        img.raycastTarget = true;                  // 遮罩挡住残余的卡片点击

        var rect = panel.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;              // 全屏拉伸
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        // ---- 获胜大字（金色） ----
        _victoryText = CreateText(panel.transform, "玩家1 获胜！", 72, new Vector2(0, -330), FontStyle.Bold);
        _victoryText.color = new Color(1f, 0.84f, 0.4f);

        // ---- 再来一局按钮 ----
        var btnGo = new GameObject("RestartButton", typeof(Image), typeof(Button));
        var btnRect = btnGo.GetComponent<RectTransform>();
        btnRect.SetParent(panel.transform, false);
        btnRect.anchorMin = btnRect.anchorMax = new Vector2(0.5f, 0.5f); // 画面中心
        btnRect.pivot = new Vector2(0.5f, 0.5f);
        btnRect.anchoredPosition = new Vector2(0, -160);
        btnRect.sizeDelta = new Vector2(360, 96);

        btnGo.GetComponent<Image>().color = new Color(0.85f, 0.62f, 0.24f);
        _restartButton = btnGo.GetComponent<Button>();

        // 按钮文字：作为子物体四向拉伸铺满按钮
        var btnLabel = CreateText(btnGo.transform, "再来一局", 40, Vector2.zero, FontStyle.Bold);
        var lbl = btnLabel.rectTransform;
        lbl.anchorMin = Vector2.zero;
        lbl.anchorMax = Vector2.one;
        lbl.anchoredPosition = Vector2.zero;
        lbl.offsetMin = lbl.offsetMax = Vector2.zero;

        panel.SetActive(false);
        _victoryPanel = panel;
    }
}
