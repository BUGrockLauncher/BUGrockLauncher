using System.Threading.Tasks;
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
            var dialog = new VersionListEndpointDialog(UserPrefs.VersionsApiUWP, UserPrefs.VersionsApiGDK);
            BUGrockLauncher.Helpers.ModalDialogHelper.SafeSetOwner(dialog, this);
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
            string msg = BuildDataLocationMessage("Release", MinecraftPackageFamilies.MINECRAFT) + "\n\n" +
                BuildDataLocationMessage("Preview", MinecraftPackageFamilies.MINECRAFT_PREVIEW) + "\n\n" +
                "Note: Data folders containing no worlds are not shown.\n\nWould you like to open your primary worlds folder in File Explorer?";

            var res = ThemedMessageBox.Show(
                msg,
                "Minecraft Worlds Locations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );

            if (res == MessageBoxResult.Yes) {
                WorldBackupHelper.OpenPrimaryWorldsFolder();
            }
        }

        private async void MenuItemCleanupForStoreInstallClicked(object sender, RoutedEventArgs e) {
            var dialog = new ProgressDialog();
            BUGrockLauncher.Helpers.ModalDialogHelper.SafeSetOwner(dialog, this);
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

                // 1.5. Smooth Animations
                AnimationHelper.IsLowPerformanceMode = (UserPrefs.WebPerformanceMode == "Low");
                if (AnimationHelper.IsLowPerformanceMode) {
                    UserPrefs.SmoothAnimationsEnabled = false;
                }
                if (SettingsSmoothAnimationsToggle != null) {
                    SettingsSmoothAnimationsToggle.IsChecked = UserPrefs.SmoothAnimationsEnabled;
                    AnimationHelper.UpdatePillState(SettingsSmoothAnimationsToggle, animate: false);
                }
                AnimationHelper.AreAnimationsEnabled = UserPrefs.SmoothAnimationsEnabled;
                UpdateAnimationStatusUI();

                // 2. World Backups
                if (SettingsAutoBackupToggle != null) {
                    SettingsAutoBackupToggle.IsChecked = UserPrefs.WorldAutoBackupEnabled;
                    AnimationHelper.UpdatePillState(SettingsAutoBackupToggle, animate: false);
                }
                UpdateBackupStatusUI();

                // 3. Discord RPC
                if (SettingsDiscordRpcToggle != null) {
                    SettingsDiscordRpcToggle.IsChecked = UserPrefs.DiscordRpcEnabled;
                    AnimationHelper.UpdatePillState(SettingsDiscordRpcToggle, animate: false);
                }
                UpdateDiscordStatusUI();

                // 4. General Preferences
                if (SettingsAutoKeysToggle != null) {
                    SettingsAutoKeysToggle.IsChecked = UserPrefs.AutoFetchDecryptionKeys;
                    AnimationHelper.UpdatePillState(SettingsAutoKeysToggle, animate: false);
                }
                if (SettingsDeleteAppxToggle != null) {
                    SettingsDeleteAppxToggle.IsChecked = UserPrefs.DeleteAppxAfterDownload;
                    AnimationHelper.UpdatePillState(SettingsDeleteAppxToggle, animate: false);
                }
                if (SettingsFpsUnlockToggle != null) {
                    AnimationHelper.UpdatePillState(SettingsFpsUnlockToggle, animate: false);
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

        

        private void UpdateAnimationStatusUI() {
            try {
                bool isLow = (UserPrefs.WebPerformanceMode == "Low");
                bool enabled = AnimationHelper.IsActive;
                if (SettingsAnimStatusBadge != null && SettingsAnimStatusLabel != null) {
                    if (enabled) {
                        SettingsAnimStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x2E, 0x24));
                        SettingsAnimStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                        SettingsAnimStatusLabel.Text = "Smooth 60fps Animations: ACTIVE";
                        SettingsAnimStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                    } else {
                        SettingsAnimStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x1A, 0x1A));
                        SettingsAnimStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E));
                        SettingsAnimStatusLabel.Text = isLow
                            ? "Disabled in Low Perf Mode (0ms Instant Snap)"
                            : "Instant 0ms Mode (Animations DISABLED)";
                        SettingsAnimStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0x81, 0x81));
                    }
                }
                            } catch { }
        }

        private void SettingsSmoothAnimationsToggle_Click(object sender, RoutedEventArgs e) {
            if (UserPrefs.WebPerformanceMode == "Low") {
                // In Low mode, all animations remain strictly disabled as requested
                UserPrefs.SmoothAnimationsEnabled = false;
                AnimationHelper.IsLowPerformanceMode = true;
                AnimationHelper.AreAnimationsEnabled = false;
                if (SettingsSmoothAnimationsToggle != null) {
                    SettingsSmoothAnimationsToggle.IsChecked = false;
                    AnimationHelper.UpdatePillState(SettingsSmoothAnimationsToggle, animate: false);
                }
                UpdateAnimationStatusUI();
                RewritePrefs();
                return;
            }
            UserPrefs.SmoothAnimationsEnabled = SettingsSmoothAnimationsToggle.IsChecked ?? true;
            AnimationHelper.AreAnimationsEnabled = UserPrefs.SmoothAnimationsEnabled;
            UpdateAnimationStatusUI();
            RewritePrefs();
            if (ServersWebView?.CoreWebView2 != null) {
                _ = ApplyPerformanceScriptAsync(ServersWebView.CoreWebView2, UserPrefs.WebPerformanceMode);
            }
            if (BrowserWebView?.CoreWebView2 != null) {
                _ = ApplyPerformanceScriptAsync(BrowserWebView.CoreWebView2, UserPrefs.WebPerformanceMode);
            }
        }

        private void SettingsTestAnimationBtn_Click(object sender, RoutedEventArgs e) {
            if (AnimationHelper.AreAnimationsEnabled) {
                ThemedMessageBox.Show("✨ Smooth physics & 60fps animations are ACTIVE!\n\nEvery page transition, button press, and dialog flows with fluid Ease-In/Out motion.", "Animation Test", MessageBoxButton.OK, MessageBoxImage.Information);
            } else {
                ThemedMessageBox.Show("⚡ Animations are currently turned OFF.\n\nAll views, buttons, and dialogs snap instantly with 0ms delay for maximum performance on any system.", "Animation Test", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SettingsFindWorldsBtn_Click(object sender, RoutedEventArgs e) {
            try {
                var worlds = WorldBackupHelper.GetAllDetectedWorldsInfo();
                var dirs = WorldBackupHelper.FindAllWorldsDirectories();

                if (worlds.Count == 0) {
                    ThemedMessageBox.Show("No Minecraft Bedrock worlds were found in your com.mojang directories.\n\nMake sure you have created or played a world at least once in Minecraft Bedrock.", "Find My Worlds", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🔍 Found {worlds.Count} Minecraft Bedrock world(s) across {dirs.Count} location(s):\n");

                int displayCount = Math.Min(worlds.Count, 8);
                for (int i = 0; i < displayCount; i++) {
                    var w = worlds[i];
                    sb.AppendLine($"• {w.DisplayName} ({w.SizeFormatted}) - Last played: {w.LastModified:yyyy-MM-dd}");
                }
                if (worlds.Count > displayCount) {
                    sb.AppendLine($"... and {worlds.Count - displayCount} more world(s).");
                }

                sb.AppendLine("\nPrimary Location:");
                sb.AppendLine(dirs.FirstOrDefault() ?? "Unknown");
                sb.AppendLine("\nWould you like to open your primary worlds folder in File Explorer?");

                var result = ThemedMessageBox.Show(sb.ToString(), "Find My Worlds", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes) {
                    WorldBackupHelper.OpenPrimaryWorldsFolder();
                }
            } catch (Exception ex) {
                ThemedMessageBox.Show($"Error locating worlds: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SettingsAutoBackupToggle_Click(object sender, RoutedEventArgs e) {
            UserPrefs.WorldAutoBackupEnabled = SettingsAutoBackupToggle.IsChecked ?? false;
            RewritePrefs();
        }

        private async void SettingsBackupWorldsNowBtn_Click(object sender, RoutedEventArgs e) {
            var btn = sender as Button;
            object? prevContent = btn?.Content;
            try {
                if (btn != null) {
                    btn.IsEnabled = false;
                    btn.Content = "⏳ Scanning worlds...";
                }

                var progress = new Progress<string>(status => {
                    if (btn != null) {
                        btn.Content = $"⏳ {status}";
                    }
                });

                var res = await Task.Run(() => WorldBackupHelper.BackupAllVersionsOrganized(progress));
                UpdateBackupStatusUI();
                if (res.TotalWorlds > 0 && res.CreatedArchives.Count > 0) {
                    string sizeMb = (res.TotalBytes / 1024.0 / 1024.0).ToString("F2");
                    string archivesList = string.Join("\n", res.CreatedArchives.Select(p => $"• {Path.GetFileName(p)}"));
                    ThemedMessageBox.Show(
                        $"Backed up {res.TotalWorlds} world(s) across {res.TotalVersions} version(s) successfully!\n" +
                        $"Total size: {sizeMb} MB\n\n" +
                        $"Organized Archives:\n{archivesList}\n\n" +
                        $"Saved in:\n{WorldBackupHelper.BackupsDirectory}",
                        "Backup Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    WorldBackupHelper.OpenBackupsFolder();
                } else {
                    ThemedMessageBox.Show("No Minecraft worlds found to backup in any version.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            } catch (Exception ex) {
                ThemedMessageBox.Show($"Backup failed: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                if (btn != null) {
                    btn.IsEnabled = true;
                    btn.Content = prevContent ?? "💾 Backup All Worlds Now";
                }
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
                string newMode = item.Tag.ToString() ?? "High";
                UserPrefs.WebPerformanceMode = newMode;

                if (newMode == "Low") {
                    AnimationHelper.IsLowPerformanceMode = true;
                    UserPrefs.SmoothAnimationsEnabled = false;
                    AnimationHelper.AreAnimationsEnabled = false;
                    if (SettingsSmoothAnimationsToggle != null) {
                        SettingsSmoothAnimationsToggle.IsChecked = false;
                        AnimationHelper.UpdatePillState(SettingsSmoothAnimationsToggle, animate: false);
                    }
                    ResetHeadToCenter(animated: false);
                    SetServerCardHover(false);
                } else {
                    AnimationHelper.IsLowPerformanceMode = false;
                    if (oldMode == "Low") {
                        UserPrefs.SmoothAnimationsEnabled = true;
                        AnimationHelper.AreAnimationsEnabled = true;
                        if (SettingsSmoothAnimationsToggle != null) {
                            SettingsSmoothAnimationsToggle.IsChecked = true;
                            AnimationHelper.UpdatePillState(SettingsSmoothAnimationsToggle, animate: true);
                        }
                    }
                }

                UpdateAnimationStatusUI();
                RewritePrefs();
                ApplyWebPerformanceMode(newMode);

                if (ServersWebView?.CoreWebView2 != null) {
                    await ApplyPerformanceScriptAsync(ServersWebView.CoreWebView2, newMode);
                }
                if (BrowserWebView?.CoreWebView2 != null) {
                    await ApplyPerformanceScriptAsync(BrowserWebView.CoreWebView2, newMode);
                }
            }
        }

        private void ApplyWebPerformanceMode(string mode) {
            try {
                AnimationHelper.IsLowPerformanceMode = (mode == "Low");
                var memTarget = (mode == "Low")
                    ? CoreWebView2MemoryUsageTargetLevel.Low
                    : CoreWebView2MemoryUsageTargetLevel.Normal;

                if (ServersWebView?.CoreWebView2 != null) {
                    ServersWebView.CoreWebView2.MemoryUsageTargetLevel = memTarget;
                    _ = ApplyPerformanceScriptAsync(ServersWebView.CoreWebView2, mode);
                }
                if (BrowserWebView?.CoreWebView2 != null) {
                    BrowserWebView.CoreWebView2.MemoryUsageTargetLevel = memTarget;
                    _ = ApplyPerformanceScriptAsync(BrowserWebView.CoreWebView2, mode);
                }
                            } catch { }
        }
}
}
