using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 工资面板出图：真实 WidgetForm 渲染，150% 缩放（420 宽）。
// 用法：SalaryShot.exe [输出目录]
class SalaryShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.mode = "fixed";
        s.startTime = "09:30";
        s.endTime = "18:00";
        s.lunchStart = "12:00";
        s.lunchEnd = "12:30";
        s.lunchDeduct = true;
        s.eveningEnabled = false;
        s.ui.edge = "free";
        s.ui.reminderEffect = "none";
        s.ui.reducedMotion = true;
        s.ui.theme = args != null && args.Length > 1 ? args[1] : "midnight";
        s.ui.autoCollapseSeconds = 0;
        s.ui.showLunch = true;
        s.salary.monthly = 28000m;
        s.salary.payday = 15;
        s.Normalize();

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            Set(form, "scale", 1.5f);
            form.MinimumSize = Size.Empty;
            form.Size = new Size(420, 282);
            Set(form, "salaryOpen", true);
            Call(form, "Recompute");
            Call(form, "ResizeForSalary");

            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                bmp.SetResolution(144, 144);
                using (var g = Graphics.FromImage(bmp))
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, form.Width, form.Height)));
                string dir = args != null && args.Length > 0 ? args[0]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "salary-live.png");
                bmp.Save(p, ImageFormat.Png);
                Console.WriteLine("ok " + p + "  " + form.Width + "x" + form.Height);
            }
            form.Close();
        }
        Application.DoEvents();
    }

    static object Call(object o, string name, params object[] args)
    {
        var m = o.GetType().GetMethod(name, F);
        if (m == null) throw new Exception("no method " + name);
        return m.Invoke(o, args);
    }

    static void Set(object o, string name, object value)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        f.SetValue(o, value);
    }
}
