using System;
using NUnit.Framework;

namespace MailForwarderAssistant.Tests
{
    [TestFixture]
    public sealed class MailboxFeatureTests
    {
        [Test]
        public void Imap_AllowsUnreadFeatures()
        {
            var settings = new AppSettings
            {
                ReceiveProtocol = MailProtocol.Imap,
                MarkReadAfterAllRulesCompleted = true
            };
            var rules = new[] { new ForwardRule { Enabled = true, OnlyUnread = true } };

            Assert.DoesNotThrow(() => MailMonitorService.ValidateMailboxFeatures(settings, rules));
        }

        [Test]
        public void Pop3_RejectsGlobalMarkReadOption()
        {
            var settings = new AppSettings
            {
                ReceiveProtocol = MailProtocol.Pop3,
                MarkReadAfterAllRulesCompleted = true
            };

            Assert.Throws<InvalidOperationException>(() =>
                MailMonitorService.ValidateMailboxFeatures(settings, new ForwardRule[0]));
        }

        [Test]
        public void Pop3_RejectsEnabledUnreadOnlyRule()
        {
            var settings = new AppSettings { ReceiveProtocol = MailProtocol.Pop3 };
            var rules = new[] { new ForwardRule { Enabled = true, OnlyUnread = true } };

            Assert.Throws<InvalidOperationException>(() =>
                MailMonitorService.ValidateMailboxFeatures(settings, rules));
        }

        [Test]
        public void Pop3_AllowsDisabledUnreadOnlyRule()
        {
            var settings = new AppSettings { ReceiveProtocol = MailProtocol.Pop3 };
            var rules = new[] { new ForwardRule { Enabled = false, OnlyUnread = true } };

            Assert.DoesNotThrow(() => MailMonitorService.ValidateMailboxFeatures(settings, rules));
        }

        [Test]
        public void MarkRead_WaitsUntilEveryMatchedRuleIsResolved()
        {
            var records = new[]
            {
                new ProcessingRecord { MarkReadRequested = true, Status = ProcessingStatus.Success },
                new ProcessingRecord { MarkReadRequested = true, Status = ProcessingStatus.RetryableFailure }
            };

            Assert.That(ProcessingPolicy.ShouldMarkRead(records), Is.False);
        }

        [Test]
        public void MarkRead_IsAllowedAfterEveryMatchedRuleSucceeds()
        {
            var records = new[]
            {
                new ProcessingRecord { MarkReadRequested = true, Status = ProcessingStatus.Success },
                new ProcessingRecord { MarkReadRequested = true, Status = ProcessingStatus.Success }
            };

            Assert.That(ProcessingPolicy.ShouldMarkRead(records), Is.True);
        }

        [Test]
        public void MarkRead_DoesNothingWhenMailboxOptionWasNotRequested()
        {
            var records = new[]
            {
                new ProcessingRecord { MarkReadRequested = false, Status = ProcessingStatus.Success }
            };

            Assert.That(ProcessingPolicy.ShouldMarkRead(records), Is.False);
        }
    }
}
