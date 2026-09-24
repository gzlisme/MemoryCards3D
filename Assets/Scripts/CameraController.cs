using UnityEngine;

/// 固定俯视摄像机（竖屏手机版）：
/// 正交投影、从桌面正上方垂直向下看、不响应任何输入。
/// 每帧（LateUpdate）强制重置位置和角度——就算有代码不小心动了相机，也会被立刻纠正回来。
public class CameraController : MonoBehaviour
{
    // 竖屏手机版：让 3×3 卡片区宽度 = 屏幕宽度，含 0.3 边距。
    // 量纲核对（写码前必算）：
    //   3×3 卡片区世界尺寸 = 2 × 间距 0.85 + 卡径 0.7 = 2.4
    //   加 0.3 边距 = 2.7（FOV 宽度目标）
    //   ortho = FOV宽 / (2 × aspect)，aspect = 屏宽 / 屏高
    //   9:16 → 2.13  9:19.5 → 2.60  9:20 → 2.67  iPad 3:4 → 1.60
    // 运行时按实际屏幕动态计算，不写死——任何竖屏设备都保证卡片区宽=屏宽。
    public float EdgePadding = 0.3f;            // 左右合计边距（世界单位）
    public const float CardAreaWidth = 2.4f;    // 3×3 卡片区边长（世界单位）

    private Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        ApplyFixedView();
    }

    // LateUpdate 在所有普通 Update 跑完后执行，适合做“最终校正”
    void LateUpdate()
    {
        ApplyFixedView();
    }

    private void ApplyFixedView()
    {
        // 位置：桌面上方正中央；角度：绕X轴转90° = 笔直向下看
        transform.position = new Vector3(0f, 4.2f, 0f);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        if (_cam == null) return;
        _cam.orthographic = true;   // 正交投影：没有透视变形，卡片比例自然

        // 竖屏动态视野：FOV 宽 = 卡片区 2.4 + 边距；宽度 = 2×ortho×aspect 反解 ortho
        float aspect = (float)Screen.width / Screen.height;
        _cam.orthographicSize = (CardAreaWidth + EdgePadding) / (2f * aspect);

        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = 50f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f); // 竖屏上下玩家区的深底背景
    }
}
