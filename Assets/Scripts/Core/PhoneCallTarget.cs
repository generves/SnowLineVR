using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 呼救交互 —— 玩家拿【手机】对着这个东西按交互键，就上报"呼救"动作。
///
/// 本关卡用在校内【路牌】上（第 4 步：呼救与信息报送）：
///   玩家从医疗箱拿出手机 → 射线指着路牌 → 按交互键
///   → 读出雪道编号 → 完成第 4 步
///
/// ⚠️ 本脚本【不继承】XRSimpleInteractable —— 路牌上已经有这个组件了
///    （XRSimpleInteractable 带 DisallowMultipleComponent，继承会冲突）。
///    所以这里只是订阅已有组件的事件。
///
/// 设置要求：
///   ① 本物体上要有 XRSimpleInteractable（路牌上已有）
///   ② 本物体上要有 Collider（路牌上已有 MeshCollider）
///   ③ 把本脚本挂上去即可，会自动找到同物体上的 XRSimpleInteractable
///
/// 用法：
///   ① 选中场景里的【路牌】
///   ② Add Component → 搜 Phone Call Target → 添加
///   ③ 填：
///        Required Item Tag = Phone
///        Action Id         = CallRescue
///        Read Sign Number  = ☑ （路牌上有 TrailSignNumber 时）
///        Only During Matching Step = ☑
/// </summary>
public class PhoneCallTarget : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("要监听哪个交互组件。留空 = 自动找本物体上的 XRSimpleInteractable")]
    public XRSimpleInteractable interactable;

    [Header("判定")]
    [Tooltip("要拿哪个道具来交互（对应 ItemTag.tagName）。留空 = 空手也能交互")]
    public string requiredItemTag = "Phone";

    [Tooltip("交互成功后上报给 StepController 的动作 ID")]
    public string actionId = "CallRescue";

    [Tooltip("只能成功一次")]
    public bool once = true;

    [Header("路牌编号")]
    [Tooltip("勾上 = 交互时把路牌上的编号读出来，写进提示里")]
    public bool readSignNumber = true;

    [Tooltip("路牌上的 TrailSignNumber。留空会自动在本物体和子物体里找")]
    public TrailSignNumber signNumber;

    [Header("限定步骤（可选）")]
    [Tooltip("勾上后，只有当【正在进行的这一步】需要这个 Action Id 时才可交互。\n" +
             "用来防止学员在错误的步骤里乱按")]
    public bool onlyDuringMatchingStep = true;

    [Header("反馈")]
    [Tooltip("成功提示。{0} 会被替换成路牌编号")]
    public string successMessage = "已拨打救援电话，报告雪道编号：{0} 号道";

    [Tooltip("路牌编号读不到时的成功提示")]
    public string successMessageNoNumber = "已拨打救援电话并报送位置";

    [Tooltip("手上道具不对时的提示。{0} = 手上的道具名")]
    public string wrongItemMessage = "「{0}」不能用来呼救，请先找到手机";

    [Tooltip("空手时的提示")]
    public string emptyHandMessage = "需要先从医疗箱里取出手机";

    [Header("调试")]
    public bool logToConsole = false;

    // ---------- 内部 ----------
    private bool done;

    private void Awake()
    {
        if (interactable == null)
            interactable = GetComponent<XRSimpleInteractable>();

        if (interactable == null)
        {
            Debug.LogWarning($"[呼救] {name} 上没有 XRSimpleInteractable，" +
                             $"请先 Add Component → XR Simple Interactable");
            return;
        }

        if (readSignNumber && signNumber == null)
            signNumber = GetComponentInChildren<TrailSignNumber>(true);

        interactable.selectEntered.AddListener(OnSelected);

        if (logToConsole)
            Debug.Log($"[呼救] {name} 已接上 XRSimpleInteractable" +
                      (signNumber != null ? $"，路牌编号 = {signNumber.CurrentValue}" : "（没找到路牌编号）"));
    }

    private void OnDestroy()
    {
        if (interactable != null)
            interactable.selectEntered.RemoveListener(OnSelected);
    }

    private void OnSelected(SelectEnterEventArgs args)
    {
        // 走 XRI 事件这条路径时，自己去找手上拿的道具
        TryInteract(null);
    }

    /// <summary>
    /// 主动触发一次呼救交互。
    ///
    /// 两条调用来源：
    ///   ① XRI 的 selectEntered（真正用控制器扳机时）
    ///   ② PropInteractor.HandleInteract()（本工程的调试交互键 F1/F2/F3）
    ///      —— 因为那套按键不驱动 XRI 的选择事件，所以要主动调这里
    ///
    /// heldItem 传 null 时会自动去找正握着道具的那只手。
    /// </summary>
    public void TryInteract(ItemPhysicsSetup heldItem)
    {
        if (once && done) return;

        if (onlyDuringMatchingStep && !IsCurrentStepExpectingThis())
        {
            if (logToConsole)
                Debug.Log($"[呼救] {name}：当前步骤不需要 '{actionId}'，忽略这次交互");
            return;
        }

        // ---------- 手上拿的是什么 ----------
        string itemTag = null;
        string itemName = null;

        if (heldItem != null)
        {
            itemTag = heldItem.ItemId;
            itemName = heldItem.ItemDisplayName;
        }
        else
        {
            var pi = FindHeldPropInteractor();
            if (pi != null && pi.HeldItem != null)
            {
                itemTag = pi.HeldItem.ItemId;
                itemName = pi.HeldItem.ItemDisplayName;
            }
        }

        if (logToConsole)
            Debug.Log($"[呼救] {name}：交互触发，手上道具 = " +
                      $"{(string.IsNullOrEmpty(itemTag) ? "（空手）" : itemTag)}");

        // ---------- 空手 ----------
        if (string.IsNullOrEmpty(itemTag))
        {
            ShowMessage(emptyHandMessage, true);
            return;
        }

        // ---------- 道具不对 ----------
        if (!string.IsNullOrEmpty(requiredItemTag) && itemTag != requiredItemTag)
        {
            ShowMessage(string.Format(wrongItemMessage,
                                      string.IsNullOrEmpty(itemName) ? itemTag : itemName), true);
            return;
        }

        // ---------- 成功 ----------
        done = true;

        string msg;
        if (readSignNumber && signNumber != null)
            msg = string.Format(successMessage, signNumber.CurrentValue);
        else
            msg = successMessageNoNumber;

        ShowMessage(msg, false);

        if (logToConsole)
            Debug.Log($"[呼救] 用「{itemName}」与「{name}」交互" +
                      (signNumber != null ? $"，雪道编号 {signNumber.CurrentValue}" : "") +
                      $" → 上报 '{actionId}'");

        var sc = FindAnyObjectByType<StepController>();
        if (sc != null && !string.IsNullOrEmpty(actionId))
            sc.ReportAction(actionId);
        else if (sc == null)
            Debug.LogWarning("[呼救] 场景里找不到 StepController，无法推进步骤");
    }

    /// <summary>当前正在进行的这一步，是不是正好需要我这个 Action Id</summary>
    private bool IsCurrentStepExpectingThis()
    {
        if (string.IsNullOrEmpty(actionId)) return true;

        var sc = FindAnyObjectByType<StepController>();
        if (sc == null) return true;          // 没步骤系统就不管

        var step = sc.CurrentStep;
        if (step == null) return true;

        if (string.IsNullOrEmpty(step.requiredActionId)) return true;

        // requiredActionId 可能是 "CallRescue" 或 "ApplyIcePack,ApplyBandage"
        foreach (var p in step.requiredActionId.Split(new[] { ',', '，', ';', '；' },
                                                       System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (p.Trim() == actionId) return true;
        }

        return false;
    }

    /// <summary>找出正握着道具的那个交互器</summary>
    private static PropInteractor FindHeldPropInteractor()
    {
        foreach (var pi in PropInteractor.Active)
        {
            if (pi == null) continue;
            if (pi.HeldItem != null) return pi;
        }
        return null;
    }

    private static void ShowMessage(string msg, bool warning)
    {
        if (string.IsNullOrEmpty(msg)) return;

        var hint = FindAnyObjectByType<HintPanel>();
        if (hint == null) return;

        if (warning) hint.ShowWarning(msg);
        else hint.ShowSuccess(msg);
    }

    /// <summary>供外部重置（重新训练时用）</summary>
    public void ResetTarget() => done = false;
}
