using UnityEngine;
using TMPro;

/// <summary>
/// 虚拟监护仪 —— 本作品「错误后果可视化」的视觉呈现层。
///
/// 职责：
///   1. 订阅 VitalsSimulator，实时显示心率 / 体温 / 疼痛三个数值
///   2. 按阈值变色：正常白 → 警告黄 → 危险红（闪烁）
///   3. 画心率折线图 —— 让学员亲眼看见「犯错那一刻曲线飙上去」
///
/// ⚠️ 本脚本用新版 Input System 的 UI（不涉及键盘，但整个工程统一新版）
/// </summary>
public class VitalsMonitor : MonoBehaviour
{
    [Header("数据源")]
    [SerializeField] private VitalsSimulator vitals;

    [Header("数值文本")]
    public TMP_Text heartRateText;
    public TMP_Text temperatureText;
    public TMP_Text painText;

    [Header("折线图")]
    [Tooltip("曲线的父物体（一个 RectTransform 容器）")]
    [SerializeField] private RectTransform graphContainer;
    [Tooltip("每个采样点的图片预制体（一个很小的白色方块 Image）")]
    [SerializeField] private GameObject pointPrefab;
    [Tooltip("采样间隔（秒）")]
    [SerializeField] private float sampleInterval = 1f;
    [Tooltip("图上最多保留多少个点")]
    [SerializeField] private int maxPoints = 50;
    [Tooltip("图的高度（像素）")]
    [SerializeField] private float graphHeight = 200f;
    [Tooltip("点与点的水平间距（像素）")]
    [SerializeField] private float pointSpacing = 34f;
    [Tooltip("曲线粗细（像素）。每个采样点会被放大成这么厚的横条，连起来就是一条线")]
    [SerializeField] private float pointThickness = 9f;

    [Header("颜色")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color warningColor = new Color(1f, 0.78f, 0.2f);
    [SerializeField] private Color dangerColor = new Color(0.91f, 0.27f, 0.24f);

    [Header("危险阈值")]
    [SerializeField] private float hrWarn = 100f, hrDanger = 130f;
    [SerializeField] private float tempWarnLow = 36f, tempDangerLow = 35f;
    [SerializeField] private float painWarn = 4f, painDanger = 7f;

    [Header("心率曲线量程")]
    [SerializeField] private float hrGraphMin = 50f;
    [SerializeField] private float hrGraphMax = 180f;

    // 内部状态
    private float sampleTimer;
    private System.Collections.Generic.List<RectTransform> points =
        new System.Collections.Generic.List<RectTransform>();
    private System.Collections.Generic.List<float> samples =
        new System.Collections.Generic.List<float>();
    private bool isBlinking;

    private void Start()
    {
        if (vitals == null)
        {
            vitals = FindAnyObjectByType<VitalsSimulator>();
            if (vitals == null)
            {
                Debug.LogWarning("[VitalsMonitor] 场景里找不到 VitalsSimulator，监护仪不会更新");
                return;
            }
        }

        vitals.OnVitalsChanged += HandleVitalsChanged;
        Debug.Log("[接线] VitalsSimulator → VitalsMonitor 已连接");
    }

    private void OnDestroy()
    {
        if (vitals != null) vitals.OnVitalsChanged -= HandleVitalsChanged;
    }

    private void Update()
    {
        // 危险时闪烁：用正弦波控制透明度
        if (isBlinking)
        {
            float a = Mathf.Lerp(0.35f, 1f, (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f);
            SetAllTextAlpha(a);
        }
    }

    private void HandleVitalsChanged(float hr, float temp, float pain)
    {
        if (heartRateText != null)
        {
            heartRateText.text = $"HR  {hr:F0}";
            heartRateText.color = PickColor(hr, hrWarn, hrDanger);
        }
        if (temperatureText != null)
        {
            temperatureText.text = $"T   {temp:F1}°C";
            temperatureText.color = PickColorLow(temp, tempWarnLow, tempDangerLow);
        }
        if (painText != null)
        {
            painText.text = $"PAIN {pain:F1}";
            painText.color = PickColor(pain, painWarn, painDanger);
        }

        isBlinking = (hr >= hrDanger) || (temp <= tempDangerLow) || (pain >= painDanger);
        if (!isBlinking) SetAllTextAlpha(1f);

        // 定时采样画曲线
        sampleTimer += Time.deltaTime;
        if (sampleTimer >= sampleInterval)
        {
            sampleTimer = 0f;
            AddSample(hr);
        }
    }

    // ---------- 折线图 ----------
    private void AddSample(float value)
    {
        if (graphContainer == null || pointPrefab == null) return;

        samples.Add(value);

        // 新建一个点
        var go = Instantiate(pointPrefab, graphContainer);
        go.SetActive(true);   // 模板本身是隐藏的，克隆出来的必须激活
        var rt = go.GetComponent<RectTransform>();
        points.Add(rt);

        // 超出上限就删掉最旧的（曲线向左滚动）
        while (samples.Count > maxPoints)
        {
            samples.RemoveAt(0);
            if (points.Count > 0)
            {
                Destroy(points[0].gameObject);
                points.RemoveAt(0);
            }
        }

        RedrawGraph();
    }

    /// <summary>
    /// 重画曲线：每个采样点放大成一段横条，首尾相接 → 视觉上连成一条连续的折线。
    /// 比离散的小圆点清晰得多，尤其在 0.9 米外的 HUD 上。
    /// </summary>
    private void RedrawGraph()
    {
        graphContainer.GetChild(0)?.SetAsFirstSibling();   // 基线垫到最底层

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] == null) continue;

            float normalized = Mathf.InverseLerp(hrGraphMin, hrGraphMax, samples[i]);
            float y = normalized * graphHeight;

            // 横条宽度 = 点间距 + 2 像素，保证相邻两段有重叠、无缝隙
            points[i].sizeDelta = new Vector2(pointSpacing + 2f, pointThickness);
            // 以左端为基准摆放，所以轴心在左侧
            points[i].pivot = new Vector2(0f, 0.5f);
            points[i].anchorMin = new Vector2(0f, 0f);
            points[i].anchorMax = new Vector2(0f, 0f);
            points[i].anchoredPosition = new Vector2(i * pointSpacing, y);

            // 颜色跟着心率走 —— 曲线飙红 = 视觉冲击
            var img = points[i].GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = PickColor(samples[i], hrWarn, hrDanger);
        }
    }

    // ---------- 工具 ----------
    private Color PickColor(float v, float warn, float danger)
    {
        if (v >= danger) return dangerColor;
        if (v >= warn) return warningColor;
        return normalColor;
    }

    /// <summary>体温是「越低越危险」，所以判断方向相反</summary>
    private Color PickColorLow(float v, float warn, float danger)
    {
        if (v <= danger) return dangerColor;
        if (v <= warn) return warningColor;
        return normalColor;
    }

    private void SetAllTextAlpha(float alpha)
    {
        SetAlpha(heartRateText, alpha);
        SetAlpha(temperatureText, alpha);
        SetAlpha(painText, alpha);
    }

    private void SetAlpha(TMP_Text t, float alpha)
    {
        if (t == null) return;
        var c = t.color;
        c.a = alpha;
        t.color = c;
    }
}
