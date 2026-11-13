using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WorkCountdown;

// 小纸条「编辑窗」实拍：PrintWindow（会话锁屏时 CopyFromScreen 拍不到，PrintWindow 还能拍）。
// 用来肉眼检查圆角输入框、居中字号。
// 用法：NoteEditorShot.exe [输出目录]（默认 test\visual\note-fix）
class NoteEditorShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName);
            Console.WriteLine(ex.Message);
            if (ex.InnerException != null) Console.WriteLine("INNER: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 99;
        }
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        string dir = args != null && args.Length > 0 ? args[0]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "note-fix");
        Directory.CreateDirectory(dir);

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = "huang-liuli";
        s.note.style = "columns";
        s.note.id = "48372";
        s.note.AddContact("62015", "阿哲");
        s.note.AddContact("11788", "小满");
        s.note.AddContact("90554", "老周");
        long now = NoteRelay.Now();
        s.note.Append(new NoteMessage("48372", "62015", "早，今天的评审几点？", now - 300), false);
        s.note.Append(new NoteMessage("62015", "48372", "下午 4 点，会议室 B", now - 240), true);
        s.note.Append(new NoteMessage("48372", "62015", "接口那版发你邮箱了", now - 180), false);
        s.note.Append(new NoteMessage("62015", "48372", "收到，中午看", now - 120), true);

        var card = new Rectangle(20, 600, 420, 282);
        using (var p = new NotePanelForm(s, Themes.Get("huang-liuli"), card, true,
            delegate(string to, string text) { return true; }, null, null, null, null))
        {
            p.Show();
            p.Activate();
            Pump(600);
            Call(p, "SelectContact", "62015", false);
            Pump(300);

            // 1. 卡片细边框（整个面板实拍）
            CaptureWindow(p, Path.Combine(dir, "panel-border.png"));

            // 2. 点输入框 → 圆角编辑窗（和面板里的圆角输入框对齐）
            Call(p, "EditCompose");
            Pump(700);
            Form ed = FindEditor(p);
            if (ed != null)
            {
                CaptureWindow(ed, Path.Combine(dir, "editor-compose.png"));
                Console.WriteLine("compose editor " + ed.Bounds + " region=" + (ed.Region != null));
            }
            else Console.WriteLine("!! compose editor not found");
            if (ed != null) SendEsc(ed);
            Pump(250);

            // 3. 双击联系人名字 → 改备注编辑窗（文字居中、字号大一号）
            Call(p, "SelectContact", "11788", false);
            Pump(200);
            Call(p, "EditRemark", "11788");
            Pump(400);
            Form ed2 = FindEditor(p);
            if (ed2 != null)
            {
                CaptureWindow(ed2, Path.Combine(dir, "editor-remark.png"));
                Console.WriteLine("remark editor " + ed2.Bounds + " region=" + (ed2.Region != null));
            }
            else Console.WriteLine("!! remark editor not found");
            if (ed2 != null) SendEsc(ed2);
            Pump(200);

            p.ClosePanel();
            Pump(400);
        }

        Console.WriteLine("ok " + dir);
    }

    /// <summary>PrintWindow 拍窗口本体重内容（锁屏也能拍；圆角外的区域留深色底，便于看圆角）。</summary>
    static void CaptureWindow(Form f, string path)
    {
        int w = Math.Max(1, f.Width), h = Math.Max(1, f.Height);
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(255, 24, 22, 18));
                IntPtr hdc = g.GetHdc();
                try { PrintWindow(f.Handle, hdc, 2); }
                finally { g.ReleaseHdc(hdc); }
            }
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("ok " + path + "  " + w + "x" + h);
    }

    static void SendEsc(Form ed)
    {
        TextBox box = FindTextBox(ed);
        if (box == null) return;
        PostMessage(box.Handle, 0x0100, (IntPtr)0x1B, IntPtr.Zero);
        PostMessage(box.Handle, 0x0101, (IntPtr)0x1B, IntPtr.Zero);
    }

    static TextBox FindTextBox(Control root)
    {
        foreach (Control c in root.Controls)
        {
            var tb = c as TextBox;
            if (tb != null) return tb;
            var inner = FindTextBox(c);
            if (inner != null) return inner;
        }
        return null;
    }
    static Form FindEditor(NotePanelForm p)
    {
        foreach (Form f in Application.OpenForms)
            if (f != p && !f.IsDisposed && f.GetType().Name == "NoteField") return f;
        return null;
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }

    static object Call(object o, string name, params object[] a)
    {
        MethodInfo found = null;
        foreach (var mi in o.GetType().GetMethods(F))
        {
            if (mi.Name != name) continue;
            if (mi.GetParameters().Length != a.Length) continue;
            found = mi;
            break;
        }
        if (found == null) throw new Exception("no method " + name + "/" + a.Length);
        return found.Invoke(o, a);
    }
}
