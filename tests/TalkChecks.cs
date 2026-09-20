// Headless checks for the proactive talking rules: greetings, idle reminders,
// random chat spacing and daily cap, interaction replies, cooldowns, settings.
using System;
using System.Collections.Generic;
using System.Globalization;
using FatFishPet;

internal static class TalkChecks
{
    private static int failures;

    private static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name);
        if (!ok) failures++;
    }

    private static bool InBank(string[] bank, string line)
    {
        foreach (string item in bank) if (item == line) return true;
        return false;
    }

    private static int Main()
    {
        DateTime morning = new DateTime(2026, 9, 20, 10, 0, 0);

        // 关闭时不说话
        PetTalk off = new PetTalk(new Random(1));
        off.Enabled = false;
        Check(off.Tick(0, morning, false) == null, "disabled: silent at startup");
        Check(off.Tick(4000, morning.AddHours(6), false) == null, "disabled: silent later");
        Check(off.React("petted", 10) == null, "disabled: silent on interaction");

        // 开机问候与时段
        PetTalk talk = new PetTalk(new Random(2));
        Check(talk.Tick(0, morning, false) == null, "no line in the first seconds");
        string first = talk.Tick(4, morning, false);
        Check(!string.IsNullOrEmpty(first), "startup greeting appears");
        Check(InBank(PetTalk.GreetingLines(PetTalk.PartOfDay(morning)), first), "startup greeting matches the time of day");
        Check(talk.Tick(20, morning, false) == null, "greeting is not repeated");
        Check(talk.Tick(200, morning, false) == null, "quiet right after the greeting");

        // 时段划分
        Check(PetTalk.PartOfDay(new DateTime(2026, 9, 20, 3, 0, 0)) == 0, "3 点算深夜");
        Check(PetTalk.PartOfDay(new DateTime(2026, 9, 20, 5, 0, 0)) == 1, "5 点算早上");
        Check(PetTalk.PartOfDay(new DateTime(2026, 9, 20, 11, 0, 0)) == 2, "11 点算下午");
        Check(PetTalk.PartOfDay(new DateTime(2026, 9, 20, 17, 0, 0)) == 3, "17 点算晚上");
        Check(PetTalk.PartOfDay(new DateTime(2026, 9, 20, 23, 0, 0)) == 0, "23 点算深夜");

        // 久坐提醒：45 分钟没有任何互动
        PetTalk idle = new PetTalk(new Random(3));
        idle.Tick(4, morning, false);
        string reminder = null;
        double reminderAt = 0;
        int idleCount = 0;
        for (double t = 60; t <= 60 * 70; t += 60)
        {
            string line = idle.Tick(t, morning.AddSeconds(t), false);
            if (line == null || !InBank(PetTalk.IdleLines(), line)) continue;
            idleCount++;
            if (reminder == null) { reminder = line; reminderAt = t; }
        }
        Check(reminder != null, "idle reminder fires after a long quiet stretch");
        Check(reminderAt >= PetTalk.IdleReminderSeconds + 1 && reminderAt <= PetTalk.IdleReminderSeconds + 400,
            "idle reminder timing (" + reminderAt + "s)");
        Check(idleCount <= 3, "idle reminder repeats at most once an hour (" + idleCount + " in 70 minutes)");

        // 随机闲聊：一直有互动，避免触发久坐提醒
        PetTalk chatty = new PetTalk(new Random(4));
        var spoken = new List<double>();
        var kinds = new List<string>();
        bool allShort = true;
        for (double t = 0; t <= 12 * 3600; t += 30)
        {
            chatty.NotifyInteraction(t);
            string line = chatty.Tick(t, morning.AddSeconds(t), false);
            if (line == null) continue;
            spoken.Add(t);
            kinds.Add(InBank(PetTalk.RandomLines(), line) ? "random" : InBank(PetTalk.GreetingLines(PetTalk.PartOfDay(morning.AddSeconds(t))), line) ? "greeting" : "other");
            if (line.Length > 24) allShort = false;
        }
        int randomCount = 0;
        foreach (string kind in kinds) if (kind == "random") randomCount++;
        Check(randomCount >= 3, "random chat repeats during a long session (" + randomCount + ")");
        Check(randomCount <= PetTalk.DailyRandomLimit, "random chat respects the daily cap (" + randomCount + ")");
        Check(allShort, "all proactive lines stay short");
        bool gapOk = true;
        for (int i = 1; i < spoken.Count; i++) if (spoken[i] - spoken[i - 1] < PetTalk.MinimumGapSeconds) gapOk = false;
        Check(gapOk, "proactive lines keep the minimum gap");
        Check(chatty.Tick(12 * 3600 + 3600, morning.AddSeconds(13 * 3600), true) == null, "stays quiet while the chat window is waiting");

        // 互动回应与冷却
        PetTalk pet = new PetTalk(new Random(5));
        string patted = pet.React("petted", 100);
        Check(patted != null && InBank(PetTalk.PetLines(), patted), "replies when petted");
        Check(pet.React("petted", 105) == null, "interaction replies have a cooldown");
        Check(pet.React("petted", 120) != null, "replies again after the cooldown");
        Check(InBank(PetTalk.LiftLines(), pet.React("lifted", 200)), "replies when lifted");
        Check(InBank(PetTalk.LandLines(), pet.React("landed", 260)), "replies when put down");
        int clickLines = 0;
        for (int i = 0; i < 100; i++) if (pet.React("clicked", 400 + i * 20) != null) clickLines++;
        Check(clickLines > 5 && clickLines < 95, "clicking sometimes answers, sometimes not (" + clickLines + "/100)");

        // 台词库本身
        var banks = new List<string[]>();
        for (int part = 0; part < 4; part++) banks.Add(PetTalk.GreetingLines(part));
        banks.Add(PetTalk.IdleLines()); banks.Add(PetTalk.RandomLines()); banks.Add(PetTalk.PetLines());
        banks.Add(PetTalk.ClickLines()); banks.Add(PetTalk.LiftLines()); banks.Add(PetTalk.LandLines());
        bool bankOk = true;
        foreach (string[] bank in banks)
        {
            if (bank.Length < 2) bankOk = false;
            foreach (string line in bank) if (string.IsNullOrEmpty(line) || line.Length > 24) bankOk = false;
        }
        Check(bankOk, "every line bank has variants and short lines");

        // 设置读写与范围
        PetTalk settings = new PetTalk();
        Check(Math.Abs(settings.IntervalMinutes - PetTalk.DefaultIntervalMinutes) < 0.001, "default talk interval");
        settings.Read("talkInterval", 3);
        Check(Math.Abs(settings.IntervalMinutes - PetTalk.MinimumIntervalMinutes) < 0.001, "interval lower clamp");
        settings.Read("talkInterval", 500);
        Check(Math.Abs(settings.IntervalMinutes - PetTalk.MaximumIntervalMinutes) < 0.001, "interval upper clamp");
        settings.Read("talkEnabled", 0);
        settings.Read("talkInterval", 45);
        PetTalk reloaded = new PetTalk();
        foreach (string line in settings.ToLines())
        {
            string[] pair = line.Split('=');
            reloaded.Read(pair[0], double.Parse(pair[1], CultureInfo.InvariantCulture));
        }
        Check(!reloaded.Enabled && Math.Abs(reloaded.IntervalMinutes - 45) < 0.001, "talk settings round trip");

        Console.WriteLine(failures == 0 ? "PASS: proactive talk checks" : ("FAIL: proactive talk checks (" + failures + ")"));
        return failures == 0 ? 0 : 1;
    }
}
