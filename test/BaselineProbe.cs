using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

class BaselineProbe
{
    static void Main()
    {
        var bmp = new Bitmap(300, 120, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(245, 196, 0));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font("Microsoft YaHei", 9.75f, FontStyle.Regular, GraphicsUnit.Point))
            {
                g.DrawString("16:45", f, Brushes.Black, 10, 0, StringFormat.GenericTypographic);
                float ascent = f.FontFamily.GetCellAscent(f.Style) * f.Size * g.DpiY / 72f / f.FontFamily.GetEmHeight(f.Style);
                Console.WriteLine("ascent=" + ascent + " height=" + f.GetHeight(g) + " sizePx=" + (f.Size * g.DpiY / 72f));
            }
            using (var f2 = new Font("Microsoft YaHei", 12.75f, FontStyle.Bold, GraphicsUnit.Point))
            {
                g.DrawString("产品评审会", f2, Brushes.Black, 10, 50, StringFormat.GenericTypographic);
                float ascent = f2.FontFamily.GetCellAscent(f2.Style) * f2.Size * g.DpiY / 72f / f2.FontFamily.GetEmHeight(f2.Style);
                Console.WriteLine("title ascent=" + ascent + " height=" + f2.GetHeight(g));
            }
        }
        bmp.Save("C:/Users/xlean/AppData/Local/Temp/baseline.png", ImageFormat.Png);
        Console.WriteLine("ok");
    }
}
