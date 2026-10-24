using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 模拟事件管理器 —— 负责整条游戏流程。
///
/// 流程：
///   进入游戏 → 隐藏生命体征面板 → 显示事件选择屏幕
///   → 玩家选择某个事件 → 选择屏幕消失 → 生命体征面板显示
///   → 该事件对应的道具与人物出现 → 开始训练
///
/// 设计要点：
///   · 每个"模拟事件"是一个 Scenario 条目，包含：名称、说明、要显示的内容根物体
///   · 内容根物体（ScenarioContent）下面放该关卡的道具和人物
///   · 场景启动时所有内容根物体都是隐藏的，选择后才激活
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [Serializable]
    public class Scenario
    {
        [Tooltip("事件名称，显示在按钮上")]
        public string title = "膝关节扭伤";

        [Tooltip("一句话说明，显示在按钮下方")]
        [TextArea(1, 3)]
        public string description = "雪道摔倒，右膝无法负重";

        [Tooltip("选择这个事件后要显示的内容根物体（下面放该关卡的道具和人物）")]
        public GameObject contentRoot;

        [Tooltip("可选：这个事件对应的第一级训练步骤列表（留空则用 StepController 默认的）")]
        public List<FirstAidStep> steps;
    }

    [Header("界面引用")]
    [Tooltip("事件选择屏幕（启动时显示，选择后隐藏）")]
    [SerializeField] private GameObject selectionScreen;

    [Tooltip("生命体征监护仪面板（启动时隐藏，选择后显示）")]
    [SerializeField] private GameObject vitalsMonitorPanel;

    [Header("模拟事件列表")]
    [SerializeField] private List<Scenario> scenarios = new List<Scenario>();

    [Header("行为")]
    [Tooltip("启动时自动进入事件选择流程")]
    [SerializeField] private bool showSelectionOnStart = true;

    [Tooltip("选择事件后，把玩家传送到指定位置（留空则不传送）")]
    [SerializeField] private Transform playerStartPoint;

    [Header("调试")]
    [SerializeField] private bool logToConsole = true;

    /// <summary>玩家选择了某个事件</summary>
    public event Action<Scenario, int> OnScenarioSelected;

    /// <summary>当前选中的事件索引（-1 = 还没选）</summary>
    public int CurrentIndex { get; private set; } = -1;

    /// <summary>当前选中的事件</summary>
    public Scenario Current =>
        (CurrentIndex >= 0 && CurrentIndex < scenarios.Count) ? scenarios[CurrentIndex] : null;

    public IReadOnlyList<Scenario> Scenarios => scenarios;

    // ==================== 生命周期 ====================
    private void Start()
    {
        // 初始状态：隐藏所有关卡内容
        HideAllContent();

        if (showSelectionOnStart) EnterSelectionMode();
    }

    // ==================== 流程控制 ====================

    /// <summary>进入「选择事件」状态：显示选择屏幕，隐藏监护仪</summary>
    public void EnterSelectionMode()
    {
        if (selectionScreen != null) selectionScreen.SetActive(true);
        if (vitalsMonitorPanel != null) vitalsMonitorPanel.SetActive(false);

        HideAllContent();

        if (logToConsole) Debug.Log("[ScenarioManager] 进入事件选择模式");
    }

    /// <summary>按索引选择一个事件（供 UI 按钮调用）</summary>
    public void SelectScenario(int index)
    {
        if (index < 0 || index >= scenarios.Count)
        {
            Debug.LogWarning($"[ScenarioManager] 事件索引 {index} 超出范围（共 {scenarios.Count} 个）");
            return;
        }
        ApplyScenario(index);
    }

    /// <summary>按名称选择一个事件</summary>
    public void SelectScenarioByName(string title)
    {
        for (int i = 0; i < scenarios.Count; i++)
        {
            if (scenarios[i].title == title) { ApplyScenario(i); return; }
        }
        Debug.LogWarning($"[ScenarioManager] 找不到名为 '{title}' 的事件");
    }

    /// <summary>回到选择界面（重新开始）</summary>
    public void ReturnToSelection()
    {
        EnterSelectionMode();
    }

    // ==================== 内部 ====================
    private void ApplyScenario(int index)
    {
        CurrentIndex = index;
        var s = scenarios[index];

        // 1. 选择屏幕消失
        if (selectionScreen != null) selectionScreen.SetActive(false);

        // 2. 生命体征面板显示
        if (vitalsMonitorPanel != null) vitalsMonitorPanel.SetActive(true);

        // 3. 隐藏其他关卡内容，显示选中的这个
        HideAllContent();
        if (s.contentRoot != null)
        {
            s.contentRoot.SetActive(true);
            Debug.Log($"[ScenarioManager] 已显示关卡内容：{s.contentRoot.name}");
        }
        else
        {
            Debug.LogWarning($"[ScenarioManager] 事件「{s.title}」没有指定内容根物体");
        }

        // 4. 可选：把玩家传送到起始点
        if (playerStartPoint != null)
        {
            var teleporter = FindAnyObjectByType<PlayerTeleporter>();
            if (teleporter != null)
            {
                teleporter.SetTarget(playerStartPoint);
                teleporter.Teleport();
            }
        }

        // 5. 启动训练流程
        StartTraining(s);

        if (logToConsole)
            Debug.Log($"[ScenarioManager] 已选择事件 [{index}] {s.title} —— {s.description}");

        OnScenarioSelected?.Invoke(s, index);
    }

    /// <summary>重置各系统并开始训练</summary>
    private void StartTraining(Scenario s)
    {
        // 重置生理数值（回到初始状态）
        var vitals = FindAnyObjectByType<VitalsSimulator>();
        if (vitals != null)
        {
            vitals.ResetVitals();
            Debug.Log("[ScenarioManager] 生理数值已重置");
        }

        // 重置训练记录
        var logger = FindAnyObjectByType<EventLogger>();
        if (logger != null) logger.ResetLog();

        // 启动步骤
        var stepCtrl = FindAnyObjectByType<StepController>();
        if (stepCtrl != null)
        {
            if (s.steps != null && s.steps.Count > 0)
                stepCtrl.BeginTraining(s.steps);      // 用该事件专属的步骤
            else
                stepCtrl.BeginTraining();             // 沿用默认步骤
        }
        else
        {
            Debug.LogWarning("[ScenarioManager] 找不到 StepController，训练不会开始");
        }
    }

    private void HideAllContent()
    {
        foreach (var s in scenarios)
        {
            if (s.contentRoot != null) s.contentRoot.SetActive(false);
        }
    }
}
