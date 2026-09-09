using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace MailForwarderAssistant
{
    public sealed class DataStore : IDisposable
    {
        private const int CurrentSchemaVersion = 2;
        private readonly object gate = new object();
        private readonly LiteDatabase database;

        public string DataDirectory { get; }

        public DataStore()
        {
            DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            Directory.CreateDirectory(DataDirectory);
            var dbPath = Path.Combine(DataDirectory, "mail-forwarder.db");
            PrepareSchemaBackup(dbPath);
            database = new LiteDatabase("Filename=" + dbPath + ";Connection=shared");
            EnsureIndexes();
            ApplyMigrations();
        }

        private void PrepareSchemaBackup(string dbPath)
        {
            var versionPath = Path.Combine(DataDirectory, "schema.version");
            int version;
            if (!File.Exists(dbPath) || (File.Exists(versionPath) && int.TryParse(File.ReadAllText(versionPath), out version) && version >= CurrentSchemaVersion))
                return;
            var backupDirectory = Path.Combine(DataDirectory, "Backups");
            Directory.CreateDirectory(backupDirectory);
            File.Copy(dbPath, Path.Combine(backupDirectory, "mail-forwarder-pre-upgrade-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db"), false);
        }

        private void ApplyMigrations()
        {
            lock (gate)
            {
                var collection = database.GetCollection<SchemaInfo>("schema");
                var info = collection.FindById(1) ?? new SchemaInfo();
                if (info.Version < CurrentSchemaVersion)
                {
                    info.Version = CurrentSchemaVersion;
                    info.UpdatedUtc = DateTime.UtcNow;
                    collection.Upsert(info);
                }
                File.WriteAllText(Path.Combine(DataDirectory, "schema.version"), CurrentSchemaVersion.ToString());
            }
        }

        private void EnsureIndexes()
        {
            lock (gate)
            {
                database.GetCollection<LogEntry>("logs").EnsureIndex(x => x.CreatedUtc);
                database.GetCollection<LogEntry>("logs").EnsureIndex(x => x.ProcessingId);
                database.GetCollection<ProcessingRecord>("processing").EnsureIndex(x => x.Status);
                database.GetCollection<ProcessingRecord>("processing").EnsureIndex(x => x.RuleId);
                database.GetCollection<ProcessingRecord>("processing").EnsureIndex(x => x.AccountKey);
                database.GetCollection<ProcessingRecord>("processing").EnsureIndex(x => x.MailStableId);
            }
        }

        public AppSettings GetSettings()
        {
            lock (gate) return database.GetCollection<AppSettings>("settings").FindById(1) ?? new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            settings.Id = 1;
            lock (gate) database.GetCollection<AppSettings>("settings").Upsert(settings);
        }

        public List<ForwardRule> GetRules()
        {
            lock (gate) return database.GetCollection<ForwardRule>("rules").FindAll().OrderBy(x => x.Name).ToList();
        }

        public ForwardRule GetRule(string id)
        {
            lock (gate) return database.GetCollection<ForwardRule>("rules").FindById(id);
        }

        public void SaveRule(ForwardRule rule)
        {
            rule.UpdatedUtc = DateTime.UtcNow;
            lock (gate) database.GetCollection<ForwardRule>("rules").Upsert(rule);
        }

        public void DeleteRule(string id)
        {
            lock (gate) database.GetCollection<ForwardRule>("rules").Delete(id);
        }

        public MailboxCursor GetCursor()
        {
            lock (gate) return database.GetCollection<MailboxCursor>("cursor").FindById(1) ?? new MailboxCursor();
        }

        public void SaveCursor(MailboxCursor cursor)
        {
            cursor.Id = 1;
            lock (gate) database.GetCollection<MailboxCursor>("cursor").Upsert(cursor);
        }

        public void ResetCursorForAccount(string accountKey)
        {
            lock (gate)
            {
                database.GetCollection<MailboxCursor>("cursor").Upsert(new MailboxCursor { AccountKey = accountKey });
            }
        }

        public bool HasInboxMarker(string id)
        {
            lock (gate) return database.GetCollection<InboxMarker>("inbox_markers").Exists(Query.EQ("_id", id));
        }

        public void AddInboxMarker(string id)
        {
            lock (gate) database.GetCollection<InboxMarker>("inbox_markers").Upsert(new InboxMarker { Id = id, SeenUtc = DateTime.UtcNow });
        }

        public ProcessingRecord GetProcessing(string id)
        {
            lock (gate) return database.GetCollection<ProcessingRecord>("processing").FindById(id);
        }

        public void SaveProcessing(ProcessingRecord record)
        {
            record.UpdatedUtc = DateTime.UtcNow;
            lock (gate) database.GetCollection<ProcessingRecord>("processing").Upsert(record);
        }

        public List<ProcessingRecord> GetProcessingForMessage(string accountKey, string stableId)
        {
            lock (gate)
            {
                return database.GetCollection<ProcessingRecord>("processing")
                    .Find(x => x.AccountKey == accountKey && x.MailStableId == stableId)
                    .ToList();
            }
        }

        public List<string> GetMessagesReadyForReadMark(string accountKey)
        {
            lock (gate)
            {
                return database.GetCollection<ProcessingRecord>("processing")
                    .Find(x => x.AccountKey == accountKey && x.Protocol == MailProtocol.Imap && !x.ReadMarked)
                    .ToList()
                    .GroupBy(x => x.MailStableId)
                    .Where(ProcessingPolicy.ShouldMarkRead)
                    .Select(group => group.Key)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(100)
                    .ToList();
            }
        }

        public bool HasPendingReadMark(string accountKey)
        {
            lock (gate)
            {
                return database.GetCollection<ProcessingRecord>("processing")
                    .Exists(x => x.AccountKey == accountKey && x.Protocol == MailProtocol.Imap && x.MarkReadRequested && !x.ReadMarked);
            }
        }

        public void MarkMessageReadCompleted(string accountKey, string stableId)
        {
            lock (gate)
            {
                var collection = database.GetCollection<ProcessingRecord>("processing");
                foreach (var item in collection.Find(x => x.AccountKey == accountKey && x.MailStableId == stableId).ToList())
                {
                    item.ReadMarked = true;
                    item.UpdatedUtc = DateTime.UtcNow;
                    collection.Update(item);
                }
            }
        }

        public List<ProcessingRecord> GetRetryable(string accountKey, MailProtocol protocol)
        {
            lock (gate)
            {
                var now = DateTime.UtcNow;
                return database.GetCollection<ProcessingRecord>("processing")
                    .Find(x => x.AccountKey == accountKey && x.Protocol == protocol && x.Status == ProcessingStatus.RetryableFailure)
                    .Where(x => !x.NextAttemptUtc.HasValue || x.NextAttemptUtc.Value <= now)
                    .OrderBy(x => x.UpdatedUtc).Take(100).ToList();
            }
        }

        public void RecoverInterruptedSends()
        {
            lock (gate)
            {
                var collection = database.GetCollection<ProcessingRecord>("processing");
                foreach (var item in collection.Find(x => x.Status == ProcessingStatus.Sending).ToList())
                {
                    item.Status = ProcessingStatus.PendingConfirmation;
                    item.LastError = "程序上次在发送过程中关闭，无法确认这封邮件是否已经发出。请在日志中人工确认。";
                    item.UpdatedUtc = DateTime.UtcNow;
                    collection.Update(item);
                    AddLogUnsafe(LogLevel.Warning, "请确认发送结果", item.RuleId, "", item.Subject, item.Sender,
                        item.Recipients, "无法确认是否已发送", item.LastError, item.Id);
                }
            }
        }

        public void AddLog(LogLevel level, string eventType, ForwardRule rule, string subject, string sender,
            string recipients, string result, string detail, string processingId = "")
        {
            lock (gate) AddLogUnsafe(level, eventType, rule == null ? "" : rule.Id, rule == null ? "" : rule.Name,
                subject, sender, recipients, result, detail, processingId);
        }

        private void AddLogUnsafe(LogLevel level, string eventType, string ruleId, string ruleName, string subject,
            string sender, string recipients, string result, string detail, string processingId)
        {
            database.GetCollection<LogEntry>("logs").Insert(new LogEntry
            {
                CreatedUtc = DateTime.UtcNow,
                Level = level,
                EventType = Safe(eventType, 100),
                RuleId = ruleId ?? "",
                RuleName = Safe(ruleName, 200),
                Subject = Safe(subject, 500),
                Sender = Safe(sender, 500),
                Recipients = Safe(recipients, 1000),
                Result = Safe(result, 200),
                Detail = Safe(detail, 3000),
                ProcessingId = processingId ?? ""
            });
        }

        public List<LogEntry> QueryLogs(LogFilter filter, int limit = 1000)
        {
            lock (gate)
            {
                IEnumerable<LogEntry> query = database.GetCollection<LogEntry>("logs").FindAll();
                if (filter != null)
                {
                    if (filter.FromLocal.HasValue)
                    {
                        var utc = filter.FromLocal.Value.ToUniversalTime();
                        query = query.Where(x => x.CreatedUtc >= utc);
                    }
                    if (filter.ToLocal.HasValue)
                    {
                        var utc = filter.ToLocal.Value.ToUniversalTime();
                        query = query.Where(x => x.CreatedUtc <= utc);
                    }
                    if (!string.IsNullOrWhiteSpace(filter.RuleText))
                        query = query.Where(x => Contains(x.RuleName, filter.RuleText));
                    if (!string.IsNullOrWhiteSpace(filter.StatusText))
                        query = query.Where(x => Contains(x.Result, filter.StatusText) || Contains(x.EventType, filter.StatusText));
                    if (!string.IsNullOrWhiteSpace(filter.SearchText))
                        query = query.Where(x => Contains(x.Subject, filter.SearchText) || Contains(x.Sender, filter.SearchText));
                }
                return query.OrderByDescending(x => x.CreatedUtc).Take(limit).ToList();
            }
        }

        public LogEntry GetLog(int id)
        {
            lock (gate) return database.GetCollection<LogEntry>("logs").FindById(id);
        }

        public bool SetProcessingStatus(string id, ProcessingStatus status)
        {
            lock (gate)
            {
                var collection = database.GetCollection<ProcessingRecord>("processing");
                var item = collection.FindById(id);
                if (item == null) return false;
                item.Status = status;
                item.UpdatedUtc = DateTime.UtcNow;
                if (status == ProcessingStatus.RetryableFailure) item.LastError = "用户已确认重新发送。";
                item.NextAttemptUtc = status == ProcessingStatus.RetryableFailure ? DateTime.UtcNow : (DateTime?)null;
                return collection.Update(item);
            }
        }

        public void CleanupOldLogs(int days)
        {
            lock (gate)
            {
                var threshold = DateTime.UtcNow.AddDays(-Math.Abs(days));
                database.GetCollection<LogEntry>("logs").DeleteMany(x => x.CreatedUtc < threshold);
            }
        }

        private static bool Contains(string source, string value)
        {
            return (source ?? "").IndexOf(value ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Safe(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\0", "");
            return value.Length <= max ? value : value.Substring(0, max);
        }

        public void Dispose()
        {
            database.Dispose();
        }
    }
}
