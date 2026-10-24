using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 交互后旋转 —— 被射线指着会高亮，按交互键后绕本地 X 轴转到指定角度，然后不再可交互。
///
/// 典型用途：担架床头板（Stretcher_Headboard）扣下来。
///
/// ===================== 怎么工作 =====================
///
///   ① 高亮：由 XRayHoverHighlight 组件负责（它读 XRSimpleInteractable 的 isHovered）
///   ② 交互：PropInteractor 的交互键（F1/F2/F3）射线指着本物体时，会调用 Interact()
///   ③ 旋转：把本地 X 从当前角度平滑转到 Target Angle X（默认 -90）
///   ④ 结束：标记为已完成，并可选地关掉 XRSimpleInteractable / Collider
///            —— 关掉交互组件后高亮也会自动消失
///
/// ===================== 设置要求 =====================
///
///   本物体上需要：
///     · Collider（射线才打得到）
///     · XRSimpleInteractable（高亮组件要用它的 isHovered）
///     · XRayHoverHighlight（高亮轮廓）
///     · 本组件
///
///   ⚠️ 如果不想高亮，可以不加 XRayHoverHighlight，本脚本照样工作。
///
/// ===================== 用法 =====================
///
///   ① 选中 Stretcher_Headboard → Add Component → 搜 Rotate On Interact
///   ② 填：
///        Target Angle X = -90
///        Duration       = 1.2
///        Can Interact   = ☑
///        Once           = ☑
///        Disable Interactable After Done = ☑
///   ③ Play，用射线指着它按交互键
/// </summary>
[DisallowMultipleComponent]
public class RotateOnInteract : MonoBehaviour
{
    [Header("旋转")]
    [Tooltip("要转到多少度（本地 X 轴）。默认 -90 = 向前扣下")]
    public float targetAngleX = -90f;

    [Tooltip("从开始转到结束用多少秒")]
    [Range(0.05f, 10f)]
    public float duration = 1.2f;

    [Tooltip("缓动曲线。默认平滑起步、平滑停止")]
    public bool useEase = true;

    [Header("交互")]
    [Tooltip("当前是否允许交互。完成后会自动关掉")]
    public bool canInteract = true;

    [Tooltip("只能交互一次")]
    public bool once = true;

    [Header("完成之后")]
    [Tooltip("关掉 XRSimpleInteractable —— 之后射线不再识别它，高亮也不会再出现")]
    public bool disableInteractableAfterDone = true;

    [Tooltip("关掉 Collider —— 只有当你希望射线完全穿过去时才需要")]
    public bool disableColliderAfterDone = false;

    [Header("上报给步骤系统（可选）")]
    [Tooltip("旋转完成后向 StepController 上报的动作 ID。留空 = 不上报。\n" +
             "例如填 ReportToRescuer，用来完成最后一步「交接与病情汇报」")]
    public string completeActionId = "";

    [Tooltip("步骤控制器。留空自动查找")]
    public StepController stepController;

    [Header("调试")]
    public bool logToConsole = false;

    // ---------- 内部 ----------
    private XRSimpleInteractable interactable;
    private Collider selfCollider;

    private float startAngleX;
    private float baseY;
    private float baseZ;

    private bool busy;
    private bool done;

    /// <summary>正在播旋转动画</summary>
    public bool IsBusy => busy;

    /// <summary>已经完成</summary>
    public bool IsDone => done;

    /// <summary>旋转结束时触发</summary>
    public event System.Action<RotateOnInteract> OnFinished;

    private void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        if (interactable == null) interactable = GetComponentInParent<XRSimpleInteractable>();

        selfCollider = GetComponent<Collider>();

        // 记下初始朝向：只动 X，Y/Z 保持不变
        Vector3 euler = transform.localRotation.eulerAngles;
        startAngleX = euler.x;
        baseY = euler.y;
        baseZ = euler.z;

        // 角度换算到 [-180, 180]，避免 0 变成 360 之类的问题
        startAngleX = NormalizeAngle(startAngleX);

        if (logToConsole)
            Debug.Log($"[旋转交互] 「{name}」初始角度 X = {startAngleX:F1}°，" +
                      $"目标 X = {targetAngleX:F1}°", this);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// 执行一次交互。由 PropInteractor 的交互键调用，
    /// 也可以在 Inspector 右键菜单手动测试。
    /// </summary>
    public void Interact()
    {
        if (done && once)
        {
            if (logToConsole) Debug.Log($"[旋转交互] 「{name}」已经完成过，忽略", this);
            return;
        }

        if (busy) return;

        if (!canInteract)
        {
            if (logToConsole) Debug.Log($"[旋转交互] 「{name}」当前不允许交互", this);
            return;
        }

        StartCoroutine(AnimateRotation());
    }

    private System.Collections.IEnumerator AnimateRotation()
    {
        busy = true;

        if (logToConsole)
            Debug.Log($"[旋转交互] 「{name}」开始旋转：X {startAngleX:F1}° → {targetAngleX:F1}°，" +
                      $"用时 {duration:F2} 秒", this);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            if (useEase) t = Mathf.SmoothStep(0f, 1f, t);

            ApplyAngleX(Mathf.Lerp(startAngleX, targetAngleX, t));

            yield return null;
        }

        ApplyAngleX(targetAngleX);

        busy = false;
        done = true;

        FinishUp();

        // ---------- 上报给步骤系统 ----------
        if (!string.IsNullOrEmpty(completeActionId))
        {
            if (stepController == null) stepController = FindAnyObjectByType<StepController>();

            if (stepController != null)
            {
                stepController.ReportAction(completeActionId);

                if (logToConsole)
                    Debug.Log($"[旋转交互] 「{name}」已上报动作 '{completeActionId}'", this);
            }
            else
            {
                Debug.LogWarning($"[旋转交互] 「{name}」找不到 StepController，" +
                                 $"无法上报 '{completeActionId}'", this);
            }
        }

        if (logToConsole)
            Debug.Log($"[旋转交互] 「{name}」旋转完成，X = {targetAngleX:F1}°，已不可再交互", this);

        OnFinished?.Invoke(this);
    }

    /// <summary>设置本地 X 角度，Y/Z 保持不变</summary>
    private void ApplyAngleX(float x)
    {
        transform.localRotation = Quaternion.Euler(x, baseY, baseZ);
    }

    /// <summary>完成后的清理：关交互组件 / 碰撞体</summary>
    private void FinishUp()
    {
        canInteract = false;

        if (disableInteractableAfterDone && interactable != null)
        {
            interactable.enabled = false;

            if (logToConsole)
                Debug.Log($"[旋转交互] 「{name}」已关闭 XRSimpleInteractable（不会再高亮）", this);
        }

        if (disableColliderAfterDone && selfCollider != null)
        {
            selfCollider.enabled = false;

            if (logToConsole)
                Debug.Log($"[旋转交互] 「{name}」已关闭 Collider", this);
        }
    }

    /// <summary>手动交互一次（Inspector 右键菜单，方便测试）</summary>
    [ContextMenu("手动交互一次")]
    public void InteractFromMenu()
    {
        Interact();
    }

    /// <summary>恢复初始状态（重新开始训练时用）</summary>
    [ContextMenu("恢复初始状态")]
    public void ResetState()
    {
        StopAllCoroutines();

        busy = false;
        done = false;
        canInteract = true;

        ApplyAngleX(startAngleX);

        if (interactable != null) interactable.enabled = true;
        if (selfCollider != null) selfCollider.enabled = true;

        if (logToConsole)
            Debug.Log($"[旋转交互] 「{name}」已恢复初始状态（X = {startAngleX:F1}°）", this);
    }
}
