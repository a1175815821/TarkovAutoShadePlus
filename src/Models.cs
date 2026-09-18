using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

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
        [DataMember] public string DisplayDevice { get; set; }
        [DataMember] public bool MultiDisplayMode { get; set; }
        [DataMember] public List<string> SelectedDisplayDevices { get; set; }
        [DataMember] public bool SmoothTransition { get; set; }
        [DataMember] public bool ProcessWatchEnabled { get; set; }
        [DataMember] public bool ProcessWatchConfigured { get; set; }
        [DataMember] public string WatchedProcessName { get; set; }
        [DataMember] public List<string> WatchedProcessNames { get; set; }
        [DataMember] public List<string> ScreenshotFolders { get; set; }
        [DataMember] public bool RealtimeEnabled { get; set; }
        [DataMember] public int RealtimeIntervalMs { get; set; }
        // 0 = 保守，1 = 均衡，2 = 灵敏。
        [DataMember] public int RealtimeSensitivity { get; set; }
        // 0 = ask, 1 = hide to tray, 2 = exit directly.
        [DataMember] public int CloseBehavior { get; set; }
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
                AlgorithmVersion = 11,
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
        }
    }

    internal static class SettingsStore
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TarkovAutoShade");
        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return AppSettings.CreateDefault();
                using (var stream = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null) return AppSettings.CreateDefault();
                    settings.Normalize();
                    return settings;
                }
            }
            catch
            {
                try
                {
                    string backup = FilePath + ".corrupt-" +
                        DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                    if (File.Exists(FilePath)) File.Copy(FilePath, backup, true);
                }
                catch { }
                return AppSettings.CreateDefault();
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temporary = FilePath + ".tmp";
                using (var stream = File.Create(temporary))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(stream, settings);
                }
                if (File.Exists(FilePath))
                {
                    try { File.Replace(temporary, FilePath, null); }
                    catch
                    {
                        File.Delete(FilePath);
                        File.Move(temporary, FilePath);
                    }
                }
                else
                {
                    File.Move(temporary, FilePath);
                }
            }
            catch
            {
                // Settings persistence must never prevent screen restoration.
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
