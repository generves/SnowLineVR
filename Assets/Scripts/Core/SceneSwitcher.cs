using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// 训练场景切换器。
///
/// 按键：1 / 2 / 3 切换到对应的训练场景（配置见 numberKeyScenes）。
/// 按键刻意避开 XR Device Simulator 占用的那些
/// （模拟器占用 W A S D Q E Z X C V 1 2 3 T R H 空格 左Shift 左Ctrl Tab Esc \）
/// —— 注意：数字键 1/2/3 模拟器也占用（用于切换视角），但场景切换是低频操作，
///    且优先级更高，实测可正常使用。若发现冲突可改为 F1/F2/F3。
///
/// 架构说明（重要）：
///   本组件挂在【常驻层】的 GameManager 上，并设置为 DontDestroyOnLoad。
///   XR Rig、HUD 面板、EventSystem 同样属于常驻层，切换场景时不会被销毁。
///   加载训练场景时采用【Additive 叠加加载】，因此常驻层始终保持存在。
/// </summary>
public class SceneSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class SceneSlot
    {
        [Tooltip("按键编号（1 = 数字键 1）")]
        public int key = 1;

        [Tooltip("要加载的场景名（必须已加入 Build Settings）")]
        public string sceneName = "";

        [Tooltip("这个训练的显示名称，会打印到 Console")]
        public string displayName = "";
    }

    [Header("场景槽位")]
    [SerializeField]
    private List<SceneSlot> slots = new List<SceneSlot>
    {
        new SceneSlot { key = 1, sceneName = "", displayName = "训练场景 1" },
        new SceneSlot { key = 2, sceneName = "", displayName = "训练场景 2" },
        new SceneSlot { key = 3, sceneName = "", displayName = "训练场景 3" },
    };

    [Header("玩家复位")]
    [Tooltip("切换场景后把玩家传送回原点")]
    [SerializeField] private bool resetPlayerPosition = true;

    [Tooltip("常驻层（XR Rig / HUD）的根物体，切换场景后用它做复位基准")]
    [SerializeField] private Transform persistentRoot;

    [Header("调试")]
    [SerializeField] private bool logToConsole = true;

    /// <summary>当前已加载的训练场景名（空 = 未加载任何训练场景）</summary>
    public string CurrentTrainingScene { get; private set; } = "";

    /// <summary>当前训练场景的根物体（用于让其他系统重新接线）</summary>
    public GameObject CurrentTrainingRoot { get; private set; }

    /// <summary>场景切换完成时触发，参数是新的训练场景根物体</summary>
    public event System.Action<GameObject> OnTrainingSceneLoaded;

    private static readonly Dictionary<int, Key> KeyMap = new Dictionary<int, Key>
    {
        { 1, Key.Digit1 }, { 2, Key.Digit2 }, { 3, Key.Digit3 }, { 4, Key.Digit4 },
        { 5, Key.Digit5 }, { 6, Key.Digit6 }, { 7, Key.Digit7 }, { 8, Key.Digit8 },
        { 9, Key.Digit9 },
    };

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        foreach (var slot in slots)
        {
            if (string.IsNullOrEmpty(slot.sceneName)) continue;
            if (!KeyMap.TryGetValue(slot.key, out var key)) continue;

            var ctrl = kb[key];
            if (ctrl != null && ctrl.wasPressedThisFrame)
            {
                SwitchTo(slot);
                return;   // 一帧只处理一次切换
            }
        }
    }

    /// <summary>切换到指定槽位的场景</summary>
    public void SwitchTo(SceneSlot slot)
    {
        if (string.IsNullOrEmpty(slot.sceneName))
        {
            Debug.LogWarning($"[SceneSwitcher] 槽位 {slot.key} 没有配置场景名");
            return;
        }

        // 检查场景是否加入了 Build Settings
        if (SceneUtility.GetBuildIndexByScenePath(slot.sceneName) < 0 &&
            Application.CanStreamedLevelBeLoaded(slot.sceneName) == false)
        {
            Debug.LogError(
                $"[SceneSwitcher] 场景 '{slot.sceneName}' 没有加入 Build Settings，无法加载。\n" +
                "请到 File → Build Settings → 把场景拖进 Scenes In Build 列表。");
            return;
        }

        string label = string.IsNullOrEmpty(slot.displayName) ? slot.sceneName : slot.displayName;
        if (logToConsole) Debug.Log($"[SceneSwitcher] 切换到：{label}（{slot.sceneName}）");

        // 卸载旧训练场景
        if (!string.IsNullOrEmpty(CurrentTrainingScene))
        {
            var old = SceneManager.GetSceneByName(CurrentTrainingScene);
            if (old.isLoaded) SceneManager.UnloadSceneAsync(old);
        }

        // 叠加加载新场景（常驻层不受影响）
        var op = SceneManager.LoadSceneAsync(slot.sceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError($"[SceneSwitcher] 加载场景 '{slot.sceneName}' 失败");
            return;
        }

        op.completed += _ =>
        {
            CurrentTrainingScene = slot.sceneName;

            var scene = SceneManager.GetSceneByName(slot.sceneName);
            CurrentTrainingRoot = FindSceneRoot(scene);

            if (resetPlayerPosition) ResetPlayer();

            OnTrainingSceneLoaded?.Invoke(CurrentTrainingRoot);
            if (logToConsole)
                Debug.Log($"[SceneSwitcher] 场景已加载：{slot.sceneName}，根物体 = " +
                          (CurrentTrainingRoot != null ? CurrentTrainingRoot.name : "(无)"));
        };
    }

    /// <summary>卸载当前训练场景（回到纯常驻层状态）</summary>
    public void UnloadTrainingScene()
    {
        if (string.IsNullOrEmpty(CurrentTrainingScene)) return;

        var s = SceneManager.GetSceneByName(CurrentTrainingScene);
        if (s.isLoaded) SceneManager.UnloadSceneAsync(s);

        CurrentTrainingScene = "";
        CurrentTrainingRoot = null;
        OnTrainingSceneLoaded?.Invoke(null);
    }

    /// <summary>把玩家复位到常驻层原点</summary>
    private void ResetPlayer()
    {
        if (persistentRoot == null) return;

        var xrOrigin = persistentRoot.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true);
        if (xrOrigin != null)
        {
            // 复位 XR Origin 根节点的位置（不要动相机本身，会破坏头显追踪）
            xrOrigin.transform.position = Vector3.zero;

            // 同时把地板偏移物体归零，避免上次的楼层偏移残留
            // 注意：这个属性在 XROrigin 上叫 CameraFloorOffsetObject（GameObject 类型）
            var floorOffset = xrOrigin.CameraFloorOffsetObject;
            if (floorOffset != null) floorOffset.transform.localPosition = Vector3.zero;

            Debug.Log("[SceneSwitcher] 玩家已复位到原点");
        }
    }

    /// <summary>在新加载的场景里找根物体（优先找带 StepController 的）</summary>
    private static GameObject FindSceneRoot(Scene scene)
    {
        if (!scene.IsValid()) return null;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<StepController>(true) != null) return root;
        }

        var roots = scene.GetRootGameObjects();
        return roots.Length > 0 ? roots[0] : null;
    }
}
