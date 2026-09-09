using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace MailForwarderAssistant
{
    public static class StartupManager
    {
        private const string ValueName = "MailForwarderAssistant";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) throw new InvalidOperationException("无法设置“随 Windows 启动”。请确认当前 Windows 用户有权修改自己的启动设置。 ");
                if (enabled)
                {
                    var path = Process.GetCurrentProcess().MainModule.FileName;
                    key.SetValue(ValueName, "\"" + path + "\" --autostart");
                }
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
