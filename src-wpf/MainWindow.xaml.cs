using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using WinForms = System.Windows.Forms;
using DrawingBitmap = System.Drawing.Bitmap;

namespace TarkovAutoShadePlus
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings settings = SettingsStore.Load();
        private readonly GammaRampController gammaController = new GammaRampController();
        private readonly DdcCiController ddcController = new DdcCiController();
        private readonly ScreenshotWatcher screenshotWatcher = new ScreenshotWatcher();

        private DispatcherTimer timestampTimer;
        private DispatcherTimer statusDotTimer;
        private DispatcherTimer settingsTimer;
        private DispatcherTimer previewRefreshTimer;
        private DispatcherTimer notificationTimer;
        private DispatcherTimer processWatchTimer;
        private DispatcherTimer ddcDebounceTimer;
        private int pendingBrightness = -1;
        private int pendingContrast = -1;
        private string pendingDdcDevice = "";
        private PreviewControl previewControl;
        private GlobalHotkey toggleHotkey;
        private WinForms.NotifyIcon trayIcon;
        private WinForms.ContextMenuStrip trayMenu;
        private WinForms.ToolStripItem trayToggleMenuItem;
        private AnalysisResult currentAnalysis;
        private string lastAnalyzedPath;
        private string displayDevice;
        private readonly List<DisplayTarget> displayTargets = new List<DisplayTarget>();
        private int analysisVersion;
        private int previewVersion;
        private bool filterModeEnabled = true;
        private bool initializing = true;
        private bool closing;
        private bool trayExitRequested;
        private Window closeChoiceDialog;
        private bool updatingHardwareControls;
        private bool updatingDisplaySelection;
        private bool updatingDisplayList;
        private bool updatingFolderSelection;
        private bool updatingProcessSelection;
        private bool promotingCustomPreset;
        // 当前生效、同时也是界面正在编辑的那份调参档案。
        // 平铺字段始终镜像这份档案，所以既有的读写逻辑不用改。
        private int activeProfileKey = AppSettings.ProfileKeyEft;
        private bool switchingProfile;
        // 手动点过档案按钮后，在前台游戏没换之前不要把选择盖回去。
        // 否则「跟随游戏」开着时点一下按钮就会被立刻弹回，看起来像没保存。
        private bool manualProfileOverride;
        private int manualProfileOverrideKey;
        private int manualProfileAnchorKey;
        private bool processWasDetected;
        private bool autoPausedByProcess;
        private bool processWatchUiInitialized;
        private bool lastProcessWatchUiEnabled;
        private bool lastProcessWatchUiActive;
        private DateTime lastProcessWatchUiRefreshUtc;
        private int processWatchEvaluationQueued;
        private int monitorProbeVersion;
        private WinEventDelegate foregroundWindowEventHandler;
        private IntPtr foregroundWindowEventHook;
        private MonitorCapabilities monitorCapabilities = new MonitorCapabilities();
        private RealtimeController realtimeController;
        private bool updatingRealtimeControls;
        private int realtimeVersion;
        private int reminderCount;
        private bool hasError;

        public MainWindow()
        {
            settings.Normalize();
            // 平铺字段只是「上次生效的那份档案」的镜像，而 activeProfileKey 是内存态。
            // 不先按 LockedProfileKey 把它恢复到同一档，界面会拿着竞技场的数值标成
            // 「原版」，随后任何一次落盘都会把这套数值写进原版档案里。
            activeProfileKey = MathUtil.Clamp(settings.LockedProfileKey,
                AppSettings.ProfileKeyEft, AppSettings.ProfileKeyArena);
            settings.GetProfile(activeProfileKey).ApplyTo(settings);
            InitializeComponent();
            RestoreWindowGeometry();
            gammaController.TransitionFailed += OnGammaTransitionFailed;
            InitializePreviewControl();
            InitializeTrayIcon();
            InitializeHotkey();
            InitializeTimers();
            BindSliderEvents();
            BindButtonEvents();
            BindPresetEvents();
            BindProfileEvents();
            BindFocusEvents();
            BindDisplayEvents();
            BindProcessWatchEvents();
            BindFolderPicker();
            AboutButton.Click += delegate { ShowAboutWindow(); };
            DiagnosticsButton.Click += delegate { ShowDiagnosticsWindow(); };
            FaultPill.MouseLeftButtonUp += delegate { OpenDiagnosticsFromPill(); };
            InitializeDisplay();
            ApplySettingsToSliders();
            InitializeProcessWatcher();
            ConfigureScreenshotFolder();
            BindRealtimeEvents();
            ApplyRealtimeSettingsToUi();
            InitializeRealtime();
            RecoverPreviousSession();
            ObsFilterStateStore.WriteDisabled();

            screenshotWatcher.ScreenshotReady += OnScreenshotReady;
            screenshotWatcher.WatcherFaulted += OnWatcherFaulted;

            Loaded += delegate
            {
                initializing = false;
                UpdateWatcherUi();
                UpdateProcessWatchUi();
                // GlobalHotkey 在同一个 Loaded 上更早注册，此时结果已经确定。
                ReportHotkeyRegistration();
                ReportSettingsLoadResult();
            };
            Closed += OnClosed;
            // 几何随时记在内存里：OnClosed 里 RestoreBounds 可能已经拿不到有效值
            // （窗口正在拆），只在退出时取一次会存成空。这里只赋 5 个整数，不写盘。
            LocationChanged += delegate { CaptureWindowGeometry(); };
            SizeChanged += delegate { CaptureWindowGeometry(); };

            RefreshSliderLabels();
            UpdateMetrics();
            UpdateWatcherUi();
            UpdateModeUi();
        }

        private void InitializeDisplay()
        {
            RefreshDisplayList(false);
        }

        private void RefreshDisplayList(bool showNotification)
        {
            bool wasRunning = IsFilterRunning();
            if (wasRunning)
            {
                string restoreError;
                gammaController.RestoreActive(out restoreError);
            }

            updatingDisplayList = true;
            bool targetOffline = false;
            try
            {
                var refreshedDisplays = GammaRampController.EnumerateDisplays();
                displayTargets.Clear();
                displayTargets.AddRange(refreshedDisplays);

                DisplayTarget selected = null;
                DisplayTarget remembered = null;
                foreach (DisplayTarget display in displayTargets)
                {
                    if (selected == null && display.Primary) selected = display;
                    if (string.Equals(display.DeviceName, displayDevice,
                        StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(display.DeviceName, settings.DisplayDevice,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        selected = display;
                        remembered = display;
                    }
                }
                if (selected == null && displayTargets.Count > 0)
                    selected = displayTargets[0];

                DisplayComboBox.Items.Clear();
                if (selected == null)
                {
                    displayDevice = "";
                    DisplayComboBox.Items.Add("未检测到显示器");
                    DisplayComboBox.SelectedIndex = 0;
                }
                else
                {
                    foreach (DisplayTarget display in displayTargets)
                        DisplayComboBox.Items.Add(display);
                    displayDevice = selected.DeviceName;
                    if (remembered != null)
                        settings.DisplayDevice = remembered.DeviceName;
                    else
                    {
                        // 用户指定的显示器这次没在线（睡着、拔掉、换过分辨率）。
                        // 临时落到可用的一台上，但不能把他的选择写掉 —— 否则
                        // 一次掉线就永久改掉目标显示器。
                        targetOffline = !string.IsNullOrWhiteSpace(settings.DisplayDevice);
                    }
                    DisplayComboBox.SelectedItem = selected;
                }

                // 这里只整理「记录」，不按在线设备剪枝：剪完再落盘，显示器睡一觉
                // 回来用户的多屏勾选就没了。在线过滤放在取用目标的时候做。
                var selectedDevices = new List<string>();
                if (settings.SelectedDisplayDevices != null)
                {
                    foreach (string device in settings.SelectedDisplayDevices)
                        AddUnique(selectedDevices, device);
                }
                if (showNotification)
                {
                    // 「刷新显示器」是用户主动表态「我的显示器变了」，只有这时才按
                    // 在线集合清理记录，否则彻底拔掉一台后就再没有地方能取消它。
                    var onlineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DisplayTarget display in displayTargets)
                        onlineNames.Add(display.DeviceName);
                    selectedDevices.RemoveAll(
                        device => !onlineNames.Contains(device));
                }
                if (selectedDevices.Count == 0 && selected != null)
                    selectedDevices.Add(selected.DeviceName);
                settings.SelectedDisplayDevices = selectedDevices;

                var validDevices = new List<string>();
                foreach (DisplayTarget display in displayTargets)
                    validDevices.Add(display.DeviceName);
                gammaController.PruneBaselines(validDevices);

                DisplayModeComboBox.SelectedIndex = settings.MultiDisplayMode ? 1 : 0;
                BuildDisplaySelectionControls();
                ApplyDisplayModeUi();
                RefreshMonitorCapabilities();
            }
            finally
            {
                updatingDisplayList = false;
            }

            if (wasRunning && currentAnalysis != null && currentAnalysis.IsUsable)
                ApplyRecommendation(currentAnalysis);
            else
                UpdateModeUi();
            if (targetOffline && !string.IsNullOrWhiteSpace(displayDevice))
                ShowNotification("上次设定的目标显示器本次未在线，已临时改用 " +
                    FriendlyDisplayLabel(displayDevice) + "；原设置仍然保留。",
                    NotificationType.Warning);
            else if (showNotification)
                ShowNotification("显示器列表已更新", NotificationType.Info);
            SaveSettings();
            if (realtimeController != null) realtimeController.Wake();
        }

        private string FriendlyDisplayLabel(string deviceName)
        {
            foreach (DisplayTarget display in displayTargets)
            {
                if (string.Equals(display.DeviceName, deviceName,
                    StringComparison.OrdinalIgnoreCase)) return display.ToString();
            }
            return deviceName.Replace(@"\\.\", "");
        }

        private void BuildDisplaySelectionControls()
        {
            if (DisplaySelectionPanel == null) return;
            updatingDisplaySelection = true;
            try
            {
                DisplaySelectionPanel.Children.Clear();
                var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (settings.SelectedDisplayDevices != null)
                {
                    foreach (string device in settings.SelectedDisplayDevices)
                        if (!string.IsNullOrWhiteSpace(device)) selected.Add(device);
                }
                if (selected.Count == 0 && !string.IsNullOrWhiteSpace(displayDevice))
                    selected.Add(displayDevice);

                bool anyChecked = false;
                foreach (DisplayTarget display in displayTargets)
                {
                    var checkBox = new CheckBox
                    {
                        Content = display.ToString(),
                        Tag = display.DeviceName,
                        IsChecked = selected.Contains(display.DeviceName),
                        Style = (Style)FindResource("TacticalCheckBox"),
                        Margin = new Thickness(0, 0, 0, 6),
                        ToolTip = "勾选后，多屏模式会将滤镜应用到此显示器。"
                    };
                    if (checkBox.IsChecked == true) anyChecked = true;
                    checkBox.Checked += OnDisplaySelectionChanged;
                    checkBox.Unchecked += OnDisplaySelectionChanged;
                    DisplaySelectionPanel.Children.Add(checkBox);
                }

                if (!anyChecked && DisplaySelectionPanel.Children.Count > 0)
                {
                    var first = DisplaySelectionPanel.Children[0] as CheckBox;
                    if (first != null) first.IsChecked = true;
                }
                SyncSelectedDisplaysFromUi();
            }
            finally
            {
                updatingDisplaySelection = false;
            }
        }

        private void SyncSelectedDisplaysFromUi()
        {
            // 界面上只列得出「当前在线」的显示器。这里必须原样保留这次没出现的
            // 勾选项，否则每次 BuildDisplaySelectionControls 都会顺手把掉线的
            // 显示器从记录里剪掉，用户的多屏勾选睡一觉就没了。
            var decided = new List<string>();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (DisplaySelectionPanel != null)
            {
                foreach (UIElement element in DisplaySelectionPanel.Children)
                {
                    var checkBox = element as CheckBox;
                    string device = checkBox == null ? null : checkBox.Tag as string;
                    if (checkBox == null || string.IsNullOrWhiteSpace(device)) continue;
                    if (listed.Contains(device)) continue;
                    listed.Add(device);
                    if (checkBox.IsChecked == true) decided.Add(device);
                }
            }
            var merged = new List<string>();
            if (settings.SelectedDisplayDevices != null)
            {
                foreach (string device in settings.SelectedDisplayDevices)
                {
                    // 在线的由勾选框说了算；这次没出现的原样保留。
                    if (!listed.Contains(device)) AddUnique(merged, device);
                }
            }
            foreach (string device in decided) AddUnique(merged, device);
            settings.SelectedDisplayDevices = merged;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (string existing in list)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)) return;
            }
            list.Add(value);
        }

        private List<string> GetTargetDisplayDevices()
        {
            if (!settings.MultiDisplayMode)
            {
                var single = new List<string>();
                if (!string.IsNullOrWhiteSpace(displayDevice)) single.Add(displayDevice);
                return single;
            }
            SyncSelectedDisplaysFromUi();
            var wanted = new List<string>(settings.SelectedDisplayDevices);
            // 勾选里可以留着这次没在线的显示器（记录要留住），但生效目标只能是
            // 真在线的那些，否则一整条应用链会因为一台读不到而整体失败。
            if (displayTargets.Count == 0) return wanted;
            var online = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayTarget display in displayTargets)
                online.Add(display.DeviceName);
            var result = new List<string>();
            foreach (string device in wanted)
            {
                if (online.Contains(device)) result.Add(device);
            }
            return result;
        }

        /// <summary>
        /// 实时线程专用的目标显示器：只读 settings 里的纯数据，绝不碰 WPF 控件。
        /// GetTargetDisplayDevices 在多屏模式下会去遍历勾选框，那在后台线程上
        /// 必定抛 InvalidOperationException，而实时循环会把异常整个吞掉 —— 结果
        /// 就是界面上写着「采样中」，实际一帧都没调。
        /// SelectedDisplayDevices 每次变更都是整体换成新 List（不就地改），
        /// 所以这里读到的引用始终是完整的。
        /// </summary>
        private List<string> GetRealtimeTargetDisplayDevices()
        {
            var result = new List<string>();
            if (settings.MultiDisplayMode)
            {
                var devices = settings.SelectedDisplayDevices;
                if (devices != null)
                {
                    foreach (string device in devices)
                    {
                        if (!string.IsNullOrWhiteSpace(device)) result.Add(device);
                    }
                }
                return result;
            }
            string single = displayDevice;
            if (string.IsNullOrWhiteSpace(single)) single = settings.DisplayDevice;
            if (!string.IsNullOrWhiteSpace(single)) result.Add(single);
            return result;
        }

        private void ApplyDisplayModeUi()
        {
            bool multi = settings.MultiDisplayMode;
            SingleDisplayPanel.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;
            MultiDisplayPanel.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            if (!multi) RebuildSingleDisplayComboBox();
        }

        private void RebuildSingleDisplayComboBox()
        {
            bool wasUpdating = updatingDisplayList;
            updatingDisplayList = true;
            try
            {
                DisplayComboBox.Items.Clear();
                DisplayTarget selected = null;
                foreach (DisplayTarget display in displayTargets)
                {
                    DisplayComboBox.Items.Add(display);
                    if (string.Equals(display.DeviceName, displayDevice,
                        StringComparison.OrdinalIgnoreCase)) selected = display;
                }
                if (selected == null && displayTargets.Count > 0)
                    selected = displayTargets[0];
                if (selected == null)
                {
                    DisplayComboBox.Items.Add("未检测到显示器");
                    DisplayComboBox.SelectedIndex = 0;
                }
                else
                {
                    DisplayComboBox.SelectedItem = selected;
                }
            }
            finally
            {
                updatingDisplayList = wasUpdating;
            }
        }

        private void ConfigureScreenshotFolder()
        {
            settings.Normalize();
            var candidates = new List<string>();
            if (settings.ScreenshotFolders != null)
            {
                foreach (string folder in settings.ScreenshotFolders)
                {
                    if (!string.IsNullOrWhiteSpace(folder) &&
                        !candidates.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(folder);
                }
            }
            if (!string.IsNullOrWhiteSpace(settings.ScreenshotFolder) &&
                !candidates.Contains(settings.ScreenshotFolder, StringComparer.OrdinalIgnoreCase))
                candidates.Insert(0, settings.ScreenshotFolder);

            // 自动发现只在「用户从没自己整理过目录列表」时跑一次。以前每次启动
            // 都把发现的目录补回列表，于是界面取消勾选的目录下次开机又回来。
            // 要重新扫描，用「重新发现」按钮。
            if (!settings.ScreenshotFoldersConfigured)
            {
                foreach (string discovered in ScreenshotFolderLocator.FindAll())
                {
                    if (!candidates.Contains(discovered, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(discovered);
                }
                settings.ScreenshotFoldersConfigured = true;
            }
            settings.ScreenshotFolders = candidates;
            if (candidates.Count > 0)
                settings.ScreenshotFolder = candidates[0];
            if (!initializing) SettingsStore.Save(settings);

            ApplyScreenshotFoldersToWatcher();
            BuildFolderSelectionControls();
            UpdateFolderSummary();
            if (screenshotWatcher.Folders.Count == 0)
                ShowNotification("未找到截图目录，请点击“添加目录”按钮手动选择。",
                    NotificationType.Warning);
        }

        private void ApplyScreenshotFoldersToWatcher()
        {
            var existing = new List<string>();
            if (settings.ScreenshotFolders != null)
            {
                foreach (string folder in settings.ScreenshotFolders)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) &&
                            !existing.Contains(folder, StringComparer.OrdinalIgnoreCase))
                            existing.Add(folder);
                    }
                    catch { }
                }
            }
            screenshotWatcher.SetFolders(existing);
            screenshotWatcher.Enabled = settings.AutoWatch;
        }

        private void BuildFolderSelectionControls()
        {
            if (FolderSelectionPanel == null) return;
            updatingFolderSelection = true;
            try
            {
                FolderSelectionPanel.Children.Clear();
                if (settings.ScreenshotFolders == null || settings.ScreenshotFolders.Count == 0)
                {
                    var empty = new TextBlock
                    {
                        Text = "暂无已记录目录，将自动发现原版/竞技场。",
                        FontFamily = (System.Windows.Media.FontFamily)FindResource("FontUi"),
                        FontSize = 10,
                        Foreground = (System.Windows.Media.Brush)FindResource("PhosphorDimBrush"),
                        TextWrapping = TextWrapping.Wrap
                    };
                    FolderSelectionPanel.Children.Add(empty);
                    return;
                }
                foreach (string folder in settings.ScreenshotFolders)
                {
                    string captured = folder;
                    bool exists;
                    try { exists = Directory.Exists(captured); }
                    catch { exists = false; }
                    var checkBox = new CheckBox
                    {
                        Content = AppSettings.GetFriendlyFolderName(captured) + "｜" + captured,
                        Tag = captured,
                        IsChecked = exists,
                        Style = (Style)FindResource("TacticalCheckBox"),
                        Margin = new Thickness(0, 0, 0, 6),
                        ToolTip = captured + (exists ? "" : "（目录不存在，仅保留记录）")
                    };
                    checkBox.Checked += OnFolderSelectionChanged;
                    checkBox.Unchecked += OnFolderSelectionChanged;
                    FolderSelectionPanel.Children.Add(checkBox);
                }
            }
            finally
            {
                updatingFolderSelection = false;
            }
        }

        private void OnFolderSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (initializing || updatingFolderSelection) return;
            var remaining = new List<string>();
            if (FolderSelectionPanel != null)
            {
                foreach (UIElement element in FolderSelectionPanel.Children)
                {
                    var checkBox = element as CheckBox;
                    string folder = checkBox == null ? null : checkBox.Tag as string;
                    if (checkBox != null && checkBox.IsChecked == true &&
                        !string.IsNullOrWhiteSpace(folder)) remaining.Add(folder);
                }
            }
            // 取消勾选即移除记录；全不勾选则停止监听但保留空列表。
            settings.ScreenshotFolders = remaining;
            if (remaining.Count > 0) settings.ScreenshotFolder = remaining[0];
            ApplyScreenshotFoldersToWatcher();
            UpdateFolderSummary();
            UpdateWatcherUi();
            SaveSettings();
        }

        private void UpdateFolderSummary()
        {
            IList<string> watched = screenshotWatcher.Folders;
            if (watched.Count == 0)
            {
                FolderPath.Text = "未找到截图目录";
                FolderPath.ToolTip = "点击添加截图目录";
            }
            else if (watched.Count == 1)
            {
                FolderPath.Text = AppSettings.GetFriendlyFolderName(watched[0]) + "｜" + watched[0];
                FolderPath.ToolTip = watched[0];
            }
            else
            {
                var names = new List<string>();
                foreach (string folder in watched)
                    names.Add(AppSettings.GetFriendlyFolderName(folder));
                FolderPath.Text = "已监听 " + watched.Count + " 个目录（" + string.Join("＋", names.ToArray()) + "）";
                FolderPath.ToolTip = string.Join("\n", watched.ToArray());
            }
        }

        private void BindFolderPicker()
        {
            FolderPath.Cursor = Cursors.Hand;
            FolderPath.ToolTip = "点击添加截图目录";
            FolderPath.MouseLeftButtonDown += delegate { OpenScreenshotFolderPicker(); };
            BrowseFolderButton.Click += delegate { OpenScreenshotFolderPicker(); };
            if (ResetFolderButton != null)
                ResetFolderButton.Click += delegate { ResetScreenshotFolders(); };
        }

        private void OpenScreenshotFolderPicker()
        {
            using (var dialog = new WinForms.FolderBrowserDialog())
            {
                dialog.Description = "选择要监听的 Screenshots 文件夹（原版/竞技场可分别添加，可同时监听多个）";
                string initial = null;
                if (settings.ScreenshotFolders != null)
                {
                    foreach (string folder in settings.ScreenshotFolders)
                    {
                        if (Directory.Exists(folder)) { initial = folder; break; }
                    }
                }
                dialog.SelectedPath = initial ?? (Directory.Exists(settings.ScreenshotFolder) ?
                    settings.ScreenshotFolder : Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments));
                if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

                string picked = dialog.SelectedPath;
                if (settings.ScreenshotFolders == null)
                    settings.ScreenshotFolders = new List<string>();
                bool exists = false;
                foreach (string folder in settings.ScreenshotFolders)
                {
                    if (string.Equals(folder, picked, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) settings.ScreenshotFolders.Add(picked);
                settings.ScreenshotFolder = picked;
                ApplyScreenshotFoldersToWatcher();
                BuildFolderSelectionControls();
                UpdateFolderSummary();
                SettingsStore.Save(settings);
                UpdateWatcherUi();
                ShowNotification("截图目录已添加，自动监听已准备就绪。", NotificationType.Success);
            }
        }

        private void ResetScreenshotFolders()
        {
            List<string> discovered = ScreenshotFolderLocator.FindAll();
            settings.ScreenshotFolders = discovered;
            if (discovered.Count > 0) settings.ScreenshotFolder = discovered[0];
            ApplyScreenshotFoldersToWatcher();
            BuildFolderSelectionControls();
            UpdateFolderSummary();
            SaveSettings();
            UpdateWatcherUi();
            ShowNotification(discovered.Count == 0 ?
                "未发现原版/竞技场截图目录，请手动添加。" :
                "已重新发现 " + discovered.Count + " 个截图目录。", discovered.Count == 0 ?
                NotificationType.Warning : NotificationType.Success);
        }

        private void InitializePreviewControl()
        {
            previewControl = new PreviewControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            PreviewContainer.Children.Insert(0, previewControl);
            PreviewEmptyState.Visibility = Visibility.Visible;
        }

        private void InitializeTrayIcon()
        {
            System.Drawing.Icon icon = null;
            try
            {
                icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch { }
            if (icon == null) icon = System.Drawing.SystemIcons.Application;

            trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add("显示主窗口", null, delegate { ShowFromTray(); });
            // 文字里的快捷键由 UpdateHotkeyUi 统一刷新，改键后这里不能停在旧按键上。
            trayToggleMenuItem = trayMenu.Items.Add("开启 / 关闭滤镜", null,
                delegate { ToggleFilter(); });
            trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            trayMenu.Items.Add("退出程序", null, delegate { RequestExitFromTray(); });

            trayIcon = new WinForms.NotifyIcon
            {
                Icon = icon,
                Text = "TarkovAutoShadePlus",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += delegate { ShowFromTray(); };

            StateChanged += delegate
            {
                if (WindowState != WindowState.Minimized) return;
                Hide();
                trayIcon.ShowBalloonTip(1800, "TarkovAutoShadePlus",
                    "已最小化到系统托盘。双击图标恢复窗口。",
                    WinForms.ToolTipIcon.Info);
            };
        }

        private void ShowFromTray()
        {
            if (closing) return;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void RequestExitFromTray()
        {
            if (closing) return;
            trayExitRequested = true;

            // The close prompt owns a nested dispatcher loop. Close it first;
            // the original OnClosing call will then finish the main-window exit.
            if (closeChoiceDialog != null)
            {
                closeChoiceDialog.DialogResult = false;
                return;
            }

            Close();
        }

        private void InitializeHotkey()
        {
            toggleHotkey = new GlobalHotkey(
                this, 0x5441,
                (GlobalHotkey.Modifiers)settings.HotkeyModifiers,
                (WinForms.Keys)settings.HotkeyKeyCode);
            toggleHotkey.HotkeyPressed += delegate
            {
                Dispatcher.BeginInvoke(new Action(ToggleFilter));
            };
            UpdateHotkeyUi();
        }

        private void UpdateHotkeyUi()
        {
            string hotkey = FormatHotkey(settings.HotkeyKeyCode, settings.HotkeyModifiers);
            HotkeyText.Text = hotkey;
            // 窗口加载完成之前 GlobalHotkey 还没机会去注册，那时的「未注册」不是失败。
            bool failed = IsLoaded && toggleHotkey != null && !toggleHotkey.IsRegistered;
            HotkeyText.Foreground = (System.Windows.Media.Brush)FindResource(
                failed ? "HazardRedBrush" : "AmberAlertBrush");
            ToggleButton.Content = (IsFilterRunning() ? "关闭滤镜  " : "开启滤镜  ") + hotkey;
            if (trayToggleMenuItem != null)
                trayToggleMenuItem.Text = "开启 / 关闭滤镜 (" + hotkey + ")";
        }

        private void ReportHotkeyRegistration()
        {
            UpdateHotkeyUi();
            if (toggleHotkey == null) return;
            string hotkey = FormatHotkey(settings.HotkeyKeyCode, settings.HotkeyModifiers);
            if (toggleHotkey.IsRegistered)
            {
                Diagnostics.Info("热键", "已注册 " + hotkey);
                return;
            }
            // 裸功能键很容易撞上 IDE / 录屏软件。注册失败原先只能靠用户自己发现
            // 「按了没反应」，这里把它说出来，并把标签染红。
            Diagnostics.Warn("热键", "注册失败：" + hotkey + "（可能已被其他程序占用）");
            ShowNotification("快捷键 " + hotkey +
                " 注册失败，可能已被其他程序占用；点「设置」换一个。",
                NotificationType.Warning);
        }

        private void ReportSettingsLoadResult()
        {
            if (string.IsNullOrEmpty(SettingsStore.LastLoadError)) return;
            // 读档失败会被默认值顶掉，用户看到的就是「设置没了」，必须说清楚。
            ShowNotification("上次的配置文件读取失败（" + SettingsStore.LastLoadError +
                "），本次已改用默认配置；原文件已备份，详见「诊断」。",
                NotificationType.Error);
        }

        private void ShowHotkeyCapture()
        {
            int capturedKey = settings.HotkeyKeyCode;
            int capturedModifiers = settings.HotkeyModifiers;
            var dialog = new Window
            {
                Title = "设置滤镜快捷键",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 390,
                Height = 260,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = (Brush)FindResource("CrtSurfaceBrush"),
                Foreground = (Brush)FindResource("PhosphorWhiteBrush")
            };

            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            var hint = new TextBlock
            {
                Text = "请按下组合键（例如 Ctrl + F8），再点击确定。",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontUi"),
                FontSize = 11,
                Foreground = (Brush)FindResource("PhosphorDimBrush")
            };
            Grid.SetRow(hint, 0);
            root.Children.Add(hint);

            var captured = new TextBlock
            {
                Text = FormatHotkey(capturedKey, capturedModifiers),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontMono"),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AmberAlertBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(captured, 1);
            root.Children.Add(captured);

            var note = new TextBlock
            {
                Text = "Esc 取消本次设置",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontUi"),
                FontSize = 10,
                Foreground = (Brush)FindResource("PhosphorDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(note, 2);
            root.Children.Add(note);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var cancel = new Button { Content = "取消", Width = 72, Height = 30, Style = (Style)FindResource("TacticalButton") };
            var confirm = new Button { Content = "确定", Width = 72, Height = 30, Margin = new Thickness(10, 0, 0, 0), Style = (Style)FindResource("TacticalButtonPrimary") };
            cancel.Click += delegate { dialog.DialogResult = false; };
            confirm.Click += delegate { dialog.DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            dialog.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { dialog.DialogResult = false; e.Handled = true; return; }
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                    key == Key.LeftAlt || key == Key.RightAlt ||
                    key == Key.LeftShift || key == Key.RightShift ||
                    key == Key.LWin || key == Key.RWin) return;
                int virtualKey = KeyInterop.VirtualKeyFromKey(key);
                if (virtualKey <= 0) return;
                ModifierKeys modifiers = Keyboard.Modifiers;
                capturedKey = virtualKey;
                capturedModifiers = 0;
                if ((modifiers & ModifierKeys.Alt) != 0) capturedModifiers |= 0x0001;
                if ((modifiers & ModifierKeys.Control) != 0) capturedModifiers |= 0x0002;
                if ((modifiers & ModifierKeys.Shift) != 0) capturedModifiers |= 0x0004;
                captured.Text = FormatHotkey(capturedKey, capturedModifiers);
                e.Handled = true;
            };
            dialog.Content = root;
            dialog.Loaded += delegate { dialog.Focus(); };

            if (toggleHotkey != null) toggleHotkey.Suspend();
            bool accepted = false;
            try
            {
                accepted = dialog.ShowDialog() == true;
            }
            finally
            {
                if (!accepted && toggleHotkey != null) toggleHotkey.Resume();
            }
            if (!accepted) return;
            int previousKey = settings.HotkeyKeyCode;
            int previousModifiers = settings.HotkeyModifiers;
            settings.HotkeyKeyCode = capturedKey;
            settings.HotkeyModifiers = capturedModifiers;
            bool registered = toggleHotkey != null && toggleHotkey.Rebind(
                (GlobalHotkey.Modifiers)capturedModifiers,
                (WinForms.Keys)capturedKey);
            if (!registered)
            {
                settings.HotkeyKeyCode = previousKey;
                settings.HotkeyModifiers = previousModifiers;
                if (toggleHotkey != null)
                {
                    toggleHotkey.Rebind(
                        (GlobalHotkey.Modifiers)previousModifiers,
                        (WinForms.Keys)previousKey);
                }
                UpdateHotkeyUi();
                UpdateModeUi();
                ShowNotification("快捷键注册失败，可能已被其他程序占用；已恢复原按键。",
                    NotificationType.Warning);
                return;
            }
            SaveSettings();
            UpdateHotkeyUi();
            UpdateModeUi();
            ShowNotification("全局快捷键已更新为 " + FormatHotkey(capturedKey, capturedModifiers),
                NotificationType.Success);
        }

        private static string FormatHotkey(int keyCode, int modifiers)
        {
            if (keyCode <= 0) return "未设置";
            var parts = new List<string>();
            if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((modifiers & 0x0001) != 0) parts.Add("Alt");
            if ((modifiers & 0x0004) != 0) parts.Add("Shift");
            parts.Add(((WinForms.Keys)keyCode).ToString());
            return string.Join(" + ", parts.ToArray());
        }

        private void InitializeTimers()
        {
            timestampTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timestampTimer.Tick += UpdateTimestamp;
            timestampTimer.Start();
            UpdateTimestamp(null, null);

            statusDotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            statusDotTimer.Tick += delegate
            {
                if (!IsFilterRunning())
                {
                    StatusDot.BeginAnimation(UIElement.OpacityProperty, null);
                    StatusDot.Opacity = 0.65;
                    return;
                }
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.4,
                    Duration = TimeSpan.FromSeconds(1),
                    AutoReverse = true
                };
                StatusDot.BeginAnimation(UIElement.OpacityProperty, animation);
            };
            statusDotTimer.Start();

            settingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            settingsTimer.Tick += delegate
            {
                settingsTimer.Stop();
                SaveSettings();
            };

            previewRefreshTimer = new DispatcherTimer { Interval =
                TimeSpan.FromMilliseconds(180) };
            previewRefreshTimer.Tick += delegate
            {
                previewRefreshTimer.Stop();
                RefreshCurrentPreview();
            };

        }

        private void UpdateTimestamp(object sender, EventArgs e)
        {
            TimestampText.Text = string.Format("{0:yyyy.MM.dd} — {0:HH:mm:ss}", DateTime.Now);
        }

        private void BindSliderEvents()
        {
            BindSlider(ShadowSlider, ShadowValue, false);
            BindSlider(HighlightSlider, HighlightValue, false);
            BindSlider(ColorSlider, ColorValue, false);
            BindSlider(IndoorSlider, IndoorValue, false);
            BindSlider(GuardSlider, GuardValue, false);
            BindSlider(BlackSlider, BlackValue, false);
            BindSlider(StrengthSlider, StrengthValue, false);
            BindSlider(ExposureSlider, ExposureValue, true);
            BindSlider(ContrastSlider, ContrastValue, true);
            BindSlider(WarmthSlider, WarmthValue, true);
            BindSlider(SaturationSlider, SaturationValue, true);
            BindSlider(FocusHueSlider, FocusHueValue, false, "°",
                UpdateFocusHueSwatch);
            BindSlider(FocusStrengthSlider, FocusStrengthValue, false);
            BindSlider(FocusRangeSlider, FocusRangeValue, true);
            BindSlider(TrimRedSlider, TrimRedValue, true);
            BindSlider(TrimGreenSlider, TrimGreenValue, true);
            BindSlider(TrimBlueSlider, TrimBlueValue, true);
            BindSlider(ArmbandLiftSlider, ArmbandLiftValue, false);
            BindSlider(ArmbandDesaturateSlider, ArmbandDesaturateValue, false);
        }

        private void BindSlider(Slider slider, TextBlock value, bool signed,
            string suffix = "", Action<double> onValueChanged = null)
        {
            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                int number = (int)Math.Round(e.NewValue);
                value.Text = (signed ? FormatSigned(number) : number.ToString()) + suffix;
                // 色卡这类纯展示元素必须在 initializing 之前刷新，
                // 否则铺值阶段（ApplySettingsToSliders）色卡会停在旧颜色上。
                if (onValueChanged != null) onValueChanged(e.NewValue);
                // switchingProfile：切档时是程序在铺值，不是用户在调，
                // 不能把刚加载的档案又顶成“自定义预设”。
                if (initializing || switchingProfile) return;
                if (PresetComboBox.SelectedIndex != 5)
                {
                    promotingCustomPreset = true;
                    PresetComboBox.SelectedIndex = 5;
                    promotingCustomPreset = false;
                }
                SyncSettingsFromSliders();
                settingsTimer.Stop();
                settingsTimer.Start();
                UpdateCurrentRecommendation();
            };
        }

        private void BindButtonEvents()
        {
            AnalyzeButton.Click += delegate { AnalyzeLatest(false); };
            ToggleButton.Click += delegate { ToggleFilter(); };
            ChangeHotkeyButton.Click += delegate { ShowHotkeyCapture(); };
            ResetDefaultsButton.Click += delegate { RestoreDefaultFilterValues(); };
            SmoothTransitionCheckBox.Checked += delegate
            {
                settings.SmoothTransition = true;
                SaveSettings();
            };
            SmoothTransitionCheckBox.Unchecked += delegate
            {
                settings.SmoothTransition = false;
                SaveSettings();
            };

            MonitorBrightnessSlider.ValueChanged += OnMonitorSliderChanged;
            MonitorContrastSlider.ValueChanged += OnMonitorSliderChanged;
        }

        // 点一下快捷色相时，若用户还没开过聚焦，就顺手给一个能看出效果、
        // 又不至于压过画面的强度。0 强度下点色相什么都看不到，很容易被当成没生效。
        private const int DefaultFocusStrengthOnPick = 35;

        private void BindFocusEvents()
        {
            BindFocusHuePreset(FocusHueEmberButton, 25, "火光橙");
            BindFocusHuePreset(FocusHueBloodButton, 0, "血迹红");
            BindFocusHuePreset(FocusHueFoliageButton, 110, "植被绿");
            BindFocusHuePreset(FocusHueCyanButton, 195, "霓虹青");

            FocusEnabledCheckBox.Checked += delegate
            {
                if (initializing || switchingProfile) return;
                settings.FocusEnabled = true;
                UpdateFocusEnabledUi();
                CommitColorChange("已启用色彩聚焦");
            };
            FocusEnabledCheckBox.Unchecked += delegate
            {
                if (initializing || switchingProfile) return;
                settings.FocusEnabled = false;
                UpdateFocusEnabledUi();
                CommitColorChange("已停用色彩聚焦（参数保留）");
            };

            ArmbandEnabledCheckBox.Checked += delegate
            {
                if (initializing || switchingProfile) return;
                settings.ArmbandEnabled = true;
                UpdateArmbandEnabledUi();
                CommitColorChange("已启用臂环增强");
            };
            ArmbandEnabledCheckBox.Unchecked += delegate
            {
                if (initializing || switchingProfile) return;
                settings.ArmbandEnabled = false;
                UpdateArmbandEnabledUi();
                CommitColorChange("已停用臂环增强（参数保留）");
            };
        }

        /// <summary>
        /// 面板内改动统一收尾：切到自定义预设、落盘、重算推荐值。
        /// 关掉总开关时下面控件置灰，让「数值还在但不生效」这件事一眼可见。
        /// </summary>
        private void CommitColorChange(string message)
        {
            if (PresetComboBox.SelectedIndex != 5)
            {
                promotingCustomPreset = true;
                PresetComboBox.SelectedIndex = 5;
                promotingCustomPreset = false;
            }
            SyncSettingsFromSliders();
            SaveSettings();
            UpdateCurrentRecommendation();
            if (!string.IsNullOrEmpty(message))
                ShowNotification(message, NotificationType.Success);
        }

        private void UpdateFocusEnabledUi()
        {
            if (FocusControlsPanel == null) return;
            FocusControlsPanel.IsEnabled = FocusEnabledCheckBox.IsChecked == true;
        }

        private void UpdateArmbandEnabledUi()
        {
            if (ArmbandControlsPanel == null) return;
            ArmbandControlsPanel.IsEnabled = ArmbandEnabledCheckBox.IsChecked == true;
        }

        private void BindFocusHuePreset(Button button, int hue, string label)
        {
            if (button == null) return;
            button.Click += delegate
            {
                bool startedFromOff = FocusStrengthSlider.Value < 1.0;
                try
                {
                    // 和 ApplySettingsToSliders 一样：铺值期间不能触发
                    // 「跳自定义预设 / 重算推荐值」，这里手动补一次。
                    initializing = true;
                    FocusHueSlider.Value = hue;
                    if (startedFromOff)
                        FocusStrengthSlider.Value = DefaultFocusStrengthOnPick;
                }
                finally
                {
                    initializing = false;
                }
                RefreshFocusHueVisual();
                CommitColorChange(startedFromOff
                    ? "目标色调：" + label + "，聚焦强度已设为 " + DefaultFocusStrengthOnPick
                    : "目标色调：" + label);
            };
        }

        private void RefreshFocusHueVisual()
        {
            UpdateFocusHueSwatch(FocusHueSlider.Value);
        }

        private void UpdateFocusHueSwatch(double hue)
        {
            if (FocusHueSwatch == null) return;
            try
            {
                // 色卡只表示目标色相本身，用满饱和的纯色，V 压到 0.8
                // 免得在深色面板上过曝成白块。
                double h = hue % 360.0;
                if (h < 0.0) h += 360.0;
                double sectorPosition = h / 60.0;
                int sector = (int)Math.Floor(sectorPosition) % 6;
                double fraction = sectorPosition - Math.Floor(sectorPosition);
                const double value = 0.80;
                double falling = value * (1.0 - fraction);
                double rising = value * fraction;
                double red;
                double green;
                double blue;
                switch (sector)
                {
                    case 0: red = value; green = rising; blue = 0.0; break;
                    case 1: red = falling; green = value; blue = 0.0; break;
                    case 2: red = 0.0; green = value; blue = rising; break;
                    case 3: red = 0.0; green = falling; blue = value; break;
                    case 4: red = rising; green = 0.0; blue = value; break;
                    default: red = value; green = 0.0; blue = falling; break;
                }
                FocusHueSwatch.Background = new SolidColorBrush(ToMediaColor(
                    red, green, blue));
            }
            catch { }
        }

        private static Color ToMediaColor(double red, double green, double blue)
        {
            return Color.FromRgb(
                (byte)Math.Round(MathUtil.Clamp(red, 0.0, 1.0) * 255.0),
                (byte)Math.Round(MathUtil.Clamp(green, 0.0, 1.0) * 255.0),
                (byte)Math.Round(MathUtil.Clamp(blue, 0.0, 1.0) * 255.0));
        }

        private void BindDisplayEvents()
        {
            DisplayComboBox.PreviewMouseLeftButtonDown += ForceOpenComboBox;
            DisplayModeComboBox.PreviewMouseLeftButtonDown += ForceOpenComboBox;
            PresetComboBox.PreviewMouseLeftButtonDown += ForceOpenComboBox;
            RefreshDisplayButton.Click += delegate { RefreshDisplayList(true); };
            DisplayModeComboBox.SelectionChanged += delegate
            {
                if (initializing || updatingDisplayList ||
                    DisplayModeComboBox.SelectedIndex < 0) return;
                bool nextMulti = DisplayModeComboBox.SelectedIndex == 1;
                if (settings.MultiDisplayMode == nextMulti) return;

                bool wasRunning = IsFilterRunning();
                string error;
                gammaController.RestoreActive(out error);
                settings.MultiDisplayMode = nextMulti;
                if (nextMulti) BuildDisplaySelectionControls();
                ApplyDisplayModeUi();
                RefreshMonitorCapabilities();
                if (wasRunning && currentAnalysis != null && currentAnalysis.IsUsable)
                    ApplyRecommendation(currentAnalysis);
                else
                    UpdateModeUi();
                SaveSettings();
                ShowNotification(nextMulti ? "已切换到多屏模式" : "已切换到单屏模式",
                    NotificationType.Success);
            };
            DisplayComboBox.SelectionChanged += delegate
            {
                if (initializing || updatingDisplayList) return;
                var selected = DisplayComboBox.SelectedItem as DisplayTarget;
                if (selected == null || string.IsNullOrWhiteSpace(selected.DeviceName)) return;

                bool wasRunning = IsFilterRunning();
                string error;
                gammaController.RestoreActive(out error);
                displayDevice = selected.DeviceName;
                settings.DisplayDevice = displayDevice;
                RefreshMonitorCapabilities();
                if (wasRunning && currentAnalysis != null && currentAnalysis.IsUsable)
                    ApplyRecommendation(currentAnalysis);
                else
                    UpdateModeUi();
                SaveSettings();
                ShowNotification("目标显示器已切换", NotificationType.Success);
            };

            DisplayComboBox.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!DisplayComboBox.IsDropDownOpen) e.Handled = true;
            };
            DisplayModeComboBox.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!DisplayModeComboBox.IsDropDownOpen) e.Handled = true;
            };
            PresetComboBox.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!PresetComboBox.IsDropDownOpen) e.Handled = true;
            };
        }

        private void OnDisplaySelectionChanged(object sender, RoutedEventArgs e)
        {
            if (initializing || updatingDisplaySelection) return;
            SyncSelectedDisplaysFromUi();
            bool wasRunning = IsFilterRunning();
            if (wasRunning)
            {
                string error;
                gammaController.RestoreActive(out error);
                if (currentAnalysis != null && currentAnalysis.IsUsable)
                    ApplyRecommendation(currentAnalysis);
            }
            SaveSettings();
            if (!wasRunning) UpdateModeUi();
        }

        private static void ForceOpenComboBox(object sender, MouseButtonEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox == null || comboBox.IsDropDownOpen) return;
            comboBox.IsDropDownOpen = true;
            e.Handled = true;
        }

        private void BindProcessWatchEvents()
        {
            ProcessComboBox.PreviewMouseLeftButtonDown += ForceOpenComboBox;
            ProcessComboBox.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!ProcessComboBox.IsDropDownOpen) e.Handled = true;
            };
            ProcessWatchCheckBox.Checked += delegate
            {
                bool wasRunning = IsFilterRunning();
                settings.ProcessWatchEnabled = true;
                settings.ProcessWatchConfigured = true;
                bool detected = IsWatchedProcessActive();
                processWasDetected = detected;
                RefreshProcessList();
                BuildProcessSelectionControls();
                if (wasRunning && !detected)
                    PauseFilterForProcess();
                UpdateProcessWatchUi();
                if (!initializing) SaveSettings();
            };
            ProcessWatchCheckBox.Unchecked += delegate
            {
                settings.ProcessWatchEnabled = false;
                settings.ProcessWatchConfigured = true;
                autoPausedByProcess = false;
                UpdateProcessWatchUi();
                if (!initializing) SaveSettings();
            };
            ProcessComboBox.SelectionChanged += delegate
            {
                if (initializing) return;
                string selected = ProcessComboBox.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(selected)) return;
                // 下拉框改为“添加”语义：选中运行中进程即加入同时侦听列表，而非替换。
                if (settings.WatchedProcessNames == null)
                    settings.WatchedProcessNames = new List<string>();
                bool exists = false;
                foreach (string name in settings.WatchedProcessNames)
                {
                    if (string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    settings.WatchedProcessNames.Add(selected);
                    settings.WatchedProcessName = selected;
                }
                BuildProcessSelectionControls();
                processWasDetected = IsWatchedProcessActive();
                UpdateProcessWatchUi();
                if (!initializing) SaveSettings();
            };
        }

        private void InitializeProcessWatcher()
        {
            settings.Normalize();
            RefreshProcessList();
            BuildProcessSelectionControls();
            ProcessWatchCheckBox.IsChecked = settings.ProcessWatchEnabled;
            processWasDetected = IsWatchedProcessActive();
            UpdateProcessWatchUi(processWasDetected);
            foregroundWindowEventHandler = OnForegroundWindowChanged;
            foregroundWindowEventHook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                foregroundWindowEventHandler,
                0,
                0,
                WineventOutOfContext);
            processWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            processWatchTimer.Tick += delegate { EvaluateProcessWatch(); };
            processWatchTimer.Start();
        }

        private void RefreshProcessList()
        {
            var names = new List<string>();
            foreach (string defaults in AppSettings.DefaultWatchedProcessNames)
            {
                if (!names.Contains(defaults, StringComparer.OrdinalIgnoreCase))
                    names.Add(defaults);
            }
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        string name = process.ProcessName + ".exe";
                        if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch { }
            names.Sort(StringComparer.OrdinalIgnoreCase);

            bool wasInitializing = initializing;
            initializing = true;
            try
            {
                ProcessComboBox.Items.Clear();
                foreach (string name in names) ProcessComboBox.Items.Add(name);
                // 下拉框不再代表“唯一侦听”，默认选中第一个已侦听项，仅用于继续添加。
                string hint = null;
                if (settings.WatchedProcessNames != null && settings.WatchedProcessNames.Count > 0)
                    hint = settings.WatchedProcessNames[0];
                if (!string.IsNullOrWhiteSpace(hint) && names.Contains(hint, StringComparer.OrdinalIgnoreCase))
                    ProcessComboBox.SelectedItem = names.Find(delegate(string n) { return string.Equals(n, hint, StringComparison.OrdinalIgnoreCase); });
                else if (names.Count > 0)
                    ProcessComboBox.SelectedIndex = 0;
            }
            finally
            {
                initializing = wasInitializing;
            }
        }

        private void BuildProcessSelectionControls()
        {
            if (ProcessSelectionPanel == null) return;
            updatingProcessSelection = true;
            try
            {
                ProcessSelectionPanel.Children.Clear();
                if (settings.WatchedProcessNames == null || settings.WatchedProcessNames.Count == 0)
                {
                    var empty = new TextBlock
                    {
                        Text = "暂无侦听进程，请勾选原版/竞技场或从下拉框添加。",
                        FontFamily = (System.Windows.Media.FontFamily)FindResource("FontUi"),
                        FontSize = 10,
                        Foreground = (System.Windows.Media.Brush)FindResource("PhosphorDimBrush"),
                        TextWrapping = TextWrapping.Wrap
                    };
                    ProcessSelectionPanel.Children.Add(empty);
                    return;
                }
                foreach (string name in settings.WatchedProcessNames)
                {
                    string captured = name;
                    var checkBox = new CheckBox
                    {
                        Content = AppSettings.GetFriendlyProcessName(captured) + "｜" + captured,
                        Tag = captured,
                        IsChecked = true,
                        Style = (Style)FindResource("TacticalCheckBox"),
                        Margin = new Thickness(0, 0, 0, 6),
                        ToolTip = "取消勾选即停止侦听此进程，可同时侦听原版+竞技场。"
                    };
                    checkBox.Checked += OnProcessSelectionChanged;
                    checkBox.Unchecked += OnProcessSelectionChanged;
                    ProcessSelectionPanel.Children.Add(checkBox);
                }
            }
            finally
            {
                updatingProcessSelection = false;
            }
        }

        private void OnProcessSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (initializing || updatingProcessSelection) return;
            var remaining = new List<string>();
            if (ProcessSelectionPanel != null)
            {
                foreach (UIElement element in ProcessSelectionPanel.Children)
                {
                    var checkBox = element as CheckBox;
                    string name = checkBox == null ? null : checkBox.Tag as string;
                    if (checkBox != null && checkBox.IsChecked == true &&
                        !string.IsNullOrWhiteSpace(name)) remaining.Add(name);
                }
            }
            if (remaining.Count == 0)
            {
                ShowNotification("请至少保留一个侦听进程", NotificationType.Warning);
                BuildProcessSelectionControls();
                return;
            }
            settings.WatchedProcessNames = remaining;
            settings.WatchedProcessName = remaining[0];
            processWasDetected = IsWatchedProcessActive();
            UpdateProcessWatchUi();
            SaveSettings();
        }

        private List<string> GetWatchedProcessNames()
        {
            var result = new List<string>();
            if (settings.WatchedProcessNames != null)
            {
                foreach (string name in settings.WatchedProcessNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        result.Add(Path.GetFileName(name));
                }
            }
            if (result.Count == 0 && !string.IsNullOrWhiteSpace(settings.WatchedProcessName))
                result.Add(Path.GetFileName(settings.WatchedProcessName));
            return result;
        }

        private string GetActiveWatchedProcessName()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return null;
            uint processId;
            if (GetWindowThreadProcessId(foregroundWindow, out processId) == 0 ||
                processId == 0) return null;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    string foreground = process.ProcessName;
                    foreach (string watched in GetWatchedProcessNames())
                    {
                        string baseName = Path.GetFileNameWithoutExtension(watched);
                        if (string.Equals(foreground, baseName, StringComparison.OrdinalIgnoreCase))
                            return watched;
                    }
                }
            }
            catch { }
            return null;
        }

        private bool IsWatchedProcessRunning()
        {
            List<string> watched = GetWatchedProcessNames();
            if (watched.Count == 0) return false;
            try
            {
                foreach (string full in watched)
                {
                    string baseName = Path.GetFileNameWithoutExtension(full);
                    if (string.IsNullOrWhiteSpace(baseName)) continue;
                    Process[] processes = Process.GetProcessesByName(baseName);
                    try
                    {
                        if (processes.Length > 0) return true;
                    }
                    finally
                    {
                        foreach (Process process in processes) process.Dispose();
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsWatchedProcessActive()
        {
            return GetActiveWatchedProcessName() != null;
        }

        private void BindProfileEvents()
        {
            ProfileEftButton.Click += delegate
            {
                SelectProfileManually(AppSettings.ProfileKeyEft);
            };
            ProfileArenaButton.Click += delegate
            {
                SelectProfileManually(AppSettings.ProfileKeyArena);
            };
            ProfileFollowGameCheckBox.Checked += delegate
            {
                if (initializing || switchingProfile) return;
                settings.ProfileFollowGame = true;
                // 重新交给自动，之前那次手动选择不再算数。
                manualProfileOverride = false;
                SyncProfileToForeground();
                SaveSettings();
            };
            ProfileFollowGameCheckBox.Unchecked += delegate
            {
                if (initializing || switchingProfile) return;
                settings.ProfileFollowGame = false;
                SaveSettings();
            };
        }

        // GetActiveWatchedProcessName 走 Win32 GetForegroundWindow，只能 UI 线程调。
        private int ResolveForegroundGameKey()
        {
            string foreground = GetActiveWatchedProcessName();
            if (string.IsNullOrWhiteSpace(foreground)) return activeProfileKey;
            return AppSettings.ResolveGameKey(foreground);
        }

        // 手动选档案只决定「现在编辑哪一份」，不再顺手关掉跟随。
        // 以前点按钮会强制 ProfileFollowGame = false，用户勾好的跟随就这样没了。
        private void SelectProfileManually(int key)
        {
            if (initializing) return;
            if (settings.ProfileFollowGame)
            {
                manualProfileOverride = true;
                manualProfileOverrideKey = key;
                manualProfileAnchorKey = ResolveForegroundGameKey();
            }
            SwitchProfile(key);
            SaveSettings();
        }

        private int ResolveDesiredProfileKey()
        {
            if (!settings.ProfileFollowGame) return settings.LockedProfileKey;
            string foreground = GetActiveWatchedProcessName();
            // 没有游戏在前台时保持现状，避免刚切出去就被打回原版。
            if (string.IsNullOrWhiteSpace(foreground)) return activeProfileKey;
            int foregroundKey = AppSettings.ResolveGameKey(foreground);
            if (manualProfileOverride)
            {
                // 前台还是同一个游戏，尊重刚才那次手动选择。
                if (foregroundKey == manualProfileAnchorKey)
                    return manualProfileOverrideKey;
                // 前台已经换游戏了，交回自动。
                manualProfileOverride = false;
            }
            return foregroundKey;
        }

        private void SyncProfileToForeground()
        {
            if (initializing || closing || switchingProfile) return;
            SwitchProfile(ResolveDesiredProfileKey());
        }

        private void SwitchProfile(int newKey)
        {
            if (newKey == activeProfileKey) return;
            if (initializing || PresetComboBox == null)
            {
                activeProfileKey = newKey;
                return;
            }
            switchingProfile = true;
            try
            {
                // 先把界面上的当前值收进旧档案，再把新档案铺回界面。
                SyncSettingsFromSliders();
                settings.GetProfile(activeProfileKey).CaptureFrom(settings);
                activeProfileKey = newKey;
                settings.LockedProfileKey = newKey;
                settings.GetProfile(newKey).ApplyTo(settings);
                ApplySettingsToSliders();
                RefreshSliderLabels();
            }
            finally
            {
                switchingProfile = false;
            }
            // EMA 带着上一个游戏的旧场景会慢半拍，切档必须清掉。
            if (realtimeController != null) realtimeController.ResetSceneState();
            Diagnostics.Info("档案", "切换到「" + AppSettings.GetProfileLabel(newKey) + "」" +
                (settings.ProfileFollowGame ? "（跟随游戏）" : "（已锁定）"));
            UpdateCurrentRecommendation();
            UpdateProfileUi();
            SaveSettings();
        }

        private void UpdateProfileUi()
        {
            if (ProfileStatusText == null || settings == null) return;
            try
            {
                bool eft = activeProfileKey == AppSettings.ProfileKeyEft;
                string suffix;
                if (!settings.ProfileFollowGame) suffix = "（已锁定）";
                else if (manualProfileOverride) suffix = "（跟随游戏·手动选择）";
                else suffix = "（跟随游戏）";
                ProfileStatusText.Text = "当前生效：" +
                    AppSettings.GetProfileLabel(activeProfileKey) + suffix;
                Brush active = (Brush)FindResource("AmberAlertBrush");
                Brush idleBorder = (Brush)FindResource("BorderStrongBrush");
                Brush idleText = (Brush)FindResource("PhosphorDimBrush");
                ProfileEftButton.BorderBrush = eft ? active : idleBorder;
                ProfileEftButton.Foreground = eft ? active : idleText;
                ProfileArenaButton.BorderBrush = eft ? idleBorder : active;
                ProfileArenaButton.Foreground = eft ? idleText : active;
            }
            catch { }
        }

        private void EvaluateProcessWatch()
        {
            if (!settings.ProcessWatchEnabled) return;
            bool detected = IsWatchedProcessActive();
            bool stateChanged = processWasDetected != detected;
            if (stateChanged && processWasDetected && !detected && IsFilterRunning())
                PauseFilterForProcess();
            else if (stateChanged && !processWasDetected && detected && autoPausedByProcess)
                ResumeFilterForProcess();
            processWasDetected = detected;
            UpdateProcessWatchUi(detected);
        }

        private void OnForegroundWindowChanged(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThreadId,
            uint eventTime)
        {
            if (closing || Dispatcher.HasShutdownStarted) return;
            if (Interlocked.Exchange(ref processWatchEvaluationQueued, 1) != 0) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        // 放在 EvaluateProcessWatch 之前：那个方法在“滤镜自动启停”
                        // 没开时会直接返回，而档案跟随应当独立生效。
                        SyncProfileToForeground();
                        EvaluateProcessWatch();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref processWatchEvaluationQueued, 0);
                    }
                }));
            }
            catch
            {
                Interlocked.Exchange(ref processWatchEvaluationQueued, 0);
            }
        }

        private void PauseFilterForProcess()
        {
            string error;
            if (!gammaController.RestoreAll(out error))
            {
                ShowNotification("自动关闭滤镜失败：" + error, NotificationType.Warning);
                return;
            }
            RecoveryStore.Clear();
            autoPausedByProcess = true;
            ObsFilterStateStore.WriteDisabled();
            UpdateModeUi();
            ShowNotification("已离开游戏，滤镜已自动关闭", NotificationType.Info);
        }

        private void ResumeFilterForProcess()
        {
            autoPausedByProcess = false;
            if (currentAnalysis != null && currentAnalysis.IsUsable)
            {
                ApplyRecommendation(currentAnalysis);
                ShowNotification("已回到游戏，滤镜已自动开启", NotificationType.Success);
            }
            else
            {
                UpdateModeUi();
                ShowNotification("已回到游戏，请先分析一张截图", NotificationType.Info);
            }
        }

        private void UpdateProcessWatchUi()
        {
            UpdateProcessWatchUi(IsWatchedProcessActive());
        }

        private void UpdateProcessWatchUi(bool active)
        {
            bool enabled = settings.ProcessWatchEnabled;
            DateTime now = DateTime.UtcNow;
            bool stateChanged = !processWatchUiInitialized ||
                enabled != lastProcessWatchUiEnabled ||
                active != lastProcessWatchUiActive;
            bool refreshDue = (now - lastProcessWatchUiRefreshUtc).TotalSeconds >= 2.0;
            if (!stateChanged && !refreshDue) return;

            ProcessWatchControls.IsEnabled = enabled;
            ProcessWatchControls.Opacity = enabled ? 1.0 : 0.45;
            if (!enabled)
            {
                ProcessWatchStatusText.Text = "未启用";
            }
            else if (active)
            {
                string foreground = GetActiveWatchedProcessName();
                ProcessWatchStatusText.Text = string.IsNullOrWhiteSpace(foreground) ? "游戏前台" :
                    AppSettings.GetFriendlyProcessName(foreground) + "前台";
            }
            else
            {
                ProcessWatchStatusText.Text =
                    (IsWatchedProcessRunning() ? "进程后台运行" : "等待进程");
            }
            processWatchUiInitialized = true;
            lastProcessWatchUiEnabled = enabled;
            lastProcessWatchUiActive = active;
            lastProcessWatchUiRefreshUtc = now;
        }

        private void BindRealtimeEvents()
        {
            RealtimeCheckBox.Checked += delegate
            {
                if (initializing || updatingRealtimeControls) return;
                settings.RealtimeEnabled = true;
                SaveSettings();
                UpdateRealtimeStatus("采样中");
                UpdateModeUi();
                if (realtimeController != null) realtimeController.Wake();
                ShowNotification("实时全自动已开启", NotificationType.Success);
            };
            RealtimeCheckBox.Unchecked += delegate
            {
                if (initializing || updatingRealtimeControls) return;
                settings.RealtimeEnabled = false;
                SaveSettings();
                UpdateRealtimeStatus("未启用");
                UpdateModeUi();
            };
            RealtimeIntervalSlider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                int ms = (int)Math.Round(e.NewValue);
                RealtimeIntervalValue.Text = (ms / 1000.0).ToString("0.0") + "s";
                if (initializing || updatingRealtimeControls) return;
                settings.RealtimeIntervalMs = Math.Max(600, Math.Min(3000, ms));
                settingsTimer.Stop();
                settingsTimer.Start();
                if (realtimeController != null) realtimeController.Wake();
            };
            RealtimeSensitivityComboBox.SelectionChanged += delegate
            {
                if (initializing || updatingRealtimeControls ||
                    RealtimeSensitivityComboBox.SelectedIndex < 0) return;
                settings.RealtimeSensitivity = Math.Max(0, Math.Min(2,
                    RealtimeSensitivityComboBox.SelectedIndex));
                SaveSettings();
                if (realtimeController != null) realtimeController.Wake();
            };
        }

        private void ApplyRealtimeSettingsToUi()
        {
            updatingRealtimeControls = true;
            try
            {
                RealtimeCheckBox.IsChecked = settings.RealtimeEnabled;
                RealtimeIntervalSlider.Value = Math.Max(
                    RealtimeIntervalSlider.Minimum, Math.Min(
                        RealtimeIntervalSlider.Maximum, settings.RealtimeIntervalMs));
                RealtimeIntervalValue.Text =
                    (settings.RealtimeIntervalMs / 1000.0).ToString("0.0") + "s";
                RealtimeSensitivityComboBox.SelectedIndex = Math.Max(0, Math.Min(2,
                    settings.RealtimeSensitivity));
            }
            finally
            {
                updatingRealtimeControls = false;
            }
            UpdateRealtimeStatus(settings.RealtimeEnabled ? "采样中" : "未启用");
        }

        private void SyncRealtimeSettingsFromUi()
        {
            settings.RealtimeEnabled = RealtimeCheckBox.IsChecked == true;
            settings.RealtimeIntervalMs = Math.Max(600, Math.Min(3000,
                (int)Math.Round(RealtimeIntervalSlider.Value)));
            if (RealtimeSensitivityComboBox.SelectedIndex >= 0)
                settings.RealtimeSensitivity = Math.Max(0, Math.Min(2,
                    RealtimeSensitivityComboBox.SelectedIndex));
        }

        private void InitializeRealtime()
        {
            if (realtimeController != null) return;
            realtimeController = new RealtimeController {
                AcquireSettings = delegate
                {
                    try
                    {
                        if (closing || Dispatcher.HasShutdownStarted) return settings;
                        AppSettings snapshot = null;
                        // 用 BeginInvoke + 带超时的 Wait，而不是 Dispatcher.Invoke 的
                        // 超时重载：那个重载会和 Invoke(Delegate, TimeSpan,
                        // DispatcherPriority, params object[]) 撞车，后三个参数被当成
                        // 动作参数塞进 0 参数的 Action，运行时直接
                        // TargetParameterCountException。
                        // 带超时是因为 UI 线程此刻可能正阻塞在 Dispose 的 loop.Wait 里。
                        DispatcherOperation operation = Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(delegate { snapshot = CreateSettingsSnapshot(); }));
                        if (operation.Wait(TimeSpan.FromMilliseconds(500)) !=
                            DispatcherOperationStatus.Completed) return settings;
                        return snapshot ?? settings;
                    }
                    catch { return settings; }
                },
                AcquireTargets = GetRealtimeTargetDisplayDevices,
                IsRealtimeEnabled = delegate { return settings.RealtimeEnabled; },
                IsGameForeground = delegate
                {
                    if (!settings.ProcessWatchEnabled) return true;
                    return IsWatchedProcessActive();
                },
                IsManualFilterOn = delegate { return filterModeEnabled; },
                IsClosing = delegate { return closing; }
            };
            realtimeController.StableScene += OnRealtimeFrame;
            realtimeController.StatusChanged += OnRealtimeStatus;
            realtimeController.Faulted += OnRealtimeFaulted;
            realtimeController.Start();
        }

        private void OnRealtimeFrame(RealtimeFrame frame)
        {
            if (closing || Dispatcher.HasShutdownStarted)
            {
                frame.Dispose();
                return;
            }
            int version = Interlocked.Increment(ref realtimeVersion);
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closing || version != realtimeVersion)
                    {
                        frame.Dispose();
                        return;
                    }
                    currentAnalysis = frame.Analysis;
                    lastAnalyzedPath = frame.Analysis == null ? lastAnalyzedPath : frame.Analysis.FilePath;
                    previewControl.SetContent(
                        frame.OriginalPreview, frame.FilteredPreview, currentAnalysis);
                    frame.OriginalPreview = null;
                    frame.FilteredPreview = null;
                    PreviewEmptyState.Visibility = Visibility.Collapsed;
                    UpdateAnalysisMetrics(currentAnalysis);
                    // 手动关闭时只更新预览，不抢回滤镜，避免和用户意图打架。
                    if (filterModeEnabled && currentAnalysis != null && currentAnalysis.IsUsable)
                        ApplyRecommendation(currentAnalysis, false, true);
                    else
                        UpdateModeUi();
                }));
            }
            catch { frame.Dispose(); }
        }

        private void OnRealtimeStatus(string status)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!closing) UpdateRealtimeStatus(status);
                }));
            }
            catch { }
        }

        private void OnRealtimeFaulted(string message)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!closing) ShowNotification(message, NotificationType.Warning);
                }));
            }
            catch { }
        }

        private void UpdateRealtimeStatus(string status)
        {
            status = status ?? "";
            if (RealtimeStatusText != null) RealtimeStatusText.Text = status;
            if (RealtimeStateText == null) return;
            // 顶栏那条只放最短的形式，长状态（例如抓屏失败的整句话）留在面板里。
            string shortStatus;
            string brush;
            switch (status)
            {
                case "稳定":
                    shortStatus = "稳定"; brush = "TerminalGreenBrush"; break;
                case "切换中":
                    shortStatus = "跟随中"; brush = "AmberAlertBrush"; break;
                case "场景确认中":
                    shortStatus = "确认中"; brush = "AmberAlertBrush"; break;
                case "未启用":
                    shortStatus = "未启用"; brush = "PhosphorDimBrush"; break;
                case "已手动关闭":
                    shortStatus = "已暂停"; brush = "PhosphorDimBrush"; break;
                case "等待游戏前台":
                    shortStatus = "等前台"; brush = "PhosphorDimBrush"; break;
                default:
                    // 抓屏失败 / 没有可用的显示器之类
                    shortStatus = "异常"; brush = "HazardRedBrush"; break;
            }
            RealtimeStateText.Text = shortStatus;
            RealtimeStateText.Foreground = (System.Windows.Media.Brush)FindResource(brush);
            RealtimeStateText.ToolTip = status;
        }

        private void RefreshMonitorCapabilities()
        {
            int probeVersion = Interlocked.Increment(ref monitorProbeVersion);
            if (settings.MultiDisplayMode)
            {
                ApplyMonitorCapabilities(new MonitorCapabilities
                {
                    Detail = "多屏模式下请分别在各显示器上调整硬件亮度和对比度。"
                });
            }
            else if (string.IsNullOrWhiteSpace(displayDevice))
            {
                ApplyMonitorCapabilities(new MonitorCapabilities());
            }
            else
            {
                string targetDevice = displayDevice;
                ApplyMonitorCapabilities(new MonitorCapabilities
                {
                    Detail = "正在检测显示器 DDC/CI…"
                });
                Task.Factory.StartNew(delegate
                {
                    return ddcController.Probe(targetDevice);
                }).ContinueWith(delegate(Task<MonitorCapabilities> task)
                {
                    if (closing || Dispatcher.HasShutdownStarted ||
                        task.IsCanceled || task.IsFaulted ||
                        probeVersion != monitorProbeVersion)
                        return;
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (!closing && probeVersion == monitorProbeVersion &&
                                !settings.MultiDisplayMode &&
                                string.Equals(displayDevice, targetDevice,
                                    StringComparison.OrdinalIgnoreCase))
                                ApplyMonitorCapabilities(task.Result);
                        }));
                    }
                    catch { }
                });
                return;
            }
        }

        private void ApplyMonitorCapabilities(MonitorCapabilities capabilities)
        {
            monitorCapabilities = capabilities ?? new MonitorCapabilities();
            updatingHardwareControls = true;
            try
            {
                bool supported = monitorCapabilities.BrightnessSupported ||
                    monitorCapabilities.ContrastSupported;
                MonitorHardwarePanel.Opacity = supported ? 1.0 : 0.48;
                MonitorHardwarePanel.ToolTip = supported ? null :
                    (settings.MultiDisplayMode ?
                        "多屏模式不提供统一的 DDC/CI 硬件控制。" :
                        "硬件无法使用：当前显示器未提供 DDC/CI 亮度或对比度控制。");
                MonitorHardwareDetail.Text = supported ? monitorCapabilities.Detail :
                    "硬件无法使用：" + monitorCapabilities.Detail;
                ConfigureMonitorSlider(MonitorBrightnessSlider, MonitorBrightnessValue,
                    monitorCapabilities.BrightnessSupported,
                    monitorCapabilities.Brightness, monitorCapabilities.BrightnessMaximum);
                ConfigureMonitorSlider(MonitorContrastSlider, MonitorContrastValue,
                    monitorCapabilities.ContrastSupported,
                    monitorCapabilities.Contrast, monitorCapabilities.ContrastMaximum);
            }
            finally
            {
                updatingHardwareControls = false;
            }
        }

        private static void ConfigureMonitorSlider(Slider slider, TextBlock value,
            bool supported, int current, int maximum)
        {
            slider.Maximum = Math.Max(1, maximum);
            slider.IsEnabled = supported;
            slider.Value = Math.Max(0, Math.Min(slider.Maximum, current));
            value.Text = supported ? ((int)Math.Round(slider.Value)).ToString() : "--";
        }

        private void OnMonitorSliderChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (initializing || updatingHardwareControls || string.IsNullOrWhiteSpace(displayDevice)) return;
            Slider slider = sender as Slider;
            int value = (int)Math.Round(e.NewValue);
            if (slider == MonitorBrightnessSlider)
            {
                MonitorBrightnessValue.Text = value.ToString();
                pendingBrightness = value;
            }
            else if (slider == MonitorContrastSlider)
            {
                MonitorContrastValue.Text = value.ToString();
                pendingContrast = value;
            }
            else
            {
                return;
            }
            pendingDdcDevice = displayDevice;
            if (ddcDebounceTimer == null)
            {
                ddcDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
                ddcDebounceTimer.Tick += delegate
                {
                    ddcDebounceTimer.Stop();
                    FlushPendingDdc();
                };
            }
            ddcDebounceTimer.Stop();
            ddcDebounceTimer.Start();
        }

        private void FlushPendingDdc()
        {
            if (string.IsNullOrWhiteSpace(pendingDdcDevice)) return;
            int brightness = pendingBrightness;
            int contrast = pendingContrast;
            string targetDevice = pendingDdcDevice;
            pendingBrightness = -1;
            pendingContrast = -1;
            Task.Factory.StartNew(delegate
            {
                var result = new DdcWriteResult();
                if (brightness >= 0)
                {
                    string brightnessError = "";
                    result.BrightnessOk = ddcController.SetBrightness(targetDevice, brightness, out brightnessError);
                    result.BrightnessError = brightnessError;
                }
                if (contrast >= 0)
                {
                    string contrastError = "";
                    result.ContrastOk = ddcController.SetContrast(targetDevice, contrast, out contrastError);
                    result.ContrastError = contrastError;
                }
                return result;
            }).ContinueWith(delegate(Task<DdcWriteResult> task)
            {
                if (closing || Dispatcher.HasShutdownStarted) return;
                if (task.IsFaulted || task.IsCanceled) return;
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (closing) return;
                        DdcWriteResult result = task.Result;
                        if (!result.BrightnessOk && !string.IsNullOrWhiteSpace(result.BrightnessError))
                            ShowNotification(result.BrightnessError, NotificationType.Warning);
                        else if (!result.ContrastOk && !string.IsNullOrWhiteSpace(result.ContrastError))
                            ShowNotification(result.ContrastError, NotificationType.Warning);
                    }));
                }
                catch { }
            });
        }

        private sealed class DdcWriteResult
        {
            public bool BrightnessOk = true;
            public string BrightnessError = "";
            public bool ContrastOk = true;
            public string ContrastError = "";
        }

        private void BindPresetEvents()
        {
            PresetComboBox.SelectionChanged += delegate
            {
                // switchingProfile：切档时是程序在铺值，不能把新档案的预设又覆盖掉。
                if (initializing || switchingProfile || PresetComboBox.SelectedIndex < 0) return;
                int index = PresetComboBox.SelectedIndex;
                if (promotingCustomPreset) return;
                if (index == 5)
                {
                    try
                    {
                        initializing = true;
                        ApplyCustomPresetValues();
                    }
                    finally
                    {
                        initializing = false;
                    }
                    RefreshSliderLabels();
                    SyncSettingsFromSliders();
                    SaveSettings();
                    UpdateCurrentRecommendation();
                    ShowNotification("已加载自定义预设", NotificationType.Success);
                    return;
                }
                PresetValues preset = GetBuiltInPreset(index);
                try
                {
                    initializing = true;
                    ApplyPresetValues(preset);
                }
                finally
                {
                    initializing = false;
                }
                SyncSettingsFromSliders();
                SaveSettings();
                UpdateCurrentRecommendation();
                ShowNotification("预设已加载", NotificationType.Success);
            };
        }

        private void ApplyCustomPresetValues()
        {
            ShadowSlider.Value = settings.CustomShadowTarget;
            HighlightSlider.Value = settings.CustomHighlightProtection;
            ColorSlider.Value = settings.CustomColorCorrection;
            IndoorSlider.Value = settings.CustomIndoorComfort;
            GuardSlider.Value = settings.CustomSceneGuard;
            BlackSlider.Value = settings.CustomBlackPoint;
            StrengthSlider.Value = settings.CustomMaxStrength;
            ExposureSlider.Value = settings.CustomExposureBias;
            ContrastSlider.Value = settings.CustomContrastBias;
            WarmthSlider.Value = settings.CustomWarmth;
            SaturationSlider.Value = settings.CustomSaturationBias;
            FocusHueSlider.Value = settings.CustomFocusHue;
            FocusStrengthSlider.Value = settings.CustomFocusStrength;
            FocusRangeSlider.Value = settings.CustomFocusRange;
            TrimRedSlider.Value = settings.CustomTrimRed;
            TrimGreenSlider.Value = settings.CustomTrimGreen;
            TrimBlueSlider.Value = settings.CustomTrimBlue;
            FocusEnabledCheckBox.IsChecked = settings.CustomFocusEnabled;
            ArmbandEnabledCheckBox.IsChecked = settings.CustomArmbandEnabled;
            ArmbandLiftSlider.Value = settings.CustomArmbandLift;
            ArmbandDesaturateSlider.Value = settings.CustomArmbandDesaturate;
            UpdateFocusEnabledUi();
            UpdateArmbandEnabledUi();
        }

        private static PresetValues GetBuiltInPreset(int index)
        {
            switch (index)
            {
                case 1:
                    // Current Tarkov samples contain dark red and muted green
                    // lighting. Keep this preset neutral instead of removing
                    // too much of the scene's own color cast.
                    return new PresetValues(55, 86, 52, 76, 91, 50, 64, -1, -2, -1, -1);
                case 2:
                    // More shadow reach for indoor corridors, with a softer
                    // contrast and neutral color balance than the old preset.
                    return new PresetValues(74, 90, 78, 96, 92, 54, 74, 2, 1, 0, 1);
                case 3:
                    // Preserve the game's green night lighting while keeping
                    // highlights and black levels restrained.
                    return new PresetValues(58, 97, 58, 90, 100, 44, 64, 1, 2, -2, 0);
                case 4:
                    // Lift shadowed buildings without overexposing the sky.
                    return new PresetValues(62, 100, 68, 72, 100, 52, 70, -2, -1, -1, -2);
                default:
                    return new PresetValues(70, 76, 72, 72, 88, 56, 82, 0, 0, 0, 0);
            }
        }

        private void ApplyPresetValues(PresetValues preset)
        {
            ShadowSlider.Value = preset.Shadow;
            HighlightSlider.Value = preset.Highlight;
            ColorSlider.Value = preset.Color;
            IndoorSlider.Value = preset.Indoor;
            GuardSlider.Value = preset.Guard;
            BlackSlider.Value = preset.Black;
            StrengthSlider.Value = preset.Strength;
            ExposureSlider.Value = preset.Exposure;
            ContrastSlider.Value = preset.Contrast;
            WarmthSlider.Value = preset.Warmth;
            SaturationSlider.Value = preset.Saturation;
        }

        private void RestoreDefaultFilterValues()
        {
            int selectedIndex = PresetComboBox.SelectedIndex;
            // Custom preset has no separate built-in table; its factory baseline
            // is the same neutral starting point as the automatic preset.
            PresetValues defaults = GetBuiltInPreset(
                selectedIndex == 5 ? 0 : selectedIndex);
            var factory = AppSettings.CreateDefault();
            try
            {
                initializing = true;
                ApplyPresetValues(defaults);
                SmoothTransitionCheckBox.IsChecked = factory.SmoothTransition;
                // 「恢复默认数值」覆盖界面上看得见的全部参数。原先只回 11 个基础
                // 滑块，色彩聚焦和臂环还留着上次调过的偏色，按钮名不副实。
                FocusHueSlider.Value = factory.FocusHue;
                FocusStrengthSlider.Value = factory.FocusStrength;
                FocusRangeSlider.Value = factory.FocusRange;
                TrimRedSlider.Value = factory.TrimRed;
                TrimGreenSlider.Value = factory.TrimGreen;
                TrimBlueSlider.Value = factory.TrimBlue;
                FocusEnabledCheckBox.IsChecked = factory.FocusEnabled;
                ArmbandEnabledCheckBox.IsChecked = factory.ArmbandEnabled;
                ArmbandLiftSlider.Value = factory.ArmbandLift;
                ArmbandDesaturateSlider.Value = factory.ArmbandDesaturate;
            }
            finally
            {
                initializing = false;
            }

            SyncSettingsFromSliders();
            RefreshSliderLabels();
            UpdateCurrentRecommendation();
            SaveSettings();
            UpdateModeUi();
            ShowNotification(selectedIndex == 5 ?
                "已恢复自定义预设初始数值" : "已恢复所选预设默认数值",
                NotificationType.Success);
        }

        private void AnalyzeLatest(bool applyWhenReady)
        {
            string latest = screenshotWatcher.FindLatest();
            if (string.IsNullOrWhiteSpace(latest) &&
                !string.IsNullOrWhiteSpace(lastAnalyzedPath) &&
                File.Exists(lastAnalyzedPath)) latest = lastAnalyzedPath;
            if (string.IsNullOrWhiteSpace(latest))
            {
                ShowNotification("没有找到可分析的截图", NotificationType.Warning);
                return;
            }
            AnalyzePath(latest, applyWhenReady);
        }

        private void OnScreenshotReady(string filePath)
        {
            if (closing) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closing || !settings.AutoWatch) return;
                    // 截图来自哪个游戏的目录，就用哪一套参数来分析。
                    // 锁定时不再跟着截图切档，否则「锁定」形同虚设。
                    if (settings.ProfileFollowGame)
                    {
                        manualProfileOverride = false;
                        SwitchProfile(AppSettings.ResolveGameKey(filePath));
                    }
                    AnalyzePath(filePath, true);
                }));
            }
            catch { }
        }

        private void OnWatcherFaulted(string message)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!closing) ShowNotification("监听错误：" + message,
                        NotificationType.Warning);
                }));
            }
            catch { }
        }

        private void OnGammaTransitionFailed(string message)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!closing) ShowNotification("应用滤镜失败：" + message,
                        NotificationType.Error);
                }));
            }
            catch { }
        }

        private void AnalyzePath(string filePath, bool applyWhenReady)
        {
            AnalyzePath(filePath, applyWhenReady, 0);
        }

        private void AnalyzePath(string filePath, bool applyWhenReady, int retryAttempt)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                ShowNotification("截图文件不存在", NotificationType.Warning);
                return;
            }

            int version = Interlocked.Increment(ref analysisVersion);
            ApplySettingsToModel();
            AppSettings snapshot = CreateSettingsSnapshot();
            AnalyzeButton.IsEnabled = false;
            ShowNotification("正在分析截图…", NotificationType.Info);

            Task.Factory.StartNew(delegate
            {
                var package = new AnalysisPackage();
                package.Analysis = ImageAnalyzer.Analyze(filePath, snapshot);
                using (DrawingBitmap source = ImageAnalyzer.LoadStableBitmap(filePath))
                {
                    package.Original = ImageAnalyzer.BuildOriginalPreview(source);
                    if (package.Analysis.IsUsable)
                        package.Filtered = ImageAnalyzer.BuildPreview(
                            source, package.Analysis.Recommendation);
                }
                return package;
            }).ContinueWith(delegate(Task<AnalysisPackage> task)
            {
                if (closing || Dispatcher.HasShutdownStarted)
                {
                    DisposeTaskResult(task);
                    return;
                }
                try
                {
                    string capturedPath = filePath;
                    int capturedRetry = retryAttempt;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        CompleteAnalysis(task, version, applyWhenReady, capturedPath, capturedRetry);
                    }));
                }
                catch
                {
                    DisposeTaskResult(task);
                }
            });
        }

        private void CompleteAnalysis(
            Task<AnalysisPackage> task, int version, bool applyWhenReady,
            string filePath, int retryAttempt)
        {
            AnalyzeButton.IsEnabled = true;
            if (closing || version != analysisVersion)
            {
                DisposeTaskResult(task);
                return;
            }

            if (task.IsFaulted)
            {
                Exception error = task.Exception == null ? null :
                    task.Exception.GetBaseException();
                string message = error == null ? "UNKNOWN_ERROR" : error.Message;
                if (retryAttempt < 1 && !string.IsNullOrWhiteSpace(filePath) &&
                    File.Exists(filePath) && message.Contains("尚未写入完成"))
                {
                    ShowNotification("截图正在写入，稍后自动重试…", NotificationType.Info);
                    var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    retryTimer.Tick += delegate
                    {
                        retryTimer.Stop();
                        if (!closing) AnalyzePath(filePath, applyWhenReady, retryAttempt + 1);
                    };
                    retryTimer.Start();
                    return;
                }
                Diagnostics.Warn("分析", "分析失败：" + filePath + " — " + message);
                ShowNotification("分析失败：" + message,
                    NotificationType.Error);
                return;
            }

            AnalysisPackage package = task.Result;
            currentAnalysis = package.Analysis;
            lastAnalyzedPath = currentAnalysis.FilePath;
            previewControl.SetContent(package.Original, package.Filtered, currentAnalysis);
            package.ReleaseOwnership();
            PreviewEmptyState.Visibility = Visibility.Collapsed;
            UpdateAnalysisMetrics(currentAnalysis);

            if (!currentAnalysis.IsUsable)
            {
                ShowNotification("截图已跳过：" + currentAnalysis.SkipReason,
                    NotificationType.Warning);
                return;
            }

            ShowNotification("分析完成：" +
                currentAnalysis.Recommendation.ProfileName, NotificationType.Success);
            if (applyWhenReady)
            {
                // A new watched screenshot is an explicit request to refresh
                // and re-enable the filter, even after a manual hotkey disable.
                filterModeEnabled = true;
                ApplyRecommendation(currentAnalysis);
            }
        }

        private static void DisposeTaskResult(Task<AnalysisPackage> task)
        {
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                task.Result.Dispose();
        }

        private void RefreshCurrentPreview()
        {
            if (currentAnalysis == null || !File.Exists(currentAnalysis.FilePath)) return;
            AnalysisResult analysis = currentAnalysis;
            analysis.Recommendation = ToneCurve.Recommend(
                analysis, CreateSettingsSnapshot());
            UpdateAnalysisMetrics(analysis);

            int version = Interlocked.Increment(ref previewVersion);
            FilterRecommendation recommendation = analysis.Recommendation;
            string path = analysis.FilePath;
            Task.Factory.StartNew(delegate
            {
                var package = new AnalysisPackage { Analysis = analysis };
                using (DrawingBitmap source = ImageAnalyzer.LoadStableBitmap(path))
                {
                    package.Original = ImageAnalyzer.BuildOriginalPreview(source);
                    package.Filtered = ImageAnalyzer.BuildPreview(source, recommendation);
                }
                return package;
            }).ContinueWith(delegate(Task<AnalysisPackage> task)
            {
                if (closing || Dispatcher.HasShutdownStarted)
                {
                    DisposeTaskResult(task);
                    return;
                }
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (closing || version != previewVersion)
                        {
                            DisposeTaskResult(task);
                            return;
                        }
                        if (task.IsFaulted)
                        {
                            DisposeTaskResult(task);
                            return;
                        }
                        AnalysisPackage package = task.Result;
                        previewControl.SetContent(package.Original, package.Filtered, package.Analysis);
                        package.ReleaseOwnership();
                    }));
                }
                catch
                {
                    DisposeTaskResult(task);
                }
            });
        }

        private void UpdateCurrentRecommendation()
        {
            if (currentAnalysis == null || !currentAnalysis.IsUsable) return;
            currentAnalysis.Recommendation = ToneCurve.Recommend(
                currentAnalysis, CreateSettingsSnapshot());
            UpdateAnalysisMetrics(currentAnalysis);
            previewRefreshTimer.Stop();
            previewRefreshTimer.Start();
            if (realtimeController != null) realtimeController.Wake();
        }

        private void ApplyRecommendation(AnalysisResult analysis)
        {
            ApplyRecommendation(analysis, false, false);
        }

        private void ApplyRecommendation(AnalysisResult analysis, bool manualTrigger)
        {
            ApplyRecommendation(analysis, manualTrigger, false);
        }

        private void ApplyRecommendation(AnalysisResult analysis, bool manualTrigger, bool silent)
        {
            if (analysis == null || analysis.Recommendation == null) return;
            if (!manualTrigger && settings.ProcessWatchEnabled && !IsWatchedProcessActive())
            {
                autoPausedByProcess = true;
                filterModeEnabled = true;
                UpdateModeUi();
                return;
            }
            FilterRecommendation recommendation = analysis.Recommendation;
            if (recommendation.StrengthBlend <= 0.0001)
            {
                RestoreOriginal(false);
                filterModeEnabled = true;
                UpdateModeUi();
                ShowNotification("最大调整强度为 0，已保持原画", NotificationType.Warning);
                return;
            }
            List<string> targetDevices = GetTargetDisplayDevices();
            if (targetDevices.Count == 0)
            {
                bool checkedSomething = settings.SelectedDisplayDevices != null &&
                    settings.SelectedDisplayDevices.Count > 0;
                ShowNotification(!settings.MultiDisplayMode ? "没有可用的显示器" :
                    checkedSomething ? "勾选的显示器本次都不在线，请在列表里重新勾选。" :
                    "请至少勾选一个显示器", NotificationType.Error);
                return;
            }

            string error;
            if (!gammaController.CaptureBaselines(targetDevices, out error))
            {
                Diagnostics.Warn("滤镜", "读取显示器原始曲线失败：" + error);
                ShowNotification("读取显示器原始曲线失败：" + error,
                    NotificationType.Error);
                return;
            }
            var baselineRamps = new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
            foreach (string device in targetDevices)
            {
                GammaRamp? baseline = gammaController.GetBaseline(device);
                if (baseline.HasValue) baselineRamps[device] = baseline.Value;
            }
            RecoveryStore.Save(baselineRamps);

            // 过渡时长随滤镜落差缩放：小调整要跟手，跨场景大切换要拉长，
            // 否则同样的落差会在几百毫秒里被压成几帧台阶。
            int duration = settings.SmoothTransition ?
                260 + (int)Math.Round(recommendation.ChangeStrength * 900.0) : 0;
            if (!gammaController.TransitionTo(targetDevices, recommendation,
                duration, out error))
            {
                Diagnostics.Warn("滤镜", "应用滤镜失败：" + error);
                ShowNotification("应用滤镜失败：" + error,
                    NotificationType.Error);
                return;
            }

            ObsFilterStateStore.WriteActive(recommendation, duration);
            filterModeEnabled = true;
            Diagnostics.Throttled("滤镜", "applied",
                "已应用 " + recommendation.ProfileName + "（gamma " +
                recommendation.EquivalentGamma.ToString("0.00") + "、亮度 +" +
                Math.Round(recommendation.BrightnessBoost).ToString("0") + "、对比 +" +
                Math.Round(recommendation.ContrastBoost).ToString("0") + "，过渡 " +
                duration + "ms，目标 " + string.Join("/", targetDevices.ToArray()) + "）",
                TimeSpan.FromSeconds(2));
            UpdateModeUi();
            if (!silent)
                ShowNotification("已应用：" + recommendation.ProfileName,
                    NotificationType.Success);
        }

        private void ToggleFilter()
        {
            if (IsFilterRunning())
            {
                string error;
                if (!gammaController.RestoreAll(out error))
                {
                    ShowNotification("恢复原画失败：" + error, NotificationType.Error);
                    return;
                }
                RecoveryStore.Clear();
                filterModeEnabled = false;
                ObsFilterStateStore.WriteDisabled();
                UpdateModeUi();
                ShowNotification("滤镜已关闭", NotificationType.Info);
                return;
            }

            filterModeEnabled = true;
            autoPausedByProcess = false;
            if (currentAnalysis != null && currentAnalysis.IsUsable)
                ApplyRecommendation(currentAnalysis, true);
            else
            {
                UpdateModeUi();
                ShowNotification("滤镜已开启，等待分析截图", NotificationType.Info);
            }
            if (realtimeController != null) realtimeController.Wake();
        }

        private void RestoreOriginal(bool showNotification)
        {
            string error;
            if (gammaController.RestoreAll(out error))
            {
                RecoveryStore.Clear();
                filterModeEnabled = false;
                ObsFilterStateStore.WriteDisabled();
                UpdateModeUi();
                if (showNotification) ShowNotification("已恢复原始曲线",
                    NotificationType.Success);
            }
            else if (showNotification)
            {
                ShowNotification("恢复原画失败：" + error, NotificationType.Error);
            }
        }

        private void UpdateModeUi()
        {
            string[] modes = { "自动分析", "自然中性", "室内柔和", "夜视护眼", "高光保护", "自定义预设" };
            int index = PresetComboBox == null ? 0 : PresetComboBox.SelectedIndex;
            if (index < 0 || index >= modes.Length) index = 0;
            ModeText.Text = modes[index] + (settings.RealtimeEnabled ? " · 实时" : "");
            bool running = IsFilterRunning();
            RunStateText.Text = running ? "运行中" : "未运行";
            RunStateText.Foreground = running ?
                (System.Windows.Media.Brush)FindResource("AmberAlertBrush") :
                (System.Windows.Media.Brush)FindResource("PhosphorDimBrush");
            ToggleButton.Content = running ?
                "关闭滤镜  " + FormatHotkey(settings.HotkeyKeyCode, settings.HotkeyModifiers) :
                "开启滤镜  " + FormatHotkey(settings.HotkeyKeyCode, settings.HotkeyModifiers);
            ToggleButton.Style = (Style)FindResource(running ?
                "TacticalButtonActive" : "TacticalButtonPrimary");
            StatusDot.Fill = running ?
                (System.Windows.Media.Brush)FindResource("TerminalGreenBrush") :
                (System.Windows.Media.Brush)FindResource("PhosphorDimBrush");
            if (trayIcon != null)
                trayIcon.Text = "TarkovAutoShadePlus - " +
                    (running ? "滤镜已开启" : "滤镜已关闭");
        }

        private bool IsFilterRunning()
        {
            return filterModeEnabled && gammaController.HasActiveFilter;
        }

        private void UpdateWatcherUi()
        {
            bool active = screenshotWatcher.IsActive;
            StatusDot.ToolTip = IsFilterRunning() ? "滤镜正在运行；截图监听：" +
                (active ? "已启用" : "未启用") : "滤镜未运行；截图监听：" +
                (active ? "已启用" : "未启用");
            bool folderReady = screenshotWatcher.Folders.Count > 0;
            if (!folderReady && settings.ScreenshotFolders != null)
            {
                foreach (string folder in settings.ScreenshotFolders)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                        {
                            folderReady = true;
                            break;
                        }
                    }
                    catch { }
                }
            }
            FolderPath.Foreground = folderReady ?
                (System.Windows.Media.Brush)FindResource("AmberAlertBrush") :
                (System.Windows.Media.Brush)FindResource("HazardRedBrush");
            BrowseFolderButton.BorderBrush = folderReady ?
                (System.Windows.Media.Brush)FindResource("PhosphorDimBrush") :
                (System.Windows.Media.Brush)FindResource("HazardRedBrush");
            BrowseFolderButton.Foreground = folderReady ?
                (System.Windows.Media.Brush)FindResource("PhosphorWhiteBrush") :
                (System.Windows.Media.Brush)FindResource("HazardRedBrush");
            // 空状态里的徽标原先是写死的「监听中」，一个目录都没挂上时也在这么说。
            string watcherState = active ? "监听中" : "未监听";
            if (WatcherBadgeText != null)
            {
                WatcherBadgeText.Text = "状态：" + watcherState;
                WatcherBadgeText.Foreground = (System.Windows.Media.Brush)FindResource(
                    active ? "AmberAlertBrush" : "HazardRedBrush");
            }
            if (WatcherStateText != null)
            {
                WatcherStateText.Text = watcherState;
                WatcherStateText.Foreground = (System.Windows.Media.Brush)FindResource(
                    active ? "AmberAlertBrush" : "HazardRedBrush");
            }
            UpdateModeUi();
        }

        private void UpdateAnalysisMetrics(AnalysisResult result)
        {
            if (result == null)
            {
                P10Value.Text = MedianValue.Text = P95Value.Text = "--";
                DynamicRangeValue.Text = "--";
                return;
            }
            P10Value.Text = FormatByteValue(result.P10);
            MedianValue.Text = FormatByteValue(result.Median);
            P95Value.Text = FormatByteValue(result.P95);
            DynamicRangeValue.Text = FormatByteValue(result.DynamicRange);
            UpdateMetrics();
        }

        private void UpdateMetrics()
        {
            if (currentAnalysis == null || currentAnalysis.Recommendation == null)
            {
                GammaMetric.Text = "--";
                BrightnessMetric.Text = "--";
                ContrastMetric.Text = "--";
                return;
            }
            FilterRecommendation recommendation = currentAnalysis.Recommendation;
            GammaMetric.Text = recommendation.EquivalentGamma.ToString("0.00");
            BrightnessMetric.Text = "+" + Math.Round(
                recommendation.BrightnessBoost).ToString("0");
            ContrastMetric.Text = "+" + Math.Round(
                recommendation.ContrastBoost).ToString("0");
        }

        private void ApplySettingsToSliders()
        {
            SmoothTransitionCheckBox.IsChecked = settings.SmoothTransition;
            PresetComboBox.SelectedIndex = Math.Max(0, Math.Min(
                PresetComboBox.Items.Count - 1, settings.PresetIndex));
            ShadowSlider.Value = settings.ShadowTarget;
            HighlightSlider.Value = settings.HighlightProtection;
            ColorSlider.Value = settings.ColorCorrection;
            IndoorSlider.Value = settings.IndoorComfort;
            GuardSlider.Value = settings.SceneGuard;
            BlackSlider.Value = settings.BlackPoint;
            StrengthSlider.Value = settings.MaxStrength;
            ExposureSlider.Value = settings.ExposureBias;
            ContrastSlider.Value = settings.ContrastBias;
            WarmthSlider.Value = settings.Warmth;
            SaturationSlider.Value = settings.SaturationBias;
            FocusHueSlider.Value = settings.FocusHue;
            FocusStrengthSlider.Value = settings.FocusStrength;
            FocusRangeSlider.Value = settings.FocusRange;
            TrimRedSlider.Value = settings.TrimRed;
            TrimGreenSlider.Value = settings.TrimGreen;
            TrimBlueSlider.Value = settings.TrimBlue;
            FocusEnabledCheckBox.IsChecked = settings.FocusEnabled;
            ArmbandEnabledCheckBox.IsChecked = settings.ArmbandEnabled;
            ArmbandLiftSlider.Value = settings.ArmbandLift;
            ArmbandDesaturateSlider.Value = settings.ArmbandDesaturate;
            RefreshFocusHueVisual();
            UpdateFocusEnabledUi();
            UpdateArmbandEnabledUi();
            UpdateHotkeyUi();
            if (ProfileFollowGameCheckBox != null)
                ProfileFollowGameCheckBox.IsChecked = settings.ProfileFollowGame;
            UpdateProfileUi();
        }

        private void RefreshSliderLabels()
        {
            ShadowValue.Text = ((int)ShadowSlider.Value).ToString();
            HighlightValue.Text = ((int)HighlightSlider.Value).ToString();
            ColorValue.Text = ((int)ColorSlider.Value).ToString();
            IndoorValue.Text = ((int)IndoorSlider.Value).ToString();
            GuardValue.Text = ((int)GuardSlider.Value).ToString();
            BlackValue.Text = ((int)BlackSlider.Value).ToString();
            StrengthValue.Text = ((int)StrengthSlider.Value).ToString();
            ExposureValue.Text = FormatSigned((int)ExposureSlider.Value);
            ContrastValue.Text = FormatSigned((int)ContrastSlider.Value);
            WarmthValue.Text = FormatSigned((int)WarmthSlider.Value);
            SaturationValue.Text = FormatSigned((int)SaturationSlider.Value);
            FocusHueValue.Text = ((int)FocusHueSlider.Value).ToString() + "°";
            FocusStrengthValue.Text = ((int)FocusStrengthSlider.Value).ToString();
            FocusRangeValue.Text = FormatSigned((int)FocusRangeSlider.Value);
            TrimRedValue.Text = FormatSigned((int)TrimRedSlider.Value);
            TrimGreenValue.Text = FormatSigned((int)TrimGreenSlider.Value);
            TrimBlueValue.Text = FormatSigned((int)TrimBlueSlider.Value);
            ArmbandLiftValue.Text = ((int)ArmbandLiftSlider.Value).ToString();
            ArmbandDesaturateValue.Text = ((int)ArmbandDesaturateSlider.Value).ToString();
            RefreshFocusHueVisual();
            UpdateFocusEnabledUi();
            UpdateArmbandEnabledUi();
        }

        private void ApplySettingsToModel()
        {
            settings.ShadowTarget = (int)ShadowSlider.Value;
            settings.HighlightProtection = (int)HighlightSlider.Value;
            settings.ColorCorrection = (int)ColorSlider.Value;
            settings.IndoorComfort = (int)IndoorSlider.Value;
            settings.SceneGuard = (int)GuardSlider.Value;
            settings.BlackPoint = (int)BlackSlider.Value;
            settings.MaxStrength = (int)StrengthSlider.Value;
            settings.ExposureBias = (int)ExposureSlider.Value;
            settings.ContrastBias = (int)ContrastSlider.Value;
            settings.Warmth = (int)WarmthSlider.Value;
            settings.SaturationBias = (int)SaturationSlider.Value;
            settings.FocusHue = (int)FocusHueSlider.Value;
            settings.FocusStrength = (int)FocusStrengthSlider.Value;
            settings.FocusRange = (int)FocusRangeSlider.Value;
            settings.TrimRed = (int)TrimRedSlider.Value;
            settings.TrimGreen = (int)TrimGreenSlider.Value;
            settings.TrimBlue = (int)TrimBlueSlider.Value;
            settings.FocusEnabled = FocusEnabledCheckBox.IsChecked == true;
            settings.ArmbandEnabled = ArmbandEnabledCheckBox.IsChecked == true;
            settings.ArmbandLift = (int)ArmbandLiftSlider.Value;
            settings.ArmbandDesaturate = (int)ArmbandDesaturateSlider.Value;
            settings.SmoothTransition = SmoothTransitionCheckBox.IsChecked == true;
        }

        private void SyncSettingsFromSliders()
        {
            ApplySettingsToModel();
            if (PresetComboBox.SelectedIndex == 5)
            {
                settings.CustomPresetInitialized = true;
                settings.CustomShadowTarget = settings.ShadowTarget;
                settings.CustomHighlightProtection = settings.HighlightProtection;
                settings.CustomExposureBias = settings.ExposureBias;
                settings.CustomContrastBias = settings.ContrastBias;
                settings.CustomMaxStrength = settings.MaxStrength;
                settings.CustomWarmth = settings.Warmth;
                settings.CustomColorCorrection = settings.ColorCorrection;
                settings.CustomIndoorComfort = settings.IndoorComfort;
                settings.CustomSceneGuard = settings.SceneGuard;
                settings.CustomBlackPoint = settings.BlackPoint;
                settings.CustomSaturationBias = settings.SaturationBias;
                settings.CustomFocusHue = settings.FocusHue;
                settings.CustomFocusStrength = settings.FocusStrength;
                settings.CustomFocusRange = settings.FocusRange;
                settings.CustomTrimRed = settings.TrimRed;
                settings.CustomTrimGreen = settings.TrimGreen;
                settings.CustomTrimBlue = settings.TrimBlue;
                settings.CustomFocusEnabled = settings.FocusEnabled;
                settings.CustomArmbandEnabled = settings.ArmbandEnabled;
                settings.CustomArmbandLift = settings.ArmbandLift;
                settings.CustomArmbandDesaturate = settings.ArmbandDesaturate;
            }
            settings.PresetIndex = Math.Max(0, PresetComboBox.SelectedIndex);
        }

        private void SaveSettings()
        {
            if (closing || initializing) return;
            SyncSettingsFromSliders();
            SyncRealtimeSettingsFromUi();
            SyncFoldersAndProcessesFromUi();
            // 平铺字段只是当前档案的镜像，落盘前必须写回对应档案，
            // 否则切换之后这次的改动会丢。
            settings.GetProfile(activeProfileKey).CaptureFrom(settings);
            SettingsStore.Save(settings);
            ReportSaveFailureOnce();
        }

        private bool saveErrorReported;

        private void ReportSaveFailureOnce()
        {
            if (saveErrorReported || string.IsNullOrEmpty(SettingsStore.LastSaveError)) return;
            saveErrorReported = true;
            ShowNotification("配置写入失败，这次的改动重启后不会保留：" +
                SettingsStore.LastSaveError + "。点右上角「诊断」看详情。",
                NotificationType.Error);
        }

        private void SyncFoldersAndProcessesFromUi()
        {
            // 文件夹与进程列表由各自面板实时同步到 settings，这里只做一致性兜底：
            // 保持 legacy 单值字段指向列表首项，供旧逻辑/旧测试兼容。
            if (settings.ScreenshotFolders != null && settings.ScreenshotFolders.Count > 0)
                settings.ScreenshotFolder = settings.ScreenshotFolders[0];
            if (settings.WatchedProcessNames != null && settings.WatchedProcessNames.Count > 0)
                settings.WatchedProcessName = settings.WatchedProcessNames[0];
        }

        private AppSettings CreateSettingsSnapshot()
        {
            ApplySettingsToModel();
            SyncRealtimeSettingsFromUi();
            var snapshot = AppSettings.CreateDefault();
            snapshot.AlgorithmVersion = settings.AlgorithmVersion;
            snapshot.ShadowTarget = settings.ShadowTarget;
            snapshot.HighlightProtection = settings.HighlightProtection;
            snapshot.ExposureBias = settings.ExposureBias;
            snapshot.ContrastBias = settings.ContrastBias;
            snapshot.MaxStrength = settings.MaxStrength;
            snapshot.Warmth = settings.Warmth;
            snapshot.ColorCorrection = settings.ColorCorrection;
            snapshot.IndoorComfort = settings.IndoorComfort;
            snapshot.SceneGuard = settings.SceneGuard;
            snapshot.BlackPoint = settings.BlackPoint;
            snapshot.SaturationBias = settings.SaturationBias;
            snapshot.FocusHue = settings.FocusHue;
            snapshot.FocusStrength = settings.FocusStrength;
            snapshot.FocusRange = settings.FocusRange;
            snapshot.TrimRed = settings.TrimRed;
            snapshot.TrimGreen = settings.TrimGreen;
            snapshot.TrimBlue = settings.TrimBlue;
            snapshot.FocusEnabled = settings.FocusEnabled;
            snapshot.ArmbandEnabled = settings.ArmbandEnabled;
            snapshot.ArmbandLift = settings.ArmbandLift;
            snapshot.ArmbandDesaturate = settings.ArmbandDesaturate;
            snapshot.RealtimeEnabled = settings.RealtimeEnabled;
            snapshot.RealtimeIntervalMs = settings.RealtimeIntervalMs;
            snapshot.RealtimeSensitivity = settings.RealtimeSensitivity;
            snapshot.Normalize();
            return snapshot;
        }

        private void RecoverPreviousSession()
        {
            Dictionary<string, GammaRamp> ramps;
            if (!RecoveryStore.TryLoadAll(out ramps)) return;

            // 已经拔掉 / 换掉的显示器不该算恢复失败：留着它，恢复就永远不会完成，
            // 于是每次开机都弹一次「无法访问显示器」，记录也永久残留。
            var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (DisplayTarget display in GammaRampController.EnumerateDisplays())
                    connected.Add(display.DeviceName);
            }
            catch { }
            if (connected.Count > 0)
            {
                var staleDevices = new List<string>();
                foreach (string device in ramps.Keys)
                {
                    if (!connected.Contains(device)) staleDevices.Add(device);
                }
                foreach (string device in staleDevices) ramps.Remove(device);
            }
            if (ramps.Count == 0)
            {
                RecoveryStore.Clear();
                return;
            }

            string error;
            if (gammaController.ApplyDirect(ramps, out error))
            {
                RecoveryStore.Clear();
                Diagnostics.Info("恢复", "已还原 " + ramps.Count +
                    " 台显示器的原始曲线（上次异常退出）");
                ShowNotification("已恢复上次异常退出前的画面", NotificationType.Success);
            }
            else
            {
                Diagnostics.Warn("恢复", "还原上次异常退出前的画面失败：" + error);
                ShowNotification("异常退出恢复失败：" + error, NotificationType.Warning);
            }
        }

        private void ShowNotification(string message, NotificationType type)
        {
            string prefix = type == NotificationType.Success ? "完成" :
                type == NotificationType.Error ? "错误" :
                type == NotificationType.Warning ? "提醒" : "信息";
            Brush accent = type == NotificationType.Error ?
                (Brush)FindResource("HazardRedBrush") : type == NotificationType.Warning ?
                (Brush)FindResource("AmberAlertBrush") : type == NotificationType.Success ?
                (Brush)FindResource("TerminalGreenBrush") :
                (Brush)FindResource("PhosphorDimBrush");
            ActionMessageTypeText.Text = prefix;
            ActionMessageText.Text = message;
            ActionMessageTypeText.Foreground = accent;
            ActionMessageText.Foreground = (Brush)FindResource("PhosphorWhiteBrush");
            ActionMessageDot.Fill = accent;
            ActionMessagePanel.BorderBrush = accent;
            ActionMessagePanel.Opacity = 1.0;
            ActionMessagePanel.IsHitTestVisible = true;

            // 这条消息只有 3 秒，之后就被下一条顶掉 —— 「应用滤镜失败」这种必须先看
            // 见的东西因此等于没说。异常除了留久一点，还在顶栏留一个可点的计数。
            bool fault = type == NotificationType.Error || type == NotificationType.Warning;
            int seconds = fault ? 10 : 3;
            if (fault)
            {
                if (type == NotificationType.Error) hasError = true;
                reminderCount++;
                FaultPillText.Text = reminderCount > 99 ? "提醒 99+" : "提醒 " + reminderCount;
                // 只有真正的报错才是红色，「没找到截图」这种提示是琥珀色，
                // 免的一律标红之后用户直接无视它。
                string accentKey = hasError ? "HazardRedBrush" : "AmberAlertBrush";
                Brush pillBrush = (Brush)FindResource(accentKey);
                FaultPill.BorderBrush = pillBrush;
                FaultPillDot.Fill = pillBrush;
                FaultPillText.Foreground = pillBrush;
                FaultPill.Visibility = Visibility.Visible;
            }

            if (notificationTimer == null)
            {
                notificationTimer = new DispatcherTimer();
                notificationTimer.Tick += delegate
                {
                    notificationTimer.Stop();
                    ActionMessageText.Text = string.Empty;
                    ActionMessageTypeText.Text = string.Empty;
                    ActionMessagePanel.Opacity = 0.0;
                    ActionMessagePanel.IsHitTestVisible = false;
                };
            }
            notificationTimer.Stop();
            notificationTimer.Interval = TimeSpan.FromSeconds(seconds);
            notificationTimer.Start();
        }

        private void OpenDiagnosticsFromPill()
        {
            reminderCount = 0;
            hasError = false;
            FaultPill.Visibility = Visibility.Collapsed;
            ShowDiagnosticsWindow();
        }

        private void ShowDiagnosticsWindow()
        {
            var targets = new List<string>();
            foreach (string device in GetTargetDisplayDevices())
                targets.Add(FriendlyDisplayLabel(device));

            string report;
            try
            {
                report = Diagnostics.BuildReport(settings, monitorCapabilities,
                    targets.Count == 0 ? "无" : string.Join(" ｜ ", targets.ToArray()),
                    IsFilterRunning());
            }
            catch (Exception error)
            {
                report = "生成诊断信息失败：" + error.Message;
            }

            var window = new Window
            {
                Title = "诊断信息",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 720,
                Height = 560,
                MinWidth = 520,
                MinHeight = 360,
                ShowInTaskbar = false,
                Background = (Brush)FindResource("CrtSurfaceBrush"),
                Foreground = (Brush)FindResource("PhosphorWhiteBrush")
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new TextBlock
            {
                Text = "报障时把下面这份内容整段复制出来即可。路径里的用户名已隐去。",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontUi"),
                FontSize = 11,
                Foreground = (Brush)FindResource("PhosphorDimBrush"),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            var box = new TextBox
            {
                Text = report,
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontMono"),
                FontSize = 11.5,
                Background = (Brush)FindResource("InsetBrush"),
                Foreground = (Brush)FindResource("PhosphorWhiteBrush"),
                BorderBrush = (Brush)FindResource("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10)
            };
            box.SelectAll();
            Grid.SetRow(box, 1);
            root.Children.Add(box);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(buttons, 2);
            var copy = new Button
            {
                Content = "复制全部",
                Width = 92,
                Height = 32,
                Style = (Style)FindResource("TacticalButtonPrimary")
            };
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(report);
                    ShowNotification("诊断信息已复制到剪贴板", NotificationType.Success);
                }
                catch (Exception error)
                {
                    Diagnostics.Error("诊断", "复制到剪贴板失败", error);
                    ShowNotification("复制失败：" + error.Message, NotificationType.Warning);
                }
            };
            var openFolder = new Button
            {
                Content = "打开日志目录",
                Width = 110,
                Height = 32,
                Margin = new Thickness(10, 0, 0, 0),
                Style = (Style)FindResource("TacticalButton")
            };
            openFolder.Click += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.GetDirectoryName(Diagnostics.LogFilePath),
                        UseShellExecute = true
                    });
                }
                catch (Exception error)
                {
                    ShowNotification("打不开日志目录：" + error.Message, NotificationType.Warning);
                }
            };
            var close = new Button
            {
                Content = "关闭",
                Width = 72,
                Height = 32,
                Margin = new Thickness(10, 0, 0, 0),
                Style = (Style)FindResource("TacticalButton")
            };
            close.Click += delegate { window.Close(); };
            buttons.Children.Add(copy);
            buttons.Children.Add(openFolder);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            window.Content = root;
            window.ShowDialog();
        }

        private void ShowAboutWindow()
        {
            var about = new Window
            {
                Title = "关于 TarkovAutoShadePlus",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 460,
                Height = 296,
                MinWidth = 460,
                MinHeight = 296,
                MaxWidth = 460,
                MaxHeight = 296,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = (Brush)FindResource("CrtSurfaceBrush"),
                Foreground = (Brush)FindResource("PhosphorWhiteBrush")
            };

            var content = new Grid
            {
                Margin = new Thickness(24, 24, 24, 12)
            };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            content.RowDefinitions.Add(new RowDefinition {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 38
            });

            var title = new TextBlock
            {
                Text = "TarkovAutoShadePlus",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontDisplay"),
                FontSize = 24,
                FontWeight = FontWeights.Black,
                Foreground = (Brush)FindResource("PhosphorWhiteBrush")
            };
            Grid.SetRow(title, 0);
            content.Children.Add(title);

            var details = new TextBlock
            {
                Text = "版本：2.2.0\n原作者：lub大萝卜\n二改：a1175815821\n免费分享，禁止倒卖",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontMono"),
                FontSize = 12,
                Foreground = (Brush)FindResource("PhosphorDimBrush"),
                LineHeight = 20
            };
            Grid.SetRow(details, 1);
            content.Children.Add(details);

            var github = new TextBlock
            {
                Text = "GitHub仓库：TarkovAutoShadePlus",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontMono"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TerminalGreenBrush"),
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            github.MouseLeftButtonUp += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/a1175815821/TarkovAutoShadePlus",
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            Grid.SetRow(github, 2);
            content.Children.Add(github);

            var notice = new TextBlock
            {
                Text = "本工具仅调整显示器 Gamma / DDC/CI，不读取游戏进程。",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontMono"),
                FontSize = 11,
                Foreground = (Brush)FindResource("PhosphorDimBrush"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(notice, 3);
            content.Children.Add(notice);

            var close = new Button
            {
                Content = "关闭",
                Width = 72,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1),
                Style = (Style)FindResource("TacticalButton")
            };
            close.Click += delegate { about.Close(); };
            Grid.SetRow(close, 4);
            content.Children.Add(close);

            about.Content = content;
            about.ShowDialog();
        }

        private static string FormatByteValue(double value)
        {
            return Math.Round(Math.Max(0.0, Math.Min(1.0, value)) * 255.0).ToString("0");
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? "+" + value.ToString() : value.ToString();
        }

        private enum NotificationType
        {
            Info,
            Success,
            Warning,
            Error
        }

        private struct PresetValues
        {
            public readonly int Shadow;
            public readonly int Highlight;
            public readonly int Color;
            public readonly int Indoor;
            public readonly int Guard;
            public readonly int Black;
            public readonly int Strength;
            public readonly int Exposure;
            public readonly int Contrast;
            public readonly int Warmth;
            public readonly int Saturation;

            public PresetValues(int shadow, int highlight, int color, int indoor,
                int guard, int black, int strength, int exposure, int contrast,
                int warmth, int saturation)
            {
                Shadow = shadow;
                Highlight = highlight;
                Color = color;
                Indoor = indoor;
                Guard = guard;
                Black = black;
                Strength = strength;
                Exposure = exposure;
                Contrast = contrast;
                Warmth = warmth;
                Saturation = saturation;
            }
        }

        private void RestoreWindowGeometry()
        {
            try
            {
                if (settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
                {
                    Width = Math.Max(MinWidth, settings.WindowWidth.Value);
                    Height = Math.Max(MinHeight, settings.WindowHeight.Value);
                }
                // 显示器拔过、分辨率改过，存的坐标可能整窗口落在屏幕外，
                // 那样窗口看不见也点不到。只在还能落到某个屏幕上时才恢复位置。
                if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue &&
                    FallsOnSomeScreen(settings.WindowLeft.Value, settings.WindowTop.Value))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = settings.WindowLeft.Value;
                    Top = settings.WindowTop.Value;
                }
                if (settings.WindowMaximized) WindowState = WindowState.Maximized;
            }
            catch { }
        }

        private static bool FallsOnSomeScreen(double left, double top)
        {
            try
            {
                foreach (WinForms.Screen screen in WinForms.Screen.AllScreens)
                {
                    var bounds = screen.Bounds;
                    // 留一条标题栏的余量：只剩一两个像素在屏内也没法拖动窗口。
                    if (left >= bounds.Left - 200 && left <= bounds.Right - 60 &&
                        top >= bounds.Top - 4 && top <= bounds.Bottom - 30) return true;
                }
            }
            catch { }
            return false;
        }

        private void CaptureWindowGeometry()
        {
            try
            {
                // RestoreBounds 拿的是「非最大化时」的边界，最大化时也能存回正常尺寸。
                Rect normal = RestoreBounds;
                if (normal.Width >= MinWidth && normal.Height >= MinHeight)
                {
                    settings.WindowWidth = (int)Math.Round(normal.Width);
                    settings.WindowHeight = (int)Math.Round(normal.Height);
                    settings.WindowLeft = (int)Math.Round(normal.Left);
                    settings.WindowTop = (int)Math.Round(normal.Top);
                }
                settings.WindowMaximized = WindowState == WindowState.Maximized;
            }
            catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            bool exitRequested = trayExitRequested;
            trayExitRequested = false;

            if (!closing && !exitRequested)
            {
                if (settings.CloseBehavior == 1)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }

                if (settings.CloseBehavior == 0)
                {
                    bool remember;
                    bool toTray;
                    if (!ShowCloseChoice(out remember, out toTray))
                    {
                        if (trayExitRequested)
                        {
                            trayExitRequested = false;
                            exitRequested = true;
                        }
                        else
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                    if (!exitRequested)
                    {
                        if (remember)
                        {
                            settings.CloseBehavior = toTray ? 1 : 2;
                            SaveSettings();
                        }
                        if (toTray)
                        {
                            e.Cancel = true;
                            Hide();
                            return;
                        }
                    }
                }
            }
            closing = true;
            base.OnClosing(e);
        }

        private bool ShowCloseChoice(out bool remember, out bool toTray)
        {
            remember = false;
            toTray = false;
            int choice = 0;
            var dialog = new Window
            {
                Title = "关闭 TarkovAutoShade",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 456,
                Height = 224,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = (Brush)FindResource("CrtSurfaceBrush"),
                Foreground = (Brush)FindResource("PhosphorWhiteBrush")
            };
            var root = new Grid { Margin = new Thickness(26, 18, 26, 12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var heading = new TextBlock
            {
                Text = "关闭窗口",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontUi"),
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PhosphorWhiteBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);
            var message = new TextBlock
            {
                Text = "关闭后会恢复原始画面。请选择接下来的处理方式。",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontUi"),
                FontSize = 13,
                Foreground = (Brush)FindResource("PhosphorDimBrush"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(message, 1);
            root.Children.Add(message);
            var rememberBox = new CheckBox
            {
                Content = "记住我的选择",
                Style = (Style)FindResource("TacticalCheckBox")
            };
            var bottom = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(rememberBox, 0);
            bottom.Children.Add(rememberBox);
            var buttons = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            var tray = new Button { Content = "最小化到托盘", Height = 34, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("TacticalButton") };
            var exit = new Button { Content = "退出程序", Height = 34, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("TacticalButtonPrimary") };
            var cancel = new Button { Content = "取消", Height = 34, Style = (Style)FindResource("TacticalButton") };
            tray.Click += delegate { choice = 1; dialog.DialogResult = true; };
            exit.Click += delegate { choice = 2; dialog.DialogResult = true; };
            cancel.Click += delegate { dialog.DialogResult = false; };
            buttons.Children.Add(tray);
            buttons.Children.Add(exit);
            buttons.Children.Add(cancel);
            Grid.SetColumn(exit, 1);
            Grid.SetColumn(cancel, 2);
            Grid.SetColumn(buttons, 1);
            bottom.Children.Add(buttons);
            Grid.SetRow(bottom, 2);
            root.Children.Add(bottom);
            dialog.Content = root;
            closeChoiceDialog = dialog;
            try
            {
                if (dialog.ShowDialog() != true || choice == 0) return false;
                remember = rememberBox.IsChecked == true;
                toTray = choice == 1;
                return true;
            }
            finally
            {
                if (ReferenceEquals(closeChoiceDialog, dialog))
                    closeChoiceDialog = null;
            }
        }

        private sealed class AnalysisPackage : IDisposable
        {
            public AnalysisResult Analysis;
            public DrawingBitmap Original;
            public DrawingBitmap Filtered;

            public void ReleaseOwnership()
            {
                Original = null;
                Filtered = null;
            }

            public void Dispose()
            {
                if (Original != null) Original.Dispose();
                if (Filtered != null) Filtered.Dispose();
                Original = null;
                Filtered = null;
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            Diagnostics.Info("退出", "窗口关闭：还原画面、写回配置");
            closing = true;
            Interlocked.Increment(ref analysisVersion);
            Interlocked.Increment(ref previewVersion);
            Interlocked.Increment(ref realtimeVersion);
            if (realtimeController != null)
            {
                try { realtimeController.Dispose(); }
                catch { }
                realtimeController = null;
            }
            if (timestampTimer != null) timestampTimer.Stop();
            if (statusDotTimer != null) statusDotTimer.Stop();
            if (settingsTimer != null) settingsTimer.Stop();
            if (previewRefreshTimer != null) previewRefreshTimer.Stop();
            if (processWatchTimer != null) processWatchTimer.Stop();
            if (ddcDebounceTimer != null) ddcDebounceTimer.Stop();
            if (foregroundWindowEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(foregroundWindowEventHook);
                foregroundWindowEventHook = IntPtr.Zero;
            }
            screenshotWatcher.Dispose();
            toggleHotkey?.Dispose();
            SyncSettingsFromSliders();
            try { SyncRealtimeSettingsFromUi(); }
            catch { }
            SyncFoldersAndProcessesFromUi();
            CaptureWindowGeometry();
            // 平铺字段只是当前档案的镜像，落盘前必须写回对应档案，
            // 否则切换之后这次的改动会丢。
            settings.GetProfile(activeProfileKey).CaptureFrom(settings);
            SettingsStore.Save(settings);
            string ignored;
            gammaController.RestoreAll(out ignored);
            ObsFilterStateStore.WriteDisabled();
            RecoveryStore.Clear();
            gammaController.Dispose();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            if (trayMenu != null) trayMenu.Dispose();
        }

        /// <summary>
        /// 崩溃兜底：进程要死了，先把显示器曲线还原再说。画面被滤镜盖着却找不到
        /// 是谁干的，是这类工具最难受的失败方式。
        /// </summary>
        internal void RestoreScreenAfterCrash()
        {
            string ignored;
            gammaController.RestoreAll(out ignored);
            ObsFilterStateStore.WriteDisabled();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const uint EventSystemForeground = 0x0003;
        private const uint WineventOutOfContext = 0x0000;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void WinEventDelegate(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThreadId,
            uint eventTime);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr moduleHandle,
            WinEventDelegate callback,
            uint processId,
            uint threadId,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle, out uint processId);
    }
}
