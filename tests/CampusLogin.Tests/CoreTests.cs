using System.Security.Cryptography;
using System.Text;
using CampusLogin.Core;
using Xunit;

namespace CampusLogin.Tests
{
    public class CoreTests
    {
        [Theory]
        [InlineData(1, 60)]
        [InlineData(2, 120)]
        [InlineData(3, 240)]
        [InlineData(4, 300)]
        [InlineData(50, 300)]
        public void UnreachableBackoffIsCapped(int attempt, int expected)
        {
            Assert.Equal(expected, LoginLoop.Backoff(60, attempt, 300));
        }

        [Theory]
        [InlineData(1, 30)]
        [InlineData(3, 120)]
        [InlineData(10, 600)]
        public void LoginFailureBackoffIsCapped(int attempt, int expected)
        {
            Assert.Equal(expected, LoginLoop.Backoff(30, attempt, 600));
        }

        [Fact]
        public void ParsesLegacyPowerShellCredentialXml()
        {
            const string xml = @"<Objs Version=""1.1.0.1"" xmlns=""http://schemas.microsoft.com/powershell/2004/04"">
  <Obj RefId=""0"">
    <TN RefId=""0""><T>System.Management.Automation.PSCredential</T><T>System.Object</T></TN>
    <ToString>System.Management.Automation.PSCredential</ToString>
    <Props>
      <S N=""UserName"">20240000001</S>
      <SS N=""Password"">01000000d08c9ddf0115d1118c7a00c04fc297eb</SS>
    </Props>
  </Obj>
</Objs>";
            var (user, hex) = CredentialStore.ParseClixml(xml);
            Assert.Equal("20240000001", user);
            Assert.Equal("01000000d08c9ddf0115d1118c7a00c04fc297eb", hex);
        }

        [Fact]
        public void DecryptsSecureStringHexFormat()
        {
            // 模拟 ConvertFrom-SecureString：DPAPI CurrentUser、无附加熵、UTF-16LE 明文
            byte[] blob = ProtectedData.Protect(Encoding.Unicode.GetBytes("p@ss中文"), null, DataProtectionScope.CurrentUser);
            var hex = new StringBuilder();
            foreach (byte b in blob) hex.Append(b.ToString("x2"));
            Assert.Equal("p@ss中文", CredentialStore.DecryptSecureStringHex(hex.ToString()));
        }

        [Theory]
        [InlineData("Mihomo", "Meta Tunnel", true)]
        [InlineData("Clash", "Wintun Userspace Tunnel", true)]
        [InlineData("sing-box", "sing-box tun", true)]
        [InlineData("Tailscale", "Tailscale Tunnel", false)]
        [InlineData("以太网", "Intel(R) Ethernet Connection (18) I219-V", false)]
        [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", false)]
        public void DetectsProxyTunAdapters(string name, string description, bool expected)
        {
            Assert.Equal(expected, ProxyDiagnostics.IsProxyTun(name, description));
        }

        [Fact]
        public void DirectRulesUseDetectedPortalHost()
        {
            string rules = ProxyDiagnostics.BuildDirectRules("http://10.9.8.7:8080");
            Assert.Contains("IP-CIDR,2.2.2.2/32,DIRECT,no-resolve", rules);
            Assert.Contains("IP-CIDR,10.9.8.7/32,DIRECT,no-resolve", rules);
            Assert.Contains("IP-CIDR,172.16.253.121/32", ProxyDiagnostics.BuildDirectRules(null));
        }

        [Theory]
        [InlineData(@"D:\工具\校园网\CampusLogin.exe", false)]
        [InlineData(@"C:\Users\a\Downloads\CampusLogin.exe", false)]
        [InlineData(@"D:\Temp\校园网\CampusLogin.exe", false)]
        [InlineData(@"C:\Users\a\Desktop\Temp\CampusLogin.exe", false)]
        [InlineData(@"C:\Windows\Temp\x\CampusLogin.exe", true)]
        [InlineData(@"C:\Users\a\AppData\Local\Temp\Rar$EXa1.234\CampusLogin.exe", true)]
        [InlineData(@"C:\Users\a\AppData\Local\Microsoft\Windows\INetCache\IE\X1\CampusLogin.exe", true)]
        public void DetectsTemporaryLocations(string path, bool expected)
        {
            Assert.Equal(expected, Program.IsTemporaryLocation(path));
        }

        [Fact]
        public void SystemTempPathIsTemporary()
        {
            Assert.True(Program.IsTemporaryLocation(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "x", "CampusLogin.exe")));
        }
    }
}
