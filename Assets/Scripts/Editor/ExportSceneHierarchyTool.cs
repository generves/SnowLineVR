using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// 【工具】把场景里所有物体导出成文本清单，方便排查隐藏物体。
///
/// 导出内容：
///   · 完整层级树（带缩进）
///   · 每个物体的组件列表
///   · 特别标注：骨骼、蒙皮网格、Animator、Collider
///
/// 输出文件：工程根目录下的 「场景物体清单.txt」
///
/// 用法：工具 → 导出场景物体清单
/// </summary>
public static class ExportSceneHierarchyTool
{
    [MenuItem("工具/导出场景物体清单")]
    public static void Export()
    {
        var sb = new StringBuilder();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        sb.AppendLine("========================================================");
        sb.AppendLine("  场景物体清单");
        sb.AppendLine($"  场景：{scene.name}");
        sb.AppendLine($"  导出时间：{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("========================================================");
        sb.AppendLine();

        var roots = scene.GetRootGameObjects();
        sb.AppendLine($"根物体共 {roots.Length} 个");
        sb.AppendLine();

        // ---------- 逐个根物体遍历 ----------
        int boneCount = 0;
        int smrCount = 0;
        int animatorCount = 0;
        int colliderCount = 0;
        var mixamorig0 = new List<string>();
        var mixamorig1 = new List<string>();

        foreach (var root in roots)
        {
            Walk(root.transform, sb, 0, ref boneCount, ref smrCount,
                 ref animatorCount, ref colliderCount, mixamorig0, mixamorig1);
            sb.AppendLine();
        }

        // ---------- 汇总 ----------
        sb.AppendLine("========================================================");
        sb.AppendLine("  汇总统计");
        sb.AppendLine("========================================================");
        sb.AppendLine($"  骨骼物体（Transform 名含 mixamorig）：{boneCount}");
        sb.AppendLine($"  SkinnedMeshRenderer：{smrCount}");
        sb.AppendLine($"  Animator：{animatorCount}");
        sb.AppendLine($"  Collider（Box/Capsule/Sphere/Mesh）：{colliderCount}");
        sb.AppendLine();

        sb.AppendLine("--------------------------------------------------------");
        sb.AppendLine($"  mixamorig:  前缀的骨骼：{mixamorig0.Count} 根");
        sb.AppendLine("--------------------------------------------------------");
        foreach (var n in mixamorig0) sb.AppendLine($"    {n}");
        sb.AppendLine();

        sb.AppendLine("--------------------------------------------------------");
        sb.AppendLine($"  mixamorig1: 前缀的骨骼：{mixamorig1.Count} 根");
        sb.AppendLine("--------------------------------------------------------");
        foreach (var n in mixamorig1) sb.AppendLine($"    {n}");
        sb.AppendLine();

        // ---------- 写文件 ----------
        string outPath = Path.Combine(
            Path.GetDirectoryName(Application.dataPath), "场景物体清单.txt");

        try
        {
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[导出场景清单] 已写入：{outPath}");
            Debug.Log($"[导出场景清单] 根物体 {roots.Length} 个，骨骼 {boneCount} 根，" +
                      $"SkinnedMeshRenderer {smrCount} 个，Animator {animatorCount} 个");

            try { System.Diagnostics.Process.Start("notepad.exe", outPath); } catch { }

            EditorUtility.DisplayDialog("导出完成",
                $"文件已写入：\n{outPath}\n\n" +
                $"（已自动用记事本打开）\n\n" +
                $"根物体：{roots.Length} 个\n" +
                $"骨骼：{boneCount} 根\n" +
                $"  其中 mixamorig:  {mixamorig0.Count} 根\n" +
                $"  其中 mixamorig1: {mixamorig1.Count} 根\n" +
                $"SkinnedMeshRenderer：{smrCount} 个\n" +
                $"Animator：{animatorCount} 个\n\n" +
                "请把这个 txt 文件发给我。", "好");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[导出场景清单] 写文件失败：{e.Message}");
            EditorUtility.DisplayDialog("写文件失败", e.Message, "知道了");
        }
    }

    // ==================== 递归遍历 ====================
    private static void Walk(Transform t, StringBuilder sb, int depth,
        ref int boneCount, ref int smrCount, ref int animatorCount, ref int colliderCount,
        List<string> mixamorig0, List<string> mixamorig1)
    {
        if (t == null) return;

        // 收集骨骼名
        if (t.name.StartsWith("mixamorig:")) { boneCount++; mixamorig0.Add(t.name); }
        else if (t.name.StartsWith("mixamorig1:")) { boneCount++; mixamorig1.Add(t.name); }

        // 收集组件信息
        var parts = new List<string>();

        if (t.GetComponent<SkinnedMeshRenderer>() != null) { parts.Add("SkinnedMeshRenderer"); smrCount++; }
        if (t.GetComponent<MeshRenderer>() != null && t.GetComponent<SkinnedMeshRenderer>() == null) parts.Add("MeshRenderer");
        if (t.GetComponent<MeshFilter>() != null) parts.Add("MeshFilter");
        if (t.GetComponent<Animator>() != null) { parts.Add("Animator"); animatorCount++; }

        if (t.GetComponent<CapsuleCollider>() != null) { parts.Add("CapsuleCollider"); colliderCount++; }
        else if (t.GetComponent<BoxCollider>() != null) { parts.Add("BoxCollider"); colliderCount++; }
        else if (t.GetComponent<SphereCollider>() != null) { parts.Add("SphereCollider"); colliderCount++; }
        else if (t.GetComponent<MeshCollider>() != null) { parts.Add("MeshCollider"); colliderCount++; }

        if (t.GetComponent<Canvas>() != null) parts.Add("Canvas");
        if (t.GetComponent<Camera>() != null) parts.Add("Camera");
        if (t.GetComponent<Light>() != null) parts.Add("Light");
        if (t.GetComponent<AudioSource>() != null) parts.Add("AudioSource");

        // MonoBehaviour 脚本名
        foreach (var mb in t.GetComponents<MonoBehaviour>())
        {
            if (mb == null) { parts.Add("⚠️MissingScript"); continue; }
            parts.Add(mb.GetType().Name);
        }

        // 缩进 + 名字
        string indent = new string(' ', depth * 2);
        string nameTag = t.name;

        // 特殊标注
        string mark = "";
        if (t.name.StartsWith("mixamorig")) mark = "  ★骨骼";
        else if (t.GetComponent<SkinnedMeshRenderer>() != null) mark = "  ◆蒙皮网格";
        else if (t.GetComponent<Animator>() != null) mark = "  ◆动画";
        else if (t.GetComponent<Camera>() != null) mark = "  ◆相机";
        else if (t.GetComponent<Light>() != null) mark = "  ◆灯光";

        string compStr = parts.Count > 0 ? $"  [{string.Join(", ", parts)}]" : "";
        string activeStr = t.gameObject.activeSelf ? "" : "  (未激活)";

        // 位置（只对顶层几层显示，避免刷屏）
        string posStr = depth <= 2 ? $"  pos={t.position}" : "";

        sb.AppendLine($"{indent}{nameTag}{mark}{compStr}{activeStr}{posStr}");

        // 递归子物体
        foreach (Transform child in t) Walk(child, sb, depth + 1,
            ref boneCount, ref smrCount, ref animatorCount, ref colliderCount,
            mixamorig0, mixamorig1);
    }
}
