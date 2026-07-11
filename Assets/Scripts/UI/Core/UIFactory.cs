using UnityEngine;
using UnityEngine.UI;

namespace Minecraft.UI
{
    /// <summary>
    /// UI 工厂（静态）。通过代码动态创建基础 UI 元素，无需预制体。
    /// 统一 Minecraft 风格配色与字体。
    /// </summary>
    public static class UIFactory
    {
        // ==================== 共享资源 ====================

        private static Font _defaultFont;
        private static Sprite _whiteSprite;

        /// <summary>默认字体（LegacyRuntime.ttf）。</summary>
        public static Font DefaultFont
        {
            get
            {
                if (_defaultFont == null)
                    _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _defaultFont;
            }
        }

        /// <summary>共享白色精灵。</summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    Texture2D tex = Texture2D.whiteTexture;
                    _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f));
                }
                return _whiteSprite;
            }
        }

        // ==================== Minecraft 配色 ====================

        /// <summary>Minecraft 风格配色常量。</summary>
        public static class Colors
        {
            // 背景（渐变：深蓝灰 → 近黑）
            public static readonly Color BgTop = new(0.10f, 0.13f, 0.18f, 1f);
            public static readonly Color BgBottom = new(0.04f, 0.05f, 0.08f, 1f);
            public static readonly Color BgDark = new(0.06f, 0.08f, 0.11f, 0.97f);

            // 面板（石板质感）
            public static readonly Color PanelDark = new(0.14f, 0.16f, 0.20f, 1f);
            public static readonly Color PanelMid = new(0.18f, 0.20f, 0.25f, 1f);
            public static readonly Color PanelLighter = new(0.22f, 0.24f, 0.30f, 1f);
            public static readonly Color InputBg = new(0.06f, 0.07f, 0.10f, 1f);
            public static readonly Color InputBgFocus = new(0.08f, 0.10f, 0.14f, 1f);

            // 边框
            public static readonly Color Border = new(0.32f, 0.35f, 0.40f, 1f);
            public static readonly Color BorderBright = new(0.45f, 0.48f, 0.54f, 1f);
            public static readonly Color Divider = new(0.22f, 0.24f, 0.28f, 0.9f);

            // 按钮（翠绿主色 / 石板次要色）
            public static readonly Color BtnPrimary = new(0.20f, 0.52f, 0.22f, 1f);
            public static readonly Color BtnPrimaryHover = new(0.28f, 0.64f, 0.30f, 1f);
            public static readonly Color BtnPrimaryPressed = new(0.16f, 0.42f, 0.18f, 1f);
            public static readonly Color BtnSecondary = new(0.24f, 0.27f, 0.33f, 1f);
            public static readonly Color BtnSecondaryHover = new(0.32f, 0.36f, 0.42f, 1f);
            public static readonly Color BtnDisabled = new(0.18f, 0.20f, 0.24f, 0.6f);

            // 文字
            public static readonly Color TextMain = new(0.95f, 0.96f, 0.98f, 1f);
            public static readonly Color TextDim = new(0.58f, 0.62f, 0.68f, 1f);
            public static readonly Color TextTitle = new(0.98f, 0.84f, 0.38f, 1f);
            public static readonly Color TextTitleShadow = new(0.20f, 0.14f, 0.04f, 0.8f);
            public static readonly Color TextError = new(0.95f, 0.42f, 0.38f, 1f);
            public static readonly Color TextSuccess = new(0.58f, 0.85f, 0.48f, 1f);
            public static readonly Color TextAccent = new(0.45f, 0.78f, 0.95f, 1f);

            // 选中高亮
            public static readonly Color SelectedBg = new(0.22f, 0.40f, 0.20f, 1f);
            public static readonly Color SelectedBorder = new(0.45f, 0.70f, 0.35f, 1f);
        }

        // ==================== 创建方法 ====================

        /// <summary>创建画布（Canvas + CanvasScaler + GraphicRaycaster）。</summary>
        public static GameObject CreateCanvas()
        {
            var go = new GameObject("UI_Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        /// <summary>创建全屏面板（铺满父物体），带背景色。</summary>
        public static GameObject CreatePanel(Transform parent, string name, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            SetStretch(rect);

            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = bg;

            return go;
        }

        /// <summary>创建全屏渐变背景（上→下：topColor → bottomColor）。通过两层半透明Image叠加近似实现。</summary>
        public static GameObject CreateGradientBg(Transform parent, string name,
            Color topColor, Color bottomColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            SetStretch(rect);
            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = bottomColor;

            // 上层渐变叠色（上半部分用topColor）
            var topGo = new GameObject(name + "_Top", typeof(RectTransform), typeof(Image));
            topGo.transform.SetParent(go.transform, false);
            var topRect = topGo.GetComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0f, 0.5f);
            topRect.anchorMax = new Vector2(1f, 1f);
            topRect.offsetMin = Vector2.zero;
            topRect.offsetMax = Vector2.zero;
            topRect.pivot = new Vector2(0.5f, 0.5f);
            var topImg = topGo.GetComponent<Image>();
            topImg.sprite = WhiteSprite;
            topImg.color = topColor;

            return go;
        }

        /// <summary>创建带边框的面板（外层边框 + 内层背景 + 顶部高光条）。</summary>
        public static RectTransform CreateBorderedPanel(Transform parent, string name,
            Color borderColor, Color bgColor, Vector2 pos, Vector2 size)
        {
            // 外层：边框色
            var outer = CreateImage(parent, name, borderColor, pos, size);

            // 内层：背景色（略小于外层，形成边框效果）
            const float border = 3f;
            var inner = CreateImage(outer.rectTransform, name + "_Inner", bgColor,
                Vector2.zero, new Vector2(size.x - border * 2f, size.y - border * 2f));

            // 顶部高光条（1px亮线，模拟石板顶部反光）
            var highlight = CreateImage(outer.rectTransform, name + "_Highlight",
                Colors.BorderBright, new Vector2(0f, size.y * 0.5f - border - 0.5f),
                new Vector2(size.x - border * 2f, 1f));

            return outer.rectTransform;
        }

        /// <summary>创建带阴影效果的标题文字（偏移1格的暗色阴影 + 主色文字）。</summary>
        public static Text CreateShadowTitle(Transform parent, string name, string text,
            int fontSize, Color mainColor, Color shadowColor, Vector2 pos, Vector2 size)
        {
            // 阴影层（向右下偏移2px）
            var shadow = CreateText(parent, name + "_Shadow", text, fontSize, shadowColor,
                pos + new Vector2(2f, -2f), size);
            shadow.fontStyle = FontStyle.Bold;

            // 主文字层
            var main = CreateText(parent, name, text, fontSize, mainColor, pos, size);
            main.fontStyle = FontStyle.Bold;

            return main;
        }

        /// <summary>创建标准按钮（Minecraft 风格灰色）。</summary>
        public static Button CreateButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size)
        {
            return CreateStyledButton(parent, name, label, pos, size,
                Colors.BtnSecondary, Colors.BtnSecondaryHover, 18);
        }

        /// <summary>创建带自定义颜色的按钮（含内层背景 + 顶部高光 + 按下暗色）。</summary>
        public static Button CreateStyledButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, Color normalColor, Color hoverColor, int fontSize)
        {
            // 按钮外层（边框色，比normalColor暗30%）
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            SetCenter(rect, pos, size);

            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = new Color(normalColor.r * 0.6f, normalColor.g * 0.6f, normalColor.b * 0.6f, 1f);

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = hoverColor;
            // 按下色：normalColor的60%亮度
            colors.pressedColor = new Color(normalColor.r * 0.55f, normalColor.g * 0.55f, normalColor.b * 0.55f, 1f);
            colors.disabledColor = Colors.BtnDisabled;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            // 内层背景（略小，形成边框效果）
            const float btnBorder = 2f;
            var innerGo = new GameObject(name + "_Inner", typeof(RectTransform), typeof(Image));
            innerGo.transform.SetParent(go.transform, false);
            var innerRect = innerGo.GetComponent<RectTransform>();
            SetCenter(innerRect, Vector2.zero, new Vector2(size.x - btnBorder * 2f, size.y - btnBorder * 2f));
            var innerImg = innerGo.GetComponent<Image>();
            innerImg.sprite = WhiteSprite;
            innerImg.color = normalColor;
            innerImg.raycastTarget = false;
            // 内层跟随按钮状态变化（通过Button的targetGraphic=外层Image，内层仅装饰）

            // 按钮文字（带轻微阴影提升可读性）
            var text = CreateText(go.transform, name + "_Label", label, fontSize,
                Colors.TextMain, Vector2.zero, size);
            text.fontStyle = FontStyle.Bold;

            return btn;
        }

        /// <summary>创建文本。</summary>
        public static Text CreateText(Transform parent, string name, string text,
            int fontSize, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            SetCenter(rect, pos, size);

            var txt = go.GetComponent<Text>();
            txt.font = DefaultFont;
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;

            return txt;
        }

        /// <summary>创建图片（纯色块）。</summary>
        public static Image CreateImage(Transform parent, string name, Color color,
            Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            SetCenter(rect, pos, size);

            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = color;

            return img;
        }

        /// <summary>创建带边框的输入框。</summary>
        public static InputField CreateInputField(Transform parent, string name,
            string defaultValue, string placeholder, Vector2 pos, Vector2 size)
        {
            // 外层边框
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            SetCenter(rect, pos, size);

            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = Colors.Border; // 边框色

            var input = go.GetComponent<InputField>();
            input.targetGraphic = img; // 显式设置点击响应区域

            // 内层背景（略小于外层，形成边框）
            const float border = 2f;
            var innerGo = new GameObject(name + "_Bg", typeof(RectTransform), typeof(Image));
            innerGo.transform.SetParent(go.transform, false);
            var innerRect = innerGo.GetComponent<RectTransform>();
            SetCenter(innerRect, Vector2.zero, new Vector2(size.x - border * 2f, size.y - border * 2f));
            var innerImg = innerGo.GetComponent<Image>();
            innerImg.sprite = WhiteSprite;
            innerImg.color = Colors.InputBg;
            innerImg.raycastTarget = false; // 不拦截点击，让点击穿透到外层InputField

            // 占位提示文本（必须使用 Stretch 锚点铺满 InputField，否则 sizeDelta 为负导致不渲染）
            var placeholderText = CreateText(go.transform, name + "_Placeholder",
                placeholder, 16, new Color(0.5f, 0.52f, 0.55f, 0.6f), Vector2.zero, size);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.supportRichText = false; // InputField 要求
            placeholderText.raycastTarget = false;
            var phRect = placeholderText.GetComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;  // Stretch 锚点
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(10f, 0f);   // 左边距 10
            phRect.offsetMax = new Vector2(-10f, 0f);   // 右边距 10
            phRect.sizeDelta = Vector2.zero;
            input.placeholder = placeholderText;

            // 内容文本（同上，必须 Stretch 锚点）
            var contentText = CreateText(go.transform, name + "_Text", defaultValue,
                16, Colors.TextMain, Vector2.zero, size);
            contentText.alignment = TextAnchor.MiddleLeft;
            contentText.supportRichText = false; // InputField 要求
            contentText.raycastTarget = false;
            var ctRect = contentText.GetComponent<RectTransform>();
            ctRect.anchorMin = Vector2.zero;  // Stretch 锚点
            ctRect.anchorMax = Vector2.one;
            ctRect.offsetMin = new Vector2(10f, 0f);
            ctRect.offsetMax = new Vector2(-10f, 0f);
            ctRect.sizeDelta = Vector2.zero;
            input.textComponent = contentText;

            input.text = defaultValue ?? string.Empty;

            return input;
        }

        // ==================== 内部辅助 ====================

        /// <summary>设置 RectTransform 为铺满父物体。</summary>
        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>设置 RectTransform 为居中锚点。</summary>
        private static void SetCenter(RectTransform rect, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        /// <summary>设置 RectTransform 为铺满父物体（公开方法，供外部调用）。</summary>
        public static void Stretch(RectTransform rect) => SetStretch(rect);
    }
}
