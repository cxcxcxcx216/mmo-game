using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Minecraft.UI
{
    /// <summary>
    /// 修正 Unity Editor Game View 固定分辨率与实际窗口大小不匹配时的坐标错位问题。
    /// <para>
    /// 当 Game View 设为固定分辨率（如 3840x2160）但实际窗口大小不同时，
    /// Canvas 的渲染区域（pixelRect）与 Screen.width/height 不一致，
    /// 导致 Input.mousePosition 返回的坐标与 GraphicRaycaster 期望的坐标系不同，
    /// 点击位置偏移、输入框无法获得焦点。
    /// </para>
    /// <para>
    /// 此组件覆盖 mousePosition，将 Screen 坐标映射到 Canvas 渲染坐标。
    /// 需挂载到 EventSystem 所在的 GameObject 上。
    /// </para>
    /// </summary>
    [RequireComponent(typeof(EventSystem))]
    public class ScaledBaseInput : BaseInput
    {
        private Canvas _canvas;
        private float _scaleX = 1f;
        private float _scaleY = 1f;
        private bool _needScale;

        protected override void Awake()
        {
            base.Awake();
            RefreshCanvasReference();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshCanvasReference();
        }

        private void RefreshCanvasReference()
        {
            _canvas = FindObjectOfType<Canvas>();
            if (_canvas == null)
            {
                _needScale = false;
                return;
            }

            float canvasW = _canvas.pixelRect.width;
            float canvasH = _canvas.pixelRect.height;
            float screenW = Screen.width;
            float screenH = Screen.height;

            _scaleX = canvasW / screenW;
            _scaleY = canvasH / screenH;
            // 当 Canvas 渲染区域与 Screen 大小不一致时才需要缩放
            _needScale = Mathf.Abs(_scaleX - 1f) > 0.01f || Mathf.Abs(_scaleY - 1f) > 0.01f;

            if (_needScale)
                Debug.Log($"[ScaledBaseInput] 启用坐标缩放: Screen=({screenW},{screenH}), Canvas=({canvasW},{canvasH}), scale=({_scaleX:F3},{_scaleY:F3})");
        }

        private void LateUpdate()
        {
            // Canvas 渲染区域可能在运行时变化（如 Game View 窗口大小改变）
            if (_canvas == null)
            {
                RefreshCanvasReference();
                return;
            }

            float canvasW = _canvas.pixelRect.width;
            float canvasH = _canvas.pixelRect.height;
            float screenW = Screen.width;
            float screenH = Screen.height;
            float newScaleX = canvasW / screenW;
            float newScaleY = canvasH / screenH;

            if (Mathf.Abs(newScaleX - _scaleX) > 0.01f || Mathf.Abs(newScaleY - _scaleY) > 0.01f)
            {
                _scaleX = newScaleX;
                _scaleY = newScaleY;
                _needScale = Mathf.Abs(_scaleX - 1f) > 0.01f || Mathf.Abs(_scaleY - 1f) > 0.01f;
                if (_needScale)
                    Debug.Log($"[ScaledBaseInput] 坐标缩放更新: scale=({_scaleX:F3},{_scaleY:F3})");
            }
        }

        public override Vector2 mousePosition
        {
            get
            {
                Vector2 raw = base.mousePosition;
                if (!_needScale)
                    return raw;

                return new Vector2(raw.x * _scaleX, raw.y * _scaleY);
            }
        }

        public override bool mousePresent => base.mousePresent;
    }
}
