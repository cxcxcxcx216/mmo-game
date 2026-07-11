using System;
using System.Collections.Generic;

namespace Minecraft.Core.ECS
{
    /// <summary>
    /// ECS 世界。管理所有实体和组件的存储与查询。
    /// 设计要点：
    /// 1. 组件按类型分组存储（Archetype 的简化版），提高缓存命中率
    /// 2. 实体回收复用，避免 ID 无限增长
    /// 3. 线程安全：仅主线程访问（Unity 主线程模型）
    /// </summary>
    public sealed class World
    {
        // 实体 ID 分配器
        private int _nextEntityId = 1;

        // 回收的实体 ID（删除后复用）
        private readonly Stack<int> _recycledIds = new Stack<int>();

        // 所有存活的实体集合
        private readonly HashSet<int> _aliveEntities = new HashSet<int>();

        // 组件存储：Type -> (entityId -> component)
        private readonly Dictionary<Type, Dictionary<int, IComponent>> _components = new Dictionary<Type, Dictionary<int, IComponent>>();

        // 实体到组件类型的反向索引（用于销毁实体时快速清理）
        private readonly Dictionary<int, HashSet<Type>> _entityComponentTypes = new Dictionary<int, HashSet<Type>>();

        /// <summary>当前存活的实体数量。</summary>
        public int EntityCount => _aliveEntities.Count;

        // ==================== 实体管理 ====================

        /// <summary>创建一个新实体。</summary>
        public Entity CreateEntity()
        {
            int id = _recycledIds.Count > 0 ? _recycledIds.Pop() : _nextEntityId++;
            _aliveEntities.Add(id);
            _entityComponentTypes[id] = new HashSet<Type>();
            return new Entity(id);
        }

        /// <summary>销毁实体及其所有组件。</summary>
        public void DestroyEntity(Entity entity)
        {
            if (!entity.IsValid || !_aliveEntities.Contains(entity.Id))
                return;

            // 清理所有组件
            if (_entityComponentTypes.TryGetValue(entity.Id, out var types))
            {
                foreach (var type in types)
                {
                    if (_components.TryGetValue(type, out var pool))
                        pool.Remove(entity.Id);
                }
                _entityComponentTypes.Remove(entity.Id);
            }

            _aliveEntities.Remove(entity.Id);
            _recycledIds.Push(entity.Id);
        }

        /// <summary>判断实体是否存活。</summary>
        public bool Exists(Entity entity) => entity.IsValid && _aliveEntities.Contains(entity.Id);

        // ==================== 组件管理 ====================

        /// <summary>为实体添加组件。如已存在同类型组件则替换。</summary>
        public void AddComponent<T>(Entity entity, T component) where T : class, IComponent
        {
            if (!Exists(entity))
                throw new ArgumentException($"Entity {entity} does not exist", nameof(entity));
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            var type = typeof(T);
            if (!_components.TryGetValue(type, out var pool))
            {
                pool = new Dictionary<int, IComponent>();
                _components[type] = pool;
            }
            pool[entity.Id] = component;
            _entityComponentTypes[entity.Id].Add(type);
        }

        /// <summary>获取实体上的组件，不存在返回 null。</summary>
        public T GetComponent<T>(Entity entity) where T : class, IComponent
        {
            if (!entity.IsValid) return null;
            if (_components.TryGetValue(typeof(T), out var pool) && pool.TryGetValue(entity.Id, out var comp))
                return comp as T;
            return null;
        }

        /// <summary>移除实体的指定类型组件。</summary>
        public bool RemoveComponent<T>(Entity entity) where T : class, IComponent
        {
            if (!entity.IsValid) return false;
            var type = typeof(T);
            if (_components.TryGetValue(type, out var pool) && pool.Remove(entity.Id))
            {
                _entityComponentTypes[entity.Id].Remove(type);
                return true;
            }
            return false;
        }

        /// <summary>判断实体是否有指定类型组件。</summary>
        public bool HasComponent<T>(Entity entity) where T : class, IComponent
        {
            if (!entity.IsValid) return false;
            return _components.TryGetValue(typeof(T), out var pool) && pool.ContainsKey(entity.Id);
        }

        // ==================== 查询 ====================

        /// <summary>
        /// 查询所有拥有指定类型组件的实体。
        /// 返回迭代器，避免分配 List。调用方如需多次遍历可自行 ToList。
        /// </summary>
        public IEnumerable<(Entity entity, T component)> Query<T>() where T : class, IComponent
        {
            if (!_components.TryGetValue(typeof(T), out var pool))
                yield break;

            foreach (var kvp in pool)
            {
                if (_aliveEntities.Contains(kvp.Key))
                    yield return (new Entity(kvp.Key), kvp.Value as T);
            }
        }

        /// <summary>查询所有拥有两类组件的实体（联合查询）。</summary>
        public IEnumerable<(Entity entity, T1 a, T2 b)> Query<T1, T2>()
            where T1 : class, IComponent
            where T2 : class, IComponent
        {
            // 选择数量较少的组件池作为驱动
            var pool1 = _components.TryGetValue(typeof(T1), out var p1) ? p1 : null;
            if (pool1 == null) yield break;

            foreach (var kvp in pool1)
            {
                if (!_aliveEntities.Contains(kvp.Key)) continue;
                if (_components.TryGetValue(typeof(T2), out var p2) && p2.TryGetValue(kvp.Key, out var comp2))
                {
                    yield return (new Entity(kvp.Key), kvp.Value as T1, comp2 as T2);
                }
            }
        }
    }
}
