"""
================================================================
Blender 贴图重新链接脚本
================================================================
用途：给重新绑定骨骼后的模型重新接上贴图。

背景：
  从 Mixamo 下载的模型有时会丢贴图，因为 Mixamo 不保留你的材质。
  但贴图文件本身还在 Unity 工程里，直接链接回来就行。

用法：
  ① Blender 里打开你的模型（导入 FBX 或打开 .blend）
  ② 切到【Scripting】工作区（顶部标签栏）
  ③ 点【New】新建一个脚本
  ④ 把这个文件的内容全部粘贴进去
  ⑤ 改下面 TEX_DIR 的路径（如果你把贴图放在别处）
  ⑥ 点【Run Script】(Alt + P)
  ⑦ 切到【Shading】工作区看效果
================================================================
"""

import bpy
import os

# ============================================================
# 配置区：贴图文件所在的目录
# ============================================================
TEX_DIR = r"S:\unityhub\SnowLineVR\Assets\Models\ExtractedGlbTextures\tripo_convert_b9e52482-e0ee-49"

# 贴图文件名（如果文件名不同，改这里）
COLOR_FILE = "Color.jpg"          # 颜色贴图（最重要）
NORMAL_FILE = "Normal.png"        # 法线贴图
# 金属度/粗糙度是 glTF 合并贴图，需要拆通道，脚本会跳过它

# 是否同时设置 UV（一般不用改）
SETUP_UV = True


def log(msg):
    print(f"[贴图链接] {msg}")


def make_absolute(path):
    """把相对路径转成绝对路径"""
    if os.path.isabs(path):
        return path
    return os.path.abspath(os.path.join(TEX_DIR, path))


def ensure_image(filepath):
    """加载图片，如果已经在 Blender 里就复用"""
    if not os.path.exists(filepath):
        log(f"⚠️ 文件不存在：{filepath}")
        return None

    name = os.path.basename(filepath)

    # 已经加载过就复用
    for img in bpy.data.images:
        if img.filepath and os.path.normpath(bpy.path.abspath(img.filepath)) == os.path.normpath(filepath):
            log(f"复用已加载的图片：{name}")
            return img

    try:
        img = bpy.data.images.load(filepath, check_existing=True)
        log(f"已加载：{name}")
        return img
    except Exception as e:
        log(f"❌ 加载失败 {name}：{e}")
        return None


def get_principled_node(mat):
    """从材质里找到原理化 BSDF 节点"""
    if not mat.use_nodes:
        mat.use_nodes = True

    for node in mat.node_tree.nodes:
        if node.type == 'BSDF_PRINCIPLED':
            return node
    return None


def connect_texture(mat, principled, image, socket_name, is_normal=False):
    """
    把一张贴图接到指定输入口。
    is_normal=True 时额外插入法线贴图节点。
    """
    if image is None:
        return False

    nodes = mat.node_tree.nodes
    links = mat.node_tree.links

    # 检查这个输入口是否已经有连接，有就先断开
    if socket_name in principled.inputs:
        sock = principled.inputs[socket_name]
        for link in list(sock.links):
            links.remove(link)

    if is_normal:
        # 法线需要中间加一个 Normal Map 节点
        nrm_node = None
        for n in nodes:
            if n.type == 'NORMAL_MAP':
                nrm_node = n
                break
        if nrm_node is None:
            nrm_node = nodes.new('ShaderNodeNormalMap')
            nrm_node.location = (principled.location.x - 300, principled.location.y - 400)

        tex_node = nodes.new('ShaderNodeTexImage')
        tex_node.image = image
        tex_node.location = (principled.location.x - 600, principled.location.y - 400)

        links.new(tex_node.outputs['Color'], nrm_node.inputs['Color'])
        links.new(nrm_node.outputs['Normal'], principled.inputs['Normal'])
        log(f"  法线贴图已接入（含 Normal Map 节点）")
        return True
    else:
        tex_node = nodes.new('ShaderNodeTexImage')
        tex_node.image = image
        tex_node.location = (principled.location.x - 400, principled.location.y)

        links.new(tex_node.outputs['Color'], principled.inputs[socket_name])
        log(f"  {socket_name} 已接入")
        return True


def has_uv(obj):
    """检查物体有没有 UV 坐标"""
    if obj.type != 'MESH':
        return False
    return len(obj.data.uv_layers) > 0


def main():
    log("=" * 50)
    log(f"贴图目录：{TEX_DIR}")
    log("=" * 50)

    if not os.path.isdir(TEX_DIR):
        log(f"❌ 目录不存在：{TEX_DIR}")
        log("   请修改脚本开头的 TEX_DIR 变量")
        return

    # ---------- 加载贴图 ----------
    color_path = make_absolute(COLOR_FILE)
    normal_path = make_absolute(NORMAL_FILE)

    color_img = ensure_image(color_path)
    normal_img = ensure_image(normal_path)

    if color_img is None and normal_img is None:
        log("❌ 两张贴图都没加载成功，请检查路径和文件名")
        return

    # ---------- 找出所有网格物体 ----------
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if not meshes:
        log("❌ 场景里没有网格物体")
        return

    log(f"找到 {len(meshes)} 个网格物体")

    # ---------- 收集所有用到的材质 ----------
    materials = set()
    for obj in meshes:
        if SETUP_UV and not has_uv(obj):
            log(f"⚠️ '{obj.name}' 没有 UV 坐标，贴图无法正确显示")

        for slot in obj.material_slots:
            if slot.material:
                materials.add(slot.material)

    if not materials:
        # 没有任何材质 → 新建一个，赋给所有网格
        log("模型没有材质，正在新建…")
        new_mat = bpy.data.materials.new(name="Patient_Material")
        new_mat.use_nodes = True
        materials.add(new_mat)

        for obj in meshes:
            if len(obj.data.materials) == 0:
                obj.data.materials.append(new_mat)
            else:
                for i in range(len(obj.data.materials)):
                    obj.data.materials[i] = new_mat

    log(f"共 {len(materials)} 个材质需要处理")
    log("")

    # ---------- 逐个材质接贴图 ----------
    for mat in materials:
        log(f"处理材质：{mat.name}")

        principled = get_principled_node(mat)
        if principled is None:
            log("  ⚠️ 找不到原理化 BSDF 节点，跳过")
            continue

        # 颜色贴图 → Base Color
        if color_img is not None:
            connect_texture(mat, principled, color_img, 'Base Color')

        # 法线贴图 → Normal
        if normal_img is not None:
            connect_texture(mat, principled, normal_img, 'Normal', is_normal=True)

        # 设置合理的金属度和粗糙度（因为合并贴图没拆通道）
        if 'Metallic' in principled.inputs:
            principled.inputs['Metallic'].default_value = 0.05
        if 'Roughness' in principled.inputs:
            principled.inputs['Roughness'].default_value = 0.5

        log("")

    log("=" * 50)
    log("✅ 完成！")
    log("")
    log("【下一步】")
    log("  1. 切到 Shading 工作区，看模型有没有颜色")
    log("  2. 如果是灰色的，检查：")
    log("     · 模型的 UV 坐标是否正确")
    log("     · 贴图路径是否对")
    log("  3. 确认无误后：【File → External Data → Pack Resources】")
    log("     这会把贴图打包进 .blend 文件")
    log("  4. 再导出 FBX")
    log("=" * 50)


# 运行
main()
