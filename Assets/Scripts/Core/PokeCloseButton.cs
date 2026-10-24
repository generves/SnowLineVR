using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;   // SelectEnterEventArgs / HoverEnterEventArgs 等

/// <summary>
/// 三维「碰一下」按钮 —— 不依赖 UI 射线检测，用虚拟手或手柄直接触碰触发。
///
/// 为什么要做这个：
///   本工程是纯新版 Input System，而 XRUIInputModule 的鼠标输入走的是旧版
///   Input Manager（源码注释原文：enableMouseInput 在使用了 Input System UI Actions
///   时会被忽略），所以鼠标点击 World Space UI 按钮会失效。
///   把按钮做成带碰撞体的三维物体、靠 XR 交互器触碰触发，就绕开了整套 UI 射线管道。
///
/// 同时保留键盘保底，确保任何情况下都能关闭面板。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class PokeCloseButton : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("要关闭的面板。留空会自动往上找 HUD_ 开头的根物体")]
    [SerializeField] private GameObject targetPanel;

    [Header("键盘保底")]
    [Tooltip("按这个键关闭面板（不依赖任何 UI 射线）。默认退格键")]
    [SerializeField] private Key closeKey = Key.Backspace;

    [Tooltip("备用键，留空表示不使用。默认 Esc")]
    [SerializeField] private Key altCloseKey = Key.Escape;

    // 说明：早期版本这里用过空格键做备用，但空格被 XR Device Simulator 占用
    // （绑给 Manipulate Left），而且键帽大、极易误触，所以已弃用。

    [Header("视觉反馈")]
    [SerializeField] private Color normalColor = new Color(0.62f, 0.20f, 0.18f, 0.95f);
    [SerializeField] private Color hoverColor = new Color(0.95f, 0.45f, 0.15f, 1f);
    [SerializeField] private Color pressedColor = new Color(1f, 0.25f, 0.2f, 1f);

    private Image buttonImage;
    private bool isClosing;

    private void Awake()
    {
        if (targetPanel == null) targetPanel = FindPanelRoot();
        buttonImage = GetComponent<Image>();
        if (buttonImage != null) buttonImage.color = normalColor;
    }

    private void Update()
    {
        if (isClosing) return;
        if (targetPanel == null || !targetPanel.activeInHierarchy) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        bool pressed = false;

        var c = kb[closeKey];
        if (c != null && c.wasPressedThisFrame) pressed = true;

        if (altCloseKey != Key.None)
        {
            var alt = kb[altCloseKey];
            if (alt != null && alt.wasPressedThisFrame) pressed = true;
        }

        if (pressed) Close();
    }

    /// <summary>被虚拟手 / 手柄触碰时调用（由 XRSimpleInteractable 的事件转发）</summary>
    public void OnTouched()
    {
        if (buttonImage != null) buttonImage.color = pressedColor;
        Close();
    }

    // ---------- 下面三个是给 XRI 事件用的包装方法 ----------
    // XRI 的事件是 UnityEvent<T>（带参数），而 Inspector 里绑定要求方法签名匹配，
    // 所以这里提供「收下参数、转发给无参方法」的入口。

    /// <summary>接到 XRSimpleInteractable 的 Select Entered</summary>
    public void OnSelectEntered(SelectEnterEventArgs args) => OnTouched();

    /// <summary>接到 XRSimpleInteractable 的 Hover Entered</summary>
    public void OnHoverEntered(HoverEnterEventArgs args) => OnHoverEnter();

    /// <summary>接到 XRSimpleInteractable 的 Hover Exited</summary>
    public void OnHoverExited(HoverExitEventArgs args) => OnHoverExit();

    /// <summary>悬停高亮</summary>
    public void OnHoverEnter()
    {
        if (buttonImage != null) buttonImage.color = hoverColor;
    }

    public void OnHoverExit()
    {
        if (buttonImage != null) buttonImage.color = normalColor;
    }

    /// <summary>关闭面板</summary>
    public void Close()
    {
        if (isClosing) return;
        if (targetPanel == null) targetPanel = FindPanelRoot();

        if (targetPanel == null)
        {
            Debug.LogWarning("[PokeCloseButton] 没有找到要关闭的面板");
            return;
        }

        isClosing = true;
        targetPanel.SetActive(false);
        isClosing = false;

        if (buttonImage != null) buttonImage.color = normalColor;

        Debug.Log($"[PokeCloseButton] 已关闭面板：{targetPanel.name}");
    }

    public void SetTargetPanel(GameObject panel) => targetPanel = panel;

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
}
