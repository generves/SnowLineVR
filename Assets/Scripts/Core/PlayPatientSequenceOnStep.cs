using UnityEngine;

/// <summary>
/// 在指定步骤开始时，让 Patient Sequence Player 播放另一组动画序列。
///
/// ===================== 用途 =====================
///
///   你原本用 PatientSequencePlayer 播「开场」那几步（4 → 6 → 3 → 0）。
///   但到第 5 步「交接与病情汇报」时，需要换成另一组：
///
///       左腿受伤状态往左前方走   (State 9)
///       左腿受伤直走             (State 11)
///       躺到担架上               (State 16)
///       平躺轻微呼吸1            (State 14，循环)
///
///   本脚本只做一件事：第 5 步开始时，把这组 steps 塞给序列播放器并重新开播。
///
/// ===================== 用法 =====================
///
///   ① 挂在 character 上（有 PatientSequencePlayer 的那个物体）
///   ② 拖引用：
///        Player          ← 本物体的 Patient Sequence Player
///        Step Controller ← 留空自动找
///   ③ 填 Trigger At Step Number = 5
///   ④ 在【Steps】里填 4 项（下面已经预填好）
///   ⑤ 保持 Player 上的 Loop Last Step = ☑，最后一项才会一直循环
///
/// ===================== 注意 =====================
///
///   ⚠️ 必须给 character 加一个 AnimationTransitionAnchor 组件，并把它的
///      【Active 保持关闭】。本脚本会在触发时自动把它打开。
///
///      为什么需要它：
///        `AC_Patient` 里每条 transition 都是 0.2 秒 cross-fade，
///        而每段 clip 的髋骨原点各不相同 → 混合期间髋部被拉回去
///        → 表现为「每播完一个动画就返回起点」。
///        AnimationTransitionAnchor 会在过渡期间把角色焊在原地，
///        过渡一结束就松手，新动画从原地继续播。
///
///      为什么默认关闭：
///        开场那几条动画（尝试走失败 / 捂腿呻吟 / 招手 / 待机）
///        保持原来的表现，不受影响。
///
///   ⚠️ 不要用 RootMotionTransfer / PatientStretcherSequence
///      —— 你的动画是原地踏步型的（每帧位移只有 0.001 米），
///         根运动累积不适用；PatientStretcherSequence 还会把角色搬到空中。
/// </summary>
public class PlayPatientSequenceOnStep : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("要驱动的序列播放器。留空自动在本物体上找")]
    public PatientSequencePlayer player;

    [Tooltip("步骤控制器。留空自动查找")]
    public StepController stepController;

    [Tooltip("过渡锚定脚本。留空自动在本物体上找。\n" +
             "它负责在动画过渡的 0.2 秒里把角色焊在原地，避免『每播完一段就跳回原点』")]
    public AnimationTransitionAnchor transitionAnchor;

    [Header("触发条件")]
    [Tooltip("第几步【开始时】播放这组序列（对应 FirstAidStep 里的 stepIndex）")]
    public int triggerAtStepNumber = 5;

    [Tooltip("只触发一次")]
    public bool triggerOnce = true;

    [Tooltip("开始前等待几秒")]
    public float startDelay = 0f;

    [Tooltip("触发时自动打开过渡锚定，让角色在切换动画时不再跳回原点")]
    public bool enableRootMotionWhenTriggered = true;

    [Header("要播放的序列")]
    [Tooltip("按顺序播放。最后一项建议勾 Loop，让它一直循环")]
    public PatientSequencePlayer.Step[] steps = new[]
    {
        new PatientSequencePlayer.Step
        {
            state = 9, note = "往左前方走", loop = false, trimEnd = 0f, extraHold = 0f
        },
        new PatientSequencePlayer.Step
        {
            state = 11, note = "直走", loop = false, trimEnd = 0f, extraHold = 0f
        },
        new PatientSequencePlayer.Step
        {
            state = 16, note = "躺到担架上", loop = false, trimEnd = 0f, extraHold = 0f
        },
        new PatientSequencePlayer.Step
        {
            state = 14, note = "平躺轻微呼吸1（循环）", loop = true, trimEnd = 0f, extraHold = 0f
        },
    };

    [Header("调试")]
    [Tooltip("勾上后，从 Inspector 右键菜单点【立即播放这组序列】时，\n" +
             "会先用正确动作把前面的步骤自动做完，再开始播序列 —— 便于快速测试。\n" +
             "⚠️ 注意：这样自动完成的步骤会按『做对了』记入训练报告和评分。")]
    public bool completePrecedingSteps = true;

    [Tooltip("打印日志")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private bool triggered;
    private bool sequenceStarted;
    private bool debugCompleting;

    private const float StepAdvanceTimeout = 3f;

    private void Awake()
    {
        if (player == null) player = GetComponent<PatientSequencePlayer>();
        if (transitionAnchor == null) transitionAnchor = GetComponent<AnimationTransitionAnchor>();
    }

    private void Start()
    {
        if (stepController == null) stepController = FindAnyObjectByType<StepController>();

        if (player == null)
        {
            Debug.LogError($"[序列触发] 「{name}」找不到 PatientSequencePlayer，脚本无效", this);
            return;
        }

        if (stepController == null)
        {
            Debug.LogWarning($"[序列触发] 「{name}」找不到 StepController，无法按步骤触发", this);
            return;
        }

        stepController.OnStepStarted += HandleStepStarted;
    }

    private void OnDestroy()
    {
        if (stepController != null)
            stepController.OnStepStarted -= HandleStepStarted;
    }

    private void HandleStepStarted(FirstAidStep step)
    {
        if (step == null) return;
        if (triggered && triggerOnce) return;
        if (step.stepIndex != triggerAtStepNumber) return;
        if (sequenceStarted) return;

        // 保留原来的 Start Delay 行为
        if (startDelay > 0f) StartCoroutine(PlayAfterDelay());
        else PlayNow();
    }

    private System.Collections.IEnumerator PlayAfterDelay()
    {
        yield return new WaitForSeconds(startDelay);
        PlayNow();
    }

    /// <summary>
    /// 右键菜单：立即播放这组序列。
    ///
    /// 如果勾了 Complete Preceding Steps，会先自动完成前面的步骤
    /// （用每一步要求的正确动作上报，等同"做对了"），
    /// 让第 triggerAtStepNumber 步成为当前步骤，然后才开始播序列。
    /// </summary>
    [ContextMenu("立即播放这组序列")]
    public void Trigger()
    {
        if (debugCompleting) return;

        if (player == null)
        {
            Debug.LogError($"[序列触发] 「{name}」没有 Player 引用", this);
            return;
        }

        if (steps == null || steps.Length == 0)
        {
            Debug.LogError($"[序列触发] 「{name}」的 Steps 是空的", this);
            return;
        }

        StartCoroutine(TriggerRoutine());
    }

    private System.Collections.IEnumerator TriggerRoutine()
    {
        if (completePrecedingSteps && stepController != null)
        {
            debugCompleting = true;

            if (logToConsole)
                Debug.Log($"[序列触发] 调试：先把前 {triggerAtStepNumber - 1} 步自动做完", this);

            yield return StartCoroutine(CompleteStepsUpTo(triggerAtStepNumber));

            debugCompleting = false;
        }

        // 步骤推进到第 5 步时，HandleStepStarted 已经播过了
        if (!sequenceStarted)
        {
            if (startDelay > 0f) yield return new WaitForSeconds(startDelay);
            PlayNow();
        }
    }

    /// <summary>
    /// 用每一步要求的动作 ID 上报，把当前步骤推进到 targetStepNumber。
    /// 走的是正常流程（正确动作 → 正常推进），所以动画和提示都正常。
    /// </summary>
    private System.Collections.IEnumerator CompleteStepsUpTo(int targetStepNumber)
    {
        if (stepController == null) yield break;

        if (stepController.CurrentStepNumber <= 0)
        {
            Debug.LogWarning($"[序列触发] 训练还没开始（当前没有进行中的步骤），" +
                             $"请先选一个事件进入关卡再点这个菜单", this);
            yield break;
        }

        float totalGuard = 0f;

        while (!stepController.IsFinished
               && stepController.CurrentStepNumber < targetStepNumber
               && totalGuard < 30f)
        {
            totalGuard += Time.deltaTime;

            var step = stepController.CurrentStep;
            if (step == null) yield break;

            int before = stepController.CurrentStepNumber;

            // 用这一步要求的动作全都上报一遍（多动作步骤会依次匹配）
            foreach (var actionId in SplitActions(step.requiredActionId))
                stepController.ReportAction(actionId);

            if (logToConsole)
                Debug.Log($"[序列触发] 自动完成第 {before} 步「{step.stepName}」" +
                          $"（动作 {step.requiredActionId}）", this);

            // 等这一步真正推进
            float wait = 0f;
            while (stepController.CurrentStepNumber == before
                   && !stepController.IsFinished
                   && wait < StepAdvanceTimeout)
            {
                wait += Time.deltaTime;
                yield return null;
            }

            if (!stepController.IsFinished && stepController.CurrentStepNumber == before)
            {
                Debug.LogWarning($"[序列触发] 第 {before} 步没有推进" +
                                 $"（需要动作 '{step.requiredActionId}'），自动完成中止", this);
                yield break;
            }

            // 让 StepController 的协程清理完状态
            yield return null;
        }

        if (logToConsole)
            Debug.Log($"[序列触发] 自动完成结束，当前是第 {stepController.CurrentStepNumber} 步", this);
    }

    /// <summary>把 requiredActionId 拆成动作列表（支持中英文逗号、分号）</summary>
    private static string[] SplitActions(string required)
    {
        if (string.IsNullOrEmpty(required)) return new string[0];

        return required.Split(new[] { ',', '，', ';', '；' },
                              System.StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>真正开播（只执行一次）</summary>
    private void PlayNow()
    {
        if (sequenceStarted) return;

        if (player == null)
        {
            Debug.LogError($"[序列触发] 「{name}」没有 Player 引用", this);
            return;
        }

        if (steps == null || steps.Length == 0)
        {
            Debug.LogError($"[序列触发] 「{name}」的 Steps 是空的", this);
            return;
        }

        sequenceStarted = true;
        triggered = true;

        // ① 先打开过渡锚定，让切换动画时角色不再弹回原点
        if (enableRootMotionWhenTriggered && transitionAnchor != null)
        {
            transitionAnchor.SetActive(true);

            if (logToConsole)
                Debug.Log($"[序列触发] 已打开 Animation Transition Anchor（过渡期间角色不会跑位）", this);
        }
        else if (enableRootMotionWhenTriggered && transitionAnchor == null)
        {
            Debug.LogWarning($"[序列触发] 「{name}」没有 AnimationTransitionAnchor，" +
                             $"每段动画切换时会跳回原点。\n" +
                             $"  修法：给 character 加 Animation Transition Anchor 组件（Active 保持关闭）", this);
        }

        // ② 停掉正在播的（比如开场那条序列），换内容重新开播
        player.Stop();
        player.steps = steps;
        player.PlayFromStart();

        if (logToConsole)
        {
            string list = "";
            foreach (var s in steps)
                list += $"\n    State {s.state}{(s.loop ? "（循环）" : "")}  {s.note}";

            Debug.Log($"[序列触发] 第 {triggerAtStepNumber} 步开始 → 播放 {steps.Length} 段动画：{list}", this);
        }
    }
}
