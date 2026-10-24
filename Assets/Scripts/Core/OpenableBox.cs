using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;

/// <summary>
/// 可开启的箱子 —— 既是可抓取的物理物体，又能按交互键打开盖子。
///
/// 行为：
///   · 箱子本身能被抓起、移动、放下（像丢 cube 一样），碰撞会弹开
///   · 指着箱子按【交互键】→ 盖子打开，里面的道具变成可抓取
///   · 关闭时里面的道具被锁住（防止穿模飞出去）
///
/// 结构要求（在场景里搭好）：
///   箱子（根物体）        ← 挂这个组件 + ItemPhysicsSetup
///     ├── 箱体（Box）      ← 碰撞体
///     ├── 盖子（Lid）      ← 单独一个子物体，会绕铰链旋转
///     │     └── 铰链点     ← 盖子的旋转轴心（用空物体做父级）
///     └── 内部（Contents） ← 空物体，里面放所有医疗道具
///
/// 用法：
///   ① 搭好上面这个层级
///   ② 在箱子根物体上挂本组件
///   ③ 把「盖子」「内部」两个字段拖进去
///   ④ 把所有道具放进「内部」物体下面
/// </summary>
public class OpenableBox : MonoBehaviour
{
    [Header("结构引用")]
    [Tooltip("盖子物体（会绕铰链旋转）")]
    public Transform lid;

    [Tooltip("箱体物体。如果上下两半形状相同，开箱时会自动让【朝上的那一半】翻转，" +
             "避免盖子往地下翻开")]
    public Transform body;

    [Tooltip("箱子内部的容器（里面放所有道具）")]
    public Transform contents;

    [Tooltip("铰链位置。留空会用『盖子的铰链参照』自动算，见下面的 hingeReference")]
    public Transform hinge;

    [Tooltip("铰链参照物体（比如 Medical_Kit_Hinge）。用来确定转轴的位置")]
    public Transform hingeReference;

    [Tooltip("勾上=运行时自动建铰链枢轴；不勾=用你手动在编辑器里搭好的枢轴（推荐）")]
    public bool makeOwnPivot = false;

    [Tooltip("开箱时自动判断哪一半朝上，让那一半翻转开箱（避免倒放的箱子往地下开盖）")]
    public bool autoPickTopHalf = true;

    [Header("调试：强制指定翻转对象和方向")]
    [Tooltip("关掉自动判断，手动指定翻哪一半（用来测试方向）")]
    public bool forceManual = false;

    [Tooltip("强制翻转的对象：0=盖子  1=箱体")]
    [Range(0, 1)]
    public int forceWhich = 0;

    [Tooltip("强制使用的角度符号：1 = +openAngle，-1 = -openAngle")]
    [Range(-1, 1)]
    public int forceSign = 1;

    [Header("开合")]
    [Tooltip("开始就是打开状态")]
    public bool startOpened = false;

    [Tooltip("开盖角度（度）。正数=一个方向，负数=反方向。可以超过 180°")]
    public float openAngle = 100f;

    [Tooltip("朝下的那一半抬起多少度。小幅抬起可以把上盖顶离地面，避免穿地")]
    [Range(0f, 60f)]
    public float holdAngle = 15f;

    [Tooltip("开盖方向（哪根轴）。这是【世界轴】—— 黄球位置画出的就是它")]
    public Vector3 openAxis = Vector3.right;

    [Tooltip("勾上则反向翻开（如果两半都往反方向转，就勾这个）")]
    public bool reverseOpen = false;

    [Tooltip("单独反转【朝上那一半】的旋转方向（顺时针 ↔ 逆时针）")]
    public bool reverseTop = false;

    [Tooltip("单独反转【朝下那一半】的旋转方向")]
    public bool reverseBottom = false;

    [Tooltip("开合动画时长（秒）。0 = 瞬间")]
    public float openDuration = 2f;

    [Header("交互")]
    [Tooltip("按交互键才开。关掉的话碰到就开")]
    public bool requireInteractKey = true;

    [Tooltip("打开后是否可以再关上")]
    public bool canClose = true;

    [Tooltip("打开后箱子变成场景装饰：不能被选中/拖拽/关闭，也不再受物理影响")]
    public bool becomeStaticWhenOpen = true;

    [Tooltip("开箱提示文字")]
    public string openHint = "按交互键打开医疗箱";

    [Tooltip("必须在【放下箱子】（不拿在手上）的状态下才能打开")]
    public bool mustBeReleased = true;

    // ---------- 状态 ----------
    private bool isOpen = false;

    /// <summary>是否正在播放开合动画</summary>
    private bool isAnimating = false;

    private float animTimer = 0f;

    /// <summary>铰链枢轴（= 盖子的父物体）</summary>
    private Transform lidPivot;

    /// <summary>本次开箱时"朝上"的那一半</summary>
    private Transform movingPart;

    // ---------- 两半各自的"关闭旋转"和"旋转轴" ----------
    // ⚠️ 不存目标四元数，而是存【关闭时的旋转 + 轴 + 角度】。
    //    因为 Quaternion.Slerp 会走最短路径，超过 180° 就转不到位。
    //    按角度轴每帧重算就没有这个限制，角度可以任意大。
    private Quaternion topClosedRot = Quaternion.identity;
    private Quaternion bottomClosedRot = Quaternion.identity;
    private Vector3 topAxisLocal = Vector3.right;
    private Vector3 bottomAxisLocal = Vector3.right;
    private float topOpenAngle = 0f;
    private float bottomOpenAngle = 0f;

    private readonly List<Transform> contentContainers = new List<Transform>();

    /// <summary>动画期间被锁定的道具本地坐标</summary>
    private readonly List<LocalPose> contentLocals = new List<LocalPose>();

    /// <summary>是否正在锁定道具的本地坐标</summary>
    private bool lockedContents = false;

    [Tooltip("打印锁定道具本地坐标的日志（调试用）")]
    public bool logLock = false;

    [Tooltip("打印开箱过程的详细日志（比较啰嗦，排查时才开）")]
    public bool verboseLog = false;

    [Header("开箱时把道具抖开")]
    [Tooltip("开箱后给每件道具加一点随机速度，让挤在一起的道具自己分开。\n" +
             "解决『开箱后部分道具互相穿插、卡住取不出来』的问题")]
    public bool separateOnOpen = true;

    [Tooltip("抖开的力度（米/秒）。太大道具会乱飞")]
    [Range(0f, 1f)]
    public float separateStrength = 0.12f;

    /// <summary>当前是否打开</summary>
    public bool IsOpen => isOpen;

    /// <summary>箱子被打开时触发（供步骤系统桥接用）</summary>
    public event System.Action<OpenableBox> OnOpened;

    /// <summary>箱子被关闭时触发</summary>
    public event System.Action<OpenableBox> OnClosed;

    private void Awake()
    {
        ResolvePivot();
        BindHalvesToPivot();
        RecomputeRotation();

        CacheContents();

        // 初始状态
        isOpen = startOpened;
        ApplyLid(isOpen ? 1f : 0f);

        SetContentsActive(isOpen);

        // 如果一开始就是打开状态，也要解除坐标锁定
        if (isOpen) ReleaseContentLock();

        if (verboseLog) Debug.Log($"[医疗箱] 初始化完成\n" +
                  $"  枢轴 = {(lidPivot != null ? lidPivot.name : "❌ 无")}\n" +
                  $"  Lid  = {(lid != null ? lid.name : "❌ 无")}\n" +
                  $"  Body = {(body != null ? body.name : "（未指定）")}\n" +
                  $"  当前朝上的一半 = {(movingPart != null ? movingPart.name : "无")}\n" +
                  $"  （每次开箱时会重新判断）\n" +
                  $"  isOpen = {isOpen}\n" +
                  $"  内部道具数 = {contentContainers.Count}");
    }

    /// <summary>确定铰链枢轴</summary>
    private void ResolvePivot()
    {
        // ① 盖子的父物体不是箱子根物体 → 说明你已经把盖子挂到枢轴上了
        if (lid != null && lid.parent != null && lid.parent != transform)
        {
            lidPivot = lid.parent;
            return;
        }

        // ② 手动指定的 hinge / hingeReference
        Transform manual = hinge != null ? hinge : hingeReference;
        if (manual != null && lid != null && manual != lid)
        {
            lidPivot = manual;
            return;
        }

        Debug.LogWarning($"[医疗箱] 找不到铰链枢轴！\n" +
                         $"请按这个结构搭：\n" +
                         $"  {name}\n" +
                         $"    └── LidPivot（空物体，放在铰链位置）\n" +
                         $"          ├── {lid?.name ?? "盖子"}\n" +
                         $"          └── {body?.name ?? "箱体"}\n" +
                         $"然后把 Openable Box 的 Lid / Body 字段都指向对应物体。");
    }

    /// <summary>
    /// 把盖子和箱体都挂到枢轴下。
    /// 这样无论箱子怎么摆放，都能通过旋转枢轴来"翻开其中一半"。
    /// </summary>
    private void BindHalvesToPivot()
    {
        if (lidPivot == null) return;

        if (lid != null && lid.parent != lidPivot)
            lid.SetParent(lidPivot, true);   // true = 保持世界位置

        if (body != null && body != lidPivot && body.parent != lidPivot)
            body.SetParent(lidPivot, true);
    }

    /// <summary>
    /// 决定开箱时翻转【哪一半】，以及往哪个方向翻。
    ///
    /// 规则：让【当前朝上的那一半】向外翻上去。
    ///   · 正放的箱子 → 翻盖子（原本就在上面）
    ///   · 倒放的箱子 → 翻箱体（它现在在上面）
    /// 这样开箱永远是"打开"而不是"往地里翻"。
    /// </summary>
    /// <summary>
    /// 决定【这一次】开箱怎么转，并算出两半各自的目标角度。
    ///
    /// 采用【蚌壳式】：两半绕同一根轴、朝相反方向转。
    ///   · 朝上的那一半：转 -openAngle（向上翻开，露出内部）
    ///   · 朝下的那一半：转 +holdAngle（小幅抬起，把上盖顶离地面）
    /// 这样无论箱子正放还是倒放，两半都是"张开"的，不会互相穿插，也不会穿地。
    ///
    /// 每次调用 Open() / Close() 都会重新执行，所以能把箱子翻过来再开。
    /// </summary>
    private void RecomputeRotation()
    {
        if (lidPivot == null || lid == null || body == null) return;

        // ---------- 1. 判断哪一半朝上（用几何中心，不用 Transform 原点）----------
        bool bodyIsTop;
        if (forceManual)
            bodyIsTop = (forceWhich == 1);
        else
            bodyIsTop = GetVisualCenterY(body) > GetVisualCenterY(lid);

        Transform topPart = bodyIsTop ? body : lid;
        Transform bottomPart = bodyIsTop ? lid : body;

        // 兼容外部调试：movingPart 记"朝上的那一半"
        movingPart = topPart;

        // ---------- 2. 记下两半当前的本地旋转（关闭基准）----------
        topClosedRot = topPart.localRotation;
        bottomClosedRot = bottomPart.localRotation;

        // ---------- 3. 把旋转轴换算到它们的父空间 ----------
        Transform parent = topPart.parent;
        Vector3 axisLocal = (parent != null)
            ? parent.InverseTransformDirection(transform.TransformDirection(openAxis.normalized))
            : openAxis.normalized;

        // ---------- 4. 角度 ----------
        float topAngle = -openAngle;
        float bottomAngle = holdAngle;

        // 三个反向开关，叠加生效
        if (reverseOpen)
        {
            topAngle = -topAngle;
            bottomAngle = -bottomAngle;
        }
        if (reverseTop) topAngle = -topAngle;
        if (reverseBottom) bottomAngle = -bottomAngle;

        // 存下"轴 + 角度"，实际旋转在 ApplyLid 里按角度轴算。
        // 这样角度可以超过 180°，不受四元数最短路径的限制。
        topAxisLocal = axisLocal;
        bottomAxisLocal = axisLocal;
        topOpenAngle = topAngle;
        bottomOpenAngle = bottomAngle;

        if (verboseLog) Debug.Log($"[医疗箱] 本次开箱方案（蚌壳式）\n" +
                  $"  上：{topPart.name}  角度 {topAngle:F1}°\n" +
                  $"  下：{bottomPart.name}  角度 {bottomAngle:F1}°\n" +
                  $"  盖子视觉Y = {GetVisualCenterY(lid):F3}  " +
                  $"箱体视觉Y = {GetVisualCenterY(body):F3}\n" +
                  $"  旋转轴（世界）= {openAxis.normalized}   （父空间）= {axisLocal}\n" +
                  $"  开关：ReverseOpen={reverseOpen}  ReverseTop={reverseTop}  ReverseBottom={reverseBottom}");
    }

    /// <summary>
    /// 取某个物体的【几何中心】世界 Y。
    ///
    /// ⚠️ 不能直接用 GetComponentInChildren<Renderer>()，因为它的子物体里
    ///    可能有 contents 容器（里面装着道具），会抓到道具的包围盒。
    ///    这里优先用物体【自己身上】的渲染器。
    /// </summary>
    private float GetVisualCenterY(Transform t)
    {
        if (t == null) return 0f;

        // ① 优先：物体自己身上的渲染器
        var self = t.GetComponent<Renderer>();
        if (self != null) return self.bounds.center.y;

        // ② 其次：子物体里的渲染器，但要跳过 contents 容器
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (IsUnderContents(r.transform)) continue;
            return r.bounds.center.y;
        }

        // ③ 兜底：Transform 位置
        return t.position.y;
    }

    /// <summary>判断某个 Transform 是否在道具容器（名字含 contents）之下</summary>
    private bool IsUnderContents(Transform t)
    {
        Transform cur = t;
        int guard = 0;
        while (cur != null && guard++ < 10)
        {
            if (cur.name.IndexOf("contents", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // 到 Box 根就停
            if (cur == transform) break;
            cur = cur.parent;
        }
        return false;
    }

    /// <summary>
    /// 把两半设到指定开合程度（0 = 关闭，1 = 完全打开）。
    ///
    /// ⚠️ 用【轴角插值】而不是 Quaternion.Slerp：
    ///    Slerp 在四元数空间走最短路径，角度超过 180° 会抄近路
    ///    （187° 变成 -173°），看起来就像没转到位。
    ///    这里插值的是【角度数值】，然后按轴重新构造旋转，
    ///    所以角度可以任意大（180°/270°/360° 都行）。
    /// </summary>
    private void ApplyLid(float t)
    {
        if (lid == null || body == null) return;

        // 朝上的那一半：转到完全打开
        // 朝下的那一半：转到"抬起一点点"
        Transform topPart = movingPart;
        Transform bottomPart = (topPart == lid) ? body : lid;

        if (topPart != null)
        {
            float a = topOpenAngle * t;
            topPart.localRotation = Quaternion.AngleAxis(a, topAxisLocal) * topClosedRot;
        }

        if (bottomPart != null)
        {
            float a = bottomOpenAngle * t;
            bottomPart.localRotation = Quaternion.AngleAxis(a, bottomAxisLocal) * bottomClosedRot;
        }
    }

    /// <summary>
    /// 收集所有内部道具的容器。
    ///
    /// 支持两种结构：
    ///   ① 根物体下的 Contents（唯一容器）
    ///   ② 盖子/箱体里各有一个叫 contents 的子容器（道具分装在两半里）
    ///
    /// 会把找到的容器整体存下来，开箱时启用、关箱时禁用。
    /// </summary>
    private void CacheContents()
    {
        contentContainers.Clear();

        // ① 指定的 contents
        if (contents != null) contentContainers.Add(contents);

        // ② 递归找所有名字里含 "contents" 的物体
        //    （跳过已经加过的、以及箱体/盖子本身）
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == contents) continue;
            if (t == lid || t == body || t == lidPivot) continue;

            string n = t.name;
            if (n.IndexOf("contents", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!contentContainers.Contains(t)) contentContainers.Add(t);
            }
        }

        if (verboseLog)
        {
            var names = new System.Text.StringBuilder();
            foreach (var c in contentContainers)
            {
                if (c == null) continue;
                if (names.Length > 0) names.Append("、");
                names.Append($"{GetPath(c)}({c.childCount})");
            }
            Debug.Log($"[医疗箱] {contentContainers.Count} 个道具容器：{names}");
        }
    }

    /// <summary>取物体的层级路径（调试用）</summary>
    private static string GetPath(Transform t)
    {
        if (t == null) return "null";
        string p = t.name;
        Transform cur = t.parent;
        int guard = 0;
        while (cur != null && guard++ < 6)
        {
            p = cur.name + "/" + p;
            cur = cur.parent;
        }
        return p;
    }

    private void Start()
    {
        // 记录一次道具的本地坐标（关箱状态下）
        RecordContentLocals();
        lockedContents = true;
    }

    /// <summary>
    /// 每帧末把被锁定的道具本地坐标写回去。
    /// 用 LateUpdate 是因为要在物理和动画都算完之后执行，
    /// 这样道具的位置永远是记录的本地坐标 → 严格跟随父物体。
    /// </summary>
    private void LateUpdate()
    {
        if (!lockedContents) return;

        ApplyContentLocals();

        if (logLock && Time.frameCount % 30 == 0)
        {
            string sample = "（无）";
            if (contentLocals.Count > 0 && contentLocals[0].t != null)
            {
                var t0 = contentLocals[0].t;
                sample = $"{t0.name} localPos={t0.localPosition.ToString("F4")} " +
                         $"localRot={t0.localRotation.eulerAngles.ToString("F1")}";
            }
            Debug.Log($"[医疗箱] 正在锁定道具本地坐标（{contentLocals.Count} 个物体）\n" +
                      $"  示例：{sample}");
        }
    }

    private void Update()
    {
        // ⚠️ 只有"正在播放开合动画"时才更新盖子。
        //    之前漏了这个判断，导致 animTimer 从 0 开始就一直在跑动画，
        //    游戏一启动盖子就自己打开了。
        if (!isAnimating) return;
        if (openDuration <= 0.01f) { isAnimating = false; return; }

        animTimer += Time.deltaTime;
        float t = Mathf.Clamp01(animTimer / openDuration);

        bool justFinished = false;
        if (t >= 1f)
        {
            t = 1f;
            isAnimating = false;
            justFinished = true;
        }

        // 缓动，看起来更顺
        float eased = t * t * (3f - 2f * t);
        ApplyLid(eased);

        // ---------- 动画刚结束 ----------
        if (justFinished)
        {
            if (isOpen)
            {
                // ⚠️ 顺序很重要：先解除坐标锁定，再恢复物理。
                //    反过来的话，LateUpdate 还会把道具钉在原位，
                //    而物理已经在算 → 道具被"钉住又推开"，互相穿插
                //    → 出现"部分物体取不出来 / 卡住"。
                ReleaseContentLock();
                SetContentsPhysics(true);

                // 给每件道具一点随机速度，让挤在一起的道具自己分开
                if (separateOnOpen) SeparateContents();

                if (verboseLog) Debug.Log($"[医疗箱] 开箱动画结束，道具恢复物理并解除锁定");
            }
            else
            {
                // 关箱结束 → 隐藏道具
                SetContentsActive(false);
                if (verboseLog) Debug.Log($"[医疗箱] 关箱动画结束，道具已隐藏");
            }
        }
    }

    // ==================== 开 / 关 ====================
    /// <summary>打开箱子</summary>
    public void Open()
    {
        if (isOpen) return;

        // ⭐ 每次开箱都重新判断哪一面朝上
        RecomputeRotation();

        isOpen = true;
        animTimer = 0f;
        isAnimating = openDuration > 0.01f;

        if (!isAnimating)
        {
            ApplyLid(1f);
            ReleaseContentLock();           // 先解锁
            SetContentsPhysics(true);       // 再给物理
            if (separateOnOpen) SeparateContents();
        }

        // 放出里面的道具（先冻结物理，等动画结束再恢复，
        // 否则道具刚出现时会互相碰撞/弹飞）
        SetContentsActive(true);
        if (isAnimating) SetContentsPhysics(false);

        // 开箱后箱子变成"场景装饰"
        if (becomeStaticWhenOpen) MakeStatic();

        Debug.Log($"[医疗箱] 已打开，放出 {contentContainers.Count} 个容器里的道具");

        var hint = FindAnyObjectByType<HintPanel>();
        if (hint != null) hint.ShowHint($"医疗箱已打开（{contentContainers.Count} 件道具）", 3f);

        // 通知订阅者（步骤系统桥接用）
        OnOpened?.Invoke(this);
    }

    /// <summary>
    /// 把箱子变成固定场景物体：
    ///   · 不能再被抓住（XRGrabInteractable 禁用 → XRI 自动注销）
    ///   · 不再受物理影响（Rigidbody 变运动学，原地不动）
    ///   · 不能再被射线选中/高亮（碰撞体去掉"可交互"属性 + 组件禁用）
    /// </summary>
    private void MakeStatic()
    {
        // ---------- 1. 关掉抓取 ----------
        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab != null) grab.enabled = false;

        // ---------- 2. 关掉 X 光高亮 ----------
        var hl = GetComponent<XRayHoverHighlight>();
        if (hl != null)
        {
            hl.ForceShow(false);
            hl.enabled = false;
        }

        // ---------- 3. 物理变运动学（原地不动，但仍会挡住其他物体）----------
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // ---------- 4. 道具脚本停止响应悬停/选择 ----------
        var item = GetComponent<ItemPhysicsSetup>();
        if (item != null) item.enabled = false;
    }

    /// <summary>关闭箱子</summary>
    public void Close()
    {
        // 如果已经变成固定物，就不允许再关上
        if (becomeStaticWhenOpen && isOpen) return;
        if (!isOpen) return;

        // ⭐ 关箱也要按当前朝向重新判断，否则可能关不回去
        RecomputeRotation();

        isOpen = false;
        animTimer = 0f;
        isAnimating = openDuration > 0.01f;

        if (!isAnimating)
        {
            ApplyLid(0f);
            SetContentsPhysics(false);
        }

        // 关箱时道具先冻结，再隐藏（避免它们在被隐藏前乱掉）
        SetContentsPhysics(false);
        SetContentsActive(false);

        if (verboseLog) Debug.Log($"[医疗箱] 已关闭");

        // 通知订阅者（步骤系统桥接用）
        OnClosed?.Invoke(this);
    }

    /// <summary>切换开合（按交互键时调用）</summary>
    public void Toggle()
    {
        // 拿在手上时不允许打开
        if (!isOpen && mustBeReleased && IsGrabbed())
        {
            var hint0 = FindAnyObjectByType<HintPanel>();
            if (hint0 != null) hint0.ShowHint("请先把医疗箱放到地上再打开", 2.5f);
            return;
        }

        if (isOpen)
        {
            if (canClose) Close();
            else
            {
                var hint = FindAnyObjectByType<HintPanel>();
                if (hint != null) hint.ShowHint("医疗箱已经打开了", 2f);
            }
        }
        else Open();
    }

    // ==================== 内部道具 ====================
    /// <summary>开箱时启用内部道具，关箱时禁用（支持多个容器）</summary>
    private void SetContentsActive(bool active)
    {
        foreach (var c in contentContainers)
        {
            if (c == null) continue;
            if (c.gameObject.activeSelf != active)
                c.gameObject.SetActive(active);
        }
    }

    /// <summary>
    /// 开关内部道具的物理。
    ///
    /// active = false 时：
    ///   · Rigidbody 变运动学 → 不受重力、不会被碰撞推动
    ///   · 【并且每帧强行写回记录的本地坐标】→ 保证道具严格跟着
    ///     自己的父物体（箱体/盖子）走，相对位置绝对不变
    ///   · 刚体间的相互穿插也不会把它弹开
    ///
    /// 开箱动画期间先冻结，动画结束再恢复，
    /// 这样道具就不会在刚出现时互相碰撞飞出去。
    /// </summary>
    private void SetContentsPhysics(bool active)
    {
        foreach (var c in contentContainers)
        {
            if (c == null) continue;

            var bodies = c.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in bodies)
            {
                if (rb == null) continue;

                if (active)
                {
                    // ⚠️ 恢复物理前先清掉速度：
                    //    冻结期间刚体可能积累了残留速度，
                    //    一恢复就会互相弹开 / 穿插 → 部分道具卡住取不出来
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = !active;

                if (active) rb.WakeUp();
            }
        }

        if (!active)
        {
            // 记录当前所有道具的本地坐标（作为"锁定基准"）
            RecordContentLocals();
            lockedContents = true;
        }
        else
        {
            lockedContents = false;
        }
    }

    /// <summary>
    /// 给每件道具加一点随机速度，让挤在一起的道具自己分开。
    ///
    /// 为什么需要：
    ///   道具在箱子里本来就是紧挨着放好的，物理一恢复，
    ///   物理引擎会尝试把它们推开 —— 但有时会推不开，
    ///   表现为"部分道具互相穿插、卡死、拿不出来"。
    ///   加一点微小的随机速度就能让它们自然分开。
    /// </summary>
    private void SeparateContents()
    {
        if (separateStrength <= 0f) return;

        int count = 0;

        foreach (var c in contentContainers)
        {
            if (c == null) continue;

            foreach (var rb in c.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null) continue;
                if (rb.isKinematic) continue;

                Vector3 dir = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0f, 1f),        // 主要往上推，避免陷进箱底
                    Random.Range(-1f, 1f));

                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;

                rb.AddForce(dir.normalized * separateStrength, ForceMode.VelocityChange);
                count++;
            }
        }

        if (verboseLog && count > 0)
            Debug.Log($"[医疗箱] 已把 {count} 件道具轻轻抖开（力度 {separateStrength:F2}）");
    }

    /// <summary>记录容器下所有物体的本地位置/旋转/缩放</summary>
    private void RecordContentLocals()
    {
        contentLocals.Clear();

        foreach (var c in contentContainers)
        {
            if (c == null) continue;

            foreach (var t in c.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                contentLocals.Add(new LocalPose(t));
            }
        }
    }

    /// <summary>把记录的本地坐标强行写回（保证严格跟随父物体）</summary>
    /// <summary>
    /// 把记录的本地坐标写回道具。
    ///
    /// ⚠️ 必须跳过【玩家正拿在手上】的道具：
    ///    否则 XRGrabInteractable 把它移到手上后，这里每帧又把它钉回原位，
    ///    手和道具越拉越远 → XRI 判定距离过远自动脱手
    ///    → 表现成"道具拿不起来"。这就是手机拿不动的根因。
    /// </summary>
    private void ApplyContentLocals()
    {
        for (int i = 0; i < contentLocals.Count; i++)
        {
            var p = contentLocals[i];
            if (p.t == null) continue;

            // 正被玩家拿着 → 不要动它
            if (IsHeldByPlayer(p.t)) continue;

            p.t.localPosition = p.pos;
            p.t.localRotation = p.rot;
            p.t.localScale = p.scale;
        }
    }

    /// <summary>这个 Transform（或它的父级）是不是正被玩家拿在手上</summary>
    private static bool IsHeldByPlayer(Transform t)
    {
        var item = t.GetComponentInParent<ItemPhysicsSetup>();
        if (item == null) return false;
        return item.IsGrabbed;
    }

    /// <summary>
    /// 解除"道具坐标锁定"。
    /// 开箱后调用 —— 箱子已经打开，道具应该能自由取出，
    /// 再钉住它们就会导致拿不起来。
    /// </summary>
    public void ReleaseContentLock()
    {
        lockedContents = false;

        if (verboseLog)
            Debug.Log($"[医疗箱] 已解除道具坐标锁定（{contentLocals.Count} 个物体）");
    }

    /// <summary>本地坐标快照</summary>
    private struct LocalPose
    {
        public Transform t;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;

        public LocalPose(Transform tr)
        {
            t = tr;
            pos = tr.localPosition;
            rot = tr.localRotation;
            scale = tr.localScale;
        }
    }

    /// <summary>被 PropInteractor 调用：玩家指着箱子按下交互键</summary>
    public void OnInteractKeyPressed()
    {
        if (!requireInteractKey) return;

        // 必须在"放下箱子"的状态下才能开
        if (mustBeReleased && IsGrabbed())
        {
            var hint = FindAnyObjectByType<HintPanel>();
            if (hint != null) hint.ShowHint("请先把医疗箱放到地上再打开", 2.5f);
            return;
        }

        Toggle();
    }

    /// <summary>箱子现在是不是被拿在手上</summary>
    public bool IsGrabbed()
    {
        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab == null) return false;
        return grab.isSelected;
    }

    // ==================== 编辑器辅助 ====================
    private void OnValidate()
    {
        // 编辑器里只做参数校验，不改盖子位置。
        // 盖子的开合完全由代码在运行时用数学方式算，不动层级。

        // ⚠️ 不限制 openAngle 的正负 —— 负数就是反方向转，是合法用法
        if (openDuration < 0f) openDuration = 0f;
        if (openAxis.sqrMagnitude < 0.0001f) openAxis = Vector3.right;
    }

    /// <summary>在 Scene 视图里画一下铰链位置和开合方向，方便调试</summary>
    private void OnDrawGizmosSelected()
    {
        if (lid == null) return;

        // 铰链位置（枢轴会建在这里）
        Vector3 pivotPos = hinge != null ? hinge.position
                         : hingeReference != null ? hingeReference.position
                         : lid.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pivotPos, 0.03f);

        // 开合方向
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pivotPos, pivotPos + openAxis.normalized * 0.3f);

        if (contents != null)
        {
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.35f);
            var cols = contents.GetComponentsInChildren<Collider>();
            foreach (var c in cols)
                if (c != null) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
}
