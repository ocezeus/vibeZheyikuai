using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 目标达成庆祝的出图探针：把某一版在固定时刻画成 PNG（卡面 + 卡外出框层）。
//   GoalShot.exe [版本id] [秒] [collapsed] [quiet] [scale]
// 例：GoalShot.exe 05 3.2            展开卡 2×，t=3.2s
//     GoalShot.exe 01 3.2 quiet      静态帧（reducedMotion）
//     GoalShot.exe 06 1.6 collapsed  收起卡 224×68
// 输出：test\visual\goal-<id>-<t>-<big|small>[-quiet].png 和 …-out.png（带出框留白）
class GoalShot
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
            var inner = ex.InnerException;
            while (inner != null) { Console.WriteLine("INNER " + inner.GetType().FullName + ": " + inner.Message); inner = inner.InnerException; }
            Environment.ExitCode = 99;
        }
        Application.DoEvents();
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        string id = args.Length > 0 ? args[0] : "05";
        double t = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 3.2;
        bool collapsed = args.Length > 2 && args[2] == "collapsed";
        bool quiet = args.Length > 3 && args[3] == "quiet";
        float scale = args.Length > 4 ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 2f;

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.ui.theme = "midnight";
        s.ui.reducedMotion = quiet;
        s.ui.goalEffect = id;

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            Set(form, "scale", scale);
            Set(form, "snapshot", new AttendanceSnapshot { ok = true, clockIn = DateTime.Today.AddHours(9) });
            Call(form, "Recompute");
            Set(form, "collapsed", collapsed);
            form.MinimumSize = Size.Empty;
            int w = (int)Call(form, "S", collapsed ? 224f : 280f);
            int h = collapsed ? (int)Call(form, "S", 68f)
                : (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(w, h);

            // 把庆祝时钟钉在 t 上，再让 Widget 自己开播（它会走真实的渲染路径）
            GoalCelebration.FrozenMs = (int)Math.Round(t * 1000);
            var goal = (GoalCelebration)Get(form, "goal");
            goal.Start(id, quiet, 6000);
            Call(form, "SyncGoalOverlay", (double)GoalCelebration.FrozenMs, 0.0, 0.0);

            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
            System.IO.Directory.CreateDirectory(dir);
            string tag = id + "-t" + t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "-" + scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x"
                + (collapsed ? "-small" : "-big") + (quiet ? "-quiet" : "");

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                bmp.SetResolution(96f * scale, 96f * scale);
                using (var g = Graphics.FromImage(bmp))
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
                string cardPath = System.IO.Path.Combine(dir, "goal-" + tag + ".png");
                bmp.Save(cardPath, ImageFormat.Png);
                Console.WriteLine("card " + cardPath);

                // 出框层：按 CelebrationOverlayForm 的留白（140 DIP）铺一张大图，卡面本身留空
                var variant = GoalVariants.Get(id);
                if (variant != null && variant.hasOutside)
                {
                    int pad = (int)Math.Round(CelebrationOverlayForm.PadDip * scale);
                    using (var wide = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppArgb))
                    {
                        wide.SetResolution(96f * scale, 96f * scale);
                        using (var g = Graphics.FromImage(wide))
                        {
                            g.Clear(Color.Transparent);
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            // 顺序和真机一致：先卡片，再把出框层压在卡上面
                            // （CelebrationOverlayForm 是叠在卡片之上的透明层，不是垫在下面）
                            g.DrawImage(bmp, pad, pad);
                            g.TranslateTransform(pad, pad);
                            var scene = new GoalScene();
                            scene.g = g;
                            scene.W = w;
                            scene.H = h;
                            scene.scale = scale;
                            scene.collapsed = collapsed;
                            scene.quiet = quiet;
                            scene.outside = true;
                            scene.t = t;
                            scene.In = Math.Min(1, t / 1.2);
                            scene.Out = t > 7.2 ? Math.Min(1, (t - 7.2) / 0.8) : 0;
                            variant.PaintOutside(scene);
                            g.ResetTransform();
                        }
                        string outPath = System.IO.Path.Combine(dir, "goal-" + tag + "-out.png");
                        wide.Save(outPath, ImageFormat.Png);
                        Console.WriteLine("out  " + outPath);
                    }
                }
            }
            GoalCelebration.FrozenMs = -1;
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Application.DoEvents();
    }

    static void Set(object o, string name, object v)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        f.SetValue(o, v);
    }

    static object Get(object o, string name)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        return f.GetValue(o);
    }

    static object Call(object o, string name, params object[] a)
    {
        foreach (var m in o.GetType().GetMethods(F))
        {
            if (m.Name != name || m.GetParameters().Length != a.Length) continue;
            return m.Invoke(o, a);
        }
        throw new Exception("no method " + name + "/" + a.Length);
    }
}
