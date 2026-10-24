using UnityEngine;

/// <summary>
/// 伤者动画序列 —— 进入关卡后按顺序播放一段表演，最后停在循环的待机动画。
///
/// 例：左腿受伤关卡
///   ① 尝试行走但是失败
///   ② 捂腿呻吟
///   ③ 向前方招手
///   ④ 待机状态（之后一直循环）
///
/// 实现说明：
///   伤者的 Animator 控制器（AC_Patient）用【整数参数 State】切换动作。
///   每一步【完整播放一遍】当前动作，播完自动切下一步 ——
///   时长是从 Animator 当前状态里读出来的，不用手填。
///
/// 用法：
///   ① 挂在 character 上（有 Animator 的那个物体）
///   ② 在 Steps 里按顺序填 State 值
///   ③ 最后一步之后会一直循环（因为待机动画本身是循环的）
/// </summary>
[RequireComponent(typeof(Animator))]
public class PatientSequencePlayer : MonoBehaviour
{
    [System.Serializable]
    public class Step
    {
        [Tooltip("AC_Patient 控制器的 State 参数值。\n" +
                 "0=待机  1=右转  2=后退  3=向前方招手  4=尝试行走但是失败\n" +
                 "5=左转  6=捂腿呻吟  7~10=四方向走  11=直走\n" +
                 "12=平躺呻吟  13=平躺急促呼吸  14=平躺轻微呼吸1  16=躺到担架上")]
        public int state = 0;

        [Tooltip("这一步的说明，只用于日志显示")]
        public string note = "";

        [Tooltip("⭐ 勾上 = 这一步的动画【循环播放】（一直播到最后一步为止）。\n" +
                 "例如最后一步的「待机」要勾上，否则播一遍就冻在最后一帧。\n" +
                 "不勾 = 播一遍（配合 Trim End 掐掉结尾后切下一步）")]
        public bool loop = false;

        [Tooltip("掐掉结尾多少秒。\n" +
                 "0    = 完整播完整个动作\n" +
                 "0.1  = 去掉结尾 0.1 秒就切下一步\n" +
                 "适合去掉动作末尾那段多余的过渡/停顿")]
        public float trimEnd = 0f;

        [Tooltip("额外等待时间（秒）。0 = 动作播完立刻切下一步\n" +
                 "想让某个动作多停留一会儿时填这个")]
        public float extraHold = 0f;
    }

    [Header("动画序列")]
    [Tooltip("按顺序播放的步骤。勾了 Loop 的那一步会一直循环")]
    public Step[] steps = new Step[]
    {
        new Step { state = 4, note = "尝试行走但是失败", loop = false },
        new Step { state = 6, note = "捂腿呻吟",         loop = false },
        new Step { state = 3, note = "向前方招手",       loop = false },
        new Step { state = 0, note = "待机（循环）",     loop = true  },
    };

    [Header("行为")]
    [Tooltip("物体被激活时自动开始播放")]
    public bool playOnEnable = true;

    [Tooltip("参数名（AC_Patient 控制器里的整数参数名）")]
    public string stateParameter = "State";

    [Tooltip("切换动作时给一点余量，避免切在动作最后一帧上")]
    public float switchMargin = 0.05f;

    [Tooltip("最后一步是否一直循环（不再往下走）")]
    public bool loopLastStep = true;

    [Header("调试")]
    public bool logToConsole = false;

    // ---------- 内部 ----------
    private Animator animator;
    private int stateHash;
    private int currentIndex = -1;
    private float timer;
    private float currentDuration;
    private bool playing;
    private bool waitingForState;      // 刚切过去，还在过渡中，时长还没读到

    private void Awake()
    {
        animator = GetComponent<Animator>();
        stateHash = Animator.StringToHash(stateParameter);
    }

    private void OnEnable()
    {
        if (playOnEnable) PlayFromStart();
    }

    /// <summary>从第一步开始播放</summary>
    public void PlayFromStart()
    {
        currentIndex = -1;
        timer = 0f;
        playing = true;
        NextStep();
    }

    /// <summary>停止序列（停在哪就是哪）</summary>
    public void Stop()
    {
        playing = false;
    }

    /// <summary>直接跳到某个状态（不参与序列）</summary>
    public void SetState(int state)
    {
        playing = false;
        if (animator != null) animator.SetInteger(stateHash, state);
    }

    /// <summary>当前是第几步（从 0 开始），-1 表示还没开始</summary>
    public int CurrentIndex => currentIndex;

    private void Update()
    {
        if (!playing || animator == null) return;

        // ---------- 读当前动作的真实时长 ----------
        // 刚切过去时动画可能还在过渡，IsInTransition 为 true，
        // 这时 length 是混合中的值，不准。等它稳定下来再读。
        if (waitingForState)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (!animator.IsInTransition(0))
            {
                currentDuration = st.length;
                waitingForState = false;

                // ⭐ 过渡结束、状态稳定了，这时才知道对应哪个动画片段
                //    → 在这里按这一步的 Loop 设置改片段的循环模式
                ApplyLoopMode(currentIndex, st);

                if (logToConsole)
                {
                    float logTrim = steps[currentIndex].trimEnd;
                    float logPlayLen = Mathf.Max(0.05f, currentDuration - logTrim);

                    Debug.Log($"[伤者动画] {name} 第 {currentIndex + 1}/{steps.Length} 步：" +
                              $"{Note(currentIndex)}\n" +
                              $"      动作全长 = {currentDuration:F2} 秒" +
                              $"   循环 = {steps[currentIndex].loop}" +
                              (logTrim > 0.001f ? $"   掐掉结尾 {logTrim:F2} 秒" : "") +
                              $"   → 实际播放 {logPlayLen:F2} 秒");
                }
            }
            return;
        }

        // ---------- 计时 ----------
        timer += Time.deltaTime;

        // ⭐ "循环"的步骤：一直循环，不再往下走
        bool isLast = (currentIndex >= steps.Length - 1);
        bool thisLoops = (currentIndex >= 0 && currentIndex < steps.Length)
                         && steps[currentIndex].loop;

        if (thisLoops || (isLast && loopLastStep))
        {
            // 持续强制循环模式：
            // 状态切换 / 别的代码可能会把 wrapMode 改回去，
            // 每帧重新设一次，保证"循环"这一步真的是循环的
            if (thisLoops) ForceLoopMode();

            // 每隔几秒打一条播放状态，方便确认动画在动
            if (logToConsole && Time.frameCount % 120 == 0)
            {
                var st = animator.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[伤者动画] 循环中 —— 进度 {st.normalizedTime % 1f:F2}" +
                          $"  速度 {animator.speed:F2}  片段数 {clipBuffer.Count}");
            }
            return;
        }

        float trim = 0f;
        float hold = 0f;
        if (currentIndex >= 0 && currentIndex < steps.Length)
        {
            trim = steps[currentIndex].trimEnd;     // 掐掉的结尾秒数
            hold = steps[currentIndex].extraHold;   // 额外停留
        }

        // 实际要播的时长 = 动作全长 - 掐掉的结尾
        float playLen = currentDuration - trim;

        // 掐太多会导致负数，兜底成至少 0.05 秒
        if (playLen < 0.05f) playLen = 0.05f;

        // 播到位了 → 切下一步
        if (timer >= playLen + hold + switchMargin)
            NextStep();
    }

    private void NextStep()
    {
        currentIndex++;

        if (steps == null || currentIndex >= steps.Length)
        {
            playing = false;
            if (logToConsole) Debug.Log($"[伤者动画] {name} 序列播放完毕");
            return;
        }

        timer = 0f;
        currentDuration = 0f;
        waitingForState = true;

        var s = steps[currentIndex];
        animator.SetInteger(stateHash, s.state);

        if (logToConsole)
            Debug.Log($"[伤者动画] {name} 切到第 {currentIndex + 1}/{steps.Length} 步：" +
                      $"State={s.state}（{s.note}）");
    }

    private string Note(int i)
    {
        if (steps == null || i < 0 || i >= steps.Length) return "";
        return string.IsNullOrEmpty(steps[i].note) ? $"State={steps[i].state}" : steps[i].note;
    }

    // ==================== 按 Element 的 Loop 设置控制动画循环 ====================
    /// <summary>
    /// 把【当前这一步】对应的动画片段设成 Loop 或 Once。
    ///
    /// 为什么要这样做：
    ///   Mixamo 导出的动画片段名统一是 "mixamo.com"，没法按名字区分。
    ///   所以走这条路：
    ///       过渡结束后 → GetCurrentAnimatorClipInfo → 当前状态真正在播的片段
    ///   这是唯一能精确定位到"第几个 Element 对应哪个片段"的办法。
    /// </summary>
    private void ApplyLoopMode(int index, AnimatorStateInfo st)
    {
        if (steps == null || index < 0 || index >= steps.Length) return;

        // ⭐ 用 GetCurrentAnimatorClipInfo 直接拿"当前状态正在播的片段"
        //    比按名字/hash 去 animationClips 里猜可靠得多，
        //    因为 Mixamo 的片段名全都叫 mixamo.com，hash 比对会撞。
        clipBuffer.Clear();
        animator.GetCurrentAnimatorClipInfo(0, clipBuffer);

        if (clipBuffer.Count == 0)
        {
            if (logToConsole)
                Debug.LogWarning($"[伤者动画] 第 {index + 1} 步：拿不到当前动画片段，循环设置未生效");
            return;
        }

        bool wantLoop = steps[index].loop;
        var wanted = wantLoop ? WrapMode.Loop : WrapMode.Once;

        for (int i = 0; i < clipBuffer.Count; i++)
        {
            var clip = clipBuffer[i].clip;
            if (clip == null) continue;

            if (clip.wrapMode == wanted) continue;

            var before = clip.wrapMode;
            clip.wrapMode = wanted;

            if (logToConsole)
                Debug.Log($"[伤者动画] 第 {index + 1} 步片段循环设置：" +
                          $"「{clip.name}」{before} → {wanted}（Loop={wantLoop}）");
        }
    }

    private readonly System.Collections.Generic.List<AnimatorClipInfo> clipBuffer =
        new System.Collections.Generic.List<AnimatorClipInfo>();

    /// <summary>每帧强制当前片段为 Loop（循环步骤用）</summary>
    private void ForceLoopMode()
    {
        clipBuffer.Clear();
        animator.GetCurrentAnimatorClipInfo(0, clipBuffer);

        for (int i = 0; i < clipBuffer.Count; i++)
        {
            var clip = clipBuffer[i].clip;
            if (clip == null) continue;
            if (clip.wrapMode == WrapMode.Loop) continue;

            clip.wrapMode = WrapMode.Loop;
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(stateParameter)) stateParameter = "State";
        if (switchMargin < 0f) switchMargin = 0f;
    }
}
