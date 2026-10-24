using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 关卡传送器 —— 把整个 XR 玩家装备（XR Origin）搬到指定坐标。
///
/// 和"移动用的传送"（TeleportationArea）完全不同：
///   那个是在同一片场地里从 A 点跳到 B 点
///   这个是进入关卡／切换场地时，把玩家整体搬到新的起始位置
///
/// ⚠️ 关键技术点：
///   XR Origin 上带 CharacterController。CharacterController 会"锁住"位置，
///   如果直接改 Transform.position，下一帧它会把玩家推回原处。
///   所以必须先禁用 CharacterController，移动，再启用，并且 SyncTransforms。
///
/// 用法：
///   把这个组件挂在任意物体上（比如一个门、一个按钮、一个空的 GameManager），
///   把 targetPoint 设成目标位置的空物体，然后调用 Teleport()。
/// </summary>
public class PlayerTeleporter : MonoBehaviour
{
    [Header("目标位置")]
    [Tooltip("玩家要被传送到的位置。留空则使用下面的坐标")]
    [SerializeField] private Transform targetPoint;

    [Tooltip("如果没设 targetPoint，就用这个世界坐标")]
    [SerializeField] private Vector3 targetPosition = Vector3.zero;

    [Tooltip("传送后玩家面朝的方向（世界坐标 Y 轴角度，0 = 朝 +Z）")]
    [SerializeField] private float targetYaw = 0f;

    [Header("可选：键盘快捷键（便于编辑器测试）")]
    [Tooltip("按这个键触发传送。选的是 XR Device Simulator 没占用的键")]
    [SerializeField] private Key testKey = Key.None;

    [Header("调试")]
    [SerializeField] private bool logToConsole = true;

    /// <summary>传送完成时触发</summary>
    public event System.Action OnTeleported;

    private void Update()
    {
        if (testKey == Key.None) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        var c = kb[testKey];
        if (c != null && c.wasPressedThisFrame) Teleport();
    }

    /// <summary>
    /// 执行传送。可供 Button.onClick、XRSimpleInteractable 事件、
    /// 或 TriggerZone 调用。
    /// </summary>
    public void Teleport()
    {
        var origin = FindXROrigin();
        if (origin == null)
        {
            Debug.LogError("[PlayerTeleporter] 场景里找不到 XR Origin，无法传送");
            return;
        }

        Vector3 dest = (targetPoint != null) ? targetPoint.position : targetPosition;
        Quaternion rot = Quaternion.Euler(0f, targetYaw, 0f);

        // ---------- 1. 先禁用 CharacterController ----------
        // 否则它会在下一帧把玩家推回原位
        var cc = origin.GetComponent<CharacterController>();
        bool ccWasEnabled = false;
        if (cc != null)
        {
            ccWasEnabled = cc.enabled;
            cc.enabled = false;
        }

        // ---------- 2. 移动 XR Origin ----------
        origin.transform.SetPositionAndRotation(dest, rot);

        // ---------- 3. 把地板偏移归零 ----------
        // XROrigin 的相机是挂在 CameraFloorOffsetObject 下的，
        // 如果之前有过楼层偏移，这里要清掉，否则高度会不对
        var floorOffset = origin.CameraFloorOffsetObject;
        if (floorOffset != null) floorOffset.transform.localPosition = Vector3.zero;

        // ---------- 4. 强制同步物理变换 ----------
        // 让 CharacterController 和碰撞体立刻感知到新位置
        Physics.SyncTransforms();

        // ---------- 5. 恢复 CharacterController ----------
        if (cc != null) cc.enabled = ccWasEnabled;

        if (logToConsole)
            Debug.Log($"[PlayerTeleporter] 已传送到 {dest}（朝向 {targetYaw}°）");

        OnTeleported?.Invoke();
    }

    /// <summary>运行时设置目标点</summary>
    public void SetTarget(Transform point, float yaw = 0f)
    {
        targetPoint = point;
        targetYaw = yaw;
    }

    /// <summary>运行时设置目标坐标</summary>
    public void SetTargetPosition(Vector3 pos, float yaw = 0f)
    {
        targetPoint = null;
        targetPosition = pos;
        targetYaw = yaw;
    }

    private Unity.XR.CoreUtils.XROrigin FindXROrigin()
    {
        // 先在本场景找，找不到再全局找
        var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin == null)
        {
            foreach (var o in Resources.FindObjectsOfTypeAll<Unity.XR.CoreUtils.XROrigin>())
            {
                if (o.gameObject.scene.IsValid()) return o;
            }
        }
        return origin;
    }
}
