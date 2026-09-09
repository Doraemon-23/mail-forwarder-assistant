using System;
using NUnit.Framework;

namespace MailForwarderAssistant.Tests
{
    [TestFixture]
    public sealed class UserMessagesTests
    {
        [Test]
        public void ConvertsLegacyInternalTerms()
        {
            Assert.That(UserMessages.FriendlyEvent("建立基线"), Is.EqualTo("首次监测准备"));
            Assert.That(UserMessages.FriendlyResult("发送结果不明确"), Is.EqualTo("无法确认是否已发送"));
            Assert.That(UserMessages.FriendlyDetail("IMAP 邮箱 UIDVALIDITY 已改变"), Does.Contain("重新确定新邮件起点"));
        }

        [Test]
        public void GivesActionableFallbackMessage()
        {
            var text = UserMessages.ForException(new Exception("technical detail"));
            Assert.That(text, Does.Contain("检查邮箱设置"));
            Assert.That(text, Does.Not.Contain("technical detail"));
        }
    }
}
