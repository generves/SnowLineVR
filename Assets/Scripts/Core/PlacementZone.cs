using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 放置区 —— 玩家把指定的道具放到这个范围里就算完成。
///
/// 典型用法（左腿受伤关卡第 1 步）：
///   玩家从医疗箱里拿出【警示标志】→ 地上出现蓝框 → 把标志放进蓝框 → 第 1 步完成
///
/// 特性：
///   · 玩家手上拿着正确道具时才显示蓝框（拿着别的、或者空手都不显示）
///   · 道具松手后如果落在范围内 → 算完成，上报给 StepController
///   · 完成后蓝框自动消失
///
/// 设置要求：
///   · 本物体需要一个 Collider，并勾选【Is Trigger】
///   · 尺寸就是允许放置的范围（用 BoxCollider 的 Size 调）
///
/// 用法：
///   ① 建一个空物体，命名 PlacementZone_WarningSign
///   ② Add Component → Box Collider → 勾 Is Trigger → Size 调成 (1.5, 0.5, 1.5)
///   ③ Add Component → 搜 Placement Zone → 添加
///   ④ 填：
///        Required Item Tag = WarningSign
///        Action Id         = PlaceWarningSign
///        Show Blue Frame   = ☑
///   ⑤ 摆到雪道上（来车方向的上方 5~10 米处）
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlacementZone : MonoBehaviour
{
    [Header("判定")]
    [Tooltip("要放进来的是哪个道具（对应 ItemTag.tagName）。留空 = 任何道具都算")]
    public string requiredItemTag = "WarningSign";

    [Tooltip("放好后上报给 StepController 的动作 ID")]
    public string actionId = "PlaceWarningSign";

    [Tooltip("只能完成一次")]
    public bool once = true;

    [Header("显示时机")]
    [Tooltip("玩家手上拿着【指定道具】时才显示蓝框。\n" +
             "取消 = 一直显示")]
    public bool onlyWhenHoldingItem = true;

    [Tooltip("【调试】勾上后蓝框一直显示（不受手上道具影响，也不受完成状态影响）。\n" +
             "想在编辑器里看到蓝框：勾上它，然后点组件右上角 ⋮ →「重建蓝框（编辑器）」")]
    public bool debugAlwaysShowFrame = false;

    [Header("外观")]
    [Tooltip("是否自动生成地面高亮")]
    public bool showBlueFrame = true;

    [Tooltip("地面高亮的颜色")]
    public Color frameColor = new Color(0.25f, 0.65f, 1f, 0.85f);

    [Tooltip("地面高亮铺在离地面多高处（米）。稍微抬高一点避免和雪面 z-fighting")]
    public float floorYOffset = 0.01f;

    [Tooltip("是否沿高亮边缘再画一圈细边（让范围更清楚）")]
    public bool showEdge = true;

    [Tooltip("细边粗细（米）")]
    [Range(0.01f, 0.2f)]
    public float frameThickness = 0.05f;

    [Header("闪烁")]
    [Tooltip("是否闪烁（呼吸效果）")]
    public bool blink = true;

    [Tooltip("闪烁频率（每秒几个来回）")]
    [Range(0.1f, 5f)]
    public float blinkSpeed = 1.2f;

    [Tooltip("闪烁时最暗 / 最亮的不透明度")]
    [Range(0f, 1f)]
    public float blinkAlphaMin = 0.25f;
    [Range(0f, 1f)]
    public float blinkAlphaMax = 0.9f;

    [Tooltip("地面高亮用的 shader。\n" +
             "SnowLineVR/Transparent  = 不穿透（会被雪堆、箱子挡住）\n" +
             "SnowLineVR/XRayCapsule  = 穿透（任何角度都能看到）")]
    public string shaderName = "SnowLineVR/Transparent";

    [Header("反馈")]
    [Tooltip("放对了的提示文字")]
    public string successMessage = "警示标志已放置";

    [Tooltip("放进来但道具不对的提示")]
    public string wrongItemMessage = "这里需要放警示标志";

    [Header("调试")]
    public bool logToConsole = false;

    // ---------- 内部 ----------
    private Collider zoneCollider;
    private readonly List<ItemPhysicsSetup> inside = new List<ItemPhysicsSetup>();

    private GameObject frameRoot;
    private Material frameMat;
    private Material floorMat;
    private bool completed;
    private bool visible;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        var c = GetZoneCollider();
        if (c == null)
        {
            Debug.LogWarning($"[放置区] {name} 上没有 Collider，无法判定放置。请加一个 Box Collider 并勾 Is Trigger");
        }
        else
        {
            c.isTrigger = true;
        }

        // 触发器要可靠地收到事件，两边至少有一方要有 Rigidbody。
        // 被放下的道具自己有 Rigidbody，但为了保险（也为了以后放别的物体进来），
        // 这里补一个运动学 Rigidbody。
        if (GetComponent<Rigidbody>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        BuildFrame();

        // 调试开关打开时，一进游戏就显示
        SetFrameVisible(debugAlwaysShowFrame);
        visible = debugAlwaysShowFrame;
    }

    private void OnDestroy()
    {
        DestroyMaterials();
    }

    /// <summary>清掉运行时创建的材质（编辑器模式要用 DestroyImmediate）</summary>
    private void DestroyMaterials()
    {
        DestroyObj(frameMat);
        frameMat = null;

        DestroyObj(floorMat);
        floorMat = null;
    }

    /// <summary>按运行/编辑模式选择正确的删除方式</summary>
    private static void DestroyObj(Object o)
    {
        if (o == null) return;

        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    private void OnEnable()
    {
        // ⚠️ 不能只在 OnEnable 订阅一次：
        //    箱子里的道具（警示标志）一开始是【未激活】的，
        //    FindObjectsByType 默认不包含未激活物体 → 订阅不上
        //    → 松手时 OnDropped 事件没人接 → 第 1 步完不成。
        //    所以改成每帧补订阅（见 Update）。
        RefreshSubscriptions();
    }

    private void OnDisable()
    {
        foreach (var item in subscribedItems)
            if (item != null) item.OnDropped -= HandleItemDropped;
        subscribedItems.Clear();

        inside.Clear();
    }

    // ---------- 已订阅的道具（边跑边补）----------
    private readonly List<ItemPhysicsSetup> subscribedItems = new List<ItemPhysicsSetup>();
    private float rescanTimer;
    private const float RescanInterval = 0.5f;

    /// <summary>扫描场景里所有道具（含未激活的），把还没订阅的补上</summary>
    private void RefreshSubscriptions()
    {
        foreach (var item in FindObjectsByType<ItemPhysicsSetup>(FindObjectsInactive.Include))
        {
            if (item == null) continue;
            if (subscribedItems.Contains(item)) continue;

            item.OnDropped += HandleItemDropped;
            subscribedItems.Add(item);

            if (logToConsole)
                Debug.Log($"[放置区] {name} 订阅道具「{item.ItemDisplayName}」");
        }
    }

    private void Update()
    {
        // 定时补订阅（箱子里的道具可能在选关之后才激活）
        rescanTimer += Time.deltaTime;
        if (rescanTimer >= RescanInterval)
        {
            rescanTimer = 0f;
            RefreshSubscriptions();
        }

        // 是否该显示地面高亮
        bool shouldShow = debugAlwaysShowFrame
                          || (!completed
                              && (!onlyWhenHoldingItem || PlayerHoldsRequiredItem()));

        if (shouldShow != visible)
        {
            visible = shouldShow;
            SetFrameVisible(visible);
        }

        // 闪烁
        if (visible && blink) UpdateBlink();
    }

    /// <summary>让地面高亮的透明度做呼吸式变化</summary>
    private void UpdateBlink()
    {
        // sin 在 [-1,1]，映射到 [min,max]
        float k = (Mathf.Sin(Time.time * blinkSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        float a = Mathf.Lerp(blinkAlphaMin, blinkAlphaMax, k);

        var c = frameColor;
        c.a = a;

        if (floorMat != null) SetColor(floorMat, c);

        // 细边比地面稍微不透明一点，轮廓更清楚
        if (frameMat != null)
        {
            var e = frameColor;
            e.a = Mathf.Min(1f, a + 0.1f);
            SetColor(frameMat, e);
        }
    }

    /// <summary>取碰撞体（Awake 之前调用也能拿到，编辑器里重建蓝框要用）</summary>
    private Collider GetZoneCollider()
    {
        if (zoneCollider == null) zoneCollider = GetComponent<Collider>();
        return zoneCollider;
    }

    /// <summary>编辑器里自动把碰撞体设成触发器</summary>
    private void OnValidate()
    {
        var c = GetZoneCollider();
        if (c != null) c.isTrigger = true;
    }

    private bool PlayerHoldsRequiredItem()
    {
        if (string.IsNullOrEmpty(requiredItemTag)) return true;   // 不限道具 → 一直显示

        foreach (var pi in PropInteractor.Active)
        {
            if (pi == null) continue;
            var item = pi.HeldItem;
            if (item == null) continue;
            if (item.ItemId == requiredItemTag) return true;
        }
        return false;
    }

    // ==================== 进出范围 ====================
    private void OnTriggerEnter(Collider other)
    {
        var item = other.GetComponentInParent<ItemPhysicsSetup>();
        if (item == null) return;

        if (!inside.Contains(item))
        {
            inside.Add(item);
            if (logToConsole) Debug.Log($"[放置区] 「{item.ItemDisplayName}」进入范围");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var item = other.GetComponentInParent<ItemPhysicsSetup>();
        if (item == null) return;

        if (inside.Remove(item))
        {
            if (logToConsole) Debug.Log($"[放置区] 「{item.ItemDisplayName}」离开范围");
        }
    }

    /// <summary>
    /// 用【几何重叠】直接判断这件道具现在是不是在范围内。
    ///
    /// 为什么不能只信 OnTriggerEnter：
    ///   道具松手的那一帧，物理和触发器回调的顺序不固定，
    ///   inside 列表可能还没更新 → 明明放进去了却判定失败。
    ///   所以松手时再做一次精确的几何检测兜底。
    /// </summary>
    private bool IsInsideNow(ItemPhysicsSetup item)
    {
        var c = GetZoneCollider();
        if (c == null) return false;

        // 取道具所有碰撞体的世界包围盒并集
        var itemCols = item.GetComponentsInChildren<Collider>(true);
        if (itemCols == null || itemCols.Length == 0) return false;

        Bounds itemBounds = itemCols[0].bounds;
        for (int i = 1; i < itemCols.Length; i++)
        {
            if (itemCols[i] != null) itemBounds.Encapsulate(itemCols[i].bounds);
        }

        if (c is BoxCollider bc)
        {
            // 把道具包围盒中心转换到区域的本地空间，再比 BoxCollider.size
            Vector3 localCenter = transform.InverseTransformPoint(itemBounds.center);

            Vector3 half = bc.size * 0.5f;
            // Y 方向放宽：道具可能落在区域顶部附近
            half.y += 0.35f;

            Vector3 d = localCenter - bc.center;

            return Mathf.Abs(d.x) <= half.x
                && Mathf.Abs(d.y) <= half.y
                && Mathf.Abs(d.z) <= half.z;
        }

        // 其他碰撞体类型：用包围盒相交粗略判断
        return c.bounds.Intersects(itemBounds);
    }

    // ==================== 松手判定 ====================
    private void HandleItemDropped(ItemPhysicsSetup item)
    {
        if (item == null) return;
        if (completed && once) return;

        // 必须在范围内：触发器列表 或 几何重叠，两者满足一个即可
        bool inTriggerList = inside.Contains(item);
        bool inGeometry = IsInsideNow(item);
        bool inZone = inTriggerList || inGeometry;

        if (logToConsole)
            Debug.Log($"[放置区] {name}：检测到「{item.ItemDisplayName}」放下" +
                      $"，触发器列表={inTriggerList}  几何重叠={inGeometry}");

        if (!inZone) return;

        // 道具对不对
        if (!string.IsNullOrEmpty(requiredItemTag) && item.ItemId != requiredItemTag)
        {
            ShowMessage(wrongItemMessage, true);
            if (logToConsole)
                Debug.Log($"[放置区] {name}：放进的是「{item.ItemDisplayName}」，需要「{requiredItemTag}」");
            return;
        }

        // ---------- 成功 ----------
        completed = true;
        SetFrameVisible(false);
        visible = false;

        ShowMessage(successMessage, false);

        if (logToConsole)
            Debug.Log($"[放置区] {name}：已放入「{item.ItemDisplayName}」，上报 '{actionId}'");

        // 上报给步骤系统
        var sc = FindAnyObjectByType<StepController>();
        if (sc != null && !string.IsNullOrEmpty(actionId))
            sc.ReportAction(actionId);
        else if (sc == null)
            Debug.LogWarning("[放置区] 场景里找不到 StepController，无法推进步骤");
    }

    private static void ShowMessage(string msg, bool warning)
    {
        if (string.IsNullOrEmpty(msg)) return;

        var hint = FindAnyObjectByType<HintPanel>();
        if (hint == null) return;

        if (warning) hint.ShowWarning(msg);
        else hint.ShowSuccess(msg);
    }

    // ==================== 地面高亮 ====================
    /// <summary>
    /// 只生成【贴在雪面上的一块蓝色高亮】+（可选）一圈细边。
    /// 不生成竖直的边和立柱 —— 那些在 VR 里会挡住视线。
    /// </summary>
    private void BuildFrame()
    {
        if (!showBlueFrame) return;

        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            // 退回穿透版，至少能看见
            shader = Shader.Find("SnowLineVR/XRayCapsule");
            if (shader != null)
                Debug.LogWarning($"[放置区] 找不到 shader '{shaderName}'，改用 SnowLineVR/XRayCapsule（会穿透）");
        }
        if (shader == null)
        {
            Debug.LogWarning("[放置区] 找不到可用 shader，地面高亮不会显示");
            return;
        }

        frameRoot = new GameObject("__ZoneFrame");
        frameRoot.transform.SetParent(transform, false);

        // 碰撞体的本地尺寸；地面高亮铺在这个盒子的【底面】
        Vector3 size = GetLocalSize();

        // 盒子的底面在本地 y = -size.y * 0.5 处（因为 BoxCollider.center 是盒心）
        float bottomY = GetBottomLocalY(size) + floorYOffset;

        var c = frameColor;

        // ---------- 地面高亮块 ----------
        floorMat = new Material(shader) { name = "PlacementZone_Floor" };
        floorMat.renderQueue = 4490;
        SetColor(floorMat, c);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "floor";
        DestroyCollider(floor);
        floor.transform.SetParent(frameRoot.transform, false);
        floor.transform.localPosition = new Vector3(0f, bottomY, 0f);
        // 很薄的一层，视觉上就是地面上的一块色块
        floor.transform.localScale = new Vector3(size.x, 0.004f, size.z);
        ApplyMat(floor, floorMat);

        // ---------- 边缘细边（可选）----------
        if (showEdge)
        {
            frameMat = new Material(shader) { name = "PlacementZone_Edge" };
            frameMat.renderQueue = 4500;
            SetColor(frameMat, c);

            float t = frameThickness;
            float ex = size.x * 0.5f;
            float ez = size.z * 0.5f;

            MakeBar("edgeFront", new Vector3(0f, bottomY, ez), new Vector3(size.x, t, t));
            MakeBar("edgeBack", new Vector3(0f, bottomY, -ez), new Vector3(size.x, t, t));
            MakeBar("edgeRight", new Vector3(ex, bottomY, 0f), new Vector3(t, t, size.z));
            MakeBar("edgeLeft", new Vector3(-ex, bottomY, 0f), new Vector3(t, t, size.z));
        }
    }

    /// <summary>取碰撞体底面的本地 Y 坐标</summary>
    private float GetBottomLocalY(Vector3 size)
    {
        var c = GetZoneCollider();
        if (c is BoxCollider bc) return bc.center.y - bc.size.y * 0.5f;

        // 其他类型：用世界包围盒换算到本地
        if (c != null)
        {
            Vector3 s = transform.lossyScale;
            float localH = Mathf.Abs(s.y) > 0.0001f ? c.bounds.size.y / Mathf.Abs(s.y) : c.bounds.size.y;
            return -localH * 0.5f;
        }

        return -size.y * 0.5f;
    }

    private void MakeBar(string nm, Vector3 localPos, Vector3 localScale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = nm;
        DestroyCollider(go);
        go.transform.SetParent(frameRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        ApplyMat(go, frameMat);
    }

    /// <summary>
    /// 删掉自动生成的立方体自带的碰撞体。
    /// ⚠️ 编辑器模式下必须用 DestroyImmediate —— 用 Destroy 会报
    ///    "Destroy may not be called from edit mode"。
    /// </summary>
    private static void DestroyCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c == null) return;

        if (Application.isPlaying) Destroy(c);
        else DestroyImmediate(c);
    }

    private static void ApplyMat(GameObject go, Material mat)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.allowOcclusionWhenDynamic = false;
    }

    /// <summary>取碰撞体的本地尺寸（默认 1 米立方体时返回 1）</summary>
    private Vector3 GetLocalSize()
    {
        var c = GetZoneCollider();

        if (c is BoxCollider bc) return bc.size;

        if (c == null)
        {
            // 编辑器里还没加碰撞体 → 给一个默认尺寸，别崩
            return Vector3.one;
        }

        // 其他类型：用世界包围盒换算
        Vector3 s = transform.lossyScale;
        Vector3 ws = c.bounds.size;
        return new Vector3(
            Mathf.Abs(s.x) > 0.0001f ? ws.x / Mathf.Abs(s.x) : ws.x,
            Mathf.Abs(s.y) > 0.0001f ? ws.y / Mathf.Abs(s.y) : ws.y,
            Mathf.Abs(s.z) > 0.0001f ? ws.z / Mathf.Abs(s.z) : ws.z);
    }

    /// <summary>
    /// 编辑器里重建蓝框，方便摆位置。
    /// 用法：在 Inspector 里点组件右上角的 ⋮ → 「重建蓝框（编辑器）」，
    ///       或者右键组件标题。
    /// 重建时会按【Debug Always Show Frame】决定显不显示。
    /// </summary>
    [ContextMenu("重建蓝框（编辑器）")]
    public void RebuildFrameInEditor()
    {
        // 清掉旧的（编辑器模式必须用 DestroyImmediate）
        if (frameRoot != null)
        {
            DestroyObj(frameRoot);
            frameRoot = null;
        }

        // 旧的材质也一起清掉，避免反复重建时泄漏
        DestroyMaterials();

        BuildFrame();
        SetFrameVisible(debugAlwaysShowFrame);
        visible = debugAlwaysShowFrame;
    }

    private void SetFrameVisible(bool v)
    {
        if (frameRoot != null && frameRoot.activeSelf != v)
            frameRoot.SetActive(v);
    }

    private static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
    }

    /// <summary>供外部重置（重新训练时用）</summary>
    public void ResetZone()
    {
        completed = false;
        visible = false;
        inside.Clear();
        SetFrameVisible(false);
    }
}
