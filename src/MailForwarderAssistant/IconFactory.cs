using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace MailForwarderAssistant
{
    public static class IconFactory
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create(Color color)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            using (var borderPen = new Pen(Color.FromArgb(180, 45, 45, 45), 1))
            using (var envelopePen = new Pen(Color.White, 2.6F))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.FillRectangle(brush, 1, 1, 30, 30);
                graphics.DrawRectangle(borderPen, 1, 1, 30, 30);
                graphics.DrawRectangle(envelopePen, 6, 8, 20, 16);
                graphics.DrawLine(envelopePen, 7, 9, 16, 17);
                graphics.DrawLine(envelopePen, 16, 17, 25, 9);
                var handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }
}
