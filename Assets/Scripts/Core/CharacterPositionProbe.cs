using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 【诊断工具·运行时】在 Play 模式下实时打印人物各关键点的真实世界坐标。
///
/// 目的：
///   搞清楚"根物体位置"和"顶点最低点"的真实关系，不再靠猜。
///
/// 用法：
///   ① 挂在场景里任意物体上（推荐挂在 Armature 上）
///   ② Play
///   ③ 看屏幕上的青色文字，或 Console 输出
///   ④ 用 Animation Tester 切几个动画，观察数值变化
/// </summary>
public class CharacterPositionProbe : MonoBehaviour
{
    [Header("显示")]
    public bool showOnScreen = true;
    public bool logToConsole = false;

    [Tooltip("每几秒打印一次 Console")]
    public float logInterval = 2f;

    [Header("采样")]
    [Tooltip("是否每帧计算顶点最低点（较耗性能，诊断时可以开）")]
    public bool measureLowestVertex = true;

    [Tooltip("顶点采样步长（越大越快，100 表示每 100 个顶点取一个）")]
    public int vertexStep = 100;

    private Animator animator;
    private SkinnedMeshRenderer smr;
    private Transform armature;
    private Transform hips;
    private Transform feetL, feetR;

    private float logTimer;
    private float cachedLowestY = float.NaN;
    private int cachedFrame = -1;

    private void Start()
    {
        // 找 Animator
        foreach (var a in FindObjectsByType<Animator>(FindObjectsInactive.Include))
        {
            if (a == null) continue;
            if (a.runtimeAnimatorController != null) { animator = a; break; }
        }

        if (animator != null)
        {
            armature = animator.transform;

            // 找网格（可能在兄弟物体上）
            smr = animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.bones == null || smr.bones.Length < 10)
            {
                Transform root = animator.transform.root;
                foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (s != null && s.bones != null && s.bones.Length >= 10) { smr = s; break; }
                }
            }

            // 找关键骨骼
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name.ToLower();
                if (hips == null && (n.EndsWith(":hips") || n == "hips")) hips = t;
                if (feetL == null && (n.Contains("leftfoot") || n.Contains("left_foot"))) feetL = t;
                if (feetR == null && (n.Contains("rightfoot") || n.Contains("right_foot"))) feetR = t;
            }
        }

        Debug.Log($"[探针] Animator={animator?.name ?? "无"}  " +
                  $"网格={smr?.name ?? "无"}  骨骼数={(smr?.bones?.Length ?? 0)}");
    }

    private void Update()
    {
        cachedFrame = -1;   // 每帧重算

        logTimer += Time.deltaTime;
        if (logTimer < logInterval) return;
        logTimer = 0f;

        if (logToConsole) Debug.Log(BuildReport());
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ 人物位置探针 ═══");

        if (armature == null) { sb.AppendLine("找不到 Animator"); return sb.ToString(); }

        sb.AppendLine($"【根物体 {armature.name}】");
        sb.AppendLine($"  世界位置 {F(armature.position)}");
        sb.AppendLine($"  本地位置 {F(armature.localPosition)}");
        sb.AppendLine($"  lossyScale {F(armature.lossyScale)}");
        if (armature.parent != null)
        {
            sb.AppendLine($"  父物体 {armature.parent.name}");
            sb.AppendLine($"    父世界位置 {F(armature.parent.position)}");
            sb.AppendLine($"    父lossyScale {F(armature.parent.lossyScale)}");
        }

        if (hips != null)
        {
            sb.AppendLine($"【Hips】世界位置 {F(hips.position)}");
        }
        if (feetL != null)
            sb.AppendLine($"【左脚】世界位置 {F(feetL.position)}");
        if (feetR != null)
            sb.AppendLine($"【右脚】世界位置 {F(feetR.position)}");

        // 顶点最低点
        float lowest = GetLowestVertexY();
        if (!float.IsNaN(lowest))
            sb.AppendLine($"【顶点最低点 Y】{lowest:F4}");

        // 当前状态
        int st = 0;
        try { st = animator != null ? animator.GetInteger("State") : 0; } catch { }
        sb.AppendLine($"【State 参数】{st}");

        // 贴地组件
        var align = GetComponent<PatientGroundAlign>();
        if (align == null && armature != null)
            align = armature.GetComponent<PatientGroundAlign>();
        if (align != null)
        {
            sb.AppendLine($"【贴地组件】{(align.enabledAlign ? "启用" : "已禁用")}  " +
                          $"（组件本身{(align.enabled ? "启用" : "禁用")}）");
            sb.AppendLine($"  baseY={align.baseY:F4}  targetFootY={align.targetFootY:F4}  " +
                          $"extraOffset={align.extraOffset:F4}");
            if (align.offsets != null && st >= 0 && st < align.offsets.Length)
                sb.AppendLine($"  当前状态偏移={align.offsets[st]:F4}");
            if (align.stateNames != null && st >= 0 && st < align.stateNames.Length)
                sb.AppendLine($"  当前状态名={align.stateNames[st]}");
        }
        else sb.AppendLine("【贴地组件】没找到");

        return sb.ToString();
    }

    private float GetLowestVertexY()
    {
        if (!measureLowestVertex) return float.NaN;
        if (cachedFrame == Time.frameCount) return cachedLowestY;
        cachedFrame = Time.frameCount;

        if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) return float.NaN;
        var bones = smr.bones;
        if (bones == null || bones.Length == 0) return float.NaN;

        var mesh = smr.sharedMesh;
        var verts = mesh.vertices;
        var weights = mesh.boneWeights;
        var bindposes = mesh.bindposes;
        if (bindposes.Length != bones.Length) return float.NaN;

        var matrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            matrices[i] = (bones[i] != null) ? bones[i].localToWorldMatrix * bindposes[i] : Matrix4x4.identity;

        float minY = float.MaxValue;
        int step = Mathf.Max(1, vertexStep);

        for (int v = 0; v < verts.Length; v += step)
        {
            var bw = weights[v];
            Vector3 p = Vector3.zero;
            float tw = 0f;
            Acc(matrices, verts[v], bw.boneIndex0, bw.weight0, ref p, ref tw);
            Acc(matrices, verts[v], bw.boneIndex1, bw.weight1, ref p, ref tw);
            Acc(matrices, verts[v], bw.boneIndex2, bw.weight2, ref p, ref tw);
            Acc(matrices, verts[v], bw.boneIndex3, bw.weight3, ref p, ref tw);
            if (tw <= 0.0001f) continue;
            float y = p.y / tw;
            if (y < minY) minY = y;
        }

        cachedLowestY = (minY == float.MaxValue) ? float.NaN : minY;
        return cachedLowestY;
    }

    private static void Acc(Matrix4x4[] mats, Vector3 vert, int idx, float w,
        ref Vector3 sum, ref float total)
    {
        if (w <= 0f || idx < 0 || idx >= mats.Length) return;
        Vector4 p = mats[idx] * vert;
        sum += new Vector3(p.x, p.y, p.z) * w;
        total += w;
    }

    private static string F(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";

    private void OnGUI()
    {
        if (!showOnScreen) return;

        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = 16;
        style.normal.textColor = Color.cyan;
        style.wordWrap = false;

        GUI.Label(new Rect(20, 400, 1000, 300), BuildReport(), style);
    }
}
