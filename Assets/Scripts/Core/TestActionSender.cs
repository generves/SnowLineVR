using UnityEngine;
using UnityEngine.InputSystem;   // 新版 Input System（本工程启用的是这一套，不能用旧的 Input.GetKeyDown）

/// <summary>
/// 【临时调试脚本】按空格键上报一个动作。
///
/// 用途：在还没有做真实交互（抓取道具、按住肩膀）之前，
/// 用它来验证 StepController 的判定、推进、事件广播是否正常工作。
///
/// ⚠️ 等真正的交互做完后，这个脚本要删掉或禁用。
/// </summary>
public class TestActionSender : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private StepController controller;

    [Header("要上报的动作 ID")]
    [Tooltip("按哪个键（新版 Input System 的 Key 枚举，例如 Space、Enter、T）")]
    [SerializeField] private Key testKey = Key.Space;

    [Tooltip("按下去上报哪个动作 ID。要和 FirstAidStep.requiredActionId 完全一致")]
    [SerializeField] private string actionId = "PlaceWarningSign";

    [Header("自动模式")]
    [Tooltip("勾选后每一秒自动上报一次（方便连续测试 7 步）")]
    [SerializeField] private bool autoAdvance = false;

    private float timer;

    private void Update()
    {
        if (controller == null) return;

        // 自动模式：定时上报，不用手按
        if (autoAdvance)
        {
            timer += Time.deltaTime;
            if (timer >= 1f)
            {
                timer = 0f;
                controller.ReportAction(actionId);
            }
            return;
        }

        // 手动模式：监听按键
        // Keyboard.current 在没有任何键盘设备时为 null，所以要判空
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        var control = keyboard[testKey];
        if (control != null && control.wasPressedThisFrame)
        {
            controller.ReportAction(actionId);
        }
    }
}
