using UnityEngine;

namespace Minecraft.Game.Player
{
    /// <summary>
    /// 玩家控制器。基于 <see cref="CharacterController"/> 实现第一人称移动，
    /// 包含 WASD 行走、空格跳跃、Shift 潜行、双击 W 冲刺、鼠标视角控制等功能。
    /// <para>
    /// 设计要点：
    /// - 使用 CharacterController 而非 Rigidbody，适合方块世界的精确移动控制。
    /// - 鼠标水平旋转控制玩家身体（Yaw），垂直旋转控制相机（Pitch，限制 ±90°）。
    /// - 相机作为子物体，位于眼睛高度，实现第一人称视角。
    /// - 鼠标锁定（<see cref="CursorLockMode.Locked"/>），按 ESC 解锁。
    /// </para>
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        // ==================== 移动参数 ====================

        /// <summary>行走速度（单位：格/秒）。</summary>
        [SerializeField] private float walkSpeed = 4.5f;

        /// <summary>冲刺速度（双击 W 触发）。</summary>
        [SerializeField] private float sprintSpeed = 7.0f;

        /// <summary>潜行速度（按住 Shift）。</summary>
        [SerializeField] private float sneakSpeed = 2.0f;

        /// <summary>跳跃高度（单位：格）。</summary>
        [SerializeField] private float jumpHeight = 1.25f;

        /// <summary>重力加速度（单位：格/秒²）。方块世界重力较大，所以设为 -20。</summary>
        [SerializeField] private float gravity = -20f;

        /// <summary>鼠标灵敏度。</summary>
        [SerializeField] private float mouseSensitivity = 2f;

        // ==================== 玩家尺寸常量 ====================

        /// <summary>玩家身高（单位：格）。</summary>
        private const float PlayerHeight = 1.8f;

        /// <summary>眼睛高度（相机 Y 坐标，单位：格）。</summary>
        private const float EyeHeight = 1.62f;

        /// <summary>玩家碰撞体半径（单位：格）。</summary>
        private const float PlayerRadius = 0.3f;

        /// <summary>掉落保护阈值：Y 低于此值时传回出生点。</summary>
        private const float FallThreshold = -200f;

        /// <summary>出生点坐标（掉落保护传送位置，运行时由 GameBootstrap 设置为服务端返回位置）。</summary>
        private Vector3 _spawnPoint = new Vector3(0f, 50f, 0f);

        /// <summary>设置出生点（供 GameBootstrap 在进入游戏时调用）。</summary>
        public void SetSpawnPoint(Vector3 pos) => _spawnPoint = pos;

        /// <summary>双击 W 冲刺的最大间隔时间（秒）。</summary>
        private const float SprintDoubleTapTime = 0.3f;

        /// <summary>落地后施加的微小向下速度，保持 isGrounded 稳定。</summary>
        private const float GroundedStickVelocity = -2f;

        // ==================== 运行时状态 ====================

        /// <summary>CharacterController 组件引用。</summary>
        private CharacterController _controller;

        /// <summary>相机 Transform（第一人称视角）。</summary>
        private Transform _cameraTransform;

        /// <summary>水平视角（Yaw，绕 Y 轴旋转角度）。</summary>
        private float _yaw;

        /// <summary>垂直视角（Pitch，绕 X 轴旋转角度，限制在 ±90°）。</summary>
        private float _pitch;

        /// <summary>垂直速度（Y 轴速度，受重力和跳跃影响）。</summary>
        private float _velocityY;

        /// <summary>是否正在潜行。</summary>
        private bool _isSneaking;

        /// <summary>是否正在冲刺。</summary>
        private bool _isSprinting;

        /// <summary>上次按下 W 键的时间（用于双击检测）。</summary>
        private float _lastWTapTime;

        /// <summary>重力临时禁用倒计时（>0 时禁用重力下落，用于区块加载缓冲）。</summary>
        private float _gravityDisableTimer;

        // ==================== 视角摆动（Bobbing） ====================

        /// <summary>Bobbing 振幅（相机上下浮动幅度，单位：格）。</summary>
        private const float BobAmount = 0.05f;

        /// <summary>Bobbing 频率（行走时摆动速度，值越大摆动越快）。</summary>
        private const float BobSpeed = 10f;

        /// <summary>当前 Bobbing 累计相位（随移动距离累加）。</summary>
        private float _bobPhase;

        /// <summary>当前 Bobbing 偏移量（平滑过渡，避免突变）。</summary>
        private float _currentBobOffset;

        /// <summary>手持方块 Transform（行走时跟随摆动）。</summary>
        private Transform _handBlockTransform;

        /// <summary>手持方块初始位置（摆动基于此偏移）。</summary>
        private Vector3 _handBlockBaseLocalPos;

        // ==================== 第三人称视角 ====================

        /// <summary>第三人称相机距离（玩家身后）。</summary>
        private const float ThirdPersonDistance = 3.5f;

        /// <summary>第三人称相机额外高度（相对眼睛高度，略微俯视）。</summary>
        private const float ThirdPersonHeightOffset = 0.3f;

        // ---- 模型尺寸常量（与 RemotePlayerEntity 保持一致）----

        private const float HeadSize = 0.5f;
        private const float HeadY = 1.5f;
        private const float BodyWidth = 0.6f;
        private const float BodyHeight = 0.8f;
        private const float BodyDepth = 0.3f;
        private const float BodyY = 0.9f;
        private const float ArmWidth = 0.25f;
        private const float ArmHeight = 0.8f;
        private const float ArmDepth = 0.25f;
        private const float ArmY = 0.9f;
        private const float LegWidth = 0.3f;
        private const float LegHeight = 0.8f;
        private const float LegDepth = 0.3f;
        private const float LegY = 0.0f;
        private const float ArmSideOffset = 0.425f;
        private const float LegSideOffset = 0.15f;
        private const float ModelGroundOffset = 0.4f;

        // ---- 动画常量 ----

        private const float WalkLegAmplitudeDeg = 30f;
        private const float WalkArmAmplitudeDeg = 20f;
        private const float WalkFreqHzPerSpeed = 2f;
        private const float WalkSpeedThreshold = 0.1f;
        private const float IdleBreathAmplitude = 0.03f;
        private const float IdleBreathFreqHz = 1.5f;
        private const float IdleLerpSpeed = 10f;
        private const float HeadBrightenAmount = 0.25f;
        private const float LimbDarkenAmount = 0.3f;

        // ---- 第三人称运行时状态 ----

        /// <summary>是否处于第三人称视角。</summary>
        private bool _isThirdPerson;

        /// <summary>第一人称手持方块根节点（切换视角时显示/隐藏）。</summary>
        private Transform _handRoot;

        /// <summary>第三人称身体模型根节点。</summary>
        private Transform _bodyModelRoot;

        /// <summary>身体 Transform（用于呼吸动画）。</summary>
        private Transform _bodyTransform;

        /// <summary>左臂 pivot（行走摆动）。</summary>
        private Transform _leftArmPivot;

        /// <summary>右臂 pivot（行走摆动）。</summary>
        private Transform _rightArmPivot;

        /// <summary>左腿 pivot（行走摆动）。</summary>
        private Transform _leftLegPivot;

        /// <summary>右腿 pivot（行走摆动）。</summary>
        private Transform _rightLegPivot;

        /// <summary>上一帧位置（用于计算实际移动速度驱动动画）。</summary>
        private Vector3 _lastPosForAnim;

        /// <summary>行走动画累计相位。</summary>
        private float _animPhase;

        /// <summary>待机动画累计时间。</summary>
        private float _idleTime;

        // ==================== 公开属性 ====================

        /// <summary>玩家世界坐标位置。</summary>
        public Vector3 Position => transform.position;

        /// <summary>玩家朝向（Yaw 旋转）。</summary>
        public Quaternion Rotation => transform.rotation;

        /// <summary>相机 Transform（供其他系统获取视角方向）。</summary>
        public Transform CameraTransform => _cameraTransform;

        /// <summary>是否在地面（由 CharacterController 判定）。</summary>
        public bool IsGrounded => _controller != null && _controller.isGrounded;

        /// <summary>是否正在潜行。</summary>
        public bool IsSneaking => _isSneaking;

        /// <summary>是否正在冲刺。</summary>
        public bool IsSprinting => _isSprinting;

        // ==================== Unity 生命周期 ====================

        /// <summary>
        /// 初始化组件：配置 CharacterController 碰撞体，设置相机。
        /// </summary>
        private void Awake()
        {
            SetupController();
            SetupCamera();
        }

        /// <summary>启动时锁定鼠标光标。</summary>
        private void Start()
        {
            LockCursor();
        }

        /// <summary>每帧更新：处理鼠标视角、移动、掉落保护、光标锁定、视角摆动。</summary>
        private void Update()
        {
            HandleCursorLock();
            HandleViewToggle();
            HandleMouseLook();
            HandleMovement();
            HandleFallProtection();
            UpdateBobbing();
            UpdateBodyAnimation();

            // 衰减重力禁用计时器
            if (_gravityDisableTimer > 0f)
                _gravityDisableTimer -= Time.deltaTime;
        }

        // ==================== 公开方法 ====================

        /// <summary>
        /// 传送玩家到指定位置。
        /// 临时禁用 CharacterController 以直接设置坐标，之后重新启用。
        /// 传送后自动启用 3 秒重力缓冲，给区块加载留出时间，避免传送后立即掉落。
        /// </summary>
        /// <param name="pos">目标世界坐标。</param>
        public void Teleport(Vector3 pos)
        {
            if (_controller != null)
            {
                _controller.enabled = false;
                transform.position = pos;
                _controller.enabled = true;
            }
            else
            {
                transform.position = pos;
            }

            // 重置垂直速度，避免传送后受残余速度影响
            _velocityY = 0f;
            // 启用 10 秒重力缓冲，等待区块 Mesh 渲染完成（实际会在检测到地面时提前恢复）
            _gravityDisableTimer = 10f;
        }

        /// <summary>
        /// 设置玩家水平朝向（Yaw 角度）。
        /// 同步更新内部 _yaw 与 transform.rotation，但不影响相机 Pitch。
        /// </summary>
        /// <param name="yaw">Yaw 角度（度）。</param>
        public void SetYaw(float yaw)
        {
            _yaw = yaw;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ==================== 初始化 ====================

        /// <summary>
        /// 获取或添加 CharacterController，并配置碰撞体尺寸。
        /// 碰撞体：高度 1.8，半径 0.3，中心在 (0, 0.9, 0)。
        /// </summary>
        private void SetupController()
        {
            _controller = GetComponent<CharacterController>();
            if (_controller == null)
                _controller = gameObject.AddComponent<CharacterController>();

            _controller.height = PlayerHeight;
            _controller.radius = PlayerRadius;
            _controller.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
            _controller.slopeLimit = 45f;
            _controller.stepOffset = 0.6f;
        }

        /// <summary>
        /// 设置第一人称相机。优先使用子物体上的相机，其次使用主相机，最后创建新相机。
        /// 相机定位在眼睛高度（1.62 格）。
        /// </summary>
        private void SetupCamera()
        {
            // 优先查找子物体上的相机
            Camera cam = GetComponentInChildren<Camera>();

            if (cam == null)
            {
                // 其次使用主相机
                cam = Camera.main;
                if (cam != null)
                {
                    cam.transform.SetParent(transform);
                }
                else
                {
                    // 最后创建新相机
                    var camObj = new GameObject("PlayerCamera");
                    camObj.transform.SetParent(transform);
                    cam = camObj.AddComponent<Camera>();
                    cam.nearClipPlane = 0.01f;
                }
            }

            _cameraTransform = cam.transform;
            _cameraTransform.localPosition = new Vector3(0f, EyeHeight, 0f);
            _cameraTransform.localRotation = Quaternion.identity;

            // 初始化 Yaw 为当前朝向
            _yaw = transform.eulerAngles.y;

            // 查找或创建第一人称手持方块（用于行走摆动 + 视觉反馈）
            var hand = transform.Find("FirstPersonHand/HandBlock");
            if (hand == null)
            {
                hand = CreateFirstPersonHand();
            }
            _handBlockTransform = hand;
            _handBlockBaseLocalPos = hand.localPosition;
            _handRoot = hand.parent;

            // 创建第三人称身体模型（默认隐藏，切换至第三人称时显示）
            CreateThirdPersonBody();
            _lastPosForAnim = transform.position;
        }

        /// <summary>
        /// 更新视角摆动（Bobbing）。
        /// 玩家在地面移动时，相机和手持方块轻微上下浮动，增强行走沉浸感。
        /// 停止移动时平滑回归原位。
        /// </summary>
        private void UpdateBobbing()
        {
            if (_controller == null || !_controller.isGrounded)
            {
                // 不在地面时停止摆动，平滑回归
                _currentBobOffset = Mathf.Lerp(_currentBobOffset, 0f, Time.deltaTime * 8f);
                ApplyBobOffset(0f);
                return;
            }

            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            float moveAmount = Mathf.Clamp01(Mathf.Abs(horizontal) + Mathf.Abs(vertical));

            if (moveAmount > 0.01f)
            {
                // 累加相位（基于时间，冲刺时摆动更快）
                float speedFactor = _isSprinting ? 1.4f : 1f;
                _bobPhase += Time.deltaTime * BobSpeed * speedFactor;

                // 正弦摆动，振幅与移动量成正比
                float targetOffset = Mathf.Sin(_bobPhase) * BobAmount * moveAmount;
                _currentBobOffset = Mathf.Lerp(_currentBobOffset, targetOffset, Time.deltaTime * 10f);
            }
            else
            {
                // 停止移动：平滑回归零
                _currentBobOffset = Mathf.Lerp(_currentBobOffset, 0f, Time.deltaTime * 8f);
            }

            ApplyBobOffset(_currentBobOffset);
        }

        /// <summary>
        /// 应用 Bobbing 偏移到相机和手持方块。
        /// 相机上下浮动，手持方块同步浮动并轻微左右摆动。
        /// </summary>
        /// <param name="yOffset">Y 轴偏移量（格）。</param>
        private void ApplyBobOffset(float yOffset)
        {
            if (_cameraTransform != null)
            {
                if (_isThirdPerson)
                {
                    // 第三人称：相机后移至玩家身后，略微抬高，俯视身体模型
                    _cameraTransform.localPosition = new Vector3(
                        0f, EyeHeight + ThirdPersonHeightOffset + yOffset, -ThirdPersonDistance);
                }
                else
                {
                    _cameraTransform.localPosition = new Vector3(0f, EyeHeight + yOffset, 0f);
                }
            }

            // 手持方块仅在第一人称模式下显示并跟随摆动
            if (_handBlockTransform != null && !_isThirdPerson)
            {
                float sideSway = Mathf.Sin(_bobPhase * 0.5f) * 0.02f;
                _handBlockTransform.localPosition = _handBlockBaseLocalPos + new Vector3(sideSway, yOffset * 0.8f, 0f);
            }
        }

        /// <summary>
        /// 创建第一人称手持方块模型。
        /// 在相机右下方放置一个小方块，代表玩家手持的物品。
        /// 使用方块世界标准纹理（草方块），跟随相机旋转。
        /// </summary>
        private Transform CreateFirstPersonHand()
        {
            // 创建父节点（挂载到相机下，跟随视角旋转）
            var handRoot = new GameObject("FirstPersonHand");
            handRoot.transform.SetParent(_cameraTransform, false);

            // 创建手持方块
            var handBlock = new GameObject("HandBlock");
            handBlock.transform.SetParent(handRoot.transform, false);

            // 参数来自项目约定: size 0.3, pos (0.55, -0.45, 0.5), rot (-30, 25, 10)
            // 相对相机 localPosition: 右下前方
            handBlock.transform.localPosition = new Vector3(0.55f, -0.45f, 0.5f);
            handBlock.transform.localRotation = Quaternion.Euler(-30f, 25f, 10f);
            handBlock.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

            // 使用 Cube 基础网格
            var meshFilter = handBlock.AddComponent<MeshFilter>();
            meshFilter.mesh = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().sharedMesh;

            var renderer = handBlock.AddComponent<MeshRenderer>();
            // 使用草方块纹理图集中的 Sprite
            var sprite = Minecraft.Game.World.TextureAtlasGenerator.GetBlockSprite(Minecraft.Game.World.BlockType.Grass);
            if (sprite != null)
            {
                var mat = new Material(Shader.Find("Unlit/Texture"));
                mat.mainTexture = sprite.texture;
                renderer.sharedMaterial = mat;
            }
            else
            {
                // 回退: 使用纯色材质
                var mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = new Color(0.5f, 0.8f, 0.3f);
                renderer.sharedMaterial = mat;
            }

            // 手持方块不需要 Collider
            var collider = handBlock.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            return handBlock.transform;
        }

        // ==================== 鼠标视角 ====================

        /// <summary>
        /// 处理鼠标视角控制。
        /// 水平移动（Mouse X）旋转玩家身体（Yaw）；
        /// 垂直移动（Mouse Y）旋转相机（Pitch），限制在 ±90° 防止翻转。
        /// 仅在鼠标锁定时生效。
        /// </summary>
        private void HandleMouseLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            // Yaw：水平旋转玩家身体
            _yaw += mouseX;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            // Pitch：垂直旋转相机（鼠标上移 → 向上看，所以取负）
            _pitch -= mouseY;
            _pitch = Mathf.Clamp(_pitch, -90f, 90f);
            _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // ==================== 移动逻辑 ====================

        /// <summary>
        /// 处理移动：WASD 方向输入、冲刺/潜行状态、重力与跳跃、CharacterController.Move。
        /// </summary>
        private void HandleMovement()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            // ---- 双击 W 冲刺检测 ----
            UpdateSprintState(vertical);

            // ---- 潜行状态 ----
            _isSneaking = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // ---- 计算移动速度 ----
            float speed = GetCurrentSpeed(vertical);

            // ---- 相机相对方向（仅水平面）----
            Vector3 moveDir = GetCameraRelativeDirection(horizontal, vertical);

            // ---- 重力与跳跃 ----
            ApplyGravityAndJump();

            // ---- 组合最终速度向量 ----
            Vector3 velocity = moveDir * speed;
            velocity.y = _velocityY;

            // ---- 执行移动 ----
            _controller.Move(velocity * Time.deltaTime);
        }

        /// <summary>
        /// 双击 W 冲刺检测。在双击间隔内再次按下 W 则进入冲刺状态，
        /// 松开 W 时退出冲刺。
        /// </summary>
        /// <param name="vertical">前后输入值（W 为正）。</param>
        private void UpdateSprintState(float vertical)
        {
            if (Input.GetKeyDown(KeyCode.W))
            {
                if (Time.time - _lastWTapTime < SprintDoubleTapTime)
                    _isSprinting = true;
                _lastWTapTime = Time.time;
            }

            // 松开 W 退出冲刺
            if (Input.GetKeyUp(KeyCode.W))
                _isSprinting = false;
        }

        /// <summary>
        /// 根据当前状态（潜行/冲刺/行走）计算移动速度。
        /// 潜行优先级最高，冲刺仅在向前移动时生效。
        /// </summary>
        /// <param name="vertical">前后输入值（W 为正）。</param>
        /// <returns>当前移动速度。</returns>
        private float GetCurrentSpeed(float vertical)
        {
            if (_isSneaking)
                return sneakSpeed;

            if (_isSprinting && vertical > 0f)
                return sprintSpeed;

            return walkSpeed;
        }

        /// <summary>
        /// 计算相机相对的水平移动方向。
        /// 以相机前方为"前"，相机右方为"右"，投影到水平面后归一化。
        /// </summary>
        /// <param name="horizontal">左右输入（D 为正）。</param>
        /// <param name="vertical">前后输入（W 为正）。</param>
        /// <returns>归一化的水平移动方向。</returns>
        private Vector3 GetCameraRelativeDirection(float horizontal, float vertical)
        {
            // 相机前方投影到水平面（去掉 Y 分量）
            Vector3 forward = _cameraTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            // 相机右方投影到水平面
            Vector3 right = _cameraTransform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 moveDir = forward * vertical + right * horizontal;

            // 对角线移动时归一化，避免速度叠加（√2 ≈ 1.41 倍）
            if (moveDir.sqrMagnitude > 1f)
                moveDir.Normalize();

            return moveDir;
        }

        /// <summary>
        /// 应用重力与跳跃逻辑。
        /// 落地时施加微小向下速度保持 isGrounded 稳定；
        /// 按空格且在地面时以跳跃高度计算初始向上速度。
        /// 重力缓冲期间（传送后等待区块 Mesh 渲染）跳过重力，但检测到地面后提前恢复。
        /// </summary>
        private void ApplyGravityAndJump()
        {
            // 重力缓冲期间（刚传送完，区块 Mesh 可能未渲染），跳过重力
            if (_gravityDisableTimer > 0f)
            {
                _velocityY = 0f;
                // 检测到地面（区块 Mesh 已渲染并产生碰撞），提前恢复重力
                if (_controller != null && _controller.isGrounded)
                    _gravityDisableTimer = 0f;
                return;
            }

            if (_controller.isGrounded)
            {
                // 落地时重置微小向下速度，确保 isGrounded 持续为 true
                if (_velocityY < 0f)
                    _velocityY = GroundedStickVelocity;

                // 跳跃：v = √(2 * g * h)
                if (Input.GetKeyDown(KeyCode.Space))
                    _velocityY = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
            }

            // 应用重力（v = v0 + g * t）
            _velocityY += gravity * Time.deltaTime;
        }

        // ==================== 第三人称视角切换 ====================

        /// <summary>
        /// 处理 F5 切换第一/第三人称视角。
        /// 第一人称：隐藏身体模型，显示手持方块，相机位于眼睛高度。
        /// 第三人称：显示身体模型，隐藏手持方块，相机后移俯视身体。
        /// </summary>
        private void HandleViewToggle()
        {
            if (Input.GetKeyDown(KeyCode.F5))
            {
                _isThirdPerson = !_isThirdPerson;
                ApplyViewMode();
            }
        }

        /// <summary>
        /// 根据当前视角模式切换模型与手持方块的可见性。
        /// </summary>
        private void ApplyViewMode()
        {
            if (_isThirdPerson)
            {
                if (_bodyModelRoot != null)
                    _bodyModelRoot.gameObject.SetActive(true);
                if (_handRoot != null)
                    _handRoot.gameObject.SetActive(false);
            }
            else
            {
                if (_bodyModelRoot != null)
                    _bodyModelRoot.gameObject.SetActive(false);
                if (_handRoot != null)
                    _handRoot.gameObject.SetActive(true);
            }
        }

        // ==================== 第三人称身体动画 ====================

        /// <summary>
        /// 更新第三人称身体模型的行走/待机动画。
        /// 基于实际水平位移速度计算摆动频率：行走时腿臂正弦摆动，待机时呼吸微浮。
        /// </summary>
        private void UpdateBodyAnimation()
        {
            if (_bodyModelRoot == null || !_bodyModelRoot.gameObject.activeSelf)
                return;

            float dt = Time.deltaTime;
            _idleTime += dt;

            // 计算实际水平移动速度（驱动动画频率）
            Vector3 horizontalDelta = transform.position - _lastPosForAnim;
            horizontalDelta.y = 0f;
            float moveSpeed = horizontalDelta.magnitude / Mathf.Max(dt, 0.0001f);
            _lastPosForAnim = transform.position;

            if (moveSpeed > WalkSpeedThreshold)
            {
                // 行走：腿臂正弦摆动，左臂与右腿同向、右臂与左腿同向
                _animPhase += dt * Mathf.PI * 2f * (moveSpeed * WalkFreqHzPerSpeed);
                float armSwing = Mathf.Sin(_animPhase) * WalkArmAmplitudeDeg;
                float legSwing = Mathf.Sin(_animPhase) * WalkLegAmplitudeDeg;

                _leftArmPivot.localRotation = Quaternion.Euler(armSwing, 0f, 0f);
                _rightArmPivot.localRotation = Quaternion.Euler(-armSwing, 0f, 0f);
                _leftLegPivot.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
                _rightLegPivot.localRotation = Quaternion.Euler(legSwing, 0f, 0f);

                // 行走时身体回到基准高度
                if (_bodyTransform != null)
                    _bodyTransform.localPosition = new Vector3(0f, BodyY, 0f);
            }
            else
            {
                // 待机：身体呼吸微浮
                float breath = Mathf.Sin(_idleTime * Mathf.PI * 2f * IdleBreathFreqHz) * IdleBreathAmplitude;
                if (_bodyTransform != null)
                    _bodyTransform.localPosition = new Vector3(0f, BodyY + breath, 0f);

                // 四肢平滑回到自然下垂
                _leftArmPivot.localRotation = Quaternion.Lerp(
                    _leftArmPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
                _rightArmPivot.localRotation = Quaternion.Lerp(
                    _rightArmPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
                _leftLegPivot.localRotation = Quaternion.Lerp(
                    _leftLegPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
                _rightLegPivot.localRotation = Quaternion.Lerp(
                    _rightLegPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
            }
        }

        // ==================== 第三人称模型构建 ====================

        /// <summary>
        /// 创建第三人称人形身体模型。
        /// 模型结构与 RemotePlayerEntity 保持一致：头/身体/四肢 + pivot 关节架构，
        /// 支持行走摆动与待机呼吸动画。默认隐藏，切换至第三人称时显示。
        /// </summary>
        private void CreateThirdPersonBody()
        {
            // 蓝色身体，与 NPC（绿色）和其他玩家区分
            Color bodyColor = new Color(0.3f, 0.55f, 0.85f);

            var modelRoot = new GameObject("ThirdPersonBody");
            modelRoot.transform.SetParent(transform, false);
            modelRoot.transform.localPosition = new Vector3(0f, ModelGroundOffset, 0f);
            _bodyModelRoot = modelRoot.transform;

            // 颜色变体：头部稍亮，四肢稍暗，增强层次感
            Color headColor = Color.Lerp(bodyColor, Color.white, HeadBrightenAmount);
            Color limbColor = Color.Lerp(bodyColor, Color.black, LimbDarkenAmount);

            // 身体
            _bodyTransform = CreateBodyCube("Body",
                new Vector3(BodyWidth, BodyHeight, BodyDepth),
                new Vector3(0f, BodyY, 0f),
                bodyColor, modelRoot.transform).transform;

            // 头部（作为身体子对象，呼吸时跟随上下）
            CreateBodyCube("Head",
                new Vector3(HeadSize, HeadSize, HeadSize),
                new Vector3(0f, HeadY - BodyY, 0f),
                headColor, _bodyTransform);

            // 手臂：pivot 在肩部，mesh 下垂
            _leftArmPivot = CreateBodyLimb("LeftArm",
                new Vector3(ArmWidth, ArmHeight, ArmDepth),
                new Vector3(-ArmSideOffset, ArmY + ArmHeight * 0.5f, 0f),
                new Vector3(0f, -ArmHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;

            _rightArmPivot = CreateBodyLimb("RightArm",
                new Vector3(ArmWidth, ArmHeight, ArmDepth),
                new Vector3(ArmSideOffset, ArmY + ArmHeight * 0.5f, 0f),
                new Vector3(0f, -ArmHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;

            // 腿：pivot 在臀部，mesh 下垂
            _leftLegPivot = CreateBodyLimb("LeftLeg",
                new Vector3(LegWidth, LegHeight, LegDepth),
                new Vector3(-LegSideOffset, LegY + LegHeight * 0.5f, 0f),
                new Vector3(0f, -LegHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;

            _rightLegPivot = CreateBodyLimb("RightLeg",
                new Vector3(LegWidth, LegHeight, LegDepth),
                new Vector3(LegSideOffset, LegY + LegHeight * 0.5f, 0f),
                new Vector3(0f, -LegHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;

            // 默认隐藏（第一人称模式）
            modelRoot.SetActive(false);
        }

        /// <summary>
        /// 创建身体 Cube 图元：设置尺寸/位置/颜色，移除 Collider（避免干扰 CharacterController），
        /// 使用 Standard Shader 漫反射材质（无金属感、无光泽）。
        /// </summary>
        private static GameObject CreateBodyCube(string name, Vector3 size, Vector3 localPos, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.localPosition = localPos;

            // 移除自带 Collider，身体模型不参与物理碰撞
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                var mat = new Material(shader) { color = color };
                // 漫反射：无金属感、无光泽，自然哑光表面
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0f);
                renderer.sharedMaterial = mat;
            }

            return go;
        }

        /// <summary>创建带 pivot 的肢体：pivot 位于关节处，Cube mesh 作为子对象下移半个肢长。</summary>
        private static GameObject CreateBodyLimb(string name, Vector3 size, Vector3 pivotPos, Vector3 meshOffset, Color color, Transform parent)
        {
            var pivot = new GameObject(name + "_Pivot");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = pivotPos;

            CreateBodyCube(name, size, meshOffset, color, pivot.transform);
            return pivot;
        }

        // ==================== 掉落保护 ====================

        /// <summary>
        /// 检测玩家是否掉出世界（Y &lt; FallThreshold），若是则传送回出生点。
        /// </summary>
        private void HandleFallProtection()
        {
            if (transform.position.y < FallThreshold)
                Teleport(_spawnPoint);
        }

        // ==================== 光标锁定 ====================

        /// <summary>锁定鼠标光标（隐藏并限制在窗口中心）。</summary>
        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// 处理光标锁定/解锁切换。
        /// 按 ESC 解锁；鼠标点击窗口重新锁定。
        /// </summary>
        private static void HandleCursorLock()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            {
                LockCursor();
            }
        }
    }
}
