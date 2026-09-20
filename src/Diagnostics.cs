using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace TarkovAutoShadePlus
{
    /// <summary>
    /// 诊断日志。这个项目原先把所有异常都 catch 掉、界面提示 3 秒就消失，
    /// 用户报障时手上什么都没有。这里提供一条最低成本的回流：进程内留一份环形
    /// 缓冲 + 一个滚动文件，再能一键导出一份可直接贴出来的文本包。
    ///
    /// 硬性要求：日志本身绝不能成为新的故障源。所有写入都在 try/catch 里，
    /// 磁盘满 / 目录只读 / 文件被占用都只是丢掉这条记录而已。
    /// </summary>
    internal static class Diagnostics
    {
        private const int MemoryCapacity = 400;
        private const long MaximumFileBytes = 256 * 1024;
        private const int RotatedBackups = 1;

        private static readonly object sync = new object();
        private static readonly Queue<string> recent = new Queue<string>();
        private static readonly Dictionary<string, DateTime> lastWritten =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly DateTime startedAtUtc = DateTime.UtcNow;
        private static bool fileUnavailable;

        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TarkovAutoShade");
        private static readonly string FilePath = Path.Combine(Folder, "diagnostics.log");

        public static string LogFilePath { get { return FilePath; } }

        public static void Info(string area, string message) { Write("INFO", area, message, null); }
        public static void Warn(string area, string message) { Write("WARN", area, message, null); }

        public static void Error(string area, string message, Exception error)
        {
            Write("ERROR", area, error == null ? message :
                message + "：" + error.GetType().Name + " " + error.Message, error);
        }

        /// <summary>
        /// 同一个键在间隔内只记一次。实时循环和显示器探测会反复撞同一个错误，
        /// 不限流的话真正有用的几条会被刷掉。
        /// </summary>
        public static void Throttled(string area, string key, string message,
            TimeSpan minimumInterval)
        {
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                DateTime previous;
                if (lastWritten.TryGetValue(key, out previous) &&
                    (now - previous) < minimumInterval) return;
                if (lastWritten.Count > 64) lastWritten.Clear();
                lastWritten[key] = now;
            }
            Write("INFO", area, message, null);
        }

        private static void Write(string level, string area, string message, Exception error)
        {
            string line = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                " [" + level + "] " + area + "：" + message;
            // 堆栈必须进文件：这次排查启动崩溃时，它只在内存环形缓冲里，
            // 我只能去翻 Windows 事件日志才拿到调用栈。
            if (error != null && !string.IsNullOrEmpty(error.StackTrace))
            {
                line = line + Environment.NewLine + "        " + error.StackTrace.Trim().Replace(
                    Environment.NewLine, Environment.NewLine + "        ");
            }
            lock (sync)
            {
                recent.Enqueue(line);
                while (recent.Count > MemoryCapacity) recent.Dequeue();
                AppendToFile(line, error);
            }
        }

        private static void AppendToFile(string line, Exception error)
        {
            if (fileUnavailable) return;
            try
            {
                Directory.CreateDirectory(Folder);
                RotateIfNeeded();
                using (var stream = new FileStream(FilePath, FileMode.Append,
                    FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd ") + line);
                    if (error != null && error.InnerException != null)
                        writer.WriteLine("        inner: " +
                            error.InnerException.GetType().Name + " " +
                            error.InnerException.Message);
                }
            }
            catch
            {
                // 日志写不进去就只留在内存里，导出的报告仍然有效。
                fileUnavailable = true;
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (!info.Exists || info.Length < MaximumFileBytes) return;
                string oldest = FilePath + "." + RotatedBackups;
                if (File.Exists(oldest)) File.Delete(oldest);
                for (int index = RotatedBackups - 1; index >= 1; index--)
                {
                    string from = FilePath + "." + index;
                    if (File.Exists(from)) File.Move(from, FilePath + "." + (index + 1));
                }
                File.Move(FilePath, FilePath + ".1");
            }
            catch
            {
            }
        }

        public static List<string> Snapshot(int lineCount)
        {
            var result = new List<string>();
            lock (sync)
            {
                foreach (string line in recent) result.Add(line);
            }
            if (lineCount > 0 && result.Count > lineCount)
                result.RemoveRange(0, result.Count - lineCount);
            return result;
        }

        /// <summary>
        /// 一份可以直接贴到群里 / issue 里的诊断包。路径里的用户名会被替换成
        /// %USERPROFILE%，这是要发给别人看的东西。
        /// </summary>
        public static string BuildReport(AppSettings settings,
            MonitorCapabilities monitor, string targetDevices, bool filterRunning)
        {
            var builder = new StringBuilder();
            Append(builder, "程序", VersionText() + " · " +
                (Environment.Is64BitOperatingSystem ? "64 位系统" : "32 位系统") +
                (Environment.Is64BitProcess ? " · 64 位进程" : " · 32 位进程"));
            Append(builder, "系统", Environment.OSVersion.Version.ToString() +
                " (" + Environment.OSVersion.VersionString + ")");
            Append(builder, ".NET", Environment.Version.ToString());
            Append(builder, "已运行", Math.Round(
                (DateTime.UtcNow - startedAtUtc).TotalSeconds).ToString(CultureInfo.InvariantCulture) +
                " 秒");
            Append(builder, "显示器", ScreenText());
            if (monitor != null)
            {
                Append(builder, "DDC/CI", (monitor.DdcCiAvailable ? "可用" : "不可用") +
                    " · 亮度 " + (monitor.BrightnessSupported ? monitor.Brightness + "/" +
                    monitor.BrightnessMaximum : "不支持") +
                    " · 对比度 " + (monitor.ContrastSupported ? monitor.Contrast + "/" +
                    monitor.ContrastMaximum : "不支持"));
                Append(builder, "DDC 说明", monitor.Detail);
            }
            Append(builder, "生效显示器", targetDevices);
            Append(builder, "滤镜状态", filterRunning ? "已应用" : "未应用");
            if (settings != null)
            {
                Append(builder, "配置版本", "AlgorithmVersion " + settings.AlgorithmVersion);
                Append(builder, "参数", "暗部 " + settings.ShadowTarget +
                    " · 高光 " + settings.HighlightProtection +
                    " · 强度 " + settings.MaxStrength +
                    " · 色彩 " + settings.ColorCorrection +
                    " · 室内 " + settings.IndoorComfort +
                    " · 场景保护 " + settings.SceneGuard +
                    " · 黑位 " + settings.BlackPoint);
                Append(builder, "附加调色", "曝光 " + settings.ExposureBias +
                    " · 对比 " + settings.ContrastBias +
                    " · 色温 " + settings.Warmth +
                    " · 饱和 " + settings.SaturationBias +
                    " · 聚焦 " + (settings.FocusEnabled ? "开" : "关") +
                    "(" + settings.FocusHue + "°/" + settings.FocusStrength + ")" +
                    " · 臂环 " + (settings.ArmbandEnabled ?
                        "开(" + settings.ArmbandLift + "/" + settings.ArmbandDesaturate + ")" : "关"));
                Append(builder, "档案", "跟随游戏 " + (settings.ProfileFollowGame ? "开" : "关") +
                    " · 锁定档 " + AppSettings.GetProfileLabel(settings.LockedProfileKey));
                Append(builder, "实时", (settings.RealtimeEnabled ? "开" : "关") +
                    " · 间隔 " + settings.RealtimeIntervalMs + "ms" +
                    " · 灵敏度 " + settings.RealtimeSensitivity);
                Append(builder, "截图目录", Redact(string.Join(" | ",
                    (settings.ScreenshotFolders ?? new List<string>()).ToArray())));
                Append(builder, "侦听进程", string.Join(" | ",
                    (settings.WatchedProcessNames ?? new List<string>()).ToArray()));
                Append(builder, "热键", "VK " + settings.HotkeyKeyCode +
                    " + 修饰键 " + settings.HotkeyModifiers);
                Append(builder, "平滑过渡", settings.SmoothTransition ? "开" : "关");
            }
            Append(builder, "日志文件", fileUnavailable ? "当前不可写，仅有内存记录" :
                Redact(FilePath));

            builder.AppendLine();
            builder.AppendLine("---- 最近记录 ----");
            foreach (string line in Snapshot(200)) builder.AppendLine(Redact(line));
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string label, string value)
        {
            builder.Append(label).Append('：').AppendLine(value ?? "");
        }

        private static string VersionText()
        {
            try
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "未知版本" : version.ToString();
            }
            catch
            {
                return "未知版本";
            }
        }

        private static string ScreenText()
        {
            try
            {
                var builder = new StringBuilder();
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (builder.Length > 0) builder.Append(" ｜ ");
                    builder.Append(screen.DeviceName.Replace(@"\\.\", "")).Append(' ')
                        .Append(screen.Bounds.Width).Append('x').Append(screen.Bounds.Height)
                        .Append('@').Append(screen.Bounds.Left).Append(',')
                        .Append(screen.Bounds.Top)
                        .Append(screen.Primary ? "（主）" : "（副）");
                }
                return builder.Length == 0 ? "未检测到显示器" : builder.ToString();
            }
            catch (Exception error)
            {
                return "读取失败 " + error.GetType().Name;
            }
        }

        private static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            string profile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile)) return value;
            // 这是要发给别人看的东西，别把用户名带出去。
            return value.Replace(profile, "%USERPROFILE%");
        }
    }
}
