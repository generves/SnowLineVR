using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;

/// <summary>
/// 【工具】一键切换所有身体部位胶囊外壳的显示。
///
/// 两种用法：
///   · 编辑器里：工具 → 显示所有碰撞胶囊 / 隐藏所有碰撞胶囊
///     这个会修改组件的 debugAlwaysVisible 字段（会保存）
///   · 运行时：Play 模式下按 【\】 或 【F1】 键切换
///     这个不改文件，纯临时显示
///
/// 用法：工具 → 碰撞胶囊显示 → ...
/// </summary>
public static class ToggleCapsuleVisualTool
{
    [MenuItem("工具/碰撞胶囊显示/显示所有胶囊")]
    public static void ShowAll()
    {
        int n = SetAll(true);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[胶囊显示] 已开启 {n} 个身体部位的调试显示");
        EditorUtility.DisplayDialog("已显示所有胶囊",
            $"开启了 {n} 个部位的调试显示。\n\n" +
            "【注意】\n" +
            "· 编辑器里设置的是组件上的 Debug Always Visible 字段\n" +
            "· 要在【Play 模式】才能看到半透明胶囊\n" +
            "· 非 Play 模式下，选中骨骼后 Unity 会画绿色线框\n\n" +
            "场景已保存。", "好");
    }

    [MenuItem("工具/碰撞胶囊显示/隐藏所有胶囊")]
    public static void HideAll()
    {
        int n = SetAll(false);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[胶囊显示] 已关闭 {n} 个身体部位的调试显示");
        EditorUtility.DisplayDialog("已隐藏所有胶囊",
            $"关闭了 {n} 个部位的调试显示。\n\n" +
            "恢复成正常状态：只在鼠标悬停时显示橙色高亮。\n\n" +
            "场景已保存。", "好");
    }

    /// <summary>把所有身体部位的调试显示开关设为指定值</summary>
    private static int SetAll(bool on)
    {
        int count = 0;

        foreach (var pbp in Resources.FindObjectsOfTypeAll<PatientBodyPart>())
        {
            if (pbp == null) continue;
            if (!pbp.gameObject.scene.IsValid()) continue;   // 只要场景里的

            Undo.RecordObject(pbp, "Toggle Capsule Visual");
            pbp.debugAlwaysVisible = on;
            EditorUtility.SetDirty(pbp);
            count++;
        }

        // 同时把运行时静态开关也设上（Play 模式下立即生效）
        PatientBodyPart.DebugShowAll = on;

        return count;
    }

    // ==================== 信息：列出当前状态 ====================
    [MenuItem("工具/碰撞胶囊显示/查看当前状态")]
    public static void ShowStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================================");
        sb.AppendLine("  碰撞胶囊状态");
        sb.AppendLine("========================================================");
        sb.AppendLine();

        int total = 0, withVisual = 0, debugOn = 0;

        foreach (var pbp in Resources.FindObjectsOfTypeAll<PatientBodyPart>())
        {
            if (pbp == null) continue;
            if (!pbp.gameObject.scene.IsValid()) continue;

            total++;
            if (pbp.createHighlightVisual) withVisual++;
            if (pbp.debugAlwaysVisible) debugOn++;

            var cap = pbp.GetComponent<CapsuleCollider>();
            string sizeStr = cap != null
                ? $"R={cap.radius} H={cap.height} CenterY={cap.center.y}"
                : "❌ 没有 CapsuleCollider";

            sb.AppendLine($"  {pbp.part,-8}  {pbp.gameObject.name}");
            sb.AppendLine($"      胶囊：{sizeStr}");
            sb.AppendLine($"      高亮外壳：{(pbp.createHighlightVisual ? "有" : "无")}   " +
                          $"调试常显：{(pbp.debugAlwaysVisible ? "开" : "关")}");
            sb.AppendLine();
        }

        sb.AppendLine("--------------------------------------------------------");
        sb.AppendLine($"  身体部位组件总数：{total}");
        sb.AppendLine($"  有高亮外壳的：{withVisual}");
        sb.AppendLine($"  调试常显开启的：{debugOn}");
        sb.AppendLine();
        sb.AppendLine("【运行时快捷键】\\  或  F1  →  切换所有胶囊显示");

        Debug.Log("[碰撞胶囊状态]\n" + sb);
        EditorUtility.DisplayDialog("碰撞胶囊状态", sb.ToString(), "好");
    }
}
