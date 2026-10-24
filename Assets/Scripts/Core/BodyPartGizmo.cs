using UnityEngine;

/// <summary>
/// 身体部位可视化 —— 在 Scene 视图里画出碰撞体线框，方便调整尺寸。
///
/// 挂在 BodyPart_xxx 物体上（BodyPartSetupTool 会自动加），
/// 或者手动添加。
///
/// 只在 Scene 视图显示，不影响运行时渲染和性能。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BodyPartGizmo : MonoBehaviour
{
    [Tooltip("线框颜色")]
    public Color gizmoColor = new Color(0.2f, 0.9f, 0.4f, 0.7f);

    [Tooltip("是否显示半透明实体（更容易看出覆盖范围）")]
    public bool showSolid = true;

    [Tooltip("是否在游戏运行时也显示（默认只在编辑器里显示）")]
    public bool showInPlayMode = false;

    private void OnDrawGizmos()
    {
        // 运行时是否显示
        if (Application.isPlaying && !showInPlayMode) return;

        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        // 线框
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(col.center, col.size);

        // 半透明实体
        if (showSolid)
        {
            var solid = gizmoColor;
            solid.a = 0.10f;
            Gizmos.color = solid;
            Gizmos.DrawCube(col.center, col.size);
        }
    }

    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);   // 选中时亮黄
        Gizmos.DrawWireCube(col.center, col.size);

        // 再画一个稍大的框，方便看清
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
        Gizmos.DrawWireCube(col.center, col.size * 1.02f);
    }
}
