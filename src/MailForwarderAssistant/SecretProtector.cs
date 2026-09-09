using System;
using System.Security.Cryptography;
using System.Text;

namespace MailForwarderAssistant
{
    public static class SecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MailForwarderAssistant.v1");

        public static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("当前 Windows 用户无法读取之前保存的密码。请在“邮箱设置”中重新输入密码并保存。");
            }
        }
    }
}
