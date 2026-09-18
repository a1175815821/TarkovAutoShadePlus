using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace TarkovAutoShadePlus
{
    internal sealed class RealtimeFrame : IDisposable
    {
        public AnalysisResult Analysis;
        public Bitmap OriginalPreview;
        public Bitmap FilteredPreview;

        public void Dispose()
        {
            if (OriginalPreview != null) OriginalPreview.Dispose();
            if (FilteredPreview != null) FilteredPreview.Dispose();
            OriginalPreview = null;
            FilteredPreview = null;
        }
    }

    internal sealed class RealtimeController : IDisposable
    {
        private readonly object sync = new object();
        private CancellationTokenSource lifetime;
        private Task loop;
        private readonly ManualResetEventSlim wakeSignal = new ManualResetEventSlim(false);
        private bool disposed;

        // 自适应时间平滑：误差接近噪声底时用长时间常数压抖动，
        // 误差达到真实场景切换量级时切换到短时间常数快速跟上。
        private const double EmaNoiseFloor = 0.015;
        private const double EmaBigJump = 0.12;
        private const double EmaSlowTauSeconds = 9.0;
        private const double EmaFastTauSeconds = 0.7;

        private AnalysisResult lastApplied;
        private AnalysisResult lastAppliedRaw;
        private AnalysisResult smoothedScene;
        private bool hasSmoothedScene;
        private DateTime lastSampleUtc = DateTime.MinValue;
        private AnalysisResult pendingCandidate;
        private int pendingRepeat;
        private int stableSamples;
        private int captureFailures;
        private DateTime lastApplyUtc = DateTime.MinValue;
        private string lastStatus = "";
        private string lastFault = "";
        private DateTime lastFaultUtc = DateTime.MinValue;

        public Func<AppSettings> AcquireSettings;
        public Func<List<string>> AcquireTargets;
        public Func<bool> IsGameForeground;
        public Func<bool> IsManualFilterOn;
        public Func<bool> IsClosing;

        public Action<RealtimeFrame> StableScene;
        public Action<string> StatusChanged;
        public Action<string> Faulted;

        public void Start()
        {
            lock (sync)
            {
                if (disposed || loop != null) return;
                lifetime = new CancellationTokenSource();
                CancellationToken token = lifetime.Token;
                loop = Task.Factory.StartNew(delegate { Run(token); },
                    token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        public void Wake()
        {
            try { wakeSignal.Set(); }
            catch { }
        }

        private void Run(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int sleepMs = 1200;
                try
                {
                    sleepMs = Tick(token);
                }
                catch { sleepMs = 1200; }
                if (token.IsCancellationRequested) break;
                try
                {
                    wakeSignal.Reset();
                    if (WaitHandle.WaitAny(
                        new WaitHandle[] { token.WaitHandle, wakeSignal.WaitHandle },
                        Math.Max(200, sleepMs)) != WaitHandle.WaitTimeout)
                    {
                        // Woken early by settings change; resample immediately.
                    }
                }
                catch { break; }
            }
        }

        private int Tick(CancellationToken token)
        {
            Func<AppSettings> acquireSettings = AcquireSettings;
            AppSettings settings = acquireSettings == null ? null : acquireSettings();
            if (settings == null) settings = AppSettings.CreateDefault();
            int interval = MathUtil.Clamp(settings.RealtimeIntervalMs, 600, 3000);

            if (IsClosing != null)
            {
                try { if (IsClosing()) return interval; }
                catch { }
            }
            if (!settings.RealtimeEnabled)
            {
                ResetSceneState();
                SetStatus("未启用");
                return interval;
            }
            try
            {
                if (IsManualFilterOn != null && !IsManualFilterOn())
                {
                    SetStatus("已手动关闭");
                    return interval;
                }
            }
            catch { }
            try
            {
                if (IsGameForeground != null && !IsGameForeground())
                {
                    ResetSceneState();
                    SetStatus("等待游戏前台");
                    return interval;
                }
            }
            catch { }

            Func<List<string>> acquireTargets = AcquireTargets;
            List<string> targets = acquireTargets == null ?
                new List<string>() : acquireTargets();
            string captureDevice = RealtimeCapture.ResolveCaptureDevice(targets);
            if (string.IsNullOrWhiteSpace(captureDevice))
            {
                SetStatus("没有可用的显示器");
                return interval;
            }

            string captureError;
            using (Bitmap frame = RealtimeCapture.CaptureDisplay(captureDevice, out captureError))
            {
                if (token.IsCancellationRequested) return interval;
                if (frame == null)
                {
                    captureFailures++;
                    SetStatus("抓屏失败");
                    ReportFault("实时抓屏失败：" + captureError);
                    return Math.Min(4000, interval + captureFailures * 500);
                }
                captureFailures = 0;

                AnalysisResult raw;
                try
                {
                    raw = ImageAnalyzer.MeasureFrame(frame, "realtime:" + captureDevice);
                }
                catch (Exception ex)
                {
                    ReportFault("实时分析失败：" + ex.Message);
                    return interval;
                }
                if (raw == null) return interval;
                // 先对原始帧分类一次，只为拿到夜视分数。护眼判定不能走平滑值，
                // 否则进入夜视的反应会被时间常数拖慢一两个采样周期。
                ImageAnalyzer.Complete(raw, settings);

                // 单帧分位数在运动画面上抖动有几个百分点，而调光曲线里到处是
                // SmoothStep，恰好在过渡区间导数最大——这点噪声会被放大成亮度
                // 来回摆。先对统计量做自适应时间平滑，再交给调光曲线。
                AnalysisResult current = SmoothScene(raw);
                ImageAnalyzer.Complete(current, settings);
                if (current == null || !current.IsUsable || current.Recommendation == null)
                    return interval;

                double threshold;
                int needStable;
                int cooldownMs;
                GetGate(settings.RealtimeSensitivity, out threshold, out needStable, out cooldownMs);

                if (lastApplied == null)
                {
                    Emit(current, frame);
                    RecordApplied(current, raw);
                    SetStatus("切换中");
                    return interval;
                }

                bool nightCross = lastAppliedRaw != null &&
                    IsNightVisionCross(lastAppliedRaw, raw);
                double delta = Delta(lastApplied, current);
                bool profileSwitch = !string.Equals(
                    lastApplied.Recommendation == null ? "" : lastApplied.Recommendation.ProfileName,
                    current.Recommendation == null ? "" : current.Recommendation.ProfileName,
                    StringComparison.Ordinal);
                bool flashSuspect = current.P95 - lastApplied.P95 > 0.25 &&
                    Math.Abs(current.Median - lastApplied.Median) < 0.03;
                int required = needStable + (flashSuspect ? 1 : 0);

                if (nightCross)
                {
                    // 夜视切换优先保护眼睛，不等稳定确认。
                    Emit(current, frame);
                    RecordApplied(current, raw);
                    SetStatus("切换中");
                    return interval;
                }

                if ((delta < threshold && !profileSwitch) || flashSuspect && delta < threshold * 1.6)
                {
                    stableSamples++;
                    pendingCandidate = null;
                    pendingRepeat = 0;
                    SetStatus("稳定");
                    // 稳定时略微降频即可，别退到 4 秒——那样下次场景切换
                    // 要等太久才开始跟，观感就是"迟钝之后猛跳"。
                    if (stableSamples >= 10) return Math.Min(2500, interval + interval / 2);
                    return interval;
                }

                if (pendingCandidate == null || Delta(pendingCandidate, current) > threshold * 0.5)
                {
                    pendingCandidate = CloneForCompare(current);
                    pendingRepeat = 1;
                    SetStatus("场景确认中");
                    return interval;
                }
                pendingRepeat++;
                if (pendingRepeat >= required &&
                    (DateTime.UtcNow - lastApplyUtc).TotalMilliseconds >= cooldownMs)
                {
                    Emit(current, frame);
                    RecordApplied(current, raw);
                    SetStatus("切换中");
                }
                else
                {
                    SetStatus("场景确认中");
                }
                return interval;
            }
        }

        private void Emit(AnalysisResult analysis, Bitmap frame)
        {
            Action<RealtimeFrame> handler = StableScene;
            if (handler == null) return;
            // analysis 是逐帧被改写的长期平滑对象，交给 UI 前必须快照，
            // 否则界面上的读数会跟着后续采样继续漂移。
            var package = new RealtimeFrame { Analysis = CloneForCompare(analysis) };
            try
            {
                package.OriginalPreview = ImageAnalyzer.BuildOriginalPreview(frame);
                if (analysis.IsUsable && analysis.Recommendation != null)
                    package.FilteredPreview = ImageAnalyzer.BuildPreview(frame, analysis.Recommendation);
            }
            catch
            {
                package.Dispose();
                return;
            }
            try { handler(package); }
            catch { package.Dispose(); }
        }

        private void RecordApplied(AnalysisResult applied, AnalysisResult rawFrame)
        {
            lastApplied = CloneForCompare(applied);
            lastAppliedRaw = rawFrame == null ? null : CloneForCompare(rawFrame);
            pendingCandidate = null;
            pendingRepeat = 0;
            stableSamples = 0;
            lastApplyUtc = DateTime.UtcNow;
        }

        private AnalysisResult SmoothScene(AnalysisResult raw)
        {
            DateTime now = DateTime.UtcNow;
            if (!hasSmoothedScene || smoothedScene == null)
            {
                smoothedScene = CloneForCompare(raw);
                hasSmoothedScene = true;
                lastSampleUtc = now;
                return smoothedScene;
            }

            double elapsed = (now - lastSampleUtc).TotalSeconds;
            if (elapsed < 0.05) elapsed = 0.05;
            if (elapsed > 6.0) elapsed = 6.0;
            lastSampleUtc = now;

            double error = Math.Max(
                Math.Abs(raw.Median - smoothedScene.Median),
                Math.Max(
                    Math.Abs(raw.P95 - smoothedScene.P95),
                    Math.Abs(raw.P10 - smoothedScene.P10)));
            double urgency = MathUtil.SmoothStep(EmaNoiseFloor, EmaBigJump, error);
            double tau = MathUtil.Lerp(EmaSlowTauSeconds, EmaFastTauSeconds, urgency);
            double alpha = 1.0 - Math.Exp(-elapsed / tau);

            smoothedScene.P01 = Blend(smoothedScene.P01, raw.P01, alpha);
            smoothedScene.P05 = Blend(smoothedScene.P05, raw.P05, alpha);
            smoothedScene.P10 = Blend(smoothedScene.P10, raw.P10, alpha);
            smoothedScene.P25 = Blend(smoothedScene.P25, raw.P25, alpha);
            smoothedScene.Median = Blend(smoothedScene.Median, raw.Median, alpha);
            smoothedScene.P75 = Blend(smoothedScene.P75, raw.P75, alpha);
            smoothedScene.P90 = Blend(smoothedScene.P90, raw.P90, alpha);
            smoothedScene.P95 = Blend(smoothedScene.P95, raw.P95, alpha);
            smoothedScene.P99 = Blend(smoothedScene.P99, raw.P99, alpha);
            smoothedScene.EdgeEnergy = Blend(smoothedScene.EdgeEnergy, raw.EdgeEnergy, alpha);
            smoothedScene.MeanRed = Blend(smoothedScene.MeanRed, raw.MeanRed, alpha);
            smoothedScene.MeanGreen = Blend(smoothedScene.MeanGreen, raw.MeanGreen, alpha);
            smoothedScene.MeanBlue = Blend(smoothedScene.MeanBlue, raw.MeanBlue, alpha);
            smoothedScene.UpperMean = Blend(smoothedScene.UpperMean, raw.UpperMean, alpha);
            smoothedScene.LowerMean = Blend(smoothedScene.LowerMean, raw.LowerMean, alpha);
            smoothedScene.BrightFraction =
                Blend(smoothedScene.BrightFraction, raw.BrightFraction, alpha);
            smoothedScene.DynamicRange = smoothedScene.P95 - smoothedScene.P05;
            smoothedScene.FilePath = raw.FilePath;
            smoothedScene.CapturedAt = raw.CapturedAt;
            smoothedScene.Histogram = raw.Histogram;
            return smoothedScene;
        }

        private static double Blend(double from, double to, double alpha)
        {
            return from + (to - from) * alpha;
        }

        private static void GetGate(int sensitivity, out double threshold, out int needStable, out int cooldownMs)
        {
            // 自适应平滑已经先压掉了单帧噪声，这里不再需要靠多次确认防抖，
            // 确认次数与冷却整体下调一档，把反应延迟还回来。
            if (sensitivity <= 0) { threshold = 9.0; needStable = 2; cooldownMs = 2200; }
            else if (sensitivity >= 2) { threshold = 2.5; needStable = 1; cooldownMs = 800; }
            else { threshold = 5.0; needStable = 1; cooldownMs = 1200; }
        }

        private static double Delta(AnalysisResult from, AnalysisResult to)
        {
            if (from == null || to == null) return double.MaxValue;
            if (from.Recommendation == null || to.Recommendation == null) return double.MaxValue;
            double dBright = Math.Abs(to.Recommendation.BrightnessBoost - from.Recommendation.BrightnessBoost);
            double dGamma = Math.Abs(to.Recommendation.EquivalentGamma - from.Recommendation.EquivalentGamma) * 40.0;
            double dContrast = Math.Abs(to.Recommendation.ContrastBoost - from.Recommendation.ContrastBoost) * 0.8;
            return dBright + dGamma + dContrast;
        }

        private static bool IsNightVisionCross(AnalysisResult from, AnalysisResult to)
        {
            if (from == null || to == null) return false;
            return (from.NightVisionScore > 0.42) != (to.NightVisionScore > 0.42);
        }

        private static AnalysisResult CloneForCompare(AnalysisResult source)
        {
            var copy = new AnalysisResult {
                FilePath = source.FilePath,
                CapturedAt = source.CapturedAt,
                P01 = source.P01,
                P05 = source.P05,
                P10 = source.P10,
                P25 = source.P25,
                Median = source.Median,
                P75 = source.P75,
                P90 = source.P90,
                P95 = source.P95,
                P99 = source.P99,
                DynamicRange = source.DynamicRange,
                EdgeEnergy = source.EdgeEnergy,
                MeanRed = source.MeanRed,
                MeanGreen = source.MeanGreen,
                MeanBlue = source.MeanBlue,
                NightVisionScore = source.NightVisionScore,
                RedCast = source.RedCast,
                GreenCast = source.GreenCast,
                BlueCast = source.BlueCast,
                UpperMean = source.UpperMean,
                LowerMean = source.LowerMean,
                BrightFraction = source.BrightFraction,
                IsUsable = source.IsUsable,
                SkipReason = source.SkipReason,
                SceneLabel = source.SceneLabel,
                Histogram = source.Histogram
            };
            copy.Recommendation = source.Recommendation;
            return copy;
        }

        // 切档案时必须调用：否则 EMA 会带着上一个游戏的旧场景慢慢收敛，
        // 表现为切过去之后好几秒亮度才到位。
        public void ResetSceneState()
        {
            lastApplied = null;
            lastAppliedRaw = null;
            // 平滑状态一并丢弃：回到游戏时直接以当前帧重新起算，
            // 免得背着切出去之前的旧场景慢慢收敛。
            smoothedScene = null;
            hasSmoothedScene = false;
            pendingCandidate = null;
            pendingRepeat = 0;
            stableSamples = 0;
        }

        private void SetStatus(string status)
        {
            lock (sync)
            {
                if (string.Equals(lastStatus, status, StringComparison.Ordinal)) return;
                lastStatus = status;
            }
            Action<string> handler = StatusChanged;
            if (handler == null) return;
            try { handler(status); }
            catch { }
        }

        private void ReportFault(string message)
        {
            lock (sync)
            {
                DateTime now = DateTime.UtcNow;
                if (string.Equals(lastFault, message, StringComparison.Ordinal) &&
                    (now - lastFaultUtc).TotalSeconds < 15.0) return;
                lastFault = message;
                lastFaultUtc = now;
            }
            Action<string> handler = Faulted;
            if (handler == null) return;
            try { handler(message); }
            catch { }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (lifetime != null) lifetime.Cancel();
                }
                catch { }
            }
            try { wakeSignal.Set(); }
            catch { }
            try
            {
                if (loop != null) loop.Wait(3000);
            }
            catch { }
            try
            {
                if (lifetime != null) lifetime.Dispose();
            }
            catch { }
            try { wakeSignal.Dispose(); }
            catch { }
        }
    }
}
