using System;
using System.Threading;
using WorkCountdown;

// 小纸条公共中继实测：两个不同 5 位 ID 互发一条，验证连、发、订阅、收。
// 用法：RelayProbe.exe [对方ID]
class RelayProbe
{
    static void Main(string[] args)
    {
        var a = new NoteSettings();
        a.Normalize();
        var b = new NoteSettings();
        b.Normalize();
        if (args != null && args.Length > 0 && NoteSettings.ValidId(args[0])) b.id = args[0];

        string gotFrom = null, gotText = null;
        var ev = new ManualResetEvent(false);

        using (var ra = new NoteRelay(a))
        using (var rb = new NoteRelay(b))
        {
            rb.Message += delegate(NoteIncoming m)
            {
                gotFrom = m.from;
                gotText = m.text;
                ev.Set();
            };
            ra.ConnectedChanged += delegate(bool on) { Console.WriteLine("A connected=" + on + " (" + a.id + ")"); };
            rb.ConnectedChanged += delegate(bool on) { Console.WriteLine("B connected=" + on + " (" + b.id + ")"); };
            ra.Start();
            rb.Start();
            Thread.Sleep(4000);
            Console.WriteLine("A=" + ra.Connected + " B=" + rb.Connected);
            if (!ra.Connected || !rb.Connected)
            {
                Console.WriteLine("RELAY FAIL: 没有连上");
                Environment.ExitCode = 2;
                return;
            }
            bool sent = ra.SendMessage(b.id, "小纸条中继测试 " + DateTime.Now.ToString("HH:mm:ss"));
            Console.WriteLine("send=" + sent);
            bool ok = ev.WaitOne(8000);
            Console.WriteLine("recv=" + ok + " from=" + gotFrom + " text=" + gotText);
            if (!ok || gotFrom != a.id || gotText == null)
            {
                Console.WriteLine("RELAY FAIL: 没收到");
                Environment.ExitCode = 3;
                return;
            }
            Console.WriteLine("RELAY OK");
            Environment.ExitCode = 0;
        }
    }
}
