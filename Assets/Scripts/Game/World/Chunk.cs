using System.Collections;
using System.Collections.Generic;

namespace Minecraft.Game.World
{
    /// <summary>
    /// 区块数据结构。一个区块是世界中固定大小的立方体区域。
    /// 尺寸：宽 16 × 高 64 × 深 16，共 16384 个方块。
    /// 使用 byte[] 存储所有方块类型，索引公式：index = x + z*16 + y*16*16。
    /// 此顺序让同一高度层（y 固定）的方块在内存中连续，便于 Mesh 构建。
    /// </summary>
    public class Chunk : IEnumerable<(int x, int y, int z, BlockType type)>
    {
        /// <summary>区块宽度（X 轴方向方块数）。</summary>
        public const int Width = 16;

        /// <summary>区块高度（Y 轴方向方块数）。</summary>
        public const int Height = 64;

        /// <summary>区块深度（Z 轴方向方块数）。</summary>
        public const int Depth = 16;

        /// <summary>一个区块包含的方块总数。</summary>
        public const int BlockCount = Width * Height * Depth;

        /// <summary>区块在世界 X 轴的坐标（世界坐标 = chunkX * Width）。</summary>
        public int ChunkX { get; }

        /// <summary>区块在世界 Z 轴的坐标（世界坐标 = chunkZ * Depth）。</summary>
        public int ChunkZ { get; }

        /// <summary>方块数据数组，每个 byte 对应一个 BlockType。</summary>
        private readonly byte[] _blocks;

        /// <summary>区块是否被玩家修改过（修改后需要重建 Mesh）。</summary>
        public bool IsModified { get; set; }

        /// <summary>区块是否已生成地形数据。</summary>
        public bool IsGenerated { get; set; }

        /// <summary>
        /// 构造区块。仅初始化数据数组，不生成地形。
        /// 地形由 TerrainGenerator 填充。
        /// </summary>
        /// <param name="chunkX">区块 X 坐标。</param>
        /// <param name="chunkZ">区块 Z 坐标。</param>
        public Chunk(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            _blocks = new byte[BlockCount];
            IsModified = false;
            IsGenerated = false;
        }

        /// <summary>
        /// 将局部坐标转换为数组索引。
        /// 索引公式：x + z*Width + y*Width*Depth。
        /// </summary>
        public static int GetIndex(int x, int y, int z)
        {
            return x + z * Width + y * Width * Depth;
        }

        /// <summary>
        /// 判断局部坐标是否在区块范围内。
        /// </summary>
        public static bool IsInBounds(int x, int y, int z)
        {
            return x >= 0 && x < Width
                && y >= 0 && y < Height
                && z >= 0 && z < Depth;
        }

        /// <summary>
        /// 获取局部坐标处的方块类型。带边界检查，越界返回 Air。
        /// </summary>
        public BlockType GetBlock(int x, int y, int z)
        {
            if (!IsInBounds(x, y, z))
                return BlockType.Air;
            return (BlockType)_blocks[GetIndex(x, y, z)];
        }

        /// <summary>
        /// 设置局部坐标处的方块类型。带边界检查，越界则忽略。
        /// </summary>
        public void SetBlock(int x, int y, int z, BlockType type)
        {
            if (!IsInBounds(x, y, z))
                return;
            _blocks[GetIndex(x, y, z)] = (byte)type;
            IsModified = true;
        }

        /// <summary>
        /// 通过世界坐标获取方块。自动将世界坐标转为区块内局部坐标。
        /// 若世界 Y 越界返回 Air。
        /// </summary>
        /// <param name="wx">世界 X 坐标。</param>
        /// <param name="wy">世界 Y 坐标。</param>
        /// <param name="wz">世界 Z 坐标。</param>
        public BlockType GetWorldBlock(int wx, int wy, int wz)
        {
            if (wy < 0 || wy >= Height)
                return BlockType.Air;

            int lx = wx - ChunkX * Width;
            int lz = wz - ChunkZ * Depth;
            return GetBlock(lx, wy, lz);
        }

        /// <summary>
        /// 通过世界坐标设置方块。自动将世界坐标转为区块内局部坐标。
        /// 若世界 Y 越界则忽略。
        /// </summary>
        public void SetWorldBlock(int wx, int wy, int wz, BlockType type)
        {
            if (wy < 0 || wy >= Height)
                return;

            int lx = wx - ChunkX * Width;
            int lz = wz - ChunkZ * Depth;
            SetBlock(lx, wy, lz, type);
        }

        /// <summary>
        /// 直接访问底层数组（无边界检查），供高性能批量操作使用。
        /// 调用方需保证 index 在 [0, BlockCount) 范围内。
        /// </summary>
        public byte[] GetRawData() => _blocks;

        /// <summary>
        /// 遍历区块内所有非空（非 Air）方块。
        /// 迭代顺序：x → z → y，与数组布局一致，缓存友好。
        /// </summary>
        public IEnumerator<(int x, int y, int z, BlockType type)> GetEnumerator()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int z = 0; z < Depth; z++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        BlockType type = (BlockType)_blocks[GetIndex(x, y, z)];
                        if (type != BlockType.Air)
                            yield return (x, y, z, type);
                    }
                }
            }
        }

        /// <summary>非泛型迭代器实现。</summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
