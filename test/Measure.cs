using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 量“视觉”行距：用纯白底的假主题渲染卡片，扫描像素找出每一行字的上下边界，
// 打印相邻两行之间的真实空隙（而不是设计稿上的行框）。用来校准大号数字那一行。
class Measure
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    static ThemeDef Flat()
    {
        var t = new ThemeDef();
        t.id = "flat"; t.name = "flat";
        t.bgTop = Color.White; t.bgBottom = Color.White;
        t.textMain = Color.FromArgb(20, 20, 20);
        t.accent = Color.FromArgb(60, 60, 60);
        t.track = Color.FromArgb(200, 200, 200);
        t.success = Color.FromArgb(60, 60, 60);
        return t;
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        int gapField = (int)typeof(WidgetForm).GetField("RowGapDip", F).GetValue(null);
        Console.WriteLine("默认行距 " + gapField + " DIP，下面逐个大号行高试：" + Environment.NewLine);

        foreach (int gap in new int[] { 3, 5, 7 })
        foreach (int big in new int[] { 30, 34, 38, 44 })
        {
            typeof(WidgetForm).GetField("RowGapDip", F).SetValue(null, gap);
            typeof(WidgetForm).GetField("BigDip", F).SetValue(null, big);
            int height; List<string> labels;
            int[] bands = Bands(out height, out labels);
            Console.Write("行距 " + gap + " · 大号行 " + big + " DIP (卡高 " + height + "px): ");
            for (int i = 0; i < bands.Length; i += 2)
                Console.Write("[" + bands[i] + "-" + bands[i + 1] + "] ");
            Console.Write("   空隙 ");
            for (int i = 0; i < bands.Length - 2; i += 2)
                Console.Write(Gap(bands, i) + " ");
            Console.WriteLine();
        }
        Console.WriteLine(Environment.NewLine + "格式：字形高度 / 到下一行字的空隙（px，150% 缩放）");
    }

    static int Gap(int[] b, int i)
    {
        if (i + 2 >= b.Length) return 0;
        return b[i + 2] - b[i + 1];
    }

    /// <summary>返回字的上下边界序列（每两个数一段：top,bottom）。</summary>
    static int[] Bands(out int height, out List<string> labels)
    {
        labels = new List<string>();
        var s = new Settings();
        s.mode = "flex"; s.flexMinutes = 630; s.startTime = "09:00"; s.endTime = "18:00";
        s.lunchStart = "11:50"; s.lunchEnd = "13:30";
        s.eveningEnabled = true; s.eveningStart = "17:50"; s.eveningEnd = "19:00";
        s.lark.enabled = false; s.calendarEnabled = false; s.ui.edge = "free"; s.ui.theme = "ivory";
        s.ui.showLunch = true;

        var snap = new AttendanceSnapshot();
        snap.ok = true; snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            Call(form, "Recompute");
            height = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, height);

            Set(form, "theme", Flat());
            using (var bmp = new Bitmap(420, height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, height)));
                }
                var rows = new List<int>();
                for (int y = 0; y < height; y++)
                {
                    bool ink = false;
                    for (int x = 4; x < 416; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.R < 170 && c.G < 170 && c.B < 170) { ink = true; break; }
                    }
                    if (ink) rows.Add(y);
                }
                // 把连续的墨迹行合并成段
                var bands = new List<int>();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i == 0 || rows[i] - rows[i - 1] > 1) bands.Add(rows[i]);
                    if (i == rows.Count - 1 || rows[i + 1] - rows[i] > 1) bands.Add(rows[i]);
                }
                return bands.ToArray();
            }
        }
    }
}
