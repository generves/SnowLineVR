using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 指向射线（辅助用）—— 独立画一条从手柄射出的线。
///
/// 为什么需要它：
///   XRI 自带的 XRInteractorLineVisual 在【手里已经抓着东西】时会被隐藏
///   （被交互组判定为 blocked，或者 NearFarInteractor 拿不到线段点）。
///   结果就是"拿着道具时看不到射线，不知道指着哪"。
///
///   这个组件绕开 XRI 的可视化逻辑，自己画一条线，
///   只要打开就一定能看见。
///
/// 用法：
///   ① 挂在手柄的交互器上（和 XR Ray Interactor / Near-Far Interactor 同一个物体）
///   ② 勾上 Show 就能看见
///   ③ PropInteractor 会在"使用模式"下自动把它打开
/// </summary>
[RequireComponent(typeof(XRBaseInteractor))]
public class InteractorPointerLine : MonoBehaviour
{
    [Header("显示")]
    [Tooltip("勾上才画线")]
    public bool show = false;

    [Tooltip("线颜色")]
    public Color color = new Color(0.3f, 0.9f, 1f, 0.9f);

    [Tooltip("线宽（米）")]
    [Range(0.001f, 0.03f)]
    public float width = 0.006f;

    [Tooltip("没有命中任何东西时的线长（米）")]
    public float defaultLength = 5f;

    [Tooltip("命中了东西就在命中点截断")]
    public bool stopAtHit = true;

    [Tooltip("线的材质（留空会自动建一个不受光的）")]
    public Material lineMaterial;

    [Header("调试")]
    public bool logToConsole = false;

    // ---------- 内部 ----------
    private XRBaseInteractor interactor;
    private IXRRayProvider rayProvider;
    private LineRenderer lr;
    private Material runtimeMat;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        interactor = GetComponent<XRBaseInteractor>();
        rayProvider = interactor as IXRRayProvider;

        EnsureLineRenderer();
        ApplyVisible();
    }

    private void OnDestroy()
    {
        if (runtimeMat != null) Destroy(runtimeMat);
    }

    private void LateUpdate()
    {
        if (!show || lr == null) return;

        if (!TryGetRay(out Vector3 origin, out Vector3 direction))
        {
            lr.positionCount = 0;
            return;
        }

        // 终点：命中就用命中点，否则按固定长度
        float length = defaultLength;
        if (stopAtHit &&
            Physics.Raycast(origin, direction, out RaycastHit hit, defaultLength,
                ~0, QueryTriggerInteraction.Ignore))
        {
            length = hit.distance;
        }

        lr.positionCount = 2;
        lr.SetPosition(0, origin);
        lr.SetPosition(1, origin + direction * length);
    }

    /// <summary>取射线的起点和方向</summary>
    private bool TryGetRay(out Vector3 origin, out Vector3 direction)
    {
        origin = transform.position;
        direction = transform.forward;

        if (rayProvider != null)
        {
            var o = rayProvider.GetOrCreateRayOrigin();
            if (o != null)
            {
                origin = o.position;

                // 终点变换存在的话，用"起点→终点"当方向，和 XRI 的射线一致
                var e = rayProvider.rayEndTransform;
                if (e != null && (e.position - origin).sqrMagnitude > 0.000001f)
                    direction = (e.position - origin).normalized;
                else
                    direction = o.forward;

                return true;
            }
        }

        // 不是射线型交互器：用手柄自身朝前
        return true;
    }

    /// <summary>外部开关（PropInteractor 在"使用模式"下会调用）</summary>
    public void SetVisible(bool v)
    {
        show = v;
        ApplyVisible();
        if (logToConsole) Debug.Log($"[指向射线] {name} → {(v ? "显示" : "隐藏")}");
    }

    private void ApplyVisible()
    {
        if (lr != null) lr.enabled = show;
    }

    // ==================== 建 LineRenderer ====================
    private void EnsureLineRenderer()
    {
        lr = GetComponent<LineRenderer>();
        if (lr == null) lr = gameObject.AddComponent<LineRenderer>();

        lr.useWorldSpace = true;
        lr.positionCount = 0;
        lr.numCapVertices = 2;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.allowOcclusionWhenDynamic = false;

        // 材质：优先用指定的，否则建一个不受光的
        if (lineMaterial != null)
        {
            lr.material = lineMaterial;
        }
        else
        {
            if (runtimeMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh == null) sh = Shader.Find("Sprites/Default");

                if (sh != null)
                {
                    runtimeMat = new Material(sh) { name = "InteractorPointerLine_Runtime" };
                    if (runtimeMat.HasProperty(BaseColorId)) runtimeMat.SetColor(BaseColorId, color);
                    if (runtimeMat.HasProperty(ColorId)) runtimeMat.SetColor(ColorId, color);
                }
            }
            if (runtimeMat != null) lr.material = runtimeMat;
        }

        lr.startWidth = width;
        lr.endWidth = width;

        // 尽量画在全景之上
        if (lr.material != null) lr.material.renderQueue = 4500;

        ApplyVisible();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (lr != null)
        {
            lr.startWidth = width;
            lr.endWidth = width;
        }
        if (runtimeMat != null)
        {
            if (runtimeMat.HasProperty(BaseColorId)) runtimeMat.SetColor(BaseColorId, color);
            if (runtimeMat.HasProperty(ColorId)) runtimeMat.SetColor(ColorId, color);
        }
    }
}
