<#
  韶关学院校园网自动登录（v0.1）

  用法：
    .\campus-login.ps1 -Setup     # 首次使用：保存学号和密码（DPAPI 加密，只有当前 Windows 用户能解开）
    .\campus-login.ps1 -Status    # 只检查是否已登录，不做任何登录/下线操作
    .\campus-login.ps1 -Once      # 检查一次，未登录就登录一次
    .\campus-login.ps1            # 常驻：每 60 秒检查一次，掉线自动重登

  认证流程（抓包分析得出）：
    1. GET http://2.2.2.2/
         已登录 -> 302 到 portalLogout.do?...
         未登录 -> 200，页面里 location.replace("...portalScript.do?wlanuserip=...&mac=...")
    2. GET /PortalJsonAction.do?<上一步的参数>&viewStatus=1  -> 取 serverip / id / uuid / timestamp
    3. GET /quickauth.do?userid=..&passwd=..&...              -> {"code":"0","message":"认证成功"}

  安全约定：本脚本永远不会请求 portalLogout.do / quickauthdisconn.do（下线接口），
  日志里也不会写入密码。
#>
[CmdletBinding()]
param(
    [switch]$Setup,
    [switch]$Status,
    [switch]$Once,
    [int]$Interval = 60
)

$ErrorActionPreference = 'Stop'

$PortalProbe = 'http://2.2.2.2/'
$PortalHost  = 'http://172.16.253.121'
$UserAgent   = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36'

$DataDir  = Join-Path $env:LOCALAPPDATA 'CampusLogin'
$CredFile = Join-Path $DataDir 'credential.xml'
$LogFile  = Join-Path $DataDir 'campus-login.log'
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

function Write-Log([string]$Message) {
    $line = '{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    try {
        if ((Test-Path $LogFile) -and (Get-Item $LogFile).Length -gt 1MB) {
            Move-Item -Force $LogFile "$LogFile.old"
        }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
    } catch { }
}

# 直连 HTTP GET：不走系统代理、不自动跟随跳转，返回 状态码 / Location / 正文
function Invoke-Get([string]$Url, [string]$Referer, [switch]$Ajax) {
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'GET'
    $req.Proxy = $null
    $req.AllowAutoRedirect = $false
    $req.Timeout = 8000
    $req.ReadWriteTimeout = 8000
    $req.UserAgent = $UserAgent
    $req.Headers['Cache-Control'] = 'no-cache'
    if ($Referer) { $req.Referer = $Referer }
    if ($Ajax) {
        $req.Accept = 'application/json, text/javascript, */*; q=0.01'
        $req.Headers['X-Requested-With'] = 'XMLHttpRequest'
    }
    try {
        $resp = $req.GetResponse()
    } catch [System.Net.WebException] {
        if ($_.Exception.Response) { $resp = $_.Exception.Response } else { throw }
    }
    try {
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
        [pscustomobject]@{
            Status   = [int]$resp.StatusCode
            Location = $resp.Headers['Location']
            Body     = $reader.ReadToEnd()
        }
    } finally {
        $resp.Close()
    }
}

function ConvertFrom-QueryString([string]$Query) {
    $result = [ordered]@{}
    foreach ($pair in ($Query.TrimStart('?') -split '&')) {
        if (-not $pair) { continue }
        $k, $v = $pair -split '=', 2
        $result[[uri]::UnescapeDataString($k)] = [uri]::UnescapeDataString([string]$v)
    }
    $result
}

function ConvertTo-QueryString($Params) {
    ($Params.GetEnumerator() | ForEach-Object {
        '{0}={1}' -f [uri]::EscapeDataString($_.Key), [uri]::EscapeDataString([string]$_.Value)
    }) -join '&'
}

# 返回 @{ State = 'Online' | 'NeedLogin' | 'Unreachable' | 'Unknown'; Params = 门户参数 }
function Get-PortalState {
    try {
        $r = Invoke-Get $PortalProbe
    } catch {
        return @{ State = 'Unreachable'; Detail = $_.Exception.Message }
    }
    if ($r.Status -in 301, 302, 303, 307 -and $r.Location -match 'portalLogout\.do') {
        return @{ State = 'Online' }
    }
    $m = [regex]::Match($r.Body, 'portalScript\.do\?([^"''\s+]+)')
    if ($m.Success) {
        $params = ConvertFrom-QueryString $m.Groups[1].Value
        $params['url'] = 'http://2.2.2.2'
        return @{ State = 'NeedLogin'; Params = $params }
    }
    return @{ State = 'Unknown'; Detail = "HTTP $($r.Status) $($r.Location)" }
}

function Get-SavedCredential {
    if (-not (Test-Path $CredFile)) {
        throw "还没有保存账号，请先运行：.\campus-login.ps1 -Setup"
    }
    Import-Clixml $CredFile
}

function Invoke-PortalLogin($PortalParams) {
    $cred = Get-SavedCredential
    $baseQuery = ConvertTo-QueryString $PortalParams
    $referer = "$PortalHost/portal.do?$baseQuery"

    # 第 2 步：取门户配置
    $cfgResp = Invoke-Get "$PortalHost/PortalJsonAction.do?$baseQuery&viewStatus=1" -Referer $referer -Ajax
    $cfg = $cfgResp.Body | ConvertFrom-Json
    if ($cfg.authByRas) {
        throw '门户开启了 RSA 加密登录（authByRas=true），v0.1 还不支持，需要更新脚本。'
    }

    # 第 3 步：认证
    $login = [ordered]@{
        userid           = $cred.UserName
        passwd           = $cred.GetNetworkCredential().Password
        wlanuserip       = $PortalParams['wlanuserip']
        wlanacname       = $PortalParams['wlanacname']
        wlanacIp         = $cfg.serverForm.serverip
        ssid             = ''
        vlan             = $PortalParams['vlan']
        mac              = $PortalParams['mac']
        version          = $cfg.serverForm.portalVer
        portalpageid     = $cfg.portalconfig.id
        timestamp        = $cfg.portalconfig.timestamp
        uuid             = $cfg.portalconfig.uuid
        portaltype       = '0'
        hostname         = $PortalParams['hostname']
        bindCtrlId       = ''
        validateType     = '0'
        bindOperatorType = '2'
        sendFttrNotice   = '0'
    }
    $authResp = Invoke-Get "$PortalHost/quickauth.do?$(ConvertTo-QueryString $login)" -Referer $referer -Ajax
    $login = $null
    $result = $authResp.Body | ConvertFrom-Json
    [pscustomobject]@{ Success = ($result.code -eq '0'); Code = $result.code; Message = $result.message }
}

function Invoke-Check {
    $s = Get-PortalState
    switch ($s.State) {
        'Online'      { return 'Online' }
        'Unreachable' { Write-Log "认证服务器连不上（未连校园网或网线没插？）：$($s.Detail)"; return 'Unreachable' }
        'Unknown'     { Write-Log "无法识别 2.2.2.2 的响应：$($s.Detail)"; return 'Unknown' }
        'NeedLogin' {
            Write-Log "检测到未登录（IP $($s.Params['wlanuserip'])），开始认证…"
            try {
                $r = Invoke-PortalLogin $s.Params
                if ($r.Success) { Write-Log "认证成功：$($r.Message)" }
                else            { Write-Log "认证失败（code=$($r.Code)）：$($r.Message)" }
            } catch {
                Write-Log "认证出错：$($_.Exception.Message)"
            }
            return 'LoginAttempted'
        }
    }
}

# ---------- 入口 ----------

if ($Setup) {
    $user = Read-Host '学号（登录名）'
    $pass = Read-Host '校园网密码' -AsSecureString
    New-Object System.Management.Automation.PSCredential($user.Trim(), $pass) | Export-Clixml -Path $CredFile
    Write-Host "已加密保存到 $CredFile（只有当前 Windows 用户能解密）"
    return
}

if ($Status) {
    $s = Get-PortalState
    switch ($s.State) {
        'Online'      { Write-Host '状态：已登录' }
        'NeedLogin'   { Write-Host "状态：未登录（IP $($s.Params['wlanuserip'])）" }
        'Unreachable' { Write-Host "状态：认证服务器不可达 —— $($s.Detail)" }
        default       { Write-Host "状态：无法识别 —— $($s.Detail)" }
    }
    return
}

if ($Once) {
    $state = Invoke-Check
    if ($state -eq 'Online') { Write-Log '已登录，无需操作。' }
    return
}

Write-Log "常驻模式启动，每 $Interval 秒检查一次。"
$lastState = ''
while ($true) {
    try {
        $state = Invoke-Check
        if ($state -eq 'Online' -and $lastState -ne 'Online') { Write-Log '在线。' }
        $lastState = $state
        # 刚尝试过登录就 10 秒后复查，否则按正常间隔
        Start-Sleep -Seconds $(if ($state -eq 'LoginAttempted') { 10 } else { $Interval })
    } catch {
        Write-Log "检查出错：$($_.Exception.Message)"
        Start-Sleep -Seconds $Interval
    }
}
