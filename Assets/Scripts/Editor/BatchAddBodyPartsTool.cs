using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 【工具】批量给九根骨骼添加身体部位组件。
///
/// 每根骨骼会自动加上：
///   · CapsuleCollider（带预设的默认尺寸）
///   · XRSimpleInteractable（并接好三个事件）
///   · PatientBodyPart（Part 和提示文字已填好）
///
/// 加完后你只需要在 Scene 视图里调整胶囊的位置和大小。
///
/// 已经配好的左大腿（mixamorig:LeftUpLeg）会被跳过。
///
/// 用法：工具 → 批量添加身体部位（剩余九个）
/// </summary>
public static class BatchAddBodyPartsTool
{
    /// <summary>要添加的部位配置</summary>
    private struct PartDef
    {
        public string bone;          // 骨骼名
        public PatientBodyPart.BodyPart part;
        public float radius;         // 胶囊半径（骨骼本地单位）
        public float height;         // 胶囊总高（骨骼本地单位）
        public float centerY;        // 胶囊中心相对骨骼的 Y 偏移
        public string message;       // 悬停提示文字

        public PartDef(string b, PatientBodyPart.BodyPart p,
            float r, float h, float cy, string msg)
        {
            bone = b; part = p; radius = r; height = h; centerY = cy; message = msg;
        }
    }

    // 骨骼本地单位换算参考：
    //   骨骼 lossyScale ≈ 0.017，所以 1 本地单位 ≈ 0.017 米
    //   例：半径 6 → 世界半径 ≈ 0.10 米，高度 50 → 世界高度 ≈ 0.85 米
    private static readonly PartDef[] BodyParts =
    {
        new PartDef("mixamorig:Head", PatientBodyPart.BodyPart.头部,
            7f, 16f, 0f, "观察伤者：意识清醒，能对答，瞳孔正常"),

        new PartDef("mixamorig:Spine1", PatientBodyPart.BodyPart.躯干,
            13f, 30f, 0f, "试图搬动伤者会造成二次损伤。正确做法：制止其起身，保持原位并固定患处"),

        new PartDef("mixamorig:LeftArm", PatientBodyPart.BodyPart.左上臂,
            5f, 16f, -8f, "这个部位需要夹板或绷带才能处理"),

        new PartDef("mixamorig:LeftForeArm", PatientBodyPart.BodyPart.左前臂,
            4.5f, 16f, -8f, "这个部位需要夹板或绷带才能处理"),

        new PartDef("mixamorig:RightArm", PatientBodyPart.BodyPart.右上臂,
            5f, 16f, -8f, "这个部位需要夹板或绷带才能处理"),

        new PartDef("mixamorig:RightForeArm", PatientBodyPart.BodyPart.右前臂,
            4.5f, 16f, -8f, "这个部位需要夹板或绷带才能处理"),

        new PartDef("mixamorig:LeftLeg", PatientBodyPart.BodyPart.左小腿,
            5.5f, 24f, -12f, "这个部位需要夹板或冰袋才能处理"),

        new PartDef("mixamorig:RightUpLeg", PatientBodyPart.BodyPart.右大腿,
            6.5f, 30f, -15f, "这个部位需要夹板或冰袋才能处理"),

        new PartDef("mixamorig:RightLeg", PatientBodyPart.BodyPart.右小腿,
            5.5f, 24f, -12f, "这个部位需要夹板或冰袋才能处理"),
    };

    [MenuItem("工具/批量添加身体部位（剩余九个）")]
    public static void BatchAdd()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================================");
        sb.AppendLine("  批量添加身体部位");
        sb.AppendLine($"  {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("========================================================");
        sb.AppendLine();

        // ---------- 1. 找 character 的骨骼根 ----------
        Transform boneRoot = null;

        // 优先找名字是 mixamorig:Hips 的
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name == "mixamorig:Hips") { boneRoot = t; break; }
        }

        if (boneRoot == null)
        {
            EditorUtility.DisplayDialog("找不到骨骼根",
                "场景里没有名为 mixamorig:Hips 的物体。", "知道了");
            return;
        }

        sb.AppendLine($"骨骼根：{boneRoot.name}  世界位置 {F(boneRoot.position)}");

        // ---------- 2. 建立 骨骼名 → Transform 映射 ----------
        var boneMap = new Dictionary<string, Transform>();
        foreach (var t in boneRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            if (!boneMap.ContainsKey(t.name)) boneMap[t.name] = t;
        }

        sb.AppendLine($"骨骼根下一共有 {boneMap.Count} 个不同名字的物体");
        sb.AppendLine();

        // ---------- 3. 逐个处理 ----------
        sb.AppendLine("--------------------------------------------------------");
        sb.AppendLine("【逐个骨骼处理】");
        sb.AppendLine("--------------------------------------------------------");

        int added = 0, skipped = 0, failed = 0;

        foreach (var def in BodyParts)
        {
            sb.AppendLine();
            sb.AppendLine($"── {def.part}  ←  {def.bone} ──");

            if (!boneMap.ContainsKey(def.bone))
            {
                sb.AppendLine($"  ❌ 找不到骨骼 {def.bone}");
                failed++;
                continue;
            }

            Transform bone = boneMap[def.bone];

            // 已经有 PatientBodyPart 就跳过
            var existing = bone.GetComponent<PatientBodyPart>();
            if (existing != null)
            {
                sb.AppendLine($"  ⏭️ 已经有 PatientBodyPart（Part = {existing.part}），跳过");
                skipped++;
                continue;
            }

            // ---------- 3a. 加 CapsuleCollider ----------
            var capsule = bone.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = Undo.AddComponent<CapsuleCollider>(bone.gameObject);
                sb.AppendLine("  ✅ 加了 CapsuleCollider");
            }
            else sb.AppendLine("  · 已有 CapsuleCollider");

            capsule.center = new Vector3(0f, def.centerY, 0f);
            capsule.radius = def.radius;
            capsule.height = def.height;
            capsule.direction = 1;      // 1 = Y 轴
            capsule.isTrigger = false;
            EditorUtility.SetDirty(capsule);

            sb.AppendLine($"       Center=(0, {def.centerY}, 0)  Radius={def.radius}  Height={def.height}  Direction=Y");

            // ---------- 3b. 加 XRSimpleInteractable ----------
            var interactable = bone.GetComponent<XRSimpleInteractable>();
            if (interactable == null)
            {
                interactable = Undo.AddComponent<XRSimpleInteractable>(bone.gameObject);
                sb.AppendLine("  ✅ 加了 XRSimpleInteractable");
            }
            else sb.AppendLine("  · 已有 XRSimpleInteractable");

            // ---------- 3c. 加 PatientBodyPart ----------
            var pbp = Undo.AddComponent<PatientBodyPart>(bone.gameObject);
            pbp.part = def.part;
            pbp.boneName = def.bone;
            pbp.hoverMessage = def.message;
            pbp.createHighlightVisual = true;
            EditorUtility.SetDirty(pbp);

            sb.AppendLine($"  ✅ 加了 PatientBodyPart（Part = {def.part}）");

            // ---------- 3d. 接三个事件 ----------
            interactable.selectEntered.RemoveAllListeners();
            interactable.hoverEntered.RemoveAllListeners();
            interactable.hoverExited.RemoveAllListeners();

            UnityEventTools.AddPersistentListener(interactable.selectEntered, pbp.OnSelectEntered);
            UnityEventTools.AddPersistentListener(interactable.hoverEntered, pbp.OnHoverEntered);
            UnityEventTools.AddPersistentListener(interactable.hoverExited, pbp.OnHoverExited);

            sb.AppendLine("  ✅ 已接好 Select Entered / Hover Entered / Hover Exited");

            added++;
        }

        // ---------- 4. 汇总 ----------
        sb.AppendLine();
        sb.AppendLine("--------------------------------------------------------");
        sb.AppendLine($"新增：{added} 个");
        sb.AppendLine($"跳过（已存在）：{skipped} 个");
        sb.AppendLine($"失败（找不到骨骼）：{failed} 个");

        // ---------- 5. 保存场景 ----------
        var scene = boneRoot.gameObject.scene;
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine();
            sb.AppendLine($"场景已保存：{scene.name}");
        }

        Debug.Log("[批量添加身体部位]\n" + sb);
        EditorUtility.DisplayDialog("批量添加完成",
            sb + "\n\n【接下来】\n" +
            "在 Scene 视图里逐个选中九根骨骼，\n" +
            "调整胶囊的位置和大小：\n" +
            "  · 选中骨骼后能看到胶囊的绿色线框\n" +
            "  · 或点 CapsuleCollider 的 Edit Collider 按钮拖手柄", "好");
    }

    private static string F(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";
}
