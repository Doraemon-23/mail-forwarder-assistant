using System;
using System.Collections.Generic;
using System.Linq;
using MimeKit;

namespace MailForwarderAssistant
{
    public sealed class RecipientAnalysis
    {
        public int UniqueCount { get; set; }
        public int DuplicateCount { get; set; }
        public int InvalidCount { get; set; }
    }

    public static class RuleUtilities
    {
        public static bool IsMatch(ForwardRule rule, string subject, IEnumerable<string> senderAddresses = null)
        {
            if (rule == null || !rule.Enabled || rule.Keywords == null) return false;
            subject = subject ?? "";
            var subjectMatches = rule.Keywords.Any(k => !string.IsNullOrWhiteSpace(k) &&
                subject.IndexOf(k.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            if (!subjectMatches) return false;
            if (string.IsNullOrWhiteSpace(rule.SenderAddress)) return true;
            return senderAddresses != null && senderAddresses.Any(x =>
                string.Equals((x ?? "").Trim(), rule.SenderAddress.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static List<string> ParseRecipients(string text)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in (text ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                MailboxAddress address;
                if (!MailboxAddress.TryParse(value.Trim(), out address) || string.IsNullOrWhiteSpace(address.Address) ||
                    address.Address.IndexOf('@') <= 0 || address.Address.EndsWith("@", StringComparison.Ordinal))
                    throw new FormatException("邮箱地址格式不正确：" + value.Trim() + "。请按 name@example.com 的格式填写。");
                if (seen.Add(address.Address)) result.Add(address.Address);
            }
            if (result.Count == 0) throw new FormatException("请至少填写一个转发收件人邮箱地址。");
            return result;
        }

        public static RecipientAnalysis AnalyzeRecipients(string text)
        {
            var result = new RecipientAnalysis();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in SplitValues(text))
            {
                string address;
                if (!TryGetCanonicalAddress(value, out address))
                {
                    result.InvalidCount++;
                    continue;
                }
                if (seen.Add(address)) result.UniqueCount++;
                else result.DuplicateCount++;
            }
            return result;
        }

        public static string ParseOptionalMailboxAddress(string text, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string address;
            if (!TryGetCanonicalAddress(text.Trim(), out address))
                throw new FormatException((fieldName ?? "邮箱地址") + "格式不正确。请按 name@example.com 的格式填写。");
            return address;
        }

        public static List<string> GetOwnMailboxAddresses(AppSettings settings)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string address;
            if (settings != null && TryGetCanonicalAddress(settings.SenderAddress, out address)) result.Add(address);
            if (settings != null && TryGetCanonicalAddress(settings.ReceiveUserName, out address)) result.Add(address);
            return result.ToList();
        }

        public static void EnsureNoSelfRecipient(IEnumerable<string> recipients, IEnumerable<string> ownAddresses)
        {
            var own = new HashSet<string>(ownAddresses ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var duplicate = (recipients ?? Enumerable.Empty<string>()).FirstOrDefault(x => own.Contains(x ?? ""));
            if (!string.IsNullOrWhiteSpace(duplicate))
                throw new InvalidOperationException("转发收件人不能包含本邮箱地址“" + duplicate + "”，否则可能形成自动转发循环。");
        }

        public static List<string> ParseKeywords(string text)
        {
            return (text ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> SplitValues(string text)
        {
            return (text ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0);
        }

        private static bool TryGetCanonicalAddress(string value, out string addressText)
        {
            addressText = "";
            MailboxAddress address;
            if (!MailboxAddress.TryParse((value ?? "").Trim(), out address) || string.IsNullOrWhiteSpace(address.Address) ||
                address.Address.IndexOf('@') <= 0 || address.Address.EndsWith("@", StringComparison.Ordinal)) return false;
            addressText = address.Address;
            return true;
        }
    }
}
