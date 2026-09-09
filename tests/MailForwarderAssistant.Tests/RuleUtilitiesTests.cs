using System;
using NUnit.Framework;

namespace MailForwarderAssistant.Tests
{
    [TestFixture]
    public sealed class RuleUtilitiesTests
    {
        [Test]
        public void MatchesAnyKeywordIgnoringCase()
        {
            var rule = new ForwardRule { Enabled = true, Keywords = RuleUtilities.ParseKeywords("审批\nURGENT") };
            Assert.That(RuleUtilities.IsMatch(rule, "urgent: 请处理"), Is.True);
            Assert.That(RuleUtilities.IsMatch(rule, "普通通知"), Is.False);
        }

        [Test]
        public void RequiresBothSenderAndSubjectWhenSenderIsConfigured()
        {
            var rule = new ForwardRule
            {
                Enabled = true,
                SenderAddress = "boss@example.com",
                Keywords = RuleUtilities.ParseKeywords("审批")
            };

            Assert.That(RuleUtilities.IsMatch(rule, "审批通知", new[] { "BOSS@example.com" }), Is.True);
            Assert.That(RuleUtilities.IsMatch(rule, "普通通知", new[] { "boss@example.com" }), Is.False);
            Assert.That(RuleUtilities.IsMatch(rule, "审批通知", new[] { "other@example.com" }), Is.False);
        }

        [Test]
        public void ParsesAndDeduplicatesRecipients()
        {
            var recipients = RuleUtilities.ParseRecipients("one@example.com; TWO@example.com\ntwo@example.com");
            Assert.That(recipients, Is.EqualTo(new[] { "one@example.com", "TWO@example.com" }));
        }

        [Test]
        public void RejectsInvalidRecipient()
        {
            Assert.Throws<FormatException>(() => RuleUtilities.ParseRecipients("not-an-address"));
        }

        [Test]
        public void ReportsUniqueDuplicateAndInvalidRecipientCounts()
        {
            var result = RuleUtilities.AnalyzeRecipients("one@example.com; ONE@example.com; two@example.com; bad");

            Assert.That(result.UniqueCount, Is.EqualTo(2));
            Assert.That(result.DuplicateCount, Is.EqualTo(1));
            Assert.That(result.InvalidCount, Is.EqualTo(1));
        }

        [Test]
        public void RejectsForwardingToOwnMailbox()
        {
            var recipients = RuleUtilities.ParseRecipients("other@example.com; myself@example.com");

            Assert.Throws<InvalidOperationException>(() =>
                RuleUtilities.EnsureNoSelfRecipient(recipients, new[] { "MYSELF@example.com" }));
        }
    }
}
