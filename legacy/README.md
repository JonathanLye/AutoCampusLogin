> **这是 v0.1 的 PowerShell 原型，已经被 [CampusLogin.exe](../README.md) 取代**，留在这里只作参考和备用。

# campus-login.ps1 —— 测试步骤（v0.1，断网也能看）

这一版是**手动运行**的版本，用来确认能自动登录上。开机自启等下一版再加。

下面的命令都在 **PowerShell** 里运行（开始菜单搜 "PowerShell"），并且要先 `cd` 到脚本所在的文件夹。

---

## 1. 保存账号（只需要做一次，在线时做）

```powershell
powershell -ExecutionPolicy Bypass -File .\campus-login.ps1 -Setup
```

按提示输入学号和密码。输入密码时屏幕上不会显示字符，这是正常的。
账号会加密保存到 `%LOCALAPPDATA%\CampusLogin\credential.xml`，只有你这个 Windows 用户能解开。

## 2. 检查状态（随时可以运行，不会改变任何东西）

```powershell
powershell -ExecutionPolicy Bypass -File .\campus-login.ps1 -Status
```

现在应该显示 `状态：已登录`。

## 3. 真正测试一次自动登录

1. 浏览器打开 `http://2.2.2.2`，点 **注销**。这时会断网。
2. 运行状态检查，确认显示 `状态：未登录`：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\campus-login.ps1 -Status
   ```
3. 运行一次登录：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\campus-login.ps1 -Once
   ```
   成功的话会看到 `认证成功`，网络随之恢复。

## 4. 试试常驻模式（可选）

```powershell
powershell -ExecutionPolicy Bypass -File .\campus-login.ps1
```

脚本会每 60 秒检查一次，掉线就自动重登。按 **Ctrl+C** 退出。

---

## 出问题时

| 现象 | 说明 / 怎么办 |
|---|---|
| `认证失败（code=…）：…` | 把这一行原样反馈给开发者。最常见的原因是密码输错了，重新运行 `-Setup` 即可 |
| `认证服务器连不上` | 检查有没有连上校园网；Clash 开着的话，确认 2.2.2.2 的直连规则还在 |
| `无法识别 2.2.2.2 的响应` | 把这一行反馈给开发者 |
| 脚本没登上，急着上网 | 直接用浏览器打开 `http://2.2.2.2` 手动登录，不受任何影响 |

运行日志在 `%LOCALAPPDATA%\CampusLogin\campus-login.log`。**日志里没有密码**，可以直接发给开发者。

## 安全说明

- 脚本**永远不会**调用下线接口（portalLogout / quickauthdisconn）。
- 密码只会发给学校的认证服务器 172.16.253.121。这是学校系统本身的方式：明文 HTTP，跟浏览器登录完全一样。
- 抓包分析已经做完了。**桌面上的 `172.16.253.121.har` 里有明文密码，请删掉。**
