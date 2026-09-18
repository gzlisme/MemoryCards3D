using System.Collections;
using UnityEngine;

/// 单张卡片：
/// - 存数字、管正/背面状态
/// - 处理点击并转发给 GameManager 统一判定（卡片自己不做规则判断，职责单一）
/// - 用协程执行“抬起 + 绕X轴翻转180° + 落回”的翻牌动画（SmoothStep 缓动 = EaseInOut）
public class Card : MonoBehaviour
{
    [Header("卡片数据")]
    public int Value;               // 正面数字 1-9
    public bool IsFaceUp;           // 当前是否正面朝上

    [Header("动画参数")]
    public float FlipDuration = 0.4f;    // 单次翻牌时长（需求要求 0.3~0.5s）
    public float FlipLiftHeight = 0.45f; // 翻转中抬起的高度：防边缘切进桌面，顺带展示厚度剖面

    // 卡片两个固定姿态：
    // 正面朝上 = 原始姿态（数字面朝 +Y，数字为正立方向）
    // 背面朝上 = 绕X轴转180°（数字面压向桌面，玩家看到的是土褐色背面）
    private static readonly Quaternion FaceUpRot = Quaternion.identity;
    private static readonly Quaternion FaceDownRot = Quaternion.Euler(180f, 0f, 0f);

    private GameManager _manager;
    private Coroutine _flipRoutine;
    private Vector3 _homePos;   // 本卡在3x3阵列中的“家位置”（翻牌动画后要落回这里）

    void Awake()
    {
        _homePos = transform.position;
        // 统一归位：编辑态存进场景的姿态可能有差异，开局一律按状态摆正
        transform.rotation = IsFaceUp ? FaceUpRot : FaceDownRot;
    }

    void Start()
    {
        // 点击要转交裁判，先找到 GameManager
        _manager = FindObjectOfType<GameManager>();
    }

    // Unity 自动回调：物体带 Collider 且被鼠标点到时触发
    void OnMouseDown()
    {
        TryFlip();
    }

    /// 点击入口：状态合法才把请求交给裁判
    public void TryFlip()
    {
        if (IsFaceUp) return;         // 已翻开的卡不能再点
        if (_manager == null) return; // 找不到裁判就忽略
        _manager.HandleCardClicked(this);
    }

    /// 翻到正面（带动画）。由 GameManager 接受点击后调用
    public void FlipUp() { StartFlip(true); }

    /// 翻回背面（带动画）
    public void FlipDown() { StartFlip(false); }

    /// 瞬间复位为背面（不播动画）——重开局时用
    public void SetFaceDownImmediate()
    {
        if (_flipRoutine != null) { StopCoroutine(_flipRoutine); _flipRoutine = null; }
        IsFaceUp = false;
        transform.rotation = FaceDownRot;
        transform.position = _homePos;
    }

    public Vector3 GetHomePosition() { return _homePos; }

    /// 重新洗牌时更新“家位置”（并把卡片瞬移过去）
    public void SetHomePosition(Vector3 pos)
    {
        _homePos = pos;
        transform.position = pos;
    }

    private void StartFlip(bool faceUp)
    {
        // 若上一次翻牌还没播完就来了新指令：打断旧协程，从“当前姿态”接着翻，不会瞬移
        if (_flipRoutine != null) StopCoroutine(_flipRoutine);
        _flipRoutine = StartCoroutine(FlipRoutine(faceUp));
    }

    // 翻牌动画本体：
    // - 旋转用 Slerp 球面插值（四元数专用，避免欧拉角万向节问题）
    // - 缓动 t²(3-2t) 即 SmoothStep，起手慢→中间快→收尾慢，等价 EaseInOut
    private IEnumerator FlipRoutine(bool faceUp)
    {
        Quaternion from = transform.rotation;
        Quaternion to = faceUp ? FaceUpRot : FaceDownRot;
        float elapsed = 0f;

        while (elapsed < FlipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / FlipDuration);
            float eased = t * t * (3f - 2f * t);   // SmoothStep 缓动

            transform.rotation = Quaternion.Slerp(from, to, eased);
            // sin(πt)：起止贴桌面、翻转中段抬到最高，一气呵成
            Vector3 pos = _homePos;
            pos.y += Mathf.Sin(Mathf.PI * t) * FlipLiftHeight;
            transform.position = pos;
            yield return null;   // 等下一帧
        }

        // 收尾：精确落位，杜绝浮点误差累积
        transform.rotation = to;
        transform.position = _homePos;
        IsFaceUp = faceUp;
        _flipRoutine = null;

        // 翻到正面后通知裁判“动画播完了，请判定”
        if (faceUp && _manager != null)
            _manager.OnCardFlipFinished(this);
    }
}
