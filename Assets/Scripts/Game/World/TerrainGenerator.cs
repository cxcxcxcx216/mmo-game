using UnityEngine;

namespace Minecraft.Game.World
{
    /// <summary>
    /// 地形生成器。纯 C# 类（非 MonoBehaviour），根据种子填充区块的地形数据。
    /// 使用 Unity 的 Mathf.PerlinNoise 生成高度图，组合多个八度获得自然地形。
    /// 地形特征：基岩层、草地/沙漠地表、泥土层、石头层、矿石、树木。
    /// </summary>
    public class TerrainGenerator : ITerrainProvider
    {
        /// <summary>地表最低高度（海平面附近）。</summary>
        private const int MinHeight = 20;

        /// <summary>地表最高高度（山顶附近）。</summary>
        private const int MaxHeight = 40;

        /// <summary>地表高度变化范围。</summary>
        private const int HeightRange = MaxHeight - MinHeight;

        /// <summary>泥土层厚度（草地下面）。</summary>
        private const int DirtLayerThickness = 3;

        /// <summary>矿石生成的最高 Y 坐标。</summary>
        private const int OreMaxHeight = 30;

        /// <summary>钻石矿生成的最高 Y 坐标（更深才出钻石）。</summary>
        private const int DiamondMaxHeight = 16;

        /// <summary>树木生成概率（每个草地方块）。</summary>
        private const float TreeChance = 0.05f;

        /// <summary>沙漠判定阈值：生物群系噪声低于此值则为沙漠。</summary>
        private const float DesertThreshold = 0.3f;

        /// <summary>世界随机种子。</summary>
        private readonly int _seed;

        /// <summary>主噪声频率（控制地形大尺度起伏）。</summary>
        private readonly float _baseFrequency;

        /// <summary>
        /// 构造地形生成器。
        /// </summary>
        /// <param name="seed">世界种子，相同种子生成相同地形。</param>
        /// <param name="baseFrequency">主噪声频率，默认 0.02（约每 50 格一个山丘）。</param>
        public TerrainGenerator(int seed, float baseFrequency = 0.02f)
        {
            _seed = seed;
            _baseFrequency = baseFrequency;
        }

        /// <summary>
        /// 填充区块的地形数据。
        /// 流程：1.生成高度图 → 2.填充方块 → 3.散布矿石 → 4.种植树木。
        /// </summary>
        /// <param name="chunk">待填充的区块。</param>
        public void Generate(Chunk chunk)
        {
            // 高度图：记录每列的地表高度
            int[,] heightMap = new int[Chunk.Width, Chunk.Depth];
            // 沙漠图：记录每列是否为沙漠生物群系
            bool[,] desertMap = new bool[Chunk.Width, Chunk.Depth];

            GenerateHeightMap(chunk, heightMap, desertMap);
            FillTerrain(chunk, heightMap, desertMap);
            ScatterOres(chunk);
            PlantTrees(chunk, heightMap, desertMap);

            chunk.IsGenerated = true;
            chunk.IsModified = true;
        }

        /// <summary>
        /// 生成高度图和沙漠分布图。
        /// 使用多八度 Perlin 噪声叠加，模拟自然地形的分形特征。
        /// </summary>
        private void GenerateHeightMap(Chunk chunk, int[,] heightMap, bool[,] desertMap)
        {
            int worldOriginX = chunk.ChunkX * Chunk.Width;
            int worldOriginZ = chunk.ChunkZ * Chunk.Depth;

            for (int x = 0; x < Chunk.Width; x++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    int wx = worldOriginX + x;
                    int wz = worldOriginZ + z;

                    float height = SampleHeightNoise(wx, wz);
                    heightMap[x, z] = Mathf.RoundToInt(MinHeight + height * HeightRange);

                    // 生物群系噪声：低频噪声决定沙漠区域
                    float biome = Mathf.PerlinNoise(
                        (wx + _seed * 0.5f) * 0.01f,
                        (wz + _seed * 0.5f) * 0.01f);
                    desertMap[x, z] = biome < DesertThreshold;
                }
            }
        }

        /// <summary>
        /// 采样多八度高度噪声，返回归一化高度 [0, 1]。
        /// 叠加 3 个八度，每个八度频率翻倍、振幅减半，模拟分形地形。
        /// </summary>
        private float SampleHeightNoise(int wx, int wz)
        {
            // 种子作为坐标偏移，使不同种子产生不同地形
            float offsetX = _seed * 0.137f;
            float offsetZ = _seed * 0.319f;

            // 第一八度：大尺度地形
            float n1 = Mathf.PerlinNoise(
                (wx + offsetX) * _baseFrequency,
                (wz + offsetZ) * _baseFrequency);

            // 第二八度：中等细节（频率×2，振幅×0.5）
            float n2 = Mathf.PerlinNoise(
                (wx + offsetX) * _baseFrequency * 2f,
                (wz + offsetZ) * _baseFrequency * 2f) * 0.5f;

            // 第三八度：细节起伏（频率×4，振幅×0.25）
            float n3 = Mathf.PerlinNoise(
                (wx + offsetX) * _baseFrequency * 4f,
                (wz + offsetZ) * _baseFrequency * 4f) * 0.25f;

            // 归一化（总振幅 = 1 + 0.5 + 0.25 = 1.75）
            float combined = (n1 + n2 + n3) / 1.75f;
            return Mathf.Clamp01(combined);
        }

        /// <summary>
        /// 根据高度图填充区块的方块。
        /// 分层结构：基岩(y=0) → 石头 → 泥土(3层) → 草地/沙子(表层)。
        /// </summary>
        private void FillTerrain(Chunk chunk, int[,] heightMap, bool[,] desertMap)
        {
            for (int x = 0; x < Chunk.Width; x++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    int surfaceY = heightMap[x, z];
                    bool isDesert = desertMap[x, z];

                    for (int y = 0; y <= surfaceY; y++)
                    {
                        BlockType type;

                        if (y == 0)
                        {
                            // 最底层：基岩（不可破坏）
                            type = BlockType.Bedrock;
                        }
                        else if (y == surfaceY)
                        {
                            // 地表层：草地或沙子
                            type = isDesert ? BlockType.Sand : BlockType.Grass;
                        }
                        else if (y > surfaceY - DirtLayerThickness)
                        {
                            // 地表下 3 层：泥土（沙漠中也是沙子）
                            type = isDesert ? BlockType.Sand : BlockType.Dirt;
                        }
                        else
                        {
                            // 深层：石头
                            type = BlockType.Stone;
                        }

                        chunk.SetBlock(x, y, z, type);
                    }
                }
            }
        }

        /// <summary>
        /// 在石头层中散布矿石。
        /// 煤矿最常见，铁矿次之，金矿较少，钻石最稀有且只在深层出现。
        /// </summary>
        private void ScatterOres(Chunk chunk)
        {
            System.Random rng = CreateChunkRandom(chunk);

            for (int y = 1; y < OreMaxHeight && y < Chunk.Height; y++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    for (int x = 0; x < Chunk.Width; x++)
                    {
                        if (chunk.GetBlock(x, y, z) != BlockType.Stone)
                            continue;

                        double roll = rng.NextDouble();

                        // 钻石只在深层（y < 16）出现，概率最低
                        if (y < DiamondMaxHeight && roll < 0.01)
                        {
                            chunk.SetBlock(x, y, z, BlockType.Diamond);
                        }
                        // 金矿概率较低
                        else if (roll < 0.013)
                        {
                            chunk.SetBlock(x, y, z, BlockType.Gold);
                        }
                        // 铁矿概率中等
                        else if (roll < 0.03)
                        {
                            chunk.SetBlock(x, y, z, BlockType.Iron);
                        }
                        // 煤矿概率最高
                        else if (roll < 0.06)
                        {
                            chunk.SetBlock(x, y, z, BlockType.Coal);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 在草地上随机种植树木。
        /// 树木结构：树干 4-6 格原木，顶部球形树叶。
        /// 密度约 5%，仅在草地地表且空间充足时种植。
        /// </summary>
        private void PlantTrees(Chunk chunk, int[,] heightMap, bool[,] desertMap)
        {
            System.Random rng = CreateChunkRandom(chunk);
            // 跳过一些随机数，避免与矿石生成序列耦合
            rng.Next();

            for (int x = 2; x < Chunk.Width - 2; x++)
            {
                for (int z = 2; z < Chunk.Depth - 2; z++)
                {
                    // 沙漠不生树
                    if (desertMap[x, z])
                        continue;

                    int surfaceY = heightMap[x, z];
                    if (surfaceY >= Chunk.Height - 8)
                        continue;

                    // 只有草地才能长树
                    if (chunk.GetBlock(x, surfaceY, z) != BlockType.Grass)
                        continue;

                    // 5% 概率种树
                    if (rng.NextDouble() >= TreeChance)
                        continue;

                    int trunkHeight = rng.Next(4, 7); // 4-6 格
                    GenerateTree(chunk, x, surfaceY + 1, z, trunkHeight, rng);
                }
            }
        }

        /// <summary>
        /// 在指定位置生成一棵树。
        /// 树干从底部向上延伸，顶部放置球形树叶。
        /// </summary>
        /// <param name="chunk">目标区块。</param>
        /// <param name="x">树干底部 X（局部坐标）。</param>
        /// <param name="y">树干底部 Y（地表上方）。</param>
        /// <param name="z">树干底部 Z（局部坐标）。</param>
        /// <param name="trunkHeight">树干高度。</param>
        /// <param name="rng">随机数生成器。</param>
        private void GenerateTree(Chunk chunk, int x, int y, int z, int trunkHeight, System.Random rng)
        {
            int topY = y + trunkHeight - 1;

            // 树干：原木
            for (int i = 0; i < trunkHeight; i++)
            {
                chunk.SetBlock(x, y + i, z, BlockType.Wood);
            }

            // 树叶：在树干顶部生成 2-3 层球形树叶
            int leavesLayers = rng.Next(2, 4); // 2-3 层
            int leavesBaseY = topY;

            for (int ly = 0; ly < leavesLayers; ly++)
            {
                int leavesY = leavesBaseY + ly;
                if (leavesY >= Chunk.Height)
                    break;

                // 顶层半径较小，形成圆顶轮廓
                int radius = (ly == leavesLayers - 1) ? 1 : 2;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        // 圆形裁剪：角落不放置
                        if (dx * dx + dz * dz > radius * radius + 1)
                            continue;

                        int lx = x + dx;
                        int lz = z + dz;

                        // 只在空气位置放树叶，不覆盖树干
                        if (chunk.GetBlock(lx, leavesY, lz) == BlockType.Air)
                        {
                            chunk.SetBlock(lx, leavesY, lz, BlockType.Leaves);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 基于种子和区块坐标创建确定性随机数生成器。
        /// 相同的种子和区块坐标始终产生相同的随机序列，保证地形可复现。
        /// </summary>
        private System.Random CreateChunkRandom(Chunk chunk)
        {
            // 使用质数异或混合坐标，减少哈希碰撞
            int hash = _seed;
            hash ^= chunk.ChunkX * 73856093;
            hash ^= chunk.ChunkZ * 19349663;
            return new System.Random(hash);
        }
    }

    /// <summary>
    /// 地形提供者接口。ChunkManager 通过此接口解耦地形生成实现。
    /// 实现方可以是 Perlin 噪声生成器、网络加载器或存档读取器。
    /// </summary>
    public interface ITerrainProvider
    {
        /// <summary>填充指定区块的地形数据。</summary>
        /// <param name="chunk">待填充的区块。</param>
        void Generate(Chunk chunk);
    }
}
