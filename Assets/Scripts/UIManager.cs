using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// UI管理器（竖屏手机版·野餐风）：
/// 1) 玩家标记 = 3D 水果圆片（FruitPlayer：上苹果/下橙子），本类只负责按回合驱动亮暗+自转
/// 2) 胜利面板：遮罩 + 获胜玩家大字 + “再来一局”按钮
/// 所有 UI 都在运行时用代码创建——不依赖场景里的序列化引用，重建场景也不怕“丢线”。
/// 安卓适配：UI 文案为英文，统一使用引擎内置字体（LegacyRuntime.ttf，Arial 风格），
/// 全平台显示一致，不再依赖 Windows 系统字体。
public class UIManager : MonoBehaviour
{
    private FruitPlayer[] _fruitPlayers; // 水果玩家标记（上苹果/下橙子）：RefreshAll 驱动亮暗+自转
    private Text _victoryText;         // 胜利面板大字
    private GameObject _victoryPanel;  // 胜利面板整体（默认隐藏）
    private Button _restartButton;     // 再来一局按钮

    // 内置字体只加载一次，全局复用（LegacyRuntime.ttf：引擎自带 Arial 风格，全平台可用）
    private static Font _uiFont;
    private static Font UiFont
    {
        get
        {
            if (_uiFont == null)
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _uiFont;
        }
    }

    void Awake()
    {
        BuildUI();
    }

    void Start()
    {
        // 水果玩家标记接线：查找所有 FruitPlayer 并按屏幕宽高比做一次自适应布局。
        // 量纲：aspect = 宽/高（竖屏 ~0.45-0.56），FruitPlayer.Layout 内部用同一相机公式重算位置/直径。
        _fruitPlayers = FindObjectsOfType<FruitPlayer>();
        float aspect = (float)Screen.width / Screen.height;
        foreach (var fp in _fruitPlayers)
            fp.Layout(aspect);
    }

    private void BuildUI()
    {
        // 事件系统：uGUI 点击事件的分发器。缺了它按钮永远收不到点击——
        // 卡片的 OnMouseDown 走物理射线不受影响，但“再来一局”按钮必须靠它。
        // 场景与构建工具都不创建 EventSystem，故由 UI 体系在此兜底（已存在则不重复建）。
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // ===== 根画布：铺在3D画面之上，随分辨率自适应缩放 =====
        var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920); // 竖屏参考分辨率（9:16 基准）
        scaler.matchWidthOrHeight = 0f; // Match Width（竖屏短轴是宽）：字号随屏宽走，各机型观感一致
        // 量纲核对：竖屏屏比 9:16~9:20，参考 1080×1920；MatchWidth 下 UI 宽度=屏宽、
        // 高度按比例——56 号字在任何竖屏机型上视觉大小一致（横屏时代 MatchHeight 的镜像决策）

        // ===== 胜利面板：全屏遮罩 + 大字 + 再来一局按钮（默认隐藏） =====
        BuildVictoryPanel(canvas.transform);
    }

    /// 刷新玩家标记亮/暗+自转（回合状态一变就由 GameManager 调用；接口不变，只读 CurrentPlayer）
    public void RefreshAll(GameManager gm)
    {
        if (gm == null) return;

        // 驱动水果：当前回合的亮+转，等待的暗+停（FruitPlayer 内部处理 _BaseColor 与自转开关）
        if (_fruitPlayers == null) return;
        foreach (var fp in _fruitPlayers)
            fp.SetTurn(fp.PlayerNumber == gm.CurrentPlayer);
    }

    /// 显示胜利画面（GameManager 判定胜利时调用，把自己传进来给按钮接线）
    public void ShowVictory(int winnerPlayer, GameManager gm)
    {
        _victoryText.text = "Player " + winnerPlayer + " Wins!";
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

    private Text CreateText(Transform parent, string initial, int size, Vector2 pos, FontStyle style, bool glow = false, Color glowColor = default(Color))
    {
        var go = new GameObject("Txt_" + initial, typeof(Text));
        var t = go.GetComponent<Text>();
        t.font = UiFont;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.text = initial;
        t.raycastTarget = false;                   // 文字也不挡点击

        // Block4/5 文字光晕：内层集中、外层弥散，两层 Shadow 叠出"灯管"感（默认青，胜利文字传金）
        if (glow)
        {
            Color c = glowColor == default(Color) ? new Color(0.05f, 0.85f, 1.00f, 1f) : glowColor;
            var inner = go.AddComponent<UnityEngine.UI.Shadow>();
            inner.effectColor = new Color(c.r, c.g, c.b, 0.45f);
            inner.effectDistance = new Vector2(1.5f, -1.5f);
            var outer = go.AddComponent<UnityEngine.UI.Shadow>();
            outer.effectColor = new Color(c.r, c.g, c.b, 0.20f);
            outer.effectDistance = new Vector2(3.5f, -3.5f);
        }

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
        // ---- 遮罩：金色径向渐变 + 烘焙星点（Block5 运行时程序生成，零持续成本）----
        var panel = new GameObject("VictoryPanel", typeof(Image));
        var img = panel.GetComponent<Image>();
        img.sprite = MakeVictoryBackdrop();
        img.color = Color.white;                   // 渐变与透明度都烘焙在贴图里
        img.raycastTarget = true;                  // 遮罩挡住残余的卡片点击

        var rect = panel.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;              // 全屏拉伸
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        // ---- 获胜大字（金色 + 金色光晕，Block5） ----
        _victoryText = CreateText(panel.transform, "Player 1 Wins!", 72, new Vector2(0, -330), FontStyle.Bold, true, new Color(1f, 0.84f, 0.35f, 1f));
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
        var btnLabel = CreateText(btnGo.transform, "Play Again", 40, Vector2.zero, FontStyle.Bold);
        var lbl = btnLabel.rectTransform;
        lbl.anchorMin = Vector2.zero;
        lbl.anchorMax = Vector2.one;
        lbl.anchoredPosition = Vector2.zero;
        lbl.offsetMin = lbl.offsetMax = Vector2.zero;

        panel.SetActive(false);
        _victoryPanel = panel;
    }

    /// Block5 标题呼吸：小幅度（±3%）、1.5 秒周期，只在胜利面板激活时生效
    private void Update()
    {
        if (_victoryPanel == null || !_victoryPanel.activeSelf) return;
        float s = 1f + Mathf.Sin(Time.time * (Mathf.PI * 2f / 1.5f)) * 0.03f;
        _victoryText.transform.localScale = new Vector3(s, s, 1f);
    }

    /// Block5 胜利背景：512² 金色径向渐变 + 固定种子烘焙星点（程序生成，确定性）
    private Sprite MakeVictoryBackdrop()
    {
        const int s = 512;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color[s * s];
        var goldCore = new Color(1.00f, 0.84f, 0.35f, 0.92f); // 中心金亮
        var goldEdge = new Color(0.10f, 0.06f, 0.03f, 0.90f); // 边缘深棕黑
        var rng = new System.Random(20260922);                 // 固定种子：星点位置跨局一致
        var stars = new Vector2[24];
        for (int i = 0; i < stars.Length; i++)
            stars[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = x / (float)(s - 1) - 0.5f, v = y / (float)(s - 1) - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v) * 2f;
                Color col = Color.Lerp(goldCore, goldEdge, Mathf.Clamp01(r * 0.85f));
                for (int st = 0; st < stars.Length; st++)
                {
                    float dx = u - (stars[st].x - 0.5f), dy = v - (stars[st].y - 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    const float starR = 0.006f;
                    if (d < starR) col += new Color(1f, 0.9f, 0.6f, 0f) * (1f - d / starR) * 0.6f;
                }
                px[y * s + x] = col;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 512f);
    }
}
