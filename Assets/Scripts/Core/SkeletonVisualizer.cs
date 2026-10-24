using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 骨架可视化 —— 把骨骼画出来，让你能直接看见绑定的对不对。
///
/// 为什么需要：
///   Unity 不像 Blender 那样默认显示骨架。只有选中骨骼物体时，
///   Unity 才画一个小小的图标，而且看不出骨骼之间的连接关系。
///
/// 本组件画三样东西：
///   · 关节点（小球）—— 每根骨骼的位置
///   · 连接线（棍）  —— 父子骨骼之间的关系
///   · 名称标签      —— 可选，方便对照骨骼名
///
/// 关键：支持【Play 模式】下显示 —— 只有播动画时骨骼才会动，
///       静止时看不出绑定对不对。
///
/// 用法：
///   ① 选中人物模型
///   ② 工具 → 显示/隐藏骨架
///   ③ 进 Play 模式播动画，看骨骼是不是跟着身体动
/// </summary>
public class SkeletonVisualizer : MonoBehaviour
{
    [Header("显示开关")]
    [Tooltip("总开关")]
    public bool show = true;

    [Tooltip("显示关节点")]
    public bool showJoints = true;

    [Tooltip("显示连接线")]
    public bool showBones = true;

    [Tooltip("显示骨骼名（需要 Gizmos 模式，运行时不显示）")]
    public bool showNames = true;

    [Tooltip("Play 模式下也显示")]
    public bool showInPlayMode = true;

    [Header("过滤")]
    [Tooltip("隐藏扭转/辅助骨骼 —— 它们数量多且位置重叠，会挡住主骨架")]
    public bool hideHelperBones = true;

    [Tooltip("辅助骨骼关键字")]
    public string helperKeywords = "twist,roll,helper,nub,end,ik,fk";

    [Header("外观")]
    public Color jointColor = new Color(1f, 0.85f, 0.2f, 1f);
    public Color boneColor = new Color(0.2f, 0.9f, 1f, 1f);
    public Color meshBoundColor = new Color(1f, 0.3f, 0.6f, 0.6f);

    [Tooltip("关节点大小（米）")]
    public float jointSize = 0.02f;

    [Tooltip("连接线粗细（米）")]
    public float boneWidth = 0.008f;

    [Tooltip("是否显示网格包围盒（用于判断骨骼是否在身体内）")]
    public bool showMeshBounds = true;

    // 运行时画的实体
    private readonly List<GameObject> runtimeObjects = new List<GameObject>();
    private Transform[] bones;
    private SkinnedMeshRenderer skinned;
    private Material lineMat;
    private Material jointMat;

    private void Awake() => CacheBones();

    private void OnEnable()
    {
        CacheBones();
        if (Application.isPlaying && showInPlayMode) BuildRuntimeVisuals();
    }

    private void OnDisable() => ClearRuntimeVisuals();
    private void OnDestroy() => ClearRuntimeVisuals();

    private void CacheBones()
    {
        if (skinned == null) skinned = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skinned != null && skinned.bones != null) bones = skinned.bones;
    }

    // ==================== 编辑器 Gizmos ====================
    private void OnDrawGizmos()
    {
        if (!show) return;
        if (Application.isPlaying && !showInPlayMode) return;

        CacheBones();
        if (bones == null || bones.Length == 0) return;

        // ---------- 网格包围盒（判断骨骼在不在身体里）----------
        if (showMeshBounds && skinned != null)
        {
            Gizmos.color = meshBoundColor;
            Gizmos.DrawWireCube(skinned.bounds.center, skinned.bounds.size);
        }

        // ---------- 关节点 ----------
        if (showJoints)
        {
            Gizmos.color = jointColor;
            foreach (var b in bones)
            {
                if (b == null) continue;
                if (hideHelperBones && IsHelper(b.name)) continue;
                Gizmos.DrawSphere(b.position, jointSize);
            }
        }

        // ---------- 连接线 ----------
        if (showBones)
        {
            Gizmos.color = boneColor;
            foreach (var b in bones)
            {
                if (b == null || b.parent == null) continue;
                if (hideHelperBones && IsHelper(b.name)) continue;

                // 只连到同样可见的父骨骼，避免线乱飞
                if (hideHelperBones && IsHelper(b.parent.name)) continue;

                Gizmos.DrawLine(b.parent.position, b.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 选中时用更亮的颜色再画一遍
        if (!show || bones == null) return;

        Gizmos.color = Color.white;
        foreach (var b in bones)
        {
            if (b == null) continue;
            Gizmos.DrawWireSphere(b.position, jointSize * 1.6f);
        }
    }

    // ==================== 运行时实体 ====================
    private void BuildRuntimeVisuals()
    {
        ClearRuntimeVisuals();
        if (bones == null || bones.Length == 0 || !show) return;

        EnsureMaterials();

        var root = new GameObject("~SkeletonVisuals");
        root.transform.SetParent(null);   // 不挂模型下，避免被动画影响
        root.hideFlags = HideFlags.DontSave;
        runtimeObjects.Add(root);

        // ---- 关节点 ----
        if (showJoints)
        {
            foreach (var b in bones)
            {
                if (b == null) continue;
                if (hideHelperBones && IsHelper(b.name)) continue;

                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "J_" + b.name;
                s.transform.SetParent(root.transform, true);
                s.transform.position = b.position;
                s.transform.localScale = Vector3.one * jointSize * 2f;
                StripAndColor(s, jointMat);
                s.AddComponent<FollowBone>().Init(b, Vector3.zero);
                runtimeObjects.Add(s);
            }
        }

        // ---- 连接线 ----
        if (showBones)
        {
            foreach (var b in bones)
            {
                if (b == null || b.parent == null) continue;
                if (hideHelperBones && IsHelper(b.name)) continue;
                if (hideHelperBones && IsHelper(b.parent.name)) continue;

                var line = new GameObject("L_" + b.name);
                line.transform.SetParent(root.transform, true);
                var lr = line.AddComponent<LineRenderer>();
                lr.material = lineMat;
                lr.startWidth = boneWidth;
                lr.endWidth = boneWidth;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.startColor = boneColor;
                lr.endColor = boneColor;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                var f = line.AddComponent<FollowBonePair>();
                f.Init(b.parent, b);
                runtimeObjects.Add(line);
            }
        }

        Debug.Log($"[骨架可视化] 已画出 {runtimeObjects.Count - 1} 个元素" +
                  $"（骨骼 {bones.Length} 根，{(hideHelperBones ? "已隐藏辅助骨骼" : "显示全部")}）");
    }

    private void ClearRuntimeVisuals()
    {
        foreach (var go in runtimeObjects)
            if (go != null) Destroy(go);
        runtimeObjects.Clear();
    }

    private void EnsureMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        if (lineMat == null)
        {
            lineMat = new Material(shader);
            lineMat.color = boneColor;
            if (lineMat.HasProperty("_BaseColor")) lineMat.SetColor("_BaseColor", boneColor);
        }
        if (jointMat == null)
        {
            jointMat = new Material(shader);
            jointMat.color = jointColor;
            if (jointMat.HasProperty("_BaseColor")) jointMat.SetColor("_BaseColor", jointColor);
        }
    }

    private static void StripAndColor(GameObject go, Material mat)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);

        var r = go.GetComponent<MeshRenderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private bool IsHelper(string name)
    {
        string n = name.ToLower();
        foreach (var k in helperKeywords.Split(','))
        {
            var key = k.Trim();
            if (key.Length > 0 && n.Contains(key)) return true;
        }
        return false;
    }
}

/// <summary>让一个物体跟随骨骼位置</summary>
public class FollowBone : MonoBehaviour
{
    private Transform target;
    private Vector3 offset;

    public void Init(Transform bone, Vector3 off)
    {
        target = bone;
        offset = off;
    }

    private void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        transform.position = target.position + offset;
    }
}

/// <summary>让一条线连接父子两根骨骼</summary>
public class FollowBonePair : MonoBehaviour
{
    private Transform a, b;
    private LineRenderer lr;

    public void Init(Transform parent, Transform child)
    {
        a = parent; b = child;
        lr = GetComponent<LineRenderer>();
    }

    private void LateUpdate()
    {
        if (a == null || b == null || lr == null) { Destroy(gameObject); return; }
        lr.SetPosition(0, a.position);
        lr.SetPosition(1, b.position);
    }
}
