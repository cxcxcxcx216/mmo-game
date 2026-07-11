using System.Collections.Generic;
using UnityEngine;
using Minecraft.Core.ECS;
using Minecraft.Game.World;

namespace Minecraft.Game.Systems
{
    /// <summary>
    /// 区块加载系统。根据玩家位置动态加载/卸载区块，
    /// 确保玩家周围一定半径内的区块始终可用，远离的区块被回收。
    /// 加载检查有节流（默认每 0.5 秒一次），避免每帧遍历大量区块造成开销。
    /// 实际的地形生成与字典维护由 <see cref="ChunkManager"/> 负责，本系统仅负责调度。
    /// </summary>
    public class ChunkLoadSystem : SystemBase
    {
        /// <summary>加载检查间隔（秒）。每隔此时长执行一次加载/卸载判定。</summary>
        [SerializeField] private float _loadInterval = 0.5f;

        /// <summary>区块管理器（注入）。</summary>
        [SerializeField] private ChunkManager _chunkManager;

        /// <summary>玩家 Transform（注入）。加载以玩家所在区块为中心。</summary>
        [SerializeField] private Transform _playerTransform;

        /// <summary>加载计时器（累计 deltaTime，达到 _loadInterval 时触发一次）。</summary>
        private float _loadTimer;

        /// <summary>区块管理器（支持运行时注入）。</summary>
        public ChunkManager ChunkManager
        {
            get => _chunkManager;
            set => _chunkManager = value;
        }

        /// <summary>玩家 Transform（支持运行时注入）。</summary>
        public Transform PlayerTransform
        {
            get => _playerTransform;
            set => _playerTransform = value;
        }

        /// <summary>每帧更新：累计计时，到达间隔后执行加载与卸载。</summary>
        public override void OnUpdate(float deltaTime)
        {
            if (_chunkManager == null || _playerTransform == null)
                return;

            _loadTimer += deltaTime;
            if (_loadTimer < _loadInterval)
                return;
            _loadTimer = 0f;

            // 玩家世界坐标（取整数方块坐标）
            Vector3 pos = _playerTransform.position;
            int wx = Mathf.FloorToInt(pos.x);
            int wz = Mathf.FloorToInt(pos.z);

            // 加载玩家周围的区块
            int radius = _chunkManager.RenderRadius;
            _chunkManager.EnsureChunksAround(wx, wz, radius);

            // 卸载远离玩家的区块（留 2 格缓冲，避免在边界来回加载/卸载）
            int centerCX = FloorDiv(wx, Chunk.Width);
            int centerCZ = FloorDiv(wz, Chunk.Depth);
            UnloadDistantChunks(centerCX, centerCZ, radius + 2);
        }

        // ==================== 内部逻辑 ====================

        /// <summary>卸载距离玩家所在区块超过 <paramref name="maxRadius"/> 的区块。</summary>
        /// <param name="centerCX">玩家所在区块 X 坐标。</param>
        /// <param name="centerCZ">玩家所在区块 Z 坐标。</param>
        /// <param name="maxRadius">最大保留半径（单位：区块）。</param>
        private void UnloadDistantChunks(int centerCX, int centerCZ, int maxRadius)
        {
            // 先收集待卸载的区块坐标，避免在遍历 GetAllLoadedChunks 时修改底层字典
            var toUnload = new List<(int cx, int cz)>();
            int maxDistSq = maxRadius * maxRadius;

            foreach (Chunk chunk in _chunkManager.GetAllLoadedChunks())
            {
                int dx = chunk.ChunkX - centerCX;
                int dz = chunk.ChunkZ - centerCZ;
                if (dx * dx + dz * dz > maxDistSq)
                    toUnload.Add((chunk.ChunkX, chunk.ChunkZ));
            }

            for (int i = 0; i < toUnload.Count; i++)
                _chunkManager.UnloadChunk(toUnload[i].cx, toUnload[i].cz);
        }

        /// <summary>
        /// 整数向下取整除法。
        /// C# 的 / 运算符向零截断，负数需要修正（例如 -1 / 16 = 0，但向下取整应为 -1）。
        /// 与 <see cref="ChunkManager"/> 内部的坐标转换保持一致。
        /// </summary>
        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            return r < 0 ? q - 1 : q;
        }
    }
}
