using UnityEngine;

/// <summary>
/// 过渡时对齐高度 —— 解决「切换动作时人物高度跳一下」。
///
/// ===================== 问题 =====================
///
///   两个 clip 的髋部高度不同，cross-fade 结束后身体会停在
///   新 clip 的高度上，看起来就是"切换瞬间人往上一窜"。
///
///   例：躺到担架上（髋部 0.42 米）→ 平躺轻微呼吸1（髋部 0.55 米）
///
///   ⚠️ AnimationTransitionAnchor 只补偿水平方向，竖直方向故意不动
///      （否则"站立 → 躺下"那种应该降低高度的过渡会失效）。
///      所以高度差要用本脚本单独处理。
///
/// ===================== 做法 =====================
///
///   ① 切换发生的那一帧，记下髋部的世界高度 Y1
///   ② 等 settleDelay 秒，让 cross-fade 走完
///   ③ 读髋部的新高度 Y2
///   ④ 把角色整体平移 (Y1 - Y2)
///   ⑤ 这个平移是【持久的】，后续整个循环都保持正确高度
///
///   只对指定的 fromState → toState 这一种过渡生效，
///   不影响其他过渡。
///
/// ===================== 用法 =====================
///
///   ① 挂在 character 上
///   ② Root Bone 拖 mixamorig:Hips
///   ③ From State = 16（躺到担架上）、To State = 14（平躺轻微呼吸1）
///   ④ 不同的过渡各挂一个组件即可，互不干扰
/// </summary>
[DefaultExecutionOrder(20010)]   // 在 AnimationTransitionAnchor(20000) 之后跑
public class HeightMatchOnTransition : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("本物体的 Animator。留空自动获取")]
    public Animator animator;

    [Tooltip("测量高度的骨骼。通常是 mixamorig:Hips。留空自动按名字找")]
    public Transform rootBone;

    [Tooltip("自动查找骨骼时用的名字")]
    public string rootBoneName = "mixamorig:Hips";

    [Header("监视的过渡")]
    [Tooltip("Animator 里控制状态的整数参数名")]
    public string stateParameter = "State";

    [Tooltip("切换【前】的 State。16 = 躺到担架上")]
    public int fromState = 16;

    [Tooltip("切换【后】的 State。14 = 平躺轻微呼吸1")]
    public int toState = 14;

    [Header("选项")]
    [Tooltip("等混合结束再对齐（秒）。要比 AC_Patient 的 Transition Duration(0.2) 长一点")]
    public float settleDelay = 0.28f;

    [Tooltip("额外手动微调（米）。正 = 往上，负 = 往下")]
    public float manualOffsetY = 0f;

    [Tooltip("只补偿一次。取消勾选的话，每次发生这个过渡都会补偿")]
    public bool once = true;

    [Header("调试")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private int lastState = -99999;
    private bool pending;
    private float timer;
    private float yBefore;
    private bool everDone;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        if (rootBone == null && !string.IsNullOrEmpty(rootBoneName))
            rootBone = FindBone(rootBoneName);
    }

    private void Start()
    {
        if (!logToConsole) return;

        Debug.Log($"[高度对齐] 「{name}」初始化检查：\n" +
                  $"  Animator  = {(animator != null ? animator.name : "❌ 无")}\n" +
                  $"  Root Bone = {(rootBone != null ? rootBone.name : "❌ 找不到")}\n" +
                  $"  监视过渡  = {fromState} → {toState}\n" +
                  $"  等待时长  = {settleDelay:F2} 秒", this);

        if (animator == null || rootBone == null)
            Debug.LogError($"[高度对齐] 「{name}」引用不全，脚本不会动作。" +
                           $"请把 Root Bone 拖动指定为 mixamorig:Hips", this);
    }

    private Transform FindBone(string boneName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == transform) continue;
            if (t.name.Trim() == boneName) return t;
        }

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == transform) continue;
            if (t.name.IndexOf("hips", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return t;
        }

        return null;
    }

    private void LateUpdate()
    {
        if (animator == null || rootBone == null) return;

        int currentState = animator.GetInteger(stateParameter);

        // ==================== 检测到目标过渡开始 ====================
        if (lastState == fromState && currentState == toState && !pending)
        {
            if (once && everDone)
            {
                lastState = currentState;
                return;
            }

            yBefore = rootBone.position.y;
            pending = true;
            timer = 0f;

            if (logToConsole)
                Debug.Log($"[高度对齐] 检测到过渡 {fromState} → {toState}，" +
                          $"切换前髋部高度 = {yBefore:F4} 米", this);
        }

        lastState = currentState;

        // ==================== 等混合结束 → 一次性补偿 ====================
        if (pending)
        {
            timer += Time.deltaTime;

            if (timer >= settleDelay)
            {
                float yAfter = rootBone.position.y;
                float delta = (yBefore - yAfter) + manualOffsetY;

                if (Mathf.Abs(delta) > 0.00001f)
                    transform.position += new Vector3(0f, delta, 0f);

                pending = false;
                everDone = true;

                if (logToConsole)
                    Debug.Log($"[高度对齐] 髋部新高度 = {yAfter:F4} 米 → " +
                              $"整体平移 {delta:+0.0000;-0.0000;0.0000} 米" +
                              (Mathf.Abs(manualOffsetY) > 0.00001f
                                  ? $"（含手动微调 {manualOffsetY:+0.0000;-0.0000;0.0000}）"
                                  : ""), this);
            }
        }
    }

    /// <summary>手动对齐一次（Inspector 右键菜单，用来现场调试）</summary>
    [ContextMenu("立即对齐一次")]
    public void AlignOnce()
    {
        if (rootBone == null)
        {
            Debug.LogError($"[高度对齐] 「{name}」Root Bone 是空的", this);
            return;
        }

        float delta = yBefore - rootBone.position.y + manualOffsetY;

        if (Mathf.Abs(delta) > 0.00001f)
            transform.position += new Vector3(0f, delta, 0f);

        Debug.Log($"[高度对齐] 「{name}」手动平移 {delta:+0.0000;-0.0000;0.0000} 米", this);
    }
}
