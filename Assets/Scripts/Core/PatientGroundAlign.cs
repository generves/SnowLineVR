using UnityEngine;
using UnityEngine.Animations;
using System.Collections.Generic;

/// <summary>
/// 伤者贴地组件（骨骼版）—— 用【脚踝骨骼高度】对齐地面。
///
/// 核心思路：
///   穿地/浮空的本质 = 脚踝离地高度变了。
///   所以只要让每个动画的脚踝高度都对齐【站立时的脚踝高度】就行。
///
///   用骨骼世界坐标而不是顶点包围盒，因为：
///     · 骨骼坐标是 Animator 直接写入的真实数据，绝对可靠
///     · 之前用顶点+bindpose 算最低点，受这个 rig 的 bindpose 缩放影响，
///       算出来的值严重偏离实际（测出 -1.57 米，而脚踝只在 0.12 米）
///
/// 位置计算方式：
///   启动时记录根物体的"基准 Y"（baseY），之后每帧设为：
///     transform.position.y = baseY + 偏移
///   这样每次施加的都是【绝对值】而不是累加，不会漂移。
///
/// 用法：
///   ① 工具 → 生成贴地偏移表（骨骼版）
///   ② Play
/// </summary>
public class PatientGroundAlign : MonoBehaviour
{
    [Header("基准（工具自动填）")]
    [Tooltip("站立时脚踝的目标世界 Y")]
    public float targetFootY = 0.12f;

    [Tooltip("根物体的基准 Y（站立时的原始位置）")]
    public float baseY = 0f;

    [Header("偏移表（工具自动生成，索引 = State 参数的值）")]
    public float[] offsets = new float[0];
    public float[] footYs = new float[0];
    public string[] stateNames = new string[0];

    [Header("行为")]
    [Tooltip("过渡平滑速度。0 = 立即，越大越快")]
    public float smoothSpeed = 8f;

    [Tooltip("整体额外偏移（手动微调，正数抬高）")]
    public float extraOffset = 0f;

    [Tooltip("关闭后组件完全不干预位置")]
    public bool enabledAlign = true;

    [Header("调试")]
    public bool logToConsole = false;
    public bool showOnScreen = false;

    private Animator animator;
    private Transform footBone;
    private float currentOffset = 0f;
    private float targetOffset = 0f;
    private int lastState = -1;
    private bool initialized = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        // 找左脚踝
        if (animator != null)
        {
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name.ToLower();
                if (n.Contains("leftfoot") || n.Contains("left_foot"))
                {
                    footBone = t; break;
                }
            }
        }

        if (animator == null)
            Debug.LogError("[贴地] 找不到 Animator");
        else if (footBone == null)
            Debug.LogError("[贴地] 找不到左脚踝骨骼");
    }

    private void Start()
    {
        // 记录基准 Y（如果工具没填，就用当前值）
        if (Mathf.Approximately(baseY, 0f))
            baseY = transform.position.y;

        initialized = true;

        if (logToConsole)
            Debug.Log($"[贴地] 启动：根物体={transform.name} baseY={baseY:F4} " +
                      $"targetFootY={targetFootY:F4} 状态数={offsets.Length}");
    }

    private void LateUpdate()
    {
        if (!enabledAlign) return;
        if (animator == null || footBone == null || !initialized) return;

        // ---------- 1. 读状态索引 ----------
        int state = 0;
        try { state = animator.GetInteger("State"); }
        catch { }

        // ---------- 2. 查表 ----------
        float off = 0f;
        if (offsets != null && offsets.Length > 0)
        {
            state = Mathf.Clamp(state, 0, offsets.Length - 1);
            off = offsets[state];
        }

        targetOffset = off + extraOffset;

        // ---------- 3. 平滑 ----------
        if (smoothSpeed > 0.01f)
            currentOffset = Mathf.Lerp(currentOffset, targetOffset, Time.deltaTime * smoothSpeed);
        else
            currentOffset = targetOffset;

        // ---------- 4. 应用（绝对值，不累加）----------
        var p = transform.position;
        p.y = baseY + currentOffset;
        transform.position = p;

        // ---------- 5. 日志 ----------
        if (state != lastState)
        {
            lastState = state;
            if (logToConsole)
            {
                string nm = (stateNames != null && state < stateNames.Length)
                    ? stateNames[state] : $"#{state}";
                float fy = (footYs != null && state < footYs.Length) ? footYs[state] : 0f;
                Debug.Log($"[贴地] 状态 {state}「{nm}」 采样脚踝Y={fy:F4} 应用偏移={off:F4}");
            }
        }
    }

    private void OnGUI()
    {
        if (!showOnScreen) return;

        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = 16;
        style.normal.textColor = Color.green;

        int st = 0;
        try { st = animator != null ? animator.GetInteger("State") : 0; } catch { }
        string nm = (stateNames != null && st < stateNames.Length && st >= 0)
            ? stateNames[st] : $"#{st}";
        float fy = footBone != null ? footBone.position.y : 0f;

        GUI.Label(new Rect(20, 190, 1000, 80),
            $"贴地：状态 {st}「{nm}」\n" +
            $"      脚踝实际Y={fy:F4}  目标={targetFootY:F4}  " +
            $"偏移={currentOffset:F4}  baseY={baseY:F4}  根Y={transform.position.y:F4}",
            style);
    }
}
