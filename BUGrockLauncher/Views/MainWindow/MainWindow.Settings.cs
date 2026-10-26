using Microsoft.Web.WebView2.Core;
using BUGrockLauncher.Helpers;
using BUGrockLauncher.Services;
using System.Windows.Media;
using System.Windows.Controls;
﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Newtonsoft.Json;

namespace BUGrockLauncher {
    public partial class MainWindow {
        private void ShowInstalledVersionsOnlyCheckbox_Changed(object sender, RoutedEventArgs e) {
            UserPrefs.ShowInstalledOnly = ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false;
            RefreshLists();
            RewritePrefs();
        }

        private void RefreshLists() {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.InvokeAsync(RefreshLists);
                return;
            }
            foreach (var list in _versionListViews) {
                list.View.Refresh();
            }
        }

        private void DeleteAppxAfterDownloadCheck_Changed(object sender, RoutedEventArgs e) {
            if (SettingsDeleteAppxToggle != null) {
                UserPrefs.DeleteAppxAfterDownload = SettingsDeleteAppxToggle.IsChecked ?? false;
            }
            RewritePrefs();
        }

        private void AutoFetchDecryptionKeysCheck_Changed(object sender, RoutedEventArgs e) {
            if (SettingsAutoKeysToggle != null) {
                UserPrefs.AutoFetchDecryptionKeys = SettingsAutoKeysToggle.IsChecked ?? false;
            }
            RewritePrefs();
        }

        private void RewritePrefs() {
            File.WriteAllText(PREFS_PATH, JsonConvert.SerializeObject(UserPrefs));
        }

        private void MenuItemOpenLogFileClicked(object sender, RoutedEventArgs e) {
            if (!File.Exists(@"Log.txt")) {
                ThemedMessageBox.Show("Log file not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } else 
                Process.Start(new ProcessStartInfo
                {
                    FileName = @"Log.txt",
                    UseShellExecute = true
                });
        }

        private void MenuItemOpenDataDirClicked(object sender, RoutedEventArgs e) {
            Process.Start(@"explorer.exe", Directory.GetCurrentDirectory());
        }

        private void MenuItemUninstallAllVersionsClicked(object sender, RoutedEventArgs e) {
            var result = ThemedMessageBox.Show(
                "All versions of Minecraft managed by the launcher will be unregistered and deleted.\n" +
                    "Your data (worlds, etc.) won't be removed.\n\n" +
                    "Note: If you just want to reinstall Minecraft from the Store, and don't want to delete your launcher-managed versions, you can use the \"Cleanup for Store reinstall\" option instead.\n\n" +
                    "Are you sure you want to continue?",
                "Uninstall all versions",
                MessageBoxButton.OKCancel
            );
            if (result == MessageBoxResult.OK) {
                Trace.WriteLine("Starting uninstall of ALL versions!");
                foreach (var version in _versions) {
                    if (version.IsInstalled) {
                        InvokeRemove(version);
                    }
                }
                Trace.WriteLine("Scheduled uninstall of ALL versions.");
            }
        }

        private void MenuItemRefreshVersionListClicked(object sender, RoutedEventArgs e) {
            Dispatcher.Invoke(LoadVersionList);
        }

        private void OnEndpointChangedHandler(object sender, string newUwpIdsEndpoint, string newGdkPackageUrlsEndpoint) {
            UserPrefs.VersionsApiUWP = newUwpIdsEndpoint == "" ? VERSIONS_API_UWP : newUwpIdsEndpoint;
            UserPrefs.VersionsApiGDK = newGdkPackageUrlsEndpoint == "" ? VERSIONS_API_GDK : newGdkPackageUrlsEndpoint;
            Dispatcher.Invoke(LoadVersionList);
            RewritePrefs();
        }

        private void MenuItemSetVersionListEndpointClicked(object sender, RoutedEventArgs e) {
            var dialog = new VersionListEndpointDialog(UserPrefs.VersionsApiUWP, UserPrefs.VersionsApiGDK) {
                Owner = this
            };
            dialog.OnEndpointChanged += OnEndpointChangedHandler;

            dialog.Show();
        }

        private string BuildDataLocationMessage(string displayName, string packageFamily) {
            var message = "Data for " + displayName + ":";
            var locations = LocateMinecraftWorlds(packageFamily);
            if (locations.Count == 0) {
                return message + "\n - (no folders with worlds found)";
            }

            foreach (var loc in locations) {
                message += $"\n - {loc.Value} worlds found in {loc.Key}";
            }
            return message;
        }

        private void MenuItemFindMyDataClicked(object sender, RoutedEventArgs e) {
            ThemedMessageBox.Show(
                BuildDataLocationMessage("Release", MinecraftPackageFamilies.MINECRAFT) + "\n\n" +
                BuildDataLocationMessage("Preview", MinecraftPackageFamilies.MINECRAFT_PREVIEW) + "\n\n" +
                "Note: Data folders containing no worlds are not shown.",
                "Minecraft data locations"
            );
        }

        private async void MenuItemCleanupForStoreInstallClicked(object sender, RoutedEventArgs e) {
            var dialog = new ProgressDialog();
            dialog.Owner = this;
            bool allowClose = false;
            dialog.Closing += (object? sender_, CancelEventArgs e_) => {
                if (!allowClose) {
                    e_.Cancel = true;
                }
            };

            dialog.Show();

            Trace.WriteLine("Cleaning up system");
            try {
                await UnregisterPackage(MinecraftPackageFamilies.MINECRAFT, null, skipBackup: false);
                await UnregisterPackage(MinecraftPackageFamilies.MINECRAFT_PREVIEW, null, skipBackup: false);
            } catch (Exception ex) {
                Trace.WriteLine("Error cleaning up: " + ex.Message);
                ThemedMessageBox.Show("An error occurred while cleaning up. Check the log for details.", "Error");
            }
            Trace.WriteLine("Done cleaning up");
            allowClose = true;
            dialog.Close();
            ThemedMessageBox.Show("Cleanup completed. You should now be able to install Minecraft from Microsoft Store.", "Cleanup completed");
        }

    // ==============================================================
        // SETTINGS PAGE INITIALIZATION & ACTIONS
        // ==============================================================
        public void LoadSettingsPageData() {
            try {
                // 0. Web & Server Hub Performance
                if (SettingsWebPerfCombo != null) {
                    foreach (ComboBoxItem item in SettingsWebPerfCombo.Items) {
                        if (item.Tag?.ToString() == UserPrefs.WebPerformanceMode) {
                            SettingsWebPerfCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // 1. FPS Unlocker
                if (SettingsFpsUnlockToggle != null) {
                    SettingsFpsUnlockToggle.IsChecked = UserPrefs.FpsUnlockerEnabled;
                }
                UpdateFpsStatusUI();

                if (SettingsFpsCombo != null) {
                    foreach (ComboBoxItem item in SettingsFpsCombo.Items) {
                        if (item.Tag?.ToString() == UserPrefs.MaxFramerate.ToString()) {
                            SettingsFpsCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // 2. World Backups
                if (SettingsAutoBackupToggle != null) {
                    SettingsAutoBackupToggle.IsChecked = UserPrefs.WorldAutoBackupEnabled;
                }
                UpdateBackupStatusUI();

                // 3. Discord RPC
                if (SettingsDiscordRpcToggle != null) {
                    SettingsDiscordRpcToggle.IsChecked = UserPrefs.DiscordRpcEnabled;
                }
                UpdateDiscordStatusUI();

                // 4. General Preferences
                if (SettingsAutoKeysToggle != null) {
                    SettingsAutoKeysToggle.IsChecked = UserPrefs.AutoFetchDecryptionKeys;
                }
                if (SettingsDeleteAppxToggle != null) {
                    SettingsDeleteAppxToggle.IsChecked = UserPrefs.DeleteAppxAfterDownload;
                }
            } catch (Exception ex) {
                Trace.WriteLine("LoadSettingsPageData error: " + ex.Message);
            }
        }

        private void UpdateFpsStatusUI() {
            try {
                bool isUnlocked = FpsUnlockerHelper.CheckCurrentStatus(out bool vsyncOff, out int maxFps);
                if (SettingsFpsStatusBadge != null && SettingsFpsStatusText != null) {
                    if (vsyncOff && UserPrefs.FpsUnlockerEnabled) {
                        SettingsFpsStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x2E, 0x22));
                        SettingsFpsStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                        SettingsFpsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                        SettingsFpsStatusText.Text = "FPS UNLOCKED (VSYNC OFF)";
                    } else {
                        SettingsFpsStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x24, 0x18));
                        SettingsFpsStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0x9E, 0x2E));
                        SettingsFpsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF6, 0xAD, 0x55));
                        SettingsFpsStatusText.Text = "VSYNC ACTIVE / 60 FPS";
                    }
                }
            } catch { }
        }

        private void UpdateBackupStatusUI() {
            try {
                var (count, status) = WorldBackupHelper.GetBackupsStatus();
                if (SettingsBackupStatusText != null) {
                    SettingsBackupStatusText.Text = status;
                }
            } catch { }
        }

        private void UpdateDiscordStatusUI() {
            try {
                if (SettingsDiscordStatusBadge != null && SettingsDiscordStatusText != null) {
                    bool enabled = UserPrefs.DiscordRpcEnabled;
                    SettingsDiscordStatusBadge.Background = enabled ? new SolidColorBrush(Color.FromRgb(0x1C, 0x2E, 0x22)) : new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x36));
                    SettingsDiscordStatusBadge.BorderBrush = enabled ? new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69)) : new SolidColorBrush(Color.FromRgb(0x3E, 0x42, 0x59));
                    SettingsDiscordStatusText.Foreground = enabled ? new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69)) : new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4));
                    SettingsDiscordStatusText.Text = enabled ? "ACTIVE" : "DISABLED";
                }
            } catch { }
        }

        private void SettingsBackToPlay_Click(object sender, RoutedEventArgs e) {
            if (TabNavPlay != null) TabNavPlay.IsChecked = true;
        }

        private void SettingsFpsUnlockToggle_Click(object sender, RoutedEventArgs e) {
            UserPrefs.FpsUnlockerEnabled = SettingsFpsUnlockToggle.IsChecked ?? false;
            RewritePrefs();
            if (UserPrefs.FpsUnlockerEnabled) {
                FpsUnlockerHelper.ApplyFpsUnlock(disableVsync: true, UserPrefs.MaxFramerate);
            } else {
                FpsUnlockerHelper.ApplyFpsUnlock(disableVsync: false, UserPrefs.MaxFramerate);
            }
            UpdateFpsStatusUI();
        }

        private void SettingsFpsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (SettingsFpsCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int fps)) {
                UserPrefs.MaxFramerate = fps;
                RewritePrefs();
                if (UserPrefs.FpsUnlockerEnabled) {
                    FpsUnlockerHelper.ApplyFpsUnlock(disableVsync: true, fps);
                }
            }
        }

        private void SettingsApplyFpsBtn_Click(object sender, RoutedEventArgs e) {
            int modified = FpsUnlockerHelper.ApplyFpsUnlock(disableVsync: UserPrefs.FpsUnlockerEnabled, UserPrefs.MaxFramerate);
            UpdateFpsStatusUI();
            ThemedMessageBox.Show($"Performance tweaks applied to {modified} options.txt file(s)!\n\nVSync: {(UserPrefs.FpsUnlockerEnabled ? "Disabled (Unlocked)" : "Enabled")}\nMax Framerate: {(UserPrefs.MaxFramerate == 0 ? "Unlimited" : UserPrefs.MaxFramerate.ToString())}", "Performance Tweaks Applied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SettingsOpenOptionsTxtBtn_Click(object sender, RoutedEventArgs e) {
            var files = FpsUnlockerHelper.FindAllOptionsFiles();
            if (files.Count > 0) {
                Process.Start(new ProcessStartInfo { FileName = files[0], UseShellExecute = true });
            } else {
                ThemedMessageBox.Show("No options.txt file found. Run Minecraft Bedrock at least once or apply optimizations first.", "Notice");
            }
        }

        private void SettingsAutoBackupToggle_Click(object sender, RoutedEventArgs e) {
            UserPrefs.WorldAutoBackupEnabled = SettingsAutoBackupToggle.IsChecked ?? false;
            RewritePrefs();
        }

        private void SettingsBackupWorldsNowBtn_Click(object sender, RoutedEventArgs e) {
            try {
                var (count, size, path) = WorldBackupHelper.BackupWorldsNow();
                UpdateBackupStatusUI();
                if (count > 0) {
                    string sizeMb = (size / 1024.0 / 1024.0).ToString("F2");
                    ThemedMessageBox.Show($"Backed up {count} world(s) successfully!\nArchive size: {sizeMb} MB\n\nSaved to: {path}", "Backup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    ThemedMessageBox.Show("No Minecraft worlds found to backup in com.mojang.", "Notice");
                }
            } catch (Exception ex) {
                ThemedMessageBox.Show($"Backup failed: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SettingsOpenBackupsFolderBtn_Click(object sender, RoutedEventArgs e) {
            WorldBackupHelper.OpenBackupsFolder();
        }

        private void SettingsDiscordRpcToggle_Click(object sender, RoutedEventArgs e) {
            UserPrefs.DiscordRpcEnabled = SettingsDiscordRpcToggle.IsChecked ?? false;
            RewritePrefs();
            DiscordRpcService.SetEnabled(UserPrefs.DiscordRpcEnabled);
            UpdateDiscordStatusUI();
        }

        private void SettingsAutoKeysToggle_Click(object sender, RoutedEventArgs e) {
            UserPrefs.AutoFetchDecryptionKeys = SettingsAutoKeysToggle.IsChecked ?? false;
            RewritePrefs();
        }

        private void SettingsDeleteAppxToggle_Click(object sender, RoutedEventArgs e) {
            UserPrefs.DeleteAppxAfterDownload = SettingsDeleteAppxToggle.IsChecked ?? false;
            RewritePrefs();
        }

        private void SettingsLaunchCheckerBtn_Click(object sender, RoutedEventArgs e) {
            string exe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BUGrock_Launcher_Bin", "BUGrock_Check.exe");
            if (!File.Exists(exe)) exe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BUGrock_Check.exe");
            if (File.Exists(exe)) {
                Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
            } else {
                ThemedMessageBox.Show("BUGrock_Check.exe utility not found.", "Error");
            }
        }


        public void TopMenuFile_Click(object sender, RoutedEventArgs e) {
            if (TabNavSettings != null) TabNavSettings.IsChecked = true;
        }

        public void TopMenuTools_Click(object sender, RoutedEventArgs e) {
            if (TabNavSettings != null) TabNavSettings.IsChecked = true;
        }

        public void TopMenuOptions_Click(object sender, RoutedEventArgs e) {
            if (TabNavSettings != null) TabNavSettings.IsChecked = true;
        }

        private async void SettingsWebPerfCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (SettingsWebPerfCombo?.SelectedItem is ComboBoxItem item && item.Tag != null) {
                string oldMode = UserPrefs.WebPerformanceMode;
                UserPrefs.WebPerformanceMode = item.Tag.ToString() ?? "High";
                RewritePrefs();
                ApplyWebPerformanceMode(UserPrefs.WebPerformanceMode);

                if (ServersWebView?.CoreWebView2 != null) {
                    await ApplyPerformanceScriptAsync(ServersWebView.CoreWebView2, UserPrefs.WebPerformanceMode);
                    if (oldMode != UserPrefs.WebPerformanceMode) {
                        ServersWebView.CoreWebView2.Reload();
                    }
                }
                if (BrowserWebView?.CoreWebView2 != null) {
                    await ApplyPerformanceScriptAsync(BrowserWebView.CoreWebView2, UserPrefs.WebPerformanceMode);
                }
            }
        }

        private void ApplyWebPerformanceMode(string mode) {
            try {
                var memTarget = (mode == "Low")
                    ? CoreWebView2MemoryUsageTargetLevel.Low
                    : CoreWebView2MemoryUsageTargetLevel.Normal;

                if (ServersWebView?.CoreWebView2 != null) {
                    ServersWebView.CoreWebView2.MemoryUsageTargetLevel = memTarget;
                    _ = ApplyPerformanceScriptAsync(ServersWebView.CoreWebView2, mode);
                    if (mode == "High") {
                        _ = ClearWebViewCacheAsync(ServersWebView.CoreWebView2);
                    }
                }
                if (BrowserWebView?.CoreWebView2 != null) {
                    BrowserWebView.CoreWebView2.MemoryUsageTargetLevel = memTarget;
                    _ = ApplyPerformanceScriptAsync(BrowserWebView.CoreWebView2, mode);
                    if (mode == "High") {
                        _ = ClearWebViewCacheAsync(BrowserWebView.CoreWebView2);
                    }
                }

                if (mode == "Low") {
                    _ = Task.Run(() => {
                        try {
                            GC.Collect(2, GCCollectionMode.Forced, true, true);
                            GC.WaitForPendingFinalizers();
                            NativeMemoryHelper.TrimWorkingSet();
                        } catch { }
                    });
                }
            } catch { }
        }
}
}
