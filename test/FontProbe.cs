using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 打印当前解析到的中文字体，确认苹方有没有真的被用上。
class FontProbe
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("UiFont.Family   = " + UiFont.Family);
        Console.WriteLine("UiFont.IsPingFang = " + UiFont.IsPingFang);
        Console.WriteLine("UiFont.HasBoldFace = " + UiFont.HasBoldFace);
        using (var f = UiFont.Make(9f))
        using (var b = UiFont.Make(9f, FontStyle.Bold))
            Console.WriteLine("常规 = " + f.Name + " / " + f.FontFamily.Name + "   粗体 = " + b.Name + " / " + b.FontFamily.Name);
    }
}
