using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 场景程序化搭建工具（编辑器专用，放 Assets/Editor 下不会进打包）。
/// 一次把整个游戏场景用代码生成：地板、课桌、相机、灯光、9张卡片（含贴图/材质/预制体）。
/// 命令行用法：
///   Tuanjie.exe -batchmode -quit -projectPath <项目> -executeMethod SceneBuilder.BuildAll -logFile <日志>
/// 编辑器内用法：菜单 Tools/MemoryCards3D/Build All
public static class SceneBuilder
{
    // ===== 常量集中管理（调画面手感只改这里） =====
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string ArtDir = "Assets/Art";
    private const string PrefabPath = "Assets/Prefabs/Card.prefab";

    private const float TableWidth = 4.4f;        // 桌面 X 尺寸
    private const float TableDepth = 3.2f;        // 桌面 Z 尺寸
    private const float TableThickness = 0.08f;    // 桌面板真实厚度
    private const float CameraOrthoSize = 1.75f;   // 正交相机半高

    private const float CardRadius = 0.35f;        // 卡片半径（圆柱 XZ 缩放）
    private const float CardThickness = 0.04f;     // 卡片厚度：必须真实厚度，不能拿平面糊弄
    private const float GridSpacing = 0.85f;       // 3x3 阵列格距

    [MenuItem("Tools/MemoryCards3D/Build All")]
    public static void BuildAll()
    {
        Debug.Log("MC3D_BUILD_START");
        EnsureFolders();

        // 每次从空场景全新搭建：工具“幂等”，重复执行结果一致
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildFoundation();
        BuildCards();

        // 运行时管理器也一并放进场景（脚本存在才加，保证分阶段开发时本工具始终能跑）
        TryAddManagerObject("GameManager");
        TryAddManagerObject("UIManager", "GameManager");

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();

        Debug.Log("MC3D_BUILD_OK: scene saved to " + ScenePath);
    }

    // ================= 地基：地板 / 课桌 / 相机 / 灯光 =================

    private static void BuildFoundation()
    {
        // ---------- 地面（房间地板）：画面边缘露出桌面以外时，深色地板比“虚空”自然 ----------
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(30f, 0.2f, 30f);
        floor.transform.position = new Vector3(0f, -0.4f, 0f);
        floor.GetComponent<MeshRenderer>().sharedMaterial =
            CreateSolidMaterial("FloorMat", new Color(0.02f, 0.023f, 0.05f), 0.05f); // 近黑带蓝：衬托深色桌面
        floor.GetComponent<MeshRenderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off; // 地板不投影，省性能

        // ---------- 课桌：带真实厚度的木板桌面 ----------
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Table";
        // Cube 本体 1x1x1，靠缩放得到真实尺寸；位置让“桌面顶面”正好落在 y=0
        table.transform.localScale = new Vector3(TableWidth, TableThickness, TableDepth);
        table.transform.position = new Vector3(0f, -TableThickness * 0.5f, 0f);
        table.GetComponent<MeshRenderer>().sharedMaterial = CreateArenaMaterial();

        // ---------- 相机：固定正交俯视 ----------
        var camGo = new GameObject("MainCamera");
        camGo.tag = "MainCamera";              // OnMouseDown 点击拾取需要主相机标记
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();   // 一个场景只要一个声音监听器
        var ctrl = camGo.AddComponent<CameraController>();
        ctrl.OrthoSize = CameraOrthoSize;
        // 运行时由 CameraController 强制锁定；这里也摆一次，让“编辑态”看场景就是对的
        camGo.transform.position = new Vector3(0f, 4.2f, 0f);
        camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.orthographic = true;
        cam.orthographicSize = CameraOrthoSize;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.045f, 0.10f, 1f); // 深蓝黑“竞技场房间”底色

        // ---------- 方向光（自然光）+ 环境 ----------
        var lightGo = new GameObject("SunLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;          // 实时软阴影：卡片在桌面上的投影靠它
        light.intensity = 1.3f;
        light.color = new Color(0.78f, 0.86f, 1f);  // 冷白偏蓝：数字竞技场的“顶灯”
        light.shadowStrength = 0.85f;               // 阴影浓度：深色桌面上也要保证投影可读
        // 斜着照：有角度才有明暗面和斜向投影，3D体积感靠这个
        lightGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        RenderSettings.sun = light;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; // 单一环境色，明暗可控
        RenderSettings.ambientLight = new Color(0.20f, 0.22f, 0.32f);         // 深蓝紫基调的暗部环境光

        // Android 质量档（Performant）默认关闭主光阴影——验收要求“卡片投影明显”，强制开启三档。
        // URP14 的 supportsMainLightShadows 属性只读，序列化字段要走 SerializedObject 通道。
        foreach (string urpPath in new[] { "Assets/Settings/URP-Performant.asset", "Assets/Settings/URP-Balanced.asset", "Assets/Settings/URP-HighFidelity.asset" })
        {
            var urp = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(urpPath);
            if (urp == null) continue;
            var so = new SerializedObject(urp);
            var prop = so.FindProperty("m_MainLightShadowsSupported");
            if (prop != null && !prop.boolValue)
            {
                prop.boolValue = true;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(urp);
            }
        }

        Debug.Log("MC3D_STEP_OK: foundation built");
    }

    // ================= 卡片：贴图 → 材质 → 预制体 → 3x3 阵列 =================

    private static void BuildCards()
    {
        // ---------- 1. 卡片专属圆柱网格（代码手工构造） ----------
        // 实测（CylinderProbe）：团结引擎内置 Cylinder 只有1个子网格——顶/底/侧共用1个材质，
        // 做不了“数字面/土褐背面/纸板切边”三材质。所以自己造一个三子网格圆柱：
        //   submesh 0=侧面  1=顶面(数字)  2=底面(背面)
        // 自己造还有个好处：子网格顺序完全由代码定义，材质下标是确定值，不依赖引擎内部实现。
        var mesh = BuildCardMesh(CardRadius, CardThickness * 0.5f, 32);
        string meshPath = $"{ArtDir}/CardMesh.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
            AssetDatabase.DeleteAsset(meshPath);          // 幂等：先删旧资产再建新的
        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();
        // 顶面UV是我们自己布的：贴图中央(0.5,0.5)为圆心、半径0.5的圆盘——数字贴图直接画中心即可
        Debug.Log("MC3D_UV_INFO custom-mesh submeshes=[side,top,bottom] capUV=(0,0,1,1) deterministic");

        // ---------- 2. 材质：背面（土褐纸壳）、侧边（纸板切面）、9张数字面 ----------
        var backMat = CreateBackMaterial();
        var edgeMat = CreateSolidMaterial("CardEdgeMat", new Color(0.45f, 0.32f, 0.21f), 0.08f);
        var faceMats = new Material[9];
        for (int digit = 1; digit <= 9; digit++)
            faceMats[digit - 1] = CreateFaceMaterial(digit);

        // ---------- 3. 卡片预制体（网格已是最终尺寸，不用缩放） ----------
        var card = new GameObject("Card");
        var mf = card.AddComponent<MeshFilter>();
        mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        var mr = card.AddComponent<MeshRenderer>();

        // 点击碰撞体：外接盒。圆卡点边也算点中，点击手感宽容
        var box = card.AddComponent<BoxCollider>();
        box.size = new Vector3(CardRadius * 2f, CardThickness, CardRadius * 2f);
        box.center = Vector3.zero;

        card.AddComponent<Card>(); // 卡片逻辑组件（出厂 Value=0，实例上再赋）

        // 材质按子网格下标对号入座：0侧 1顶 2底（顶面先放数字1占位，实例各自覆盖）
        mr.sharedMaterials = new[] { edgeMat, faceMats[0], backMat };

        PrefabUtility.SaveAsPrefabAsset(card, PrefabPath); // 覆盖保存，幂等
        Object.DestroyImmediate(card);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        // ---------- 4. 3x3 阵列：按 1-9 顺序落格（随机性归运行时，见 GameManager.Start） ----------
        // 构建期不洗牌：场景保持确定性布局，开局由 GameManager 统一随机
        var slots = new List<Vector3>();
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                slots.Add(new Vector3((col - 1) * GridSpacing, CardThickness * 0.5f, (row - 1) * GridSpacing));

        for (int digit = 1; digit <= 9; digit++)
        {
            var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            go.name = "Card_" + digit;
            var c = go.GetComponent<Card>();
            c.Value = digit;
            c.IsFaceUp = false;
            go.transform.position = slots[digit - 1];
            // 编辑态就背面朝上：场景视图直观，开局姿态也正确（数字面压向桌面）
            go.transform.rotation = Quaternion.Euler(180f, 0f, 0f);

            // 覆盖本实例的“数字面”材质（取副本数组改，避免误改预制体）
            var rmats = go.GetComponent<MeshRenderer>().sharedMaterials;
            rmats[1] = faceMats[digit - 1]; // 1=顶面
            go.GetComponent<MeshRenderer>().sharedMaterials = rmats;
        }

        Debug.Log("MC3D_STEP_OK: cards built");
    }

    /// 用代码造卡片圆柱网格：3个子网格（0侧面/1顶面/2底面），直接建成最终尺寸。
    /// 模型 = 顶点 + 三角形 + UV + 子网格。顶/侧/底顶点互不共享 → 法线在卡边是锐利的。
    private static Mesh BuildCardMesh(float radius, float halfHeight, int segments)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var side = new List<int>();
        var top = new List<int>();
        var bottom = new List<int>();

        // ---------- 侧面：竖直圆柱面，UV 横向环绕 ----------
        for (int i = 0; i <= segments; i++)
        {
            float ang = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(ang) * radius, z = Mathf.Sin(ang) * radius;
            verts.Add(new Vector3(x, -halfHeight, z)); uvs.Add(new Vector2(i / (float)segments, 0f)); // 底圈
            verts.Add(new Vector3(x,  halfHeight, z)); uvs.Add(new Vector2(i / (float)segments, 1f)); // 顶圈
        }
        for (int i = 0; i < segments; i++)
        {
            int b0 = i * 2, t0 = i * 2 + 1, b1 = (i + 1) * 2, t1 = (i + 1) * 2 + 1;
            // 绕向修正（探针实测铁律）：Unity 可见面=从观察方向看顶点逆时针排布。
            // 初版“顺时针=正面”判断反了，导致所有面朝内/朝下，产生镜像数字与透视穿帮。
            side.AddRange(new[] { b0, t0, b1, t0, t1, b1 }); // 交换每组三角形的2、3顶点→法线朝外
        }

        // ---------- 顶面（数字面）：三角扇；UV=以贴图(0.5,0.5)为圆心的圆盘 ----------
        int topC = verts.Count; verts.Add(new Vector3(0, halfHeight, 0)); uvs.Add(new Vector2(0.5f, 0.5f));
        int topR = verts.Count;
        for (int i = 0; i <= segments; i++)
        {
            float ang = i / (float)segments * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(ang) * radius, halfHeight, Mathf.Sin(ang) * radius));
            uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
        }
        for (int i = 0; i < segments; i++)
            top.AddRange(new[] { topC, topR + i + 1, topR + i }); // 绕向修正：正面朝上(+Y)

        // ---------- 底面（背面）：同样的扇形，绕向反过来让面朝下 ----------
        int botC = verts.Count; verts.Add(new Vector3(0, -halfHeight, 0)); uvs.Add(new Vector2(0.5f, 0.5f));
        int botR = verts.Count;
        for (int i = 0; i <= segments; i++)
        {
            float ang = i / (float)segments * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(ang) * radius, -halfHeight, Mathf.Sin(ang) * radius));
            uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
        }
        for (int i = 0; i < segments; i++)
            bottom.AddRange(new[] { botC, botR + i, botR + i + 1 }); // 绕向修正：正面朝下(-Y)

        var mesh = new Mesh { name = "CardMesh" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(side, 0);
        mesh.SetTriangles(top, 1);
        mesh.SetTriangles(bottom, 2);
        mesh.RecalculateNormals();  // 接缝顶点不共享 → 卡片边缘是锐利切边（要的效果）
        mesh.RecalculateBounds();
        return mesh;
    }

    // ================= 程序化贴图 =================

    /// 卡面贴图：深蓝黑底 + 字体渲染霓虹数字（分档配色）+ 同色细环。
    /// 数字用引擎内置字体 LegacyRuntime.ttf 渲染：两遍校准字号（墨迹高度=贴图高70%）→
    /// 字体图集提取字形像素 → 度量包络居中 → alpha 遮罩两级模糊发光。
    /// 居中与高度全部来自字体度量数据（数学保证），见日志 MC3D_DIGIT_CENTER。
    private static Texture2D GenerateFaceTexture(int digit, Color neon)
    {
        const int size = 512;
        const int rtRes = 1024;    // 字形渲染临时 RT 边长
        // ---------- 1. TextMesh 渲染数字到透明 RT（白字，alpha=覆盖度） ----------
        // 弃用字体图集 API（RequestCharactersInTexture/GetCharacterInfo 在大字号下 uv 异常），
        // 改走 TextMesh+相机渲染：与游戏 UI 同一渲染路径，最稳。
        var textGo = new GameObject("FontDigit");
        textGo.hideFlags = HideFlags.HideAndDontSave;
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textGo.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material; // 代码创建必须手动挂字体材质
        tm.fontSize = 100;
        tm.characterSize = 1f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        tm.text = digit.ToString();

        var camGo = new GameObject("FontCam");
        camGo.hideFlags = HideFlags.HideAndDontSave;
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = 8f;                        // 视野 16×16 单位，100px 字形绰绰有余
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);  // 透明底：alpha 即字形覆盖度
        cam.transform.position = new Vector3(0f, 0f, -3f); // 默认朝 +Z 看，正对文字

        var rt = RenderTexture.GetTemporary(rtRes, rtRes, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var raw = new Texture2D(rtRes, rtRes, TextureFormat.RGBA32, false);
        raw.ReadPixels(new Rect(0, 0, rtRes, rtRes), 0, 0);
        raw.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(textGo);

        // ---------- 2. 量墨迹包围盒（alpha > 0.1） ----------
        var rawPx = raw.GetPixels();
        int minX = rtRes, maxX = -1, minY = rtRes, maxY = -1;
        for (int y = 0; y < rtRes; y++)
            for (int x = 0; x < rtRes; x++)
                if (rawPx[y * rtRes + x].a > 0.1f)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        if (maxX < 0)
        {
            Debug.LogError($"MC3D_FONT_FAIL: digit {digit} 字形未渲染（TextMesh 无输出）");
            // 兜底：返回纯深底贴图（游戏仍可运行，数字缺失待人工介入）
            var fallback = new Texture2D(size, size);
            var fb = new Color[size * size];
            for (int i = 0; i < fb.Length; i++) fb[i] = new Color(0.045f, 0.06f, 0.115f);
            fallback.SetPixels(fb); fallback.Apply();
            return fallback;
        }
        int inkW = maxX - minX + 1, inkH = maxY - minY + 1;

        // ---------- 3. 重采样墨迹区域 → 70% 高度、等比宽、居中放入 512² ----------
        int targetH = (int)(size * 0.70f);           // 358
        int targetW = Mathf.Max(1, Mathf.RoundToInt(inkW * (float)targetH / inkH));
        int dstX = (size - targetW) / 2, dstY = (size - targetH) / 2;
        var mask = new float[size * size];              // 重采样后的 alpha 遮罩（0..1）
        for (int ty = 0; ty < targetH; ty++)
        {
            int sy = minY + (int)((ty + 0.5f) * inkH / targetH);
            for (int tx = 0; tx < targetW; tx++)
            {
                int sx = minX + (int)((tx + 0.5f) * inkW / targetW);
                mask[(dstY + ty) * size + (dstX + tx)] = rawPx[sy * rtRes + sx].a;
            }
        }
        Debug.Log($"MC3D_DIGIT_CENTER value={digit} inkBox={inkW}x{inkH} dst=({dstX},{dstY}) " +
                  $"center=({dstX + targetW / 2f - (size - 1) / 2f:F1},{dstY + targetH / 2f - (size - 1) / 2f:F1})px");

        // ---- 遮罩两级模糊 → 霓虹光晕（假Bloom，零运行时成本） ----
        var halo1 = BoxBlur(mask, size, 18);
        var halo2 = BoxBlur(mask, size, 44);

        // ---- 合成：深底 + 细环 + 光晕 + 过曝字芯 ----
        var px = new Color[size * size];
        var deepBase = new Color(0.045f, 0.06f, 0.115f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1) - 0.5f;
                float v = y / (float)(size - 1) - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v);
                int i = y * size + x;

                // 1) 深底 + 微噪
                Color col = deepBase * (1f + (Mathf.PerlinNoise(x * 0.3f, y * 0.3f) - 0.5f) * 0.10f);

                // 2) 同色细环（r=0.40，窄细、低亮度陪衬）
                col = Color.Lerp(col, neon * 0.55f, Sstep(0.004f, 0f, Mathf.Abs(r - 0.40f)) * 0.75f);

                // 3) 两级霓虹光晕（假Bloom）
                col += neon * (halo1[i] * 0.42f + halo2[i] * 0.12f);

                // 4) 字形本体：过曝霓虹芯（RT 渲染自带 AA，边缘天然平滑）
                if (mask[i] > 0f) col = Color.Lerp(col, neon * 1.12f, mask[i]);

                px[i] = col;
            }
        }

        var tex = new Texture2D(size, size);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// 从纹理中截取一个矩形区域（直接 GetPixels 失败时经 RT 中转兜底）
    private static Color[] GrabTextureRegion(Texture2D src, int gx, int gy, int gw, int gh)
    {
        try { return src.GetPixels(gx, gy, gw, gh); }
        catch
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tmp = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;
            var px = tmp.GetPixels(gx, gy, gw, gh);
            Object.DestroyImmediate(tmp);
            RenderTexture.ReleaseTemporary(rt);
            return px;
        }
    }

    /// 分离式盒模糊 ×2（先横后竖，两轮≈高斯）
    private static float[] BoxBlur(float[] src, int size, int radius)
    {
        var tmp = new float[size * size];
        var dst = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0f; int n = 0;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int xx = x + dx; if (xx < 0 || xx >= size) continue;
                    sum += src[y * size + xx]; n++;
                }
                tmp[y * size + x] = sum / n;
            }
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float sum = 0f; int n = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int yy = y + dy; if (yy < 0 || yy >= size) continue;
                    sum += tmp[yy * size + x]; n++;
                }
                dst[y * size + x] = sum / n;
            }
        return dst;
    }

    private static Material CreateFaceMaterial(int digit)
    {
        // 三档配色：1-3 霓虹青 / 4-6 霓虹品红 / 7-9 金
        Color neon = digit <= 3 ? new Color(0.05f, 0.85f, 1.00f)
                  : digit <= 6 ? new Color(1.00f, 0.25f, 0.80f)
                  : new Color(1.00f, 0.76f, 0.22f);
        string texPath = $"{ArtDir}/CardFace_{digit}.png";
        File.WriteAllBytes(texPath, GenerateFaceTexture(digit, neon).EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);
        var mat = NewLitMaterial($"{ArtDir}/CardFaceMat_{digit}.mat");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        mat.SetFloat("_Smoothness", 0.10f); // 微光泽衬托霓虹
        return mat;
    }

    /// 霓虹卡背贴图：深蓝黑底 + 青→品红渐变霓虹环（光晕=贴图假Bloom）+ 星点 + 内部微波纹
    private static Material CreateBackMaterial()
    {
        const int size = 256;
        var px = new Color[size * size];
        var cyan = new Color(0.05f, 0.85f, 1.00f);        // 霓虹青
        var magenta = new Color(1.00f, 0.25f, 0.80f);     // 霓虹品红
        var baseIn = new Color(0.028f, 0.04f, 0.085f);    // 中心深蓝黑
        var baseOut = new Color(0.07f, 0.105f, 0.185f);   // 近环处稍亮（引导视线到环）

        // 星点：固定种子的确定性伪随机位置（所有卡背一致，重复构建不变）
        var rng = new System.Random(20260921);
        var stars = new Vector2[16];
        for (int i = 0; i < stars.Length; i++)
        {
            float sx, sy;
            do { sx = (float)rng.NextDouble() - 0.5f; sy = (float)rng.NextDouble() - 0.5f; }
            while (sx * sx + sy * sy > 0.09f);            // 只落在内部区域
            stars[i] = new Vector2(sx, sy);
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1) - 0.5f;
                float v = y / (float)(size - 1) - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);

                // 1) 底色：中心暗，向霓虹环渐亮
                Color col = Color.Lerp(baseIn, baseOut, Mathf.Clamp01(r / 0.40f));

                // 2) 内部微波纹：极淡的“能量涟漪”
                col += new Color(0.015f, 0.04f, 0.06f, 0f) *
                       (Mathf.Sin(r * 46f) * 0.5f + 0.5f) * Mathf.Clamp01(0.40f - r);

                // 3) 霓虹渐变环：r=0.40，颜色随角度 青↔品红 流动；双层结构=亮芯+环体
                // 注意：Mathf.SmoothStep 是插值函数(from,to,t)，不是GLSL的smoothstep(edge0,edge1,x)！
                // 曾误用导致环遮罩恒≈0，环只剩光晕在撑（亮度只有设计值的30%）。
                float ringMask = Sstep(0.016f, 0f, Mathf.Abs(r - 0.40f)); // 环体（宽）
                float coreMask = Sstep(0.006f, 0f, Mathf.Abs(r - 0.40f)); // 灯管芯（窄，过曝）
                Color neon = Color.Lerp(cyan, magenta, Mathf.Sin(ang) * 0.5f + 0.5f);
                col = Color.Lerp(col, neon, ringMask * 0.85f);
                col = Color.Lerp(col, neon * 1.15f, coreMask); // 芯比环体再亮一档→“灯管”感

                // 4) 环光晕：宽幅低亮叠在环两侧——贴图假Bloom（不花运行时性能）
                float glow = Mathf.Clamp01(1f - Mathf.Abs(r - 0.40f) / 0.11f) * 0.30f;
                col += neon * glow * (1f - ringMask);

                // 5) 星点：青白色小亮点
                for (int s = 0; s < stars.Length; s++)
                {
                    float dx = u - stars[s].x, dy = v - stars[s].y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    const float starR = 0.013f;
                    if (d < starR) col += new Color(0.55f, 0.85f, 1.05f, 0f) * (1f - d / starR);
                }

                px[y * size + x] = col;
            }
        }
        var tex = new Texture2D(size, size);
        tex.SetPixels(px);
        tex.Apply();

        string texPath = $"{ArtDir}/CardBack.png";
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);

        var mat = NewLitMaterial($"{ArtDir}/CardBackMat.mat");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        mat.SetFloat("_Smoothness", 0.10f); // 微光泽衬托霓虹，仍远离“塑料感”
        return mat;
    }

    // ---- 圆头粗笔画数字：每个数字 = 若干笔画的折线序列（折点依次相连） ----
    // 坐标系 X∈[-0.5,0.5]、Y∈[-1,1]，所有数字按左右对称设计（X包络=±0.5，居中由此数学保证）。
    // 渲染原理：像素到线段距离 < 半宽 → 胶囊形笔画（自带圆头，无需额外画端点）。
    private static readonly string[] DigitPaths =
    {
        "0,-1 0,1",                                                                    // 1：居中单竖
        "-0.5,1 0.5,1 0.5,0.55 -0.1,-0.35 -0.5,-0.6 0.5,-0.6",                        // 2：顶杠→右竖→斜杠→底杠
        "-0.5,1 0.32,0.92 0.5,0.5 0.16,0.08 | 0.16,0.08 0.45,-0.05 0.5,-0.48 0.22,-0.9 -0.5,-1", // 3：双弧连笔
        "-0.5,1 -0.5,-0.1 | -0.5,-0.1 0.5,-0.1 | 0.5,0.32 0.5,-1",                     // 4：左竖+中横+右竖
        "-0.5,1 0.46,1 | -0.5,1 -0.5,0.15 | -0.5,0.15 0.3,0.08 0.5,-0.28 0.3,-0.85 -0.28,-1 -0.5,-0.72", // 5：顶杠+左竖+中横右碗
        "0.5,1 -0.2,0.85 -0.5,0.3 -0.5,-0.35 -0.27,-0.85 0.1,-1 0.45,-0.75 0.5,-0.32 0.08,-0.1 -0.5,-0.1", // 6
        "-0.5,1 0.5,1 0.05,-1",                                                        // 7：顶杠+对角线
        "0,0.14 0.28,0.22 0.43,0.55 0.28,0.88 0,0.96 -0.28,0.88 -0.43,0.55 -0.28,0.22 0,0.14 | " +
        "0,-0.02 0.32,-0.08 0.5,-0.5 0.32,-0.92 0,-0.98 -0.32,-0.92 -0.5,-0.5 -0.32,-0.08 0,-0.02", // 8：双环
        "0,0.06 0.34,0.12 0.5,0.5 0.34,0.88 0,0.94 -0.34,0.88 -0.5,0.5 -0.34,0.12 0,0.06 | 0.5,0.5 0.5,-1", // 9：上圆环+右竖下延
    };

    /// 解析数字笔画字符串 → 线段端点数组（缩放到贴图归一化：半宽0.15、半高0.31）
    private static Vector2[][] ParseDigitStrokes(int digit)
    {
        var parts = DigitPaths[digit - 1].Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
        var strokes = new Vector2[parts.Length][];
        for (int i = 0; i < parts.Length; i++)
        {
            var pts = parts[i].Trim().Split(' ');
            var list = new List<Vector2>(pts.Length);
            foreach (var p in pts)
            {
                var xy = p.Split(',');
                float sx = float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture) * 0.30f;
                float sy = float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture) * 0.31f;
                list.Add(new Vector2(sx, sy));
            }
            strokes[i] = list.ToArray();
        }
        return strokes;
    }

    /// 像素到该数字所有笔画线段的最小距离（< 半宽即在笔画内）
    private static float MinDistToStrokes(Vector2[][] strokes, float u, float v)
    {
        float best = float.MaxValue;
        foreach (var s in strokes)
            for (int i = 0; i < s.Length - 1; i++)
            {
                float d = DistToSeg(u, v, s[i].x, s[i].y, s[i + 1].x, s[i + 1].y);
                if (d < best) best = d;
            }
        return best;
    }

    /// 点到线段的距离（标准投影截断法）
    private static float DistToSeg(float px, float py, float ax, float ay, float bx, float by)
    {
        float abx = bx - ax, aby = by - ay;
        float t = ((px - ax) * abx + (py - ay) * aby) / (abx * abx + aby * aby);
        t = Mathf.Clamp01(t);
        float dx = ax + abx * t - px, dy = ay + aby * t - py;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// GLSL 式 smoothstep：x<=edge0 返回0，x>=edge1 返回1，中间平滑过渡（支持 edge1<edge0 反向）
    /// Unity 的 Mathf.SmoothStep(from,to,t) 是插值函数，语义完全不同，勿混用！
    private static float Sstep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    // ================= 通用小工具 =================

    /// 竞技场桌面贴图：深蓝紫径向渐变 + 细网格线 + 淡冷色纤维痕 + 暗角（视觉升级：数字竞技场风）
    private static Material CreateArenaMaterial()
    {
        const int size = 512;
        var tex = new Texture2D(size, size);
        var pixels = new Color[size * size];
        var coreCol = new Color(0.22f, 0.13f, 0.48f);  // 中心：紫罗兰（提亮版，光照后紫色相清晰可读）
        var edgeCol = new Color(0.035f, 0.05f, 0.13f); // 边缘：近黑蓝

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1) - 0.5f;
                float v = y / (float)(size - 1) - 0.5f;
                float dist = Mathf.Sqrt(u * u + v * v);

                // 1) 径向渐变：中心偏亮的紫，向边缘压到深蓝黑
                Color col = Color.Lerp(coreCol, edgeCol, Mathf.Clamp01(dist * 1.7f));

                // 2) 淡冷色纤维痕：沿X极拉伸的微噪声（保留一点旧木纹的质感层，几乎不可见）
                col *= 1f + (Mathf.PerlinNoise(u * 4f, v * 40f) - 0.5f) * 0.06f;

                // 3) 细网格线：每32px一条、2px宽；对比度按深底可见性校准（探针迭代2）
                if (x % 32 == 0 || x % 32 == 1 || y % 32 == 0 || y % 32 == 1)
                    col += new Color(0.02f, 0.10f, 0.14f, 0f);

                // 4) 暗角：四角再压暗，把视线收向中心
                float vig = Mathf.Clamp01(dist - 0.42f) / 0.29f;
                col *= 1f - vig * vig * 0.22f;

                pixels[y * size + x] = col;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        // 旧版木纹资产清理（风格更替，不留死资产）
        foreach (string old in new[] { $"{ArtDir}/WoodTable.png", $"{ArtDir}/WoodTableMat.mat" })
            if (AssetDatabase.LoadAssetAtPath<Object>(old) != null)
                AssetDatabase.DeleteAsset(old);

        string texPath = $"{ArtDir}/ArenaDesk.png";
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);

        var mat = NewLitMaterial($"{ArtDir}/ArenaDeskMat.mat");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        mat.SetFloat("_Smoothness", 0.18f); // 微哑光，避免深色面反光变“塑料”
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// 新建（或删了重建，保证幂等）一个 URP Lit 材质资源
    private static Material NewLitMaterial(string assetPath)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null)
            AssetDatabase.DeleteAsset(assetPath);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, assetPath);
        return mat;
    }

    private static Material CreateSolidMaterial(string name, Color color, float smoothness)
    {
        var mat = NewLitMaterial($"{ArtDir}/{name}.mat");
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        return mat;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        if (!AssetDatabase.IsValidFolder(ArtDir)) AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
    }

    /// 按“类型名字符串”添加管理器物体：脚本存在→加进场景；不存在→静默跳过。
    /// 用全程序集搜索而不是写死程序集名（asmdef 结构调整时不用改这里）。
    private static void TryAddManagerObject(string typeName, string parentName = null)
    {
        var type = FindTypeEverywhere(typeName);
        if (type == null)
        {
            Debug.Log("MC3D_INFO: skip " + typeName + " (script not present yet)");
            return;
        }
        var go = new GameObject(typeName);
        if (parentName != null)
        {
            var parent = GameObject.Find(parentName);
            if (parent != null) go.transform.SetParent(parent.transform, false);
        }
        go.AddComponent(type);
        Debug.Log("MC3D_STEP_OK: added " + typeName + " object");
    }

    /// 在当前已加载的所有程序集里按类型名搜索（游戏类型可能在不同程序集里）
    private static System.Type FindTypeEverywhere(string typeName)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(typeName);
            if (t != null) return t;
        }
        return null;
    }
}
