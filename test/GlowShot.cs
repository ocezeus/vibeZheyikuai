using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 卡片背面流光的实机出图：真 WidgetForm + 真 CardGlowForm，底下垫一块和
// design/card-glow-3.html 第 3 列同样配色的底板，方便和设计稿并排比。
// 用法：GlowShot.exe [主题id] [扫边角度,..] [柔光透明度]
//   角度默认 0,90,180,270（= 5.5s 一圈的 0/25/50/75%），柔光默认钉在 0.5，传 -1 走呼吸曲线
class GlowShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static double[] degrees = new double[] { 0, 90, 180, 270 };
    static double halo = 0.5;
    const double SweepMs = 5500;

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
        string themeId = args != null && args.Length > 0 ? args[0] : "midnight";
        if (args != null && args.Length > 1)
        {
            string[] parts = args[1].Split(',');
            degrees = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                degrees[i] = double.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        }
        if (args != null && args.Length > 2)
            halo = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.ui.theme = themeId;
        s.ui.alwaysOnTop = true;
        s.ui.autoCollapseSeconds = 0;
        s.ui.reducedMotion = false;
        s.breakNotify = false;     // 别让午休/晚休的提醒卡盖住卡片，出图只看出流光
        s.NormalizeSchedules();    // 先把默认的午休/晚休/下班日程建出来，再全部关掉
        if (s.schedules != null)
            foreach (var f in s.schedules) { f.notify = false; f.endNotify = false; f.enabled = false; }

        using (var back = new BackdropForm())
        {
            back.TopMost = true;      // 盖住别的窗口，截图里只留设计稿那块底板
            back.SetBounds(180, 200, 820, 700);
            back.Show();
            Pump(200);

            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                form.Show();
                Call(form, "SetCollapsed", true);
                Pump(1400);
                Center(form, back);
                Pump(500);
                Shoot(back, form, themeId, "small");

                Call(form, "SetCollapsed", false);
                Pump(1400);
                Center(form, back);
                Pump(500);
                Shoot(back, form, themeId, "big");

                TimeRender(form);
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
            back.Close();
        }
        Application.DoEvents();
    }

    static double DpiScale(Control c)
    {
        using (var g = c.CreateGraphics()) return g.DpiX / 96.0;
    }

    /// <summary>让卡片在底板里水平居中，上边留 130 DIP。</summary>
    static void Center(WidgetForm form, Form back)
    {
        int top = back.Top + (int)Math.Round(130 * DpiScale(form));
        form.SetBounds(back.Left + (back.Width - form.Width) / 2, top, form.Width, form.Height);
    }

    static void Shoot(BackdropForm back, WidgetForm form, string themeId, string sizeName)
    {
        var glow = (Form)Get(form, "cardGlow");
        if (glow == null) { Console.WriteLine("NO GLOW"); return; }
        SetStatic(glow.GetType(), "Frozen", true);
        SetStatic(glow.GetType(), "HaloAlpha", halo);
        Set(glow, "startAt", 0);

        foreach (double deg in degrees)
        {
            // 只按扫边角度算时钟：deg/360 × 5.5s
            SetStatic(glow.GetType(), "FixedClock", (int)Math.Round(deg / 360.0 * SweepMs));
            glow.Invalidate();
            Pump(150);
            SaveShot(back, form, sizeName + "-" + themeId + "-r" + deg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
        // 出完图把时钟放开，免得后面的尺寸切换按冻结时间算
        SetStatic(glow.GetType(), "HaloAlpha", -1.0);
        SetStatic(glow.GetType(), "Frozen", false);
    }

    /// <summary>量一下 Render 一帧要多久（30fps 的预算是 33ms）。</summary>
    static void TimeRender(WidgetForm form)
    {
        var glow = (Form)Get(form, "cardGlow");
        if (glow == null) return;
        SetStatic(glow.GetType(), "Frozen", true);
        Set(glow, "startAt", 0);
        var m = glow.GetType().GetMethod("Render", F);
        for (int i = 0; i < 5; i++) m.Invoke(glow, null);
        var sw = Stopwatch.StartNew();
        const int n = 40;
        for (int i = 0; i < n; i++) { SetStatic(glow.GetType(), "FixedClock", i * 37); m.Invoke(glow, null); }
        sw.Stop();
        Console.WriteLine("render avg " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.00") + " ms  (" + glow.Width + "x" + glow.Height + ")");
        SetStatic(glow.GetType(), "Frozen", false);
    }

    /// <summary>
    /// 不用屏幕截图：把底板、光晕窗、卡片窗三张位图按真实位置合成，
    /// 免得别的窗口（Lark 之类）压在截图上。
    /// </summary>
    static void SaveShot(BackdropForm back, WidgetForm form, string name)
    {
        var r = form.Bounds;
        int pad = (int)Math.Round(100 * DpiScale(form));
        r.Inflate(pad, pad);
        r.Intersect(Screen.FromRectangle(form.Bounds).Bounds);
        using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                back.PaintInto(g, r);
                DrawLayer(g, Get(form, "cardGlow"), r, 1.0);
                DrawLayer(g, form, r, 240.0 / 255.0);   // 卡片窗按 240/255 推上去的
            }
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, name + ".png");
            bmp.Save(p, ImageFormat.Png);
            Console.WriteLine("ok " + p + "  " + r.Width + "x" + r.Height);
        }
    }

    static void DrawLayer(Graphics g, object form, Rectangle region, double alpha)
    {
        if (form == null) return;
        var frame = (Bitmap)Get(form, "frame");
        if (frame == null) return;
        var dest = new Rectangle(((Control)form).Left - region.Left, ((Control)form).Top - region.Top, frame.Width, frame.Height);
        if (alpha >= 0.999) { g.DrawImageUnscaled(frame, dest.X, dest.Y); return; }
        using (var ia = new ImageAttributes())
        {
            var cm = new ColorMatrix();
            cm.Matrix33 = (float)alpha;
            ia.SetColorMatrix(cm);
            g.DrawImage(frame, dest, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, ia);
        }
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end) { Application.DoEvents(); Thread.Sleep(10); }
    }

    static void Set(object o, string name, object v) { o.GetType().GetField(name, F).SetValue(o, v); }
    static void SetStatic(Type t, string name, object v) { t.GetField(name, F).SetValue(null, v); }
    static object Get(object o, string name) { return o.GetType().GetField(name, F).GetValue(o); }
    static object Call(object o, string name, params object[] a)
    {
        var m = o.GetType().GetMethod(name, F);
        if (m == null) throw new Exception("no method " + name);
        return m.Invoke(o, a);
    }
}

/// <summary>design/card-glow-3.html 里那块面板的底色：160deg 深蓝渐变 + 顶部中间一层淡蓝。</summary>
class BackdropForm : Form
{
    public BackdropForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintPanel(e.Graphics);
    }

    /// <summary>把底板画到一张离屏图上：region 是屏幕坐标的裁剪区。</summary>
    public void PaintInto(Graphics g, Rectangle region)
    {
        GraphicsState st = g.Save();
        g.TranslateTransform(Left - region.Left, Top - region.Top);
        PaintPanel(g);
        g.Restore(st);
    }

    void PaintPanel(Graphics g)
    {
        var r = new Rectangle(0, 0, Width, Height);
        using (var b = new LinearGradientBrush(r, Color.FromArgb(0x10, 0x18, 0x22), Color.FromArgb(0x0A, 0x0F, 0x16), 70f))
            g.FillRectangle(b, r);
        using (var p = new GraphicsPath())
        {
            p.AddEllipse(new Rectangle((int)(-Width * 0.1), (int)(-Height * 0.35), (int)(Width * 1.2), (int)(Height * 1.1)));
            using (var br = new PathGradientBrush(p))
            {
                br.CenterPoint = new PointF(Width / 2f, Height * 0.3f);
                br.CenterColor = Color.FromArgb(26, 70, 110, 170);
                br.SurroundColors = new Color[] { Color.FromArgb(0, 70, 110, 170) };
                g.FillPath(br, p);
            }
        }
    }
}
