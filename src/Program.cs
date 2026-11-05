using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WorkCountdown
{
    internal static class Program
    {
        private const string MutexName = "WorkCountdownWidget.SingleInstance";
        private const string WindowTitle = "下班倒计时";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr h, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr h);

        [STAThread]
        private static void Main(string[] args)
        {
            // 上一次退出可能还没把进程收干净：等一小会儿再抢，别让「重新打开没反应」发生
            Mutex mutex = null;
            bool createdNew = false;
            for (int attempt = 0; attempt < 25 && !createdNew; attempt++)
            {
                mutex = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    mutex.Dispose();
                    mutex = null;
                    Thread.Sleep(120);
                }
            }

            if (!createdNew)
            {
                // 确实还有一个在跑：把它的窗口叫到前面来，别什么都不做就退出
                BringExistingToFront();
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // --portable：配置写到 exe 旁边的 data 目录，U 盘 / 免安装用
                if (args != null && Array.IndexOf(args, "--portable") >= 0)
                {
                    try
                    {
                        string marker = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.txt");
                        if (!System.IO.File.Exists(marker))
                            System.IO.File.WriteAllText(marker, "便携模式：配置保存在同目录的 data 文件夹里\r\n", new System.Text.UTF8Encoding(false));
                    }
                    catch { }
                }

                var store = new SettingsStore();
                bool firstRun = !store.Exists;
                var settings = store.Load();

                var form = new WidgetForm(store, settings);
                form.EnableTray();

                if (firstRun)
                {
                    form.Shown += delegate
                    {
                        form.BeginInvoke((MethodInvoker)delegate { form.RunFirstRun(); });
                    };
                }
                else if (args != null && args.Length > 0 && args[0] == "--settings")
                {
                    form.Shown += delegate { form.BeginInvoke((MethodInvoker)delegate { form.OpenSettingsPublic(); }); };
                }

                Application.Run(form);
            }
            finally
            {
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    mutex.Dispose();
                }
            }
        }

        /// <summary>已经有一个实例在跑：找到它的窗口，显示出来并激活。</summary>
        private static void BringExistingToFront()
        {
            try
            {
                IntPtr h = FindWindow(null, WindowTitle);
                if (h == IntPtr.Zero) return;
                ShowWindow(h, 5);            // SW_SHOW
                SetForegroundWindow(h);
            }
            catch { }
        }
    }
}
