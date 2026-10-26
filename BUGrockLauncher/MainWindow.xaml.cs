using BUGrockLauncher.Services;
﻿using System;
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

            var versionsApiUWP = UserPrefs.VersionsApiUWP != "" ? UserPrefs.VersionsApiUWP : VERSIONS_API_UWP;
            var versionsApiGDK = UserPrefs.VersionsApiGDK != "" ? UserPrefs.VersionsApiGDK : VERSIONS_API_GDK;
            _versions = new VersionList("versions_uwp.json", IMPORTED_VERSIONS_PATH, versionsApiUWP, this, VersionEntryPropertyChanged, "versions_gdk.json", versionsApiGDK);

            InitializeComponent();
            DeleteAppxAfterDownloadOption.DataContext = this;
            AutoFetchDecryptionKeysOption.DataContext = this;
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
                _versions.VersionsApiUWP = UserPrefs.VersionsApiUWP != "" ? UserPrefs.VersionsApiUWP : VERSIONS_API_UWP;
                _versions.VersionsApiGDK = UserPrefs.VersionsApiGDK != "" ? UserPrefs.VersionsApiGDK : VERSIONS_API_GDK;
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
            bool isBrowserOpen = (InAppBrowserOverlay != null && InAppBrowserOverlay.Visibility == Visibility.Visible);

            PlayViewGrid.Visibility = (isPlay && !isBrowserOpen && !isAddons) ? Visibility.Visible : Visibility.Collapsed;
            if (ServerCardBorder != null) ServerCardBorder.Visibility = (isPlay && !isBrowserOpen && !isAddons) ? Visibility.Visible : Visibility.Collapsed;
            if (ServersWebView != null) ServersWebView.Visibility = (isPlay && !isBrowserOpen && !isAddons) ? Visibility.Visible : Visibility.Collapsed;

            InstallationsViewGrid.Visibility = (isInstallations && !isBrowserOpen && !isAddons) ? Visibility.Visible : Visibility.Collapsed;

            if (AddonsManagerViewGrid != null) {
                AddonsManagerViewGrid.Visibility = (isAddons && !isBrowserOpen) ? Visibility.Visible : Visibility.Collapsed;
                if (isAddons && !isBrowserOpen) {
                    LoadAddonsManagerData();
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

        private void BtnClose_Click(object sender, RoutedEventArgs e) {
            Close();
        }

    }
}
