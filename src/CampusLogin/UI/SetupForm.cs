using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CampusLogin.Core;

namespace CampusLogin.UI
{
    /// <summary>账号设置窗：首次运行、账号出错、托盘菜单"账号设置…"时弹出。</summary>
    internal sealed class SetupForm : Form
    {
        readonly Settings _settings;
        string _savedUser;
        readonly TextBox _user;
        readonly TextBox _pass;
        readonly CheckBox _showPass;
        readonly CheckBox _autoStart;
        readonly CheckBox _notify;
        readonly Label _status;
        readonly Label _error;

        public SetupForm(Settings settings, string statusText, string errorText)
        {
            _settings = settings;
            _savedUser = CredentialStore.TryLoadUserName();

            Text = $"{AppInfo.Name} {AppInfo.VersionText} - 账号设置";
            Icon = Icons.App;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

            _status = new Label { AutoSize = true, Text = statusText ?? "", Margin = new Padding(0, 0, 0, 8) };
            grid.Controls.Add(_status, 0, 0);
            grid.SetColumnSpan(_status, 2);

            _error = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                ForeColor = Color.FromArgb(185, 28, 28),
                Text = errorText ?? "",
                Visible = !string.IsNullOrEmpty(errorText),
                Margin = new Padding(0, 0, 0, 8),
            };
            grid.Controls.Add(_error, 0, 1);
            grid.SetColumnSpan(_error, 2);

            grid.Controls.Add(FieldLabel("学号："), 0, 2);
            _user = new TextBox { Dock = DockStyle.Fill, Text = _savedUser ?? "" };
            grid.Controls.Add(_user, 1, 2);

            grid.Controls.Add(FieldLabel("校园网密码："), 0, 3);
            _pass = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            grid.Controls.Add(_pass, 1, 3);

            _showPass = new CheckBox { Text = "显示密码", AutoSize = true };
            _showPass.CheckedChanged += (s, e) => _pass.UseSystemPasswordChar = !_showPass.Checked;
            grid.Controls.Add(_showPass, 1, 4);

            if (_savedUser == null && File.Exists(Paths.LegacyCredentialFile))
            {
                var import = new LinkLabel { Text = "导入旧版脚本保存的账号", AutoSize = true, Margin = new Padding(0, 6, 6, 0) };
                import.LinkClicked += (s, e) => ImportLegacy(import);
                grid.Controls.Add(import, 0, 4);
            }

            bool firstTime = !settings.SetupDone;
            _autoStart = new CheckBox
            {
                Text = "开机自动运行（推荐）",
                AutoSize = true,
                Checked = firstTime || AutoStart.IsEnabled,
                Margin = new Padding(3, 10, 3, 3),
            };
            grid.Controls.Add(_autoStart, 0, 5);
            grid.SetColumnSpan(_autoStart, 2);

            _notify = new CheckBox { Text = "自动登录成功时在右下角提示", AutoSize = true, Checked = settings.Notify };
            grid.Controls.Add(_notify, 0, 6);
            grid.SetColumnSpan(_notify, 2);

            var privacy = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                ForeColor = SystemColors.GrayText,
                Text = "密码用 Windows 加密保存在本机（只有你的 Windows 账户能解开），只会发送给学校认证服务器。"
                     + "程序不会注销你的网络，也不会联网上传任何数据。",
                Margin = new Padding(0, 10, 0, 10),
            };
            grid.Controls.Add(privacy, 0, 7);
            grid.SetColumnSpan(privacy, 2);

            var location = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                ForeColor = SystemColors.GrayText,
                Text = $"程序位置：{AutoStart.CurrentExe}\r\n开机自启会从这里启动程序，开启后请不要移动或删除这个文件。",
                Margin = new Padding(0, 0, 0, 10),
            };
            grid.Controls.Add(location, 0, 8);
            grid.SetColumnSpan(location, 2);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            var save = new Button { Text = "保存", AutoSize = true };
            save.Click += OnSave;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            grid.Controls.Add(buttons, 0, 9);
            grid.SetColumnSpan(buttons, 2);

            Controls.Add(grid);
            AcceptButton = save;
            CancelButton = cancel;

            if (_savedUser != null) SetCueBanner(_pass, "已保存，不修改请留空");
            Shown += (s, e) =>
            {
                Activate();
                (string.IsNullOrEmpty(_user.Text) ? _user : _pass).Focus();
            };
        }

        public void SetStatus(string statusText) => _status.Text = statusText ?? "";

        static Label FieldLabel(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 6, 6),
        };

        void OnSave(object sender, EventArgs e)
        {
            string user = _user.Text.Trim();
            if (user.Length == 0)
            {
                ShowError("请填写学号。");
                _user.Focus();
                return;
            }

            string pass = _pass.Text;
            if (pass.Length == 0)
            {
                if (_savedUser == null || !string.Equals(_savedUser, user, StringComparison.Ordinal))
                {
                    ShowError("请填写密码。");
                    _pass.Focus();
                    return;
                }
            }

            try
            {
                if (pass.Length > 0)
                    CredentialStore.Save(new Credential { UserName = user, Password = pass });
                _settings.Notify = _notify.Checked;
                _settings.SetupDone = true;
                _settings.Save();
                // 只在设置真的变化时才动启动文件夹
                if (_autoStart.Checked && !AutoStart.IsEnabled) AutoStart.Enable();
                else if (!_autoStart.Checked && AutoStart.IsEnabled) AutoStart.Disable();
            }
            catch (Exception ex)
            {
                ShowError($"保存失败：{ex.Message}");
                return;
            }
            finally
            {
                _pass.Text = "";
            }

            Log.Write($"已保存账号设置（学号 {user}，开机自启：{(_autoStart.Checked ? "开" : "关")}）。");
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>用户主动点击时才解密 v0.1 的旧凭据（启动时不碰任何密码文件）。</summary>
        void ImportLegacy(LinkLabel link)
        {
            Credential cred = CredentialStore.TryImportLegacy();
            if (cred == null)
            {
                ShowError("导入失败，请直接输入学号和密码。");
                return;
            }
            cred.Password = null;
            _savedUser = cred.UserName;
            _user.Text = cred.UserName;
            SetCueBanner(_pass, "已导入，不修改请留空");
            link.Text = "已导入 ✓";
            link.Enabled = false;
            _error.Visible = false;
        }

        public void ShowError(string text)
        {
            _error.Text = text;
            _error.Visible = true;
        }

        const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        static void SetCueBanner(TextBox box, string text)
        {
            box.HandleCreated += (s, e) => SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
            if (box.IsHandleCreated) SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }
}
