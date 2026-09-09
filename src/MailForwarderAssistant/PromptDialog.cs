using System.Drawing;
using System.Windows.Forms;

namespace MailForwarderAssistant
{
    internal static class PromptDialog
    {
        public static string Show(IWin32Window owner, string prompt, string title, string initialValue)
        {
            using (var form = new Form
            {
                Text = title,
                Width = 480,
                Height = 170,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Font = new Font("Microsoft YaHei UI", 9F)
            })
            {
                var label = new Label { Left = 15, Top = 16, Width = 430, Text = prompt };
                var input = new TextBox { Left = 15, Top = 45, Width = 430, Text = initialValue ?? "" };
                var ok = new Button { Text = "确定", Left = 272, Top = 80, Width = 82, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Left = 363, Top = 80, Width = 82, DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { label, input, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                Localization.ApplyTo(form);
                return form.ShowDialog(owner) == DialogResult.OK ? input.Text : "";
            }
        }
    }
}
