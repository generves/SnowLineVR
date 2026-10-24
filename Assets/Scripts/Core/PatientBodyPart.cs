using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System;

/// <summary>
/// 伤者身体部位 —— 可交互的身体部位组件。
///
/// 交互规则：
///   · 空手触碰  → 橙色高亮 + 显示提示文字
///   · 手持道具  → 绿色高亮（道具适用）/ 红色高亮（道具不适用）
///
/// 十个部位（按骨骼对应）：
///   头部   → mixamorig:Head
///   躯干   → mixamorig:Spine1
///   左上臂 → mixamorig:LeftArm
///   左前臂 → mixamorig:LeftForeArm
///   右上臂 → mixamorig:RightArm
///   右前臂 → mixamorig:RightForeArm
///   左大腿 → mixamorig:LeftUpLeg
///   左小腿 → mixamorig:LeftLeg
///   右大腿 → mixamorig:RightUpLeg
///   右小腿 → mixamorig:RightLeg
///
/// 高亮方式：用一个胶囊形外壳罩住碰撞体，悬停时变色。
/// </summary>
[RequireComponent(typeof(CapsuleCollider))]
public class PatientBodyPart : MonoBehaviour
{
    public enum BodyPart
    {
        头部,
        躯干,
        左上臂,
        左前臂,
        右上臂,
        右前臂,
        左大腿,
        左小腿,
        右大腿,
        右小腿,
    }

    [Header("部位标识")]
    public BodyPart part = BodyPart.躯干;

    [Tooltip("对应的骨骼名（仅供参考，方便对照）")]
    public string boneName = "";

    [Header("胶囊碰撞体")]
    [Tooltip("这是哪一个部位，决定提示文字")]
    [TextArea(1, 3)]
    public string hoverMessage = "这个部位需要相应的急救物品才能处理";

    [Header("视觉反馈")]
    [Tooltip("是否生成胶囊高亮外壳")]
    public bool createHighlightVisual = true;

    [Tooltip("空手指着本部位时的颜色（橙色）")]
    public Color emptyHandColor = new Color(1f, 0.55f, 0.1f, 0.35f);

    // ---------- 拿着道具、射线正指着本部位（醒目）----------
    [Tooltip("拿着【适用】道具指着本部位 → 亮绿色「就是这里，按交互键」")]
    public Color aimedValidColor = new Color(0.15f, 1f, 0.35f, 0.75f);

    [Tooltip("拿着【不适用】道具指着本部位 → 亮红色「这里用错工具了」")]
    public Color aimedWrongColor = new Color(1f, 0.15f, 0.1f, 0.75f);

    // ---------- 拿着道具但没指着（暗淡提示）----------
    [Tooltip("手里拿着【适用】道具、但没指着 → 淡绿色提示「这里可以用」")]
    public Color holdValidHintColor = new Color(0.25f, 0.95f, 0.35f, 0.18f);

    [Tooltip("手里拿着【不适用】道具、但没指着 → 淡红色提示")]
    public Color holdWrongHintColor = new Color(1f, 0.2f, 0.15f, 0.15f);

    // ---------- 旧字段（保留，避免场景里已配的值丢失）----------
    [Tooltip("（旧）手持正确道具时的颜色。请用 Aimed Valid Color")]
    public Color validItemColor = new Color(0.25f, 0.95f, 0.35f, 0.4f);

    [Tooltip("（旧）手持错误道具时的颜色。请用 Aimed Wrong Color")]
    public Color wrongItemColor = new Color(1f, 0.2f, 0.15f, 0.4f);

    [Tooltip("手里拿着适用道具但没指着时，也给一个淡绿提示")]
    public bool highlightWhenHoldingValidItem = true;

    [Header("直接接触（Poke）")]
    [Tooltip("拿着道具【碰到】这个部位就自动算使用（真实世界里的做法）")]
    public bool enablePokeUse = true;

    [Tooltip("Poke 触发后多久内不再重复触发（秒），防止一次接触触发多次")]
    public float pokeCooldown = 1f;

    [Header("调试")]
    [Tooltip("调试用：勾上后胶囊外壳一直可见（不悬停也显示），方便调位置")]
    public bool debugAlwaysVisible = false;

    [Tooltip("调试模式下的显示颜色（半透明青色）")]
    public Color debugColor = new Color(0.2f, 0.9f, 1f, 0.25f);

    [Tooltip("是否允许用键盘快捷键切换调试显示")]
    public bool allowDebugHotkey = true;

    [Tooltip("打印每次悬停/交互的日志（很啰嗦，只在排查时打开）")]
    public bool logToConsole = false;

    // ---------- 对外事件 ----------
    /// <summary>空手交互。参数：本部位</summary>
    public event Action<PatientBodyPart> OnEmptyHandInteract;
    /// <summary>道具处理成功。参数：(部位, 道具标签)</summary>
    public event Action<PatientBodyPart, string> OnItemApplied;
    /// <summary>道具用错。参数：(部位, 道具标签)</summary>
    public event Action<PatientBodyPart, string> OnWrongItem;

    // ---------- 内部状态 ----------
    private CapsuleCollider capsule;
    private MeshRenderer highlightRenderer;
    private Material highlightMaterial;
    private bool isHovering;
    private XRBaseInteractor currentInteractor;

    // ---------- 全局调试开关（所有部位共用） ----------
    /// <summary>所有身体部位共用：是否强制显示胶囊外壳</summary>
    public static bool DebugShowAll = false;

    /// <summary>记录上一帧的调试状态，用于检测变化</summary>
    private static bool lastDebugShowAll = false;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        capsule = GetComponent<CapsuleCollider>();
        if (capsule != null) capsule.isTrigger = false;

        // ---------- 关键：只让本部位的 Interactable 管自己的碰撞体 ----------
        // 骨骼是链式的（Hips → LeftUpLeg → LeftLeg → ...），
        // XRBaseInteractable.Awake() 会执行 GetComponentsInChildren()，
        // 把【所有子物体】的碰撞体都收进 m_Colliders。
        // 于是父骨骼的 Interactable 抢注了子骨骼的碰撞体，
        // XRI 的「碰撞体→Interactable」映射表就错了：
        //   射线打中小腿的碰撞体 → 查表返回大腿 → 高亮大腿
        //
        // 这里清掉并只留自己的那一个。
        StripCollidersToOwn();

        // 单个部位的调试开关也算进全局
        if (debugAlwaysVisible) DebugShowAll = true;

        EnsureHighlightVisual();

        // 材质刚创建时是全透明的，这里立即刷一次，
        // 否则要等鼠标划过才显示（调试模式下会看不到胶囊）
        UpdateHighlight();
    }

    /// <summary>
    /// 把 XRSimpleInteractable 的碰撞体列表裁剪成【只有自己那一个】。
    /// 同时会打印诊断日志，方便确认裁剪是否生效。
    /// </summary>
    private void StripCollidersToOwn()
    {
        var interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        if (interactable == null || capsule == null) return;

        // 先记下原来的内容（诊断用）
        int before = interactable.colliders.Count;
        var others = new System.Collections.Generic.List<string>();
        foreach (var c in interactable.colliders)
        {
            if (c == null) { others.Add("null"); continue; }
            if (c == capsule) continue;
            others.Add(c.transform.name);
        }

        interactable.colliders.Clear();
        interactable.colliders.Add(capsule);

        // 只有"确实抢了别人的碰撞体"时才打日志，而且默认只在详细模式下打
        // （十个部位会各打一条，很容易刷屏）
        if (others.Count > 0 && logToConsole)
        {
            if (logToConsole) Debug.LogWarning($"[碰撞体修正] 「{part}」({gameObject.name}) 原本注册了 {before} 个碰撞体，" +
                             $"其中多余的：{string.Join("、", others)}  → 已裁剪为只剩自己");
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器里改组件 / 保存场景时调用。
    /// 用来把【序列化保存下来的】多余碰撞体清掉 —— 否则即使运行时清了，
    /// 保存进场景的旧数据还会在下次注册时抢注子骨骼的碰撞体。
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        StripCollidersToOwn();
    }
#endif

    private void Start()
    {
        // Start 里再刷一次：确保其它部位的 Awake 也已经跑完
        UpdateHighlight();

        // 订阅"手上道具变化"—— 拿着适用道具时要把本部位高亮成绿色
        PropInteractor.OnHeldItemsChanged += OnPlayerHeldItemsChanged;
    }

    /// <summary>玩家手上道具变了 → 重新决定要不要高亮本部位</summary>
    private void OnPlayerHeldItemsChanged()
    {
        UpdateHighlight();
    }

    /// <summary>
    /// 当前玩家手上有没有【适用于本部位】的道具。
    /// 用 PropInteractor 的全局集合，所以不管是左手还是右手拿的都能识别。
    /// </summary>
    private bool PlayerHoldsValidItem(out string heldTag)
    {
        heldTag = null;
        foreach (var t in PropInteractor.HeldTags)
        {
            if (IsItemValidForPart(t)) { heldTag = t; return true; }
        }
        return false;
    }

    /// <summary>当前玩家手上有没有道具（任意）</summary>
    private bool PlayerHoldsAnyItem(out string heldTag)
    {
        heldTag = null;
        foreach (var t in PropInteractor.HeldTags) { heldTag = t; return true; }
        return false;
    }

    // 上一帧的高亮判定状态（变化时才刷材质，避免每帧都 SetColor）
    private bool lastAimed;
    private bool lastHoldsAny;

    private void Update()
    {
        // ---------- 每帧检测"有没有被手持道具的射线指着" ----------
        // 不能只靠 XRI 的悬停事件：手上有道具时那个事件可能不来。
        // 自己判断，状态变了才真正改材质。
        bool aimed = AimedByAnyHand(out _);
        bool holdsAny = PlayerHoldsAnyItem(out _);

        if (aimed != lastAimed || holdsAny != lastHoldsAny)
        {
            lastAimed = aimed;
            lastHoldsAny = holdsAny;
            UpdateHighlight();
        }

        // 键盘快捷键：切换所有胶囊的显示
        // ⚠️ 用 F4 / F5，因为 XR Device Simulator 占用了：
        //    a d s w q e leftShift space leftCtrl x c z r \ v 1 2 3 tab escape y t u h
        // 而 F1~F3 被 PropInteractor 占用。
        if (allowDebugHotkey &&
            (DebugKeyDown(KeyCode.F4) || DebugKeyDown(KeyCode.F5)))
        {
            DebugShowAll = !DebugShowAll;
            lastDebugShowAll = DebugShowAll;
            UpdateHighlight();

            if (logToConsole) Debug.Log($"[调试] 胶囊显示：{(DebugShowAll ? "开启" : "关闭")}");

            // 开启时顺便打一份所有胶囊的真实世界坐标，方便诊断
            if (DebugShowAll) DumpAllCapsules();
            return;
        }

        // 调试状态被别的地方改变时，跟着刷新
        if (DebugShowAll != lastDebugShowAll)
        {
            lastDebugShowAll = DebugShowAll;
            UpdateHighlight();
        }
    }

    // ==================== 诊断：打印所有胶囊的世界坐标 ====================
    private static void DumpAllCapsules()
    {
        var all = new System.Collections.Generic.List<PatientBodyPart>();
        foreach (var p in FindObjectsByType<PatientBodyPart>())
            if (p != null && p.capsule != null) all.Add(p);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========================================================");
        sb.AppendLine($"  十个碰撞胶囊的真实位置（共 {all.Count} 个）");
        sb.AppendLine("========================================================");

        foreach (var p in all)
        {
            var c = p.capsule;
            Vector3 worldCenter = c.transform.TransformPoint(c.center);
            Vector3 ls = c.transform.lossyScale;

            // 胶囊在世界空间的半径和总高
            float wr = c.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
            float wh = c.height * Mathf.Abs(ls.y) + 2f * wr;

            sb.AppendLine();
            sb.AppendLine($"【{p.part}】骨骼 {p.gameObject.name}");
            sb.AppendLine($"   世界中心 = ({worldCenter.x:F3}, {worldCenter.y:F3}, {worldCenter.z:F3})");
            sb.AppendLine($"   世界半径 = {wr:F3} 米    世界总高 = {wh:F3} 米");
            sb.AppendLine($"   Y 范围 = {worldCenter.y - wh * 0.5f:F3} ~ {worldCenter.y + wh * 0.5f:F3}");
            sb.AppendLine($"   本地参数 Center=({c.center.x:F1},{c.center.y:F1},{c.center.z:F1}) " +
                          $"R={c.radius:F1} H={c.height:F1} Dir={c.direction}");

            // ---------- 高亮外壳的状态 ----------
            var visual = p.transform.Find("HighlightVisual");
            if (visual == null)
            {
                sb.AppendLine("   高亮外壳：❌ 不存在！");
            }
            else
            {
                var mr2 = visual.GetComponent<MeshRenderer>();
                var mf2 = visual.GetComponent<MeshFilter>();
                bool meshOk = mf2 != null && mf2.sharedMesh != null;

                string matInfo = "无材质";
                if (mr2 != null && mr2.sharedMaterial != null)
                {
                    var m = mr2.sharedMaterial;
                    Color mc = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.magenta;
                    matInfo = $"shader={m.shader.name}  color=({mc.r:F2},{mc.g:F2},{mc.b:F2},{mc.a:F2})";
                }

                sb.AppendLine($"   高亮外壳：{(visual.gameObject.activeSelf ? "激活" : "❌未激活")}  " +
                              $"网格{(meshOk ? "正常" : "❌缺失")}  " +
                              $"渲染器{(mr2 != null ? (mr2.enabled ? "启用" : "❌禁用") : "❌无")}");
                sb.AppendLine($"      {matInfo}");
            }
        }

        // ---------- 检测重叠（竖直胶囊的准确算法）----------
        sb.AppendLine();
        sb.AppendLine("--------------------------------------------------------");
        sb.AppendLine("  重叠检测（竖直胶囊：轴向区间重叠 + 水平距离 < 半径和）");
        sb.AppendLine("--------------------------------------------------------");

        int overlapCount = 0;
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                var a = all[i].capsule;
                var b = all[j].capsule;

                Vector3 ca = a.transform.TransformPoint(a.center);
                Vector3 cb = b.transform.TransformPoint(b.center);

                Vector3 lsA = a.transform.lossyScale;
                Vector3 lsB = b.transform.lossyScale;

                float ra = a.radius * Mathf.Max(Mathf.Abs(lsA.x), Mathf.Abs(lsA.z));
                float rb = b.radius * Mathf.Max(Mathf.Abs(lsB.x), Mathf.Abs(lsB.z));

                float ha = a.height * Mathf.Abs(lsA.y);
                float hb = b.height * Mathf.Abs(lsB.y);

                // 轴向（Y）区间
                float aMin = ca.y - ha * 0.5f, aMax = ca.y + ha * 0.5f;
                float bMin = cb.y - hb * 0.5f, bMax = cb.y + hb * 0.5f;

                bool yOverlap = aMin < bMax && bMin < aMax;

                // 水平距离（XZ 平面）
                float dx = ca.x - cb.x;
                float dz = ca.z - cb.z;
                float hDist = Mathf.Sqrt(dx * dx + dz * dz);

                if (yOverlap && hDist < ra + rb)
                {
                    overlapCount++;
                    sb.AppendLine($"  ⚠️ {all[i].part} ↔ {all[j].part}");
                    sb.AppendLine($"       水平距离 {hDist:F3} < 半径和 {ra + rb:F3}   " +
                                  $"水平穿透 {ra + rb - hDist:F3} 米");
                    sb.AppendLine($"       Y 区间 [{aMin:F3},{aMax:F3}] 与 [{bMin:F3},{bMax:F3}] 重叠");
                }
            }
        }

        if (overlapCount == 0) sb.AppendLine("  ✅ 没有重叠");

        // ⚠️ 本方法是 static，不能访问实例字段 logToConsole。
        //    这里是用户按 F4 主动触发的诊断，直接输出即可。
        Debug.Log(sb.ToString());
    }

    /// <summary>兼容新旧输入系统的按键检测</summary>
    private static bool DebugKeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;

        // ⚠️ 新输入系统必须逐个列出按键，不能直接用 KeyCode
        switch (key)
        {
            case KeyCode.F4: return kb.f4Key.wasPressedThisFrame;
            case KeyCode.F5: return kb.f5Key.wasPressedThisFrame;
            default: return false;
        }
#else
        return Input.GetKeyDown(key);
#endif
    }

    private void OnDestroy()
    {
        PropInteractor.OnHeldItemsChanged -= OnPlayerHeldItemsChanged;
        UnsubscribeInteractor();
        if (highlightMaterial != null) Destroy(highlightMaterial);
    }

    // ==================== XRI 事件入口 ====================
    public void OnHoverEntered(HoverEnterEventArgs args)
    {
        isHovering = true;
        currentInteractor = args.interactorObject as XRBaseInteractor;
        SubscribeInteractor();
        UpdateHighlight();

        // 诊断日志：看 XRI 到底把悬停事件发给了哪个部位
        if (DebugShowAll)
        {
            string interactorName = "?";
            var comp = args.interactorObject as Component;
            if (comp != null) interactorName = comp.gameObject.name;

            if (logToConsole) Debug.Log($"[悬停] 收到事件的部位 = 「{part}」 骨骼={gameObject.name}  " +
                      $"交互器={interactorName}");
        }
    }

    public void OnHoverExited(HoverExitEventArgs args)
    {
        isHovering = false;
        UnsubscribeInteractor();
        UpdateHighlight();

        if (DebugShowAll)
            if (logToConsole) Debug.Log($"[悬停] 离开的部位 = 「{part}」 骨骼={gameObject.name}");
    }

    public void OnSelectEntered(SelectEnterEventArgs args)
    {
        var interactor = args.interactorObject as XRBaseInteractor;
        string heldTag = GetHeldItemTag(interactor);

        if (string.IsNullOrEmpty(heldTag)) HandleEmptyHand();
        else HandleItem(heldTag);
    }

    // ==================== 交互逻辑 ====================
    private void HandleEmptyHand()
    {
        ShowFeedback(hoverMessage, false);
        Debug.Log($"[伤者交互] 空手触碰「{part}」");
        OnEmptyHandInteract?.Invoke(this);
    }

    private void HandleItem(string itemTag)
    {
        if (IsItemValidForPart(itemTag))
        {
            ShowFeedback($"已用「{itemTag}」处理{part}", false);
            Debug.Log($"[伤者交互] 「{itemTag}」→ {part}  正确处理");
            OnItemApplied?.Invoke(this, itemTag);
        }
        else
        {
            ShowFeedback($"「{itemTag}」不适用于{part}", true);
            Debug.Log($"[伤者交互] 「{itemTag}」→ {part}  道具不对");
            OnWrongItem?.Invoke(this, itemTag);
        }
    }

    // ==================== 供外部调用：把道具用在某个部位上 ====================
    /// <summary>
    /// 当玩家把道具【松开】在某个身体部位上时，由道具调用这个方法。
    /// 判定正确/错误，并触发对应的事件（步骤系统靠事件推进）。
    ///
    /// 参数：
    ///   itemTag        道具标识（如 "Splint"）
    ///   displayName    道具中文名（用于提示文字）
    ///
    /// 返回：是否处理正确
    /// </summary>
    public bool TryApplyItem(string itemTag, string displayName = null)
    {
        if (string.IsNullOrEmpty(itemTag)) return false;

        string showName = string.IsNullOrEmpty(displayName) ? itemTag : displayName;

        if (IsItemValidForPart(itemTag))
        {
            ShowFeedback($"已用「{showName}」处理{part}", false);
            Debug.Log($"[道具应用] 「{showName}」→「{part}」   ✅ 正确");
            OnItemApplied?.Invoke(this, itemTag);
            return true;
        }

        ShowFeedback($"「{showName}」不适用于{part}", true);
        if (logToConsole) Debug.LogWarning($"[道具应用] 「{showName}」→「{part}」   ❌ 道具不适用");
        OnWrongItem?.Invoke(this, itemTag);
        return false;
    }

    /// <summary>这个部位是否已经被处理过了（供步骤系统判断）</summary>
    public bool IsTreated { get; private set; }

    /// <summary>标记为已处理（供步骤系统在道具正确应用后调用）</summary>
    public void MarkTreated()
    {
        IsTreated = true;
    }

    /// <summary>道具与部位的匹配规则表</summary>
    public bool IsItemValidForPart(string itemTag)
    {
        if (string.IsNullOrEmpty(itemTag)) return false;

        switch (part)
        {
            // 腿（大腿 + 小腿）：夹板固定、冰敷、绷带加压包扎
            case BodyPart.左大腿:
            case BodyPart.左小腿:
            case BodyPart.右大腿:
            case BodyPart.右小腿:
                return itemTag == "Splint"
                    || itemTag == "IcePack"
                    || itemTag == "Bandage";

            // 手臂（上臂 + 前臂）：夹板、绷带
            case BodyPart.左上臂:
            case BodyPart.左前臂:
            case BodyPart.右上臂:
            case BodyPart.右前臂:
                return itemTag == "Splint" || itemTag == "Bandage";

            // 头：绷带（外伤）
            case BodyPart.头部:
                return itemTag == "Bandage";

            // 躯干：保温毯、隔热垫
            case BodyPart.躯干:
                return itemTag == "Blanket" || itemTag == "Mat";

            default:
                return false;
        }
    }

    // ==================== 获取玩家手上的道具 ====================
    private string GetHeldItemTag(XRBaseInteractor interactor)
    {
        if (interactor == null) return null;

        var selected = interactor.interactablesSelected;
        if (selected == null || selected.Count == 0) return null;

        foreach (var sel in selected)
        {
            var comp = sel as Component;
            if (comp == null) continue;

            var itemTag = comp.GetComponent<ItemTag>();
            if (itemTag != null && !string.IsNullOrEmpty(itemTag.tagName))
                return itemTag.tagName;
        }
        return null;
    }

    // ==================== 交互器事件订阅 ====================
    private void SubscribeInteractor()
    {
        if (currentInteractor == null) return;
        currentInteractor.selectEntered.AddListener(OnInteractorSelectEntered);
        currentInteractor.selectExited.AddListener(OnInteractorSelectExited);
    }

    private void UnsubscribeInteractor()
    {
        if (currentInteractor == null) return;
        currentInteractor.selectEntered.RemoveListener(OnInteractorSelectEntered);
        currentInteractor.selectExited.RemoveListener(OnInteractorSelectExited);
        currentInteractor = null;
    }

    private void OnInteractorSelectEntered(SelectEnterEventArgs args) => UpdateHighlight();
    private void OnInteractorSelectExited(SelectExitEventArgs args) => UpdateHighlight();

    // ==================== 视觉反馈 ====================
    /// <summary>
    /// 决定本部位现在显示什么颜色。
    ///
    /// 规则（按优先级）：
    ///   · 拿着道具 + 射线正指着本部位 + 适用 → 亮绿（aimedValidColor）
    ///   · 拿着道具 + 射线正指着本部位 + 不适用 → 亮红（aimedWrongColor）
    ///   · 拿着适用道具但没指着                → 淡绿提示（holdValidHintColor）
    ///   · 拿着不适用道具但没指着              → 淡红提示（holdWrongHintColor）
    ///   · 空手指着/悬停                       → 橙色（emptyHandColor）
    ///   · 其他                                → 完全透明
    ///
    /// ⚠️ "指着"用的是自己打的射线（PropInteractor.GetAimedBodyPart），
    ///    不依赖 XRI 的悬停事件 —— 那个事件在"手上有道具"时不可靠。
    /// </summary>
    private void UpdateHighlight()
    {
        if (highlightRenderer == null) return;

        Color c = new Color(0f, 0f, 0f, 0f);          // 默认透明

        // ---------- 拿着道具 + 射线正指着本部位 ----------
        if (AimedByAnyHand(out string aimedTag))
        {
            c = IsItemValidForPart(aimedTag) ? aimedValidColor : aimedWrongColor;
        }
        // ---------- 拿着道具但没指着 ----------
        else if (PlayerHoldsAnyItem(out string anyTag))
        {
            if (IsItemValidForPart(anyTag) && highlightWhenHoldingValidItem)
                c = holdValidHintColor;
            else
                c = holdWrongHintColor;
        }
        // ---------- 空手指着 / XRI 悬停 ----------
        else if (isHovering || hoveredByAnyHand())
        {
            c = emptyHandColor;
        }
        else if (DebugShowAll)
        {
            c = debugColor;
        }

        if (highlightMaterial == null) return;

        // 按属性是否存在逐个设置（自定义 shader 用 _BaseColor，URP 标准用 _BaseColor/_Color）
        if (highlightMaterial.HasProperty(BaseColorId))
            highlightMaterial.SetColor(BaseColorId, c);

        if (highlightMaterial.HasProperty(ColorId))
            highlightMaterial.SetColor(ColorId, c);
    }

    // ==================== 直接接触（Poke）：拿着道具碰到部位就算使用 ====================
    private float lastPokeTime = -999f;

    // ==================== 高亮辅助查询 ====================

    /// <summary>
    /// 有没有任意一只手的射线正指着本部位，且那只手拿着道具。
    /// 通过它拿到"那只手拿的道具标识"，用来决定亮绿还是亮红。
    /// </summary>
    private bool AimedByAnyHand(out string heldTag)
    {
        heldTag = null;

        foreach (var pi in PropInteractor.Active)
        {
            if (pi == null) continue;
            if (pi.GetAimedBodyPart() != this) continue;

            var item = pi.HeldItem;
            if (item == null) continue;

            heldTag = item.ItemId;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 有没有任意一只手的射线正指着本部位（不限手上有没有道具）。
    /// 空手时用它替代不可靠的 XRI 悬停事件。
    /// </summary>
    private bool hoveredByAnyHand()
    {
        foreach (var pi in PropInteractor.Active)
        {
            if (pi == null) continue;
            if (pi.GetAimedBodyPart() == this) return true;
        }
        return false;
    }

    /// <summary>
    /// 有碰撞体进入本部位的触发器。
    ///
    /// 用途：真实世界里急救就是"把绷带缠上去"——直接接触。VR 里对应 Poke。
    /// 触发条件：
    ///   · 进来的物体上挂着 XRGrabInteractable（也就是能被抓的道具）
    ///   · 那个道具当前正被某只手抓着
    ///   · 道具适用于本部位
    ///
    /// ⚠️ 本部位的胶囊碰撞体需要勾上 Is Trigger 才会走到这里。
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!enablePokeUse) return;
        if (other == null) return;
        if (Time.time - lastPokeTime < pokeCooldown) return;

        // 进来的东西是不是"被抓着的道具"
        var item = other.GetComponentInParent<ItemPhysicsSetup>();
        if (item == null) return;
        if (!item.IsGrabbed) return;          // 没被抓住的不算"玩家拿它碰上来"

        string tag = item.ItemId;
        if (string.IsNullOrEmpty(tag)) return;

        // 用错道具碰到 → 只提示，不判定为"使用"（避免玩家乱碰刷错）
        if (!IsItemValidForPart(tag))
        {
            if (logToConsole) Debug.Log($"[Poke] 「{item.ItemDisplayName}」碰到「{part}」，但道具不适用 → 不触发");
            return;
        }

        lastPokeTime = Time.time;
        if (logToConsole) Debug.Log($"[Poke] 「{item.ItemDisplayName}」碰到「{part}」 → 直接算使用 ✅");
        TryApplyItem(tag, item.ItemDisplayName);
    }

    // ==================== 供子弹/射线等外部系统复用 ====================

    private void ShowFeedback(string msg, bool isWarning)
    {
        if (string.IsNullOrEmpty(msg)) return;

        var hint = FindAnyObjectByType<HintPanel>();
        if (hint != null)
        {
            if (isWarning) hint.ShowWarning(msg);
            else hint.ShowHint(msg);
        }

        if (!logToConsole) return;
        if (isWarning) Debug.LogWarning($"[伤者反馈] {msg}");
        else Debug.Log($"[伤者反馈] {msg}");
    }

    // ==================== 生成胶囊高亮外壳 ====================
    private void EnsureHighlightVisual()
    {
        if (!createHighlightVisual) return;

        // 已经生成过就删掉重建
        // （之前的版本会"复用"，但残留对象的材质可能是 null 或旧 shader，
        //   导致胶囊不显示 —— 直接重建最可靠）
        var existing = transform.Find("HighlightVisual");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        highlightRenderer = null;
        highlightMaterial = null;

        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        if (capsule == null) return;

        var go = new GameObject("HighlightVisual");
        go.transform.SetParent(transform, false);

        // 用程序化生成的胶囊网格
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = CapsuleMeshFactory.Get();

        highlightRenderer = go.AddComponent<MeshRenderer>();
        highlightRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        highlightRenderer.receiveShadows = false;

        // ---------- 位置和缩放 ----------
        // 单位胶囊：总高 1.0，半径 0.5，圆柱段半高 0.25
        //
        // CapsuleCollider 的 height 是"含两端半球的总高"，
        // 圆柱段半高 = (height - 2*radius) / 2
        //
        // 单位胶囊的圆柱段半高是 0.25，所以 Y 缩放 = 圆柱段半高 / 0.25
        float r = Mathf.Max(capsule.radius, 0.0001f);
        float cylinderHalf = Mathf.Max((capsule.height - 2f * r) * 0.5f, 0.0001f);
        float yScale = cylinderHalf / 0.25f;

        go.transform.localPosition = capsule.center;

        // 胶囊的轴向：Unity 的 CapsuleCollider 有 Direction 选项
        switch (capsule.direction)
        {
            case 0:   // X 轴
                go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                go.transform.localScale = new Vector3(r / 0.5f, yScale, r / 0.5f);
                break;

            case 2:   // Z 轴
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(r / 0.5f, yScale, r / 0.5f);
                break;

            default:  // Y 轴（默认）
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3(r / 0.5f, yScale, r / 0.5f);
                break;
        }

        // ---------- 材质：X 光穿透胶囊 ----------
        // 用自定义 shader（SnowLineVR/XRayCapsule）
        // 它硬编码了 ZTest Always + 透明混合，所以胶囊会穿透角色蒙皮显示
        var xrayShader = Shader.Find("SnowLineVR/XRayCapsule");

        if (xrayShader != null)
        {
            highlightMaterial = new Material(xrayShader);
            highlightMaterial.name = "BodyPartHighlight_XRay_Runtime";

            var startColor = new Color(0f, 0f, 0f, 0f);
            if (highlightMaterial.HasProperty(BaseColorId))
                highlightMaterial.SetColor(BaseColorId, startColor);

            // shader 里已经硬编码 ZTest Always（穿透）+ 透明混合，
            // 这里只需要保证渲染队列在最后
            highlightMaterial.renderQueue = 4000;
        }
        else
        {
            // 兜底：找不到自定义 shader 就用 URP Unlit
            Debug.LogWarning("[身体部位] 找不到 shader SnowLineVR/XRayCapsule，改用 URP Unlit（无穿透效果）");

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            highlightMaterial = new Material(shader);
            highlightMaterial.name = "BodyPartHighlight_Runtime";

            var startColor = new Color(0f, 0f, 0f, 0f);
            highlightMaterial.color = startColor;
            if (highlightMaterial.HasProperty(BaseColorId)) highlightMaterial.SetColor(BaseColorId, startColor);

            if (highlightMaterial.HasProperty("_Surface")) highlightMaterial.SetFloat("_Surface", 1f);
            if (highlightMaterial.HasProperty("_Blend")) highlightMaterial.SetFloat("_Blend", 0f);
            if (highlightMaterial.HasProperty("_AlphaClip")) highlightMaterial.SetFloat("_AlphaClip", 0f);
            if (highlightMaterial.HasProperty("_SrcBlend"))
                highlightMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (highlightMaterial.HasProperty("_DstBlend"))
                highlightMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (highlightMaterial.HasProperty("_ZWrite")) highlightMaterial.SetFloat("_ZWrite", 0f);
            if (highlightMaterial.HasProperty("_Cull")) highlightMaterial.SetFloat("_Cull", 0f);

            highlightMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            highlightMaterial.DisableKeyword("_ALPHATEST_ON");
            highlightMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        highlightRenderer.sharedMaterial = highlightMaterial;
    }
}

