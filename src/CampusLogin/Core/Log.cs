using System;
using System.IO;
using System.Text;

namespace CampusLogin.Core
{
    /// <summary>所有落盘位置集中在这里，"清除数据"时按这里删除。程序本身是便携版，不安装。</summary>
    internal static class Paths
    {
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        /// <summary>数据目录（与 v0.1 相同）：凭据、设置、日志。</summary>
        public static readonly string DataDir = Path.Combine(LocalAppData, "CampusLogin");
        public static readonly string CredentialFile = Path.Combine(DataDir, "credential.dat");
        public static readonly string LegacyCredentialFile = Path.Combine(DataDir, "credential.xml");
        public static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
        public static readonly string LogFile = Path.Combine(DataDir, "campus-login.log");

        public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);
    }

    /// <summary>
    /// 简单文本日志，格式与 v0.1 一致。约定：任何调用方都不得传入密码或含密码的 URL。
    /// </summary>
    internal static class Log
    {
        const long MaxBytes = 1024 * 1024;
        static readonly object Gate = new object();
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>命令行模式下同时输出到控制台。</summary>
        public static bool EchoToConsole { get; set; }

        /// <summary>卸载后置为 false，避免退出前的日志又把数据目录建回来。</summary>
        public static bool FileEnabled { get; set; } = true;

        public static void Write(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
            if (EchoToConsole)
            {
                try { Console.WriteLine(line); } catch { }
            }
            if (!FileEnabled) return;
            lock (Gate)
            {
                try
                {
                    Paths.EnsureDataDir();
                    var fi = new FileInfo(Paths.LogFile);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = Paths.LogFile + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Paths.LogFile, old);
                    }
                    File.AppendAllText(Paths.LogFile, line + "\r\n", Utf8);
                }
                catch
                {
                    // 日志写不进去不应影响登录本身
                }
            }
        }
    }
}
