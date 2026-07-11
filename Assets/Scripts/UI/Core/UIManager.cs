using System.Collections.Generic;
using UnityEngine;

namespace Minecraft.UI
{
    /// <summary>
    /// UI 管理器。单例 <see cref="MonoBehaviour"/>，统一注册、查找、显示、隐藏所有 UI 面板。
    /// <para>
    /// 使用方式：
    /// 1. 通过 <see cref="Register"/> 注册面板。
    /// 2. 通过 <see cref="Show"/> / <see cref="Hide"/> / <see cref="HideAll"/> 控制显隐。
    /// 3. 通过 <see cref="Get{T}"/> 获取面板实例并调用面板特有方法。
    /// </para>
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ==================== 单例 ====================

        /// <summary>全局单例实例。</summary>
        public static UIManager Instance { get; private set; }

        // ==================== 数据 ====================

        /// <summary>已注册的面板字典，键为面板名称。</summary>
        private readonly Dictionary<string, UIBase> _panels = new Dictionary<string, UIBase>();

        // ==================== Unity 生命周期 ====================

        /// <summary>初始化单例。本游戏为单场景，无需 DontDestroyOnLoad。</summary>
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

        // ==================== 公开方法 ====================

        /// <summary>
        /// 注册面板。同名面板会被覆盖。
        /// </summary>
        /// <param name="name">面板名称（注册键）。</param>
        /// <param name="panel">面板实例。</param>
        public void Register(string name, UIBase panel)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning("[UIManager] 注册失败：名称为空。");
                return;
            }

            if (panel == null)
            {
                Debug.LogWarning($"[UIManager] 注册失败：面板为空（name={name}）。");
                return;
            }

            _panels[name] = panel;
        }

        /// <summary>
        /// 获取指定名称的面板，并转换为指定类型。
        /// </summary>
        /// <typeparam name="T">面板类型（须为 <see cref="UIBase"/> 子类）。</typeparam>
        /// <param name="name">面板名称。</param>
        /// <returns>面板实例；未注册或类型不匹配时返回 null。</returns>
        public T Get<T>(string name) where T : UIBase
        {
            if (_panels.TryGetValue(name, out UIBase panel))
                return panel as T;

            return null;
        }

        /// <summary>显示指定面板。</summary>
        /// <param name="name">面板名称。</param>
        public void Show(string name)
        {
            if (_panels.TryGetValue(name, out UIBase panel))
            {
                panel.Show();
            }
            else
            {
                Debug.LogWarning($"[UIManager] 显示失败：未注册面板 '{name}'。");
            }
        }

        /// <summary>隐藏指定面板。</summary>
        /// <param name="name">面板名称。</param>
        public void Hide(string name)
        {
            if (_panels.TryGetValue(name, out UIBase panel))
                panel.Hide();
        }

        /// <summary>隐藏所有已注册面板。</summary>
        public void HideAll()
        {
            foreach (var pair in _panels)
                pair.Value.Hide();
        }
    }
}
