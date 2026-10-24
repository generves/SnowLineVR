using UnityEngine;

/// <summary>
/// 到指定步骤才显示 —— 让一组物体在某个步骤【开始时】出现。
///
/// 本关卡的用法：
///   整个 ambulance_stretcher_current_2part（担架 + 挡板）一直隐藏，
///   直到第 5 步「交接与病情汇报」开始时才出现。
///
/// ===================== 用法 =====================
///
///   ① 新建空物体 VisualGroup_Stretcher，放在 ScenarioContent_01 下面
///   ② 把担架相关的物体拖进 Targets
///   ③ 挂本脚本，填：
///        Trigger At Step Number = 5
///        Hide On Start          = ☑
///   ④ ⚠️ 同时把 Targets 里的物体在 Hierarchy 里【手动取消激活】
///      只靠 Hide On Start 的话，第一帧可能闪一下
///
///   ⚠️ 如果担架和挡板是同一个根物体下的父子关系，
///      建议把【顶层那个根物体】拖进 Targets，而不是逐个拖子物体。
/// </summary>
public class RevealOnStepStart : MonoBehaviour
{
    [Header("要显示的物体")]
    [Tooltip("默认隐藏、到指定步骤才显示。建议拖顶层根物体")]
    public GameObject[] targets;

    [Header("触发条件")]
    [Tooltip("第几步【开始时】显示（对应 FirstAidStep 里的 stepIndex）")]
    public int triggerAtStepNumber = 5;

    [Tooltip("步骤控制器。留空自动查找")]
    public StepController stepController;

    [Header("行为")]
    [Tooltip("启动时先把 Targets 隐藏起来")]
    public bool hideOnStart = true;

    [Tooltip("只显示一次。取消勾选的话，每次进入这一步都会再显示")]
    public bool once = true;

    [Header("调试")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private bool shown;

    /// <summary>是否已经显示过</summary>
    public bool Shown => shown;

    private void Start()
    {
        if (hideOnStart) SetTargetsActive(false);

        if (stepController == null) stepController = FindAnyObjectByType<StepController>();

        if (stepController == null)
        {
            Debug.LogWarning($"[步骤显隐] 「{name}」找不到 StepController，不会按步骤显示", this);
            return;
        }

        stepController.OnStepStarted += HandleStepStarted;

        if (logToConsole)
            Debug.Log($"[步骤显隐] 「{name}」已订阅步骤事件，" +
                      $"将在第 {triggerAtStepNumber} 步开始时显示 " +
                      $"{CountTargets()} 个物体", this);
    }

    private void OnDestroy()
    {
        if (stepController != null)
            stepController.OnStepStarted -= HandleStepStarted;
    }

    private void HandleStepStarted(FirstAidStep step)
    {
        if (step == null) return;
        if (shown && once) return;
        if (step.stepIndex != triggerAtStepNumber) return;

        Show();
    }

    private int CountTargets()
    {
        if (targets == null) return 0;
        int n = 0;
        foreach (var t in targets) if (t != null) n++;
        return n;
    }

    private void SetTargetsActive(bool v)
    {
        if (targets == null) return;

        foreach (var t in targets)
        {
            if (t == null) continue;
            if (t.activeSelf == v) continue;
            t.SetActive(v);
        }
    }

    // ==================== 对外接口 ====================

    /// <summary>手动显示</summary>
    [ContextMenu("手动显示")]
    public void Show()
    {
        shown = true;
        SetTargetsActive(true);

        if (logToConsole)
            Debug.Log($"[步骤显隐] 「{name}」在第 {triggerAtStepNumber} 步显示 " +
                      $"{CountTargets()} 个物体", this);
    }

    /// <summary>手动隐藏</summary>
    [ContextMenu("手动隐藏")]
    public void Hide()
    {
        shown = false;
        SetTargetsActive(false);

        if (logToConsole)
            Debug.Log($"[步骤显隐] 「{name}」已隐藏", this);
    }
}
