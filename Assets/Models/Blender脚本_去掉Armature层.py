"""
================================================================
Blender 脚本：把"骨架物体"降级为空物体
================================================================
用途：让骨骼直接挂在模型根物体下，去掉 Armature 这一层容器。

背景：
  Generic 动画按【完整路径】匹配骨骼，比如：
     mixamorig1:Hips/mixamorig1:Spine/...

  但 Blender 导出 FBX 时，骨架会多出一层名为 "Armature" 的容器：
     Armature/mixamorig1:Hips/mixamorig1:Spine/...
        ↑ 多了一层

  结果 Unity 里所有动画曲线都匹配不上 → 角色保持 T-pose 不动。

  本脚本把 Armature 从"骨架物体"改成"普通空物体"，
  这样它就不会被导出成骨骼容器，骨骼会直接挂在模型根下。

用法：
  ① Blender 里打开你的模型
  ② 顶部切到【Scripting】工作区
  ③ 点【New】，把本文件内容全部粘贴进去
  ④ 点【Run Script】(Alt + P)
  ⑤ 检查输出
  ⑥ 导出 FBX：
       File → Export → FBX
       Path Custom Properties 不勾
       Path Mode        → Copy
       📦 图标           → 点亮
       Apply Transform   → 勾
       Include → 只勾 Mesh 和 Armature（不勾 Animation）
  ⑦ 放回 Unity 的 Assets/Models/
================================================================
"""

import bpy


def log(msg):
    print(f"[去Armature层] {msg}")


def main():
    log("=" * 58)
    log("把骨架物体降级为空物体（去掉 Armature 容器层）")
    log("=" * 58)

    armatures = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if not armatures:
        log("❌ 场景里没有骨架物体")
        return

    log(f"找到 {len(armatures)} 个骨架物体")

    for arm in armatures:
        log("")
        log(f"处理：{arm.name}")
        log(f"  当前类型：{arm.type}")
        log(f"  骨骼数：{len(arm.data.bones)}")
        log(f"  父物体：{arm.parent.name if arm.parent else '（无，是顶层）'}")
        log(f"  子物体数：{len(arm.children)}")

        # 检查它有几个子物体
        if len(arm.children) == 0:
            log("  ⚠️ 没有子物体，跳过")
            continue

        # 记录子物体的世界变换
        children_info = []
        for child in arm.children:
            children_info.append({
                'obj': child,
                'matrix_world': child.matrix_world.copy(),
                'parent': child.parent,
            })

        # 记录骨架自己的世界变换
        arm_matrix = arm.matrix_world.copy()

        # ---------- 关键操作：把骨架类型改成 EMPTY ----------
        try:
            arm.type = 'EMPTY'
            log("  ✅ 已把骨架物体改成空物体（骨骼数据仍保留在下面）")
        except Exception as e:
            log(f"  ❌ 改类型失败：{e}")
            continue

        # 恢复骨架的世界变换（改类型后可能被重置）
        arm.matrix_world = arm_matrix

        # ---------- 检查子物体是否还挂着 ----------
        still_children = list(arm.children)
        log(f"  改完后子物体数：{len(still_children)}")

        if len(still_children) == 0:
            log("  ⚠️ 子物体脱离了！正在重新挂回…")
            for info in children_info:
                try:
                    info['obj'].parent = arm
                    info['obj'].matrix_world = info['matrix_world']
                except Exception as e:
                    log(f"    ❌ 挂回失败 {info['obj'].name}：{e}")
            log(f"  重新挂回的：{len(arm.children)} 个")

        # 列出子物体名，方便确认
        log("  子物体列表：")
        for child in arm.children:
            log(f"    · {child.name}  （类型 {child.type}）")

    # ---------- 汇总 ----------
    log("")
    log("=" * 58)
    log("✅ 处理完成")
    log("")
    log("【接下来】")
    log("  1. 检查 Outliner：")
    log("     Armature 现在应该是普通物体（灰色图标），不是骨架（蓝色图标）")
    log("     它下面应该挂着 mixamorig1:Hips 等骨骼")
    log("")
    log("  2. 导出 FBX：")
    log("     File → Export → FBX")
    log("       Path Mode        → Copy")
    log("       📦 图标           → 点亮")
    log("       Apply Transform   → 勾")
    log("       Include → 只勾 Mesh 和 Armature（不勾 Animation）")
    log("")
    log("  3. 放回 Unity：Assets/Models/")
    log("")
    log("  4. Unity 里跑：工具 → 诊断 Generic 动画绑定")
    log("     应该看到「✅ 能匹配角色骨骼的路径：52」")
    log("=" * 58)


# ============================================================
# 先看一眼当前结构
# ============================================================
log("【当前场景结构预览】")
for o in bpy.data.objects:
    if o.type in ('ARMATURE', 'MESH', 'EMPTY'):
        indent = ""
        p = o.parent
        depth = 0
        while p is not None and depth < 4:
            indent += "  "
            p = p.parent
            depth += 1
        log(f"  {indent}[{o.type}] {o.name}")

log("")

main()
