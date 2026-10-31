using BUGrockLauncher.Helpers;
using System.Threading;
using BUGrockLauncher.Services;

using System;

using System.Collections.Generic;

using System.ComponentModel;

using System.Diagnostics;

using System.IO;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Data;

using System.Windows.Input;

using System.Windows.Interop;

using BUGrockLauncher.WPFDataTypes;

using Newtonsoft.Json;

using Version = BUGrockLauncher.WPFDataTypes.Version;



namespace BUGrockLauncher {

    /// <summary>

    /// Interaction logic for MainWindow.xaml

    /// </summary>

    public partial class MainWindow : Window, ICommonVersionCommands {

        private const string GDK_SHIM_NAME = @"GDKLaunchShim.exe";

        private const string GDK_DECRYPT_HELPER_NAME = @"GDKDecryptHelper.exe";

        private static readonly string PREFS_PATH = @"preferences.json";

        private static readonly string IMPORTED_VERSIONS_PATH = @"imported_versions";

        private static string VERSIONS_API_UWP => AppConfig.VersionsApiUwp;

        private static string VERSIONS_API_GDK => AppConfig.VersionsApiGdk;



        private VersionList _versions;

        public Preferences UserPrefs { get; }



        private HashSet<CollectionViewSource> _versionListViews = new HashSet<CollectionViewSource>();



        private readonly VersionDownloader _anonVersionDownloader = new VersionDownloader();

        private readonly VersionDownloader _userVersionDownloader = new VersionDownloader();

        private readonly Task _userVersionDownloaderLoginTask;

        private volatile int _userVersionDownloaderLoginTaskStarted;

        private volatile bool _hasLaunchTask = false;

        private volatile bool _hasGdkExtractTask = false;

        private bool _isLoadingVersionList = false;



        public MainWindow() {

            Preferences? userPrefs = null;

            if (File.Exists(PREFS_PATH)) {

                try {

                    userPrefs = JsonConvert.DeserializeObject<Preferences>(File.ReadAllText(PREFS_PATH));

                } catch (Exception ex) {

                    Trace.WriteLine("Failed to parse preferences.json: " + ex.Message);

                }

            }

            if (userPrefs == null) {

                UserPrefs = new Preferences();

                RewritePrefs();

            } else {

                UserPrefs = userPrefs;

            }

            if (UserPrefs.CustomInstallDirectories == null) {

                UserPrefs.CustomInstallDirectories = new Dictionary<string, string>();

            }



            var versionsApiUWP = (!string.IsNullOrWhiteSpace(UserPrefs.VersionsApiUWP) && !UserPrefs.VersionsApiUWP.Contains("BUGrockLauncher/BUGrockLauncher")) ? UserPrefs.VersionsApiUWP : VERSIONS_API_UWP;

            var versionsApiGDK = !string.IsNullOrWhiteSpace(UserPrefs.VersionsApiGDK) ? UserPrefs.VersionsApiGDK : VERSIONS_API_GDK;

            _versions = new VersionList("versions_uwp.json", IMPORTED_VERSIONS_PATH, versionsApiUWP, this, VersionEntryPropertyChanged, "versions_gdk.json", versionsApiGDK);



            InitializeComponent();

            if (Application.Current != null) { Application.Current.MainWindow = this; }
            StateChanged += MainWindow_StateChanged;



            ShowInstalledVersionsOnlyCheckbox.DataContext = this;



            var versionListViewAll = Resources["versionListViewAll"] as CollectionViewSource;

            if (versionListViewAll != null) {

                versionListViewAll.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {

                    var v = e.Item as Version;

                    e.Accepted = v != null && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));

                });

                versionListViewAll.Source = _versions;

                AllVersionList.DataContext = versionListViewAll;

                _versionListViews.Add(versionListViewAll);

            }



            var versionListViewRelease = Resources["versionListViewRelease"] as CollectionViewSource;

            versionListViewRelease.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {

                var v = e.Item as Version;

                e.Accepted = v.VersionType == VersionType.Release && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));

            });

            versionListViewRelease.Source = _versions;

            ReleaseVersionList.DataContext = versionListViewRelease;

            _versionListViews.Add(versionListViewRelease);



            var versionListViewBeta = Resources["versionListViewBeta"] as CollectionViewSource;

            versionListViewBeta.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {

                var v = e.Item as Version;

                e.Accepted = v.VersionType == VersionType.Beta && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));

            });

            versionListViewBeta.Source = _versions;

            BetaVersionList.DataContext = versionListViewBeta;

            _versionListViews.Add(versionListViewBeta);



            var versionListViewPreview = Resources["versionListViewPreview"] as CollectionViewSource;

            versionListViewPreview.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {

                var v = e.Item as Version;

                e.Accepted = v.VersionType == VersionType.Preview && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));

            });

            versionListViewPreview.Source = _versions;

            PreviewVersionList.DataContext = versionListViewPreview;

            _versionListViews.Add(versionListViewPreview);



            var versionListViewImported = Resources["versionListViewImported"] as CollectionViewSource;

            versionListViewImported.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {

                var v = e.Item as Version;

                e.Accepted = v.VersionType == VersionType.Imported;

            });



            versionListViewImported.Source = _versions;

            ImportedVersionList.DataContext = versionListViewImported;

            _versionListViews.Add(versionListViewImported);



            _userVersionDownloaderLoginTask = new Task(() => {

                _userVersionDownloader.EnableUserAuthorization();

            });



            // Ensure Win32 window handle is created so WebView2 can initialize in the background

            try {

                var helper = new WindowInteropHelper(this);

                helper.EnsureHandle();

            } catch { }



            _ = InitializeWebViewsAsync();



            Loaded += (s, e) => {

                InitSteveHead();

                UpdateQuickPlayStatus();

                if (VersionTabs != null && AllTab != null) VersionTabs.SelectedItem = AllTab;

                _ = LoadVersionListAsync();

                Task.Run(() => AddonManagerService.EnsureDefaultSkinPacksInstalled());

            };

        }





        private void LoadVersionList() {

            _ = LoadVersionListAsync();

        }



        private void ApplySortingToViews() {

            if (!Dispatcher.CheckAccess()) {

                Dispatcher.InvokeAsync(ApplySortingToViews);

                return;

            }

            var versionComparer = new VersionViewComparer();

            foreach (var cvs in _versionListViews) {

                if (cvs.View is ListCollectionView lcv) {

                    lcv.CustomSort = versionComparer;

                }

            }

        }



        public async Task LoadVersionListAsync(SplashScreenWindow? splash = null) {

            if (_isLoadingVersionList) return;

            _isLoadingVersionList = true;

            try {

                // Detach custom sort during reload/populate to prevent O(N^2) sorting overhead while adding items

                foreach (var cvs in _versionListViews) {

                    if (cvs.View is ListCollectionView lcv) {

                        lcv.CustomSort = null;

                    }

                }

                _versions.VersionsApiUWP = (!string.IsNullOrWhiteSpace(UserPrefs.VersionsApiUWP) && !UserPrefs.VersionsApiUWP.Contains("BUGrockLauncher/BUGrockLauncher")) ? UserPrefs.VersionsApiUWP : VERSIONS_API_UWP;

                _versions.VersionsApiGDK = !string.IsNullOrWhiteSpace(UserPrefs.VersionsApiGDK) ? UserPrefs.VersionsApiGDK : VERSIONS_API_GDK;

                _versions.PrepareForReload();



                LoadingProgressGrid.Visibility = Visibility.Visible;



                if (splash != null) splash.SetStatus("Loading GDK versions...");

                LoadingProgressLabel.Content = "Loading GDK versions";

                LoadingProgressBar.Value = 1;

                try {

                    await _versions.DownloadVersionsGDK();

                } catch (Exception e) {

                    Trace.WriteLine("GDK list download failed:\n" + e.ToString());

                    try { await _versions.LoadFromCacheGDK(); } catch { }

                }



                if (splash != null) splash.SetStatus("Loading UWP versions...");

                LoadingProgressLabel.Content = "Loading UWP versions";

                LoadingProgressBar.Value = 2;

                try {

                    await _versions.DownloadVersionsUWP();

                } catch (Exception e) {

                    Trace.WriteLine("UWP list download failed:\n" + e.ToString());

                    try { await _versions.LoadFromCacheUWP(); } catch { }

                }



                if (splash != null) splash.SetStatus("Loading imported versions...");

                LoadingProgressLabel.Content = "Loading imported versions";

                LoadingProgressBar.Value = 3;

                await _versions.LoadImported();



                ApplyCustomInstallDirectories();

                SyncInstalledInstances();

                ApplySortingToViews();

                RefreshLists();



                LoadingProgressGrid.Visibility = Visibility.Collapsed;

                UpdateQuickPlayStatus();

            } finally {

                _isLoadingVersionList = false;

            }

        }



        private void ApplyCustomInstallDirectories() {

            if (UserPrefs?.CustomInstallDirectories == null) return;

            foreach (var v in _versions) {

                if (v.VersionType != VersionType.Imported && UserPrefs.CustomInstallDirectories.TryGetValue(v.Name, out var customDir)) {

                    if (!string.IsNullOrWhiteSpace(customDir) && Directory.Exists(customDir)) {

                        v.GameDirectory = customDir;

                        v.UpdateInstallStatus();

                    }

                }

            }

        }



        public void SyncInstalledInstances() {

            InstanceManager.Instance.LoadInstances();

            var instances = InstanceManager.Instance.GetInstances();

            foreach (var inst in instances) {

                var existing = _versions.FirstOrDefault(v => v.InstanceId == inst.Id);

                if (existing != null) {

                    existing.CustomInstanceName = inst.Name;

                    existing.GameDirectory = inst.GameDirectory;

                    existing.DataDirectory = inst.DataDirectory;

                    existing.CheckCanUpgrade(_versions);

                    existing.UpdateInstallStatus();

                } else {

                    var cat = _versions.FirstOrDefault(c => c.Name == inst.VersionNumber && c.PackageType == inst.PackageType);

                    var ver = new Version(cat?.UUID ?? Version.UNKNOWN_UUID, inst.VersionNumber, inst.VersionType, false, this, inst.PackageType, cat?.DownloadURLs) {

                        InstanceId = inst.Id,

                        CustomInstanceName = inst.Name,

                        GameDirectory = inst.GameDirectory,

                        DataDirectory = inst.DataDirectory,

                        RenameCommand = RenameCommand,

                        DuplicateCommand = DuplicateCommand,

                        UpgradeCommand = UpgradeCommand

                    };

                    ver.CheckCanUpgrade(_versions);

                    _versions.Add(ver);

                }

            }

            foreach (var v in _versions) {

                v.CheckCanUpgrade(_versions);

            }

        }



        private void VersionEntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {

            if (_isLoadingVersionList) return;

            if (!Dispatcher.CheckAccess()) {

                Dispatcher.InvokeAsync(() => VersionEntryPropertyChanged(sender, e));

                return;

            }

            if (e.PropertyName == "IsInstalled" || e.PropertyName == "IsStateChanging" ||

                e.PropertyName == "CustomInstanceName" || e.PropertyName == "CanUpgrade" ||

                e.PropertyName == "DisplayName" || string.IsNullOrEmpty(e.PropertyName)) {

                RefreshLists();

                UpdateQuickPlayStatus();

            }

        }



        private object? _lastNavTabClicked = null;

        private DateTime _lastNavTabClickTime = DateTime.MinValue;



        private void TabNav_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {

            if (sender is not RadioButton tab) return;



            bool isBrowserOpen = (InAppBrowserOverlay != null && InAppBrowserOverlay.Visibility == Visibility.Visible);

            if (!isBrowserOpen) return;



            // When the in-app browser is open, double-clicking (two clicks in rapid succession)

            // is required to close the browser and switch views.

            // On the FIRST click, we set e.Handled = true to completely prevent WPF from toggling IsChecked,

            // so the green indicator line stays firmly on the current active tab until the second click!

            bool isSecondClick = (e.ClickCount >= 2) || 

                                 (_lastNavTabClicked == tab && (DateTime.Now - _lastNavTabClickTime).TotalMilliseconds <= 800);



            if (isSecondClick) {

                _lastNavTabClicked = null;

                _lastNavTabClickTime = DateTime.MinValue;

                e.Handled = true;

                tab.IsChecked = true;

                CloseInAppBrowser();

                TabNav_Checked(tab, new RoutedEventArgs());

            } else {

                _lastNavTabClicked = tab;

                _lastNavTabClickTime = DateTime.Now;

                e.Handled = true;

            }

        }



        private void TabNav_Checked(object sender, RoutedEventArgs e) {
            if (PlayViewGrid == null || InstallationsViewGrid == null) return;
            bool isPlay = (TabNavPlay?.IsChecked == true);
            bool isInstallations = (TabNavInstallations?.IsChecked == true);
            bool isAddons = (TabNavAddons?.IsChecked == true);
            bool isSettings = (TabNavSettings?.IsChecked == true);
            bool isBrowserOpen = (InAppBrowserOverlay != null && InAppBrowserOverlay.Visibility == Visibility.Visible);

            PlayViewGrid.Visibility = (isPlay && !isBrowserOpen && !isAddons && !isSettings) ? Visibility.Visible : Visibility.Collapsed;
            if (ServerCardBorder != null) ServerCardBorder.Visibility = (isPlay && !isBrowserOpen && !isAddons && !isSettings) ? Visibility.Visible : Visibility.Collapsed;
            if (ServersWebView != null) {
                bool shouldShowServers = (isPlay && !isBrowserOpen && !isAddons && !isSettings);
                ServersWebView.Visibility = shouldShowServers ? Visibility.Visible : Visibility.Collapsed;
                try {
                    if (ServersWebView.CoreWebView2 != null) {
                        if (shouldShowServers) {
                            if (UserPrefs.WebPerformanceMode == "High" && _serversRevealed) {
                                // High Performance Mode: Simply unfreeze in place instantly!
                                ServersWebView.Margin = new Thickness(0);
                                ServersWebView.Visibility = Visibility.Visible;
                                if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;
                                try {
                                    ServersWebView.CoreWebView2?.Resume();
                                    ServersWebView.CoreWebView2.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Normal;
                                } catch { }
                            } else {
                                ServersWebView.CoreWebView2.Resume();
                                ServersWebView.CoreWebView2.MemoryUsageTargetLevel = (UserPrefs.WebPerformanceMode == "Low")
                                    ? Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low
                                    : Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Normal;
                                if (!_serversInitialNavStarted && (string.IsNullOrEmpty(ServersWebView.Source?.ToString()) || ServersWebView.Source.ToString() == "about:blank")) {
                                    _serversInitialNavStarted = true;
                                    ServersWebView.CoreWebView2.Navigate(AppConfig.ServersHubUrl);
                                }
                            }
                        } else {
                            if (UserPrefs.WebPerformanceMode == "Low") {
                                _ = ServersWebView.CoreWebView2.TrySuspendAsync();
                                ServersWebView.CoreWebView2.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low;
                                _ = ClearWebViewCacheAsync(ServersWebView.CoreWebView2);
                            } else if (UserPrefs.WebPerformanceMode == "Balanced") {
                                _ = ServersWebView.CoreWebView2.TrySuspendAsync();
                                ServersWebView.CoreWebView2.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low;
                            }
                            // In "High" performance mode: keep ServersWebView cached in memory without wiping!
                        }
                    }
                } catch { }
            }

            InstallationsViewGrid.Visibility = (isInstallations && !isBrowserOpen && !isAddons && !isSettings) ? Visibility.Visible : Visibility.Collapsed;

            if (AddonsManagerViewGrid != null) {
                AddonsManagerViewGrid.Visibility = (isAddons && !isBrowserOpen && !isSettings) ? Visibility.Visible : Visibility.Collapsed;
                if (isAddons && !isBrowserOpen && !isSettings) {
                    LoadAddonsManagerData();
                }
            }

            if (SettingsViewGrid != null) {
                SettingsViewGrid.Visibility = (isSettings && !isBrowserOpen) ? Visibility.Visible : Visibility.Collapsed;
                if (isSettings && !isBrowserOpen) {
                    LoadSettingsPageData();
                }
            }
        }



        private void SubFilter_Checked(object sender, RoutedEventArgs e) {

            if (VersionTabs == null) return;

            if (SubFilterAll != null && SubFilterAll.IsChecked == true) {

                VersionTabs.SelectedItem = AllTab;

            } else if (SubFilterRelease != null && SubFilterRelease.IsChecked == true) {

                VersionTabs.SelectedItem = ReleaseTab;

            } else if (SubFilterPreview != null && SubFilterPreview.IsChecked == true) {

                VersionTabs.SelectedItem = PreviewTab;

            } else if (SubFilterBeta != null && SubFilterBeta.IsChecked == true) {

                VersionTabs.SelectedItem = BetaTab;

            } else if (SubFilterImported != null && SubFilterImported.IsChecked == true) {

                VersionTabs.SelectedItem = ImportedTab;

            }

        }



        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {

            if (e.ChangedButton == MouseButton.Left) DragMove();

        }



        private void BtnMinimize_Click(object sender, RoutedEventArgs e) {

            WindowState = WindowState.Minimized;

        }



        private void BtnMaximize_Click(object sender, RoutedEventArgs e) {

            WindowState = (WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);

        }



        private int _modalDimCount = 0;

        public void ShowModalDim() {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(ShowModalDim);
                return;
            }
            _modalDimCount++;
            if (GlobalModalDimOverlay != null) {
                GlobalModalDimOverlay.Visibility = Visibility.Visible;
            }
            if (ServersWebView != null) {
                ServersWebView.IsHitTestVisible = false;
            }
        }

        public void HideModalDim() {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(HideModalDim);
                return;
            }
            _modalDimCount = Math.Max(0, _modalDimCount - 1);
            if (_modalDimCount == 0) {
                if (GlobalModalDimOverlay != null) {
                    GlobalModalDimOverlay.Visibility = Visibility.Collapsed;
                }
                if (ServersWebView != null) {
                    ServersWebView.IsHitTestVisible = true;
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) {
            Close();
        }


        private static bool IsMinecraftRunning() {
            try {
                return Process.GetProcessesByName("Minecraft.Windows").Length > 0 ||
                       Process.GetProcessesByName("Minecraft").Length > 0;
            } catch {
                return false;
            }
        }

        private CancellationTokenSource? _minimizeTrimCts = null;

        private void MainWindow_StateChanged(object? sender, EventArgs e) {
            if (WindowState == WindowState.Minimized) {
                _minimizeTrimCts?.Cancel();
                _minimizeTrimCts = new CancellationTokenSource();
                var token = _minimizeTrimCts.Token;

                _ = Task.Run(async () => {
                    try {
                        // Wait 5 seconds after minimizing as requested
                        await Task.Delay(5000, token);
                        if (token.IsCancellationRequested) return;

                        bool isStillMinimized = false;
                        bool isBrowserOpen = false;

                        Dispatcher.Invoke(() => {
                            isStillMinimized = (WindowState == WindowState.Minimized);
                            isBrowserOpen = (InAppBrowserOverlay != null && InAppBrowserOverlay.Visibility == Visibility.Visible);
                        });

                        if (!isStillMinimized || token.IsCancellationRequested) return;

                        // STRICT RULE: Only zero the cache if Minecraft is actively running and launcher is minimized!
                        bool mcRunning = IsMinecraftRunning();
                        if (mcRunning) {
                            await Dispatcher.InvokeAsync(async () => {
                                // If in-app browser is currently open, DO NOT touch its cache!
                                if (!isBrowserOpen) {
                                    try {
                                        if (BrowserWebView?.CoreWebView2 != null) {
                                            await ClearWebViewCacheAsync(BrowserWebView.CoreWebView2);
                                        }
                                    } catch { }
                                }

                                // Clear ServersWebView cache since Minecraft is playing and launcher is minimized
                                try {
                                    if (ServersWebView?.CoreWebView2 != null) {
                                        await ClearWebViewCacheAsync(ServersWebView.CoreWebView2);
                                    }
                                } catch { }
                            });

                            try {
                                GC.Collect(2, GCCollectionMode.Forced, true, true);
                                GC.WaitForPendingFinalizers();
                                NativeMemoryHelper.TrimWorkingSet();
                            } catch { }
                        }

                    } catch (OperationCanceledException) { }
                    catch { }
                });
            } else {
                _minimizeTrimCts?.Cancel();
                if (TabNavPlay?.IsChecked == true && (InAppBrowserOverlay == null || InAppBrowserOverlay.Visibility != Visibility.Visible)) {
                    if (ServersWebView != null && ServersWebView.Visibility == Visibility.Visible) {
                        try {
                            ServersWebView.CoreWebView2?.Resume();
                        } catch { }
                    }
                }
            }
        }
    }
}
