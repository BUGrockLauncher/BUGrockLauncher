using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BUGrockLauncher {
    public partial class MainWindow {
        #region Addon & Content Manager
        private List<DownloadedAddonItem> _allDownloadedAddons = new List<DownloadedAddonItem>();
        private List<TargetMinecraftVersion> _allTargetVersions = new List<TargetMinecraftVersion>();


        private void AddonsBackToPlayBtn_Click(object sender, RoutedEventArgs e) {
            if (TabNavPlay != null) {
                TabNavPlay.IsChecked = true;
            }
        }

        private void AddonsRefreshBtn_Click(object sender, RoutedEventArgs e) {
            LoadAddonsManagerData();
        }

        private void AddonsOpenDdfwFolderBtn_Click(object sender, RoutedEventArgs e) {
            try {
                DDFWManager.EnsureDirectories();
                Process.Start("explorer.exe", $"\"{DDFWManager.RootFolder}\"");
            } catch (Exception ex) {
                ThemedMessageBox.Show($"Could not open DDFW folder: {ex.Message}", "Error");
            }
        }

        private void AddonsImportFileBtn_Click(object sender, RoutedEventArgs e) {
            var ofd = new Microsoft.Win32.OpenFileDialog {
                Filter = "Minecraft Packages (*.mcpack;*.mcaddon;*.mcworld;*.zip)|*.mcpack;*.mcaddon;*.mcworld;*.zip|All Files (*.*)|*.*",
                Title = "Import Minecraft Addon / Pack"
            };
            if (ofd.ShowDialog() == true) {
                try {
                    DDFWManager.EnsureDirectories();
                    string targetFolder = DDFWManager.DetermineTargetFolder(string.Empty, ofd.FileName, ofd.FileName);
                    string destFile = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(ofd.FileName));
                    File.Copy(ofd.FileName, destFile, overwrite: true);
                    ThemedMessageBox.Show($"Imported '{System.IO.Path.GetFileName(ofd.FileName)}' successfully!", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadAddonsManagerData();
                } catch (Exception ex) {
                    ThemedMessageBox.Show($"Failed to import file: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void LoadAddonsManagerData() {
            try {
                if (AddonsCountStatusLabel != null) AddonsCountStatusLabel.Text = "Scanning downloaded addons...";
                _allDownloadedAddons = await Task.Run(() => AddonManagerService.ScanDownloadedAddons());

                var active = GetCurrentlyActiveVersion();
                _allTargetVersions = await Task.Run(() => AddonManagerService.ScanTargetVersions(active, _versions));

                ApplyAddonsFilter();

                if (TargetVersionsListBox != null) {
                    TargetVersionsListBox.ItemsSource = _allTargetVersions;
                }
            } catch (Exception ex) {
                Trace.WriteLine($"LoadAddonsManagerData error: {ex.Message}");
            }
        }

        private void AddonsSearchBox_TextChanged(object sender, TextChangedEventArgs e) {
            if (AddonCatFilterVersionManager?.IsChecked == true) {
                ApplyVersionManagerFilter();
            } else {
                ApplyAddonsFilter();
            }
        }

        private void AddonCatFilter_Checked(object sender, RoutedEventArgs e) {
            if (AddonCatFilterVersionManager?.IsChecked == true) {
                // Collapse right side panel and divider
                if (AddonsContentDividerCol != null) AddonsContentDividerCol.Width = new GridLength(0);
                if (AddonsTargetPanelCol != null) AddonsTargetPanelCol.Width = new GridLength(0);
                if (AddonsTargetPanelBorder != null) AddonsTargetPanelBorder.Visibility = Visibility.Collapsed;

                // Toggle views
                if (DdfwAddonsContainerGrid != null) DdfwAddonsContainerGrid.Visibility = Visibility.Collapsed;
                if (VersionManagerContainerGrid != null) VersionManagerContainerGrid.Visibility = Visibility.Visible;

                LoadVersionManagerData();
            } else {
                // Restore right side panel and divider
                if (AddonsContentDividerCol != null) AddonsContentDividerCol.Width = new GridLength(14);
                if (AddonsTargetPanelCol != null) AddonsTargetPanelCol.Width = new GridLength(360);
                if (AddonsTargetPanelBorder != null) AddonsTargetPanelBorder.Visibility = Visibility.Visible;

                // Toggle views
                if (DdfwAddonsContainerGrid != null) DdfwAddonsContainerGrid.Visibility = Visibility.Visible;
                if (VersionManagerContainerGrid != null) VersionManagerContainerGrid.Visibility = Visibility.Collapsed;

                ApplyAddonsFilter();
            }
        }

        private List<ManagedVersionItem> _allManagedVersions = new List<ManagedVersionItem>();
        private ManagedVersionItem? _currentModalVersion;

        private async void LoadVersionManagerData() {
            try {
                var active = GetCurrentlyActiveVersion();
                _allManagedVersions = await Task.Run(() => AddonManagerService.ScanManagedVersions(active, _versions));
                ApplyVersionManagerFilter();
            } catch (Exception ex) {
                Trace.WriteLine($"LoadVersionManagerData error: {ex.Message}");
            }
        }

        private void ApplyVersionManagerFilter() {
            if (ManagedVersionsItemsControl == null) return;
            string query = (AddonsSearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(query)) {
                ManagedVersionsItemsControl.ItemsSource = _allManagedVersions;
            } else {
                ManagedVersionsItemsControl.ItemsSource = _allManagedVersions.Where(v =>
                    v.Name.ToLowerInvariant().Contains(query) ||
                    v.VersionString.ToLowerInvariant().Contains(query) ||
                    v.DirectoryPath.ToLowerInvariant().Contains(query)).ToList();
            }
        }

        private void RefreshVersionManager_Click(object sender, RoutedEventArgs e) {
            LoadVersionManagerData();
        }

        private void ToggleVersionExpand_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is ManagedVersionItem item) {
                item.IsExpanded = !item.IsExpanded;
            }
        }

        private void OpenVersionFolder_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is ManagedVersionItem item) {
                if (Directory.Exists(item.DirectoryPath)) {
                    Process.Start("explorer.exe", $"\"{item.DirectoryPath}\"");
                } else {
                    ThemedMessageBox.Show($"Directory not found:\n{item.DirectoryPath}", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void DeleteInstalledPack_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is InstalledItemInfo item) {
                var res = ThemedMessageBox.Show(
                    $"Are you sure you want to delete '{item.Name}'?\n\nFolder: {item.FolderName}\nPath: {item.ItemPath}",
                    "Delete Installed Pack",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );
                if (res == MessageBoxResult.Yes) {
                    bool deleted = AddonManagerService.DeleteInstalledItem(item);
                    if (deleted) {
                        if (item.OwnerVersion != null) {
                            if (item.ItemType == "ResourcePack") item.OwnerVersion.ResourcePacks.Remove(item);
                            else if (item.ItemType == "BehaviorPack") item.OwnerVersion.BehaviorPacks.Remove(item);
                            else if (item.ItemType == "SkinPack") item.OwnerVersion.SkinPacks.Remove(item);
                            item.OwnerVersion.NotifyCounts();
                        }
                    } else {
                        ThemedMessageBox.Show("Could not delete pack folder. If Minecraft is open, please close it first.", "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void OpenSkinPackFolder_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is InstalledItemInfo item) {
                if (!string.IsNullOrEmpty(item.ItemPath) && Directory.Exists(item.ItemPath)) {
                    try {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.ItemPath}\"") { UseShellExecute = true });
                    } catch { }
                }
            }
        }

        private void OpenVersionSkinPacksDirectory_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is ManagedVersionItem ver) {
                string spPath = ver.GetSkinPacksDirectoryPath();
                if (!string.IsNullOrEmpty(spPath)) {
                    try {
                        Directory.CreateDirectory(spPath);
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{spPath}\"") { UseShellExecute = true });
                    } catch { }
                }
            }
        }

        private void DeleteInstalledWorld_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is InstalledItemInfo item) {
                var res = ThemedMessageBox.Show(
                    $"Are you sure you want to permanently delete world '{item.Name}'?\n\nFolder: {item.FolderName}",
                    "Delete Saved World",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );
                if (res == MessageBoxResult.Yes) {
                    bool deleted = AddonManagerService.DeleteInstalledItem(item);
                    if (deleted) {
                        if (item.OwnerVersion != null) {
                            item.OwnerVersion.Worlds.Remove(item);
                            item.OwnerVersion.NotifyCounts();
                        }
                    } else {
                        ThemedMessageBox.Show("Could not delete world folder. If Minecraft is open, please close it first.", "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void DeleteInstalledServer_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is InstalledServerInfo server) {
                var res = ThemedMessageBox.Show(
                    $"Are you sure you want to delete server '{server.ServerName}' ({server.ServerAddress}:{server.Port})?",
                    "Delete Multiplayer Server",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );
                if (res == MessageBoxResult.Yes) {
                    bool deleted = AddonManagerService.DeleteInstalledServer(server);
                    if (deleted) {
                        if (server.OwnerVersion != null) {
                            server.OwnerVersion.Servers.Remove(server);
                            server.OwnerVersion.NotifyCounts();
                        }
                    } else {
                        ThemedMessageBox.Show("Could not delete server entry from external_servers.txt.", "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void OpenAddServerModal_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is ManagedVersionItem version) {
                _currentModalVersion = version;
                if (AddServerTargetVersionLabel != null) {
                    AddServerTargetVersionLabel.Text = $"Target Version: {version.Name}";
                }
                if (NewServerNameBox != null) NewServerNameBox.Text = string.Empty;
                if (NewServerAddressBox != null) NewServerAddressBox.Text = string.Empty;
                if (NewServerPortBox != null) NewServerPortBox.Text = "19132";
                if (AddServerModalOverlay != null) AddServerModalOverlay.Visibility = Visibility.Visible;
            }
        }

        private void CancelAddServer_Click(object sender, RoutedEventArgs e) {
            if (AddServerModalOverlay != null) AddServerModalOverlay.Visibility = Visibility.Collapsed;
            _currentModalVersion = null;
        }

        private void ConfirmAddServer_Click(object sender, RoutedEventArgs e) {
            if (_currentModalVersion == null) return;
            string sName = (NewServerNameBox?.Text ?? string.Empty).Trim();
            string sAddr = (NewServerAddressBox?.Text ?? string.Empty).Trim();
            string sPortStr = (NewServerPortBox?.Text ?? "19132").Trim();

            if (string.IsNullOrEmpty(sName) || string.IsNullOrEmpty(sAddr)) {
                ThemedMessageBox.Show("Please enter both server name and address.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int port = 19132;
            int.TryParse(sPortStr, out port);
            if (port <= 0 || port > 65535) port = 19132;

            bool success = AddonManagerService.AddInstalledServer(_currentModalVersion, sName, sAddr, port);
            if (success) {
                _currentModalVersion.LoadDetails();
                if (AddServerModalOverlay != null) AddServerModalOverlay.Visibility = Visibility.Collapsed;
                _currentModalVersion = null;
            } else {
                ThemedMessageBox.Show("Failed to save server entry to external_servers.txt.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyAddonsFilter() {
            if (DownloadedAddonsListBox == null) return;

            string query = (AddonsSearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = _allDownloadedAddons.AsEnumerable();

            if (AddonCatFilterBehavior?.IsChecked == true) {
                filtered = filtered.Where(x => x.Category.IndexOf("Behavior", StringComparison.OrdinalIgnoreCase) != -1 || x.Category.IndexOf("Addon", StringComparison.OrdinalIgnoreCase) != -1);
            } else if (AddonCatFilterResource?.IsChecked == true) {
                filtered = filtered.Where(x => x.Category.IndexOf("Resource", StringComparison.OrdinalIgnoreCase) != -1 || x.Category.IndexOf("Texture", StringComparison.OrdinalIgnoreCase) != -1);
            } else if (AddonCatFilterWorlds?.IsChecked == true) {
                filtered = filtered.Where(x => x.Category.IndexOf("World", StringComparison.OrdinalIgnoreCase) != -1 || x.Category.IndexOf("Map", StringComparison.OrdinalIgnoreCase) != -1);
            } else if (AddonCatFilterSkins?.IsChecked == true) {
                filtered = filtered.Where(x => x.Category.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) != -1);
            }

            if (!string.IsNullOrEmpty(query)) {
                filtered = filtered.Where(x => x.DisplayName.ToLowerInvariant().Contains(query) || x.FileName.ToLowerInvariant().Contains(query));
            }

            var list = filtered.ToList();
            DownloadedAddonsListBox.ItemsSource = list;

            if (AddonsCountStatusLabel != null) {
                AddonsCountStatusLabel.Text = $"{list.Count} item(s) found in DDFW repository";
            }

            UpdateCompatibilityAndDetailsUI();
        }

        private void DownloadedAddonsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (DownloadedAddonsListBox != null && DownloadedAddonsListBox.SelectedIndex != -1) {
                DownloadedAddonsListBox.SelectedIndex = -1;
            }
            UpdateCompatibilityAndDetailsUI();
        }

        private void TargetVersionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (TargetVersionsListBox != null && TargetVersionsListBox.SelectedIndex != -1) {
                TargetVersionsListBox.SelectedIndex = -1;
            }
            UpdateCompatibilityAndDetailsUI();
        }

        private void AddonCheckbox_Changed(object sender, RoutedEventArgs e) {
            UpdateCompatibilityAndDetailsUI();
        }

        private void VersionCheckbox_Changed(object sender, RoutedEventArgs e) {
            UpdateCompatibilityAndDetailsUI();
        }

        private void AddonsSelectAll_Click(object sender, RoutedEventArgs e) {
            if (DownloadedAddonsListBox?.ItemsSource is IEnumerable<DownloadedAddonItem> visibleItems) {
                foreach (var addon in visibleItems) {
                    addon.IsSelected = true;
                }
            } else {
                foreach (var addon in _allDownloadedAddons) {
                    addon.IsSelected = true;
                }
            }
            UpdateCompatibilityAndDetailsUI();
        }

        private void AddonsClearAll_Click(object sender, RoutedEventArgs e) {
            foreach (var addon in _allDownloadedAddons) {
                addon.IsSelected = false;
            }
            UpdateCompatibilityAndDetailsUI();
        }

        private void VersionsSelectAll_Click(object sender, RoutedEventArgs e) {
            foreach (var ver in _allTargetVersions) {
                ver.IsSelected = true;
            }
            UpdateCompatibilityAndDetailsUI();
        }

        private void VersionsClearAll_Click(object sender, RoutedEventArgs e) {
            foreach (var ver in _allTargetVersions) {
                ver.IsSelected = false;
            }
            UpdateCompatibilityAndDetailsUI();
        }

        private List<DownloadedAddonItem> GetEffectiveSelectedAddons() {
            return _allDownloadedAddons.Where(a => a.IsSelected).ToList();
        }

        private List<TargetMinecraftVersion> GetEffectiveSelectedVersions() {
            return _allTargetVersions.Where(v => v.IsSelected).ToList();
        }

        private void UpdateCompatibilityAndDetailsUI() {
            var selectedPacks = GetEffectiveSelectedAddons();
            var selectedTargets = GetEffectiveSelectedVersions();

            if (AddonTargetPathText != null) {
                if (selectedTargets.Count == 1) {
                    AddonTargetPathText.Text = selectedTargets[0].DirectoryPath;
                    AddonTargetPathText.ToolTip = selectedTargets[0].DirectoryPath;
                } else if (selectedTargets.Count > 1) {
                    AddonTargetPathText.Text = $"{selectedTargets.Count} target versions selected";
                    AddonTargetPathText.ToolTip = string.Join("\n", selectedTargets.Select(t => $"{t.Name}: {t.DirectoryPath}"));
                } else {
                    AddonTargetPathText.Text = "None selected";
                    AddonTargetPathText.ToolTip = null;
                }
            }

            if (InstallAddonActionButton != null) {
                if (selectedPacks.Count > 1 || selectedTargets.Count > 1) {
                    int packsCount = selectedPacks.Count;
                    int targetsCount = selectedTargets.Count;
                    InstallAddonActionButton.Content = $"📥 INSTALL ({packsCount}) TO ({targetsCount}) VERSION{(targetsCount > 1 ? "S" : "")}";
                } else {
                    InstallAddonActionButton.Content = "📥 INSTALL TO SELECTED VERSION";
                }
            }

            if (AddonCompatibilityBadge == null || AddonCompatibilityText == null) return;

            if (selectedPacks.Count == 0 || selectedTargets.Count == 0) {
                AddonCompatibilityBadge.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x2A));
                AddonCompatibilityBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x30, 0x42));
                AddonCompatibilityText.Text = "Select pack(s) and target version(s) to inspect compatibility";
                AddonCompatibilityText.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4));
                return;
            }

            var warnings = new List<string>();
            foreach (var pack in selectedPacks) {
                foreach (var target in selectedTargets) {
                    var (isComp, tVer, mVer) = AddonManagerService.CheckCompatibility(target, pack);
                    if (!isComp) {
                        warnings.Add($"'{pack.DisplayName}' requires v{mVer}+ (target '{target.Name}' is v{tVer})");
                    }
                }
            }

            if (warnings.Count == 0) {
                AddonCompatibilityBadge.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x2E, 0x22));
                AddonCompatibilityBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                AddonCompatibilityText.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                if (selectedPacks.Count == 1 && selectedTargets.Count == 1) {
                    var (isComp, tVer, mVer) = AddonManagerService.CheckCompatibility(selectedTargets[0], selectedPacks[0]);
                    AddonCompatibilityText.Text = mVer == "Unknown"
                        ? $"✔ Compatible: Target is v{tVer} (No minimum engine version specified)"
                        : $"✔ Compatible: Target v{tVer} meets required v{mVer}";
                } else {
                    AddonCompatibilityText.Text = $"✔ All {selectedPacks.Count} selected pack(s) are compatible with {selectedTargets.Count} target version(s)";
                }
            } else {
                AddonCompatibilityBadge.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x25, 0x18));
                AddonCompatibilityBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0x9E, 0x2E));
                AddonCompatibilityText.Foreground = new SolidColorBrush(Color.FromRgb(0xF6, 0xAD, 0x55));
                AddonCompatibilityText.Text = $"⚠ Warning: {warnings.Count} compatibility warning(s) detected. Packs may not load properly.";
            }
        }

        private void AddonToggleExpand_Click(object sender, RoutedEventArgs e) {
            e.Handled = true;
            if (sender is FrameworkElement fe && fe.Tag is DownloadedAddonItem item) {
                item.IsExpanded = !item.IsExpanded;
            }
        }

        private void AddonRevealExplorer_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is DownloadedAddonItem item) {
                if (File.Exists(item.FilePath)) {
                    Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                }
            }
        }

        private void AddonDeleteFile_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is DownloadedAddonItem item) {
                var res = ThemedMessageBox.Show(
                    $"Are you sure you want to delete '{item.DisplayName}' from your downloaded addons?\n\nFile: {item.FileName}",
                    "Delete Addon",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );
                if (res == MessageBoxResult.Yes) {
                    try {
                        if (File.Exists(item.FilePath)) {
                            File.Delete(item.FilePath);
                        }
                        LoadAddonsManagerData();
                    } catch (Exception ex) {
                        ThemedMessageBox.Show($"Failed to delete file: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void InstallAddonActionButton_Click(object sender, RoutedEventArgs e) {
            var selectedPacks = GetEffectiveSelectedAddons();
            if (selectedPacks.Count == 0) {
                ThemedMessageBox.Show("Please check the checkbox of at least one addon, texture pack, or world from the left list.", "No Pack Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedTargets = GetEffectiveSelectedVersions();
            if (selectedTargets.Count == 0) {
                ThemedMessageBox.Show("Please check the checkbox of at least one target Minecraft version from the right list.", "No Target Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Compatibility check across all pairs
            var warnings = new List<string>();
            foreach (var pack in selectedPacks) {
                foreach (var target in selectedTargets) {
                    var (isComp, tVer, mVer) = AddonManagerService.CheckCompatibility(target, pack);
                    if (!isComp) {
                        warnings.Add($"• '{pack.DisplayName}' requires v{mVer}+ (target '{target.Name}' is v{tVer})");
                    }
                }
            }

            if (warnings.Count > 0) {
                string warningSummary = string.Join("\n", warnings.Take(5));
                if (warnings.Count > 5) warningSummary += $"\n...and {warnings.Count - 5} more warnings.";

                var warnRes = ThemedMessageBox.Show(
                    $"Compatibility Warning:\n\n" +
                    $"Some selected packs require a higher Minecraft version than their targets:\n\n" +
                    $"{warningSummary}\n\n" +
                    "They may not load properly or could cause crashes in-game.\n\n" +
                    "Do you want to proceed and install anyway at your own risk?",
                    "Compatibility Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (warnRes != MessageBoxResult.Yes) {
                    return;
                }
            }

            if (InstallAddonActionButton != null) InstallAddonActionButton.IsEnabled = false;
            if (AddonInstallProgressBar != null) {
                AddonInstallProgressBar.Value = 0;
                AddonInstallProgressBar.Visibility = Visibility.Visible;
            }

            int totalOperations = selectedPacks.Count * selectedTargets.Count;
            int opIndex = 0;
            int successful = 0;
            var failureDetails = new List<string>();

            if (AddonInstallFeedbackText != null) {
                AddonInstallFeedbackText.Text = $"Installing 0/{totalOperations}...";
                AddonInstallFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            }

            try {
                foreach (var pack in selectedPacks) {
                    foreach (var target in selectedTargets) {
                        opIndex++;
                        string prefix = totalOperations > 1 ? $"[{opIndex}/{totalOperations}] " : "";

                        var (ok, msg) = await Task.Run(() => AddonManagerService.InstallAddon(pack, target, (curr, tot, fileName) => {
                            double pct = tot > 0 ? ((double)curr / tot) * 100.0 : 0;
                            Dispatcher.InvokeAsync(() => {
                                if (AddonInstallProgressBar != null) {
                                    AddonInstallProgressBar.Value = pct;
                                }
                                if (AddonInstallFeedbackText != null) {
                                    AddonInstallFeedbackText.Text = $"{prefix}Extracting '{pack.DisplayName}': {curr}/{tot} ({pct:F0}%)";
                                    AddonInstallFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                                }
                            });
                        }));

                        if (ok) {
                            successful++;
                        } else {
                            failureDetails.Add($"• '{pack.DisplayName}' to '{target.Name}': {msg}");
                        }
                    }
                }

                if (failureDetails.Count == 0) {
                    string successMsg = totalOperations == 1
                        ? $"Pack '{selectedPacks[0].DisplayName}' installed successfully into '{selectedTargets[0].Name}'!"
                        : $"Successfully installed {selectedPacks.Count} pack(s) into {selectedTargets.Count} version(s)! ({successful}/{totalOperations} operations succeeded)";

                    if (AddonInstallProgressBar != null) {
                        AddonInstallProgressBar.Value = 100;
                    }
                    if (AddonInstallFeedbackText != null) {
                        AddonInstallFeedbackText.Text = $"✔ {successMsg}";
                        AddonInstallFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                    }
                    ThemedMessageBox.Show(successMsg, "Installation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    string failMsg = $"{successful}/{totalOperations} installed successfully.\n\nFailed:\n" + string.Join("\n", failureDetails);
                    if (AddonInstallFeedbackText != null) {
                        AddonInstallFeedbackText.Text = $"⚠ {successful}/{totalOperations} completed, {failureDetails.Count} failed";
                        AddonInstallFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E));
                    }
                    ThemedMessageBox.Show(failMsg, "Batch Installation Result", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            } finally {
                if (InstallAddonActionButton != null) InstallAddonActionButton.IsEnabled = true;
            }
        }
        #endregion
    }
}
