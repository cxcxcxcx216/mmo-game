using System;
using UnityEngine;
using MMO.Protocol;

namespace Minecraft.MMO
{
    /// <summary>
    /// 游戏状态机。管理游戏从启动到进入游戏的完整流程状态转换。
    /// <para>
    /// 状态流转：
    /// <code>
    /// Offline → Connecting → Login → RoleList → EnteringGame → InGame
    ///    ↑________________________________________________________|
    /// </code>
    /// </para>
    /// <para>
    /// 设计要点：
    /// 1. 单例 <see cref="MonoBehaviour"/>，跨场景持久化。
    /// 2. 监听 <see cref="NetworkManager"/> 事件，自动驱动状态转换。
    /// 3. 通过 <see cref="OnStateChanged"/> 事件通知 UI 层响应状态变化。
    /// 4. 离线模式直接跳到 InGame 状态，不经过登录流程。
    /// </para>
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        // ==================== 状态枚举 ====================

        /// <summary>游戏状态枚举。</summary>
        public enum GameState
        {
            /// <summary>未连接（初始状态）。</summary>
            Offline,

            /// <summary>正在连接服务器。</summary>
            Connecting,

            /// <summary>已连接，等待登录。</summary>
            Login,

            /// <summary>已登录，正在请求或显示角色列表。</summary>
            RoleList,

            /// <summary>正在进入游戏。</summary>
            EnteringGame,

            /// <summary>已在游戏中。</summary>
            InGame,
        }

        // ==================== 单例 ====================

        /// <summary>全局单例实例。</summary>
        public static GameStateManager Instance { get; private set; }

        // ==================== 事件 ====================

        /// <summary>游戏状态变化事件。参数：旧状态、新状态。</summary>
        public event Action<GameState, GameState> OnStateChanged;

        /// <summary>进入游戏成功事件。参数：服务端返回的玩家信息。</summary>
        public event Action<PlayerInfo> OnEnterGameSuccess;

        /// <summary>进入游戏失败事件。参数：错误码。</summary>
        public event Action<int> OnEnterGameFailed;

        // ==================== 状态属性 ====================

        /// <summary>当前游戏状态。</summary>
        public GameState CurrentState { get; private set; } = GameState.Offline;

        /// <summary>是否处于网络在线模式（非离线）。</summary>
        public bool IsOnlineMode => CurrentState != GameState.Offline;

        // ==================== Unity 生命周期 ====================

        /// <summary>初始化单例并绑定网络事件。</summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>销毁时清理单例引用。</summary>
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ==================== 状态转换 ====================

        /// <summary>
        /// 启动在线模式：连接服务器并进入登录流程。
        /// </summary>
        /// <param name="host">服务器地址。</param>
        /// <param name="port">服务器端口。</param>
        public void StartOnlineMode(string host, int port)
        {
            var net = NetworkManager.Instance;
            if (net == null)
            {
                Debug.LogError("[GameState] NetworkManager 未初始化");
                return;
            }

            // 绑定网络事件（仅绑定一次）
            BindNetworkEvents(net);

            TransitionTo(GameState.Connecting);
            net.Connect(host, port);
        }

        /// <summary>
        /// 启动离线模式：跳过登录流程，直接进入游戏。
        /// </summary>
        public void StartOfflineMode()
        {
            TransitionTo(GameState.InGame);
        }

        /// <summary>请求角色列表（登录成功后由 UI 调用）。</summary>
        public void RequestRoleList(int serverId)
        {
            var net = NetworkManager.Instance;
            if (net != null && net.IsConnected)
                net.RequestRoleList(serverId);
        }

        /// <summary>进入游戏（选角完成后由 UI 调用）。</summary>
        public void EnterGame(long roleId)
        {
            var net = NetworkManager.Instance;
            if (net == null || !net.IsConnected)
            {
                Debug.LogWarning("[GameState] 未连接服务器，无法进入游戏");
                return;
            }

            TransitionTo(GameState.EnteringGame);
            net.EnterGame(roleId);
        }

        /// <summary>离开游戏，回到角色列表。</summary>
        public void LeaveGame()
        {
            var net = NetworkManager.Instance;
            if (net != null)
                net.LeaveGame();

            TransitionTo(GameState.RoleList);
        }

        /// <summary>断开连接，回到离线状态。即使状态未变也强制触发事件，确保 UI 正确刷新。</summary>
        public void GoOffline()
        {
            var net = NetworkManager.Instance;
            if (net != null && net.IsConnected)
                net.Disconnect();

            // 强制回到 Offline 并触发事件（不使用 TransitionTo，因为 TransitionTo 在状态相同时会跳过）
            GameState old = CurrentState;
            CurrentState = GameState.Offline;
            Debug.Log($"[GameState] {old} → {CurrentState} (forced)");
            OnStateChanged?.Invoke(old, CurrentState);
        }

        // ==================== 内部逻辑 ====================

        /// <summary>绑定网络事件回调（仅绑定一次）。</summary>
        private bool _eventsBound;

        private void BindNetworkEvents(NetworkManager net)
        {
            if (_eventsBound)
                return;

            net.OnConnected += HandleConnected;
            net.OnConnectFailed += HandleConnectFailed;
            net.OnDisconnected += HandleDisconnected;
            net.OnLoginAck += HandleLoginAck;
            net.OnServerListAck += HandleServerListAck;
            net.OnRoleListAck += HandleRoleListAck;
            net.OnEnterGameAck += HandleEnterGameAck;

            _eventsBound = true;
        }

        /// <summary>执行状态转换并触发事件。</summary>
        private void TransitionTo(GameState newState)
        {
            if (CurrentState == newState)
                return;

            GameState old = CurrentState;
            CurrentState = newState;
            Debug.Log($"[GameState] {old} → {newState}");
            OnStateChanged?.Invoke(old, newState);
        }

        // ==================== 网络事件处理 ====================

        /// <summary>连接成功：进入登录状态。</summary>
        private void HandleConnected()
        {
            TransitionTo(GameState.Login);
        }

        /// <summary>连接失败：回到离线状态。</summary>
        private void HandleConnectFailed(string reason)
        {
            Debug.LogWarning($"[GameState] 连接失败: {reason}");
            TransitionTo(GameState.Offline);
        }

        /// <summary>连接断开：回到离线状态。</summary>
        private void HandleDisconnected(string reason)
        {
            Debug.LogWarning($"[GameState] 连接断开: {reason}");
            TransitionTo(GameState.Offline);
        }

        /// <summary>登录响应：成功则自动请求服务器列表。</summary>
        private void HandleLoginAck(LoginAck msg)
        {
            if (msg.code == ErrorCode.SUCCESS)
            {
                // 登录成功后自动请求服务器列表，再由服务器列表响应驱动角色列表请求
                var net = NetworkManager.Instance;
                if (net != null)
                    net.RequestServerList();
            }
        }

        /// <summary>服务器列表响应：自动选择第一个服务器并请求角色列表。</summary>
        private void HandleServerListAck(ServerListAck msg)
        {
            if (msg.code != ErrorCode.SUCCESS)
            {
                Debug.LogWarning($"[GameState] 获取服务器列表失败: {ErrorCode.Describe(msg.code)}");
                return;
            }

            if (msg.servers == null || msg.servers.Count == 0)
            {
                Debug.LogWarning("[GameState] 服务器列表为空");
                return;
            }

            // 自动选择第一个可用服务器
            var net = NetworkManager.Instance;
            if (net != null)
            {
                int serverId = msg.servers[0].serverId;
                net.RequestRoleList(serverId);
            }
        }

        /// <summary>角色列表响应：进入角色列表状态。</summary>
        private void HandleRoleListAck(RoleListAck msg)
        {
            if (CurrentState == GameState.Login || CurrentState == GameState.Connecting)
                TransitionTo(GameState.RoleList);
        }

        /// <summary>进入游戏响应：成功则进入游戏状态。</summary>
        private void HandleEnterGameAck(EnterGameAck msg)
        {
            if (msg.code == ErrorCode.SUCCESS)
            {
                TransitionTo(GameState.InGame);
                OnEnterGameSuccess?.Invoke(msg.player);
            }
            else
            {
                TransitionTo(GameState.RoleList);
                OnEnterGameFailed?.Invoke(msg.code);
                Debug.LogWarning($"[GameState] 进入游戏失败: {ErrorCode.Describe(msg.code)}");
            }
        }
    }
}
