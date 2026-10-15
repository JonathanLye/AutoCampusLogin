using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace CampusLogin.Core
{
    /// <summary>
    /// 便携版：程序放在哪就从哪运行，不复制自己、不写注册表 Run 键、不做自删除。
    /// 开机自启 = 在"启动"文件夹里放一个快捷方式（用户可见，可手动删除），只在用户勾选并保存时创建。
    /// </summary>
    internal static class AutoStart
    {
        public const string Arg = "--autostart";
        const string ShortcutName = "校园网自动登录.lnk";

        public static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

        public static string CurrentExe => Path.GetFullPath(Assembly.GetEntryAssembly().Location);

        public static Version CurrentVersion => Assembly.GetEntryAssembly().GetName().Version;

        /// <summary>快捷方式存在并指向当前这个 exe。</summary>
        public static bool IsEnabled => SamePath(GetTarget(), CurrentExe);

        /// <summary>快捷方式存在，但指向别的位置（程序被移动过，或是另一份副本）。</summary>
        public static bool IsStale
        {
            get
            {
                string target = GetTarget();
                return target != null && !SamePath(target, CurrentExe);
            }
        }

        /// <summary>返回启动快捷方式指向的程序；没有快捷方式返回 null。</summary>
        public static string GetTarget()
        {
            if (!File.Exists(ShortcutPath)) return null;
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(ShortcutPath, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch (Exception)
            {
                return "";
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        public static void Enable()
        {
            string exe = CurrentExe;
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(exe);
                link.SetArguments(Arg);
                link.SetWorkingDirectory(Path.GetDirectoryName(exe));
                link.SetDescription("开机后自动完成校园网认证");
                link.SetIconLocation(exe, 0);
                ((IPersistFile)link).Save(ShortcutPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
            Log.Write($"开机自启：开（{ShortcutPath} → {exe}）");
        }

        public static void Disable()
        {
            if (!File.Exists(ShortcutPath)) return;
            File.Delete(ShortcutPath);
            Log.Write("开机自启：关");
        }

        static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- IShellLink COM 接口（Windows 自带的快捷方式组件）----

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }

    internal static class Uninstaller
    {
        /// <summary>
        /// 清除本程序留下的一切：启动快捷方式 + 数据目录（账号、设置、日志）。
        /// 程序文件本身由用户自己删除（便携版，不做自删除）。
        /// </summary>
        public static void RemoveAllData()
        {
            Log.Write("清除数据。");
            Log.FileEnabled = false;
            AutoStart.Disable();
            if (Directory.Exists(Paths.DataDir)) Directory.Delete(Paths.DataDir, recursive: true);
        }
    }
}
