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

    private const float MatWidth = 3.5f;           // 野餐垫 X 尺寸（铺满竖屏 FOV 宽 2.7 + 余量）
    private const float MatDepth = 7.0f;           // 野餐垫 Z 尺寸（覆盖竖屏 FOV 高最大 6.0 @9:20）
    private const float MatThickness = 0.005f;     // 垫厚：几乎贴地
    private const float CameraOrthoSize = 2.5f;   // 竖屏正交相机半高【占位值】：运行时由 CameraController 按 Screen 宽高比动态计算（ortho=2.7/(2×aspect)），此处仅编辑态预览

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
        BuildFruitPlayers();

        // 运行时管理器也一并放进场景（脚本存在才加，保证分阶段开发时本工具始终能跑）
        TryAddManagerObject("GameManager");
        TryAddManagerObject("UIManager", "GameManager");

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();

        Debug.Log("MC3D_BUILD_OK: scene saved to " + ScenePath);
    }

    // ================= 地基：地板 / 野餐垫 / 相机 / 灯光 =================

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

        // ---------- 野餐垫：铺满整个视野的薄垫（竖屏手机版背景，替代旧木桌） ----------
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "PicnicMat";
        // 量纲推导（写码前必算）：
        // - 竖屏 FOV 宽 2.7（相机公式），高最大 6.0（9:20 宽高比 0.45×）
        // - 垫 3.5×7.0 带余量覆盖所有目标宽高比，正交相机零露馅
        // - 卡片底 y=0 恰躺垫面；垫顶微沉 -0.001（pos -0.0035+厚/2 0.0025），与卡底差 1mm，彻底消除 Z-fighting 共面闪烁
        // - 投影关闭：消除旧"顶部黑长条"（桌子投地板的阴影）；receiveShadows 保持开，卡片影子照常落在垫上
        table.transform.localScale = new Vector3(MatWidth, MatThickness, MatDepth);
        table.transform.position = new Vector3(0f, -MatThickness * 0.5f - 0.001f, 0f);
        var tableR = table.GetComponent<MeshRenderer>();
        tableR.sharedMaterial = CreatePicnicMat();
        tableR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // ---------- 相机：固定正交俯视 ----------
        var camGo = new GameObject("MainCamera");
        camGo.tag = "MainCamera";              // OnMouseDown 点击拾取需要主相机标记
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();   // 一个场景只要一个声音监听器
        var ctrl = camGo.AddComponent<CameraController>();
        ctrl.EdgePadding = 0.3f;  // 竖屏动态视野参数（卡片区 2.4 + 边距 0.3 = FOV 宽 2.7）
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

            // TMP 3D 数字子物体（B 方案：数字不再烘焙进贴图，根治图集渗色）
            AttachTmpDigitToCard(go, digit);
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

    /// 卡面贴图：深蓝黑底 + 微噪 + 同色细环（纯底图）。
    /// B 方案（2026-09-22）：数字不再烘焙进贴图，由 TMP 3D 子物体承担（AttachTmpDigitToCard），
    /// 从根上移除 TextMesh 动态字体图集渲染链路——渗色横线伪影不复存在。
    private static Texture2D GenerateFaceTexture(int digit, Color neon)
    {
        const int size = 512;

        // ---- 合成：深底 + 微噪 + 光池 + 细环 ----
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

                // 2) 光池（假 Bloom，与卡背霓虹环同款贴图技法，2026-09-23 批准）：
                // 量纲核对：圆面 capUV 全贴图映射 → 512px=卡径；数字墨迹高 70%=±179px；
                // 半径 0.42（归一化 0.5 坐标系）=215px——墨迹缘(179px)处 t≈0.43、alpha≈0.11，
                // 笔画边缘也有淡光（用户选 0.42 而非 0.35 的原因）；细环 r=0.40 处 t≈0.95、
                // alpha≈0.0008 已近零，环线不受影响。衰减 alpha=0.35×(1-t)²，峰值≈细环亮度的64%。
                float poolT = r / 0.42f;
                if (poolT < 1f)
                {
                    float poolA = 0.35f * (1f - poolT) * (1f - poolT);
                    col += neon * poolA;
                }

                // 3) 同色细环（r=0.40，窄细、低亮度陪衬）
                col = Color.Lerp(col, neon * 0.55f, Sstep(0.004f, 0f, Mathf.Abs(r - 0.40f)) * 0.75f);

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

    /// 野餐垫贴图：黄白维希格纹（三色：白/浅黄/深黄交叉）+ 轻布噪，铺满全屏背景。
    /// 量纲推导（写码前必算，2026-09-24 密度加密 8×16→16×32）：垫 3.5(X)×7.0(Z)，X:Z=1:2；
    /// 贴图 1024²、X 16 格（每格 64px）/ Z 32 格（每格 32px）→ 世界格边 3.5/16 = 7.0/32 = 0.21875
    /// （正方形格：贴图格 X:Z=64:32px 被 UV 拉伸到 X:Z=1:2 的垫面后恰成正方形）；
    /// 屏幕格边 ≈87px ≈ 卡径的 1/8（旧 175px 偏"粗糙"，加密后 4 倍密度细密）。
    /// 格缘半像素 Sstep 抗锯齿（X/Z 像素密度不同，各自 aa）；维希图案 = 竖浅黄带 × 横白带，交叉深黄。
    private static Material CreatePicnicMat()
    {
        const int size = 1024;
        const float cellsX = 16f;  // X 方向格数（垫 3.5 宽 → 世界格边 3.5/16 = 0.21875）
        const float cellsZ = 32f;  // Z 方向格数（垫 7.0 深 → 世界格边 7.0/32 = 0.21875）
        const float aaX = 0.5f * cellsX / size;  // 半像素（格单位）：0.5×16/1024 = 0.0078
        const float aaZ = 0.5f * cellsZ / size;  // 0.5×32/1024 = 0.0156
        var white = new Color(1.00f, 1.00f, 1.00f);
        var lightYellow = new Color(0.98f, 0.91f, 0.49f); // #FBE87E
        var deepYellow = new Color(0.88f, 0.75f, 0.16f);  // #E0C028

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float fz = (y + 0.5f) / size * cellsZ;
            float cz = 2f * Mathf.Floor(fz * 0.5f) + 0.5f;   // 最近横带中心
            float wz = 1f - Sstep(0.5f - aaZ, 0.5f + aaZ, Mathf.Abs(fz - cz));
            for (int x = 0; x < size; x++)
            {
                float fx = (x + 0.5f) / size * cellsX;
                float cx = 2f * Mathf.Floor(fx * 0.5f) + 0.5f;   // 最近竖带中心
                float wx = 1f - Sstep(0.5f - aaX, 0.5f + aaX, Mathf.Abs(fx - cx));

                Color col = white;
                col = Color.Lerp(col, lightYellow, Mathf.Max(wx, wz)); // 带区浅黄（竖或横）
                col = Color.Lerp(col, deepYellow, wx * wz);            // 交叉点深黄
                col *= 1f + (Mathf.PerlinNoise(x * 0.08f, y * 0.08f) - 0.5f) * 0.05f; // 布面微噪
                px[y * size + x] = col;
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        // 旧竞技场桌面资产清理（风格更替，不留死资产；先例：木纹清理）。
        // 此时新场景已重建、桌子材质已换 PicnicMatMat，旧引用不复存在，删除安全。
        foreach (string old in new[] { $"{ArtDir}/ArenaDesk.png", $"{ArtDir}/ArenaDeskMat.mat" })
            if (AssetDatabase.LoadAssetAtPath<Object>(old) != null)
                AssetDatabase.DeleteAsset(old);

        string texPath = $"{ArtDir}/PicnicMat.png";
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);

        var mat = NewLitMaterial($"{ArtDir}/PicnicMatMat.mat");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        mat.SetFloat("_Smoothness", 0.05f); // 哑光布面
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

    // ================= Jev 集成（阶段A：独立测试，不动 BuildAll） =================

    /// <summary>
    /// 从构建日志构造 Jev 的 state 字符串。
    /// 结构：[terminal] + [markers] + [compile errors] + [errors] + [tail 20行]
    /// 总长 ≤7000 字符（JevHelper 的 8000 硬截断只作最后保险）。
    /// 绝不整段截断——硬截会丢末尾的 return code 行。
    /// </summary>
    private static string BuildJevState(string logPath)
    {
        if (!System.IO.File.Exists(logPath)) return null;

        // 共享读：-logFile 指向的文件被编辑器日志写入器持锁，普通 ReadAllLines 会 Sharing violation（A3 实测）
        var lines = ReadAllLinesShared(logPath);
        var sb = new System.Text.StringBuilder();

        // 1) terminal: 从末尾 50 行内找 return code
        string terminal = "(not found)";
        int scanStart = System.Math.Max(0, lines.Length - 50);
        for (int i = lines.Length - 1; i >= scanStart; i--)
        {
            if (lines[i].Contains("return code"))
            {
                terminal = lines[i].Trim();
                break;
            }
        }
        sb.AppendLine("[terminal] " + terminal);

        // 2) markers + errors
        bool hasBuildOk = false;
        int errorCsCount = 0;
        var errorLines = new System.Collections.Generic.List<string>();
        foreach (var line in lines)
        {
            if (line.Contains("MC3D_BUILD_OK")) hasBuildOk = true;
            if (line.Contains("error CS"))
            {
                errorCsCount++;
                if (errorLines.Count < 30)
                    errorLines.Add(line.Length > 200 ? line.Substring(0, 200) : line);
            }
            else if ((line.Contains("Exception") || line.Contains("Aborting batchmode") || line.Contains("Build Failed"))
                     && errorLines.Count < 30)
            {
                errorLines.Add(line.Length > 200 ? line.Substring(0, 200) : line);
            }
        }
        sb.AppendLine("[markers] BUILD_OK=" + hasBuildOk);
        sb.AppendLine("[compile errors] " + errorCsCount);

        // 3) errors 段
        if (errorLines.Count == 0)
        {
            sb.AppendLine("[errors] (none)");
        }
        else
        {
            sb.AppendLine("[errors]");
            foreach (var e in errorLines) sb.AppendLine(e);
        }

        // 4) tail 20 行
        sb.AppendLine("[tail]");
        int tailStart = System.Math.Max(0, lines.Length - 20);
        for (int i = tailStart; i < lines.Length; i++)
        {
            if (sb.Length > 7000) break;
            sb.AppendLine(lines[i].Length > 200 ? lines[i].Substring(0, 200) : lines[i]);
        }

        // 5) 最后保险：硬截断到 7000
        string result = sb.ToString();
        if (result.Length > 7000) result = result.Substring(0, 7000);
        return result;
    }

    /// 读取可能正被编辑器写入的日志文件：FileShare.ReadWrite 允许与日志写入器并发
    private static string[] ReadAllLinesShared(string path)
    {
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open,
            System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
        using (var sr = new System.IO.StreamReader(fs))
        {
            var list = new System.Collections.Generic.List<string>();
            string line;
            while ((line = sr.ReadLine()) != null) list.Add(line);
            return list.ToArray();
        }
    }

    [MenuItem("Tools/MemoryCards3D/Test Jev Integration")]
    public static void TestJevIntegration()
    {
        Debug.Log("MC3D_JEV_TEST_START");

        string consolePath = Application.consoleLogPath;
        Debug.Log("MC3D_JEV_CONSOLE_PATH: " + consolePath);

        // 优先命令行 -logFile，兜底固定路径
        string logPath = null;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-logFile") { logPath = args[i + 1]; break; }
        }
        if (string.IsNullOrEmpty(logPath) || !System.IO.File.Exists(logPath))
            logPath = "D:/work/unity/_font_pipeline_build_log.txt";

        Debug.Log("MC3D_JEV_LOG_PATH: " + logPath);

        string state = BuildJevState(logPath);
        if (state == null)
        {
            Debug.LogError("MC3D_JEV_TEST: log file not found");
            return;
        }
        Debug.Log("MC3D_JEV_STATE_LEN: " + state.Length);

        float? hasError = JevHelper.AskNoul(state, "Does the log contain compile errors or exceptions?");
        float? isSuccess = JevHelper.AskNoul(state, "If no errors, is the build successful?");

        Debug.Log("MC3D_JEV_TEST_RESULT hasError=" + hasError + " isSuccess=" + isSuccess);
        Debug.Log("MC3D_JEV_TEST_DONE");
    }

    // ================= Jev 集成（阶段B：独立验证方法，不嵌入 BuildAll） =================

    /// <summary>
    /// 确定要读的构建日志路径。三级兜底：
    ///   1) 环境变量 JEV_BUILD_LOG
    ///   2) 命令行 --jev-build-log &lt;path&gt;
    ///   3) 固定路径 D:/work/unity/_build_latest.log（命中时打 warning 防陈旧日志）
    /// 返回 null 表示全都失败。
    /// </summary>
    private static string ResolveBuildLogPath(out string source)
    {
        // 1) 环境变量
        string envPath = System.Environment.GetEnvironmentVariable("JEV_BUILD_LOG");
        if (!string.IsNullOrEmpty(envPath) && System.IO.File.Exists(envPath))
        {
            source = "ENV";
            return envPath;
        }

        // 2) 命令行 --jev-build-log
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--jev-build-log" && System.IO.File.Exists(args[i + 1]))
            {
                source = "CLI";
                return args[i + 1];
            }
        }

        // 3) 固定兜底
        string fallback = "D:/work/unity/_build_latest.log";
        if (System.IO.File.Exists(fallback))
        {
            Debug.LogWarning("MC3D_JEV_VERIFY: using FALLBACK log path, may be stale: " + fallback);
            source = "FALLBACK";
            return fallback;
        }

        source = "NONE";
        return null;
    }

    [MenuItem("Tools/MemoryCards3D/Verify Build with Jev")]
    public static void VerifyJev()
    {
        Debug.Log("MC3D_JEV_VERIFY_START");

        // 1) 确定日志路径（三级兜底 + 来源级别）
        string source;
        string logPath = ResolveBuildLogPath(out source);
        if (logPath == null)
        {
            Debug.LogWarning("MC3D_JEV_VERIFY_SKIP: no log found");
            Debug.Log("MC3D_JEV_VERIFY_DONE");
            return;
        }
        Debug.Log("MC3D_JEV_VERIFY_LOG_PATH: " + logPath + " (source=" + source + ")");

        // 2) 构造 state（复用阶段A的 BuildJevState）
        string state = BuildJevState(logPath);
        if (state == null)
        {
            Debug.LogWarning("MC3D_JEV_VERIFY_SKIP: state build failed");
            Debug.Log("MC3D_JEV_VERIFY_DONE");
            return;
        }
        Debug.Log("MC3D_JEV_VERIFY_STATE_LEN: " + state.Length);

        // 3) 调用 Jev（失败不阻塞）
        float? hasError = null;
        float? isSuccess = null;
        try
        {
            hasError = JevHelper.AskNoul(state, "Does the log contain compile errors or exceptions?");
            isSuccess = JevHelper.AskNoul(state, "If no errors, is the build successful?");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("MC3D_JEV_VERIFY: Jev call failed - " + ex.Message);
        }

        // 4) 根据置信度记录结果（不阻塞）
        if (hasError.HasValue && hasError.Value >= 0.90f)
            Debug.LogWarning("MC3D_JEV_VERIFY: HIGH ERROR PROBABILITY " + hasError.Value.ToString("F2"));
        if (isSuccess.HasValue && isSuccess.Value >= 0.90f)
            Debug.Log("MC3D_JEV_VERIFY: BUILD SUCCESS confirmed " + isSuccess.Value.ToString("F2"));

        Debug.Log("MC3D_JEV_VERIFY_RESULT hasError=" + hasError + " isSuccess=" + isSuccess);
        Debug.Log("MC3D_JEV_VERIFY_DONE");
    }

    // ================= TMP 3D 数字方案（B 方案：根治动态图集渗色横线） =================

    [MenuItem("Tools/MemoryCards3D/Import TMP Essentials")]
    public static void ImportTmpEssentials()
    {
        Debug.Log("MC3D_TMP_IMPORT_START");

        // 项目根 = Assets 的父目录
        string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
        string pkgRelPath = "Library/PackageCache/com.unity.textmeshpro@3.0.6/Package Resources/TMP Essential Resources.unitypackage";
        string pkgAbsPath = System.IO.Path.Combine(projectRoot, pkgRelPath).Replace('\\', '/');

        Debug.Log("MC3D_TMP_IMPORT_PATH: " + pkgAbsPath);

        if (!System.IO.File.Exists(pkgAbsPath))
        {
            Debug.LogError("MC3D_TMP_IMPORT_FAIL: package not found at " + pkgAbsPath);
            return;
        }

        try
        {
            AssetDatabase.ImportPackage(pkgAbsPath, false);
            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("MC3D_TMP_IMPORT_FAIL: exception " + ex.Message);
            return;
        }

        // 同步验证导入结果
        string settingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        string fontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        bool settingsOk = System.IO.File.Exists(System.IO.Path.Combine(projectRoot, settingsPath));
        bool fontOk = System.IO.File.Exists(System.IO.Path.Combine(projectRoot, fontPath));

        if (settingsOk && fontOk)
            Debug.Log("MC3D_TMP_IMPORT_VERIFY: OK");
        else
            Debug.LogError($"MC3D_TMP_IMPORT_VERIFY: MISSING (settings={settingsOk}, font={fontOk})");

        Debug.Log("MC3D_TMP_IMPORT_DONE");
    }

    /// TMP 数字发光材质：SDF shader + GLOW_ON，观感对标卡背霓虹灯管（贴图假Bloom 的同款效果）
    private static Material CreateTmpDigitMaterial(int digit, Color color)
    {
        var shader = Shader.Find("TextMeshPro/Distance Field");
        var mat = new Material(shader);
        mat.SetColor(Shader.PropertyToID("_FaceColor"), Color.white);

        // Glow：外扩光晕，色同数字（参数首跑后可按探针微调）
        mat.EnableKeyword("GLOW_ON");
        mat.SetColor(Shader.PropertyToID("_GlowColor"), new Color(color.r, color.g, color.b, 1f));
        mat.SetFloat(Shader.PropertyToID("_GlowOffset"), 0.6f);
        mat.SetFloat(Shader.PropertyToID("_GlowInner"), 0.15f);
        mat.SetFloat(Shader.PropertyToID("_GlowOuter"), 0.45f);
        mat.SetFloat(Shader.PropertyToID("_GlowPower"), 0.75f);

        string matPath = $"{ArtDir}/CardFaceTmpMat_{digit}.mat";
        AssetDatabase.CreateAsset(mat, matPath); // 材质必须落盘为资产，场景实例引用才持久
        Debug.Log($"MC3D_TMP_MATERIAL_CREATED value={digit} color=({color.r:F2},{color.g:F2},{color.b:F2}) path={matPath}");
        return mat;
    }

    /// 给卡片实例挂 TMP 3D 数字子物体（B 方案核心）：数字=卡值、三档配色、浮高 2mm 防 Z-fighting。
    /// 翻牌为刚性 Slerp 旋转，子物体随父级转动（翻到背面数字面朝下，天然不可见），Card.cs 零改动。
    private static void AttachTmpDigitToCard(GameObject card, int digit)
    {
        Color neon = digit <= 3 ? new Color(0.05f, 0.85f, 1.00f)
                  : digit <= 6 ? new Color(1.00f, 0.25f, 0.80f)
                  : new Color(1.00f, 0.76f, 0.22f);

        var go = new GameObject("TmpDigit");
        go.transform.SetParent(card.transform, false);
        go.transform.localPosition = new Vector3(0f, 0.022f, 0f);        // 卡顶面(y=+0.02)上方 2mm
        // 推导（写码前必推）：相机 Euler(90,0,0) 俯视 → 屏幕上方=+Z、右方=+X。
        // 绕X -90° 字面朝 +Y 但字倒立(up=-Z)；绕Z 180° 把 up 修到 +Z，但同时把 right 翻成 -X
        // ——右手系字"面朝+Y 且 up=+Z"时 right 必为 -X，与相机右方(+X)相反 = 俯视镜像（手性冲突）。
        // 纯旋转做不到"朝上+正立+不镜像"，必须 localScale.x=-1 翻手性抵消。
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 180f);
        go.transform.localScale = new Vector3(-1f, 1f, 1f);             // 抵消俯视镜像；TMP 是 2D 文字 shader，负 scale 不影响光照

        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        tmp.text = digit.ToString();
        tmp.font = TMPro.TMP_Settings.defaultFontAsset;                   // Essentials 的 LiberationSans SDF
        // 量纲核对（实测系数，写码前必算）：fontSize 单位=点，1点≈0.1单位行高，
        // 数字墨迹高（cap高度）≈ fontSize × 0.07 单位（0.1×cap比0.7，第1轮实测50%卡径吻合）。
        // 目标 = 卡径 0.7 × 70% = 0.49 单位 → fontSize = 0.49 / 0.07 ≈ 7
        tmp.fontSize = 7f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;                // 水平+垂直双居中
        tmp.color = neon;
        tmp.raycastTarget = false;                                        // 不挡卡片 OnMouseDown 物理射线
        tmp.fontSharedMaterial = CreateTmpDigitMaterial(digit, neon);

        Debug.Log($"MC3D_TMP_DIGIT_ATTACHED value={digit} pos=(0,0.022,0) rot=(-90,0,180) scale=(-1,1,1) fontSize=7");
    }

    // ================= 水果玩家标记（Stage B：苹果/橙子圆片 + TMP 名字） =================

    /// 水果圆片 mesh：单面三角扇，法线朝 +Y（俯视可见）。
    /// 绕向按"逆时针=正面"铁律（从 +Y 往下看，Unity 可见面=顶点逆时针）。
    /// UV 以贴图 (0.5,0.5) 为圆心、半径 0.5 的圆盘映射（与卡面顶面同法）。
    /// 独立 mesh 不碰 CardMesh.asset；radius 为世界半径，由调用方量纲推算后传入。
    private static Mesh BuildFruitDiscMesh(float radius, int segments = 48)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        verts.Add(new Vector3(0f, 0f, 0f));               // 圆心
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i <= segments; i++)
        {
            float ang = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(ang) * radius, z = Mathf.Sin(ang) * radius;
            verts.Add(new Vector3(x, 0f, z));
            uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
        }
        for (int i = 0; i < segments; i++)
        {
            // 顶视相机朝 -Y 看：从上往下看要逆时针。
            // 世界 XZ 平面上 (cos,sin) 随 ang 增大逆时针（从+Y上方看）——扇形 (中心, i+1, i) 使正面朝上。
            tris.AddRange(new[] { 0, i + 2, i + 1 });
        }

        var mesh = new Mesh { name = "FruitDisc" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();   // 单面朝上
        mesh.RecalculateBounds();
        return mesh;
    }

    /// 构建水果玩家标记：上苹果 / 下橙子。
    /// 结构：FruitRoot（定位+TMP名字，不转）→ SpinGroup（圆片+贴图，转）。
    /// 名字挂 FruitRoot 不挂 SpinGroup——否则随组自转，转到侧对相机时文字变薄线不可读。
    /// 量纲推导（写码前必算）：
    ///   贴图 500²、中心(250,250)；扫描不透明像素(alpha>0.1)到中心的最大距离=真实墨迹半径；
    ///   圆片贴图空间半径需 ≥ 墨迹半径（墨迹全在圆内才不裁角），另加 5% 边距。
    ///   世界直径 = clamp(0.6×玩家区高, 0.45, 0.8)：区高=(FOV高-2.4)/2，
    ///   9:16 区高1.2→0.72 / 9:19.5 区高1.73→0.8 顶格 / iPad 3:4 区高0.6→0.45 保底。
    ///   位置：玩家区中心 z=±(1.2+FOV高/2)/2，运行时由 FruitPlayer 按 aspect 算（编辑态摆 9:16 参考值）。
    ///   名字：水果圆心正下方（z 向卡片区偏移），距圆片半径+0.15；TMP 3D 躺平
    ///   Euler(-90,0,180)+scale(-1,1,1)（已验证防镜像公式），fontSize≈4（墨迹高≈0.28 单位）。
    private static void BuildFruitPlayers()
    {
        string appleTexPath = $"{ArtDir}/apple.png";
        string orangeTexPath = $"{ArtDir}/orange.png";
        var appleTex = AssetDatabase.LoadAssetAtPath<Texture2D>(appleTexPath);
        var orangeTex = AssetDatabase.LoadAssetAtPath<Texture2D>(orangeTexPath);
        if (appleTex == null || orangeTex == null)
        {
            Debug.LogError("MC3D_FRUIT_FAIL: apple.png or orange.png not found in " + ArtDir);
            return;
        }

        // 1) 扫描不透明像素最大半径（裁角安全判定）
        float appleInk = MaxInkRadius(appleTex);
        float orangeInk = MaxInkRadius(orangeTex);
        Debug.Log($"MC3D_FRUIT_INK_RADIUS apple={appleInk:F0} orange={orangeInk:F0}");
        if (appleInk > 250f || orangeInk > 250f)
        {
            Debug.LogError($"MC3D_FRUIT_FAIL: ink radius exceeds texture half-size (apple={appleInk:F0}, orange={orangeInk:F0}, limit 250)");
            return;
        }

        // 2) 材质：URP Lit + 水果贴图 + Alpha Clip 四件套（属性+keyword 须在 CreateAsset 前设置才持久化）
        //    贴图透明区 alpha=0 < _Cutoff 0.5 → 丢弃不渲染：黑圆底消失，水果直接躺在垫上
        var appleMat = NewLitMaterial($"{ArtDir}/AppleDiscMat.mat");
        appleMat.mainTexture = appleTex;
        appleMat.SetFloat("_Smoothness", 0.10f);
        appleMat.SetFloat("_Surface", 0f);      // Opaque（alpha 只用于裁切，不做半透明混合）
        appleMat.SetFloat("_AlphaClip", 1f);    // Inspector 开关位
        appleMat.EnableKeyword("_ALPHATEST_ON"); // ★ 真正生效的 shader keyword
        appleMat.SetFloat("_Cutoff", 0.5f);     // 裁切阈值
        var orangeMat = NewLitMaterial($"{ArtDir}/OrangeDiscMat.mat");
        orangeMat.mainTexture = orangeTex;
        orangeMat.SetFloat("_Smoothness", 0.10f);
        orangeMat.SetFloat("_Surface", 0f);
        orangeMat.SetFloat("_AlphaClip", 1f);
        orangeMat.EnableKeyword("_ALPHATEST_ON");
        orangeMat.SetFloat("_Cutoff", 0.5f);
        AssetDatabase.SaveAssets();

        // 3) 水果组组装：直径用 9:16 参考值 0.72（区高1.2×0.6），运行时 FruitPlayer 自适应
        BuildFruit("Apple", appleTex, appleMat, appleInk, true);
        BuildFruit("Orange", orangeTex, orangeMat, orangeInk, false);
        Debug.Log("MC3D_STEP_OK: fruit players built");
    }

    /// 组装单个水果：FruitRoot(名/定位) → SpinGroup(圆片) ；编辑态 z 按 9:16 竖屏参考 ±1.8。
    private static void BuildFruit(string name, Texture2D tex, Material mat, float inkRadius, bool top)
    {
        // 量纲：贴图空间墨迹半径 inkRadius(px) / 250(半边) = 世界半径基准比例；
        // 世界直径 0.72（9:16 参考）→ 半径 0.36 → 圆内墨迹的世界半径 = 0.36×(ink/250)。
        // 圆片 mesh 半径直接用 0.36（整圆），墨迹小于圆贴图自动带透明边。
        const float refDiameter = 0.72f;   // 9:16 玖玩家区 1.2 × 60%
        float refRadius = refDiameter * 0.5f;

        var root = new GameObject("Fruit_" + name);
        // 位置量纲（编辑态 9:16 参考）：FOV高 4.8 → 区中心 z = (1.2+2.4)/2 = 1.8；运行时由 FruitPlayer 重算。
        float z = top ? 1.8f : -1.8f;
        root.transform.position = new Vector3(0f, 0.02f, z);   // 贴垫面微浮 2mm（与卡片同高，防 Z-fighting）

        // 挂运行时组件：自转/亮暗/按 aspect 自适应布局（PlayerNumber：上苹果=1，下橙子=2）
        var fruitPlayer = root.AddComponent<FruitPlayer>();
        fruitPlayer.PlayerNumber = top ? 1 : 2;   // SpinSpeed 默认 120°/s = 3 秒/圈

        var spin = new GameObject("SpinGroup");
        spin.transform.SetParent(root.transform, false);

        var discGo = new GameObject("Disc", typeof(MeshFilter), typeof(MeshRenderer));
        discGo.transform.SetParent(spin.transform, false);
        var mesh = BuildFruitDiscMesh(refRadius);
        string meshPath = $"{ArtDir}/FruitDisc_{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
            AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();
        discGo.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        var mr = discGo.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 薄片不投怪影

        // 名字显示已按用户决策移除（水果种类+亮暗自明，无需文字）

        Debug.Log($"MC3D_FRUIT_BUILT name={name} top={top} inkRadius={inkRadius:F0} pos=(0,0.02,{z})");
    }

    /// 扫描贴图不透明像素(alpha>0.1)到贴图中心的最大距离（真实墨迹半径，px）。
    private static float MaxInkRadius(Texture2D tex)
    {
        // Texture2D 需可读：AssetDatabase 导入的贴图默认可读性跟随导入设置；
        // GetPixels 失败时经 RenderTexture 中转（复用 GrabTextureRegion 思路）。
        Color[] px;
        try { px = tex.GetPixels(); }
        catch
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var t2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            t2.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            t2.Apply();
            RenderTexture.active = prev;
            px = t2.GetPixels();
            Object.DestroyImmediate(t2);
            RenderTexture.ReleaseTemporary(rt);
        }
        float cx = tex.width * 0.5f, cy = tex.height * 0.5f;
        float best = 0f;
        for (int y = 0; y < tex.height; y++)
            for (int x = 0; x < tex.width; x++)
            {
                var c = px[y * tex.width + x];
                if (c.a > 0.1f)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > best) best = d;
                }
            }
        return best;
    }
}
