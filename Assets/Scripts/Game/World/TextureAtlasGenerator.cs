using UnityEngine;

namespace Minecraft.Game.World
{
    /// <summary>
    /// 程序化生成 Minecraft 风格的方块纹理图集。
    /// 生成 256x256 的 Texture2D，包含 16x16 像素图块，使用 SetPixel 逐像素绘制。
    /// 使用固定种子保证纹理一致，filterMode = Point 保持像素感。
    /// </summary>
    public static class TextureAtlasGenerator
    {
        // ==================== 图集常量 ====================

        /// <summary>图集边长（像素）。</summary>
        private const int AtlasSize = 256;

        /// <summary>单个图块边长（像素）。</summary>
        private const int TileSize = 16;

        /// <summary>每行图块数量。</summary>
        private const int TilesPerRow = AtlasSize / TileSize;

        /// <summary>固定随机种子，保证每次生成的纹理一致。</summary>
        private const int RandomSeed = 1337;

        // ==================== 图块索引（第一行：16种方块默认纹理）====================

        private const int TileGrass = 0;        // 草侧面
        private const int TileDirt = 1;         // 泥土
        private const int TileStone = 2;        // 石头
        private const int TileCobblestone = 3;  // 圆石
        private const int TileSand = 4;         // 沙子
        private const int TileWoodSide = 5;     // 原木侧面
        private const int TileLeaves = 6;       // 树叶
        private const int TileWater = 7;        // 水
        private const int TileBedrock = 8;      // 基岩
        private const int TileCoal = 9;         // 煤矿
        private const int TileIron = 10;        // 铁矿
        private const int TileGold = 11;        // 金矿
        private const int TileDiamond = 12;     // 钻石矿
        private const int TilePlanks = 13;      // 木板
        private const int TileGlass = 14;       // 玻璃
        private const int TileAir = 15;         // 空气（透明占位）

        // ==================== 图块索引（第二行：特殊面纹理）====================

        private const int TileGrassTop = 16;    // 草顶面
        private const int TileWoodTop = 17;     // 原木截面（年轮）

        // ==================== 单例缓存 ====================

        private static Texture2D _cachedAtlas;

        // ==================== 公开方法 ====================

        /// <summary>
        /// 获取生成的纹理图集（单例缓存）。首次调用时生成，后续返回缓存。
        /// </summary>
        /// <returns>256x256 的 Texture2D 图集，filterMode = Point。</returns>
        public static Texture2D GetAtlasTexture()
        {
            if (_cachedAtlas == null)
                _cachedAtlas = GenerateAtlas();
            return _cachedAtlas;
        }

        /// <summary>
        /// 获取指定方块指定面的 UV 坐标。
        /// 返回 4 个 Vector2，顺序与 ChunkMeshBuilder 的面顶点顺序一致。
        /// <para>faceIndex: 0=Top, 1=Bottom, 2=North, 3=South, 4=East, 5=West</para>
        /// <para>Grass 顶面使用草顶纹理；Wood 顶面/底面使用年轮截面。</para>
        /// </summary>
        /// <param name="type">方块类型。</param>
        /// <param name="faceIndex">面索引：0=Top 1=Bottom 2=North 3=South 4=East 5=West。</param>
        /// <returns>4 个 UV 坐标，对应面的 4 个顶点。</returns>
        public static Vector2[] GetUV(BlockType type, int faceIndex)
        {
            int tile = GetTileIndex(type, faceIndex);
            int baseX = (tile % TilesPerRow) * TileSize;
            int baseY = (tile / TilesPerRow) * TileSize;

            float uMin = baseX / (float)AtlasSize;
            float uMax = (baseX + TileSize) / (float)AtlasSize;
            float vMin = baseY / (float)AtlasSize;
            float vMax = (baseY + TileSize) / (float)AtlasSize;

            // 各面顶点顺序见 ChunkMeshBuilder.FaceVertices
            switch (faceIndex)
            {
                case 0: // Top: 顶点 (x=0,z=0)(x=0,z=1)(x=1,z=1)(x=1,z=0)
                    return new Vector2[]
                    {
                        new Vector2(uMin, vMin),
                        new Vector2(uMin, vMax),
                        new Vector2(uMax, vMax),
                        new Vector2(uMax, vMin),
                    };
                case 1: // Bottom: 顶点 (x=0,z=0)(x=1,z=0)(x=1,z=1)(x=0,z=1)
                    return new Vector2[]
                    {
                        new Vector2(uMin, vMin),
                        new Vector2(uMax, vMin),
                        new Vector2(uMax, vMax),
                        new Vector2(uMin, vMax),
                    };
                case 2: // North: 顶点 (x=0,y=0)(x=1,y=0)(x=1,y=1)(x=0,y=1)
                    return new Vector2[]
                    {
                        new Vector2(uMin, vMin),
                        new Vector2(uMax, vMin),
                        new Vector2(uMax, vMax),
                        new Vector2(uMin, vMax),
                    };
                case 3: // South: 顶点 (x=0,y=0)(x=0,y=1)(x=1,y=1)(x=1,y=0)
                    return new Vector2[]
                    {
                        new Vector2(uMin, vMin),
                        new Vector2(uMin, vMax),
                        new Vector2(uMax, vMax),
                        new Vector2(uMax, vMin),
                    };
                case 4: // East: 顶点 (z=0,y=0)(z=0,y=1)(z=1,y=1)(z=1,y=0)
                    return new Vector2[]
                    {
                        new Vector2(uMin, vMin),
                        new Vector2(uMin, vMax),
                        new Vector2(uMax, vMax),
                        new Vector2(uMax, vMin),
                    };
                case 5: // West: 顶点 (z=0,y=0)(z=1,y=0)(z=1,y=1)(z=0,y=1)
                    return new Vector2[]
                    {
                        new Vector2(uMin, vMin),
                        new Vector2(uMax, vMin),
                        new Vector2(uMax, vMax),
                        new Vector2(uMin, vMax),
                    };
                default:
                    return new Vector2[]
                    {
                        new Vector2(uMin, vMin),
                        new Vector2(uMax, vMin),
                        new Vector2(uMax, vMax),
                        new Vector2(uMin, vMax),
                    };
            }
        }

        // ==================== 图块索引映射 ====================

        /// <summary>
        /// 根据方块类型和面索引获取图块索引。
        /// Grass 顶面使用草顶纹理；Wood 顶面/底面使用年轮截面。
        /// </summary>
        private static int GetTileIndex(BlockType type, int faceIndex)
        {
            switch (type)
            {
                case BlockType.Grass:
                    return faceIndex == 0 ? TileGrassTop : TileGrass;
                case BlockType.Wood:
                    return (faceIndex == 0 || faceIndex == 1) ? TileWoodTop : TileWoodSide;
                case BlockType.Dirt:        return TileDirt;
                case BlockType.Stone:       return TileStone;
                case BlockType.Cobblestone: return TileCobblestone;
                case BlockType.Sand:        return TileSand;
                case BlockType.Leaves:      return TileLeaves;
                case BlockType.Water:       return TileWater;
                case BlockType.Bedrock:     return TileBedrock;
                case BlockType.Coal:        return TileCoal;
                case BlockType.Iron:        return TileIron;
                case BlockType.Gold:        return TileGold;
                case BlockType.Diamond:     return TileDiamond;
                case BlockType.Planks:      return TilePlanks;
                case BlockType.Glass:       return TileGlass;
                default:                    return TileAir;
            }
        }

        // ==================== 图集生成 ====================

        /// <summary>
        /// 生成纹理图集。创建 256x256 Texture2D，逐像素绘制所有图块。
        /// </summary>
        private static Texture2D GenerateAtlas()
        {
            var atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false);
            atlas.filterMode = FilterMode.Point;   // 像素感，不使用平滑过滤
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.name = "ProceduralBlockAtlas";

            // 使用固定种子，保证每次生成的纹理一致
            var rng = new System.Random(RandomSeed);

            // 第一行：16种方块默认纹理
            DrawGrassSide(atlas, rng);
            DrawDirt(atlas, rng);
            DrawStone(atlas, rng);
            DrawCobblestone(atlas, rng);
            DrawSand(atlas, rng);
            DrawWoodSide(atlas, rng);
            DrawLeaves(atlas, rng);
            DrawWater(atlas, rng);
            DrawBedrock(atlas, rng);
            DrawOre(atlas, rng, TileCoal, new Color(0.05f, 0.05f, 0.05f, 1f));
            DrawOre(atlas, rng, TileIron, new Color(0.7f, 0.5f, 0.35f, 1f));
            DrawOre(atlas, rng, TileGold, new Color(0.9f, 0.75f, 0.15f, 1f));
            DrawOre(atlas, rng, TileDiamond, new Color(0.3f, 0.85f, 0.85f, 1f));
            DrawPlanks(atlas, rng);
            DrawGlass(atlas, rng);
            DrawAir(atlas);

            // 第二行：特殊面纹理
            DrawGrassTop(atlas, rng);
            DrawWoodTop(atlas, rng);

            atlas.Apply();
            return atlas;
        }

        // ==================== 像素绘制辅助 ====================

        /// <summary>设置图块内某像素的颜色。</summary>
        /// <param name="atlas">目标图集。</param>
        /// <param name="tileIndex">图块索引。</param>
        /// <param name="lx">图块内 X 坐标（0~15）。</param>
        /// <param name="ly">图块内 Y 坐标（0~15）。</param>
        /// <param name="color">像素颜色。</param>
        private static void SetTilePixel(Texture2D atlas, int tileIndex, int lx, int ly, Color color)
        {
            int x = (tileIndex % TilesPerRow) * TileSize + lx;
            int y = (tileIndex / TilesPerRow) * TileSize + ly;
            atlas.SetPixel(x, y, color);
        }

        /// <summary>在基准色上添加随机噪点变化。</summary>
        /// <param name="baseColor">基准颜色。</param>
        /// <param name="amplitude">噪点幅度（0~1）。</param>
        /// <param name="rng">随机数生成器。</param>
        /// <returns>添加噪点后的颜色。</returns>
        private static Color NoiseColor(Color baseColor, float amplitude, System.Random rng)
        {
            float n = (float)rng.NextDouble() * amplitude - amplitude * 0.5f;
            return new Color(
                Mathf.Clamp01(baseColor.r + n),
                Mathf.Clamp01(baseColor.g + n),
                Mathf.Clamp01(baseColor.b + n),
                baseColor.a);
        }

        // ==================== 各方块纹理绘制 ====================

        /// <summary>草侧面：上部草绿色，下部泥土色，边界不规则。</summary>
        private static void DrawGrassSide(Texture2D atlas, System.Random rng)
        {
            Color grass = new Color(0.45f, 0.65f, 0.25f);
            Color dirt = new Color(0.55f, 0.4f, 0.25f);

            for (int x = 0; x < TileSize; x++)
            {
                // 草层高度 3~5 像素，边界不规则
                int grassH = 3 + rng.Next(0, 3);
                for (int y = 0; y < TileSize; y++)
                {
                    // y=0 是底部，草在顶部（高 y 值）
                    Color c = (y >= TileSize - grassH) ? grass : dirt;
                    SetTilePixel(atlas, TileGrass, x, y, NoiseColor(c, 0.15f, rng));
                }
            }
        }

        /// <summary>草顶面：草绿色带噪点。</summary>
        private static void DrawGrassTop(Texture2D atlas, System.Random rng)
        {
            Color grass = new Color(0.45f, 0.65f, 0.25f);
            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                    SetTilePixel(atlas, TileGrassTop, x, y, NoiseColor(grass, 0.18f, rng));
        }

        /// <summary>泥土：棕色带小石子噪点。</summary>
        private static void DrawDirt(Texture2D atlas, System.Random rng)
        {
            Color dirt = new Color(0.55f, 0.4f, 0.25f);
            Color stone = new Color(0.35f, 0.3f, 0.25f);

            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                {
                    // 约 20% 概率出现小石子
                    Color c = rng.Next(0, 5) == 0 ? stone : dirt;
                    SetTilePixel(atlas, TileDirt, x, y, NoiseColor(c, 0.12f, rng));
                }
        }

        /// <summary>石头：灰色带裂纹纹理。</summary>
        private static void DrawStone(Texture2D atlas, System.Random rng)
        {
            Color stone = new Color(0.5f, 0.5f, 0.5f);
            Color crack = new Color(0.3f, 0.3f, 0.3f);

            // 先铺石头底色
            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                    SetTilePixel(atlas, TileStone, x, y, NoiseColor(stone, 0.1f, rng));

            // 绘制几条随机裂纹
            for (int i = 0; i < 3; i++)
            {
                int cx = rng.Next(2, TileSize - 2);
                int cy = rng.Next(2, TileSize - 2);
                int len = 3 + rng.Next(0, 4);
                for (int j = 0; j < len; j++)
                {
                    if (cx < 0 || cx >= TileSize || cy < 0 || cy >= TileSize)
                        break;
                    SetTilePixel(atlas, TileStone, cx, cy, crack);
                    cx += rng.Next(-1, 2);
                    cy += rng.Next(-1, 2);
                }
            }
        }

        /// <summary>圆石：不规则石块拼接图案。</summary>
        private static void DrawCobblestone(Texture2D atlas, System.Random rng)
        {
            Color mortar = new Color(0.2f, 0.2f, 0.2f);

            // 先全部填充为砂浆色（缝隙）
            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                    SetTilePixel(atlas, TileCobblestone, x, y, mortar);

            // 定义几个不规则石块区域：x, y, w, h
            int[,] blocks =
            {
                { 0, 0, 6, 5 },   { 7, 0, 5, 6 },   { 13, 0, 3, 4 },
                { 0, 6, 4, 5 },   { 5, 7, 6, 4 },   { 12, 5, 4, 6 },
                { 0, 11, 5, 5 },  { 6, 12, 5, 4 },  { 11, 12, 5, 4 },
            };

            // 逐块填充不同灰度
            for (int i = 0; i < blocks.GetLength(0); i++)
            {
                float shade = 0.42f + (float)rng.NextDouble() * 0.16f;
                Color blockColor = new Color(shade, shade, shade);
                int bx = blocks[i, 0], by = blocks[i, 1];
                int bw = blocks[i, 2], bh = blocks[i, 3];
                for (int x = bx; x < bx + bw && x < TileSize; x++)
                    for (int y = by; y < by + bh && y < TileSize; y++)
                        SetTilePixel(atlas, TileCobblestone, x, y, NoiseColor(blockColor, 0.1f, rng));
            }
        }

        /// <summary>沙子：浅黄色带细沙噪点。</summary>
        private static void DrawSand(Texture2D atlas, System.Random rng)
        {
            Color sand = new Color(0.85f, 0.8f, 0.55f);
            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                    SetTilePixel(atlas, TileSand, x, y, NoiseColor(sand, 0.08f, rng));
        }

        /// <summary>原木侧面：竖向木纹纹理。</summary>
        private static void DrawWoodSide(Texture2D atlas, System.Random rng)
        {
            Color bark = new Color(0.45f, 0.32f, 0.18f);
            Color darkBark = new Color(0.35f, 0.25f, 0.14f);

            for (int x = 0; x < TileSize; x++)
            {
                // 每隔几像素一条暗色木纹
                Color col = (x % 4 == 0 || x % 7 == 0) ? darkBark : bark;
                for (int y = 0; y < TileSize; y++)
                    SetTilePixel(atlas, TileWoodSide, x, y, NoiseColor(col, 0.1f, rng));
            }
        }

        /// <summary>原木截面：年轮图案。</summary>
        private static void DrawWoodTop(Texture2D atlas, System.Random rng)
        {
            Color ring = new Color(0.6f, 0.45f, 0.28f);
            Color darkRing = new Color(0.4f, 0.3f, 0.18f);

            float cx = TileSize * 0.5f;
            float cy = TileSize * 0.5f;

            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                {
                    // 计算到中心的距离，按距离分段形成年轮
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    Color c = ((int)(dist * 1.2f) % 3 == 0) ? darkRing : ring;
                    SetTilePixel(atlas, TileWoodTop, x, y, NoiseColor(c, 0.08f, rng));
                }
        }

        /// <summary>树叶：绿色叶片纹理，带透明孔洞。</summary>
        private static void DrawLeaves(Texture2D atlas, System.Random rng)
        {
            Color leaf = new Color(0.3f, 0.55f, 0.2f);
            Color darkLeaf = new Color(0.2f, 0.4f, 0.15f);
            Color transparent = new Color(0, 0, 0, 0);

            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                {
                    int r = rng.Next(0, 10);
                    if (r < 2)
                        SetTilePixel(atlas, TileLeaves, x, y, transparent);       // 透明孔洞
                    else if (r < 5)
                        SetTilePixel(atlas, TileLeaves, x, y, NoiseColor(darkLeaf, 0.1f, rng));
                    else
                        SetTilePixel(atlas, TileLeaves, x, y, NoiseColor(leaf, 0.12f, rng));
                }
        }

        /// <summary>水：蓝色波纹。</summary>
        private static void DrawWater(Texture2D atlas, System.Random rng)
        {
            Color water = new Color(0.25f, 0.4f, 0.8f, 0.7f);
            Color lightWater = new Color(0.35f, 0.5f, 0.9f, 0.7f);

            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                {
                    // 正弦波纹
                    float wave = Mathf.Sin(x * 0.8f) * Mathf.Cos(y * 0.6f);
                    Color c = wave > 0 ? lightWater : water;
                    SetTilePixel(atlas, TileWater, x, y, NoiseColor(c, 0.05f, rng));
                }
        }

        /// <summary>基岩：深灰黑色不规则图案。</summary>
        private static void DrawBedrock(Texture2D atlas, System.Random rng)
        {
            Color dark = new Color(0.2f, 0.2f, 0.2f);
            Color darker = new Color(0.1f, 0.1f, 0.1f);

            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                {
                    Color c = rng.Next(0, 3) == 0 ? darker : dark;
                    SetTilePixel(atlas, TileBedrock, x, y, NoiseColor(c, 0.08f, rng));
                }
        }

        /// <summary>矿石：石头底色 + 指定颜色矿斑。</summary>
        /// <param name="oreColor">矿石颜色。</param>
        private static void DrawOre(Texture2D atlas, System.Random rng, int tileIndex, Color oreColor)
        {
            Color stone = new Color(0.5f, 0.5f, 0.5f);

            // 先铺石头底色
            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                    SetTilePixel(atlas, tileIndex, x, y, NoiseColor(stone, 0.1f, rng));

            // 撒矿斑：随机选几个位置，每个位置画 2x2 矿斑
            int spotCount = 3 + rng.Next(0, 3);
            for (int i = 0; i < spotCount; i++)
            {
                int ox = rng.Next(1, TileSize - 2);
                int oy = rng.Next(1, TileSize - 2);
                SetTilePixel(atlas, tileIndex, ox, oy, NoiseColor(oreColor, 0.1f, rng));
                SetTilePixel(atlas, tileIndex, ox + 1, oy, NoiseColor(oreColor, 0.1f, rng));
                SetTilePixel(atlas, tileIndex, ox, oy + 1, NoiseColor(oreColor, 0.1f, rng));
                SetTilePixel(atlas, tileIndex, ox + 1, oy + 1, NoiseColor(oreColor, 0.1f, rng));
            }
        }

        /// <summary>木板：横向木板纹理。</summary>
        private static void DrawPlanks(Texture2D atlas, System.Random rng)
        {
            Color plank = new Color(0.6f, 0.45f, 0.28f);
            Color darkLine = new Color(0.4f, 0.3f, 0.18f);

            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                {
                    // 每 4 像素一条木板分隔线
                    Color c = (y % 4 == 0) ? darkLine : plank;
                    SetTilePixel(atlas, TilePlanks, x, y, NoiseColor(c, 0.08f, rng));
                }
        }

        /// <summary>玻璃：浅蓝色半透明，带边框。</summary>
        private static void DrawGlass(Texture2D atlas, System.Random rng)
        {
            Color glass = new Color(0.85f, 0.95f, 0.95f, 0.35f);
            Color frame = new Color(0.7f, 0.85f, 0.85f, 0.8f);

            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                {
                    // 最外圈 1 像素为边框
                    bool isBorder = x == 0 || x == TileSize - 1 || y == 0 || y == TileSize - 1;
                    Color c = isBorder ? frame : glass;
                    SetTilePixel(atlas, TileGlass, x, y, NoiseColor(c, 0.04f, rng));
                }
        }

        /// <summary>空气：全透明（占位）。</summary>
        private static void DrawAir(Texture2D atlas)
        {
            Color transparent = new Color(0, 0, 0, 0);
            for (int x = 0; x < TileSize; x++)
                for (int y = 0; y < TileSize; y++)
                    SetTilePixel(atlas, TileAir, x, y, transparent);
        }
    }
}
