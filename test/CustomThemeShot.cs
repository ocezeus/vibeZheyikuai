using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 自建主题：背景/进度条分开 + 编辑对话框 + 卡片实拍
class CustomThemeShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        var store = new SettingsStore();
        var s = store.Load();
        var ct = new CustomTheme();
        ct.id = "my-test";
        ct.name = "夜航橙";
        ct.bg = "#101C2E";
        ct.bar = "#FFB454";
        ct.accent = "#FFB454";
        s.customThemes.Add(ct);
        s.ui.theme = ct.id;
        Themes.SetCustom(s.customThemes);
        var t = Themes.Get(ct.id);
        Console.WriteLine("theme bgTop=" + t.bgTop + " accent=" + t.accent + " text=" + t.textMain);

        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            form.Size = new Size(420, 282);
            var snap = new AttendanceSnapshot(); snap.ok = true; snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
            Set(form, "snapshot", snap);
            Call(form, "Recompute");
            using (var bmp = new Bitmap(420, 282))
            {
                bmp.SetResolution(144, 144);
                using (var g = Graphics.FromImage(bmp))
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                var frame = (Bitmap)Get(form, "frame");
                if (frame != null) frame.Save(Path.Combine(dir, "custom-theme-card.png"));
            }
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }

        using (var dlg = new ColorWheelForm("编辑主题", ct.name, ColorTranslator.FromHtml(ct.bg), ColorTranslator.FromHtml(ct.bar)))
        {
            dlg.Show();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            using (var bmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Path.Combine(dir, "color-wheel.png"));
            }
            dlg.Close();
        }

        using (var dlg = new SettingsForm(store, s))
        {
            dlg.Show();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            var tabs = (TabControl)Get(dlg, "tabs");
            tabs.SelectedIndex = 3;
            Application.DoEvents();
            Thread.Sleep(100);
            Application.DoEvents();
            using (var bmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Path.Combine(dir, "settings-custom.png"));
            }
            dlg.Close();
        }
        Console.WriteLine("done");
    }
}
