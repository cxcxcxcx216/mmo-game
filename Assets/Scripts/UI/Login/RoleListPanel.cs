using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Minecraft.MMO;
using MMO.Protocol;

namespace Minecraft.UI
{
    /// <summary>
    /// 角色列表面板。Minecraft 风格深色渐变主题，支持选角/创角/进入游戏。
    /// <para>
    /// 布局：渐变背景 + 带边框卡片（阴影大标题 + 副标题 + 分隔线 + 角色列表 + 创建角色区 + 进入游戏/返回按钮 + 状态提示）
    /// </para>
    /// </summary>
    public class RoleListPanel : UIBase
    {
        // ==================== 布局常量 ====================

        private static readonly Vector2 CardSize = new(620f, 720f);
        private static readonly Vector2 RoleEntrySize = new(520f, 64f);
        private static readonly Vector2 BtnSize = new(240f, 50f);
        private static readonly Vector2 InputSize = new(340f, 44f);
        private const float RoleListHeight = 280f;

        // ==================== 控件引用 ====================

        private RectTransform _roleListContainer;
        private InputField _nameInput;
        private Button _enterButton;
        private Button _createButton;
        private Text _statusText;

        // ==================== 数据 ====================

        private readonly List<RoleInfo> _roles = new();
        private int _selectedIndex = -1;

        // ==================== 初始化 ====================

        public override void Initialize()
        {
            base.Initialize();

            UIFactory.CreateGradientBg(transform, "Bg", UIFactory.Colors.BgTop, UIFactory.Colors.BgBottom);
            CreateRoleCard();
            BindNetworkEvents();
        }

        public override void OnShow()
        {
            if (_roles.Count == 0)
                SetStatus("正在加载角色列表...", false);
        }

        // ==================== UI 构建 ====================

        private void CreateRoleCard()
        {
            var cardRT = UIFactory.CreateBorderedPanel(transform, "Card",
                UIFactory.Colors.Border, UIFactory.Colors.PanelDark,
                Vector2.zero, CardSize);

            float cursorY = CardSize.y * 0.5f - 52f;

            // 大标题（带阴影）
            UIFactory.CreateShadowTitle(cardRT, "Title", "选 择 角 色",
                34, UIFactory.Colors.TextTitle, UIFactory.Colors.TextTitleShadow,
                new Vector2(0f, cursorY), new Vector2(CardSize.x - 40f, 44f));

            cursorY -= 60f;

            // 副标题
            UIFactory.CreateText(cardRT, "Subtitle", "— 点击角色进入游戏，或在下方创建新角色 —",
                15, UIFactory.Colors.TextAccent, new Vector2(0f, cursorY),
                new Vector2(CardSize.x - 40f, 22f));

            cursorY -= 36f;

            // 分隔线
            UIFactory.CreateImage(cardRT, "Divider1", UIFactory.Colors.Divider,
                new Vector2(0f, cursorY), new Vector2(RoleEntrySize.x, 2f));

            cursorY -= 24f;

            // 角色列表容器（带边框背景）
            var listBg = UIFactory.CreateBorderedPanel(cardRT, "RoleListBg",
                UIFactory.Colors.Border, new Color(0.08f, 0.10f, 0.13f, 1f),
                new Vector2(0f, cursorY - RoleListHeight * 0.5f),
                new Vector2(RoleEntrySize.x, RoleListHeight));

            // 容器指向内层背景（角色条目的实际父物体），VerticalLayoutGroup 也加在内层
            _roleListContainer = listBg.Find("RoleListBg_Inner") as RectTransform;
            if (_roleListContainer == null) _roleListContainer = listBg;
            SetupRoleListLayout(_roleListContainer);

            cursorY -= RoleListHeight + 24f;

            // 分隔线
            UIFactory.CreateImage(cardRT, "Divider2", UIFactory.Colors.Divider,
                new Vector2(0f, cursorY), new Vector2(RoleEntrySize.x, 2f));

            cursorY -= 24f;

            // 创建角色区标题
            UIFactory.CreateText(cardRT, "CreateLabel", "创建新角色",
                16, UIFactory.Colors.TextDim, new Vector2(0f, cursorY),
                new Vector2(RoleEntrySize.x, 22f)).alignment = TextAnchor.MiddleLeft;

            cursorY -= 30f;

            // 创建角色输入框 + 按钮（同一行）
            _nameInput = UIFactory.CreateInputField(cardRT, "NameInput",
                "", "输入角色名", new Vector2(-100f, cursorY), InputSize);

            _createButton = UIFactory.CreateStyledButton(cardRT, "CreateBtn",
                "创 建", new Vector2(220f, cursorY), new Vector2(140f, 44f),
                UIFactory.Colors.BtnSecondary, UIFactory.Colors.BtnSecondaryHover, 18);
            _createButton.onClick.AddListener(OnCreateRoleClicked);

            cursorY -= 56f;

            // 进入游戏 + 返回按钮
            _enterButton = UIFactory.CreateStyledButton(cardRT, "EnterBtn",
                "进 入 游 戏", new Vector2(-130f, cursorY), BtnSize,
                UIFactory.Colors.BtnPrimary, UIFactory.Colors.BtnPrimaryHover, 20);
            _enterButton.onClick.AddListener(OnEnterGameClicked);
            _enterButton.interactable = false;

            var backButton = UIFactory.CreateStyledButton(cardRT, "BackBtn",
                "返 回 登 录", new Vector2(130f, cursorY), BtnSize,
                UIFactory.Colors.BtnSecondary, UIFactory.Colors.BtnSecondaryHover, 16);
            backButton.onClick.AddListener(OnBackClicked);

            cursorY -= BtnSize.y + 16f;

            // 状态提示
            _statusText = UIFactory.CreateText(cardRT, "Status",
                "请选择角色或创建新角色", 14, UIFactory.Colors.TextDim,
                new Vector2(0f, cursorY), new Vector2(CardSize.x - 40f, 22f));
        }

        private static void SetupRoleListLayout(RectTransform container)
        {
            var vlg = container.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        // ==================== 事件绑定 ====================

        private void BindNetworkEvents()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnRoleListAck += HandleRoleListAck;
            net.OnCreateRoleAck += HandleCreateRoleAck;
            net.OnEnterGameAck += HandleEnterGameAck;
        }

        // ==================== 按钮回调 ====================

        private void OnCreateRoleClicked()
        {
            string name = _nameInput.text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                SetStatus("角色名不能为空", true);
                return;
            }

            // 客户端预校验：2~12 字符，仅中文/字母/数字（与服务端 InputValidator 一致）
            if (name.Length < 2 || name.Length > 12)
            {
                SetStatus("角色名长度需 2~12 字符", true);
                return;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                             (c >= '0' && c <= '9') || (c >= '\u4e00' && c <= '\u9fa5');
                if (!valid)
                {
                    SetStatus("角色名仅支持中文、字母、数字", true);
                    return;
                }
            }

            var net = NetworkManager.Instance;
            if (net == null || !net.IsConnected)
            {
                SetStatus("未连接服务器，请返回登录", true);
                return;
            }

            SetStatus("正在创建角色...", false);
            _createButton.interactable = false;
            Debug.Log($"[RoleList] 创建角色: name={name}, serverId={net.ServerId}");
            net.CreateRole(net.ServerId, name, profession: 1);
        }

        private void OnEnterGameClicked()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _roles.Count)
            {
                SetStatus("请先选择一个角色", true);
                return;
            }

            var gameState = GameStateManager.Instance;
            if (gameState == null)
            {
                SetStatus("系统未初始化", true);
                return;
            }

            long roleId = _roles[_selectedIndex].roleId;
            string roleName = _roles[_selectedIndex].name;
            SetStatus("正在进入游戏...", false);
            _enterButton.interactable = false;
            Debug.Log($"[RoleList] 进入游戏: roleId={roleId}, name={roleName}");

            gameState.EnterGame(roleId);
        }

        private void OnBackClicked()
        {
            var gameState = GameStateManager.Instance;
            if (gameState != null)
                gameState.GoOffline();
        }

        // ==================== 网络事件处理 ====================

        private void HandleRoleListAck(RoleListAck msg)
        {
            if (msg.code != ErrorCode.SUCCESS)
            {
                Debug.LogWarning($"[RoleList] 角色列表获取失败: code={msg.code}, desc={ErrorCode.Describe(msg.code)}");
                SetStatus($"加载失败: {ErrorCode.Describe(msg.code)}", true);
                return;
            }

            _roles.Clear();
            _roles.AddRange(msg.roles);
            _selectedIndex = -1;

            RefreshRoleList();
            Debug.Log($"[RoleList] 角色列表加载成功: count={_roles.Count}");
            SetStatus(_roles.Count > 0 ? $"共 {_roles.Count} 个角色，请选择" : "暂无角色，请在下方创建", false);
        }

        private void HandleCreateRoleAck(CreateRoleAck msg)
        {
            _createButton.interactable = true;

            if (msg.code == ErrorCode.SUCCESS)
            {
                Debug.Log($"[RoleList] 角色创建成功: roleId={msg.role?.roleId}, name={msg.role?.name}");
                SetStatus("角色创建成功", false);
                _nameInput.text = "";
                // 重新请求角色列表
                var net = NetworkManager.Instance;
                if (net != null && net.IsConnected)
                    net.RequestRoleList(net.ServerId);
            }
            else
            {
                Debug.LogWarning($"[RoleList] 角色创建失败: code={msg.code}, desc={ErrorCode.Describe(msg.code)}");
                SetStatus($"创建失败: {ErrorCode.Describe(msg.code)}", true);
            }
        }

        private void HandleEnterGameAck(EnterGameAck msg)
        {
            if (msg.code != ErrorCode.SUCCESS)
            {
                Debug.LogWarning($"[RoleList] 进入游戏失败: code={msg.code}, desc={ErrorCode.Describe(msg.code)}");
                SetStatus($"进入游戏失败: {ErrorCode.Describe(msg.code)}", true);
                _enterButton.interactable = true;
            }
            else
            {
                Debug.Log($"[RoleList] 进入游戏成功: roleId={msg.player?.roleId}");
            }
        }

        // ==================== 角色列表 UI ====================

        private void RefreshRoleList()
        {
            // 清除所有旧条目（_roleListContainer 已指向内层，其子物体全是角色条目或提示）
            for (int i = _roleListContainer.childCount - 1; i >= 0; i--)
                Destroy(_roleListContainer.GetChild(i).gameObject);

            for (int i = 0; i < _roles.Count; i++)
                CreateRoleEntry(_roleListContainer, i, _roles[i]);

            if (_roles.Count == 0)
            {
                var hintGo = new GameObject("EmptyHint", typeof(RectTransform));
                hintGo.transform.SetParent(_roleListContainer, false);
                var hintRect = hintGo.GetComponent<RectTransform>();
                hintRect.anchorMin = Vector2.zero;
                hintRect.anchorMax = Vector2.one;
                hintRect.offsetMin = Vector2.zero;
                hintRect.offsetMax = Vector2.zero;
                var le = hintGo.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;
                var hintText = hintGo.AddComponent<Text>();
                hintText.font = UIFactory.DefaultFont;
                hintText.text = "暂无角色，请在下方创建";
                hintText.fontSize = 15;
                hintText.color = UIFactory.Colors.TextDim;
                hintText.alignment = TextAnchor.MiddleCenter;
                hintText.supportRichText = false;
                hintText.raycastTarget = false;
            }
        }

        private void CreateRoleEntry(Transform parent, int index, RoleInfo role)
        {
            var entryGo = new GameObject($"Role_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
            entryGo.transform.SetParent(parent, false);

            var entryRect = entryGo.GetComponent<RectTransform>();
            entryRect.anchorMin = Vector2.zero;
            entryRect.anchorMax = Vector2.one;
            entryRect.offsetMin = Vector2.zero;
            entryRect.offsetMax = Vector2.zero;

            var entryLe = entryGo.AddComponent<LayoutElement>();
            entryLe.preferredHeight = RoleEntrySize.y;

            var entryImg = entryGo.GetComponent<Image>();
            entryImg.sprite = UIFactory.WhiteSprite;
            entryImg.color = UIFactory.Colors.PanelMid;

            var entryBtn = entryGo.GetComponent<Button>();
            var colors = entryBtn.colors;
            colors.normalColor = UIFactory.Colors.PanelMid;
            colors.highlightedColor = UIFactory.Colors.PanelLighter;
            colors.pressedColor = UIFactory.Colors.PanelDark;
            colors.fadeDuration = 0.08f;
            entryBtn.colors = colors;

            int capturedIndex = index;
            entryBtn.onClick.AddListener(() => SelectRole(capturedIndex));

            // 角色信息（Stretch 锚点铺满条目，避免 sizeDelta 为负）
            string professionName = GetProfessionName(role.profession);
            string infoText = $"{role.name}    Lv.{role.level}    {professionName}";
            var info = UIFactory.CreateText(entryRect, "Info", infoText,
                18, UIFactory.Colors.TextMain, Vector2.zero,
                new Vector2(RoleEntrySize.x - 20f, RoleEntrySize.y));
            info.alignment = TextAnchor.MiddleLeft;
            info.fontStyle = FontStyle.Bold;
            info.supportRichText = false;
            info.raycastTarget = false;
            var infoRect = info.GetComponent<RectTransform>();
            infoRect.anchorMin = Vector2.zero;
            infoRect.anchorMax = Vector2.one;
            infoRect.offsetMin = new Vector2(16f, 0f);
            infoRect.offsetMax = new Vector2(-16f, 0f);
            infoRect.sizeDelta = Vector2.zero;
        }

        private void SelectRole(int index)
        {
            _selectedIndex = index;

            // 更新条目高亮（_roleListContainer 已是内层容器）
            for (int i = 0; i < _roleListContainer.childCount; i++)
            {
                var child = _roleListContainer.GetChild(i);
                var img = child.GetComponent<Image>();
                if (img != null && child.name.StartsWith("Role_"))
                {
                    int roleIdx = int.Parse(child.name.Substring(5));
                    img.color = (roleIdx == index)
                        ? UIFactory.Colors.SelectedBg  // 选中：暗绿
                        : UIFactory.Colors.PanelMid;   // 未选中
                }
            }

            _enterButton.interactable = true;

            if (index >= 0 && index < _roles.Count)
                SetStatus($"已选中: {_roles[index].name} (Lv.{_roles[index].level})", false);
        }

        // ==================== 辅助 ====================

        private static string GetProfessionName(int profession)
        {
            return profession switch
            {
                1 => "矿工",
                2 => "战士",
                3 => "建筑师",
                _ => $"职业{profession}",
            };
        }

        private void SetStatus(string message, bool isError)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.color = isError
                    ? UIFactory.Colors.TextError
                    : UIFactory.Colors.TextSuccess;
            }
        }
    }
}
