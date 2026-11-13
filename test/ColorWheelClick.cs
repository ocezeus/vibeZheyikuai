using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 真点一下调色窗的 SV 方块，确认鼠标事件有没有落到控件上。
class ColorWheelClick
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    static BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        using (var dlg = new ColorWheelForm("测试", "测试", Color.FromArgb(16, 28, 46), Color.FromArgb(255, 180, 84)))
        {
            dlg.TopMost = true;
            dlg.Show();
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(40); }
            var sv = (Control)Get(dlg, "sv");
            var hex = (Label)Get(dlg, "lblHex");
            Console.WriteLine("sv bounds=" + sv.Bounds + " visible=" + sv.Visible + " enabled=" + sv.Enabled
                + " screen=" + sv.PointToScreen(new Point(sv.Width / 2, sv.Height / 2)) + " hex=" + hex.Text.Replace("\r\n", " | "));
            Point p = sv.PointToScreen(new Point((int)(sv.Width * 0.75), (int)(sv.Height * 0.3)));
            SetCursorPos(p.X, p.Y);
            Thread.Sleep(120);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(40); }
            Console.WriteLine("after click hex=" + hex.Text.Replace("\r\n", " | "));
            Console.WriteLine("sat/val before=" + Get(sv, "Sat") + "/" + Get(sv, "Val"));
            sv.GetType().GetMethod("Pick", F).Invoke(sv, new object[] { new Point(sv.Width / 2, sv.Height / 2) });
            Console.WriteLine("sat/val after=" + Get(sv, "Sat") + "/" + Get(sv, "Val") + " hex=" + hex.Text.Replace("\r\n", " | "));
            dlg.GetType().GetMethod("SyncFromHsv", F).Invoke(dlg, null);
            Console.WriteLine("after sync hex=" + hex.Text.Replace("\r\n", " | "));
            sv.GetType().GetMethod("OnMouseDown", F).Invoke(sv, new object[] { new MouseEventArgs(MouseButtons.Left, 1, sv.Width / 2, sv.Height / 2, 0) });
            for (int i = 0; i < 3; i++) { Application.DoEvents(); Thread.Sleep(40); }
            Console.WriteLine("after direct hex=" + hex.Text.Replace("\r\n", " | "));
            dlg.Close();
        }
    }
}
