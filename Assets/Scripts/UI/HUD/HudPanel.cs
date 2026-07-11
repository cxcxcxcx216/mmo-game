using UnityEngine;
using UnityEngine.UI;
using Minecraft.Game.Player;
using Minecraft.Game.World;

namespace Minecraft.UI
{
    /// <summary>
    /// 游戏 HUD 面板。左上角显示 FPS、玩家坐标、当前方块类型、朝向角度；
    /// 右上角显示十字准星（白色半透明十字，20×20）。每帧更新信息。
    /// <para>玩家引用在运行时懒加载查找（<see cref="PlayerController"/> / <see cref="PlayerInteraction"/>），
    /// 玩家尚未创建时显示占位符 "—"。</para>
    /// </summary>
    public class HudPanel : UIBase
    {
        // ==================== 配置常量 ====================

        /// <summary>信息区距屏幕左上角的边距。</summary>
        private const float InfoMargin = 10f;

        /// <summary>信息区尺寸。</summary>
        private static readonly Vector2 InfoSize = new Vector2(280f, 110f);

        /// <summary>十字准星尺寸（20×20）。</summary>
        private const float CrosshairSize = 20f;

        /// <summary>十字准星线条粗细。</summary>
        private const float CrosshairThickness = 2f;

        /// <summary>十字准星距屏幕右上角的边距。</summary>
        private const float CrosshairMargin = 20f;

        /// <summary>FPS 刷新间隔（秒）。</summary>
        private const float FpsUpdateInterval = 0.5f;

        // ==================== 运行时状态 ====================

        /// <summary>信息文本组件。</summary>
        private Text _infoText;

        /// <summary>玩家控制器（懒加载）。</summary>
        private PlayerController _player;

        /// <summary>玩家交互（懒加载，用于获取当前选中方块）。</summary>
        private PlayerInteraction _interaction;

        /// <summary>FPS 累计时间。</summary>
        private float _fpsAccumTime;

        /// <summary>FPS 累计帧数。</summary>
        private int _fpsFrameCount;

        /// <summary>当前显示的 FPS 值。</summary>
        private int _fpsValue;

        // ==================== 初始化 ====================

        /// <summary>初始化 HUD：自身铺满屏幕，创建信息区与十字准星。</summary>
        public override void Initialize()
        {
            base.Initialize();

            // 自身铺满屏幕（不添加 Image，避免遮挡游戏点击）
            var selfRect = GetComponent<RectTransform>();
            if (selfRect != null)
                Stretch(selfRect);

            CreateInfoArea();
            CreateCrosshair();
        }

        // ==================== Unity 生命周期 ====================

        /// <summary>每帧更新：懒加载玩家、统计 FPS、刷新信息文本。</summary>
        private void Update()
        {
            // 懒加载查找玩家（玩家可能在 HUD 之后创建）
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                _interaction = FindObjectOfType<PlayerInteraction>();
            }

            UpdateFps();
            UpdateInfo();
        }

        // ==================== UI 构建 ====================

        /// <summary>创建左上角信息区（半透明背景 + 文本）。</summary>
        private void CreateInfoArea()
        {
            // 半透明背景容器，锚定左上角
            var bg = UIFactory.CreateImage(transform, "InfoBg",
                new Color(0f, 0f, 0f, 0.4f), Vector2.zero, InfoSize);
            bg.raycastTarget = false;

            var bgRect = bg.rectTransform;
            bgRect.anchorMin = new Vector2(0f, 1f);
            bgRect.anchorMax = new Vector2(0f, 1f);
            bgRect.pivot = new Vector2(0f, 1f);
            bgRect.anchoredPosition = new Vector2(InfoMargin, -InfoMargin);

            // 信息文本（铺满容器，左上对齐，留内边距）
            _infoText = UIFactory.CreateText(bgRect, "InfoText", string.Empty, 14,
                Color.white, Vector2.zero, InfoSize);
            _infoText.alignment = TextAnchor.UpperLeft;

            var textRect = _infoText.rectTransform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(6f, 6f);   // 左下内边距
            textRect.offsetMax = new Vector2(-6f, -6f); // 右上内边距
        }

        /// <summary>创建右上角十字准星（白色半透明十字，20×20）。</summary>
        private void CreateCrosshair()
        {
            // 容器（透明），锚定右上角
            var container = new GameObject("Crosshair", typeof(RectTransform));
            container.transform.SetParent(transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(1f, 1f);
            containerRect.anchorMax = new Vector2(1f, 1f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(-CrosshairMargin, -CrosshairMargin);
            containerRect.sizeDelta = new Vector2(CrosshairSize, CrosshairSize);

            // 十字颜色：白色半透明
            Color crossColor = new Color(1f, 1f, 1f, 0.6f);

            // 水平横条（铺满容器宽度）
            var hBar = UIFactory.CreateImage(containerRect, "CrossH",
                crossColor, Vector2.zero, new Vector2(CrosshairSize, CrosshairThickness));
            hBar.raycastTarget = false;

            // 垂直竖条（铺满容器高度）
            var vBar = UIFactory.CreateImage(containerRect, "CrossV",
                crossColor, Vector2.zero, new Vector2(CrosshairThickness, CrosshairSize));
            vBar.raycastTarget = false;
        }

        // ==================== 数据更新 ====================

        /// <summary>累计帧数与时间，每隔 <see cref="FpsUpdateInterval"/> 秒计算一次 FPS。</summary>
        private void UpdateFps()
        {
            _fpsAccumTime += Time.unscaledDeltaTime;
            _fpsFrameCount++;

            if (_fpsAccumTime >= FpsUpdateInterval)
            {
                _fpsValue = Mathf.RoundToInt(_fpsFrameCount / _fpsAccumTime);
                _fpsAccumTime = 0f;
                _fpsFrameCount = 0;
            }
        }

        /// <summary>刷新信息文本：FPS、坐标、方块类型、朝向角度。</summary>
        private void UpdateInfo()
        {
            string fps = _fpsValue.ToString();
            string posText;
            string blockText;
            string facingText;

            if (_player != null)
            {
                Vector3 p = _player.Position;
                posText = $"{p.x:F1} / {p.y:F1} / {p.z:F1}";

                // 朝向角度（Yaw，水平方向）
                facingText = $"{_player.Rotation.eulerAngles.y:F1}°";

                // 当前选中方块类型
                if (_interaction != null)
                {
                    BlockType type = _interaction.SelectedBlock;
                    blockText = BlockDefinition.Get(type).Name;
                }
                else
                {
                    blockText = "—";
                }
            }
            else
            {
                posText = "—";
                blockText = "—";
                facingText = "—";
            }

            _infoText.text =
                $"FPS: {fps}\n" +
                $"XYZ: {posText}\n" +
                $"方块: {blockText}\n" +
                $"朝向: {facingText}";
        }

        // ==================== 内部辅助 ====================

        /// <summary>设置 RectTransform 为铺满父物体。</summary>
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
