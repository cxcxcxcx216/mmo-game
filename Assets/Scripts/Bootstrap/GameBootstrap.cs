using UnityEngine;
using UnityEngine.EventSystems;
using Minecraft.Core.ECS;
using Minecraft.Game.World;
using Minecraft.Game.Systems;
using Minecraft.Game.Player;
using Minecraft.UI;
using Minecraft.MMO;
using Minecraft.Config;
using MMO.Protocol;

namespace Minecraft.Bootstrap
{
    /// <summary>
    /// 游戏入口引导器。负责创建和初始化所有子系统，管理登录→选角→进游戏的完整流程。
    /// <para>
    /// 启动阶段（Awake）：
    /// 1. 创建 <see cref="NetworkManager"/> 与 <see cref="GameStateManager"/>（单例，跨场景持久化）。
    /// 2. 创建 UI 系统（Canvas + <see cref="UIManager"/> + <see cref="LoginPanel"/> + <see cref="RoleListPanel"/>）。
    /// 3. 显示登录面板，等待用户操作。
    /// </para>
    /// <para>
    /// 进入游戏阶段（GameState → InGame）：
    /// 1. 创建方块世界（ChunkManager + 地形生成）。
    /// 2. 创建玩家（PlayerController + PlayerInteraction）。
    /// 3. 创建 ECS 系统（ChunkLoadSystem + ChunkRenderSystem）。
    /// 4. 创建游戏内 UI（HUD + 快捷栏 + 背包）。
    /// 5. 创建远程实体管理器与移动同步组件（在线模式）。
    /// </para>
    /// <para>
    /// 离线模式：跳过登录流程直接进入游戏，不创建网络同步组件。
    /// </para>
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        // ==================== 配置字段 ====================

        [Header("世界配置")]
        [SerializeField] private int terrainSeed = 1337;
        [SerializeField] private int renderRadius = 6;

        // ==================== 管理器引用 ====================

        private NetworkManager _networkManager;
        private GameStateManager _gameStateManager;
        private UIManager _uiManager;

        // ==================== 游戏世界引用 ====================

        private World _world;
        private ChunkManager _chunkManager;
        private ChunkLoadSystem _chunkLoadSystem;
        private ChunkRenderSystem _chunkRenderSystem;
        private PlayerController _playerController;
        private PlayerInteraction _playerInteraction;
        private RemoteEntityManager _remoteEntityManager;
        private PlayerNetworkSync _playerNetworkSync;

        /// <summary>游戏世界是否已创建（防止重复创建）。</summary>
        private bool _gameWorldCreated;

        // ==================== Unity 生命周期 ====================

        /// <summary>
        /// 启动入口：创建相机、管理器与 UI，监听状态变化，显示登录面板。
        /// 相机在启动时即创建，避免登录界面出现 "No cameras rendering"。
        /// </summary>
        private void Awake()
        {
            CleanupStaleObjects();

            // 加载配置表
            ConfigManager.LoadAll();

            _world = new World();

            CreateCamera();
            CreateManagers();
            CreateUISystem();

            // 监听游戏状态变化
            _gameStateManager.OnStateChanged += HandleStateChanged;
            _gameStateManager.OnEnterGameSuccess += HandleEnterGameSuccess;

            // 显示登录面板
            _uiManager.Show("LoginPanel");

            Debug.Log("[GameBootstrap] 启动完成，等待登录");
        }

        /// <summary>应用退出时清理。</summary>
        private void OnApplicationQuit()
        {
            _world = null;
        }

        /// <summary>清理场景中可能残留的运行时对象（防止误保存）。</summary>
        private void CleanupStaleObjects()
        {
            string[] staleNames = { "Chunk_", "[ChunkManager]", "[Player]", "[Camera]" };
            for (int i = 0; i < staleNames.Length; i++)
            {
                var found = GameObject.Find(staleNames[i]);
                if (found != null)
                    DestroyImmediate(found);
            }
        }

        // ==================== 管理器创建 ====================

        /// <summary>创建网络管理器与游戏状态管理器（单例，DontDestroyOnLoad）。</summary>
        private void CreateManagers()
        {
            var netGo = new GameObject("[NetworkManager]");
            _networkManager = netGo.AddComponent<NetworkManager>();

            var gsGo = new GameObject("[GameStateManager]");
            _gameStateManager = gsGo.AddComponent<GameStateManager>();
        }

        // ==================== UI 系统 ====================

        /// <summary>
        /// 创建 UI 系统：Canvas + UIManager + EventSystem + 登录面板 + 角色列表面板。
        /// 登录面板与角色列表面板在启动时创建但隐藏，由状态机控制显隐。
        /// </summary>
        private void CreateUISystem()
        {
            // Canvas
            var canvasGo = UIFactory.CreateCanvas();
            canvasGo.transform.SetParent(transform);

            // UIManager（单例）
            _uiManager = canvasGo.AddComponent<UIManager>();

            // EventSystem（UI 交互必需）+ ScaledBaseInput（修正 Retina 坐标错位）
            var esGo = new GameObject("[EventSystem]");
            esGo.transform.SetParent(transform);
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
            esGo.AddComponent<Minecraft.UI.ScaledBaseInput>();

            // 登录面板（Canvas 子物体必须有 RectTransform，铺满父物体）
            var loginGo = new GameObject("LoginPanel", typeof(RectTransform));
            loginGo.transform.SetParent(canvasGo.transform, false);
            SetFullscreen(loginGo.GetComponent<RectTransform>());
            var loginPanel = loginGo.AddComponent<LoginPanel>();
            loginPanel.Initialize();
            _uiManager.Register("LoginPanel", loginPanel);

            // 注册面板（创建后立即隐藏，由登录面板的「注册账号」按钮触发显示）
            var registerGo = new GameObject("RegisterPanel", typeof(RectTransform));
            registerGo.transform.SetParent(canvasGo.transform, false);
            SetFullscreen(registerGo.GetComponent<RectTransform>());
            var registerPanel = registerGo.AddComponent<RegisterPanel>();
            registerPanel.Initialize();
            _uiManager.Register("RegisterPanel", registerPanel);
            registerPanel.Hide();

            // 角色列表面板（创建后立即隐藏，避免覆盖登录面板）
            var roleListGo = new GameObject("RoleListPanel", typeof(RectTransform));
            roleListGo.transform.SetParent(canvasGo.transform, false);
            SetFullscreen(roleListGo.GetComponent<RectTransform>());
            var roleListPanel = roleListGo.AddComponent<RoleListPanel>();
            roleListPanel.Initialize();
            _uiManager.Register("RoleListPanel", roleListPanel);
            roleListPanel.Hide();
        }

        // ==================== 状态变化处理 ====================

        /// <summary>
        /// 游戏状态变化处理：控制 UI 显隐与游戏世界创建/销毁。
        /// </summary>
        private void HandleStateChanged(GameStateManager.GameState oldState,
            GameStateManager.GameState newState)
        {
            switch (newState)
            {
                case GameStateManager.GameState.Offline:
                    // 回到离线：隐藏角色列表与注册面板，显示登录，销毁游戏世界
                    _uiManager.Hide("RoleListPanel");
                    _uiManager.Hide("RegisterPanel");
                    _uiManager.Show("LoginPanel");
                    if (_gameWorldCreated)
                        DestroyGameWorld();
                    break;

                case GameStateManager.GameState.RoleList:
                    // 角色列表：隐藏登录与注册面板，显示角色列表
                    _uiManager.Hide("LoginPanel");
                    _uiManager.Hide("RegisterPanel");
                    _uiManager.Show("RoleListPanel");
                    break;

                case GameStateManager.GameState.InGame:
                    // 进入游戏：隐藏所有登录 UI，创建游戏世界
                    _uiManager.Hide("LoginPanel");
                    _uiManager.Hide("RegisterPanel");
                    _uiManager.Hide("RoleListPanel");
                    if (!_gameWorldCreated)
                        CreateGameWorld();
                    break;
            }
        }

        /// <summary>
        /// 进入游戏成功：用服务端返回的位置与朝向初始化玩家，
        /// 并启动移动同步与远程实体管理。
        /// 传送前会先强制加载目标位置周围的区块，并在客户端查找真实地面高度，
        /// 避免服务端与客户端地形不一致导致玩家卡入地下。
        /// </summary>
        private void HandleEnterGameSuccess(PlayerInfo playerInfo)
        {
            if (_playerController != null && playerInfo != null)
            {
                Vector3 serverPos = playerInfo.position;
                int wx = Mathf.FloorToInt(serverPos.x);
                int wz = Mathf.FloorToInt(serverPos.z);

                // 传送前先强制加载目标位置周围的区块，防止掉落
                if (_chunkManager != null)
                {
                    _chunkManager.EnsureChunksAround(wx, wz, _chunkManager.RenderRadius);
                    Debug.Log($"[GameBootstrap] 已预加载区块: wx={wx}, wz={wz}, radius={_chunkManager.RenderRadius}");
                }

                // 查找客户端真实地面高度（服务端与客户端地形可能不一致）
                int groundY = FindGroundHeight(wx, wz);
                Vector3 spawnPos = new Vector3(serverPos.x, groundY + 2f, serverPos.z);

                _playerController.Teleport(spawnPos);

                // 设置出生点为服务端位置（掉落保护传送回此处而非世界原点）
                _playerController.SetSpawnPoint(spawnPos);

                float yaw = GetYawFromDirection(playerInfo.direction);
                _playerController.SetYaw(yaw);

                Debug.Log($"[GameBootstrap] 玩家初始化: serverPos={serverPos}, groundY={groundY}, spawnPos={spawnPos}, yaw={yaw}");
            }

            // 启动移动同步与远程实体管理（仅在线模式）
            if (_playerNetworkSync != null && _playerController != null)
                _playerNetworkSync.Initialize(_playerController);

            if (_remoteEntityManager != null)
                _remoteEntityManager.StartListening();
        }

        /// <summary>
        /// 查找指定世界坐标 (wx, wz) 处的最高非空气方块 Y 坐标。
        /// 从最高层向下扫描，返回第一个非空气方块的 Y。若全部为空气返回 0。
        /// </summary>
        private int FindGroundHeight(int wx, int wz)
        {
            if (_chunkManager == null) return 0;

            const int maxHeight = 64; // Chunk.Height 通常为 64
            for (int y = maxHeight - 1; y >= 0; y--)
            {
                var block = _chunkManager.GetBlock(wx, y, wz);
                if (block != Minecraft.Game.World.BlockType.Air)
                    return y;
            }
            return 0;
        }

        // ==================== 游戏世界创建 ====================

        /// <summary>
        /// 创建游戏世界：相机 + 方块世界 + 玩家 + ECS 系统 + 游戏内 UI + 网络同步组件。
        /// </summary>
        private void CreateGameWorld()
        {
            CreateCamera();
            CreateChunkWorld();
            CreatePlayer();
            CreateSystems();
            CreateGameUI();
            CreateNetworkComponents();

            _gameWorldCreated = true;

            // 离线模式：直接放到默认位置
            if (!_gameStateManager.IsOnlineMode)
            {
                _playerController.Teleport(new Vector3(8, 45, 8));
            }

            Debug.Log("[GameBootstrap] 游戏世界创建完成");
        }

        /// <summary>销毁游戏世界：清理所有运行时创建的游戏对象。</summary>
        private void DestroyGameWorld()
        {
            // 停止网络同步
            if (_playerNetworkSync != null)
                _playerNetworkSync.Stop();
            if (_remoteEntityManager != null)
                _remoteEntityManager.StopListening();

            // 清理子对象（ChunkManager/Player/Systems/UI 都挂在 GameBootstrap 下）
            string[] childNames = { "[ChunkManager]", "[Player]", "[WorldUpdater]",
                "[Camera]", "HudPanel", "HotbarPanel", "InventoryPanel" };

            for (int i = 0; i < childNames.Length; i++)
            {
                var found = transform.Find(childNames[i]);
                if (found != null)
                    Destroy(found.gameObject);
            }

            // 清理所有 Chunk_ 开头的对象
            var chunkObjects = FindObjectsOfType<GameObject>();
            for (int i = 0; i < chunkObjects.Length; i++)
            {
                if (chunkObjects[i].name.StartsWith("Chunk_"))
                    Destroy(chunkObjects[i]);
            }

            _playerController = null;
            _playerInteraction = null;
            _chunkManager = null;
            _chunkLoadSystem = null;
            _chunkRenderSystem = null;
            _remoteEntityManager = null;
            _playerNetworkSync = null;

            _gameWorldCreated = false;
            Debug.Log("[GameBootstrap] 游戏世界已销毁");
        }

        // ==================== 相机 ====================

        /// <summary>创建主相机。</summary>
        private void CreateCamera()
        {
            if (Camera.main != null) return;

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.55f, 0.65f);
            cam.transform.position = new Vector3(8, 45, 8);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            camGo.AddComponent<FlareLayer>();
            camGo.AddComponent<AudioListener>();
        }

        // ==================== 方块世界 ====================

        /// <summary>创建方块世界：ChunkManager + 地形生成器。</summary>
        private void CreateChunkWorld()
        {
            var chunkGo = new GameObject("[ChunkManager]");
            chunkGo.transform.SetParent(transform);
            _chunkManager = chunkGo.AddComponent<ChunkManager>();
            _chunkManager.TerrainProvider = new TerrainGenerator(terrainSeed);

            // 通过反射设置渲染半径
            var renderField = typeof(ChunkManager).GetField("_renderRadius",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (renderField != null)
                renderField.SetValue(_chunkManager, renderRadius);
        }

        // ==================== 玩家 ====================

        /// <summary>创建玩家：GameObject + CharacterController + PlayerController + PlayerInteraction。</summary>
        private void CreatePlayer()
        {
            var playerGo = new GameObject("[Player]");
            playerGo.transform.SetParent(transform);
            playerGo.transform.position = new Vector3(8, 45, 8);

            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.slopeLimit = 60f;
            cc.stepOffset = 0.6f;

            _playerController = playerGo.AddComponent<PlayerController>();
            _playerInteraction = playerGo.AddComponent<PlayerInteraction>();

            // 注入 ChunkManager
            var cmField = typeof(PlayerInteraction).GetField("chunkManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cmField != null)
                cmField.SetValue(_playerInteraction, _chunkManager);
        }

        // ==================== ECS 系统 ====================

        /// <summary>创建并注册 ECS 系统：区块加载 + 区块渲染。</summary>
        private void CreateSystems()
        {
            var updaterGo = new GameObject("[WorldUpdater]");
            updaterGo.transform.SetParent(transform);
            var updater = updaterGo.AddComponent<WorldUpdater>();

            // 区块加载系统
            var loadGo = new GameObject("[ChunkLoadSystem]");
            loadGo.transform.SetParent(updaterGo.transform);
            _chunkLoadSystem = loadGo.AddComponent<ChunkLoadSystem>();
            _chunkLoadSystem.Initialize(_world);
            _chunkLoadSystem.ChunkManager = _chunkManager;
            _chunkLoadSystem.PlayerTransform = _playerController.transform;
            updater.Register(_chunkLoadSystem);

            // 区块渲染系统
            var renderGo = new GameObject("[ChunkRenderSystem]");
            renderGo.transform.SetParent(updaterGo.transform);
            _chunkRenderSystem = renderGo.AddComponent<ChunkRenderSystem>();
            _chunkRenderSystem.Initialize(_world);
            _chunkRenderSystem.ChunkManager = _chunkManager;
            updater.Register(_chunkRenderSystem);
        }

        // ==================== 游戏内 UI ====================

        /// <summary>创建游戏内 UI：HUD + 快捷栏 + 背包。</summary>
        private void CreateGameUI()
        {
            var canvas = _uiManager.GetComponent<Canvas>();

            // HUD 面板
            var hudGo = new GameObject("HudPanel", typeof(RectTransform));
            hudGo.transform.SetParent(canvas.transform, false);
            SetFullscreen(hudGo.GetComponent<RectTransform>());
            var hud = hudGo.AddComponent<HudPanel>();
            hud.Initialize();
            _uiManager.Register("HudPanel", hud);
            hud.Show();

            // 快捷栏面板
            var hotbarGo = new GameObject("HotbarPanel", typeof(RectTransform));
            hotbarGo.transform.SetParent(canvas.transform, false);
            SetFullscreen(hotbarGo.GetComponent<RectTransform>());
            var hotbar = hotbarGo.AddComponent<HotbarPanel>();
            hotbar.Initialize();
            _uiManager.Register("HotbarPanel", hotbar);
            hotbar.Show();

            // 同步快捷栏默认内容
            hotbar.SetSlot(0, BlockType.Grass);
            hotbar.SetSlot(1, BlockType.Dirt);
            hotbar.SetSlot(2, BlockType.Stone);
            hotbar.SetSlot(3, BlockType.Cobblestone);
            hotbar.SetSlot(4, BlockType.Planks);
            hotbar.SetSlot(5, BlockType.Wood);
            hotbar.SetSlot(6, BlockType.Sand);
            hotbar.SetSlot(7, BlockType.Glass);
            hotbar.SetSlot(8, BlockType.Leaves);
        }

        // ==================== 网络组件 ====================

        /// <summary>
        /// 创建网络同步组件：远程实体管理器 + 玩家移动同步。
        /// 仅在在线模式下创建（离线模式不创建）。
        /// </summary>
        private void CreateNetworkComponents()
        {
            // 远程实体管理器（始终创建，离线模式下不会收到任何消息）
            var remoteGo = new GameObject("[RemoteEntityManager]");
            remoteGo.transform.SetParent(transform);
            _remoteEntityManager = remoteGo.AddComponent<RemoteEntityManager>();

            // 玩家移动同步组件（挂载到玩家对象上）
            _playerNetworkSync = _playerController.gameObject.AddComponent<PlayerNetworkSync>();
        }

        // ==================== 辅助方法 ====================

        /// <summary>从方向向量提取 Yaw 角度（绕 Y 轴旋转）。</summary>
        private static float GetYawFromDirection(ProtoVector3 direction)
        {
            Vector3 dir = direction;
            if (dir.sqrMagnitude < 0.001f)
                return 0f;

            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        /// <summary>设置 RectTransform 为铺满父物体（stretch 模式）。</summary>
        private static void SetFullscreen(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
