using System;
using System.Net;
using System.Text.RegularExpressions;
using CampusLogin.Core;

namespace CampusLogin.Portal
{
    internal enum PortalState
    {
        /// <summary>已认证在线。</summary>
        Online,
        /// <summary>在校园网内，但还没认证。</summary>
        NeedLogin,
        /// <summary>连不上 2.2.2.2：不在校园网，或被 TUN 代理拦截。</summary>
        Unreachable,
        /// <summary>能连上但响应无法识别。</summary>
        Unknown,
    }

    internal sealed class ProbeResult
    {
        public PortalState State { get; set; }
        /// <summary>门户参数（仅 NeedLogin 时有值）。</summary>
        public QueryParams Params { get; set; }
        /// <summary>认证服务器根地址，例如 http://172.16.253.121（仅 NeedLogin 时有值）。</summary>
        public string PortalBase { get; set; }
        public string Detail { get; set; }
        public string UserIp => Params?["wlanuserip"];
    }

    internal enum LoginOutcome
    {
        Success,
        /// <summary>账号或密码类问题：不能自动重试，否则可能触发锁号。</summary>
        AccountProblem,
        /// <summary>其他失败：可以退避后重试。</summary>
        Failed,
    }

    internal sealed class LoginResult
    {
        public LoginOutcome Outcome { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }

    internal interface IPortalAdapter
    {
        ProbeResult Probe();
        LoginResult Login(ProbeResult probe, Credential credential);
    }

    /// <summary>
    /// 韶关学院校园网门户（抓包分析得出，与 v0.1 的 campus-login.ps1 相同）：
    ///   1. GET http://2.2.2.2/
    ///        已登录 -> 302 到 portalLogout.do?...
    ///        未登录 -> 200，页面里 location.replace("...portalScript.do?wlanuserip=...&amp;mac=...")
    ///   2. GET /PortalJsonAction.do?&lt;上一步的参数&gt;&amp;viewStatus=1 -> 取 serverip / id / uuid / timestamp
    ///   3. GET /quickauth.do?userid=..&amp;passwd=..&amp;...           -> {"code":"0","message":"认证成功"}
    ///
    /// 安全约定：永远不会请求 portalLogout.do / quickauthdisconn.do（下线接口），
    /// 日志里不写入密码，也不写入含密码的 URL。
    /// </summary>
    internal sealed class SgcPortalAdapter : IPortalAdapter
    {
        public const string ProbeUrl = "http://2.2.2.2/";
        public const string DefaultPortalBase = "http://172.16.253.121";

        // 可选的绝对地址前缀 + portalScript.do?参数。参数部分的字符集与 v0.1 的正则一致（已在真机验证）。
        static readonly Regex PortalScriptRegex = new Regex(
            @"(?:(https?://[^/""'\s]+)[^""'\s]*?)?portalScript\.do\?([^""'\s+]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex LogoutRedirectRegex = new Regex(@"portalLogout\.do", RegexOptions.IgnoreCase);

        // 返回这些字样时视为账号问题，停止自动重试，避免反复用错误密码登录导致锁号。
        // 只匹配明确指向账号本身的词；"会话已过期""uuid不存在""账号已在线"之类的临时错误走退避重试。
        // 没被识别出来的密码错误由 LoginLoop 的"连续失败 5 次即暂停"兜底。
        static readonly Regex AccountProblemRegex = new Regex(
            "密码|口令|用户名|用户不存在|(账号|帐号|用户)(不存在|错误|无效|被锁|已锁|锁定|冻结|停用|禁用|欠费|停机)|欠费|停机|已锁定|被锁定|被冻结|已冻结",
            RegexOptions.CultureInvariant);

        public ProbeResult Probe()
        {
            HttpResult r;
            try
            {
                r = HttpUtil.Get(ProbeUrl);
            }
            catch (Exception ex)
            {
                return new ProbeResult { State = PortalState.Unreachable, Detail = ex.Message };
            }
            return ParseProbe(r);
        }

        /// <summary>解析 2.2.2.2 的响应（纯函数，便于单元测试）。</summary>
        internal static ProbeResult ParseProbe(HttpResult r)
        {
            bool redirect = r.Status == 301 || r.Status == 302 || r.Status == 303 || r.Status == 307;
            if (redirect && r.Location != null && LogoutRedirectRegex.IsMatch(r.Location))
                return new ProbeResult { State = PortalState.Online };

            var m = PortalScriptRegex.Match(r.Body ?? "");
            if (m.Success)
            {
                var p = QueryParams.Parse(m.Groups[2].Value);
                p["url"] = "http://2.2.2.2";
                return new ProbeResult
                {
                    State = PortalState.NeedLogin,
                    Params = p,
                    PortalBase = ChoosePortalBase(m.Groups[1].Success ? m.Groups[1].Value : null),
                };
            }

            return new ProbeResult { State = PortalState.Unknown, Detail = $"HTTP {r.Status} {r.Location}".TrimEnd() };
        }

        /// <summary>
        /// 门户地址优先用页面里给出的；但密码会发到这个地址，所以只接受内网 IPv4，
        /// 其他情况一律回退到已知的认证服务器。
        /// </summary>
        internal static string ChoosePortalBase(string detected)
        {
            if (string.IsNullOrEmpty(detected)) return DefaultPortalBase;
            if (!Uri.TryCreate(detected, UriKind.Absolute, out Uri uri)) return DefaultPortalBase;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return DefaultPortalBase;
            if (!IPAddress.TryParse(uri.Host, out IPAddress ip) || !IsPrivateIPv4(ip)) return DefaultPortalBase;
            return uri.GetLeftPart(UriPartial.Authority);
        }

        internal static bool IsPrivateIPv4(IPAddress ip)
        {
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        public LoginResult Login(ProbeResult probe, Credential credential)
        {
            if (probe?.State != PortalState.NeedLogin) throw new ArgumentException("只有未登录状态才能认证", nameof(probe));
            string portal = probe.PortalBase ?? DefaultPortalBase;
            string baseQuery = probe.Params.ToString();
            string referer = $"{portal}/portal.do?{baseQuery}";

            // 第 2 步：取门户配置
            var cfgResp = HttpUtil.Get($"{portal}/PortalJsonAction.do?{baseQuery}&viewStatus=1", referer, ajax: true);
            object cfg;
            try
            {
                cfg = Json.Parse(cfgResp.Body);
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"门户配置不是有效的 JSON（HTTP {cfgResp.Status}）");
            }
            if (Json.IsTrue(cfg, "authByRas"))
                throw new NotSupportedException("门户开启了 RSA 加密登录（authByRas=true），当前版本还不支持，请到项目主页反馈。");

            // 第 3 步：认证（参数与浏览器完全一致）
            var login = new QueryParams
            {
                ["userid"] = credential.UserName,
                ["passwd"] = credential.Password,
                ["wlanuserip"] = probe.Params["wlanuserip"],
                ["wlanacname"] = probe.Params["wlanacname"],
                ["wlanacIp"] = Json.Get(cfg, "serverForm", "serverip"),
                ["ssid"] = "",
                ["vlan"] = probe.Params["vlan"],
                ["mac"] = probe.Params["mac"],
                ["version"] = Json.Get(cfg, "serverForm", "portalVer"),
                ["portalpageid"] = Json.Get(cfg, "portalconfig", "id"),
                ["timestamp"] = Json.Get(cfg, "portalconfig", "timestamp"),
                ["uuid"] = Json.Get(cfg, "portalconfig", "uuid"),
                ["portaltype"] = "0",
                ["hostname"] = probe.Params["hostname"],
                ["bindCtrlId"] = "",
                ["validateType"] = "0",
                ["bindOperatorType"] = "2",
                ["sendFttrNotice"] = "0",
            };

            HttpResult authResp;
            try
            {
                authResp = HttpUtil.Get($"{portal}/quickauth.do?{login}", referer, ajax: true);
            }
            catch (WebException ex)
            {
                // 只保留状态，不让任何可能带 URL（含密码）的信息流出
                throw new InvalidOperationException($"认证请求失败：{ex.Status}");
            }
            return ParseLoginResponse(authResp.Body);
        }

        /// <summary>解析 quickauth.do 的响应并分类（纯函数，便于单元测试）。</summary>
        internal static LoginResult ParseLoginResponse(string body)
        {
            object json;
            try
            {
                json = Json.Parse(body);
            }
            catch (Exception)
            {
                return new LoginResult { Outcome = LoginOutcome.Failed, Message = "认证响应不是有效的 JSON" };
            }
            string code = Json.Get(json, "code");
            string message = Json.Get(json, "message") ?? "";
            LoginOutcome outcome;
            if (code == "0") outcome = LoginOutcome.Success;
            else if (AccountProblemRegex.IsMatch(message)) outcome = LoginOutcome.AccountProblem;
            else outcome = LoginOutcome.Failed;
            return new LoginResult { Outcome = outcome, Code = code, Message = message };
        }
    }
}
