using UnityEngine;

/// 固定俯视摄像机：
/// 正交投影、从桌面正上方垂直向下看、不响应任何输入。
/// 每帧（LateUpdate）强制重置位置和角度——就算有代码不小心动了相机，也会被立刻纠正回来。
public class CameraController : MonoBehaviour
{
    [Header("画面范围：正交相机的半高，单位=世界单位，调大看到更全")]
    public float OrthoSize = 1.75f;

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
        _cam.orthographicSize = OrthoSize;
        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = 50f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f); // 桌面外的深色“房间”底色
    }
}
