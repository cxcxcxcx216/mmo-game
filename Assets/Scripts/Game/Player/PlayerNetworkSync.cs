using UnityEngine;
using Minecraft.MMO;

namespace Minecraft.Game.Player
{
    /// <summary>
    /// 玩家移动网络同步组件。定时将本地玩家的位置与朝向上报到服务端，
    /// 供其他客户端通过 <see cref="EntityMoveBroadcast"/> 接收并渲染。
    /// <para>
    /// 同步策略：
    /// 1. 定时上报（默认每 0.1 秒）。
    /// 2. 位置移动超过阈值或朝向变化超过阈值时立即上报（减少静止时的网络开销）。
    /// 3. 仅在 <see cref="NetworkManager.IsInGame"/> 时工作，离线模式不上报。
    /// </para>
    /// </summary>
    public class PlayerNetworkSync : MonoBehaviour
    {
        // ==================== 配置字段 ====================

        /// <summary>定时同步间隔（秒）。</summary>
        [SerializeField] private float syncInterval = 0.1f;

        /// <summary>位置变化阈值（超过此距离立即上报）。</summary>
        [SerializeField] private float positionThreshold = 0.1f;

        /// <summary>朝向变化阈值（超过此角度立即上报）。</summary>
        [SerializeField] private float rotationThreshold = 5f;

        // ==================== 运行时状态 ====================

        /// <summary>玩家控制器引用。</summary>
        private PlayerController _player;

        /// <summary>距上次上报的累计时间。</summary>
        private float _syncTimer;

        /// <summary>上次上报的位置。</summary>
        private Vector3 _lastSentPosition;

        /// <summary>上次上报的朝向（Yaw 角度）。</summary>
        private float _lastSentYaw;

        /// <summary>是否已初始化。</summary>
        private bool _initialized;

        // ==================== 公开方法 ====================

        /// <summary>
        /// 初始化同步组件。由 <see cref="GameBootstrap"/> 在进入游戏后调用。
        /// </summary>
        /// <param name="player">本地玩家控制器。</param>
        public void Initialize(PlayerController player)
        {
            _player = player;
            _lastSentPosition = player.Position;
            _lastSentYaw = player.Rotation.eulerAngles.y;
            _syncTimer = 0f;
            _initialized = true;
        }

        /// <summary>停止同步（离开游戏时调用）。</summary>
        public void Stop()
        {
            _initialized = false;
        }

        // ==================== Unity 生命周期 ====================

        /// <summary>每帧检查是否需要上报移动数据。</summary>
        private void Update()
        {
            if (!_initialized || _player == null)
                return;

            var net = NetworkManager.Instance;
            if (net == null || !net.IsInGame)
                return;

            _syncTimer += Time.deltaTime;

            Vector3 currentPos = _player.Position;
            float currentYaw = _player.Rotation.eulerAngles.y;

            // 检查是否超过阈值（位置或朝向变化足够大）
            float posDelta = Vector3.Distance(currentPos, _lastSentPosition);
            float yawDelta = Mathf.Abs(Mathf.DeltaAngle(_lastSentYaw, currentYaw));

            // 静止时不发送：仅在位置/朝向变化超过阈值时上报
            bool hasMoved = posDelta >= positionThreshold || yawDelta >= rotationThreshold;
            if (!hasMoved)
            {
                _syncTimer = 0f;
                return;
            }

            // 有移动时，按定时间隔节流（避免每帧都发）
            if (_syncTimer < syncInterval)
                return;

            // 计算当前移动速度（水平面）
            float speed = _player.IsSprinting ? 7f : (_player.IsSneaking ? 2f : 4.5f);
            if (!_player.IsGrounded)
                speed = 0f; // 空中不移动时速度为 0

            // 朝向转为方向向量（Yaw → (sin, 0, cos)）
            float yawRad = currentYaw * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));

            // 发送移动同步消息
            net.SendMove(currentPos, direction, speed);

            _lastSentPosition = currentPos;
            _lastSentYaw = currentYaw;
            _syncTimer = 0f;
        }
    }
}
