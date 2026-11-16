using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WorkCountdown;

/// <summary>
/// 设置窗口换肤实拍 + 切肤联动自检：
///  1) 6 套皮肤各拍一张「外观」页（外加默认/无垠的「考勤」页），PrintWindow 拍窗口本体；
///  2) 从一个窗口实例里点风格块 → 检查内存 ui.skin、主题色卡锁定/提示、保存写盘字段；
/// 输出到 test\visual\skins\，WC_NO_SAVE=1 不会回写用户设置。
/// </summary>
static class SkinSettingsShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "skins");
        Directory.CreateDirectory(dir);

        var store = new SettingsStore();
        string[] ids = { "default", "quiet", "mica", "paper", "instrument", "neon" };

        // ---- 1. 每套皮肤各来一张 ----
        foreach (string id in ids)
        {
            var s = MakeSettings(id);
            using (var dlg = new SettingsForm(store, s))
            {
                dlg.CreateControl();
                dlg.Show();
                Pump(120);

                var tabs = (TabControl)Get(dlg, "tabs");
                Call(dlg, "SelectPage", 3);          // 外观
                Pump(180);

                Report(dlg, "外观/" + id);
                Capture(dlg, Path.Combine(dir, "settings-" + id + ".png"), true);

                if (id == "default" || id == "quiet")
                {
                    Call(dlg, "SelectPage", 0);      // 考勤（看卡片 / 输入框 / 标签）
                    Pump(150);
                    Capture(dlg, Path.Combine(dir, "settings-" + id + "-attendance.png"), false);
                }
                dlg.Close();
            }
        }

        // ---- 2. 同一个窗口里点风格块：立刻预览 + 主题色卡联动 + 保存字段 ----
        Check("切换联动", store, dir);

        Console.WriteLine("skins -> " + dir);
    }

    static void Check(string tag, SettingsStore store, string dir)
    {
        var s = MakeSettings("default");
        using (var dlg = new SettingsForm(store, s))
        {
            dlg.CreateControl();
            dlg.Show();
            Pump(120);
            Call(dlg, "SelectPage", 3);
            Pump(150);

            var skinPicker = (Control)Get(dlg, "skinPicker");
            var picker = (ThemePicker)Get(dlg, "picker");
            var lblLocked = (Label)Get(dlg, "lblThemeLocked");
            var tabs = (TabControl)Get(dlg, "tabs");
            var page = tabs.TabPages[3];

            // 点第 6 块（霓虹）
            ClickBlock(skinPicker, 5);
            Pump(150);
            Console.WriteLine("[check] 点霓虹 -> ui.skin=" + s.ui.skin +
                " / picker.Locked=" + picker.Locked + " / 提示可见=" + lblLocked.Visible +
                " / 选中块=" + (string)Get(skinPicker, "selectedId"));
            Capture(dlg, Path.Combine(dir, "settings-preview-neon.png"), false);

            // 点回默认
            ClickBlock(skinPicker, 0);
            Pump(150);
            Console.WriteLine("[check] 点回默认 -> ui.skin=" + s.ui.skin +
                " / picker.Locked=" + picker.Locked + " / 提示可见=" + lblLocked.Visible);

            // 点仪表，再走一次保存（closeAfter=false，只写盘不关窗）
            ClickBlock(skinPicker, 4);
            Pump(120);
            var save = typeof(SettingsForm).GetMethod("Save", F, null, new Type[] { typeof(bool) }, null);
            bool ok = (bool)save.Invoke(dlg, new object[] { false });
            var skinField = typeof(SettingsForm).GetField("skinPicker", F).GetValue(dlg);
            Console.WriteLine("[check] 保存后 -> ok=" + ok + " / s.ui.skin=" + s.ui.skin +
                " / 窗口标题=" + dlg.Text);
            Console.WriteLine("[check] 外观页卡片数=" + page.Controls.Count +
                " / 页高=" + page.Height + " / ClientSize=" + dlg.ClientSize);

            // 滚轮：外观页比可视高度长，给色卡发一条 WM_MOUSEWHEEL 看内容区滚没滚
            var host = (Panel)Get(dlg, "contentHost");
            int before = -host.AutoScrollPosition.Y;
            PostMessage(picker.Handle, 0x020A, (IntPtr)(-120 << 16), IntPtr.Zero);   // 往下滚一格
            Pump(150);
            int after = -host.AutoScrollPosition.Y;
            Console.WriteLine("[check] 滚轮滚动量 before=" + before + " after=" + after +
                " (滚轮" + (after > before ? "生效" : "没生效") + ")");

            // 滚回顶部，再拍一张切到「纸条」页的样子（内容也超一屏）
            host.AutoScrollPosition = new Point(0, 0);
            Call(dlg, "SelectPage", 6);
            Pump(150);
            Console.WriteLine("[check] 纸条页 -> 页高=" + tabs.TabPages[6].Height +
                " / 可滚=" + (host.VerticalScroll.Maximum - host.ClientSize.Height));
            Capture(dlg, Path.Combine(dir, "settings-note-page.png"), false);
            dlg.Close();
        }
    }

    /// <summary>反射调 OnMouseDown 点第 index 个风格块（= 鼠标点在块中心）。</summary>
    static void ClickBlock(Control skinPicker, int index)
    {
        var rectOf = skinPicker.GetType().GetMethod("RectOf", F);
        var r = (Rectangle)rectOf.Invoke(skinPicker, new object[] { index });
        var onDown = skinPicker.GetType().GetMethod("OnMouseDown", F);
        onDown.Invoke(skinPicker, new object[]
            { new MouseEventArgs(MouseButtons.Left, 1, r.X + r.Width / 2, r.Y + r.Height / 2, 0) });
    }

    static Settings MakeSettings(string skin)
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.skin = skin;
        s.ui.theme = "midnight";
        s.ui.opacity = 94;
        s.ui.autoCollapseSeconds = 20;
        s.mode = "fixed";
        s.startTime = "09:00";
        s.endTime = "18:00";
        s.Normalize();
        Themes.SetCustom(s.customThemes);
        return s;
    }

    static void Report(Form dlg, string pageName)
    {
        var tabs = (TabControl)Get(dlg, "tabs");
        var page = tabs.TabPages[tabs.SelectedIndex];
        var host = (Panel)Get(dlg, "contentHost");
        var clip = (Panel)Get(dlg, "tabClip");
        Console.WriteLine(pageName + " | client=" + dlg.ClientSize +
            " | page=" + page.Width + "x" + page.Height +
            " | tabHeader=" + Get(dlg, "tabHeader") + " chrome=" + Get(dlg, "tabChrome") +
            " | clip=" + clip.Bounds +
            " | host=" + host.ClientSize +
            " | scrollRange=" + (host.VerticalScroll.Maximum - host.ClientSize.Height));
    }

    static void Capture(Form f, string path, bool report)
    {
        int w = Math.Max(1, f.Width), h = Math.Max(1, f.Height);
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(255, 32, 32, 36));
                IntPtr hdc = g.GetHdc();
                try { PrintWindow(f.Handle, hdc, 2); }
                finally { g.ReleaseHdc(hdc); }
            }
            bmp.Save(path, ImageFormat.Png);
        }
        if (report) Console.WriteLine("  -> " + path + "  " + w + "x" + h);
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }
}
