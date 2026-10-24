using UnityEngine;

using System.Collections.Generic;

/// <summary>
/// X 光描边高亮 —— 射线悬停（或抓取）时，给物品描一圈【穿透可见】的轮廓。
///
/// 效果：
///   · 射线指向物品 → 物品外围出现一圈发光轮廓
///   · 轮廓穿过其他物体也看得见（X 光），被手挡住时也能看到
///   · 抓住物品时轮廓保持（提示"在我手上"）
///
/// 原理：
///   复制一份物品的网格，套上 SnowLineVR/XRayOutline 这个 shader。
///   该 shader 会沿【法线方向】把顶点往外推一点点，配合 Cull Front
///   形成一圈均匀包在外面的轮廓 —— 不管网格轴心在哪都正确。
///   再配合 ZTest Always，轮廓就能穿透其他物体显示。
///
/// 用法：
///   ① 在场景里选中道具
///   ② Add Component → 搜 XRay Hover Highlight → 添加
///   （ItemPhysicsSetup 也会自动加上这个组件，一般不用手动加）
/// </summary>
public class XRayHoverHighlight : MonoBehaviour
{
    [Header("外观")]
    [Tooltip("轮廓颜色")]
    public Color outlineColor = new Color(1f, 0.85f, 0.25f, 0.9f);

    [Tooltip("轮廓粗细（外扩比例）。0.08 = 向外扩 8%。配合穿透显示，8% 左右比较明显")]
    [Range(0.01f, 0.3f)]
    public float outlineWidth = 0.08f;

    [Tooltip("内部填充透明度。0 = 只留一圈细边（推荐）；1 = 整个物体被涂满")]
    [Range(0f, 1f)]
    public float fillAlpha = 0.12f;

    [Tooltip("指定要描边的物体。留空 = 用本物体（或下面子物体的）网格")]
    public Transform meshRoot;

    [Tooltip("要描边的【多个】物体（多部件模型用这个，比如医疗箱的箱体+盖子）。" +
             "填了这里就会额外加上它们，和 Mesh Root 一起生效")]
    public List<Transform> meshRoots = new List<Transform>();

    [Tooltip("是否只在悬停/抓取时显示（关掉就常亮，用于调试）")]
    public bool onlyWhenHovered = true;

    [Header("性能")]
    [Tooltip("最多给几个网格加轮廓。多部件模型（箱体+盖子）至少要 2")]
    public int maxMeshes = 2;

    [Tooltip("勾上后，根物体没有网格时会去子物体里找")]
    public bool searchChildren = false;

    [Tooltip("超过这个顶点数的网格跳过，避免拖慢性能")]
    public int maxVerticesPerMesh = 30000;

    [Header("调试")]
    [Tooltip("在 Console 打印高亮的构建过程和状态")]
    public bool logToConsole = false;

    [Tooltip("测试模式：轮廓变成纯红、不透明、穿透一切。开启后一定看得见（除非网格本身有问题）")]
    public bool testMode = false;

    // ---------- 内部 ----------
    private readonly List<GameObject> outlines = new List<GameObject>();

    /// <summary>每个轮廓对应的源网格（每帧跟随用）</summary>
    private readonly List<Transform> outlineSources = new List<Transform>();
    private Material outlineMaterial;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interactable;
    private bool isActive = false;
    private bool built = false;

    /// <summary>网格迟到时，隔多久重试一次（秒）</summary>
    private const float RetryInterval = 0.5f;
    private float retryTimer = 0f;

    /// <summary>轮廓外壳比本体大多少（形成"边"的厚度）= outlineWidth + 这个余量</summary>
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");

    private void Awake()
    {
        // ⚠️ 注意：不要在 Awake 里找 XRBaseInteractable！
        //    ItemPhysicsSetup.Awake() 会动态 AddComponent<XRGrabInteractable>()，
        //    而 AddComponent 会【立刻】执行新组件的 Awake。
        //    所以此刻 XRGrabInteractable 还没被加上，一定找不到。
        //    改到 Start（在所有 Awake 之后）再找。
    }

    /// <summary>在所有 Awake 跑完之后再找交互组件</summary>
    private void ResolveInteractable()
    {
        if (interactable != null) return;

        interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        if (interactable == null)
            interactable = GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();

        if (interactable == null)
            Debug.LogWarning($"[X光高亮] {name} 上没有 XRBaseInteractable，悬停不会触发高亮");
        else
            D($"[INT] 找到交互组件：{interactable.GetType().Name}");
    }

    private void Start()
    {
        D("[A] Start 被调用");
        ResolveInteractable();
        BuildOutlines();
        SetOutlineVisible(!onlyWhenHovered);
        D($"[B] Start 结束，轮廓数 = {outlines.Count}");
    }

    /// <summary>打印"几何中心"的两种算法结果，用于排查轮廓偏移</summary>
    private void LogCenterCompare(Transform src)
    {
        if (src == null) return;

        var mf = src.GetComponent<MeshFilter>();
        var mr = src.GetComponent<Renderer>();

        string meshStr = "（无网格）";
        string meshLocalStr = "（无网格）";
        if (mf != null && mf.sharedMesh != null)
        {
            meshLocalStr = mf.sharedMesh.bounds.center.ToString("F5");
            meshStr = src.TransformPoint(mf.sharedMesh.bounds.center).ToString("F5");
        }

        string rendStr = (mr != null) ? mr.bounds.center.ToString("F5") : "（无渲染器）";
        string rendLocalStr = (mr != null)
            ? src.InverseTransformPoint(mr.bounds.center).ToString("F5")
            : "（无渲染器）";

        if (logToConsole) Debug.Log($"[X光高亮] {name} ── 几何中心对比（源={src.name}）──\n" +
                  $"  ① 网格本地 bounds.center          = {meshLocalStr}\n" +
                  $"  ① → 转到世界坐标                   = {meshStr}\n" +
                  $"  ② 渲染器世界 bounds.center          = {rendStr}\n" +
                  $"  ② → 转回源本地坐标                 = {rendLocalStr}\n" +
                  $"  源 lossyScale = {src.lossyScale.ToString("F5")}\n" +
                  $"  源世界位置    = {src.position.ToString("F5")}\n" +
                  $"  源世界旋转    = {src.rotation.eulerAngles.ToString("F2")}");
    }

    /// <summary>诊断日志（带统一前缀，方便在 Console 里过滤）</summary>
    private void D(string msg)
    {
        if (logToConsole) Debug.Log($"[X光高亮] {name} {msg}");
    }

    private void OnDestroy()
    {
        foreach (var go in outlines) if (go != null) Destroy(go);
        outlines.Clear();
        outlineSources.Clear();
        if (outlineMaterial != null) Destroy(outlineMaterial);
    }

    /// <summary>每帧把轮廓对齐到源网格（轮廓放在场景根，需要自己跟随）</summary>
    private void LateUpdate()
    {
        if (outlines.Count == 0) return;

        for (int i = 0; i < outlines.Count; i++)
        {
            var go = outlines[i];
            if (go == null || !go.activeSelf) continue;
            if (i >= outlineSources.Count) continue;

            SyncOne(go, outlineSources[i]);
        }

        // 同步之后再打诊断（此时 Renderer.bounds 才是最新的）
        if (logToConsole) LogAlignmentOnce();
    }

    /// <summary>
    /// 把轮廓的世界位置/旋转/缩放对齐到源网格。
    ///
    /// 轮廓放在【场景根】，localScale 就是世界缩放，不受父级缩放链影响。
    ///
    /// ⚠️ 位置的关键（踩过的坑）：
    ///    网格的几何体可能【偏离它自己的原点】（比如本地 bounds.center 不为 0）。
    ///    轮廓用的是同一个网格，所以它的几何体也偏离同样的量。
    ///
    ///    如果我把轮廓的【原点】摆到几何中心上，
    ///    那轮廓的几何体就会再额外偏移一次 → 视觉上偏了 2 倍。
    ///
    ///    正确做法：让轮廓的【原点】和源物体的【原点】位置一致，
    ///    这样两者的几何体才会完全重合。
    /// </summary>
    private void SyncOne(GameObject go, Transform src)
    {
        if (go == null || src == null) return;

        // 源物体的原点在世界的位置
        // （注：不用 Renderer.bounds.center —— 那是几何中心，不是原点）
        Vector3 srcOriginWorld = src.position;

        go.transform.position = srcOriginWorld;
        go.transform.rotation = src.rotation;
        go.transform.localScale = src.lossyScale;
    }

    private bool loggedSync = false;

    /// <summary>
    /// 诊断：比较【轮廓副本】和【源网格】的实际渲染包围盒。
    /// 必须在 LateUpdate 里做 —— 因为同一帧内改完 Transform 后，
    /// Renderer.bounds 还没重算，会读到过期数据。
    /// </summary>
    private void LogAlignmentOnce()
    {
        if (loggedSync) return;
        if (outlines.Count == 0 || outlineSources.Count == 0) return;
        if (outlines[0] == null || outlineSources[0] == null) return;

        loggedSync = true;

        var go = outlines[0];
        var src = outlineSources[0];

        var orr = go.GetComponent<Renderer>();
        var srr = src.GetComponent<Renderer>();
        var mf = src.GetComponent<MeshFilter>();

        string cmp = "（缺少渲染器，无法比较）";
        if (orr != null && srr != null)
        {
            Vector3 oCenter = orr.bounds.center;
            Vector3 sCenter = srr.bounds.center;
            Vector3 diff = oCenter - sCenter;

            cmp = $"  轮廓渲染包围盒中心 = {oCenter.ToString("F4")}  size={orr.bounds.size.ToString("F4")}\n" +
                  $"  源  渲染包围盒中心 = {sCenter.ToString("F4")}  size={srr.bounds.size.ToString("F4")}\n" +
                  $"  ★ 中心差值 = {diff.ToString("F4")}   长度={diff.magnitude:F4} 米";
        }

        if (logToConsole) Debug.Log($"[X光高亮] {name} 轮廓对齐诊断（源={src.name}）\n" +
                  $"  轮廓 Transform.position = {go.transform.position.ToString("F4")}\n" +
                  $"  源   world position     = {src.position.ToString("F4")}\n" +
                  $"  轮廓 lossyScale         = {go.transform.lossyScale.ToString("F4")}\n" +
                  $"  源   lossyScale         = {src.lossyScale.ToString("F4")}\n" +
                  $"  网格本地 bounds.center   = {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center.ToString("F5") : "无")}\n" +
                  cmp);
    }

    private void Update()
    {
        // ---------- 延迟重建 ----------
        // 有些道具（比如手机）的 MeshFilter 在 Start 时还是空的，
        // 网格要等运行时才被赋上。这里检测到网格出现就重建轮廓。
        if (built && outlines.Count == 0)
        {
            retryTimer += Time.deltaTime;
            if (retryTimer >= RetryInterval)
            {
                retryTimer = 0f;
                TryRebuild();
            }
        }

        if (!built || interactable == null || outlines.Count == 0) return;

        bool want = !onlyWhenHovered
                    || interactable.isHovered
                    || interactable.isSelected;

        if (want == isActive) return;

        isActive = want;
        SetOutlineVisible(want);

        if (logToConsole) Debug.Log($"[X光高亮] {name} 轮廓 {(want ? "显示" : "隐藏")}");
    }

    /// <summary>网格迟到时重建轮廓</summary>
    private void TryRebuild()
    {
        // 先看现在有没有可用的网格
        bool hasMesh = false;
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf != null && mf.sharedMesh != null) { hasMesh = true; break; }
        }

        if (!hasMesh) return;

        if (logToConsole) Debug.Log($"[X光高亮] {name} 的网格出现了，重建轮廓");

        // 允许重新构建
        built = false;
        ClearOutlines();
        BuildOutlines();

        // 立刻按当前悬停状态显示
        if (outlines.Count > 0)
        {
            bool want = !onlyWhenHovered
                        || (interactable != null && (interactable.isHovered || interactable.isSelected));
            isActive = want;
            SetOutlineVisible(want);
        }
    }

    /// <summary>清掉已有的轮廓副本（保留材质）</summary>
    private void ClearOutlines()
    {
        foreach (var go in outlines)
            if (go != null) Destroy(go);
        outlines.Clear();
        outlineSources.Clear();
    }

    // ==================== 建轮廓 ====================
    private void BuildOutlines()
    {
        if (built) return;
        built = true;

        var shader = Shader.Find("SnowLineVR/XRayOutline");
        if (shader == null)
        {
            Debug.LogWarning("[X光高亮] 找不到 shader SnowLineVR/XRayOutline，轮廓无法显示");
            return;
        }

        // 自己建一份材质，避免多个物品共用导致颜色互相影响
        outlineMaterial = new Material(shader);
        outlineMaterial.name = "ItemOutline_Runtime";
        outlineMaterial.renderQueue = 4000;   // 最后渲染，盖在所有东西上

        // ---------- 关键：按缩放换算外扩量 ----------
        // shader 里的 _OutlineWidth 是在【物体空间】沿法线偏移的，
        // 而这个偏移量最终会被物体的 lossyScale 缩放。
        //
        // 例：手机被缩放了 0.0653 倍
        //     如果直接传 0.02，实际世界宽度只有 0.02 × 0.0653 = 1.3 毫米 → 看不见
        //     所以要先除以 lossyScale，渲染出来才是真的 0.02 米
        //
        // RefreshMaterial 里会处理颜色、宽度、以及测试模式
        RefreshMaterial();

        D($"[SCALE] lossyScale={F(transform.lossyScale)}  " +
          $"外扩比例={outlineWidth:F4}（{outlineWidth * 100f:F1}%）  " +
          $"填充透明度={fillAlpha:F2}  测试模式={(testMode ? "开" : "关")}");

        // ---------- 收集要描边的网格 ----------
        // 网格可能在【根物体自己身上】（GLB 道具常见），也可能在子物体上。
        var filters = new List<MeshFilter>();
        var renderers = new List<MeshRenderer>();

        // ① 优先用指定的 meshRoot / meshRoots
        if (meshRoot != null)
        {
            TryAddMesh(meshRoot, filters, renderers);
        }
        foreach (var root in meshRoots)
        {
            if (root != null) TryAddMesh(root, filters, renderers);
        }

        // ② 根物体自己
        if (filters.Count < maxMeshes)
            TryAddMesh(transform, filters, renderers);

        // ③ 子物体（可选）
        //    箱子这种"多部件拼装"的物体要关掉，
        //    否则盖子、铰链也会被描边，旋转时会跟着乱飞。
        if (searchChildren && filters.Count < maxMeshes)
        {
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filters.Count >= maxMeshes) break;
                if (mf == null) continue;
                TryAddMesh(mf.transform, filters, renderers);
            }
        }

        if (filters.Count == 0)
        {
            // 常见于"网格在运行时才赋给 MeshFilter"的道具（比如手机）：
            // 场景里 MeshFilter 是空的，但游戏跑起来后会被代码填上。
            // 所以这里不能报错放弃 —— 交给 Update 里的延迟重建处理。
            var allMf = GetComponentsInChildren<MeshFilter>(true);
            D($"[C1] 没找到网格，放弃。MeshFilter 总数 = {allMf.Length}");

            foreach (var m in allMf)
            {
                if (m == null) continue;
                D($"[C2]   子项 {m.name}：网格 = " +
                  $"{(m.sharedMesh == null ? "null" : m.sharedMesh.name + " 顶点" + m.sharedMesh.vertexCount)}" +
                  $"  渲染器 = {(m.GetComponent<MeshRenderer>() != null ? "有" : "无")}");
            }
            return;
        }

        if (logToConsole)
        {
            // 逐个打印网格信息，方便定位"找到了但看不见"的问题
            for (int i = 0; i < filters.Count; i++)
            {
                var srcMf = filters[i];
                var mesh = srcMf.sharedMesh;
                Bounds b = renderers[i].bounds;

                D($"[D{i}] 对象={srcMf.name}  网格={mesh.name}  顶点={mesh.vertexCount}  " +
                  $"子网格={mesh.subMeshCount}");
                D($"[D{i}]   世界中心=({b.center.x:F3},{b.center.y:F3},{b.center.z:F3})  " +
                  $"世界大小=({b.size.x:F3},{b.size.y:F3},{b.size.z:F3})");
                D($"[D{i}]   lossyScale={F(srcMf.transform.lossyScale)}  " +
                  $"本地大小={F(srcMf.transform.localScale)}  世界位置={F(srcMf.transform.position)}");
            }
        }

        // ---------- 给每个网格建一个轮廓副本 ----------
        float k = Mathf.Max(outlineWidth, 0.0001f);   // 外扩比例，例如 0.03

        for (int i = 0; i < filters.Count; i++)
        {
            var srcMf = filters[i];

            // ⚠️ 轮廓【不设父物体】，直接放在场景根。
            //    这样它的 localScale 就是世界缩放，完全不受父级缩放链、
            //    父级位置、父级旋转的影响 —— 缩放和位置都只由我们自己控制。
            //    位置/旋转/缩放在 SyncOne() 里每帧跟随源网格。
            var go = new GameObject($"__Outline_{i}");

            // 立刻同步一次（避免第一帧出现在原点）
            SyncOne(go, srcMf.transform);

            var nmf = go.AddComponent<MeshFilter>();
            nmf.sharedMesh = srcMf.sharedMesh;

            var nmr = go.AddComponent<MeshRenderer>();
            nmr.sharedMaterial = outlineMaterial;
            nmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            nmr.receiveShadows = false;
            nmr.allowOcclusionWhenDynamic = false;

            outlines.Add(go);
            outlineSources.Add(srcMf.transform);

            // 打印几何中心对比（只在第一个网格上打一次）
            if (logToConsole && outlines.Count == 1)
                LogCenterCompare(srcMf.transform);

            D($"[G{i}] 轮廓副本（场景根，无父物体）：\n" +
              $"      源网格 = {srcMf.name}\n" +
              $"      源 lossyScale = {srcMf.transform.lossyScale}\n" +
              $"      轮廓世界位置 = {go.transform.position}\n" +
              $"      轮廓世界缩放 = {go.transform.lossyScale}\n" +
              $"      外扩比例 = {k:F4}");
        }

        D($"[E] 建了 {outlines.Count} 个轮廓副本");

        // 打印轮廓副本的实际状态，用于排查"建了但看不见"
        foreach (var go in outlines)
        {
            if (go == null) continue;
            var omr = go.GetComponent<MeshRenderer>();
            var omf = go.GetComponent<MeshFilter>();

            string matInfo = "无材质";
            if (omr != null && omr.sharedMaterial != null)
            {
                var m = omr.sharedMaterial;
                Color mc = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.magenta;
                matInfo = $"shader={m.shader.name}  color=({mc.r:F2},{mc.g:F2},{mc.b:F2},{mc.a:F2})";
            }

            D($"[F] {go.name}  激活={go.activeSelf}  " +
              $"网格={(omf != null && omf.sharedMesh != null ? omf.sharedMesh.name : "无")}  " +
              $"世界位置={F(go.transform.position)}  缩放={F(go.transform.lossyScale)}");
            D($"[F]   {matInfo}  渲染器={(omr != null ? (omr.enabled ? "启用" : "禁用") : "无")}");
        }
    }

    private static string F(Vector3 v) => $"({v.x:F4},{v.y:F4},{v.z:F4})";

    /// <summary>判断某个物体能不能描边，能就登记进来</summary>
    private void TryAddMesh(Transform t,
        List<MeshFilter> filters, List<MeshRenderer> renderers)
    {
        if (t == null) return;
        if (filters.Count >= maxMeshes) return;

        // 不能给自己创建的轮廓副本再描边（否则无限套娃）
        if (t.name.StartsWith("__Outline_")) return;

        var mf = t.GetComponent<MeshFilter>();
        var mr = t.GetComponent<MeshRenderer>();

        if (mf == null)
        {
            if (logToConsole) Debug.Log($"[X光高亮]   · {t.name}：没有 MeshFilter，跳过");
            return;
        }

        if (mf.sharedMesh == null)
        {
            // "空网格"：有 MeshFilter 但没指定 mesh。
            // 常见于把多个子网格拼起来的道具（比如手机）
                if (logToConsole) Debug.Log($"[X光高亮]   · {t.name}：MeshFilter 是空的，跳过（继续找子物体）");
            return;
        }

        if (mr == null)
        {
            if (logToConsole) Debug.Log($"[X光高亮]   · {t.name}：没有 MeshRenderer，跳过");
            return;
        }

        if (mf.sharedMesh.vertexCount > maxVerticesPerMesh)
        {
            if (logToConsole)
                Debug.Log($"[X光高亮]   · {t.name}：顶点太多" +
                          $"（{mf.sharedMesh.vertexCount} > {maxVerticesPerMesh}），跳过");
            return;
        }

        if (filters.Contains(mf)) return;

        filters.Add(mf);
        renderers.Add(mr);
    }

    private void SetOutlineVisible(bool visible)
    {
        foreach (var go in outlines)
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
    }

    /// <summary>运行时改颜色/粗细立即生效</summary>
    private void RefreshMaterial()
    {
        if (outlineMaterial == null) return;

        if (testMode)
        {
            // 测试模式：纯红不透明，外扩比例调到很大
            if (outlineMaterial.HasProperty(BaseColorId))
                outlineMaterial.SetColor(BaseColorId, Color.red);
            if (outlineMaterial.HasProperty(OutlineWidthId))
                outlineMaterial.SetFloat(OutlineWidthId, 0.3f);
            if (outlineMaterial.HasProperty(FillAlphaId))
                outlineMaterial.SetFloat(FillAlphaId, 0.6f);

            if (logToConsole) Debug.Log($"[X光高亮] {name} 【测试模式】纯红 + 外扩 0.3");
        }
        else
        {
            if (outlineMaterial.HasProperty(BaseColorId))
                outlineMaterial.SetColor(BaseColorId, outlineColor);

            // 外扩是【比例】，不用管物体缩放
            if (outlineMaterial.HasProperty(OutlineWidthId))
                outlineMaterial.SetFloat(OutlineWidthId, outlineWidth);

            // 内部填充透明度
            if (outlineMaterial.HasProperty(FillAlphaId))
                outlineMaterial.SetFloat(FillAlphaId, fillAlpha);
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying) RefreshMaterial();
    }

    /// <summary>供外部强制开关（比如步骤系统要求"只高亮某件道具"）</summary>
    public void ForceShow(bool show)
    {
        onlyWhenHovered = false;
        isActive = show;
        SetOutlineVisible(show);
    }
}
