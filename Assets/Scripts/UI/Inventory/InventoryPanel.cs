using UnityEngine;
using UnityEngine.UI;
using Minecraft.Game.World;

namespace Minecraft.UI
{
    /// <summary>
    /// 背包面板（按 E 键打开/关闭）。居中显示，大小 400×400。
    /// 包含 27 格背包空间（3 行 9 列，每格 40×40），显示方块图标与数量；
    /// 顶部标题"背包"，右上角关闭按钮。
    /// <para>
    /// 采用 <see cref="CanvasGroup"/> 控制显隐（而非 SetActive），
    /// 使 GameObject 始终保持激活，从而可在隐藏状态下监听 E 键重新打开。
    /// </para>
    /// </summary>
    public class InventoryPanel : UIBase
    {
        // ==================== 配置常量 ====================

        /// <summary>背包总格数（3 行 × 9 列）。</summary>
        public const int SlotCount = 27;

        /// <summary>列数。</summary>
        private const int ColumnCount = 9;

        /// <summary>行数。</summary>
        private const int RowCount = 3;

        /// <summary>面板尺寸。</summary>
        private static readonly Vector2 PanelSize = new Vector2(400f, 400f);

        /// <summary>单格尺寸（40×40）。</summary>
        private const float SlotSize = 40f;

        /// <summary>格间距。</summary>
        private const float SlotGap = 2f;

        /// <summary>方块图标内缩尺寸。</summary>
        private const float IconSize = 34f;

        // ==================== 运行时状态 ====================

        /// <summary>CanvasGroup，用于控制显隐。</summary>
        private CanvasGroup _canvasGroup;

        /// <summary>面板背景 Transform（供标题、按钮、网格等子元素挂载）。</summary>
        private RectTransform _panelTransform;

        /// <summary>每格的方块图标 Image。</summary>
        private readonly Image[] _slotIcons = new Image[SlotCount];

        /// <summary>每格的数量文本。</summary>
        private readonly Text[] _slotCounts = new Text[SlotCount];

        // ==================== 初始化 ====================

        /// <summary>初始化背包：配置 CanvasGroup，构建面板、标题、网格、关闭按钮，并初始隐藏。</summary>
        public override void Initialize()
        {
            base.Initialize();

            // 自身铺满屏幕（作为点击屏蔽层）
            var selfRect = GetComponent<RectTransform>();
            if (selfRect != null)
                Stretch(selfRect);

            // 配置 CanvasGroup（保持 GameObject 激活，仅控制可见性）
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            CreatePanel();
            CreateTitle();
            CreateCloseButton();
            CreateGrid();

            // 初始隐藏（保持 GameObject 激活以便监听 E 键）
            Hide();
        }

        // ==================== 显隐重写 ====================

        /// <summary>
        /// 显示背包（CanvasGroup 恢复可见且可交互）。
        /// 重写基类方法以保持 GameObject 激活，确保 <see cref="Update"/> 持续运行。
        /// </summary>
        public override void Show()
        {
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            IsVisible = true;
            OnShow();
        }

        /// <summary>
        /// 隐藏背包（CanvasGroup 透明且不可交互，但 GameObject 仍激活）。
        /// 重写基类方法以保持 GameObject 激活，确保 <see cref="Update"/> 持续运行。
        /// </summary>
        public override void Hide()
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            IsVisible = false;
            OnHide();
        }

        // ==================== Unity 生命周期 ====================

        /// <summary>每帧检测 E 键，切换背包显隐。</summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
                Toggle();
        }

        // ==================== 公开方法 ====================

        /// <summary>
        /// 设置某格的方块与数量。
        /// </summary>
        /// <param name="slot">槽位索引（0~26）。</param>
        /// <param name="type">方块类型。</param>
        /// <param name="count">数量。</param>
        public void SetItem(int slot, BlockType type, int count)
        {
            if (slot < 0 || slot >= SlotCount)
                return;

            Image icon = _slotIcons[slot];
            if (icon != null)
            {
                if (type == BlockType.Air || count <= 0)
                {
                    icon.color = Color.clear;
                }
                else
                {
                    icon.color = BlockDefinition.Get(type).SideColor;
                }
            }

            Text countText = _slotCounts[slot];
            if (countText != null)
            {
                countText.text = (type != BlockType.Air && count > 0) ? count.ToString() : string.Empty;
            }
        }

        /// <summary>清空所有格子。</summary>
        public void Clear()
        {
            for (int i = 0; i < SlotCount; i++)
                SetItem(i, BlockType.Air, 0);
        }

        // ==================== UI 构建 ====================

        /// <summary>创建居中的背包面板背景。</summary>
        private void CreatePanel()
        {
            var panel = UIFactory.CreateImage(transform, "InventoryBg",
                new Color(0.12f, 0.12f, 0.12f, 0.95f), Vector2.zero, PanelSize);

            var rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = PanelSize;

            _panelTransform = rect;
        }

        /// <summary>创建顶部标题"背包"。</summary>
        private void CreateTitle()
        {
            var title = UIFactory.CreateText(_panelTransform, "Title", "背包",
                24, Color.white, new Vector2(0f, 170f), new Vector2(400f, 40f));
            title.alignment = TextAnchor.MiddleCenter;
        }

        /// <summary>创建右上角关闭按钮。</summary>
        private void CreateCloseButton()
        {
            var btn = UIFactory.CreateButton(_panelTransform, "CloseBtn", "关闭",
                new Vector2(150f, 170f), new Vector2(80f, 30f));
            btn.onClick.AddListener(Hide);
        }

        /// <summary>创建 3×9 的格子网格。</summary>
        private void CreateGrid()
        {
            // 网格总尺寸：9 * 40 + 8 * 2 = 376；3 * 40 + 2 * 2 = 124
            float gridWidth = ColumnCount * SlotSize + (ColumnCount - 1) * SlotGap;
            float gridHeight = RowCount * SlotSize + (RowCount - 1) * SlotGap;

            var grid = new GameObject("Grid", typeof(RectTransform));
            grid.transform.SetParent(_panelTransform, false);

            var gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0.5f, 0.5f);
            gridRect.anchorMax = new Vector2(0.5f, 0.5f);
            gridRect.pivot = new Vector2(0.5f, 0.5f);
            gridRect.anchoredPosition = new Vector2(0f, -30f);
            gridRect.sizeDelta = new Vector2(gridWidth, gridHeight);

            float startX = -gridWidth * 0.5f + SlotSize * 0.5f;
            float startY = gridHeight * 0.5f - SlotSize * 0.5f;
            float stepX = SlotSize + SlotGap;
            float stepY = SlotSize + SlotGap;

            for (int row = 0; row < RowCount; row++)
            {
                for (int col = 0; col < ColumnCount; col++)
                {
                    int index = row * ColumnCount + col;
                    float x = startX + col * stepX;
                    float y = startY - row * stepY;
                    CreateSlot(gridRect, index, x, y);
                }
            }
        }

        /// <summary>创建单个背包格子（背景、图标、数量）。</summary>
        private void CreateSlot(Transform parent, int index, float x, float y)
        {
            var slot = new GameObject($"InvSlot_{index}", typeof(RectTransform));
            slot.transform.SetParent(parent, false);

            var slotRect = slot.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0.5f, 0.5f);
            slotRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotRect.pivot = new Vector2(0.5f, 0.5f);
            slotRect.anchoredPosition = new Vector2(x, y);
            slotRect.sizeDelta = new Vector2(SlotSize, SlotSize);

            // 格子背景（深灰色）
            var bg = UIFactory.CreateImage(slotRect, "Bg",
                new Color(0.25f, 0.25f, 0.25f, 1f), Vector2.zero, new Vector2(SlotSize, SlotSize));
            bg.raycastTarget = false;

            // 方块图标（内缩，颜色由 SetItem 设置）
            var icon = UIFactory.CreateImage(slotRect, "Icon",
                Color.clear, Vector2.zero, new Vector2(IconSize, IconSize));
            icon.raycastTarget = false;
            _slotIcons[index] = icon;

            // 右下角数量文本
            var count = UIFactory.CreateText(slotRect, "Count", string.Empty,
                12, Color.white, Vector2.zero, new Vector2(SlotSize, SlotSize));
            count.alignment = TextAnchor.LowerRight;

            var countRect = count.rectTransform;
            countRect.offsetMin = new Vector2(0f, 2f);
            countRect.offsetMax = new Vector2(-4f, 0f);

            _slotCounts[index] = count;
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
