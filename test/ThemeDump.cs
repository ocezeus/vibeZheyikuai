using System;
using System.Globalization;
using System.Text;
using WorkCountdown;

// 把内置 + 自建主题导出成 JSON，给 design 里的交互预览页用。
// 用法：ThemeDump.exe > themes.json
class ThemeDump
{
    static string Hex(System.Drawing.Color c)
    {
        return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
    }

    static void Main()
    {
        var sb = new StringBuilder();
        sb.Append("[");
        ThemeDef[] all = Themes.All;
        for (int i = 0; i < all.Length; i++)
        {
            ThemeDef t = all[i];
            if (i > 0) sb.Append(",");
            sb.Append("{");
            sb.Append("\"id\":\"").Append(t.id).Append("\",");
            sb.Append("\"name\":\"").Append(t.name.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\",");
            sb.Append("\"accent\":\"").Append(Hex(t.accent)).Append("\",");
            sb.Append("\"success\":\"").Append(Hex(t.success)).Append("\",");
            sb.Append("\"bgTop\":\"").Append(Hex(t.bgTop)).Append("\",");
            sb.Append("\"bgBottom\":\"").Append(Hex(t.bgBottom)).Append("\",");
            sb.Append("\"track\":\"").Append(Hex(t.track)).Append("\",");
            sb.Append("\"textMain\":\"").Append(Hex(t.textMain)).Append("\",");
            sb.Append("\"textMuted\":\"").Append(Hex(t.textMuted)).Append("\",");
            sb.Append("\"textDim\":\"").Append(Hex(t.textDim)).Append("\",");
            sb.Append("\"edge\":\"").Append(Hex(t.edge)).Append("\",");
            sb.Append("\"light\":").Append(t.id == "ivory" || Luma(t.bgTop) > 0.56 ? "true" : "false");
            sb.Append("}");
        }
        sb.Append("]");
        Console.OutputEncoding = Encoding.UTF8;
        Console.Write(sb.ToString());
    }

    static double Luma(System.Drawing.Color c)
    {
        return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
    }
}
