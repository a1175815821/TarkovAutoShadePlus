using System;

namespace TarkovAutoShadePlus
{
    internal static class ToneCurve
    {
        public static FilterRecommendation Recommend(AnalysisResult result, AppSettings settings)
        {
            double visibility = settings.ShadowTarget / 100.0;
            double protection = settings.HighlightProtection / 100.0;
            double strength = settings.MaxStrength / 100.0;
            double exposureBias = settings.ExposureBias / 20.0;
            double contrastBias = settings.ContrastBias / 20.0;
            double colorCorrection = settings.ColorCorrection / 100.0;
            double saturationBias = settings.SaturationBias / 20.0;
            double indoorComfort = settings.IndoorComfort / 100.0;
            double sceneGuard = settings.SceneGuard / 100.0;
            double blackPointBias = (settings.BlackPoint - 50) / 50.0;

            double darkness = 1.0 - MathUtil.SmoothStep(0.10, 0.46, result.Median);
            double shadowDeficit = 1.0 - MathUtil.SmoothStep(0.055, 0.205, result.P10);
            double darkScore = MathUtil.Clamp(
                darkness * 0.68 + shadowDeficit * 0.32, 0.0, 1.0);
            double brightScore = MathUtil.SmoothStep(0.42, 0.66, result.Median);
            double backlightScore =
                MathUtil.SmoothStep(0.72, 0.94, result.P95) *
                (1.0 - MathUtil.SmoothStep(0.25, 0.50, result.Median));
            double daylightEvidence =
                MathUtil.SmoothStep(0.28, 0.58, result.P75) *
                MathUtil.SmoothStep(0.60, 0.90, result.P95);
            double spatialDaylightRaw =
                MathUtil.SmoothStep(0.10, 0.35, result.UpperMean) * 0.45 +
                MathUtil.SmoothStep(0.015, 0.165, result.BrightFraction) * 0.35 +
                MathUtil.SmoothStep(0.72, 0.94, result.P95) * 0.20;
            double spatialDaylight =
                MathUtil.SmoothStep(0.25, 0.55, spatialDaylightRaw);
            double outdoorEvidence = Math.Max(daylightEvidence, spatialDaylight);
            double brightSceneScore = MathUtil.Clamp(
                Math.Max(
                    brightScore,
                    MathUtil.SmoothStep(0.74, 0.94, result.P95) *
                    (0.45 + 0.55 * MathUtil.SmoothStep(0.35, 0.62, result.Median))),
                0.0,
                1.0);
            double indoorScore = MathUtil.Clamp(
                (1.0 - outdoorEvidence) *
                (0.35 + 0.65 * darkScore) *
                (1.0 - 0.35 * result.NightVisionScore),
                0.0,
                1.0);
            double protectionScore = MathUtil.Clamp(
                sceneGuard * (
                    0.78 * brightSceneScore +
                    0.92 * result.NightVisionScore +
                    0.52 * backlightScore),
                0.0,
                1.0);
            double comfortScore = indoorComfort * indoorScore;
            double flatScore = 1.0 -
                MathUtil.SmoothStep(0.14, 0.42, result.DynamicRange);
            double graySceneGate = MathUtil.SmoothStep(0.07, 0.24, result.Median);
            double automaticGrayRecovery = flatScore * graySceneGate *
                (0.018 + 0.030 * darkScore) *
                (1.0 - 0.35 * protectionScore);
            double blackPointRecovery = MathUtil.Clamp(
                automaticGrayRecovery + 0.045 * blackPointBias *
                (0.30 + 0.70 * graySceneGate),
                -0.035,
                0.090);
            double effectiveDarkScore = darkScore *
                (1.0 - 0.88 * outdoorEvidence) *
                (1.0 - 0.78 * protectionScore);
            double extremeDarkRecovery = 1.0 - MathUtil.SmoothStep(
                0.035, 0.12, result.Median);
            // A dark Tarkov interior often contains a small bright source or
            // sky opening. Recover more shadow detail only when the upper
            // tail is not already bright, so the new lift does not wash out
            // doors, windows, or lamps in the same frame.
            double darkDetailRecovery = extremeDarkRecovery *
                (1.0 - MathUtil.SmoothStep(0.48, 0.82, result.P95)) *
                (1.0 - 0.45 * protectionScore);

            // Calibrated against the user's proven presets:
            // outdoor day ~= gamma 1.30 / brightness 6 / contrast 4
            // dark interior ~= gamma 1.55 / brightness 55 / contrast 21
            double visibilityScale = 0.78 + visibility * 0.34;
            double equivalentGamma = 1.30 - 0.08 * brightScore +
                0.25 * effectiveDarkScore * visibilityScale;
            equivalentGamma += 0.04 * darkDetailRecovery * visibilityScale;
            double brightnessBoost = 6.0 * (1.0 - brightScore * 0.68) +
                49.0 * effectiveDarkScore * visibilityScale;
            // Keep the darkest playable interiors above the visibility floor
            // even when a sparse sample under-represents their shadow detail.
            brightnessBoost += 4.0 * extremeDarkRecovery * shadowDeficit;
            brightnessBoost += 5.0 * darkDetailRecovery * shadowDeficit;
            double contrastBoost = 4.0 + 17.0 * effectiveDarkScore +
                4.0 * flatScore - 3.0 * backlightScore;

            double exposureSceneScale = 0.45 + 0.55 * (1.0 - brightScore);
            double manualLiftGate = 1.0 - 0.82 * protectionScore;
            equivalentGamma += 0.07 * exposureBias * exposureSceneScale *
                manualLiftGate;
            brightnessBoost += 10.0 * exposureBias * exposureSceneScale *
                manualLiftGate;
            brightnessBoost *= 1.0 - 0.38 * comfortScore;
            brightnessBoost *= 1.0 - 0.88 * protectionScore;
            brightnessBoost -= 8.0 * protectionScore;
            contrastBoost += 7.0 * contrastBias;
            contrastBoost -= 8.0 * comfortScore;
            contrastBoost -= 3.0 * protectionScore;
            equivalentGamma -= 0.24 * protectionScore;
            equivalentGamma -= 0.055 * comfortScore;

            equivalentGamma = MathUtil.Clamp(equivalentGamma, 1.01, 1.65);
            brightnessBoost = MathUtil.Clamp(brightnessBoost, 0.0, 60.0);
            contrastBoost = MathUtil.Clamp(contrastBoost, 0.0, 25.0);

            double gamma = 1.0 / equivalentGamma;
            double shadowLift = brightnessBoost / 100.0 * 0.45;
            double contrast = contrastBoost / 100.0 * 0.45;
            double compression = protection *
                (0.055 + 0.20 * backlightScore + 0.17 * brightScore);
            compression += 0.16 * comfortScore + 0.24 * protectionScore;
            compression = MathUtil.Clamp(compression, 0.0, 0.48);
            double warmth = settings.Warmth / 20.0 * 0.020;

            double meanLuma = 0.2126 * result.MeanRed +
                0.7152 * result.MeanGreen + 0.0722 * result.MeanBlue;
            double correctionScale = 0.82 * colorCorrection;
            double redBalance = MathUtil.Clamp(
                (meanLuma - result.MeanRed) * correctionScale,
                -0.075,
                0.075);
            double greenBalance = MathUtil.Clamp(
                (meanLuma - result.MeanGreen) * correctionScale,
                -0.075,
                0.075);
            double blueBalance = MathUtil.Clamp(
                (meanLuma - result.MeanBlue) * correctionScale,
                -0.075,
                0.075);

            // Gamma ramps are per-channel, so this is a restrained saturation
            // control: it expands or contracts each channel's distance from
            // scene luminance without introducing a new hue on neutral scenes.
            double saturationShape = 0.42 * saturationBias;
            redBalance += (result.MeanRed - meanLuma) * saturationShape;
            greenBalance += (result.MeanGreen - meanLuma) * saturationShape;
            blueBalance += (result.MeanBlue - meanLuma) * saturationShape;
            redBalance = MathUtil.Clamp(redBalance, -0.075, 0.075);
            greenBalance = MathUtil.Clamp(greenBalance, -0.075, 0.075);
            blueBalance = MathUtil.Clamp(blueBalance, -0.075, 0.075);

            // ---- 色彩聚焦 ----
            // Gamma Ramp 只能按「通道 × 输入电平」改写，程序拿不到像素的邻居通道，
            // 因此做不到真正的 HSV 选区（"只把橙色物体提亮"）。
            // 这里做的是通道偏向式定向调色：把目标色相所在的通道在选定的亮度窗口
            // 里推高，同时把其余通道压回 —— 观感上就是目标色系变浓、互补色变淡。
            // 端点（纯黑 / 纯白）由窗口函数天然豁免，所以不会污染黑白场。
            // 总开关关掉时保留滑块数值但完全不参与计算，
            // 这样用户可以随时在「有 / 无」之间来回对比，不用把调好的参数清掉。
            double focusRed = 0.0;
            double focusGreen = 0.0;
            double focusBlue = 0.0;
            if (settings.FocusEnabled)
            {
                double focusStrength = settings.FocusStrength / 100.0;
                if (focusStrength > 0.0)
                {
                    double hueRed;
                    double hueGreen;
                    double hueBlue;
                    HueChannelWeights(settings.FocusHue,
                        out hueRed, out hueGreen, out hueBlue);
                    // 系数按「强度 100 时中间调约 ±30/255」定：改得动，但不至于
                    // 把画面压成单色。默认最大调整强度 0.82 会再统一缩放一次。
                    double lift = 0.085 * focusStrength;
                    focusRed += lift * hueRed;
                    focusGreen += lift * hueGreen;
                    focusBlue += lift * hueBlue;
                    double pushDown = 0.075 * focusStrength;
                    focusRed -= pushDown * (1.0 - hueRed);
                    focusGreen -= pushDown * (1.0 - hueGreen);
                    focusBlue -= pushDown * (1.0 - hueBlue);
                }

                // 手动调色：分通道直接偏移，与目标色相独立，方便自己校色。
                const double trimLift = 0.060 / AppSettings.TrimLimit;
                focusRed += settings.TrimRed * trimLift;
                focusGreen += settings.TrimGreen * trimLift;
                focusBlue += settings.TrimBlue * trimLift;
            }

            double focusCenter = 0.5 + 0.30 * (
                settings.FocusRange / (double)AppSettings.FocusRangeLimit);

            // ---- 臂环增强（红 / 蓝双色）----
            // 「双色强化」用 v² 作为权重：饱和的红蓝物体（v 高）被抬得最多，
            // 中性灰所在的中间调权重只有 0.25，所以对灰白的污染比线性提升小。
            // 「环境去色」压 G 通道的中间调，把橄榄 / 灌木 / 草地的绿分量拉掉。
            // 注意这仍然是通道级操作：G 被压多少，中性灰就朝品红偏多少，
            // 这个偏色在数学上无法和「选择性去色」分开，只能靠强度换取。
            double armbandRed = 0.0;
            double armbandBlue = 0.0;
            double armbandGreen = 0.0;
            if (settings.ArmbandEnabled)
            {
                double armbandLift = settings.ArmbandLift /
                    (double)AppSettings.ArmbandLiftLimit;
                double armbandDesaturate = settings.ArmbandDesaturate /
                    (double)AppSettings.ArmbandDesaturateLimit;
                armbandRed = 0.100 * armbandLift;
                armbandBlue = 0.100 * armbandLift;
                armbandGreen = -0.110 * armbandDesaturate;
            }

            var recommendation = new FilterRecommendation {
                ProfileName = GetProfileName(
                    result, effectiveDarkScore, brightScore,
                    backlightScore, outdoorEvidence, comfortScore),
                EquivalentGamma = equivalentGamma,
                BrightnessBoost = brightnessBoost,
                ContrastBoost = contrastBoost,
                StrengthBlend = strength,
                Gamma = gamma,
                ShadowLift = shadowLift,
                HighlightCompression = compression,
                BlackPointRecovery = blackPointRecovery,
                Contrast = contrast,
                Warmth = warmth,
                RedBalance = redBalance,
                GreenBalance = greenBalance,
                BlueBalance = blueBalance,
                FocusRed = focusRed,
                FocusGreen = focusGreen,
                FocusBlue = focusBlue,
                FocusCenter = focusCenter,
                ArmbandRed = armbandRed,
                ArmbandBlue = armbandBlue,
                ArmbandGreen = armbandGreen
            };
            BuildLuts(recommendation);
            return recommendation;
        }

        // 目标色相 -> 归一化 RGB 权重。用标准 HSV 取 S=V=1 的纯色，
        // 于是权重天然表达「这个色相里各通道占多少」：
        // 暖橙 (25°) = (1.00, 0.42, 0.00)，青蓝 (195°) = (0.00, 0.51, 1.00)。
        private static void HueChannelWeights(double hue,
            out double red, out double green, out double blue)
        {
            double normalized = hue % 360.0;
            if (normalized < 0.0) normalized += 360.0;
            double sectorPosition = normalized / 60.0;
            int sector = (int)Math.Floor(sectorPosition) % 6;
            double fraction = sectorPosition - Math.Floor(sectorPosition);
            double falling = 1.0 - fraction;
            switch (sector)
            {
                case 0: red = 1.0; green = fraction; blue = 0.0; break;
                case 1: red = falling; green = 1.0; blue = 0.0; break;
                case 2: red = 0.0; green = 1.0; blue = fraction; break;
                case 3: red = 0.0; green = falling; blue = 1.0; break;
                case 4: red = fraction; green = 0.0; blue = 1.0; break;
                default: red = 1.0; green = 0.0; blue = falling; break;
            }
        }

        // 色彩聚焦的亮度窗口：单峰、峰值恒为 1、峰位可移，且 v=0 / v=1 处恒为 0。
        // 峰位 0.5 时正好退化成既有的 4v(1-v)，所以「作用亮度」归零时，
        // 手动通道微调与既有的暖色 / 通道平衡走同一条曲线形状。
        // 两端强制为 0 很关键：否则作用亮度推到高光时会和 ToWord 的
        // 端点夹取撞出 17/255 级别的白场台阶。
        private static double FocusWindow(double value, double lowExponent,
            double highExponent, double normalization)
        {
            if (normalization <= 0.0) return 0.0;
            double low = Math.Pow(value, lowExponent);
            double high = Math.Pow(1.0 - value, highExponent);
            return low * high * normalization;
        }

        private static void BuildLuts(FilterRecommendation recommendation)
        {
            recommendation.Red = new ushort[256];
            recommendation.Green = new ushort[256];
            recommendation.Blue = new ushort[256];

            double previousRed = 0.0;
            double previousGreen = 0.0;
            double previousBlue = 0.0;
            double totalChange = 0.0;

            // 聚焦窗口的指数与归一化系数只依赖峰位，循环外算一次即可。
            double focusLowExponent = 2.0 * recommendation.FocusCenter;
            double focusHighExponent = 2.0 - focusLowExponent;
            double focusPeak = Math.Pow(recommendation.FocusCenter, focusLowExponent) *
                Math.Pow(1.0 - recommendation.FocusCenter, focusHighExponent);
            double focusNormalization = focusPeak > 1e-9 ? 1.0 / focusPeak : 0.0;

            for (int i = 0; i < 256; i++)
            {
                double input = i / 255.0;
                double value = Math.Pow(input, recommendation.Gamma);

                double blackFloor = recommendation.BrightnessBoost / 100.0 * 0.060;
                double toeActivation = MathUtil.SmoothStep(0.0, 0.035, input);
                value += blackFloor * toeActivation * (1.0 - value);

                double shadowMask = 1.0 - MathUtil.SmoothStep(0.42, 0.86, value);
                double toeGate = MathUtil.SmoothStep(0.005, 0.045, input);
                value += recommendation.ShadowLift *
                    4.0 * value * (1.0 - value) * shadowMask * toeGate;

                double midLift = recommendation.BrightnessBoost / 100.0 * 0.090;
                value += midLift * 4.0 * value * (1.0 - value) *
                    (1.0 - MathUtil.SmoothStep(0.72, 0.96, value));

                value = 0.46 + (value - 0.46) *
                    (1.0 + recommendation.Contrast);

                double highlightMask = MathUtil.SmoothStep(0.52, 0.92, value);
                value -= recommendation.HighlightCompression *
                    4.0 * value * (1.0 - value) * highlightMask;

                // Restore a small amount of black separation after lifting
                // shadows. Positive BlackPoint values reduce gray haze while
                // keeping the original black endpoint unchanged.
                double blackPointMask = 1.0 -
                    MathUtil.SmoothStep(0.10, 0.78, value);
                value -= recommendation.BlackPointRecovery *
                    blackPointMask * (0.45 + 0.55 * (1.0 - input));

                value = MathUtil.Clamp(value, 0.0, 1.0);
                value = MathUtil.Lerp(input, value, recommendation.StrengthBlend);
                double colorShape = 4.0 * value * (1.0 - value);
                double focusShape = FocusWindow(value, focusLowExponent,
                    focusHighExponent, focusNormalization);
                double appliedWarmth =
                    recommendation.Warmth * recommendation.StrengthBlend;
                double appliedRedBalance =
                    recommendation.RedBalance * recommendation.StrengthBlend;
                double appliedGreenBalance =
                    recommendation.GreenBalance * recommendation.StrengthBlend;
                double appliedBlueBalance =
                    recommendation.BlueBalance * recommendation.StrengthBlend;
                double appliedFocusRed =
                    recommendation.FocusRed * recommendation.StrengthBlend;
                double appliedFocusGreen =
                    recommendation.FocusGreen * recommendation.StrengthBlend;
                double appliedFocusBlue =
                    recommendation.FocusBlue * recommendation.StrengthBlend;
                // 臂环的 R / B 提升只认饱和区：权重 v² 在中间调只有 0.25。
                double armbandShape = value * value;
                double appliedArmbandRed =
                    recommendation.ArmbandRed * recommendation.StrengthBlend;
                double appliedArmbandBlue =
                    recommendation.ArmbandBlue * recommendation.StrengthBlend;
                double appliedArmbandGreen =
                    recommendation.ArmbandGreen * recommendation.StrengthBlend;
                double red = MathUtil.Clamp(
                    value + (appliedWarmth + appliedRedBalance) *
                    colorShape + appliedFocusRed * focusShape +
                    appliedArmbandRed * armbandShape, 0.0, 1.0);
                double green = MathUtil.Clamp(
                    value + appliedGreenBalance * colorShape +
                    appliedFocusGreen * focusShape +
                    appliedArmbandGreen * colorShape, 0.0, 1.0);
                double blue = MathUtil.Clamp(
                    value - appliedWarmth * colorShape +
                    appliedBlueBalance * colorShape +
                    appliedFocusBlue * focusShape +
                    appliedArmbandBlue * armbandShape, 0.0, 1.0);

                // SetDeviceGammaRamp requires a monotonic ramp on many drivers.
                red = Math.Max(previousRed, red);
                green = Math.Max(previousGreen, green);
                blue = Math.Max(previousBlue, blue);
                previousRed = red;
                previousGreen = green;
                previousBlue = blue;

                recommendation.Red[i] = ToWord(red);
                recommendation.Green[i] = ToWord(green);
                recommendation.Blue[i] = ToWord(blue);
                totalChange += Math.Abs(green - input);
            }

            recommendation.Red[0] = 0;
            recommendation.Green[0] = 0;
            recommendation.Blue[0] = 0;
            recommendation.Red[255] = ushort.MaxValue;
            recommendation.Green[255] = ushort.MaxValue;
            recommendation.Blue[255] = ushort.MaxValue;
            recommendation.ChangeStrength = MathUtil.Clamp(
                totalChange / 256.0 * 4.2, 0.0, 1.0);
        }

        private static string GetProfileName(
            AnalysisResult result,
            double darkScore,
            double brightScore,
            double backlightScore,
            double daylightEvidence,
            double comfortScore)
        {
            if (backlightScore > 0.50 && darkScore > 0.45 &&
                daylightEvidence < 0.45)
                return "逆光平衡";
            if (result.NightVisionScore > 0.42)
                return "夜视护眼";
            if (brightScore > 0.60)
                return "强光保护";
            if (darkScore > 0.78)
                return "暗室强增益";
            if (darkScore > 0.48)
                return "暗场增强";
            if (comfortScore > 0.48)
                return "室内柔和";
            if (daylightEvidence > 0.48)
                return "白天柔和";
            if (result.DynamicRange < 0.20)
                return "低对比增强";
            return "白天柔和";
        }

        public static ushort[] Identity()
        {
            var values = new ushort[256];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ushort)(i * 257);
            return values;
        }

        private static ushort ToWord(double value)
        {
            return (ushort)Math.Round(MathUtil.Clamp(value, 0.0, 1.0) * ushort.MaxValue);
        }
    }
}
