using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 步骤清单（常驻）—— 训练过程中一直在学员视线前方显示"第几步 / 共几步 / 这步要做什么"。
///
/// 为什么医疗 VR 需要它：
///   · 学员手上拿着器械，腾不出手去翻菜单
///   · 步骤顺序本身就是考核点（比如 RICE 的顺序）
///   · 中途走神后能一眼看到"我在哪一步"
///
/// 显示形式（勾选式）：
///   ✅ 1. 环境安全评估
///   ✅ 2. 暴露与检查
///   ▶ 3. 冰敷右小腿          ← 当前步高亮
///     4. 加压包扎
///     5. 抬高患肢
///
/// 用法：
///   ① 建一个 World Space Canvas，挂在训练区前方（约 1.5 米远、略低于视线）
///   ② Canvas 下建一个空物体当容器，挂本脚本
///   ③ 在 Canvas 下建一个 Text (TMP) 当【行模板】，拖到 Line Prefab 字段，
///      然后把它取消激活（本脚本会克隆它）
///   ④ 把 StepController 拖进来（留空会自动查找）
/// </summary>
public class StepChecklistPanel : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("步骤来源。留空会自动在场景里查找")]
    public StepController stepController;

    [Tooltip("每一行用的 Text (TMP) 模板。会被克隆，所以模板本身要取消激活")]
    public TMP_Text linePrefab;

    [Tooltip("【当前步的说明行】用的 Text (TMP) 模板（可选）。\n" +
             "填了之后，当前进行中的那一步会在下面多显示一行 instruction。\n" +
             "模板本身也要取消激活。\n" +
             "留空 = 不显示说明行")]
    public TMP_Text instructionPrefab;

    [Tooltip("说明行相对步骤名的缩进（Canvas 单位）")]
    public float instructionIndent = 45f;

    [Tooltip("说明行占用的额外空隙（相对主行高度的倍数）")]
    public float instructionGapFactor = 0.35f;

    [Tooltip("行的父物体（通常是这个物体自己）")]
    public Transform lineContainer;

    [Header("外观")]
    [Tooltip("面板标题（留空则不显示标题）")]
    public TMP_Text titleText;

    [Tooltip("标题内容。{0}=当前步序号  {1}=总步数")]
    public string titleFormat = "急救步骤  {0}/{1}";

    [Tooltip("行间距倍数。1.0 = 按模板 Text 的高度排；\n" +
             "行与行叠在一起时调大（比如 1.4）")]
    public float lineSpacingFactor = 1.15f;

    [Tooltip("第一行相对容器的偏移（Canvas 本地单位）")]
    public Vector2 firstLineOffset = Vector2.zero;

    [Tooltip("已完成步骤的颜色（变暗）")]
    public Color doneColor = new Color(0.55f, 0.62f, 0.7f, 0.75f);

    [Tooltip("当前步骤的颜色（高亮）")]
    public Color currentColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Tooltip("未开始步骤的颜色（更暗）")]
    public Color pendingColor = new Color(0.72f, 0.76f, 0.82f, 0.6f);

    [Tooltip("当前步说明文字的颜色")]
    public Color instructionColor = new Color(0.95f, 0.95f, 0.92f, 0.9f);

    [Header("颜色控制")]
    [Tooltip("勾上 = 脚本接管步骤名的颜色（按 已完成/当前/未开始 自动变色）\n" +
             "取消 = 用 Text 组件自己在 Inspector 里的颜色，脚本不改")]
    public bool controlLineColor = true;

    [Tooltip("勾上 = 脚本接管说明行的颜色\n" +
             "取消 = 用 InstructionTemplate 自己在 Inspector 里的颜色")]
    public bool controlInstructionColor = true;

    // ⚠️ 用纯 ASCII 符号，避免中文字体缺字渲染成方框
    [Tooltip("已完成的前缀")]
    public string donePrefix = "[v] ";

    [Tooltip("当前步的前缀")]
    public string currentPrefix = "> ";

    [Tooltip("未开始的前缀")]
    public string pendingPrefix = "[ ] ";

    [Header("行为")]
    [Tooltip("训练未开始时是否隐藏面板")]
    public bool hideBeforeTraining = true;

    [Tooltip("全部完成后保留多久（秒），然后隐藏。0 = 不隐藏")]
    public float hideAfterFinish = 0f;

    [Header("调试")]
    public bool logToConsole = false;

    // ---------- 内部 ----------
    private readonly List<TMP_Text> lines = new List<TMP_Text>();
    private readonly List<TMP_Text> instructions = new List<TMP_Text>();
    private int currentIndex = -1;
    private bool built;

    private void Start()
    {
        if (stepController == null) stepController = FindAnyObjectByType<StepController>();

        if (stepController == null)
        {
            Debug.LogWarning("[步骤清单] 场景里找不到 StepController，清单不会显示");
            return;
        }

        if (lineContainer == null) lineContainer = transform;

        stepController.OnStepStarted += HandleStepStarted;
        stepController.OnStepCompleted += HandleStepCompleted;
        stepController.OnAllStepsCompleted += HandleAllCompleted;

        BuildLines();

        // 还没开始训练 → 先藏起来
        if (hideBeforeTraining) SetVisible(false);
    }

    private void OnDestroy()
    {
        if (stepController == null) return;
        stepController.OnStepStarted -= HandleStepStarted;
        stepController.OnStepCompleted -= HandleStepCompleted;
        stepController.OnAllStepsCompleted -= HandleAllCompleted;
    }

    // ==================== 建行 ====================
    private void BuildLines()
    {
        if (built) return;
        built = true;

        if (linePrefab == null)
        {
            Debug.LogWarning("[步骤清单] 没有指定【行模板 Text】，清单不会显示");
            return;
        }

        int total = stepController.TotalSteps;

        // ⚠️ 先读一次模板的高度（必须在 SetActive(false) 之前，也要在克隆之前），
        //    缓存下来给 Relayout() 用
        float lineHeight = GetLineHeight();
        float instrHeight = GetInstrHeight();

        // 模板本身隐藏，只用来克隆
        linePrefab.gameObject.SetActive(false);
        if (instructionPrefab != null) instructionPrefab.gameObject.SetActive(false);

        for (int i = 0; i < total; i++)
        {
            var s = stepController.Steps[i];

            // ---------- 主行（步骤名）----------
            var t = Instantiate(linePrefab, lineContainer);
            t.gameObject.name = $"Line_{i + 1}";
            t.gameObject.SetActive(true);
            t.text = $"{pendingPrefix}{s.stepIndex}. {s.stepName}";
            if (controlLineColor) t.color = pendingColor;
            lines.Add(t);

            // ---------- 说明行（只给当前步用，平时隐藏）----------
            if (instructionPrefab != null)
            {
                var ins = Instantiate(instructionPrefab, lineContainer);
                ins.gameObject.name = $"Instruction_{i + 1}";
                ins.gameObject.SetActive(false);          // 默认隐藏
                ins.text = s.instruction;
                if (controlInstructionColor) ins.color = instructionColor;
                instructions.Add(ins);
            }
        }

        // ⭐ 位置不在这里算死：交给 Relayout()，
        //    它会根据"当前步的说明行是否显示"动态排布
        Relayout();

        if (logToConsole)
            Debug.Log($"[步骤清单] 建了 {lines.Count} 行" +
                      (instructions.Count > 0 ? $" + {instructions.Count} 条说明行" : "") +
                      $"（主行高 {lineHeight:F1}，说明行高 {instrHeight:F1}）");
    }

    /// <summary>
    /// 重新排布所有行。
    ///
    /// 规则（用户要求）：
    ///   · 不显示说明的步骤 → 紧挨着上一行
    ///   · 当前步显示说明时 → 从它开始，下面的步骤整体往下让出空间
    ///
    /// 因为行是从上往下排的，"整体下移"等价于：
    ///   在说明行出现的位置【插入】一段空隙，后面的行自然被推下去。
    /// </summary>
    private void Relayout()
    {
        if (lines.Count == 0) return;

        float lineHeight = GetLineHeight();
        float stepGap = lineHeight * Mathf.Max(0.5f, lineSpacingFactor);
        float instrHeight = GetInstrHeight();
        float instrBlock = instrHeight + lineHeight * instructionGapFactor;

        float leftX = GetLeftX();
        float y = GetTopY() + firstLineOffset.y;

        for (int i = 0; i < lines.Count; i++)
        {
            // ---------- 主行 ----------
            var t = lines[i];
            if (t != null)
            {
                var rt = t.rectTransform;
                rt.anchoredPosition = new Vector2(leftX + firstLineOffset.x, y);
            }

            y -= stepGap;

            // ---------- 说明行 ----------
            if (i < instructions.Count && instructions[i] != null)
            {
                var ins = instructions[i];
                bool show = (i == currentIndex)
                            && !string.IsNullOrEmpty(stepController.Steps[i].instruction);

                if (ins.gameObject.activeSelf != show) ins.gameObject.SetActive(show);

                if (show)
                {
                    // 说明行摆在主行下面
                    ins.rectTransform.anchoredPosition = new Vector2(
                        leftX + firstLineOffset.x + instructionIndent, y);

                    // 让出空间 —— 后面的行继续往下
                    y -= instrBlock;
                }
            }
        }
    }

    private float cachedLineHeight;
    private float cachedInstrHeight;
    private float cachedLeftX;
    private float cachedTopY;

    private float GetLineHeight()
    {
        if (cachedLineHeight > 0.5f) return cachedLineHeight;

        var rt = linePrefab.rectTransform;
        cachedLineHeight = rt.rect.height;
        if (cachedLineHeight < 1f) cachedLineHeight = rt.sizeDelta.y;
        if (cachedLineHeight < 1f) cachedLineHeight = 40f;
        return cachedLineHeight;
    }

    private float GetInstrHeight()
    {
        if (cachedInstrHeight > 0.5f) return cachedInstrHeight;

        if (instructionPrefab != null)
        {
            var rt = instructionPrefab.rectTransform;
            cachedInstrHeight = rt.rect.height;
            if (cachedInstrHeight < 1f) cachedInstrHeight = rt.sizeDelta.y;
        }
        if (cachedInstrHeight < 1f) cachedInstrHeight = GetLineHeight() * 0.6f;
        return cachedInstrHeight;
    }

    private float GetLeftX()
    {
        if (Mathf.Abs(cachedLeftX) < 0.001f)
            cachedLeftX = linePrefab.rectTransform.anchoredPosition.x;
        return cachedLeftX;
    }

    private float GetTopY()
    {
        // 第一次调用时把模板原始 Y 记下来
        if (Mathf.Abs(cachedTopY) < 0.001f)
            cachedTopY = linePrefab.rectTransform.anchoredPosition.y;
        return cachedTopY;
    }

    // ==================== 事件 ====================
    private void HandleStepStarted(FirstAidStep step)
    {
        // 找到这一步在列表里的位置
        currentIndex = IndexOf(step);

        Refresh();

        if (hideBeforeTraining) SetVisible(true);

        if (titleText != null)
            titleText.text = string.Format(titleFormat,
                stepController.CurrentStepNumber, stepController.TotalSteps);

        if (logToConsole)
            Debug.Log($"[步骤清单] 当前第 {currentIndex + 1} 步：{step.stepName}");
    }

    private void HandleStepCompleted(FirstAidStep step, bool correct)
    {
        Refresh();
    }

    private void HandleAllCompleted()
    {
        Refresh();

        if (titleText != null) titleText.text = "急救步骤  完成";

        if (hideAfterFinish > 0.01f)
            Invoke(nameof(HideSelf), hideAfterFinish);

        if (logToConsole) Debug.Log("[步骤清单] 全部步骤完成");
    }

    private void HideSelf() => SetVisible(false);

    // ==================== 刷新 ====================
    private void Refresh()
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var step = stepController.Steps[i];
            var t = lines[i];
            if (t == null) continue;

            if (i < currentIndex)
            {
                // 已经走过的 → 打勾
                t.text = $"{donePrefix}{step.stepIndex}. {step.stepName}";
                if (controlLineColor) t.color = doneColor;
            }
            else if (i == currentIndex)
            {
                // 当前步
                t.text = $"{currentPrefix}{step.stepIndex}. {step.stepName}";
                if (controlLineColor) t.color = currentColor;
            }
            else
            {
                // 还没到
                t.text = $"{pendingPrefix}{step.stepIndex}. {step.stepName}";
                if (controlLineColor) t.color = pendingColor;
            }
        }

        // ⭐ 重新排布：当前步的说明行显示 → 后面的行整体往下让位
        Relayout();
    }

    private int IndexOf(FirstAidStep step)
    {
        var list = stepController.Steps;
        for (int i = 0; i < list.Count; i++)
            if (list[i] == step) return i;
        return -1;
    }

    private void SetVisible(bool v)
    {
        // 只隐藏行，不隐藏 Canvas 本身（否则标题也没了）
        foreach (var t in lines)
            if (t != null) t.gameObject.SetActive(v);

        // 说明行由 Refresh() 按"是不是当前步"决定，隐藏时全部关掉
        if (!v)
        {
            foreach (var ins in instructions)
                if (ins != null) ins.gameObject.SetActive(false);
        }
        else
        {
            Refresh();
        }

        if (titleText != null) titleText.gameObject.SetActive(v);
    }

    /// <summary>供外部强制显示/隐藏</summary>
    public void SetPanelVisible(bool v) => SetVisible(v);
}
