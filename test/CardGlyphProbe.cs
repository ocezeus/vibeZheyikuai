using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 大卡片（展开态）右上角 5 个图标的离屏像素验收：小飞机 / ¥ / 齿轮 / 刷新 / 折叠箭头。
//
// 做法：照 test\Check.cs 的方式 new 一个 WidgetForm，强制 scale=1.5、展开、420×456，
// 直接调 OnPaint 画到 144dpi 的 Bitmap 上；再用反射拿 5 个命中区 rect，
// 在命中区里逐像素找「和背景色差 > 18」的墨迹包围盒，量宽/高/中心。
//
// 判定：五个图标宽、高彼此相差 ≤15%；每个图标中心与命中区中心偏差 ≤3px；
//       墨迹外框约 14 DIP（12.4~15.2）；墨迹不超出命中区。
//
// 用法：
//   CardGlyphProbe.exe measure <输出.png> [主题id] [1=展开工资面板]
//   例：CardGlyphProbe.exe measure test\visual\glyph-after.png ivory 1
// 探针只读配置（WC_NO_SAVE / WC_NO_RELAY），不会回写用户设置，也不需要真实鼠标。
class CardGlyphProbe
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static int pass, fail;

    static void Ok(string name, bool cond, string detail)
    {
        if (cond) { pass++; Console.WriteLine("PASS  " + name + "  " + detail); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  " + detail); }
    }

    static object Call(object o, string name, params object[] args)
    {
        MethodInfo found = null;
        foreach (var mi in o.GetType().GetMethods(F))
        {
            if (mi.Name != name) continue;
            if (mi.GetParameters().Length != args.Length) continue;
            found = mi;
            break;
        }
        if (found == null) throw new Exception("no method " + name + "/" + args.Length);
        return found.Invoke(o, args);
    }

    static void Set(object o, string name, object value)
    {
        var fi = o.GetType().GetField(name, F);
        if (fi == null) throw new Exception("no field " + name);
        fi.SetValue(o, value);
    }

    static object Get(object o, string name)
    {
        var fi = o.GetType().GetField(name, F);
        if (fi == null) throw new Exception("no field " + name);
        return fi.GetValue(o);
    }

    static void Main(string[] args)
    {
        // 探针 / 截图程序只读配置：不回写设置，也不发小纸条中转请求
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");

        string mode = args.Length > 0 ? args[0] : "measure";
        if (mode != "measure")
        {
            Console.WriteLine("用法: CardGlyphProbe.exe measure <输出.png> [主题id] [1=展开工资面板]");
            Environment.Exit(2);
        }

        string outPath = args.Length > 1 ? args[1] : @"test\visual\glyph-measure.png";
        string themeId = args.Length > 2 ? args[2] : "";
        bool salaryOpen = args.Length > 3 && args[3] == "1";
        Run(outPath, themeId, salaryOpen);

        Console.WriteLine();
        Console.WriteLine("合计: " + pass + " PASS / " + fail + " FAIL");
        Environment.Exit(fail == 0 ? 0 : 1);
    }

    // ---------------- 验收 ----------------

    static void Run(string outPath, string themeId, bool salaryOpen)
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.Normalize();
        if (!string.IsNullOrEmpty(themeId)) s.ui.theme = themeId;

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            float realScale = (float)Get(form, "scale");
            Console.WriteLine("info  实际 DPI scale = " + realScale.ToString("0.00") + "（探针按 1.5 / 144dpi 渲染）");
            Set(form, "scale", 1.5f);
            Set(form, "collapsed", false);
            Set(form, "expandAnim", false);
            if (salaryOpen) Set(form, "salaryOpen", true);
            form.MinimumSize = Size.Empty;
            form.Size = new Size(420, 456);
            Call(form, "Recompute");

            using (var bmp = new Bitmap(420, 456, PixelFormat.Format32bppArgb))
            {
                bmp.SetResolution(144, 144);
                using (var g = Graphics.FromImage(bmp))
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 456)));

                string dir = System.IO.Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                bmp.Save(outPath, ImageFormat.Png);
                string zoomPath = SaveZoom(bmp, outPath);
                Console.WriteLine("info  已保存 " + outPath);
                Console.WriteLine("info  已保存 " + zoomPath + "（4x 放大，方便目视比对）");

                var rects = new[]
                {
                    new { name = "plane(小纸条)", field = "rectNote" },
                    new { name = "salary(¥)",    field = "rectSalary" },
                    new { name = "gear(设置)",   field = "rectGear" },
                    new { name = "refresh(刷新)",field = "rectRefresh" },
                    new { name = "chevron(折叠)",field = "rectChevron" },
                };

                var boxes = new RectangleF[rects.Length];
                var centers = new PointF[rects.Length];
                Console.WriteLine("info  背景 = 命中区最外 3px 一圈的中位色；阈值 = 色差 > 18");
                Console.WriteLine("info  " + "图标".PadRight(14) + "命中区(x,y,w,h)".PadRight(24)
                    + "墨迹 bbox 左/上/宽/高(px)".PadRight(34) + "宽x高(DIP)".PadRight(16) + "中心偏差(px)");
                for (int i = 0; i < rects.Length; i++)
                {
                    var r = (Rectangle)Get(form, rects[i].field);
                    RectangleF box = InkBox(bmp, r, 18);
                    boxes[i] = box;
                    centers[i] = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                    float dw = box.Width / 1.5f, dh = box.Height / 1.5f;
                    float cx = box.X + box.Width / 2f - centers[i].X;
                    float cy = box.Y + box.Height / 2f - centers[i].Y;
                    Console.WriteLine("info  " + rects[i].name.PadRight(16)
                        + string.Format("({0},{1},{2},{3})", r.X, r.Y, r.Width, r.Height).PadRight(24)
                        + string.Format("{0,4:0} {1,4:0} {2,4:0} {3,4:0}", box.X, box.Y, box.Width, box.Height).PadRight(34)
                        + string.Format("{0,5:0.0} x {1,5:0.0}", dw, dh).PadRight(16)
                        + string.Format("{0,5:0.0} , {1,5:0.0}", cx, cy));
                }
                Console.WriteLine();

                // 1) 每个图标都没被命中区裁掉、也没画到命中区外面
                for (int i = 0; i < rects.Length; i++)
                {
                    var r = (Rectangle)Get(form, rects[i].field);
                    bool inside = boxes[i].Width > 0 && boxes[i].X >= r.X - 0.5f && boxes[i].Y >= r.Y - 0.5f
                        && boxes[i].Right <= r.Right + 0.5f && boxes[i].Bottom <= r.Bottom + 0.5f;
                    Ok(rects[i].name + " 墨迹落在命中区里", inside,
                        "box=" + Fmt(boxes[i]) + " rect=" + r);
                }

                // 2) 五个图标宽/高彼此相差 ≤15%（视觉尺寸统一的关键）
                float minW = float.MaxValue, maxW = 0, minH = float.MaxValue, maxH = 0;
                foreach (var b in boxes)
                {
                    minW = Math.Min(minW, b.Width); maxW = Math.Max(maxW, b.Width);
                    minH = Math.Min(minH, b.Height); maxH = Math.Max(maxH, b.Height);
                }
                Ok("五个图标宽度彼此相差 ≤15%", maxW <= minW * 1.15f,
                    string.Format("min={0:0}px max={1:0}px 比值={2:0.000}", minW, maxW, maxW / minW));
                Ok("五个图标高度彼此相差 ≤15%", maxH <= minH * 1.15f,
                    string.Format("min={0:0}px max={1:0}px 比值={2:0.000}", minH, maxH, maxH / minH));

                // 3) 每个图标墨迹中心与命中区中心偏差 ≤3px
                for (int i = 0; i < rects.Length; i++)
                {
                    float dx = boxes[i].X + boxes[i].Width / 2f - centers[i].X;
                    float dy = boxes[i].Y + boxes[i].Height / 2f - centers[i].Y;
                    Ok(rects[i].name + " 中心对齐命中区中心",
                        Math.Abs(dx) <= 3f && Math.Abs(dy) <= 3f,
                        string.Format("偏差 {0:0.0} , {1:0.0} px", dx, dy));
                }

                // 4) 视觉外框约 14 DIP 见方（±1 DIP 容差）
                for (int i = 0; i < rects.Length; i++)
                {
                    float dw = boxes[i].Width / 1.5f, dh = boxes[i].Height / 1.5f;
                    Ok(rects[i].name + " 视觉外框约 14 DIP",
                        dw >= 12.4f && dw <= 15.2f && dh >= 12.4f && dh <= 15.2f,
                        string.Format("{0:0.0} x {1:0.0} DIP", dw, dh));
                }
            }
        }
    }

    /// <summary>用命中区四周 3px 的一圈像素取背景中位色（图标墨迹离边框至少 5px），
    /// 再逐像素找色差大于阈值的包围盒。</summary>
    static RectangleF InkBox(Bitmap bmp, Rectangle r, int threshold)
    {
        var ringR = new System.Collections.Generic.List<int>();
        var ringG = new System.Collections.Generic.List<int>();
        var ringB = new System.Collections.Generic.List<int>();
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                bool ring = x < r.X + 3 || x >= r.Right - 3 || y < r.Y + 3 || y >= r.Bottom - 3;
                if (!ring) continue;
                Color c = bmp.GetPixel(x, y);
                ringR.Add(c.R); ringG.Add(c.G); ringB.Add(c.B);
            }
        int mr = MedianList(ringR), mg = MedianList(ringG), mb = MedianList(ringB);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                Color c = bmp.GetPixel(x, y);
                int d = Math.Max(Math.Abs(c.R - mr), Math.Max(Math.Abs(c.G - mg), Math.Abs(c.B - mb)));
                if (d > threshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        if (maxX < 0) return RectangleF.Empty;
        return new RectangleF(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    static int MedianList(System.Collections.Generic.List<int> a)
    {
        var copy = a.ToArray();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    static string Fmt(RectangleF b)
    {
        return string.Format("({0:0},{1:0},{2:0},{3:0})", b.X, b.Y, b.Width, b.Height);
    }

    /// <summary>把右上角图标那一行裁出来放大 4 倍，方便肉眼比对。</summary>
    static string SaveZoom(Bitmap src, string outPath)
    {
        var crop = new Rectangle(180, 8, 240, 44);
        string zoomPath = System.IO.Path.ChangeExtension(outPath, null) + "-zoom.png";
        using (var small = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(small))
            {
                g.Clear(Color.White);
                g.DrawImage(src, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
            }
            using (var zoom = new Bitmap(crop.Width * 4, crop.Height * 4, PixelFormat.Format32bppArgb))
            {
                zoom.SetResolution(144, 144);
                using (var g = Graphics.FromImage(zoom))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(small, new Rectangle(0, 0, zoom.Width, zoom.Height));
                }
                zoom.Save(zoomPath, ImageFormat.Png);
            }
        }
        return zoomPath;
    }
}
