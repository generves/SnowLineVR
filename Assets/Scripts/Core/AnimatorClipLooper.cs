using UnityEngine;

/// <summary>
/// 让指定的动画片段变成【循环播放】。
///
/// 为什么需要它：
///   Mixamo 导出的动画默认 Wrap Mode = Loop Once（播一遍就冻在最后一帧）。
///   待机、呼吸、呻吟这类动作必须循环，否则人物播完就不动了。
///
///   批量改 FBX 的导入设置要点开每个文件的 Animation 标签，很麻烦；
///   这个组件在【运行时】直接改 RuntimeAnimatorController 里的片段设置。
///
/// 用法：
///   ① 挂在 character 上（有 Animator 的那个物体）
///   ② 在 Loop Clips 里填【片段名包含的关键字】
///        · 留空则按下面的默认列表
///        · 关键字只要片段名里出现了就算匹配
///   ③ Play 时会打印每个片段的"改前 → 改后"循环状态
///   ④ 确认没问题后可以取消勾选 Log To Console
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimatorClipLooper : MonoBehaviour
{
    [Header("要改成循环的片段")]
    [Tooltip("片段名里包含这些关键字就改成循环。\n" +
             "留空 = 用下面的默认列表（待机 / 呻吟 / 呼吸 / 招手 / 尝试行走）")]
    public string[] loopKeywords = new string[]
    {
        "待机",
        "Idle",
        "呻吟",
        "呼吸",
        "招手",
        "呼唤",
        "Wave",
        "Hurting",
        "Stumble",
        "Pain",
        "Nodding",
        "Breathless",
        "尝试行走",
        "捂腿",
        "捂着",
    };

    [Header("要改成只播一次的片段")]
    [Tooltip("片段名里包含这些关键字就强制改成 Loop Once（优先级高于上面的循环列表）。\n" +
             "比如『躺到担架上』这种一次性动作")]
    public string[] onceKeywords = new string[]
    {
        "担架",
    };

    [Header("行为")]
    [Tooltip("在 Start 里自动执行一次")]
    public bool applyOnStart = true;

    [Tooltip("每帧都检查（防止之后有别的代码改回去）")]
    public bool watchEveryFrame = false;

    [Header("调试")]
    public bool logToConsole = true;

    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void Start()
    {
        if (applyOnStart) Apply();
    }

    private void Update()
    {
        if (watchEveryFrame) Apply();
    }

    /// <summary>把匹配到的片段改成循环</summary>
    public void Apply()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) return;

        var controller = animator.runtimeAnimatorController;
        if (controller == null)
        {
            Debug.LogWarning("[动画循环] Animator 上没有 RuntimeAnimatorController");
            return;
        }

        int changed = 0;

        foreach (var clip in controller.animationClips)
        {
            if (clip == null) continue;

            bool shouldLoop = ShouldLoop(clip.name);
            var wanted = shouldLoop ? WrapMode.Loop : WrapMode.Once;

            if (clip.wrapMode != wanted)
            {
                var before = clip.wrapMode;
                clip.wrapMode = wanted;
                changed++;

                if (logToConsole)
                    Debug.Log($"[动画循环] 「{clip.name}」：{before} → {wanted}");
            }
            else if (logToConsole)
            {
                Debug.Log($"[动画循环] 「{clip.name}」：已经是 {clip.wrapMode}，不动");
            }
        }

        if (logToConsole)
            Debug.Log($"[动画循环] 共改了 {changed} 个片段（总 {controller.animationClips.Length} 个）");
    }

    private bool ShouldLoop(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return false;

        // 强制"只播一次"的优先
        foreach (var k in onceKeywords)
        {
            if (string.IsNullOrEmpty(k)) continue;
            if (clipName.Contains(k)) return false;
        }

        // 循环关键字
        foreach (var k in loopKeywords)
        {
            if (string.IsNullOrEmpty(k)) continue;
            if (clipName.Contains(k)) return true;
        }

        return false;
    }
}
