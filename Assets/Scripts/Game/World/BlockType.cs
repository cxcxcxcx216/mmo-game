using UnityEngine;

namespace Minecraft.Game.World
{
    /// <summary>
    /// 方块类型枚举。每个值对应一种方块（空气、草、土、石头等）。
    /// 使用 byte 存储以节省内存（一个区块 16x16x256 = 65536 个方块）。
    /// </summary>
    public enum BlockType : byte
    {
        Air = 0,        // 空气（透明，不渲染）
        Grass = 1,      // 草方块（顶面绿色，侧面土色）
        Dirt = 2,       // 泥土
        Stone = 3,      // 石头
        Cobblestone = 4,// 圆石
        Sand = 5,       // 沙子
        Wood = 6,       // 原木（树干）
        Leaves = 7,     // 树叶
        Water = 8,      // 水（半透明）
        Bedrock = 9,    // 基岩（不可破坏）
        Coal = 10,      // 煤矿
        Iron = 11,      // 铁矿
        Gold = 12,      // 金矿
        Diamond = 13,   // 钻石矿
        Planks = 14,    // 木板
        Glass = 15,     // 玻璃（透明）
    }

    /// <summary>
    /// 方块定义。描述每种方块的渲染属性（颜色、透明度、硬度等）。
    /// 使用静态数组缓存，避免每次查询都创建对象。
    /// </summary>
    public static class BlockDefinition
    {
        /// <summary>所有方块定义，索引即 BlockType 的 byte 值。</summary>
        public static readonly BlockInfo[] All = new BlockInfo[16];

        static BlockDefinition()
        {
            All[0] = new BlockInfo(BlockType.Air, "空气", new Color32(0, 0, 0, 0), isSolid: false, isTransparent: true);
            All[1] = new BlockInfo(BlockType.Grass, "草方块", new Color32(95, 159, 53, 255), topColor: new Color32(120, 190, 70, 255));
            All[2] = new BlockInfo(BlockType.Dirt, "泥土", new Color32(134, 96, 67, 255));
            All[3] = new BlockInfo(BlockType.Stone, "石头", new Color32(128, 128, 128, 255), hardness: 5);
            All[4] = new BlockInfo(BlockType.Cobblestone, "圆石", new Color32(110, 110, 110, 255), hardness: 5);
            All[5] = new BlockInfo(BlockType.Sand, "沙子", new Color32(218, 203, 137, 255));
            All[6] = new BlockInfo(BlockType.Wood, "原木", new Color32(102, 76, 42, 255), topColor: new Color32(160, 120, 70, 255));
            All[7] = new BlockInfo(BlockType.Leaves, "树叶", new Color32(60, 130, 45, 255), isTransparent: true);
            All[8] = new BlockInfo(BlockType.Water, "水", new Color32(60, 100, 200, 160), isSolid: false, isTransparent: true);
            All[9] = new BlockInfo(BlockType.Bedrock, "基岩", new Color32(60, 60, 60, 255), hardness: -1); // 不可破坏
            All[10] = new BlockInfo(BlockType.Coal, "煤矿", new Color32(48, 48, 48, 255), hardness: 5);
            All[11] = new BlockInfo(BlockType.Iron, "铁矿", new Color32(180, 150, 130, 255), hardness: 6);
            All[12] = new BlockInfo(BlockType.Gold, "金矿", new Color32(220, 190, 60, 255), hardness: 6);
            All[13] = new BlockInfo(BlockType.Diamond, "钻石矿", new Color32(100, 220, 220, 255), hardness: 7);
            All[14] = new BlockInfo(BlockType.Planks, "木板", new Color32(160, 120, 70, 255));
            All[15] = new BlockInfo(BlockType.Glass, "玻璃", new Color32(220, 240, 240, 120), isTransparent: true);
        }

        /// <summary>获取指定方块类型的定义。</summary>
        public static ref BlockInfo Get(BlockType type) => ref All[(int)type];

        /// <summary>判断方块是否为空气。</summary>
        public static bool IsAir(BlockType type) => type == BlockType.Air;

        /// <summary>判断方块是否为固体（可碰撞）。</summary>
        public static bool IsSolid(BlockType type) => All[(int)type].IsSolid;
    }

    /// <summary>
    /// 单个方块的静态属性定义。
    /// </summary>
    public struct BlockInfo
    {
        public BlockType Type;
        public string Name;
        public Color32 SideColor;   // 侧面/底面色
        public Color32 TopColor;    // 顶面色（如不指定则用 SideColor）
        public bool IsSolid;        // 是否固体（可碰撞）
        public bool IsTransparent;  // 是否透明（渲染时需要排序）
        public int Hardness;        // 硬度（-1 表示不可破坏，0 瞬间破坏）

        public BlockInfo(BlockType type, string name, Color32 sideColor,
            Color32? topColor = null, bool isSolid = true, bool isTransparent = false, int hardness = 1)
        {
            Type = type;
            Name = name;
            SideColor = sideColor;
            TopColor = topColor ?? sideColor;
            IsSolid = isSolid;
            IsTransparent = isTransparent;
            Hardness = hardness;
        }

        /// <summary>获取某一面（0-5）的颜色。</summary>
        public Color32 GetFaceColor(int faceIndex)
        {
            // faceIndex: 0=Top 1=Bottom 2-5=Side
            return faceIndex == 0 ? TopColor : SideColor;
        }
    }
}
