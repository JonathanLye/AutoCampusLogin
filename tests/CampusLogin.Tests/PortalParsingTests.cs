using CampusLogin.Portal;
using Xunit;

namespace CampusLogin.Tests
{
    // 测试数据均为虚构值
    public class PortalParsingTests
    {
        const string Query = "wlanuserip=10.20.30.40&wlanacname=TEST-AC&mac=aa-bb-cc-dd-ee-ff&vlan=123&hostname=MY-PC";

        [Fact]
        public void RedirectToLogoutPageMeansOnline()
        {
            var r = SgcPortalAdapter.ParseProbe(new HttpResult
            {
                Status = 302,
                Location = "http://172.16.253.121/portalLogout.do?wlanuserip=10.20.30.40",
                Body = "",
            });
            Assert.Equal(PortalState.Online, r.State);
        }

        [Fact]
        public void RelativeScriptUrlUsesDefaultPortal()
        {
            var r = SgcPortalAdapter.ParseProbe(new HttpResult
            {
                Status = 200,
                Body = "<script>location.replace(\"portalScript.do?" + Query + "\")</script>",
            });
            Assert.Equal(PortalState.NeedLogin, r.State);
            Assert.Equal(SgcPortalAdapter.DefaultPortalBase, r.PortalBase);
            Assert.Equal("10.20.30.40", r.UserIp);
            Assert.Equal("aa-bb-cc-dd-ee-ff", r.Params["mac"]);
            Assert.Equal("MY-PC", r.Params["hostname"]);
            Assert.Equal("http://2.2.2.2", r.Params["url"]);
        }

        [Fact]
        public void AbsoluteScriptUrlOnPrivateHostIsUsed()
        {
            var r = SgcPortalAdapter.ParseProbe(new HttpResult
            {
                Status = 200,
                Body = "<html><script type='text/javascript'>location.replace('http://10.9.8.7:8080/eportal/portalScript.do?" + Query + "');</script></html>",
            });
            Assert.Equal(PortalState.NeedLogin, r.State);
            Assert.Equal("http://10.9.8.7:8080", r.PortalBase);
            Assert.Equal("TEST-AC", r.Params["wlanacname"]);
        }

        [Theory]
        [InlineData("http://evil.example.com")]
        [InlineData("http://8.8.8.8")]
        [InlineData("ftp://10.0.0.1")]
        [InlineData("not a url")]
        [InlineData(null)]
        public void UntrustedPortalHostFallsBackToDefault(string detected)
        {
            Assert.Equal(SgcPortalAdapter.DefaultPortalBase, SgcPortalAdapter.ChoosePortalBase(detected));
        }

        [Fact]
        public void UnrecognisedResponseIsUnknown()
        {
            var r = SgcPortalAdapter.ParseProbe(new HttpResult { Status = 200, Body = "<html>hello</html>" });
            Assert.Equal(PortalState.Unknown, r.State);
            Assert.Contains("200", r.Detail);
        }

        [Fact]
        public void RedirectElsewhereIsUnknown()
        {
            var r = SgcPortalAdapter.ParseProbe(new HttpResult { Status = 302, Location = "http://example.com/", Body = "" });
            Assert.Equal(PortalState.Unknown, r.State);
        }

        [Fact]
        public void QueryParamsRoundTripKeepsOrderAndEscapes()
        {
            var p = QueryParams.Parse("?b=2&a=hello%20world&c=&d=%E4%B8%AD");
            Assert.Equal("hello world", p["a"]);
            Assert.Equal("", p["c"]);
            Assert.Equal("中", p["d"]);
            p["a"] = "x&y";
            p["z"] = "1";
            Assert.Equal("b=2&a=x%26y&c=&d=%E4%B8%AD&z=1", p.ToString());
        }

        [Theory]
        [InlineData("{\"code\":\"0\",\"message\":\"认证成功\"}", "Success")]
        [InlineData("{\"code\":0,\"message\":\"认证成功\"}", "Success")]
        [InlineData("{\"code\":\"1\",\"message\":\"用户名或密码错误\"}", "AccountProblem")]
        [InlineData("{\"code\":\"2\",\"message\":\"账号已欠费停机\"}", "AccountProblem")]
        [InlineData("{\"code\":\"3\",\"message\":\"系统繁忙，请稍后再试\"}", "Failed")]
        [InlineData("<html>502 Bad Gateway</html>", "Failed")]
        [InlineData("{\"code\":\"1\",\"message\":\"账号不存在\"}", "AccountProblem")]
        [InlineData("{\"code\":\"1\",\"message\":\"该用户已被锁定\"}", "AccountProblem")]
        // 临时错误：不能当成账号问题，否则会错误地永久暂停
        [InlineData("{\"code\":\"1\",\"message\":\"会话已过期，请刷新页面\"}", "Failed")]
        [InlineData("{\"code\":\"1\",\"message\":\"uuid不存在\"}", "Failed")]
        [InlineData("{\"code\":\"1\",\"message\":\"该账号已在线\"}", "Failed")]
        public void LoginResponseClassification(string body, string expected)
        {
            Assert.Equal(expected, SgcPortalAdapter.ParseLoginResponse(body).Outcome.ToString());
        }

        [Fact]
        public void JsonGetHandlesMixedTypes()
        {
            var o = Json.Parse("{\"serverForm\":{\"serverip\":\"10.1.1.1\",\"portalVer\":2},\"authByRas\":false,\"portalconfig\":{\"id\":42,\"uuid\":null}}");
            Assert.Equal("10.1.1.1", Json.Get(o, "serverForm", "serverip"));
            Assert.Equal("2", Json.Get(o, "serverForm", "portalVer"));
            Assert.Equal("42", Json.Get(o, "portalconfig", "id"));
            Assert.Null(Json.Get(o, "portalconfig", "uuid"));
            Assert.Null(Json.Get(o, "missing", "path"));
            Assert.False(Json.IsTrue(o, "authByRas"));
            Assert.True(Json.IsTrue(Json.Parse("{\"authByRas\":\"true\"}"), "authByRas"));
        }
    }
}
