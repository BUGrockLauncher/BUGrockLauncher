using System;
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
            UserPrefs.DeleteAppxAfterDownload = DeleteAppxAfterDownloadOption.IsChecked;
            RewritePrefs();
        }

        private void AutoFetchDecryptionKeysCheck_Changed(object sender, RoutedEventArgs e) {
            UserPrefs.AutoFetchDecryptionKeys = AutoFetchDecryptionKeysOption.IsChecked;
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

    }
}
