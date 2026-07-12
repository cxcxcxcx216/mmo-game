using UnityEngine;
using UnityEngine.UI;
using Minecraft.MMO;
using Minecraft.Config;
using MMO.Protocol;

namespace Minecraft.UI
{
    /// <summary>
    /// 注册面板。用户输入账号+密码+确认密码进行注册。
    /// <para>
    /// 服务端开启 auto-register，注册本质上是调用 Login 接口：
    /// - 账号不存在 → 自动注册并登录成功
    /// - 账号已存在且密码正确 → 登录成功（提示账号已存在）
    /// - 账号已存在且密码错误 → 登录失败
    /// </para>
    /// </summary>
    public class RegisterPanel : UIBase
    {
        // ==================== 布局常量 ====================

        private static readonly Vector2 CardSize = new(480f, 520f);
        private static readonly Vector2 InputSize = new(380f, 44f);
        private static readonly Vector2 PrimaryBtnSize = new(380f, 54f);
        private static readonly Vector2 SecondaryBtnSize = new(380f, 44f);

        // ==================== 控件引用 ====================

        private InputField _accountInput;
        private InputField _passwordInput;
        private InputField _confirmPasswordInput;
        private Button _registerButton;
        private Button _backButton;
        private Text _statusText;

        /// <summary>待注册的账号密码（连接成功后用于发送 LoginReq）。</summary>
        private string _pendingAccount;
        private string _pendingPassword;

        // ==================== 初始化 ====================

        public override void Initialize()
        {
            base.Initialize();

            // 全屏渐变背景
            UIFactory.CreateGradientBg(transform, "Bg", UIFactory.Colors.BgTop, UIFactory.Colors.BgBottom);

            CreateRegisterCard();
            BindNetworkEvents();
        }

        // ==================== UI 构建 ====================

        private void CreateRegisterCard()
        {
            var cardRT = UIFactory.CreateBorderedPanel(transform, "Card",
                UIFactory.Colors.Border, UIFactory.Colors.PanelDark,
                Vector2.zero, CardSize);

            float cursorY = CardSize.y * 0.5f - 52f;

            // 大标题
            UIFactory.CreateShadowTitle(cardRT, "Title", "注 册 账 号",
                34, UIFactory.Colors.TextTitle, UIFactory.Colors.TextTitleShadow,
                new Vector2(0f, cursorY), new Vector2(CardSize.x - 40f, 44f));

            cursorY -= 60f;

            // 副标题
            UIFactory.CreateText(cardRT, "Subtitle", "— 新账号将自动注册并登录 —",
                15, UIFactory.Colors.TextAccent, new Vector2(0f, cursorY),
                new Vector2(CardSize.x - 40f, 22f));

            cursorY -= 34f;

            // 分隔线
            UIFactory.CreateImage(cardRT, "Divider1", UIFactory.Colors.Divider,
                new Vector2(0f, cursorY), new Vector2(InputSize.x, 2f));

            cursorY -= 28f;

            // 账号
            CreateLabeledInput(cardRT, "AccountInput", "账号（4~64位，字母数字下划线）", "", ref cursorY);

            // 密码
            CreateLabeledInput(cardRT, "PasswordInput", "密码（至少8位）", "", ref cursorY);

            // 确认密码
            CreateLabeledInput(cardRT, "ConfirmPasswordInput", "确认密码", "", ref cursorY);

            cursorY -= 6f;

            // 注册按钮
            _registerButton = UIFactory.CreateStyledButton(cardRT, "RegisterBtn",
                "注 册 并 登 录", new Vector2(0f, cursorY), PrimaryBtnSize,
                UIFactory.Colors.BtnPrimary, UIFactory.Colors.BtnPrimaryHover, 22);
            _registerButton.onClick.AddListener(OnRegisterClicked);

            cursorY -= PrimaryBtnSize.y + 10f;

            // 返回按钮
            _backButton = UIFactory.CreateStyledButton(cardRT, "BackBtn",
                "返 回 登 录", new Vector2(0f, cursorY), SecondaryBtnSize,
                UIFactory.Colors.BtnSecondary, UIFactory.Colors.BtnSecondaryHover, 16);
            _backButton.onClick.AddListener(OnBackClicked);

            cursorY -= SecondaryBtnSize.y + 12f;

            // 状态提示
            _statusText = UIFactory.CreateText(cardRT, "Status",
                "请输入账号和密码", 14, UIFactory.Colors.TextDim,
                new Vector2(0f, cursorY), new Vector2(CardSize.x - 40f, 22f));
        }

        /// <summary>创建带标签的输入框。</summary>
        private void CreateLabeledInput(Transform parent, string name,
            string label, string defaultValue, ref float cursorY)
        {
            UIFactory.CreateText(parent, name + "_Label", label,
                13, UIFactory.Colors.TextDim, new Vector2(0f, cursorY),
                new Vector2(InputSize.x, 20f)).alignment = TextAnchor.MiddleLeft;

            cursorY -= 24f;

            var input = UIFactory.CreateInputField(parent, name, defaultValue, "",
                new Vector2(0f, cursorY), InputSize);

            // 密码框遮罩
            if (name == "PasswordInput" || name == "ConfirmPasswordInput")
                input.contentType = InputField.ContentType.Password;

            cursorY -= InputSize.y + 14f;

            switch (name)
            {
                case "AccountInput": _accountInput = input; break;
                case "PasswordInput": _passwordInput = input; break;
                case "ConfirmPasswordInput": _confirmPasswordInput = input; break;
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

        private void OnRegisterClicked()
        {
            string account = _accountInput.text.Trim();
            string password = _passwordInput.text;
            string confirm = _confirmPasswordInput.text;

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

            // 确认密码
            if (password != confirm)
            {
                SetStatus("两次密码输入不一致", true);
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

            Debug.Log($"[RegisterPanel] 开始注册: account={account}");
            gameState.StartOnlineMode(server.Host, server.Port);
        }

        private void OnBackClicked()
        {
            var uiManager = FindObjectOfType<UIManager>();
            if (uiManager != null)
            {
                Hide();
                uiManager.Show("LoginPanel");
            }
        }

        // ==================== 网络事件处理 ====================

        private void HandleConnected()
        {
            SetStatus("连接成功，正在注册...", false);

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
                Debug.Log($"[RegisterPanel] 注册成功: account={_pendingAccount}, accountId={msg.accountId}");
                SetStatus("注册成功，正在进入游戏...", false);
                // 登录成功后 GameStateManager 会自动驱动状态转换到 RoleList
            }
            else
            {
                string desc = ErrorCode.Describe(msg.code);
                Debug.LogWarning($"[RegisterPanel] 注册失败: account={_pendingAccount}, code={msg.code}, desc={desc}");
                SetStatus($"注册失败: {desc}", true);
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
            if (_registerButton != null)
                _registerButton.interactable = interactable;
            // 返回按钮始终可用
            if (_backButton != null)
                _backButton.interactable = true;
        }

        // ==================== 显隐回调 ====================

        /// <summary>面板显示时自动聚焦账号输入框，用户可直接键盘输入。</summary>
        public override void OnShow()
        {
            // 延迟一帧聚焦，确保 Canvas 已完成布局
            Invoke(nameof(FocusFirstInput), 0.05f);
        }

        public override void OnHide()
        {
            CancelInvoke();
            // 清空输入框，防止下次显示时残留
            if (_accountInput != null) _accountInput.text = "";
            if (_passwordInput != null) _passwordInput.text = "";
            if (_confirmPasswordInput != null) _confirmPasswordInput.text = "";
        }

        /// <summary>
        /// 面板销毁时取消所有网络事件订阅，避免 MissingReferenceException 和重复订阅。
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
            Debug.Log("[RegisterPanel] OnShow: 自动聚焦账号输入框");
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
                else if (current == _passwordInput?.gameObject)
                    _confirmPasswordInput?.ActivateInputField();
                else
                    _accountInput?.ActivateInputField();
            }

            // Enter 键提交注册
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_registerButton != null && _registerButton.interactable)
                    _registerButton.onClick.Invoke();
            }
        }

    }
}
