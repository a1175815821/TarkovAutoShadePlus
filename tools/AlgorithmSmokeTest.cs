using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using TarkovAutoShadePlus;

internal static class AlgorithmSmokeTest
{
    private sealed class Bucket
    {
        public string Name;
        public int Count;
        public double InputP10;
        public double InputP50;
        public double InputP95;
        public double OutputP10;
        public double OutputP50;
        public double OutputP95;
        public double Gamma;
        public double Brightness;
    }

    private static int Main(string[] args)
    {
        string folder = args.Length > 0 ? args[0] :
            AppSettings.GetDefaultScreenshotFolder();
        int step = args.Length > 1 ? Math.Max(1, int.Parse(args[1])) : 12;
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Screenshot folder not found: " + folder);
            return 2;
        }

        string[] files = Directory.GetFiles(folder, "*.png");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var settings = AppSettings.CreateDefault();
        var buckets = new Dictionary<string, Bucket>();
        int usable = 0;
        int skipped = 0;
        int failures = ValidateSettingsMigration();
        failures += ValidateSceneGuards();
        failures += ValidateColorFocus();
        failures += ValidateSwitchesAndArmband();
        failures += ValidateProfilePersistence();
        failures += ValidateStoragePersistence();
        failures += ValidateDiagnostics();
        failures += ValidateScreenshotDecode();
        bool biasChecksRun = false;

        for (int i = 0; i < files.Length; i += step)
        {
            AnalysisResult result;
            try { result = ImageAnalyzer.Analyze(files[i], settings); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Read failed: " + ex.Message);
                failures++;
                continue;
            }

            if (!result.IsUsable)
            {
                skipped++;
                continue;
            }

            usable++;
            FilterRecommendation recommendation = result.Recommendation;
            if (!ValidateLut(recommendation))
            {
                Console.Error.WriteLine("Invalid LUT: " + files[i]);
                failures++;
            }

            if (!biasChecksRun)
            {
                failures += ValidateBiasControls(result);
                biasChecksRun = true;
            }

            string name = BucketName(result.Median);
            Bucket bucket;
            if (!buckets.TryGetValue(name, out bucket))
            {
                bucket = new Bucket { Name = name };
                buckets[name] = bucket;
            }
            bucket.Count++;
            bucket.InputP10 += result.P10;
            bucket.InputP50 += result.Median;
            bucket.InputP95 += result.P95;
            bucket.OutputP10 += Map(recommendation, result.P10);
            bucket.OutputP50 += Map(recommendation, result.Median);
            bucket.OutputP95 += Map(recommendation, result.P95);
            bucket.Gamma += recommendation.EquivalentGamma;
            bucket.Brightness += recommendation.BrightnessBoost;
        }

        if (skipped != 0)
        {
            Console.Error.WriteLine(
                "Playable PNGs must not be rejected as loading or low-information frames.");
            failures++;
        }

        Console.WriteLine("Sampled: " + usable + " usable, " + skipped +
            " skipped, step " + step);
        foreach (string name in new[] {
            "extreme-dark", "dark", "balanced", "daylight", "bright"
        })
        {
            Bucket bucket;
            if (!buckets.TryGetValue(name, out bucket)) continue;
            Print(bucket);
        }

        Bucket extreme;
        if (buckets.TryGetValue("extreme-dark", out extreme) &&
            Average(extreme.OutputP50, extreme.Count) < 0.24)
        {
            Console.Error.WriteLine("Extreme-dark median lift is too weak.");
            failures++;
        }

        Bucket bright;
        if (buckets.TryGetValue("bright", out bright) &&
            Average(bright.OutputP95, bright.Count) > 0.93)
        {
            Console.Error.WriteLine("Bright-scene highlights are insufficiently protected.");
            failures++;
        }

        Console.WriteLine(failures == 0 ? "PASS" : "FAILURES: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static bool ValidateLut(FilterRecommendation recommendation)
    {
        if (recommendation.Green[0] != 0 ||
            recommendation.Green[255] != ushort.MaxValue)
            return false;
        for (int i = 1; i < 256; i++)
        {
            if (recommendation.Red[i] < recommendation.Red[i - 1] ||
                recommendation.Green[i] < recommendation.Green[i - 1] ||
                recommendation.Blue[i] < recommendation.Blue[i - 1])
                return false;
        }
        return true;
    }

    private static int ValidateSettingsMigration()
    {
        var legacy = new AppSettings {
            AlgorithmVersion = 2,
            ScreenshotFolder = AppSettings.GetDefaultScreenshotFolder(),
            ShadowTarget = 0,
            HighlightProtection = 0,
            MaxStrength = 0,
            Warmth = -20
        };
        legacy.Normalize();
        bool processesOk = legacy.WatchedProcessNames != null &&
            legacy.WatchedProcessNames.Contains(AppSettings.EftProcessName) &&
            legacy.WatchedProcessNames.Contains(AppSettings.ArenaProcessName);
        bool foldersOk = legacy.ScreenshotFolders != null &&
            legacy.ScreenshotFolders.Count > 0;
        // 采样间隔的迁移值是 800ms（v11 定的），不是更早的 1200ms。
        bool realtimeOk = !legacy.RealtimeEnabled &&
            legacy.RealtimeIntervalMs == 800 &&
            legacy.RealtimeSensitivity == 1;
        // v13 的迁移只为新增的色彩聚焦填默认值，且强度必须是 0，
        // 否则老用户升级后画面会自己变色。
        // v14 补了色彩聚焦的总开关与臂环增强。从 v2 一路升上来的配置
        // 强度是 0，所以总开关应当落在「关」，画面不受任何影响。
        bool focusOk = legacy.FocusHue == AppSettings.DefaultFocusHue &&
            legacy.FocusStrength == 0 &&
            legacy.FocusRange == 0 &&
            legacy.TrimRed == 0 &&
            legacy.TrimGreen == 0 &&
            legacy.TrimBlue == 0 &&
            !legacy.FocusEnabled &&
            !legacy.ArmbandEnabled &&
            legacy.ArmbandLift == 0 &&
            legacy.ArmbandDesaturate == 0 &&
            legacy.EftProfile != null && legacy.ArenaProfile != null &&
            legacy.EftProfile.FocusHue == AppSettings.DefaultFocusHue &&
            legacy.ArenaProfile.FocusHue == AppSettings.DefaultFocusHue &&
            legacy.EftProfile.FocusStrength == 0 &&
            legacy.ArenaProfile.FocusStrength == 0 &&
            legacy.EftProfile.FocusHue == legacy.ArenaProfile.FocusHue;

        if (legacy.AlgorithmVersion == 14 &&
            legacy.ShadowTarget == 70 &&
            legacy.HighlightProtection == 76 &&
            legacy.MaxStrength == 82 &&
            // v3 的「全零恢复」分支会先把 Warmth 写成 +3，
            // v5 再把那个恢复值归零，所以从 2 升上来的配置最终是 0。
            legacy.Warmth == 0 &&
            legacy.ColorCorrection == 72 &&
            legacy.IndoorComfort == 72 &&
            legacy.SceneGuard == 88 &&
            legacy.BlackPoint == 56 &&
            legacy.SmoothTransition &&
            processesOk && foldersOk && realtimeOk && focusOk)
            return 0;

        Console.Error.WriteLine("Legacy zero-strength settings were not recovered.");
        return 1;
    }

    /// <summary>
    /// 色彩聚焦：Gamma Ramp 只能按通道改写，所以这里验证的是
    /// 「目标色相的通道被推高、互补通道被压回」，以及强度 0 完全惰性。
    /// </summary>
    private static int ValidateColorFocus()
    {
        var neutralScene = new AnalysisResult {
            P10 = 0.15,
            Median = 0.34,
            P75 = 0.44,
            P95 = 0.72,
            P99 = 0.80,
            DynamicRange = 0.58,
            EdgeEnergy = 0.04,
            MeanRed = 0.32,
            MeanGreen = 0.33,
            MeanBlue = 0.32
        };

        FilterRecommendation baseline = ToneCurve.Recommend(
            neutralScene, AppSettings.CreateDefault());

        // 强度 0 时换色相必须完全惰性，否则「没开聚焦」也会改画面。
        var idleHue = AppSettings.CreateDefault();
        idleHue.FocusHue = 0;
        idleHue.FocusStrength = 0;
        FilterRecommendation idle = ToneCurve.Recommend(neutralScene, idleHue);

        var warm = AppSettings.CreateDefault();
        warm.FocusHue = 25;
        warm.FocusStrength = 100;
        FilterRecommendation warmRecommendation = ToneCurve.Recommend(neutralScene, warm);

        var cool = AppSettings.CreateDefault();
        cool.FocusHue = 195;
        cool.FocusStrength = 100;
        FilterRecommendation coolRecommendation = ToneCurve.Recommend(neutralScene, cool);

        // 手动通道微调只碰自己那一路。
        var redOnly = AppSettings.CreateDefault();
        redOnly.TrimRed = 20;
        FilterRecommendation redOnlyRecommendation = ToneCurve.Recommend(neutralScene, redOnly);

        // 作用亮度：同一个色相，负值该把峰值压到暗部，正值该推到亮部。
        var darkRange = AppSettings.CreateDefault();
        darkRange.FocusHue = 25;
        darkRange.FocusStrength = 100;
        darkRange.FocusRange = -AppSettings.FocusRangeLimit;
        FilterRecommendation darkRecommendation = ToneCurve.Recommend(neutralScene, darkRange);

        var brightRange = AppSettings.CreateDefault();
        brightRange.FocusHue = 25;
        brightRange.FocusStrength = 100;
        brightRange.FocusRange = AppSettings.FocusRangeLimit;
        FilterRecommendation brightRecommendation = ToneCurve.Recommend(neutralScene, brightRange);

        int failures = 0;
        int mid = 128;

        if (!RampEquals(baseline.Red, idle.Red) ||
            !RampEquals(baseline.Green, idle.Green) ||
            !RampEquals(baseline.Blue, idle.Blue))
        {
            Console.Error.WriteLine(
                "Color focus is not inert at strength 0.");
            failures++;
        }

        // 满强度下中间调至少要动 12/255（≈ 3000/65535），否则这个滑块
        // 在实机上等于看不见；256 级量表里 12 档是肉眼可辨的下限。
        const int VisibleDelta = 3000;

        int warmRed = RampDelta(warmRecommendation.Red, baseline.Red, mid);
        int warmBlue = RampDelta(warmRecommendation.Blue, baseline.Blue, mid);
        if (warmRed <= VisibleDelta || warmBlue >= -VisibleDelta)
        {
            Console.Error.WriteLine(
                "Warm focus does not raise red / lower blue (red " +
                warmRed + ", blue " + warmBlue + ").");
            failures++;
        }

        int coolRed = RampDelta(coolRecommendation.Red, baseline.Red, mid);
        int coolBlue = RampDelta(coolRecommendation.Blue, baseline.Blue, mid);
        if (coolBlue <= VisibleDelta || coolRed >= -VisibleDelta)
        {
            Console.Error.WriteLine(
                "Cool focus does not raise blue / lower red (red " +
                coolRed + ", blue " + coolBlue + ").");
            failures++;
        }

        if (!RampEquals(baseline.Green, redOnlyRecommendation.Green) ||
            !RampEquals(baseline.Blue, redOnlyRecommendation.Blue) ||
            RampDelta(redOnlyRecommendation.Red, baseline.Red, mid) <= VisibleDelta)
        {
            Console.Error.WriteLine(
                "Manual red trim is not confined to the red channel.");
            failures++;
        }

        int darkPeak = PeakDeltaIndex(darkRecommendation.Red, baseline.Red);
        int brightPeak = PeakDeltaIndex(brightRecommendation.Red, baseline.Red);
        if (darkPeak <= 0 || darkPeak >= 255 ||
            brightPeak <= 0 || brightPeak >= 255 ||
            darkPeak >= brightPeak)
        {
            Console.Error.WriteLine(
                "Focus range does not move the effect (dark peak " +
                darkPeak + ", bright peak " + brightPeak + ").");
            failures++;
        }

        if (!ValidateLut(warmRecommendation) ||
            !ValidateLut(coolRecommendation) ||
            !ValidateLut(darkRecommendation) ||
            !ValidateLut(brightRecommendation) ||
            !ValidateLut(redOnlyRecommendation))
        {
            Console.Error.WriteLine(
                "Color focus broke ramp monotonicity or the black / white endpoints.");
            failures++;
        }

        Console.WriteLine(
            "Color focus: warm R{0:+0;-0} B{1:+0;-0} / cool R{2:+0;-0} B{3:+0;-0} " +
            "/ red-trim R{4:+0;-0} / range peaks {5} vs {6}",
            warmRed, warmBlue, coolRed, coolBlue,
            RampDelta(redOnlyRecommendation.Red, baseline.Red, mid),
            darkPeak, brightPeak);
        return failures;
    }

    /// <summary>
    /// 色彩聚焦总开关 + 臂环增强。重点验证两件事：
    /// 开关关掉时输出必须逐字节回到基线，以及臂环只碰它该碰的通道。
    /// </summary>
    private static int ValidateSwitchesAndArmband()
    {
        var neutralScene = new AnalysisResult {
            P10 = 0.15,
            Median = 0.34,
            P75 = 0.44,
            P95 = 0.72,
            P99 = 0.80,
            DynamicRange = 0.58,
            EdgeEnergy = 0.04,
            MeanRed = 0.32,
            MeanGreen = 0.33,
            MeanBlue = 0.32
        };

        FilterRecommendation baseline = ToneCurve.Recommend(
            neutralScene, AppSettings.CreateDefault());

        int failures = 0;

        // 1) 色彩聚焦总开关：数值全开但 FocusEnabled=false，必须与基线完全相同。
        var focusOff = AppSettings.CreateDefault();
        focusOff.FocusEnabled = false;
        focusOff.FocusHue = 25;
        focusOff.FocusStrength = 100;
        focusOff.FocusRange = -20;
        focusOff.TrimRed = 20;
        focusOff.TrimGreen = -20;
        focusOff.TrimBlue = 20;
        FilterRecommendation focusOffRecommendation = ToneCurve.Recommend(
            neutralScene, focusOff);
        if (!RampEquals(baseline.Red, focusOffRecommendation.Red) ||
            !RampEquals(baseline.Green, focusOffRecommendation.Green) ||
            !RampEquals(baseline.Blue, focusOffRecommendation.Blue))
        {
            Console.Error.WriteLine(
                "Disabling color focus does not restore the unfiltered curve.");
            failures++;
        }

        // 2) 臂环增强默认是关的：Enabled=false 时两个滑块的值不该有任何作用。
        var armbandIdle = AppSettings.CreateDefault();
        armbandIdle.ArmbandEnabled = false;
        armbandIdle.ArmbandLift = 100;
        armbandIdle.ArmbandDesaturate = 100;
        FilterRecommendation armbandIdleRecommendation = ToneCurve.Recommend(
            neutralScene, armbandIdle);
        if (!RampEquals(baseline.Red, armbandIdleRecommendation.Red) ||
            !RampEquals(baseline.Green, armbandIdleRecommendation.Green) ||
            !RampEquals(baseline.Blue, armbandIdleRecommendation.Blue))
        {
            Console.Error.WriteLine(
                "Armband controls leak into the curve while disabled.");
            failures++;
        }

        // 3) 双色强化：只抬红蓝，绿通道必须逐字节不变。
        var lift = AppSettings.CreateDefault();
        lift.ArmbandEnabled = true;
        lift.ArmbandLift = 100;
        FilterRecommendation liftRecommendation = ToneCurve.Recommend(neutralScene, lift);
        int liftIndex = 200;
        int liftRed = RampDelta(liftRecommendation.Red, baseline.Red, liftIndex);
        int liftBlue = RampDelta(liftRecommendation.Blue, baseline.Blue, liftIndex);
        if (!RampEquals(baseline.Green, liftRecommendation.Green))
        {
            Console.Error.WriteLine("Armband lift touched the green channel.");
            failures++;
        }
        if (liftRed <= 0 || liftBlue <= 0)
        {
            Console.Error.WriteLine(
                "Armband lift did not raise red / blue (red " + liftRed +
                ", blue " + liftBlue + ").");
            failures++;
        }

        // 4) 环境去色：只压绿通道的中间调，红蓝必须逐字节不变。
        var desaturate = AppSettings.CreateDefault();
        desaturate.ArmbandEnabled = true;
        desaturate.ArmbandDesaturate = 100;
        FilterRecommendation desaturateRecommendation = ToneCurve.Recommend(
            neutralScene, desaturate);
        int greenDrop = -RampDelta(desaturateRecommendation.Green, baseline.Green, 128);
        if (!RampEquals(baseline.Red, desaturateRecommendation.Red) ||
            !RampEquals(baseline.Blue, desaturateRecommendation.Blue))
        {
            Console.Error.WriteLine(
                "Environment desaturation leaked into red / blue.");
            failures++;
        }
        if (greenDrop < 3000)
        {
            Console.Error.WriteLine(
                "Environment desaturation is too weak at full strength (" +
                greenDrop + ").");
            failures++;
        }

        // 5) 中间调锚点：双色强化的权重是 v²，必须偏向饱和区，否则它就退化成
        //    一层全局品红染色（权重恒定则比值 = 1.0）。
        //    实测比值约 0.58，而不是理论上的 0.5 —— 因为基础滤镜会把暗场中间调
        //    提亮，输入第 128 档在曲线上已经落到高值区，v² 的区分度被压缩了。
        //    阈值取 0.75，能拦住「权重改成常数或线性」这类退化。
        int midLiftRed = RampDelta(liftRecommendation.Red, baseline.Red, 128);
        if (midLiftRed * 4 > liftRed * 3)
        {
            Console.Error.WriteLine(
                "Armband lift is not biased toward the saturated range (mid " +
                midLiftRed + " vs high " + liftRed + ").");
            failures++;
        }

        if (!ValidateLut(liftRecommendation) ||
            !ValidateLut(desaturateRecommendation))
        {
            Console.Error.WriteLine(
                "Armband controls broke ramp monotonicity or the black / white endpoints.");
            failures++;
        }

        Console.WriteLine(
            "Switches/armband: focus-off identical={0} / lift R{1:+0;-0} B{2:+0;-0} " +
            "mid{3:+0;-0} / desaturate G-{4}",
            RampEquals(baseline.Green, focusOffRecommendation.Green),
            liftRed, liftBlue, midLiftRed, greenDrop);
        return failures;
    }

    /// <summary>
    /// 双端档案必须各自独立地活过「落盘 → 重启」。重启后当前生效的是哪一档，
    /// 只能靠 LockedProfileKey 认回来（MainWindow 构造时按它恢复 activeProfileKey，
    /// 再把该档铺回平铺字段）。这里守住两件事：LockedProfileKey 始终等于最后一次
    /// 切换的档位；并且恢复后第一次落盘不会把另一档写成当前档的数值。
    /// </summary>
    private static int ValidateProfilePersistence()
    {
        // 原版档案调一个独有组合。
        var first = AppSettings.CreateDefault();
        first.ShadowTarget = 33;
        first.ExposureBias = 7;
        first.LockedProfileKey = AppSettings.ProfileKeyEft;
        first.GetProfile(AppSettings.ProfileKeyEft).CaptureFrom(first);

        // 切到竞技场并改成另一组数值 —— 这一步不该碰到原版档案。
        var second = RoundTripSettings(first);
        second.LockedProfileKey = AppSettings.ProfileKeyArena;
        second.GetProfile(AppSettings.ProfileKeyArena).ApplyTo(second);
        second.ShadowTarget = 91;
        second.GetProfile(AppSettings.ProfileKeyArena).CaptureFrom(second);

        // 模拟重启后用户随手改一格，这是原先覆盖原版档案的那一步。
        var restored = RoundTripSettings(second);
        int restoredKey = MathUtil.Clamp(restored.LockedProfileKey,
            AppSettings.ProfileKeyEft, AppSettings.ProfileKeyArena);
        restored.GetProfile(restoredKey).ApplyTo(restored);
        restored.ShadowTarget = 90;
        restored.GetProfile(restoredKey).CaptureFrom(restored);

        int failures = 0;
        TuningProfile eft = restored.GetProfile(AppSettings.ProfileKeyEft);
        TuningProfile arena = restored.GetProfile(AppSettings.ProfileKeyArena);
        if (restoredKey != AppSettings.ProfileKeyArena)
        {
            Console.Error.WriteLine("重启后没有认回竞技场档案，当前档位 " + restoredKey +
                "。LockedProfileKey 必须跟着每次切换更新。");
            failures++;
        }
        if (arena.ShadowTarget != 90)
        {
            Console.Error.WriteLine("竞技场档案在重启落盘后丢了数值，读到 " +
                arena.ShadowTarget + "。");
            failures++;
        }
        if (eft.ShadowTarget != 33 || eft.ExposureBias != 7)
        {
            Console.Error.WriteLine(
                "原版档案被重启后的落盘覆盖成了竞技场的数值（shadow " + eft.ShadowTarget +
                ", exposure " + eft.ExposureBias + "）。重启时必须先把 activeProfileKey " +
                "恢复到 LockedProfileKey，再落盘。");
            failures++;
        }
        Console.WriteLine("Profiles: eft shadow={0} exposure={1} / arena shadow={2} / key={3}",
            eft.ShadowTarget, eft.ExposureBias, arena.ShadowTarget, restoredKey);
        return failures;
    }

    /// <summary>
    /// 按 SettingsStore 的同一套序列化走一圈，模拟进程重启后的读档结果。
    /// </summary>
    private static AppSettings RoundTripSettings(AppSettings settings)
    {
        var serializer = new DataContractJsonSerializer(typeof(AppSettings));
        byte[] json;
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, settings);
            json = stream.ToArray();
        }
        using (var stream = new MemoryStream(json))
        {
            var reloaded = serializer.ReadObject(stream) as AppSettings;
            if (reloaded == null) throw new InvalidDataException("设置无法反序列化。");
            reloaded.Normalize();
            return reloaded;
        }
    }

    /// <summary>
    /// 逐字段体检配置到底存不存得住：给每个可持久化字段填一个「在它自己合法区间内、
    /// 且与其它字段互不相同」的哨兵值，走 SettingsStore 的同一套序列化一圈，再比回来。
    /// 漏 [DataMember]、被 Normalize 的迁移覆写、平铺字段与 TuningProfile 之间漏抄，
    /// 都会在这里现形。加新字段时不用改这个测试 —— 它是反射驱动的。
    /// </summary>
    private static int ValidateStoragePersistence()
    {
        int failures = 0;
        var filled = AppSettings.CreateDefault();
        int fieldCount = FillStorageProbe(filled);

        var reloaded = RoundTripSettings(filled);
        foreach (PropertyInfo property in typeof(AppSettings).GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetGetMethod() == null || property.GetSetMethod() == null) continue;
            object before = property.GetValue(filled, null);
            object after = property.GetValue(reloaded, null);
            if (SameStoredValue(before, after)) continue;
            Console.Error.WriteLine("[存储] AppSettings." + property.Name + " 存前=" +
                DescribeStored(before) + " 取回=" + DescribeStored(after));
            failures++;
        }

        // 平铺字段 <-> TuningProfile 双向镜像：CaptureFrom 或 ApplyTo 漏抄一个字段，
        // 那一档的参数就会在切换 / 落盘时被悄悄丢掉。
        var profile = new TuningProfile();
        int mirrored = 0;
        profile.CaptureFrom(filled);
        foreach (PropertyInfo source in typeof(AppSettings).GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            PropertyInfo target = typeof(TuningProfile).GetProperty(source.Name,
                BindingFlags.Public | BindingFlags.Instance);
            if (target == null || target.GetGetMethod() == null ||
                target.GetSetMethod() == null || source.GetGetMethod() == null) continue;
            mirrored++;
            object wanted = source.GetValue(filled, null);
            object stored = target.GetValue(profile, null);
            if (!Equals(wanted, stored))
            {
                Console.Error.WriteLine("[镜像] CaptureFrom 漏抄 TuningProfile." + target.Name +
                    "（应为 " + DescribeStored(wanted) + "，实为 " + DescribeStored(stored) + "）");
                failures++;
                continue;
            }
            var fresh = AppSettings.CreateDefault();
            profile.ApplyTo(fresh);
            object restored = source.GetValue(fresh, null);
            if (!Equals(wanted, restored))
            {
                Console.Error.WriteLine("[镜像] ApplyTo 漏抄 AppSettings." + source.Name +
                    "（应为 " + DescribeStored(wanted) + "，实为 " + DescribeStored(restored) + "）");
                failures++;
            }
        }

        // Normalize 会被 CreateSettingsSnapshot 在每次实时采样时重复调用，必须幂等。
        string once = SerializeSettings(filled);
        filled.Normalize();
        filled.Normalize();
        if (once != SerializeSettings(filled))
        {
            Console.Error.WriteLine("[镜像] Normalize 不幂等，重复调用会持续改写配置。");
            failures++;
        }

        Console.WriteLine(failures == 0 ?
            "Storage: 字段 " + fieldCount + " 个（镜像可比 " + mirrored +
            " 个）全部可持久化，Normalize 幂等" :
            "Storage: 字段 " + fieldCount + " 个中发现 " + failures + " 处问题");
        return failures;
    }

    private static string SerializeSettings(AppSettings settings)
    {
        var serializer = new DataContractJsonSerializer(typeof(AppSettings));
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, settings);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static int FillStorageProbe(AppSettings settings)
    {
        int count = 0;
        // 列表与它的 legacy 单值镜像必须一致，否则 Normalize 会把首项补回列表 ——
        // 那是设计行为，不是丢配置。
        string folder = @"C:\Audit\Escape from Tarkov\Screenshots";
        string process = "EscapeFromTarkov.exe";
        string device = @"\\.\DISPLAY1";
        foreach (PropertyInfo property in typeof(AppSettings).GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            MethodInfo setter = property.GetSetMethod();
            if (setter == null) continue;
            Type type = property.PropertyType;
            string name = property.Name;
            if (type == typeof(int))
            {
                if (name == "AlgorithmVersion") continue;
                setter.Invoke(settings, new object[] { StorageProbeValue(name, count) });
                count++;
            }
            else if (type == typeof(int?))
            {
                // 窗口位置必须验负数：副屏常常在主屏左边。
                int value = name.Contains("Width") ? 1600 :
                    name.Contains("Height") ? 900 : -1400;
                setter.Invoke(settings, new object[] { value });
                count++;
            }
            else if (type == typeof(bool))
            {
                // CustomPresetInitialized 为 false 时，Normalize 里那条不受版本号
                // 限制的迁移会把整套 Custom* 覆写掉。
                setter.Invoke(settings, new object[] { true });
                count++;
            }
            else if (type == typeof(string))
            {
                setter.Invoke(settings, new object[] {
                    name == "ScreenshotFolder" ? folder :
                    name == "WatchedProcessName" ? process :
                    name == "DisplayDevice" ? device : "audit-" + name });
                count++;
            }
            else if (type == typeof(List<string>))
            {
                setter.Invoke(settings, new object[] { new List<string> {
                    name == "ScreenshotFolders" ? folder :
                    name == "WatchedProcessNames" ? process : device } });
                count++;
            }
            else if (type == typeof(TuningProfile))
            {
                var profile = new TuningProfile();
                FillProfileStorageProbe(profile, ref count);
                setter.Invoke(settings, new object[] { profile });
            }
        }
        return count;
    }

    private static void FillProfileStorageProbe(TuningProfile profile, ref int count)
    {
        foreach (PropertyInfo property in typeof(TuningProfile).GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            MethodInfo setter = property.GetSetMethod();
            if (setter == null) continue;
            Type type = property.PropertyType;
            string name = property.Name;
            if (type == typeof(int))
            {
                setter.Invoke(profile, new object[] { StorageProbeValue(
                    name.StartsWith("Custom") ? name.Substring(6) : name, count) });
                count++;
            }
            else if (type == typeof(bool))
            {
                setter.Invoke(profile, new object[] { true });
                count++;
            }
        }
    }

    /// <summary>
    /// 哨兵值必须落在字段自己的合法区间内，否则量到的是 Clamp 和迁移的行为，
    /// 而不是「存不存得住」。
    /// </summary>
    private static int StorageProbeValue(string name, int index)
    {
        switch (name)
        {
            case "PresetIndex": return 3;
            case "HotkeyKeyCode": return 120;
            case "HotkeyModifiers": return 3;
            case "RealtimeIntervalMs": return 1500;
            case "RealtimeSensitivity": return 2;
            case "CloseBehavior": return 1;
            case "LockedProfileKey": return 1;
        }
        if (name.EndsWith("Hue")) return 200 + (index % 40);
        if (IsSignedBiasName(name)) return -3 - (index % 4);
        return 40 + (index % 20);
    }

    private static bool IsSignedBiasName(string name)
    {
        return name.Contains("Exposure") || name.Contains("Contrast") ||
            name.Contains("Warmth") || name.Contains("Saturation") ||
            name.Contains("Trim") || name.Contains("FocusRange");
    }

    // List<T> 与 TuningProfile 是引用类型，Equals 只比引用，必须深比。
    private static bool SameStoredValue(object left, object right)
    {
        var leftList = left as List<string>;
        var rightList = right as List<string>;
        if (leftList != null && rightList != null)
        {
            if (leftList.Count != rightList.Count) return false;
            for (int i = 0; i < leftList.Count; i++)
            {
                if (!string.Equals(leftList[i], rightList[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
        var leftProfile = left as TuningProfile;
        var rightProfile = right as TuningProfile;
        if (leftProfile != null && rightProfile != null)
            return SameProfileState(leftProfile, rightProfile);
        return Equals(left, right);
    }

    private static bool SameProfileState(TuningProfile left, TuningProfile right)
    {
        foreach (PropertyInfo property in typeof(TuningProfile).GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetGetMethod() == null) continue;
            if (!Equals(property.GetValue(left, null), property.GetValue(right, null)))
                return false;
        }
        return true;
    }

    private static string DescribeStored(object value)
    {
        if (value == null) return "null";
        var list = value as List<string>;
        if (list != null) return "List[" + string.Join("|", list.ToArray()) + "]";
        if (value is TuningProfile) return "TuningProfile";
        return value.ToString();
    }

    /// <summary>
    /// 诊断模块：内存环形缓冲、限流、以及导出报告不能把用户名带出去。
    /// </summary>
    private static int ValidateDiagnostics()
    {
        int failures = 0;

        Diagnostics.Info("自检", "环形缓冲第一条");
        Diagnostics.Warn("自检", "环形缓冲第二条");
        List<string> recent = Diagnostics.Snapshot(1);
        if (recent.Count != 1 || !recent[0].Contains("环形缓冲第二条"))
        {
            Console.Error.WriteLine("Snapshot 没有取到最新的一条记录。");
            failures++;
        }
        if (Diagnostics.Snapshot(0).Count > 400)
        {
            Console.Error.WriteLine("内存环形缓冲没有上限，长时间跑会一直涨。");
            failures++;
        }

        int beforeThrottle = Diagnostics.Snapshot(0).Count;
        var interval = TimeSpan.FromHours(1);
        for (int i = 0; i < 20; i++)
            Diagnostics.Throttled("自检", "throttle-probe", "只该留一条", interval);
        int afterThrottle = Diagnostics.Snapshot(0).Count;
        if (afterThrottle - beforeThrottle != 1)
        {
            Console.Error.WriteLine("限流失效：20 次相同键写入产生了 " +
                (afterThrottle - beforeThrottle) + " 条记录。");
            failures++;
        }

        var settings = AppSettings.CreateDefault();
        string report = Diagnostics.BuildReport(settings, new MonitorCapabilities(),
            @"\\.\DISPLAY1", true);
        if (report.Length == 0 || !report.Contains("显示器") || !report.Contains("参数"))
        {
            Console.Error.WriteLine("诊断报告缺内容，贴出去也说明不了问题。");
            failures++;
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) && report.Contains(profile))
        {
            Console.Error.WriteLine("诊断报告里带着用户目录（用户名会一起发出去）。");
            failures++;
        }
        // 截图目录里最容易泄露用户名，确认它被替换过了。
        var withPath = AppSettings.CreateDefault();
        withPath.ScreenshotFolder = profile + @"\Custom Shots";
        withPath.ScreenshotFolders = new List<string> { profile + @"\Custom Shots" };
        string leaked = Diagnostics.BuildReport(withPath, null, "无", false);
        if (leaked.Contains(profile))
        {
            Console.Error.WriteLine("截图目录路径没有被脱敏，报障时会把用户名一起发出去。");
            failures++;
        }
        Console.WriteLine(failures == 0 ?
            "Diagnostics: 环形缓冲 / 限流 / 报告脱敏均正常" :
            "Diagnostics: 发现 " + failures + " 处问题");
        return failures;
    }

    /// <summary>
    /// 读图重试只该服务于「游戏还在写这张图」。长度不再变化却仍然解不出图像的文件
    /// （坏图、压根不是图片）要尽快报错，而不是把分析按在那儿等满 5 秒。
    /// </summary>
    private static int ValidateScreenshotDecode()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "tas-decode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int failures = 0;
        try
        {
            string valid = Path.Combine(root, "valid.png");
            using (var bitmap = new Bitmap(16, 16))
                bitmap.Save(valid, ImageFormat.Png);
            string error;
            long elapsed;
            if (TryLoadBitmap(valid, out error, out elapsed))
            {
                Console.WriteLine("Decode: valid png ok");
            }
            else
            {
                Console.Error.WriteLine("正常 PNG 读不出来：" + error);
                failures++;
            }

            string broken = Path.Combine(root, "broken.png");
            File.WriteAllText(broken, "this is definitely not a png");
            bool brokenLoaded = TryLoadBitmap(broken, out error, out elapsed);
            if (brokenLoaded)
            {
                Console.Error.WriteLine("非图片文件竟然解码成功。");
                failures++;
            }
            else if (elapsed > 2500)
            {
                Console.Error.WriteLine("坏文件等了 " + elapsed +
                    "ms 才报错，还在等满重试窗口。");
                failures++;
            }
            else if (error.Contains("尚未写入完成"))
            {
                Console.Error.WriteLine("坏文件被误报成「还在写入」：" + error);
                failures++;
            }
            else
            {
                Console.WriteLine("Decode: broken file rejected in " + elapsed + "ms");
            }

            string missing = Path.Combine(root, "missing.png");
            if (TryLoadBitmap(missing, out error, out elapsed) || elapsed > 500 ||
                error.Contains("尚未写入完成"))
            {
                Console.Error.WriteLine("不存在的截图没有立刻失败（" + elapsed +
                    "ms）：" + error);
                failures++;
            }
            return failures;
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch { }
        }
    }

    private static bool TryLoadBitmap(string path, out string error, out long elapsedMilliseconds)
    {
        var watch = Stopwatch.StartNew();
        error = "";
        try
        {
            using (Bitmap ignored = ImageAnalyzer.LoadStableBitmap(path)) { }
            elapsedMilliseconds = watch.ElapsedMilliseconds;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message == null ? "" : ex.Message;
            elapsedMilliseconds = watch.ElapsedMilliseconds;
            return false;
        }
    }

    private static bool RampEquals(ushort[] left, ushort[] right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }
        return true;
    }

    private static int RampDelta(ushort[] focused, ushort[] baseline, int index)
    {
        return focused[index] - baseline[index];
    }

    private static int PeakDeltaIndex(ushort[] focused, ushort[] baseline)
    {
        int peak = 0;
        int peakValue = -1;
        for (int i = 1; i < 255; i++)
        {
            int delta = RampDelta(focused, baseline, i);
            if (delta > peakValue)
            {
                peakValue = delta;
                peak = i;
            }
        }
        return peak;
    }

    private static int ValidateBiasControls(AnalysisResult result)
    {
        var darker = AppSettings.CreateDefault();
        darker.ExposureBias = -20;
        var brighter = AppSettings.CreateDefault();
        brighter.ExposureBias = 20;
        var flatter = AppSettings.CreateDefault();
        flatter.ContrastBias = -20;
        var punchier = AppSettings.CreateDefault();
        punchier.ContrastBias = 20;

        FilterRecommendation dark = ToneCurve.Recommend(result, darker);
        FilterRecommendation bright = ToneCurve.Recommend(result, brighter);
        FilterRecommendation flat = ToneCurve.Recommend(result, flatter);
        FilterRecommendation punch = ToneCurve.Recommend(result, punchier);

        int failures = 0;
        if (bright.BrightnessBoost <= dark.BrightnessBoost ||
            bright.EquivalentGamma <= dark.EquivalentGamma)
        {
            Console.Error.WriteLine("Exposure bias does not change the curve.");
            failures++;
        }
        if (punch.ContrastBoost <= flat.ContrastBoost)
        {
            Console.Error.WriteLine("Contrast bias does not change the curve.");
            failures++;
        }
        return failures;
    }

    private static int ValidateSceneGuards()
    {
        var bright = new AnalysisResult {
            P10 = 0.24,
            Median = 0.68,
            P75 = 0.78,
            P95 = 0.98,
            P99 = 0.99,
            DynamicRange = 0.74,
            EdgeEnergy = 0.04,
            MeanRed = 0.36,
            MeanGreen = 0.37,
            MeanBlue = 0.36
        };
        var nightVision = new AnalysisResult {
            P10 = 0.04,
            Median = 0.18,
            P75 = 0.32,
            P95 = 0.76,
            P99 = 0.86,
            DynamicRange = 0.72,
            EdgeEnergy = 0.04,
            MeanRed = 0.10,
            MeanGreen = 0.30,
            MeanBlue = 0.09,
            NightVisionScore = 0.90
        };
        var mutedGreenNight = new AnalysisResult {
            P10 = 0.02,
            Median = 0.12,
            P75 = 0.20,
            P95 = 0.52,
            P99 = 0.68,
            DynamicRange = 0.50,
            EdgeEnergy = 0.02,
            MeanRed = 0.08,
            MeanGreen = 0.18,
            MeanBlue = 0.12,
            NightVisionScore = 0.55
        };
        var redCast = new AnalysisResult {
            P10 = 0.05,
            Median = 0.22,
            P75 = 0.35,
            P95 = 0.70,
            P99 = 0.82,
            DynamicRange = 0.65,
            EdgeEnergy = 0.04,
            MeanRed = 0.38,
            MeanGreen = 0.25,
            MeanBlue = 0.20
        };

        FilterRecommendation brightRecommendation = ToneCurve.Recommend(
            bright, AppSettings.CreateDefault());
        FilterRecommendation nightRecommendation = ToneCurve.Recommend(
            nightVision, AppSettings.CreateDefault());
        FilterRecommendation redRecommendation = ToneCurve.Recommend(
            redCast, AppSettings.CreateDefault());

        int failures = 0;
        if (brightRecommendation.BrightnessBoost > 1.0 ||
            brightRecommendation.EquivalentGamma > 1.12)
        {
            Console.Error.WriteLine(
                "Bright-scene guard still permits too much lift.");
            failures++;
        }
        if (nightRecommendation.BrightnessBoost > 1.0 ||
            nightRecommendation.EquivalentGamma > 1.20)
        {
            Console.Error.WriteLine(
                "Night-vision guard still permits too much lift.");
            failures++;
        }
        FilterRecommendation mutedGreenRecommendation = ToneCurve.Recommend(
            mutedGreenNight, AppSettings.CreateDefault());
        mutedGreenNight.NightVisionScore = 0.0;
        FilterRecommendation unguardedGreenRecommendation = ToneCurve.Recommend(
            mutedGreenNight, AppSettings.CreateDefault());
        if (mutedGreenRecommendation.HighlightCompression <= 0.0 ||
            mutedGreenRecommendation.BrightnessBoost >=
                unguardedGreenRecommendation.BrightnessBoost)
        {
            Console.Error.WriteLine(
                "Muted green night scene is not receiving night protection.");
            failures++;
        }
        if (redRecommendation.RedBalance >= 0.0 ||
            redRecommendation.GreenBalance <= 0.0 ||
            redRecommendation.BlueBalance <= 0.0)
        {
            Console.Error.WriteLine(
                "Color correction does not counter a red cast.");
            failures++;
        }

        var grayScene = new AnalysisResult {
            P10 = 0.06,
            Median = 0.17,
            P75 = 0.28,
            P95 = 0.52,
            P99 = 0.63,
            DynamicRange = 0.12,
            EdgeEnergy = 0.03,
            MeanRed = 0.19,
            MeanGreen = 0.19,
            MeanBlue = 0.19
        };
        var balancedBlackPoint = AppSettings.CreateDefault();
        balancedBlackPoint.BlackPoint = 50;
        var strongerBlackPoint = AppSettings.CreateDefault();
        strongerBlackPoint.BlackPoint = 90;
        FilterRecommendation balancedRecommendation = ToneCurve.Recommend(
            grayScene, balancedBlackPoint);
        FilterRecommendation strongerRecommendation = ToneCurve.Recommend(
            grayScene, strongerBlackPoint);
        if (strongerRecommendation.BlackPointRecovery <=
            balancedRecommendation.BlackPointRecovery)
        {
            Console.Error.WriteLine("Black-point control does not reduce gray haze.");
            failures++;
        }
        return failures;
    }

    private static double Map(FilterRecommendation recommendation, double value)
    {
        int index = MathUtil.Clamp((int)Math.Round(value * 255.0), 0, 255);
        return recommendation.Green[index] / 65535.0;
    }

    private static string BucketName(double median)
    {
        if (median < 0.08) return "extreme-dark";
        if (median < 0.20) return "dark";
        if (median < 0.42) return "balanced";
        if (median < 0.58) return "daylight";
        return "bright";
    }

    private static void Print(Bucket bucket)
    {
        Console.WriteLine(
            "{0,-12} n={1,3}  P10 {2:0.000}->{3:0.000}  " +
            "P50 {4:0.000}->{5:0.000}  P95 {6:0.000}->{7:0.000}  " +
            "gamma {8:0.00}  brightness +{9:0}",
            bucket.Name,
            bucket.Count,
            Average(bucket.InputP10, bucket.Count),
            Average(bucket.OutputP10, bucket.Count),
            Average(bucket.InputP50, bucket.Count),
            Average(bucket.OutputP50, bucket.Count),
            Average(bucket.InputP95, bucket.Count),
            Average(bucket.OutputP95, bucket.Count),
            Average(bucket.Gamma, bucket.Count),
            Average(bucket.Brightness, bucket.Count));
    }

    private static double Average(double total, int count)
    {
        return count == 0 ? 0.0 : total / count;
    }
}
