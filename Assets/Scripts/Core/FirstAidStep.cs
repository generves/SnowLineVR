using UnityEngine;

/// <summary>
/// 单个急救训练步骤的数据定义。
///
/// 职责：只存数据，不含任何逻辑。
/// 为什么这么做：把「第几步、提示什么、判定什么、错了什么后果」全部变成可在 Inspector 里改的资源文件，
/// 以后调整流程不需要改动任何一行代码。
///
/// 使用方式：Project 窗口右键 → Create → 急救训练 → 步骤
/// </summary>
[CreateAssetMenu(fileName = "Step", menuName = "急救训练/步骤")]
public class FirstAidStep : ScriptableObject
{
    [Header("基本信息")]
    [Tooltip("第几步，从 1 开始")]
    public int stepIndex = 1;

    [Tooltip("步骤名称，会显示在训练报告里，例如：环境安全评估")]
    public string stepName = "新步骤";

    [Tooltip("给学员看的操作提示，例如：观察雪道上方，确认无来车后放置警示标志")]
    [TextArea(2, 4)]
    public string instruction = "";

    [Header("判定条件")]
    [Tooltip("这一步要求触发的动作 ID，例如 PlaceWarningSign。\n" +
             "外部交互物体调用 ReportAction 时传的就是这个字符串。\n" +
             "⚠️ 可以写多个，用英文逗号分隔（中英文逗号、分号都支持），例如：\n" +
             "    ApplyIcePack,ApplyBandage")]
    public string requiredActionId = "";

    [Tooltip("勾上 = 必须把 requiredActionId 里的动作【全部做完】才算过这一步。\n" +
             "不勾 = 只要命中其中任意一个就算过。\n" +
             "单个动作的步骤勾不勾都一样。")]
    public bool sequential = false;

    [Tooltip("限时（秒）。填 0 表示不限时")]
    public float timeLimit = 0f;

    [Header("反馈内容")]
    [Tooltip("做对时的反馈")]
    [TextArea(2, 4)]
    public string correctFeedback = "";

    [Tooltip("做错时的教学提示。这是本作品的核心教学价值所在，要写清楚「错在哪、为什么错」")]
    [TextArea(2, 4)]
    public string wrongHint = "";

    [Tooltip("超时未操作时的提示")]
    [TextArea(2, 4)]
    public string timeoutHint = "";

    [Header("错误后果")]
    [Tooltip("勾选后，本步做错会被标记为「关键错误」，在报告中高亮并大幅扣分")]
    public bool isCriticalError = false;

    [Tooltip("做错时疼痛指数的变化量，例如 +5 表示疼痛加剧 5 点")]
    public float painDelta = 0f;

    [Tooltip("做错时体温的变化量，例如 -0.8 表示体温下降 0.8 摄氏度")]
    public float tempDelta = 0f;

    [Tooltip("做错时心率的变化量，例如 +25 表示心率上升 25")]
    public float heartRateDelta = 0f;

    [Header("分值")]
    [Tooltip("本步满分")]
    [Range(0, 100)]
    public int score = 10;
}
