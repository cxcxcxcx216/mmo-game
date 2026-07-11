using UnityEngine;

namespace Minecraft.Core.ECS
{
    /// <summary>
    /// ECS 系统基类。系统是纯逻辑处理器，每帧被 World 调度执行。
    /// 系统不持有状态，所有数据来自 World 中的组件。
    /// 继承 MonoBehaviour 以便在 Unity 生命周期中调度。
    /// </summary>
    public abstract class SystemBase : MonoBehaviour
    {
        /// <summary>系统所属的世界（由 Bootstrap 注入）。</summary>
        protected World World { get; private set; }

        /// <summary>是否启用该系统。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>初始化系统（注入 World 引用）。</summary>
        public void Initialize(World world)
        {
            World = world;
            OnInitialize();
        }

        /// <summary>系统初始化回调（子类重写）。</summary>
        protected virtual void OnInitialize() { }

        /// <summary>每帧更新（子类重写）。由 WorldUpdater 统一调度。</summary>
        public virtual void OnUpdate(float deltaTime) { }

        /// <summary>系统销毁回调（子类重写）。</summary>
        protected virtual void OnShutdown() { }

        void OnDestroy() => OnShutdown();
    }

    /// <summary>
    /// 系统调度器。挂载到场景，统一驱动所有 SystemBase 的更新。
    /// 确保系统按固定顺序执行，避免 MonoBehaviour 的执行顺序不确定性。
    /// </summary>
    public class WorldUpdater : MonoBehaviour
    {
        private System.Collections.Generic.List<SystemBase> _systems = new System.Collections.Generic.List<SystemBase>();

        public void Register(SystemBase system)
        {
            if (system != null && !_systems.Contains(system))
                _systems.Add(system);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i].Enabled)
                    _systems[i].OnUpdate(dt);
            }
        }
    }
}
