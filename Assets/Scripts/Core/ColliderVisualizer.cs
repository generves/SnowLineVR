using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 碰撞体可视化（线框版）—— 把碰撞体的实际形状画成【穿透一切的线框盒子】。
///
/// 特点：
///   · 用 12 根细边拼成盒子边框，中间是空的，多个碰撞体叠在一起也看得清
///   · 穿透所有贴图和模型，Game 视图和 Scene 视图都能看到
///   · 支持多个碰撞体，碰撞体在子物体上也能找到
///   · 形状按碰撞体本地 center/size 换算，不随物体旋转变形
///
/// 用法：
///   ① 挂在物体上
///   ② 勾上【Show】
/// </summary>
public class ColliderVisualizer : MonoBehaviour
{
    [Header("显示")]
    [Tooltip("勾上才显示")]
    public bool show = true;

    [Tooltip("线框颜色")]
    public Color color = new Color(1f, 0f, 0.8f, 1f);

    [Tooltip("线框粗细（世界单位，米）")]
    [Range(0.002f, 0.1f)]
    public float lineThickness = 0.012f;

    [Tooltip("是否另外画一个绿色小方块标出中心")]
    public bool showCenter = true;

    [Tooltip("是否在 Console 打印找到的碰撞体")]
    public bool logToConsole = false;

    [Header("目标")]
    [Tooltip("留空 = 自动找本物体及所有子物体上的碰撞体")]
    public List<Collider> targetColliders = new List<Collider>();

    // ---------- 内部：每个碰撞体对应一个"框" ----------
    private class Frame
    {
        public Collider col;
        public GameObject root;          // 框的父物体
        public GameObject[] edges;       // 12 根边
        public GameObject centerMarker;
    }

    private readonly List<Frame> frames = new List<Frame>();
    private Material lineMat;
    private Material centerMat;
    private float checkTimer = 0f;
    private const float CheckInterval = 0.5f;

    // 12 根边的方向：[是否沿 X][是否沿 Y][是否沿 Z] 的 8 个角 + 12 条连线
    // 每条边用 (起点, 终点) 的 +-1 角点索引描述
    private static readonly int[,] EdgeCorners = new int[,]
    {
        // 沿 X 的 4 条
        {0,1},{2,3},{4,5},{6,7},
        // 沿 Y 的 4 条
        {0,2},{1,3},{4,6},{5,7},
        // 沿 Z 的 4 条
        {0,4},{1,5},{2,6},{3,7},
    };

    // 8 个角点的符号（立方体的 8 个顶点）
    private static readonly Vector3[] Corners = new Vector3[]
    {
        new Vector3(-0.5f,-0.5f,-0.5f), // 0
        new Vector3( 0.5f,-0.5f,-0.5f), // 1
        new Vector3(-0.5f, 0.5f,-0.5f), // 2
        new Vector3( 0.5f, 0.5f,-0.5f), // 3
        new Vector3(-0.5f,-0.5f, 0.5f), // 4
        new Vector3( 0.5f,-0.5f, 0.5f), // 5
        new Vector3(-0.5f, 0.5f, 0.5f), // 6
        new Vector3( 0.5f, 0.5f, 0.5f), // 7
    };

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void OnEnable()
    {
        EnsureMaterials();
        Rebuild();
    }

    private void OnDisable() => DestroyAll();
    private void OnDestroy()
    {
        DestroyAll();
        if (lineMat != null) Destroy(lineMat);
        if (centerMat != null) Destroy(centerMat);
    }

    private void Update()
    {
        if (!show)
        {
            if (frames.Count > 0) DestroyAll();
            return;
        }

        // 碰撞体是运行时生成的 → 定期检查
        checkTimer += Time.deltaTime;
        if (checkTimer >= CheckInterval)
        {
            checkTimer = 0f;
            var found = FindColliders();
            if (found.Count != frames.Count) Rebuild();
        }

        SyncAll();
    }

    // ==================== 材质 ====================
    private void EnsureMaterials()
    {
        var shader = Shader.Find("SnowLineVR/XRayCapsule");
        if (shader == null)
        {
            Debug.LogWarning("[碰撞体可视化] 找不到 shader SnowLineVR/XRayCapsule");
            return;
        }

        if (lineMat == null) lineMat = new Material(shader) { name = "ColliderViz_Line" };
        if (centerMat == null) centerMat = new Material(shader) { name = "ColliderViz_Center" };

        SetColor(lineMat, color);
        SetColor(centerMat, new Color(0f, 1f, 0.2f, 1f));
        lineMat.renderQueue = 4500;
        centerMat.renderQueue = 4510;
    }

    // ==================== 找碰撞体 ====================
    private List<Collider> FindColliders()
    {
        var list = new List<Collider>();

        foreach (var c in targetColliders)
            if (c != null) list.Add(c);

        if (list.Count > 0) return list;

        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            if (c == null) continue;
            if (c.gameObject.name.StartsWith("__ColliderViz")) continue;
            list.Add(c);
        }

        return list;
    }

    // ==================== 建线框 ====================
    private void Rebuild()
    {
        if (!show) return;

        EnsureMaterials();
        if (lineMat == null) return;

        DestroyAll();

        var colliders = FindColliders();
        if (colliders.Count == 0)
        {
            if (logToConsole)
                Debug.Log($"[碰撞体可视化] {name} 暂时找不到 Collider（可能是运行时生成），稍后重试");
            return;
        }

        foreach (var c in colliders)
        {
            var f = new Frame { col = c };

            // 框的根物体
            f.root = new GameObject($"__ColliderViz_{c.gameObject.name}");
            // 不设父物体：独立在世界空间，避免受缩放链影响
            f.root.transform.position = c.bounds.center;
            f.root.transform.rotation = Quaternion.identity;

            // 12 根边
            f.edges = new GameObject[12];
            for (int i = 0; i < 12; i++)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
                e.name = $"edge{i}";
                var auto = e.GetComponent<Collider>();
                if (auto != null) Destroy(auto);

                var mr = e.GetComponent<MeshRenderer>();
                mr.sharedMaterial = lineMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.allowOcclusionWhenDynamic = false;

                e.transform.SetParent(f.root.transform, false);
                f.edges[i] = e;
            }

            // 中心标记
            if (showCenter)
            {
                var cm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cm.name = "center";
                var ac = cm.GetComponent<Collider>();
                if (ac != null) Destroy(ac);

                var cmr = cm.GetComponent<MeshRenderer>();
                cmr.sharedMaterial = centerMat;
                cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cmr.receiveShadows = false;
                cmr.allowOcclusionWhenDynamic = false;

                cm.transform.SetParent(f.root.transform, false);
                f.centerMarker = cm;
            }

            frames.Add(f);

            if (logToConsole)
            {
                var b = c.bounds;
                Debug.Log($"[碰撞体可视化] {c.GetType().Name} @ {c.gameObject.name}\n" +
                          $"    世界包围盒 = {b.min} ~ {b.max}");
            }
        }
    }

    // ==================== 每帧同步位置尺寸 ====================
    private void SyncAll()
    {
        float t = Mathf.Max(0.001f, lineThickness);

        foreach (var f in frames)
        {
            if (f.root == null || f.col == null) continue;

            // 求碰撞体的【世界中心】和【世界尺寸】
            if (!GetWorldBox(f.col, out Vector3 center, out Vector3 size, out Quaternion rot))
                continue;

            f.root.transform.position = center;
            f.root.transform.rotation = rot;

            // 12 根边：按角点位置摆放
            for (int i = 0; i < 12; i++)
            {
                var edge = f.edges[i];
                if (edge == null) continue;

                Vector3 a = Vector3.Scale(Corners[EdgeCorners[i, 0]], size);
                Vector3 b = Vector3.Scale(Corners[EdgeCorners[i, 1]], size);

                edge.transform.localPosition = (a + b) * 0.5f;

                Vector3 d = b - a;
                float len = d.magnitude;
                // 判断这条边沿哪个轴：比较三个分量的绝对值
                Vector3 abs = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
                if (abs.x >= abs.y && abs.x >= abs.z)
                    edge.transform.localScale = new Vector3(len, t, t);
                else if (abs.y >= abs.z)
                    edge.transform.localScale = new Vector3(t, len, t);
                else
                    edge.transform.localScale = new Vector3(t, t, len);

                edge.transform.localRotation = Quaternion.identity;
            }

            // 中心标记
            if (f.centerMarker != null)
            {
                float s = Mathf.Max(0.01f, Mathf.Min(size.x, size.y, size.z) * 0.2f);
                f.centerMarker.transform.localPosition = Vector3.zero;
                f.centerMarker.transform.localRotation = Quaternion.identity;
                f.centerMarker.transform.localScale = Vector3.one * s;
            }
        }
    }

    /// <summary>求碰撞体的世界中心、世界尺寸、以及应该用的旋转</summary>
    private bool GetWorldBox(Collider c, out Vector3 center, out Vector3 size, out Quaternion rot)
    {
        center = Vector3.zero;
        size = Vector3.one;
        rot = Quaternion.identity;

        if (c is BoxCollider bc)
        {
            Transform t = bc.transform;
            Vector3 s = t.lossyScale;
            Vector3 localSize = bc.size;

            center = t.TransformPoint(bc.center);
            size = new Vector3(
                Mathf.Abs(localSize.x * s.x),
                Mathf.Abs(localSize.y * s.y),
                Mathf.Abs(localSize.z * s.z));
            rot = t.rotation;
            return true;
        }

        // 其他类型（MeshCollider 等）：用世界 AABB 近似
        var b = c.bounds;
        center = b.center;
        size = b.size;
        rot = Quaternion.identity;
        return true;
    }

    private void DestroyAll()
    {
        foreach (var f in frames)
            if (f.root != null) Destroy(f.root);
        frames.Clear();
    }

    private static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
    }

    public void SetVisible(bool v)
    {
        show = v;
        if (v) Rebuild(); else DestroyAll();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        EnsureMaterials();
    }
}
