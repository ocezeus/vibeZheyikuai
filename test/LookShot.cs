using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 外观页截图：看新加的「提醒扩散 / 扩散样式」两行有没有挤在一起。
class LookShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        var store = new SettingsStore();
        var s = store.Load();
        s.ui.reminderSpread = "screen";
        s.ui.reminderSpreadStyle = "bar";
        using (var dlg = new SettingsForm(store, s))
        {
            dlg.CreateControl();
            dlg.Show();
            var tabs = (TabControl)Get(dlg, "tabs");
            tabs.SelectedIndex = 3;
            Application.DoEvents();
            System.Threading.Thread.Sleep(120);
            Application.DoEvents();
            using (var bmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "look-new.png"));
            }
            var rbSpread = (RadioButton)Get(dlg, "rbSpreadScreen");
            var rbStyle = (RadioButton)Get(dlg, "rbStyleBar");
            Console.WriteLine("spreadScreen=" + rbSpread.Checked + " styleBar=" + rbStyle.Checked);
            dlg.Close();
        }
    }
}
