using System;
using System.Globalization;

namespace FatFishPet
{
    // 主动搭话：在没有对话窗口参与的情况下，按时间、久坐和互动情况让她自己说一两句。
    // 全部使用本地台词（零成本、断网可用、立刻显示），不发起任何网络请求。
    // Tick 只依赖传入的单调时间 now 和本地时间 localNow，便于离线检查。
    internal sealed class PetTalk
    {
        public const double MinimumIntervalMinutes = 5;
        public const double MaximumIntervalMinutes = 90;
        public const double DefaultIntervalMinutes = 30;
        // 两条主动搭话之间的最小间隔，避免连续刷屏。
        public const double MinimumGapSeconds = 180;
        // 多久没有互动就提醒休息，以及两次提醒之间至少间隔多久。
        public const double IdleReminderSeconds = 45 * 60;
        public const double IdleReminderRepeatSeconds = 60 * 60;
        // 互动回应（摸头、点击、拎起、落地）之间的最小间隔。
        public const double InteractionGapSeconds = 12;
        public const int DailyRandomLimit = 16;

        public bool Enabled = true;
        public double IntervalMinutes = DefaultIntervalMinutes;

        private readonly Random random;
        private double startedAt = double.NaN;
        private double lastSpoke = double.NaN;
        private double lastInteraction = double.NaN;
        private double lastIdleReminder = double.NaN;
        private double nextRandom = double.NaN;
        private bool greetedStartup;
        private int greetedPart = -1;
        private int lastDay = -1;
        private int randomToday;
        public int SpokenToday { get; private set; }

        public PetTalk() : this(new Random()) { }
        internal PetTalk(Random source) { random = source; }

        public static string[] GreetingLines(int part)
        {
            if (part == 1) return new[] { "早上好～今天也一起加油吧。", "早呀，记得吃早饭哦。", "新的一天，我在这儿陪你。" };
            if (part == 2) return new[] { "下午好，要不要起来动一动？", "下午的时间慢慢来就好啦。", "我刚刚打了个小盹～" };
            if (part == 3) return new[] { "晚上好，今天辛苦了。", "天黑啦，我在这儿陪着你。", "忙完记得好好休息呀。" };
            return new[] { "这么晚还没睡呀，我陪你一会儿。", "夜深了，别太拼啦。", "再忙也要记得睡觉哦。" };
        }

        public static string[] IdleLines()
        {
            return new[] { "坐久了，起来伸个懒腰吧～", "喝口水吧，我等你回来。", "眼睛也该歇一会儿啦。", "要不要看看窗外？就十秒钟。", "肩膀是不是僵啦？转一转。" };
        }

        public static string[] RandomLines()
        {
            return new[] {
                "在忙什么呀？", "我刚刚打了个小盹……", "今天有什么开心的事吗？", "要是累了就靠一会儿，我不吵你。",
                "你有好久没摸我的头啦。", "桌面角落的风景我盯了半天。", "记得把要紧的事记下来哦。",
                "今天也要好好吃饭。", "要不要休息五分钟？", "我的头发好像被自己弄乱了……",
                "有不懂的事可以问我呀，虽然我不一定懂。", "你不在的时候，我就数窗外的云。"
            };
        }

        public static string[] PetLines() { return new[] { "嘿嘿，好舒服～", "再摸摸也可以哦。", "呜……头发要乱啦。" }; }
        public static string[] ClickLines() { return new[] { "呀！", "怎么啦？", "在的在的～" }; }
        public static string[] LiftLines() { return new[] { "哇——飞起来啦！", "轻一点、轻一点～" }; }
        public static string[] LandLines() { return new[] { "稳稳落地～", "呼，回来了。" }; }

        // 0 深夜、1 早上、2 下午、3 晚上。
        public static int PartOfDay(DateTime localNow)
        {
            int hour = localNow.Hour;
            if (hour >= 5 && hour < 11) return 1;
            if (hour >= 11 && hour < 17) return 2;
            if (hour >= 17 && hour < 23) return 3;
            return 0;
        }

        public void NotifyInteraction(double now) { lastInteraction = now; }

        // 返回这一拍要说的话，没有就是 null。
        public string Tick(double now, DateTime localNow, bool chatBusy)
        {
            if (!Enabled) return null;
            if (double.IsNaN(startedAt)) { startedAt = now; lastInteraction = now; }
            if (chatBusy) return null;
            if (localNow.DayOfYear != lastDay) { lastDay = localNow.DayOfYear; randomToday = 0; }

            // 启动后短暂等待，再说第一句问候。
            if (!greetedStartup && now - startedAt >= 4)
            {
                greetedStartup = true; greetedPart = PartOfDay(localNow);
                return Speak(now, Pick(GreetingLines(greetedPart)));
            }
            // 跨时段时补一句问候，但至少隔两小时。
            int part = PartOfDay(localNow);
            if (greetedPart < 0) greetedPart = part;
            else if (part != greetedPart && now - lastSpoke >= 2 * 3600)
            {
                greetedPart = part;
                return Speak(now, Pick(GreetingLines(part)));
            }

            if (now - lastSpoke < MinimumGapSeconds) return null;

            // 久坐提醒：一段时间没有互动。
            if (now - lastInteraction >= IdleReminderSeconds &&
                (double.IsNaN(lastIdleReminder) || now - lastIdleReminder >= IdleReminderRepeatSeconds))
            {
                lastIdleReminder = now;
                return Speak(now, Pick(IdleLines()));
            }

            // 随机闲聊。
            if (double.IsNaN(nextRandom)) nextRandom = now + RandomGap();
            if (now >= nextRandom)
            {
                nextRandom = now + RandomGap();
                if (randomToday >= DailyRandomLimit) return null;
                randomToday++;
                return Speak(now, Pick(RandomLines()));
            }
            return null;
        }

        // 用户和她互动时的即时回应：petted、clicked、lifted、landed。
        public string React(string kind, double now)
        {
            if (!Enabled) return null;
            if (!double.IsNaN(lastSpoke) && now - lastSpoke < InteractionGapSeconds) return null;
            if (kind == "clicked" && random.Next(100) < 45) return null;
            string[] bank = kind == "lifted" ? LiftLines()
                : kind == "landed" ? LandLines()
                : kind == "petted" ? PetLines()
                : ClickLines();
            return Speak(now, Pick(bank));
        }

        private string Speak(double now, string line) { lastSpoke = now; SpokenToday++; return line; }
        private double RandomGap() { return IntervalMinutes * 60 * (0.6 + random.NextDouble() * 0.8); }
        private string Pick(string[] options) { return options[random.Next(options.Length)]; }

        public bool Read(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            if (key == "talkEnabled") { Enabled = value != 0; return true; }
            if (key == "talkInterval") { IntervalMinutes = Math.Max(MinimumIntervalMinutes, Math.Min(MaximumIntervalMinutes, value)); return true; }
            return false;
        }

        public string[] ToLines()
        {
            return new[] { "talkEnabled=" + (Enabled ? "1" : "0"),
                           "talkInterval=" + IntervalMinutes.ToString("0.##", CultureInfo.InvariantCulture) };
        }
    }
}
