using System;
using System.Threading;
using WorkCountdown;

// 给某个 5 位 ID（装在跑的那台）发一条小纸条，用来验证接收 / 闪光 / 未读。
// 用法：NoteSend.exe 46040 "你好，这是一条测试"
class NoteSend
{
    static void Main(string[] args)
    {
        if (args == null || args.Length < 2 || !NoteSettings.ValidId(args[0]))
        {
            Console.WriteLine("用法：NoteSend.exe <5位ID> <文字>");
            Environment.ExitCode = 2;
            return;
        }
        var me = new NoteSettings();
        me.Normalize();
        Console.WriteLine("from=" + me.id + " to=" + args[0]);
        using (var relay = new NoteRelay(me))
        {
            relay.ConnectedChanged += delegate(bool on) { Console.WriteLine("connected=" + on); };
            relay.Start();
            for (int i = 0; i < 40 && !relay.Connected; i++) Thread.Sleep(100);
            if (!relay.Connected) { Console.WriteLine("RELAY NOT CONNECTED"); Environment.ExitCode = 3; return; }
            bool ok = relay.SendMessage(args[0], args[1]);
            Console.WriteLine("sent=" + ok);
            Thread.Sleep(250);
            Environment.ExitCode = ok ? 0 : 4;
        }
    }
}
