using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// HUD 面板滚动器 —— 让面板支持键盘滚动。
///
/// 为什么需要它：
///   在 VR 里没有鼠标滚轮可用，ScrollRect 默认只认滚轮。
///   这个脚本把键盘按键接到 ScrollRect 上，编辑器和头显里都能翻页。
///
/// 用的按键刻意避开了 XR Device Simulator 占用的那些
/// （模拟器占用 W A S D Q E Z X C V 1 2 3 T R H 空格 左Shift 左Ctrl Tab Esc \）
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SimpleScroller : MonoBehaviour
{
    [Header("引用（留空自动查找同物体或子物体上的 ScrollRect）")]
    [SerializeField] private UnityEngine.UI.ScrollRect scrollRect;

    [Header("滚动按键")]
    [SerializeField] private Key upKey = Key.UpArrow;
    [SerializeField] private Key downKey = Key.DownArrow;
    [SerializeField] private Key pageUpKey = Key.PageUp;
    [SerializeField] private Key pageDownKey = Key.PageDown;

    [Header("滚动速度")]
    [Tooltip("方向键的滚动速度（每秒滚动视口高度的比例）")]
    [SerializeField] private float arrowSpeed = 0.6f;
    [Tooltip("翻页键一次翻多少（视口高度的比例）")]
    [SerializeField] private float pageStep = 0.85f;

    private void Awake()
    {
        if (scrollRect == null) scrollRect = GetComponent<UnityEngine.UI.ScrollRect>();
        if (scrollRect == null) scrollRect = GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);

        if (scrollRect == null)
            Debug.LogWarning($"[SimpleScroller] '{name}' 上找不到 ScrollRect，键盘滚动不会生效");
    }

    private void Update()
    {
        if (scrollRect == null) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        float delta = 0f;

        if (Press(kb, upKey)) delta += arrowSpeed * Time.deltaTime;
        if (Press(kb, downKey)) delta -= arrowSpeed * Time.deltaTime;
        if (Press(kb, pageUpKey)) delta += pageStep;
        if (Press(kb, pageDownKey)) delta -= pageStep;

        if (Mathf.Abs(delta) > 0.0001f)
        {
            // verticalNormalizedPosition: 1 = 顶部, 0 = 底部
            scrollRect.verticalNormalizedPosition =
                Mathf.Clamp01(scrollRect.verticalNormalizedPosition + delta);
        }
    }

    private bool Press(Keyboard kb, Key key)
    {
        var c = kb[key];
        return c != null && c.isPressed;
    }
}
