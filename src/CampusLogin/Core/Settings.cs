using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CampusLogin.Core
{
    /// <summary>用户设置，保存在 %LOCALAPPDATA%\CampusLogin\settings.json。</summary>
    internal sealed class Settings
    {
        /// <summary>登录成功、账号出错等情况是否弹出右下角通知。</summary>
        public bool Notify { get; set; } = true;

        /// <summary>在线时的检查间隔（秒）。</summary>
        public int IntervalSeconds { get; set; } = 60;

        /// <summary>首次设置是否已完成（用来决定开机启动时要不要弹设置窗）。</summary>
        public bool SetupDone { get; set; }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(Paths.SettingsFile))
                {
                    var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile, Encoding.UTF8));
                    if (s != null)
                    {
                        s.IntervalSeconds = Math.Max(15, Math.Min(s.IntervalSeconds, 3600));
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"读取设置失败，使用默认设置：{ex.Message}");
            }
            return new Settings();
        }

        public void Save()
        {
            Paths.EnsureDataDir();
            File.WriteAllText(Paths.SettingsFile, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
        }
    }
}
