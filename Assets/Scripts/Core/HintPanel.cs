using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// 提示面板 —— 在 VR 里显示操作反馈与教学提示。
///
/// 特点：
///   · 基于 TextMeshPro，挂在任何 World Space Canvas 的文本上即可
///   · 支持限时自动隐藏
///   · 支持消息队列（连续触发时不会互相覆盖）
///   · 可选：危险提示用红色、正确提示用绿色
///
/// 用法：
///   ① 建一个 World Space Canvas，里面放一个 TMP 文本
///   ② 把本组件挂在文本物体上
///   ③ 其他脚本调用 ShowHint("提示文字")
/// </summary>
public class HintPanel : MonoBehaviour
{
    [Header("显示")]
    [Tooltip("用来显示文字的组件。留空会自动找本物体上的 TMP")]
    [SerializeField] private TMP_Text hintText;

    [Tooltip("提示面板的根物体（可选，用于整体显隐）")]
    [SerializeField] private GameObject panelRoot;

    [Header("行为")]
    [Tooltip("默认显示时长（秒）。0 = 一直显示到下次更新")]
    [SerializeField] private float defaultDuration = 4f;

    [Tooltip("是否用队列（连续调用时逐条显示，而不是互相覆盖）")]
    [SerializeField] private bool useQueue = false;

    [Header("颜色")]
    [SerializeField] private Color normalColor = new Color(0.92f, 0.95f, 0.98f);
    [SerializeField] private Color warningColor = new Color(1f, 0.72f, 0.25f);
    [SerializeField] private Color errorColor = new Color(0.95f, 0.35f, 0.3f);
    [SerializeField] private Color successColor = new Color(0.45f, 0.92f, 0.55f);

    [Header("调试")]
    [SerializeField] private bool logToConsole = true;

    private Coroutine hideCoroutine;
    private readonly System.Collections.Generic.Queue<string> messageQueue =
        new System.Collections.Generic.Queue<string>();

    private void Awake()
    {
        if (hintText == null) hintText = GetComponent<TMP_Text>();
        if (hintText == null) hintText = GetComponentInChildren<TMP_Text>(true);

        if (hintText == null)
            Debug.LogWarning("[HintPanel] 没有找到 TMP 文本组件，提示不会显示");

        // 启动时先隐藏
        if (panelRoot != null) panelRoot.SetActive(false);
        else if (hintText != null) hintText.text = "";
    }

    // ==================== 对外接口 ====================

    /// <summary>显示一条提示（默认时长）</summary>
    public void ShowHint(string message)
    {
        ShowHint(message, defaultDuration, HintType.Normal);
    }

    /// <summary>显示一条提示，指定时长</summary>
    public void ShowHint(string message, float duration)
    {
        ShowHint(message, duration, HintType.Normal);
    }

    /// <summary>显示一条提示，指定类型（会决定颜色）</summary>
    public void ShowHint(string message, float duration, HintType type)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (logToConsole) Debug.Log($"[提示] {message}");

        if (useQueue)
        {
            messageQueue.Enqueue(message);
            if (hideCoroutine == null) hideCoroutine = StartCoroutine(ProcessQueue());
            return;
        }

        DisplayNow(message, duration, type);
    }

    /// <summary>显示"正确"提示（绿色）</summary>
    public void ShowSuccess(string message, float duration = -1f)
    {
        ShowHint(message, duration < 0 ? defaultDuration : duration, HintType.Success);
    }

    /// <summary>显示"警告"提示（橙色）</summary>
    public void ShowWarning(string message, float duration = -1f)
    {
        ShowHint(message, duration < 0 ? defaultDuration : duration, HintType.Warning);
    }

    /// <summary>显示"错误"提示（红色）</summary>
    public void ShowError(string message, float duration = -1f)
    {
        ShowHint(message, duration < 0 ? defaultDuration : duration, HintType.Error);
    }

    /// <summary>立即隐藏</summary>
    public void HideHint()
    {
        if (hideCoroutine != null) { StopCoroutine(hideCoroutine); hideCoroutine = null; }
        messageQueue.Clear();

        if (hintText != null) hintText.text = "";
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ==================== 内部 ====================

    public enum HintType { Normal, Success, Warning, Error }

    private void DisplayNow(string message, float duration, HintType type)
    {
        if (hideCoroutine != null) { StopCoroutine(hideCoroutine); hideCoroutine = null; }

        if (panelRoot != null) panelRoot.SetActive(true);

        if (hintText != null)
        {
            hintText.text = message;
            hintText.color = type switch
            {
                HintType.Success => successColor,
                HintType.Warning => warningColor,
                HintType.Error => errorColor,
                _ => normalColor
            };
        }

        if (duration > 0f) hideCoroutine = StartCoroutine(HideAfter(duration));
    }

    private IEnumerator HideAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        if (hintText != null) hintText.text = "";
        if (panelRoot != null) panelRoot.SetActive(false);
        hideCoroutine = null;
    }

    private IEnumerator ProcessQueue()
    {
        while (messageQueue.Count > 0)
        {
            string msg = messageQueue.Dequeue();
            DisplayNow(msg, defaultDuration, HintType.Normal);
            yield return new WaitForSeconds(defaultDuration + 0.2f);
        }
        hideCoroutine = null;
    }
}
