using UnityEngine;

/// <summary>
/// 胶囊网格生成器 —— 程序化生成一个单位尺寸的胶囊体。
///
/// 为什么要它：
///   Unity 内置图元（Cube / Sphere / Capsule / Cylinder / Plane / Quad）
///   里【没有胶囊体】—— 虽然物理碰撞体有 CapsuleCollider，
///   但可视化用的网格没有。
///   所以高亮外壳只能用代码生成。
///
/// 生成的胶囊规格（单位尺寸）：
///   · 总高 1.0（Y 轴从 -0.5 到 +0.5）
///   · 半径 0.5（X/Z 从 -0.5 到 +0.5）
///   · 中心和轴心都在原点
///
/// 用法：把它当成圆柱体来缩放 —— 因为胶囊在 Y 方向"两端各多出半径的长度"，
///       所以代码里要按 (radius, height/2 - radius, radius) 的比例缩放。
/// </summary>
public static class CapsuleMeshFactory
{
    private const int LongitudeSegments = 24;   // 圆周分段
    private const int LatitudeSegments = 8;     // 半球分段

    private static Mesh cached;

    /// <summary>取得单位胶囊网格（带缓存，重复调用不会重复生成）</summary>
    public static Mesh Get()
    {
        if (cached != null) return cached;

        cached = Build();
        cached.name = "ProceduralCapsule";
        return cached;
    }

    // ==================== 生成 ====================
    private static Mesh Build()
    {
        var vertices = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var triangles = new System.Collections.Generic.List<int>();

        const float radius = 0.5f;
        const float cylinderHalf = 0.25f;   // 圆柱段半高（总高 1 = 0.25*2 + 0.5*2）

        // ---------- 1. 圆柱段 ----------
        // 沿 Y 轴从 -cylinderHalf 到 +cylinderHalf
        int cylStart = vertices.Count;

        for (int y = 0; y <= 1; y++)
        {
            float yPos = (y == 0) ? -cylinderHalf : cylinderHalf;
            for (int lon = 0; lon <= LongitudeSegments; lon++)
            {
                float u = lon / (float)LongitudeSegments;
                float angle = u * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                vertices.Add(new Vector3(cos * radius, yPos, sin * radius));
                normals.Add(new Vector3(cos, 0f, sin));
                uvs.Add(new Vector2(u, y * 0.5f));
            }
        }

        int ringSize = LongitudeSegments + 1;
        for (int lon = 0; lon < LongitudeSegments; lon++)
        {
            int a = cylStart + lon;
            int b = cylStart + lon + 1;
            int c = cylStart + ringSize + lon;
            int d = cylStart + ringSize + lon + 1;

            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(b); triangles.Add(c); triangles.Add(d);
        }

        // ---------- 2. 上半球 ----------
        AppendHemisphere(vertices, normals, uvs, triangles,
            centerY: cylinderHalf, isTop: true, radius, LongitudeSegments, LatitudeSegments);

        // ---------- 3. 下半球 ----------
        AppendHemisphere(vertices, normals, uvs, triangles,
            centerY: -cylinderHalf, isTop: false, radius, LongitudeSegments, LatitudeSegments);

        // ---------- 4. 组装 ----------
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        return mesh;
    }

    // ==================== 半球 ====================
    private static void AppendHemisphere(
        System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<int> triangles,
        float centerY, bool isTop, float radius,
        int longitudeSegments, int latitudeSegments)
    {
        int startIndex = vertices.Count;

        // 顶点：纬度 0 = 赤道，纬度 latitudeSegments = 极点
        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = lat / (float)latitudeSegments;
            float phi = v * Mathf.PI * 0.5f;          // 0 → 90 度
            float ringRadius = Mathf.Cos(phi) * radius;
            float yOffset = Mathf.Sin(phi) * radius;
            float yPos = isTop ? centerY + yOffset : centerY - yOffset;

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = lon / (float)longitudeSegments;
                float angle = u * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                Vector3 pos = new Vector3(cos * ringRadius, yPos, sin * ringRadius);
                Vector3 nrm = new Vector3(cos * Mathf.Cos(phi), isTop ? Mathf.Sin(phi) : -Mathf.Sin(phi), sin * Mathf.Cos(phi));

                vertices.Add(pos);
                normals.Add(nrm.normalized);
                uvs.Add(new Vector2(u, v));
            }
        }

        int ringSize = longitudeSegments + 1;

        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int a = startIndex + lat * ringSize + lon;
                int b = a + 1;
                int c = a + ringSize;
                int d = c + 1;

                if (isTop)
                {
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
                else
                {
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
        }
    }
}
