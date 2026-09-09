using System;
using System.Collections.Generic;
using LiteDB;

namespace MailForwarderAssistant
{
    public enum MailProtocol { Imap, Pop3 }
    public enum SocketSecurityMode { None, Auto, SslOnConnect, StartTls }
    public enum MonitorState { Paused, Running, Error }
    public enum ProcessingStatus { Sending, Success, RetryableFailure, PendingConfirmation, Failed, SkippedRead }
    public enum LogLevel { Information, Warning, Error }

    public sealed class AppSettings
    {
        [BsonId] public int Id { get; set; } = 1;
        public MailProtocol ReceiveProtocol { get; set; } = MailProtocol.Imap;
        public string ReceiveHost { get; set; } = "";
        public int ReceivePort { get; set; } = 993;
        public SocketSecurityMode ReceiveSecurity { get; set; } = SocketSecurityMode.SslOnConnect;
        public string ReceiveUserName { get; set; } = "";
        public string ReceivePasswordEncrypted { get; set; } = "";
        public string ImapFolder { get; set; } = "INBOX";
        public bool ReceiveAllowInvalidCertificate { get; set; }
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 25;
        public SocketSecurityMode SmtpSecurity { get; set; } = SocketSecurityMode.None;
        public bool SmtpAuthenticate { get; set; } = true;
        public bool SmtpReuseReceiveCredentials { get; set; } = true;
        public string SmtpUserName { get; set; } = "";
        public string SmtpPasswordEncrypted { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string SenderAddress { get; set; } = "";
        public bool SmtpAllowInvalidCertificate { get; set; }
        public int PollIntervalMinutes { get; set; } = 5;
        public bool StartWithWindows { get; set; }
        public bool AutoStartMonitoring { get; set; } = true;
        public bool NotifySuccess { get; set; }
        public bool NotifyWarning { get; set; }
        public bool NotifyError { get; set; } = true;
        public bool NotifyRecovery { get; set; } = true;
        public bool UseTraditionalChinese { get; set; }
        public bool MarkReadAfterAllRulesCompleted { get; set; }

        [BsonIgnore] public string ReceivePassword
        {
            get { return SecretProtector.Unprotect(ReceivePasswordEncrypted); }
            set { ReceivePasswordEncrypted = SecretProtector.Protect(value); }
        }

        [BsonIgnore] public string SmtpPassword
        {
            get { return SecretProtector.Unprotect(SmtpPasswordEncrypted); }
            set { SmtpPasswordEncrypted = SecretProtector.Protect(value); }
        }
    }

    public sealed class ForwardRule
    {
        [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新规则";
        public bool Enabled { get; set; } = true;
        public bool NeedsBaseline { get; set; } = true;
        public bool OnlyUnread { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
        public string SenderAddress { get; set; } = "";
        public string SubjectPrefix { get; set; } = "Fw:";
        public string IntroText { get; set; } = "";
        public string SignatureText { get; set; } = "";
        public List<string> Recipients { get; set; } = new List<string>();
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class MailboxCursor
    {
        [BsonId] public int Id { get; set; } = 1;
        public string AccountKey { get; set; } = "";
        public bool Initialized { get; set; }
        public uint ImapUidValidity { get; set; }
        public uint LastImapUid { get; set; }
    }

    public sealed class InboxMarker
    {
        [BsonId] public string Id { get; set; }
        public DateTime SeenUtc { get; set; }
    }

    public sealed class ProcessingRecord
    {
        [BsonId] public string Id { get; set; }
        public string AccountKey { get; set; }
        public string RuleId { get; set; }
        public string MailStableId { get; set; }
        public MailProtocol Protocol { get; set; }
        public ProcessingStatus Status { get; set; }
        public int Attempts { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? NextAttemptUtc { get; set; }
        public string Subject { get; set; }
        public string Sender { get; set; }
        public string Recipients { get; set; }
        public string LastError { get; set; }
        public bool MarkReadRequested { get; set; }
        public bool ReadMarked { get; set; }
    }

    public sealed class SchemaInfo
    {
        [BsonId] public int Id { get; set; } = 1;
        public int Version { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class LogEntry
    {
        [BsonId] public int Id { get; set; }
        public DateTime CreatedUtc { get; set; }
        public LogLevel Level { get; set; }
        public string EventType { get; set; }
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string Subject { get; set; }
        public string Sender { get; set; }
        public string Recipients { get; set; }
        public string Result { get; set; }
        public string Detail { get; set; }
        public string ProcessingId { get; set; }
    }

    public sealed class LogFilter
    {
        public DateTime? FromLocal { get; set; }
        public DateTime? ToLocal { get; set; }
        public string RuleText { get; set; }
        public string StatusText { get; set; }
        public string SearchText { get; set; }
    }
}
