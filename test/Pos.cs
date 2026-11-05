using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using WorkCountdown;

class PosProbe
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Console.WriteLine("主屏 " + Screen.PrimaryScreen.Bounds);
        foreach (Screen s in Screen.AllScreens)
            Console.WriteLine("  " + s.DeviceName + " bounds=" + s.Bounds + " working=" + s.WorkingArea);

        var store = new SettingsStore();
        var st = store.Load();
        st.ui.edge = "free";
        st.ui.x = 2154;
        st.ui.y = 958;
        using (var form = new WidgetForm(store, st))
        {
            var f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            form.GetType().GetMethod("PlaceInitial", f).Invoke(form, null);
            form.Show();
            Application.DoEvents();
            RECT r;
            GetWindowRect(form.Handle, out r);
            Console.WriteLine("form.Left/Top = " + form.Left + "," + form.Top + "  Size=" + form.Width + "x" + form.Height);
            Console.WriteLine("window rect  = " + r.L + "," + r.T + "  " + (r.R - r.L) + "x" + (r.B - r.T));
            Console.WriteLine("DeviceDpi=" + form.DeviceDpi + "  scale=" + form.GetType().GetField("scale", f).GetValue(form));
        }
    }
}
