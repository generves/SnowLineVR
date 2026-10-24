using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;   // Key 类型在这里

/// <summary>
/// 事件选择按钮 —— 供「模拟事件选择」界面使用。
///
/// 为什么不用 UI 的 Button.onClick：
///   本工程是纯新版 Input System，XRUIInputModule 的鼠标输入失效，
///   World Space UI 的射线点击在编辑器里都点不动，头显里更不可靠。
///   所以改成三维交互：按钮带 BoxCollider + XRSimpleInteractable，
///   玩家用射线/手柄指向它并按扳机就能触发。
///
/// 同时保留键盘快捷方式，方便编辑器测试。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ScenarioButton : MonoBehaviour
{
    [Header("索引")]
    [Tooltip("对应 ScenarioManager 里第几个事件（从 0 开始）")]
    [SerializeField] private int scenarioIndex;

    [Header("颜色反馈")]
    [SerializeField] private Color normalColor = new Color(0.18f, 0.32f, 0.46f, 0.95f);
    [SerializeField] private Color hoverColor = new Color(0.28f, 0.48f, 0.68f, 1f);
    [SerializeField] private Color pressedColor = new Color(0.35f, 0.65f, 0.9f, 1f);

    [Header("键盘测试")]
    [Tooltip("【调试用】勾上后可以用键盘选事件。正式测试时关掉，避免误按")]
    [SerializeField] private bool allowKeyboardShortcut = false;

    [Tooltip("按这个键也能选择（模拟器没占用的键）。None = 不用")]
    [SerializeField] private Key testKey = Key.None;

    private UnityEngine.UI.Image background;
    private ScenarioManager manager;

    private void Awake()
    {
        background = GetComponent<UnityEngine.UI.Image>();
        if (background != null) normalColor = background.color;

        manager = FindAnyObjectByType<ScenarioManager>();

        // ⚠️ 已移除"自动分配数字键 1/2/3"的逻辑：
        //    那会让按 1 直接跳到第一个事件，绕过事件选择界面。
        //    现在必须手动勾上 allowKeyboardShortcut 并填 testKey 才能用键盘。
    }

    private void Update()
    {
        if (!allowKeyboardShortcut) return;
        if (testKey == Key.None) return;
        if (!gameObject.activeInHierarchy) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        var c = kb[testKey];
        if (c != null && c.wasPressedThisFrame) OnSelected();
    }

    // ---------- 供 XRSimpleInteractable 事件调用 ----------
    /// <summary>接到 XRSimpleInteractable 的 Select Entered</summary>
    public void OnSelectEntered(SelectEnterEventArgs args) => OnSelected();

    /// <summary>接到 XRSimpleInteractable 的 Hover Entered</summary>
    public void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (background != null) background.color = hoverColor;
    }

    /// <summary>接到 XRSimpleInteractable 的 Hover Exited</summary>
    public void OnHoverExited(HoverExitEventArgs args)
    {
        if (background != null) background.color = normalColor;
    }

    /// <summary>被选中（也可直接由 Button.onClick 调用，兼容鼠标可用的情况）</summary>
    public void OnSelected()
    {
        if (background != null) background.color = pressedColor;

        if (manager == null) manager = FindAnyObjectByType<ScenarioManager>();
        if (manager == null)
        {
            Debug.LogError("[ScenarioButton] 找不到 ScenarioManager，无法选择事件");
            return;
        }

        Debug.Log($"[ScenarioButton] 选择了事件索引 {scenarioIndex}");
        manager.SelectScenario(scenarioIndex);
    }

    /// <summary>供编辑器工具设置索引</summary>
    public void SetIndex(int i) => scenarioIndex = i;
}
