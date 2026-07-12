using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
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
        [SerializeField] private int renderRadius = 10;

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

            // 取消订阅方块变更广播
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnBlockChangeBroadcast -= HandleBlockChangeBroadcast;
            }

            // 清理子对象（ChunkManager/Player/Systems/UI 都挂在 GameBootstrap 下）
            string[] childNames = { "[ChunkManager]", "[Player]", "[WorldUpdater]",
                "[Camera]", "HudPanel", "HotbarPanel", "InventoryPanel" };

            for (int i = 0; i < childNames.Length; i++)
            {
                var found = transform.Find(childNames[i]);
                if (found != null)
                    Destroy(found.gameObject);
            }

            // 清理所有 Chunk_ 开头的对象（优先通过 ChunkRenderSystem 的 transform 遍历子物体）
            if (_chunkRenderSystem != null)
            {
                var renderTransform = _chunkRenderSystem.transform;
                int childCount = renderTransform.childCount;
                // 倒序遍历，避免销毁后索引错位
                for (int i = childCount - 1; i >= 0; i--)
                {
                    Transform child = renderTransform.GetChild(i);
                    if (child.name.StartsWith("Chunk_"))
                        Destroy(child.gameObject);
                }
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] ChunkRenderSystem 引用不可用，回退到全场景遍历清理 Chunk_ 对象");
                var chunkObjects = FindObjectsOfType<GameObject>();
                for (int i = 0; i < chunkObjects.Length; i++)
                {
                    if (chunkObjects[i].name.StartsWith("Chunk_"))
                        Destroy(chunkObjects[i]);
                }
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

        /// <summary>创建主相机（含天空盒、雾效、方向光、环境光）。</summary>
        private void CreateCamera()
        {
            if (Camera.main != null)
            {
                SetupLightingAndSky();
                return;
            }

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.5f, 0.7f, 0.95f);
            cam.transform.position = new Vector3(8, 45, 8);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;

            // 雾效：远处方块淡入天空色，隐藏渲染边界
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.68f, 0.78f, 0.92f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 200f;

            camGo.AddComponent<FlareLayer>();
            camGo.AddComponent<AudioListener>();

            SetupLightingAndSky();
        }

        /// <summary>
        /// 配置场景光照与天空盒：
        /// - 方向光（太阳）带柔和阴影
        /// - 环境光梯度（天空/地面/反射）
        /// - 程序化天空盒材质（渐变蓝色天空）
        /// </summary>
        private static void SetupLightingAndSky()
        {
            // ===== 方向光（太阳光） =====
            var sunGo = GameObject.Find("DirectionalLight");
            if (sunGo == null)
            {
                sunGo = new GameObject("DirectionalLight");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.94f, 0.82f);
                sun.intensity = 1.4f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.80f;
                sun.shadowResolution = LightShadowResolution.Medium;
                sun.shadowBias = 0.04f;
                sun.shadowNormalBias = 0.4f;
                sun.shadowNearPlane = 0.2f;
                // 太阳角度：从西南偏上方照射，模拟下午阳光
                sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            // ===== 环境光 =====
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.64f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.52f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.30f, 0.26f);
            RenderSettings.ambientIntensity = 1.25f;
            RenderSettings.reflectionIntensity = 1.1f;

            // ===== 程序化天空盒（渐变蓝色） =====
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var skyMat = new Material(skyShader);
                skyMat.name = "ProceduralSkybox";
                skyMat.SetColor("_SunTint", new Color(1f, 0.88f, 0.65f));
                skyMat.SetColor("_SkyTint", new Color(0.45f, 0.68f, 1f));
                skyMat.SetFloat("_AtmosphereThickness", 1.3f);
                skyMat.SetFloat("_Exposure", 1.15f);
                RenderSettings.skybox = skyMat;
            }
        }

        // ==================== 方块世界 ====================

        /// <summary>创建方块世界：ChunkManager + 地形生成器。</summary>
        private void CreateChunkWorld()
        {
            var chunkGo = new GameObject("[ChunkManager]");
            chunkGo.transform.SetParent(transform);
            _chunkManager = chunkGo.AddComponent<ChunkManager>();
            _chunkManager.TerrainProvider = new TerrainGenerator(terrainSeed);
            _chunkManager.SetRenderRadius(renderRadius);
        }

        // ==================== 玩家 ====================

        /// <summary>创建玩家：GameObject + CharacterController + PlayerController + PlayerInteraction + 视觉模型。</summary>
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
            _playerInteraction.SetChunkManager(_chunkManager);

            // 创建玩家视觉模型（第一人称手臂 + 第三人称身体）
            CreatePlayerVisuals(playerGo);
        }

        /// <summary>
        /// 创建玩家视觉模型：
        /// - 第一人称：相机前方悬浮的手臂方块（模拟手持物品）
        /// - 第三人称身体（其他玩家可见，由RemotePlayerEntity使用）
        /// </summary>
        private static void CreatePlayerVisuals(GameObject playerGo)
        {
            // ===== 第三人称身体模型（其他玩家可见） =====
            var bodyGo = new GameObject("PlayerBody");
            bodyGo.transform.SetParent(playerGo.transform, false);
            bodyGo.transform.localPosition = new Vector3(0f, 0f, 0f);

            // 身体（躯干）
            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(bodyGo.transform, false);
            torso.transform.localScale = new Vector3(0.6f, 0.8f, 0.35f);
            torso.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var torsoMat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
            torsoMat.name = "Player_Torso";
            torsoMat.color = new Color(0.25f, 0.45f, 0.70f); // 蓝色上衣
            torso.GetComponent<MeshRenderer>().sharedMaterial = torsoMat;

            // 头部
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(bodyGo.transform, false);
            head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            head.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            var headMat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
            headMat.name = "Player_Head";
            headMat.color = new Color(0.95f, 0.80f, 0.65f); // 肤色
            head.GetComponent<MeshRenderer>().sharedMaterial = headMat;

            // 左手臂
            var leftArm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftArm.name = "LeftArm";
            leftArm.transform.SetParent(bodyGo.transform, false);
            leftArm.transform.localScale = new Vector3(0.2f, 0.8f, 0.2f);
            leftArm.transform.localPosition = new Vector3(-0.4f, 1.1f, 0f);
            leftArm.GetComponent<MeshRenderer>().sharedMaterial = torsoMat;

            // 右手臂
            var rightArm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightArm.name = "RightArm";
            rightArm.transform.SetParent(bodyGo.transform, false);
            rightArm.transform.localScale = new Vector3(0.2f, 0.8f, 0.2f);
            rightArm.transform.localPosition = new Vector3(0.4f, 1.1f, 0f);
            rightArm.GetComponent<MeshRenderer>().sharedMaterial = torsoMat;

            // 左腿
            var leftLeg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftLeg.name = "LeftLeg";
            leftLeg.transform.SetParent(bodyGo.transform, false);
            leftLeg.transform.localScale = new Vector3(0.25f, 0.7f, 0.25f);
            leftLeg.transform.localPosition = new Vector3(-0.15f, 0.35f, 0f);
            var legMat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
            legMat.name = "Player_Legs";
            legMat.color = new Color(0.18f, 0.22f, 0.35f); // 深蓝色裤子
            leftLeg.GetComponent<MeshRenderer>().sharedMaterial = legMat;

            // 右腿
            var rightLeg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightLeg.name = "RightLeg";
            rightLeg.transform.SetParent(bodyGo.transform, false);
            rightLeg.transform.localScale = new Vector3(0.25f, 0.7f, 0.25f);
            rightLeg.transform.localPosition = new Vector3(0.15f, 0.35f, 0f);
            rightLeg.GetComponent<MeshRenderer>().sharedMaterial = legMat;

            // 第三人称模型默认隐藏（第一人称看不到自己身体）
            bodyGo.SetActive(false);

            // ===== 第一人称手持方块（相机前方的悬浮方块，参考Minecraft视角优化） =====
            var handGo = new GameObject("FirstPersonHand");
            handGo.transform.SetParent(playerGo.transform, false);
            var handBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handBlock.name = "HandBlock";
            handBlock.transform.SetParent(handGo.transform, false);
            // 尺寸缩小到0.3格（Minecraft标准），减少视野遮挡
            handBlock.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            // 位置偏右下角，Y略低于眼睛高度，Z靠近相机但不挡中心视线
            handBlock.transform.localPosition = new Vector3(0.55f, 1.45f, 0.5f);
            // 倾斜角度加大，让方块"躺"在手中（X旋转-30°让顶面朝向相机）
            handBlock.transform.localRotation = Quaternion.Euler(-30f, 25f, 10f);
            var handMat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
            handMat.name = "HandBlock_Mat";
            handMat.color = new Color(0.55f, 0.75f, 0.40f); // 草绿色方块
            handBlock.GetComponent<MeshRenderer>().sharedMaterial = handMat;

            // 设置相机FOV为70°（Minecraft默认值），物品在边缘更不显眼
            var cam = playerGo.GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cam.fieldOfView = 70f;
            }
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

            // 订阅方块变更广播（在线模式下由服务端推送其他玩家的方块修改）
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnBlockChangeBroadcast += HandleBlockChangeBroadcast;
            }
        }

        /// <summary>处理服务端推送的方块变更广播：更新本地方块数据。</summary>
        private void HandleBlockChangeBroadcast(BlockChangeBroadcast msg)
        {
            if (_chunkManager == null)
                return;

            // 将服务端的 blockType 映射到客户端 BlockType 枚举
            BlockType type = (BlockType)msg.blockType;
            _chunkManager.SetBlock(msg.x, msg.y, msg.z, type);
            Debug.Log($"[BlockSync] 收到方块变更广播: pos=({msg.x},{msg.y},{msg.z}), type={type}");
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
