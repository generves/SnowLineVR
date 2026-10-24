using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 关闭按钮 —— 点一下隐藏指定面板。用于报告面板右上角的「关闭」。
///
/// 提供两条关闭途径：
///   1. 鼠标点击 / VR 手柄射线点击（走 EventSystem 射线检测）
///   2. 键盘快捷键（保底，不依赖任何 UI 射线）
///
/// 为什么要有键盘保底：UI 射线在 VR 里受 Canvas 事件相机、输入模块、
/// 阻挡层等多种因素影响，容易失效。有个不依赖 UI 的关闭方式更可靠。
/// </summary>
public class ClosePanelButton : MonoBehaviour
{
    [Tooltip("点击后要隐藏的物体（一般是整个面板根物体）")]
    [SerializeField] private GameObject targetPanel;

    [Header("键盘快捷键（保底）")]
    [Tooltip("按这个键也能关闭面板。选的是 XR Device Simulator 没占用的键")]
    [SerializeField] private Key closeKey = Key.Backspace;

    [Tooltip("空格键也能关（空格被模拟器占用了，但作为备用多一个选择）")]
    [SerializeField] private bool alsoAllowSpace = true;

    private void Awake()
    {
        // 没指定目标就自动往上找面板根物体
        if (targetPanel == null) targetPanel = FindPanelRoot();
    }

    private void Update()
    {
        // 只有面板显示着的时候才响应
        if (targetPanel == null || !targetPanel.activeInHierarchy) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        bool pressed = false;

        var c = kb[closeKey];
        if (c != null && c.wasPressedThisFrame) pressed = true;

        if (alsoAllowSpace)
        {
            var sp = kb[Key.Space];
            if (sp != null && sp.wasPressedThisFrame) pressed = true;
        }

        if (pressed) Close();
    }

    /// <summary>给 UI 的 Button.onClick 调用</summary>
    public void Close()
    {
        if (targetPanel == null) targetPanel = FindPanelRoot();

        if (targetPanel == null)
        {
            Debug.LogWarning("[ClosePanelButton] 没有找到要关闭的面板");
            return;
        }

        targetPanel.SetActive(false);
        Debug.Log($"[ClosePanelButton] 已关闭面板：{targetPanel.name}");
    }

    /// <summary>往上找到面板根物体（名字以 HUD_ 或 ReportPanel 开头的那个）</summary>
    private GameObject FindPanelRoot()
    {
        var t = transform;
        GameObject fallback = null;

        while (t != null)
        {
            if (t.name.StartsWith("HUD_") || t.name.StartsWith("ReportPanel")) return t.gameObject;
            if (fallback == null && t.GetComponent<Canvas>() != null) fallback = t.gameObject;
            t = t.parent;
        }

        return fallback;
    }

    /// <summary>供编辑器工具设置目标面板</summary>
    public void SetTargetPanel(GameObject panel)
    {
        targetPanel = panel;
    }
}
