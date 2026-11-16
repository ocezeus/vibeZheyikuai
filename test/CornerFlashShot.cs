using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 未读「左上角每 5 秒快闪两下」出图探针：
// 把 noteBlinkAt 拨到不同 elapsed，逐帧渲染展开/收起卡片，拼成对照图。
class CornerFlashShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    static object Field(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static void SetField(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Prop(object o, string n) { return o.GetType().GetProperty(n, F).GetValue(o, null); }
    static object Call(object o, string n, params object[] a)
    {
        foreach (var m in o.GetType().GetMethods(F))
            if (m.Name == n && m.GetParameters().Length == (a == null ? 0 : a.Length)) return m.Invoke(o, a);
        throw new MissingMethodException(o.GetType().Name + "." + n);
    }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        Application.EnableVisualStyles();

        var store = new SettingsStore();
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.mode = "fixed";
        s.startTime = "09:00";
        s.endTime = "18:00";
        s.ui.skin = "default";
        s.ui.theme = "midnight";
        s.note.id = "48372";
        s.note.AddContact("62015", "阿哲");
        s.note.Append(new NoteMessage("62015", "48372", "在吗？这条是小纸条未读", 1731400000), true);
        s.Normalize();

        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        snap.fetchedAt = DateTime.Now;

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "skins");
        Directory.CreateDirectory(dir);

        // 两次快闪：0-150ms、250-400ms；200ms 正好在暗隙里（对照帧）
        int[] offsets = new int[] { 40, 120, 200, 300, 380, 600 };

        using (var form = new WidgetForm(store, s))
        {
            SetField(form, "snapshot", snap);
            SetField(form, "flowPhase", 0.42);

            foreach (bool collapsed in new bool[] { false, true })
            {
                SetField(form, "scale", 1.5f);
                SetField(form, "collapsed", collapsed);
                SetField(form, "salaryOpen", false);
                form.MinimumSize = Size.Empty;
                int h = collapsed ? 102 : (int)Prop(form, "ExpandedH");
                form.Size = new Size(collapsed ? 336 : 420, h);
                Call(form, "Recompute");

                foreach (int off in offsets)
                {
                    SetField(form, "noteBlinkLast", Environment.TickCount - off);
                    SetField(form, "noteBlinkAt", Environment.TickCount - off);
                    using (var bmp = new Bitmap(collapsed ? 336 : 420, h))
                    {
                        bmp.SetResolution(96, 96);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(18, 20, 26));
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            Call(form, "RenderCard", g);
                        }
                        string name = (collapsed ? "collapsed" : "expanded") + "-" + off + "ms.png";
                        bmp.Save(Path.Combine(dir, "cornerflash-" + name));
                    }
                }
            }
        }

        // 拼一张对照图：上排展开、下排收起
        int cellW = 420, cellH = 282, pad = 14, label = 22;
        using (var sheet = new Bitmap(pad * 2 + cellW * offsets.Length, pad * 2 + (cellH + label) * 2))
        {
            sheet.SetResolution(96, 96);
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(10, 11, 15));
                using (var f = new Font("Consolas", 10))
                using (var br = new SolidBrush(Color.FromArgb(210, 216, 226)))
                {
                    for (int row = 0; row < 2; row++)
                    {
                        bool collapsed = row == 1;
                        int y = pad + row * (cellH + label);
                        for (int i = 0; i < offsets.Length; i++)
                        {
                            int x = pad + i * cellW;
                            using (var img = Image.FromFile(Path.Combine(dir, "cornerflash-" + (collapsed ? "collapsed" : "expanded") + "-" + offsets[i] + "ms.png")))
                            {
                                g.DrawImageUnscaled(img, x, y + label);
                            }
                            g.DrawString((collapsed ? "收起 " : "展开 ") + offsets[i] + "ms", f, br, x + 4, y + 2);
                        }
                    }
                }
            }
            sheet.Save(Path.Combine(dir, "sheet-cornerflash.png"));
        }
        Console.WriteLine("-> " + dir);
    }
}
