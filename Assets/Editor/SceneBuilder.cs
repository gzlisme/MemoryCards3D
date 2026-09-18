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
            CreateSolidMaterial("FloorMat", new Color(0.13f, 0.13f, 0.17f), 0.05f);
        floor.GetComponent<MeshRenderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off; // 地板不投影，省性能

        // ---------- 课桌：带真实厚度的木板桌面 ----------
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Table";
        // Cube 本体 1x1x1，靠缩放得到真实尺寸；位置让“桌面顶面”正好落在 y=0
        table.transform.localScale = new Vector3(TableWidth, TableThickness, TableDepth);
        table.transform.position = new Vector3(0f, -TableThickness * 0.5f, 0f);
        table.GetComponent<MeshRenderer>().sharedMaterial = CreateWoodMaterial();

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
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f);

        // ---------- 方向光（自然光）+ 环境 ----------
        var lightGo = new GameObject("SunLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;          // 实时软阴影：卡片在桌面上的投影靠它
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.95f, 0.86f);  // 略偏暖，像午后日光
        // 斜着照：有角度才有明暗面和斜向投影，3D体积感靠这个
        lightGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        RenderSettings.sun = light;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; // 单一环境色，明暗可控
        RenderSettings.ambientLight = new Color(0.30f, 0.32f, 0.37f);         // 偏暗偏冷，衬托暖主光

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

    /// 数字面贴图：奶油纸底 + 细噪点 + 棕色装饰环 + 七段数码管数字。
    /// 顶面UV是我们自己布的圆盘（圆心=贴图中心），所以数字直接画在贴图正中央。
    private static Texture2D GenerateFaceTexture(int digit)
    {
        const int size = 256;
        var px = new Color[size * size];
        var cream = new Color(0.95f, 0.91f, 0.82f);
        var ring = new Color(0.54f, 0.38f, 0.27f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 以贴图中心为原点的归一化坐标
                float u = x / (float)(size - 1) - 0.5f;
                float v = y / (float)(size - 1) - 0.5f;
                float dist = Mathf.Sqrt(u * u + v * v);

                // 纸底 + 细微噪点（纸而不是塑料的感觉）
                float noise = (Mathf.PerlinNoise(x * 0.25f, y * 0.25f) - 0.5f) * 0.10f;
                Color col = cream * (1f + noise);
                // 边缘装饰环
                if (Mathf.Abs(dist - 0.40f) < 0.028f) col = ring;
                px[y * size + x] = col;
            }
        }

        // 数字画在贴图正中央（顶面UV圆盘的圆心）
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float span = size - 1;

        var ink = new Color(0.23f, 0.15f, 0.10f);
        DrawDigit(px, size, digit, cx, cy, span * 0.17f, span * 0.32f, span * 0.085f, ink);

        var tex = new Texture2D(size, size);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Material CreateFaceMaterial(int digit)
    {
        string texPath = $"{ArtDir}/CardFace_{digit}.png";
        File.WriteAllBytes(texPath, GenerateFaceTexture(digit).EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);
        var mat = NewLitMaterial($"{ArtDir}/CardFaceMat_{digit}.mat");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        mat.SetFloat("_Smoothness", 0.08f); // 纸面：低光滑度，不反光
        return mat;
    }

    /// 背面贴图：土褐色纸壳 + 噪点 + 同心压纹（粗糙质感）
    private static Material CreateBackMaterial()
    {
        const int size = 256;
        var px = new Color[size * size];
        var baseCol = new Color(0.54f, 0.385f, 0.265f); // 土褐色
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1) - 0.5f;
                float v = y / (float)(size - 1) - 0.5f;
                float dist = Mathf.Sqrt(u * u + v * v);
                float noise = (Mathf.PerlinNoise(x * 0.18f, y * 0.18f) - 0.5f) * 0.18f;
                float rings = Mathf.Sin(dist * 55f) * 0.05f; // 同心环模拟压制纸纹
                float t = Mathf.Clamp01(0.5f + noise + rings);
                px[y * size + x] = Color.Lerp(baseCol * 0.82f, baseCol * 1.12f, t);
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
        mat.SetFloat("_Smoothness", 0.05f); // 硬纸壳：几乎不反光
        return mat;
    }

    // ---- 七段数码管：经典计算器数字，纯代码画，无需任何字体 ----
    // 段位定义：A顶杠 B右上 C右下 D底杠 E左下 F左上 G中杠
    private static readonly bool[][] SegMap =
    {
        new[] { false, true,  true,  false, false, false, false }, // 1
        new[] { true,  true,  false, true,  true,  false, true  }, // 2
        new[] { true,  true,  true,  true,  false, false, true  }, // 3
        new[] { false, true,  true,  false, false, true,  true  }, // 4
        new[] { true,  false, true,  true,  false, true,  true  }, // 5
        new[] { true,  false, true,  true,  true,  true,  true  }, // 6
        new[] { true,  true,  true,  false, false, false, false }, // 7
        new[] { true,  true,  true,  true,  true,  true,  true  }, // 8
        new[] { true,  true,  true,  true,  false, true,  true  }, // 9
    };

    private static void DrawDigit(Color[] px, int size, int digit,
        float cx, float cy, float halfW, float halfH, float th, Color c)
    {
        bool[] s = SegMap[digit - 1];
        // 三根横杠：A上 / G中 / D下
        if (s[0]) FillRect(px, size, cx - halfW, cy + halfH - th, cx + halfW, cy + halfH, c); // A
        if (s[6]) FillRect(px, size, cx - halfW, cy - th * 0.5f, cx + halfW, cy + th * 0.5f, c); // G
        if (s[3]) FillRect(px, size, cx - halfW, cy - halfH, cx + halfW, cy - halfH + th, c); // D
        // 四根竖条：F左上 / E左下 / B右上 / C右下
        if (s[5]) FillRect(px, size, cx - halfW, cy, cx - halfW + th, cy + halfH, c);         // F
        if (s[4]) FillRect(px, size, cx - halfW, cy - halfH, cx - halfW + th, cy, c);         // E
        if (s[1]) FillRect(px, size, cx + halfW - th, cy, cx + halfW, cy + halfH, c);         // B
        if (s[2]) FillRect(px, size, cx + halfW - th, cy - halfH, cx + halfW, cy, c);         // C
    }

    private static void FillRect(Color[] px, int size, float x0, float y0, float x1, float y1, Color c)
    {
        int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0)), ix1 = Mathf.Min(size - 1, Mathf.CeilToInt(x1));
        int iy0 = Mathf.Max(0, Mathf.FloorToInt(y0)), iy1 = Mathf.Min(size - 1, Mathf.CeilToInt(y1));
        for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
                px[y * size + x] = c;
    }

    // ================= 通用小工具 =================

    /// 程序化木纹贴图 + 磨砂木材质（需求：桌面不能是纯色平面）
    private static Material CreateWoodMaterial()
    {
        const int size = 512;
        var tex = new Texture2D(size, size);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                // 木纹修正：噪声沿X（桌长方向）拉伸=纤维顺纹；细密高频线=年轮切面。
                // 初版的拉伸轴写反了，纹理呈“竖向沙丘纹”。
                float fiber = Mathf.PerlinNoise(u * 2.5f, v * 14f);
                float fiber2 = Mathf.PerlinNoise(u * 6f, v * 36f);
                float grain = Mathf.PerlinNoise(u * 30f, v * 160f);
                float streak = Mathf.Sin((v * 22f + fiber * 2f) * Mathf.PI) * 0.5f + 0.5f;
                float t = Mathf.Clamp01(streak * 0.5f + fiber * 0.25f + fiber2 * 0.15f + grain * 0.1f);
                //Color c = Color.Lerp(new Color(0.36f, 0.24f, 0.14f), new Color(0.60f, 0.43f, 0.26f), t);
				Color c = Color.Lerp(new Color(0.18f, 0.10f, 0.06f), new Color(0.32f, 0.19f, 0.11f), t);
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        string texPath = $"{ArtDir}/WoodTable.png";
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);

        var mat = NewLitMaterial($"{ArtDir}/WoodTableMat.mat");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        mat.SetFloat("_Smoothness", 0.12f); // 低光滑度=磨砂木面
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
