using System;
using System.Threading;
using System.Windows.Forms;
using MessageBox = MailForwarderAssistant.LocalizedMessageBox;

namespace MailForwarderAssistant
{
    internal static class Program
    {
        private const string MutexName = "Local\\MailForwarderAssistant-56D3BE69-E10B-4F90-A522-170652786E04";

        [STAThread]
        private static void Main()
        {
            Localization.InitializeFromPreference();
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    MessageBox.Show("邮件转发助手已经在运行，请查看任务栏托盘。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => ShowFatal(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowFatal(e.ExceptionObject as Exception);

                using (var store = new DataStore())
                using (var controller = new AppController(store))
                {
                    Localization.SetLanguage(store.GetSettings().UseTraditionalChinese, false);
                    store.RecoverInterruptedSends();
                    store.CleanupOldLogs(180);
                    Application.Run(new MainForm(store, controller));
                }
            }
        }

        private static void ShowFatal(Exception exception)
        {
            try
            {
                MessageBox.Show(UserMessages.ForException(exception),
                    "程序遇到问题", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
