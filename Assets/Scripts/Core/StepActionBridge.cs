using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 玩法 → 步骤系统 的桥接器。
///
/// 为什么需要它：
///   StepController 靠 FirstAidStep.requiredActionId（字符串）推进步骤，
///   但真实的玩法事件是"把道具用在某个部位上""打开医疗箱"这类东西，
///   两者的名字对不上。本脚本负责翻译。
///
/// 映射规则（左腿受伤关卡）：
///   报告类动作（钥匙报告 / 呼叫救援 / 交接汇报）→ 直接按键上报
///   开医疗箱                                     → 上报 actionId
///   把道具用在身体部位上                          → 按道具名映射（见下）
///
/// 用法：
///   ① 挂在 GameManager 上（或者场景里任意常驻物体）
///   ② Step Controller 留空会自动查找
///   ③ 玩法自动上报：把道具用在身体部位上、打开医疗箱
///   ④ 摆放/呼救这类需要专项判定的步骤，由 PlacementZone 等组件自己上报
/// </summary>
public class StepActionBridge : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("步骤控制器。留空会自动查找")]
    public StepController stepController;

    [Header("自动监听")]
    [Tooltip("打开医疗箱时自动上报（对应 ApplySplint 这类步骤前的准备动作）")]
    public bool listenBoxOpened = true;

    [Tooltip("把道具用在身体部位上时自动上报")]
    public bool listenItemApplied = true;

    [Header("道具名 → 动作 ID 映射")]
    [Tooltip("把 ItemTag.tagName（如 Splint）映射成 FirstAidStep.requiredActionId（如 ApplySplint）")]
    public List<ItemActionMap> itemMaps = new List<ItemActionMap>
    {
        new ItemActionMap { itemTag = "Splint",  actionId = "ApplySplint" },
        new ItemActionMap { itemTag = "IcePack", actionId = "ApplyIcePack" },
        new ItemActionMap { itemTag = "Bandage", actionId = "ApplyBandage" },
        new ItemActionMap { itemTag = "Blanket", actionId = "ApplyBlanket" },
        new ItemActionMap { itemTag = "Mat",     actionId = "ApplyMat" },
        new ItemActionMap { itemTag = "Phone",   actionId = "CallRescue" },
    };

    [Header("开箱动作")]
    [Tooltip("打开医疗箱时上报的动作 ID")]
    public string boxOpenActionId = "OpenMedicalKit";

    [Header("调试")]
    public bool logToConsole = false;

    [System.Serializable]
    public class ItemActionMap
    {
        [Tooltip("ItemTag 上的 tagName，例如 Splint")]
        public string itemTag;
        [Tooltip("对应的动作 ID，例如 ApplySplint")]
        public string actionId;
    }

    private void Start()
    {
        if (stepController == null) stepController = FindAnyObjectByType<StepController>();

        if (stepController == null)
        {
            Debug.LogWarning("[步骤桥接] 场景里找不到 StepController，玩法不会推进步骤");
            return;
        }

        // ⚠️ 不能只在 Start 里订阅一次：
        //    关卡内容（角色、医疗箱）一开始是【未激活】的，
        //    FindObjectsByType 默认不包含未激活物体 → 会一个都找不到。
        //    所以改成每帧补订阅（见 Update）。
        RefreshSubscriptions();

        if (logToConsole) Debug.Log("[步骤桥接] 已接上 StepController");
    }

    // ---------- 已订阅的组件（边跑边补，防止漏掉"后来才激活"的物体）----------
    private readonly List<PatientBodyPart> subscribedParts = new List<PatientBodyPart>();
    private readonly List<OpenableBox> subscribedBoxes = new List<OpenableBox>();
    private float rescanTimer;
    private const float RescanInterval = 0.5f;

    /// <summary>
    /// 扫描场景里所有【已激活】的身体部位和医疗箱，把还没订阅的补上。
    ///
    /// 为什么要反复扫：
    ///   关卡内容在选关之后才会被激活，而 Start 只跑一次。
    /// </summary>
    private void RefreshSubscriptions()
    {
        if (listenItemApplied)
        {
            foreach (var part in FindObjectsByType<PatientBodyPart>(FindObjectsInactive.Include))
            {
                if (part == null) continue;

                // 没订阅过就订阅
                if (!subscribedParts.Contains(part))
                {
                    part.OnItemApplied += HandleItemApplied;
                    subscribedParts.Add(part);

                    if (logToConsole)
                        Debug.Log($"[步骤桥接] 订阅身体部位「{part.part}」");
                }
            }
        }

        if (listenBoxOpened)
        {
            foreach (var box in FindObjectsByType<OpenableBox>(FindObjectsInactive.Include))
            {
                if (box == null) continue;

                if (!subscribedBoxes.Contains(box))
                {
                    box.OnOpened += HandleBoxOpened;
                    subscribedBoxes.Add(box);

                    if (logToConsole)
                        Debug.Log($"[步骤桥接] 订阅医疗箱「{box.name}」");
                }
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var part in subscribedParts)
            if (part != null) part.OnItemApplied -= HandleItemApplied;
        subscribedParts.Clear();

        foreach (var box in subscribedBoxes)
            if (box != null) box.OnOpened -= HandleBoxOpened;
        subscribedBoxes.Clear();
    }

    // ⚠️ 已移除 F1~F5 快捷键上报。
    //    步骤必须靠【真实玩法】完成：
    //      · 摆放警示标志  → PlacementZone 判定
    //      · 制动固定/RICE/保温 → 把道具用在身体部位上（本脚本监听）
    //      · 呼救/交接     → 由专门的交互脚本上报

    private void Update()
    {
        // 定时补订阅：关卡内容可能在 Start 之后才被激活
        rescanTimer += Time.deltaTime;
        if (rescanTimer >= RescanInterval)
        {
            rescanTimer = 0f;
            RefreshSubscriptions();
        }
    }

    // ==================== 事件处理 ====================
    private void HandleItemApplied(PatientBodyPart part, string itemTag)
    {
        // ⭐ 特例：第 1 步「环境安全评估」还没做，就先拿夹板处理腿
        //    → 第 1 步记为未完成（扣分），直接前进到第 2 步，
        //      并且【一步到位】把第 2 步也完成。
        if (itemTag == "Splint" && IsAtStep(1))
        {
            StartCoroutine(SplintSkipsFirstStep(part));
            return;
        }

        string actionId = MapItem(itemTag);
        if (string.IsNullOrEmpty(actionId)) return;

        Report(actionId, $"使用「{itemTag}」于「{part.part}」");
    }

    private bool IsAtStep(int stepNumber)
    {
        if (stepController == null) return false;

        var step = stepController.CurrentStep;
        return step != null && step.stepIndex == stepNumber;
    }

    /// <summary>
    /// 夹板跳过第 1 步的专用流程：
    ///   ① 第 1 步记为「未完成」并前进（ForceNext 会广播 OnStepCompleted(step,false)）
    ///   ② 等真正进入第 2 步
    ///   ③ 立刻用 ApplySplint 把第 2 步判定为完成
    /// </summary>
    private System.Collections.IEnumerator SplintSkipsFirstStep(PatientBodyPart part)
    {
        if (stepController == null)
        {
            Debug.LogWarning("[步骤桥接] 找不到 StepController，无法执行夹板跳步特例");
            yield break;
        }

        if (logToConsole)
            Debug.Log($"[步骤桥接] 特例：第 1 步时用夹板处理「{part.part}」" +
                      $" → 跳过第 1 步并直接完成第 2 步", this);

        int beforeNumber = stepController.CurrentStepNumber;

        // ① 第 1 步记为未完成并前进
        stepController.ForceNext();

        // ② 等它真正推进（ForceNext 有 consequenceDelay 延迟）
        float waited = 0f;
        while (!stepController.IsFinished
               && stepController.CurrentStepNumber == beforeNumber
               && waited < 5f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // 再多等一帧，让 BeginStep 把 isProcessing 清掉
        yield return null;

        if (stepController.IsFinished)
        {
            if (logToConsole) Debug.Log("[步骤桥接] 夹板跳步特例：步骤已全部结束", this);
            yield break;
        }

        // ③ 第 2 步直接完成
        if (logToConsole)
            Debug.Log($"[步骤桥接] 夹板跳步特例：第 {stepController.CurrentStepNumber} 步" +
                      $"「{(stepController.CurrentStep != null ? stepController.CurrentStep.stepName : "?")}」" +
                      $" → 直接判定完成", this);

        stepController.ReportAction(MapItem("Splint") ?? "ApplySplint");
    }

    private void HandleBoxOpened(OpenableBox box)
    {
        // ⭐ 只有当前步骤【真的要求】开箱时，才把「开箱」当成一次动作上报。
        //
        //   之前无条件上报的后果：
        //     第 1 步要求放警示牌，玩家一开局就打开医疗箱
        //     → 上报 "OpenMedicalKit" → 不匹配 → 被判成做错
        //     → 时间轴多一条「[关键错误] 环境安全评估」
        //     → 但玩家明明什么都没做错。
        //
        //   医疗箱只是取道具的容器，本身不是急救动作。
        if (!CurrentStepNeeds(boxOpenActionId))
        {
            if (logToConsole)
                Debug.Log($"[步骤桥接] 打开「{box.name}」，" +
                          $"但当前步骤不需要 '{boxOpenActionId}' → 不上报（不算错）", this);
            return;
        }

        Report(boxOpenActionId, $"打开「{box.name}」");
    }

    /// <summary>当前步骤的 requiredActionId 里是否包含这个动作</summary>
    private bool CurrentStepNeeds(string actionId)
    {
        if (stepController == null) return false;
        if (string.IsNullOrEmpty(actionId)) return false;

        var step = stepController.CurrentStep;
        if (step == null) return false;
        if (string.IsNullOrEmpty(step.requiredActionId)) return false;

        foreach (var a in step.requiredActionId.Split(new[] { ',', '，', ';', '；' },
                                                       System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (a.Trim() == actionId) return true;
        }

        return false;
    }

    private string MapItem(string itemTag)
    {
        if (string.IsNullOrEmpty(itemTag)) return null;

        foreach (var m in itemMaps)
        {
            if (m == null) continue;
            if (m.itemTag == itemTag) return m.actionId;
        }

        // 没配映射就直接用道具名（方便临时测试）
        return itemTag;
    }

    private void Report(string actionId, string why)
    {
        if (string.IsNullOrEmpty(actionId)) return;

        if (logToConsole)
            Debug.Log($"[步骤桥接] 上报 '{actionId}'   （{why}）");

        stepController.ReportAction(actionId);
    }
}
