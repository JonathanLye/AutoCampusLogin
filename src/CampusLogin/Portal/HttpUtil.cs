using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace CampusLogin.Portal
{
    internal sealed class HttpResult
    {
        public int Status { get; set; }
        public string Location { get; set; }
        public string Body { get; set; }
    }

    internal static class HttpUtil
    {
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

        /// <summary>
        /// 直连 HTTP GET：不走系统代理、不自动跟随跳转，返回 状态码 / Location / 正文。
        /// 注意：Proxy=null 只能绕过"系统代理"，绕不过 Clash 等的 TUN 模式。
        /// </summary>
        public static HttpResult Get(string url, string referer = null, bool ajax = false, int timeoutMs = 8000)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Proxy = null;
            req.AllowAutoRedirect = false;
            req.KeepAlive = false;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = UserAgent;
            req.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
            if (ajax)
            {
                req.Accept = "application/json, text/javascript, */*; q=0.01";
                req.Headers["X-Requested-With"] = "XMLHttpRequest";
            }

            HttpWebResponse resp;
            try
            {
                resp = (HttpWebResponse)req.GetResponse();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse errorResp)
            {
                resp = errorResp;
            }

            using (resp)
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return new HttpResult
                {
                    Status = (int)resp.StatusCode,
                    Location = resp.Headers[HttpResponseHeader.Location],
                    Body = ReadLimited(reader, MaxBodyChars),
                };
            }
        }

        // 门户页面和 JSON 都只有几 KB；限制大小，防止冒充 2.2.2.2 的页面返回超大响应耗尽内存
        const int MaxBodyChars = 512 * 1024;

        static string ReadLimited(TextReader reader, int maxChars)
        {
            var sb = new StringBuilder();
            var buf = new char[8192];
            int n;
            while (sb.Length < maxChars && (n = reader.Read(buf, 0, Math.Min(buf.Length, maxChars - sb.Length))) > 0)
                sb.Append(buf, 0, n);
            return sb.ToString();
        }
    }

    /// <summary>保持参数顺序的查询字符串（对应 v0.1 的 ConvertFrom/To-QueryString）。</summary>
    internal sealed class QueryParams : IEnumerable<KeyValuePair<string, string>>
    {
        readonly List<KeyValuePair<string, string>> _items = new List<KeyValuePair<string, string>>();

        public string this[string key]
        {
            get
            {
                foreach (var kv in _items)
                    if (kv.Key == key) return kv.Value;
                return null;
            }
            set
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Key == key)
                    {
                        _items[i] = new KeyValuePair<string, string>(key, value);
                        return;
                    }
                }
                _items.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        public int Count => _items.Count;

        public static QueryParams Parse(string query)
        {
            var result = new QueryParams();
            if (string.IsNullOrEmpty(query)) return result;
            foreach (string pair in query.TrimStart('?').Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq);
                string v = eq < 0 ? "" : pair.Substring(eq + 1);
                result[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v);
            }
            return result;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var kv in _items)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            return sb.ToString();
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>门户返回的 JSON 字段类型不固定（数字/字符串/布尔混用），所以用弱类型读取。</summary>
    internal static class Json
    {
        public static object Parse(string json) => new JavaScriptSerializer().DeserializeObject(json);

        /// <summary>按路径取值并转成字符串；任一层不存在返回 null。</summary>
        public static string Get(object root, params string[] path)
        {
            object cur = root;
            foreach (string key in path)
            {
                if (cur is IDictionary<string, object> dict && dict.TryGetValue(key, out object next))
                    cur = next;
                else
                    return null;
            }
            if (cur == null) return null;
            if (cur is bool b) return b ? "true" : "false";
            return Convert.ToString(cur, CultureInfo.InvariantCulture);
        }

        public static bool IsTrue(object root, params string[] path)
        {
            string v = Get(root, path);
            return v != null && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
        }
    }
}
