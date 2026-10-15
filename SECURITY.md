# 安全说明

## 报告安全问题

如果你发现了安全问题（例如密码可能泄露、程序可能被利用），请**不要**公开提 Issue。
请通过 GitHub 的 [Private vulnerability reporting](https://github.com/JonathanLye/AutoCampusLogin/security/advisories/new) 私下报告。

## 设计上的安全约定

- 凭据使用 Windows DPAPI（CurrentUser 范围）加密保存，只有本人的 Windows 账户能解密。
- 密码只发送给学校认证服务器。认证服务器地址只接受内网 IPv4 地址，其余情况一律回退到已知地址。
- 程序从不调用门户的下线接口（`portalLogout.do`、`quickauthdisconn.do`）。
- 密码、含密码的 URL 都不会写进日志或异常信息。
- 程序没有遥测，除了 `2.2.2.2` 和学校认证服务器，不访问任何网络地址。
- 不复制自身、不写注册表自启、不自删除、不需要管理员权限。开机自启和读取凭据都只在用户明确操作后发生。
- 发布的 exe 由 GitHub Actions 从公开源码构建，附带 SHA256 和构建来源证明（`gh attestation verify`）。

## 已知限制

- 学校认证系统使用明文 HTTP，同一网络里的攻击者理论上可以截获登录请求。浏览器登录也有同样的问题，程序无法改变。
- 程序目前没有代码签名，首次运行时 Windows SmartScreen 会提示"未知发布者"。
