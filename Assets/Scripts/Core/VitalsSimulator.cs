using UnityEngine;
using System;

/// <summary>
/// 伤者生理模拟 —— 心率、体温、疼痛。
///
/// ===================== 新的后果模型 =====================
///
///   步骤做对 / 做错  →  只影响【评分】（由 EventLogger / ReportGenerator 处理）
///                      【完全不动身体数值】
///   道具用在人身上    →  才影响身体（由 ItemConsequence 调用这里的方法）
///
///   所以 FirstAidStep 里的 painDelta / tempDelta / heartRateDelta 已经不再使用。
///
/// ===================== 心率公式 =====================
///
///   HR = 88
///        + 疼痛 × 5
///        + (36.4 − 体温) × 20
///        − (当前步骤数 − 1) × 6
///
///   步骤数从 1 开始：第 1 步不减、第 5 步减 24。
///   含义：随着急救推进，伤者情绪逐渐稳定。
///
/// ===================== 体温 =====================
///
///   环境降温（越冷越难降）：
///     速率 = 0.008 × (体温 − 32) / (36.4 − 32)   ℃/秒
///     体温 36.4 → 0.008/秒；35.0 → 0.0055/秒；32.0 → 0（停在 32）
///
///   保温毯（全局只能用一次）：
///     · 体温 < 36 时可用
///     · 生效 40 秒：每秒 +0.02℃，上限 36℃
///     · 这 40 秒内完全不受环境降温（累计免除不超过 0.8℃）
///     · 不影响评分、不影响当前步骤
///
/// ===================== 疼痛 =====================
///
///   只增不减。达到 10 → 触发 OnCriticalFailure（训练终止）。
///   同时记录局内峰值 MaxPain，供最终评分扣分使用。
/// </summary>
[DisallowMultipleComponent]
public class VitalsSimulator : MonoBehaviour
{
    [Header("初始状态")]
    [Tooltip("心率基准值（公式里的 88）")]
    public float baseHeartRate = 88f;

    [Tooltip("体温基准值（公式里的 36.4）")]
    public float baseTemperature = 36.4f;

    [Tooltip("初始疼痛")]
    public float basePain = 3f;

    [Header("心率公式系数")]
    [Tooltip("HR = 基准 + 疼痛×本值 + (基准体温−体温)×温度系数 − (步骤数−1)×步骤系数")]
    public float painToHeartRate = 5f;

    [Tooltip("每低 1℃ 心率上升多少")]
    public float tempToHeartRate = 20f;

    [Tooltip("每推进 1 步心率下降多少")]
    public float stepToHeartRate = 6f;

    [Tooltip("心率向目标值逼近的速度，越大越跟得紧")]
    [Range(0.1f, 20f)]
    public float heartRateSmooth = 2.5f;

    [Header("体温 · 环境降温")]
    [Tooltip("体温在基准值(36.4℃)时每秒下降多少度")]
    public float coolingRateAtNormal = 0.008f;

    [Tooltip("体温下限。越接近它降得越慢，到它就完全停止")]
    public float temperatureFloor = 32f;

    [Header("体温 · 保温毯")]
    [Tooltip("体温低于这个值才能用保温毯")]
    public float blanketUseThreshold = 36f;

    [Tooltip("保温持续多少秒")]
    public float blanketDuration = 40f;

    [Tooltip("保温期间每秒回升多少度")]
    public float blanketWarmRate = 0.02f;

    [Tooltip("保温能升到的上限")]
    public float blanketCap = 36f;

    [Tooltip("保温期间最多免除多少度的环境降温（本设计下 40 秒只降 0.32℃，实际不会触顶）")]
    public float blanketMaxBlockedCooling = 0.8f;

    [Header("疼痛")]
    [Tooltip("疼痛上限。达到它 → 训练终止")]
    public float painMax = 10f;

    [Header("数值范围")]
    public float hrMin = 40f, hrMax = 180f;
    public float tempMin = 32f, tempMax = 39f;
    public float painMin = 0f;

    [Header("引用")]
    [Tooltip("用来取『当前步骤数』。留空自动查找")]
    public StepController stepController;

    [Header("调试")]
    public bool logToConsole = true;

    // ---------- 对外只读状态 ----------
    public float HeartRate { get; private set; }
    public float Temperature { get; private set; }
    public float Pain { get; private set; }

    /// <summary>局内疼痛峰值（最终评分要扣分）</summary>
    public float MaxPain { get; private set; }

    /// <summary>保温毯是否已经用过（全局只能用一次）</summary>
    public bool BlanketUsed { get; private set; }

    /// <summary>伤者是否已经危急（疼痛到顶）</summary>
    public bool IsCritical { get; private set; }

    /// <summary>当前是否在保温状态，以及剩余秒数</summary>
    public bool IsWarming => blanketTimer > 0f;
    public float WarmingRemaining => Mathf.Max(0f, blanketTimer);

    /// <summary>每帧广播 (心率, 体温, 疼痛)</summary>
    public event Action<float, float, float> OnVitalsChanged;

    /// <summary>疼痛达到上限时触发一次</summary>
    public event Action OnCriticalFailure;

    // ---------- 内部 ----------
    private float blanketTimer;
    private float blockedCooling;      // 本次保温累计免除的降温量
    private bool failureFired;

    private void Awake()
    {
        if (stepController == null) stepController = FindAnyObjectByType<StepController>();

        HeartRate = baseHeartRate;
        Temperature = baseTemperature;
        Pain = basePain;
        MaxPain = basePain;
    }

    private int CurrentStepNumber
    {
        get
        {
            if (stepController == null) stepController = FindAnyObjectByType<StepController>();
            return stepController != null ? stepController.CurrentStepNumber : 1;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // ==================== 体温 ====================
        if (blanketTimer > 0f)
        {
            // ---------- 保温中：升温 + 不降温 ----------
            blanketTimer -= dt;
            if (blanketTimer < 0f) blanketTimer = 0f;

            if (Temperature < blanketCap)
                Temperature = Mathf.Min(blanketCap, Temperature + blanketWarmRate * dt);
        }
        else
        {
            // ---------- 环境降温：越低越慢 ----------
            float span = Mathf.Max(0.01f, baseTemperature - temperatureFloor);
            float k = Mathf.Clamp01((Temperature - temperatureFloor) / span);
            float cool = coolingRateAtNormal * k * dt;

            // 保温期间的"免除降温"额度（这里采用直接不降温，额度仅用于日志/未来扩展）
            if (blockedCooling < blanketMaxBlockedCooling)
            {
                float allow = Mathf.Min(cool, blanketMaxBlockedCooling - blockedCooling);
                blockedCooling += allow;
            }

            Temperature -= cool;
        }

        Temperature = Mathf.Clamp(Temperature, tempMin, tempMax);

        // ==================== 疼痛 ====================
        Pain = Mathf.Clamp(Pain, painMin, painMax);

        if (Pain > MaxPain)
            MaxPain = Pain;

        // ==================== 心率（公式 + 平滑） ====================
        int stepNumber = CurrentStepNumber;

        float targetHR = baseHeartRate
                       + Pain * painToHeartRate
                       + (baseTemperature - Temperature) * tempToHeartRate
                       - Mathf.Max(0, stepNumber - 1) * stepToHeartRate;

        targetHR = Mathf.Clamp(targetHR, hrMin, hrMax);

        HeartRate = Mathf.Lerp(HeartRate, targetHR, heartRateSmooth * dt);
        HeartRate = Mathf.Clamp(HeartRate, hrMin, hrMax);

        // ==================== 广播 ====================
        OnVitalsChanged?.Invoke(HeartRate, Temperature, Pain);
    }

    // ==================== 对外接口 ====================

    /// <summary>
    /// 【兼容保留 · 空实现】
    ///
    /// StepController.Start() 里还订阅着：
    ///     OnStepCompleted += vitals.ApplyConsequence;
    ///
    /// 但新的后果模型是：
    ///     步骤做对 / 做错 → 只影响评分，不动身体
    ///     道具用在人身上   → 才影响身体（由 ItemConsequence 调用 AddPain / AddTemperature / TryUseBlanket）
    ///
    /// 所以这里什么都不做，只为不让 StepController 报 CS1061。
    ///
    /// ⚠️ 想彻底干净的话，可以把 StepController 第 79 行那句订阅删掉，
    ///    然后再删掉本方法。
    /// </summary>
    public void ApplyConsequence(FirstAidStep step, bool correct)
    {
        // 故意为空：步骤对错不再影响身体数值
    }

    /// <summary>加疼痛。达到上限会触发 OnCriticalFailure（只触发一次）</summary>
    public void AddPain(float delta)
    {
        if (Mathf.Abs(delta) < 0.00001f) return;

        Pain = Mathf.Clamp(Pain + delta, painMin, painMax);

        if (Pain > MaxPain) MaxPain = Pain;

        if (logToConsole)
            Debug.Log($"[生理] 疼痛 {delta:+0.00;-0.00;0.00} → 当前 {Pain:F2}（峰值 {MaxPain:F2}）", this);

        if (!failureFired && Pain >= painMax)
        {
            failureFired = true;
            IsCritical = true;

            if (logToConsole)
                Debug.LogError($"[生理] 疼痛达到上限 {painMax} → 伤者状况危急", this);

            OnCriticalFailure?.Invoke();
        }
    }

    /// <summary>增减体温（例如冰袋 −0.2）</summary>
    public void AddTemperature(float delta)
    {
        if (Mathf.Abs(delta) < 0.00001f) return;

        Temperature = Mathf.Clamp(Temperature + delta, tempMin, tempMax);

        if (logToConsole)
            Debug.Log($"[生理] 体温 {delta:+0.00;-0.00;0.00} → 当前 {Temperature:F2}℃", this);
    }

    /// <summary>
    /// 尝试使用保温毯。
    /// 返回 true = 开始保温；false = 无效（已经用过 或 体温够高）。
    /// 全局只能用一次。
    /// </summary>
    public bool TryUseBlanket()
    {
        if (BlanketUsed)
        {
            if (logToConsole) Debug.Log("[生理] 保温毯已经用过一次，无效", this);
            return false;
        }

        if (Temperature >= blanketUseThreshold)
        {
            if (logToConsole)
                Debug.Log($"[生理] 体温 {Temperature:F2}℃ ≥ {blanketUseThreshold}℃，不需要保温", this);
            return false;
        }

        BlanketUsed = true;
        blanketTimer = blanketDuration;
        blockedCooling = 0f;

        if (logToConsole)
            Debug.Log($"[生理] 开始保温：{blanketDuration} 秒，每秒 +{blanketWarmRate}℃，上限 {blanketCap}℃", this);

        return true;
    }

    /// <summary>重置到初始状态（重新训练时用）</summary>
    public void ResetVitals()
    {
        HeartRate = baseHeartRate;
        Temperature = baseTemperature;
        Pain = basePain;
        MaxPain = basePain;

        blanketTimer = 0f;
        blockedCooling = 0f;
        BlanketUsed = false;

        failureFired = false;
        IsCritical = false;
    }

    /// <summary>当前心率公式的目标值（调试用）</summary>
    public float GetTargetHeartRate()
    {
        int stepNumber = CurrentStepNumber;

        return baseHeartRate
             + Pain * painToHeartRate
             + (baseTemperature - Temperature) * tempToHeartRate
             - Mathf.Max(0, stepNumber - 1) * stepToHeartRate;
    }
}
