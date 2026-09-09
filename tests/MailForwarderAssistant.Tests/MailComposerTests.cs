using System.IO;
using System.Linq;
using System.Text;
using MimeKit;
using NUnit.Framework;

namespace MailForwarderAssistant.Tests
{
    [TestFixture]
    public sealed class MailComposerTests
    {
        [Test]
        public void KeepsBodyOrderAndAttachment()
        {
            var original = new MimeMessage();
            original.From.Add(MailboxAddress.Parse("source@example.com"));
            original.To.Add(MailboxAddress.Parse("box@example.com"));
            original.Subject = "审批通知";
            var originalBody = new BodyBuilder { TextBody = "原始正文", HtmlBody = "<p>原始正文</p>" };
            originalBody.Attachments.Add("测试.txt", Encoding.UTF8.GetBytes("attachment"));
            original.Body = originalBody.ToMessageBody();

            var rule = new ForwardRule
            {
                SubjectPrefix = "转发：",
                IntroText = "请处理",
                SignatureText = "自动转发",
                Recipients = RuleUtilities.ParseRecipients("target@example.com")
            };
            var settings = new AppSettings { SenderAddress = "box@example.com", SenderName = "办公邮箱" };

            var result = MailComposer.Compose(original, rule, settings);

            Assert.That(result.Subject, Is.EqualTo("转发：审批通知"));
            Assert.That(result.TextBody.IndexOf("请处理"), Is.LessThan(result.TextBody.IndexOf("原始正文")));
            Assert.That(result.TextBody.IndexOf("原始正文"), Is.LessThan(result.TextBody.IndexOf("自动转发")));
            Assert.That(result.Attachments.Count(), Is.EqualTo(1));
            Assert.That(((MimePart)result.Attachments.Single()).FileName, Is.EqualTo("测试.txt"));
        }
    }
}
