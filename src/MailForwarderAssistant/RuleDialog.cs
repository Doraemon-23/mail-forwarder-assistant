using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MessageBox = MailForwarderAssistant.LocalizedMessageBox;

namespace MailForwarderAssistant
{
    public sealed class RuleDialog : Form
    {
        private readonly TextBox nameBox = new TextBox();
        private readonly CheckBox enabledBox = new CheckBox { Text = "启用规则", AutoSize = true };
        private readonly CheckBox onlyUnreadBox = new CheckBox { Text = "只转发未读邮件", AutoSize = true };
        private readonly TextBox senderBox = new TextBox();
        private readonly TextBox keywordsBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly TextBox prefixBox = new TextBox();
        private readonly TextBox introBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly TextBox signatureBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly TextBox recipientsBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly Label recipientSummaryLabel = new Label();
        private readonly List<string> ownAddresses;
        private readonly ForwardRule original;
        private readonly MailProtocol receiveProtocol;

        public ForwardRule Result { get; private set; }

        public RuleDialog(ForwardRule rule, IEnumerable<string> ownAddresses, MailProtocol receiveProtocol)
        {
            original = rule ?? new ForwardRule();
            this.ownAddresses = (ownAddresses ?? Enumerable.Empty<string>()).ToList();
            this.receiveProtocol = receiveProtocol;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = rule == null ? "新增转发规则" : "编辑转发规则";
            Width = 740;
            Height = 760;
            MinimumSize = new Size(660, 620);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            BuildUi();
            LoadRule();
            recipientsBox.TextChanged += (s, e) => UpdateRecipientSummary();
            UpdateRecipientSummary();
            Localization.ApplyTo(this);
        }

        private void BuildUi()
        {
            var lineHeight = TextRenderer.MeasureText("邮箱", Font).Height;
            var singleLineRowHeight = Math.Max(44, lineHeight + 16);
            var twoLineEditorHeight = Math.Max(48, lineHeight * 2 + 10);
            var threeLineEditorHeight = Math.Max(64, lineHeight * 3 + 10);
            var hintHeight = Math.Max(36, lineHeight + 14);
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14),
                ColumnCount = 2,
                RowCount = 8
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, singleLineRowHeight));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, singleLineRowHeight));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, singleLineRowHeight));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, twoLineEditorHeight + hintHeight + 6));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, singleLineRowHeight));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, twoLineEditorHeight + 6));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, twoLineEditorHeight + 6));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, threeLineEditorHeight + hintHeight + 6));

            AddRow(table, 0, "规则名称：", nameBox);
            var ruleOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            enabledBox.Margin = new Padding(0, 4, 12, 2);
            onlyUnreadBox.Margin = new Padding(0, 4, 0, 2);
            ruleOptions.Controls.Add(enabledBox);
            ruleOptions.Controls.Add(onlyUnreadBox);
            AddRow(table, 1, "规则选项：", ruleOptions);
            AddRow(table, 2, "发件人邮箱：", senderBox);
            AddRow(table, 3, "标题关键字：", keywordsBox, "每行、逗号或分号分隔；填写发件人后，两项必须同时命中", null, hintHeight);
            AddRow(table, 4, "转发标题前缀：", prefixBox);
            AddRow(table, 5, "正文前置文字：", introBox);
            AddRow(table, 6, "正文后置签名：", signatureBox);
            AddRow(table, 7, "转发收件人：", recipientsBox, "", recipientSummaryLabel, hintHeight);

            keywordsBox.MinimumSize = new Size(0, twoLineEditorHeight);
            introBox.MinimumSize = new Size(0, twoLineEditorHeight);
            signatureBox.MinimumSize = new Size(0, twoLineEditorHeight);
            recipientsBox.MinimumSize = new Size(0, threeLineEditorHeight);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 7, 0, 0) };
            var save = new Button { Text = "保存", AutoSize = true, MinimumSize = new Size(110, 40), Padding = new Padding(12, 5, 12, 5) };
            var cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(110, 40), Padding = new Padding(12, 5, 12, 5), DialogResult = DialogResult.Cancel };
            save.Click += SaveClicked;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(table);
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            shell.Controls.Add(scroll, 0, 0);
            shell.Controls.Add(buttons, 0, 1);
            Controls.Add(shell);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control, string hint = null, Label hintLabel = null, int hintHeight = 36)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, AutoEllipsis = false }, 0, row);
            if (hintLabel == null && string.IsNullOrEmpty(hint))
            {
                control.Dock = DockStyle.Fill;
                table.Controls.Add(control, 1, row);
            }
            else
            {
                var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, hintHeight));
                control.Dock = DockStyle.Fill;
                panel.Controls.Add(control, 0, 0);
                var explanation = hintLabel ?? new Label();
                explanation.Text = hintLabel == null ? hint : explanation.Text;
                explanation.ForeColor = Color.DimGray;
                explanation.AutoSize = false;
                explanation.Dock = DockStyle.Fill;
                explanation.TextAlign = ContentAlignment.MiddleLeft;
                explanation.Font = new Font("Microsoft YaHei UI", 8F);
                panel.Controls.Add(explanation, 0, 1);
                table.Controls.Add(panel, 1, row);
            }
        }

        private void LoadRule()
        {
            nameBox.Text = original.Name;
            enabledBox.Checked = original.Enabled;
            onlyUnreadBox.Checked = original.OnlyUnread;
            senderBox.Text = original.SenderAddress;
            keywordsBox.Text = string.Join(Environment.NewLine, original.Keywords ?? Enumerable.Empty<string>());
            prefixBox.Text = original.SubjectPrefix;
            introBox.Text = original.IntroText;
            signatureBox.Text = original.SignatureText;
            recipientsBox.Text = string.Join(Environment.NewLine, original.Recipients ?? Enumerable.Empty<string>());
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            try
            {
                var keywords = RuleUtilities.ParseKeywords(keywordsBox.Text);
                if (string.IsNullOrWhiteSpace(nameBox.Text)) throw new InvalidOperationException("请填写规则名称。");
                if (keywords.Count == 0) throw new InvalidOperationException("请至少填写一个标题关键字。");
                if (onlyUnreadBox.Checked && receiveProtocol != MailProtocol.Imap)
                    throw new InvalidOperationException("“只转发未读邮件”仅支持 IMAP。请先在“邮箱设置”中选择 IMAP 收信方式，或者取消此选项。");
                var senderAddress = RuleUtilities.ParseOptionalMailboxAddress(senderBox.Text, "发件人邮箱");
                var recipients = RuleUtilities.ParseRecipients(recipientsBox.Text);
                RuleUtilities.EnsureNoSelfRecipient(recipients, ownAddresses);
                var isNew = string.IsNullOrWhiteSpace(original.Id);
                var newlyEnabled = !original.Enabled && enabledBox.Checked;
                Result = new ForwardRule
                {
                    Id = isNew ? Guid.NewGuid().ToString("N") : original.Id,
                    Name = nameBox.Text.Trim(),
                    Enabled = enabledBox.Checked,
                    NeedsBaseline = isNew || newlyEnabled || original.NeedsBaseline,
                    OnlyUnread = onlyUnreadBox.Checked,
                    Keywords = keywords,
                    SenderAddress = senderAddress,
                    SubjectPrefix = prefixBox.Text,
                    IntroText = introBox.Text,
                    SignatureText = signatureBox.Text,
                    Recipients = recipients,
                    UpdatedUtc = DateTime.UtcNow
                };
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(UserMessages.ForException(ex), "规则尚未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateRecipientSummary()
        {
            var analysis = RuleUtilities.AnalyzeRecipients(recipientsBox.Text);
            var parts = new List<string> { "去重后共 " + analysis.UniqueCount + " 人" };
            if (analysis.DuplicateCount > 0) parts.Add("已忽略 " + analysis.DuplicateCount + " 个重复地址");
            if (analysis.InvalidCount > 0) parts.Add("有 " + analysis.InvalidCount + " 项格式待检查");
            var hasSelf = false;
            if (analysis.UniqueCount > 0 && analysis.InvalidCount == 0)
            {
                try { RuleUtilities.EnsureNoSelfRecipient(RuleUtilities.ParseRecipients(recipientsBox.Text), ownAddresses); }
                catch (InvalidOperationException) { hasSelf = true; parts.Add("包含本邮箱地址，不能保存"); }
            }
            recipientSummaryLabel.Text = Localization.T(string.Join("；", parts) + "。多个收件人会在同一封邮件中相互可见。");
            recipientSummaryLabel.ForeColor = analysis.InvalidCount > 0 || hasSelf ? Color.Firebrick : Color.DimGray;
        }
    }
}
