using System;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 诊断：抽屉卡用的三个字体到底解析成了什么、量出来的宽度是多少。
class FontDiag
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var s = new Settings();
        var ev = new ReminderEvent();
        ev.title = "产品评审会 · Q4 排期";
        ev.place = "3F · 云杉会议室";
        ev.source = "lark";
        ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(10));
        ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));
        using (var d = new DrawerForm(s, ev, new Rectangle(800, 200, 224, 68), true, 1f, false, null))
        {
            foreach (var name in new[] { "fTitle", "fMeta", "fMetaBold" })
            {
                var f = (Font)Get(d, name);
                Console.WriteLine(name + ": name=" + f.Name + " size=" + f.Size + " style=" + f.Style +
                    " height=" + f.GetHeight() + " family=" + f.FontFamily.Name);
            }
            using (var bmp = new Bitmap(440, 68))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var fTitle = (Font)Get(d, "fTitle");
                var fMeta = (Font)Get(d, "fMeta");
                var fMetaBold = (Font)Get(d, "fMetaBold");
                Console.WriteLine("plain bmp dpi=" + g.DpiY + " titleH=" + fTitle.GetHeight(g) + " metaH=" + fMeta.GetHeight(g));
                Console.WriteLine("w title=" + g.MeasureString(ev.title, fTitle, 100000, StringFormat.GenericTypographic).Width);
            }
            using (var bmp = new Bitmap(440, 68, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                bmp.SetResolution(96, 96);
                using (var g = Graphics.FromImage(bmp))
                {
                    var fTitle = (Font)Get(d, "fTitle");
                    var fMeta = (Font)Get(d, "fMeta");
                    Console.WriteLine("96dpi bmp dpi=" + g.DpiY + " titleH=" + fTitle.GetHeight(g) + " metaH=" + fMeta.GetHeight(g));
                    Console.WriteLine("w title=" + g.MeasureString(ev.title, fTitle, 100000, StringFormat.GenericTypographic).Width);
                    using (var br = new SolidBrush(Color.FromArgb(0x33, 0x26, 0x0A)))
                        g.DrawString(ev.title, fTitle, br, 10, 10, StringFormat.GenericTypographic);
                }
                bmp.Save("C:/Users/xlean/AppData/Local/Temp/diag-96.png", System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    static object Get(object o, string name)
    {
        return o.GetType().GetField(name, F).GetValue(o);
    }
}
