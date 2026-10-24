using UnityEngine;

/// <summary>
/// 道具标识 —— 挂在可抓取的急救道具上，用来标记"这是什么道具"。
///
/// 用途：
///   PatientBodyPart 和 DropZone 都需要知道"玩家手上拿的是什么"，
///   靠的就是这个组件上的 tagName。
///
/// 命名约定（要和 PatientBodyPart.IsItemValidForPart 里的规则对应）：
///   Splint    → 夹板
///   IcePack   → 冰袋
///   Blanket   → 保温毯
///   Mat       → 隔热垫
///   Bandage   → 绷带
///   Phone     → 手机
///   WarningSign → 警示标志
///   Gloves    → 手套
/// </summary>
public class ItemTag : MonoBehaviour
{
    [Tooltip("道具标识名。要和交互规则里用的名字一致")]
    public string tagName = "";

    [Tooltip("中文显示名（给玩家看的提示里用）")]
    public string displayName = "";

    /// <summary>返回用于显示的名字：优先用中文名，没有就用标识名</summary>
    public string DisplayName =>
        string.IsNullOrEmpty(displayName) ? tagName : displayName;

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(tagName))
            displayName = tagName;
    }
}
