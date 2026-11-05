using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

class Dbg
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var theme = Themes.Get("lv-kongque");
        Console.WriteLine("theme accent=" + theme.accent.R + "," + theme.accent.G + "," + theme.accent.B
            + " bgTop=" + theme.bgTop.R + "," + theme.bgTop.G + "," + theme.bgTop.B);

        var picker = new ThemePicker();
        picker.Size = new Size(456, 104);
        picker.SelectedId = "";
        using (var bmp = new Bitmap(picker.Width, picker.Height))
        {
            picker.CreateControl();
            picker.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "picker.png"));
            // 第一个色块区域 y 3..23，打印第 4 列附近的一条竖线颜色
            for (int y = 0; y < 30; y += 2)
            {
                Color c = bmp.GetPixel(25, y);
                Console.WriteLine("y=" + y + " -> " + c.R + "," + c.G + "," + c.B);
            }
        }
        // 也看看有几个非白像素
        using (var bmp = new Bitmap(picker.Width, picker.Height))
        {
            picker.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            int nonWhite = 0, accent = 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 246 && c.G > 246 && c.B > 246) continue;
                    nonWhite++;
                    foreach (var t in Themes.All)
                        if (Math.Abs(c.R - t.accent.R) < 26 && Math.Abs(c.G - t.accent.G) < 26 && Math.Abs(c.B - t.accent.B) < 26) { accent++; break; }
                }
            Console.WriteLine("nonWhite=" + nonWhite + " accentLike=" + accent);
        }
    }
}
