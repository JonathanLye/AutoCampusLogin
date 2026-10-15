using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace CampusLogin.Core
{
    internal sealed class Credential
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// 账号密码用 Windows DPAPI（CurrentUser）加密保存：只有当前 Windows 用户在这台电脑上能解开，
    /// 文件被拷走也没用。
    /// </summary>
    internal static class CredentialStore
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutoCampusLogin/credential/v1");

        public static bool Exists => File.Exists(Paths.CredentialFile);

        /// <summary>读取凭据；没有保存过返回 null。</summary>
        public static Credential Load()
        {
            if (!File.Exists(Paths.CredentialFile)) return null;
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(Paths.CredentialFile), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plain));
                dict.TryGetValue("u", out string user);
                dict.TryGetValue("p", out string pass);
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
                    throw new InvalidDataException("保存的账号文件内容不完整");
                return new Credential { UserName = user, Password = pass };
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        /// <summary>只读学号（用于界面显示），读取失败返回 null。</summary>
        public static string TryLoadUserName()
        {
            try { return Load()?.UserName; } catch { return null; }
        }

        public static void Save(Credential credential)
        {
            if (string.IsNullOrWhiteSpace(credential?.UserName)) throw new ArgumentException("学号不能为空");
            if (string.IsNullOrEmpty(credential.Password)) throw new ArgumentException("密码不能为空");
            string json = new JavaScriptSerializer().Serialize(new Dictionary<string, string>
            {
                ["u"] = credential.UserName.Trim(),
                ["p"] = credential.Password,
            });
            byte[] plain = Encoding.UTF8.GetBytes(json);
            try
            {
                byte[] blob = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                Paths.EnsureDataDir();
                string tmp = Paths.CredentialFile + ".tmp";
                File.WriteAllBytes(tmp, blob);
                if (File.Exists(Paths.CredentialFile)) File.Replace(tmp, Paths.CredentialFile, null);
                else File.Move(tmp, Paths.CredentialFile);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        /// <summary>
        /// 导入 v0.1 脚本用 Export-Clixml 保存的 credential.xml。
        /// 旧文件保留不动（v0.1 脚本仍可作为备用），卸载时随数据目录一起删除。没有旧文件或解不开时返回 null。
        /// </summary>
        public static Credential TryImportLegacy()
        {
            if (!File.Exists(Paths.LegacyCredentialFile)) return null;
            try
            {
                var (user, hex) = ParseClixml(File.ReadAllText(Paths.LegacyCredentialFile));
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(hex)) return null;
                var cred = new Credential { UserName = user, Password = DecryptSecureStringHex(hex) };
                Save(cred);
                Log.Write($"已从 v0.1 导入保存的账号 {user}。");
                return cred;
            }
            catch (Exception ex)
            {
                Log.Write($"导入 v0.1 账号失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>从 PSCredential 的 Clixml 里取出用户名和密码密文（十六进制）。</summary>
        internal static (string User, string PasswordHex) ParseClixml(string xml)
        {
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("ps", "http://schemas.microsoft.com/powershell/2004/04");
            string user = doc.SelectSingleNode("//ps:Props/ps:S[@N='UserName']", ns)?.InnerText;
            string hex = doc.SelectSingleNode("//ps:Props/ps:SS[@N='Password']", ns)?.InnerText;
            return (user?.Trim(), hex?.Trim());
        }

        /// <summary>
        /// ConvertFrom-SecureString（不带 -Key）的格式：DPAPI CurrentUser、无附加熵，明文是 UTF-16LE。
        /// </summary>
        internal static string DecryptSecureStringHex(string hex)
        {
            byte[] blob = HexToBytes(hex);
            byte[] plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.Unicode.GetString(plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        internal static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0) throw new FormatException("十六进制长度不是偶数");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }
    }
}
