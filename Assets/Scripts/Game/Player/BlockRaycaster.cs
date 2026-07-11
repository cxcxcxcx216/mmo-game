using UnityEngine;
using Minecraft.Game.World;

namespace Minecraft.Game.Player
{
    /// <summary>
    /// 方块射线命中信息。记录射线击中方块的位置、法线、类型和距离。
    /// 命中法线指向射线来源方向（与射线行进方向相反），用于确定放置方块的位置。
    /// </summary>
    public struct BlockRaycastHit
    {
        /// <summary>命中方块的世界整数坐标。</summary>
        public Vector3Int BlockPosition;

        /// <summary>命中面的法线（指向射线来源方向），用于确定相邻放置位置。</summary>
        public Vector3Int Normal;

        /// <summary>命中方块的类型。</summary>
        public BlockType BlockType;

        /// <summary>射线起点到命中点的距离（世界单位）。</summary>
        public float Distance;
    }

    /// <summary>
    /// 方块射线检测器。使用 DDA（Digital Differential Analyzer，数字微分分析器）算法
    /// 在方块网格中进行射线遍历，返回第一个被击中的固体方块。
    /// <para>
    /// 为什么不用 <see cref="Physics.Raycast"/>？
    /// 因为方块世界不使用 Unity Collider（性能考虑——一个区块就有上万个方块），
    /// 而是通过 <see cref="ChunkManager.GetBlock"/> 直接查询方块数据。
    /// DDA 算法沿射线逐格前进，每步只做一次方块查询，效率远高于 Physics.Raycast。
    /// </para>
    /// <para>
    /// DDA 算法原理（Amanatides &amp; Woo, 1987）：
    /// 1. 将起点转换为方块网格坐标（Floor 取整）。
    /// 2. 计算射线在每个轴方向上跨越一个方块的所需距离（tDelta）。
    /// 3. 计算射线到下一个方块边界的距离（tMax）。
    /// 4. 每次沿 tMax 最小的轴前进一个方块，直到命中固体方块或超过最大距离。
    /// </para>
    /// </summary>
    public static class BlockRaycaster
    {
        /// <summary>
        /// 从指定起点沿指定方向发射射线，返回第一个固体方块的信息。
        /// </summary>
        /// <param name="origin">射线起点（世界坐标）。</param>
        /// <param name="direction">射线方向（无需归一化，方法内部会处理）。</param>
        /// <param name="maxDistance">射线最大检测距离（世界单位）。</param>
        /// <param name="chunkManager">区块管理器，用于查询方块数据。</param>
        /// <param name="hit">输出：命中信息。未命中时为默认值。</param>
        /// <returns>是否命中固体方块。</returns>
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
            ChunkManager chunkManager, out BlockRaycastHit hit)
        {
            hit = default;

            if (chunkManager == null)
                return false;

            // 归一化方向向量，使 t 值直接对应世界距离
            direction = direction.normalized;

            // 处理零方向（无法发射射线）
            if (direction.sqrMagnitude < 1e-6f)
                return false;

            // ==================== 初始化 DDA 参数 ====================

            // 当前所在方块的整数坐标（起点所在的方块）
            int x = Mathf.FloorToInt(origin.x);
            int y = Mathf.FloorToInt(origin.y);
            int z = Mathf.FloorToInt(origin.z);

            // 各轴步进方向：+1（正方向）或 -1（负方向），方向分量为 0 时步进为 0
            int stepX = direction.x > 0 ? 1 : (direction.x < 0 ? -1 : 0);
            int stepY = direction.y > 0 ? 1 : (direction.y < 0 ? -1 : 0);
            int stepZ = direction.z > 0 ? 1 : (direction.z < 0 ? -1 : 0);

            // tMax：射线从起点到下一个方块边界的距离
            // tDelta：射线跨越一个完整方块所需的距离（= 1 / |方向分量|）
            float tMaxX, tMaxY, tMaxZ;
            float tDeltaX, tDeltaY, tDeltaZ;

            ComputeAxisParams(origin.x, direction.x, stepX, out tMaxX, out tDeltaX);
            ComputeAxisParams(origin.y, direction.y, stepY, out tMaxY, out tDeltaY);
            ComputeAxisParams(origin.z, direction.z, stepZ, out tMaxZ, out tDeltaZ);

            // 命中面的法线（指向射线来源方向，即与步进方向相反）
            // 初始为零向量（起点所在方块无"进入面"）
            Vector3Int normal = Vector3Int.zero;

            // 当前射线行进距离
            float t = 0f;

            // ==================== DDA 主循环 ====================

            while (t <= maxDistance)
            {
                // 检查当前方块是否为固体
                BlockType blockType = chunkManager.GetBlock(x, y, z);
                if (BlockDefinition.IsSolid(blockType))
                {
                    hit = new BlockRaycastHit
                    {
                        BlockPosition = new Vector3Int(x, y, z),
                        Normal = normal,
                        BlockType = blockType,
                        Distance = t
                    };
                    return true;
                }

                // 沿 tMax 最小的轴前进一个方块
                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    // X 轴前进
                    x += stepX;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    normal = new Vector3Int(-stepX, 0, 0);
                }
                else if (tMaxY < tMaxZ)
                {
                    // Y 轴前进
                    y += stepY;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    normal = new Vector3Int(0, -stepY, 0);
                }
                else
                {
                    // Z 轴前进
                    z += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    normal = new Vector3Int(0, 0, -stepZ);
                }
            }

            // 超过最大距离仍未命中
            return false;
        }

        // ==================== 内部工具 ====================

        /// <summary>
        /// 计算单个轴的 DDA 参数（tMax 和 tDelta）。
        /// <para>
        /// tMax：从起点到该轴下一个方块边界的距离。
        /// 当方向为正时，下一个边界是 floor(origin) + 1；
        /// 当方向为负时，下一个边界是 floor(origin) 本身。
        /// </para>
        /// <para>
        /// tDelta：跨越一个完整方块所需的距离，等于 1 / |方向分量|。
        /// </para>
        /// </summary>
        /// <param name="originComponent">起点在该轴的分量。</param>
        /// <param name="directionComponent">方向在该轴的分量。</param>
        /// <param name="step">该轴的步进方向（+1 或 -1 或 0）。</param>
        /// <param name="tMax">输出：到下一个边界的距离。</param>
        /// <param name="tDelta">输出：跨越一个方块的距离。</param>
        private static void ComputeAxisParams(float originComponent, float directionComponent,
            int step, out float tMax, out float tDelta)
        {
            if (step != 0)
            {
                // 当前方块的整数坐标
                int voxel = Mathf.FloorToInt(originComponent);
                // 下一个方块边界坐标（正方向走取 voxel+1，负方向走取 voxel）
                float boundary = step > 0 ? (voxel + 1) : voxel;
                // tMax = (边界 - 起点) / 方向
                tMax = (boundary - originComponent) / directionComponent;
                // tDelta = 1 / |方向|
                tDelta = Mathf.Abs(1f / directionComponent);
            }
            else
            {
                // 该轴方向为零，射线永远不会跨越此轴的方块边界
                tMax = float.MaxValue;
                tDelta = 0f;
            }
        }
    }
}
