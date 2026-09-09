using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace MailForwarderAssistant
{
    public static class UserMessages
    {
        public static string FriendlyEvent(string value)
        {
            switch (value ?? "")
            {
                case "建立基线": return "首次监测准备";
                case "待人工确认": return "请确认发送结果";
                case "监测异常": return "暂时无法检查邮箱";
                case "监测停止": return "监测已停止";
                case "重试失败": return "无法再次发送";
                default: return value ?? "";
            }
        }

        public static string FriendlyResult(string value)
        {
            switch (value ?? "")
            {
                case "发送结果不明确": return "无法确认是否已发送";
                case "等待自动重试": return "稍后自动重试";
                case "异常": return "需要处理";
                case "失败": return "未成功";
                default: return value ?? "";
            }
        }

        public static string FriendlyDetail(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "没有更多信息。";
            if (value.IndexOf("UIDVALIDITY", StringComparison.OrdinalIgnoreCase) >= 0)
                return "邮箱服务器重新编排了邮件编号。为避免旧邮件被再次转发，请在“邮箱设置”中点击“重新确定新邮件起点”。";
            if (value.IndexOf("不支持 UIDL", StringComparison.OrdinalIgnoreCase) >= 0)
                return "当前 POP3 收信方式无法取得邮件唯一编号。请改用 IMAP，或联系邮箱管理员确认 POP3 的 UIDL 功能。";
            if (value.IndexOf("IMAP UID", StringComparison.OrdinalIgnoreCase) >= 0)
                return "无法找到这封邮件在服务器中的位置，因此不能自动重试。请人工确认是否需要再次发送。";
            if (value.IndexOf("SMTP 服务器接受", StringComparison.OrdinalIgnoreCase) >= 0)
                return "邮件已经提交给发信服务器。";
            return value;
        }

        public static string ForException(Exception exception)
        {
            var aggregate = exception as AggregateException;
            if (aggregate != null) exception = aggregate.Flatten().InnerExceptions.Count > 0 ? aggregate.Flatten().InnerExceptions[0] : exception;
            if (exception == null) return "操作没有完成，请稍后重试。";

            var smtp = exception as SmtpCommandException;
            if (smtp != null)
            {
                switch (smtp.ErrorCode)
                {
                    case SmtpErrorCode.SenderNotAccepted:
                        return "发信服务器不接受当前发件人地址。请检查“发件人邮箱”，或联系邮箱管理员确认允许使用的地址。";
                    case SmtpErrorCode.RecipientNotAccepted:
                        return "发信服务器不接受一个或多个收件人地址。请检查转发规则中的收件人。";
                    case SmtpErrorCode.MessageNotAccepted:
                        return "发信服务器拒绝了这封邮件。可能是邮件过大、附件受限或服务器策略不允许，请联系邮箱管理员核实。";
                    default:
                        return "发信服务器拒绝了本次操作。请检查发信设置和邮件地址，仍无法解决时请联系邮箱管理员。";
                }
            }

            if (exception is MailKit.Security.AuthenticationException || exception is ServiceNotAuthenticatedException)
                return "邮箱登录失败。请检查用户名和密码是否正确，并确认该账户允许使用当前收信或发信方式。";
            if (exception is SslHandshakeException)
                return "无法与邮箱服务器建立安全连接。请检查安全模式是否正确；如果公司使用内部证书，请联系管理员确认后再启用“忽略证书错误”。";
            if (exception is SocketException)
                return "无法连接邮箱服务器。请确认电脑已连接公司内网，并检查服务器地址和端口。";
            if (exception is ServiceNotConnectedException)
                return "与邮箱服务器的连接已经断开。程序稍后会自动重试，请确认公司内网连接正常。";
            if (exception is CryptographicException)
                return "无法读取已保存的密码。请在“邮箱设置”中重新输入密码并保存。";
            if (exception is FatalMonitorException || exception is FormatException || exception is InvalidOperationException)
                return exception.Message;
            if (exception is IOException || exception is ProtocolException)
                return "与邮箱服务器通信时连接中断。程序稍后会自动重试，请确认公司内网连接稳定。";

            return "操作没有成功。请检查邮箱设置和公司内网连接后重试；如果问题持续，请在“日志查询”中查看发生时间并联系管理员。";
        }
    }
}
