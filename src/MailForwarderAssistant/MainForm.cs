using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MessageBox = MailForwarderAssistant.LocalizedMessageBox;

namespace MailForwarderAssistant
{
    public sealed class MainForm : Form
    {
        private readonly DataStore store;
        private readonly AppController controller;
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly NotifyIcon tray = new NotifyIcon { Visible = true };
        private readonly ToolStripMenuItem monitorToggleMenuItem = new ToolStripMenuItem();
        private readonly Icon runningIcon = IconFactory.Create(Color.SeaGreen);
        private readonly Icon pausedIcon = IconFactory.Create(Color.Gray);
        private readonly Icon errorIcon = IconFactory.Create(Color.Firebrick);
        private readonly Label stateLabel = new Label { AutoSize = true, Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold) };
        private readonly Label stateDetail = new Label { AutoSize = true, ForeColor = Color.DimGray };
        private readonly Label lastCycleLabel = new Label { AutoSize = true, ForeColor = Color.DimGray };
        private readonly Label todayChecksLabel = StatisticValueLabel();
        private readonly Label todayForwardedLabel = StatisticValueLabel();
        private readonly Label todayFailedLabel = StatisticValueLabel();
        private readonly Button startButton = new Button { Text = "启动监测", AutoSize = true, MinimumSize = new Size(135, 40), Padding = new Padding(12, 5, 12, 5) };
        private readonly Button pauseButton = new Button { Text = "暂停监测", AutoSize = true, MinimumSize = new Size(135, 40), Padding = new Padding(12, 5, 12, 5) };
        private readonly DataGridView rulesGrid = CreateGrid();
        private readonly DataGridView logsGrid = CreateGrid();
        private readonly Dictionary<string, Control> settingControls = new Dictionary<string, Control>();
        private readonly DateTimePicker logFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Width = 160 };
        private readonly DateTimePicker logTo = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Width = 160 };
        private readonly TextBox logRule = new TextBox { Width = 110 };
        private readonly TextBox logStatus = new TextBox { Width = 110 };
        private readonly TextBox logSearch = new TextBox { Width = 170 };
        private readonly ComboBox languageSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
        private bool allowClose;
        private bool changingLanguage;
        private string lastNotifiedError;

        public MainForm(DataStore store, AppController controller)
        {
            this.store = store;
            this.controller = controller;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Localization.ProductTitle;
            Width = 1040;
            Height = 720;
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = pausedIcon;
            BuildUi();
            BuildTray();
            LoadSettings();
            RefreshRules();
            RefreshLogs();
            ApplyState(MonitorState.Paused, "监测已暂停", false);
            ApplyLanguage();

            controller.StateChanged += ControllerStateChanged;
            controller.CycleCompleted += (s, e) => Ui(() =>
            {
                lastCycleLabel.Text = Localization.T("最近完成：") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                UpdateRuntimeStatistics();
                RefreshLogs();
                var settings = store.GetSettings();
                if (e.Forwarded > 0 && settings.NotifySuccess)
                    Notify("邮件转发成功", "本轮成功转发 " + e.Forwarded + " 封邮件。", ToolTipIcon.Info);
                if (e.Failed > 0 && settings.NotifyWarning)
                    Notify("有邮件需要处理", "本轮有 " + e.Failed + " 封邮件未成功发送，或需要你确认是否已经发送。", ToolTipIcon.Warning);
            });
            Shown += (s, e) =>
            {
                var settings = store.GetSettings();
                if (settings.StartWithWindows)
                {
                    try { StartupManager.SetEnabled(true); }
                    catch (Exception ex) { store.AddLog(LogLevel.Warning, "无法更新开机启动", null, "", "", "", "需要检查", UserMessages.ForException(ex)); }
                }
                if (settings.AutoStartMonitoring && IsConfigurationUsable(settings)) controller.Start();
                if (Environment.GetCommandLineArgs().Any(x => string.Equals(x, "--autostart", StringComparison.OrdinalIgnoreCase))) HideToTray();
            };
            FormClosing += OnFormClosing;
        }

        private void BuildUi()
        {
            tabs.TabPages.Add(BuildStatusPage());
            tabs.TabPages.Add(BuildRulesPage());
            tabs.TabPages.Add(BuildSettingsPage());
            tabs.TabPages.Add(BuildLogsPage());

            languageSelector.Items.AddRange(new object[] { "简体中文", "繁體中文" });
            changingLanguage = true;
            languageSelector.SelectedIndex = Localization.UseTraditionalChinese ? 1 : 0;
            changingLanguage = false;
            languageSelector.SelectedIndexChanged += LanguageSelectorChanged;

            var languageBar = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(6, 1, 8, 1),
                BackColor = SystemColors.Control,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            languageBar.Controls.Add(languageSelector);
            languageBar.Controls.Add(new Label
            {
                Text = "界面语言：",
                AutoSize = true,
                Margin = new Padding(4, 4, 4, 2)
            });

            var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Action positionLanguageBar = () =>
            {
                var left = Math.Max(0, host.ClientSize.Width - languageBar.Width - 3);
                if (languageBar.Left != left || languageBar.Top != 0)
                    languageBar.Location = new Point(left, 0);
            };
            host.Controls.Add(tabs);
            host.Controls.Add(languageBar);
            languageBar.BringToFront();
            host.Resize += (s, e) => positionLanguageBar();
            languageBar.SizeChanged += (s, e) => positionLanguageBar();
            Controls.Add(host);
            positionLanguageBar();
        }

        private void LanguageSelectorChanged(object sender, EventArgs e)
        {
            if (changingLanguage) return;
            var useTraditionalChinese = languageSelector.SelectedIndex == 1;
            try
            {
                var settings = store.GetSettings();
                settings.UseTraditionalChinese = useTraditionalChinese;
                store.SaveSettings(settings);
                Localization.SetLanguage(useTraditionalChinese, true);
                ApplyLanguage();
            }
            catch (Exception ex)
            {
                changingLanguage = true;
                languageSelector.SelectedIndex = Localization.UseTraditionalChinese ? 1 : 0;
                changingLanguage = false;
                MessageBox.Show(UserMessages.ForException(ex), "无法切换界面语言", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplyLanguage()
        {
            changingLanguage = true;
            try
            {
                Localization.ApplyTo(this);
                Localization.ApplyTo(tray.ContextMenuStrip);
                Text = Localization.ProductTitle;
                tray.Text = Truncate(Localization.ProductTitle + " - " + stateLabel.Text, 63);
                languageSelector.SelectedIndex = Localization.UseTraditionalChinese ? 1 : 0;
                RefreshRules();
                RefreshLogs();
                ApplyState(controller.State, controller.LastMessage, false);
            }
            finally
            {
                changingLanguage = false;
            }
        }

        private TabPage BuildStatusPage()
        {
            var page = new TabPage("运行状态");
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(35), WrapContents = false };
            panel.Controls.Add(stateLabel);
            panel.Controls.Add(stateDetail);
            panel.Controls.Add(lastCycleLabel);
            panel.Controls.Add(new Label
            {
                Text = "今日运行累计（本次程序启动）",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(45, 65, 82),
                Margin = new Padding(0, 24, 0, 8)
            });
            var statistics = new TableLayoutPanel { Width = 760, Height = 92, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            statistics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            statistics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            statistics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            statistics.Controls.Add(StatisticCard("监测次数", todayChecksLabel, Color.SteelBlue), 0, 0);
            statistics.Controls.Add(StatisticCard("转发成功", todayForwardedLabel, Color.SeaGreen), 1, 0);
            statistics.Controls.Add(StatisticCard("转发失败", todayFailedLabel, Color.Firebrick), 2, 0);
            panel.Controls.Add(statistics);
            var buttons = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 22, 0, 0) };
            startButton.Click += (s, e) => controller.Start();
            pauseButton.Click += async (s, e) => await controller.PauseAsync();
            buttons.Controls.Add(startButton);
            buttons.Controls.Add(pauseButton);
            panel.Controls.Add(buttons);
            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(780, 0),
                Margin = new Padding(0, 30, 0, 0),
                ForeColor = Color.DimGray,
                Text = "提示：关闭窗口后程序仍会在右下角托盘中运行。暂停期间不会检查邮件；恢复后会补查暂停期间收到的新邮件。"
            };
            panel.Controls.Add(note);
            page.Controls.Add(panel);
            return page;
        }

        private TabPage BuildRulesPage()
        {
            var page = new TabPage("转发规则");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rulesGrid.Columns.Add("Name", "规则名称");
            rulesGrid.Columns.Add("Enabled", "状态");
            rulesGrid.Columns.Add("Sender", "发件人邮箱");
            rulesGrid.Columns.Add("Keywords", "标题关键字（任一）");
            rulesGrid.Columns.Add("Recipients", "收件人");
            rulesGrid.Columns.Add("Prefix", "标题前缀");
            rulesGrid.Columns[0].Width = 140;
            rulesGrid.Columns[1].Width = 145;
            rulesGrid.Columns[2].Width = 190;
            rulesGrid.Columns[3].Width = 220;
            rulesGrid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            rulesGrid.Columns[5].Width = 130;
            rulesGrid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelectedRule(); };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 9, 10, 9), WrapContents = false };
            var add = ToolbarButton("新增规则", 135);
            var edit = ToolbarButton("编辑", 100);
            var toggle = ToolbarButton("启用/停用", 160);
            var delete = ToolbarButton("删除", 100);
            add.Click += (s, e) => EditRule(null);
            edit.Click += (s, e) => EditSelectedRule();
            toggle.Click += (s, e) => ToggleSelectedRule();
            delete.Click += (s, e) => DeleteSelectedRule();
            bar.Controls.AddRange(new Control[] { add, edit, toggle, delete });
            layout.Controls.Add(bar, 0, 0);
            layout.Controls.Add(rulesGrid, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildSettingsPage()
        {
            var page = new TabPage("邮箱设置");
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, Padding = new Padding(14), Dock = DockStyle.Top };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var protocol = Combo("IMAP", "POP3");
            var receiveSecurity = Combo("无加密", "自动", "SSL 直连", "STARTTLS");
            var smtpSecurity = Combo("无加密", "自动", "SSL 直连", "STARTTLS");
            AddSetting(table, "收信协议", "protocol", protocol, "收信服务器", "receiveHost", new TextBox());
            AddSetting(table, "收信端口", "receivePort", Number(1, 65535), "安全模式", "receiveSecurity", receiveSecurity);
            AddSetting(table, "OA/邮箱用户名", "receiveUser", new TextBox(), "OA/邮箱密码", "receivePassword", PasswordBox());
            AddSetting(table, "IMAP 文件夹", "imapFolder", new TextBox(), "监测间隔（分钟）", "interval", Number(1, 1440));
            AddSetting(table, "SMTP 服务器", "smtpHost", new TextBox(), "SMTP 端口", "smtpPort", Number(1, 65535));
            AddSetting(table, "SMTP 安全模式", "smtpSecurity", smtpSecurity, "发件人邮箱", "senderAddress", new TextBox());
            AddSetting(table, "发件人名称", "senderName", new TextBox(), "SMTP 用户名", "smtpUser", new TextBox());
            AddSetting(table, "SMTP 密码", "smtpPassword", PasswordBox(), "", "", new Label());

            var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            options.Controls.Add(Check("smtpAuth", "发信服务器需要登录"));
            options.Controls.Add(Check("reuseCredentials", "发信使用同一用户名和密码"));
            options.Controls.Add(Check("receiveInvalid", "忽略收信服务器证书错误（不推荐）"));
            options.Controls.Add(Check("smtpInvalid", "忽略发信服务器证书错误（不推荐）"));
            options.Controls.Add(Check("startup", "随 Windows 启动"));
            options.Controls.Add(Check("autoStart", "启动程序后自动监测"));
            options.Controls.Add(Check("markReadAfterAll", "所有匹配规则处理完成后，将原邮件设为已读（仅支持 IMAP）"));
            var row = table.RowCount++;
            table.Controls.Add(options, 0, row);
            table.SetColumnSpan(options, 4);

            var notices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            notices.Controls.Add(new Label { Text = "托盘通知：", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            notices.Controls.Add(Check("notifySuccess", "转发成功"));
            notices.Controls.Add(Check("notifyWarning", "需要注意"));
            notices.Controls.Add(Check("notifyError", "异常"));
            notices.Controls.Add(Check("notifyRecovery", "异常恢复"));
            row = table.RowCount++;
            table.Controls.Add(notices, 0, row);
            table.SetColumnSpan(notices, 4);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 8) };
            var save = ToolbarButton("保存设置", 130);
            var testReceive = ToolbarButton("测试收信连接", 175);
            var testSmtp = ToolbarButton("发送测试邮件", 175);
            var resetPosition = ToolbarButton("重新确定新邮件起点", 230);
            save.Click += SaveSettingsClicked;
            testReceive.Click += TestReceiveClicked;
            testSmtp.Click += TestSmtpClicked;
            resetPosition.Click += ResetMonitorPositionClicked;
            buttons.Controls.AddRange(new Control[] { save, testReceive, testSmtp, resetPosition });
            row = table.RowCount++;
            table.Controls.Add(buttons, 0, row);
            table.SetColumnSpan(buttons, 4);
            scroll.Controls.Add(table);
            page.Controls.Add(scroll);
            return page;
        }

        private TabPage BuildLogsPage()
        {
            var page = new TabPage("日志查询");
            var filterArea = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2, Padding = new Padding(8, 7, 8, 4), Margin = Padding.Empty };
            filterArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            filterArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            filters.Controls.AddRange(new Control[]
            {
                LabelFor("开始"), logFrom, LabelFor("结束"), logTo, LabelFor("规则"), logRule,
                LabelFor("状态"), logStatus, LabelFor("主题/发件人"), logSearch
            });
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 5, 0, 0), WrapContents = false };
            var query = ToolbarButton("查询", 90);
            var details = ToolbarButton("详情", 90);
            var export = ToolbarButton("导出 CSV", 115);
            var delivered = ToolbarButton("标记已送达", 150);
            var retry = ToolbarButton("确认重新发送", 165);
            query.Click += (s, e) => RefreshLogs();
            details.Click += (s, e) => ShowSelectedLog();
            export.Click += ExportLogsClicked;
            delivered.Click += (s, e) => UpdatePending(ProcessingStatus.Success, false);
            retry.Click += (s, e) => UpdatePending(ProcessingStatus.RetryableFailure, true);
            actions.Controls.AddRange(new Control[] { query, details, export, delivered, retry });
            filterArea.Controls.Add(filters, 0, 0);
            filterArea.Controls.Add(actions, 0, 1);

            logsGrid.Columns.Add("Time", "时间");
            logsGrid.Columns.Add("Level", "级别");
            logsGrid.Columns.Add("Event", "事件");
            logsGrid.Columns.Add("Rule", "规则");
            logsGrid.Columns.Add("Subject", "原主题");
            logsGrid.Columns.Add("Sender", "发件人");
            logsGrid.Columns.Add("Result", "结果");
            logsGrid.Columns[0].Width = 165;
            logsGrid.Columns[1].Width = 90;
            logsGrid.Columns[2].Width = 115;
            logsGrid.Columns[3].Width = 125;
            logsGrid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            logsGrid.Columns[5].Width = 160;
            logsGrid.Columns[6].Width = 135;
            logsGrid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowSelectedLog(); };
            page.Controls.Add(logsGrid);
            page.Controls.Add(filterArea);
            return page;
        }

        private void BuildTray()
        {
            tray.Icon = pausedIcon;
            tray.DoubleClick += (s, e) => ShowWindow();
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主界面", null, (s, e) => ShowWindow());
            monitorToggleMenuItem.Click += async (s, e) => await ToggleMonitoringAsync();
            menu.Items.Add(monitorToggleMenuItem);
            menu.Items.Add("规则配置", null, (s, e) => { ShowWindow(); tabs.SelectedIndex = 1; });
            menu.Items.Add("日志查询", null, (s, e) => { ShowWindow(); tabs.SelectedIndex = 3; RefreshLogs(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, async (s, e) => await ExitApplicationAsync());
            menu.Opening += (s, e) => UpdateMonitorToggleMenu();
            tray.ContextMenuStrip = menu;
            UpdateMonitorToggleMenu();
        }

        private async Task ToggleMonitoringAsync()
        {
            if (controller.IsMonitoring) await controller.PauseAsync();
            else controller.Start();
            UpdateMonitorToggleMenu();
        }

        private void UpdateMonitorToggleMenu()
        {
            monitorToggleMenuItem.Text = Localization.T(controller.IsMonitoring ? "暂停监测" : "启动监测");
        }

        private void LoadSettings()
        {
            var x = store.GetSettings();
            SetCombo("protocol", x.ReceiveProtocol == MailProtocol.Imap ? 0 : 1);
            SetText("receiveHost", x.ReceiveHost); SetNumber("receivePort", x.ReceivePort); SetCombo("receiveSecurity", (int)x.ReceiveSecurity);
            SetText("receiveUser", x.ReceiveUserName); SetText("receivePassword", TrySecret(() => x.ReceivePassword));
            SetText("imapFolder", x.ImapFolder); SetNumber("interval", x.PollIntervalMinutes);
            SetText("smtpHost", x.SmtpHost); SetNumber("smtpPort", x.SmtpPort); SetCombo("smtpSecurity", (int)x.SmtpSecurity);
            SetText("senderAddress", x.SenderAddress); SetText("senderName", x.SenderName);
            SetText("smtpUser", x.SmtpUserName); SetText("smtpPassword", TrySecret(() => x.SmtpPassword));
            SetCheck("smtpAuth", x.SmtpAuthenticate); SetCheck("reuseCredentials", x.SmtpReuseReceiveCredentials);
            SetCheck("receiveInvalid", x.ReceiveAllowInvalidCertificate); SetCheck("smtpInvalid", x.SmtpAllowInvalidCertificate);
            SetCheck("startup", x.StartWithWindows); SetCheck("autoStart", x.AutoStartMonitoring);
            SetCheck("markReadAfterAll", x.MarkReadAfterAllRulesCompleted);
            SetCheck("notifySuccess", x.NotifySuccess); SetCheck("notifyWarning", x.NotifyWarning);
            SetCheck("notifyError", x.NotifyError); SetCheck("notifyRecovery", x.NotifyRecovery);
        }

        private AppSettings ReadSettings()
        {
            var x = store.GetSettings();
            x.ReceiveProtocol = ComboIndex("protocol") == 0 ? MailProtocol.Imap : MailProtocol.Pop3;
            x.ReceiveHost = TextOf("receiveHost").Trim(); x.ReceivePort = NumberOf("receivePort"); x.ReceiveSecurity = (SocketSecurityMode)ComboIndex("receiveSecurity");
            x.ReceiveUserName = TextOf("receiveUser").Trim(); x.ReceivePassword = TextOf("receivePassword");
            x.ImapFolder = TextOf("imapFolder").Trim(); x.PollIntervalMinutes = NumberOf("interval");
            x.SmtpHost = TextOf("smtpHost").Trim(); x.SmtpPort = NumberOf("smtpPort"); x.SmtpSecurity = (SocketSecurityMode)ComboIndex("smtpSecurity");
            x.SenderAddress = TextOf("senderAddress").Trim(); x.SenderName = TextOf("senderName").Trim();
            x.SmtpUserName = TextOf("smtpUser").Trim(); x.SmtpPassword = TextOf("smtpPassword");
            x.SmtpAuthenticate = Checked("smtpAuth"); x.SmtpReuseReceiveCredentials = Checked("reuseCredentials");
            x.ReceiveAllowInvalidCertificate = Checked("receiveInvalid"); x.SmtpAllowInvalidCertificate = Checked("smtpInvalid");
            x.StartWithWindows = Checked("startup"); x.AutoStartMonitoring = Checked("autoStart");
            x.MarkReadAfterAllRulesCompleted = Checked("markReadAfterAll");
            x.NotifySuccess = Checked("notifySuccess"); x.NotifyWarning = Checked("notifyWarning");
            x.NotifyError = Checked("notifyError"); x.NotifyRecovery = Checked("notifyRecovery");
            return x;
        }

        private void SaveSettingsClicked(object sender, EventArgs e)
        {
            try
            {
                var old = store.GetSettings();
                var settings = ReadSettings();
                MailMonitorService.ValidateSettings(settings);
                MailMonitorService.ValidateMailboxFeatures(settings, store.GetRules());
                var ownAddresses = RuleUtilities.GetOwnMailboxAddresses(settings);
                foreach (var rule in store.GetRules()) RuleUtilities.EnsureNoSelfRecipient(rule.Recipients, ownAddresses);
                var oldKey = SafeAccountKey(old);
                var newKey = MailMonitorService.BuildAccountKey(settings);
                store.SaveSettings(settings);
                StartupManager.SetEnabled(settings.StartWithWindows);
                var accountChanged = !string.IsNullOrEmpty(oldKey) && oldKey != newKey;
                if (accountChanged) store.ResetCursorForAccount(newKey);
                MessageBox.Show(accountChanged
                        ? "设置已保存。由于收信账户已经更换，程序会先记住当前收件箱中的邮件，只处理之后收到的新邮件。"
                        : "邮箱设置已保存。",
                    "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(UserMessages.ForException(ex), "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private async void TestReceiveClicked(object sender, EventArgs e)
        {
            try
            {
                UseWaitCursor = true;
                await new MailMonitorService(store).TestReceiveConnectionAsync(ReadSettings());
                MessageBox.Show("收信连接和身份验证成功。", "测试成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(UserMessages.ForException(ex), "收信测试未通过", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { UseWaitCursor = false; }
        }

        private async void TestSmtpClicked(object sender, EventArgs e)
        {
            var address = PromptDialog.Show(this, "请输入测试邮件收件人地址：", "发送测试邮件", TextOf("senderAddress"));
            if (string.IsNullOrWhiteSpace(address)) return;
            try
            {
                UseWaitCursor = true;
                await new MailMonitorService(store).SendTestMessageAsync(ReadSettings(), address.Trim());
                MessageBox.Show("发信设置可用，测试邮件已经提交给发信服务器。", "发送成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(UserMessages.ForException(ex), "测试邮件未发出", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { UseWaitCursor = false; }
        }

        private async void ResetMonitorPositionClicked(object sender, EventArgs e)
        {
            try
            {
                var settings = store.GetSettings();
                MailMonitorService.ValidateSettings(settings);
                var answer = MessageBox.Show(
                    "程序将把当前收件箱中的邮件视为已有邮件，不会转发它们；只处理重新启动监测后收到的新邮件。\r\n\r\n确定要继续吗？",
                    "重新确定新邮件起点", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
                await controller.PauseAsync();
                store.ResetCursorForAccount(MailMonitorService.BuildAccountKey(settings));
                foreach (var rule in store.GetRules().Where(x => x.Enabled))
                {
                    rule.NeedsBaseline = true;
                    store.SaveRule(rule);
                }
                store.AddLog(LogLevel.Information, "重新确定新邮件起点", null, "", "", "", "等待首次监测",
                    "用户已重新设置监测起点。下次启动监测时，当前收件箱中的邮件不会被转发。 ");
                RefreshRules();
                RefreshLogs();
                MessageBox.Show("新的起点将在下次启动监测时确定。请返回“运行状态”并点击“启动监测”。",
                    "操作完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UserMessages.ForException(ex), "无法重新确定起点", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshRules()
        {
            rulesGrid.Rows.Clear();
            foreach (var rule in store.GetRules())
            {
                var ruleStatus = rule.Enabled ? (rule.NeedsBaseline ? "等待首次监测" : "已启用") : "已停用";
                if (rule.OnlyUnread) ruleStatus += "；仅未读";
                var index = rulesGrid.Rows.Add(rule.Name, Localization.T(ruleStatus),
                    string.IsNullOrWhiteSpace(rule.SenderAddress) ? Localization.T("不限发件人") : rule.SenderAddress,
                    string.Join("；", rule.Keywords), string.Join("；", rule.Recipients), rule.SubjectPrefix);
                rulesGrid.Rows[index].Tag = rule.Id;
            }
        }

        private void EditSelectedRule()
        {
            var id = SelectedTag(rulesGrid);
            if (id == null) return;
            EditRule(store.GetRule(id));
        }

        private void EditRule(ForwardRule rule)
        {
            var settings = store.GetSettings();
            using (var dialog = new RuleDialog(rule, RuleUtilities.GetOwnMailboxAddresses(settings), settings.ReceiveProtocol))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                store.SaveRule(dialog.Result);
                RefreshRules();
            }
        }

        private void ToggleSelectedRule()
        {
            var id = SelectedTag(rulesGrid);
            var rule = id == null ? null : store.GetRule(id);
            if (rule == null) return;
            var enabling = !rule.Enabled;
            if (enabling)
            {
                try
                {
                    var settings = store.GetSettings();
                    RuleUtilities.EnsureNoSelfRecipient(rule.Recipients, RuleUtilities.GetOwnMailboxAddresses(settings));
                    MailMonitorService.ValidateMailboxFeatures(settings, new[] { rule });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(UserMessages.ForException(ex), "无法启用规则", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            rule.Enabled = enabling;
            if (enabling) rule.NeedsBaseline = true;
            store.SaveRule(rule);
            RefreshRules();
        }

        private void DeleteSelectedRule()
        {
            var id = SelectedTag(rulesGrid);
            if (id == null) return;
            if (MessageBox.Show("确定删除所选规则吗？历史日志和去重记录仍会保留。", "删除规则", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            store.DeleteRule(id);
            RefreshRules();
        }

        private void RefreshLogs()
        {
            var filter = new LogFilter
            {
                FromLocal = logFrom.Checked ? logFrom.Value.Date : (DateTime?)null,
                ToLocal = logTo.Checked ? logTo.Value.Date.AddDays(1).AddTicks(-1) : (DateTime?)null,
                RuleText = logRule.Text.Trim(), StatusText = Localization.ToSimplified(logStatus.Text.Trim()), SearchText = logSearch.Text.Trim()
            };
            logsGrid.Rows.Clear();
            foreach (var log in store.QueryLogs(filter))
            {
                var index = logsGrid.Rows.Add(log.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    Localization.T(LevelText(log.Level)), Localization.T(UserMessages.FriendlyEvent(log.EventType)), log.RuleName, log.Subject, log.Sender,
                    Localization.T(UserMessages.FriendlyResult(log.Result)));
                logsGrid.Rows[index].Tag = log.Id;
            }
        }

        private void ShowSelectedLog()
        {
            var idText = SelectedTag(logsGrid);
            int id;
            if (!int.TryParse(idText, out id)) return;
            var log = store.GetLog(id);
            if (log == null) return;
            MessageBox.Show("时间：" + log.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                "\r\n级别：" + LevelText(log.Level) + "\r\n事件：" + UserMessages.FriendlyEvent(log.EventType) + "\r\n规则：" + log.RuleName +
                "\r\n主题：" + log.Subject + "\r\n发件人：" + log.Sender + "\r\n收件人：" + log.Recipients +
                "\r\n结果：" + UserMessages.FriendlyResult(log.Result) + "\r\n\r\n详情：\r\n" + UserMessages.FriendlyDetail(log.Detail),
                "日志详情", MessageBoxButtons.OK, log.Level == LogLevel.Error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }

        private void UpdatePending(ProcessingStatus status, bool retry)
        {
            var idText = SelectedTag(logsGrid);
            int id;
            if (!int.TryParse(idText, out id)) return;
            var log = store.GetLog(id);
            var record = log == null || string.IsNullOrWhiteSpace(log.ProcessingId) ? null : store.GetProcessing(log.ProcessingId);
            if (record == null || record.Status != ProcessingStatus.PendingConfirmation)
            {
                MessageBox.Show("这条日志不需要人工操作。只有“无法确认是否已发送”的邮件才能在这里确认。",
                    "无需处理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var message = retry ? "重新发送可能造成重复邮件。确认要在下一轮监测中重试吗？" : "确认将此记录标记为已送达，并且以后不再自动发送吗？";
            if (MessageBox.Show(message, "人工确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!store.SetProcessingStatus(log.ProcessingId, status)) MessageBox.Show("找不到这封邮件的信息。请刷新日志后再试。", "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
            {
                store.AddLog(LogLevel.Information, "人工处理", record == null ? null : store.GetRule(record.RuleId),
                    record == null ? log.Subject : record.Subject, record == null ? log.Sender : record.Sender,
                    record == null ? log.Recipients : record.Recipients, retry ? "已确认重试" : "已标记送达",
                    retry ? "用户确认承担可能重复的风险，并将邮件加入安全重试队列。" : "用户确认邮件已经送达，不再自动发送。", log.ProcessingId);
                RefreshLogs();
            }
        }

        private void ExportLogsClicked(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog { Filter = Localization.T("CSV 文件 (*.csv)|*.csv"), FileName = Localization.T("邮件转发日志_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var logs = store.QueryLogs(new LogFilter(), 100000);
                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine(Localization.T("时间,级别,事件,规则,原主题,发件人,收件人,结果,详情"));
                    foreach (var x in logs) writer.WriteLine(string.Join(",", new[]
                    {
                        Csv(x.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")), Csv(Localization.T(LevelText(x.Level))), Csv(Localization.T(UserMessages.FriendlyEvent(x.EventType))),
                        Csv(x.RuleName), Csv(x.Subject), Csv(x.Sender), Csv(x.Recipients), Csv(Localization.T(UserMessages.FriendlyResult(x.Result))), Csv(Localization.T(UserMessages.FriendlyDetail(x.Detail)))
                    }));
                }
                MessageBox.Show("日志已导出。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ControllerStateChanged(object sender, MonitorStateChangedEventArgs e)
        {
            Ui(() => ApplyState(e.State, e.Message, e.IsRecovery));
        }

        private void ApplyState(MonitorState state, string message, bool recovery)
        {
            stateLabel.Text = Localization.T(state == MonitorState.Running ? "运行中" : state == MonitorState.Error ? "需要处理" : "已暂停");
            stateLabel.ForeColor = state == MonitorState.Running ? Color.SeaGreen : state == MonitorState.Error ? Color.Firebrick : Color.Gray;
            stateDetail.Text = Localization.T(message);
            UpdateRuntimeStatistics();
            var icon = state == MonitorState.Running ? runningIcon : state == MonitorState.Error ? errorIcon : pausedIcon;
            tray.Icon = icon;
            Icon = icon;
            tray.Text = Truncate(Localization.ProductTitle + " - " + stateLabel.Text, 63);
            UpdateMonitorToggleMenu();
            startButton.Enabled = state != MonitorState.Running;
            pauseButton.Enabled = state == MonitorState.Running || state == MonitorState.Error;
            var settings = store.GetSettings();
            if (state == MonitorState.Error && settings.NotifyError && lastNotifiedError != message)
            {
                Notify("邮件监测需要处理", message, ToolTipIcon.Error);
                lastNotifiedError = message;
            }
            else if (recovery && settings.NotifyRecovery)
            {
                Notify("邮件监测已恢复", message, ToolTipIcon.Info);
                lastNotifiedError = null;
            }
            if (state == MonitorState.Running && !recovery) lastNotifiedError = null;
        }

        private void Notify(string title, string text, ToolTipIcon icon)
        {
            tray.BalloonTipTitle = Localization.T(title);
            tray.BalloonTipText = Truncate(Localization.T(text), 240);
            tray.BalloonTipIcon = icon;
            tray.ShowBalloonTip(5000);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowClose) return;
            e.Cancel = true;
            HideToTray();
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void ShowWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private async Task ExitApplicationAsync()
        {
            allowClose = true;
            await controller.PauseAsync();
            tray.Visible = false;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Dispose(); runningIcon.Dispose(); pausedIcon.Dispose(); errorIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private static DataGridView CreateGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false, AutoGenerateColumns = false, BackgroundColor = Color.White,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 44,
                RowTemplate = { Height = 34 }
            };
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 4, 5, 4);
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.Padding = new Padding(4, 3, 4, 3);
            return grid;
        }

        private static Label StatisticValueLabel()
        {
            return new Label
            {
                Text = "0",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
        }

        private static Control StatisticCard(string title, Label value, Color color)
        {
            value.ForeColor = color;
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(14, 9, 14, 9), Margin = new Padding(0, 0, 10, 0), BackColor = Color.FromArgb(245, 247, 249) };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.DimGray, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) }, 0, 0);
            card.Controls.Add(value, 0, 1);
            return card;
        }

        private void UpdateRuntimeStatistics()
        {
            var statistics = controller.GetRuntimeStatistics();
            todayChecksLabel.Text = statistics.CheckCount.ToString("N0");
            todayForwardedLabel.Text = statistics.ForwardedCount.ToString("N0");
            todayFailedLabel.Text = statistics.FailedCount.ToString("N0");
        }

        private void AddSetting(TableLayoutPanel table, string l1, string k1, Control c1, string l2, string k2, Control c2)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            table.Controls.Add(LabelFor(l1), 0, row); c1.Dock = DockStyle.Fill; table.Controls.Add(c1, 1, row); if (!string.IsNullOrEmpty(k1)) settingControls[k1] = c1;
            table.Controls.Add(LabelFor(l2), 2, row); c2.Dock = DockStyle.Fill; table.Controls.Add(c2, 3, row); if (!string.IsNullOrEmpty(k2)) settingControls[k2] = c2;
        }

        private CheckBox Check(string key, string text)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(5, 5, 12, 5) };
            settingControls[key] = box;
            return box;
        }

        private static Label LabelFor(string text) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 7, 4, 4) };
        private static ComboBox Combo(params string[] items) { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; c.Items.AddRange(items); c.SelectedIndex = 0; return c; }
        private static NumericUpDown Number(int min, int max) => new NumericUpDown { Minimum = min, Maximum = max, ThousandsSeparator = false };
        private static TextBox PasswordBox() => new TextBox { UseSystemPasswordChar = true };
        private static Button ToolbarButton(string text, int width) => new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, 40),
            Padding = new Padding(12, 5, 12, 5),
            Margin = new Padding(4, 2, 4, 2)
        };
        private void SetText(string k, string v) => settingControls[k].Text = v ?? "";
        private string TextOf(string k) => settingControls[k].Text;
        private void SetNumber(string k, int v) { var n = (NumericUpDown)settingControls[k]; n.Value = Math.Min(n.Maximum, Math.Max(n.Minimum, v)); }
        private int NumberOf(string k) => Decimal.ToInt32(((NumericUpDown)settingControls[k]).Value);
        private void SetCombo(string k, int v) { var c = (ComboBox)settingControls[k]; c.SelectedIndex = Math.Max(0, Math.Min(c.Items.Count - 1, v)); }
        private int ComboIndex(string k) => ((ComboBox)settingControls[k]).SelectedIndex;
        private void SetCheck(string k, bool v) => ((CheckBox)settingControls[k]).Checked = v;
        private bool Checked(string k) => ((CheckBox)settingControls[k]).Checked;
        private static string SelectedTag(DataGridView grid) => grid.SelectedRows.Count == 0 || grid.SelectedRows[0].Tag == null ? null : Convert.ToString(grid.SelectedRows[0].Tag, CultureInfo.InvariantCulture);
        private static string TrySecret(Func<string> getter) { try { return getter(); } catch { return ""; } }
        private static string SafeAccountKey(AppSettings s) { try { return string.IsNullOrWhiteSpace(s.ReceiveHost) ? "" : MailMonitorService.BuildAccountKey(s); } catch { return ""; } }
        private static bool IsConfigurationUsable(AppSettings s) { try { MailMonitorService.ValidateSettings(s); return true; } catch { return false; } }
        private static string LevelText(LogLevel x) => x == LogLevel.Error ? "错误" : x == LogLevel.Warning ? "警告" : "信息";
        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
        private void Ui(Action action) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(action); else action(); }
    }
}
