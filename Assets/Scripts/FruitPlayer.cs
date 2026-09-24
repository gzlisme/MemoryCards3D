using UnityEngine;

/// <summary>
/// 水果玩家：自转 + 亮/暗切换 + 运行时布局。
/// 挂在 FruitRoot 上（不转），驱动 SpinGroup 自转。
///
/// 关键设计：
/// - FruitRoot（本组件所在）：不转，放名字
/// - SpinGroup（子物体）：转，放圆片 + 贴图
/// - 变暗：修改渲染器材质实例的 _BaseColor × 0.4，不污染资产
/// </summary>
public class FruitPlayer : MonoBehaviour
{
    [Header("玩家编号（1 = 苹果 / 2 = 橙子）")]
    public int PlayerNumber = 1;

    [Header("自转参数")]
    public float SpinSpeed = 120f; // 度/秒 = 3 秒/圈

    private Transform _spinGroup;
    private Renderer _discRenderer;
    private Color _originalColor;
    private bool _active;

    void Awake()
    {
        // 缓存 SpinGroup（子物体）
        _spinGroup = transform.Find("SpinGroup");
        if (_spinGroup == null)
            Debug.LogError($"FruitPlayer: SpinGroup not found under {gameObject.name}");

        // 缓存 Disc 渲染器
        if (_spinGroup != null)
        {
            var disc = _spinGroup.Find("Disc");
            if (disc != null)
            {
                _discRenderer = disc.GetComponent<Renderer>();
                if (_discRenderer != null)
                    _originalColor = _discRenderer.material.GetColor("_BaseColor");
            }
        }
    }

    void Update()
    {
        if (_active && _spinGroup != null)
            _spinGroup.Rotate(0f, SpinSpeed * Time.deltaTime, 0f);
    }

    /// <summary>
    /// 运行时布局：按相机 aspect 计算 FOV 高、玩家区中心、圆片直径。
    /// 由外部（SceneBuilder 或 UIManager）在 Start 时调用一次。
    /// </summary>
    public void Layout(float aspect)
    {
        // 量纲推导（写进注释）：
        // - 相机 ortho = 2.7 / (2 × aspect)，FOV 宽 = 2.7，FOV 高 = 2.7 / aspect
        // - 玩家区高 = FOV 高 - 卡片区高 2.4（含边距），除以 2 = 单侧玩家区高
        // - 水果中心 z = ±(卡片区半高 + 玩家区半高) = ±(1.2 + 玩家区高/2)
        // - 圆片直径 = clamp(0.6 × 玩家区高, 0.45, 0.8)
        float fovHeight = 2.7f / aspect;
        float playerZoneHeight = Mathf.Max(0.3f, (fovHeight - 2.4f) / 2f);

        float z = (PlayerNumber == 1 ? 1f : -1f) * (1.2f + playerZoneHeight / 2f);
        float diameter = Mathf.Clamp(0.6f * playerZoneHeight, 0.45f, 0.8f);

        // 设置 FruitRoot 位置（y 浮在垫上一点）
        transform.position = new Vector3(0f, diameter * 0.5f + 0.002f, z);

        // 圆片缩放（mesh 是按参考半径 0.36 建的，缩放比 = diameter / 0.72 参考直径）
        if (_spinGroup != null)
        {
            var disc = _spinGroup.Find("Disc");
            if (disc != null)
            {
                float s = diameter / 0.72f;   // 量纲：mesh 参考直径 0.72（9:16），按目标直径等比缩放
                disc.localScale = new Vector3(s, 1f, s);
            }
        }
    }

    /// <summary>
    /// 切换回合状态：亮 + 转 / 暗 + 停
    /// </summary>
    public void SetTurn(bool isActive)
    {
        _active = isActive;

        if (_discRenderer != null)
        {
            Color c = _originalColor;
            c.r *= isActive ? 1f : 0.4f;
            c.g *= isActive ? 1f : 0.4f;
            c.b *= isActive ? 1f : 0.4f;
            _discRenderer.material.SetColor("_BaseColor", c);
        }
    }
}
