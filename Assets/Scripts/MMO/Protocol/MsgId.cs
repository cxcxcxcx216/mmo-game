namespace MMO.Protocol
{
    /// <summary>
    /// 消息 ID 枚举，与服务端 msg_id.proto 完全对齐。
    /// 约定：请求 ID 偶数，响应/推送 ID 奇数。
    /// </summary>
    public static class MsgId
    {
        public const int NONE = 0;

        // ---- 登录段 1000-1999 ----
        public const int LOGIN_REQ = 1000;
        public const int LOGIN_ACK = 1001;
        public const int SERVER_LIST_REQ = 1002;
        public const int SERVER_LIST_ACK = 1003;
        public const int ROLE_LIST_REQ = 1004;
        public const int ROLE_LIST_ACK = 1005;
        public const int CREATE_ROLE_REQ = 1006;
        public const int CREATE_ROLE_ACK = 1007;
        public const int ENTER_GAME_REQ = 1008;
        public const int ENTER_GAME_ACK = 1009;

        // ---- 游戏段 2000-2999 ----
        public const int MOVE_REQ = 2000;
        public const int ENTITY_ENTER_VIEW = 2001;
        public const int ENTITY_LEAVE_VIEW = 2003;
        public const int ENTITY_MOVE_BROADCAST = 2005;
        public const int ENTITY_SPAWN = 2007;
        public const int ENTITY_DESPAWN = 2009;
        public const int LEAVE_GAME_REQ = 2010;
        public const int LEAVE_GAME_ACK = 2011;

        // ---- 扩展段 2050-2099 ----
        public const int MOVE_ACK = 2050;
        public const int AOI_SYNC_PACKET = 2051;
        public const int CAST_SKILL_REQ = 2052;
        public const int COMBAT_RESULT = 2053;

        // ---- 背包段 2020-2039 ----
        public const int USE_ITEM_REQ = 2020;
        public const int USE_ITEM_ACK = 2021;
        public const int DISCARD_ITEM_REQ = 2022;
        public const int DISCARD_ITEM_ACK = 2023;
        public const int INVENTORY_SYNC = 2025;

        // ---- 心跳段 3000-3999 ----
        public const int HEARTBEAT_REQ = 3000;
        public const int HEARTBEAT_ACK = 3001;
    }

    /// <summary>错误码，与服务端 common.proto 对齐</summary>
    public static class ErrorCode
    {
        public const int SUCCESS = 0;
        public const int UNKNOWN = 1;
        public const int PARAM_INVALID = 2;
        public const int ACCOUNT_NOT_EXIST = 1001;
        public const int PASSWORD_WRONG = 1002;
        public const int ACCOUNT_BANNED = 1003;
        public const int ROLE_NOT_EXIST = 2001;
        public const int ROLE_NAME_EXISTS = 2002;
        public const int ROLE_LIMIT = 2003;
        public const int NOT_IN_GAME = 3001;
        public const int MOVE_INVALID = 3002;
        public const int CELL_FULL = 3003;
        public const int ITEM_NOT_EXIST = 4001;
        public const int ITEM_NOT_DISCARDABLE = 4002;
        public const int ITEM_CONFIG_NOT_EXIST = 4003;
        public const int BAG_FULL = 4004;
        public const int ITEM_COUNT_NOT_ENOUGH = 4005;
        public const int SESSION_EXPIRED = 9001;
        public const int INNER_ERROR = 9999;

        public static string Describe(int code)
        {
            return code switch
            {
                SUCCESS => "成功",
                UNKNOWN => "未知错误",
                PARAM_INVALID => "参数无效",
                ACCOUNT_NOT_EXIST => "账号不存在",
                PASSWORD_WRONG => "密码错误",
                ACCOUNT_BANNED => "账号已封禁",
                ROLE_NOT_EXIST => "角色不存在",
                ROLE_NAME_EXISTS => "角色名已存在",
                ROLE_LIMIT => "角色数量已达上限",
                NOT_IN_GAME => "未在游戏中",
                MOVE_INVALID => "移动无效",
                CELL_FULL => "Cell 已满",
                ITEM_NOT_EXIST => "道具不存在",
                ITEM_NOT_DISCARDABLE => "道具不可丢弃",
                ITEM_CONFIG_NOT_EXIST => "道具配置不存在",
                BAG_FULL => "背包已满",
                ITEM_COUNT_NOT_ENOUGH => "道具数量不足",
                SESSION_EXPIRED => "会话已过期",
                INNER_ERROR => "服务器内部错误",
                _ => $"未知错误码({code})"
            };
        }
    }

    /// <summary>实体类型</summary>
    public enum EntityType
    {
        UNKNOWN = 0,
        PLAYER = 1,
        NPC = 2,
        MONSTER = 3,
        ITEM = 4,
    }
}
