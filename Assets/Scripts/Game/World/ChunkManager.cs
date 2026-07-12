using System.Collections.Generic;
using UnityEngine;

namespace Minecraft.Game.World
{
    /// <summary>
    /// 区块管理器。管理所有已加载区块的创建、查询、卸载。
    /// 作为 MonoBehaviour 挂载到场景，但 Update 不做任何事——
    /// 实际的区块加载调度由 ChunkLoadSystem（ECS 系统）驱动调用。
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        /// <summary>默认渲染半径（单位：区块）。玩家周围此半径内的区块会被加载。</summary>
        [SerializeField] private int _renderRadius = 8;

        /// <summary>默认地形生成种子（用于在未注入 TerrainProvider 时创建默认生成器）。</summary>
        [SerializeField] private int _seed = 1337;

        /// <summary>已加载区块字典，键为 (chunkX, chunkZ) 元组。</summary>
        private readonly Dictionary<(int cx, int cz), Chunk> _chunks = new Dictionary<(int cx, int cz), Chunk>();

        /// <summary>地形数据提供者。可通过此属性注入自定义生成逻辑。</summary>
        public ITerrainProvider TerrainProvider { get; set; }

        /// <summary>渲染半径（单位：区块）。</summary>
        public int RenderRadius => _renderRadius;

        /// <summary>设置渲染半径（单位：区块）。由 GameBootstrap 在初始化时调用。</summary>
        public void SetRenderRadius(int radius) => _renderRadius = radius;

        /// <summary>当前已加载的区块数量。</summary>
        public int LoadedChunkCount => _chunks.Count;

        /// <summary>
        /// Unity 生命周期初始化。若未注入 TerrainProvider，则使用默认的 Perlin 噪声生成器。
        /// </summary>
        private void Awake()
        {
            if (TerrainProvider == null)
                TerrainProvider = new TerrainGenerator(_seed);
        }

        /// <summary>
        /// 每帧更新。此方法故意留空——区块加载调度由 ChunkLoadSystem 驱动。
        /// </summary>
        private void Update()
        {
        }

        // ==================== 区块查询 ====================

        /// <summary>
        /// 获取指定坐标的区块。若未加载则返回 null。
        /// </summary>
        /// <param name="cx">区块 X 坐标。</param>
        /// <param name="cz">区块 Z 坐标。</param>
        public Chunk GetChunk(int cx, int cz)
        {
            _chunks.TryGetValue((cx, cz), out Chunk chunk);
            return chunk;
        }

        /// <summary>
        /// 判断指定区块是否已加载。
        /// </summary>
        public bool HasChunk(int cx, int cz) => _chunks.ContainsKey((cx, cz));

        /// <summary>
        /// 通过世界坐标获取方块类型。自动定位目标区块。
        /// 若区块未加载或 Y 越界，返回 Air。
        /// </summary>
        /// <param name="wx">世界 X 坐标。</param>
        /// <param name="wy">世界 Y 坐标。</param>
        /// <param name="wz">世界 Z 坐标。</param>
        public BlockType GetBlock(int wx, int wy, int wz)
        {
            if (wy < 0 || wy >= Chunk.Height)
                return BlockType.Air;

            int cx = WorldToChunkCoord(wx);
            int cz = WorldToChunkCoord(wz);
            Chunk chunk = GetChunk(cx, cz);
            if (chunk == null)
                return BlockType.Air;

            return chunk.GetWorldBlock(wx, wy, wz);
        }

        /// <summary>
        /// 通过世界坐标设置方块类型。自动定位目标区块并标记重建。
        /// 若区块未加载或 Y 越界，则忽略。
        /// </summary>
        /// <param name="wx">世界 X 坐标。</param>
        /// <param name="wy">世界 Y 坐标。</param>
        /// <param name="wz">世界 Z 坐标。</param>
        /// <param name="type">要设置的方块类型。</param>
        public void SetBlock(int wx, int wy, int wz, BlockType type)
        {
            if (wy < 0 || wy >= Chunk.Height)
                return;

            int cx = WorldToChunkCoord(wx);
            int cz = WorldToChunkCoord(wz);
            Chunk chunk = GetChunk(cx, cz);
            if (chunk == null)
                return;

            chunk.SetWorldBlock(wx, wy, wz, type);
        }

        // ==================== 区块加载/卸载 ====================

        /// <summary>
        /// 加载指定区块。若已加载则直接返回。
        /// 创建区块并通过 TerrainProvider 生成地形数据。
        /// </summary>
        /// <param name="cx">区块 X 坐标。</param>
        /// <param name="cz">区块 Z 坐标。</param>
        /// <returns>加载完成的区块。</returns>
        public Chunk LoadChunk(int cx, int cz)
        {
            var key = (cx, cz);
            if (_chunks.TryGetValue(key, out Chunk existing))
                return existing;

            Chunk chunk = new Chunk(cx, cz);
            TerrainProvider?.Generate(chunk);
            _chunks[key] = chunk;
            return chunk;
        }

        /// <summary>
        /// 卸载指定区块。从字典中移除，释放内存。
        /// </summary>
        /// <param name="cx">区块 X 坐标。</param>
        /// <param name="cz">区块 Z 坐标。</param>
        /// <returns>是否成功卸载（区块原本存在则返回 true）。</returns>
        public bool UnloadChunk(int cx, int cz)
        {
            return _chunks.Remove((cx, cz));
        }

        /// <summary>
        /// 确保玩家周围指定半径内的区块已加载。
        /// 以玩家世界坐标对应的区块为中心，加载半径范围内所有未加载的区块。
        /// </summary>
        /// <param name="wx">玩家世界 X 坐标。</param>
        /// <param name="wz">玩家世界 Z 坐标。</param>
        /// <param name="radius">加载半径（单位：区块）。若 ≤0 则使用默认 RenderRadius。</param>
        public void EnsureChunksAround(int wx, int wz, int radius)
        {
            if (radius <= 0)
                radius = _renderRadius;

            int centerCX = WorldToChunkCoord(wx);
            int centerCZ = WorldToChunkCoord(wz);

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    // 圆形区域裁剪（距中心超过半径的角落不加载）
                    if (dx * dx + dz * dz > radius * radius)
                        continue;

                    int cx = centerCX + dx;
                    int cz = centerCZ + dz;
                    if (!HasChunk(cx, cz))
                        LoadChunk(cx, cz);
                }
            }
        }

        /// <summary>
        /// 获取所有已加载区块的枚举器。供 Mesh 构建系统遍历使用。
        /// </summary>
        public IEnumerable<Chunk> GetAllLoadedChunks() => _chunks.Values;

        // ==================== 工具方法 ====================

        /// <summary>
        /// 将世界坐标转换为区块坐标（向下取整）。
        /// 例如：wx=15 → 0，wx=16 → 1，wx=-1 → -1。
        /// </summary>
        private static int WorldToChunkCoord(int worldCoord)
        {
            return FloorDiv(worldCoord, Chunk.Width);
        }

        /// <summary>
        /// 整数向下取整除法。
        /// C# 的 / 运算符向零截断，负数需要修正（-1 / 16 = 0，但我们需要 -1）。
        /// </summary>
        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            return r < 0 ? q - 1 : q;
        }
    }
}
