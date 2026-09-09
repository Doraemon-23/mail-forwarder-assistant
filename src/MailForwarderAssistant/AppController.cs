using System;
using System.Threading;
using System.Threading.Tasks;

namespace MailForwarderAssistant
{
    public sealed class MonitorStateChangedEventArgs : EventArgs
    {
        public MonitorState State { get; set; }
        public string Message { get; set; }
        public bool IsRecovery { get; set; }
    }

    public sealed class RuntimeStatistics
    {
        public DateTime Date { get; set; }
        public int CheckCount { get; set; }
        public int ForwardedCount { get; set; }
        public int FailedCount { get; set; }
    }

    public sealed class AppController : IDisposable
    {
        private readonly DataStore store;
        private readonly MailMonitorService service;
        private readonly object gate = new object();
        private readonly object statisticsGate = new object();
        private CancellationTokenSource stopSource;
        private Task loopTask;
        private bool hadError;
        private DateTime statisticsDate = DateTime.Today;
        private int todayCheckCount;
        private int todayForwardedCount;
        private int todayFailedCount;

        public MonitorState State { get; private set; } = MonitorState.Paused;
        public string LastMessage { get; private set; } = "监测已暂停";
        public bool IsMonitoring
        {
            get
            {
                lock (gate) return loopTask != null && !loopTask.IsCompleted;
            }
        }
        public event EventHandler<MonitorStateChangedEventArgs> StateChanged;
        public event EventHandler<CycleSummary> CycleCompleted;

        public RuntimeStatistics GetRuntimeStatistics()
        {
            lock (statisticsGate)
            {
                ResetStatisticsIfDayChanged();
                return new RuntimeStatistics
                {
                    Date = statisticsDate,
                    CheckCount = todayCheckCount,
                    ForwardedCount = todayForwardedCount,
                    FailedCount = todayFailedCount
                };
            }
        }

        public AppController(DataStore store)
        {
            this.store = store;
            service = new MailMonitorService(store);
        }

        public bool Start()
        {
            lock (gate)
            {
                if (loopTask != null && !loopTask.IsCompleted) return true;
                try
                {
                    var settings = store.GetSettings();
                    MailMonitorService.ValidateSettings(settings);
                    MailMonitorService.ValidateMailboxFeatures(settings, store.GetRules());
                    var ownAddresses = RuleUtilities.GetOwnMailboxAddresses(settings);
                    foreach (var rule in store.GetRules())
                        RuleUtilities.EnsureNoSelfRecipient(rule.Recipients, ownAddresses);
                }
                catch (Exception ex)
                {
                    SetState(MonitorState.Error, UserMessages.ForException(ex), false);
                    return false;
                }
                stopSource = new CancellationTokenSource();
                SetState(MonitorState.Running, "正在监测邮箱", false);
                loopTask = Task.Run(() => LoopAsync(stopSource.Token));
                return true;
            }
        }

        public async Task PauseAsync()
        {
            Task task;
            lock (gate)
            {
                if (stopSource != null) stopSource.Cancel();
                task = loopTask;
            }
            if (task != null)
            {
                try { await task.ConfigureAwait(true); }
                catch (OperationCanceledException) { }
            }
            lock (gate)
            {
                loopTask = null;
                if (stopSource != null) stopSource.Dispose();
                stopSource = null;
            }
            SetState(MonitorState.Paused, "监测已暂停", false);
        }

        public Task<CycleSummary> TestReceiveAsync(AppSettings settings) => service.TestReceiveConnectionAsync(settings).ContinueWith(t =>
        {
            if (t.IsFaulted) throw t.Exception.InnerException;
            return new CycleSummary();
        }, TaskScheduler.Default);

        public Task SendTestAsync(AppSettings settings, string recipient) => service.SendTestMessageAsync(settings, recipient);

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                RecordCheckStarted();
                try
                {
                    var summary = await service.RunCycleAsync().ConfigureAwait(false);
                    RecordSummary(summary);
                    var text = summary.WasBaseline ? "首次准备完成，正在等待新邮件" : $"刚刚检查 {summary.Scanned} 封新邮件，成功转发 {summary.Forwarded} 封，{summary.Failed} 封需要处理";
                    var recovery = hadError && summary.Failed == 0;
                    hadError = summary.Failed > 0;
                    SetState(summary.Failed > 0 ? MonitorState.Error : MonitorState.Running, text, recovery);
                    CycleCompleted?.Invoke(this, summary);
                }
                catch (FatalMonitorException ex)
                {
                    hadError = true;
                    var message = UserMessages.ForException(ex);
                    store.AddLog(LogLevel.Error, "监测已停止", null, "", "", "", "需要处理", message);
                    SetState(MonitorState.Error, message, false);
                    break;
                }
                catch (Exception ex)
                {
                    hadError = true;
                    var message = UserMessages.ForException(ex);
                    store.AddLog(LogLevel.Error, "暂时无法检查邮箱", null, "", "", "", "稍后自动重试", message);
                    SetState(MonitorState.Error, message, false);
                }

                var settings = store.GetSettings();
                try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, settings.PollIntervalMinutes)), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void RecordCheckStarted()
        {
            lock (statisticsGate)
            {
                ResetStatisticsIfDayChanged();
                todayCheckCount++;
            }
        }

        private void RecordSummary(CycleSummary summary)
        {
            lock (statisticsGate)
            {
                ResetStatisticsIfDayChanged();
                todayForwardedCount += summary == null ? 0 : summary.Forwarded;
                todayFailedCount += summary == null ? 0 : summary.Failed;
            }
        }

        private void ResetStatisticsIfDayChanged()
        {
            var today = DateTime.Today;
            if (statisticsDate == today) return;
            statisticsDate = today;
            todayCheckCount = 0;
            todayForwardedCount = 0;
            todayFailedCount = 0;
        }

        private void SetState(MonitorState state, string message, bool recovery)
        {
            State = state;
            LastMessage = message;
            StateChanged?.Invoke(this, new MonitorStateChangedEventArgs { State = state, Message = message, IsRecovery = recovery });
        }

        public void Dispose()
        {
            if (stopSource != null) stopSource.Cancel();
            if (stopSource != null) stopSource.Dispose();
        }
    }
}
