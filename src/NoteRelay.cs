using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace WorkCountdown
{
    /// <summary>收到的一条小纸条（从中继解析出来）。</summary>
    public class NoteIncoming
    {
        public string from;
        public string to;
        public string text;
        public long ts;
        public string nick;     // 发送方自己设置的昵称（没有就是 ""）
    }

    /// <summary>
    /// 小纸条的公共中继：手写 MQTT 3.1.1（TCP），不引第三方库。
    /// 默认连 broker.hivemq.com:1883；两种对话各用一套主题：
    ///   1:1（收件人是个人 5 位 ID）  inbox xzt/v1/msg/&lt;id&gt;
    ///   对话码（任意 5 位字符）      xzt/v1/room/&lt;hex(UTF8(code))&gt;
    ///   在线状态 xzt/v1/presence/&lt;id&gt;（retained，用来按 ID 搜索 / 判断在线）
    /// 中继只转发，不落库；消息只存在本机 settings.json（每个对话最多 8 条）。
    /// 对话码走十六进制编码，任意字符（中文 / 标点 / MQTT 通配符）都能安全放进主题。
    /// </summary>
    public class NoteRelay : IDisposable
    {
        private const string TopicPrefix = "xzt/v1";
        internal const string RoomTopicPrefix = TopicPrefix + "/room/";
        internal const string PresenceTopicPrefix = TopicPrefix + "/presence/";
        private readonly Func<NoteSettings> settingsProvider;
        private readonly string clientId;
        private TcpClient tcp;
        private NetworkStream stream;
        private Thread worker;
        private volatile bool running;
        private volatile bool connected;
        private readonly object writeLock = new object();
        private readonly object probeLock = new object();
        private readonly object subLock = new object();
        private readonly Dictionary<string, Action<bool>> probes = new Dictionary<string, Action<bool>>();
        private readonly HashSet<string> subscribed = new HashSet<string>();
        private int subPacketId;
        private Timer keepAlive;
        private int disposed;

        public event Action<NoteIncoming> Message;
        public event Action<bool> ConnectedChanged;
        /// <summary>收到某个人的在线状态（对方 retained presence）：(对方 5 位 ID, 对方昵称, 是否在线)。</summary>
        public event Action<string, string, bool> Profile;

        public bool Connected { get { return connected; } }

        /// <summary>固定配置（测试 / 探针）：中继始终读这一份。</summary>
        public NoteRelay(NoteSettings settings)
            : this(Constant(settings))
        {
        }

        /// <summary>
        /// 配置提供者：每次访问都读当前 settings.note。窗口换了一份 Settings 时不需要重建连接，
        /// 昵称 / 对话变化也能在同一个连接上原地生效。
        /// </summary>
        public NoteRelay(Func<NoteSettings> settingsProvider)
        {
            this.settingsProvider = settingsProvider;
            NoteSettings initial = Cfg;
            clientId = "xzt" + initial.id + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static Func<NoteSettings> Constant(NoteSettings settings)
        {
            return delegate { return settings; };
        }

        private NoteSettings Cfg
        {
            get
            {
                NoteSettings current = settingsProvider == null ? null : settingsProvider();
                return current == null ? new NoteSettings() : current;
            }
        }

        public void Start()
        {
            if (running) return;
            running = true;
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "note-relay";
            worker.Start();
        }

        public void Stop()
        {
            running = false;
            try { if (tcp != null) tcp.Close(); } catch { }
            try { if (keepAlive != null) keepAlive.Dispose(); } catch { }
            keepAlive = null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1) return;
            Stop();
        }

        // ---------------------------------------------------------------- 网络循环

        private void Run()
        {
            while (running)
            {
                try
                {
                    var c = new TcpClient();
                    c.NoDelay = true;
                    var ar = c.BeginConnect(Cfg.relayHost, Cfg.relayPort, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(8000))
                    {
                        try { c.Close(); } catch { }
                        throw new IOException("connect timeout");
                    }
                    c.EndConnect(ar);
                    tcp = c;
                    stream = c.GetStream();
                    stream.ReadTimeout = 120000;
                    Send(ConnectPacket(clientId));
                    ReadLoop();
                }
                catch { }
                try { if (tcp != null) tcp.Close(); } catch { }
                tcp = null;
                stream = null;
                bool was = connected;
                connected = false;
                if (was) RaiseConnected(false);
                if (!running) break;
                Thread.Sleep(10000);
            }
        }

        private void ReadLoop()
        {
            while (running)
            {
                int first = stream.ReadByte();
                if (first < 0) return;
                int length = 0, multiplier = 1, ib;
                do
                {
                    ib = stream.ReadByte();
                    if (ib < 0) return;
                    length += (ib & 0x7f) * multiplier;
                    multiplier *= 128;
                    if (multiplier > 128 * 128 * 128) return;
                } while ((ib & 0x80) != 0);
                if (length < 0 || length > 1024 * 1024) return;
                var body = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = stream.Read(body, read, length - read);
                    if (n <= 0) return;
                    read += n;
                }
                Handle((byte)(first >> 4), (byte)(first & 0x0f), body);
            }
        }

        private void Handle(byte type, byte flags, byte[] body)
        {
            if (type == 2)                 // CONNACK
            {
                connected = true;
                lock (subLock) { subscribed.Clear(); subPacketId = 0; }
                SubscribeTopic(InboxTopic(Cfg.id));
                SubscribeRooms();
                SubscribeBulletin();
                SubscribeContactPresence();
                Send(PublishPacket(PresenceTopic(Cfg.id), PresenceJson(Cfg.id, Cfg.nickname), true));
                StartKeepAlive();
                RaiseConnected(true);
                return;
            }
            if (type == 3)                 // PUBLISH
            {
                string topic;
                string payload;
                if (!ParsePublish(flags, body, out topic, out payload)) return;
                AcceptPublish(topic, payload);
                return;
            }
            // SUBACK / PINGRESP / 其它：不影响
        }

        /// <summary>
        /// 一条 PUBLISH 的业务分发：收件箱 / 对话码 / 在线状态。网络循环只负责解包，
        /// 这里同时是测试可走的分发入口（不需要真实连接）：返回 false 表示主题与本模块无关或被拒绝。
        /// </summary>
        internal bool AcceptPublish(string topic, string payload)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            if (topic == InboxTopic(Cfg.id))
                return DeliverMessage(payload, null);

            if (topic.StartsWith(RoomTopicPrefix, StringComparison.Ordinal))
            {
                // 对话码对话：payload 里的 to 必须就是这个主题对应的码，防止串台
                string code;
                if (!ParseRoomTopic(topic, out code)) return false;
                return DeliverMessage(payload, code);
            }

            if (topic.StartsWith(PresenceTopicPrefix, StringComparison.Ordinal))
            {
                DeliverPresence(topic.Substring(PresenceTopicPrefix.Length), payload);
                return true;
            }
            return false;
        }

        /// <summary>解出一条小纸条并交给 Message；roomCode 非空时要求 to 等于该码。</summary>
        private bool DeliverMessage(string payload, string roomCode)
        {
            NoteIncoming msg = DecodeMessage(payload);
            if (msg == null) return false;
            if (roomCode != null && !string.Equals(msg.to, roomCode, StringComparison.Ordinal)) return false;
            if (Message == null) return true;
            try { Message(msg); } catch { }
            return true;
        }

        private void RaiseConnected(bool on)
        {
            if (ConnectedChanged != null) { try { ConnectedChanged(on); } catch { } }
        }

        private void StartKeepAlive()
        {
            if (keepAlive != null) return;
            keepAlive = new Timer(delegate
            {
                if (!connected) return;
                try { Send(PingPacket()); } catch { }
                try { Send(PublishPacket(PresenceTopic(Cfg.id), PresenceJson(Cfg.id, Cfg.nickname), true)); } catch { }
            }, null, 15000, 15000);
        }

        private void Send(byte[] packet)
        {
            lock (writeLock)
            {
                if (stream == null) throw new IOException("not connected");
                stream.Write(packet, 0, packet.Length);
                stream.Flush();
            }
        }

        // ---------------------------------------------------------------- 对外动作

        /// <summary>
        /// 发一条小纸条，成功返回 true（对方不一定在线，中继不存离线消息）。
        /// 目标是 5 位数字 = 发给这个人；其它 5 位字符 = 发到这个对话码。
        /// </summary>
        public bool SendMessage(string toId, string text)
        {
            string code = NoteSettings.NormalizeCode(toId);
            if (!connected || !NoteSettings.ValidCode(code) || string.IsNullOrEmpty(text)) return false;
            try
            {
                bool room = !NoteSettings.ValidId(code);
                string payload = BuildMessageJson(Cfg.id, code, text, Now(), Cfg.nickname);
                Send(PublishPacket(room ? RoomTopic(code) : InboxTopic(code), payload, false));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 新增（或恢复）对话码对话后补订阅。已连接时调一下；没连接的话 CONNACK 时会全量补。
        /// 订阅只增不减：删掉的对话即使还收着消息，Widget 那边没有对应对话也会丢掉。
        /// 顺带重发一次自己的 retained presence：设置里刚改完昵称时，对方马上就能看到新昵称。
        /// </summary>
        public void RefreshSubscriptions()
        {
            if (!connected) return;
            SubscribeRooms();
            SubscribeBulletin();
            SubscribeContactPresence();
            try { Send(PublishPacket(PresenceTopic(Cfg.id), PresenceJson(Cfg.id, Cfg.nickname), true)); } catch { }
        }

        /// <summary>
        /// 公共房间「大字报」：不管本机对话列表长什么样都订阅（Normalize 会把它补进 contacts，
        /// 这里再显式订一次，保证老配置 / 手改坏的情况下也收得到）。
        /// </summary>
        private void SubscribeBulletin()
        {
            SubscribeTopic(RoomTopic(NoteSettings.BulletinCode));
        }

        private void SubscribeRooms()
        {
            try
            {
                var list = Cfg.contacts == null ? new NoteContact[0] : Cfg.contacts.ToArray();
                for (int i = 0; i < list.Length; i++)
                {
                    var c = list[i];
                    if (c == null) continue;
                    string code = NoteSettings.NormalizeCode(c.id);
                    if (!Cfg.IsRoom(code)) continue;
                    SubscribeTopic(RoomTopic(code));
                }
            }
            catch { }
        }

        /// <summary>
        /// 订阅所有联系人的 retained 在线状态主题：一连上就能拿到对方昵称 / 在线标记，
        /// 之后对方改昵称或上下线也会推过来（见 Profile 事件）。
        /// </summary>
        private void SubscribeContactPresence()
        {
            try
            {
                var list = Cfg.contacts == null ? new NoteContact[0] : Cfg.contacts.ToArray();
                for (int i = 0; i < list.Length; i++)
                {
                    var c = list[i];
                    if (c == null) continue;
                    string id = NoteSettings.NormalizeCode(c.id);
                    if (!NoteSettings.ValidId(id)) continue;
                    SubscribeTopic(PresenceTopic(id));
                }
            }
            catch { }
        }

        /// <summary>订阅一个主题（同一个主题每次连接只发一次 SUBSCRIBE）。</summary>
        private void SubscribeTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return;
            int pid;
            lock (subLock)
            {
                if (subscribed.Contains(topic)) return;
                subPacketId = (subPacketId % 60000) + 1;
                pid = subPacketId;
            }
            try
            {
                Send(SubscribePacket(topic, pid));
                lock (subLock) subscribed.Add(topic);
            }
            catch { }
        }

        /// <summary>按 ID 搜人：订阅对方 retained 在线状态，1.8s 内收到且在 120s 内算在线。</summary>
        public void Probe(string id, Action<bool> done)
        {
            if (!connected || !NoteSettings.ValidId(id)) { if (done != null) done(false); return; }
            lock (probeLock) probes[id] = done;
            try { Send(SubscribePacket(PresenceTopic(id))); } catch { }
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(1800);
                Action<bool> cb = null;
                lock (probeLock)
                {
                    if (probes.TryGetValue(id, out cb)) probes.Remove(id);
                }
                if (cb != null) { try { cb(false); } catch { } }
            });
        }

        /// <summary>
        /// 收到任何人的在线状态（自己订阅的联系人 retained presence、或 Probe 临时订阅）：
        /// 先广播 Profile（id, 对方昵称, 是否在线），再照旧唤醒 Probe 的回调。
        /// </summary>
        private void DeliverPresence(string id, string payload)
        {
            bool online = false;
            string nick = "";
            try
            {
                var ser = new JavaScriptSerializer();
                var p = ser.Deserialize<PresencePayload>(payload);
                if (p != null && p.id == id)
                {
                    online = Math.Abs(Now() - p.ts) <= 120;
                    nick = NoteSettings.CleanNick(p.nick);
                }
            }
            catch { }
            if (!string.Equals(id, Cfg.id, StringComparison.Ordinal)) RaiseProfile(id, nick, online);
            Action<bool> cb = null;
            lock (probeLock)
            {
                if (probes.TryGetValue(id, out cb)) probes.Remove(id);
            }
            if (cb != null) { try { cb(online); } catch { } }
        }

        private void RaiseProfile(string id, string nick, bool online)
        {
            var h = Profile;
            if (h == null) return;
            try { h(id, nick, online); } catch { }
        }

        // ---------------------------------------------------------------- MQTT 编码

        internal static string InboxTopic(string id) { return TopicPrefix + "/msg/" + id; }
        internal static string PresenceTopic(string id) { return TopicPrefix + "/presence/" + id; }

        /// <summary>对话码 → 中继主题：xzt/v1/room/&lt;hex&gt;（任意字符都能安全放进 MQTT 主题）。</summary>
        internal static string RoomTopic(string code) { return RoomTopicPrefix + HexEncode(NoteSettings.NormalizeCode(code)); }

        /// <summary>主题 → 对话码；不是规范的对话码主题就返回 false。</summary>
        internal static bool ParseRoomTopic(string topic, out string code)
        {
            code = null;
            if (string.IsNullOrEmpty(topic)) return false;
            if (!topic.StartsWith(RoomTopicPrefix, StringComparison.Ordinal)) return false;
            string raw = HexDecode(topic.Substring(RoomTopicPrefix.Length));
            if (!NoteSettings.ValidCode(raw)) return false;
            if (!string.Equals(RoomTopic(raw), topic, StringComparison.Ordinal)) return false;   // 只认小写规范形式
            code = raw;
            return true;
        }

        internal static string HexEncode(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            var sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        internal static string HexDecode(string hex)
        {
            if (string.IsNullOrEmpty(hex) || (hex.Length & 1) != 0) return null;
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
            {
                int hi = HexValue(hex[i * 2]), lo = HexValue(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                b[i] = (byte)((hi << 4) | lo);
            }
            try { return Encoding.UTF8.GetString(b); } catch { return null; }
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        internal static long Now()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal static string BuildMessageJson(string from, string to, string text, long ts)
        {
            return BuildMessageJson(from, to, text, ts, "");
        }

        internal static string BuildMessageJson(string from, string to, string text, long ts, string nick)
        {
            var ser = new JavaScriptSerializer();
            return ser.Serialize(new MessagePayload { v = 1, from = from, to = to, text = text, ts = ts, nick = nick ?? "" });
        }

        internal static string PresenceJson(string id)
        {
            return PresenceJson(id, "");
        }

        internal static string PresenceJson(string id, string nick)
        {
            var ser = new JavaScriptSerializer();
            return ser.Serialize(new PresencePayload { v = 1, id = id, ts = Now(), nick = nick ?? "" });
        }

        internal static NoteIncoming DecodeMessage(string payload)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var p = ser.Deserialize<MessagePayload>(payload);
                // from 永远是发送者的个人 5 位 ID；to 可以是个人 ID（1:1）或 5 位对话码
                if (p == null || !NoteSettings.ValidId(p.from) || !NoteSettings.ValidCode(p.to)) return null;
                string text = (p.text ?? "").Trim();
                if (text.Length == 0) return null;
                if (text.Length > NoteSettings.MaxText) text = text.Substring(0, NoteSettings.MaxText);
                var m = new NoteIncoming();
                m.from = p.from;
                m.to = p.to;
                m.text = text;
                m.ts = p.ts > 0 ? p.ts : Now();
                m.nick = NoteSettings.CleanNick(p.nick);
                return m;
            }
            catch { return null; }
        }

        internal class MessagePayload
        {
            public int v { get; set; }
            public string from { get; set; }
            public string to { get; set; }
            public string text { get; set; }
            public long ts { get; set; }
            public string nick { get; set; }
        }

        internal class PresencePayload
        {
            public int v { get; set; }
            public string id { get; set; }
            public long ts { get; set; }
            public string nick { get; set; }
        }

        internal static byte[] ConnectPacket()
        {
            return ConnectPacket("xzt-client");
        }

        internal static byte[] ConnectPacket(string clientId)
        {
            var body = new List<byte>();
            AddString(body, "MQTT");
            body.Add(4);                    // 3.1.1
            body.Add(0x02);                 // clean session
            body.Add(0x00); body.Add(0x3c); // keepalive 60s
            AddString(body, clientId);
            return Packet(1, 0, body);
        }

        internal static byte[] SubscribePacket(string topic)
        {
            return SubscribePacket(topic, 1);
        }

        internal static byte[] SubscribePacket(string topic, int packetId)
        {
            var body = new List<byte>();
            int pid = packetId <= 0 ? 1 : (packetId > 65535 ? 1 : packetId);
            body.Add((byte)(pid >> 8)); body.Add((byte)(pid & 0xff));   // packet id
            AddString(body, topic);
            body.Add(0x00);                      // QoS 0
            return Packet(8, 0x02, body);
        }

        internal static byte[] PublishPacket(string topic, string payload, bool retain)
        {
            var body = new List<byte>();
            AddString(body, topic);
            body.AddRange(Encoding.UTF8.GetBytes(payload));
            return Packet(3, (byte)(retain ? 0x01 : 0x00), body);
        }

        internal static byte[] PingPacket()
        {
            return Packet(12, 0, new List<byte>());
        }

        internal static bool ParsePublish(byte flags, byte[] body, out string topic, out string payload)
        {
            topic = null; payload = null;
            try
            {
                int len = (body[0] << 8) | body[1];
                if (len < 0 || 2 + len > body.Length) return false;
                topic = Encoding.UTF8.GetString(body, 2, len);
                int idx = 2 + len;
                int qos = (flags >> 1) & 3;
                if (qos > 0) idx += 2;          // packet id（QoS0 时没有）
                if (idx > body.Length) return false;
                payload = Encoding.UTF8.GetString(body, idx, body.Length - idx);
                return true;
            }
            catch { return false; }
        }

        private static void AddString(List<byte> list, string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            list.Add((byte)(b.Length >> 8));
            list.Add((byte)(b.Length & 0xff));
            list.AddRange(b);
        }

        private static byte[] Packet(int type, byte flags, List<byte> body)
        {
            var head = new List<byte>();
            head.Add((byte)((type << 4) | flags));
            int len = body.Count;
            do
            {
                int digit = len % 128;
                len /= 128;
                if (len > 0) digit |= 0x80;
                head.Add((byte)digit);
            } while (len > 0);
            head.AddRange(body);
            return head.ToArray();
        }
    }
}
