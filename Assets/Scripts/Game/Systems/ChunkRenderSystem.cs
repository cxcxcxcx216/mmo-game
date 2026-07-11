using System.Collections.Generic;
using UnityEngine;
using Minecraft.Core.ECS;
using Minecraft.Game.World;

namespace Minecraft.Game.Systems
{
    /// <summary>
    /// 区块渲染系统。负责为每个已加载区块创建/更新 GameObject 与 Mesh，
    /// 将 <see cref="ChunkMeshBuilder"/> 构建的几何数据呈现到屏幕上。
    /// 设计要点：
    /// 1. 固体与透明方块分别使用独立 Mesh，透明 Mesh 通过子物体渲染（不同混合模式）。
    /// 2. 所有区块共享同一固体材质与同一透明材质，降低 Draw Call 与内存开销。
    /// 3. 每帧重建数量有上限（默认 4），避免大量 Mesh 构建造成掉帧。
    /// </summary>
    public class ChunkRenderSystem : SystemBase
    {
        /// <summary>每帧最多重建的区块数量（防止 Mesh 构建造成掉帧）。</summary>
        private const int MaxRebuildsPerFrame = 4;

        /// <summary>区块管理器（注入）。提供已加载区块的查询与跨区块邻接方块查询。</summary>
        [SerializeField] private ChunkManager _chunkManager;

        /// <summary>所有区块的渲染数据，键为 (chunkX, chunkZ)。</summary>
        private readonly Dictionary<(int cx, int cz), ChunkRenderData> _renderData =
            new Dictionary<(int cx, int cz), ChunkRenderData>();

        /// <summary>固体方块共享材质（缓存）。</summary>
        private Material _solidMaterial;

        /// <summary>透明方块共享材质（缓存）。</summary>
        private Material _transparentMaterial;

        /// <summary>区块管理器（支持运行时注入）。</summary>
        public ChunkManager ChunkManager
        {
            get => _chunkManager;
            set => _chunkManager = value;
        }

        /// <summary>当前缓存的渲染对象数量。</summary>
        public int RenderDataCount => _renderData.Count;

        /// <summary>每帧更新：同步区块状态并按需重建 Mesh。</summary>
        public override void OnUpdate(float deltaTime)
        {
            if (_chunkManager == null)
                return;

            EnsureMaterials();

            // 阶段 1：清理已卸载区块对应的渲染对象
            CleanupUnloadedChunks();

            // 阶段 2：为新加载的区块创建渲染对象，并标记需要重建的区块
            foreach (Chunk chunk in _chunkManager.GetAllLoadedChunks())
            {
                var key = (chunk.ChunkX, chunk.ChunkZ);
                if (!_renderData.TryGetValue(key, out ChunkRenderData data))
                {
                    data = CreateRenderData(chunk);
                    _renderData[key] = data;
                }
                if (chunk.IsModified)
                    data.NeedsRebuild = true;
            }

            // 阶段 3：重建 Mesh（每帧限制数量，超出部分顺延至下一帧）
            int rebuilt = 0;
            foreach (var kvp in _renderData)
            {
                if (rebuilt >= MaxRebuildsPerFrame)
                    break;

                ChunkRenderData data = kvp.Value;
                if (!data.NeedsRebuild)
                    continue;

                // 重新查询区块引用，避免使用可能已过期（卸载后重建）的旧引用
                Chunk chunk = _chunkManager.GetChunk(kvp.Key.cx, kvp.Key.cz);
                if (chunk == null)
                {
                    data.NeedsRebuild = false;
                    continue;
                }

                RebuildMeshes(data, chunk);
                data.NeedsRebuild = false;
                chunk.IsModified = false;
                rebuilt++;
            }
        }

        /// <summary>系统销毁：清理所有渲染对象与共享材质，避免 Unity 对象泄漏。</summary>
        protected override void OnShutdown()
        {
            foreach (var kvp in _renderData)
                DestroyRenderData(kvp.Value);
            _renderData.Clear();

            if (_solidMaterial != null)
            {
                Destroy(_solidMaterial);
                _solidMaterial = null;
            }
            if (_transparentMaterial != null)
            {
                Destroy(_transparentMaterial);
                _transparentMaterial = null;
            }
        }

        // ==================== 内部逻辑 ====================

        /// <summary>确保共享材质已创建（懒加载，兼容未调用 Initialize 的场景）。</summary>
        private void EnsureMaterials()
        {
            if (_solidMaterial == null)
                _solidMaterial = CreateSolidMaterial();
            if (_transparentMaterial == null)
                _transparentMaterial = CreateTransparentMaterial();
        }

        /// <summary>销毁已不在 ChunkManager 中的区块渲染对象。</summary>
        private void CleanupUnloadedChunks()
        {
            // 先收集待移除的键，避免在遍历字典时修改字典
            var toRemove = new List<(int cx, int cz)>();
            foreach (var kvp in _renderData)
            {
                if (!_chunkManager.HasChunk(kvp.Key.cx, kvp.Key.cz))
                    toRemove.Add(kvp.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                if (_renderData.TryGetValue(toRemove[i], out ChunkRenderData data))
                {
                    DestroyRenderData(data);
                    _renderData.Remove(toRemove[i]);
                }
            }
        }

        /// <summary>
        /// 为区块创建渲染对象（GameObject + MeshFilter + MeshRenderer + Mesh）。
        /// GameObject 位置对齐到区块在世界中的原点。
        /// </summary>
        private ChunkRenderData CreateRenderData(Chunk chunk)
        {
            var data = new ChunkRenderData();

            // 创建 GameObject。位置设为原点：ChunkMeshBuilder 已将世界坐标烘焙进顶点，
            // 若再偏移 GameObject 会导致坐标翻倍。
            var go = new GameObject($"Chunk_{chunk.ChunkX}_{chunk.ChunkZ}");
            go.transform.SetParent(transform, false);
            go.transform.position = Vector3.zero;

            // 添加 MeshFilter + MeshRenderer + MeshCollider
            // MeshCollider 让 CharacterController 能与方块世界产生物理碰撞
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _solidMaterial;
            var collider = go.AddComponent<MeshCollider>();
            collider.convex = false; // 非凸碰撞体，支持复杂方块表面

            // 创建固体 Mesh（使用 32 位索引，避免超过 65535 顶点上限）
            var solidMesh = new Mesh
            {
                name = $"Chunk_{chunk.ChunkX}_{chunk.ChunkZ}_Solid",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };
            filter.sharedMesh = solidMesh;

            // 创建透明 Mesh（暂不挂载，待检测到透明方块时再创建子物体渲染）
            var transparentMesh = new Mesh
            {
                name = $"Chunk_{chunk.ChunkX}_{chunk.ChunkZ}_Transparent",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };

            data.GameObject = go;
            data.MeshFilter = filter;
            data.MeshRenderer = renderer;
            data.SolidMesh = solidMesh;
            data.TransparentMesh = transparentMesh;
            data.NeedsRebuild = true; // 新建区块需要立即构建 Mesh
            return data;
        }

        /// <summary>重建指定区块的固体与透明 Mesh，并更新碰撞体。</summary>
        private void RebuildMeshes(ChunkRenderData data, Chunk chunk)
        {
            // 构建固体 Mesh，同时检测区块是否包含透明方块
            ChunkMeshBuilder.BuildMesh(chunk, _chunkManager, data.SolidMesh, out bool hasTransparent);

            // 更新 MeshCollider（固体 Mesh 作为碰撞体）
            var collider = data.GameObject.GetComponent<MeshCollider>();
            if (collider != null)
                collider.sharedMesh = data.SolidMesh;

            if (hasTransparent)
            {
                // 构建透明 Mesh 并确保透明渲染子物体存在
                ChunkMeshBuilder.BuildTransparentMesh(chunk, _chunkManager, data.TransparentMesh);
                EnsureTransparentRenderer(data);
            }
            else
            {
                // 无透明方块：清空透明 Mesh 并隐藏子物体
                if (data.TransparentMesh.vertexCount > 0)
                    data.TransparentMesh.Clear();
                if (data.TransparentGameObject != null && data.TransparentGameObject.activeSelf)
                    data.TransparentGameObject.SetActive(false);
            }
        }

        /// <summary>确保透明渲染子物体存在并激活（按需创建，复用以减少分配）。</summary>
        private void EnsureTransparentRenderer(ChunkRenderData data)
        {
            if (data.TransparentGameObject == null)
            {
                var tgo = new GameObject(data.GameObject.name + "_Transparent");
                tgo.transform.SetParent(data.GameObject.transform, false);
                tgo.transform.localPosition = Vector3.zero;

                var tfilter = tgo.AddComponent<MeshFilter>();
                var trenderer = tgo.AddComponent<MeshRenderer>();
                trenderer.sharedMaterial = _transparentMaterial;
                tfilter.sharedMesh = data.TransparentMesh;

                data.TransparentGameObject = tgo;
                data.TransparentMeshFilter = tfilter;
                data.TransparentMeshRenderer = trenderer;
            }
            else if (!data.TransparentGameObject.activeSelf)
            {
                data.TransparentGameObject.SetActive(true);
            }
        }

        /// <summary>销毁渲染对象关联的 GameObject 与 Mesh（父物体会连带销毁透明子物体）。</summary>
        private void DestroyRenderData(ChunkRenderData data)
        {
            if (data.GameObject != null)
                Destroy(data.GameObject);
            if (data.SolidMesh != null)
                Destroy(data.SolidMesh);
            if (data.TransparentMesh != null)
                Destroy(data.TransparentMesh);
        }

        // ==================== 材质创建 ====================

        /// <summary>创建固体方块共享材质（Standard 不透明，使用程序化纹理图集，开启 GPU 实例化）。</summary>
        private static Material CreateSolidMaterial()
        {
            Texture2D atlas = TextureAtlasGenerator.GetAtlasTexture();

            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");
            var mat = new Material(shader);
            mat.name = "Chunk_Solid";
            mat.mainTexture = atlas;
            mat.color = Color.white;

            // 固体方块使用不透明渲染模式
            mat.SetInt("_Mode", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

            // 启用 GPU 实例化（降低 Draw Call）
            mat.enableInstancing = true;
            return mat;
        }

        /// <summary>
        /// 创建透明方块共享材质（Standard 透明模式，使用程序化纹理图集）。
        /// Standard Shader 的 _Mode：0=Opaque 1=Cutout 2=Fade 3=Transparent。
        /// 仅设置 _Mode 不会真正切换混合，还需配套设置 Blend/ZWrite/关键字/渲染队列。
        /// </summary>
        private static Material CreateTransparentMaterial()
        {
            Texture2D atlas = TextureAtlasGenerator.GetAtlasTexture();

            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            mat.name = "Chunk_Transparent";
            mat.mainTexture = atlas;
            mat.color = Color.white;
            mat.enableInstancing = true;

            mat.SetInt("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        // ==================== 渲染数据结构 ====================

        /// <summary>
        /// 单个区块的渲染数据。持有渲染所需的 GameObject、组件与 Mesh。
        /// 固体方块与透明方块分别使用独立 Mesh；透明 Mesh 通过子物体渲染（不同混合模式）。
        /// </summary>
        private class ChunkRenderData
        {
            /// <summary>区块主物体（渲染固体 Mesh）。</summary>
            public GameObject GameObject;

            /// <summary>固体 Mesh 过滤器。</summary>
            public MeshFilter MeshFilter;

            /// <summary>固体 Mesh 渲染器。</summary>
            public MeshRenderer MeshRenderer;

            /// <summary>固体方块 Mesh。</summary>
            public Mesh SolidMesh;

            /// <summary>透明方块 Mesh。</summary>
            public Mesh TransparentMesh;

            /// <summary>透明渲染子物体（按需创建，作为主物体的子级）。</summary>
            public GameObject TransparentGameObject;

            /// <summary>透明 Mesh 过滤器。</summary>
            public MeshFilter TransparentMeshFilter;

            /// <summary>透明 Mesh 渲染器。</summary>
            public MeshRenderer TransparentMeshRenderer;

            /// <summary>是否需要重建 Mesh（用于每帧限流）。</summary>
            public bool NeedsRebuild;
        }
    }
}
