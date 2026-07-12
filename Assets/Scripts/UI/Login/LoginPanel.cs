using UnityEngine;
using UnityEngine.UI;
using Minecraft.MMO;
using Minecraft.Config;
using MMO.Protocol;

namespace Minecraft.UI
{
    /// <summary>
    /// 登录面板。Minecraft 风格深色渐变主题。
    /// <para>
    /// 服务器地址已写死在 ServerConfig.json 中，用户只需输入账号密码。
    /// </para>
    /// <para>
    /// 在线流程：输入账号密码 → 点击「登录」→ Connect → LoginReq → LoginAck → 自动进入选角。
    /// 离线流程：点击「离线模式」→ 直接进入游戏。
    /// </para>
    /// </summary>
    public class LoginPanel : UIBase
    {
        // ==================== 布局常量 ====================

        private static readonly Vector2 CardSize = new(480f, 520f);
        private static readonly Vector2 InputSize = new(380f, 44f);
        private static readonly Vector2 PrimaryBtnSize = new(380f, 54f);
        private static readonly Vector2 SecondaryBtnSize = new(380f, 44f);

        // ==================== 控件引用 ====================

        private InputField _accountInput;
        private InputField _passwordInput;
        private Button _loginButton;
        private Button _offlineButton;
        private Text _statusText;
        private Text _serverLabel;

        /// <summary>待登录的账号密码（连接成功后用于发送 LoginReq）。</summary>
        private string _pendingAccount;
        private string _pendingPassword;

        // ==================== 初始化 ====================

        public override void Initialize()
        {
            base.Initialize();

            // 全屏渐变背景
            UIFactory.CreateGradientBg(transform, "Bg", UIFactory.Colors.BgTop, UIFactory.Colors.BgBottom);

            CreateLoginCard();
            BindNetworkEvents();
        }

        // ==================== UI 构建 ====================

        private void CreateLoginCard()
        {
            // 带边框的卡片
            var cardRT = UIFactory.CreateBorderedPanel(transform, "Card",
                UIFactory.Colors.Border, UIFactory.Colors.PanelDark,
                Vector2.zero, CardSize);

            float cursorY = CardSize.y * 0.5f - 52f;

            // 大标题（带阴影）
            UIFactory.CreateShadowTitle(cardRT, "Title", "MINECRAFT MMO",
                38, UIFactory.Colors.TextTitle, UIFactory.Colors.TextTitleShadow,
                new Vector2(0f, cursorY), new Vector2(CardSize.x - 40f, 48f));

            cursorY -= 64f;

            // 副标题
            UIFactory.CreateText(cardRT, "Subtitle", "— 开放世界沙盒 MMO · 全服互通 —",
                15, UIFactory.Colors.TextAccent, new Vector2(0f, cursorY),
                new Vector2(CardSize.x - 40f, 22f));

            cursorY -= 34f;

            // 分隔线
            UIFactory.CreateImage(cardRT, "Divider1", UIFactory.Colors.Divider,
                new Vector2(0f, cursorY), new Vector2(InputSize.x, 2f));

            cursorY -= 28f;

            // 服务器信息（从配置表读取，只读显示）
            var server = ConfigManager.GetDefaultServer();
            string serverText = server != null
                ? $"服务器: {server.Name} ({server.Host}:{server.Port})"
                : "服务器: 未配置";
            _serverLabel = UIFactory.CreateText(cardRT, "ServerLabel", serverText,
                14, UIFactory.Colors.TextDim, new Vector2(0f, cursorY),
                new Vector2(InputSize.x, 20f));
            _serverLabel.alignment = TextAnchor.MiddleCenter;

            cursorY -= 28f;

            // 账号（默认空，需用户输入）
            CreateLabeledInput(cardRT, "AccountInput", "账号", "", ref cursorY);

            // 密码（默认空，需用户输入）
            CreateLabeledInput(cardRT, "PasswordInput", "密码", "", ref cursorY);

            cursorY -= 6f;

            // 登录按钮（主按钮，翠绿色）
            _loginButton = UIFactory.CreateStyledButton(cardRT, "LoginBtn",
                "登 录 服 务 器", new Vector2(0f, cursorY), PrimaryBtnSize,
                UIFactory.Colors.BtnPrimary, UIFactory.Colors.BtnPrimaryHover, 22);
            _loginButton.onClick.AddListener(OnLoginClicked);

            cursorY -= PrimaryBtnSize.y + 10f;

            // 注册按钮 + 离线按钮（同一行）
            var registerBtn = UIFactory.CreateStyledButton(cardRT, "RegisterBtn",
                "注 册 账 号", new Vector2(-105f, cursorY), new Vector2(180f, 44f),
                UIFactory.Colors.BtnSecondary, UIFactory.Colors.BtnSecondaryHover, 16);
            registerBtn.onClick.AddListener(OnRegisterClicked);

            _offlineButton = UIFactory.CreateStyledButton(cardRT, "OfflineBtn",
                "离 线 模 式", new Vector2(105f, cursorY), new Vector2(180f, 44f),
                UIFactory.Colors.BtnSecondary, UIFactory.Colors.BtnSecondaryHover, 16);
            _offlineButton.onClick.AddListener(OnOfflineClicked);

            cursorY -= SecondaryBtnSize.y + 12f;

            // 状态提示
            _statusText = UIFactory.CreateText(cardRT, "Status",
                "输入账号密码后点击登录", 14, UIFactory.Colors.TextDim,
                new Vector2(0f, cursorY), new Vector2(CardSize.x - 40f, 22f));
        }

        /// <summary>创建带标签的输入框。</summary>
        private void CreateLabeledInput(Transform parent, string name,
            string label, string defaultValue, ref float cursorY)
        {
            UIFactory.CreateText(parent, name + "_Label", label,
                14, UIFactory.Colors.TextDim, new Vector2(0f, cursorY),
                new Vector2(InputSize.x, 20f)).alignment = TextAnchor.MiddleLeft;

            cursorY -= 26f;

            var input = UIFactory.CreateInputField(parent, name, defaultValue, "",
                new Vector2(0f, cursorY), InputSize);

            cursorY -= InputSize.y + 16f;

            // 按名称保存引用
            switch (name)
            {
                case "AccountInput": _accountInput = input; break;
                case "PasswordInput": _passwordInput = input; break;
            }
        }

        // ==================== 事件绑定 ====================

        private void BindNetworkEvents()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnConnected += HandleConnected;
            net.OnConnectFailed += HandleConnectFailed;
            net.OnDisconnected += HandleDisconnected;
            net.OnLoginAck += HandleLoginAck;
        }

        // ==================== 按钮回调 ====================

        private void OnLoginClicked()
        {
            string account = _accountInput.text.Trim();
            string password = _passwordInput.text;

            // 客户端预校验：账号 4~64 字符，字母数字下划线
            if (string.IsNullOrEmpty(account))
            {
                SetStatus("账号不能为空", true);
                return;
            }
            if (account.Length < 4 || account.Length > 64)
            {
                SetStatus("账号长度需 4~64 字符", true);
                return;
            }
            for (int i = 0; i < account.Length; i++)
            {
                char c = account[i];
                bool valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                             (c >= '0' && c <= '9') || c == '_';
                if (!valid)
                {
                    SetStatus("账号仅支持字母、数字、下划线", true);
                    return;
                }
            }

            // 客户端预校验：密码 8~72 字节
            if (string.IsNullOrEmpty(password) || password.Length < 8)
            {
                SetStatus("密码至少 8 位", true);
                return;
            }

            // 从配置表读取服务器地址
            var server = ConfigManager.GetDefaultServer();
            if (server == null)
            {
                SetStatus("未找到服务器配置", true);
                return;
            }

            _pendingAccount = account;
            _pendingPassword = password;

            var gameState = GameStateManager.Instance;
            if (gameState == null)
            {
                SetStatus("系统未初始化", true);
                return;
            }

            SetStatus($"正在连接 {server.Host}:{server.Port} ...", false);
            SetButtonsInteractable(false);

            gameState.StartOnlineMode(server.Host, server.Port);
        }

        /// <summary>点击「注册账号」：切换到注册面板。</summary>
        private void OnRegisterClicked()
        {
            var uiManager = FindObjectOfType<UIManager>();
            if (uiManager != null)
            {
                Hide();
                uiManager.Show("RegisterPanel");
            }
        }

        private void OnOfflineClicked()
        {
            var gameState = GameStateManager.Instance;
            if (gameState == null)
            {
                SetStatus("系统未初始化", true);
                return;
            }

            SetStatus("正在进入离线模式...", false);
            gameState.StartOfflineMode();
        }

        // ==================== 网络事件处理 ====================

        private void HandleConnected()
        {
            // 如果没有待登录的账号密码（例如用户切换到了注册面板），不发送登录请求
            if (string.IsNullOrEmpty(_pendingAccount))
                return;

            SetStatus("连接成功，正在登录...", false);

            var net = NetworkManager.Instance;
            if (net != null)
                net.Login(_pendingAccount, _pendingPassword);
        }

        private void HandleConnectFailed(string reason)
        {
            SetStatus($"连接失败: {reason}", true);
            SetButtonsInteractable(true);
        }

        private void HandleDisconnected(string reason)
        {
            SetStatus($"连接断开: {reason}", true);
            SetButtonsInteractable(true);
        }

        private void HandleLoginAck(LoginAck msg)
        {
            if (msg.code == ErrorCode.SUCCESS)
            {
                SetStatus("登录成功，正在加载...", false);
            }
            else
            {
                SetStatus($"登录失败: {ErrorCode.Describe(msg.code)}", true);
                SetButtonsInteractable(true);
            }
        }

        // ==================== UI 辅助 ====================

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

        private void SetButtonsInteractable(bool interactable)
        {
            if (_loginButton != null)
                _loginButton.interactable = interactable;
            // 离线按钮始终可用
            if (_offlineButton != null)
                _offlineButton.interactable = true;
        }

        // ==================== 显隐回调 ====================

        /// <summary>面板显示时自动聚焦账号输入框，用户可直接键盘输入。</summary>
        public override void OnShow()
        {
            Invoke(nameof(FocusFirstInput), 0.05f);
        }

        public override void OnHide()
        {
            CancelInvoke();
        }

        /// <summary>
        /// 面板销毁时取消所有网络事件订阅，避免 MissingReferenceException 和重复订阅。
        /// NetworkManager 是跨场景持久化单例，不随面板销毁而释放引用。
        /// </summary>
        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;
            net.OnConnected -= HandleConnected;
            net.OnConnectFailed -= HandleConnectFailed;
            net.OnDisconnected -= HandleDisconnected;
            net.OnLoginAck -= HandleLoginAck;
        }

        private void FocusFirstInput()
        {
            if (!IsVisible || _accountInput == null) return;
            _accountInput.ActivateInputField();
            Debug.Log("[LoginPanel] OnShow: 自动聚焦账号输入框");
        }

        private void Update()
        {
            if (!IsVisible) return;

            // Tab 键切换输入框
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                var current = es != null ? es.currentSelectedGameObject : null;
                if (current == _accountInput?.gameObject)
                    _passwordInput?.ActivateInputField();
                else
                    _accountInput?.ActivateInputField();
            }

            // Enter 键登录
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_loginButton != null && _loginButton.interactable)
                    _loginButton.onClick.Invoke();
            }
        }
    }
}
