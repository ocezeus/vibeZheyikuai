using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 新建主题预览 / 字体设置出图。用法：PreviewShots.exe [输出目录]
class PreviewShots
{
    [STAThread]
    static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = args != null && args.Length > 0 ? args[0]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        using (var dlg = new ColorWheelForm("新建主题", "我的主题",
            Color.FromArgb(20, 24, 14), Color.FromArgb(164, 203, 255)))
        {
            dlg.Show();
            Pump(450);
            using (var bmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
                bmp.Save(Path.Combine(dir, "theme-preview.png"), ImageFormat.Png);
            }
            dlg.Close();
        }

        using (var dlg = new FontsForm("", "Consolas"))
        {
            dlg.Show();
            Pump(350);
            using (var bmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
                bmp.Save(Path.Combine(dir, "fonts-dialog.png"), ImageFormat.Png);
            }
            dlg.Close();
        }
        Console.WriteLine("ok " + dir);
        Application.DoEvents();
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end) { Application.DoEvents(); Thread.Sleep(10); }
    }
}
