using UnityEngine;

namespace Minecraft.Game.World
{
    /// <summary>
    /// 地形生成器。纯 C# 类（非 MonoBehaviour），根据种子填充区块的地形数据。
    /// 使用 Unity 的 Mathf.PerlinNoise 生成高度图与生物群系图，组合多个八度获得自然地形。
    /// 地形特征：4 种生物群系（草地/沙漠/雪原/丛林，带过渡带）、水域与沙滩、
    /// 分层地表、矿脉化矿石、多树种树木、生物群系专属装饰。
    /// </summary>
    public class TerrainGenerator : ITerrainProvider
    {
        // ==================== 高度范围常量 ====================

        /// <summary>全局地表最低高度。</summary>
        private const int MinHeight = 20;

        /// <summary>全局地表最高高度。</summary>
        private const int MaxHeight = 60;

        /// <summary>海平面高度（低于此值的空气方块填充为水）。</summary>
        private const int SeaLevel = 24;

        /// <summary>沙滩范围：地表高度 <= 海平面+此值 时草地变为沙子。</summary>
        private const int BeachRange = 1;

        // ==================== 生物群系高度范围常量 ====================

        /// <summary>草地生物群系高度范围：平缓起伏。</summary>
        private const int GrassMin = 20, GrassMax = 35;

        /// <summary>沙漠生物群系高度范围：平坦。</summary>
        private const int DesertMin = 22, DesertMax = 28;

        /// <summary>雪原生物群系高度范围：有山脉。</summary>
        private const int SnowMin = 35, SnowMax = 60;

        /// <summary>丛林生物群系高度范围：中等起伏。</summary>
        private const int JungleMin = 25, JungleMax = 40;

        // ==================== 地层常量 ====================

        /// <summary>泥土层厚度（地表下的泥土层数）。</summary>
        private const int DirtLayerThickness = 3;

        // ==================== 矿石常量 ====================

        /// <summary>煤矿生成的最高 Y 坐标。</summary>
        private const int CoalMaxHeight = 32;

        /// <summary>铁矿生成的最高 Y 坐标。</summary>
        private const int IronMaxHeight = 24;

        /// <summary>金矿生成的最高 Y 坐标。</summary>
        private const int GoldMaxHeight = 16;

        /// <summary>钻石矿生成的最高 Y 坐标。</summary>
        private const int DiamondMaxHeight = 12;

        /// <summary>每个区块的煤矿脉尝试次数。</summary>
        private const int CoalVeinAttempts = 40;

        /// <summary>每个区块的铁矿石尝试次数。</summary>
        private const int IronVeinAttempts = 25;

        /// <summary>每个区块的金矿石尝试次数。</summary>
        private const int GoldVeinAttempts = 12;

        /// <summary>每个区块的钻石矿石尝试次数。</summary>
        private const int DiamondVeinAttempts = 6;

        /// <summary>煤矿脉大小范围。</summary>
        private const int CoalVeinMin = 3, CoalVeinMax = 5;

        /// <summary>铁矿脉大小范围。</summary>
        private const int IronVeinMin = 2, IronVeinMax = 4;

        /// <summary>金矿脉大小范围。</summary>
        private const int GoldVeinMin = 2, GoldVeinMax = 3;

        /// <summary>钻石矿脉大小范围。</summary>
        private const int DiamondVeinMin = 1, DiamondVeinMax = 2;

        // ==================== 植被/装饰概率常量 ====================

        /// <summary>普通树木生成概率（参考 Minecraft：每 chunk 1-3 棵）。</summary>
        private const float TreeChance = 0.015f;

        /// <summary>丛林树生成概率（丛林稍密，每 chunk 3-5 棵）。</summary>
        private const float JungleTreeChance = 0.025f;

        /// <summary>树林集群噪声频率（低频，决定树林中心位置）。</summary>
        private const float ForestClusterFrequency = 0.012f;

        /// <summary>树林密度阈值：集群噪声值高于此值才种树（形成聚集+空地）。</summary>
        private const float ForestDensityThreshold = 0.55f;

        /// <summary>地表装饰生成概率（花/蘑菇，参考 Minecraft 约 6%）。</summary>
        private const float DecorationChance = 0.06f;

        // ==================== 噪声参数常量 ====================

        /// <summary>生物群系噪声频率（低频，决定大尺度生物群系分布）。</summary>
        private const float BiomeFrequency = 0.008f;

        /// <summary>生物群系过渡带宽度（归一化 band 空间 [0,1) 内的边缘比例）。</summary>
        private const float BiomeTransition = 0.15f;

        // ==================== 生物群系枚举 ====================

        /// <summary>生物群系类型。由低频 Perlin 噪声划分 4 个带确定。</summary>
        private enum Biome
        {
            Grass = 0,      // 草地：绿色草地，橡树/白桦，草丛与花
            Desert = 1,     // 沙漠：沙子，仙人掌，枯灌木
            Snow = 2,       // 雪原：雪方块，松树，冰块
            Jungle = 3      // 丛林：深色草，高大丛林树，藤蔓与蘑菇
        }

        // ==================== 字段 ====================

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

        // ==================== 主流程 ====================

        /// <summary>
        /// 填充区块的地形数据。
        /// 流程：1.生成生物群系与高度图 → 2.填充方块(含水域) → 3.矿脉化矿石 → 4.树木 → 5.装饰。
        /// </summary>
        /// <param name="chunk">待填充的区块。</param>
        public void Generate(Chunk chunk)
        {
            // 高度图：记录每列的地表高度
            int[,] heightMap = new int[Chunk.Width, Chunk.Depth];
            // 生物群系图：记录每列的主生物群系
            Biome[,] biomeMap = new Biome[Chunk.Width, Chunk.Depth];

            GenerateBiomeAndHeight(chunk, heightMap, biomeMap);
            FillTerrain(chunk, heightMap, biomeMap);
            PlaceOreVeins(chunk);
            PlantTrees(chunk, heightMap, biomeMap);
            PlantDecorations(chunk, heightMap, biomeMap);
            GenerateClouds(chunk);

            chunk.IsGenerated = true;
            chunk.IsModified = true;
        }

        // ==================== 生物群系与高度图 ====================

        /// <summary>
        /// 生成生物群系图与高度图。
        /// 生物群系由低频 Perlin 噪声划分为 4 个带；在带的边缘对相邻生物群系的高度做线性插值，
        /// 形成 3-5 格的过渡带。高度由多八度噪声按各生物群系的高度范围缩放得到。
        /// </summary>
        private void GenerateBiomeAndHeight(Chunk chunk, int[,] heightMap, Biome[,] biomeMap)
        {
            int worldOriginX = chunk.ChunkX * Chunk.Width;
            int worldOriginZ = chunk.ChunkZ * Chunk.Depth;

            for (int x = 0; x < Chunk.Width; x++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    int wx = worldOriginX + x;
                    int wz = worldOriginZ + z;

                    // 生物群系噪声：低频，划分 4 个带
                    float b = Mathf.PerlinNoise(
                        (wx + _seed * 0.5f) * BiomeFrequency,
                        (wz + _seed * 0.5f) * BiomeFrequency);
                    b = Mathf.Clamp01(b);

                    // 将 [0,1] 映射到 4 个带 [0,4)
                    float band = b * 4f;
                    int idx = Mathf.Clamp(Mathf.FloorToInt(band), 0, 3);
                    float frac = Mathf.Clamp01(band - idx);
                    Biome primary = (Biome)idx;

                    // 计算过渡混合因子：靠近带边缘时与相邻生物群系混合
                    Biome neighbor = primary;
                    float blend = 0f; // 0=纯主生物群系，1=完全邻接生物群系
                    if (frac < BiomeTransition && idx > 0)
                    {
                        neighbor = (Biome)(idx - 1);
                        blend = (BiomeTransition - frac) / BiomeTransition;
                    }
                    else if (frac > 1f - BiomeTransition && idx < 3)
                    {
                        neighbor = (Biome)(idx + 1);
                        blend = (frac - (1f - BiomeTransition)) / BiomeTransition;
                    }

                    // 共享高度噪声（多八度），保证过渡时高度连续
                    float n = SampleHeightNoise(wx, wz);
                    int hPrimary = BiomeHeight(primary, n);
                    int hNeighbor = BiomeHeight(neighbor, n);
                    int height = Mathf.RoundToInt(Mathf.Lerp(hPrimary, hNeighbor, blend));
                    height = Mathf.Clamp(height, MinHeight, MaxHeight);

                    heightMap[x, z] = height;
                    biomeMap[x, z] = primary;
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
        /// 根据生物群系与归一化噪声计算地表高度。
        /// 不同生物群系有不同的高度范围，决定其地貌特征（沙漠平坦、雪原多山等）。
        /// </summary>
        private int BiomeHeight(Biome biome, float n)
        {
            switch (biome)
            {
                case Biome.Desert: return DesertMin + Mathf.RoundToInt(n * (DesertMax - DesertMin));
                case Biome.Snow:   return SnowMin + Mathf.RoundToInt(n * (SnowMax - SnowMin));
                case Biome.Jungle: return JungleMin + Mathf.RoundToInt(n * (JungleMax - JungleMin));
                default:           return GrassMin + Mathf.RoundToInt(n * (GrassMax - GrassMin));
            }
        }

        // ==================== 地表填充（含水域） ====================

        /// <summary>
        /// 根据高度图与生物群系填充区块方块。
        /// 分层结构：基岩(y=0) → 石头 → 泥土(3层) → 生物群系专属地表。
        /// 水域：地表低于海平面时，地表到海平面之间的空气填充为 Water。
        /// 沙滩：近海平面的草地/丛林地表替换为沙子。
        /// </summary>
        private void FillTerrain(Chunk chunk, int[,] heightMap, Biome[,] biomeMap)
        {
            for (int x = 0; x < Chunk.Width; x++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    int surfaceY = heightMap[x, z];
                    Biome biome = biomeMap[x, z];
                    bool isUnderwater = surfaceY < SeaLevel;
                    bool isBeach = surfaceY <= SeaLevel + BeachRange;

                    // 确定地表与亚地表方块类型
                    GetSurfaceTypes(biome, isUnderwater, isBeach,
                        out BlockType surfaceType, out BlockType subSurfaceType);

                    for (int y = 0; y <= surfaceY && y < Chunk.Height; y++)
                    {
                        BlockType type;
                        if (y == 0)
                        {
                            type = BlockType.Bedrock;                       // 基岩层
                        }
                        else if (y == surfaceY)
                        {
                            type = surfaceType;                              // 地表层
                        }
                        else if (y > surfaceY - DirtLayerThickness)
                        {
                            type = subSurfaceType;                           // 亚地表层
                        }
                        else
                        {
                            type = BlockType.Stone;                          // 深层石头
                        }

                        chunk.SetBlock(x, y, z, type);
                    }

                    // 水域填充：地表到海平面之间的空气变为水
                    if (isUnderwater)
                    {
                        int waterTop = Mathf.Min(SeaLevel, Chunk.Height - 1);
                        for (int y = surfaceY + 1; y <= waterTop; y++)
                        {
                            if (chunk.GetBlock(x, y, z) == BlockType.Air)
                                chunk.SetBlock(x, y, z, BlockType.Water);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 根据生物群系与水/沙滩状态确定地表与亚地表方块类型。
        /// 水下与沙滩的草地/丛林地表替换为沙子（湖床/海滩）。
        /// </summary>
        private void GetSurfaceTypes(Biome biome, bool isUnderwater, bool isBeach,
            out BlockType surfaceType, out BlockType subSurfaceType)
        {
            switch (biome)
            {
                case Biome.Desert:
                    surfaceType = BlockType.Sand;
                    subSurfaceType = BlockType.Sand;
                    break;
                case Biome.Snow:
                    // 雪原水下变为泥土（湖床），否则雪方块
                    surfaceType = isUnderwater ? BlockType.Dirt : BlockType.Snow;
                    subSurfaceType = BlockType.Dirt;
                    break;
                case Biome.Jungle:
                    surfaceType = (isUnderwater || isBeach) ? BlockType.Sand : BlockType.JungleGrass;
                    subSurfaceType = (isUnderwater || isBeach) ? BlockType.Sand : BlockType.Dirt;
                    break;
                default: // Grass
                    surfaceType = (isUnderwater || isBeach) ? BlockType.Sand : BlockType.Grass;
                    subSurfaceType = (isUnderwater || isBeach) ? BlockType.Sand : BlockType.Dirt;
                    break;
            }
        }

        // ==================== 矿脉化矿石 ====================

        /// <summary>
        /// 在石头层中生成矿石矿脉。
        /// 按深度限制矿种：煤矿最浅最多，铁矿次之，金矿更深更少，钻石最深最稀有。
        /// 每个矿脉从随机起点出发，随机游走放置若干方块，仅替换石头。
        /// </summary>
        private void PlaceOreVeins(Chunk chunk)
        {
            System.Random rng = CreateChunkRandom(chunk);

            // 按稀有度从高到低生成，避免稀有矿被普通矿覆盖
            GenerateOreType(chunk, rng, BlockType.Diamond, DiamondMaxHeight,
                DiamondVeinAttempts, DiamondVeinMin, DiamondVeinMax);
            GenerateOreType(chunk, rng, BlockType.Gold, GoldMaxHeight,
                GoldVeinAttempts, GoldVeinMin, GoldVeinMax);
            GenerateOreType(chunk, rng, BlockType.Iron, IronMaxHeight,
                IronVeinAttempts, IronVeinMin, IronVeinMax);
            GenerateOreType(chunk, rng, BlockType.Coal, CoalMaxHeight,
                CoalVeinAttempts, CoalVeinMin, CoalVeinMax);
        }

        /// <summary>
        /// 生成单种矿石的若干矿脉。
        /// </summary>
        private void GenerateOreType(Chunk chunk, System.Random rng, BlockType ore,
            int maxY, int attempts, int sizeMin, int sizeMax)
        {
            for (int i = 0; i < attempts; i++)
            {
                int x = rng.Next(0, Chunk.Width);
                int y = rng.Next(1, Mathf.Max(2, maxY));
                int z = rng.Next(0, Chunk.Depth);

                if (chunk.GetBlock(x, y, z) != BlockType.Stone)
                    continue;

                int size = rng.Next(sizeMin, sizeMax + 1);
                PlaceOreVein(chunk, x, y, z, ore, size, rng);
            }
        }

        /// <summary>
        /// 从起点随机游走放置一个矿脉。每步随机选择 6 方向之一移动，仅替换石头方块。
        /// 游走使矿石聚集成不规则矿脉，而非单点散布。
        /// </summary>
        private void PlaceOreVein(Chunk chunk, int x, int y, int z, BlockType ore,
            int size, System.Random rng)
        {
            int cx = x, cy = y, cz = z;
            for (int i = 0; i < size; i++)
            {
                if (chunk.GetBlock(cx, cy, cz) == BlockType.Stone)
                    chunk.SetBlock(cx, cy, cz, ore);

                // 随机游走：6 个方向之一
                int dir = rng.Next(0, 6);
                switch (dir)
                {
                    case 0: cx = Mathf.Clamp(cx + 1, 0, Chunk.Width - 1); break;
                    case 1: cx = Mathf.Clamp(cx - 1, 0, Chunk.Width - 1); break;
                    case 2: cy = Mathf.Clamp(cy + 1, 1, Chunk.Height - 1); break;
                    case 3: cy = Mathf.Clamp(cy - 1, 1, Chunk.Height - 1); break;
                    case 4: cz = Mathf.Clamp(cz + 1, 0, Chunk.Depth - 1); break;
                    case 5: cz = Mathf.Clamp(cz - 1, 0, Chunk.Depth - 1); break;
                }
            }
        }

        // ==================== 树木 ====================

        /// <summary>
        /// 根据生物群系种植树木（集群化：树木聚集成林，林间有空地）。
        /// 用低频 Perlin 噪声决定树林中心，噪声值高于阈值才种树。
        /// 草地：橡树/白桦/灌木；雪原：松树；丛林：高大丛林树（带藤蔓）；沙漠：无树。
        /// </summary>
        private void PlantTrees(Chunk chunk, int[,] heightMap, Biome[,] biomeMap)
        {
            System.Random rng = CreateChunkRandom(chunk);
            // 跳过若干随机数，避免与矿石生成序列耦合
            rng.Next();

            int worldOriginX = chunk.ChunkX * Chunk.Width;
            int worldOriginZ = chunk.ChunkZ * Chunk.Depth;

            for (int x = 2; x < Chunk.Width - 2; x++)
            {
                for (int z = 2; z < Chunk.Depth - 2; z++)
                {
                    Biome biome = biomeMap[x, z];
                    int surfaceY = heightMap[x, z];

                    // 水中及海平面以下不种树
                    if (surfaceY < SeaLevel)
                        continue;
                    // 预留 13 格高度空间
                    if (surfaceY >= Chunk.Height - 13)
                        continue;

                    // 集群噪声：决定是否处于树林区域
                    int wx = worldOriginX + x;
                    int wz = worldOriginZ + z;
                    float forestNoise = Mathf.PerlinNoise(
                        (wx + _seed * 0.7f) * ForestClusterFrequency,
                        (wz + _seed * 0.7f) * ForestClusterFrequency);
                    forestNoise = Mathf.Clamp01(forestNoise);

                    // 噪声值低于阈值 = 空地（草地/花海），不种树
                    if (forestNoise < ForestDensityThreshold)
                        continue;

                    // 噪声值越高，种树概率越高（树林中心更密）
                    float densityBoost = (forestNoise - ForestDensityThreshold) / (1f - ForestDensityThreshold);
                    float effectiveChance = TreeChance * (0.5f + densityBoost * 1.5f);

                    BlockType surface = chunk.GetBlock(x, surfaceY, z);
                    double roll = rng.NextDouble();

                    switch (biome)
                    {
                        case Biome.Grass:
                            if (surface != BlockType.Grass)
                                continue;
                            if (roll >= effectiveChance)
                                continue;
                            PickGrassTree(chunk, x, surfaceY + 1, z, rng);
                            break;

                        case Biome.Snow:
                            if (surface != BlockType.Snow)
                                continue;
                            if (roll >= effectiveChance)
                                continue;
                            GeneratePineTree(chunk, x, surfaceY + 1, z, rng);
                            break;

                        case Biome.Jungle:
                            if (surface != BlockType.JungleGrass)
                                continue;
                            // 丛林不受集群噪声限制，本身就更密
                            if (roll >= JungleTreeChance)
                                continue;
                            GenerateJungleTree(chunk, x, surfaceY + 1, z, rng);
                            break;

                        // 沙漠无树
                        case Biome.Desert:
                        default:
                            break;
                    }
                }
            }
        }

        /// <summary>草地树种随机选择：橡树(45%)/白桦(30%)/灌木(25%)。</summary>
        private void PickGrassTree(Chunk chunk, int x, int y, int z, System.Random rng)
        {
            double treeTypeRoll = rng.NextDouble();
            if (treeTypeRoll < 0.45)
                GenerateOakTree(chunk, x, y, z, rng);
            else if (treeTypeRoll < 0.75)
                GenerateBirchTree(chunk, x, y, z, rng);
            else
                GenerateBush(chunk, x, y, z, rng);
        }

        /// <summary>
        /// 橡树：4-6格树干，顶部2-3层圆冠（半径2，顶层1）。
        /// </summary>
        private void GenerateOakTree(Chunk chunk, int x, int y, int z, System.Random rng)
        {
            int trunkHeight = rng.Next(4, 7);
            int topY = y + trunkHeight - 1;

            PlaceTrunk(chunk, x, y, z, trunkHeight, BlockType.Wood);

            int leavesLayers = rng.Next(2, 4);
            int leavesStartY = topY - 1;
            for (int ly = 0; ly < leavesLayers; ly++)
            {
                int leavesY = leavesStartY + ly;
                if (leavesY >= Chunk.Height) break;
                int radius = (ly == leavesLayers - 1) ? 1 : 2;
                PlaceLeavesCircle(chunk, x, leavesY, z, radius, BlockType.Leaves);
            }
        }

        /// <summary>
        /// 松树：6-10格高深色树干，锥形深绿树叶从底部半径3递减到顶部1。
        /// </summary>
        private void GeneratePineTree(Chunk chunk, int x, int y, int z, System.Random rng)
        {
            int trunkHeight = rng.Next(6, 11);
            int topY = y + trunkHeight - 1;

            PlaceTrunk(chunk, x, y, z, trunkHeight, BlockType.PineLog);

            int leavesStartY = y + trunkHeight / 2;
            int totalLeafLayers = topY - leavesStartY + 2;
            for (int ly = 0; ly < totalLeafLayers; ly++)
            {
                int leavesY = leavesStartY + ly;
                if (leavesY >= Chunk.Height) break;
                int radius = Mathf.Max(1, 3 - ly * 3 / Mathf.Max(1, totalLeafLayers));
                PlaceLeavesCircle(chunk, x, leavesY, z, radius, BlockType.PineLeaves);
            }

            // 顶尖放一个树叶
            if (topY + 1 < Chunk.Height && chunk.GetBlock(x, topY + 1, z) == BlockType.Air)
                chunk.SetBlock(x, topY + 1, z, BlockType.PineLeaves);
        }

        /// <summary>
        /// 白桦：5-7格白色树干，细长浅黄绿树冠（半径1-2，3-4层紧凑树叶）。
        /// </summary>
        private void GenerateBirchTree(Chunk chunk, int x, int y, int z, System.Random rng)
        {
            int trunkHeight = rng.Next(5, 8);
            int topY = y + trunkHeight - 1;

            PlaceTrunk(chunk, x, y, z, trunkHeight, BlockType.BirchLog);

            int leavesLayers = rng.Next(3, 5);
            int leavesStartY = topY - 1;
            for (int ly = 0; ly < leavesLayers; ly++)
            {
                int leavesY = leavesStartY + ly;
                if (leavesY >= Chunk.Height) break;
                // 交替半径：1-2-1-1 形成细长轮廓
                int radius = (ly == 1) ? 2 : 1;
                PlaceLeavesCircle(chunk, x, leavesY, z, radius, BlockType.BirchLeaves);
            }
        }

        /// <summary>
        /// 灌木：1-2格短树干，周围一圈低矮树叶（半径2，1-2层）。
        /// </summary>
        private void GenerateBush(Chunk chunk, int x, int y, int z, System.Random rng)
        {
            int trunkHeight = rng.Next(1, 3);

            PlaceTrunk(chunk, x, y, z, trunkHeight, BlockType.Wood);

            int leavesLayers = rng.Next(1, 3);
            int leavesStartY = y + trunkHeight;
            for (int ly = 0; ly < leavesLayers; ly++)
            {
                int leavesY = leavesStartY + ly;
                if (leavesY >= Chunk.Height) break;
                PlaceLeavesCircle(chunk, x, leavesY, z, 2, BlockType.Leaves);
            }
        }

        /// <summary>
        /// 丛林树：8-12格高大树干，3-4层大圆冠（半径3，顶层1），树干四侧悬挂藤蔓。
        /// </summary>
        private void GenerateJungleTree(Chunk chunk, int x, int y, int z, System.Random rng)
        {
            int trunkHeight = rng.Next(8, 13);
            int topY = y + trunkHeight - 1;

            PlaceTrunk(chunk, x, y, z, trunkHeight, BlockType.Wood);

            int leavesLayers = rng.Next(3, 5);
            int leavesStartY = topY - 2;
            for (int ly = 0; ly < leavesLayers; ly++)
            {
                int leavesY = leavesStartY + ly;
                if (leavesY >= Chunk.Height) break;
                int radius = (ly == leavesLayers - 1) ? 1 : (ly == leavesLayers - 2 ? 2 : 3);
                PlaceLeavesCircle(chunk, x, leavesY, z, radius, BlockType.JungleLeaves);
            }

            PlaceVines(chunk, x, topY, z, rng);
        }

        /// <summary>
        /// 在树干四侧悬挂藤蔓：从树干上部向下垂落 2-4 格 Vine 方块。
        /// </summary>
        private void PlaceVines(Chunk chunk, int trunkX, int topY, int trunkZ, System.Random rng)
        {
            // 四个水平方向偏移
            int[] dxs = { 1, -1, 0, 0 };
            int[] dzs = { 0, 0, 1, -1 };

            for (int i = 0; i < 4; i++)
            {
                if (rng.NextDouble() >= 0.6f)
                    continue;

                int vx = trunkX + dxs[i];
                int vz = trunkZ + dzs[i];
                int startY = topY - rng.Next(0, 3);
                int len = rng.Next(2, 5);

                for (int k = 0; k < len; k++)
                {
                    int vy = startY - k;
                    if (vy < 1) break;
                    if (chunk.GetBlock(vx, vy, vz) == BlockType.Air)
                        chunk.SetBlock(vx, vy, vz, BlockType.Vine);
                    else
                        break;
                }
            }
        }

        // ==================== 树木辅助方法 ====================

        /// <summary>
        /// 放置竖直树干：从 (x,y,z) 向上放置 height 个 type 方块。
        /// </summary>
        private void PlaceTrunk(Chunk chunk, int x, int y, int z, int height, BlockType type)
        {
            for (int i = 0; i < height; i++)
            {
                if (y + i >= Chunk.Height) break;
                chunk.SetBlock(x, y + i, z, type);
            }
        }

        /// <summary>
        /// 在指定高度放置一层圆形树叶。
        /// 遍历以 (cx,cz) 为中心、radius 为半径的正方形，用圆形裁剪（dx²+dz² <= r²+1），
        /// 仅在目标为空气时放置，避免覆盖树干或已有方块。
        /// </summary>
        private void PlaceLeavesCircle(Chunk chunk, int cx, int cy, int cz,
            int radius, BlockType leafType)
        {
            if (cy < 0 || cy >= Chunk.Height) return;

            int r2 = radius * radius + 1; // 圆形裁剪阈值（+1 使边缘略饱满）
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dz * dz > r2)
                        continue;
                    int lx = cx + dx, lz = cz + dz;
                    if (chunk.GetBlock(lx, cy, lz) == BlockType.Air)
                        chunk.SetBlock(lx, cy, lz, leafType);
                }
            }
        }

        // ==================== 地表装饰 ====================

        /// <summary>
        /// 根据生物群系放置地表装饰。
        /// 草地：草丛/花/小石头；沙漠：仙人掌/枯灌木；雪原：冰块/雪堆；丛林：蘑菇/草丛。
        /// 仅在水面以上、地表匹配时装饰。
        /// </summary>
        private void PlantDecorations(Chunk chunk, int[,] heightMap, Biome[,] biomeMap)
        {
            System.Random rng = CreateChunkRandom(chunk);
            // 跳过若干随机数，避免与树木生成序列耦合
            rng.Next();
            rng.Next();

            for (int x = 0; x < Chunk.Width; x++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    int surfaceY = heightMap[x, z];
                    if (surfaceY + 1 >= Chunk.Height)
                        continue;
                    // 水下不装饰
                    if (surfaceY < SeaLevel)
                        continue;

                    Biome biome = biomeMap[x, z];
                    BlockType surface = chunk.GetBlock(x, surfaceY, z);

                    switch (biome)
                    {
                        case Biome.Grass:
                            if (surface == BlockType.Grass)
                                DecorateGrass(chunk, x, surfaceY, z, rng);
                            break;
                        case Biome.Desert:
                            if (surface == BlockType.Sand)
                                DecorateDesert(chunk, x, surfaceY, z, rng);
                            break;
                        case Biome.Snow:
                            if (surface == BlockType.Snow)
                                DecorateSnow(chunk, x, surfaceY, z, rng);
                            break;
                        case Biome.Jungle:
                            if (surface == BlockType.JungleGrass)
                                DecorateJungle(chunk, x, surfaceY, z, rng);
                            break;
                    }
                }
            }
        }

        /// <summary>草地装饰：红花/黄花/小石头（草丛改用预制体 Cross 模型，不在方块层生成）。</summary>
        private void DecorateGrass(Chunk chunk, int x, int surfaceY, int z, System.Random rng)
        {
            if (rng.NextDouble() >= DecorationChance)
                return;

            double roll = rng.NextDouble();
            if (roll < 0.45)
            {
                // 红花
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.FlowerRed);
            }
            else if (roll < 0.85)
            {
                // 黄花
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.FlowerYellow);
            }
            else
            {
                // 小石头
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.Cobblestone);
            }
        }

        /// <summary>沙漠装饰：仙人掌(3-4格竖列)/枯灌木(Wood单格)/小圆石。</summary>
        private void DecorateDesert(Chunk chunk, int x, int surfaceY, int z, System.Random rng)
        {
            if (rng.NextDouble() >= DecorationChance)
                return;

            double roll = rng.NextDouble();
            if (roll < 0.4)
            {
                // 仙人掌：3-4 格 Cactus 竖列
                int h = rng.Next(3, 5);
                for (int k = 1; k <= h; k++)
                    TryPlaceAbove(chunk, x, surfaceY + k, z, BlockType.Cactus);
            }
            else if (roll < 0.7)
            {
                // 枯灌木：单格 Wood
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.Wood);
            }
            else
            {
                // 小圆石
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.Cobblestone);
            }
        }

        /// <summary>雪原装饰：冰块/雪堆。</summary>
        private void DecorateSnow(Chunk chunk, int x, int surfaceY, int z, System.Random rng)
        {
            if (rng.NextDouble() >= DecorationChance)
                return;

            double roll = rng.NextDouble();
            if (roll < 0.35)
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.Ice);
            else
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.Snow);
        }

        /// <summary>丛林装饰：蘑菇/草丛。</summary>
        private void DecorateJungle(Chunk chunk, int x, int surfaceY, int z, System.Random rng)
        {
            if (rng.NextDouble() >= DecorationChance)
                return;

            double roll = rng.NextDouble();
            if (roll < 0.4)
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.Mushroom);
            else
                TryPlaceAbove(chunk, x, surfaceY + 1, z, BlockType.Leaves);
        }

        /// <summary>
        /// 在指定位置放置方块，仅当目标为空气时放置（避免覆盖已有方块）。
        /// </summary>
        private void TryPlaceAbove(Chunk chunk, int x, int y, int z, BlockType type)
        {
            if (y < 0 || y >= Chunk.Height) return;
            if (chunk.GetBlock(x, y, z) == BlockType.Air)
                chunk.SetBlock(x, y, z, type);
        }

        // ==================== 随机数 ====================

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

        // ==================== 云层 ====================

        /// <summary>
        /// 在高空生成云层。用 Perlin 噪声决定云的形状，白色半透明 Cloud 方块聚集。
        /// 云层高度固定在 Y=58~62，不随地形变化，形成稳定的天空层。
        /// </summary>
        private void GenerateClouds(Chunk chunk)
        {
            // 云层高度：Chunk.Height=64，地表最高 MaxHeight=60，云层放在 58~62
            // 在雪原山脉区域云雾缭绕山顶，其他区域云层飘浮空中
            const int cloudBaseY = 58;
            const int cloudThickness = 4;
            // 云噪声频率：低频形成大块云朵
            const float cloudFrequency = 0.04f;
            // 云密度阈值：噪声值高于此值才放云方块
            const float cloudThreshold = 0.62f;

            int worldOriginX = chunk.ChunkX * Chunk.Width;
            int worldOriginZ = chunk.ChunkZ * Chunk.Depth;

            for (int x = 0; x < Chunk.Width; x++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    int wx = worldOriginX + x;
                    int wz = worldOriginZ + z;

                    // 采样云噪声
                    float cloudNoise = Mathf.PerlinNoise(
                        (wx + _seed * 1.3f) * cloudFrequency,
                        (wz + _seed * 1.3f) * cloudFrequency);
                    cloudNoise = Mathf.Clamp01(cloudNoise);

                    if (cloudNoise < cloudThreshold)
                        continue;

                    // 噪声值越高，云越厚
                    int thickness = Mathf.RoundToInt(
                        (cloudNoise - cloudThreshold) / (1f - cloudThreshold) * cloudThickness) + 1;
                    thickness = Mathf.Clamp(thickness, 1, cloudThickness);

                    for (int y = 0; y < thickness; y++)
                    {
                        int cy = cloudBaseY + y;
                        if (cy >= Chunk.Height)
                            break;
                        if (chunk.GetBlock(x, cy, z) == BlockType.Air)
                            chunk.SetBlock(x, cy, z, BlockType.Cloud);
                    }
                }
            }
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
