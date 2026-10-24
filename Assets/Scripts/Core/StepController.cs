using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 步骤推进器 —— 本项目的心脏。
///
/// 职责：
///   1. 按列表顺序驱动 FirstAidStep
///   2. 接收外部上报的「我做了某个动作」，判定对错
///   3. 做错时【不阻断】，记录错误后继续推进（体现"急救没有撤销键"）
///   4. 限时检测
///   5. 通过事件把「进入下一步 / 完成一步 / 全部完成」广播出去
///
/// 设计要点：本类不含任何具体的急救步骤内容，全部内容来自 FirstAidStep 资产。
/// </summary>
public class StepController : MonoBehaviour
{
    [Header("步骤列表（按顺序拖入 7 个 FirstAidStep 资产）")]
    [SerializeField] private List<FirstAidStep> steps = new List<FirstAidStep>();

    [Header("反馈延迟（秒）")]
    [Tooltip("做错后先让学员读完提示，再施加后果。这个停顿是建立因果关系的关键，别调太小")]
    [SerializeField] private float wrongFeedbackDelay = 2.5f;

    [Tooltip("施加后果后，停顿多久再进入下一步")]
    [SerializeField] private float consequenceDelay = 0.8f;

    [Header("调试")]
    [SerializeField] private bool logToConsole = true;

    // ---------- 对外事件 ----------
    /// <summary>进入某一步时触发</summary>
    public event Action<FirstAidStep> OnStepStarted;
    /// <summary>某一步完成时触发。bool = 是否正确</summary>
    public event Action<FirstAidStep, bool> OnStepCompleted;
    /// <summary>全部步骤完成时触发</summary>
    public event Action OnAllStepsCompleted;
    /// <summary>所有需要显示的反馈文本（正确/错误/超时）都走这里，交给 HintPanel 显示</summary>
    public event Action<string> OnHintRequested;

    // ---------- 状态 ----------
    private int currentIndex = -1;
    private float stepStartTime;
    private bool isProcessing;      // 正在处理上一步的结果，期间忽略新输入
    private bool isFinished;

    /// <summary>当前步骤，没有则返回 null</summary>
    public FirstAidStep CurrentStep
    {
        get
        {
            if (currentIndex < 0 || currentIndex >= steps.Count) return null;
            return steps[currentIndex];
        }
    }

    /// <summary>当前是第几步（1 开始），未开始为 0</summary>
    public int CurrentStepNumber => currentIndex + 1;

    /// <summary>步骤总数</summary>
    public int TotalSteps => steps.Count;

    /// <summary>训练是否已结束</summary>
    public bool IsFinished => isFinished;

    /// <summary>只读的步骤列表（报告生成器需要用它算分）</summary>
    public IReadOnlyList<FirstAidStep> Steps => steps;

    // ---------- 生命周期 ----------
    private void Start()
    {
        // 自动连接同一物体上的 VitalsSimulator：
        // 步骤判定结果 → 施加生理后果。这样避免手动拖引用出错。
        var vitals = GetComponent<VitalsSimulator>();
        if (vitals != null)
        {
            OnStepCompleted += vitals.ApplyConsequence;
            Debug.Log("[接线] StepController → VitalsSimulator 已连接");
        }
        else if (logToConsole)
        {
            Debug.LogWarning("[接线] 同一物体上没找到 VitalsSimulator，错误后果不会生效");
        }

        // 不在这里自动开始 —— 由 ScenarioManager 在玩家选完事件后调用 BeginTraining()。
        // 如果场景里没有 ScenarioManager（比如单独测试步骤系统），
        // 就退回到「启动即开始」的旧行为。
        if (FindAnyObjectByType<ScenarioManager>() == null)
        {
            if (logToConsole) Debug.Log("[StepController] 未检测到 ScenarioManager，启动即开始训练");
            StartCoroutine(BeginStep(0));
        }
        else
        {
            if (logToConsole) Debug.Log("[StepController] 等待 ScenarioManager 选择事件后再开始");
        }
    }

    /// <summary>
    /// 开始（或重新开始）训练。由 ScenarioManager 在玩家选定事件后调用。
    /// </summary>
    public void BeginTraining()
    {
        StopAllCoroutines();
        isFinished = false;
        isProcessing = false;
        currentIndex = -1;

        if (logToConsole) Debug.Log("[StepController] 训练开始");
        StartCoroutine(BeginStep(0));
    }

    /// <summary>
    /// 换一套步骤并重新开始（供多关卡切换使用）。
    /// 传 null 或空列表则沿用现有步骤。
    /// </summary>
    public void BeginTraining(List<FirstAidStep> newSteps)
    {
        if (newSteps != null && newSteps.Count > 0)
        {
            steps = new List<FirstAidStep>(newSteps);
            Debug.Log($"[StepController] 已载入 {steps.Count} 个新步骤");
        }
        BeginTraining();
    }

    private void Update()
    {
        if (isFinished || isProcessing) return;

        var step = CurrentStep;
        if (step == null || step.timeLimit <= 0f) return;

        // 限时检测：超时只记一次（不推进 —— 学员必须自己把这一步做对）
        if (Time.time - stepStartTime > step.timeLimit && !timeoutRecorded)
        {
            timeoutRecorded = true;

            if (logToConsole) Debug.LogWarning($"[超时] 第 {step.stepIndex} 步：{step.stepName}");

            string hint = string.IsNullOrEmpty(step.timeoutHint) ? step.wrongHint : step.timeoutHint;
            if (!string.IsNullOrEmpty(hint)) OnHintRequested?.Invoke(hint);

            // 按"做错"记一次 → 施加后果 + 扣分
            OnStepCompleted?.Invoke(step, false);
        }
    }

    private bool timeoutRecorded;

    // ---------- 对外接口 ----------
    /// <summary>
    /// 外部（交互物体、PlacementZone 等）通过这个方法上报「我完成了某个动作」。
    ///
    /// 判定规则（用户要求）：
    ///   ① 动作对 → 这一步完成，推进到下一步
    ///   ② 动作错 → 【不推进】，计入错误扣分，继续留在这一步
    ///
    /// requiredActionId 支持【多个动作，用逗号分隔】，例如：
    ///       ApplyIcePack,ApplyBandage
    /// 此时必须把列表里的动作都做一遍才算过（见 FirstAidStep.sequential）。
    /// </summary>
    public void ReportAction(string actionId)
    {
        if (isFinished || isProcessing) return;

        var step = CurrentStep;
        if (step == null) return;

        bool matched = MatchesRequired(step, actionId);

        // ---------- 这个动作对不对 ----------
        if (!matched)
        {
            // ❌ 做错了：不推进，只记错 + 提示
            if (logToConsole)
                Debug.LogWarning($"[错误] 第 {step.stepIndex} 步「{step.stepName}」" +
                                 $"上报了 '{actionId}'，但需要的是 '{step.requiredActionId}'" +
                                 $" → 不推进，计入扣分");

            StartCoroutine(HandleWrong(step, actionId));
            return;
        }

        // ---------- 动作对，但多动作步骤可能还没做完 ----------
        if (step.sequential)
        {
            // 去掉一个待做动作
            pendingActions.Remove(actionId);
            doneActions.Add(actionId);

            if (pendingActions.Count > 0)
            {
                // ✅ 部分完成：不推进，提示还差什么
                string left = string.Join("、", pendingActions);

                if (logToConsole)
                    Debug.Log($"[进行中] 第 {step.stepIndex} 步「{step.stepName}」" +
                              $"完成 '{actionId}'，还差：{left} → 不推进");

                OnHintRequested?.Invoke($"已完成这一步的一部分，还需要：{left}");
                return;
            }
        }

        // ---------- 这一步全部做完 ----------
        if (logToConsole)
            Debug.Log($"[正确] 第 {step.stepIndex} 步「{step.stepName}」完成（触发动作 '{actionId}'）");

        StartCoroutine(HandleCorrect(step, actionId));
    }

    /// <summary>
    /// 把 requiredActionId 拆成动作列表。
    /// 支持多个动作，用英文/中文逗号或分号分隔。
    /// </summary>
    private static string[] ParseRequired(FirstAidStep step)
    {
        if (step == null || string.IsNullOrEmpty(step.requiredActionId))
            return new string[0];

        return step.requiredActionId.Split(new[] { ',', '，', ';', '；' },
                                           System.StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>上报的动作是不是这一步要求的动作之一</summary>
    private static bool MatchesRequired(FirstAidStep step, string actionId)
    {
        if (string.IsNullOrEmpty(actionId)) return false;

        foreach (var p in ParseRequired(step))
            if (p.Trim() == actionId) return true;

        return false;
    }

    // ---------- 多动作步骤的进度 ----------
    private readonly HashSet<string> doneActions = new HashSet<string>();
    private readonly List<string> pendingActions = new List<string>();

    /// <summary>当前这一步还差哪些动作没做（只对 sequential 的步骤有意义）</summary>
    public IReadOnlyList<string> RemainingActions => pendingActions;

    /// <summary>进入某一步时，重置"多动作"的进度</summary>
    private void ResetStepProgress(FirstAidStep step)
    {
        doneActions.Clear();
        pendingActions.Clear();

        if (step == null) return;

        foreach (var p in ParseRequired(step))
        {
            string a = p.Trim();
            if (!string.IsNullOrEmpty(a)) pendingActions.Add(a);
        }
    }

    /// <summary>跳过当前步骤（调试用）—— 会按"做错"记录一次</summary>
    [ContextMenu("调试：强制进入下一步")]
    public void ForceNext()
    {
        if (isFinished || isProcessing) return;
        var step = CurrentStep;
        if (step == null) return;

        // 强制跳过 = 记一次错，然后推进
        StartCoroutine(ForceSkip(step));
    }

    private IEnumerator ForceSkip(FirstAidStep step)
    {
        isProcessing = true;

        OnStepCompleted?.Invoke(step, false);

        if (consequenceDelay > 0f)
            yield return new WaitForSeconds(consequenceDelay);

        StartCoroutine(BeginStep(currentIndex + 1));
    }

    // ---------- 内部流程 ----------
    private IEnumerator BeginStep(int index)
    {
        currentIndex = index;
        isProcessing = false;

        // 全部做完
        if (index >= steps.Count)
        {
            isFinished = true;
            Debug.Log("[完成] 全部步骤结束");
            OnAllStepsCompleted?.Invoke();
            yield break;
        }

        var step = steps[index];
        stepStartTime = Time.time;
        timeoutRecorded = false;          // 新的一步，超时标记清零

        // ⭐ 进入这一步时重置"多动作"的进度
        ResetStepProgress(step);

        if (logToConsole) Debug.Log($"[开始] 第 {step.stepIndex} 步：{step.stepName}");

        OnStepStarted?.Invoke(step);
        OnHintRequested?.Invoke($"第 {step.stepIndex} 步：{step.instruction}");
        yield break;
    }

    /// <summary>
    /// 做对了：广播结果 → 推进到下一步。
    /// </summary>
    private IEnumerator HandleCorrect(FirstAidStep step, string actionId)
    {
        isProcessing = true;

        if (!string.IsNullOrEmpty(step.correctFeedback))
            OnHintRequested?.Invoke(step.correctFeedback);

        // 广播结果（VitalsSimulator / EventLogger 在这里接住）
        OnStepCompleted?.Invoke(step, true);

        // 停顿一下让曲线 / 动画动起来，再进入下一步
        if (consequenceDelay > 0f)
            yield return new WaitForSeconds(consequenceDelay);

        StartCoroutine(BeginStep(currentIndex + 1));
    }

    /// <summary>
    /// 做错了：【不推进】。
    /// 只显示教学提示 + 广播错误（VitalsSimulator 施加后果、EventLogger 记进报告扣分），
    /// 然后留在当前这一步，等学员做对。
    /// </summary>
    private IEnumerator HandleWrong(FirstAidStep step, string actionId)
    {
        isProcessing = true;

        if (!string.IsNullOrEmpty(step.wrongHint))
            OnHintRequested?.Invoke(step.wrongHint);

        if (logToConsole && step.isCriticalError)
            Debug.LogWarning($"[关键错误] {step.stepName}：{step.wrongHint}");

        // 广播"做错了" → 生命体征恶化 + 记入报告
        OnStepCompleted?.Invoke(step, false);

        // 停一下，让学员意识到「我刚才做错了」
        if (wrongFeedbackDelay > 0f)
            yield return new WaitForSeconds(wrongFeedbackDelay);

        // ⭐ 关键：解除处理中状态，留在当前步骤，等学员重新操作
        isProcessing = false;

        if (logToConsole)
            Debug.Log($"[停留] 仍在第 {step.stepIndex} 步「{step.stepName}」，等学员做对");
    }
}
