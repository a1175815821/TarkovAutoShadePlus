using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace TarkovAutoShadePlus
{
    internal static class RealtimeCapture
    {
        public static Bitmap CaptureDisplay(string deviceName, out string error)
        {
            error = "";
            try
            {
                Screen screen = null;
                Screen[] screens = Screen.AllScreens;
                if (!string.IsNullOrWhiteSpace(deviceName))
                {
                    foreach (Screen item in screens)
                    {
                        if (string.Equals(item.DeviceName, deviceName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            screen = item;
                            break;
                        }
                    }
                }
                if (screen == null)
                {
                    foreach (Screen item in screens)
                    {
                        if (item.Primary) { screen = item; break; }
                    }
                }
                if (screen == null && screens.Length > 0) screen = screens[0];
                if (screen == null)
                {
                    error = "没有可用的显示器。";
                    return null;
                }

                Rectangle bounds = screen.Bounds;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    error = "显示器区域无效。";
                    return null;
                }

                var bitmap = new Bitmap(
                    bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(
                            bounds.Left, bounds.Top, 0, 0, bounds.Size,
                            CopyPixelOperation.SourceCopy);
                    }
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static string ResolveCaptureDevice(IList<string> targets)
        {
            if (targets != null)
            {
                foreach (string device in targets)
                {
                    if (!string.IsNullOrWhiteSpace(device)) return device;
                }
            }
            try
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.Primary) return screen.DeviceName;
                }
                if (Screen.AllScreens.Length > 0) return Screen.AllScreens[0].DeviceName;
            }
            catch { }
            return null;
        }
    }
}
