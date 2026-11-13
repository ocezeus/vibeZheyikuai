using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

class GlyphTest
{
    static void Main()
    {
        var bmp = new Bitmap(460, 260, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(245, 196, 0));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font("Microsoft YaHei", 12.75f, FontStyle.Bold, GraphicsUnit.Point))
            using (var br = new SolidBrush(Color.FromArgb(0x33, 0x26, 0x0A)))
            {
                string t = "产品评审会 · Q4 排期";
                // 1) 点 + GenericTypographic
                g.DrawString("1: " + t, f, br, 10, 4);
                // 2) 矩形 + GenericTypographic，不带任何 flag
                g.DrawString(t, f, br, new RectangleF(10, 34, 382, 22.4f), new StringFormat(StringFormat.GenericTypographic));
                // 3) 矩形 + NoWrap + Ellipsis + Center（抽屉里的写法）
                var sf = new StringFormat(StringFormat.GenericTypographic);
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(t, f, br, new RectangleF(10, 64, 382, 22.4f), sf);
                // 4) 矩形 + 默认 StringFormat
                g.DrawString(t, f, br, new RectangleF(10, 94, 382, 22.4f), new StringFormat());
                // 5) 矩形 + GenericTypographic + 无裁剪，但 LineAlignment=Center
                var sf2 = new StringFormat(StringFormat.GenericTypographic);
                sf2.LineAlignment = StringAlignment.Center;
                g.DrawString(t, f, br, new RectangleF(10, 124, 382, 22.4f), sf2);
                // 6) 矩形 + 默认 + NoWrap/Ellipsis/Center
                var sf3 = new StringFormat();
                sf3.Trimming = StringTrimming.EllipsisCharacter;
                sf3.FormatFlags |= StringFormatFlags.NoWrap;
                sf3.LineAlignment = StringAlignment.Center;
                g.DrawString(t, f, br, new RectangleF(10, 154, 382, 22.4f), sf3);
            }
            using (var f2 = new Font("Microsoft YaHei", 9.75f, FontStyle.Regular, GraphicsUnit.Point))
            using (var br2 = new SolidBrush(Color.FromArgb(0x5C, 0x44, 0x00)))
            {
                var sf = new StringFormat(StringFormat.GenericTypographic);
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("3F 云杉会议室 · 还有 10 分钟", f2, br2, new RectangleF(10, 184, 382, 17.2f), sf);
            }
        }
        bmp.Save("C:/Users/xlean/AppData/Local/Temp/glyph-variants.png", ImageFormat.Png);
        Console.WriteLine("ok");
    }
}
