# 校园网自动登录（韶关学院）

开机后自动完成韶关学院校园网（`2.2.2.2`）的网页认证，掉线了自动重新登录。
单个 exe，不用安装，不需要管理员权限，开源免费。

- 双击运行，填一次学号和密码，以后开机就不用管了
- 常驻右下角托盘：绿色表示在线，灰色表示不在校园网，红色表示需要处理
- 换网、插拔网线、睡眠唤醒后立即检查
- 程序约 80 KB，只依赖 Windows 10/11 自带的 .NET Framework 4.8

> 非学校官方软件。登录方式与浏览器网页登录完全相同，请遵守学校的校园网使用规定。

---

## 下载与使用

1. 到 [Releases](https://github.com/JonathanLye/AutoCampusLogin/releases) 下载最新的 `CampusLogin.exe`。
2. **把它放到一个固定的文件夹里**，例如 `D:\工具\校园网\`。开机自启会从这个位置启动，之后不要移动或删除它。
   不要在压缩包里直接双击，也不要放在临时文件夹里。
3. 双击运行，在弹出的窗口里填学号和校园网密码，勾选「开机自动运行」，点「保存」。

完成。右下角托盘里会出现一个 Wi-Fi 图标。

### 第一次运行时 Windows 弹出"Windows 已保护你的电脑"？

本程序目前**没有代码签名证书**（个人开发者很难免费拿到），所以 Windows SmartScreen 会提示"未知发布者"。
点 **「更多信息」→「仍要运行」** 即可。只有第一次会这样。

如果不放心，可以先按下面的方法[校验文件](#校验下载的文件)，或者直接[从源码自己编译](#从源码构建)。

## 托盘菜单

| 菜单项 | 作用 |
|---|---|
| 状态：… | 当前状态 |
| 立即检查 | 马上检查一次，没登录就登录 |
| 账号设置… | 修改学号、密码和选项（双击托盘图标也能打开） |
| 开机自动运行 | 在"启动"文件夹里创建或删除快捷方式 |
| 自动登录成功时提示 | 是否弹出"校园网已自动登录"的通知 |
| 打开日志 | 查看运行日志 |
| 开着 Clash 等代理？设置帮助… | 见下文 |
| 清除数据并退出… | 删除开机自启、保存的账号、设置和日志 |

## 安全与隐私

这个程序会处理你的校园网密码，下面把它做什么、不做什么写清楚：

**会做的事**
- 用 Windows 自带的 DPAPI 加密保存学号和密码，文件位于 `%LOCALAPPDATA%\CampusLogin\credential.dat`。
  只有你的 Windows 账户在这台电脑上能解开，文件被拷走也没用。
- 访问 `http://2.2.2.2` 判断是否已登录。没登录时，把账号密码发给学校认证服务器（`172.16.253.121`）。
  这和浏览器网页登录发出的请求**完全一样**。学校系统本身用的就是明文 HTTP，这一点程序无法改变。
- 勾选「开机自动运行」时，在 `shell:startup`（启动文件夹）里放一个快捷方式，你可以随时手动删除。

**不会做的事**
- 不会访问除 `2.2.2.2` 和学校认证服务器以外的任何网址：没有统计，没有遥测，也不会自动检查更新。
- 不会注销你的网络（代码里从不调用下线接口）。
- 不会把密码写进日志。日志里有学号和本机 IP。
- 不会复制自己、不写注册表自启、不需要管理员权限。

核心逻辑都在 [`src/CampusLogin/Portal/SgcPortalAdapter.cs`](src/CampusLogin/Portal/SgcPortalAdapter.cs)，欢迎审查。

## 校验下载的文件

每个 Release 都由 GitHub Actions 从公开源码自动构建，附带 `SHA256SUMS.txt` 和[构建来源证明](https://docs.github.com/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds)。

在 PowerShell 里计算 SHA256，和 Release 页面上的值对比：

```powershell
Get-FileHash .\CampusLogin.exe -Algorithm SHA256
```

装了 [GitHub CLI](https://cli.github.com/) 的话，可以验证这个 exe 确实是由本仓库的公开源码构建的：

```powershell
gh attestation verify .\CampusLogin.exe --repo JonathanLye/AutoCampusLogin
```

## 开着 Clash 等代理时连不上

Clash Verge、v2rayN、sing-box 等软件开着 **TUN 模式**时，会接管电脑上的所有流量。
如果规则里没有把 `2.2.2.2` 设成直连，它会被转发到代理节点，程序就连不上认证服务器。
程序检测到这种情况会弹出提示。也可以点托盘菜单里的「开着 Clash 等代理？设置帮助…」，复制下面的规则：

```
IP-CIDR,2.2.2.2/32,DIRECT,no-resolve
IP-CIDR,172.16.253.121/32,DIRECT,no-resolve
PROCESS-NAME,CampusLogin.exe,DIRECT
```

Clash Verge 的设置方法：「订阅」页 → 右键当前订阅 →「编辑规则」→ 把规则粘贴到**前置规则** → 保存。

## 杀毒软件报毒怎么办

没有签名的新程序有时会被杀毒软件的"行为分析"误判。
本程序刻意避开了容易被误判的行为（不复制自身、不写注册表 Run 键、不做自删除），
在卡巴斯基下实测没有报毒。如果你的杀毒软件仍然拦截：

1. 先[校验文件](#校验下载的文件)，确认是从本仓库下载的原版。
2. 把程序所在的文件夹加入杀毒软件的信任列表或排除项。
3. 欢迎[提 Issue](https://github.com/JonathanLye/AutoCampusLogin/issues)，附上杀毒软件名称、检测名称和 MD5，我们会提交误报申诉。

## 卸载

托盘菜单 →「清除数据并退出…」，然后删除 `CampusLogin.exe` 即可。

也可以手动清理：删除 `%LOCALAPPDATA%\CampusLogin` 文件夹，以及 `shell:startup` 里的"校园网自动登录"快捷方式。

## 常见问题

**能在登录 Windows 之前就认证吗？**
不能。密码是用你的 Windows 账户加密的，要登录 Windows 之后才能解开。登录后约 10 秒内会完成认证。

**Windows 右下角还会弹"登录到网络"的窗口？**
那是 Windows 自带的网络检测。程序认证完成后它就没用了，直接关掉即可。

**密码改了怎么办？**
程序会提示认证失败并暂停自动登录，避免反复用旧密码导致账号被锁。打开「账号设置」填新密码即可。

**支持其他学校吗？**
目前只适配韶关学院的认证系统。代码按"门户适配器"组织（`IPortalAdapter`），欢迎为其他学校贡献适配。

## 命令行

```
CampusLogin.exe --status      只检查是否已登录，不做任何改动
CampusLogin.exe --once        检查一次，未登录就登录一次（需要先在界面里保存过账号）
CampusLogin.exe --uninstall   清除开机自启和保存的账号/设置/日志
CampusLogin.exe --version     显示版本
```

## 从源码构建

需要 [.NET SDK](https://dotnet.microsoft.com/download)（8 或更新版本），以及 Windows 上自带的 .NET Framework 4.8。

```powershell
dotnet test  tests/CampusLogin.Tests/CampusLogin.Tests.csproj -c Release
dotnet build src/CampusLogin/CampusLogin.csproj -c Release
# 产物：src/CampusLogin/bin/Release/net48/CampusLogin.exe（只需要这一个文件）
```

## 项目结构

```
src/CampusLogin/
  Portal/SgcPortalAdapter.cs   认证流程：探测 2.2.2.2 → 取门户配置 → quickauth 登录
  Portal/HttpUtil.cs           直连 HTTP 请求、查询字符串、JSON 读取
  Core/LoginLoop.cs            后台检测循环：退避重试、防锁号、事件触发
  Core/CredentialStore.cs      DPAPI 加密保存账号
  Core/AutoStart.cs            启动文件夹快捷方式；清除数据
  Core/ProxyDiagnostics.cs     Clash / mihomo 等 TUN 网卡检测
  UI/                          托盘、账号设置窗、代理帮助窗
tests/CampusLogin.Tests/       单元测试（测试数据均为虚构）
legacy/                        v0.1 PowerShell 原型
docs/                          抓包指南、杀毒软件兼容性测试步骤
```

## 许可证

[MIT](LICENSE)
