using System;
using System.Threading;
using CampusLogin.Portal;

namespace CampusLogin.Core
{
    internal enum LoopState
    {
        Starting,
        Online,
        LoggingIn,
        /// <summary>连不上认证服务器（不在校园网，或被代理 TUN 拦截）。</summary>
        Unreachable,
        Unknown,
        /// <summary>未登录且还没保存账号。</summary>
        NoCredential,
        /// <summary>账号或密码有问题，已暂停自动登录。</summary>
        AccountProblem,
        /// <summary>认证失败，正在退避重试。</summary>
        LoginError,
    }

    internal sealed class LoopStatus
    {
        public LoopState State { get; set; }
        public string Detail { get; set; }
        public string UserIp { get; set; }
        /// <summary>检测到的代理 TUN 网卡（仅 Unreachable/Unknown 时可能有值）。</summary>
        public string TunAdapter { get; set; }
        public string PortalBase { get; set; }
    }

    internal enum LoopNotice
    {
        LoggedIn,
        AccountProblem,
        RepeatedFailures,
        TunSuspected,
        NeedCredential,
    }

    /// <summary>
    /// 后台检测循环：检查 → 需要就登录 → 按状态决定下次检查时间。
    /// 事件在后台线程触发，UI 需要自行切回界面线程。
    /// </summary>
    internal sealed class LoginLoop : IDisposable
    {
        const int RecheckAfterLoginSeconds = 10;
        const int RepeatedFailureNoticeThreshold = 3;
        // 连续失败这么多次后也暂停：万一密码错误的提示没被识别出来，也不会无限重试导致锁号
        const int MaxConsecutiveFailures = 5;

        readonly IPortalAdapter _portal;
        readonly Func<int> _intervalSeconds;
        readonly object _gate = new object();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        Thread _thread;
        volatile bool _stopping;

        DateTime _nextCheckAt = DateTime.MinValue;
        string _pendingReason = "启动";
        bool _paused;            // 账号出错后暂停自动登录，直到用户改账号、手动检查，或用户自己登录成功
        string _pauseReason;
        int _loginFailures;      // 连续的非账号类失败次数
        int _unreachableStreak;  // 连续连不上的次数
        bool _tunNoticeShown;    // 本次离线期间是否已经提示过 TUN

        public event Action<LoopStatus> StatusChanged;
        public event Action<LoopNotice, string> Notice;

        public LoopStatus Status { get; private set; } = new LoopStatus { State = LoopState.Starting };

        public LoginLoop(IPortalAdapter portal, Func<int> intervalSeconds)
        {
            _portal = portal;
            _intervalSeconds = intervalSeconds;
        }

        public void Start(int initialDelaySeconds = 0)
        {
            lock (_gate) _nextCheckAt = DateTime.UtcNow.AddSeconds(initialDelaySeconds);
            _thread = new Thread(Run) { IsBackground = true, Name = "LoginLoop" };
            _thread.Start();
        }

        /// <summary>请求在 delaySeconds 秒后检查（如果比原计划更早）。</summary>
        public void Trigger(string reason, int delaySeconds = 0, bool resume = false)
        {
            lock (_gate)
            {
                if (resume) _paused = false;
                DateTime at = DateTime.UtcNow.AddSeconds(delaySeconds);
                if (at < _nextCheckAt)
                {
                    _nextCheckAt = at;
                    _pendingReason = reason;
                }
            }
            _wake.Set();
        }

        void Run()
        {
            while (!_stopping)
            {
                string reason = null;
                TimeSpan wait;
                lock (_gate)
                {
                    wait = _nextCheckAt - DateTime.UtcNow;
                    if (wait <= TimeSpan.Zero)
                    {
                        reason = _pendingReason;
                        _pendingReason = "定时检查";
                        _nextCheckAt = DateTime.MaxValue;
                    }
                }

                if (reason == null)
                {
                    _wake.WaitOne(wait > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : wait);
                    continue;
                }

                int delay;
                try
                {
                    delay = CheckOnce(reason);
                }
                catch (Exception ex)
                {
                    Log.Write($"检查出错：{ex.Message}");
                    delay = _intervalSeconds();
                }

                lock (_gate)
                {
                    DateTime at = DateTime.UtcNow.AddSeconds(delay);
                    if (at < _nextCheckAt) _nextCheckAt = at;
                }
            }
        }

        /// <summary>执行一次检查，返回下次检查前要等待的秒数。</summary>
        int CheckOnce(string reason)
        {
            int interval = _intervalSeconds();
            ProbeResult probe = _portal.Probe();

            switch (probe.State)
            {
                case PortalState.Online:
                    _loginFailures = 0;
                    _unreachableStreak = 0;
                    _tunNoticeShown = false;
                    // 在线说明账号能用（比如用户已在浏览器里登录），解除暂停，下次掉线照常自动登录
                    lock (_gate) _paused = false;
                    Publish(new LoopStatus { State = LoopState.Online }, "在线。");
                    return interval;

                case PortalState.Unreachable:
                case PortalState.Unknown:
                {
                    _unreachableStreak++;
                    string tun = ProxyDiagnostics.FindProxyTunAdapter();
                    var state = probe.State == PortalState.Unreachable ? LoopState.Unreachable : LoopState.Unknown;
                    string msg = state == LoopState.Unreachable
                        ? $"认证服务器连不上（不在校园网，或被代理拦截）：{probe.Detail}"
                        : $"无法识别 2.2.2.2 的响应：{probe.Detail}";
                    if (tun != null) msg += $"；检测到代理 TUN 网卡 {tun}";
                    Publish(new LoopStatus { State = state, Detail = probe.Detail, TunAdapter = tun }, msg);
                    if (tun != null && !_tunNoticeShown)
                    {
                        _tunNoticeShown = true;
                        RaiseNotice(LoopNotice.TunSuspected, tun);
                    }
                    // 不在学校时安静地慢慢退避：60s → 120s → 240s → 300s
                    return Backoff(interval, _unreachableStreak, 300);
                }

                case PortalState.NeedLogin:
                    _unreachableStreak = 0;
                    return HandleNeedLogin(probe, reason, interval);

                default:
                    return interval;
            }
        }

        int HandleNeedLogin(ProbeResult probe, string reason, int interval)
        {
            bool paused;
            lock (_gate) paused = _paused;
            if (paused)
            {
                string reasonText;
                lock (_gate) reasonText = _pauseReason;
                Publish(new LoopStatus { State = LoopState.AccountProblem, UserIp = probe.UserIp, Detail = reasonText }, null);
                return interval;
            }

            Credential cred;
            try
            {
                cred = CredentialStore.Load();
            }
            catch (Exception ex)
            {
                Log.Write($"读取保存的账号失败：{ex.Message}");
                cred = null;
            }
            if (cred == null)
            {
                bool first = Status.State != LoopState.NoCredential;
                Publish(new LoopStatus { State = LoopState.NoCredential, UserIp = probe.UserIp }, "未登录，但还没有设置账号。");
                if (first) RaiseNotice(LoopNotice.NeedCredential, null);
                return interval;
            }

            Publish(new LoopStatus { State = LoopState.LoggingIn, UserIp = probe.UserIp, PortalBase = probe.PortalBase },
                $"检测到未登录（IP {probe.UserIp}，触发原因：{reason}），开始认证…");

            LoginResult result;
            try
            {
                result = _portal.Login(probe, cred);
            }
            catch (Exception ex)
            {
                result = new LoginResult { Outcome = LoginOutcome.Failed, Message = ex.Message };
            }
            finally
            {
                cred.Password = null;
            }

            switch (result.Outcome)
            {
                case LoginOutcome.Success:
                    _loginFailures = 0;
                    Log.Write($"认证成功：{result.Message}");
                    RaiseNotice(LoopNotice.LoggedIn, probe.UserIp);
                    return RecheckAfterLoginSeconds;

                case LoginOutcome.AccountProblem:
                    _loginFailures = 0;
                    Pause(result.Message);
                    Publish(new LoopStatus { State = LoopState.AccountProblem, UserIp = probe.UserIp, Detail = result.Message },
                        $"认证失败（code={result.Code}）：{result.Message}。账号可能有问题，已暂停自动登录，等待重新设置账号。");
                    RaiseNotice(LoopNotice.AccountProblem, result.Message);
                    return interval;

                default:
                    _loginFailures++;
                    int wait = Backoff(30, _loginFailures, 600);
                    Publish(new LoopStatus { State = LoopState.LoginError, UserIp = probe.UserIp, Detail = result.Message },
                        $"认证失败（code={result.Code ?? "-"}）：{result.Message}。{wait} 秒后重试。");
                    if (_loginFailures >= MaxConsecutiveFailures)
                    {
                        _loginFailures = 0;
                        string why = $"连续 {MaxConsecutiveFailures} 次认证失败（最后一次：{result.Message}）";
                        Pause(why);
                        Publish(new LoopStatus { State = LoopState.AccountProblem, UserIp = probe.UserIp, Detail = why },
                            why + "。为避免账号被锁，已暂停自动登录。");
                        RaiseNotice(LoopNotice.AccountProblem, why);
                        return interval;
                    }
                    if (_loginFailures == RepeatedFailureNoticeThreshold)
                        RaiseNotice(LoopNotice.RepeatedFailures, result.Message);
                    return wait;
            }
        }

        void Pause(string reason)
        {
            lock (_gate)
            {
                _paused = true;
                _pauseReason = reason;
            }
        }

        /// <summary>指数退避：baseSeconds × 2^(n-1)，不超过 maxSeconds。</summary>
        internal static int Backoff(int baseSeconds, int attempt, int maxSeconds)
        {
            if (attempt <= 1) return Math.Min(baseSeconds, maxSeconds);
            double v = baseSeconds * Math.Pow(2, Math.Min(attempt - 1, 16));
            return (int)Math.Min(v, maxSeconds);
        }

        /// <summary>更新状态；只有状态变化时才写日志，避免离线时日志刷屏。</summary>
        void Publish(LoopStatus status, string logMessage)
        {
            // 连不上时 Detail 是异常原文（超时/拒绝连接会交替出现），不参与比较，否则离线时日志刷屏
            bool detailMatters = status.State == LoopState.AccountProblem || status.State == LoopState.LoginError;
            bool changed = status.State != Status.State
                || status.TunAdapter != Status.TunAdapter
                || (detailMatters && status.Detail != Status.Detail);
            Status = status;
            if (changed && logMessage != null) Log.Write(logMessage);
            if (changed) StatusChanged?.Invoke(status);
        }

        void RaiseNotice(LoopNotice notice, string detail)
        {
            try { Notice?.Invoke(notice, detail); } catch { }
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            _thread?.Join(2000);
            _wake.Dispose();
        }
    }
}
