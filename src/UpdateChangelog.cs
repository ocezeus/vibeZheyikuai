using System.Collections.Generic;

namespace WorkCountdown
{
    /// <summary>检查更新页「更新记录」里的一条大版本变更。</summary>
    public class ChangelogEntry
    {
        public string Version { get; private set; }   // 大版本号，如 "4.0.0" / "3.8"
        public string Date { get; private set; }      // yyyy-MM-dd
        public string Title { get; private set; }     // 一句话标题（历史字段，列表改看 Summary）
        public string Summary { get; private set; }   // 一句话说清该版本全部变化（列表行完整显示）
        public string[] Bullets { get; private set; } // 2-5 条要点

        public ChangelogEntry(string version, string date, string title, string summary, string[] bullets)
        {
            Version = version;
            Date = date;
            Title = title;
            Summary = summary == null ? "" : summary;
            Bullets = bullets == null ? new string[0] : bullets;
        }
    }

    /// <summary>
    /// 「检查更新」页底部展示的变更记录，按版本从新到旧。
    ///
    /// 记录规则：
    /// 1. 每个大版本（次要版本 X.Y 迭代）一条，新的排在前面；
    /// 2. 同一大版本下的补丁版本（3.3.1 / 3.3.2 / 3.8.1 等）并入该大版本条目，不单列；
    /// 3. 发新版时必须在这里加一条，Latest 自然就是 Entries[0]，并与 AppInfo.Version 对齐；
    /// 4. Summary 必须一句话覆盖该条全部 Bullets 的关键点（列表行直接显示它），
    ///    Bullets 保留完整要点（点开该行看全文）；内容从 CHANGELOG.md / README.md 摘取，
    ///    只写用户看得出来的变化与真实验证结果。
    /// </summary>
    public static class UpdateChangelog
    {
        private static readonly List<ChangelogEntry> entries = Build();

        /// <summary>全部变更记录（从新到旧）。</summary>
        public static List<ChangelogEntry> Entries { get { return entries; } }

        /// <summary>最新一条，正常就是当前 AppInfo.Version。</summary>
        public static ChangelogEntry Latest
        {
            get { return entries.Count > 0 ? entries[0] : null; }
        }

        private static List<ChangelogEntry> Build()
        {
            var list = new List<ChangelogEntry>();

            list.Add(new ChangelogEntry("4.0.0", "2026-11-16", "深模块重构与更新记录",
                "提醒决策收进 ReminderEngine、同步生命周期与日历缓存收进 SyncEngine，配置保存结果可见且失败保留旧配置、设置按实际变化应用，深模块归属整理完成，检查更新页新增可展开的「更新记录」，测试 566/355 与探针 72/72 全绿、兼容约定不变。",
                new string[] {
                "提醒决策集中到 ReminderEngine，同步生命周期与日历缓存集中到 SyncEngine",
                "配置保存结果在界面上可见；保存失败保留旧配置，按实际变化应用设置并隔离副作用",
                "深模块归属整理与行为测试收敛，检查更新页新增「更新记录」",
                "测试 556/350 → 566/355 全绿、UI 探针 72/72 全过；配置目录 / exe 名 / 产品名等兼容约定未变"
            }));

            list.Add(new ChangelogEntry("3.8", "2026-11-16", "小纸条历史 16 条 / 一屏 8 条，未读角标快闪",
                "小纸条每个对话保留 16 条、历史页一屏 8 条并可滚轮看更早，修好历史页最后一行淡入不完整和群聊最后一行被输入框裁掉，未读时卡片左上角每 5 秒快闪、读完自动停。",
                new string[] {
                "小纸条每个对话保留 16 条，历史页一屏 8 条、滚轮看更早",
                "修复历史页最后一行淡入不完整、群聊最后一行被输入框裁掉",
                "未读时大小卡片左上角每 5 秒快闪两下，读完自动停"
            }));

            list.Add(new ChangelogEntry("3.7", "2026-11-16", "六套整体界面主题",
                "新增默认、无垠、云母、宣纸、仪表、霓虹六套整体界面主题并整套换肤，设置窗口改为左栏导航加内容卡片、换肤覆盖卡片 / 设置页 / 小纸条 / 右键胶囊，默认皮肤与 v3.6 逐像素一致且 39 套配色主题仍可切换。",
                new string[] {
                "默认 + 无垠 / 云母 / 宣纸 / 仪表 / 霓虹六套界面风格，整套界面一起换",
                "设置窗口改为左栏导航 + 内容卡片，换肤覆盖卡片 / 设置页 / 小纸条 / 右键胶囊",
                "默认皮肤与 v3.6 逐像素一致，39 套配色主题仍可切换"
            }));

            list.Add(new ChangelogEntry("3.6", "2026-11-13", "磨砂玻璃 / 昵称 / 预览通知 / 检查更新",
                "加入磨砂玻璃背景、卡片页双击改昵称、小纸条联系人两排，预览通知可把六类提醒在卡片上真播一遍且不改日程、不弹托盘，并上线检查更新、全量更新与双资产自动发版。",
                new string[] {
                "磨砂玻璃背景、卡片页双击改昵称、小纸条联系人两排",
                "预览通知：六类提醒可在卡片上真播一遍，不改日程、不弹托盘",
                "检查更新 + 全量更新 + 自动发版（GitHub Release 双资产）"
            }));

            list.Add(new ChangelogEntry("3.5", "2026-11-13", "工作日显示修复 + 个人昵称",
                "修复工作日下标错位导致的「周五显示休息」并自动迁移一次旧配置，个人昵称设置一次后会在对方添加对话和群聊里直接显示。",
                new string[] {
                "修复工作日下标错位导致的「周五显示休息」，旧配置自动迁移一次",
                "个人昵称：设置一次，对方添加对话和群聊里直接显示"
            }));

            list.Add(new ChangelogEntry("3.4", "2026-11-13", "小纸条对话码 + 边框加粗 + 滚动删除",
                "小纸条支持 5 位对话码让不同人输入同一个码进入同一对话，卡片边框加粗到 1.8 DIP、对话多了可滚动且右键可删除，大卡片五个图标统一大小与高度。",
                new string[] {
                "小纸条支持 5 位对话码：不同人输入同一个码就进同一个对话",
                "卡片边框加粗到 1.8 DIP；对话多了可滚动、右键可删除",
                "大卡片五个图标统一大小与高度"
            }));

            list.Add(new ChangelogEntry("3.3", "2026-11-13", "小纸条上线（抽屉 / 双栏）",
                "小纸条上线并提供抽屉式、极简双栏两套布局与公共 MQTT 中继，面板尺寸和字号改按 DIP 计算、聊天正文不再被缩小，修掉三处交互 bug 并更名为「上班工具箱」。",
                new string[] {
                "小纸条上线：抽屉式 / 极简双栏两套布局，公共 MQTT 中继",
                "面板尺寸与字号按 DIP 走，聊天正文不再被缩小",
                "修小纸条三处交互 bug；项目更名「上班工具箱」"
            }));

            list.Add(new ChangelogEntry("3.2", "2026-11-12", "取消工资双击固定 + 胶囊细边",
                "取消工资卡片双击固定、¥ 只保留单击展开与收起，右键胶囊加上主题色细边并去掉投影，气泡改为淡出缩回动画。",
                new string[] {
                "取消工资卡片双击固定，¥ 只保留单击展开 / 收起",
                "右键胶囊加主题色细边、去掉投影，气泡带淡出缩回动画"
            }));

            return list;
        }
    }
}
