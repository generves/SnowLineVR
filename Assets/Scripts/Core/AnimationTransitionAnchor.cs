using UnityEngine;

/// <summary>
/// 动画过渡锚定 —— 解决「每播完一个动画就跳回原点」。
///
/// ===================== 症状与原因 =====================
///
///   症状：角色播完一段动画（比如"直走"），切换到下一段（比如"躺到担架上"）时，
///         身体会瞬间弹回原点。
///
///   原因：
///     `AC_Patient` 里每条 transition 都是 0.2 秒的 cross-fade。
///     而每段 clip 的髋骨原点都是【各自那一份】——
///       "直走"播完后髋部在 A 点，切到"躺到担架上"时新 clip 的髋部在 B 点。
///     cross-fade 期间 Unity 把两个姿势按权重混合，
///     于是髋部位置被平滑地"拉"到 B 点 → 看起来就是弹回原点。
///
/// ===================== 做法 =====================
///
///   过渡【开始】的那一刻，记住髋骨当前的世界位置（锚点）。
///   过渡【进行中】每一帧，把角色水平平移，让髋骨保持在锚点上。
///   过渡【结束】后松开，新动画从锚点继续播。
///
///   效果：过渡的 0.2 秒里角色"站在原地换姿势"，不会跑位。
///         姿势本身照常混合（该躺下还是会躺下），只是位置被焊住。
///
///   为什么只水平：
///     竖直方向必须让动画自己控制（躺下时要降低高度），
///     焊住竖直方向会让角色"悬在空中躺"。
///
/// ===================== 用法 =====================
///
///   ① 挂在 character 上（有 Animator 的那个物体）
///   ② Root Bone 拖 mixamorig:Hips（留空会自动按名字找）
///   ③ Active 默认【关闭】—— 由 PlayPatientSequenceOnStep 在需要时打开
///   ④ 保留 Log To Console 看诊断
/// </summary>
[DefaultExecutionOrder(20000)]
public class AnimationTransitionAnchor : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("本物体的 Animator。留空自动获取")]
    public Animator animator;

    [Tooltip("动画的根骨骼。通常是 mixamorig:Hips。留空会按名字自动找")]
    public Transform rootBone;

    [Tooltip("自动查找根骨骼时用的名字")]
    public string rootBoneName = "mixamorig:Hips";

    [Header("选项")]
    [Tooltip("总开关。默认关闭，由别的脚本在需要时打开")]
    public bool active = false;

    [Tooltip("只焊水平方向（推荐）。竖直方向交给动画自己控制")]
    public bool horizontalOnly = true;

    [Header("调试")]
    [Tooltip("打印每次过渡的锚定与修正量")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private bool wasInTransition;
    private Vector3 anchorWorld;
    private int anchorStateHash;
    private float correctionThisTransition;

    private bool warnedNoAnimator;
    private bool warnedNoBone;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        ResolveRootBone();
    }

    private void Start()
    {
        if (!logToConsole) return;

        Debug.Log($"[过渡锚定] 「{name}」初始化检查：\n" +
                  $"  Animator  = {(animator != null ? animator.name : "❌ 无")}\n" +
                  $"  Root Bone = {(rootBone != null ? rootBone.name : "❌ 找不到")}" +
                  (rootBone != null && rootBone.parent == transform ? "，是本物体的直接子物体 ✅" : "") + "\n" +
                  $"  Active    = {active}" +
                  (active ? "" : "（等待被打开）"), this);

        if (animator == null || rootBone == null)
            Debug.LogError($"[过渡锚定] 「{name}」引用不全，脚本不会动作。" +
                           $"请把 Root Bone 拖动指定为 mixamorig:Hips", this);
    }

    private void ResolveRootBone()
    {
        if (rootBone != null) return;

        // ① 精确名字
        if (!string.IsNullOrEmpty(rootBoneName))
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == transform) continue;
                if (t.name.Trim() == rootBoneName) { rootBone = t; return; }
            }
        }

        // ② 名字里含 hips
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == transform) continue;
            if (t.name.IndexOf("hips", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                rootBone = t;
                return;
            }
        }
    }

    private void LateUpdate()
    {
        if (!active) return;

        if (animator == null)
        {
            if (!warnedNoAnimator)
            {
                warnedNoAnimator = true;
                Debug.LogError($"[过渡锚定] 「{name}」没有 Animator，脚本不会动作", this);
            }
            return;
        }

        if (rootBone == null)
        {
            if (!warnedNoBone)
            {
                warnedNoBone = true;
                Debug.LogError($"[过渡锚定] 「{name}」的 Root Bone 是空的，脚本不会动作。\n" +
                               $"  请把 mixamorig:Hips 拖到 Root Bone 字段上", this);
            }
            return;
        }

        bool inTransition = animator.IsInTransition(0);

        // ---------- 过渡开始 ----------
        if (inTransition && !wasInTransition)
        {
            anchorWorld = rootBone.position;
            anchorStateHash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            correctionThisTransition = 0f;

            if (logToConsole)
                Debug.Log($"[过渡锚定] 过渡开始 → 锚定髋骨世界位置 " +
                          $"{anchorWorld.ToString("F3")}（目标状态 hash={anchorStateHash}）", this);
        }

        // ---------- 过渡进行中：把角色焊在锚点上 ----------
        if (inTransition)
        {
            Vector3 shift = anchorWorld - rootBone.position;

            if (horizontalOnly)
            {
                shift.y = 0f;
            }

            if (shift.sqrMagnitude > 0f)
            {
                transform.position += shift;
                correctionThisTransition += shift.magnitude;
            }
        }

        // ---------- 过渡结束 ----------
        if (!inTransition && wasInTransition)
        {
            if (logToConsole)
                Debug.Log($"[过渡锚定] 过渡结束 → 本次共修正 {correctionThisTransition:F4} 米" +
                          $"（这就是原本会让角色『弹回原点』的那段位移）", this);
        }

        wasInTransition = inTransition;
    }

    /// <summary>运行时开关。打开时不会产生任何瞬移。</summary>
    public void SetActive(bool on)
    {
        active = on;

        if (on)
        {
            ResolveRootBone();
            wasInTransition = false;
            correctionThisTransition = 0f;
        }

        if (logToConsole)
            Debug.Log($"[过渡锚定] 「{name}」{(on ? "开启" : "关闭")}" +
                      (on && rootBone != null ? $"，根骨骼 = {rootBone.name}" : "") +
                      (on && rootBone == null ? "，⚠️ 但 Root Bone 是空的" : ""), this);
    }
}
