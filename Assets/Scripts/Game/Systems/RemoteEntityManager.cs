using System.Collections.Generic;
using UnityEngine;
using Minecraft.MMO;
using Minecraft.Game.Player;
using MMO.Protocol;

namespace Minecraft.Game.Systems
{
    /// <summary>
    /// 远程实体管理器。监听 <see cref="NetworkManager"/> 的实体同步事件，
    /// 在客户端创建、更新、销毁远程玩家实体的渲染对象。
    /// <para>
    /// 处理的消息：
    /// - <see cref="MsgId.ENTITY_ENTER_VIEW"/>：实体进入视野，创建 <see cref="RemotePlayerEntity"/>。
    /// - <see cref="MsgId.ENTITY_LEAVE_VIEW"/>：实体离开视野，销毁对应渲染对象。
    /// - <see cref="MsgId.ENTITY_MOVE_BROADCAST"/>：实体移动，更新目标位置、朝向与速度。
    /// </para>
    /// <para>
    /// 设计要点：
    /// 1. 普通 <see cref="MonoBehaviour"/>，事件驱动，无需每帧轮询。
    /// 2. NPC 固定绿色 + "[NPC]" 名牌前缀 + 0.9 倍缩放；玩家颜色基于实体 ID 哈希生成。
    /// 3. 离开游戏时自动清理所有远程实体。
    /// </para>
    /// </summary>
    public class RemoteEntityManager : MonoBehaviour
    {
        // ==================== 数据 ====================

        /// <summary>所有远程实体，键为实体 ID。</summary>
        private readonly Dictionary<long, RemotePlayerEntity> _entities = new Dictionary<long, RemotePlayerEntity>();

        /// <summary>远程实体根节点（组织层级，便于清理）。</summary>
        private Transform _root;

        /// <summary>是否已绑定网络事件。</summary>
        private bool _eventsBound;

        // ==================== 公开属性 ====================

        /// <summary>当前管理的远程实体数量。</summary>
        public int EntityCount => _entities.Count;

        // ==================== Unity 生命周期 ====================

        /// <summary>初始化：创建根节点并绑定网络事件。</summary>
        private void Awake()
        {
            var rootGo = new GameObject("[RemoteEntities]");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;
        }

        /// <summary>销毁时清理所有远程实体。</summary>
        private void OnDestroy()
        {
            ClearAllEntities();

            var net = NetworkManager.Instance;
            if (net != null && _eventsBound)
            {
                net.OnEntityEnterView -= HandleEntityEnterView;
                net.OnEntityLeaveView -= HandleEntityLeaveView;
                net.OnEntityMoveBroadcast -= HandleEntityMoveBroadcast;
                _eventsBound = false;
            }
        }

        // ==================== 公开方法 ====================

        /// <summary>绑定网络事件，开始接收实体同步消息。由 GameBootstrap 在进入游戏后调用。</summary>
        public void StartListening()
        {
            if (_eventsBound)
                return;

            var net = NetworkManager.Instance;
            if (net == null)
                return;

            net.OnEntityEnterView += HandleEntityEnterView;
            net.OnEntityLeaveView += HandleEntityLeaveView;
            net.OnEntityMoveBroadcast += HandleEntityMoveBroadcast;
            _eventsBound = true;
        }

        /// <summary>解绑网络事件并清理所有远程实体。由 GameBootstrap 在离开游戏时调用。</summary>
        public void StopListening()
        {
            if (_eventsBound)
            {
                var net = NetworkManager.Instance;
                if (net != null)
                {
                    net.OnEntityEnterView -= HandleEntityEnterView;
                    net.OnEntityLeaveView -= HandleEntityLeaveView;
                    net.OnEntityMoveBroadcast -= HandleEntityMoveBroadcast;
                }
                _eventsBound = false;
            }

            ClearAllEntities();
        }

        // ==================== 网络事件处理 ====================

        /// <summary>实体进入视野：创建远程实体渲染对象。</summary>
        private void HandleEntityEnterView(EntityEnterView msg)
        {
            if (msg?.entity == null)
                return;

            long entityId = msg.entity.id;

            // 已存在则更新（避免重复创建）
            if (_entities.TryGetValue(entityId, out var existing))
            {
                existing.UpdateTarget(msg.entity.position, GetYawFromDirection(msg.entity.direction), msg.entity.speed);
                return;
            }

            // NPC 与玩家统一创建流程，仅颜色 / 标记不同
            bool isNpc = msg.entity.type == EntityType.NPC;
            Color bodyColor = isNpc ? new Color(0.3f, 0.8f, 0.3f) : GenerateColorFromId(entityId);
            float yaw = GetYawFromDirection(msg.entity.direction);

            var go = new GameObject($"Remote_{msg.entity.type}_{entityId}");
            go.transform.SetParent(_root, false);

            var entity = go.AddComponent<RemotePlayerEntity>();
            entity.Initialize(entityId, msg.entity.name, msg.entity.position, yaw, msg.entity.speed, bodyColor, isNpc);

            _entities[entityId] = entity;

            Debug.Log($"[RemoteEntity] 实体进入视野: {msg.entity.name} (id={entityId}, type={msg.entity.type}) at {msg.entity.position}");
        }

        /// <summary>实体离开视野：销毁远程实体渲染对象。</summary>
        private void HandleEntityLeaveView(EntityLeaveView msg)
        {
            if (msg == null)
                return;

            long entityId = msg.entityId;
            if (_entities.TryGetValue(entityId, out var entity))
            {
                Destroy(entity.gameObject);
                _entities.Remove(entityId);
                Debug.Log($"[RemoteEntity] 实体离开视野: id={entityId}");
            }
        }

        /// <summary>实体移动广播：更新远程实体的目标位置与朝向。</summary>
        private void HandleEntityMoveBroadcast(EntityMoveBroadcast msg)
        {
            if (msg == null)
                return;

            if (_entities.TryGetValue(msg.entityId, out var entity))
            {
                float yaw = GetYawFromDirection(msg.direction);
                entity.UpdateTarget(msg.position, yaw, msg.speed);
            }
        }

        // ==================== 内部辅助 ====================

        /// <summary>清理所有远程实体。</summary>
        private void ClearAllEntities()
        {
            foreach (var kvp in _entities)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value.gameObject);
            }
            _entities.Clear();
        }

        /// <summary>从方向向量提取 Yaw 角度（绕 Y 轴旋转）。</summary>
        private static float GetYawFromDirection(ProtoVector3 direction)
        {
            Vector3 dir = direction;
            if (dir.sqrMagnitude < 0.001f)
                return 0f;

            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        /// <summary>基于实体 ID 生成稳定的颜色（HSV 色相空间）。</summary>
        private static Color GenerateColorFromId(long entityId)
        {
            float hue = (entityId * 0.618033988749f) % 1f; // 黄金角分布，色相分布均匀
            return Color.HSVToRGB(hue, 0.6f, 0.85f);
        }
    }
}
