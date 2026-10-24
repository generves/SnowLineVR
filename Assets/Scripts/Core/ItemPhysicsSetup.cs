using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Collections.Generic;

/// <summary>
/// 道具物理与抓取 —— 让物品能被抓起、放下受重力、碰撞弹开。
///
/// 挂上这个组件后，它会自动配置好一整套物理 + 抓取设置，
/// 你只需要在场景里给物品摆好位置即可。
///
/// 自动做的事：
///   · 补齐 Rigidbody（质量、阻力、碰撞检测）
///   · 补齐 Collider（没有就按渲染包围盒加一个 BoxCollider）
///   · 补齐 XRGrabInteractable（抓取方式、投掷、双手抓取）
///   · 生成弹性物理材质（碰撞会弹开）
///
/// 抓取/放下时还会自动切换物理状态：
///   · 拿起  → 关闭重力、变成运动学（跟着手走，不会乱晃）
///   · 放下  → 恢复重力、恢复物理（掉落、碰撞、弹开）
///
/// 用法：
///   ① 在场景里选中道具物体
///   ② Add Component → 搜 Item Physics Setup → 添加
///   ③ 再 Add Component → 搜 Item Tag → 填上道具标识
///
/// 注意：不需要手动加 Collider 或 Rigidbody，
///       本组件会在 Awake 时自动补齐（没有碰撞体就按渲染包围盒生成一个）。
/// </summary>
public class ItemPhysicsSetup : MonoBehaviour
{
    [Header("抓取")]
    [Tooltip("抓取时物品相对手柄的偏移，通常不用改")]
    public Vector3 attachOffset = Vector3.zero;

    [Tooltip("是否允许投掷（松手后按挥动速度飞出去）")]
    public bool throwOnDetach = true;

    [Tooltip("投掷速度倍率")]
    public float throwVelocityScale = 1.5f;

    [Tooltip("跟随手柄的平滑程度。0 = 硬跟随（最跟手），越大越软")]
    [Range(0f, 1f)]
    public float positionDamping = 0.2f;

    [Tooltip("跟随手柄旋转的平滑程度。0 = 硬跟随，越大越软")]
    [Range(0f, 1f)]
    public float rotationDamping = 0.4f;

    [Header("物理")]
    [Tooltip("质量（公斤）。医疗道具 0.3~3 之间比较真实")]
    public float mass = 0.5f;

    [Tooltip("弹性。0 = 不弹，1 = 完全弹回")]
    [Range(0f, 1f)]
    public float bounciness = 0.35f;

    [Tooltip("摩擦。越小越滑")]
    [Range(0f, 1f)]
    public float friction = 0.6f;

    [Tooltip("空气阻力")]
    public float linearDamping = 0.1f;

    [Tooltip("旋转阻力")]
    public float angularDamping = 0.5f;

    [Header("使用判定")]
    [Tooltip("松手时如果正好在某个身体部位上，就算『使用』这个道具")]
    public bool useOnReleaseOverBodyPart = true;

    [Tooltip("按交互键显示物品介绍时，提示面板显示多久")]
    public float infoDisplayDuration = 5f;

    [Header("碰撞体")]
    [Tooltip("碰撞体类型：\n" +
             "· BoxFromBounds  用包围盒生成一个方块（简单，但只能近似）\n" +
             "· MeshConvex     用网格生成凸包（形状完全贴合，推荐）")]
    public ColliderMode colliderMode = ColliderMode.MeshConvex;

    [Tooltip("用哪个物体来生成碰撞体。" +
             "MeshConvex 模式填【要贴合的那个网格物体】；" +
             "多部件物体可以只填主体")]
    public Transform colliderSource;

    [Tooltip("MeshConvex 模式下，是否给【colliderSource 的所有子物体】也各加一个 MeshCollider")]
    public bool meshIncludeChildren = true;

    [Tooltip("碰撞体比包围盒缩小一点的比例（只对 BoxFromBounds 有效）")]
    [Range(0.5f, 1.2f)]
    public float colliderShrink = 0.95f;

    [Tooltip("勾上后 BoxFromBounds 模式不再自动计算，完全用下面两个值")]
    public bool overrideCollider = false;

    [Tooltip("手动指定碰撞体中心（本地坐标）")]
    public Vector3 overrideCenter = Vector3.zero;

    [Tooltip("手动指定碰撞体尺寸（本地坐标）")]
    public Vector3 overrideSize = new Vector3(0.5f, 0.5f, 0.5f);

    [Tooltip("在 Console 打印碰撞体的世界范围（排查浮空/穿透用）")]
    public bool logColliderBounds = false;

    [Header("启动自检（修『部分道具无法交互』）")]
    [Tooltip("勾上后，启动时自检并【补注册】交互组件。\n" +
             "背景：XRInteractionManager 是把 Collider → Interactable 存成字典的，\n" +
             "      同一帧批量启用物体时，注册顺序不稳定，个别道具会注册失败，\n" +
             "      表现为『射线射上去没有高亮、按交互键选不中』。\n" +
             "      这个自检会在启动后重新注册一遍，保证每件道具都能交互。")]
    public bool repairInteractionOnStart = true;

    [Tooltip("自检失败时的重试间隔（秒）")]
    public float repairRetryInterval = 0.5f;

    [Tooltip("最多重试几次（0 = 一直重试到成功）")]
    public int repairMaxRetries = 0;

    /// <summary>碰撞体生成方式</summary>
    public enum ColliderMode
    {
        /// <summary>按包围盒生成一个 BoxCollider（近似）</summary>
        BoxFromBounds,

        /// <summary>按网格生成凸包 MeshCollider（贴合）</summary>
        MeshConvex,
    }

    [Header("悬停轮廓高亮")]
    [Tooltip("轮廓颜色")]
    public Color outlineColor = new Color(1f, 0.85f, 0.25f, 0.9f);

    [Tooltip("轮廓粗细（外扩比例）。0.08 = 向外扩 8%")]
    [Range(0.01f, 0.3f)]
    public float outlineWidth = 0.08f;

    [Tooltip("中间填充透明度。0 = 只留一圈边")]
    [Range(0f, 1f)]
    public float outlineFillAlpha = 0.12f;

    [Tooltip("专门用来描边的物体。留空会自动猜（多部件物体建议手动指定）")]
    public Transform outlineMeshRoot;

    // ---------- 内部 ----------
    private Rigidbody rb;
    private Collider col;
    private XRGrabInteractable grab;
    private ItemTag itemTag;

    private bool isGrabbed = false;

    /// <summary>
    /// 算出碰撞体应该用的世界包围盒。
    /// 优先用 colliderSource；否则用本物体及子物体所有渲染器合起来。
    /// </summary>
    private Bounds? ComputeBounds()
    {
        var root = colliderSource != null ? colliderSource : transform;
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        bool has = false;
        Bounds total = new Bounds();

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // 跳过我们自己建的高亮外壳
            if (r.gameObject.name.StartsWith("__Outline_")) continue;
            // 跳过禁用的
            if (!r.enabled) continue;

            if (!has) { total = r.bounds; has = true; }
            else total.Encapsulate(r.bounds);
        }

        return has ? total : (Bounds?)null;
    }

    /// <summary>统计参与包围盒计算的渲染器数量（调试用）</summary>
    private int CountRenderers()
    {
        var root = colliderSource != null ? colliderSource : transform;
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (r.gameObject.name.StartsWith("__Outline_")) continue;
            if (!r.enabled) continue;
            n++;
        }
        return n;
    }

    /// <summary>当前是否被玩家拿在手上</summary>
    public bool IsGrabbed => isGrabbed;

    /// <summary>玩家松手放下时触发（供放置区之类的系统判断"有没有放到指定位置"）</summary>
    public event System.Action<ItemPhysicsSetup> OnDropped;

    /// <summary>道具标识（来自 ItemTag 组件）</summary>
    public string ItemId => itemTag != null ? itemTag.tagName : "";

    /// <summary>道具显示名</summary>
    public string ItemDisplayName =>
        itemTag != null ? itemTag.DisplayName : gameObject.name;

    /// <summary>物品介绍文字（给"按交互键显示介绍"用）</summary>
    public string Description =>
        itemTag != null ? itemTag.displayName : "";

    private void Awake()
    {
        itemTag = GetComponent<ItemTag>();
        if (itemTag == null) itemTag = GetComponentInParent<ItemTag>();

        SetupPhysics();
        SetupGrabInteractable();
        SetupHoverHighlight();
    }

    private void Start()
    {
        if (repairInteractionOnStart) StartCoroutine(RepairInteractionRoutine());
    }

    /// <summary>
    /// 启动自检：确保碰撞体和交互组件都齐了，并且【已经注册到 XRInteractionManager】。
    ///
    /// 为什么需要：
    ///   这些道具的 Collider 和 XRGrabInteractable 都是运行时 AddComponent 加上的，
    ///   而 XRInteractionManager 靠一张 Collider → Interactable 的字典做快速查找。
    ///   同一帧批量启用物体（关卡根物体 SetActive(true)）时，
    ///   XRGrabInteractable.OnEnable 可能在碰撞体还没生成时就跑了，
    ///   于是注册到的 colliders 列表是空的 → 道具永远无法被射线选中。
    ///   注册顺序不稳定，所以每局失效的道具都不一样。
    /// </summary>
    private System.Collections.IEnumerator RepairInteractionRoutine()
    {
        int attempts = 0;

        while (true)
        {
            attempts++;

            // 等物体真正激活（关卡根物体是选关之后才激活的）
            if (!gameObject.activeInHierarchy)
            {
                yield return new WaitForSeconds(repairRetryInterval);
                if (repairMaxRetries > 0 && attempts >= repairMaxRetries) yield break;
                continue;
            }

            if (EnsureRegistered())
            {
                if (logColliderBounds)
                    Debug.Log($"[交互自检] 「{name}」已确认可交互（第 {attempts} 次检查）");
                yield break;
            }

            if (logColliderBounds)
                Debug.LogWarning($"[交互自检] 「{name}」第 {attempts} 次检查未通过，{repairRetryInterval} 秒后重试");

            yield return new WaitForSeconds(repairRetryInterval);
            if (repairMaxRetries > 0 && attempts >= repairMaxRetries)
            {
                Debug.LogWarning($"[交互自检] 「{name}」重试 {attempts} 次仍未通过，放弃");
                yield break;
            }
        }
    }

    /// <summary>
    /// 检查并补齐：Collider、Rigidbody、XRGrabInteractable、XRInteractionManager 注册。
    /// 返回 true 表示这件道具现在可以正常交互。
    /// </summary>
    private bool EnsureRegistered()
    {
        // ---------- 1. 碰撞体 ----------
        if (col == null) col = GetComponent<Collider>();
        if (col == null) col = GetComponentInChildren<Collider>();

        if (col == null)
        {
            // 碰撞体没生成成功（Awake 时物体未激活，或网格不可读）
            col = colliderMode == ColliderMode.MeshConvex
                ? CreateMeshColliders()
                : CreateBoxFromBounds();

            if (col != null) col.isTrigger = false;

            if (col == null)
            {
                Debug.LogWarning($"[交互自检] 「{name}」无法生成碰撞体，这件道具将无法交互");
                return false;
            }
        }

        // ---------- 2. 刚体 ----------
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.01f, mass);
            rb.linearDamping = linearDamping;
            rb.angularDamping = angularDamping;
        }

        // ---------- 3. 交互组件 ----------
        if (grab == null) grab = GetComponent<XRGrabInteractable>();
        if (grab == null)
        {
            grab = gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.trackPosition = true;
            grab.trackRotation = true;
            grab.selectMode = InteractableSelectMode.Multiple;
            grab.throwOnDetach = throwOnDetach;
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        // ---------- 4. 确认交互组件认识这个碰撞体 ----------
        if (grab.colliders == null || grab.colliders.Count == 0)
        {
            grab.colliders.Add(col);

            if (logColliderBounds)
                Debug.Log($"[交互自检] 「{name}」的 XRGrabInteractable.colliders 之前是空的，已补上 {col.GetType().Name}");
        }

        // ---------- 5. 确认已经注册到 XRInteractionManager ----------
        var manager = grab.interactionManager;
        if (manager == null) manager = FindAnyObjectByType<XRInteractionManager>();

        if (manager == null)
        {
            if (logColliderBounds)
                Debug.LogWarning($"[交互自检] 「{name}」找不到 XRInteractionManager");
            return false;
        }

        // 重新注册一遍：先注销再注册，避免"注册过但 colliders 是空的"这种情况。
        // ⚠️ 必须显式转成 IXRInteractable —— XRInteractionManager 上有
        //    已过时的 XRBaseInteractable 重载，不转换会命中它并报 CS0619。
        var ixr = (IXRInteractable)grab;
        manager.UnregisterInteractable(ixr);
        manager.RegisterInteractable(ixr);

        return true;
    }

    /// <summary>自动加上 X 光描边高亮，并把这里配的参数传过去</summary>
    private void SetupHoverHighlight()
    {
        var hl = GetComponent<XRayHoverHighlight>();
        if (hl == null) hl = gameObject.AddComponent<XRayHoverHighlight>();

        // 把 Inspector 里配的参数同步给高亮组件
        hl.outlineColor = outlineColor;
        hl.outlineWidth = outlineWidth;
        hl.fillAlpha = outlineFillAlpha;
        if (outlineMeshRoot != null) hl.meshRoot = outlineMeshRoot;
    }

    // ==================== 物理配置 ====================
    private void SetupPhysics()
    {
        // ---------- Rigidbody ----------
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = Mathf.Max(0.01f, mass);
        rb.linearDamping = linearDamping;
        rb.angularDamping = angularDamping;
        rb.useGravity = true;
        rb.isKinematic = false;

        // 连续碰撞检测：防止快速挥动时穿透桌面
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // 不要让物理把道具推来推去导致抖动
        rb.maxAngularVelocity = 20f;

        // ---------- Collider ----------
        // 先检查有没有现成的碰撞体（包括子物体上的）
        col = GetComponent<Collider>();
        if (col == null) col = GetComponentInChildren<Collider>();

        if (col != null)
        {
            // 已经有碰撞体，直接用，不再生成
            if (logColliderBounds)
                Debug.Log($"[碰撞体] 「{name}」已有碰撞体：{col.GetType().Name}" +
                          $"（在 {col.gameObject.name} 上），跳过自动生成");
        }
        else if (colliderMode == ColliderMode.MeshConvex)
        {
            col = CreateMeshColliders();
        }
        else
        {
            col = CreateBoxFromBounds();
        }

        // 实体碰撞体（不是触发器），这样才会和地面/其他道具碰撞
        if (col != null) col.isTrigger = false;
    }

    // ==================== MeshConvex：贴合网格的凸包碰撞体 ====================
    /// <summary>
    /// 给指定网格（及其子物体）各加一个 Convex MeshCollider。
    /// 形状完全贴合模型，不需要算包围盒 —— 这是 Unity 里最标准的做法。
    /// </summary>
    private Collider CreateMeshColliders()
    {
        var root = colliderSource != null ? colliderSource : transform;

        var meshFilters = new List<MeshFilter>();
        if (meshIncludeChildren)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf != null && mf.sharedMesh != null) meshFilters.Add(mf);
        }
        else
        {
            var mf = root.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) meshFilters.Add(mf);
        }

        if (meshFilters.Count == 0)
        {
            Debug.LogWarning($"[碰撞体] 「{name}」在 {root.name} 里找不到网格，" +
                             $"无法生成 MeshCollider。改用 BoxFromBounds。");
            return CreateBoxFromBounds();
        }

        Collider first = null;
        int count = 0;
        var addedNames = new System.Text.StringBuilder();

        foreach (var mf in meshFilters)
        {
            // 跳过已经有的
            if (mf.GetComponent<Collider>() != null) continue;

            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;          // 必须勾：动态 Rigidbody 只支持凸包
            mc.isTrigger = false;

            if (first == null) first = mc;
            count++;

            if (addedNames.Length > 0) addedNames.Append("、");
            addedNames.Append(mf.name);
        }

        // 所有 MeshCollider 的日志合成一条，避免"每个网格一条"刷屏
        if (logColliderBounds && count > 0)
            Debug.Log($"[碰撞体] 「{name}」生成了 {count} 个 MeshCollider：{addedNames}");

        return first;
    }

    // ==================== BoxFromBounds：按包围盒生成方块 ====================
    /// <summary>按渲染包围盒生成一个 BoxCollider（近似形状）</summary>
    private Collider CreateBoxFromBounds()
    {
        if (gameObject.GetComponent<Collider>() == null)
        {
            // 没碰撞体就按渲染包围盒自动加一个
            var box = gameObject.AddComponent<BoxCollider>();

            // 用哪个物体算包围盒：
            //   · 指定了 colliderSource → 用它的所有渲染器合起来的包围盒
            //   · 否则 → 用本物体及子物体的所有渲染器
            Bounds? b = ComputeBounds();

            // 手动模式：完全用指定值
            if (overrideCollider)
            {
                box.center = overrideCenter;
                box.size = overrideSize;
                Debug.Log($"[碰撞体] 「{name}」【手动模式】\n" +
                          $"  center = {box.center}\n" +
                          $"  size   = {box.size}\n" +
                          $"  世界范围 = {box.bounds.min} ~ {box.bounds.max}");
            }
            else if (b.HasValue)
            {
                Vector3 worldCenter = b.Value.center;
                Vector3 worldSize = b.Value.size * colliderShrink;

                // ⚠️ renderer.bounds 是世界空间的，而 BoxCollider 的
                //    center/size 是【本地空间】的，必须按 lossyScale 换算。
                Vector3 s = transform.lossyScale;
                box.center = transform.InverseTransformPoint(worldCenter);
                box.size = new Vector3(
                    Mathf.Abs(s.x) > 0.0001f ? worldSize.x / Mathf.Abs(s.x) : worldSize.x,
                    Mathf.Abs(s.y) > 0.0001f ? worldSize.y / Mathf.Abs(s.y) : worldSize.y,
                    Mathf.Abs(s.z) > 0.0001f ? worldSize.z / Mathf.Abs(s.z) : worldSize.z);

                if (logColliderBounds)
                {
                    Debug.Log($"[碰撞体] 「{name}」\n" +
                              $"  Collider Source = {(colliderSource != null ? colliderSource.name : "（未指定）")}\n" +
                              $"  世界包围盒中心 = {worldCenter}\n" +
                              $"  世界包围盒大小 = {b.Value.size}  → 缩小后 {worldSize}\n" +
                              $"  本物体 lossyScale = {s}\n" +
                              $"  → BoxCollider.center = {box.center}\n" +
                              $"  → BoxCollider.size   = {box.size}\n" +
                              $"  → 碰撞体世界范围 = {box.bounds.min} ~ {box.bounds.max}\n" +
                              $"  使用的渲染器数量 = {CountRenderers()}");
                }
            }
            else
            {
                box.size = Vector3.one * 0.2f;   // 兜底：20 厘米的方块
            }

            col = box;
        }
        else
        {
            col = gameObject.GetComponent<Collider>();
        }

        // ---------- 物理材质（弹性 + 摩擦）----------
        if (col != null)
        {
            var mat = new PhysicsMaterial($"ItemPhysics_{name}")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = PhysicsMaterialCombine.Average,
                frictionCombine = PhysicsMaterialCombine.Average
            };
            col.material = mat;
        }

        return col;
    }

    // ==================== 抓取配置 ====================
    private void SetupGrabInteractable()
    {
        grab = GetComponent<XRGrabInteractable>();
        if (grab == null) grab = gameObject.AddComponent<XRGrabInteractable>();

        // ---------- 移动方式 ----------
        // VelocityTracking：跟随手柄但保留速度，最跟手也最自然
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

        // ---------- 跟随手感 ----------
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.trackScale = false;
        grab.velocityDamping = positionDamping;
        grab.angularVelocityDamping = rotationDamping;
        grab.attachEaseInTime = 0.15f;
        grab.snapToColliderVolume = true;
        grab.useDynamicAttach = true;

        // ---------- 投掷 ----------
        grab.throwOnDetach = throwOnDetach;
        grab.throwVelocityScale = throwVelocityScale;
        grab.throwAngularVelocityScale = 1f;
        grab.throwSmoothingDuration = 0.25f;
        grab.forceGravityOnDetach = true;

        // ---------- 双手抓取（大件道具需要两只手抬）----------
        grab.selectMode = InteractableSelectMode.Multiple;

        // ---------- 事件接线 ----------
        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
    }

    private void OnDestroy()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }
    }

    // ==================== 抓取 / 放下 ====================
    private void OnGrabbed(SelectEnterEventArgs args)
    {
        isGrabbed = true;

        // 拿在手上时关掉物理，避免抖动
        if (rb != null)
        {
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isGrabbed = false;

        // 松手恢复物理
        if (rb != null)
        {
            rb.useGravity = true;
        }

        // ---------- 松手时判断"是否用在某个身体部位上" ----------
        if (useOnReleaseOverBodyPart)
            TryUseOnNearestBodyPart();

        // 通知订阅者（放置区之类的系统）
        OnDropped?.Invoke(this);
    }

    /// <summary>
    /// 松手瞬间，检查是否正好压在某个身体部位上。
    /// 是 → 调用该部位的 TryApplyItem
    /// </summary>
    private void TryUseOnNearestBodyPart()
    {
        if (col == null) return;

        // 用"稍微放大一点的包围盒"做检测，容错更好
        Bounds b = col.bounds;
        b.Expand(0.06f);

        var parts = FindObjectsByType<PatientBodyPart>();

        PatientBodyPart best = null;
        float bestDist = float.MaxValue;

        foreach (var p in parts)
        {
            if (p == null) continue;

            var partCol = p.GetComponent<Collider>();
            if (partCol == null) continue;

            // 两个包围盒相交才算"压在部位上"
            if (!b.Intersects(partCol.bounds)) continue;

            float d = Vector3.Distance(b.center, partCol.bounds.center);
            if (d < bestDist) { bestDist = d; best = p; }
        }

        if (best != null)
        {
            string id = ItemId;
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"[道具] {name} 没有 ItemTag，无法判定用途");
                return;
            }

            Debug.Log($"[道具使用] 「{ItemDisplayName}」→「{best.part}」");
            best.TryApplyItem(id, ItemDisplayName);
        }
    }

    // ==================== 被 PropInteractor 调用：显示介绍 ====================
    /// <summary>按交互键时显示这个道具的介绍</summary>
    public void ShowInfo()
    {
        string text = BuildInfoText();

        var hint = FindAnyObjectByType<HintPanel>();
        if (hint != null) hint.ShowHint(text, infoDisplayDuration);

        Debug.Log($"[道具介绍] {text}");
    }

    private string BuildInfoText()
    {
        if (itemTag == null)
            return $"【{gameObject.name}】\n（未配置 ItemTag，没有介绍文字）";

        string used = DescribeUsage(itemTag.tagName);

        string desc = string.IsNullOrEmpty(itemTag.displayName)
            ? "（暂无说明）" : itemTag.displayName;

        // 如果 displayName 只是标识名的复读，就只显示用途
        if (desc == itemTag.tagName) desc = "";

        return string.IsNullOrEmpty(desc)
            ? $"【{used}】"
            : $"【{itemTag.tagName}】\n{desc}\n\n用法：{used}";
    }

    /// <summary>按道具标识给出用途说明</summary>
    private static string DescribeUsage(string tag)
    {
        switch (tag)
        {
            case "Splint": return "用于固定骨折的肢体（上臂 / 前臂 / 大腿 / 小腿）";
            case "IcePack": return "用于冷敷扭伤、挫伤的部位（大腿 / 小腿）";
            case "Bandage": return "用于包扎外伤出血，或对肢体加压包扎（头部 / 手臂 / 腿部）";
            case "Blanket": return "用于给失温伤者保温（躯干）";
            case "Mat": return "用于隔绝地面低温（躯干）";
            case "Phone": return "用于拨打急救电话、记录伤情";
            case "Gloves": return "用于保护自己，避免接触伤者体液";
            case "WarningSign": return "用于在事故现场后方设立警示";
            default: return "急救道具";
        }
    }
}
