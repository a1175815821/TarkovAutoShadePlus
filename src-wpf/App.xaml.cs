using System;
using System.Threading;
using System.Windows;

namespace TarkovAutoShadePlus
{
    public partial class App : Application
    {
        private const string InstanceMutexName = "Local\\TarkovAutoShade.SingleInstance";
        private Mutex instanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            instanceMutex = new Mutex(true, InstanceMutexName, out createdNew);
            if (!createdNew)
            {
                instanceMutex.Dispose();
                instanceMutex = null;
                MessageBox.Show("TarkovAutoShadePlus 已在运行，无法同时打开多个窗口。",
                    "TarkovAutoShadePlus", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            if (Environment.OSVersion.Version.Major >= 6)
            {
                SetProcessDPIAware();
            }

            base.OnStartup(e);

            // 崩溃时先把画面还原，再让日志留下现场：这个工具改的是显示器曲线，
            // 进程死了画面却一直被滤镜盖着，是最难受的失败方式。
            this.DispatcherUnhandledException += delegate(object sender,
                System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
            {
                Diagnostics.Error("崩溃", "UI 线程未处理异常", args.Exception);
                TryRestoreScreen();
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender,
                UnhandledExceptionEventArgs args)
            {
                Diagnostics.Error("崩溃", "进程未处理异常", args.ExceptionObject as Exception);
                TryRestoreScreen();
            };

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            Diagnostics.Info("启动", "主窗口已显示");
        }

        private void TryRestoreScreen()
        {
            try
            {
                var window = MainWindow as MainWindow;
                if (window != null) window.RestoreScreenAfterCrash();
            }
            catch (Exception error)
            {
                Diagnostics.Error("崩溃", "还原画面也失败了", error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (instanceMutex != null)
            {
                try { instanceMutex.ReleaseMutex(); }
                catch (ApplicationException) { }
                instanceMutex.Dispose();
                instanceMutex = null;
            }
            base.OnExit(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
