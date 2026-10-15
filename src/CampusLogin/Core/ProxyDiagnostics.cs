using System;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace CampusLogin.Core
{
    /// <summary>
    /// 检测 Clash / mihomo / sing-box 等代理软件的 TUN 虚拟网卡。
    /// TUN 模式会接管所有流量，2.2.2.2 如果没有设成直连，就会被转发到代理节点，导致认证页打不开。
    /// 程序本身绕不过 TUN（Proxy=null 只对系统代理有效），所以只检测并引导用户加直连规则。
    /// </summary>
    internal static class ProxyDiagnostics
    {
        // 只匹配会接管全局流量的代理 TUN；Tailscale、Hyper-V 等不在此列
        static readonly Regex TunPattern = new Regex(
            @"mihomo|clash|meta tunnel|sing-?box|v2rayn|nekoray|hiddify",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>返回第一个处于启用状态的代理 TUN 网卡名，没有返回 null。</summary>
        public static string FindProxyTunAdapter()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (IsProxyTun(ni.Name, ni.Description)) return $"{ni.Name}（{ni.Description}）";
                }
            }
            catch (Exception ex)
            {
                Log.Write($"枚举网卡失败：{ex.Message}");
            }
            return null;
        }

        internal static bool IsProxyTun(string name, string description) =>
            TunPattern.IsMatch(name ?? "") || TunPattern.IsMatch(description ?? "");

        /// <summary>给 Clash Verge 等加在规则最前面的直连规则。</summary>
        public static string BuildDirectRules(string portalBase)
        {
            string portalHost = "172.16.253.121";
            if (Uri.TryCreate(portalBase, UriKind.Absolute, out Uri uri)) portalHost = uri.Host;
            return string.Join("\r\n",
                "IP-CIDR,2.2.2.2/32,DIRECT,no-resolve",
                $"IP-CIDR,{portalHost}/32,DIRECT,no-resolve",
                "PROCESS-NAME,CampusLogin.exe,DIRECT");
        }
    }
}
