using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TarkovAutoShadePlus
{
    internal sealed class GammaRampController : IDisposable
    {
        private const int TransitionIntervalMilliseconds = 40;
        // 步数太少时，一次跨场景切换会被切成肉眼可见的几级台阶。
        // 因此先按目标时长反推 tick 间隔，并给一个步数下限。
        private const int MinimumTransitionSteps = 10;
        private readonly object sync = new object();
        private readonly Dictionary<string, GammaRamp> baselines =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GammaRamp> currentRamps =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeDevices =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private System.Threading.Timer transitionTimer;
        private int transitionIntervalMilliseconds;
        private double transitionDurationMilliseconds;
        private Stopwatch transitionClock;
        private readonly Dictionary<string, GammaRamp> transitionFromRamps =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GammaRamp> transitionToRamps =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private bool hasTransition;
        private bool disposed;

        public event Action<string> TransitionFailed;

        public bool HasActiveFilter { get { return activeDevices.Count > 0 || hasTransition; } }

        public static List<DisplayTarget> EnumerateDisplays()
        {
            var result = new List<DisplayTarget>();
            foreach (Screen screen in Screen.AllScreens)
            {
                result.Add(new DisplayTarget {
                    DeviceName = screen.DeviceName,
                    FriendlyName = screen.DeviceName.Replace(@"\\.\", ""),
                    Primary = screen.Primary
                });
            }
            return result;
        }

        public bool CaptureBaseline(string deviceName, out string error)
        {
            error = "";
            lock (sync)
            {
                if (baselines.ContainsKey(deviceName)) return true;
                GammaRamp ramp;
                if (!TryGet(deviceName, out ramp, out error)) return false;
                baselines[deviceName] = ramp.Clone();
                return true;
            }
        }

        public bool CaptureBaselines(IList<string> deviceNames, out string error)
        {
            error = "";
            if (deviceNames == null || deviceNames.Count == 0)
            {
                error = "没有选择显示器。";
                return false;
            }
            foreach (string deviceName in deviceNames)
            {
                if (string.IsNullOrWhiteSpace(deviceName) ||
                    !CaptureBaseline(deviceName, out error))
                    return false;
            }
            return true;
        }

        public bool TransitionTo(
            string deviceName,
            FilterRecommendation recommendation,
            int durationMilliseconds,
            out string error)
        {
            var devices = new List<string>();
            devices.Add(deviceName);
            return TransitionTo(devices, recommendation, durationMilliseconds, out error);
        }

        public bool TransitionTo(
            IList<string> deviceNames,
            FilterRecommendation recommendation,
            int durationMilliseconds,
            out string error)
        {
            error = "";
            if (recommendation == null)
            {
                error = "没有可应用的滤镜建议。";
                return false;
            }
            if (deviceNames == null || deviceNames.Count == 0)
            {
                error = "没有选择显示器。";
                return false;
            }

            lock (sync)
            {
                if (!CaptureBaselines(deviceNames, out error)) return false;

                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string deviceName in deviceNames)
                {
                    if (!string.IsNullOrWhiteSpace(deviceName)) targets.Add(deviceName);
                }
                if (targets.Count == 0)
                {
                    error = "没有选择显示器。";
                    return false;
                }

                var requestedRamps = new Dictionary<string, GammaRamp>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string deviceName in targets)
                    requestedRamps[deviceName] = GammaRamp.FromRecommendation(recommendation);

                bool sameTargets = activeDevices.Count == targets.Count;
                if (sameTargets)
                {
                    foreach (string deviceName in targets)
                    {
                        if (!activeDevices.Contains(deviceName))
                        {
                            sameTargets = false;
                            break;
                        }
                    }
                }
                if (sameTargets)
                {
                    bool sameDestination = true;
                    foreach (string deviceName in targets)
                    {
                        GammaRamp existing;
                        if (hasTransition)
                            sameDestination = transitionToRamps.TryGetValue(
                                deviceName, out existing) &&
                                RampsEqual(existing, requestedRamps[deviceName]);
                        else
                            sameDestination = currentRamps.TryGetValue(
                                deviceName, out existing) &&
                                RampsEqual(existing, requestedRamps[deviceName]);
                        if (!sameDestination) break;
                    }
                    if (sameDestination) return true;
                }

                StopTransition();

                foreach (string oldDevice in new List<string>(activeDevices))
                {
                    if (!targets.Contains(oldDevice)) RestoreInternal(oldDevice);
                }

                var immediateRamps = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
                foreach (string deviceName in targets)
                {
                    GammaRamp target = requestedRamps[deviceName];
                    immediateRamps[deviceName] = target;
                    GammaRamp from;
                    if (!activeDevices.Contains(deviceName) ||
                        !currentRamps.TryGetValue(deviceName, out from))
                        from = baselines[deviceName].Clone();
                    transitionFromRamps[deviceName] = from.Clone();
                    transitionToRamps[deviceName] = target.Clone();
                }

                activeDevices.Clear();
                foreach (string deviceName in targets) activeDevices.Add(deviceName);
                if (durationMilliseconds <= 0)
                {
                    var appliedDevices = new List<string>();
                    foreach (KeyValuePair<string, GammaRamp> item in immediateRamps)
                    {
                        if (!TrySet(item.Key, item.Value, out error))
                        {
                            foreach (string appliedDevice in appliedDevices)
                                RestoreInternal(appliedDevice);
                            activeDevices.Clear();
                            currentRamps.Clear();
                            return false;
                        }
                        currentRamps[item.Key] = item.Value.Clone();
                        appliedDevices.Add(item.Key);
                    }
                    return true;
                }

                int steps = Math.Max(MinimumTransitionSteps,
                    (int)Math.Round(
                        durationMilliseconds / (double)TransitionIntervalMilliseconds));
                transitionIntervalMilliseconds = Math.Max(16,
                    (int)Math.Round(durationMilliseconds / (double)steps));
                transitionDurationMilliseconds = Math.Max(1.0, durationMilliseconds);
                transitionClock = Stopwatch.StartNew();
                hasTransition = true;
                transitionTimer = new System.Threading.Timer(delegate {
                    TickTransition();
                }, null, 0, transitionIntervalMilliseconds);
                return true;
            }
        }

        public bool Reapply(out string error)
        {
            lock (sync)
            {
                error = "";
                bool success = true;
                foreach (string deviceName in activeDevices)
                {
                    GammaRamp ramp;
                    string itemError = "";
                    if (!currentRamps.TryGetValue(deviceName, out ramp) ||
                        !TrySet(deviceName, ramp, out itemError))
                    {
                        success = false;
                        if (error.Length == 0) error = itemError;
                    }
                }
                return success;
            }
        }

        public bool RestoreAll(out string error)
        {
            List<string> missingDevices = null;
            lock (sync)
            {
                StopTransition();
                error = "";
                bool success = true;
                var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (Screen screen in Screen.AllScreens)
                        connected.Add(screen.DeviceName);
                }
                catch { }
                foreach (KeyValuePair<string, GammaRamp> item in baselines)
                {
                    // 已拔除的显示器不再视为恢复失败，避免换屏后误报。
                    if (connected.Count > 0 && !connected.Contains(item.Key))
                    {
                        if (missingDevices == null) missingDevices = new List<string>();
                        missingDevices.Add(item.Key);
                        continue;
                    }
                    string itemError;
                    if (!TrySet(item.Key, item.Value, out itemError))
                    {
                        success = false;
                        if (error.Length == 0) error = itemError;
                    }
                }
                if (missingDevices != null)
                {
                    foreach (string stale in missingDevices) baselines.Remove(stale);
                }
                activeDevices.Clear();
                currentRamps.Clear();
                return success;
            }
        }

        public bool RestoreActive(out string error)
        {
            lock (sync)
            {
                StopTransition();
                error = "";
                bool success = true;
                foreach (string deviceName in new List<string>(activeDevices))
                {
                    GammaRamp baseline;
                    string itemError = "";
                    if (!baselines.TryGetValue(deviceName, out baseline) ||
                        !TrySet(deviceName, baseline, out itemError))
                    {
                        success = false;
                        if (error.Length == 0) error = itemError;
                    }
                }
                activeDevices.Clear();
                currentRamps.Clear();
                return success;
            }
        }

        public GammaRamp? GetBaseline(string deviceName)
        {
            lock (sync)
            {
                GammaRamp ramp;
                return baselines.TryGetValue(deviceName, out ramp) ?
                    (GammaRamp?)ramp.Clone() : null;
            }
        }

        public void PruneBaselines(IList<string> validDevices)
        {
            if (validDevices == null) return;
            lock (sync)
            {
                var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string device in validDevices)
                {
                    if (!string.IsNullOrWhiteSpace(device)) valid.Add(device);
                }
                var stale = new List<string>();
                foreach (string device in baselines.Keys)
                {
                    if (!valid.Contains(device)) stale.Add(device);
                }
                foreach (string device in stale) baselines.Remove(device);
            }
        }

        public bool ApplyDirect(string deviceName, GammaRamp ramp, out string error)
        {
            lock (sync)
            {
                return TrySet(deviceName, ramp, out error);
            }
        }

        public bool ApplyDirect(IDictionary<string, GammaRamp> ramps, out string error)
        {
            error = "";
            if (ramps == null || ramps.Count == 0)
            {
                error = "没有可恢复的显示器曲线。";
                return false;
            }
            lock (sync)
            {
                bool success = true;
                foreach (KeyValuePair<string, GammaRamp> item in ramps)
                {
                    string itemError;
                    if (!TrySet(item.Key, item.Value, out itemError))
                    {
                        success = false;
                        if (error.Length == 0) error = itemError;
                    }
                }
                return success;
            }
        }

        public bool Probe(string deviceName, out string error)
        {
            GammaRamp ignored;
            return TryGet(deviceName, out ignored, out error);
        }

        private void TickTransition()
        {
            string failedDevice = null;
            string failedError = "";
            lock (sync)
            {
                if (transitionTimer == null || !hasTransition)
                    return;

                // 用经过时间而不是 tick 计数来求进度：系统定时器会抖动，
                // 按计数推进会让节奏忽快忽慢，甚至首帧就跳到 1/steps。
                double amount = transitionClock == null ? 1.0 :
                    MathUtil.Clamp(transitionClock.Elapsed.TotalMilliseconds /
                        transitionDurationMilliseconds, 0.0, 1.0);
                amount = amount * amount * (3.0 - 2.0 * amount);
                foreach (KeyValuePair<string, GammaRamp> item in transitionToRamps)
                {
                    GammaRamp ramp = GammaRamp.Lerp(
                        transitionFromRamps[item.Key], item.Value, amount);
                    string setError;
                    if (!TrySet(item.Key, ramp, out setError))
                    {
                        failedDevice = item.Key;
                        failedError = setError;
                        StopTransition();
                        break;
                    }
                    currentRamps[item.Key] = ramp;
                }

                if (failedDevice == null && amount >= 1.0)
                {
                    foreach (KeyValuePair<string, GammaRamp> item in transitionToRamps)
                        currentRamps[item.Key] = item.Value.Clone();
                    StopTransition();
                }
            }
            if (failedDevice != null)
            {
                Action<string> handler = TransitionFailed;
                if (handler != null)
                {
                    try { handler(failedDevice + "：" + failedError); }
                    catch { }
                }
            }
        }

        private bool RestoreInternal(string deviceName)
        {
            GammaRamp baseline;
            if (!baselines.TryGetValue(deviceName, out baseline)) return true;
            string ignored;
            return TrySet(deviceName, baseline, out ignored);
        }

        private static bool RampsEqual(GammaRamp left, GammaRamp right)
        {
            if (left.Red == null || left.Green == null || left.Blue == null ||
                right.Red == null || right.Green == null || right.Blue == null)
                return false;
            for (int i = 0; i < 256; i++)
            {
                if (left.Red[i] != right.Red[i] ||
                    left.Green[i] != right.Green[i] ||
                    left.Blue[i] != right.Blue[i]) return false;
            }
            return true;
        }

        private void StopTransition()
        {
            if (transitionTimer != null)
            {
                transitionTimer.Dispose();
                transitionTimer = null;
            }
            if (transitionClock != null)
            {
                transitionClock.Stop();
                transitionClock = null;
            }
            hasTransition = false;
            transitionFromRamps.Clear();
            transitionToRamps.Clear();
        }

        private static bool TryGet(string deviceName, out GammaRamp ramp, out string error)
        {
            ramp = GammaRamp.CreateEmpty();
            error = "";
            IntPtr dc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                error = "无法创建设备上下文：" + deviceName;
                return false;
            }
            try
            {
                var value = GammaRamp.CreateEmpty();
                if (!GetDeviceGammaRamp(dc, ref value))
                {
                    error = "读取系统 Gamma 失败（错误 " +
                        Marshal.GetLastWin32Error().ToString() + "）。";
                    Diagnostics.Throttled("Gamma", "get-fail:" + deviceName,
                        deviceName + " " + error, TimeSpan.FromSeconds(10));
                    return false;
                }
                ramp = value;
                return true;
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        private static bool TrySet(
            string deviceName, GammaRamp ramp, out string error)
        {
            error = "";
            IntPtr dc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                error = "无法访问显示器：" + deviceName;
                Diagnostics.Throttled("Gamma", "dc-fail:" + deviceName,
                    error + "（该显示器可能已断开或刚换过驱动）", TimeSpan.FromSeconds(10));
                return false;
            }
            try
            {
                GammaRamp copy = ramp.Clone();
                if (!SetDeviceGammaRamp(dc, ref copy))
                {
                    error = "应用 Gamma 曲线失败。请关闭 HDR，并检查显卡色彩设置。";
                    Diagnostics.Throttled("Gamma", "set-fail:" + deviceName,
                        deviceName + " " + error, TimeSpan.FromSeconds(5));
                    return false;
                }
                return true;
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            string ignored;
            RestoreAll(out ignored);
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDC(
            string driver, string device, string output, IntPtr initData);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool GetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool SetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;

        public static GammaRamp CreateEmpty()
        {
            return new GammaRamp {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };
        }

        public static GammaRamp FromRecommendation(FilterRecommendation recommendation)
        {
            return new GammaRamp {
                Red = (ushort[])recommendation.Red.Clone(),
                Green = (ushort[])recommendation.Green.Clone(),
                Blue = (ushort[])recommendation.Blue.Clone()
            };
        }

        public GammaRamp Clone()
        {
            return new GammaRamp {
                Red = Red == null ? new ushort[256] : (ushort[])Red.Clone(),
                Green = Green == null ? new ushort[256] : (ushort[])Green.Clone(),
                Blue = Blue == null ? new ushort[256] : (ushort[])Blue.Clone()
            };
        }

        public static GammaRamp Lerp(GammaRamp from, GammaRamp to, double amount)
        {
            GammaRamp result = CreateEmpty();
            for (int i = 0; i < 256; i++)
            {
                result.Red[i] = Interpolate(from.Red[i], to.Red[i], amount);
                result.Green[i] = Interpolate(from.Green[i], to.Green[i], amount);
                result.Blue[i] = Interpolate(from.Blue[i], to.Blue[i], amount);
            }
            return result;
        }

        private static ushort Interpolate(ushort from, ushort to, double amount)
        {
            return (ushort)Math.Round(from + (to - from) * amount);
        }
    }
}
