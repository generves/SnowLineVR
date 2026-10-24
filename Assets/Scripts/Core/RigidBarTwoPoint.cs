using UnityEngine;

/// <summary>
/// 刚性长条两点对齐 —— 长条沿「A → B」方向摆放，长度固定、永不拉伸。
///
/// 典型用途：夹板绑在大腿(a1)和小腿(b1)上，夹板两端 a2 / b2 尽量贴住这两点。
///
/// ===================== 层级要求（很重要）=====================
///
///   character
///     └─ mixamorig:LeftUpLeg（大腿骨骼）
///          └─ a1              ← 目标点 A（空物体）
///     └─ mixamorig:LeftLeg（小腿骨骼）
///          └─ b1              ← 目标点 B（空物体）
///
///   Splint                     ← ⭐ 本脚本挂在【夹板】上
///     ├─ a2                    ← 夹板端点 A（空物体，夹板的一端）
///     └─ b2                    ← 夹板端点 B（空物体，夹板的另一端）
///
/// ⚠️ a2 / b2 必须是【夹板的子物体】；
///    脚本必须挂在【夹板】上，不是挂在 a2 / b2 上。
///
/// ===================== 原理 =====================
///
///   长条是刚性的 → 长度 L 固定；而 a1 到 b1 的距离 d 会随膝盖弯曲变化。
///   所以「两端都精确贴住」只在 d == L 时才成立，其余情况必须折中。
///
///   三种折中方式（Align Mode）：
///     对称居中   ：长条中心放在 a1、b1 的中点 → 两端各承担一半误差。
///                  当 d > L（长条太短）时，a2、b2 都会落在 a1、b1 之间。
///     以A端为准  ：a2 始终贴在 a1 上，长条朝 b1 方向延伸。
///     以B端为准  ：b2 始终贴在 b1 上。
///
///   ⚠️ 本脚本不做「手工算角度」，
///      用的是 Quaternion.FromToRotation：直接由「当前方向 → 目标方向」求旋转，
///      不会出现大于 180 度转错方向的问题。
/// </summary>
[DefaultExecutionOrder(10000)]   // 在动画、地面对齐之后执行
public class RigidBarTwoPoint : MonoBehaviour
{
    public enum AlignMode
    {
        对称居中,
        以A端为准,
        以B端为准,
    }

    [Header("长条的两端（必须是【本物体】的子物体）")]
    [Tooltip("长条的 A 端参考点。是一个空物体，摆在长条的一端")]
    public Transform endA;

    [Tooltip("长条的 B 端参考点。是另一个空物体，摆在长条的另一端")]
    public Transform endB;

    [Header("要对齐的两个目标点")]
    [Tooltip("A 端要对齐的目标。比如大腿骨骼下的空物体 a1")]
    public Transform targetA;

    [Tooltip("B 端要对齐的目标。比如小腿骨骼下的空物体 b1")]
    public Transform targetB;

    [Header("对齐方式")]
    [Tooltip("对称居中 = 长条中心放在 a1、b1 的中点，两端各承担一半误差（推荐）\n" +
             "以A端为准 = a2 死贴 a1\n" +
             "以B端为准 = b2 死贴 b1")]
    public AlignMode mode = AlignMode.对称居中;

    [Header("启用控制")]
    [Tooltip("必须勾上脚本才会生效。\n" +
             "长条还在医疗箱里时建议先关闭，用上之后再在 Inspector 里勾上")]
    public bool active = true;

    [Header("物理")]
    [Tooltip("勾上 = 一挂上组件就立刻把长条的 Rigidbody 锁成 Kinematic、关掉重力。\n" +
             "⚠️ 非常重要：挂了这个组件，长条就是「由脚本摆放」的物体，\n" +
             "   不该再受重力/碰撞影响。不勾的话，引用没填全会导致它被物理甩飞。")]
    public bool lockPhysicsAlways = true;

    [Tooltip("勾上 = 每帧再把 Rigidbody 锁一次（防止别的脚本改回去）")]
    public bool forceKinematicRigidbody = true;

    [Header("平滑（可选）")]
    [Tooltip("勾上后位置会插值过渡。会有一点延迟，一般不用勾")]
    public bool useSmoothing = false;

    [Tooltip("位置插值速度，越大越跟手")]
    [Range(0.01f, 1f)]
    public float positionSmooth = 0.35f;

    [Header("安全限制")]
    [Tooltip("勾上 = 限制单帧最大位移，防止配置错误时夹板飞出地图。\n" +
             "正常情况第一帧就贴合，不会被限制卡住。强烈建议保持勾选")]
    public bool clampShiftPerFrame = true;

    [Tooltip("单帧最大位移（米）。正常贴合需要的位移远小于这个值")]
    [Range(0.05f, 5f)]
    public float maxShiftPerFrame = 0.5f;

    [Header("调试")]
    [Tooltip("在 Console 打印当前偏差，每 0.5 秒一条")]
    public bool logToConsole = false;

    [Tooltip("启动时自动检查配置并报告问题（强烈建议保持勾选）")]
    public bool logSetupProblems = true;

    private float logTimer;
    private bool hasDriven;
    private bool warnedBadEndpoints;
    private bool warnedBigShift;

    /// <summary>端点是不是都在本物体下面（跟着本物体一起动）</summary>
    private bool AreEndpointsChildren()
    {
        if (endA == null || endB == null) return false;
        if (endA == transform || endB == transform) return false;
        return endA.IsChildOf(transform) && endB.IsChildOf(transform);
    }

    private void Awake()
    {
        // ⚠️ 一挂上就锁死物理。
        //    这一步【不看 Active、不看引用是否填全】—— 因为只要挂了这个组件，
        //    长条就是「由脚本摆放」的物体，不该再被重力/碰撞影响。
        //    否则引用没填全时脚本会提前 return，Rigidbody 仍是动态的，
        //    结果就是被重力/碰撞甩飞（正是 Splint (1) 飞走的原因）。
        if (lockPhysicsAlways) LockPhysics();
    }

    private void Start()
    {
        // 自动补齐端点：如果没指定，就在自己的子物体里找名字叫 a2 / b2 的
        AutoFindEndpoints();

        if (logSetupProblems) Diagnose();
    }

    /// <summary>把刚体锁成 Kinematic 并清零速度</summary>
    private void LockPhysics()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb == null) return;

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>在自己和子物体里找名字匹配的 Transform</summary>
    private void AutoFindEndpoints()
    {
        if (endA == null) endA = FindChildByName("a2");
        if (endB == null) endB = FindChildByName("b2");
    }

    private Transform FindChildByName(string childName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            if (t == transform) continue;          // 跳过自己
            if (t.name.Trim() == childName) return t;
        }
        return null;
    }

    /// <summary>
    /// 检查配置并报告问题。
    /// 在 Inspector 里点组件右上角 ⋮ →「检查引用与状态」也能随时调用。
    /// </summary>
    [ContextMenu("检查引用与状态")]
    public void Diagnose()
    {
        bool ok = true;

        // ---------- 1. 启用状态 ----------
        if (!active)
        {
            Debug.LogWarning($"[长条对齐] 「{name}」没有生效：Active 没勾上。" +
                             $"请在 Inspector 里勾选 Active", this);
            ok = false;
        }

        // ---------- 2. 四个引用 ----------
        if (endA == null) { Debug.LogError($"[长条对齐] 「{name}」的 End A 没指定", this); ok = false; }
        if (endB == null) { Debug.LogError($"[长条对齐] 「{name}」的 End B 没指定", this); ok = false; }
        if (targetA == null) { Debug.LogError($"[长条对齐] 「{name}」的 Target A 没指定（大腿下的 a1）", this); ok = false; }
        if (targetB == null) { Debug.LogError($"[长条对齐] 「{name}」的 Target B 没指定（小腿下的 b1）", this); ok = false; }

        if (!ok)
        {
            Debug.LogWarning($"[长条对齐] 「{name}」配置不完整，脚本不会动作", this);
            return;
        }

        // ---------- 3. 层级检查 ----------
        if (endA == transform || endB == transform)
        {
            Debug.LogError($"[长条对齐] 「{name}」：End A / End B 不能是本物体自己。" +
                           $"请把它们设成摆在本物体两端的【空子物体】", this);
            ok = false;
        }

        if (!endA.IsChildOf(transform) || !endB.IsChildOf(transform))
        {
            Debug.LogError($"[长条对齐] 「{name}」：End A / End B 【不是本物体的子物体】。\n" +
                           $"  端点必须是夹板的子物体，否则脚本旋转/移动夹板时端点不跟着动，\n" +
                           $"  计算出来的长度和中心全是错的。\n" +
                           $"  修法：在 Hierarchy 里把端点物体【拖到「{name}」上面】变成它的子物体。\n" +
                           $"  当前 End A = {(endA != null ? endA.name : "null")}（父物体 = {(endA != null && endA.parent != null ? endA.parent.name : "无")}）\n" +
                           $"       End B = {(endB != null ? endB.name : "null")}（父物体 = {(endB != null && endB.parent != null ? endB.parent.name : "无")}）",
                           this);
            ok = false;
        }

        if (transform.IsChildOf(endA) || transform.IsChildOf(endB))
        {
            Debug.LogError($"[长条对齐] 「{name}」：脚本挂错物体了。" +
                           $"它应该挂在【长条】上，而不是挂在端点 a2 / b2 上", this);
            ok = false;
        }

        // ---------- 4. 物理冲突 ----------
        var rb = GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            if (forceKinematicRigidbody)
            {
                Debug.Log($"[长条对齐] 「{name}」有非 Kinematic 的 Rigidbody，" +
                          $"本脚本会强制改成 Kinematic 以避免抢 transform", this);
            }
            else
            {
                Debug.LogWarning($"[长条对齐] 「{name}」有非 Kinematic 的 Rigidbody 。" +
                                 $"物理会和本脚本抢 transform（表现为不动或抖动）。" +
                                 $"建议把 Rigidbody 的 Is Kinematic 勾上", this);
            }
        }

        // ---------- 5. 抓取冲突 ----------
        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab != null && grab.enabled)
        {
            Debug.LogWarning($"[长条对齐] 「{name}」上还有启用的 XRGrabInteractable 。" +
                             $"抓取时它会和本脚本抢 transform。绑好之后建议禁用它", this);
        }

        // ---------- 6. 量一下几何关系 ----------
        float barLength = Vector3.Distance(endB.position, endA.position);
        float d = Vector3.Distance(targetB.position, targetA.position);

        string advice;
        if (barLength < 0.001f)
            advice = "❌ 长条长度是 0 —— a2 和 b2 摆在同一位置了，请分别放到夹板两端";
        else if (Mathf.Abs(d - barLength) < 0.01f)
            advice = "✅ 长度几乎相等，两端都能贴合";
        else if (d > barLength)
            advice = $"⚠️ 夹板太短（差 {d - barLength:F3} 米）→ 对称居中时两端都会落在 a1、b1 之间";
        else
            advice = $"⚠️ 夹板太长（多 {barLength - d:F3} 米）→ 对称居中时两端会顶出 a1、b1 之外";

        Debug.Log($"[长条对齐] 「{name}」检查结果：\n" +
                  $"  夹板长度 L = {barLength:F3} 米\n" +
                  $"  两目标距离 d = {d:F3} 米\n" +
                  $"  {advice}\n" +
                  $"  当前模式 = {mode}", this);
    }

    private void LateUpdate()
    {
        // ⚠️ 先锁物理，再做任何判断。
        //    这样即使 Active 没勾、引用没填全，长条也不会被重力/碰撞甩飞。
        if (forceKinematicRigidbody) LockPhysics();

        if (!active) return;

        if (endA == null || endB == null || targetA == null || targetB == null)
            return;

        // ⚠️⚠️ 硬性要求：端点必须是本物体的子物体。
        //
        //   原因：本脚本是【增量式】修正 ——
        //     每次算出「当前中点 → 目标中点」的位移再移动自己，
        //     下一帧靠端点跟着动、重新计算，位移会迅速收敛到 0。
        //
        //   如果端点不是子物体，它不会跟着动 →
        //   curMid 永远是同一个值 → 每帧算出同样的位移 →
        //   夹板每帧固定移动那段距离 → 一进场就飞出地图。
        //
        //   所以这里直接不驱动，只报错。
        if (!AreEndpointsChildren())
        {
            if (!warnedBadEndpoints)
            {
                warnedBadEndpoints = true;
                Debug.LogError(
                    $"[长条对齐] 「{name}」停止驱动：End A / End B 不是本物体的子物体。\n" +
                    $"  End A = {(endA != null ? endA.name : "null")}" +
                    $"（父物体 = {(endA != null && endA.parent != null ? endA.parent.name : "无")}）\n" +
                    $"  End B = {(endB != null ? endB.name : "null")}" +
                    $"（父物体 = {(endB != null && endB.parent != null ? endB.parent.name : "无")}）\n" +
                    $"  → 请在 Hierarchy 里把这两个端点【拖到「{name}」上面】变成它的子物体。\n" +
                    $"  → 不修的话夹板会被每帧推同样一段距离，直接飞出地图。",
                    this);
            }
            return;
        }

        Vector3 pA = targetA.position;
        Vector3 pB = targetB.position;

        Vector3 axis = pB - pA;
        float d = axis.magnitude;

        if (d < 0.0001f) return;      // 两点重合，方向没有意义
        axis /= d;                    // 单位方向

        // ---------- 1. 旋转：让 endA → endB 与 a1 → b1 平行 ----------
        Vector3 curAxis = endB.position - endA.position;
        if (curAxis.sqrMagnitude > 1e-8f)
        {
            Quaternion delta = Quaternion.FromToRotation(curAxis.normalized, axis);
            transform.rotation = delta * transform.rotation;
        }

        // ---------- 2. 平移：把长条放到合适位置 ----------
        // 长条长度从两个端点算出来，不依赖 pivot 在哪里
        float barLength = (endB.position - endA.position).magnitude;

        Vector3 wantMid;
        switch (mode)
        {
            case AlignMode.以A端为准:
                // a2 贴在 a1 → 中心 = a1 + 方向 × 半长
                wantMid = pA + axis * (barLength * 0.5f);
                break;

            case AlignMode.以B端为准:
                // b2 贴在 b1 → 中心 = b1 - 方向 × 半长
                wantMid = pB - axis * (barLength * 0.5f);
                break;

            default:
                // 对称居中 → 中心 = 两点中点
                wantMid = (pA + pB) * 0.5f;
                break;
        }

        Vector3 curMid = (endA.position + endB.position) * 0.5f;
        Vector3 shift = wantMid - curMid;

        // ---------- 兜底：限制单帧位移 ----------
        // 正常情况（端点是自己子物体）第一帧就会贴合，位移很小。
        // 如果出现大位移，说明配置有问题 —— 这里钳住它，
        // 让夹板宁可"慢慢挪过去"也绝不飞出地图。
        if (clampShiftPerFrame)
        {
            float mag = shift.magnitude;
            if (mag > maxShiftPerFrame)
            {
                shift = shift / mag * maxShiftPerFrame;

                if (!warnedBigShift)
                {
                    warnedBigShift = true;
                    Debug.LogWarning($"[长条对齐] 「{name}」单帧位移被限制为 {maxShiftPerFrame:F2} 米" +
                                     $"（原本需要 {mag:F2} 米）。请检查端点/目标点是不是摆错了", this);
                }
            }
        }

        if (useSmoothing)
            transform.position += shift * positionSmooth;
        else
            transform.position += shift;

        if (!hasDriven)
        {
            hasDriven = true;
            if (logSetupProblems)
                Debug.Log($"[长条对齐] 「{name}」开始驱动了（模式 = {mode}）", this);
        }

        // ---------- 日志（限流，每 0.5 秒一条）----------
        if (logToConsole)
        {
            logTimer += Time.deltaTime;
            if (logTimer >= 0.5f)
            {
                logTimer = 0f;

                float devA = Vector3.Distance(endA.position, pA);
                float devB = Vector3.Distance(endB.position, pB);

                Debug.Log($"[长条对齐] d={d:F3}  L={barLength:F3}  差值={d - barLength:+0.000;-0.000;0.000}  " +
                          $"a2 偏差={devA:F3}  b2 偏差={devB:F3}", this);
            }
        }
    }
}
