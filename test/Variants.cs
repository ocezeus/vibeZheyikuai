using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 生成「卡片行距」对比图：每套行距渲染一张展开卡 + 一条收起横条，纵向拼成一张便于挑。
class Variants
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static BindingFlags SF = BindingFlags.Static | BindingFlags.NonPublic;

    static void Set(object o, string name, object value) { o.GetType().GetField(name, F).SetValue(o, value); }
    static object Call(object o, string name, params object[] args) { return o.GetType().GetMethod(name, F).Invoke(o, args); }
    static object Get(object o, string name) { return o.GetType().GetProperty(name, F).GetValue(o, null); }
    static void SetGap(int gap) { typeof(WidgetForm).GetField("RowGapDip", SF).SetValue(null, gap); }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        int[] gaps = new int[] { 3, 5, 7 };
        string[] names = new string[] { "A · 紧凑", "B · 均衡", "C · 稍松" };
        const float scale = 1.5f;
        int cardW = (int)(280 * scale), pillW = (int)(224 * scale);
        float dpi = 144f;

        Bitmap[] cards = new Bitmap[gaps.Length];
        Bitmap[] pills = new Bitmap[gaps.Length];
        string[] heights = new string[gaps.Length];

        for (int i = 0; i < gaps.Length; i++)
        {
            SetGap(gaps[i]);
            var s = new Settings();
            s.mode = "flex";
            s.flexMinutes = 630;
            s.startTime = "09:00";
            s.endTime = "18:00";
            s.lunchStart = "11:50";
            s.lunchEnd = "13:30";
            s.eveningEnabled = true;
            s.eveningStart = "17:50";
            s.eveningEnd = "19:00";
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.theme = "bai-yudu";
            s.ui.autoCollapseSeconds = 0;
            s.ui.reminderEffect = "glow";
            s.NormalizeSchedules();

            var snap = new AttendanceSnapshot();
            snap.ok = true;
            snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
            snap.fetchedAt = DateTime.Now;

            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                form.MinimumSize = Size.Empty;
                Set(form, "scale", scale);
                Set(form, "snapshot", snap);
                Set(form, "collapsed", false);
                form.Size = new Size(cardW, (int)Get(form, "ExpandedH"));
                Call(form, "Recompute");
                heights[i] = (int)Get(form, "ExpandedH") + "px / " + (int)((int)Get(form, "ExpandedH") / scale) + " DIP";
                cards[i] = Render(form, dpi);

                Set(form, "collapsed", true);
                form.Size = new Size(pillW, (int)Call(form, "S", 68f));
                Call(form, "Recompute");
                pills[i] = Render(form, dpi);
            }
        }

        int pad = 28, labelH = 54, gapY = 24;
        int pillGap = 28;
        int rowH = 0;
        foreach (var c in cards) rowH = Math.Max(rowH, c.Height);
        int width = pad * 2 + cardW + pillGap + pillW;
        int height = pad + (labelH + rowH + gapY) * gaps.Length + 20;

        using (var canvas = new Bitmap(width, height))
        {
            canvas.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(233, 236, 242))) g.FillRectangle(bg, 0, 0, width, height);

                using (var fBig = UiFont.Make(11f, FontStyle.Bold))
                using (var fSmall = UiFont.Make(9f, FontStyle.Regular))
                {
                    int y = pad;
                    for (int i = 0; i < gaps.Length; i++)
                    {
                        using (var ink = new SolidBrush(Color.FromArgb(28, 34, 48)))
                            g.DrawString(names[i] + "　行距 " + gaps[i] + " DIP　卡片高 " + heights[i], fBig, ink, pad, y);
                        using (var muted = new SolidBrush(Color.FromArgb(110, 118, 134)))
                            g.DrawString(i == 1 ? "推荐：和现在一样高（273px），每行间距完全一致"
                                : (i == 0 ? "比现在矮 18px，行与行挨得更近" : "比现在高 18px，留白更多"),
                                fSmall, muted, pad, y + 26);

                        int top = y + labelH;
                        g.DrawImage(cards[i], pad, top);
                        g.DrawImage(pills[i], pad + cardW + pillGap, top);
                        y += labelH + rowH + gapY;
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "variants.png"));
        }
        MakeFontSheet(dir, dpi);
        Console.WriteLine("variants -> " + Path.Combine(dir, "variants.png") + "  " + width + "x" + height);
        foreach (var g in gaps) Console.WriteLine("  行距 " + g + " DIP");
    }

    static void MakeFontSheet(string dir, float dpi)
    {
        string[] fams = new string[] { "Microsoft YaHei UI", "Noto Sans SC" };
        string[] titles = new string[] { "现在的微软雅黑", "换成的字体：Noto Sans SC（苹方的开源近亲，字重、字面几乎一样）" };
        int w = 940, rowH = 132;
        using (var canvas = new Bitmap(w, 116 + rowH * fams.Length))
        {
            canvas.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(233, 236, 242))) g.FillRectangle(bg, 0, 0, canvas.Width, canvas.Height);
                using (var title = new SolidBrush(Color.FromArgb(28, 34, 48)))
                using (var muted = new SolidBrush(Color.FromArgb(105, 113, 130)))
                using (var ink = new SolidBrush(Color.FromArgb(30, 34, 44)))
                {
                    g.DrawString("中文字体：28pt 数字 / 17pt 标题 / 9pt 说明", UiFont.Make(10f, FontStyle.Bold), title, 24, 16);
                    g.DrawString("这台 Windows 没装苹方，用的是它的开源近亲；把 PingFang.ttc 放进 exe 旁边的 fonts\\ 目录就会自动换成真苹方。",
                        UiFont.Make(8.5f, FontStyle.Regular), muted, 24, 42);
                    int y = 78;
                    for (int i = 0; i < fams.Length; i++)
                    {
                        using (var big = new Font(fams[i], 28f, FontStyle.Bold, GraphicsUnit.Point))
                        using (var mid = new Font(fams[i], 17f, FontStyle.Bold, GraphicsUnit.Point))
                        using (var small = new Font(fams[i], 9f, FontStyle.Regular, GraphicsUnit.Point))
                        {
                            g.DrawString(titles[i], UiFont.Make(8.5f, FontStyle.Regular), muted, 24, y);
                            g.DrawString("09:20:04", big, ink, 24, y + 18);
                            g.DrawString("距离下班 · 预计 19:32 打卡", mid, ink, 330, y + 18);
                            g.DrawString("上班 09:00 · 应勤 10h30m · 距午休 1h40m", small, ink, 330, y + 70);
                        }
                        y += rowH;
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "fonts.png"));
        }
    }

    static Bitmap Render(WidgetForm form, float dpi)
    {
        var bmp = new Bitmap(form.Width, form.Height);
        bmp.SetResolution(dpi, dpi);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(233, 236, 242));
            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, form.Width, form.Height)));
        }
        return bmp;
    }
}
