using UnityEngine;
using TMPro;

/// <summary>
/// 路牌数字 —— 让路牌上显示 1~9 的随机数字。
///
/// 用法：
///   ① 挂在【路牌】根物体上（或者挂在 Canvas 上都行）
///   ② 把正反两面的 Text (TMP) 拖进【Texts】列表
///   ③ Play 时会随机出一个数字，正反面显示同一个数字
///
/// 为什么正反面要同一个数字：
///   路牌是个实体，正反两面写不同的数字在物理上说不通。
///
/// 为什么要随机：
///   防止学员背答案 —— 每次训练都要重新【读数字 → 报告位置】。
/// </summary>
public class TrailSignNumber : MonoBehaviour
{
    [Header("路牌上的文字")]
    [Tooltip("正反面的 Text (TMP) 都拖进来。运行时会让它们显示同一个数字")]
    public TMP_Text[] texts;

    [Header("数字范围")]
    [Tooltip("最小数字（含）")]
    public int minValue = 1;

    [Tooltip("最大数字（含）")]
    public int maxValue = 9;

    [Header("刷新时机")]
    [Tooltip("每次进入 Play 都重新随机一次")]
    public bool randomizeOnPlay = true;

    [Tooltip("用固定值（勾上后不随机，用来调试判定的）")]
    public bool useFixedValue = false;

    [Tooltip("固定值")]
    public int fixedValue = 3;

    [Header("手写感（可选）")]
    [Tooltip("给每个文字加一点随机旋转，看起来像手写的")]
    public bool jitterRotation = false;

    [Tooltip("最大随机旋转角度")]
    [Range(0f, 15f)]
    public float jitterAngle = 3f;

    [Header("调试")]
    public bool logToConsole = false;

    /// <summary>本次随机出来的数字</summary>
    public int CurrentValue { get; private set; }

    private void Start()
    {
        if (randomizeOnPlay)
        {
            Randomize();
        }
        else
        {
            Apply(fixedValue);
        }
    }

    /// <summary>重新随机一个数字</summary>
    public void Randomize()
    {
        int v = useFixedValue
            ? fixedValue
            : Random.Range(Mathf.Min(minValue, maxValue), Mathf.Max(minValue, maxValue) + 1);
        Apply(v);
    }

    /// <summary>设定指定数字</summary>
    public void Apply(int value)
    {
        CurrentValue = value;
        string s = value.ToString();

        if (texts == null || texts.Length == 0)
        {
            Debug.LogWarning($"[路牌数字] {name} 没有指定 Text (TMP)，数字不会显示");
            return;
        }

        foreach (var t in texts)
        {
            if (t == null) continue;
            t.text = s;

            if (jitterRotation)
            {
                t.rectTransform.localRotation = Quaternion.Euler(
                    Random.Range(-jitterAngle, jitterAngle),
                    Random.Range(-jitterAngle, jitterAngle),
                    Random.Range(-jitterAngle, jitterAngle));
            }
        }

        if (logToConsole)
            Debug.Log($"[路牌数字] {name} 显示数字 = {value}（共 {texts.Length} 个文字对象）");
    }
}
