using System.Collections.Generic;
using UnityEngine;

namespace Minecraft.Game.World
{
    /// <summary>
    /// 区块 Mesh 构建器。将区块方块数据转换为 Unity 可渲染的 Mesh。
    /// 核心思路：遍历区块所有方块，对每个非空气方块检查 6 个面，
    /// 仅当相邻方块是空气或透明时才生成该面的几何数据（面剔除）。
    /// 顶点使用世界坐标，避免运行时变换；颜色使用 Color32 节省内存。
    /// </summary>
    public static class ChunkMeshBuilder
    {
        // ==================== 面索引常量 ====================

        /// <summary>面索引：顶面（+Y 方向）。</summary>
        private const int FaceTop = 0;
        /// <summary>面索引：底面（-Y 方向）。</summary>
        private const int FaceBottom = 1;
        /// <summary>面索引：北面（+Z 方向）。</summary>
        private const int FaceNorth = 2;
        /// <summary>面索引：南面（-Z 方向）。</summary>
        private const int FaceSouth = 3;
        /// <summary>面索引：东面（+X 方向）。</summary>
        private const int FaceEast = 4;
        /// <summary>面索引：西面（-X 方向）。</summary>
        private const int FaceWest = 5;
        /// <summary>面的总数（6 个方向）。</summary>
        private const int FaceCount = 6;

        // ==================== 静态面数据 ====================

        /// <summary>
        /// 每个面的 4 个顶点偏移量（相对于方块最小角 (x,y,z)）。
        /// 顶点顺序保证三角形索引 0,1,2,0,2,3 为顺时针正面。
        /// 索引顺序：Top, Bottom, North, South, East, West。
        /// </summary>
        private static readonly Vector3[][] FaceVertices = new Vector3[FaceCount][];

        /// <summary>
        /// 每个面的法线方向。
        /// </summary>
        private static readonly Vector3[] FaceNormals = new Vector3[FaceCount]
        {
            new Vector3(0f, 1f, 0f),   // Top
            new Vector3(0f, -1f, 0f),  // Bottom
            new Vector3(0f, 0f, 1f),   // North
            new Vector3(0f, 0f, -1f),  // South
            new Vector3(1f, 0f, 0f),   // East
            new Vector3(-1f, 0f, 0f),  // West
        };

        /// <summary>
        /// 每个面的相邻方块偏移量（用于面剔除时查询邻接方块）。
        /// </summary>
        private static readonly Vector3Int[] FaceNeighborOffsets = new Vector3Int[FaceCount]
        {
            new Vector3Int(0, 1, 0),   // Top → 检查上方
            new Vector3Int(0, -1, 0),  // Bottom → 检查下方
            new Vector3Int(0, 0, 1),   // North → 检查 +Z 方向
            new Vector3Int(0, 0, -1),  // South → 检查 -Z 方向
            new Vector3Int(1, 0, 0),   // East → 检查 +X 方向
            new Vector3Int(-1, 0, 0),  // West → 检查 -X 方向
        };

        /// <summary>
        /// 每个面的环境光照系数（模拟简易 AO 效果）。
        /// 顶面最亮（1.0），底面最暗（0.5），侧面中等（0.8）。
        /// </summary>
        private static readonly float[] FaceAmbient = new float[FaceCount]
        {
            1.0f,   // Top
            0.5f,   // Bottom
            0.8f,   // North
            0.8f,   // South
            0.8f,   // East
            0.8f,   // West
        };

        /// <summary>
        /// 静态构造函数。初始化各面的顶点偏移数据。
        /// 顶点定义参照 Minecraft 标准立方体面布局。
        /// </summary>
        static ChunkMeshBuilder()
        {
            // Top (y+1): (x,y+1,z) (x,y+1,z+1) (x+1,y+1,z+1) (x+1,y+1,z)
            FaceVertices[FaceTop] = new Vector3[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 1f, 1f),
                new Vector3(1f, 1f, 1f),
                new Vector3(1f, 1f, 0f),
            };

            // Bottom (y): (x,y,z) (x+1,y,z) (x+1,y,z+1) (x,y,z+1)
            FaceVertices[FaceBottom] = new Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, 1f),
                new Vector3(0f, 0f, 1f),
            };

            // North (z+1): (x,y,z+1) (x+1,y,z+1) (x+1,y+1,z+1) (x,y+1,z+1)
            FaceVertices[FaceNorth] = new Vector3[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 1f, 1f),
                new Vector3(0f, 1f, 1f),
            };

            // South (z): (x,y,z) (x,y+1,z) (x+1,y+1,z) (x+1,y,z)
            FaceVertices[FaceSouth] = new Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(1f, 0f, 0f),
            };

            // East (x+1): (x+1,y,z) (x+1,y+1,z) (x+1,y+1,z+1) (x+1,y,z+1)
            FaceVertices[FaceEast] = new Vector3[]
            {
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(1f, 1f, 1f),
                new Vector3(1f, 0f, 1f),
            };

            // West (x): (x,y,z) (x,y,z+1) (x,y+1,z+1) (x,y+1,z)
            FaceVertices[FaceWest] = new Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 1f, 1f),
                new Vector3(0f, 1f, 0f),
            };
        }

        // ==================== 公开方法 ====================

        /// <summary>
        /// 构建区块的固体方块 Mesh。
        /// 遍历所有非空气方块，对每个暴露的面生成顶点、三角形、颜色和法线。
        /// 透明方块（水、玻璃、树叶）不包含在此 Mesh 中，需另行调用 <see cref="BuildTransparentMesh"/>。
        /// </summary>
        /// <param name="chunk">要构建的区块数据。</param>
        /// <param name="chunkManager">区块管理器，用于跨区块查询邻接方块。</param>
        /// <param name="mesh">目标 Mesh，将被清空并填充新数据。</param>
        public static void BuildMesh(Chunk chunk, ChunkManager chunkManager, Mesh mesh)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            BuildMeshInternal(chunk, chunkManager, vertices, triangles, colors, normals, uvs,
                buildTransparent: false, out _);

            SubmitMesh(mesh, vertices, triangles, colors, normals, uvs);
        }

        /// <summary>
        /// 构建区块的固体方块 Mesh，并报告是否包含透明方块。
        /// 若 <paramref name="hasTransparentFaces"/> 为 true，调用方应额外调用
        /// <see cref="BuildTransparentMesh"/> 构建透明面 Mesh。
        /// </summary>
        /// <param name="chunk">要构建的区块数据。</param>
        /// <param name="chunkManager">区块管理器。</param>
        /// <param name="mesh">目标 Mesh。</param>
        /// <param name="hasTransparentFaces">输出：区块是否包含需要单独渲染的透明方块。</param>
        public static void BuildMesh(Chunk chunk, ChunkManager chunkManager, Mesh mesh,
            out bool hasTransparentFaces)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            BuildMeshInternal(chunk, chunkManager, vertices, triangles, colors, normals, uvs,
                buildTransparent: false, out hasTransparentFaces);

            SubmitMesh(mesh, vertices, triangles, colors, normals, uvs);
        }

        /// <summary>
        /// 单独构建区块的透明方块 Mesh（水、玻璃、树叶）。
        /// 同类型的透明方块相邻时不生成面（避免水面或玻璃内部出现重复面）。
        /// 透明方块与固体方块相邻时也不生成面（固体方块的面对已由 <see cref="BuildMesh"/> 生成）。
        /// </summary>
        /// <param name="chunk">要构建的区块数据。</param>
        /// <param name="chunkManager">区块管理器。</param>
        /// <param name="mesh">目标 Mesh。</param>
        public static void BuildTransparentMesh(Chunk chunk, ChunkManager chunkManager, Mesh mesh)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            BuildMeshInternal(chunk, chunkManager, vertices, triangles, colors, normals, uvs,
                buildTransparent: true, out _);

            SubmitMesh(mesh, vertices, triangles, colors, normals, uvs);
        }

        // ==================== 内部实现 ====================

        /// <summary>
        /// Mesh 构建核心逻辑。
        /// 遍历区块所有方块，根据 <paramref name="buildTransparent"/> 选择构建固体面或透明面。
        /// 遍历顺序 y → z → x，与数组布局一致，缓存友好。
        /// </summary>
        /// <param name="chunk">区块数据。</param>
        /// <param name="chunkManager">区块管理器。</param>
        /// <param name="vertices">顶点列表（输出）。</param>
        /// <param name="triangles">三角形索引列表（输出）。</param>
        /// <param name="colors">顶点颜色列表（输出）。</param>
        /// <param name="normals">法线列表（输出）。</param>
        /// <param name="uvs">UV 坐标列表（输出）。</param>
        /// <param name="buildTransparent">true 构建透明方块面，false 构建固体方块面。</param>
        /// <param name="hasTransparentFaces">输出：是否遇到透明方块（仅在 buildTransparent=false 时有意义）。</param>
        private static void BuildMeshInternal(Chunk chunk, ChunkManager chunkManager,
            List<Vector3> vertices, List<int> triangles,
            List<Color32> colors, List<Vector3> normals, List<Vector2> uvs,
            bool buildTransparent, out bool hasTransparentFaces)
        {
            hasTransparentFaces = false;

            // 区块在世界坐标的偏移量（顶点直接使用世界坐标，避免运行时变换）
            int offsetX = chunk.ChunkX * Chunk.Width;
            int offsetZ = chunk.ChunkZ * Chunk.Depth;

            // 直接访问底层数组，避免每次调用 GetBlock 的边界检查开销
            byte[] blocks = chunk.GetRawData();

            for (int y = 0; y < Chunk.Height; y++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    for (int x = 0; x < Chunk.Width; x++)
                    {
                        BlockType type = (BlockType)blocks[Chunk.GetIndex(x, y, z)];
                        if (type == BlockType.Air)
                            continue;

                        ref BlockInfo info = ref BlockDefinition.Get(type);

                        if (info.IsTransparent)
                        {
                            // 标记存在透明方块，供调用方决定是否构建透明 Mesh
                            hasTransparentFaces = true;
                            // 构建固体 Mesh 时跳过透明方块
                            if (!buildTransparent)
                                continue;
                        }
                        else
                        {
                            // 构建透明 Mesh 时跳过固体方块
                            if (buildTransparent)
                                continue;
                        }

                        // 检查 6 个面
                        for (int face = 0; face < FaceCount; face++)
                        {
                            Vector3Int neighborOffset = FaceNeighborOffsets[face];
                            int nx = x + neighborOffset.x;
                            int ny = y + neighborOffset.y;
                            int nz = z + neighborOffset.z;

                            // 获取相邻方块（支持跨区块查询）
                            BlockType neighborType = GetNeighborBlock(chunk, chunkManager, nx, ny, nz);

                            // 面剔除判断
                            if (!ShouldRenderFace(type, neighborType, buildTransparent))
                                continue;

                            // 添加该面的几何数据
                            AddFace(vertices, triangles, colors, normals, uvs,
                                x, y, z, offsetX, offsetZ, face, info, type);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 获取相邻方块类型。
        /// 若坐标在当前区块范围内，直接从区块读取（快速路径）；
        /// 否则通过 ChunkManager 以世界坐标查询（跨区块边界）。
        /// </summary>
        /// <param name="chunk">当前区块。</param>
        /// <param name="chunkManager">区块管理器。</param>
        /// <param name="nx">相邻方块的局部 X 坐标。</param>
        /// <param name="ny">相邻方块的局部 Y 坐标。</param>
        /// <param name="nz">相邻方块的局部 Z 坐标。</param>
        /// <returns>相邻方块的类型，越界或区块未加载时返回 Air。</returns>
        private static BlockType GetNeighborBlock(Chunk chunk, ChunkManager chunkManager,
            int nx, int ny, int nz)
        {
            // 在当前区块内：直接读取，无需跨区块查询
            if (Chunk.IsInBounds(nx, ny, nz))
            {
                return chunk.GetBlock(nx, ny, nz);
            }

            // 跨区块查询：将局部坐标转换为世界坐标
            int worldX = chunk.ChunkX * Chunk.Width + nx;
            int worldZ = chunk.ChunkZ * Chunk.Depth + nz;
            return chunkManager.GetBlock(worldX, ny, worldZ);
        }

        /// <summary>
        /// 判断某面是否应该渲染（面剔除逻辑）。
        /// 规则：只渲染暴露的面（相邻方块是空气或透明）。
        /// 构建透明 Mesh 时，同类透明方块之间不生成面。
        /// </summary>
        /// <param name="currentType">当前方块类型。</param>
        /// <param name="neighborType">相邻方块类型。</param>
        /// <param name="buildTransparent">是否正在构建透明 Mesh。</param>
        /// <returns>true 表示该面应该渲染。</returns>
        private static bool ShouldRenderFace(BlockType currentType, BlockType neighborType,
            bool buildTransparent)
        {
            ref BlockInfo neighborInfo = ref BlockDefinition.Get(neighborType);

            // 相邻是固体方块（非空气、非透明）：该面被遮挡，不渲染
            if (!neighborInfo.IsTransparent)
                return false;

            // 以下：相邻是空气或透明方块

            if (!buildTransparent)
            {
                // 构建固体 Mesh：相邻透明（含空气）则渲染该面
                return true;
            }

            // 构建透明 Mesh：同类透明方块之间不渲染（避免内部重复面）
            return neighborType != currentType;
        }

        /// <summary>
        /// 向列表中添加一个面的几何数据。
        /// 每个面包含：4 个顶点 + 2 个三角形 + 4 个颜色 + 4 个法线 + 4 个 UV。
        /// 顶点坐标为世界坐标（区块偏移 + 局部坐标 + 面顶点偏移）。
        /// </summary>
        /// <param name="vertices">顶点列表。</param>
        /// <param name="triangles">三角形索引列表。</param>
        /// <param name="colors">颜色列表。</param>
        /// <param name="normals">法线列表。</param>
        /// <param name="uvs">UV 坐标列表。</param>
        /// <param name="x">方块局部 X 坐标。</param>
        /// <param name="y">方块局部 Y 坐标。</param>
        /// <param name="z">方块局部 Z 坐标。</param>
        /// <param name="offsetX">区块世界 X 偏移。</param>
        /// <param name="offsetZ">区块世界 Z 偏移。</param>
        /// <param name="faceIndex">面索引（0-5）。</param>
        /// <param name="info">方块的属性信息。</param>
        /// <param name="type">方块类型（用于查询纹理 UV）。</param>
        private static void AddFace(List<Vector3> vertices, List<int> triangles,
            List<Color32> colors, List<Vector3> normals, List<Vector2> uvs,
            int x, int y, int z, int offsetX, int offsetZ,
            int faceIndex, BlockInfo info, BlockType type)
        {
            // 当前面的顶点起始索引（三角形索引基于此偏移）
            int startIndex = vertices.Count;

            // 方块最小角的世界坐标基准
            float bx = x + offsetX;
            float by = y;
            float bz = z + offsetZ;

            // 添加 4 个顶点（世界坐标）
            Vector3[] faceVerts = FaceVertices[faceIndex];
            vertices.Add(new Vector3(bx + faceVerts[0].x, by + faceVerts[0].y, bz + faceVerts[0].z));
            vertices.Add(new Vector3(bx + faceVerts[1].x, by + faceVerts[1].y, bz + faceVerts[1].z));
            vertices.Add(new Vector3(bx + faceVerts[2].x, by + faceVerts[2].y, bz + faceVerts[2].z));
            vertices.Add(new Vector3(bx + faceVerts[3].x, by + faceVerts[3].y, bz + faceVerts[3].z));

            // 添加 2 个三角形（顺时针为正面：0,1,2 和 0,2,3）
            triangles.Add(startIndex);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 3);

            // 添加 4 个顶点颜色（面颜色 × 环境光照系数）
            Color32 faceColor = info.GetFaceColor(faceIndex);
            Color32 shadedColor = ApplyAmbient(faceColor, FaceAmbient[faceIndex]);
            colors.Add(shadedColor);
            colors.Add(shadedColor);
            colors.Add(shadedColor);
            colors.Add(shadedColor);

            // 添加 4 个法线
            Vector3 normal = FaceNormals[faceIndex];
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            // 添加 4 个 UV 坐标（从纹理图集查询，顺序与顶点一致：0,1,2,3）
            Vector2[] faceUVs = TextureAtlasGenerator.GetUV(type, faceIndex);
            uvs.Add(faceUVs[0]);
            uvs.Add(faceUVs[1]);
            uvs.Add(faceUVs[2]);
            uvs.Add(faceUVs[3]);
        }

        /// <summary>
        /// 将环境光照系数应用到颜色上。
        /// 仅影响 RGB 通道，保留 Alpha 通道（透明度不受光照影响）。
        /// </summary>
        /// <param name="color">原始颜色。</param>
        /// <param name="factor">光照系数（0~1）。</param>
        /// <returns>应用光照后的颜色。</returns>
        private static Color32 ApplyAmbient(Color32 color, float factor)
        {
            return new Color32(
                (byte)(color.r * factor),
                (byte)(color.g * factor),
                (byte)(color.b * factor),
                color.a
            );
        }

        /// <summary>
        /// 将构建好的几何数据提交到 Mesh。
        /// 顺序：清空 → 顶点 → 三角形 → 颜色 → 法线 → UV → 重算边界。
        /// </summary>
        /// <param name="mesh">目标 Mesh。</param>
        /// <param name="vertices">顶点列表。</param>
        /// <param name="triangles">三角形索引列表。</param>
        /// <param name="colors">颜色列表。</param>
        /// <param name="normals">法线列表。</param>
        /// <param name="uvs">UV 坐标列表。</param>
        private static void SubmitMesh(Mesh mesh, List<Vector3> vertices, List<int> triangles,
            List<Color32> colors, List<Vector3> normals, List<Vector2> uvs)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
        }
    }
}
