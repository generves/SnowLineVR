using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;      // XROrigin 类型在这里（XRI 3.x）
using System.Text;

/// <summary>
/// XR 相机位置监视器 —— 在 Play 模式下实时显示相机高度和位置来源。
///
/// 解决的问题：
///   "桌面测试时的相机高度" 和 "戴头显时的高度" 是不是一回事？
///   XR Origin 的高度、Camera Offset、头显追踪，三者怎么叠加？
///
/// 本脚本每帧打印：
///   · XR Origin 世界位置
///   · Camera Offset 本地位置
///   · Main Camera 的本地位置（被追踪系统写入的值）
///   · Main Camera 的世界位置（眼睛实际在哪）
///   · 相机的父物体是谁（判断是否被追踪接管）
///
/// 用法：
///   ① 挂在 XR Origin 上（或场景里任意物体上）
///   ② Play，看左上角覆盖文字和 Console
/// </summary>
public class XRCameraMonitor : MonoBehaviour
{
    [Header("显示")]
    [Tooltip("在屏幕上显示诊断信息。正式测试时关掉")]
    public bool showOnScreen = false;
    public bool logToConsole = false;
    public int fontSize = 20;

    [Tooltip("每多少秒打印一次 Console 日志")]
    public float logInterval = 2f;

    private Transform origin;
    private Transform camOffset;
    private Camera cam;
    private float logTimer;

    private void Start()
    {
        // 找 XR Origin
        var rig = GetComponent<XROrigin>();
        if (rig != null) origin = rig.transform;
        if (origin == null)
        {
            // 用不带 FindObjectsSortMode 的重载（带排序模式的那个已废弃）
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t != null && t.name.StartsWith("XR Origin")) { origin = t; break; }
            }
        }

        if (origin != null)
        {
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Camera Offset") camOffset = t;
                if (t.name == "Main Camera") cam = t.GetComponent<Camera>();
            }
        }

        if (cam == null) cam = Camera.main;

        Debug.Log($"[XR相机监视] 已启动。XR Origin={(origin ? origin.name : "未找到")}，" +
                  $"Camera={(cam ? cam.name : "未找到")}");
    }

    private void Update()
    {
        if (!showOnScreen && !logToConsole) return;

        logTimer += Time.deltaTime;
        if (logTimer < logInterval) return;
        logTimer = 0f;

        if (logToConsole) Debug.Log(BuildReport());
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine("═══ XR 相机位置 ═══");

        if (origin != null)
            sb.AppendLine($"XR Origin 世界位置 : {F(origin.position)}");
        else
            sb.AppendLine("XR Origin          : 未找到");

        if (camOffset != null)
            sb.AppendLine($"Camera Offset 本地 : {F(camOffset.localPosition)}");
        else
            sb.AppendLine("Camera Offset      : 未找到");

        if (cam != null)
        {
            var ct = cam.transform;

            sb.AppendLine($"Main Camera 本地   : {F(ct.localPosition)}");
            sb.AppendLine($"Main Camera 世界   : {F(ct.position)}");
            sb.AppendLine($"Main Camera 父物体 : {(ct.parent != null ? ct.parent.name : "无")}");

            // 关键判断：相机的本地位置是不是被追踪系统改写了
            bool localIsZero = ct.localPosition.sqrMagnitude < 0.0001f;
            sb.AppendLine();
            sb.AppendLine($"相机本地偏移是否为 0 : {(localIsZero ? "是" : "否")}");

            if (!localIsZero)
            {
                sb.AppendLine("→ 追踪系统【正在写入】相机本地位置。");
                sb.AppendLine("  说明当前有活动的 XR 输入设备（头显或模拟器），");
                sb.AppendLine("  相机的实际位置 = XR Origin + Camera Offset + 追踪值。");
            }
            else
            {
                sb.AppendLine("→ 追踪系统没有写入相机位置（本地偏移为 0）。");
                sb.AppendLine("  相机的世界位置 = XR Origin + Camera Offset。");
            }

            // 眼睛相对平台的高度
            float groundY = 0f;
            var pad = GameObject.Find("TrainingPad");
            if (pad != null)
            {
                var c = pad.GetComponent<Collider>();
                groundY = c != null ? c.bounds.max.y : pad.transform.position.y;
            }
            sb.AppendLine();
            sb.AppendLine($"眼睛离平台顶面 : {ct.position.y - groundY:F3} 米");
        }

        return sb.ToString();
    }

    private static string F(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    private void OnGUI()
    {
        if (!showOnScreen) return;

        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.normal.textColor = Color.cyan;
        style.alignment = TextAnchor.UpperLeft;

        GUI.Label(new Rect(20, 110, 900, 300), BuildReport(), style);
    }
}
