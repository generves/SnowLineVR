"""
================================================================
Blender 骨骼前缀批量重命名脚本
================================================================
用途：把模型的骨骼前缀从 mixamorig: 改成 mixamorig1:，
      让它和 Mixamo 下载的动画文件完全匹配。
      这样 Unity 里就能改用 Generic 模式，动作 100% 还原。

背景：
  模型骨骼：mixamorig:Hips、mixamorig:Spine ...
  动画骨骼：mixamorig1:Hips、mixamorig1:Spine ...
                ↑ 差一个 "1"

  Unity Generic 模式按【骨骼名】匹配，差一个字符就对不上。
  统一之后，动画数据会原样套上去，没有任何重定向偏差。

用法：
  ① Blender 里打开你的模型（带骨骼的）
  ② 顶部切到【Scripting】工作区
  ③ 点【New】，把本文件内容全部粘贴进去
  ④ 点【Run Script】(Alt + P)
  ⑤ 检查输出，确认改了多少根骨骼
  ⑥ 导出 FBX：
       File → Export → FBX
       Path Mode        → Copy
       📦 图标           → 点亮
       Apply Transform   → 勾
       Include → 只勾 Mesh 和 Armature（不勾 Animation）
================================================================
"""

import bpy

# ============================================================
# 配置：旧前缀 → 新前缀
# ============================================================
OLD_PREFIX = "mixamorig:"
NEW_PREFIX = "mixamorig1:"

# 是否同时重命名骨架物体本身（一般不用）
RENAME_ARMATURE = False


def log(msg):
    print(f"[骨骼改名] {msg}")


def rename_bones():
    log("=" * 55)
    log(f"旧前缀：{OLD_PREFIX}")
    log(f"新前缀：{NEW_PREFIX}")
    log("=" * 55)

    # ---------- 找出所有骨架 ----------
    armatures = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if not armatures:
        log("❌ 场景里没有骨架（Armature）")
        log("   请确认导入的是带骨骼的模型")
        return

    log(f"找到 {len(armatures)} 个骨架")

    total_renamed = 0
    already_ok = 0

    for arm in armatures:
        log("")
        log(f"处理骨架：{arm.name}")
        log(f"  骨骼总数：{len(arm.data.bones)}")

        renamed = 0

        for bone in arm.data.bones:
            old_name = bone.name

            # 已经是新前缀 → 跳过
            if old_name.startswith(NEW_PREFIX):
                already_ok += 1
                continue

            # 匹配旧前缀 → 替换
            if old_name.startswith(OLD_PREFIX):
                # 去掉旧前缀，取后半部分，再加新前缀
                core = old_name[len(OLD_PREFIX):]
                new_name = NEW_PREFIX + core

                try:
                    bone.name = new_name
                    renamed += 1
                    total_renamed += 1
                except Exception as e:
                    log(f"  ❌ 改名失败 {old_name}：{e}")

        log(f"  已改名：{renamed} 根")

        # 顺便看看改完后的前几个
        log("  改名后的前 8 根骨骼：")
        for i, bone in enumerate(arm.data.bones):
            if i >= 8:
                break
            log(f"    {bone.name}")

        # 可选：重命名骨架物体
        if RENAME_ARMATURE:
            if arm.name.startswith(OLD_PREFIX.rstrip(':')):
                arm.name = arm.name.replace(OLD_PREFIX.rstrip(':'), NEW_PREFIX.rstrip(':'))

    # ---------- 汇总 ----------
    log("")
    log("=" * 55)
    log(f"✅ 完成：共改名 {total_renamed} 根骨骼")
    if already_ok > 0:
        log(f"   （另有 {already_ok} 根已经是新前缀，跳过）")
    log("=" * 55)
    log("")
    log("【下一步：导出 FBX】")
    log("  File → Export → FBX")
    log("    Path Mode        → Copy")
    log("    📦 图标           → 点亮（打包贴图）")
    log("    Apply Transform   → 勾")
    log("    Include → 只勾 Mesh 和 Armature（不勾 Animation）")
    log("")
    log("【导出后在 Unity 里】")
    log("  1. 放回 Assets/Models/")
    log("  2. 选中模型 → Rig → Animation Type = Generic → Apply")
    log("  3. 选中所有动画 fbx → Rig → Animation Type = Generic → Apply")
    log("  4. 工具 → 动画 → 重建 Controller 并挂到模型")


def check_current_names():
    """先看一眼当前骨骼命名，方便判断"""
    log("")
    log("【当前骨骼命名预览】")
    for arm in bpy.data.objects:
        if arm.type != 'ARMATURE':
            continue
        log(f"  骨架 {arm.name}：")
        for i, bone in enumerate(arm.data.bones):
            if i >= 5:
                log(f"    ...（共 {len(arm.data.bones)} 根）")
                break
            log(f"    {bone.name}")
        break


# ============================================================
# 执行
# ============================================================
check_current_names()
rename_bones()
