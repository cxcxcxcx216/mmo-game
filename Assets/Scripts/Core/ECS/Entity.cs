using System;

namespace Minecraft.Core.ECS
{
    /// <summary>
    /// 实体 ID。在 ECS 架构中，实体本身只是一个轻量级的标识符，
    /// 所有数据都存储在组件中，所有逻辑都在系统中执行。
    /// 使用 int 而非引用类型，便于 GC 友好和缓存友好。
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        /// <summary>无效实体（ID = 0）。</summary>
        public static readonly Entity Null = new Entity(0);

        /// <summary>实体唯一标识符。0 表示无效。</summary>
        public readonly int Id;

        public Entity(int id) => Id = id;

        public bool IsValid => Id > 0;

        public bool Equals(Entity other) => Id == other.Id;
        public override bool Equals(object obj) => obj is Entity e && Equals(e);
        public override int GetHashCode() => Id;
        public override string ToString() => $"Entity#{Id}";

        public static bool operator ==(Entity a, Entity b) => a.Id == b.Id;
        public static bool operator !=(Entity a, Entity b) => a.Id != b.Id;
    }
}
