using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 胶囊菜单退场逐帧：同一张「修改主题」面板按退场进度 0 → 1 渲染成 13 帧，
// 叠在浅色渐变底上，用来核对「淡出 + 往卡片方向缩回」是不是顺的。
// 用法：BubbleExitShot.exe [输出目录]
class BubbleExitShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName);
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 99;
        }
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = "midnight";
        s.ui.alwaysOnTop = true;

        var card = new Rectangle(40, 300, 336, 102);
        using (var m = new BubbleMenuForm(s, Themes.Get("midnight"), card, true, null, null, null, null, null))
        {
            Set(m, "openedAt", Environment.TickCount - 5000);
            Call(m, "OpenItem", 1);
            int extra = (int)Call(m, "S", (float)BubbleMenuForm.ExtraHDip(BubbleAction.Theme));
            Set(m, "animating", false);
            Set(m, "openExtra", (double)extra);
            Set(m, "openTo", (double)extra);
            Call(m, "LayoutWindow");

            string dir = args != null && args.Length > 0 ? args[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "bubble-exit");
            Directory.CreateDirectory(dir);

            const int frames = 13;
            for (int i = 0; i < frames; i++)
            {
                double p = i / (double)(frames - 1);
                Set(m, "closingForm", true);
                Set(m, "exitAt", Environment.TickCount - (int)Math.Round(BubbleMenuForm.ExitMs * p));
                using (var bmp = new Bitmap(m.Width + 40, m.Height + 40, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        using (var br = new LinearGradientBrush(new Rectangle(0, 0, bmp.Width, bmp.Height),
                            Color.FromArgb(242, 245, 250), Color.FromArgb(214, 222, 234), 120f))
                            g.FillRectangle(br, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        g.TranslateTransform(20, 20);
                        Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                    }
                    bmp.Save(Path.Combine(dir, "f" + i.ToString("000") + ".png"), ImageFormat.Png);
                }
            }
            Console.WriteLine("ok " + dir + "  " + frames + " 帧  菜单 " + m.Width + "x" + m.Height);
        }
        Application.DoEvents();
    }

    static void Set(object o, string name, object v)
    {
        o.GetType().GetField(name, F).SetValue(o, v);
    }

    static object Call(object o, string name, params object[] a)
    {
        var m = o.GetType().GetMethod(name, F);
        if (m == null) throw new Exception("no method " + name);
        return m.Invoke(o, a);
    }
}
