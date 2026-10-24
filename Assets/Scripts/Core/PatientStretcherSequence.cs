using UnityEngine;
using System.Collections;

/// <summary>
/// 伤员上担架序列 —— 走到担架旁 → 吸附站位 → 躺到担架上 → 切换到躺姿。
///
/// ===================== 设计依据 =====================
///
///   位移由 RootMotionTransfer 负责（动画自带位移，转移到 character 上），
///   本脚本只负责「什么时候切哪个动画」「什么时候停」「什么时候吸附」。
///
/// ===================== 四个阶段 =====================
///
///   阶段 1  走路：播 Walk State，一直走到离 Dock Point 小于 Stop Distance
///   阶段 2  吸附：把角色的 Position / Rotation 直接设成 Dock Point 的值
///                → 消除根运动累计的误差，保证每次躺下的起点一致
///   阶段 3  躺下：播 Lie Down State，等它播完
///   阶段 4  躺姿：播 Lying State（循环），结束
///
/// ===================== 用法 =====================
///
///   ① 在担架旁边建一个空物体 DockPoint
///        Position = 伤员准备躺下时该站的位置（脚下地面）
///        Rotation = 躺下时的朝向
///   ② 本脚本挂在 character 上
///   ③ 拖引用：
///        Animator       ← 本物体的 Animator
///        Dock Point     ← DockPoint
///        Step Controller ← 留空会自动找
///   ④ 填状态编号（默认就是你现在的配置）：
///        Walk State      = 11   （左腿受伤直走）
///        Lie Down State  = 16   （躺到担架上）
///        Lying State     = 12   （平躺呻吟）
///   ⑤ 自动触发：第 5 步「交接与病情汇报」完成后开始
///
/// ===================== 调参提示 =====================
///
///   · 角色走过头 / 走不到位 → 调 Stop Distance（0.10 ~ 0.25）
///   · 角色一直走到超时     → RootMotionTransfer 没生效，或 Dock Point 太远
///   · 躺下之后位置偏了     → 用 Lying State Offset 微调（Inspector 里改数值即可）
/// </summary>
[DefaultExecutionOrder(20001)]
public class PatientStretcherSequence : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("本物体的 Animator。留空自动获取")]
    public Animator animator;

    [Tooltip("担架旁的站位点（空物体）")]
    public Transform dockPoint;

    [Tooltip("步骤控制器。留空自动查找")]
    public StepController stepController;

    [Header("动画状态编号")]
    [Tooltip("Animator 里控制状态的整数参数名")]
    public string stateParameter = "State";

    [Tooltip("走路的状态编号")]
    public int walkState = 11;

    [Tooltip("躺到担架上的状态编号")]
    public int lieDownState = 16;

    [Tooltip("躺下之后循环播放的躺姿状态编号")]
    public int lyingState = 12;

    [Header("触发方式")]
    [Tooltip("所有步骤完成后自动开始")]
    public bool autoStartWhenAllStepsDone = true;

    [Tooltip("开始前等待几秒（让玩家看清「交接完成」的提示）")]
    public float startDelay = 0.5f;

    [Header("走路")]
    [Tooltip("离 Dock Point 多近就算到了（米）")]
    [Range(0.05f, 1f)]
    public float stopDistance = 0.15f;

    [Tooltip("开始走路时，先把角色朝向对准 Dock Point")]
    public bool faceDockAtStart = true;

    [Tooltip("模型正前方和 +Z 的偏差角度。走反了就填 180")]
    public float facingYawOffset = 0f;

    [Tooltip("走路阶段最长持续多久（秒），超时就直接吸附，防止卡死")]
    public float maxWalkDuration = 40f;

    [Header("吸附")]
    [Tooltip("吸附用多长时间（秒）。0 = 瞬间吸附（推荐，误差小时看不出来）")]
    public float snapDuration = 0f;

    [Header("躺下之后")]
    [Tooltip("躺下动画播完后是否切到躺姿循环")]
    public bool switchToLyingState = true;

    [Tooltip("切到躺姿后对角色位置做的微调（如果躺的位置偏了，改这里）")]
    public Vector3 lyingStateOffset = Vector3.zero;

    [Tooltip("躺下动画播放到最后百分之几就算播完")]
    [Range(0.5f, 1f)]
    public float lieDownFinishAt = 0.95f;

    [Header("运行时要临时禁用的组件")]
    [Tooltip("序列开始后把这些组件禁用（例如 Patient Sequence Player，避免它继续改 State）")]
    public Behaviour[] disableWhileRunning;

    [Header("调试")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private int stateHash;
    private bool running;
    private bool finished;

    /// <summary>序列是否正在运行</summary>
    public bool IsRunning => running;

    /// <summary>序列是否已经完成</summary>
    public bool IsFinished => finished;

    /// <summary>一行结束的回调（供外部挂后续逻辑）</summary>
    public event System.Action OnSequenceFinished;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        stateHash = Animator.StringToHash(stateParameter);
    }

    private void Start()
    {
        if (stepController == null) stepController = FindAnyObjectByType<StepController>();

        if (stepController != null && autoStartWhenAllStepsDone)
            stepController.OnAllStepsCompleted += HandleAllStepsCompleted;
        else if (stepController == null)
            Debug.LogWarning($"[上担架] 「{name}」找不到 StepController，无法自动触发", this);
    }

    private void OnDestroy()
    {
        if (stepController != null)
            stepController.OnAllStepsCompleted -= HandleAllStepsCompleted;
    }

    private void HandleAllStepsCompleted()
    {
        if (running || finished) return;

        if (logToConsole) Debug.Log($"[上担架] 全部步骤完成 → 开始上担架序列", this);
        StartCoroutine(RunSequence());
    }

    /// <summary>手动开始（也可以从别的脚本调用）</summary>
    [ContextMenu("手动开始上担架")]
    public void StartSequence()
    {
        if (running || finished) return;
        StartCoroutine(RunSequence());
    }

    // ==================== 主流程 ====================
    private IEnumerator RunSequence()
    {
        if (running) yield break;
        running = true;

        if (disableWhileRunning != null)
        {
            foreach (var b in disableWhileRunning)
                if (b != null) b.enabled = false;
        }

        if (dockPoint == null)
        {
            Debug.LogError($"[上担架] 「{name}」没有指定 Dock Point，序列中止", this);
            running = false;
            yield break;
        }

        if (animator == null)
        {
            Debug.LogError($"[上担架] 「{name}」没有 Animator，序列中止", this);
            running = false;
            yield break;
        }

        // ---------- 阶段 0：等待 ----------
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        // ---------- 阶段 1：走路 ----------
        yield return StartCoroutine(WalkToDock());

        // ---------- 阶段 2：吸附 ----------
        yield return StartCoroutine(SnapToDock());

        // ---------- 阶段 3：躺下 ----------
        yield return StartCoroutine(PlayLieDown());

        // ---------- 阶段 4：躺姿 ----------
        if (switchToLyingState)
        {
            SetState(lyingState);

            if (lyingStateOffset != Vector3.zero)
            {
                yield return null;      // 等一帧让新姿态生效
                transform.position += lyingStateOffset;

                if (logToConsole)
                    Debug.Log($"[上担架] 应用躺姿微调 {lyingStateOffset}", this);
            }
        }

        finished = true;
        running = false;

        if (logToConsole) Debug.Log($"[上担架] 序列完成", this);
        OnSequenceFinished?.Invoke();
    }

    // ==================== 阶段 1：走路 ====================
    private IEnumerator WalkToDock()
    {
        // 先转向 Dock Point，否则会往错误方向走
        if (faceDockAtStart)
        {
            Vector3 dir = dockPoint.position - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(dir.normalized)
                                     * Quaternion.Euler(0f, facingYawOffset, 0f);
            }
        }

        SetState(walkState);

        if (logToConsole)
            Debug.Log($"[上担架] 开始走路：距离 {HorizontalDistance(transform.position, dockPoint.position):F2} 米", this);

        float elapsed = 0f;
        float lastLog = 0f;

        while (elapsed < maxWalkDuration)
        {
            elapsed += Time.deltaTime;

            float dist = HorizontalDistance(transform.position, dockPoint.position);

            if (dist <= stopDistance)
            {
                if (logToConsole)
                    Debug.Log($"[上担架] 已到达站位（剩余 {dist:F3} 米，用时 {elapsed:F2} 秒）", this);
                yield break;
            }

            if (logToConsole && elapsed - lastLog > 2f)
            {
                lastLog = elapsed;
                Debug.Log($"[上担架] 走路中：剩余 {dist:F2} 米（已用 {elapsed:F1} 秒）", this);
            }

            yield return null;
        }

        // 超时：可能根运动没生效
        Debug.LogWarning($"[上担架] 走路超时（{maxWalkDuration} 秒），" +
                         $"剩余 {HorizontalDistance(transform.position, dockPoint.position):F2} 米。\n" +
                         $"  常见原因：① RootMotionTransfer 没挂 / 没生效\n" +
                         $"            ② Dock Point 太远（超过动画能走的距离 × 时间）", this);
    }

    // ==================== 阶段 2：吸附 ====================
    private IEnumerator SnapToDock()
    {
        Vector3 targetPos = dockPoint.position;
        Quaternion targetRot = dockPoint.rotation;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        float error = Vector3.Distance(startPos, targetPos);

        if (snapDuration <= 0.001f)
        {
            transform.position = targetPos;
            transform.rotation = targetRot;
        }
        else
        {
            float t = 0f;
            while (t < snapDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / snapDuration);
                transform.position = Vector3.Lerp(startPos, targetPos, k);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, k);
                yield return null;
            }

            transform.position = targetPos;
            transform.rotation = targetRot;
        }

        // 吸附后要重置根运动基准，否则下一帧会把「瞬移」当成位移再推一次
        var rmt = GetComponent<RootMotionTransfer>();
        if (rmt != null) rmt.ResetReference();

        if (logToConsole)
            Debug.Log($"[上担架] 已吸附到站位（修正了 {error:F3} 米的累计误差）", this);
    }

    // ==================== 阶段 3：躺下 ====================
    private IEnumerator PlayLieDown()
    {
        SetState(lieDownState);

        // 等过渡结束，读真实时长
        float timeout = 2f;
        float t = 0f;
        float clipLength = 0f;

        while (t < timeout)
        {
            t += Time.deltaTime;
            var st = animator.GetCurrentAnimatorStateInfo(0);

            if (!animator.IsInTransition(0) && st.length > 0.01f)
            {
                clipLength = st.length;
                break;
            }
            yield return null;
        }

        if (clipLength <= 0.01f)
        {
            Debug.LogWarning($"[上担架] 读不到躺下动画的时长，等 2 秒后继续", this);
            yield return new WaitForSeconds(2f);
            yield break;
        }

        if (logToConsole)
            Debug.Log($"[上担架] 躺下动画长度 {clipLength:F2} 秒", this);

        yield return new WaitForSeconds(clipLength * lieDownFinishAt);
    }

    // ==================== 工具 ====================
    private void SetState(int value)
    {
        if (animator == null) return;
        animator.SetInteger(stateHash, value);

        if (logToConsole) Debug.Log($"[上担架] State = {value}", this);
    }

    /// <summary>只算水平距离（忽略高度差）</summary>
    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
