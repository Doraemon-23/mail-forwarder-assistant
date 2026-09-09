using NUnit.Framework;

namespace MailForwarderAssistant.Tests
{
    [TestFixture]
    public class LocalizationTests
    {
        [Test]
        public void TraditionalChinese_ConvertsCommonInterfacePhrases()
        {
            Localization.SetLanguage(true, false);
            try
            {
                Assert.That(Localization.T("日志查询；正文后置签名；邮件转发助手 v1.0.10"),
                    Is.EqualTo("日誌查詢；正文後置簽名；郵件轉發助手 v1.0.10"));
            }
            finally
            {
                Localization.SetLanguage(false, false);
            }
        }

        [Test]
        public void SimplifiedChinese_ConvertsTraditionalInterfaceTextBack()
        {
            Localization.SetLanguage(false, false);
            Assert.That(Localization.T("日誌查詢；正文後置簽名；郵件轉發助手 v1.0.10"),
                Is.EqualTo("日志查询；正文后置签名；邮件转发助手 v1.0.10"));
        }
    }
}
