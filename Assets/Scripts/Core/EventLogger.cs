using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 一次训练事件的记录条目。
/// </summary>
[Serializable]
public class AidEvent
{
    public float time;          // 相对训练开始的时间（秒）
    public int stepIndex;       // 第几步
    public string stepName;     // 步骤名
    public bool isCorrect;      // 是否做对
    public bool isCritical;     // 是否是关键错误
    public string detail;       // 说明（做对=正确反馈，做错=教学提示）
}

/// <summary>
/// 训练全过程记录器 —— 训练报告的数据源。
///
/// 职责：订阅 StepController 的事件，把每一步的结果记录成一条 AidEvent。
/// 不做任何 UI，只负责「记录事实」和「算分」。
/// </summary>
public class EventLogger : MonoBehaviour
{
    [Header("引用（留空会自动查找场景里的 StepController）")]
    [SerializeField] private StepController stepController;

    [Header("调试")]
    [SerializeField] private bool logToConsole = true;

    [Header("伤者危急")]
    [Tooltip("疼痛到上限时，先显示这条提示")]
    [SerializeField] private string criticalFailMessage = "伤者状况危急，训练终止";

    [Tooltip("提示停留多少秒后出报告")]
    [SerializeField] private float criticalFailDelay = 2f;

    /// <summary>全部步骤完成时触发，参数是本次训练的全部事件</summary>
    public event Action<List<AidEvent>> OnTrainingFinished;

    private readonly List<AidEvent> events = new List<AidEvent>();
    private float trainingStartTime;
    private bool finished;      // 防止正常完成和危急终止重复出报告

    public IReadOnlyList<AidEvent> Events => events;

    private void Start()
    {
        if (stepController == null) stepController = FindAnyObjectByType<StepController>();

        if (stepController == null)
        {
            Debug.LogWarning("[EventLogger] 场景里找不到 StepController，无法记录训练过程");
            return;
        }

        trainingStartTime = Time.time;
        stepController.OnStepCompleted += RecordStep;
        stepController.OnAllStepsCompleted += Finish;

        // 伤者危急 → 终止训练
        var vitals = FindAnyObjectByType<VitalsSimulator>();
        if (vitals != null) vitals.OnCriticalFailure += HandleCriticalFailure;

        Debug.Log("[接线] StepController → EventLogger 已连接");
    }

    private void OnDestroy()
    {
        if (stepController != null)
        {
            stepController.OnStepCompleted -= RecordStep;
            stepController.OnAllStepsCompleted -= Finish;
        }

        var vitals = FindAnyObjectByType<VitalsSimulator>();
        if (vitals != null) vitals.OnCriticalFailure -= HandleCriticalFailure;
    }

    /// <summary>伤者疼痛达到上限 → 提示 2 秒后终止训练并出报告</summary>
    private void HandleCriticalFailure()
    {
        if (finished) return;
        StartCoroutine(CriticalFailureRoutine());
    }

    private System.Collections.IEnumerator CriticalFailureRoutine()
    {
        if (logToConsole)
            Debug.LogWarning("[训练终止] 伤者状况危急，2 秒后出报告");

        // 提示
        var hint = FindAnyObjectByType<HintPanel>();
        if (hint != null) hint.ShowError(criticalFailMessage, criticalFailDelay + 0.5f);

        // 冻结步骤系统，防止期间继续推进
        if (stepController != null) stepController.enabled = false;

        yield return new WaitForSeconds(criticalFailDelay);

        Finish();
    }

    private void RecordStep(FirstAidStep step, bool correct)
    {
        var e = new AidEvent
        {
            time = Time.time - trainingStartTime,
            stepIndex = step.stepIndex,
            stepName = step.stepName,
            isCorrect = correct,
            isCritical = step.isCriticalError && !correct,
            detail = correct ? step.correctFeedback : step.wrongHint
        };
        events.Add(e);

        if (logToConsole)
        {
            string mark = correct ? "[正确]" : (e.isCritical ? "[关键错误]" : "[错误]");
            Debug.Log($"[记录] {mark} 第 {step.stepIndex} 步 {step.stepName}");
        }
    }

    private void Finish()
    {
        if (finished) return;
        finished = true;

        float total = Time.time - trainingStartTime;
        if (logToConsole)
        {
            int wrong = 0, critical = 0;
            foreach (var e in events) { if (!e.isCorrect) wrong++; if (e.isCritical) critical++; }
            Debug.Log($"[训练结束] 用时 {FormatTime(total)}，共 {events.Count} 步，错误 {wrong} 处（其中关键错误 {critical} 处）");
        }
        OnTrainingFinished?.Invoke(events);
    }

    /// <summary>外部强制结束训练（例如伤者危急）</summary>
    public void FinishNow() => Finish();

    /// <summary>总用时（秒）</summary>
    public float GetTotalTime() => Time.time - trainingStartTime;

    /// <summary>重置记录（重新训练时用）</summary>
    public void ResetLog()
    {
        events.Clear();
        trainingStartTime = Time.time;
        finished = false;

        // 被终止时可能禁用了 StepController，这里恢复
        var sc = FindAnyObjectByType<StepController>();
        if (sc != null) sc.enabled = true;
    }

    /// <summary>把秒数格式化成 mm:ss</summary>
    public static string FormatTime(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m:00}:{s:00}";
    }
}
