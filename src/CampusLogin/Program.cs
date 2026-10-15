using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CampusLogin.Core;
using CampusLogin.Portal;
using CampusLogin.UI;

namespace CampusLogin
{
    internal static class AppInfo
    {
        public const string Name = "校园网自动登录";
        public const string ProjectUrl = "https://github.com/JonathanLye/AutoCampusLogin";
        public const string ReleasesUrl = ProjectUrl + "/releases";

        const string AppId = "CampusLogin-7d1f3c2a-5b8e-4a61-9c0d-2e6f8b4a1c55";
        public const string MutexName = @"Local\" + AppId;
        /// <summary>再次双击 exe 时，通知已运行的实例弹出设置窗。</summary>
        public const string ShowEventName = @"Local\" + AppId + "-show";
        /// <summary>命令行清除数据时，通知已运行的实例退出。</summary>
        public const string ExitEventName = @"Local\" + AppId + "-exit";

        public static string VersionText => $"v{AutoStart.CurrentVersion.ToString(3)}";
    }

    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var flags = new HashSet<string>(args.Select(a => a.Trim().ToLowerInvariant()));
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write($"未处理的异常：{e.ExceptionObject}");

            if (flags.Contains("--status") || flags.Contains("--once") || flags.Contains("--version")
                || flags.Contains("--uninstall") || flags.Contains("--help") || flags.Contains("-h") || flags.Contains("/?"))
            {
                return RunCli(flags);
            }
            return RunGui(flags);
        }

        // ---------------- 图形界面（托盘）模式 ----------------

        static int RunGui(HashSet<string> flags)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Log.Write($"界面线程异常：{e.Exception}");

            if (IsTemporaryLocation(AutoStart.CurrentExe))
            {
                MessageBox.Show(
                    "看起来你是直接在压缩包里（或临时文件夹里）打开的程序。\n\n"
                    + "请先把 CampusLogin.exe 放到一个固定的文件夹，例如 D:\\工具\\校园网\\，再从那里双击运行。"
                    + "开机自启会从这个位置启动程序，所以之后不要移动或删除它。",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }

            using (var mutex = new Mutex(false, AppInfo.MutexName))
            {
                if (!TryAcquire(mutex, 0))
                {
                    // 已经在运行：让它弹出设置窗即可
                    Signal(AppInfo.ShowEventName);
                    return 0;
                }
                try
                {
                    Application.Run(new TrayApp(launchedAtLogon: flags.Contains(AutoStart.Arg)));
                    return 0;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// 在系统临时目录运行（多半是在压缩包里直接双击）：既不适合开机自启，也容易被杀软当成可疑程序。
        /// 只认系统的临时目录，用户自己建的 D:\Temp 之类不算。
        /// </summary>
        internal static bool IsTemporaryLocation(string exePath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(exePath)) + Path.DirectorySeparatorChar;
            string temp = Path.GetFullPath(Path.GetTempPath());
            if (!temp.EndsWith(Path.DirectorySeparatorChar.ToString())) temp += Path.DirectorySeparatorChar;
            return dir.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                || dir.IndexOf(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase) >= 0
                || dir.IndexOf(@"\Windows\Temp\", StringComparison.OrdinalIgnoreCase) >= 0
                || dir.IndexOf(@"\INetCache\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool TryAcquire(Mutex mutex, int timeoutMs)
        {
            try
            {
                return mutex.WaitOne(timeoutMs);
            }
            catch (AbandonedMutexException)
            {
                return true; // 上一个实例被强制结束，锁已经归我们
            }
        }

        static void Signal(string eventName)
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(eventName))
                    ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
        }

        // ---------------- 命令行模式 ----------------

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        static int RunCli(HashSet<string> flags)
        {
            AttachConsole(-1); // 从 cmd / PowerShell 运行时把输出接到父控制台
            Console.WriteLine();

            if (flags.Contains("--version"))
            {
                Console.WriteLine($"{AppInfo.Name} {AppInfo.VersionText}");
                return 0;
            }

            if (flags.Contains("--uninstall"))
            {
                Signal(AppInfo.ExitEventName);
                using (var mutex = new Mutex(false, AppInfo.MutexName))
                {
                    if (!TryAcquire(mutex, 15000))
                    {
                        Console.WriteLine("程序仍在运行，请先在托盘图标上右键 → 退出，然后再试。");
                        return 1;
                    }
                    try
                    {
                        Uninstaller.RemoveAllData();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"清除时出错：{ex.Message}");
                        return 1;
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }
                Console.WriteLine("已清除：开机自启快捷方式、保存的账号、设置和日志都已删除。");
                Console.WriteLine($"要彻底移除，删除程序文件即可：{AutoStart.CurrentExe}");
                return 0;
            }

            if (flags.Contains("--status"))
            {
                var probe = new SgcPortalAdapter().Probe();
                switch (probe.State)
                {
                    case PortalState.Online:
                        Console.WriteLine("状态：已登录");
                        return 0;
                    case PortalState.NeedLogin:
                        Console.WriteLine($"状态：未登录（IP {probe.UserIp}，认证服务器 {probe.PortalBase}）");
                        return 1;
                    case PortalState.Unreachable:
                        Console.WriteLine($"状态：认证服务器不可达 —— {probe.Detail}");
                        PrintTunHint();
                        return 2;
                    default:
                        Console.WriteLine($"状态：无法识别 —— {probe.Detail}");
                        PrintTunHint();
                        return 3;
                }
            }

            if (flags.Contains("--once"))
            {
                Log.EchoToConsole = true;
                return RunOnce();
            }

            Console.WriteLine($"{AppInfo.Name} {AppInfo.VersionText}");
            Console.WriteLine("  （不带参数）  托盘常驻模式");
            Console.WriteLine("  --status      只检查是否已登录，不做任何改动");
            Console.WriteLine("  --once        检查一次，未登录就登录一次");
            Console.WriteLine("  --uninstall   清除开机自启和保存的账号/设置/日志");
            Console.WriteLine("  --version     显示版本");
            return 0;
        }

        static int RunOnce()
        {
            var portal = new SgcPortalAdapter();
            var probe = portal.Probe();
            switch (probe.State)
            {
                case PortalState.Online:
                    Log.Write("已登录，无需操作。");
                    return 0;
                case PortalState.Unreachable:
                    Log.Write($"认证服务器连不上（未连校园网，或被代理拦截）：{probe.Detail}");
                    PrintTunHint();
                    return 2;
                case PortalState.Unknown:
                    Log.Write($"无法识别 2.2.2.2 的响应：{probe.Detail}");
                    return 3;
            }

            Credential cred;
            try
            {
                cred = CredentialStore.Load();
            }
            catch (Exception ex)
            {
                Log.Write($"读取保存的账号失败（请双击运行程序重新设置）：{ex.Message}");
                return 4;
            }
            if (cred == null)
            {
                Log.Write("还没有保存账号，请先双击运行程序完成设置。");
                return 4;
            }
            Log.Write($"检测到未登录（IP {probe.UserIp}），开始认证…");
            try
            {
                var r = portal.Login(probe, cred);
                Log.Write(r.Outcome == LoginOutcome.Success
                    ? $"认证成功：{r.Message}"
                    : $"认证失败（code={r.Code}）：{r.Message}");
                return r.Outcome == LoginOutcome.Success ? 0 : 5;
            }
            catch (Exception ex)
            {
                Log.Write($"认证出错：{ex.Message}");
                return 5;
            }
            finally
            {
                cred.Password = null;
            }
        }

        static void PrintTunHint()
        {
            string tun = ProxyDiagnostics.FindProxyTunAdapter();
            if (tun == null) return;
            Console.WriteLine($"提示：检测到代理 TUN 网卡 {tun}。如果你在校园网内，请在代理软件的规则最前面加上：");
            Console.WriteLine(ProxyDiagnostics.BuildDirectRules(null));
        }
    }
}
