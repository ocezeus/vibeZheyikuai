using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 工资面板小数跳动出图：15 帧 × 55ms，真实 WidgetForm 渲染。
// 用法：SalaryTickShot.exe [输出目录]
class SalaryTickShot
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
        s.ui.reducedMotion = true;      // 验证「关动效也保留工资刷新」
        s.ui.theme = "midnight";
        s.ui.autoCollapseSeconds = 0;
        s.ui.showLunch = true;
        s.salary.monthly = 28000m;
        s.salary.payday = 15;
        s.Normalize();

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            Set(form, "scale", 1.5f);
            form.MinimumSize = Size.Empty;
            form.Size = new Size(420, 300);
            Set(form, "salaryOpen", true);
            Call(form, "Recompute");
            Call(form, "ResizeForSalary");

            string dir = args != null && args.Length > 0 ? args[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "salary-tick");
            Directory.CreateDirectory(dir);
            for (int i = 0; i < 18; i++)
            {
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, form.Width, form.Height)));
                    bmp.Save(Path.Combine(dir, "f" + i.ToString("000") + ".png"), ImageFormat.Png);
                }
                if (i < 17) Thread.Sleep(55);
            }
            Console.WriteLine("ok " + dir + "  " + form.Width + "x" + form.Height);
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
