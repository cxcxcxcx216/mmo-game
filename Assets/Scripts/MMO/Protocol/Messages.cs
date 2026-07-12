using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MMO.Protocol
{
    // ==================== 通用结构 ====================

    /// <summary>三维向量（位置/方向/速度），匹配 common.proto Vector3</summary>
    [Serializable]
    public struct ProtoVector3
    {
        public float x;
        public float y;
        public float z;

        public ProtoVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static implicit operator Vector3(ProtoVector3 v) => new(v.x, v.y, v.z);
        public static implicit operator ProtoVector3(Vector3 v) => new(v.x, v.y, v.z);

        public void Serialize(Stream s)
        {
            ProtoSerializer.WriteFloat(s, 1, x);
            ProtoSerializer.WriteFloat(s, 2, y);
            ProtoSerializer.WriteFloat(s, 3, z);
        }

        public static ProtoVector3 Deserialize(byte[] data, ref int offset)
        {
            var v = new ProtoVector3();
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: v.x = ProtoSerializer.ReadFloat(data, ref offset); break;
                    case 2: v.y = ProtoSerializer.ReadFloat(data, ref offset); break;
                    case 3: v.z = ProtoSerializer.ReadFloat(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return v;
        }

        public override string ToString() => $"({x:F1}, {y:F1}, {z:F1})";
    }

    // ==================== 登录协议 ====================

    public class LoginReq
    {
        public string account = "";
        public string password = "";
        public string clientVersion = "";
        public int platform;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteString(s, 1, account);
                ProtoSerializer.WriteString(s, 2, password);
                ProtoSerializer.WriteString(s, 3, clientVersion);
                ProtoSerializer.WriteInt32(s, 4, platform);
            });
        }
    }

    public class LoginAck
    {
        public int code;
        public string sessionKey = "";
        public long accountId;

        public static LoginAck Deserialize(byte[] data)
        {
            var msg = new LoginAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2: msg.sessionKey = ProtoSerializer.ReadString(data, ref offset); break;
                    case 3: msg.accountId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class ServerInfo
    {
        public int serverId;
        public string name = "";
        public int status;
        public int onlineCount;

        public static ServerInfo Deserialize(byte[] data, ref int offset)
        {
            var msg = new ServerInfo();
            int end = offset + (int)ProtoSerializer.ReadVarint(data, ref offset);
            // 这里的 data 是子消息体，直接在原数组上读
            // 但 ReadMessageBytes 已经截取了子数组，这里直接处理
            return DeserializeBody(data, ref offset, end);
        }

        public static ServerInfo DeserializeBody(byte[] data, ref int offset, int end)
        {
            var msg = new ServerInfo();
            while (offset < end)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.serverId = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2: msg.name = ProtoSerializer.ReadString(data, ref offset); break;
                    case 3: msg.status = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 4: msg.onlineCount = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class ServerListReq
    {
        public int platform;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteInt32(s, 1, platform);
            });
        }
    }

    public class ServerListAck
    {
        public int code;
        public List<ServerInfo> servers = new();

        public static ServerListAck Deserialize(byte[] data)
        {
            var msg = new ServerListAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2:
                        byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                        int subOff = 0;
                        msg.servers.Add(ServerInfo.DeserializeBody(sub, ref subOff, sub.Length));
                        break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class RoleInfo
    {
        public long roleId;
        public string name = "";
        public int level;
        public int profession;
        public long lastLoginTime;

        public static RoleInfo DeserializeBody(byte[] data, ref int offset, int end)
        {
            var msg = new RoleInfo();
            while (offset < end)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.roleId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 2: msg.name = ProtoSerializer.ReadString(data, ref offset); break;
                    case 3: msg.level = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 4: msg.profession = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 5: msg.lastLoginTime = ProtoSerializer.ReadInt64(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class RoleListReq
    {
        public int serverId;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteInt32(s, 1, serverId);
            });
        }
    }

    public class RoleListAck
    {
        public int code;
        public List<RoleInfo> roles = new();

        public static RoleListAck Deserialize(byte[] data)
        {
            var msg = new RoleListAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2:
                        byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                        int subOff = 0;
                        msg.roles.Add(RoleInfo.DeserializeBody(sub, ref subOff, sub.Length));
                        break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class CreateRoleReq
    {
        public int serverId;
        public string name = "";
        public int profession;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteInt32(s, 1, serverId);
                ProtoSerializer.WriteString(s, 2, name);
                ProtoSerializer.WriteInt32(s, 3, profession);
            });
        }
    }

    public class CreateRoleAck
    {
        public int code;
        public RoleInfo role;

        public static CreateRoleAck Deserialize(byte[] data)
        {
            var msg = new CreateRoleAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2:
                        byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                        int subOff = 0;
                        msg.role = RoleInfo.DeserializeBody(sub, ref subOff, sub.Length);
                        break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class EnterGameReq
    {
        public long roleId;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteInt64(s, 1, roleId);
            });
        }
    }

    public class PlayerInfo
    {
        public long roleId;
        public string name = "";
        public int level;
        public int profession;
        public long gold;
        public ProtoVector3 position;
        public ProtoVector3 direction;
        public int spaceId;

        public static PlayerInfo DeserializeBody(byte[] data, ref int offset, int end)
        {
            var msg = new PlayerInfo();
            while (offset < end)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.roleId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 2: msg.name = ProtoSerializer.ReadString(data, ref offset); break;
                    case 3: msg.level = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 4: msg.profession = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 5: msg.gold = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 6:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.position = ProtoVector3.Deserialize(sub, ref subOff);
                            break;
                        }
                    case 7:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.direction = ProtoVector3.Deserialize(sub, ref subOff);
                            break;
                        }
                    case 8: msg.spaceId = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class EnterGameAck
    {
        public int code;
        public PlayerInfo player;
        public long serverTime;

        public static EnterGameAck Deserialize(byte[] data)
        {
            var msg = new EnterGameAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.player = PlayerInfo.DeserializeBody(sub, ref subOff, sub.Length);
                            break;
                        }
                    case 3: msg.serverTime = ProtoSerializer.ReadInt64(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class LeaveGameReq
    {
        public byte[] Serialize() => ProtoSerializer.Serialize(_ => { });
    }

    public class LeaveGameAck
    {
        public int code;

        public static LeaveGameAck Deserialize(byte[] data)
        {
            var msg = new LeaveGameAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    // ==================== 游戏协议 ====================

    public class MoveReq
    {
        public ProtoVector3 position;
        public ProtoVector3 direction;
        public float speed = 5f;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                WriteVec3(s, 1, position);
                WriteVec3(s, 2, direction);
                ProtoSerializer.WriteFloat(s, 3, speed);
            });
        }

        private static void WriteVec3(Stream s, int fieldNumber, ProtoVector3 v)
        {
            using var ms = new MemoryStream();
            v.Serialize(ms);
            byte[] data = ms.ToArray();
            if (data.Length == 0) return;
            ProtoSerializer.WriteTag(s, fieldNumber, 2);
            ProtoSerializer.WriteVarint(s, (ulong)data.Length);
            s.Write(data, 0, data.Length);
        }
    }

    public class EntitySyncInfo
    {
        public long id;
        public EntityType type;
        public ProtoVector3 position;
        public ProtoVector3 direction;
        public float speed;
        public string name = "";
        public int level;

        public static EntitySyncInfo DeserializeBody(byte[] data, ref int offset, int end)
        {
            var msg = new EntitySyncInfo();
            while (offset < end)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.id = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 2: msg.type = (EntityType)ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 3:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.position = ProtoVector3.Deserialize(sub, ref subOff);
                            break;
                        }
                    case 4:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.direction = ProtoVector3.Deserialize(sub, ref subOff);
                            break;
                        }
                    case 5: msg.speed = ProtoSerializer.ReadFloat(data, ref offset); break;
                    case 6: msg.name = ProtoSerializer.ReadString(data, ref offset); break;
                    case 7: msg.level = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class EntityEnterView
    {
        public EntitySyncInfo entity;

        public static EntityEnterView Deserialize(byte[] data)
        {
            var msg = new EntityEnterView();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.entity = EntitySyncInfo.DeserializeBody(sub, ref subOff, sub.Length);
                            break;
                        }
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class EntityLeaveView
    {
        public long entityId;

        public static EntityLeaveView Deserialize(byte[] data)
        {
            var msg = new EntityLeaveView();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.entityId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class EntityMoveBroadcast
    {
        public long entityId;
        public ProtoVector3 position;
        public ProtoVector3 direction;
        public float speed;

        public static EntityMoveBroadcast Deserialize(byte[] data)
        {
            var msg = new EntityMoveBroadcast();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.entityId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 2:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.position = ProtoVector3.Deserialize(sub, ref subOff);
                            break;
                        }
                    case 3:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.direction = ProtoVector3.Deserialize(sub, ref subOff);
                            break;
                        }
                    case 4: msg.speed = ProtoSerializer.ReadFloat(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class EntitySpawn
    {
        public EntitySyncInfo entity;

        public static EntitySpawn Deserialize(byte[] data)
        {
            var ev = EntityEnterView.Deserialize(data);
            return new EntitySpawn { entity = ev.entity };
        }
    }

    public class EntityDespawn
    {
        public long entityId;
        public int reason;

        public static EntityDespawn Deserialize(byte[] data)
        {
            var msg = new EntityDespawn();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.entityId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 2: msg.reason = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    // ==================== 背包协议 ====================

    public class ItemInfo
    {
        public long itemId;
        public int configId;
        public int count;
        public string name = "";
        public int type;
        public string icon = "";

        public static ItemInfo DeserializeBody(byte[] data, ref int offset, int end)
        {
            var msg = new ItemInfo();
            while (offset < end)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.itemId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 2: msg.configId = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 3: msg.count = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 4: msg.name = ProtoSerializer.ReadString(data, ref offset); break;
                    case 5: msg.type = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 6: msg.icon = ProtoSerializer.ReadString(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class UseItemReq
    {
        public long itemId;
        public int count;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteInt64(s, 1, itemId);
                ProtoSerializer.WriteInt32(s, 2, count);
            });
        }
    }

    public class UseItemAck
    {
        public int code;
        public long itemId;
        public int remaining;

        public static UseItemAck Deserialize(byte[] data)
        {
            var msg = new UseItemAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2: msg.itemId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 3: msg.remaining = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class DiscardItemReq
    {
        public long itemId;
        public int count;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteInt64(s, 1, itemId);
                ProtoSerializer.WriteInt32(s, 2, count);
            });
        }
    }

    public class DiscardItemAck
    {
        public int code;
        public long itemId;
        public int remaining;

        public static DiscardItemAck Deserialize(byte[] data)
        {
            var msg = new DiscardItemAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.code = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2: msg.itemId = ProtoSerializer.ReadInt64(data, ref offset); break;
                    case 3: msg.remaining = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    public class InventorySync
    {
        public List<ItemInfo> items = new();
        public int capacity;

        public static InventorySync Deserialize(byte[] data)
        {
            var msg = new InventorySync();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1:
                        {
                            byte[] sub = ProtoSerializer.ReadMessageBytes(data, ref offset);
                            int subOff = 0;
                            msg.items.Add(ItemInfo.DeserializeBody(sub, ref subOff, sub.Length));
                            break;
                        }
                    case 2: msg.capacity = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    // ==================== 心跳 ====================

    public class HeartbeatReq
    {
        public byte[] Serialize() => Array.Empty<byte>();
    }

    public class HeartbeatAck
    {
        public long serverTime;

        public static HeartbeatAck Deserialize(byte[] data)
        {
            var msg = new HeartbeatAck();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.serverTime = ProtoSerializer.ReadInt64(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }

    // ==================== 地图状态同步 ====================

    /// <summary>方块变更请求（客户端→服务端）。</summary>
    public class BlockChangeReq
    {
        public int x;
        public int y;
        public int z;
        public int blockType;

        public byte[] Serialize()
        {
            return ProtoSerializer.Serialize(s =>
            {
                ProtoSerializer.WriteInt32(s, 1, x);
                ProtoSerializer.WriteInt32(s, 2, y);
                ProtoSerializer.WriteInt32(s, 3, z);
                ProtoSerializer.WriteInt32(s, 4, blockType);
            });
        }
    }

    /// <summary>方块变更广播（服务端→客户端）。</summary>
    public class BlockChangeBroadcast
    {
        public int x;
        public int y;
        public int z;
        public int blockType;

        public static BlockChangeBroadcast Deserialize(byte[] data)
        {
            var msg = new BlockChangeBroadcast();
            int offset = 0;
            while (offset < data.Length)
            {
                int tag = ProtoSerializer.ReadTag(data, ref offset);
                int field = tag >> 3;
                int wire = tag & 0x7;
                switch (field)
                {
                    case 1: msg.x = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 2: msg.y = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 3: msg.z = ProtoSerializer.ReadInt32(data, ref offset); break;
                    case 4: msg.blockType = ProtoSerializer.ReadInt32(data, ref offset); break;
                    default: ProtoSerializer.SkipField(data, ref offset, wire); break;
                }
            }
            return msg;
        }
    }
}
