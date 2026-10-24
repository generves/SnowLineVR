using UnityEngine;
using System.Collections.Generic;
using System.Text;
using TMPro;

/// <summary>
/// 训练报告生成器 —— 把事件记录变成一份「可执行的复盘」。
///
/// ⚠️ 重要：本组件必须挂在【始终激活】的物体上（例如 GameManager）。
/// 因为 Unity 不会对未激活的 GameObject 调用 Start()，
/// 如果把本组件挂在默认隐藏的报告面板上，它将永远收不到「训练结束」事件。
/// 报告面板的显隐由 reportPanel 字段控制。
///
/// 设计要点：报告不只打分，必须给出【按优先级的改进建议】。
/// 打分让人紧张，建议让人成长 —— 这决定了作品是"考核工具"还是"教学工具"。
/// </summary>
public class ReportGenerator : MonoBehaviour
{
    [Header("引用（留空会自动查找）")]
    [SerializeField] private EventLogger eventLogger;

    [Header("报告显示")]
    [Tooltip("用来显示报告的文本组件（会自动在 reportPanel 里查找）")]
    [SerializeField] private TMP_Text reportText;

    [Tooltip("报告面板根物体（默认隐藏，训练结束后显示）")]
    [SerializeField] private GameObject reportPanel;

    [Tooltip("报告重新显示时，自动滚回顶部")]
    [SerializeField] private bool scrollToTopOnShow = true;

    [Header("分值")]
    [Tooltip("及格线百分比（0-1）")]
    [SerializeField] private float passThreshold = 0.6f;

    private void Awake()
    {
        // 用 Awake 而不是 Start：更早执行，且不受其他组件 Start 顺序影响
        if (eventLogger == null) eventLogger = FindAnyObjectByType<EventLogger>();

        if (eventLogger != null)
        {
            eventLogger.OnTrainingFinished += HandleTrainingFinished;
            Debug.Log("[接线] EventLogger → ReportGenerator 已连接");
        }
        else
        {
            Debug.LogWarning("[ReportGenerator] 找不到 EventLogger，报告不会生成");
        }
    }

    private void Start()
    {
        // 场景开始时不显示报告面板
        if (reportPanel != null && reportPanel != gameObject) reportPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (eventLogger != null) eventLogger.OnTrainingFinished -= HandleTrainingFinished;
    }

    private void HandleTrainingFinished(List<AidEvent> events)
    {
        string report = BuildReport(events);

        // 面板可能还没被赋值，尝试自动查找
        if (reportPanel == null) reportPanel = FindPanelByName("ReportPanelCanvas");
        if (reportText == null && reportPanel != null)
            reportText = reportPanel.GetComponentInChildren<TMP_Text>(true);

        if (reportText != null) reportText.text = report;

        if (reportPanel != null)
        {
            reportPanel.SetActive(true);

            if (scrollToTopOnShow)
            {
                var scroll = reportPanel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
                if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            }
        }
        else
        {
            Debug.LogWarning("[ReportGenerator] 没有找到报告面板，报告只输出到了 Console");
        }

        Debug.Log("[报告已生成]\n" + report);
    }

    private static GameObject FindPanelByName(string name)
    {
        // 包含未激活物体，所以不能用 GameObject.Find
        foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rt.name == name && rt.gameObject.scene.IsValid()) return rt.gameObject;
        }
        return null;
    }

    /// <summary>生成报告文本</summary>
    public string BuildReport(List<AidEvent> events)
    {
        var steps = FindAllSteps();

        // ---- 算分 ----
        int maxScore = 0;
        foreach (var s in steps) maxScore += s.score;

        int earned = 0;
        foreach (var e in events)
        {
            if (!e.isCorrect) continue;
            var s = FindStep(steps, e.stepIndex);
            if (s != null) earned += s.score;
        }

        // ---- 身体负担额外扣分：max(0, (疼痛峰值 - 4) × 3) ----
        float maxPain = 0f;
        var vitals = FindAnyObjectByType<VitalsSimulator>();
        if (vitals != null) maxPain = vitals.MaxPain;

        int extraDeduction = Mathf.Max(0, Mathf.RoundToInt((maxPain - 4f) * 3f));

        // 最终分最低为 0
        int score = Mathf.Clamp(earned - extraDeduction, 0, maxScore);

        float totalTime = (eventLogger != null) ? eventLogger.GetTotalTime() : 0f;
        float ratio = (maxScore > 0) ? (float)score / maxScore : 0f;
        string grade = ratio >= 0.9f ? "A" : ratio >= 0.75f ? "B" : ratio >= passThreshold ? "C" : "D";

        var sb = new StringBuilder();

        // ---- 表头 ----
        sb.AppendLine("训练报告 · 冰雪运动现场急救");
        sb.AppendLine("-----------------------------");
        sb.AppendLine($"总评：{score} / {maxScore}    等级 {grade}");

        if (extraDeduction > 0)
        {
            sb.AppendLine($"  步骤得分 {earned}，身体负担扣分 {extraDeduction}" +
                          $"（疼痛峰值 {maxPain:F1} → ({maxPain:F1} − 4) × 3）");
        }
        else if (maxPain > 0f)
        {
            sb.AppendLine($"  步骤得分 {earned}，身体负担扣分 0（疼痛峰值 {maxPain:F1} 未超过 4）");
        }

        sb.AppendLine($"用时：{EventLogger.FormatTime(totalTime)}");

        int wrongCount = 0, criticalCount = 0;
        foreach (var e in events) { if (!e.isCorrect) wrongCount++; if (e.isCritical) criticalCount++; }
        sb.AppendLine($"错误：{wrongCount} 处" + (criticalCount > 0 ? $"（其中关键错误 {criticalCount} 处）" : ""));
        sb.AppendLine();

        // ---- 时间轴 ----
        sb.AppendLine("时间轴回放");
        foreach (var e in events)
        {
            // 用纯文字标记，不用 emoji 符号（中文字体里通常没有 ✅⚠️❌ 的字形，会变方框）
            string mark = e.isCorrect ? "[正确]  " : (e.isCritical ? "[关键错误] " : "[错误]  ");
            sb.AppendLine($"  {EventLogger.FormatTime(e.time)}  {mark}{e.stepName}");
            if (!e.isCorrect && !string.IsNullOrEmpty(e.detail))
                sb.AppendLine($"             -> {e.detail}");
        }
        sb.AppendLine();

        // ---- 改进建议 ----
        sb.AppendLine("改进建议（按优先级）");
        int n = 1;
        bool any = false;

        foreach (var e in events)
        {
            if (e.isCorrect || !e.isCritical || string.IsNullOrEmpty(e.detail)) continue;
            sb.AppendLine($"  {n++}. 【关键】{e.detail}");
            any = true;
        }
        foreach (var e in events)
        {
            if (e.isCorrect || e.isCritical || string.IsNullOrEmpty(e.detail)) continue;
            sb.AppendLine($"  {n++}. {e.detail}");
            any = true;
        }

        if (!any) sb.AppendLine("  全部步骤操作规范，无改进项。");

        sb.AppendLine();
        sb.AppendLine("-----------------------------");
        sb.AppendLine("注：本报告用于教学训练，不构成临床诊疗建议。");

        return sb.ToString();
    }

    private FirstAidStep[] FindAllSteps()
    {
        var controller = FindAnyObjectByType<StepController>();
        if (controller == null || controller.Steps == null) return new FirstAidStep[0];

        var result = new List<FirstAidStep>();
        foreach (var s in controller.Steps) if (s != null) result.Add(s);
        return result.ToArray();
    }

    private FirstAidStep FindStep(FirstAidStep[] steps, int index)
    {
        foreach (var s in steps) if (s != null && s.stepIndex == index) return s;
        return null;
    }
}
