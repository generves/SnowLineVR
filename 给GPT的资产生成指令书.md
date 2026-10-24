# 给 GPT 的资产生成指令书
## 项目：VR 冰雪运动急救训练系统 —— 3D 道具四视图生成

---

# 第一部分：项目背景（供你理解用途，不必复述给我）

我正在开发一个 **VR 虚拟现实训练系统**，用于教学"雪场急救"技能。学员戴上 VR 头显后，会面对一个虚拟的雪场事故场景，需要用双手抓取真实的急救道具，完成止血、固定、保温等操作。

**因此我需要一批 3D 道具模型。** 工作流程是：

```
你生成四视图 → 我交给 Tripo（图生 3D 工具）→ 生成 3D 模型 → 导入 Unity 引擎
```

**你的任务**：为我下面列出的每一个道具，生成 **四视图参考图**。

---

# 第二部分：核心要求（每次生成都必须遵守）

## 2.1 要生成几张图、什么视角

**每个道具需要 4 张独立图片**，分别是：

| 序号 | 视角 | 英文 | 作用 |
|---|---|---|---|
| 1 | **正视图** | front view | 提供正面细节 |
| 2 | **左视图** | left side view | 提供左侧轮廓 |
| 3 | **右视图** | right side view | 提供右侧轮廓 |
| 4 | **后视图** | back view | 补全背面信息 |

**⚠️ 关键：四张图必须分别生成，不要拼在一张图里。** 拼图会让下游的 3D 重建工具把它们误判成四个不同物体。

## 2.2 技术规范（硬性要求，缺一不可）

| 要求 | 说明 |
|---|---|
| **正交投影** | Orthographic projection。**绝对不能有透视变形**（近大远小会让 3D 重建出错） |
| **纯白背景** | Pure white background (#FFFFFF)，干净、无渐变、无阴影投射到背景上 |
| **均匀漫射光** | Even diffuse lighting。物体上可以有立体感，但**不要有强烈的高光点或投影** |
| **物体居中** | 物体占据画面约 70% 面积，居中放置，四周留白 |
| **四张图同比例** | 同一道具的四张图，**物体大小必须完全一致**，不能一张大一张小 |
| **单件孤立** | 画面里**只能有这一个物体**，不能有其他道具、背景、装饰 |
| **无文字水印** | 不要任何标签、箭头、文字说明、logo |

## 2.3 尺寸信息怎么给（重要说明）

**尺寸数值只写在文字描述里，不要画进图里。**

```
✅ 正确：在文字描述里写明 "45 cm long × 11 cm wide × 1 cm thick"
❌ 错误：在画面里画一把标尺、或者标注尺寸线
```

**为什么**：

| 原因 | 说明 |
|---|---|
| **画面越干净，3D 重建越准** | 图生 3D 工具会把画面里所有可见内容都尝试重建。标尺可能被**融合进模型**，变成道具上多出来的一根棍子，之后很难清理 |
| **标尺提供不了真实尺度** | 图生 3D 的输出是**归一化网格**（统一大小），不会有绝对尺寸概念。标尺不会让它"知道"这个道具是 45 厘米 |
| **尺寸在别处校正更精确** | 我会在 Blender / Unity 里按数值精确缩放，比图片参照准确得多 |

**所以你的任务只有一件：让道具的「形状与比例」准确。** 绝对尺寸由我在引擎里设定。

## 2.4 画面里只能有这一个物体

```
The image must contain ONLY the target object.
Absolutely nothing else: no ruler, no scale bar, no measuring reference,
no hands, no supporting surface, no props, no decorative elements.
Pure white background only.
```

## 2.5 ⚠️ 六个绝对不能做的事

| 禁止 | 原因 |
|---|---|
| ❌ **不要画标尺、尺寸线、任何测量参照物** | 会被融合进模型，变成多余的几何体，清理麻烦 |
| ❌ **不要画手、不要画"被人拿着"的状态** | 手会被一起建模进去，变成废模型。**只要物体本身** |
| ❌ **不要在物体上生成任何文字、字母、数字、Logo** | AI 生成的文字是乱码，中文更是鬼画符。**需要文字的地方一律留白**（我在引擎里用文本工具加） |
| ❌ **不要生成透明或半透明材质** | 下游工具处理不了透明度。透明物体（如冰袋）**用不透明的蓝色建模** |
| ❌ **不要生成多个物体组合** | 比如"冰袋+绷带放一起"——会被切碎。**一件道具一张图** |
| ❌ **不要写实到有复杂内部结构** | 只要外观轮廓和表面材质，不需要内部构造 |

## 2.6 视觉风格统一（保证整套道具协调）

所有道具都要遵循同一套风格，这样放进同一个场景里不违和：

```
Clean, realistic product-render style. Semi-realistic, not cartoonish,
not photorealistic-hyperdetailed. Matte surfaces with subtle wear and
realistic material texture. Muted, natural color palette suitable for a
winter sports / outdoor emergency context. No stylization, no cel-shading.
```

---

# 第三部分：通用 Prompt 模板

**每次生成时，把下面的模板 + 第三部分之后的具体道具描述组合起来发给我。**

```
Generate 4 separate images (NOT a single combined sheet) of the SAME object:

  1. Front view (orthographic)
  2. Left side view (orthographic)
  3. Right side view (orthographic)
  4. Back view (orthographic)

STYLE:
Clean realistic product-render style, semi-realistic (not cartoonish, not
hyper-detailed). Matte surfaces with subtle realistic material texture.
Even diffuse studio lighting. Pure white background (#FFFFFF), no shadows
cast on the background, no gradients.

TECHNICAL REQUIREMENTS (critical):
- Orthographic projection only — absolutely no perspective distortion
- The object must be centered and occupy about 70% of the frame
- All 4 images must show the object at EXACTLY the same scale
- The image must contain ONLY the target object — nothing else in the frame.
  No ruler, no scale bar, no measuring reference, no hands, no props,
  no supporting surface, no decorative elements. Pure white background only.
- No text, no letters, no numbers, no logos, no watermarks, no arrows
- No hands, no person holding the object
- Do NOT draw any size annotations or measurement markings on or near
  the object. Real-world dimensions are communicated in the text
  description below, NOT in the image.

OBJECT TO GENERATE:
[在此粘贴具体道具描述]
```

---

# 第四部分：道具清单（按优先级排序）

> **建议按顺序生成**。第一批是当前最急需的。
> 每一条都可以直接连同上方的模板一起发给我。

---

## 🥇 第一批：膝关节扭伤训练场景（最急需）

---

### 道具 1｜急救夹板（可塑形夹板）

```
A reusable emergency splint (moldable splint), adult forearm/leg size.
Rectangular flat strip, 45 cm long × 11 cm wide × 1 cm thick.
Material: matte off-white foam/plastic composite.
Surface: subtle longitudinal anti-slip grooves running along the length;
slightly curved cross-section (it is moldable, so it has a gentle
curve to wrap around a limb).
Edges: fully rounded, NOT sharp.
Two short velcro straps attached at roughly 1/3 and 2/3 of its length,
lying flat against the surface.
Color: off-white / light beige.
```

**尺寸标注**：长 45cm × 宽 11cm × 厚 1cm

---

### 道具 2｜冰袋（化学冰袋 / 凝胶冰袋）

```
A reusable gel ice pack for sports injury.
Approximately 20 cm long × 12 cm wide × 4 cm thick.
Material: opaque blue gel inside a soft white non-woven fabric sleeve.
Shape: a soft, slightly bulging rectangular pouch — NOT a rigid box.
It should look pliable, with gentle irregular bulges where the gel sits.
Feature: a distinct horizontal pinch/crease line across the middle
(this is where the pack is divided into two gel compartments).
Corners: rounded and soft.
Color: white fabric exterior with the blue gel faintly visible as
a soft blue tint through the fabric.
```

**尺寸标注**：长 20cm × 宽 12cm × 厚 4cm
**⚠️ 注意**：用**不透明**材质，不要真做成透明的

---

### 道具 3｜急救保温毯（折叠状态）

```
An emergency foil blanket (mylar space blanket), in its FOLDED state.
Folded size: 20 cm long × 12 cm wide × 3 cm thick — roughly the size
of a thick envelope.
Material: bright silver aluminized polyester film — highly reflective,
metallic.
Surface: covered in crisp creases and fold lines from being folded many
times; the reflective surface should show broken, fragmented highlights
(not a smooth mirror).
Edges: slightly curled and uneven where the folds meet.
Color: bright silver / chrome.
```

**尺寸标注**：长 20cm × 宽 12cm × 厚 3cm
**备注**：这是视觉冲击力最强的道具，银色反光在雪地里很醒目

---

### 道具 4｜折叠式警示三角牌

```
A folding safety warning triangle (roadside/hazard type), unfolded.
Size: 60 cm tall × 40 cm wide at the base, 2 cm thick.
Material: bright orange-red engineering plastic.
Structure: an upright triangular frame on a folding base with two
support legs angled outward for stability.
Feature: reflective horizontal stripes on the triangular face
(simple silver/white stripes — DO NOT draw any text, symbols or letters,
leave the face otherwise blank).
Base: black rubber feet at the bottom of the support legs.
Color: bright orange-red body, black base.
```

**尺寸标注**：高 60cm × 底宽 40cm × 厚 2cm
**⚠️ 注意**：三角面上的**文字/图案必须留白**，只要反光条纹

---

### 道具 5｜手机（通用智能手机）

```
A modern generic smartphone. No brand logos, no visible brand identity.
Size: 15.5 cm tall × 7.5 cm wide × 0.8 cm thick.
Material: black glass front panel with a dark grey metal frame.
Screen: completely BLANK and off (pure black, no UI, no icons, no text).
Back: a raised rectangular camera module in the top-left corner with
2-3 lens circles; matte dark grey or black back panel.
Edges: rounded corners, very thin profile.
```

**尺寸标注**：长 15.5cm × 宽 7.5cm × 厚 0.8cm
**⚠️ 注意**：屏幕**全黑留白**，不要生成任何界面；不要任何品牌标识

---

### 道具 6｜雪道标识牌（立柱式）

```
A ski slope marker sign post.
Total height: 200 cm. Sign panel: 40 cm wide × 60 cm tall.
Structure: a vertical grey metal pole (6 cm diameter) with a flat
rectangular sign panel mounted near the top, and a round base plate
at the bottom for stability.
Sign panel: blue background with a white border frame — the panel face
must be COMPLETELY BLANK, with no text, no numbers, no symbols at all.
Material: matte powder-coated metal.
Color: grey pole, blue panel with white border.
```

**尺寸标注**：总高 200cm，牌面 40×60cm，柱径 6cm
**⚠️ 注意**：牌面**完全留白**——雪道编号我会在引擎里用文本加上去

---

### 道具 7｜弹性绷带（卷起状态）

```
An elastic compression bandage in a ROLLED-UP state.
Rolled dimensions: 7 cm diameter × 10 cm wide (the roll's height).
Material: beige/off-white woven fabric with visible fabric texture.
Feature: the side of the roll must clearly show the layered spiral
wrapping — you should be able to count the individual layers of
bandage wound around the roll.
A short loose tail of bandage, about 15 cm, extending from the roll
and lying flat.
Color: beige / light skin tone fabric.
```

**尺寸标注**：卷起直径 7cm × 高 10cm
**⚠️ 注意**：**必须是卷起状态**（展开的绷带在 VR 里没法抓取）

---

### 道具 8｜隔热垫（卷起状态）

```
A foam camping/survival sleeping mat, in its ROLLED-UP state.
Rolled dimensions: 60 cm long × 15 cm diameter.
Material: dark grey / olive-green closed-cell foam with a reflective
aluminized surface on one side.
Surface: a clear repeating diamond or hexagonal embossed texture
across the reflective surface.
The roll should look tightly wound, with the outer edge of the mat
visible as a spiral line at both ends.
Color: dark grey and olive green, with a silver reflective face.
```

**尺寸标注**：长 60cm，卷起直径 15cm
**⚠️ 注意**：**卷起状态**，不要铺开

---

### 道具 9｜伤者人物（滑雪者，仰卧姿态）

> ⚠️ 这个最复杂，**建议单独一次生成**，且生成后我会在 Blender 里额外处理骨骼

```
A person in winter ski clothing, lying on their back (supine position),
as if injured after a fall on a ski slope.

Body: adult male, 170 cm tall, standard proportions.
Pose: lying flat on the back, legs slightly apart, arms relaxed at the
sides, with the RIGHT HAND resting on the RIGHT KNEE (suggesting a
knee injury).
Clothing: a thick dark navy-blue ski jacket (waterproof nylon shell,
matte finish with natural fabric wrinkles at the elbows and shoulders),
matching ski pants, sturdy snow boots, and a matte black ski helmet
still on the head.
Face: visible, eyes open, expression showing pain but conscious
(frowning, brow furrowed). Do not cover the face with goggles.
Goggles: pushed up on top of the helmet, not over the eyes.
Color palette: navy blue jacket and pants, black helmet and boots.
```

**尺寸标注**：身高 170cm
**⚠️ 特别说明**：
- 这是**角色**不是道具，请生成**全身四视图**
- 服装要**厚实**（滑雪服的特征），有自然褶皱
- 面部表情要**痛苦但清醒**（不是昏迷）
- 我需要在此基础上添加动画，所以**姿态要自然、四肢不要重叠遮挡**

---

## 🥈 第二批：心肺复苏场景

---

### 道具 10｜AED 自动体外除颤器

```
An AED (Automated External Defibrillator) portable unit.
Size: 30 cm wide × 24 cm tall × 10 cm thick, including a carrying handle
on top.
Material: bright yellow-green (the international AED color) hard plastic
housing with a dark grey front panel.
Features: a rectangular screen area on the front (leave the screen
COMPLETELY BLANK — pure dark grey, no UI), and two large round buttons
below it (one green, one red).
A sturdy integrated carrying handle on top.
Color: bright yellow-green body, dark grey panel.
```

**尺寸标注**：宽 30cm × 高 24cm × 厚 10cm
**⚠️ 注意**：屏幕和按钮上**不要任何文字或符号**

---

### 道具 11｜AED 电极片（密封包装）

```
A sealed pair of AED electrode pads in their foil pouch.
Pouch size: 15 cm × 12 cm, about 1 cm thick.
Material: silver aluminized foil pouch, slightly puffy/bulging because
of the pads and gel inside.
Feature: a serrated tear notch on one edge; the faint outline of the
two pads is visible through the foil. Leave the pouch surface BLANK —
no text, no diagrams, no symbols.
Color: bright silver foil.
```

**尺寸标注**：包装 15×12cm × 厚 1cm
**⚠️ 注意**：包装袋上**不要任何图示或文字**（真实的 AED 电极片包装有图示，但 AI 生成会乱码）

---

## 🥉 第三批：失温场景与环境道具

---

### 道具 12｜滑雪板（单板）

```
A snowboard.
Size: 150 cm long × 28 cm wide, about 1.5 cm thick.
Material: glossy top sheet with a bright color (fluorescent yellow or
bright blue), and a black base.
Features: two binding assemblies mounted on the top surface, positioned
at the center with roughly 45 cm between them. The bindings are black
with visible straps and high backs.
Shape: characteristic snowboard outline — rounded nose and tail,
slightly waisted in the middle.
The metal edge running around the perimeter should be visible as a
thin steel line.
```

**尺寸标注**：长 150cm × 宽 28cm

---

### 道具 13｜滑雪杖（单支）

```
A single ski pole.
Size: 120 cm long, 2 cm shaft diameter.
Material: aluminum alloy shaft with a matte finish, black plastic grip
at the top, and a circular plastic basket (snow ring) 5 cm above the
bottom tip.
Features: a wrist strap attached to the grip (loose, hanging), and a
sharp metal tip at the bottom.
Color: silver shaft, black grip and basket.
```

**尺寸标注**：长 120cm，杆径 2cm

---

### 道具 14｜滑雪头盔

```
A ski/snowboard helmet, shown as a standalone object (NOT on a head).
Size: fits a 56-60 cm head circumference; outer shell approximately
22 cm long × 18 cm wide × 16 cm tall.
Material: matte black hard shell with a visible dark grey EPS foam
liner at the lower edge.
Features: 3-5 ventilation openings on the top and back; an adjustable
chin strap hanging loose; a goggle retaining clip at the back.
Color: matte black, dark grey liner.
```

**尺寸标注**：外壳约 22×18×16cm
**⚠️ 注意**：**独立物体**，不要生成"戴在某人头上"

---

### 道具 15｜滑雪护目镜

```
Ski goggles, standalone object.
Size: 18 cm wide × 10 cm tall × 8 cm deep (including the frame curve).
Lens: dark tinted — model it as OPAQUE dark grey/black, NOT transparent.
Frame: black soft rubber/foam frame with a curved shape that would fit
a face.
Strap: an adjustable black elastic strap attached to both sides,
looping behind.
The lens should have a subtle spherical curvature.
Color: black frame and strap, dark opaque lens.
```

**尺寸标注**：宽 18cm × 高 10cm
**⚠️ 注意**：镜片用**不透明深色**建模，不要真透明

---

### 道具 16｜对讲机

```
A handheld two-way radio (walkie-talkie), ski patrol style.
Size: 12 cm tall × 6 cm wide × 3.5 cm thick (excluding antenna).
Material: black rugged engineering plastic.
Features: a rubberized antenna about 8 cm long on the top; a small
blank screen area on the upper front (leave BLANK, no text or numbers);
several small buttons below the screen; a large push-to-talk button
on the left side; textured rubber grip panels on the sides.
Color: matte black with dark grey accents.
```

**尺寸标注**：高 12cm × 宽 6cm × 厚 3.5cm（不含天线）
**⚠️ 注意**：屏幕和按键上**不要文字数字**

---

### 道具 17｜医用丁腈手套（一只，平铺）

```
A single disposable nitrile examination glove, lying flat and empty.
Size: 24 cm long (wrist to middle fingertip) × 12 cm wide across the palm.
Material: light blue nitrile rubber, matte finish with a slight sheen.
Features: all five fingers spread apart and lying flat; visible seam
lines along the fingers; a rolled cuff at the wrist opening.
The glove should look like it is lying flat on a surface, not being worn.
Color: light blue.
```

**尺寸标注**：长 24cm × 宽 12cm
**⚠️ 注意**：**五指展开、平铺**，不要握拳；**不要有手在里面**

---

## 第四批：扩展场景道具（可后续生成）

---

### 道具 18｜颈椎固定器（颈托）

```
A cervical collar (neck brace), adult universal size, shown in its
OPEN / flat state.
Size: 12 cm tall, 40 cm long when laid open.
Material: rigid off-white plastic shell with grey foam padding along
the inner edges.
Features: a chin support cut-out at the top front; a trachea opening
hole at the front center; velcro fastening straps at both ends.
Shape: when viewed from the side, it has a distinct curve — low at the
front, rising toward the back to support the neck.
Color: off-white shell, grey padding.
```

---

### 道具 19｜雪崩信标（收发器）

```
An avalanche transceiver (beacon) device.
Size: 12 cm tall × 7 cm wide × 3 cm thick.
Material: bright orange rugged plastic housing (the standard color for
avalanche safety gear).
Features: a dark LCD screen area on the front (leave COMPLETELY BLANK);
a single large rotary switch/knob on the front; a lanyard attachment
hole at the top corner.
Color: bright orange body with dark grey/black details.
```

**⚠️ 注意**：屏幕**留白**

---

### 道具 20｜探雪杆（折叠状态）

```
A collapsible avalanche probe, in its FOLDED state.
Folded length: 40 cm. Diameter: 1.3 cm.
Material: bright colored aluminum alloy (orange or yellow) with
black plastic locking ferrules between segments.
Features: clearly visible 3-4 segment folding structure, with the
bundled segments held together by a thin internal cable; a pointed
metal tip at one end; a small loop/handle at the other end.
Color: bright orange or yellow segments, black connectors.
```

---

### 道具 21｜雪铲

```
An avalanche snow shovel.
Total length: 75 cm. Blade: 28 cm wide × 22 cm tall.
Material: bright colored metal blade (orange or red) with a black
D-shaped grip handle at the top of the shaft.
Features: strengthening ribs/ridges on the blade face; the shaft is
detachable from the blade (a visible connection joint); the blade has
a slightly scooped shape.
Color: bright orange/red blade, black shaft and grip.
```

---

### 道具 22｜救援雪橇（船型）

```
A rescue sled (toboggan style), for transporting an injured person
on snow.
Size: 220 cm long × 60 cm wide × 25 cm deep.
Material: bright orange rigid plastic hull, with a slightly upturned
curved front (like a canoe bow).
Features: a flat interior floor; side walls about 25 cm high;
several molded handhold cut-outs along the sides; a tow rope attached
at the front.
Color: bright orange, with black rope and handles.
```

---

### 道具 23｜滑雪场护栏网（单片）

```
A single section of ski resort safety netting / barrier fence.
Size: 200 cm wide × 100 cm tall, plus support posts.
Structure: a rectangular net panel (woven mesh, small diamond or square
openings, orange or black netting) stretched on a frame; two vertical
support posts at the ends, with flat base plates.
Material: the net is flexible woven cord; posts are metal or rigid
plastic, padded with foam at the top.
Color: orange netting, black/dark grey posts.
```

---

# 第五部分：请按这个方式回复我

对于每个道具，请：

1. **先生成一张四视图拼版预览**（把前/左/右/后拼在一起），让我快速确认整体效果
2. **再分别输出 4 张独立的大图**（前视图、左视图、右视图、后视图）
3. 如果某张图的视角不准确（比如"左视图"看起来像斜视图），请**重新生成那一张**

**遇到不确定的地方，直接问我，不要自己猜。** 特别是尺寸和材质，问清楚比生成错了返工快。

---

# 第六部分：开始

**请从「道具 1｜急救夹板」开始。**

之后按清单顺序依次生成即可。每完成一个道具，等我确认后再进行下一个。
