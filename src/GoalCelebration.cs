using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace WorkCountdown
{
    // ============================================================================
    // 卡内「今日目标达成」庆祝：时间轴 + 画面参数 + 六个版本（01/05/06/07/08/10）
    //
    // 设计稿：design/card-spread-notify-v4.html，规格：design/GOAL-SPEC-v4.md
    // 卡面 280×188 DIP（收起 224×68），文字/底色裁在卡内，装饰可以出框（出框走
    // CelebrationOverlayForm 的透明层，这里只负责卡内那一刀）。
    // ============================================================================

    /// <summary>
    /// 一次庆祝的时间轴：入场 1.2s → 停留（默认 6s，跟 ui.reminderHoldSeconds 走）→ 退场 0.8s。
    /// 只算时间，不碰窗口；探针用 FrozenMs 把时钟钉在某一帧。
    /// </summary>
    internal sealed class GoalCelebration
    {
        public const int InMs = 1200;
        public const int OutMs = 800;
        public const int DefaultHoldMs = 6000;

        /// <summary>测试探针：>= 0 时 ElapsedMs 直接返回它（同一帧可以反复出图）。</summary>
        internal static int FrozenMs = -1;

        private DateTime startAt = DateTime.MinValue;
        private DateTime exitAt = DateTime.MinValue;
        private double exitFromMs;

        public bool Active { get; private set; }
        public bool Exiting { get; private set; }
        public string VariantId { get; private set; }
        public bool Quiet { get; private set; }
        public int HoldMs { get; private set; }

        public GoalCelebration()
        {
            VariantId = GoalVariants.DefaultId;
            HoldMs = DefaultHoldMs;
        }

        public void Start(string variantId, bool quiet, int holdMs)
        {
            VariantId = GoalVariants.Normalize(variantId);
            Quiet = quiet;
            HoldMs = Math.Max(3000, Math.Min(20000, holdMs));
            startAt = DateTime.Now;
            exitAt = DateTime.MinValue;
            exitFromMs = 0;
            Exiting = false;
            Active = true;
        }

        /// <summary>原地从头再播一遍（右键「预览通知 → 目标达成」用）。</summary>
        public void Replay()
        {
            startAt = DateTime.Now;
            exitAt = DateTime.MinValue;
            exitFromMs = 0;
            Exiting = false;
            Active = true;
        }

        public void BeginExit()
        {
            if (!Active || Exiting) return;
            Exiting = true;
            exitAt = DateTime.Now;
            exitFromMs = ElapsedMs(DateTime.Now);
        }

        public void Stop()
        {
            Active = false;
            Exiting = false;
            startAt = DateTime.MinValue;
        }

        /// <summary>从开始到现在的毫秒（退场阶段继续往上走）。</summary>
        public double ElapsedMs(DateTime now)
        {
            if (FrozenMs >= 0) return FrozenMs;
            if (!Active) return 0;
            if (Exiting) return exitFromMs + (now - exitAt).TotalMilliseconds;
            return (now - startAt).TotalMilliseconds;
        }

        /// <summary>总时长（入场 + 停留 + 退场），过点就没必要再画了。</summary>
        public double TotalMs(DateTime now)
        {
            return InMs + HoldMs + OutMs;
        }

        /// <summary>推进一帧：到时自动进入退场、退场放完自动结束。返回是否还活着。</summary>
        public bool Tick(DateTime now)
        {
            if (!Active) return false;
            double ms = ElapsedMs(now);
            if (!Exiting)
            {
                if (Quiet)
                {
                    // 静态帧：停留到点直接结束，不做退场
                    if (ms >= InMs + HoldMs) { Stop(); return false; }
                    return true;
                }
                if (ms >= InMs + HoldMs) BeginExit();
                return true;
            }
            if (ms >= InMs + HoldMs + OutMs) { Stop(); return false; }
            return true;
        }

        /// <summary>入场进度 0..1（退场时恒为 1）。</summary>
        public double InP(double ms)
        {
            if (Quiet) return ms >= InMs ? 1 : Math.Max(0, ms / InMs);
            return GoalScene.Clamp01(ms / InMs);
        }

        /// <summary>退场进度 0..1。</summary>
        public double OutP(double ms)
        {
            double tail = ms - InMs - HoldMs;
            if (tail <= 0) return 0;
            return GoalScene.Clamp01(tail / OutMs);
        }
    }

    // ============================================================================
    // 画面参数：一次绘制需要的全部上下文
    // ============================================================================

    internal sealed class GoalScene
    {
        public Graphics g;
        /// <summary>卡面宽/高（物理像素）。展开 = 280×188×scale，收起 = 224×68×scale。</summary>
        public float W, H;
        public float scale = 1f;
        public bool collapsed;
        /// <summary>reducedMotion / HighContrast：只画静态帧，不做闪烁、爆开、抖动。</summary>
        public bool quiet;
        /// <summary>true = 正在画卡外溢出层（坐标原点仍是卡面左上角，允许负坐标）。</summary>
        public bool outside;
        /// <summary>秒（从入场开始）。</summary>
        public double t;
        /// <summary>入场 0..1。</summary>
        public double In;
        /// <summary>退场 0..1（0 = 全亮，1 = 退完）。</summary>
        public double Out;

        public float S(float dip) { return dip * scale; }
        public float CX { get { return W / 2f; } }
        public float CY { get { return H / 2f; } }
        /// <summary>停留强度：入场后 1，退场往 0 落。</summary>
        public double Hold { get { return Math.Max(0, 1 - Out); } }
        public double InOut { get { return Math.Min(In, Math.Max(0, 1 - Out)); } }

        public Font Serif(float dip, FontStyle style = FontStyle.Regular) { return GoalFonts.Get("serif", dip, style); }
        public Font Sans(float dip, FontStyle style = FontStyle.Regular) { return GoalFonts.Get("sans", dip, style); }
        public Font Kai(float dip, FontStyle style = FontStyle.Regular) { return GoalFonts.Get("kai", dip, style); }
        public Font Mono(float dip, FontStyle style = FontStyle.Regular) { return GoalFonts.Get("mono", dip, style); }

        /// <summary>t 在 [from, from+dur] 秒里的 0..1 缓动进度（smoothstep）。</summary>
        public double Ease(double fromSec, double durSec)
        {
            if (durSec <= 0) return t >= fromSec ? 1 : 0;
            return Clamp01((t - fromSec) / durSec);
        }

        public double Linear(double fromSec, double durSec)
        {
            if (durSec <= 0) return t >= fromSec ? 1 : 0;
            return Clamp01((t - fromSec) / durSec);
        }

        /// <summary>0..1 之间的正弦脉冲（用来做呼吸、闪烁）。</summary>
        public double Pulse(double periodSec, double phaseSec = 0)
        {
            if (periodSec <= 0) return 1;
            return 0.5 - 0.5 * Math.Cos(2 * Math.PI * ((t + phaseSec) / periodSec));
        }

        public double Ramp(double fromSec, double toSec, double a, double b)
        {
            return a + (b - a) * Ease(fromSec, toSec - fromSec);
        }

        public static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        public static double Smooth(double v)
        {
            v = Clamp01(v);
            return v * v * (3 - 2 * v);
        }
    }

    // ============================================================================
    // 版本接口 + 注册表（各版本在自己的文件里实现，注册表反射自动收集）
    // ============================================================================

    internal interface IGoalVariant
    {
        /// <summary>设计稿编号："01" / "05" / "06" / "07" / "08" / "10"。</summary>
        string id { get; }
        /// <summary>设置里显示的名字。</summary>
        string name { get; }
        /// <summary>是否有越出卡框的装饰（有才会拉起透明出框层）。</summary>
        bool hasOutside { get; }
        /// <summary>画卡内部分：调用方已把坐标原点对齐到卡面左上角、并按卡片圆角裁好。</summary>
        void PaintInside(GoalScene s);
        /// <summary>画出框部分：坐标原点同样在卡面左上角，允许负坐标。</summary>
        void PaintOutside(GoalScene s);
    }

    internal static class GoalVariants
    {
        public const string DefaultId = "05";

        private static List<IGoalVariant> all;
        private static readonly Dictionary<string, IGoalVariant> map = new Dictionary<string, IGoalVariant>();

        public static IReadOnlyList<IGoalVariant> All()
        {
            Ensure();
            return all;
        }

        public static IGoalVariant Get(string id)
        {
            Ensure();
            IGoalVariant v;
            return id != null && map.TryGetValue(id, out v) ? v : null;
        }

        /// <summary>把任意输入收敛成已知编号，未知就用默认（05 星屑）。</summary>
        public static string Normalize(string id)
        {
            Ensure();
            return id != null && map.ContainsKey(id) ? id : DefaultId;
        }

        private static void Ensure()
        {
            if (all != null) return;
            var list = new List<IGoalVariant>();
            Type[] types;
            try { types = typeof(GoalVariants).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            if (types != null)
            {
                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface || !t.IsClass) continue;
                    if (!typeof(IGoalVariant).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    try
                    {
                        var v = (IGoalVariant)Activator.CreateInstance(t);
                        if (v != null && !string.IsNullOrEmpty(v.id) && !map.ContainsKey(v.id))
                        {
                            map[v.id] = v;
                            list.Add(v);
                        }
                    }
                    catch { }
                }
            }
            list.Sort(delegate(IGoalVariant a, IGoalVariant b) { return string.CompareOrdinal(a.id, b.id); });
            all = list;
        }

        /// <summary>设置下拉用的 (id, 显示名)。</summary>
        public static string[] Ids()
        {
            Ensure();
            var ids = new List<string>();
            foreach (var v in all) ids.Add(v.id);
            return ids.ToArray();
        }

        public static string NameOf(string id)
        {
            var v = Get(Normalize(id));
            return v == null ? id : v.name;
        }
    }

    // ============================================================================
    // 字体：按「角色 + DIP 字号」缓存，字号换算成 pt（1 DIP = 0.75pt）
    // ============================================================================

    internal static class GoalFonts
    {
        private static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
        private static readonly Dictionary<string, string> resolved = new Dictionary<string, string>();

        // 宋体（衬线）/ 楷体 / 黑体 / 等宽 四套，按「装了哪个用哪个」的顺序找
        private static readonly string[][] families = new string[][]
        {
            new string[] { "Source Han Serif SC", "Noto Serif SC", "Songti SC", "STSong", "SimSun", "宋体", "STZhongsong", "DengXian" },
            new string[] { "KaiTi", "Kaiti SC", "STKaiti", "楷体", "KaiTi_GB2312", "Source Han Serif SC", "SimSun", "宋体" },
            new string[] { "PingFang SC", "Noto Sans SC", "Microsoft YaHei UI", "Microsoft YaHei" },
            new string[] { "Consolas", "Cascadia Mono", "Courier New" }
        };

        private static string RoleIndex(string role)
        {
            if (role == "serif") return "0";
            if (role == "kai") return "1";
            if (role == "mono") return "3";
            return "2";
        }

        /// <summary>清掉缓存（换字体 / 设置页应用后调一次；字体对象不释放，进程里就这么点）。</summary>
        public static void Reset()
        {
            cache.Clear();
            resolved.Clear();
        }

        public static Font Get(string role, float dip, FontStyle style)
        {
            int size = Math.Max(6, (int)Math.Round(dip * 0.75f));   // pt
            string key = RoleIndex(role) + "|" + size + "|" + (int)style;
            Font f;
            if (cache.TryGetValue(key, out f)) return f;

            string family = Resolve(role);
            try
            {
                if (string.Equals(role, "sans", StringComparison.Ordinal))
                    f = UiFont.Make(size, style);          // 跟着设置的界面字体走
                else if (!string.IsNullOrEmpty(family) && !string.Equals(family, "sans", StringComparison.Ordinal))
                    f = new Font(family, size, style, GraphicsUnit.Point);
                else
                    f = UiFont.Make(size, style);
            }
            catch { f = UiFont.Make(size, style); }
            cache[key] = f;
            return f;
        }

        /// <summary>这一套里第一个装了的族名；sans 返回 "sans"（交给 UiFont 走设置）。</summary>
        private static string Resolve(string role)
        {
            string key = RoleIndex(role);
            string done;
            if (resolved.TryGetValue(key, out done)) return done;
            string found = null;
            if (key == "2") found = "sans";
            else
            {
                List<string> installed = null;
                try
                {
                    using (var col = new InstalledFontCollection())
                    {
                        installed = new List<string>();
                        foreach (var fam in col.Families) installed.Add(fam.Name);
                    }
                }
                catch { }
                if (installed != null)
                {
                    var prefer = families[int.Parse(key)];
                    foreach (var want in prefer)
                        foreach (var name in installed)
                            if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) { found = name; break; }
                }
            }
            if (found == null) found = "sans";
            resolved[key] = found;
            return found;
        }
    }
}
