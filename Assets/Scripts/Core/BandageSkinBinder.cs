using UnityEngine;

/// <summary>
/// 绷带蒙皮绑定器 —— 把绷带网格转换成【由角色骨骼驱动的 SkinnedMeshRenderer】，
/// 让它像皮肤一样跟着膝盖弯曲变形。
///
/// ===================== 为什么需要它 =====================
///
///   刚性模型只能整体跟着一根骨骼走，膝盖一弯就会穿模。
///   要让绷带「贴紧并变形」，必须按【顶点】做骨骼权重混合 —— 也就是蒙皮。
///
///   本脚本用你放在腿上的三个锚点，自动算出每个顶点的权重：
///       大腿锚点  →  权重 (大腿 100%, 小腿   0%)
///       膝盖锚点  →  权重 (大腿  50%, 小腿  50%)
///       小腿锚点  →  权重 (大腿   0%, 小腿 100%)
///   中间按位置线性插值 —— 和角色蒙皮网格用的是同一套机制（线性混合蒙皮 LBS）。
///
/// ===================== 重要说明 =====================
///
///   ⚠️ 锚点【只在绑定时用一次】，绑定完就没用了。
///
///   ⚠️ 网格必须可读：
///      选中 FBX → Inspector → Model → 勾 Read/Write Enabled → Apply
///
///   ⚠️ 本脚本会：
///       ① 新建一个子物体 __Skinned，把真正的蒙皮网格放在它上面
///       ② 把本物体和子物体上【原来的所有渲染器】关掉（避免重复渲染 / 旧模型干扰）
///      所以不会受 FBX 自带蒙皮骨架的影响。
///
/// ===================== 用法 =====================
///
///   ① 把绷带模型拖进场景，摆成「竖直套在腿上」的姿势
///   ② 右键 → Prefab → Unpack Prefab Completely
///   ③ 在腿上建三个空物体：Anchor_Thigh / Anchor_Knee / Anchor_Calf
///   ④ 把本脚本挂在【绷带物体】上（带网格的那个）
///   ⑤ 拖引用：
///        Thigh Anchor ← Anchor_Thigh
///        Knee Anchor  ← Anchor_Knee
///        Calf Anchor  ← Anchor_Calf
///        Thigh Bone   ← mixamorig:LeftUpLeg
///        Calf Bone    ← mixamorig:LeftLeg
///   ⑥ Play
/// </summary>
public class BandageSkinBinder : MonoBehaviour
{
    [Header("腿上的三个锚点（只在绑定时用一次）")]
    [Tooltip("大腿端的锚点：绷带上端对应的腿上位置")]
    public Transform thighAnchor;

    [Tooltip("膝关节的锚点。留空 = 退化成「两点线性插值」")]
    public Transform kneeAnchor;

    [Tooltip("小腿端的锚点：绷带下端对应的腿上位置")]
    public Transform calfAnchor;

    [Header("要跟随的两根骨骼")]
    [Tooltip("大腿骨骼，通常是 mixamorig:LeftUpLeg")]
    public Transform thighBone;

    [Tooltip("小腿骨骼，通常是 mixamorig:LeftLeg")]
    public Transform calfBone;

    [Header("选项")]
    [Tooltip("进游戏后自动绑定（在第一帧 LateUpdate 里执行，确保动画姿势已经生效）")]
    public bool bindOnStart = true;

    [Tooltip("绑定后把本物体和子物体上原来的渲染器全部关掉")]
    public bool disableOriginalRenderers = true;

    [Header("调试")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private bool bound;
    private Mesh sourceMesh;
    private SkinnedMeshRenderer smr;
    private Transform holder;
    private Material[] savedMaterials;

    private const string HolderName = "__Skinned";

    private void LateUpdate()
    {
        if (!bound && bindOnStart) Bind();
    }

    /// <summary>
    /// 执行绑定。在 Inspector 里点组件右上角 ⋮ →「重新绑定蒙皮」也能手动调用。
    /// </summary>
    [ContextMenu("重新绑定蒙皮")]
    public void Bind()
    {
        // ==================== 1. 找源网格 ====================
        if (!ResolveSourceMesh(out string sourceInfo))
            return;

        if (!sourceMesh.isReadable)
        {
            Debug.LogError($"[绷带蒙皮] 网格「{sourceMesh.name}」不可读，无法计算权重。\n" +
                           $"  修法：Project 里选中这个模型 → Inspector → Model 标签 →\n" +
                           $"        勾上【Read/Write Enabled】→ 点 Apply", this);
            return;
        }

        // ==================== 2. 校验引用 ====================
        if (thighAnchor == null || calfAnchor == null)
        {
            Debug.LogError($"[绷带蒙皮] 「{name}」：Thigh Anchor / Calf Anchor 必须指定", this);
            return;
        }

        if (thighBone == null || calfBone == null)
        {
            Debug.LogError($"[绷带蒙皮] 「{name}」：Thigh Bone / Calf Bone 必须指定", this);
            return;
        }

        // ==================== 3. 复制网格 ====================
        var mesh = Instantiate(sourceMesh);
        mesh.name = sourceMesh.name + "_Skinned";

        var verts = sourceMesh.vertices;

        // ==================== 4. 算每个顶点的权重 ====================
        Vector3 pThigh = thighAnchor.position;
        Vector3 pKnee = kneeAnchor != null
            ? kneeAnchor.position
            : Vector3.Lerp(thighAnchor.position, calfAnchor.position, 0.5f);
        Vector3 pCalf = calfAnchor.position;

        // 注意：顶点是【网格本地坐标】，要先转到世界再和锚点比
        Matrix4x4 meshLocalToWorld = transform.localToWorldMatrix;

        var weights = new BoneWeight[verts.Length];

        int countThigh = 0;     // 几乎 100% 大腿
        int countBlend = 0;     // 混合
        int countCalf = 0;      // 几乎 100% 小腿

        float tMin = 1f, tMax = 0f;

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 world = meshLocalToWorld.MultiplyPoint3x4(verts[i]);
            float t = ComputeT(world, pThigh, pKnee, pCalf, kneeAnchor != null);

            if (t < tMin) tMin = t;
            if (t > tMax) tMax = t;

            weights[i].boneIndex0 = 0;
            weights[i].weight0 = 1f - t;     // 大腿
            weights[i].boneIndex1 = 1;
            weights[i].weight1 = t;          // 小腿

            if (t < 0.02f) countThigh++;
            else if (t > 0.98f) countCalf++;
            else countBlend++;
        }

        mesh.boneWeights = weights;

        // ==================== 5. bindpose ====================
        Matrix4x4[] bindposes = new Matrix4x4[2];
        bindposes[0] = thighBone.worldToLocalMatrix * meshLocalToWorld;
        bindposes[1] = calfBone.worldToLocalMatrix * meshLocalToWorld;
        mesh.bindposes = bindposes;

        float checkError = SelfCheckBindPoses(verts, meshLocalToWorld, bindposes);

        // ==================== 6. 建一个干净的子物体承载蒙皮渲染 ====================
        holder = transform.Find(HolderName);
        if (holder == null)
        {
            var go = new GameObject(HolderName);
            holder = go.transform;
            holder.SetParent(transform, false);
        }

        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one;

        smr = holder.GetComponent<SkinnedMeshRenderer>();
        if (smr == null) smr = holder.gameObject.AddComponent<SkinnedMeshRenderer>();

        smr.sharedMesh = mesh;
        smr.bones = new[] { thighBone, calfBone };
        smr.rootBone = thighBone;
        smr.updateWhenOffscreen = true;
        smr.enabled = true;
        smr.sharedMaterials = BuildMaterials(mesh);

        // ==================== 7. 关掉所有旧渲染器 ====================
        if (disableOriginalRenderers)
        {
            int hidden = DisableOriginalRenderers();

            if (logToConsole && hidden > 0)
                Debug.Log($"[绷带蒙皮] 「{name}」已关闭 {hidden} 个旧渲染器（含 FBX 自带的蒙皮渲染器）", this);
        }

        bound = true;

        // ==================== 8. 报告 ====================
        if (logToConsole) LogReport(verts.Length, mesh.subMeshCount, tMin, tMax,
                                    countThigh, countBlend, countCalf, checkError, sourceInfo, mesh);
    }

    // ==================== 源网格解析 ====================
    /// <summary>
    /// 找出网格来源。可能是 MeshFilter＋MeshRenderer，也可能是 FBX 自带的 SkinnedMeshRenderer。
    /// </summary>
    private bool ResolveSourceMesh(out string info)
    {
        info = "";

        var mf = GetComponent<MeshFilter>();
        var srcSmr = GetComponent<SkinnedMeshRenderer>();
        var srcMr = GetComponent<MeshRenderer>();

        // 优先 MeshFilter
        if (mf != null && mf.sharedMesh != null)
        {
            sourceMesh = mf.sharedMesh;
            if (srcMr != null) savedMaterials = srcMr.sharedMaterials;

            info = $"MeshFilter + {(srcMr != null ? "MeshRenderer" : "无渲染器")}";
            return true;
        }

        // 其次 FBX 自带的 SkinnedMeshRenderer
        if (srcSmr != null && srcSmr.sharedMesh != null)
        {
            sourceMesh = srcSmr.sharedMesh;
            savedMaterials = srcSmr.sharedMaterials;
            info = "FBX 自带的 SkinnedMeshRenderer";
            return true;
        }

        Debug.LogError($"[绷带蒙皮] 「{name}」上找不到任何网格（没有 MeshFilter 也没有带网格的 SkinnedMeshRenderer）", this);
        return false;
    }

    // ==================== 材质 ====================
    private Material[] BuildMaterials(Mesh mesh)
    {
        int need = Mathf.Max(1, mesh.subMeshCount);

        // 优先用原来渲染器的材质
        if (savedMaterials != null && savedMaterials.Length > 0)
        {
            if (savedMaterials.Length == need) return savedMaterials;

            var fixedArr = new Material[need];
            for (int i = 0; i < need; i++)
                fixedArr[i] = savedMaterials[Mathf.Min(i, savedMaterials.Length - 1)];
            return fixedArr;
        }

        // 兜底：默认材质，至少能看见
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = "Bandage_Fallback";
        Debug.LogWarning($"[绷带蒙皮] 「{name}」没找到原材质，用默认材质替代", this);

        var arr = new Material[need];
        for (int i = 0; i < need; i++) arr[i] = mat;
        return arr;
    }

    // ==================== 关闭旧渲染器 ====================
    private int DisableOriginalRenderers()
    {
        int n = 0;

        // 本物体 + 所有子物体上的渲染器（但跳过我们自己建的 holder）
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (smr != null && r == smr) continue;          // 跳过新的
            if (holder != null && r.transform.IsChildOf(holder)) continue;  // 跳过 holder 下

            if (r.enabled)
            {
                r.enabled = false;
                n++;
            }
        }

        return n;
    }

    // ==================== bindpose 自检 ====================
    /// <summary>
    /// 用算出来的 bindpose 对几个顶点做一次蒙皮，
    /// 结果应该等于顶点的原始世界位置（误差 ≈ 0）。
    /// </summary>
    private float SelfCheckBindPoses(Vector3[] verts, Matrix4x4 localToWorld, Matrix4x4[] bindposes)
    {
        if (verts.Length == 0) return 0f;

        Matrix4x4 skinA = thighBone.localToWorldMatrix * bindposes[0];
        Matrix4x4 skinB = calfBone.localToWorldMatrix * bindposes[1];

        int samples = Mathf.Min(12, verts.Length);
        int step = Mathf.Max(1, verts.Length / samples);

        float maxError = 0f;

        for (int i = 0; i < verts.Length; i += step)
        {
            Vector3 originalWorld = localToWorld.MultiplyPoint3x4(verts[i]);

            // bindpose 正确时，任何权重都应该还原到同一位置
            Vector3 skinned = Vector3.Lerp(
                skinA.MultiplyPoint3x4(verts[i]),
                skinB.MultiplyPoint3x4(verts[i]),
                0.5f);

            float err = Vector3.Distance(originalWorld, skinned);
            if (err > maxError) maxError = err;
        }

        return maxError;
    }

    // ==================== 报告 ====================
    private void LogReport(int vertCount, int subMeshCount, float tMin, float tMax,
                           int cThigh, int cBlend, int cCalf, float checkError,
                           string sourceInfo, Mesh mesh)
    {
        string warn = "";
        if (tMax - tMin < 0.05f) warn += "\n  ⚠️ t 范围太窄：锚点可能摆错了";
        if (checkError > 0.01f) warn += $"\n  ❌ bindpose 自检误差 {checkError:F4} 米 —— 蒙皮矩阵算错了";
        if (cBlend == 0) warn += "\n  ❌ 没有任何混合权重的顶点 —— 绷带不会弯曲";

        // 回读验证：确认权重真的写进网格了
        var readback = mesh.boneWeights;
        string readbackInfo = "回读失败";
        if (readback != null && readback.Length > 0)
        {
            int mid = readback.Length / 2;
            readbackInfo = $"首顶点 weight0≈{readback[0].weight0:F2}，中间顶点 weight0≈{readback[mid].weight0:F2}";
        }

        Debug.Log($"[绷带蒙皮] 「{name}」绑定完成：\n" +
                  $"  网格来源 = {sourceInfo}\n" +
                  $"  顶点数 = {vertCount}  子网格数 = {subMeshCount}\n" +
                  $"  t 范围 = {tMin:F3} ~ {tMax:F3}\n" +
                  $"  权重分布 = 大腿端 {cThigh} 个 / 混合 {cBlend} 个 / 小腿端 {cCalf} 个\n" +
                  $"  权重回读 = {readbackInfo}\n" +
                  $"  骨骼 = {thighBone.name} / {calfBone.name}" +
                  (kneeAnchor == null ? "  （没设膝盖锚点）" : "") + "\n" +
                  $"  承载物体的父级 = {(holder.parent != null ? holder.parent.name : "无")}\n" +
                  $"  bindposes = {mesh.bindposes.Length} 个，bones = {smr.bones.Length} 个\n" +
                  $"  bindpose 自检误差 = {checkError:F6} 米" + warn, this);
    }

    // ==================== 位置 → 参数 t ====================
    private static float ComputeT(Vector3 p, Vector3 a, Vector3 b, Vector3 c, bool hasKnee)
    {
        if (!hasKnee) return ProjectParam(p, a, c, out _);

        float u1 = ProjectParam(p, a, b, out float d1);
        float u2 = ProjectParam(p, b, c, out float d2);

        if (d1 <= d2) return 0.5f * u1;
        return 0.5f + 0.5f * u2;
    }

    private static float ProjectParam(Vector3 p, Vector3 a, Vector3 b, out float dist)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;

        if (len2 < 1e-10f)
        {
            dist = Vector3.Distance(p, a);
            return 0f;
        }

        float u = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        Vector3 closest = a + ab * u;
        dist = Vector3.Distance(p, closest);
        return u;
    }

    /// <summary>是否已经绑定过</summary>
    public bool IsBound => bound;
}
