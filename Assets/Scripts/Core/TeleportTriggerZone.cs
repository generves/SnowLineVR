using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// 传送触发区 —— 玩家走进这个区域，就触发传送。
///
/// 用于"走进门 → 传送到关卡起点"这类设计。
///
/// 设置要求：
///   · 本物体需要一个 Collider，并勾选 Is Trigger
///   · 需要一个 Rigidbody（设为 Kinematic），否则触发事件不会产生
///
/// 判定方式（二选一）：
///   A. 用 Tag 判定     —— 需要给 XR Origin 打上指定 Tag
///   B. 用 XROrigin 判定 —— 检测进入的碰撞体是不是玩家（推荐，不用配 Tag）
/// </summary>
[RequireComponent(typeof(Collider))]
public class TeleportTriggerZone : MonoBehaviour
{
    [Header("传送目标")]
    [SerializeField] private PlayerTeleporter teleporter;

    [Tooltip("玩家进入后传送到的目标点（会覆盖 teleporter 自己的设置）")]
    [SerializeField] private Transform targetPoint;

    [Tooltip("传送后玩家朝向（Y 轴角度，0 = 朝 +Z）")]
    [SerializeField] private float targetYaw = 0f;

    [Header("触发条件")]
    [Tooltip("勾选后只触发一次，之后失效")]
    [SerializeField] private bool triggerOnce = true;

    [Tooltip("进入后延迟多少秒再传送（0 = 立即）")]
    [SerializeField] private float delay = 0f;

    [Tooltip("可选：只有带着这个 Tag 的物体进入才触发。留空则用 XROrigin 判定")]
    [SerializeField] private string requiredTag = "";

    [Header("视觉提示")]
    [Tooltip("可选：触发区可见的模型（比如一道光柱、一个门框）")]
    [SerializeField] private GameObject visual;

    [Header("调试")]
    [SerializeField] private bool logToConsole = true;

    private bool hasTriggered;

    private void Reset()
    {
        // 在编辑器里添加组件时自动把 Collider 设成 Trigger
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        // 触发事件需要至少一个 Rigidbody
        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;   //  kinematic 不受重力影响，但能产生触发事件
            rb.useGravity = false;
        }

        if (teleporter == null) teleporter = FindAnyObjectByType<PlayerTeleporter>();

        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[TeleportTriggerZone] '{name}' 的 Collider 没有勾选 Is Trigger，触发可能不生效");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggerOnce && hasTriggered) return;

        if (!IsPlayer(other)) return;

        hasTriggered = true;

        if (logToConsole)
            Debug.Log($"[TeleportTriggerZone] 玩家进入 '{name}'，准备传送");

        if (visual != null) visual.SetActive(false);

        if (delay > 0f) Invoke(nameof(DoTeleport), delay);
        else DoTeleport();
    }

    private void DoTeleport()
    {
        if (teleporter == null)
        {
            Debug.LogError("[TeleportTriggerZone] 没有找到 PlayerTeleporter，无法传送");
            return;
        }

        // 如果本区域指定了目标点，就覆盖传送器的设置
        if (targetPoint != null) teleporter.SetTarget(targetPoint, targetYaw);

        teleporter.Teleport();
    }

    /// <summary>判断进入的碰撞体是不是玩家</summary>
    private bool IsPlayer(Collider other)
    {
        // 方式 A：按 Tag 判定
        if (!string.IsNullOrEmpty(requiredTag))
            return other.CompareTag(requiredTag);

        // 方式 B：看它是不是挂在 XR Origin 下面（推荐）
        var xrOrigin = other.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
        return xrOrigin != null;
    }

    /// <summary>手动重置触发状态（便于反复测试）</summary>
    public void ResetTrigger()
    {
        hasTriggered = false;
        if (visual != null) visual.SetActive(true);
    }
}
