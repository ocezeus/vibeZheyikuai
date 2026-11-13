using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// 只验证 UpdateLayeredWindow 会不会把 400x300 的源图放大到 800x600 的窗口。
class UlwScale
{
    [StructLayout(LayoutKind.Sequential)] struct P { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct S { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct B { public byte op, flags, alpha, fmt; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref P pt, ref S sz, IntPtr src, ref P ps, int key, ref B b, int f);

    [STAThread]
    static void Main()
    {
        var f = new LayeredForm();
        f.FormBorderStyle = FormBorderStyle.None;
        f.StartPosition = FormStartPosition.Manual;
        f.ShowInTaskbar = false;
        f.SetBounds(100, 100, 800, 600);
        f.Show();
        Thread.Sleep(300);

        using (var bmp = new Bitmap(400, 300, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Red); g.FillRectangle(Brushes.Blue, 200, 0, 200, 300); }
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
            IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            IntPtr old = SelectObject(mem, hb);
            var pt = new P(); var ps = new P(); var sz = new S { cx = 800, cy = 600 };
            var bl = new B { op = 0, flags = 0, alpha = 255, fmt = 1 };
            bool ok = UpdateLayeredWindow(f.Handle, screen, ref pt, ref sz, mem, ref ps, 0, ref bl, 2);
            Console.WriteLine("ulw=" + ok + " err=" + Marshal.GetLastWin32Error());
            f.TopMost = true;
            Thread.Sleep(500);
            using (var shot = new Bitmap(800, 600))
            using (var g = Graphics.FromImage(shot))
            {
                g.CopyFromScreen(100, 100, 0, 0, new Size(800, 600));
                Console.WriteLine("TL=" + shot.GetPixel(50, 50) + " TR=" + shot.GetPixel(750, 50) + " BL=" + shot.GetPixel(50, 550));
                shot.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "ulw-scale.png"));
            }
            SelectObject(mem, old); DeleteObject(hb); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
        f.Close();
    }

    class LayeredForm : Form
    {
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000; return cp; }
        }
    }
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
}
