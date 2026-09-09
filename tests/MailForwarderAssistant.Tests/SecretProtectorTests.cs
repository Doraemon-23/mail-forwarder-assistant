using NUnit.Framework;

namespace MailForwarderAssistant.Tests
{
    [TestFixture]
    public sealed class SecretProtectorTests
    {
        [Test]
        public void ProtectsAndRestoresForCurrentUser()
        {
            var encrypted = SecretProtector.Protect("内部密码-123");
            Assert.That(encrypted, Is.Not.EqualTo("内部密码-123"));
            Assert.That(SecretProtector.Unprotect(encrypted), Is.EqualTo("内部密码-123"));
        }
    }
}
