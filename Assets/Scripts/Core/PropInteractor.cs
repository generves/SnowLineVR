using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 道具交互键 —— 挂在手柄的交互器上，用【交互键】对物品做操作。
///
/// 按下交互键时按优先级判断：
///   ① 手上拿着道具 + 射线指着【身体部位】 → 使用道具（核心玩法）
///   ② 手上拿着道具（没指部位）            → 显示这个道具的介绍
///   ③ 空手 + 指着医疗箱                   → 打开 / 关闭箱子
///   ④ 空手 + 指着身体部位                 → 提示需要拿对应物品
///   ⑤ 空手 + 指着地上道具                 → 显示这个道具的介绍
///
/// 按键：
///   右手柄 → B 键（secondaryButton）
///   左手柄 → Y 键（secondaryButton）
///   键盘调试 → F1（左手）/ F2（右手）/ F3（双手都响应）
///
/// ⚠️ 按键选择说明：
///   · 不用 A / X（primaryButton）—— XR Device Simulator 占用了 A
///   · 不用 E —— 模拟器把 E 当成"抬升/下降"
///   · 所以用 F1/F2/F3，全部避开模拟器
///
/// 用法：
///   ① 在 Hierarchy 里选中 Left Controller / Right Controller
///   ② 展开它，找到 Near-Far Interactor（或 Ray Interactor）
///   ③ Add Component → 搜 Prop Interactor → 添加
///   ④ 把【Hand】设成对应的手（左手柄 Left、右手柄 Right）
/// </summary>
[RequireComponent(typeof(XRBaseInteractor))]
public class PropInteractor : MonoBehaviour
{
    public enum HandKind
    {
        自动判断,
        左手,
        右手,
    }

    [Header("手")]
    [Tooltip("这是哪只手。自动判断会按物体名字里的 Left / Right 猜")]
    public HandKind hand = HandKind.自动判断;

    [Header("检测")]
    [Tooltip("兜底射线的最远检测距离（米）")]
    public float maxDistance = 5f;

    [Tooltip("能被交互的层")]
    public LayerMask interactableLayers = ~0;

    [Header("道具使用")]
    [Tooltip("拿着道具对准身体部位时，玩家离部位的最大距离（米）。0 = 不限")]
    public float useMaxDistance = 2.5f;

    [Tooltip("勾上后，空手按交互键会切换『使用模式』（手里有道具时就能指向目标）")]
    public bool allowUseMode = true;

    [Header("键盘调试按键")]
    [Tooltip("切换『使用模式』的调试键。F6 = 左手，F7 = 右手（避开模拟器占用的键）")]
    public bool allowKeyboardDebugKeys = true;

    // ==================== 静态注册表（双手协作要用） ====================
    private static readonly List<PropInteractor> All = new List<PropInteractor>();

    /// <summary>场景里所有活着的交互器（身体部位要遍历它们判断"谁指着我"）</summary>
    public static IReadOnlyList<PropInteractor> Active => All;

    /// <summary>
    /// 当前玩家手上拿着的道具标识集合（所有手加起来）。
    /// 身体部位靠这个来决定"要不要高亮成可用/不可用"。
    /// </summary>
    public static readonly HashSet<string> HeldTags = new HashSet<string>();

    /// <summary>手上拿着的道具集合发生变化时触发（身体部位订阅它来刷新高亮）</summary>
    public static event Action OnHeldItemsChanged;

    /// <summary>是否处于『使用模式』：拿着道具时也能指向别的物体</summary>
    public bool IsUseMode { get; private set; }

    [Header("调试")]
    [Tooltip("按下交互键时在 Console 打印判定过程")]
    public bool logToConsole = false;

    [Tooltip("允许用键盘 E 键模拟交互（配合 XR Device Simulator 测试）")]
    public bool allowKeyboardDebug = true;

    // ---------- 内部 ----------
    private XRBaseInteractor interactor;
    private bool handResolved = false;

    /// <summary>自己画的指向射线（使用模式下显示）</summary>
    private InteractorPointerLine pointerLine;

#if ENABLE_INPUT_SYSTEM
    private InputAction interactAction;
#endif

    private void Awake()
    {
        interactor = GetComponent<XRBaseInteractor>();
        ResolveHand();

        // 自动补一条自己画的指向射线：
        // XRI 自带的 XRInteractorLineVisual 在"抓着东西"时会被隐藏，
        // 所以拿着道具时看不到指着哪 —— 用这个补上。
        pointerLine = GetComponent<InteractorPointerLine>();
        if (pointerLine == null) pointerLine = gameObject.AddComponent<InteractorPointerLine>();
        pointerLine.SetVisible(false);
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
#if ENABLE_INPUT_SYSTEM
        BuildAction();
#endif
    }

    private void OnDisable()
    {
        All.Remove(this);
        RebuildHeldTags();

#if ENABLE_INPUT_SYSTEM
        if (interactAction != null)
        {
            interactAction.Disable();
            interactAction.Dispose();
            interactAction = null;
        }
#endif
    }

    private void Update()
    {
        // 手上道具变化时刷新全局集合（身体部位据此高亮）
        DetectHeldChange();

        if (allowUseMode && WasModeSwitchPressed())
        {
            ToggleUseMode();
        }

        // 只在"手上有道具 + 指着有效身体部位"时显示指向线
        UpdatePointerLineVisibility();

        if (WasInteractPressed()) HandleInteract();
    }

    /// <summary>
    /// 指向线的显示条件（用户要求）：
    ///   · 手里没东西            → 隐藏
    ///   · 手里有东西但没指到目标 → 隐藏
    ///   · 有东西 + 指着有效目标  → 显示
    ///   · 另外：手上的道具本身不算目标（射线会穿透它继续往前打）
    /// </summary>
    private void UpdatePointerLineVisibility()
    {
        if (pointerLine == null) return;

        bool show = IsAimingAtValidTarget(out _);

        if (show != pointerLine.show)
            pointerLine.SetVisible(show);
    }

    // ==================== 使用模式 ====================
    /// <summary>
    /// 切换『使用模式』。
    ///
    /// 用途：解决"手里抓着道具就没法用射线指别的物体"的问题。
    ///   · 关：拿着道具时射线被选择槽占满 → 指不到目标
    ///   · 开：交互器改用别的途径保留一条指向射线 → 可以"拿着道具指目标"
    /// </summary>
    public void ToggleUseMode()
    {
        IsUseMode = !IsUseMode;
        ApplyUseModeToInteractor(IsUseMode);

        if (logToConsole) Debug.Log($"[道具使用模式] {gameObject.name}（{hand}）→ {(IsUseMode ? "开" : "关")}");
        ShowHint(IsUseMode
            ? "使用模式：拿着道具指向伤者部位，按交互键即可使用"
            : "已退出使用模式", false);
    }

    /// <summary>
    /// 使用模式的开关（真机能用上的部分）。
    ///
    /// ⚠️ XRI 的机制说明：
    ///   · 【选择模式】`selectMode` 在 Interactable 上（不是 Interactor），
    ///     控制的是"一个物体能被几只手同时抓"。
    ///   · 【交互器】这边能控制的是 `allowHover` / `allowSelect`，
    ///     控制的是"这只手还能不能悬停 / 选择别的物体"。
    ///
    ///   所以"拿着道具还能不能指向别的物体"其实是 XRI 的固有行为 ——
    ///   交互器在已选择状态下【仍然会继续悬停】其他物体，
    ///   身体部位的高亮就是靠这个悬停事件触发的。
    ///
    ///   这个开关做的是：进入使用模式后，把 allowHover / allowSelect 显式打开，
    ///   并关掉 Near-Far 的"近场优先"，保证射线（远场）一直有效。
    /// </summary>
    /// <summary>
    /// 使用模式对交互器的影响。
    ///
    /// ⚠️ XRI 机制说明：
    ///   · 【选择模式】`selectMode` 在 Interactable 上（不是 Interactor）
    ///   · 【交互器】能控制的是 allowHover / allowSelect
    ///   · 线条可见性由 disableVisualsWhenBlockedInGroup 控制（默认 true = 抓着东西就不画线）
    ///
    ///   指向线不由这里控制 —— 由 UpdatePointerLineVisibility() 每帧按条件决定。
    /// </summary>
    private void ApplyUseModeToInteractor(bool on)
    {
        if (interactor == null) return;

        // 悬停必须一直开着 —— 身体部位的高亮依赖它
        interactor.allowHover = true;
        interactor.allowSelect = true;

        // 组内被挡住时不要隐藏视觉 —— 这是"拿着东西看不到射线"的主因之一
        interactor.disableVisualsWhenBlockedInGroup = false;

        // Near-Far 交互器：使用模式下强制开启远场射线
        var nf = interactor as NearFarInteractor;
        if (nf != null)
        {
            nf.enableFarCasting = true;
            nf.enableNearCasting = !on;
        }

        if (logToConsole)
        {
            Debug.Log($"[道具使用模式] {gameObject.name}（{hand}）→ {(on ? "开" : "关")}\n" +
                      $"  交互器类型 = {interactor.GetType().Name}\n" +
                      $"  allowHover = true  allowSelect = true\n" +
                      $"  disableVisualsWhenBlockedInGroup = false" +
                      (nf != null ? $"\n  enableFarCasting = true  enableNearCasting = {!on}" : ""));
        }
    }

    // ==================== 手上道具集合（供身体部位高亮） ====================
    private HashSet<string> lastTags = new HashSet<string>();

    private void DetectHeldChange()
    {
        var now = new HashSet<string>();
        var sel = interactor != null ? interactor.interactablesSelected : null;
        if (sel != null)
        {
            foreach (var s in sel)
            {
                var comp = s as Component;
                if (comp == null) continue;
                var tag = comp.GetComponentInParent<ItemTag>();
                if (tag != null && !string.IsNullOrEmpty(tag.tagName)) now.Add(tag.tagName);
            }
        }

        if (now.Count == lastTags.Count && now.SetEquals(lastTags)) return;

        lastTags = now;
        RebuildHeldTags();
    }

    /// <summary>重建全局『手上道具』集合，并通知所有身体部位刷新高亮</summary>
    private static void RebuildHeldTags()
    {
        HeldTags.Clear();

        foreach (var pi in All)
        {
            if (pi == null) continue;
            foreach (var t in pi.lastTags) HeldTags.Add(t);
        }

        OnHeldItemsChanged?.Invoke();
    }

    // ==================== 射线瞄准（使用模式用） ====================
    /// <summary>本手射线的起点</summary>
    public Vector3 RayOrigin
    {
        get
        {
            var rp = interactor as IXRRayProvider;
            if (rp != null)
            {
                var o = rp.GetOrCreateRayOrigin();
                if (o != null) return o.position;
            }
            return transform.position;
        }
    }

    /// <summary>本手射线的方向</summary>
    public Vector3 RayDirection
    {
        get
        {
            var rp = interactor as IXRRayProvider;
            if (rp != null)
            {
                var o = rp.GetOrCreateRayOrigin();
                if (o != null)
                {
                    var e = rp.rayEndTransform;
                    if (e != null && (e.position - o.position).sqrMagnitude > 0.000001f)
                        return (e.position - o.position).normalized;

                    // 没有终点变换时，用手柄自身朝向（比 castOrigin.forward 可靠）
                    if (interactor != null) return interactor.transform.forward;
                    return o.forward;
                }
            }
            if (interactor != null) return interactor.transform.forward;
            return transform.forward;
        }
    }

    /// <summary>
    /// 使用模式射线的瞄准查询。
    ///
    /// ⚠️ 关键：射线会【跳过手上拿着的道具】。
    ///    道具就挂在交互器的 attach 点上，正好挡在射线路径里。
    ///    如果只打一次射线，永远打在自己手上的东西上，指不到别的目标。
    ///
    /// 做法：从射线起点往前打；如果命中的是手上道具，
    ///       就【从那个命中点继续往前打】，直到打到别的物体或到射程为止。
    /// </summary>
    /// <param name="allowBodyParts">是否把身体部位算作目标</param>
    /// <param name="allowProps">是否把地上的道具算作目标</param>
    public GameObject GetRayAimedObject(bool allowBodyParts = true, bool allowProps = true)
    {
        Vector3 origin = RayOrigin;
        Vector3 dir = RayDirection;
        float remaining = maxDistance;

        // 最多穿透 4 层（正常情况下只会穿透手上道具那一层）
        for (int i = 0; i < 4 && remaining > 0.01f; i++)
        {
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, remaining,
                    interactableLayers, QueryTriggerInteraction.Ignore))
                break;

            var item = hit.collider.GetComponentInParent<ItemPhysicsSetup>();

            // 命中的是手上道具 → 跳过它，从命中点继续往前
            if (item != null && IsHeldByMe(item))
            {
                float adv = hit.distance + 0.01f;      // 推进一点点，避免重复命中同一个面
                origin += dir * adv;
                remaining -= adv;
                continue;
            }

            // 是道具但不允许道具当目标 → 也算打到了东西，直接返回 null
            if (!allowProps && item != null) return null;

            // 是身体部位但不允许 → 同上
            if (!allowBodyParts && hit.collider.GetComponentInParent<PatientBodyPart>() != null)
                return null;

            // 打到了有效目标
            return hit.collider.gameObject;
        }

        return null;
    }

    /// <summary>本手现在是不是"拿着道具 + 指着一个有效目标"。指向线的显示条件。</summary>
    public bool IsAimingAtValidTarget(out GameObject target)
    {
        target = null;

        // 手上没东西 → 不算
        if (GetHeldItem() == null) return false;

        target = GetRayAimedObject();
        return target != null;
    }

    /// <summary>本手现在拿着哪件道具（没有则 null）。供身体部位查询。</summary>
    public ItemPhysicsSetup HeldItem => GetHeldItem();

    /// <summary>
    /// 本手射线现在指着哪个身体部位（没有或手上没道具则 null）。
    ///
    /// 用途：身体部位据此决定"亮成绿色（适用）还是红色（不适用）"。
    /// </summary>
    public PatientBodyPart GetAimedBodyPart()
    {
        // 手上没东西就不算"瞄准"（空手的高亮走 XRI 悬停）
        if (GetHeldItem() == null) return null;

        var go = GetRayAimedObject();
        return go != null ? go.GetComponentInParent<PatientBodyPart>() : null;
    }

    /// <summary>本手现在拿着的道具是不是这个</summary>
    private bool IsHeldByMe(ItemPhysicsSetup item)
    {
        var sel = interactor != null ? interactor.interactablesSelected : null;
        if (sel == null) return false;

        foreach (var s in sel)
        {
            var comp = s as Component;
            if (comp == null) continue;
            if (comp.GetComponentInParent<ItemPhysicsSetup>() == item) return true;
        }
        return false;
    }

    /// <summary>找【另一只手】正在拿的道具（双手协作用）</summary>
    private ItemPhysicsSetup GetItemFromOtherHand()
    {
        foreach (var pi in All)
        {
            if (pi == null || pi == this) continue;
            if (pi.hand == hand) continue;      // 同一只手不算

            var sel = pi.interactor != null ? pi.interactor.interactablesSelected : null;
            if (sel == null) continue;

            foreach (var s in sel)
            {
                var comp = s as Component;
                if (comp == null) continue;
                var item = comp.GetComponentInParent<ItemPhysicsSetup>();
                if (item != null) return item;
            }
        }
        return null;
    }


    // ==================== 手别判断 ====================
    private void ResolveHand()
    {
        if (handResolved) return;
        handResolved = true;

        if (hand != HandKind.自动判断) return;

        // 从自己往上找，看哪个祖先名字里有 Left / Right
        Transform t = transform;
        int guard = 0;
        while (t != null && guard++ < 12)
        {
            string n = t.name.ToLower();
            if (n.Contains("left")) { hand = HandKind.左手; return; }
            if (n.Contains("right")) { hand = HandKind.右手; return; }
            t = t.parent;
        }
    }

    // ==================== 按键 ====================
#if ENABLE_INPUT_SYSTEM
    private void BuildAction()
    {
        if (interactAction != null) return;

        // 用 secondaryButton：
        //   右手柄 = B 键
        //   左手柄 = Y 键
        // 不用 primaryButton（A/X），因为 XR Device Simulator 把 A 当做移动键。
        string path = hand == HandKind.左手
            ? "<XRController>{LeftHand}/secondaryButton"
            : "<XRController>{RightHand}/secondaryButton";

        interactAction = new InputAction("PropInteract", InputActionType.Button, path);
        interactAction.Enable();
    }
#endif

    private bool WasInteractPressed()
    {
        // ---------- 手柄按键 ----------
#if ENABLE_INPUT_SYSTEM
        if (interactAction != null && interactAction.WasPressedThisFrame())
            return true;

        // ---------- 键盘调试 ----------
        // ⚠️ 已被 XR Device Simulator 占用的键（不能用）：
        //    a d s w q e leftShift space leftCtrl x c z r \ v 1 2 3 tab escape y t u h
        // 所以这里用功能键 F1~F3，不会冲突。
        if (allowKeyboardDebug)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                // F1：左手柄用；F2：右手柄用
                if (hand == HandKind.左手 && kb.f1Key != null && kb.f1Key.wasPressedThisFrame) return true;
                if (hand == HandKind.右手 && kb.f2Key != null && kb.f2Key.wasPressedThisFrame) return true;

                // F3：两只手都响应（方便快速测试）
                if (kb.f3Key != null && kb.f3Key.wasPressedThisFrame) return true;
            }
        }
        return false;
#else
        if (allowKeyboardDebug)
        {
            if (hand == HandKind.左手 && Input.GetKeyDown(KeyCode.F1)) return true;
            if (hand == HandKind.右手 && Input.GetKeyDown(KeyCode.F2)) return true;
            if (Input.GetKeyDown(KeyCode.F3)) return true;
        }
        return false;
#endif
    }

    /// <summary>
    /// 是否按下了『切换使用模式』的键。
    /// 键盘调试：F6 = 左手，F7 = 右手。
    /// 真机上手柄用——手柄没有额外按键可用时，可以改成"长按交互键"。
    /// </summary>
    private bool WasModeSwitchPressed()
    {
        if (!allowKeyboardDebugKeys) return false;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        if (hand == HandKind.左手 && kb.f6Key != null && kb.f6Key.wasPressedThisFrame) return true;
        if (hand == HandKind.右手 && kb.f7Key != null && kb.f7Key.wasPressedThisFrame) return true;
        // F8：两只手都响应
        if (kb.f8Key != null && kb.f8Key.wasPressedThisFrame) return true;
        return false;
#else
        if (hand == HandKind.左手 && Input.GetKeyDown(KeyCode.F6)) return true;
        if (hand == HandKind.右手 && Input.GetKeyDown(KeyCode.F7)) return true;
        if (Input.GetKeyDown(KeyCode.F8)) return true;
        return false;
#endif
    }

    // ==================== 交互逻辑 ====================
    /// <summary>
    /// 按下交互键的总入口。三种"用道具处理部位"的方式都在这里收口：
    ///
    ///   方式 A（双手分工）  ：另一只手拿着道具，这只手用射线指着部位
    ///   方式 B（使用模式）  ：同一只手，开了『使用模式』后拿着道具也能指
    ///   方式 C（直接接触）  ：由 PatientBodyPart 的触发器负责，不经过这里
    /// </summary>
    private void HandleInteract()
    {
        if (logToConsole) Debug.Log($"[交互键] {gameObject.name}（{hand}）按下");

        // ---------- 诊断：射线实际命中了什么 ----------
        LogRaycastInfo();

        // 射线指着的物体（先算出来，后面几个分支都要用）
        var held = GetHeldItem();

        // ⚠️ 统一用 GetRayAimedObject()：它会【穿透手上拿着的道具】继续往前打。
        //    否则手上有道具时，射线永远打在自己手上的东西上，指不到别的目标。
        GameObject target = GetRayAimedObject();

        // 兜底：新射线没打到东西时，退回 XRI 的悬停结果
        //（比如射线起点和 XRI 内部不一致的少数情况）
        if (target == null) target = GetLookedAtObject();

        var part = target != null ? target.GetComponentInParent<PatientBodyPart>() : null;

        // ============================================================
        // ① 指着身体部位，且"手上有可用的道具" → 使用
        //    道具来源按顺序：自己手上的 → 另一只手上的
        // ============================================================
        if (part != null)
        {
            ItemPhysicsSetup itemToUse = held;
            string source = "本手";

            if (itemToUse == null)
            {
                itemToUse = GetItemFromOtherHand();
                source = "另一只手";
            }

            if (itemToUse != null)
            {
                if (logToConsole)
                    Debug.Log($"[道具使用] 道具来自{source}：「{itemToUse.ItemDisplayName}」" +
                              $" → 部位「{part.part}」");
                HandleUseOnBodyPart(itemToUse, part);
                return;
            }

            // 两手都空 → 提示
            if (logToConsole)
                Debug.Log($"[交互键] 空手指向「{part.part}」→ 需要拿对应的急救物品");
            ShowHint(part.hoverMessage, true);
            return;
        }

        // ============================================================
        // ② 手上有道具但没指身体部位
        //    · 指着一个 PhoneCallTarget（比如路牌）→ 走呼救交互
        //    · 使用模式下 → 提示指哪；普通模式 → 显示道具介绍
        // ============================================================
        if (held != null)
        {
            // ⭐ 手机 → 路牌 呼救
            //    注意：本脚本的交互键（F1/F2/F3）是自建的调试键，
            //    不会驱动 XRI 的 selectEntered 事件，
            //    所以路牌上的 XRSimpleInteractable 收不到选择事件，
            //    PhoneCallTarget 必须由这里主动调用。
            var callTarget = target != null ? target.GetComponentInParent<PhoneCallTarget>() : null;
            if (callTarget != null)
            {
                if (logToConsole)
                    Debug.Log($"[交互键] 手上的「{held.ItemDisplayName}」→ 呼救目标「{callTarget.name}」");

                callTarget.TryInteract(held);
                return;
            }

            if (IsUseMode)
            {
                ShowHint("现在指着的不是伤者部位，请把射线指向需要处理的部位", true);
                return;
            }

            if (logToConsole) Debug.Log($"[交互键] 手上有「{held.ItemDisplayName}」但没指向身体部位 → 显示介绍");
            held.ShowInfo();
            return;
        }

        // ============================================================
        // ③ 空手：射线指着的物体
        // ============================================================
        if (target == null)
        {
            if (logToConsole) Debug.Log("[交互键] 射线没指到物体");
            ShowHint("请把射线指向要操作的道具或伤者部位", false);
            return;
        }

        if (logToConsole) Debug.Log($"[交互键] 射线命中：{GetPath(target.transform)}");

        // ③-a 医疗箱 → 开 / 关
        var box = target.GetComponentInParent<OpenableBox>();
        if (box != null)
        {
                if (logToConsole) Debug.Log($"[交互键] 指向医疗箱「{box.name}」→ {(box.IsOpen ? "关闭" : "打开")}");
            box.Toggle();
            return;
        }

        // ③-a2 可旋转交互物（担架床头板之类）→ 播旋转动画，之后不可再交互
        var rotatable = target.GetComponentInParent<RotateOnInteract>();
        if (rotatable != null)
        {
            if (logToConsole)
                Debug.Log($"[交互键] 指向旋转交互物「{rotatable.name}」" +
                          $"→ {(rotatable.IsDone ? "已完成" : "执行旋转")}");

            rotatable.Interact();
            return;
        }

        // ③-b 普通道具 → 显示介绍
        var item = target.GetComponentInParent<ItemPhysicsSetup>();
        if (item != null)
        {
            if (logToConsole) Debug.Log($"[交互键] 指向道具「{item.ItemDisplayName}」→ 显示介绍");
            item.ShowInfo();
            return;
        }

        if (logToConsole) Debug.Log($"[交互键] 「{target.name}」不是可交互道具");
        ShowHint($"「{target.name}」不能这样操作", true);
    }

    // ==================== 拿道具 → 用在身体部位上 ====================
    /// <summary>
    /// 玩家手上拿着道具，射线指着某个身体部位，按下了交互键。
    ///
    /// 判定流程：
    ///   ① 距离检查（不能隔太远"隔空治疗"）
    ///   ② 道具是否适用于该部位
    ///   ③ 调 PatientBodyPart.TryApplyItem → 正确/错误的提示和事件都由它负责
    /// </summary>
    private void HandleUseOnBodyPart(ItemPhysicsSetup held, PatientBodyPart part)
    {
        // 从【拿着道具的那只手】量距离 —— 双手协作时量的是持物手，不是指人的手
        Vector3 from = held.transform.position;
        float distance = Vector3.Distance(from, part.transform.position);

        if (useMaxDistance > 0f && distance > useMaxDistance)
        {
            ShowHint($"太远了，走近一点再处理（{distance:F1} 米）", true);
            if (logToConsole)
                Debug.Log($"[道具使用] 距离 {distance:F2} 米 > 上限 {useMaxDistance} 米，不生效");
            return;
        }

        if (logToConsole)
        {
            Debug.Log($"[道具使用] 手上「{held.ItemDisplayName}」→ 部位「{part.part}」" +
                      $"  距离={distance:F2}米  {(part.IsItemValidForPart(held.ItemId) ? "✅适用" : "❌不适用")}");
        }

        // 正确/错误的提示、事件都在 TryApplyItem 里统一处理
        part.TryApplyItem(held.ItemId, held.ItemDisplayName);
    }

    private static void ShowHint(string msg, bool warning)
    {
        var hint = FindAnyObjectByType<HintPanel>();
        if (hint == null) return;

        if (warning) hint.ShowWarning(msg);
        else hint.ShowHint(msg);
    }

    /// <summary>诊断：打印交互器的悬停列表和一条自打射线的命中结果</summary>
    private void LogRaycastInfo()
    {
        if (!logToConsole) return;

        // ---------- 悬停列表（XRI 算好的）----------
        string hoveredStr = "（空）";
        if (interactor != null && interactor.interactablesHovered != null
            && interactor.interactablesHovered.Count > 0)
        {
            hoveredStr = "";
            foreach (var h in interactor.interactablesHovered)
            {
                var comp = h as Component;
                hoveredStr += (comp != null ? GetPath(comp.transform) : "?") + "  ";
            }
        }

        // ---------- 自己打一条射线 ----------
        Vector3 origin;
        Vector3 dir;
        bool gotRay = false;

        if (interactor is IXRRayProvider rp)
        {
            var o = rp.GetOrCreateRayOrigin();
            var e = rp.rayEndTransform;
            if (o != null)
            {
                origin = o.position;
                dir = (e != null && (e.position - origin).sqrMagnitude > 0.0001f)
                    ? (e.position - origin).normalized
                    : o.forward;
                gotRay = true;
            }
            else { origin = transform.position; dir = transform.forward; }
        }
        else
        {
            var at = interactor != null ? interactor.GetAttachTransform(null) : null;
            Transform t = at != null ? at : transform;
            origin = t.position;
            dir = t.forward;
        }
        gotRay = true;

        string hitStr = "（没打中）";
        string distStr = "";
        if (gotRay && Physics.Raycast(origin, dir, out RaycastHit hit, maxDistance,
                interactableLayers, QueryTriggerInteraction.Ignore))
        {
            hitStr = GetPath(hit.collider.transform);
            distStr = $"  距离={hit.distance:F2}米";
        }

        if (logToConsole) Debug.Log($"[交互键] 诊断\n" +
                  $"  射线起点 = {origin}\n" +
                  $"  射线方向 = {dir}\n" +
                  $"  最大距离 = {maxDistance}\n" +
                  $"  自打射线命中 = {hitStr}{distStr}\n" +
                  $"  XRI 悬停列表 = {hoveredStr}\n" +
                  $"  交互器类型 = {(interactor != null ? interactor.GetType().Name : "无")}");
    }

    /// <summary>取物体的完整层级路径（方便定位）</summary>
    private static string GetPath(Transform t)
    {
        if (t == null) return "null";
        string path = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 8)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    /// <summary>取得手上正拿着的道具</summary>
    private ItemPhysicsSetup GetHeldItem()
    {
        if (interactor == null) return null;

        var selected = interactor.interactablesSelected;
        if (selected == null || selected.Count == 0) return null;

        foreach (var sel in selected)
        {
            var comp = sel as Component;
            if (comp == null) continue;

            var item = comp.GetComponentInParent<ItemPhysicsSetup>();
            if (item != null) return item;
        }
        return null;
    }

    /// <summary>射线指到的物体</summary>
    private GameObject GetLookedAtObject()
    {
        // ---------- 方案 1：直接读交互器当前的悬停目标 ----------
        // 最可靠：XRI 已经把"射线指到的东西"算好了（近距抓取和远距射线都覆盖），
        // 而且不用管具体是哪种交互器类型。
        var hovered = interactor.interactablesHovered;
        if (hovered != null && hovered.Count > 0)
        {
            foreach (var h in hovered)
            {
                var comp = h as Component;
                if (comp != null) return comp.gameObject;
            }
        }

        // ---------- 方案 2：XRRayInteractor 自带的命中查询 ----------
        if (interactor is XRRayInteractor ray)
        {
            if (ray.TryGetCurrent3DRaycastHit(out RaycastHit hit) && hit.collider != null)
                return hit.collider.gameObject;
        }

        // ---------- 方案 3：兜底，自己从射线原点往前打一条 ----------
        Vector3 origin;
        Vector3 direction;

        // NearFarInteractor 通过 IXRRayProvider 暴露射线原点与终点
        if (interactor is IXRRayProvider rayProvider)
        {
            var originTf = rayProvider.GetOrCreateRayOrigin();
            var endTf = rayProvider.rayEndTransform;

            if (originTf != null)
            {
                origin = originTf.position;

                if (endTf != null && (endTf.position - origin).sqrMagnitude > 0.0001f)
                    direction = (endTf.position - origin).normalized;
                else
                    direction = originTf.forward;
            }
            else
            {
                origin = interactor.transform.position;
                direction = interactor.transform.forward;
            }
        }
        else
        {
            var at = interactor.GetAttachTransform(null);
            Transform t = at != null ? at : interactor.transform;
            origin = t.position;
            direction = t.forward;
        }

        if (Physics.Raycast(origin, direction, out RaycastHit hit3,
                maxDistance, interactableLayers, QueryTriggerInteraction.Ignore))
            return hit3.collider.gameObject;

        return null;
    }
}

