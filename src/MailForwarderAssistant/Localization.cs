using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MailForwarderAssistant
{
    public static class Localization
    {
        private const uint SimplifiedChinese = 0x02000000;
        private const uint TraditionalChinese = 0x04000000;
        private const string TraditionalPreference = "zh-Hant";
        private const string SimplifiedPreference = "zh-Hans";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int LCMapStringEx(string localeName, uint mapFlags, string source, int sourceLength,
            StringBuilder destination, int destinationLength, IntPtr versionInformation, IntPtr reserved, IntPtr sortHandle);

        public static bool UseTraditionalChinese { get; private set; }
        public static string ProductTitle => T("邮件转发助手 v1.0.10");

        public static void InitializeFromPreference()
        {
            try
            {
                var path = PreferencePath();
                if (File.Exists(path))
                    UseTraditionalChinese = string.Equals(File.ReadAllText(path).Trim(), TraditionalPreference, StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }

        public static void SetLanguage(bool useTraditionalChinese, bool savePreference)
        {
            UseTraditionalChinese = useTraditionalChinese;
            if (!savePreference) return;
            var path = PreferencePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, useTraditionalChinese ? TraditionalPreference : SimplifiedPreference, Encoding.UTF8);
        }

        public static string T(string value)
        {
            var converted = UseTraditionalChinese ? ConvertChinese(value, TraditionalChinese) : ConvertChinese(value, SimplifiedChinese);
            return UseTraditionalChinese ? NormalizeTraditionalTerms(converted) : NormalizeSimplifiedTerms(converted);
        }

        public static string ToSimplified(string value)
        {
            return NormalizeSimplifiedTerms(ConvertChinese(value, SimplifiedChinese));
        }

        public static void ApplyTo(Control root)
        {
            if (root == null) return;
            ApplyControl(root);
        }

        public static void ApplyTo(ToolStrip strip)
        {
            if (strip == null) return;
            ApplyItems(strip.Items);
        }

        private static void ApplyControl(Control control)
        {
            if (!(control is TextBoxBase) && !(control is NumericUpDown) && !(control is DateTimePicker) && !(control is ComboBox))
                control.Text = T(control.Text);

            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.BeginUpdate();
                try
                {
                    for (var index = 0; index < combo.Items.Count; index++)
                        if (combo.Items[index] is string) combo.Items[index] = T((string)combo.Items[index]);
                }
                finally { combo.EndUpdate(); }
            }

            var grid = control as DataGridView;
            if (grid != null)
                foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = T(column.HeaderText);

            var strip = control as ToolStrip;
            if (strip != null) ApplyItems(strip.Items);
            foreach (Control child in control.Controls) ApplyControl(child);
        }

        private static void ApplyItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.Text = T(item.Text);
                var menu = item as ToolStripMenuItem;
                if (menu != null) ApplyItems(menu.DropDownItems);
            }
        }

        private static string ConvertChinese(string value, uint mapFlag)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            try
            {
                var required = LCMapStringEx("zh-CN", mapFlag, value, -1, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (required <= 0) return value;
                var output = new StringBuilder(required);
                var written = LCMapStringEx("zh-CN", mapFlag, value, -1, output, output.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                return written <= 0 ? value : output.ToString().TrimEnd('\0');
            }
            catch { return value; }
        }

        private static string NormalizeTraditionalTerms(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return value
                .Replace("日志", "日誌")
                .Replace("后", "後")
                .Replace("這里", "這裡")
                .Replace("里面", "裡面");
        }

        private static string NormalizeSimplifiedTerms(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return value
                .Replace("日誌", "日志")
                .Replace("後", "后")
                .Replace("這裡", "这里")
                .Replace("裡面", "里面");
        }

        private static string PreferencePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ui-language.txt");
        }
    }

    internal static class LocalizedMessageBox
    {
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return MessageBox.Show(Localization.T(text), Localization.T(caption), buttons, icon);
        }
    }
}
