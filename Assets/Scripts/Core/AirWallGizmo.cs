using UnityEngine;

/// <summary>
/// 空气墙可视化 —— 在 Scene 视图里画出碰撞体的线框，让不可见的墙"看得见"。
///
/// 挂在空气墙上即可。它只影响 Scene 视图的显示，不影响运行时渲染，
/// 所以在 Game 视图和头显里这套墙依然是不可见的。
///
/// 用法：把这个组件挂到空气墙上（或用 工具 → 空气墙可视化 批量添加）。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class AirWallGizmo : MonoBehaviour
{
    [Tooltip("线框颜色")]
    public Color gizmoColor = new Color(0.2f, 0.85f, 1f, 0.6f);

    [Tooltip("是否显示实体半透明面（更容易看清墙的范围）")]
    public bool showSolid = true;

    private void OnDrawGizmos()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;

        // 画线框
        Gizmos.DrawWireCube(col.center, col.size);

        // 画半透明实体（更直观地看出墙的厚度和范围）
        if (showSolid)
        {
            var solid = gizmoColor;
            solid.a = 0.12f;
            Gizmos.color = solid;
            Gizmos.DrawCube(col.center, col.size);
        }
    }

    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);   // 选中时变亮黄
        Gizmos.DrawWireCube(col.center, col.size);
    }
}
