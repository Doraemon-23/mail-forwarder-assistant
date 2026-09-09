using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MimeKit;

namespace MailForwarderAssistant
{
    public static class MailComposer
    {
        public static MimeMessage Compose(MimeMessage original, ForwardRule rule, AppSettings settings)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var forwarded = new MimeMessage();
            forwarded.From.Add(new MailboxAddress(settings.SenderName ?? "", settings.SenderAddress));
            foreach (var recipient in rule.Recipients) forwarded.To.Add(MailboxAddress.Parse(recipient));
            forwarded.Subject = (rule.SubjectPrefix ?? "") + (original.Subject ?? "");
            forwarded.Date = DateTimeOffset.Now;

            var headerText = BuildHeaderText(original);
            var builder = new BodyBuilder
            {
                TextBody = JoinText(rule.IntroText, headerText, original.TextBody ?? StripHtml(original.HtmlBody), rule.SignatureText),
                HtmlBody = JoinHtml(rule.IntroText, BuildHeaderHtml(original), original.HtmlBody ?? ToHtml(original.TextBody), rule.SignatureText)
            };

            CopyAttachments(original, builder);
            CopyLinkedResources(original, builder);
            forwarded.Body = builder.ToMessageBody();
            return forwarded;
        }

        private static void CopyAttachments(MimeMessage original, BodyBuilder builder)
        {
            foreach (var attachment in original.Attachments)
            {
                var mimePart = attachment as MimePart;
                if (mimePart != null)
                {
                    using (var stream = new MemoryStream())
                    {
                        mimePart.Content.DecodeTo(stream);
                        var fileName = string.IsNullOrWhiteSpace(mimePart.FileName) ? "附件" : mimePart.FileName;
                        builder.Attachments.Add(fileName, stream.ToArray(), mimePart.ContentType);
                    }
                    continue;
                }

                var messagePart = attachment as MessagePart;
                if (messagePart != null)
                {
                    using (var stream = new MemoryStream())
                    {
                        messagePart.Message.WriteTo(stream);
                        var fileName = messagePart.ContentDisposition == null || string.IsNullOrWhiteSpace(messagePart.ContentDisposition.FileName)
                            ? "转发邮件.eml" : messagePart.ContentDisposition.FileName;
                        builder.Attachments.Add(fileName, stream.ToArray(), new ContentType("message", "rfc822"));
                    }
                }
            }
        }

        private static void CopyLinkedResources(MimeMessage original, BodyBuilder builder)
        {
            var attachmentSet = original.Attachments.OfType<MimePart>().ToList();
            foreach (var part in original.BodyParts.OfType<MimePart>())
            {
                if (attachmentSet.Contains(part) || string.IsNullOrWhiteSpace(part.ContentId)) continue;
                if (part is TextPart) continue;
                using (var stream = new MemoryStream())
                {
                    part.Content.DecodeTo(stream);
                    var fileName = string.IsNullOrWhiteSpace(part.FileName) ? part.ContentId : part.FileName;
                    var resource = builder.LinkedResources.Add(fileName, stream.ToArray(), part.ContentType);
                    resource.ContentId = part.ContentId;
                    resource.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
                }
            }
        }

        private static string BuildHeaderText(MimeMessage message)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---------------- 原邮件 ----------------");
            sb.AppendLine("发件人：" + message.From);
            sb.AppendLine("时间：" + message.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("收件人：" + message.To);
            sb.AppendLine("主题：" + (message.Subject ?? ""));
            sb.AppendLine("----------------------------------------");
            return sb.ToString();
        }

        private static string BuildHeaderHtml(MimeMessage message)
        {
            return "<hr><div style=\"font-family:Segoe UI,Microsoft YaHei,sans-serif;font-size:12px;color:#555\">" +
                   "<div><b>发件人：</b>" + WebUtility.HtmlEncode(message.From.ToString()) + "</div>" +
                   "<div><b>时间：</b>" + WebUtility.HtmlEncode(message.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")) + "</div>" +
                   "<div><b>收件人：</b>" + WebUtility.HtmlEncode(message.To.ToString()) + "</div>" +
                   "<div><b>主题：</b>" + WebUtility.HtmlEncode(message.Subject ?? "") + "</div></div><hr>";
        }

        private static string JoinText(string intro, string header, string body, string signature)
        {
            return string.Join(Environment.NewLine + Environment.NewLine,
                new[] { intro, header, body, signature }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string JoinHtml(string intro, string header, string bodyHtml, string signature)
        {
            return "<div style=\"font-family:Segoe UI,Microsoft YaHei,sans-serif\">" +
                   ToHtml(intro) + header + (bodyHtml ?? "") + ToHtml(signature) + "</div>";
        }

        private static string ToHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return "<div>" + WebUtility.HtmlEncode(text).Replace("\r\n", "<br>").Replace("\n", "<br>") + "</div>";
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            var withoutTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            return WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(withoutTags, @"\s+", " ")).Trim();
        }
    }
}
