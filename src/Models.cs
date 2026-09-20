using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace TarkovAutoShadePlus
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public int AlgorithmVersion { get; set; }
        [DataMember] public string ScreenshotFolder { get; set; }
        [DataMember] public bool AutoWatch { get; set; }
        [DataMember] public int ShadowTarget { get; set; }
        [DataMember] public int HighlightProtection { get; set; }
        [DataMember] public int ExposureBias { get; set; }
        [DataMember] public int ContrastBias { get; set; }
        [DataMember] public int MaxStrength { get; set; }
        [DataMember] public int Warmth { get; set; }
        [DataMember] public int ColorCorrection { get; set; }
        [DataMember] public int IndoorComfort { get; set; }
        [DataMember] public int SceneGuard { get; set; }
        [DataMember] public int BlackPoint { get; set; }
        [DataMember] public int SaturationBias { get; set; }
        // 色彩聚焦总开关。关掉时保留全部数值但不参与计算，
        // 方便临时对比「有 / 无」而不用把调好的参数清掉。
        [DataMember] public bool FocusEnabled { get; set; }
        // 色彩聚焦：把某个色相的 RGB 通道在中间调推高、其余通道压回。
        // Gamma Ramp 只能按通道逐级改写，做不到真正的「按像素色相选择」，
        // 所以这里是通道偏向式的定向调色，不是 HSV 选区。
        [DataMember] public int FocusHue { get; set; }
        [DataMember] public int FocusStrength { get; set; }
        // 作用亮度：负值偏暗部，正值偏亮部，0 为中间调。
        [DataMember] public int FocusRange { get; set; }
        // 手动通道微调，用于自己调色。
        [DataMember] public int TrimRed { get; set; }
        [DataMember] public int TrimGreen { get; set; }
        [DataMember] public int TrimBlue { get; set; }
        // 臂环增强：红 / 蓝双色。同样受 Gamma Ramp 的约束，
        // 只能做「红蓝更跳 + 压制绿通道」，做不到把其余色相真正转成灰度。
        [DataMember] public bool ArmbandEnabled { get; set; }
        [DataMember] public int ArmbandLift { get; set; }
        [DataMember] public int ArmbandDesaturate { get; set; }
        [DataMember] public int HotkeyKeyCode { get; set; }
        [DataMember] public int HotkeyModifiers { get; set; }
        [DataMember] public int PresetIndex { get; set; }
        [DataMember] public bool CustomPresetInitialized { get; set; }
        [DataMember] public int CustomShadowTarget { get; set; }
        [DataMember] public int CustomHighlightProtection { get; set; }
        [DataMember] public int CustomExposureBias { get; set; }
        [DataMember] public int CustomContrastBias { get; set; }
        [DataMember] public int CustomMaxStrength { get; set; }
        [DataMember] public int CustomWarmth { get; set; }
        [DataMember] public int CustomColorCorrection { get; set; }
        [DataMember] public int CustomIndoorComfort { get; set; }
        [DataMember] public int CustomSceneGuard { get; set; }
        [DataMember] public int CustomBlackPoint { get; set; }
        [DataMember] public int CustomSaturationBias { get; set; }
        [DataMember] public int CustomFocusHue { get; set; }
        [DataMember] public int CustomFocusStrength { get; set; }
        [DataMember] public int CustomFocusRange { get; set; }
        [DataMember] public int CustomTrimRed { get; set; }
        [DataMember] public int CustomTrimGreen { get; set; }
        [DataMember] public int CustomTrimBlue { get; set; }
        [DataMember] public bool CustomFocusEnabled { get; set; }
        [DataMember] public bool CustomArmbandEnabled { get; set; }
        [DataMember] public int CustomArmbandLift { get; set; }
        [DataMember] public int CustomArmbandDesaturate { get; set; }
        [DataMember] public string DisplayDevice { get; set; }
        [DataMember] public bool MultiDisplayMode { get; set; }
        [DataMember] public List<string> SelectedDisplayDevices { get; set; }
        [DataMember] public bool SmoothTransition { get; set; }
        [DataMember] public bool ProcessWatchEnabled { get; set; }
        [DataMember] public bool ProcessWatchConfigured { get; set; }
        [DataMember] public string WatchedProcessName { get; set; }
        [DataMember] public List<string> WatchedProcessNames { get; set; }
        [DataMember] public List<string> ScreenshotFolders { get; set; }
        // 用户是否自己整理过截图目录列表。没整理过时才允许开机自动发现补全，
        // 否则「取消勾选某个目录」这件事下一次启动就被发现逻辑悄悄撤销了。
        [DataMember] public bool ScreenshotFoldersConfigured { get; set; }
        [DataMember] public bool RealtimeEnabled { get; set; }
        [DataMember] public int RealtimeIntervalMs { get; set; }
        // 0 = 保守，1 = 均衡，2 = 灵敏。
        [DataMember] public int RealtimeSensitivity { get; set; }
        // 0 = ask, 1 = hide to tray, 2 = exit directly.
        [DataMember] public int CloseBehavior { get; set; }
        // 窗口尺寸与位置。null 表示从没存过，界面沿用 XAML 的固定值。
        // 以前只吃 XAML 里的 1280x820，用户把窗口拉大重启就弹回去。
        // 位置必须能存负数（副屏常在主屏左边），所以用可空而不是拿 0 当哨兵。
        [DataMember] public int? WindowWidth { get; set; }
        [DataMember] public int? WindowHeight { get; set; }
        [DataMember] public int? WindowLeft { get; set; }
        [DataMember] public int? WindowTop { get; set; }
        [DataMember] public bool WindowMaximized { get; set; }
        // 原版 / 竞技场各自的调参档案。
        // 当前生效的那一份会镜像到上面的平铺字段，因此所有既有的读写逻辑不用改。
        [DataMember] public TuningProfile EftProfile { get; set; }
        [DataMember] public TuningProfile ArenaProfile { get; set; }
        // true = 跟随前台游戏自动切换档案；false = 锁定到 LockedProfileKey。
        [DataMember] public bool ProfileFollowGame { get; set; }
        [DataMember] public int LockedProfileKey { get; set; }

        public static AppSettings CreateDefault()
        {
            var created = new AppSettings {
                AlgorithmVersion = 14,
                ScreenshotFolder = GetDefaultScreenshotFolder(),
                AutoWatch = true,
                ShadowTarget = 70,
                HighlightProtection = 76,
                ExposureBias = 0,
                ContrastBias = 0,
                MaxStrength = 82,
                Warmth = 0,
                ColorCorrection = 72,
                IndoorComfort = 72,
                SceneGuard = 88,
                BlackPoint = 56,
                SaturationBias = 0,
                FocusEnabled = true,
                FocusHue = DefaultFocusHue,
                FocusStrength = 0,
                FocusRange = 0,
                TrimRed = 0,
                TrimGreen = 0,
                TrimBlue = 0,
                ArmbandEnabled = false,
                ArmbandLift = 0,
                ArmbandDesaturate = 0,
                HotkeyKeyCode = 119,
                HotkeyModifiers = 0,
                PresetIndex = 0,
                CustomPresetInitialized = true,
                CustomShadowTarget = 70,
                CustomHighlightProtection = 76,
                CustomExposureBias = 0,
                CustomContrastBias = 0,
                CustomMaxStrength = 82,
                CustomWarmth = 0,
                CustomColorCorrection = 72,
                CustomIndoorComfort = 72,
                CustomSceneGuard = 88,
                CustomBlackPoint = 56,
                CustomSaturationBias = 0,
                CustomFocusEnabled = true,
                CustomFocusHue = DefaultFocusHue,
                CustomFocusStrength = 0,
                CustomFocusRange = 0,
                CustomTrimRed = 0,
                CustomTrimGreen = 0,
                CustomTrimBlue = 0,
                CustomArmbandEnabled = false,
                CustomArmbandLift = 0,
                CustomArmbandDesaturate = 0,
                DisplayDevice = "",
                MultiDisplayMode = false,
                SelectedDisplayDevices = new List<string>(),
                SmoothTransition = true,
                ProcessWatchEnabled = false,
                ProcessWatchConfigured = true,
                WatchedProcessName = EftProcessName,
                WatchedProcessNames = new List<string> { EftProcessName, ArenaProcessName },
                ScreenshotFolders = new List<string> { GetDefaultScreenshotFolder() },
                RealtimeEnabled = false,
                RealtimeIntervalMs = 800,
                RealtimeSensitivity = 1,
                ProfileFollowGame = true,
                LockedProfileKey = ProfileKeyEft
            };
            created.EftProfile = new TuningProfile();
            created.EftProfile.CaptureFrom(created);
            created.ArenaProfile = new TuningProfile();
            created.ArenaProfile.CaptureFrom(created);
            created.ArenaProfile.ExposureBias = MathUtil.Clamp(
                created.ArenaProfile.ExposureBias - ArenaExposureTrim, -20, 20);
            return created;
        }

        public const string EftProcessName = "EscapeFromTarkov.exe";
        public const string ArenaProcessName = "EscapeFromTarkovArena.exe";

        public static readonly string[] DefaultWatchedProcessNames = new string[] {
            EftProcessName, ArenaProcessName
        };

        public static readonly string[] KnownGameFolderNames = new string[] {
            "Escape from Tarkov",
            "Escape from Tarkov Arena"
        };

        public const int ProfileKeyEft = 0;
        public const int ProfileKeyArena = 1;

        // 迁移时竞技场在复制原版的基础上只压一点亮度微调。
        // 竞技场地图整体更亮，同样的提亮幅度在这里会偏过。
        public const int ArenaExposureTrim = 3;

        // 色彩聚焦的默认目标色相：25° 暖橙，对应火光、皮肤、木料与大部分敌人轮廓。
        public const int DefaultFocusHue = 25;

        // 色彩聚焦的作用亮度范围（FocusRange）与手动通道微调（Trim*）的量程。
        public const int FocusRangeLimit = 20;
        public const int TrimLimit = 20;

        // 臂环增强两项强度的量程。
        public const int ArmbandLiftLimit = 100;
        public const int ArmbandDesaturateLimit = 100;

        // 进程名与截图路径都能用来判断属于哪一端：Arena 两端命名都带 Arena。
        public static int ResolveGameKey(string processNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(processNameOrPath)) return ProfileKeyEft;
            if (processNameOrPath.IndexOf("Arena", StringComparison.OrdinalIgnoreCase) >= 0)
                return ProfileKeyArena;
            return ProfileKeyEft;
        }

        public static string GetProfileLabel(int key)
        {
            return key == ProfileKeyArena ? "竞技场" : "原版";
        }

        public TuningProfile GetProfile(int key)
        {
            if (key == ProfileKeyArena)
            {
                if (ArenaProfile == null) ArenaProfile = new TuningProfile();
                return ArenaProfile;
            }
            if (EftProfile == null) EftProfile = new TuningProfile();
            return EftProfile;
        }

        public static string GetFriendlyProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return "未知进程";
            string file = Path.GetFileName(processName);
            if (string.Equals(file, ArenaProcessName, StringComparison.OrdinalIgnoreCase))
                return "竞技场";
            if (string.Equals(file, EftProcessName, StringComparison.OrdinalIgnoreCase))
                return "原版";
            return Path.GetFileNameWithoutExtension(file);
        }

        public static string GetFriendlyFolderName(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return "未知目录";
            if (folder.IndexOf("Arena", StringComparison.OrdinalIgnoreCase) >= 0)
                return "竞技场";
            if (folder.IndexOf("Escape from Tarkov", StringComparison.OrdinalIgnoreCase) >= 0)
                return "原版";
            try { return Path.GetFileName(Path.GetDirectoryName(folder)) ?? folder; }
            catch { return folder; }
        }

        public static string GetDefaultScreenshotFolder()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "Escape from Tarkov", "Screenshots");
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(ScreenshotFolder))
                ScreenshotFolder = GetDefaultScreenshotFolder();

            // Version 1 intentionally shipped with conservative defaults.
            // Migrate once so existing users receive the recalibrated curve.
            if (AlgorithmVersion < 2)
            {
                ShadowTarget = Math.Max(ShadowTarget, 70);
                HighlightProtection = Math.Max(HighlightProtection, 76);
                MaxStrength = Math.Max(MaxStrength, 82);
                AlgorithmVersion = 2;
            }

            if (AlgorithmVersion < 3)
            {
                ExposureBias = 0;
                ContrastBias = 0;

                // Recover the common "everything at zero" state, which makes
                // every generated curve identical to the unfiltered desktop.
                if (ShadowTarget == 0 &&
                    HighlightProtection == 0 &&
                    MaxStrength == 0)
                {
                    ShadowTarget = 70;
                    HighlightProtection = 76;
                    MaxStrength = 82;
                    Warmth = 3;
                }
                AlgorithmVersion = 3;
            }

            if (AlgorithmVersion < 4)
            {
                ColorCorrection = 72;
                IndoorComfort = 72;
                SceneGuard = 88;
                AlgorithmVersion = 4;
            }

            if (AlgorithmVersion < 5)
            {
                // Version 3 used +3 as a recovery default. It is not a
                // deliberate user color preference, so return that legacy
                // value to the new neutral baseline.
                if (Warmth == 3) Warmth = 0;
                BlackPoint = 56;
                AlgorithmVersion = 5;
            }

            if (AlgorithmVersion < 6)
            {
                SaturationBias = 0;
                HotkeyKeyCode = 119;
                HotkeyModifiers = 0;
                AlgorithmVersion = 6;
            }

            if (AlgorithmVersion < 7 || !CustomPresetInitialized)
            {
                CustomShadowTarget = ShadowTarget;
                CustomHighlightProtection = HighlightProtection;
                CustomExposureBias = ExposureBias;
                CustomContrastBias = ContrastBias;
                CustomMaxStrength = MaxStrength;
                CustomWarmth = Warmth;
                CustomColorCorrection = ColorCorrection;
                CustomIndoorComfort = IndoorComfort;
                CustomSceneGuard = SceneGuard;
                CustomBlackPoint = BlackPoint;
                CustomSaturationBias = SaturationBias;
                CustomPresetInitialized = true;
                AlgorithmVersion = 7;
            }

            if (AlgorithmVersion < 8)
                AlgorithmVersion = 8;

            if (AlgorithmVersion < 9)
            {
                SmoothTransition = true;
                AlgorithmVersion = 9;
            }

            if (ScreenshotFolders == null)
                ScreenshotFolders = new List<string>();
            if (WatchedProcessNames == null)
                WatchedProcessNames = new List<string>();

            if (AlgorithmVersion < 10)
            {
                if (!string.IsNullOrWhiteSpace(ScreenshotFolder) &&
                    !ContainsIgnoreCase(ScreenshotFolders, ScreenshotFolder))
                    ScreenshotFolders.Insert(0, ScreenshotFolder);
                if (!string.IsNullOrWhiteSpace(WatchedProcessName) &&
                    !ContainsIgnoreCase(WatchedProcessNames, WatchedProcessName))
                    WatchedProcessNames.Insert(0, WatchedProcessName);
                // 老用户默认只监听了原版，升级后自动补上竞技场，做到双端同时监听。
                // 若用户之前刻意只留自定义进程，也补上双默认，避免升级后丢游戏。
                foreach (string defaults in DefaultWatchedProcessNames)
                {
                    if (!ContainsIgnoreCase(WatchedProcessNames, defaults))
                        WatchedProcessNames.Add(defaults);
                }
                AlgorithmVersion = 10;
            }

            if (SelectedDisplayDevices == null)
                SelectedDisplayDevices = new List<string>();

            ShadowTarget = MathUtil.Clamp(ShadowTarget, 0, 100);
            HighlightProtection = MathUtil.Clamp(HighlightProtection, 0, 100);
            ExposureBias = MathUtil.Clamp(ExposureBias, -20, 20);
            ContrastBias = MathUtil.Clamp(ContrastBias, -20, 20);
            MaxStrength = MathUtil.Clamp(MaxStrength, 0, 100);
            Warmth = MathUtil.Clamp(Warmth, -20, 20);
            ColorCorrection = MathUtil.Clamp(ColorCorrection, 0, 100);
            IndoorComfort = MathUtil.Clamp(IndoorComfort, 0, 100);
            SceneGuard = MathUtil.Clamp(SceneGuard, 0, 100);
            BlackPoint = MathUtil.Clamp(BlackPoint, 0, 100);
            SaturationBias = MathUtil.Clamp(SaturationBias, -20, 20);
            FocusHue = MathUtil.Clamp(FocusHue, 0, 359);
            FocusStrength = MathUtil.Clamp(FocusStrength, 0, 100);
            FocusRange = MathUtil.Clamp(FocusRange, -FocusRangeLimit, FocusRangeLimit);
            TrimRed = MathUtil.Clamp(TrimRed, -TrimLimit, TrimLimit);
            TrimGreen = MathUtil.Clamp(TrimGreen, -TrimLimit, TrimLimit);
            TrimBlue = MathUtil.Clamp(TrimBlue, -TrimLimit, TrimLimit);
            ArmbandLift = MathUtil.Clamp(ArmbandLift, 0, ArmbandLiftLimit);
            ArmbandDesaturate = MathUtil.Clamp(
                ArmbandDesaturate, 0, ArmbandDesaturateLimit);
            if (HotkeyKeyCode <= 0 || HotkeyKeyCode > 255) HotkeyKeyCode = 119;
            HotkeyModifiers = HotkeyModifiers & 0x0007;
            PresetIndex = MathUtil.Clamp(PresetIndex, 0, 5);
            CustomShadowTarget = MathUtil.Clamp(CustomShadowTarget, 0, 100);
            CustomHighlightProtection = MathUtil.Clamp(CustomHighlightProtection, 0, 100);
            CustomExposureBias = MathUtil.Clamp(CustomExposureBias, -20, 20);
            CustomContrastBias = MathUtil.Clamp(CustomContrastBias, -20, 20);
            CustomMaxStrength = MathUtil.Clamp(CustomMaxStrength, 0, 100);
            CustomWarmth = MathUtil.Clamp(CustomWarmth, -20, 20);
            CustomColorCorrection = MathUtil.Clamp(CustomColorCorrection, 0, 100);
            CustomIndoorComfort = MathUtil.Clamp(CustomIndoorComfort, 0, 100);
            CustomSceneGuard = MathUtil.Clamp(CustomSceneGuard, 0, 100);
            CustomBlackPoint = MathUtil.Clamp(CustomBlackPoint, 0, 100);
            CustomSaturationBias = MathUtil.Clamp(CustomSaturationBias, -20, 20);
            CustomFocusHue = MathUtil.Clamp(CustomFocusHue, 0, 359);
            CustomFocusStrength = MathUtil.Clamp(CustomFocusStrength, 0, 100);
            CustomFocusRange = MathUtil.Clamp(
                CustomFocusRange, -FocusRangeLimit, FocusRangeLimit);
            CustomTrimRed = MathUtil.Clamp(CustomTrimRed, -TrimLimit, TrimLimit);
            CustomTrimGreen = MathUtil.Clamp(CustomTrimGreen, -TrimLimit, TrimLimit);
            CustomTrimBlue = MathUtil.Clamp(CustomTrimBlue, -TrimLimit, TrimLimit);
            CustomArmbandLift = MathUtil.Clamp(
                CustomArmbandLift, 0, ArmbandLiftLimit);
            CustomArmbandDesaturate = MathUtil.Clamp(
                CustomArmbandDesaturate, 0, ArmbandDesaturateLimit);
            if (DisplayDevice == null) DisplayDevice = "";
            var normalizedDisplays = new List<string>();
            foreach (string device in SelectedDisplayDevices)
            {
                if (!string.IsNullOrWhiteSpace(device) &&
                    !normalizedDisplays.Contains(device))
                    normalizedDisplays.Add(device);
            }
            SelectedDisplayDevices = normalizedDisplays;
            ScreenshotFolders = NormalizePathList(ScreenshotFolders, 8);
            WatchedProcessNames = NormalizeProcessList(WatchedProcessNames, 8);
            if (ScreenshotFolders.Count > 0 && string.IsNullOrWhiteSpace(ScreenshotFolder))
                ScreenshotFolder = ScreenshotFolders[0];
            if (string.IsNullOrWhiteSpace(ScreenshotFolder))
                ScreenshotFolder = GetDefaultScreenshotFolder();
            if (!ContainsIgnoreCase(ScreenshotFolders, ScreenshotFolder))
                ScreenshotFolders.Insert(0, ScreenshotFolder);
            if (WatchedProcessNames.Count > 0 && string.IsNullOrWhiteSpace(WatchedProcessName))
                WatchedProcessName = WatchedProcessNames[0];
            if (string.IsNullOrWhiteSpace(WatchedProcessName))
                WatchedProcessName = EftProcessName;
            if (!ContainsIgnoreCase(WatchedProcessNames, WatchedProcessName))
                WatchedProcessNames.Insert(0, WatchedProcessName);
            if (!ProcessWatchConfigured)
            {
                ProcessWatchEnabled = false;
                ProcessWatchConfigured = true;
            }
            if (AlgorithmVersion < 11)
            {
                // 实时全自动默认关闭，由用户主动开启，避免升级后突然常驻抓屏。
                RealtimeIntervalMs = 800;
                RealtimeSensitivity = 1;
                AlgorithmVersion = 11;
            }

            if (AlgorithmVersion < 12)
            {
                // 原版 / 竞技场参数独立。旧配置只有一套，复制成两份；
                // 竞技场额外压一点亮度微调，因为竞技场地图整体更亮。
                var eft = new TuningProfile();
                eft.CaptureFrom(this);
                var arena = new TuningProfile();
                arena.CaptureFrom(this);
                arena.ExposureBias = MathUtil.Clamp(
                    arena.ExposureBias - ArenaExposureTrim, -20, 20);
                EftProfile = eft;
                ArenaProfile = arena;
                AlgorithmVersion = 12;
            }
            // 老版本程序写回的 json 没有这两个字段，反序列化后为 null，必须兜底。
            if (EftProfile == null)
            {
                EftProfile = new TuningProfile();
                EftProfile.CaptureFrom(this);
            }
            if (ArenaProfile == null)
            {
                ArenaProfile = new TuningProfile();
                ArenaProfile.CaptureFrom(this);
            }

            if (AlgorithmVersion < 13)
            {
                // 色彩聚焦是新增参数，老配置里没有。默认强度为 0，
                // 也就是「不影响画面」，需要用户自己在界面上开启。
                // 色相给 25° 暖橙，只是让色卡有个合理的起点。
                FocusHue = DefaultFocusHue;
                FocusStrength = 0;
                FocusRange = 0;
                TrimRed = TrimGreen = TrimBlue = 0;
                EftProfile.FocusHue = DefaultFocusHue;
                ArenaProfile.FocusHue = DefaultFocusHue;
                EftProfile.FocusStrength = 0;
                ArenaProfile.FocusStrength = 0;
                EftProfile.FocusRange = 0;
                ArenaProfile.FocusRange = 0;
                EftProfile.TrimRed = EftProfile.TrimGreen = EftProfile.TrimBlue = 0;
                ArenaProfile.TrimRed = ArenaProfile.TrimGreen = ArenaProfile.TrimBlue = 0;
                EftProfile.FocusEnabled = true;
                ArenaProfile.FocusEnabled = true;
                AlgorithmVersion = 13;
            }

            if (AlgorithmVersion < 14)
            {
                // v14 给色彩聚焦补了总开关。已经有配置的人（v13 时把强度调上去了）
                // 应当保持开启，否则升级后会发现刚调好的效果自己没了。
                FocusEnabled = FocusStrength > 0;
                EftProfile.FocusEnabled = EftProfile.FocusStrength > 0;
                ArenaProfile.FocusEnabled = ArenaProfile.FocusStrength > 0;
                // 臂环增强是全新功能，一律默认关闭，需要用户自己开。
                ArmbandEnabled = false;
                ArmbandLift = 0;
                ArmbandDesaturate = 0;
                EftProfile.ArmbandEnabled = false;
                ArenaProfile.ArmbandEnabled = false;
                EftProfile.ArmbandLift = EftProfile.ArmbandDesaturate = 0;
                ArenaProfile.ArmbandLift = ArenaProfile.ArmbandDesaturate = 0;
                AlgorithmVersion = 14;
            }

            EftProfile.ClampAll();
            ArenaProfile.ClampAll();
            LockedProfileKey = MathUtil.Clamp(
                LockedProfileKey, ProfileKeyEft, ProfileKeyArena);

            RealtimeIntervalMs = MathUtil.Clamp(RealtimeIntervalMs, 600, 3000);
            RealtimeSensitivity = MathUtil.Clamp(RealtimeSensitivity, 0, 2);
            CloseBehavior = MathUtil.Clamp(CloseBehavior, 0, 2);
        }

        private static bool ContainsIgnoreCase(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value)) return false;
            foreach (string item in list)
            {
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static List<string> NormalizePathList(List<string> list, int maxCount)
        {
            var result = new List<string>();
            if (list == null) return result;
            foreach (string item in list)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                string trimmed = item.Trim();
                if (ContainsIgnoreCase(result, trimmed)) continue;
                result.Add(trimmed);
                if (result.Count >= maxCount) break;
            }
            return result;
        }

        private static List<string> NormalizeProcessList(List<string> list, int maxCount)
        {
            var result = new List<string>();
            if (list == null) return result;
            foreach (string item in list)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                string file = Path.GetFileName(item.Trim());
                if (string.IsNullOrWhiteSpace(file)) continue;
                if (ContainsIgnoreCase(result, file)) continue;
                result.Add(file);
                if (result.Count >= maxCount) break;
            }
            return result;
        }
    }

    /// <summary>
    /// 一份完整的调参档案。原版与竞技场各持一份，切换时整体对调，
    /// 避免两套参数互相污染。
    /// </summary>
    [DataContract]
    internal sealed class TuningProfile
    {
        [DataMember] public int ShadowTarget { get; set; }
        [DataMember] public int HighlightProtection { get; set; }
        [DataMember] public int ExposureBias { get; set; }
        [DataMember] public int ContrastBias { get; set; }
        [DataMember] public int MaxStrength { get; set; }
        [DataMember] public int Warmth { get; set; }
        [DataMember] public int ColorCorrection { get; set; }
        [DataMember] public int IndoorComfort { get; set; }
        [DataMember] public int SceneGuard { get; set; }
        [DataMember] public int BlackPoint { get; set; }
        [DataMember] public int SaturationBias { get; set; }
        [DataMember] public bool FocusEnabled { get; set; }
        [DataMember] public int FocusHue { get; set; }
        [DataMember] public int FocusStrength { get; set; }
        [DataMember] public int FocusRange { get; set; }
        [DataMember] public int TrimRed { get; set; }
        [DataMember] public int TrimGreen { get; set; }
        [DataMember] public int TrimBlue { get; set; }
        [DataMember] public bool ArmbandEnabled { get; set; }
        [DataMember] public int ArmbandLift { get; set; }
        [DataMember] public int ArmbandDesaturate { get; set; }
        [DataMember] public int PresetIndex { get; set; }
        [DataMember] public bool CustomPresetInitialized { get; set; }
        [DataMember] public int CustomShadowTarget { get; set; }
        [DataMember] public int CustomHighlightProtection { get; set; }
        [DataMember] public int CustomExposureBias { get; set; }
        [DataMember] public int CustomContrastBias { get; set; }
        [DataMember] public int CustomMaxStrength { get; set; }
        [DataMember] public int CustomWarmth { get; set; }
        [DataMember] public int CustomColorCorrection { get; set; }
        [DataMember] public int CustomIndoorComfort { get; set; }
        [DataMember] public int CustomSceneGuard { get; set; }
        [DataMember] public int CustomBlackPoint { get; set; }
        [DataMember] public int CustomSaturationBias { get; set; }
        [DataMember] public int CustomFocusHue { get; set; }
        [DataMember] public int CustomFocusStrength { get; set; }
        [DataMember] public int CustomFocusRange { get; set; }
        [DataMember] public int CustomTrimRed { get; set; }
        [DataMember] public int CustomTrimGreen { get; set; }
        [DataMember] public int CustomTrimBlue { get; set; }
        [DataMember] public bool CustomFocusEnabled { get; set; }
        [DataMember] public bool CustomArmbandEnabled { get; set; }
        [DataMember] public int CustomArmbandLift { get; set; }
        [DataMember] public int CustomArmbandDesaturate { get; set; }

        public void CaptureFrom(AppSettings settings)
        {
            if (settings == null) return;
            ShadowTarget = settings.ShadowTarget;
            HighlightProtection = settings.HighlightProtection;
            ExposureBias = settings.ExposureBias;
            ContrastBias = settings.ContrastBias;
            MaxStrength = settings.MaxStrength;
            Warmth = settings.Warmth;
            ColorCorrection = settings.ColorCorrection;
            IndoorComfort = settings.IndoorComfort;
            SceneGuard = settings.SceneGuard;
            BlackPoint = settings.BlackPoint;
            SaturationBias = settings.SaturationBias;
            FocusEnabled = settings.FocusEnabled;
            FocusHue = settings.FocusHue;
            FocusStrength = settings.FocusStrength;
            FocusRange = settings.FocusRange;
            TrimRed = settings.TrimRed;
            TrimGreen = settings.TrimGreen;
            TrimBlue = settings.TrimBlue;
            ArmbandEnabled = settings.ArmbandEnabled;
            ArmbandLift = settings.ArmbandLift;
            ArmbandDesaturate = settings.ArmbandDesaturate;
            PresetIndex = settings.PresetIndex;
            CustomPresetInitialized = settings.CustomPresetInitialized;
            CustomShadowTarget = settings.CustomShadowTarget;
            CustomHighlightProtection = settings.CustomHighlightProtection;
            CustomExposureBias = settings.CustomExposureBias;
            CustomContrastBias = settings.CustomContrastBias;
            CustomMaxStrength = settings.CustomMaxStrength;
            CustomWarmth = settings.CustomWarmth;
            CustomColorCorrection = settings.CustomColorCorrection;
            CustomIndoorComfort = settings.CustomIndoorComfort;
            CustomSceneGuard = settings.CustomSceneGuard;
            CustomBlackPoint = settings.CustomBlackPoint;
            CustomSaturationBias = settings.CustomSaturationBias;
            CustomFocusHue = settings.CustomFocusHue;
            CustomFocusStrength = settings.CustomFocusStrength;
            CustomFocusRange = settings.CustomFocusRange;
            CustomTrimRed = settings.CustomTrimRed;
            CustomTrimGreen = settings.CustomTrimGreen;
            CustomTrimBlue = settings.CustomTrimBlue;
            CustomFocusEnabled = settings.CustomFocusEnabled;
            CustomArmbandEnabled = settings.CustomArmbandEnabled;
            CustomArmbandLift = settings.CustomArmbandLift;
            CustomArmbandDesaturate = settings.CustomArmbandDesaturate;
            ClampAll();
        }

        public void ApplyTo(AppSettings settings)
        {
            if (settings == null) return;
            settings.ShadowTarget = ShadowTarget;
            settings.HighlightProtection = HighlightProtection;
            settings.ExposureBias = ExposureBias;
            settings.ContrastBias = ContrastBias;
            settings.MaxStrength = MaxStrength;
            settings.Warmth = Warmth;
            settings.ColorCorrection = ColorCorrection;
            settings.IndoorComfort = IndoorComfort;
            settings.SceneGuard = SceneGuard;
            settings.BlackPoint = BlackPoint;
            settings.SaturationBias = SaturationBias;
            settings.FocusEnabled = FocusEnabled;
            settings.FocusHue = FocusHue;
            settings.FocusStrength = FocusStrength;
            settings.FocusRange = FocusRange;
            settings.TrimRed = TrimRed;
            settings.TrimGreen = TrimGreen;
            settings.TrimBlue = TrimBlue;
            settings.ArmbandEnabled = ArmbandEnabled;
            settings.ArmbandLift = ArmbandLift;
            settings.ArmbandDesaturate = ArmbandDesaturate;
            settings.PresetIndex = PresetIndex;
            settings.CustomPresetInitialized = CustomPresetInitialized;
            settings.CustomShadowTarget = CustomShadowTarget;
            settings.CustomHighlightProtection = CustomHighlightProtection;
            settings.CustomExposureBias = CustomExposureBias;
            settings.CustomContrastBias = CustomContrastBias;
            settings.CustomMaxStrength = CustomMaxStrength;
            settings.CustomWarmth = CustomWarmth;
            settings.CustomColorCorrection = CustomColorCorrection;
            settings.CustomIndoorComfort = CustomIndoorComfort;
            settings.CustomSceneGuard = CustomSceneGuard;
            settings.CustomBlackPoint = CustomBlackPoint;
            settings.CustomSaturationBias = CustomSaturationBias;
            settings.CustomFocusHue = CustomFocusHue;
            settings.CustomFocusStrength = CustomFocusStrength;
            settings.CustomFocusRange = CustomFocusRange;
            settings.CustomTrimRed = CustomTrimRed;
            settings.CustomTrimGreen = CustomTrimGreen;
            settings.CustomTrimBlue = CustomTrimBlue;
            settings.CustomFocusEnabled = CustomFocusEnabled;
            settings.CustomArmbandEnabled = CustomArmbandEnabled;
            settings.CustomArmbandLift = CustomArmbandLift;
            settings.CustomArmbandDesaturate = CustomArmbandDesaturate;
        }

        public void ClampAll()
        {
            ShadowTarget = MathUtil.Clamp(ShadowTarget, 0, 100);
            HighlightProtection = MathUtil.Clamp(HighlightProtection, 0, 100);
            ExposureBias = MathUtil.Clamp(ExposureBias, -20, 20);
            ContrastBias = MathUtil.Clamp(ContrastBias, -20, 20);
            MaxStrength = MathUtil.Clamp(MaxStrength, 0, 100);
            Warmth = MathUtil.Clamp(Warmth, -20, 20);
            ColorCorrection = MathUtil.Clamp(ColorCorrection, 0, 100);
            IndoorComfort = MathUtil.Clamp(IndoorComfort, 0, 100);
            SceneGuard = MathUtil.Clamp(SceneGuard, 0, 100);
            BlackPoint = MathUtil.Clamp(BlackPoint, 0, 100);
            SaturationBias = MathUtil.Clamp(SaturationBias, -20, 20);
            FocusHue = MathUtil.Clamp(FocusHue, 0, 359);
            FocusStrength = MathUtil.Clamp(FocusStrength, 0, 100);
            FocusRange = MathUtil.Clamp(FocusRange, -AppSettings.FocusRangeLimit,
                AppSettings.FocusRangeLimit);
            TrimRed = MathUtil.Clamp(TrimRed, -AppSettings.TrimLimit, AppSettings.TrimLimit);
            TrimGreen = MathUtil.Clamp(TrimGreen, -AppSettings.TrimLimit, AppSettings.TrimLimit);
            TrimBlue = MathUtil.Clamp(TrimBlue, -AppSettings.TrimLimit, AppSettings.TrimLimit);
            ArmbandLift = MathUtil.Clamp(ArmbandLift, 0, AppSettings.ArmbandLiftLimit);
            ArmbandDesaturate = MathUtil.Clamp(ArmbandDesaturate, 0,
                AppSettings.ArmbandDesaturateLimit);
            PresetIndex = MathUtil.Clamp(PresetIndex, 0, 5);
            CustomShadowTarget = MathUtil.Clamp(CustomShadowTarget, 0, 100);
            CustomHighlightProtection = MathUtil.Clamp(CustomHighlightProtection, 0, 100);
            CustomExposureBias = MathUtil.Clamp(CustomExposureBias, -20, 20);
            CustomContrastBias = MathUtil.Clamp(CustomContrastBias, -20, 20);
            CustomMaxStrength = MathUtil.Clamp(CustomMaxStrength, 0, 100);
            CustomWarmth = MathUtil.Clamp(CustomWarmth, -20, 20);
            CustomColorCorrection = MathUtil.Clamp(CustomColorCorrection, 0, 100);
            CustomIndoorComfort = MathUtil.Clamp(CustomIndoorComfort, 0, 100);
            CustomSceneGuard = MathUtil.Clamp(CustomSceneGuard, 0, 100);
            CustomBlackPoint = MathUtil.Clamp(CustomBlackPoint, 0, 100);
            CustomSaturationBias = MathUtil.Clamp(CustomSaturationBias, -20, 20);
            CustomFocusHue = MathUtil.Clamp(CustomFocusHue, 0, 359);
            CustomFocusStrength = MathUtil.Clamp(CustomFocusStrength, 0, 100);
            CustomFocusRange = MathUtil.Clamp(CustomFocusRange,
                -AppSettings.FocusRangeLimit, AppSettings.FocusRangeLimit);
            CustomTrimRed = MathUtil.Clamp(CustomTrimRed,
                -AppSettings.TrimLimit, AppSettings.TrimLimit);
            CustomTrimGreen = MathUtil.Clamp(CustomTrimGreen,
                -AppSettings.TrimLimit, AppSettings.TrimLimit);
            CustomTrimBlue = MathUtil.Clamp(CustomTrimBlue,
                -AppSettings.TrimLimit, AppSettings.TrimLimit);
            CustomArmbandLift = MathUtil.Clamp(CustomArmbandLift, 0,
                AppSettings.ArmbandLiftLimit);
            CustomArmbandDesaturate = MathUtil.Clamp(CustomArmbandDesaturate, 0,
                AppSettings.ArmbandDesaturateLimit);
        }
    }

    internal static class SettingsStore
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TarkovAutoShade");
        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        // 读档失败会被默认值悄悄顶掉，落盘失败则改动根本写不出去 —— 这两条就是
        // 用户感到的「设置重启后就没了」。原先它们一声不响，现在留给界面和日志。
        public static string LastLoadError = "";
        public static string LastSaveError = "";

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return AppSettings.CreateDefault();
                using (var stream = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null)
                    {
                        LastLoadError = "配置文件内容为空";
                        Diagnostics.Warn("设置", "配置文件内容为空，本次改用默认配置");
                        return AppSettings.CreateDefault();
                    }
                    settings.Normalize();
                    LastLoadError = "";
                    return settings;
                }
            }
            catch (Exception error)
            {
                string backupName = "";
                try
                {
                    string backup = FilePath + ".corrupt-" +
                        DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                    if (File.Exists(FilePath))
                    {
                        File.Copy(FilePath, backup, true);
                        backupName = Path.GetFileName(backup);
                    }
                }
                catch { }
                LastLoadError = error.GetType().Name + " " + error.Message;
                Diagnostics.Error("设置", "读取配置失败，本次改用默认配置" +
                    (backupName.Length == 0 ? "" : "（原文件已备份为 " + backupName + "）"), error);
                return AppSettings.CreateDefault();
            }
        }

        public static void Save(AppSettings settings)
        {
            byte[] payload;
            try
            {
                using (var stream = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(stream, settings);
                    payload = stream.ToArray();
                }
            }
            catch (Exception error)
            {
                LastSaveError = error.GetType().Name + " " + error.Message;
                Diagnostics.Error("设置", "序列化配置失败，本次改动未保存", error);
                return;
            }

            string temporary = FilePath + ".tmp";
            // 原先是「删掉正式文件、再把临时文件搬过去」：这两步之间只要失败一次
            // （杀软或同步盘占用很常见），配置文件就整个消失，下次启动直接回落到
            // 默认值 —— 用户看到的正是「设置重启后没了」。覆盖写不存在中间没有
            // 文件的窗口，最坏情况只是这次没更新成。
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllBytes(temporary, payload);
                    File.Copy(temporary, FilePath, true);
                    LastSaveError = "";
                    return;
                }
                catch (Exception error)
                {
                    LastSaveError = error.GetType().Name + " " + error.Message;
                    if (attempt < 2)
                    {
                        Thread.Sleep(120);
                        continue;
                    }
                    Diagnostics.Error("设置", "保存配置失败，本次改动不会留存（新内容留在 " +
                        Path.GetFileName(temporary) + "）", error);
                }
            }
        }
    }

    internal sealed class AnalysisResult
    {
        public string FilePath;
        public DateTime CapturedAt;
        public double P01;
        public double P05;
        public double P10;
        public double P25;
        public double Median;
        public double P75;
        public double P90;
        public double P95;
        public double P99;
        public double DynamicRange;
        public double EdgeEnergy;
        public double MeanRed;
        public double MeanGreen;
        public double MeanBlue;
        public double NightVisionScore;
        public double RedCast;
        public double GreenCast;
        public double BlueCast;
        public double UpperMean;
        public double LowerMean;
        public double BrightFraction;
        public bool IsUsable;
        public string SkipReason;
        public string SceneLabel;
        public int[] Histogram;
        public FilterRecommendation Recommendation;
    }

    internal sealed class FilterRecommendation
    {
        public string ProfileName;
        public double EquivalentGamma;
        public double BrightnessBoost;
        public double ContrastBoost;
        public double StrengthBlend;
        public double Gamma;
        public double ShadowLift;
        public double HighlightCompression;
        public double BlackPointRecovery;
        public double Contrast;
        public double Warmth;
        public double RedBalance;
        public double GreenBalance;
        public double BlueBalance;
        // 色彩聚焦：按目标色相算出的分通道偏移，以及它作用的亮度窗口中心。
        public double FocusRed;
        public double FocusGreen;
        public double FocusBlue;
        public double FocusCenter;
        // 臂环增强：R / B 在饱和区（高值，权重 v²）的提升，以及 G 中间调的压制。
        public double ArmbandRed;
        public double ArmbandBlue;
        public double ArmbandGreen;
        public double ChangeStrength;
        public ushort[] Red;
        public ushort[] Green;
        public ushort[] Blue;
    }

    internal sealed class DisplayTarget
    {
        public string DeviceName;
        public string FriendlyName;
        public bool Primary;

        public override string ToString()
        {
            return FriendlyName + (Primary ? "（主显示器）" : "");
        }
    }

    internal static class MathUtil
    {
        public static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }

        public static double SmoothStep(double start, double end, double value)
        {
            if (end <= start) return value >= end ? 1.0 : 0.0;
            double t = Clamp((value - start) / (end - start), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
