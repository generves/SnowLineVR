using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 用对道具之后才显示 —— 玩家把指定道具用在指定部位上，这组物体才出现。
///
/// 本关卡的用法：
///   ① 夹板组（Splint (1)、Splint (2)、两根尼龙弹性带）
///        Required Item Tag = Splint
///        Allowed Parts     = 左大腿、左小腿
///      → 玩家用夹板处理左腿之后，腿上才出现夹板和绑带
///
///   ② 绷带组（绷带缠绕的模型）
///        Required Item Tag = Bandage
///        Allowed Parts     = 左大腿、左小腿
///      → 玩家用绷带包扎左腿之后，腿上才出现绷带
///
/// 原理：
///   订阅所有 PatientBodyPart 的 OnItemApplied 事件，
///   只有「道具标识匹配」+「部位在允许列表里」时才把目标物体 SetActive(true)。
///
/// 为什么不用 Start 里订阅一次就够了：
///   关卡内容（角色、身体部位）是【选关之后才激活】的，
///   FindObjectsByType 默认不包含未激活物体 → 会漏订阅。
///   所以改成每 0.5 秒补扫一次（和 StepActionBridge 同样的处理）。
///
/// 设置建议：
///   ⚠️ 把 Targets 里的物体在 Hierarchy 里【手动取消激活】（不打勾）。
///      只靠 Hide On Start 的话，第一帧可能闪一下。
/// </summary>
public class RevealOnItemApplied : MonoBehaviour
{
    [Header("要显示的东西")]
    [Tooltip("默认隐藏、满足条件后才显示的物体。\n" +
             "建议在 Hierarchy 里先手动取消激活，避免第一帧闪烁")]
    public GameObject[] targets;

    [Header("触发条件")]
    [Tooltip("需要哪个道具（对应 ItemTag.tagName），例如 Splint / Bandage。留空 = 任何道具")]
    public string requiredItemTag = "Splint";

    [Tooltip("勾上 = 只有【允许部位】里的部位才触发；取消 = 任何部位都触发")]
    public bool filterByPart = true;

    [Tooltip("允许触发的部位。用夹板时一般填 左大腿、左小腿")]
    public PatientBodyPart.BodyPart[] allowedParts = new[]
    {
        PatientBodyPart.BodyPart.左大腿,
        PatientBodyPart.BodyPart.左小腿,
    };

    [Header("行为")]
    [Tooltip("启动时先把 Targets 隐藏起来")]
    public bool hideOnStart = true;

    [Tooltip("只触发一次。取消 = 每次满足条件都会显示（已经显示了就无所谓）")]
    public bool oneShot = true;

    [Header("调试")]
    public bool logToConsole = true;

    // ---------- 内部 ----------
    private readonly List<PatientBodyPart> subscribed = new List<PatientBodyPart>();
    private float rescanTimer;
    private const float RescanInterval = 0.5f;

    private bool revealed;

    /// <summary>是否已经显示过</summary>
    public bool Revealed => revealed;

    private void Start()
    {
        if (hideOnStart) SetTargetsActive(false);

        RefreshSubscriptions();
    }

    private void Update()
    {
        // 已经显示过就不用再扫了
        if (oneShot && revealed) return;

        rescanTimer += Time.deltaTime;
        if (rescanTimer >= RescanInterval)
        {
            rescanTimer = 0f;
            RefreshSubscriptions();
        }
    }

    private void OnDisable()
    {
        foreach (var p in subscribed)
            if (p != null) p.OnItemApplied -= HandleItemApplied;

        subscribed.Clear();
    }

    /// <summary>
    /// 扫描场景里所有身体部位（含未激活的），把还没订阅的补上。
    /// </summary>
    private void RefreshSubscriptions()
    {
        foreach (var p in FindObjectsByType<PatientBodyPart>(FindObjectsInactive.Include))
        {
            if (p == null) continue;
            if (subscribed.Contains(p)) continue;

            p.OnItemApplied += HandleItemApplied;
            subscribed.Add(p);

            if (logToConsole)
                Debug.Log($"[显隐] 「{name}」订阅身体部位「{p.part}」", this);
        }
    }

    private void HandleItemApplied(PatientBodyPart part, string itemTag)
    {
        if (oneShot && revealed) return;
        if (part == null) return;

        // ---------- 道具是否匹配 ----------
        if (!string.IsNullOrEmpty(requiredItemTag) && itemTag != requiredItemTag)
            return;

        // ---------- 部位是否允许 ----------
        if (filterByPart && !IsAllowedPart(part.part))
        {
            if (logToConsole)
                Debug.Log($"[显隐] 「{name}」：道具对上了（{itemTag}），" +
                          $"但部位「{part.part}」不在允许列表里 → 不显示", this);
            return;
        }

        // ---------- 显示 ----------
        Reveal(part, itemTag);
    }

    private bool IsAllowedPart(PatientBodyPart.BodyPart p)
    {
        if (allowedParts == null || allowedParts.Length == 0) return true;   // 空 = 不限

        foreach (var a in allowedParts)
            if (a == p) return true;

        return false;
    }

    private void Reveal(PatientBodyPart part, string itemTag)
    {
        revealed = true;
        SetTargetsActive(true);

        if (logToConsole)
            Debug.Log($"[显隐] 「{name}」：用「{itemTag}」处理了「{part.part}」" +
                      $" → 显示 {CountValidTargets()} 个物体", this);
    }

    private int CountValidTargets()
    {
        int n = 0;
        if (targets == null) return 0;
        foreach (var t in targets) if (t != null) n++;
        return n;
    }

    private void SetTargetsActive(bool v)
    {
        if (targets == null) return;

        foreach (var t in targets)
        {
            if (t == null) continue;
            if (t.activeSelf == v) continue;
            t.SetActive(v);
        }
    }

    // ==================== 对外接口 ====================

    /// <summary>手动显示（供别的脚本调用，比如重新开始训练时）</summary>
    [ContextMenu("手动显示")]
    public void Show()
    {
        revealed = true;
        SetTargetsActive(true);
        if (logToConsole) Debug.Log($"[显隐] 「{name}」手动显示", this);
    }

    /// <summary>手动隐藏（回到初始状态）</summary>
    [ContextMenu("手动隐藏")]
    public void Hide()
    {
        revealed = false;
        SetTargetsActive(false);
        if (logToConsole) Debug.Log($"[显隐] 「{name}」手动隐藏", this);
    }

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(requiredItemTag)) requiredItemTag = "Splint";
    }
}
