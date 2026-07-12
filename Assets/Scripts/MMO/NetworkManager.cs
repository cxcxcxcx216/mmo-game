using System;
using UnityEngine;
using MMO.Network;
using MMO.Protocol;

namespace Minecraft.MMO
{
    /// <summary>
    /// 网络管理器。整合 <see cref="TcpNetworkClient"/> 与 <see cref="MessageDispatcher"/>，
    /// 提供游戏业务级的网络 API（登录、选角、进游戏、移动同步、实体同步等）。
    /// <para>
    /// 设计要点：
    /// 1. 单例 <see cref="MonoBehaviour"/>，跨场景持久化。
    /// 2. 内部创建 <see cref="TcpNetworkClient"/> 子组件，统一管理连接生命周期。
    /// 3. 所有协议消息通过 <see cref="MessageDispatcher"/> 派发到主线程。
    /// 4. 通过 C# 事件向 UI 层通知登录流程结果（成功/失败/超时）。
    /// 5. 离线模式下不创建连接，直接进入游戏（单机沙盒）。
    /// </para>
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        // ==================== 单例 ====================

        /// <summary>全局单例实例。</summary>
        public static NetworkManager Instance { get; private set; }

        // ==================== 组件引用 ====================

        /// <summary>TCP 客户端子组件。</summary>
        private TcpNetworkClient _client;

        /// <summary>消息分发器（主线程调用）。</summary>
        private readonly MessageDispatcher _dispatcher = new MessageDispatcher();

        // ==================== 业务事件 ====================

        /// <summary>连接成功。</summary>
        public event Action OnConnected;

        /// <summary>连接失败。参数：失败原因。</summary>
        public event Action<string> OnConnectFailed;

        /// <summary>连接断开。参数：断开原因。</summary>
        public event Action<string> OnDisconnected;

        /// <summary>登录响应。</summary>
        public event Action<LoginAck> OnLoginAck;

        /// <summary>服务器列表响应。</summary>
        public event Action<ServerListAck> OnServerListAck;

        /// <summary>角色列表响应。</summary>
        public event Action<RoleListAck> OnRoleListAck;

        /// <summary>创建角色响应。</summary>
        public event Action<CreateRoleAck> OnCreateRoleAck;

        /// <summary>进入游戏响应。</summary>
        public event Action<EnterGameAck> OnEnterGameAck;

        /// <summary>离开游戏响应。</summary>
        public event Action<LeaveGameAck> OnLeaveGameAck;

        /// <summary>实体进入视野。</summary>
        public event Action<EntityEnterView> OnEntityEnterView;

        /// <summary>实体离开视野。</summary>
        public event Action<EntityLeaveView> OnEntityLeaveView;

        /// <summary>实体移动广播。</summary>
        public event Action<EntityMoveBroadcast> OnEntityMoveBroadcast;

        /// <summary>背包同步。</summary>
        public event Action<InventorySync> OnInventorySync;

        /// <summary>方块变更广播（其他玩家修改了方块）。</summary>
        public event Action<BlockChangeBroadcast> OnBlockChangeBroadcast;

        // ==================== 状态属性 ====================

        /// <summary>是否已连接服务器。</summary>
        public bool IsConnected => _client != null && _client.IsConnected;

        /// <summary>当前会话密钥（登录成功后由服务端下发）。</summary>
        public string SessionKey => _client?.SessionKey;

        /// <summary>当前账号 ID。</summary>
        public long AccountId { get; private set; }

        /// <summary>当前服务器 ID。</summary>
        public int ServerId { get; private set; }

        /// <summary>当前角色 ID。</summary>
        public long RoleId { get; private set; }

        /// <summary>是否已进入游戏。</summary>
        public bool IsInGame { get; private set; }

        // ==================== 断线重连 ====================

        /// <summary>重连最大尝试次数。</summary>
        private const int MaxReconnectAttempts = 5;

        /// <summary>重连初始延迟（秒），每次失败翻倍。</summary>
        private const float ReconnectInitialDelay = 2f;

        /// <summary>重连最大延迟（秒）。</summary>
        private const float ReconnectMaxDelay = 30f;

        /// <summary>上次登录用的账号（用于重连）。</summary>
        private string _lastAccount;

        /// <summary>上次登录用的密码（用于重连）。</summary>
        private string _lastPassword;

        /// <summary>上次登录用的客户端版本（用于重连）。</summary>
        private string _lastClientVersion = "1.0.0";

        /// <summary>上次登录用的平台（用于重连）。</summary>
        private int _lastPlatform = 1;

        /// <summary>上次连接的服务器地址（用于重连）。</summary>
        private string _lastHost;

        /// <summary>上次连接的服务器端口（用于重连）。</summary>
        private int _lastPort;

        /// <summary>当前重连尝试次数。</summary>
        private int _reconnectAttempts;

        /// <summary>是否正在重连中。</summary>
        public bool IsReconnecting { get; private set; }

        /// <summary>重连成功事件。</summary>
        public event Action OnReconnectSuccess;

        /// <summary>重连失败事件（超过最大重试次数）。参数：失败原因。</summary>
        public event Action<string> OnReconnectFailed;

        // ==================== Unity 生命周期 ====================

        /// <summary>初始化单例、创建 TCP 客户端、注册消息处理器。</summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // 在主线程提前初始化 MainThreadDispatcher，避免子线程访问时创建 GameObject 报错
            _ = UnityMainThreadDispatcher.Instance;

            CreateTcpClient();
            RegisterHandlers();
        }

        /// <summary>销毁时清理单例引用与连接。</summary>
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_client != null)
                _client.Disconnect(silent: true);
        }

        private void OnApplicationQuit()
        {
            if (IsInGame)
                LeaveGame();

            if (_client != null)
                _client.Disconnect(silent: true);
        }

        // ==================== 初始化 ====================

        /// <summary>创建 TCP 客户端子组件并绑定事件回调。</summary>
        private void CreateTcpClient()
        {
            _client = gameObject.AddComponent<TcpNetworkClient>();
            _client.OnMessageReceived += HandleMessageReceived;
            _client.OnConnected += () => OnConnected?.Invoke();
            _client.OnConnectFailed += reason => OnConnectFailed?.Invoke(reason);
            _client.OnDisconnected += reason => OnDisconnected?.Invoke(reason);
        }

        /// <summary>注册所有协议消息的处理器，将字节流反序列化为强类型消息后触发事件。</summary>
        private void RegisterHandlers()
        {
            _dispatcher.Register<LoginAck>(MsgId.LOGIN_ACK,
                msg => HandleLoginAck(msg), LoginAck.Deserialize);
            _dispatcher.Register<ServerListAck>(MsgId.SERVER_LIST_ACK,
                msg => OnServerListAck?.Invoke(msg), ServerListAck.Deserialize);
            _dispatcher.Register<RoleListAck>(MsgId.ROLE_LIST_ACK,
                msg => OnRoleListAck?.Invoke(msg), RoleListAck.Deserialize);
            _dispatcher.Register<CreateRoleAck>(MsgId.CREATE_ROLE_ACK,
                msg => OnCreateRoleAck?.Invoke(msg), CreateRoleAck.Deserialize);
            _dispatcher.Register<EnterGameAck>(MsgId.ENTER_GAME_ACK,
                msg => HandleEnterGameAck(msg), EnterGameAck.Deserialize);
            _dispatcher.Register<LeaveGameAck>(MsgId.LEAVE_GAME_ACK,
                msg => HandleLeaveGameAck(msg), LeaveGameAck.Deserialize);
            _dispatcher.Register<EntityEnterView>(MsgId.ENTITY_ENTER_VIEW,
                msg => OnEntityEnterView?.Invoke(msg), EntityEnterView.Deserialize);
            _dispatcher.Register<EntityLeaveView>(MsgId.ENTITY_LEAVE_VIEW,
                msg => OnEntityLeaveView?.Invoke(msg), EntityLeaveView.Deserialize);
            _dispatcher.Register<EntityMoveBroadcast>(MsgId.ENTITY_MOVE_BROADCAST,
                msg => OnEntityMoveBroadcast?.Invoke(msg), EntityMoveBroadcast.Deserialize);
            _dispatcher.Register<InventorySync>(MsgId.INVENTORY_SYNC,
                msg => OnInventorySync?.Invoke(msg), InventorySync.Deserialize);
            _dispatcher.Register<HeartbeatAck>(MsgId.HEARTBEAT_ACK,
                msg => { /* 心跳响应已由 TcpNetworkClient 处理 serverTime */ }, HeartbeatAck.Deserialize);
            _dispatcher.Register<BlockChangeBroadcast>(MsgId.BLOCK_CHANGE_BROADCAST,
                msg => OnBlockChangeBroadcast?.Invoke(msg), BlockChangeBroadcast.Deserialize);
        }

        /// <summary>TCP 消息回调：将消息派发到 <see cref="MessageDispatcher"/>。</summary>
        private void HandleMessageReceived(int msgId, byte[] payload)
        {
            _dispatcher.Dispatch(msgId, payload);
        }

        // ==================== 连接 API ====================

        /// <summary>异步连接服务器。</summary>
        /// <param name="host">服务器地址。</param>
        /// <param name="port">服务器端口。</param>
        public void Connect(string host, int port)
        {
            Debug.Log($"[Network] 正在连接服务器 {host}:{port}");
            _lastHost = host;
            _lastPort = port;
            if (_client == null)
                CreateTcpClient();

            _client.Connect(host, port);
        }

        /// <summary>断开连接并重置游戏内状态。主动断开时停止重连。</summary>
        public void Disconnect()
        {
            Debug.Log("[Network] 主动断开连接");
            StopReconnect();
            if (_client != null)
                _client.Disconnect();
            IsInGame = false;
        }

        // ==================== 登录流程 API ====================

        /// <summary>发送登录请求。</summary>
        /// <param name="account">账号。</param>
        /// <param name="password">密码。</param>
        /// <param name="clientVersion">客户端版本号。</param>
        /// <param name="platform">平台标识（1=PC）。</param>
        public void Login(string account, string password, string clientVersion = "1.0.0", int platform = 1)
        {
            Debug.Log($"[Network] 发送登录请求: account={account}, version={clientVersion}, platform={platform}");
            // 保存登录信息用于断线重连
            _lastAccount = account;
            _lastPassword = password;
            _lastClientVersion = clientVersion;
            _lastPlatform = platform;
            var req = new LoginReq
            {
                account = account,
                password = password,
                clientVersion = clientVersion,
                platform = platform
            };
            _client.Send(MsgId.LOGIN_REQ, req.Serialize());
        }

        /// <summary>请求服务器列表。</summary>
        /// <param name="platform">平台标识。</param>
        public void RequestServerList(int platform = 1)
        {
            var req = new ServerListReq { platform = platform };
            _client.Send(MsgId.SERVER_LIST_REQ, req.Serialize());
        }

        /// <summary>请求角色列表。登录成功后调用。</summary>
        /// <param name="serverId">服务器 ID。</param>
        public void RequestRoleList(int serverId)
        {
            ServerId = serverId;
            var req = new RoleListReq { serverId = serverId };
            _client.Send(MsgId.ROLE_LIST_REQ, req.Serialize());
        }

        /// <summary>创建新角色。</summary>
        /// <param name="serverId">服务器 ID。</param>
        /// <param name="name">角色名。</param>
        /// <param name="profession">职业 ID。</param>
        public void CreateRole(int serverId, string name, int profession)
        {
            Debug.Log($"[Network] 发送创角请求: serverId={serverId}, name={name}, profession={profession}");
            var req = new CreateRoleReq
            {
                serverId = serverId,
                name = name,
                profession = profession
            };
            _client.Send(MsgId.CREATE_ROLE_REQ, req.Serialize());
        }

        /// <summary>进入游戏。</summary>
        /// <param name="roleId">角色 ID。</param>
        public void EnterGame(long roleId)
        {
            Debug.Log($"[Network] 发送进入游戏请求: roleId={roleId}");
            RoleId = roleId;
            var req = new EnterGameReq { roleId = roleId };
            _client.Send(MsgId.ENTER_GAME_REQ, req.Serialize());
        }

        /// <summary>离开游戏。</summary>
        public void LeaveGame()
        {
            if (!IsInGame)
                return;

            var req = new LeaveGameReq();
            _client.Send(MsgId.LEAVE_GAME_REQ, req.Serialize());
        }

        // ==================== 游戏内同步 API ====================

        /// <summary>上报玩家移动数据。</summary>
        /// <param name="position">当前世界坐标。</param>
        /// <param name="direction">朝向（Yaw 角度归一化向量）。</param>
        /// <param name="speed">当前移动速度。</param>
        public void SendMove(Vector3 position, Vector3 direction, float speed)
        {
            if (!IsConnected || !IsInGame)
                return;

            var req = new MoveReq
            {
                position = position,
                direction = direction,
                speed = speed
            };
            _client.Send(MsgId.MOVE_REQ, req.Serialize());
        }

        /// <summary>上报方块变更（破坏/放置方块）。</summary>
        /// <param name="x">方块世界坐标 X。</param>
        /// <param name="y">方块世界坐标 Y。</param>
        /// <param name="z">方块世界坐标 Z。</param>
        /// <param name="blockType">新方块类型（0=空气/破坏）。</param>
        public void SendBlockChange(int x, int y, int z, int blockType)
        {
            if (!IsConnected || !IsInGame)
                return;

            var req = new BlockChangeReq
            {
                x = x,
                y = y,
                z = z,
                blockType = blockType
            };
            _client.Send(MsgId.BLOCK_CHANGE_REQ, req.Serialize());
        }

        // ==================== 内部处理 ====================

        /// <summary>处理登录响应：记录账号 ID 与会话密钥，再通知 UI 层。</summary>
        private void HandleLoginAck(LoginAck msg)
        {
            if (msg.code == ErrorCode.SUCCESS)
            {
                AccountId = msg.accountId;
                if (_client != null)
                    _client.SessionKey = msg.sessionKey;
                Debug.Log($"[Network] 登录成功: accountId={msg.accountId}, sessionKey长度={msg.sessionKey?.Length ?? 0}");
            }
            else
            {
                Debug.LogWarning($"[Network] 登录失败: code={msg.code}, desc={ErrorCode.Describe(msg.code)}");
            }

            OnLoginAck?.Invoke(msg);
        }

        /// <summary>处理进入游戏响应：标记已进入游戏状态。</summary>
        private void HandleEnterGameAck(EnterGameAck msg)
        {
            if (msg.code == ErrorCode.SUCCESS)
            {
                IsInGame = true;
                if (msg.player != null)
                    RoleId = msg.player.roleId;
                Debug.Log($"[Network] 进入游戏成功: roleId={msg.player?.roleId}, pos={msg.player?.position}, dir={msg.player?.direction}");
            }
            else
            {
                Debug.LogWarning($"[Network] 进入游戏失败: code={msg.code}, desc={ErrorCode.Describe(msg.code)}");
            }

            OnEnterGameAck?.Invoke(msg);
        }

        /// <summary>处理离开游戏响应：重置状态。</summary>
        private void HandleLeaveGameAck(LeaveGameAck msg)
        {
            IsInGame = false;
            OnLeaveGameAck?.Invoke(msg);
        }

        // ==================== 断线重连 ====================

        /// <summary>
        /// 启动断线重连流程。使用之前保存的登录信息自动重连并恢复游戏状态。
        /// 重连流程：连接 → 登录 → 请求服务器列表 → 请求角色列表 → 进入游戏。
        /// 每次失败后延迟重试，延迟时间指数退避。
        /// </summary>
        public void StartReconnect()
        {
            if (IsReconnecting)
                return;

            if (string.IsNullOrEmpty(_lastAccount) || string.IsNullOrEmpty(_lastHost))
            {
                Debug.LogWarning("[Network] 无法重连：缺少登录信息");
                OnReconnectFailed?.Invoke("缺少登录信息");
                return;
            }

            IsReconnecting = true;
            _reconnectAttempts = 0;
            StartCoroutine(ReconnectCoroutine());
        }

        /// <summary>停止重连（主动断开时调用）。</summary>
        public void StopReconnect()
        {
            IsReconnecting = false;
            _reconnectAttempts = 0;
            StopAllCoroutines();
        }

        /// <summary>断线重连协程：指数退避重试，成功后自动恢复游戏状态。</summary>
        private System.Collections.IEnumerator ReconnectCoroutine()
        {
            long savedRoleId = RoleId;
            int savedServerId = ServerId;

            while (_reconnectAttempts < MaxReconnectAttempts && IsReconnecting)
            {
                _reconnectAttempts++;
                float delay = Mathf.Min(ReconnectInitialDelay * Mathf.Pow(2, _reconnectAttempts - 1), ReconnectMaxDelay);
                Debug.Log($"[Network] 断线重连第 {_reconnectAttempts}/{MaxReconnectAttempts} 次尝试，{delay:F1}秒后重连...");

                yield return new WaitForSeconds(delay);

                if (!IsReconnecting)
                    yield break;

                // 重新创建 TCP 客户端（旧的已断开）
                if (_client != null)
                    Destroy(_client);
                CreateTcpClient();

                // ===== 连接阶段 =====
                bool connectDone = false;
                bool connectSuccess = false;
                System.Action onConn = null;
                System.Action<string> onFail = null;
                onConn = () => { connectDone = true; connectSuccess = true; };
                onFail = (reason) => { connectDone = true; connectSuccess = false; };
                OnConnected += onConn;
                OnConnectFailed += onFail;
                try
                {
                    _client.Connect(_lastHost, _lastPort);

                    // 等待连接结果（最多 10 秒）
                    float timeout = 10f;
                    while (!connectDone && timeout > 0f)
                    {
                        timeout -= Time.deltaTime;
                        yield return null;
                    }

                    if (!connectDone)
                    {
                        Debug.LogWarning($"[Network] 重连超时（第 {_reconnectAttempts} 次）");
                        continue;
                    }

                    if (!connectSuccess)
                    {
                        Debug.LogWarning($"[Network] 重连失败（第 {_reconnectAttempts} 次）");
                        continue;
                    }
                }
                finally
                {
                    // 任何退出路径都清理订阅，防止协程被终止时泄漏
                    OnConnected -= onConn;
                    OnConnectFailed -= onFail;
                }

                // ===== 登录阶段 =====
                Debug.Log("[Network] 重连成功，开始自动登录...");
                bool loginDone = false;
                bool loginSuccess = false;
                System.Action<LoginAck> onLoginAck = null;
                onLoginAck = (msg) =>
                {
                    loginDone = true;
                    loginSuccess = (msg.code == ErrorCode.SUCCESS);
                };
                OnLoginAck += onLoginAck;
                try
                {
                    var loginReq = new LoginReq
                    {
                        account = _lastAccount,
                        password = _lastPassword,
                        clientVersion = _lastClientVersion,
                        platform = _lastPlatform
                    };
                    _client.Send(MsgId.LOGIN_REQ, loginReq.Serialize());

                    // 等待登录结果
                    float timeout = 10f;
                    while (!loginDone && timeout > 0f)
                    {
                        timeout -= Time.deltaTime;
                        yield return null;
                    }

                    if (!loginDone)
                    {
                        Debug.LogWarning("[Network] 重连登录超时");
                        continue;
                    }

                    if (!loginSuccess)
                    {
                        Debug.LogWarning("[Network] 重连登录失败");
                        break;
                    }
                }
                finally
                {
                    OnLoginAck -= onLoginAck;
                }

                // ===== 服务器列表阶段 =====
                ServerId = savedServerId;
                _client.Send(MsgId.SERVER_LIST_REQ, new ServerListReq { platform = _lastPlatform }.Serialize());

                bool serverListDone = false;
                System.Action<ServerListAck> onServerList = null;
                onServerList = (msg) => { serverListDone = true; };
                OnServerListAck += onServerList;
                try
                {
                    float timeout = 10f;
                    while (!serverListDone && timeout > 0f)
                    {
                        timeout -= Time.deltaTime;
                        yield return null;
                    }

                    if (!serverListDone)
                    {
                        Debug.LogWarning("[Network] 重连请求服务器列表超时");
                        continue;
                    }
                }
                finally
                {
                    OnServerListAck -= onServerList;
                }

                // ===== 角色列表阶段 =====
                _client.Send(MsgId.ROLE_LIST_REQ, new RoleListReq { serverId = savedServerId }.Serialize());

                bool roleListDone = false;
                System.Action<RoleListAck> onRoleList = null;
                onRoleList = (msg) => { roleListDone = true; };
                OnRoleListAck += onRoleList;
                try
                {
                    float timeout = 10f;
                    while (!roleListDone && timeout > 0f)
                    {
                        timeout -= Time.deltaTime;
                        yield return null;
                    }

                    if (!roleListDone)
                    {
                        Debug.LogWarning("[Network] 重连请求角色列表超时");
                        continue;
                    }
                }
                finally
                {
                    OnRoleListAck -= onRoleList;
                }

                // ===== 进入游戏阶段 =====
                RoleId = savedRoleId;
                _client.Send(MsgId.ENTER_GAME_REQ, new EnterGameReq { roleId = savedRoleId }.Serialize());

                bool enterDone = false;
                System.Action<EnterGameAck> onEnter = null;
                onEnter = (msg) => { enterDone = true; };
                OnEnterGameAck += onEnter;
                try
                {
                    float timeout = 15f;
                    while (!enterDone && timeout > 0f)
                    {
                        timeout -= Time.deltaTime;
                        yield return null;
                    }

                    if (!enterDone)
                    {
                        Debug.LogWarning("[Network] 重连进入游戏超时");
                        continue;
                    }
                }
                finally
                {
                    OnEnterGameAck -= onEnter;
                }

                // 重连完全成功
                IsReconnecting = false;
                _reconnectAttempts = 0;
                Debug.Log("[Network] 断线重连成功，已恢复游戏状态");
                OnReconnectSuccess?.Invoke();
                yield break;
            }

            // 重连失败（超过最大次数或登录失败）
            IsReconnecting = false;
            string failReason = $"断线重连失败（已尝试 {_reconnectAttempts} 次）";
            Debug.LogWarning($"[Network] {failReason}");
            OnReconnectFailed?.Invoke(failReason);
        }
    }
}
