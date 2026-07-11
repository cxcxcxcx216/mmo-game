using UnityEngine;
using UnityEngine.UI;
using Minecraft.Game.World;

namespace Minecraft.UI
{
    /// <summary>
    /// 快捷栏面板（Minecraft 风格底部快捷栏）。
    /// 底部居中显示 9 个格子，对应数字键 1~9。每格 50×50，间距 4 像素。
    /// 选中格有白色边框高亮；格内显示方块图标（纯色块，颜色取自 <see cref="BlockDefinition"/>.SideColor）
    /// 与左上角数字编号。
    /// <para>本面板仅负责显示；按键选择由 <see cref="Game.Player.PlayerInteraction"/> 处理。</para>
    /// </summary>
    public class HotbarPanel : UIBase
    {
        // ==================== 配置常量 ====================

        /// <summary>快捷栏槽位数。</summary>
        public const int SlotCount = 9;

        /// <summary>单格尺寸（56×56，更大更清晰）。</summary>
        private const float SlotSize = 56f;

        /// <summary>格间距。</summary>
        private const float SlotGap = 6f;

        /// <summary>容器距屏幕底部的偏移。</summary>
        private const float BottomOffset = 24f;

        /// <summary>方块图标内缩尺寸（图标比格子略小，留出边框）。</summary>
        private const float IconSize = 48f;

        /// <summary>选中边框尺寸（略大于格子，形成 3px 翡翠绿边）。</summary>
        private const float SelectionSize = 62f;

        // ==================== 运行时状态 ====================

        /// <summary>每格的方块类型。</summary>
        private readonly BlockType[] _slotTypes = new BlockType[SlotCount];

        /// <summary>每格的方块图标 Image。</summary>
        private readonly Image[] _slotIcons = new Image[SlotCount];

        /// <summary>每格的选中边框 Image。</summary>
        private readonly Image[] _slotSelections = new Image[SlotCount];

        /// <summary>当前选中的槽位索引。</summary>
        private int _selectedSlot;

        // ==================== 公开属性 ====================

        /// <summary>当前选中方块类型。</summary>
        public BlockType SelectedBlock => _slotTypes[_selectedSlot];

        // ==================== 初始化 ====================

        /// <summary>初始化快捷栏：创建底部容器与 9 个格子。</summary>
        public override void Initialize()
        {
            base.Initialize();

            // 自身铺满屏幕（不添加 Image，避免遮挡游戏点击）
            var selfRect = GetComponent<RectTransform>();
            if (selfRect != null)
                Stretch(selfRect);

            CreateHotbar();
        }

        // ==================== 公开方法 ====================

        /// <summary>
        /// 设置某格的方块类型（更新图标颜色）。
        /// </summary>
        /// <param name="index">槽位索引（0~8）。</param>
        /// <param name="type">方块类型。</param>
        public void SetSlot(int index, BlockType type)
        {
            if (index < 0 || index >= SlotCount)
                return;

            _slotTypes[index] = type;

            Image icon = _slotIcons[index];
            if (icon == null)
                return;

            if (type == BlockType.Air)
            {
                icon.color = Color.clear;
            }
            else
            {
                // 颜色取自方块的侧面色
                icon.color = BlockDefinition.Get(type).SideColor;
            }
        }

        /// <summary>
        /// 高亮选中格（显示白色边框）。
        /// </summary>
        /// <param name="index">槽位索引（0~8）。</param>
        public void SetSelected(int index)
        {
            if (index < 0 || index >= SlotCount)
                return;

            _selectedSlot = index;

            for (int i = 0; i < SlotCount; i++)
            {
                if (_slotSelections[i] != null)
                    _slotSelections[i].enabled = (i == index);
            }
        }

        // ==================== UI 构建 ====================

        /// <summary>创建底部居中的快捷栏容器与格子。</summary>
        private void CreateHotbar()
        {
            // 容器总宽度 = 9 * 50 + 8 * 4 = 482
            float totalWidth = SlotCount * SlotSize + (SlotCount - 1) * SlotGap;
            float containerHeight = SlotSize;

            var container = new GameObject("Hotbar", typeof(RectTransform));
            container.transform.SetParent(transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0f);
            containerRect.anchorMax = new Vector2(0.5f, 0f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(0f, BottomOffset + containerHeight * 0.5f);
            containerRect.sizeDelta = new Vector2(totalWidth, containerHeight);

            // 计算每格中心 x（相对容器中心）
            float startX = -totalWidth * 0.5f + SlotSize * 0.5f;
            float step = SlotSize + SlotGap;

            for (int i = 0; i < SlotCount; i++)
            {
                float x = startX + i * step;
                CreateSlot(containerRect, i, x);
            }

            // 默认选中第 0 格
            SetSelected(0);
        }

        /// <summary>创建单个快捷栏格子（边框、背景、图标、编号）。</summary>
        private void CreateSlot(Transform parent, int index, float x)
        {
            // 格子容器
            var slot = new GameObject($"Slot_{index + 1}", typeof(RectTransform));
            slot.transform.SetParent(parent, false);

            var slotRect = slot.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0.5f, 0.5f);
            slotRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotRect.pivot = new Vector2(0.5f, 0.5f);
            slotRect.anchoredPosition = new Vector2(x, 0f);
            slotRect.sizeDelta = new Vector2(SlotSize, SlotSize);

            // 外框（深色描边）
            var border = UIFactory.CreateImage(slotRect, "Border",
                new Color(0.08f, 0.10f, 0.14f, 0.9f), Vector2.zero, new Vector2(SlotSize, SlotSize));
            border.raycastTarget = false;

            // 选中边框（翡翠绿，略大于格子，居中，默认隐藏）
            var selection = UIFactory.CreateImage(slotRect, "Selection",
                new Color(0.30f, 0.75f, 0.45f, 1f), Vector2.zero, new Vector2(SelectionSize, SelectionSize));
            selection.raycastTarget = false;
            selection.enabled = false;
            _slotSelections[index] = selection;

            // 格子背景（深灰半透明，略小于外框形成 2px 边）
            const float borderInset = 2f;
            var bg = UIFactory.CreateImage(slotRect, "Bg",
                new Color(0.15f, 0.18f, 0.24f, 0.92f), Vector2.zero,
                new Vector2(SlotSize - borderInset, SlotSize - borderInset));
            bg.raycastTarget = false;

            // 方块图标（内缩，颜色由 SetSlot 设置）
            var icon = UIFactory.CreateImage(slotRect, "Icon",
                Color.clear, Vector2.zero, new Vector2(IconSize, IconSize));
            icon.raycastTarget = false;
            _slotIcons[index] = icon;

            // 左上角数字编号（1~9），带阴影效果
            var numberShadow = UIFactory.CreateText(slotRect, "NumberShadow", (index + 1).ToString(),
                12, new Color(0f, 0f, 0f, 0.8f), Vector2.zero, new Vector2(SlotSize, SlotSize));
            numberShadow.alignment = TextAnchor.UpperLeft;
            numberShadow.supportRichText = false;
            numberShadow.raycastTarget = false;
            var shadowRect = numberShadow.rectTransform;
            shadowRect.offsetMin = new Vector2(5f, -1f);
            shadowRect.offsetMax = new Vector2(2f, -4f);

            var number = UIFactory.CreateText(slotRect, "Number", (index + 1).ToString(),
                12, new Color(0.95f, 0.98f, 1f), Vector2.zero, new Vector2(SlotSize, SlotSize));
            number.alignment = TextAnchor.UpperLeft;
            number.supportRichText = false;
            number.raycastTarget = false;
            var numberRect = number.rectTransform;
            numberRect.offsetMin = new Vector2(4f, 0f);
            numberRect.offsetMax = new Vector2(0f, -3f);

            // 初始类型为空气
            _slotTypes[index] = BlockType.Air;
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
