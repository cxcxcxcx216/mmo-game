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

        /// <summary>每帧更新：处理鼠标视角、移动、掉落保护、光标锁定。</summary>
        private void Update()
        {
            HandleCursorLock();
            HandleMouseLook();
            HandleMovement();
            HandleFallProtection();

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
