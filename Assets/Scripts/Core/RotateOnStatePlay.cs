using UnityEngine;

/// <summary>
/// 播放某个动作时旋转角色朝向 —— 用于「右转 / 左转」这类动画。
///
/// ===================== 两种角度来源 =====================
///
///   【跟随动画】（推荐）
///     每帧从髋骨的本地 Y 旋转里读出"这一帧转了多少"，应用到角色对象上，
///     再把髋骨的 Y 旋转钉回去。
///     → 动画转多少，角色就转多少，不用手填角度，绝不会转两遍。
///
///   【固定角度】
///     按你填的 Yaw Degrees 平滑转完。
///     → 只在动画没有自带旋转、或你想强行指定时才用。
///
/// ===================== 为什么需要它 =====================
///
///   Mixamo 的「右转」是 In Place 动画：身体在骨骼里转了，
///   但角色对象的 rotation 没变。切到下一段动画时姿势又回正，
///   所以最终朝向没变 —— 看起来"转了个寂寞"。
///
///   「跟随动画」模式把骨骼里的旋转"搬到"对象上：
///     视觉上完全一致（连续、不跳），
///     但切到下一段后朝向会留在新方向。
///
/// ===================== 旋转方向约定（Unity）=====================
///
///   transform.RotateAround(p, up, +90)
///     → 前方向 +Z 转到 +X
///     → 从 +Y 往下看（俯视）是【顺时针】
///
///   「跟随动画」模式不需要填角度，方向由动画决定。
///   「固定角度」模式下：右转填 90，左转填 -90。
///
/// ===================== 用法 =====================
///
///   ① 挂在 character 上
///   ② 填：
///        State Parameter = State
///        Watch State     = 1          （1 = 左腿受伤右转）
///        Yaw Source      = 跟随动画
///        Root Bone       ← mixamorig:Hips
///        Pivot Bone      ← mixamorig:Hips
///   ③ 左转再加一个组件：
///        Watch State = 5              （5 = 左腿受伤左转）
///
///   ⚠️ 同一物体可以挂多个本组件，各自管一个 State。
/// </summary>
public class RotateOnStatePlay : MonoBehaviour
{
    public enum YawSource
    {
        跟随动画,
        固定角度,
    }

    [Header("引用")]
    [Tooltip("本物体的 Animator。留空自动获取")]
    public Animator animator;

    [Tooltip("读取旋转用的根骨骼。通常是 mixamorig:Hips。留空自动按名字找")]
    public Transform rootBone;

    [Tooltip("自动查找根骨骼时用的名字")]
    public string rootBoneName = "mixamorig:Hips";

    [Tooltip("旋转的圆心（绕哪个点转）。留空自动用根骨骼")]
    public Transform pivotBone;

    [Tooltip("勾上 = 绕【骨骼位置】转（人原地打转）\n" +
             "取消 = 绕【本物体原点】转 —— 你的角色原点不在身体中轴上，会甩出去一大段")]
    public bool pivotAtBone = true;

    [Header("监视的状态")]
    [Tooltip("Animator 里控制状态的整数参数名")]
    public string stateParameter = "State";

    [Tooltip("监视哪个 State 值。1 = 左腿受伤右转，5 = 左腿受伤左转")]
    public int watchState = 1;

    [Header("角度来源")]
    [Tooltip("跟随动画 = 从骨骼里读旋转量（推荐，不会转两遍）\n" +
             "固定角度 = 用下面填的 Yaw Degrees")]
    public YawSource yawSource = YawSource.跟随动画;

    [Tooltip("【仅固定角度模式】要转多少度。右转 90，左转 -90")]
    public float yawDegrees = 90f;

    [Tooltip("【仅固定角度模式】勾上 = 随动画平滑转完；取消 = 播完再瞬间转")]
    public bool smoothWithAnimation = true;

    [Header("行为")]
    [Tooltip("只转一次。取消勾选的话，每次进入这个状态都会再转一次")]
    public bool applyOnce = true;

    [Header("调试")]
    [Tooltip("打印每一帧读到的骨骼旋转增量（很啰嗦，排查时才开）")]
    public bool logEveryFrame = false;

    [Tooltip("打印进入 / 离开状态时的汇总")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private bool watching;
    private bool doneThisEnter;
    private bool everApplied;

    private float watchStartTime;
    private float clipLength = -1f;
    private float appliedSoFar;          // 固定角度模式用
    private float transferredTotal;      // 跟随动画模式：累计转移了多少度

    private float lastBoneYaw;
    private float pinBoneYaw;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        ResolveBones();
    }

    private void Start()
    {
        if (!logToConsole) return;

        string rootInfo = rootBone != null ? rootBone.name : "❌ 找不到";
        string pivotInfo = !pivotAtBone
            ? "本物体原点（⚠️ 可能甩出去）"
            : (pivotBone != null ? pivotBone.name : "❌ 找不到骨骼，退回原点");

        Debug.Log($"[转向] 「{name}」初始化检查：\n" +
                  $"  监视状态 = {watchState}\n" +
                  $"  角度来源 = {yawSource}" +
                  (yawSource == YawSource.固定角度 ? $"（{yawDegrees}°）" : "") + "\n" +
                  $"  Root Bone = {rootInfo}\n" +
                  $"  旋转圆心 = {pivotInfo}", this);
    }

    private void ResolveBones()
    {
        // 根骨骼
        if (rootBone == null)
        {
            if (!string.IsNullOrEmpty(rootBoneName))
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t == transform) continue;
                    if (t.name.Trim() == rootBoneName) { rootBone = t; break; }
                }
            }

            if (rootBone == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t == transform) continue;
                    if (t.name.IndexOf("hips", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        rootBone = t;
                        break;
                    }
                }
            }
        }

        // 圆心：默认跟根骨骼一样
        if (pivotBone == null) pivotBone = rootBone;
    }

    private void LateUpdate()
    {
        if (animator == null) return;

        int currentState = animator.GetInteger(stateParameter);

        // ==================== 进入监视状态 ====================
        if (currentState == watchState)
        {
            if (!watching)
            {
                if (applyOnce && everApplied) return;   // 已经转过了

                watching = true;
                doneThisEnter = false;
                watchStartTime = Time.time;
                clipLength = -1f;
                appliedSoFar = 0f;
                transferredTotal = 0f;

                if (yawSource == YawSource.跟随动画 && rootBone != null)
                {
                    pinBoneYaw = rootBone.localRotation.eulerAngles.y;
                    lastBoneYaw = pinBoneYaw;
                }

                if (logToConsole)
                    Debug.Log($"[转向] 「{name}」进入状态 {watchState}" +
                              (yawSource == YawSource.跟随动画
                                  ? "，开始从动画提取旋转"
                                  : $"，准备旋转 {yawDegrees}°"), this);
            }

            if (yawSource == YawSource.跟随动画) TransferFromAnimation();
            else
            {
                if (clipLength < 0f && !animator.IsInTransition(0))
                {
                    float len = animator.GetCurrentAnimatorStateInfo(0).length;
                    if (len > 0.01f) clipLength = len;
                }

                if (!doneThisEnter) ApplyManualRotation();
            }

            return;
        }

        // ==================== 已经离开监视状态 ====================
        if (watching)
        {
            if (!doneThisEnter && yawSource == YawSource.固定角度)
                FinishManualRotation();

            if (logToConsole)
            {
                if (yawSource == YawSource.跟随动画)
                    Debug.Log($"[转向] 「{name}」离开状态 {watchState}，" +
                              $"本次从动画转移了 {transferredTotal:F1}° → " +
                              $"当前朝向 Y = {transform.eulerAngles.y:F1}°", this);
                else
                    Debug.Log($"[转向] 「{name}」离开状态 {watchState}，" +
                              $"累计旋转 {appliedSoFar:F1}° → " +
                              $"当前朝向 Y = {transform.eulerAngles.y:F1}°", this);
            }

            watching = false;
            doneThisEnter = false;
            appliedSoFar = 0f;
        }
    }

    // ==================== 跟随动画：从骨骼里提取旋转 ====================
    private void TransferFromAnimation()
    {
        if (rootBone == null) return;

        Vector3 euler = rootBone.localRotation.eulerAngles;
        float curYaw = euler.y;

        // 和上一帧的差（处理 350° → 10° 这类回绕）
        float delta = Mathf.DeltaAngle(lastBoneYaw, curYaw);
        lastBoneYaw = curYaw;

        if (Mathf.Abs(delta) > 0.001f)
        {
            // ① 把这部分旋转转移到角色对象上
            ApplyYaw(delta);
            transferredTotal += delta;

            // ② 把骨骼的 Y 旋转钉回起始值，避免身体转两次
            rootBone.localRotation = Quaternion.Euler(euler.x, pinBoneYaw, euler.z);

            if (logEveryFrame)
                Debug.Log($"[转向] 本帧转移 {delta:F2}°（累计 {transferredTotal:F1}°）", this);
        }
    }

    // ==================== 固定角度模式 ====================
    private void ApplyManualRotation()
    {
        float progress;

        if (smoothWithAnimation && clipLength > 0.01f)
        {
            progress = Mathf.Clamp01((Time.time - watchStartTime) / clipLength);
        }
        else if (clipLength > 0.01f)
        {
            progress = Mathf.Clamp01((Time.time - watchStartTime) / clipLength) >= 0.95f ? 1f : 0f;
        }
        else
        {
            progress = 1f;      // 读不到时长就直接转完
        }

        float targetAngle = yawDegrees * progress;
        float delta = targetAngle - appliedSoFar;

        if (Mathf.Abs(delta) < 0.0001f)
        {
            if (progress >= 1f) doneThisEnter = true;
            return;
        }

        ApplyYaw(delta);
        appliedSoFar = targetAngle;

        if (progress >= 1f)
        {
            doneThisEnter = true;
            everApplied = true;

            if (logToConsole)
                Debug.Log($"[转向] 「{name}」旋转完成，累计 {appliedSoFar:F1}° → " +
                          $"当前朝向 Y = {transform.eulerAngles.y:F1}°", this);
        }
    }

    private void FinishManualRotation()
    {
        float delta = yawDegrees - appliedSoFar;

        if (Mathf.Abs(delta) > 0.0001f)
        {
            ApplyYaw(delta);
            appliedSoFar = yawDegrees;
        }

        doneThisEnter = true;
        everApplied = true;
    }

    // ==================== 真正的旋转 ====================
    /// <summary>
    /// 绕身体中轴旋转。
    /// ⚠️ 必须用 RotateAround：你的 character 原点不在身体中轴上，
    ///    用 transform.Rotate 会把角色甩出去一大段。
    /// </summary>
    private void ApplyYaw(float deltaDegrees)
    {
        if (Mathf.Abs(deltaDegrees) < 0.0001f) return;

        if (pivotAtBone && pivotBone != null)
        {
            Vector3 pivot = pivotBone.position;
            pivot.y = transform.position.y;
            transform.RotateAround(pivot, Vector3.up, deltaDegrees);
        }
        else
        {
            transform.Rotate(0f, deltaDegrees, 0f, Space.World);
        }
    }

    /// <summary>手动转一次（Inspector 右键菜单，方便测试方向）</summary>
    [ContextMenu("手动旋转一次")]
    public void RotateOnce()
    {
        float angle = yawSource == YawSource.固定角度 ? yawDegrees : 90f;

        ApplyYaw(angle);

        Debug.Log($"[转向] 「{name}」手动旋转 {angle}° → " +
                  $"当前朝向 Y = {transform.eulerAngles.y:F1}°，" +
                  $"圆心 = {(pivotAtBone && pivotBone != null ? pivotBone.name : "本物体原点")}", this);
    }
}
