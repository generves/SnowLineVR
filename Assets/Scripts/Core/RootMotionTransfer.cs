using UnityEngine;

/// <summary>
/// 手动根运动（Custom Root Motion）—— 把动画里根骨骼产生的位移，
/// 转移到本物体（character）身上。
///
/// ===================== 解决什么问题 =====================
///
///   每段动画的根骨骼原点都是各自那一份，切换 clip 时网格会跳回原点
///   → 表现为「每播完一个动画就返回起点」。
///
///   本脚本把每段动画的位移【累积】到本物体上，于是：
///     动画切到下一段时，网格从"上一段的落点"继续，不再跳回。
///
///   它做的事，本质上等于 Unity 内部的 Apply Root Motion：
///     ① 每帧读根骨骼本地位置，算出与上一帧的差值
///     ② 差值转换到世界空间（含缩放！）后加到本物体的位置
///     ③ 把根骨骼水平位置钉回本动画的起始位置，避免网格移动两次
///
///   竖直方向（Y）不钉，保留动画原本的起伏 / 蹲下 / 躺下。
///
/// ===================== 用法 =====================
///
///   ① 挂在 character 上（有 Animator 的那个物体）
///   ② Root Bone 拖 mixamorig:Hips（留空会自动按名字找）
///   ③ Active 默认【关闭】—— 由 PlayPatientSequenceOnStep 在需要时打开
///   ④ 想看诊断就保持 Log To Console = ☑
/// </summary>
[DefaultExecutionOrder(20000)]   // 尽量最后执行，避免被别的 LateUpdate 覆盖
public class RootMotionTransfer : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("本物体的 Animator。留空自动获取")]
    public Animator animator;

    [Tooltip("动画的根骨骼。通常是 mixamorig:Hips。留空会按下面的名字自动找")]
    public Transform rootBone;

    [Tooltip("自动查找根骨骼时用的名字")]
    public string rootBoneName = "mixamorig:Hips";

    [Header("选项")]
    [Tooltip("总开关。默认关闭，由别的脚本在需要时打开")]
    public bool active = false;

    [Tooltip("位移倍率。1 = 和动画完全一致")]
    [Range(0.1f, 3f)]
    public float motionScale = 1f;

    [Tooltip("单帧【世界】水平位移超过这个值（米），判定为动画切换或循环回绕，丢弃这一帧")]
    [Range(0.02f, 1f)]
    public float discontinuityThreshold = 0.15f;

    [Tooltip("把根骨骼的水平位置钉住。必须保持勾选，否则网格会被推动两次")]
    public bool pinRootHorizontal = true;

    [Header("调试")]
    [Tooltip("总调试开关。会打印初始化检查 + 前几帧的实际位移量")]
    public bool logToConsole = true;

    [Tooltip("打印每一次被丢弃的不连续位移")]
    public bool logDiscontinuity = false;

    // ---------- 内部 ----------
    private Vector3 lastLocalPos;
    private Vector3 pinRef;
    private int lastStateHash;
    private bool hasRef;

    private int appliedFrames;         // 已经应用过多少帧（用于前几帧打印）
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

        string boneInfo;
        if (rootBone == null) boneInfo = "❌ 找不到";
        else
        {
            boneInfo = $"{rootBone.name}（世界位置 {rootBone.position.ToString("F3")}）";
            if (rootBone.parent == transform) boneInfo += "，是本物体的直接子物体 ✅";
            else boneInfo += $"，父物体是「{(rootBone.parent != null ? rootBone.parent.name : "无")}」";
        }

        Debug.Log($"[根运动] 「{name}」初始化检查：\n" +
                  $"  Animator  = {(animator != null ? animator.name : "❌ 无")}\n" +
                  $"  Root Bone = {boneInfo}\n" +
                  $"  物体缩放  = {transform.lossyScale.ToString("F5")}  ← 位移换算要用它\n" +
                  $"  Active    = {active}" +
                  (active ? "" : "（等待被打开）"), this);

        if (animator == null || rootBone == null)
            Debug.LogError($"[根运动] 「{name}」引用不全，脚本不会动作。" +
                           $"请把 Root Bone 拖动指定为 mixamorig:Hips", this);
    }

    /// <summary>按名字找根骨骼。先精确匹配，再模糊匹配 Hips</summary>
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

        // ② 名字里含 Hips（大小写不敏感）
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
                Debug.LogError($"[根运动] 「{name}」没有 Animator，脚本不会动作", this);
            }
            return;
        }

        if (rootBone == null)
        {
            if (!warnedNoBone)
            {
                warnedNoBone = true;
                Debug.LogError($"[根运动] 「{name}」的 Root Bone 是空的，脚本不会动作。\n" +
                               $"  请把 mixamorig:Hips 拖到 Root Bone 字段上", this);
            }
            return;
        }

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = stateInfo.shortNameHash;

        Vector3 cur = rootBone.localPosition;

        // ---------- 第一次 / 切换了动画 → 重新建立基准 ----------
        if (!hasRef || stateHash != lastStateHash)
        {
            ResetReference(cur, stateHash);

            if (logToConsole && appliedFrames == 0)
                Debug.Log($"[根运动] 建立基准（状态 hash={stateHash}，根骨骼本地位置 {cur.ToString("F2")}）", this);

            return;
        }

        Vector3 delta = cur - lastLocalPos;
        lastLocalPos = cur;

        // 只看水平方向（这是【本地/模型单位】）
        Vector3 horizontalLocal = new Vector3(delta.x, 0f, delta.z);

        // ⚠️ 必须用 TransformVector（含缩放），不能用 TransformDirection（会归一化、丢掉缩放）
        Vector3 worldDelta = transform.TransformVector(horizontalLocal) * motionScale;

        // ---------- 不连续：动画切换 / 循环回绕 → 丢弃这一帧 ----------
        // 注意：这里比较的是【世界米】
        if (worldDelta.magnitude > discontinuityThreshold)
        {
            if (logDiscontinuity)
                Debug.Log($"[根运动] 丢弃一次不连续位移 {worldDelta.magnitude:F3} 米", this);

            pinRef = new Vector3(cur.x, 0f, cur.z);
            return;
        }

        // ---------- 把位移应用到物体上 ----------
        if (worldDelta.sqrMagnitude > 0f)
        {
            transform.position += worldDelta;

            // 前 10 帧无条件打印，方便确认脚本真的在动
            if (logToConsole && appliedFrames < 10)
            {
                Debug.Log($"[根运动] 应用位移 {worldDelta.magnitude:F4} 米" +
                          $"（累计第 {appliedFrames + 1} 帧）", this);
            }

            appliedFrames++;
        }

        // ---------- 把根骨骼水平位置钉回去，避免网格移动两次 ----------
        if (pinRootHorizontal)
        {
            rootBone.localPosition = new Vector3(pinRef.x, cur.y, pinRef.z);
        }
    }

    private void ResetReference(Vector3 cur, int stateHash)
    {
        lastLocalPos = cur;
        pinRef = new Vector3(cur.x, 0f, cur.z);
        lastStateHash = stateHash;
        hasRef = true;
    }

    /// <summary>
    /// 运行时开关。
    /// 打开时会把【当前】根骨骼位置作为基准，所以不会因为"打开瞬间"产生位移跳变。
    /// </summary>
    public void SetActive(bool on)
    {
        active = on;

        if (on)
        {
            ResolveRootBone();
            ResetReference();
            appliedFrames = 0;
        }

        if (logToConsole)
            Debug.Log($"[根运动] 「{name}」{(on ? "开启" : "关闭")}" +
                      (on && rootBone != null ? $"，根骨骼 = {rootBone.name}" : "") +
                      (on && rootBone == null ? "，⚠️ 但 Root Bone 是空的" : ""), this);
    }

    /// <summary>手动重置基准。在把角色瞬移到别处之后要调用一次。</summary>
    public void ResetReference()
    {
        if (rootBone == null) return;

        ResetReference(rootBone.localPosition,
                       animator != null ? animator.GetCurrentAnimatorStateInfo(0).shortNameHash : 0);
    }
}
