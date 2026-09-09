using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace MailForwarderAssistant
{
    public sealed class FatalMonitorException : Exception
    {
        public FatalMonitorException(string message) : base(message) { }
    }

    public sealed class CycleSummary
    {
        public int Scanned { get; set; }
        public int Forwarded { get; set; }
        public int Failed { get; set; }
        public bool WasBaseline { get; set; }
    }

    public sealed class MailMonitorService
    {
        private readonly DataStore store;

        public MailMonitorService(DataStore store)
        {
            this.store = store;
        }

        public async Task<CycleSummary> RunCycleAsync()
        {
            var settings = store.GetSettings();
            ValidateSettings(settings);
            ValidateMailboxFeatures(settings, store.GetRules());
            var accountKey = BuildAccountKey(settings);
            var cursor = store.GetCursor();
            if (!string.Equals(cursor.AccountKey, accountKey, StringComparison.Ordinal))
            {
                cursor = new MailboxCursor { AccountKey = accountKey };
                store.SaveCursor(cursor);
            }

            return settings.ReceiveProtocol == MailProtocol.Imap
                ? await RunImapAsync(settings, cursor, accountKey).ConfigureAwait(false)
                : await RunPop3Async(settings, cursor, accountKey).ConfigureAwait(false);
        }

        public async Task TestReceiveConnectionAsync(AppSettings settings)
        {
            ValidateReceiveSettings(settings);
            if (settings.ReceiveProtocol == MailProtocol.Imap)
            {
                using (var client = new ImapClient())
                {
                    ConfigureCertificate(client, settings.ReceiveAllowInvalidCertificate);
                    await client.ConnectAsync(settings.ReceiveHost, settings.ReceivePort, ToSecureSocketOptions(settings.ReceiveSecurity)).ConfigureAwait(false);
                    await client.AuthenticateAsync(settings.ReceiveUserName, settings.ReceivePassword).ConfigureAwait(false);
                    var folder = string.Equals(settings.ImapFolder, "INBOX", StringComparison.OrdinalIgnoreCase)
                        ? client.Inbox : await client.GetFolderAsync(settings.ImapFolder).ConfigureAwait(false);
                    await folder.OpenAsync(settings.MarkReadAfterAllRulesCompleted ? FolderAccess.ReadWrite : FolderAccess.ReadOnly).ConfigureAwait(false);
                    await folder.CloseAsync().ConfigureAwait(false);
                    await client.DisconnectAsync(true).ConfigureAwait(false);
                }
            }
            else
            {
                using (var client = new Pop3Client())
                {
                    ConfigureCertificate(client, settings.ReceiveAllowInvalidCertificate);
                    await client.ConnectAsync(settings.ReceiveHost, settings.ReceivePort, ToSecureSocketOptions(settings.ReceiveSecurity)).ConfigureAwait(false);
                    await client.AuthenticateAsync(settings.ReceiveUserName, settings.ReceivePassword).ConfigureAwait(false);
                    if (!client.SupportsUids) throw new FatalMonitorException("当前 POP3 收信方式无法取得邮件唯一编号，因此不能可靠避免重复转发。请在“邮箱设置”中改用 IMAP，或联系邮箱管理员确认 POP3 的 UIDL 功能。 ");
                    await client.DisconnectAsync(true).ConfigureAwait(false);
                }
            }
        }

        public async Task SendTestMessageAsync(AppSettings settings, string recipient)
        {
            ValidateSmtpSettings(settings);
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.SenderName ?? "", settings.SenderAddress));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = "邮件转发助手测试邮件";
            message.Body = new TextPart("plain") { Text = "这是一封连接测试邮件。收到此邮件表示发信设置可以正常使用。" };
            await SendAsync(message, settings).ConfigureAwait(false);
        }

        private async Task<CycleSummary> RunImapAsync(AppSettings settings, MailboxCursor cursor, string accountKey)
        {
            var summary = new CycleSummary();
            using (var client = new ImapClient())
            {
                ConfigureCertificate(client, settings.ReceiveAllowInvalidCertificate);
                await client.ConnectAsync(settings.ReceiveHost, settings.ReceivePort, ToSecureSocketOptions(settings.ReceiveSecurity)).ConfigureAwait(false);
                await client.AuthenticateAsync(settings.ReceiveUserName, settings.ReceivePassword).ConfigureAwait(false);
                var folder = string.Equals(settings.ImapFolder, "INBOX", StringComparison.OrdinalIgnoreCase)
                    ? client.Inbox : await client.GetFolderAsync(settings.ImapFolder).ConfigureAwait(false);
                var needsWriteAccess = settings.MarkReadAfterAllRulesCompleted || store.HasPendingReadMark(accountKey);
                await folder.OpenAsync(needsWriteAccess ? FolderAccess.ReadWrite : FolderAccess.ReadOnly).ConfigureAwait(false);
                var allUids = await folder.SearchAsync(SearchQuery.All).ConfigureAwait(false);
                var maxUid = allUids.Count == 0 ? 0U : allUids.Max(x => x.Id);

                if (!cursor.Initialized)
                {
                    cursor.Initialized = true;
                    cursor.ImapUidValidity = folder.UidValidity;
                    cursor.LastImapUid = maxUid;
                    store.SaveCursor(cursor);
                    FinishBaselines();
                    summary.WasBaseline = true;
                    store.AddLog(LogLevel.Information, "首次监测准备", null, "", "", "", "准备完成",
                        "已记住收件箱中现有邮件。现有邮件不会转发，之后收到的新邮件将按规则处理。", "");
                    await client.DisconnectAsync(true).ConfigureAwait(false);
                    return summary;
                }

                if (cursor.ImapUidValidity != folder.UidValidity)
                    throw new FatalMonitorException("邮箱服务器重新编排了邮件编号。为避免旧邮件被再次转发，程序已停止监测。请在“邮箱设置”中点击“重新确定新邮件起点”，然后重新启动监测。 ");

                await RetryImapAsync(folder, settings, accountKey, summary).ConfigureAwait(false);
                await ApplyPendingReadMarksAsync(folder, accountKey, summary).ConfigureAwait(false);
                var readyRules = store.GetRules().Where(x => x.Enabled && !x.NeedsBaseline).ToList();
                var newUids = allUids.Where(x => x.Id > cursor.LastImapUid).OrderBy(x => x.Id).ToList();
                var summaries = newUids.Count == 0
                    ? new List<IMessageSummary>()
                    : (await folder.FetchAsync(newUids, MessageSummaryItems.UniqueId | MessageSummaryItems.Flags).ConfigureAwait(false)).ToList();
                var unreadByUid = summaries.ToDictionary(x => x.UniqueId,
                    x => !x.Flags.GetValueOrDefault().HasFlag(MessageFlags.Seen));
                foreach (var uid in newUids)
                {
                    var message = await folder.GetMessageAsync(uid).ConfigureAwait(false);
                    summary.Scanned++;
                    await ProcessMessageAsync(message, "imap:" + folder.UidValidity + ":" + uid.Id,
                        MailProtocol.Imap, accountKey, readyRules, settings, summary,
                        unreadByUid.ContainsKey(uid) ? (bool?)unreadByUid[uid] : null).ConfigureAwait(false);
                    await TryMarkMessageReadAsync(folder, accountKey, "imap:" + folder.UidValidity + ":" + uid.Id, summary).ConfigureAwait(false);
                    cursor.LastImapUid = uid.Id;
                    store.SaveCursor(cursor);
                }
                FinishBaselines();
                await client.DisconnectAsync(true).ConfigureAwait(false);
            }
            return summary;
        }

        private async Task RetryImapAsync(IMailFolder folder, AppSettings settings, string accountKey, CycleSummary summary)
        {
            foreach (var record in store.GetRetryable(accountKey, MailProtocol.Imap))
            {
                uint uidValue;
                if (!TryParseLastUInt(record.MailStableId, out uidValue))
                {
                    MarkUnavailable(record, "无法找到这封待重试邮件在服务器中的位置，因此不能自动重试。请人工确认邮件是否需要再次发送。 ");
                    continue;
                }
                var rule = store.GetRule(record.RuleId);
                if (rule == null || !rule.Enabled) continue;
                try
                {
                    var message = await folder.GetMessageAsync(new UniqueId(uidValue)).ConfigureAwait(false);
                    await SendForRuleAsync(message, record.MailStableId, MailProtocol.Imap, accountKey, rule, settings, summary, record).ConfigureAwait(false);
                }
                catch (MessageNotFoundException)
                {
                    MarkUnavailable(record, "原邮件已从收件箱中删除或移动，程序无法再次读取。请人工确认是否需要重新发送。 ");
                }
            }
        }

        private async Task<CycleSummary> RunPop3Async(AppSettings settings, MailboxCursor cursor, string accountKey)
        {
            var summary = new CycleSummary();
            using (var client = new Pop3Client())
            {
                ConfigureCertificate(client, settings.ReceiveAllowInvalidCertificate);
                await client.ConnectAsync(settings.ReceiveHost, settings.ReceivePort, ToSecureSocketOptions(settings.ReceiveSecurity)).ConfigureAwait(false);
                await client.AuthenticateAsync(settings.ReceiveUserName, settings.ReceivePassword).ConfigureAwait(false);
                if (!client.SupportsUids) throw new FatalMonitorException("当前 POP3 收信方式无法取得邮件唯一编号，因此不能可靠避免重复转发。请在“邮箱设置”中改用 IMAP，或联系邮箱管理员确认 POP3 的 UIDL 功能。 ");
                var uids = await client.GetMessageUidsAsync().ConfigureAwait(false);

                if (!cursor.Initialized)
                {
                    foreach (var uid in uids) store.AddInboxMarker(PopMarker(accountKey, uid));
                    cursor.Initialized = true;
                    store.SaveCursor(cursor);
                    FinishBaselines();
                    summary.WasBaseline = true;
                    store.AddLog(LogLevel.Information, "首次监测准备", null, "", "", "", "准备完成",
                        "已记住收件箱中现有邮件。现有邮件不会转发，之后收到的新邮件将按规则处理。", "");
                    await client.DisconnectAsync(true).ConfigureAwait(false);
                    return summary;
                }

                await RetryPop3Async(client, uids, settings, accountKey, summary).ConfigureAwait(false);
                var readyRules = store.GetRules().Where(x => x.Enabled && !x.NeedsBaseline).ToList();
                for (var index = 0; index < uids.Count; index++)
                {
                    var marker = PopMarker(accountKey, uids[index]);
                    if (store.HasInboxMarker(marker)) continue;
                    var message = await client.GetMessageAsync(index).ConfigureAwait(false);
                    summary.Scanned++;
                    await ProcessMessageAsync(message, "pop:" + uids[index], MailProtocol.Pop3, accountKey,
                        readyRules, settings, summary, null).ConfigureAwait(false);
                    store.AddInboxMarker(marker);
                }
                FinishBaselines();
                await client.DisconnectAsync(true).ConfigureAwait(false);
            }
            return summary;
        }

        private async Task RetryPop3Async(Pop3Client client, IList<string> uids, AppSettings settings, string accountKey, CycleSummary summary)
        {
            foreach (var record in store.GetRetryable(accountKey, MailProtocol.Pop3))
            {
                var uid = record.MailStableId.StartsWith("pop:") ? record.MailStableId.Substring(4) : "";
                var index = uids.IndexOf(uid);
                if (index < 0)
                {
                    MarkUnavailable(record, "原邮件已从收件箱中删除，程序无法再次读取。请人工确认是否需要重新发送。 ");
                    continue;
                }
                var rule = store.GetRule(record.RuleId);
                if (rule == null || !rule.Enabled) continue;
                var message = await client.GetMessageAsync(index).ConfigureAwait(false);
                await SendForRuleAsync(message, record.MailStableId, MailProtocol.Pop3, accountKey, rule, settings, summary, record).ConfigureAwait(false);
            }
        }

        private async Task ProcessMessageAsync(MimeMessage message, string stableId, MailProtocol protocol,
            string accountKey, List<ForwardRule> rules, AppSettings settings, CycleSummary summary, bool? isUnread)
        {
            foreach (var rule in rules)
            {
                if (!RuleUtilities.IsMatch(rule, message.Subject, message.From.Mailboxes.Select(x => x.Address))) continue;
                var id = ProcessingId(accountKey, rule.Id, stableId);
                var existing = store.GetProcessing(id);
                if (existing != null) continue;
                if (rule.OnlyUnread)
                {
                    if (!isUnread.HasValue)
                        throw new FatalMonitorException("邮件服务器没有返回这封邮件的已读状态，因此无法执行“只转发未读邮件”规则。请稍后重试，或联系邮箱管理员检查 IMAP 服务。");
                    if (!isUnread.Value)
                    {
                        store.SaveProcessing(new ProcessingRecord
                        {
                            Id = id,
                            AccountKey = accountKey,
                            RuleId = rule.Id,
                            MailStableId = stableId,
                            Protocol = protocol,
                            Status = ProcessingStatus.SkippedRead,
                            Attempts = 0,
                            CreatedUtc = DateTime.UtcNow,
                            Subject = message.Subject ?? "",
                            Sender = message.From.ToString(),
                            Recipients = string.Join(";", rule.Recipients ?? new List<string>())
                        });
                        store.AddLog(LogLevel.Information, "邮件转发", rule, message.Subject, message.From.ToString(),
                            string.Join(";", rule.Recipients ?? new List<string>()), "已跳过",
                            "检查这封邮件时，它已经是已读状态；该规则设置为只转发未读邮件，因此没有转发。", id);
                        continue;
                    }
                }
                await SendForRuleAsync(message, stableId, protocol, accountKey, rule, settings, summary, null).ConfigureAwait(false);
            }
        }

        private async Task SendForRuleAsync(MimeMessage original, string stableId, MailProtocol protocol,
            string accountKey, ForwardRule rule, AppSettings settings, CycleSummary summary, ProcessingRecord record)
        {
            var recipients = string.Join(";", rule.Recipients ?? new List<string>());
            if (record == null)
            {
                record = new ProcessingRecord
                {
                    Id = ProcessingId(accountKey, rule.Id, stableId),
                    AccountKey = accountKey,
                    RuleId = rule.Id,
                    MailStableId = stableId,
                    Protocol = protocol,
                    Status = ProcessingStatus.Sending,
                    Attempts = 1,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    Subject = original.Subject ?? "",
                    Sender = original.From.ToString(),
                    Recipients = recipients,
                    MarkReadRequested = protocol == MailProtocol.Imap && settings.MarkReadAfterAllRulesCompleted
                };
            }
            else
            {
                record.Status = ProcessingStatus.Sending;
                record.Attempts++;
            }
            store.SaveProcessing(record);

            var sendStarted = false;
            try
            {
                var forwarded = MailComposer.Compose(original, rule, settings);
                await SendAsync(forwarded, settings, () => sendStarted = true).ConfigureAwait(false);
                record.Status = ProcessingStatus.Success;
                record.LastError = "";
                record.NextAttemptUtc = null;
                store.SaveProcessing(record);
                summary.Forwarded++;
                store.AddLog(LogLevel.Information, "邮件转发", rule, original.Subject, original.From.ToString(),
                    recipients, "转发成功", "邮件已经提交给发信服务器。", record.Id);
            }
            catch (Exception ex)
            {
                var definite = !sendStarted || ex is SmtpCommandException || ex is AuthenticationException;
                record.Status = definite ? ProcessingStatus.RetryableFailure : ProcessingStatus.PendingConfirmation;
                record.LastError = SafeException(ex);
                record.NextAttemptUtc = definite ? DateTime.UtcNow.AddMinutes(RetryDelayMinutes(record.Attempts)) : (DateTime?)null;
                store.SaveProcessing(record);
                summary.Failed++;
                store.AddLog(definite ? LogLevel.Error : LogLevel.Warning,
                    definite ? "转发暂未成功" : "请确认发送结果", rule, original.Subject, original.From.ToString(), recipients,
                    definite ? "稍后自动重试" : "无法确认是否已发送", record.LastError, record.Id);
            }
        }

        private static async Task SendAsync(MimeMessage message, AppSettings settings, Action sendingBegins = null)
        {
            using (var client = new SmtpClient())
            {
                ConfigureCertificate(client, settings.SmtpAllowInvalidCertificate);
                await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, ToSecureSocketOptions(settings.SmtpSecurity)).ConfigureAwait(false);
                if (settings.SmtpAuthenticate)
                {
                    var user = settings.SmtpReuseReceiveCredentials ? settings.ReceiveUserName : settings.SmtpUserName;
                    var password = settings.SmtpReuseReceiveCredentials ? settings.ReceivePassword : settings.SmtpPassword;
                    await client.AuthenticateAsync(user, password).ConfigureAwait(false);
                }
                if (sendingBegins != null) sendingBegins();
                await client.SendAsync(message).ConfigureAwait(false);
                await client.DisconnectAsync(true).ConfigureAwait(false);
            }
        }

        private static int RetryDelayMinutes(int attempts)
        {
            var exponent = Math.Max(0, Math.Min(5, attempts - 1));
            return Math.Min(60, 2 * (1 << exponent));
        }

        private void MarkUnavailable(ProcessingRecord record, string detail)
        {
            record.Status = ProcessingStatus.Failed;
            record.LastError = detail;
            store.SaveProcessing(record);
            store.AddLog(LogLevel.Error, "无法再次发送", store.GetRule(record.RuleId), record.Subject, record.Sender,
                record.Recipients, "需要人工处理", detail, record.Id);
        }

        private async Task ApplyPendingReadMarksAsync(IMailFolder folder, string accountKey, CycleSummary summary)
        {
            foreach (var stableId in store.GetMessagesReadyForReadMark(accountKey))
                await TryMarkMessageReadAsync(folder, accountKey, stableId, summary).ConfigureAwait(false);
        }

        private async Task TryMarkMessageReadAsync(IMailFolder folder, string accountKey, string stableId, CycleSummary summary)
        {
            var records = store.GetProcessingForMessage(accountKey, stableId);
            if (!ProcessingPolicy.ShouldMarkRead(records)) return;
            var sample = records.FirstOrDefault();
            uint uidValue;
            if (!TryParseLastUInt(stableId, out uidValue))
            {
                store.MarkMessageReadCompleted(accountKey, stableId);
                store.AddLog(LogLevel.Warning, "更新邮件状态", null, sample == null ? "" : sample.Subject,
                    sample == null ? "" : sample.Sender, sample == null ? "" : sample.Recipients,
                    "无法设置已读", "无法识别原邮件在服务器中的位置；不会重新发送邮件。", sample == null ? "" : sample.Id);
                return;
            }

            try
            {
                await folder.AddFlagsAsync(new UniqueId(uidValue), MessageFlags.Seen, true).ConfigureAwait(false);
                store.MarkMessageReadCompleted(accountKey, stableId);
                store.AddLog(LogLevel.Information, "更新邮件状态", null, sample == null ? "" : sample.Subject,
                    sample == null ? "" : sample.Sender, sample == null ? "" : sample.Recipients,
                    "已设为已读", "这封邮件命中的所有规则都已处理完成，原邮件已设为已读。", sample == null ? "" : sample.Id);
            }
            catch (MessageNotFoundException)
            {
                store.MarkMessageReadCompleted(accountKey, stableId);
                store.AddLog(LogLevel.Warning, "更新邮件状态", null, sample == null ? "" : sample.Subject,
                    sample == null ? "" : sample.Sender, sample == null ? "" : sample.Recipients,
                    "原邮件已不存在", "转发已经完成，但原邮件已被删除或移动，因此无法再设置为已读。不会重新发送邮件。", sample == null ? "" : sample.Id);
            }
            catch (Exception ex)
            {
                summary.Failed++;
                store.AddLog(LogLevel.Warning, "更新邮件状态", null, sample == null ? "" : sample.Subject,
                    sample == null ? "" : sample.Sender, sample == null ? "" : sample.Recipients,
                    "稍后重试设置已读", "邮件转发已经完成，但暂时无法把原邮件设为已读。程序稍后只会重试更新已读状态，不会重复转发。" + SafeException(ex),
                    sample == null ? "" : sample.Id);
            }
        }

        private void FinishBaselines()
        {
            foreach (var rule in store.GetRules().Where(x => x.NeedsBaseline))
            {
                rule.NeedsBaseline = false;
                store.SaveRule(rule);
            }
        }

        public static void ValidateSettings(AppSettings settings)
        {
            ValidateReceiveSettings(settings);
            ValidateSmtpSettings(settings);
            if (settings.PollIntervalMinutes < 1 || settings.PollIntervalMinutes > 1440)
                throw new InvalidOperationException("监测间隔必须在 1 到 1440 分钟之间。");
        }

        public static void ValidateMailboxFeatures(AppSettings settings, IEnumerable<ForwardRule> rules)
        {
            if (settings == null || settings.ReceiveProtocol == MailProtocol.Imap) return;
            if (settings.MarkReadAfterAllRulesCompleted)
                throw new InvalidOperationException("“所有匹配规则处理完成后，将原邮件设为已读”仅支持 IMAP。请改用 IMAP，或者取消此选项。");
            if ((rules ?? Enumerable.Empty<ForwardRule>()).Any(x => x.Enabled && x.OnlyUnread))
                throw new InvalidOperationException("当前有启用的规则设置为“只转发未读邮件”，但 POP3 无法取得可靠的已读状态。请改用 IMAP，或者取消规则中的这个选项。");
        }

        private static void ValidateReceiveSettings(AppSettings settings)
        {
            if (settings == null) throw new InvalidOperationException("尚未保存邮箱设置，请先完成“邮箱设置”。");
            if (string.IsNullOrWhiteSpace(settings.ReceiveHost)) throw new InvalidOperationException("请填写收信服务器地址。");
            if (settings.ReceivePort < 1 || settings.ReceivePort > 65535) throw new InvalidOperationException("收信端口必须是 1 到 65535 之间的数字。");
            if (string.IsNullOrWhiteSpace(settings.ReceiveUserName)) throw new InvalidOperationException("请填写 OA/邮箱用户名。");
            if (string.IsNullOrEmpty(settings.ReceivePassword)) throw new InvalidOperationException("请填写 OA/邮箱密码。");
            if (settings.ReceiveProtocol == MailProtocol.Imap && string.IsNullOrWhiteSpace(settings.ImapFolder))
                throw new InvalidOperationException("请填写 IMAP 文件夹。");
        }

        private static void ValidateSmtpSettings(AppSettings settings)
        {
            if (settings == null) throw new InvalidOperationException("尚未保存邮箱设置，请先完成“邮箱设置”。");
            if (string.IsNullOrWhiteSpace(settings.SmtpHost)) throw new InvalidOperationException("请填写 SMTP 服务器地址。");
            if (settings.SmtpPort < 1 || settings.SmtpPort > 65535) throw new InvalidOperationException("SMTP 端口必须是 1 到 65535 之间的数字。");
            MailboxAddress sender;
            if (!MailboxAddress.TryParse(settings.SenderAddress ?? "", out sender)) throw new InvalidOperationException("发件人邮箱地址格式不正确，请按 name@example.com 的格式填写。");
            if (settings.SmtpAuthenticate && !settings.SmtpReuseReceiveCredentials)
            {
                if (string.IsNullOrWhiteSpace(settings.SmtpUserName) || string.IsNullOrEmpty(settings.SmtpPassword))
                    throw new InvalidOperationException("你选择了发信使用另一账户，请填写发信服务器的用户名和密码。");
            }
        }

        public static string BuildAccountKey(AppSettings settings)
        {
            var raw = settings.ReceiveProtocol + "|" + settings.ReceiveHost.Trim().ToLowerInvariant() + "|" +
                      settings.ReceivePort + "|" + settings.ReceiveUserName.Trim().ToLowerInvariant() + "|" +
                      (settings.ImapFolder ?? "").Trim().ToLowerInvariant();
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string ProcessingId(string accountKey, string ruleId, string stableId)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(accountKey + "|" + ruleId + "|" + stableId));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        private static string PopMarker(string accountKey, string uid)
        {
            return accountKey + "|" + uid;
        }

        private static bool TryParseLastUInt(string text, out uint value)
        {
            var part = (text ?? "").Split(':').LastOrDefault();
            return uint.TryParse(part, out value);
        }

        private static SecureSocketOptions ToSecureSocketOptions(SocketSecurityMode mode)
        {
            switch (mode)
            {
                case SocketSecurityMode.None: return SecureSocketOptions.None;
                case SocketSecurityMode.SslOnConnect: return SecureSocketOptions.SslOnConnect;
                case SocketSecurityMode.StartTls: return SecureSocketOptions.StartTls;
                default: return SecureSocketOptions.Auto;
            }
        }

        private static void ConfigureCertificate(MailService client, bool allowInvalid)
        {
            if (allowInvalid) client.ServerCertificateValidationCallback = (s, c, h, e) => true;
        }

        private static string SafeException(Exception exception)
        {
            var text = UserMessages.ForException(exception);
            text = text.Replace("\0", "");
            return text.Length <= 2000 ? text : text.Substring(0, 2000);
        }
    }
}
