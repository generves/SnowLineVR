using UnityEngine;

/// <summary>
/// 日志总开关 —— 用来控制整个项目里"开发调试日志"的量。
///
/// 用法：
///   ① 挂在 GameManager 上（或者什么都不挂，静态也能用）
///   ② 在 Inspector 里切换等级，全项目的日志会跟着变
///
/// 或者用键盘快捷键：F10 在【安静 / 详细】之间切换
///
/// 设计说明：
///   · Unity 的 Debug.Log 在 VR 里开销不小（每帧几百条会明显掉帧），
///     而且 Console 刷屏会让人看不到真正的错误。
///   · 所以开发时用详细，演示/录制时切安静。
/// </summary>
public class LogControl : MonoBehaviour
{
    public enum Level
    {
        安静,       // 只输出警告和错误
        正常,       // 关键节点输出（默认）
        详细,       // 全部输出（调试用）
    }

    [Tooltip("日志等级")]
    public Level level = Level.正常;

    [Tooltip("允许用键盘 F10 快捷切换 安静/详细")]
    public bool allowHotkey = true;

    /// <summary>全局等级（其他脚本通过它判断要不要打日志）</summary>
    public static Level Current { get; private set; } = Level.正常;

    // ---------- 供其他脚本调用的判断 ----------
    /// <summary>要不要打【详细】日志（最啰嗦的那些）</summary>
    public static bool Verbose => Current == Level.详细;

    /// <summary>要不要打【正常】日志（关键节点）</summary>
    public static bool Normal => Current != Level.安静;

    private void Awake()
    {
        Current = level;
    }

    private void OnDestroy()
    {
        // 恢复默认，避免影响下一次 Play
        Current = Level.正常;
    }

    private void Update()
    {
        if (!allowHotkey) return;
        if (!WasTogglePressed()) return;

        // 在【安静】和【详细】之间切换
        level = (level == Level.详细) ? Level.安静 : Level.详细;
        Current = level;

        Debug.Log($"[日志] 等级切换为【{level}】" +
                  (level == Level.安静 ? "（只输出警告和错误）" : "（全部输出）"));
    }

    private bool WasTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        return kb != null && kb.f10Key != null && kb.f10Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F10);
#endif
    }

    private void OnValidate()
    {
        if (Application.isPlaying) Current = level;
    }
}
