using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 渲染探针 V4（修复验证版）：
/// 1) 转储子网格绕向法线（预期：顶=+Y、底=-Y、侧=朝外）
/// 2) 全场景俯视渲染（预期：9张卡全部显示土褐色背面）
/// 3) 单卡翻到正面朝上的特写（预期：数字正立、无镜像）
public static class RenderProbe
{
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

        // ---------- 1. 子网格绕向法线验证 ----------
        var anyCard = Object.FindObjectsOfType<Card>()[0];
        var mesh = anyCard.GetComponent<MeshFilter>().sharedMesh;
        var verts = mesh.vertices;
        for (int sm = 0; sm < mesh.subMeshCount; sm++)
        {
            var tris = mesh.GetTriangles(sm);
            var n = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]).normalized;
            Debug.Log($"MC3D_SUBMESH idx={sm} windingNormal=({n.x:F2},{n.y:F2},{n.z:F2})");
        }

        // ---------- 2. 全场景俯视 ----------
        var camGo = new GameObject("ProbeCam");
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        SetupCam(cam, new Vector3(0f, 4.2f, 0f), 1.75f);
        Render(cam, "D:/work/unity/_verify_overview.png", 2048);

        // ---------- 3. 卡片5翻到正面朝上（游戏翻牌后玩家所见） ----------
        Card target = null;
        foreach (var c in Object.FindObjectsOfType<Card>())
            if (c.Value == 5) { target = c; break; }
        if (target != null)
        {
            target.transform.rotation = Quaternion.identity;
            var pos = target.transform.position;
            SetupCam(cam, new Vector3(pos.x, pos.y + 1.2f, pos.z), 0.45f);
            Render(cam, "D:/work/unity/_verify_faceup.png", 1024);
            target.transform.rotation = Quaternion.Euler(180f, 0f, 0f); // 复原（不保存场景，双保险）
        }

        Debug.Log("MC3D_PROBE_DONE");
    }

    private static void SetupCam(Camera cam, Vector3 pos, float orthoSize)
    {
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.orthographic = true;
        cam.orthographicSize = orthoSize;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
    }

    private static void Render(Camera cam, string path, int res)
    {
        var rt = new RenderTexture(res, res, 24);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Debug.Log("MC3D_PROBE saved " + path);
    }
}
