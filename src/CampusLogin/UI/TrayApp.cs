using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CampusLogin.Core;
using CampusLogin.Portal;
using Microsoft.Win32;

namespace CampusLogin.UI
{
    /// <summary>托盘常驻程序：没有主窗口，只有右下角图标和菜单。</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        readonly Settings _settings;
        readonly LoginLoop _loop;
        readonly NotifyIcon _tray;
        readonly Control _ui; // 用来把后台线程的事件切回界面线程
        readonly ToolStripMenuItem _statusItem;
        readonly ToolStripMenuItem _autoStartItem;
        readonly ToolStripMenuItem _notifyItem;
        readonly ToolStripMenuItem _proxyHelpItem;
        readonly EventWaitHandle _showEvent;
        readonly EventWaitHandle _exitEvent;
        readonly RegisteredWaitHandle _showWait;
        readonly RegisteredWaitHandle _exitWait;

        SetupForm _setupForm;
        ClashHelpForm _proxyForm;
        Action _balloonClick;
        System.Drawing.Font _boldFont;
        bool _exiting;

        public TrayApp(bool launchedAtLogon)
        {
            _ui = new Control();
            _ui.CreateControl();
            _settings = Settings.Load();
            Log.Write($"启动 {AppInfo.VersionText}（{(launchedAtLogon ? "开机自启" : "手动启动")}，位置 {AutoStart.CurrentExe}）。");

            // ---- 托盘菜单 ----
            var menu = new ContextMenuStrip();
            _statusItem = new ToolStripMenuItem("状态：正在检查…") { Enabled = false };
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("立即检查", null, (s, e) => _loop.Trigger("手动检查", 0, resume: true));
            menu.Items.Add("账号设置…", null, (s, e) => ShowSetup(null));
            menu.Items.Add(new ToolStripSeparator());
            _autoStartItem = new ToolStripMenuItem("开机自动运行") { CheckOnClick = true };
            _autoStartItem.Click += (s, e) => ToggleAutoStart();
            menu.Items.Add(_autoStartItem);
            _notifyItem = new ToolStripMenuItem("自动登录成功时提示") { CheckOnClick = true, Checked = _settings.Notify };
            _notifyItem.Click += (s, e) => { _settings.Notify = _notifyItem.Checked; SaveSettings(); };
            menu.Items.Add(_notifyItem);
            menu.Items.Add("打开日志", null, (s, e) => OpenLog());
            _proxyHelpItem = new ToolStripMenuItem("开着 Clash 等代理？设置帮助…", null, (s, e) => ShowProxyHelp());
            menu.Items.Add(_proxyHelpItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add($"项目主页 / 检查更新（当前 {AppInfo.VersionText}）", null, (s, e) => OpenUrl(AppInfo.ReleasesUrl));
            menu.Items.Add("清除数据并退出…", null, (s, e) => UninstallFromMenu());
            menu.Items.Add("退出", null, (s, e) => ExitApp());
            menu.Opening += (s, e) => _autoStartItem.Checked = AutoStart.IsEnabled;

            _tray = new NotifyIcon
            {
                Icon = Icons.ForState(LoopState.Starting),
                Text = AppInfo.Name,
                ContextMenuStrip = menu,
                Visible = true,
            };
            _tray.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowTrayMenu();
            };
            _tray.DoubleClick += (s, e) => ShowSetup(null);
            // 不在 BalloonTipClosed 里清空：Win10/11 的通知超时后会进操作中心，用户之后点击仍要能响应
            _tray.BalloonTipClicked += (s, e) => _balloonClick?.Invoke();

            // ---- 后台检测循环 ----
            _loop = new LoginLoop(new SgcPortalAdapter(), () => _settings.IntervalSeconds);
            _loop.StatusChanged += st => OnUi(() => UpdateStatus(st));
            _loop.Notice += (n, d) => OnUi(() => OnNotice(n, d));

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // ---- 与其他实例通信：再次双击 exe → 弹设置；升级/卸载 → 退出 ----
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowEventName);
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ExitEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (o, t) => OnUi(() => ShowSetup(null)), null, -1, false);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (o, t) => OnUi(ExitApp), null, -1, false);

            FirstRunFlow(launchedAtLogon);
            // 开机时稍等再检查：网络可能还没就绪，也避免一启动就密集动作
            _loop.Start(initialDelaySeconds: launchedAtLogon ? 10 : 1);
        }

        void FirstRunFlow(bool launchedAtLogon)
        {
            if (!CredentialStore.Exists || !_settings.SetupDone)
            {
                // 开机自启时不主动弹窗，等检测到需要登录时再用通知提醒
                if (!launchedAtLogon) ShowSetup(null);
                return;
            }

            // 只提示，不自动改写：自启快捷方式指向的是别处（程序被移动过）
            if (AutoStart.IsStale)
            {
                Balloon("开机自启指向了旧位置",
                    "程序好像被移动过。点这里打开设置，重新勾选\"开机自动运行\"并保存。", ToolTipIcon.Warning, () => ShowSetup(null));
            }
        }

        // ---------------- 状态与通知 ----------------

        void UpdateStatus(LoopStatus st)
        {
            string text = DescribeStatus(st);
            _statusItem.Text = "状态：" + text;
            _tray.Icon = Icons.ForState(st.State);
            _tray.Text = Truncate($"{AppInfo.Name}：{text}", 63); // NotifyIcon.Text 最长 63 字符
            bool tun = st.TunAdapter != null;
            if (tun && _boldFont == null)
                _boldFont = new System.Drawing.Font(_proxyHelpItem.Font, System.Drawing.FontStyle.Bold);
            if (tun != (_proxyHelpItem.Font == _boldFont))
                _proxyHelpItem.Font = tun ? _boldFont : null;
            _setupForm?.SetStatus("当前状态：" + text);
        }

        internal static string DescribeStatus(LoopStatus st)
        {
            switch (st.State)
            {
                case LoopState.Online: return "已在线";
                case LoopState.LoggingIn: return "正在认证…";
                case LoopState.Unreachable:
                    return st.TunAdapter != null ? "连不上认证服务器（可能被代理拦截）" : "未连接校园网";
                case LoopState.Unknown: return "认证页面异常，稍后重试";
                case LoopState.NoCredential: return "未登录，请先设置账号";
                case LoopState.AccountProblem: return "账号或密码有误，已暂停自动登录";
                case LoopState.LoginError: return "认证失败，稍后自动重试";
                default: return "正在检查…";
            }
        }

        void OnNotice(LoopNotice notice, string detail)
        {
            switch (notice)
            {
                case LoopNotice.LoggedIn:
                    if (_settings.Notify)
                        Balloon("校园网已自动登录", detail != null ? $"本机 IP {detail}" : "认证成功", ToolTipIcon.Info);
                    break;

                case LoopNotice.AccountProblem:
                    Balloon("校园网登录失败", $"{detail}\n已暂停自动登录，请检查学号和密码。", ToolTipIcon.Error, () => ShowSetup(detail));
                    ShowSetup($"学校认证服务器返回：{detail}。请检查学号和密码。");
                    break;

                case LoopNotice.RepeatedFailures:
                    Balloon("校园网多次认证失败", $"{detail}\n程序会继续自动重试；也可以先用浏览器打开 2.2.2.2 手动登录。", ToolTipIcon.Warning, OpenLog);
                    break;

                case LoopNotice.TunSuspected:
                    Balloon("连不上校园网认证服务器",
                        "检测到 Clash 等代理开着 TUN 模式。如果你在校园网内，点这里查看怎么设置直连。",
                        ToolTipIcon.Warning, ShowProxyHelp);
                    break;

                case LoopNotice.NeedCredential:
                    Balloon("校园网未登录", "还没有设置校园网账号，点这里设置。", ToolTipIcon.Warning, () => ShowSetup(null));
                    break;
            }
        }

        void Balloon(string title, string text, ToolTipIcon icon, Action onClick = null)
        {
            _balloonClick = onClick;
            _tray.ShowBalloonTip(8000, title, text, icon);
        }

        // ---------------- 窗口 ----------------

        void ShowSetup(string error)
        {
            if (_exiting) return;
            if (_setupForm != null)
            {
                if (error != null) _setupForm.ShowError(error);
                _setupForm.Activate();
                return;
            }
            _setupForm = new SetupForm(_settings, "当前状态：" + DescribeStatus(_loop?.Status ?? new LoopStatus()), error);
            _setupForm.FormClosed += (s, e) =>
            {
                bool saved = _setupForm.DialogResult == DialogResult.OK;
                _setupForm.Dispose();
                _setupForm = null;
                if (saved)
                {
                    _notifyItem.Checked = _settings.Notify;
                    _loop.Trigger("账号已更新", 0, resume: true);
                }
            };
            _setupForm.Show();
        }

        void ShowProxyHelp()
        {
            if (_proxyForm != null)
            {
                _proxyForm.Activate();
                return;
            }
            var st = _loop.Status;
            _proxyForm = new ClashHelpForm(st.TunAdapter ?? ProxyDiagnostics.FindProxyTunAdapter(), st.PortalBase);
            _proxyForm.FormClosed += (s, e) => { _proxyForm.Dispose(); _proxyForm = null; };
            _proxyForm.Show();
            _proxyForm.Activate();
        }

        void ShowTrayMenu()
        {
            // NotifyIcon 左键默认不弹菜单，借用它内部的方法，让左键和右键一样
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(_tray, null);
        }

        // ---------------- 菜单动作 ----------------

        void ToggleAutoStart()
        {
            bool enable = _autoStartItem.Checked;
            try
            {
                if (enable) AutoStart.Enable();
                else AutoStart.Disable();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置开机自启失败：{ex.Message}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void SaveSettings()
        {
            try { _settings.Save(); }
            catch (Exception ex) { Log.Write($"保存设置失败：{ex.Message}"); }
        }

        static void OpenLog()
        {
            if (File.Exists(Paths.LogFile)) Process.Start("notepad.exe", $"\"{Paths.LogFile}\"");
            else MessageBox.Show("还没有日志。", AppInfo.Name);
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"打不开浏览器：{ex.Message}\n{url}", AppInfo.Name); }
        }

        void UninstallFromMenu()
        {
            var answer = MessageBox.Show(
                "将删除：\n  • 开机自启快捷方式\n  • 保存的账号、设置和日志\n\n然后程序退出。不会注销当前的校园网登录。确定吗？",
                $"清除{AppInfo.Name}的数据", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.OK) return;
            try
            {
                Uninstaller.RemoveAllData();
                MessageBox.Show($"已清除。要彻底移除，退出后删除程序文件即可：\n{AutoStart.CurrentExe}",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"清除时出错：{ex.Message}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ExitApp();
        }

        // ---------------- 系统事件 ----------------

        void OnNetworkAddressChanged(object sender, EventArgs e) => _loop.Trigger("网络变化", 3);

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) _loop.Trigger("从睡眠唤醒", 5);
        }

        void OnUi(Action action)
        {
            if (_exiting || _ui.IsDisposed) return;
            try { _ui.BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _showWait.Unregister(null);
            _exitWait.Unregister(null);
            _setupForm?.Close();
            _proxyForm?.Close();
            _tray.Visible = false;
            _tray.Dispose();
            _boldFont?.Dispose();
            _loop.Dispose();
            _showEvent.Dispose();
            _exitEvent.Dispose();
            Log.Write("退出。");
            ExitThread();
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
