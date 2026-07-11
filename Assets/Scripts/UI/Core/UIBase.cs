using UnityEngine;

namespace Minecraft.UI
{
    /// <summary>
    /// UI 面板基类（抽象）。所有 UI 面板继承此类，
    /// 通过 <see cref="Show"/> / <see cref="Hide"/> / <see cref="Toggle"/> 控制显隐，
    /// 并可在 <see cref="OnShow"/> / <see cref="OnHide"/> 中执行面板特定的显示/隐藏逻辑。
    /// </summary>
    public abstract class UIBase : MonoBehaviour
    {
        // ==================== 状态属性 ====================

        /// <summary>当前面板是否可见。子类可设置（用于自定义显隐实现）。</summary>
        public bool IsVisible { get; protected set; }

        // ==================== 公开方法 ====================

        /// <summary>
        /// 初始化面板。子类可重写以创建子元素、绑定数据等。
        /// 应在面板注册后、首次显示前调用一次。
        /// </summary>
        public virtual void Initialize()
        {
        }

        /// <summary>
        /// 显示面板。激活 GameObject 并触发 <see cref="OnShow"/>。
        /// 子类可重写以改用 CanvasGroup 等方式控制显隐（保持 Update 运行）。
        /// </summary>
        public virtual void Show()
        {
            gameObject.SetActive(true);
            IsVisible = true;
            OnShow();
        }

        /// <summary>
        /// 隐藏面板。关闭 GameObject 并触发 <see cref="OnHide"/>。
        /// 子类可重写以改用 CanvasGroup 等方式控制显隐（保持 Update 运行）。
        /// </summary>
        public virtual void Hide()
        {
            gameObject.SetActive(false);
            IsVisible = false;
            OnHide();
        }

        /// <summary>切换面板显隐状态。</summary>
        public virtual void Toggle()
        {
            if (IsVisible)
                Hide();
            else
                Show();
        }

        // ==================== 子类回调 ====================

        /// <summary>显示后的回调，子类可重写以执行显示逻辑（如刷新数据）。</summary>
        public virtual void OnShow()
        {
        }

        /// <summary>隐藏后的回调，子类可重写以执行隐藏逻辑。</summary>
        public virtual void OnHide()
        {
        }
    }
}
