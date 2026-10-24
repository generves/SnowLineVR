using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 道具应用的身体后果 —— 把「把道具用在伤者身上」翻译成生理影响。
///
/// ===================== 为什么要单独一个脚本 =====================
///
///   新的后果模型是：
///     步骤做对 / 做错 → 只影响评分，不动身体
///     道具用在人身上   → 才影响身体，且取决于「当前步骤需不需要这个道具」
///
///   本脚本订阅 PatientBodyPart.OnItemApplied，按下面的规则施加影响。
///
/// ===================== 规则表 =====================
///
///   冰袋 IcePack
///     当前步骤需要它   → 无影响
///     当前步骤不需要   → 体温 −0.2℃ / 次
///
///   夹板 Splint / 绷带 Bandage
///     当前步骤需要它   → 无影响
///     当前步骤不需要   → 疼痛 +（递减增量）
///        第 1 次 +1.00，第 10 次 +0.40，线性递减，10 次合计正好 +7.00
///        超过 10 次不再增加
///
///   保温毯 Blanket
///     · 体温 < 36 且【全局没用过】→ 开始 40 秒保温（+0.02℃/秒，上限 36℃）
///     · 已经用过 / 体温够高 → 完全忽视
///     · 不影响评分、不影响当前步骤
///
///   特例（由 StepActionBridge 负责执行，这里只保证不惩罚）
///     当前是第 1 步时使用夹板 → 视为「正当使用」，不加疼痛
/// </summary>
public class ItemConsequence : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("生理模拟器。留空自动查找")]
    public VitalsSimulator vitals;

    [Tooltip("步骤控制器。留空自动查找（用来判断『当前步骤需不需要这个道具』）")]
    public StepController stepController;

    [Header("冰袋")]
    public string icePackTag = "IcePack";

    [Tooltip("不在正确步骤时，每用一次冰袋体温下降多少度")]
    public float icePackTemperatureDrop = 0.2f;

    [Header("夹板 / 绷带（错误时机）")]
    public string splintTag = "Splint";
    public string bandageTag = "Bandage";

    [Tooltip("第 1 次错误使用的疼痛增量")]
    public float painIncrementFirst = 1.0f;

    [Tooltip("第 10 次错误使用的疼痛增量（中间线性递减）")]
    public float painIncrementLast = 0.4f;

    [Tooltip("最多累计错几次（超过后不再增加疼痛）")]
    public int maxWrongUses = 10;

    [Header("保温毯")]
    public string blanketTag = "Blanket";

    [Header("道具 → 动作 ID 映射")]
    [Tooltip("用来判断『当前步骤需不需要这个道具』。要和 FirstAidStep.requiredActionId 里的写法一致")]
    public List<ItemActionMap> itemToAction = new List<ItemActionMap>
    {
        new ItemActionMap { itemTag = "Splint",  actionId = "ApplySplint" },
        new ItemActionMap { itemTag = "IcePack", actionId = "ApplyIcePack" },
        new ItemActionMap { itemTag = "Bandage", actionId = "ApplyBandage" },
        new ItemActionMap { itemTag = "Blanket", actionId = "ApplyBlanket" },
    };

    [System.Serializable]
    public class ItemActionMap
    {
        public string itemTag;
        public string actionId;
    }

    [Header("调试")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private readonly List<PatientBodyPart> subscribed = new List<PatientBodyPart>();
    private float rescanTimer;
    private const float RescanInterval = 0.5f;

    private int wrongUseCount;

    /// <summary>已经错误使用夹板/绷带的次数</summary>
    public int WrongUseCount => wrongUseCount;

    private void Awake()
    {
        if (vitals == null) vitals = FindAnyObjectByType<VitalsSimulator>();
        if (stepController == null) stepController = FindAnyObjectByType<StepController>();
    }

    private void Start()
    {
        RefreshSubscriptions();
    }

    private void Update()
    {
        // 身体部位在选关之后才激活，所以要定时补订阅
        rescanTimer += Time.deltaTime;
        if (rescanTimer >= RescanInterval)
        {
            rescanTimer = 0f;
            RefreshSubscriptions();
        }
    }

    private void OnDestroy()
    {
        foreach (var p in subscribed)
            if (p != null) p.OnItemApplied -= HandleItemApplied;

        subscribed.Clear();
    }

    private void RefreshSubscriptions()
    {
        foreach (var p in FindObjectsByType<PatientBodyPart>(FindObjectsInactive.Include))
        {
            if (p == null) continue;
            if (subscribed.Contains(p)) continue;

            p.OnItemApplied += HandleItemApplied;
            subscribed.Add(p);
        }
    }

    // ==================== 主逻辑 ====================
    private void HandleItemApplied(PatientBodyPart part, string itemTag)
    {
        if (part == null || string.IsNullOrEmpty(itemTag)) return;
        if (vitals == null) return;

        // 当前步骤需不需要这个道具
        bool requiredNow = IsRequiredNow(itemTag);

        // 特例：第 1 步时用夹板 → 视为正当使用（跳步由 StepActionBridge 负责），不加惩罚
        bool isSplintAtFirstStep = (itemTag == splintTag) && IsAtStep(1);
        if (isSplintAtFirstStep) requiredNow = true;

        // ---------- 冰袋 ----------
        if (itemTag == icePackTag)
        {
            if (!requiredNow)
            {
                vitals.AddTemperature(-icePackTemperatureDrop);

                if (logToConsole)
                    Debug.Log($"[道具后果] 冰袋用在「{part.part}」但当前步骤不需要它 " +
                              $"→ 体温 −{icePackTemperatureDrop}℃", this);
            }
            return;
        }

        // ---------- 夹板 / 绷带 ----------
        if (itemTag == splintTag || itemTag == bandageTag)
        {
            if (requiredNow)
            {
                if (logToConsole)
                    Debug.Log($"[道具后果]「{itemTag}」用在「{part.part}」，当前步骤正需要它 → 无惩罚", this);
                return;
            }

            ApplyWrongTimePain(itemTag, part);
            return;
        }

        // ---------- 保温毯 ----------
        if (itemTag == blanketTag)
        {
            bool started = vitals.TryUseBlanket();

            if (logToConsole)
            {
                if (started)
                    Debug.Log($"[道具后果] 保温毯生效：体温将回升（当前 {vitals.Temperature:F2}℃）", this);
                else
                    Debug.Log($"[道具后果] 保温毯无效（已用过或体温 {vitals.Temperature:F2}℃ 已足够）", this);
            }
        }
    }

    /// <summary>错误时机用夹板/绷带 → 递增但递减的疼痛</summary>
    private void ApplyWrongTimePain(string itemTag, PatientBodyPart part)
    {
        wrongUseCount++;

        if (wrongUseCount > maxWrongUses)
        {
            if (logToConsole)
                Debug.Log($"[道具后果]「{itemTag}」错误使用第 {wrongUseCount} 次，已超过上限 {maxWrongUses} 次 → 不再增加疼痛", this);
            return;
        }

        // 第 1 次 → painIncrementFirst，第 10 次 → painIncrementLast，线性递减
        float t = Mathf.Clamp01((wrongUseCount - 1) / 9f);
        float increment = Mathf.Lerp(painIncrementFirst, painIncrementLast, t);

        vitals.AddPain(increment);

        if (logToConsole)
            Debug.Log($"[道具后果]「{itemTag}」用在「{part.part}」，当前步骤不需要它 " +
                      $"→ 疼痛 +{increment:F2}（第 {wrongUseCount}/{maxWrongUses} 次错误）", this);
    }

    // ==================== 工具 ====================
    private bool IsAtStep(int stepNumber)
    {
        if (stepController == null) return false;

        var step = stepController.CurrentStep;
        return step != null && step.stepIndex == stepNumber;
    }

    private bool IsRequiredNow(string itemTag)
    {
        if (stepController == null) return false;

        var step = stepController.CurrentStep;
        if (step == null) return false;
        if (string.IsNullOrEmpty(step.requiredActionId)) return false;

        string actionId = MapAction(itemTag);
        if (string.IsNullOrEmpty(actionId)) return false;

        foreach (var a in step.requiredActionId.Split(new[] { ',', '，', ';', '；' },
                                                       System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (a.Trim() == actionId) return true;
        }

        return false;
    }

    private string MapAction(string itemTag)
    {
        if (itemToAction == null) return null;

        foreach (var m in itemToAction)
        {
            if (m == null) continue;
            if (m.itemTag == itemTag) return m.actionId;
        }

        return null;
    }

    /// <summary>重置计数（重新训练时用）</summary>
    public void ResetCounters()
    {
        wrongUseCount = 0;
    }
}
