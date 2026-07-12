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
        private const float InfoMargin = 14f;

        /// <summary>信息区尺寸。</summary>
        private static readonly Vector2 InfoSize = new Vector2(260f, 116f);

        /// <summary>十字准星尺寸（24×24，略大一些更清晰）。</summary>
        private const float CrosshairSize = 24f;

        /// <summary>十字准星线条粗细。</summary>
        private const float CrosshairThickness = 3f;

        /// <summary>FPS 刷新间隔（秒）。</summary>
        private const float FpsUpdateInterval = 0.5f;

        /// <summary>玩家引用兜底查找的重试间隔（秒），避免每帧全场景遍历。</summary>
        private const float FindRetryInterval = 0.5f;

        // ==================== 运行时状态 ====================

        /// <summary>信息文本组件。</summary>
        private Text _infoText;

        /// <summary>玩家控制器（懒加载，可由 GameBootstrap 主动注入）。</summary>
        private PlayerController _player;

        /// <summary>玩家交互（懒加载，用于获取当前选中方块，可由 GameBootstrap 主动注入）。</summary>
        private PlayerInteraction _interaction;

        /// <summary>玩家引用兜底查找的计时器（节流用）。</summary>
        private float _findRetryTimer;

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

        /// <summary>每帧更新：懒加载玩家（节流兜底）、统计 FPS、刷新信息文本。</summary>
        private void Update()
        {
            // 兜底查找玩家：仅当未注入且计时器到达间隔时才尝试，避免每帧全场景遍历
            if (_player == null)
            {
                _findRetryTimer += Time.unscaledDeltaTime;
                if (_findRetryTimer >= FindRetryInterval)
                {
                    _findRetryTimer = 0f;
                    _player = FindObjectOfType<PlayerController>();
                    _interaction = FindObjectOfType<PlayerInteraction>();
                }
            }

            UpdateFps();
            UpdateInfo();
        }

        // ==================== 外部注入 ====================

        /// <summary>主动注入玩家控制器，避免每帧 FindObjectOfType 全场景遍历。</summary>
        /// <param name="player">玩家控制器引用。</param>
        public void SetPlayer(PlayerController player)
        {
            _player = player;
            _findRetryTimer = 0f;
        }

        /// <summary>主动注入玩家交互组件，避免每帧 FindObjectOfType 全场景遍历。</summary>
        /// <param name="interaction">玩家交互组件引用。</param>
        public void SetInteraction(PlayerInteraction interaction)
        {
            _interaction = interaction;
        }

        // ==================== UI 构建 ====================

        /// <summary>创建左上角信息区（圆角渐变背景 + 阴影 + 文本）。</summary>
        private void CreateInfoArea()
        {
            // 外层边框（深色描边），锚定左上角
            var borderRect = UIFactory.CreateImage(transform, "InfoBorder",
                new Color(0.08f, 0.10f, 0.14f, 0.88f), Vector2.zero, InfoSize).rectTransform;
            borderRect.anchorMin = new Vector2(0f, 1f);
            borderRect.anchorMax = new Vector2(0f, 1f);
            borderRect.pivot = new Vector2(0f, 1f);
            borderRect.anchoredPosition = new Vector2(InfoMargin, -InfoMargin);

            // 内层背景（略小，形成 1px 边框效果）
            var innerSize = new Vector2(InfoSize.x - 2f, InfoSize.y - 2f);
            var innerBg = UIFactory.CreateImage(borderRect, "InfoBg",
                new Color(0.12f, 0.16f, 0.22f, 0.92f), Vector2.zero, innerSize);
            innerBg.raycastTarget = false;
            var innerRect = innerBg.rectTransform;
            Stretch(innerRect);
            innerRect.offsetMin = new Vector2(1f, 1f);
            innerRect.offsetMax = new Vector2(-1f, -1f);

            // 标题条（顶部翡翠色细条，增加层次感）
            var titleBar = UIFactory.CreateImage(innerRect, "TitleBar",
                new Color(0.20f, 0.55f, 0.35f, 0.9f), Vector2.zero, new Vector2(innerSize.x, 3f));
            titleBar.raycastTarget = false;
            var titleRect = titleBar.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            // 信息文本（铺满内层背景，左上对齐，留内边距）
            _infoText = UIFactory.CreateText(innerRect, "InfoText", string.Empty, 14,
                new Color(0.92f, 0.95f, 1f), Vector2.zero, innerSize);
            _infoText.alignment = TextAnchor.UpperLeft;
            _infoText.supportRichText = false;
            _infoText.raycastTarget = false;

            var textRect = _infoText.rectTransform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(8f, 6f);   // 左下内边距
            textRect.offsetMax = new Vector2(-8f, -10f); // 右上内边距（顶部留出标题条空间）
        }

        /// <summary>创建屏幕中心十字准星（白色半透明十字 + 中心点）。</summary>
        private void CreateCrosshair()
        {
            // 容器（透明），锚定屏幕中心
            var container = new GameObject("Crosshair", typeof(RectTransform));
            container.transform.SetParent(transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = Vector2.zero;
            containerRect.sizeDelta = new Vector2(CrosshairSize, CrosshairSize);

            // 十字颜色：白色带轻微发光感
            Color crossColor = new Color(1f, 1f, 1f, 0.75f);
            // 描边颜色：深色，增加对比度
            Color outlineColor = new Color(0f, 0f, 0f, 0.5f);

            // 描边横条（略大，在白色横条下方）
            var hOutline = UIFactory.CreateImage(containerRect, "CrossHOutline",
                outlineColor, Vector2.zero, new Vector2(CrosshairSize + 2f, CrosshairThickness + 2f));
            hOutline.raycastTarget = false;

            // 描边竖条
            var vOutline = UIFactory.CreateImage(containerRect, "CrossVOutline",
                outlineColor, Vector2.zero, new Vector2(CrosshairThickness + 2f, CrosshairSize + 2f));
            vOutline.raycastTarget = false;

            // 水平横条（铺满容器宽度）
            var hBar = UIFactory.CreateImage(containerRect, "CrossH",
                crossColor, Vector2.zero, new Vector2(CrosshairSize, CrosshairThickness));
            hBar.raycastTarget = false;

            // 垂直竖条（铺满容器高度）
            var vBar = UIFactory.CreateImage(containerRect, "CrossV",
                crossColor, Vector2.zero, new Vector2(CrosshairThickness, CrosshairSize));
            vBar.raycastTarget = false;

            // 中心点（小方块，增加瞄准精度感）
            var dot = UIFactory.CreateImage(containerRect, "CrossDot",
                new Color(1f, 0.85f, 0.3f, 0.9f), Vector2.zero, new Vector2(2f, 2f));
            dot.raycastTarget = false;
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
