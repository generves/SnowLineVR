using UnityEngine;

/// <summary>
/// 动画切换测试器 —— 运行时用键盘切动画，方便快速预览每个动作。
///
/// 挂在人物模型上即可。按数字键 0-8 切动画。
///
/// 快捷键：
///   数字键 0~8   → 切换到对应索引的动画
///   ← / →        → 上一个 / 下一个动画
///   空格         → 暂停 / 继续动画播放
///   R            → 回到第一个动画
///
/// 注意：XR Device Simulator 占用了 W A S D Q E Z X C V 1 2 3 T R H 等键，
///       所以本脚本用【方向键】和【数字键 4~9】这些没被占用的键。
/// </summary>
public class AnimationTester : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("留空则自动找本物体或子物体上的 Animator")]
    public Animator animator;

    [Header("状态名（会自动从 Animator Controller 读取）")]
    public string[] stateNames;

    [Header("显示")]
    [Tooltip("在屏幕上显示当前动画名。正式测试时关掉")]
    public bool showOnScreen = false;

    [Tooltip("屏幕文字大小")]
    public int fontSize = 28;

    [Tooltip("是否允许用键盘切动画 / 暂停。正式测试时关掉，避免误按")]
    public bool allowHotkeys = false;

    private int currentIndex = 0;
    private float speedBeforePause = 1f;
    private bool paused = false;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        LoadStateNames();
    }

    private void LoadStateNames()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        var ctrl = animator.runtimeAnimatorController;
        var states = ctrl.animationClips;
        if (states == null || states.Length == 0) return;

        // 从 Controller 的层状态里取名字
        var layerStates = animator.runtimeAnimatorController.animationClips;
        var names = new System.Collections.Generic.List<string>();

        // RuntimeAnimatorController 拿不到状态名，用 AnimationClip 名字兜底
        foreach (var c in layerStates)
        {
            if (c == null) continue;
            names.Add(c.name);
        }

        if (names.Count > 0) stateNames = names.ToArray();
    }

    private void Update()
    {
        if (animator == null) return;

        // 正式测试时把 allowHotkeys 关掉 —— 避免误按切动画 / 暂停
        if (!allowHotkeys) return;

        // ---------- 数字键 4~9 → 索引 3~8 ----------
        // （1/2/3 被 XR Device Simulator 占用了）
        for (int i = 3; i <= 8; i++)
        {
            if (GetKeyDown(KeyCode.Alpha0 + (i - 3) + 4))
                SwitchTo(i);
        }

        // 数字键 0 → 索引 0
        if (GetKeyDown(KeyCode.Alpha0)) SwitchTo(0);

        // ---------- 方向键切换 ----------
        if (GetKeyDown(KeyCode.RightArrow)) SwitchTo(currentIndex + 1);
        if (GetKeyDown(KeyCode.LeftArrow)) SwitchTo(currentIndex - 1);

        // ---------- 暂停 / 回到开头 ----------
        // 注意：空格和 R 都被 XR Device Simulator 占用了，所以用回车和退格
        if (GetKeyDown(KeyCode.Return)) TogglePause();
        if (GetKeyDown(KeyCode.Backspace)) SwitchTo(0);
    }

    /// <summary>切到指定索引的动画</summary>
    public void SwitchTo(int index)
    {
        if (animator == null) return;

        int count = GetStateCount();
        if (count == 0) return;

        // 循环
        index = ((index % count) + count) % count;
        currentIndex = index;

        animator.SetInteger("State", index);
        animator.SetTrigger("Switch");

        string name = GetStateName(index);
        Debug.Log($"[动画测试] 切到 [{index}] {name}");
    }

    /// <summary>切到指定名字的动画</summary>
    public void SwitchTo(string stateName)
    {
        if (stateNames == null) return;
        for (int i = 0; i < stateNames.Length; i++)
        {
            if (stateNames[i] == stateName) { SwitchTo(i); return; }
        }
        Debug.LogWarning($"[动画测试] 找不到状态 '{stateName}'");
    }

    private void TogglePause()
    {
        if (animator == null) return;

        if (paused)
        {
            animator.speed = speedBeforePause;
            paused = false;
            Debug.Log("[动画测试] 继续播放");
        }
        else
        {
            speedBeforePause = animator.speed;
            animator.speed = 0f;
            paused = true;
            Debug.Log("[动画测试] 已暂停");
        }
    }

    private int GetStateCount()
    {
        if (stateNames != null && stateNames.Length > 0) return stateNames.Length;
        if (animator != null && animator.runtimeAnimatorController != null)
            return animator.runtimeAnimatorController.animationClips.Length;
        return 0;
    }

    private string GetStateName(int index)
    {
        if (stateNames != null && index >= 0 && index < stateNames.Length)
            return stateNames[index];
        return $"索引 {index}";
    }

    // ---------- 输入兼容：新旧输入系统都能用 ----------
    private bool GetKeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // 纯新输入系统
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        var ctrl = MapToNewInput(key);
        return ctrl != null && ctrl.wasPressedThisFrame;
#else
        return Input.GetKeyDown(key);
#endif
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private UnityEngine.InputSystem.Controls.KeyControl MapToNewInput(KeyCode key)
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return null;

        switch (key)
        {
            case KeyCode.Alpha0: return kb.digit0Key;
            case KeyCode.Alpha4: return kb.digit4Key;
            case KeyCode.Alpha5: return kb.digit5Key;
            case KeyCode.Alpha6: return kb.digit6Key;
            case KeyCode.Alpha7: return kb.digit7Key;
            case KeyCode.Alpha8: return kb.digit8Key;
            case KeyCode.Alpha9: return kb.digit9Key;
            case KeyCode.LeftArrow: return kb.leftArrowKey;
            case KeyCode.RightArrow: return kb.rightArrowKey;
            case KeyCode.Return: return kb.enterKey;
            case KeyCode.Backspace: return kb.backspaceKey;
            default: return null;
        }
    }
#endif

    private void OnGUI()
    {
        if (!showOnScreen) return;
        if (animator == null) return;

        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.normal.textColor = Color.yellow;
        style.fontStyle = FontStyle.Bold;

        string txt = $"动画 [{currentIndex}] {GetStateName(currentIndex)}";
        if (paused) txt += "   (已暂停)";
        txt += "\n数字键 0 / 4~9 切换    ← → 上一个/下一个    回车 暂停    退格 回开头";

        GUI.Label(new Rect(20, 20, 1400, 100), txt, style);
    }
}
