using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BUGrockLauncher.WPFDataTypes;
using BUGrockLauncher.Services;
using BUGrockLauncher.Views.Dialogs;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Core;
using Windows.Management.Deployment;
using Windows.Storage;
using Windows.System;
using Version = BUGrockLauncher.WPFDataTypes.Version;

namespace BUGrockLauncher {
    public partial class MainWindow {
        public ICommand LaunchCommand => new RelayCommand((v) => InvokeLaunch((Version)v));

        public ICommand RemoveCommand => new RelayCommand((v) => InvokeRemove((Version)v));

        public ICommand DownloadCommand => new RelayCommand((v) => InvokeDownload((Version)v));

        public ICommand RenameCommand => new RelayCommand((v) => InvokeRename((Version)v));

        public ICommand DuplicateCommand => new RelayCommand((v) => InvokeDuplicate((Version)v));

        public ICommand UpgradeCommand => new RelayCommand((v) => InvokeUpgrade((Version)v));

        private bool _externalSwitchWarningConfirmed = false;

        private void InvokeLaunch(Version v) {
            if (_hasLaunchTask)
                return;
            _hasLaunchTask = true;
            Task.Run(async () => {
                try {
                    // Check if switching away from external Minecraft without prior confirmation
                    if (!v.IsExternal) {
                        var currentActive = GetCurrentlyActiveVersion();
                        if (currentActive != null && currentActive.IsExternal && currentActive != v) {
                            if (_externalSwitchWarningConfirmed) {
                                _externalSwitchWarningConfirmed = false;
                            } else {
                                bool confirmed = false;
                                Dispatcher.Invoke(() => {
                                    var res = ThemedMessageBox.Show(
                                        $"Warning: An externally installed Minecraft ({currentActive.Name}) was detected.\n\n" +
                                        "Installing or switching to this version will replace your external Minecraft installation. " +
                                        "You will NOT be able to restore it unless you have its .appx package or re-download it from the Microsoft Store.\n\n" +
                                        "Do you want to proceed?",
                                        "External Minecraft Detected",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Warning
                                    );
                                    confirmed = (res == MessageBoxResult.Yes);
                                });

                                if (!confirmed) {
                                    _hasLaunchTask = false;
                                    HideHeroProgress();
                                    return;
                                }
                            }
                        }
                    }

                    // External Minecraft launch: direct launch without data move or re-registering
                    if (v.IsExternal) {
                        UpdateHeroProgress("Launching Minecraft...", 100, isIndeterminate: true);
                        bool launched = false;
                        try {
                            var pkgList = await AppDiagnosticInfo.RequestInfoForPackageAsync(v.GamePackageFamily);
                            if (pkgList.Count > 0) {
                                var res = await pkgList[0].LaunchAsync();
                                if (res.ExtendedError == null) {
                                    launched = true;
                                }
                            }
                        } catch (Exception ex) {
                            Trace.WriteLine("External launch via AppDiagnosticInfo failed: " + ex);
                        }

                        if (!launched) {
                            try {
                                string uri = v.VersionType == VersionType.Preview ? "minecraft-preview://" : "minecraft://";
                                Process.Start(new ProcessStartInfo {
                                    FileName = uri,
                                    UseShellExecute = true
                                });
                                launched = true;
                            } catch (Exception ex) {
                                Trace.WriteLine("External protocol launch failed: " + ex);
                            }
                        }

                        await Task.Delay(1500);
                        HideHeroProgress();
                        _hasLaunchTask = false;
                        return;
                    }

                    if (v.IsInstance && !string.IsNullOrEmpty(v.InstanceId)) {
                        var inst = InstanceManager.Instance.GetInstanceById(v.InstanceId);
                        if (inst != null) {
                            Trace.WriteLine("Applying data isolation for instance: " + inst.Name);
                            InstanceManager.Instance.ApplyDataIsolation(inst, v.GamePackageFamily);
                        }
                    }

                    v.StateChangeInfo = new VersionStateChangeInfo(VersionState.MovingData);
                    UpdateHeroProgress("Restoring Minecraft worlds...", 100, isIndeterminate: true);
                    if (!MoveMinecraftData(v.GamePackageFamily, v.PackageType)) {
                        Trace.WriteLine("Data restore error, aborting launch");
                        v.StateChangeInfo = null;
                        _hasLaunchTask = false;
                        HideHeroProgress();
                        return;
                    }
                    v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Registering);
                    UpdateHeroProgress("Registering package...", 100, isIndeterminate: true);
                    string gameDir = Path.GetFullPath(v.GameDirectory);
                    AddonManagerService.EnsureDefaultSkinPacksInstalled(gameDir);
                    try {
                        await ReRegisterPackage(v.GamePackageFamily, gameDir, v);
                    } catch (Exception e) {
                        Trace.WriteLine("App re-register failed:\n" + e.ToString());
                        ThemedMessageBox.Show("App re-register failed:\n" + e.ToString());
                        _hasLaunchTask = false;
                        v.StateChangeInfo = null;
                        HideHeroProgress();
                        return;
                    }
                    v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Launching);
                    UpdateHeroProgress("Launching Minecraft...", 100, isIndeterminate: true);

                    if (v.PackageType == PackageType.GDK) {
                        string exePath = Path.Combine(gameDir, "Minecraft.Windows.exe");
                        if (!IsValidPeExecutable(exePath)) {
                            Trace.WriteLine("Refusing to launch encrypted/corrupted executable: " + exePath);
                            ThemedMessageBox.Show(
                                "Minecraft cannot be launched because Minecraft.Windows.exe is still encrypted.\n\n" +
                                "Please delete this installation and reinstall it with 'Auto-fetch decryption keys' enabled in Options.",
                                "Cannot Launch Minecraft",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                            _hasLaunchTask = false;
                            v.StateChangeInfo = null;
                            HideHeroProgress();
                            return;
                        }
                    }

                    try {
                        var pkg = await AppDiagnosticInfo.RequestInfoForPackageAsync(v.GamePackageFamily);
                        if (pkg.Count > 0) {
                            if (pkg.Count > 1) {
                                Trace.WriteLine("Multiple packages found ???");
                            }
                            var result = await pkg[0].LaunchAsync();
                            if (result.ExtendedError != null) {
                                Trace.WriteLine("LaunchAsync didn't throw, but returned an extended error???");
                                throw result.ExtendedError;
                            }
                        } else {
                            throw new Exception("No packages found for package family " + v.GamePackageFamily);
                        }
                        Trace.WriteLine("App launch finished!");
                    } catch (Exception e) {
                        Trace.WriteLine("App launch failed:\n" + e.ToString());
                        ThemedMessageBox.Show("App launch failed:\n" + e.ToString());
                        return;
                    } finally {
                        _hasLaunchTask = false;
                        v.StateChangeInfo = null;
                        HideHeroProgress();
                    }
                } catch {
                    _hasLaunchTask = false;
                    v.StateChangeInfo = null;
                    HideHeroProgress();
                }
            });
        }

        private async Task DeploymentProgressWrapper(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> t, Version? version, CancellationToken cancellationToken = default) {
            TaskCompletionSource<int> src = new TaskCompletionSource<int>();
            using var reg = cancellationToken.Register(() => {
                try {
                    t.Cancel();
                    src.TrySetCanceled(cancellationToken);
                } catch { }
            });

            t.Progress += (v, p) => {
                Trace.WriteLine("Deployment progress: " + p.state + " " + p.percentage + "%");
                int pct = (int)p.percentage;
                int remaining = 100 - pct;
                UpdateHeroProgress($"Staging package... {pct}% ({remaining}% remaining)", pct, false);

                if (version?.StateChangeInfo != null) {
                    Dispatcher.BeginInvoke(() => {
                        if (version.StateChangeInfo != null) {
                            version.StateChangeInfo.MaxProgress = 100;
                            version.StateChangeInfo.Progress = (long)p.percentage;
                        }
                    });
                }
            };
            t.Completed += (v, p) => {
                if (p == AsyncStatus.Canceled) {
                    src.TrySetCanceled(cancellationToken);
                } else if (p == AsyncStatus.Error) {
                    Trace.WriteLine("Deployment failed: " + v.GetResults().ErrorText + " (error code " + v.GetResults().ExtendedErrorCode.HResult + ")");
                    src.TrySetException(new Exception("Deployment failed: " + v.GetResults().ErrorText));
                } else {
                    Trace.WriteLine("Deployment done: " + p);
                    src.TrySetResult(1);
                }
            };
            await src.Task;
        }

        private int GetWorldCountInDataDir(string dataDir) {
            var worldsFolder = Path.Combine(dataDir, "games", "com.mojang", "minecraftWorlds");
            if (!Directory.Exists(worldsFolder)) {
                return 0;
            }
            return Directory.GetDirectories(worldsFolder).Length;
        }

        private Dictionary<string, int> LocateMinecraftWorlds(string packageFamily) {
            List<string> candidates = new List<string>();

            var uwpDataDir = GetMinecraftUWPDataDir(packageFamily);
            if (uwpDataDir != "") {
                candidates.Add(uwpDataDir);
            }

            candidates.AddRange(GetMinecraftGDKDataDirs(packageFamily));
            candidates.Add(GetBackupMinecraftDataDir());

            var worldLocations = new Dictionary<string, int>();
            foreach(var dataDir in candidates) {
                Trace.WriteLine("Checking for worlds in: " + dataDir);
                var worldsFolder = Path.Combine(dataDir, "games", "com.mojang", "minecraftWorlds");
                if (!Directory.Exists(worldsFolder)) {
                    Trace.WriteLine("No worlds found in: " + worldsFolder);
                    continue;
                }
                int worlds = Directory.GetDirectories(worldsFolder).Length;
                if (worlds > 0) {
                    worldLocations[dataDir] = worlds;
                    Trace.WriteLine("Found " + worlds + " worlds in: " + worldsFolder);
                } else {
                    Trace.WriteLine("No worlds found in: " + worldsFolder);
                }
            }

            return worldLocations;
        }

        private string GetBackupMinecraftDataDir() {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string tmpDir = Path.Combine(localAppData, "TmpMinecraftLocalState");
            return tmpDir;
        }

        private string GetMinecraftUWPRootDir(string packageFamily) {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                packageFamily
            );
        }

        private string GetMinecraftUWPDataDir(string packageFamily) {
            return Path.Combine(GetMinecraftUWPRootDir(packageFamily), "LocalState");
        }

        private string GetMinecraftGDKRootDir(string packageFamily) {
            string infix;
            switch(packageFamily) {
                case MinecraftPackageFamilies.MINECRAFT:
                    infix = "Minecraft Bedrock";
                    break;
                case MinecraftPackageFamilies.MINECRAFT_PREVIEW:
                    infix = "Minecraft Bedrock Preview";
                    break;
                default: throw new ArgumentException("Invalid Minecraft package family: " + packageFamily);
            }
            var gdkRootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                infix
            );
            return gdkRootDir;
        }

        private List<string> GetMinecraftGDKDataDirs(string packageFamily) {
            var parentDir = Path.Combine(
                GetMinecraftGDKRootDir(packageFamily),
                "Users"
            );
            var results = new List<string>();

            if (!Directory.Exists(parentDir)) {
                Trace.WriteLine("GDK Users directory doesn't exist: " + parentDir);
                return results;
            }

            results.AddRange(Directory.EnumerateDirectories(parentDir));

            return results;
        }

        private bool BackupMinecraftDataForRemoval(string packageFamily) {
            ApplicationData data;
            try {
                data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            }catch (FileNotFoundException e) {
                Trace.WriteLine("BackupMinecraftDataForRemoval: Application data not found for package family " + packageFamily + ": " + e.ToString());
                Trace.WriteLine("This should mean the package isn't installed, so we don't need to backup the data");
                return true;
            }
            if (!Directory.Exists(data.LocalFolder.Path)) {
                //this is fine only for GDK versions
                Trace.WriteLine("LocalState folder " + data.LocalFolder.Path + " doesn't exist, so it can't be backed up");
                return true;
            }
            string tmpDir = GetBackupMinecraftDataDir();
            if (Directory.Exists(tmpDir)) {
                if (GetWorldCountInDataDir(tmpDir) > 0) {
                    Trace.WriteLine("BackupMinecraftDataForRemoval: " + tmpDir + " already exists with worlds");
                    var res = ThemedMessageBox.Show(
                        "A previous backup of your Minecraft worlds was found in temporary storage.\n\n" +
                        "Would you like to preserve these worlds and continue the installation?\n\n" +
                        "Click 'Yes' to safely keep your worlds and proceed, or 'No' to cancel and inspect them in File Explorer.",
                        "Saved Worlds Backup Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );
                    if (res == MessageBoxResult.Yes) {
                        return true;
                    } else {
                        try { Process.Start("explorer.exe", tmpDir); } catch { }
                        return false;
                    }
                }
                Directory.Delete(tmpDir, recursive: true);
            }
            Trace.WriteLine("Moving Minecraft data to: " + tmpDir);
            Directory.Move(data.LocalFolder.Path, tmpDir);

            return true;
        }

        private void RestoreMove(string from, string to) {
            foreach (var f in Directory.EnumerateFiles(from)) {
                string ft = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(ft)) {
                    if (ThemedMessageBox.Show("The file " + ft + " already exists in the destination.\nDo you want to replace it? The old file will be lost otherwise.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    File.Delete(ft);
                }
                File.Move(f, ft);
            }
            foreach (var f in Directory.EnumerateDirectories(from)) {
                string tp = Path.Combine(to, Path.GetFileName(f));
                if (!Directory.Exists(tp)) {
                    if (File.Exists(tp) && ThemedMessageBox.Show("The file " + tp + " is not a directory. Do you want to remove it? The data from the old directory will be lost otherwise.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    Directory.CreateDirectory(tp);
                }
                RestoreMove(f, tp);
            }
        }

        private bool RestoreUWPData(string src, string uwpDataDir, string uwpParent) {
            Trace.WriteLine("Restoring Minecraft data from src dir " + src + " to " + uwpDataDir);
            try {
                if (Directory.Exists(uwpDataDir)) {
                    Trace.WriteLine("Deleting: " + uwpDataDir);
                    Directory.Delete(uwpDataDir, recursive: true);
                }
                if (!Directory.Exists(uwpParent)) {
                    Trace.WriteLine("Creating parent dir: " + uwpParent);
                    Directory.CreateDirectory(uwpParent);
                }
                Trace.WriteLine("Restoring files");
                RestoreMove(src, uwpDataDir);
                Trace.WriteLine("Deleting src dir: " + src);
                Directory.Delete(src, true);
                Trace.WriteLine("Restore complete");
                return true;
            } catch (Exception e) {
                Trace.WriteLine("Failed restoring Minecraft data from " + src + ": " + e.ToString());
                ThemedMessageBox.Show("Failed to move Minecraft data from:\n"
                    + src
                    + "\nto:\n"
                    + uwpDataDir
                    + "\n\nCheck the log file for more information.", "Data restore error"
                );
                return false;
            }
        }

        private bool MoveMinecraftData(string packageFamily, PackageType destinationType) {
            var dataLocations = LocateMinecraftWorlds(packageFamily);
            if (dataLocations.Count == 0) {
                Trace.WriteLine("No Minecraft data found to restore or link");
                return true;
            }

            string gdkRoot = GetMinecraftGDKRootDir(packageFamily);
            string uwpDataDir = GetMinecraftUWPDataDir(packageFamily);

            if (dataLocations.Count > 1) {
                var messageString = "";
                foreach (var loc in dataLocations) {
                    messageString += $"\n - {loc.Key}: {loc.Value} worlds";
                }

                //GDK might put worlds in several places, but this is fine as long as the target version is also GDK
                if (destinationType == PackageType.GDK) {
                    Trace.WriteLine("Checking for dirs with prefix: " + gdkRoot);
                    bool gdkOnly = true;
                    foreach (var loc in dataLocations) {
                        if (!loc.Key.StartsWith(gdkRoot)) {
                            gdkOnly = false;
                            Trace.WriteLine("Folder " + loc.Key + " doesn't start with " + gdkRoot);
                            break;
                        } else {
                            Trace.WriteLine("Folder " + loc.Key + " is in GDK data, ignoring");
                        }
                    }

                    if (gdkOnly) {
                        Trace.WriteLine("Worlds found in multiple places, but all of them are GDK:" + messageString);
                        Trace.WriteLine("This is fine since the target version is also GDK, doing nothing");
                        return true;
                    }
                }

                Trace.WriteLine("Can't automatically restore Minecraft data - multiple locations with worlds found:" + messageString);

                string destinationFolder = destinationType == PackageType.UWP ? uwpDataDir : Path.Combine(gdkRoot, "Users");
                
                bool proceed = Dispatcher.Invoke(() => {
                    var dialog = new WorldConflictDialog(dataLocations, destinationFolder);
                    dialog.Owner = this;
                    return dialog.ShowDialog() == true;
                });
                return proceed;
            }

            string dataLocation = dataLocations.Keys.First();

            string tmpDir = GetBackupMinecraftDataDir();

            string uwpParent = GetMinecraftUWPRootDir(packageFamily);
            if (dataLocation == tmpDir) {
                //we don't know where GDK will want to store this due to the user folder names containing some kind of UID
                //so we restore to UWP location and let Minecraft handle the GDK migration by itself
                Trace.WriteLine("Restoring Minecraft data from backup dir " + tmpDir + " to " + uwpDataDir);
                if (!RestoreUWPData(tmpDir, uwpDataDir, uwpParent)) {
                    return false;
                }
                dataLocation = uwpDataDir;
            }

            if (destinationType == PackageType.GDK && dataLocation == uwpDataDir) {
                Trace.WriteLine("Deleting uwpMigration.dat, so GDK Minecraft will migrate data from UWP next time it's used");
                var uwpMigrationDat = Path.Combine(
                    GetMinecraftGDKRootDir(packageFamily),
                    "games",
                    "com.mojang",
                    "uwpMigration.dat"
                );
                Trace.WriteLine("uwpMigration.dat path: " + uwpMigrationDat);
                try {
                    File.Delete(uwpMigrationDat);
                    return true;
                } catch (Exception e) {
                    Trace.WriteLine("Failed deleting uwpMigration.dat: " + e.ToString());
                    ThemedMessageBox.Show(
                        "Failed deleting uwpMigration.dat file.\n" +
                        "Your worlds will be visible to UWP versions, but GDK versions won't see them unless you move them back manually.\n\n" +
                        "Please delete the following file manually: " + uwpMigrationDat +
                        "\n\nAlternatively, you can copy your worlds back to the GDK folder next time you run a GDK version." +
                        "\nYour worlds are currently located at: " + uwpDataDir +
                        "Data migration notice"
                    );
                    return false;
                }
            } else if (destinationType == PackageType.UWP && dataLocation != uwpDataDir) {
                //this should mean that we found data in a GDK location, so move it for UWP
                var gdkDataDir = dataLocations.Keys.First();
                if (!RestoreUWPData(gdkDataDir, uwpDataDir, uwpParent)) {
                    return false;
                }

                return true;
            } else {
                Trace.WriteLine("Minecraft data already in the right place " + dataLocation);
                return true;
            }
        }

        private async Task RemovePackage(Package pkg, string packageFamily, Version? version, bool skipBackup) {
            Trace.WriteLine("Removing package: " + pkg.Id.FullName);
            if (!pkg.IsDevelopmentMode) {
                if (!skipBackup) {
                    if (!BackupMinecraftDataForRemoval(packageFamily)) {
                        throw new Exception("Failed backing up Minecraft data before uninstalling package");
                    }
                }
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, RemovalOptions.RemoveForAllUsers), version);
            } else {
                Trace.WriteLine("Package is in development mode");
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, RemovalOptions.PreserveApplicationData | RemovalOptions.RemoveForAllUsers), version);
            }
            Trace.WriteLine("Removal of package done: " + pkg.Id.FullName);
        }

        private string GetPackagePath(Package pkg) {
            try {
                return pkg.InstalledLocation.Path;
            } catch (FileNotFoundException) {
                return "";
            }
        }

        private async Task UnregisterPackage(string packageFamily, Version? version, bool skipBackup) {
            foreach (var pkg in new PackageManager().FindPackages(packageFamily)) {
                string location = GetPackagePath(pkg);
                Trace.WriteLine("Removing package: " + pkg.Id.FullName + " " + location);
                await RemovePackage(pkg, packageFamily, version, skipBackup);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes
        );

        private async Task ReRegisterPackage(string packageFamily, string gameDir, Version version) {
            Trace.WriteLine("Registering package");
            string manifestPath = Path.Combine(gameDir, "AppxManifest.xml");

            bool updateManifest = false;

            if (version.PackageType == PackageType.GDK) {
                string shimPath = Path.Combine(gameDir, GDK_SHIM_NAME);

                string backupManifestPath = Path.Combine(gameDir, "AppxManifest_original.xml");
                bool hasManifestBackup = File.Exists(backupManifestPath);

                if (!File.Exists(shimPath)) {
                    updateManifest = true;
                    if (hasManifestBackup) {
                        //we need to redo the manifest for older versions that didn't have the shim present
                        File.Copy(backupManifestPath, manifestPath, overwrite: true);
                        Trace.WriteLine("Manifest needs re-patching because GDK launch shim has been added to an old install");
                    } else {
                        Trace.WriteLine("Adding launch shim to new GDK install");
                    }
                } else {
                    Trace.WriteLine("Updating GDK launch shim");
                }

                string shimSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GDK_SHIM_NAME);
                File.Delete(shimPath);
                if (CreateHardLink(shimPath, shimSourcePath, IntPtr.Zero)) {
                    //hardlink is way less annoying for development, in theory
                    Trace.WriteLine("Successfully hardlinked GDK launch shim");
                } else {
                    File.Copy(shimSourcePath, shimPath, overwrite: true);
                    Trace.WriteLine("Couldn't create hard link for GDK shim, copying instead");
                }

                //avoid patching the manifest unless necessary, the user might have edited it
                if (updateManifest) {
                    Trace.WriteLine("Patching AppxManifest.xml");
                    if (!hasManifestBackup) {
                        Trace.WriteLine("Backing up original manifest");
                        File.Copy(manifestPath, backupManifestPath);
                    }
                    FixGDKManifest(manifestPath);
                }
            }

            foreach (var pkg in new PackageManager().FindPackages(packageFamily)) {
                string location = GetPackagePath(pkg);
                if (location == gameDir && !updateManifest) {
                    Trace.WriteLine("Skipping package removal - same path: " + pkg.Id.FullName + " " + location);
                    return;
                }
                await RemovePackage(pkg, packageFamily, version, skipBackup: false);
            }

            Trace.WriteLine("Manifest path: " + manifestPath);
            await DeploymentProgressWrapper(new PackageManager().RegisterPackageAsync(new Uri(manifestPath), null, DeploymentOptions.DevelopmentMode), version);
            Trace.WriteLine("App re-register done!");
        }

        private void InvokeDownload(Version v) {
            string suggestedName = !string.IsNullOrEmpty(v.CustomInstanceName) ? v.CustomInstanceName : v.Name;
            var dialog = new NewInstanceDialog(v.DisplayName, v.Name, suggestedName) {
                Owner = this
            };
            if (dialog.ShowDialog() != true) {
                return;
            }

            string chosenName = dialog.InstanceName;
            string chosenDir = dialog.SelectedDirectory;
            string safeCleanName = InstanceManager.SanitizeInstanceName(chosenName);

            CancellationTokenSource cancelSource = new CancellationTokenSource();
            v.IsNew = false;
            v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Initializing);
            v.StateChangeInfo.CancelCommand = new RelayCommand((o) => cancelSource.Cancel());

            Trace.WriteLine("Download start for instance: " + chosenName);
            Task.Run(async () => {
                string dlExt = (v.PackageType == PackageType.UWP ? ".Appx" : ".msixvc");
                string parentDir = Path.GetDirectoryName(chosenDir) ?? InstanceManager.Instance.InstancesDirectory;
                string dlPath = Path.Combine(parentDir, safeCleanName + dlExt);

                VersionDownloader downloader = _anonVersionDownloader;
                if (v.VersionType == VersionType.Beta) {
                    downloader = _userVersionDownloader;
                    if (Interlocked.CompareExchange(ref _userVersionDownloaderLoginTaskStarted, 1, 0) == 0) {
                        _userVersionDownloaderLoginTask.Start();
                    }
                    Trace.WriteLine("Waiting for authentication");
                    try {
                        await _userVersionDownloaderLoginTask;
                        Trace.WriteLine("Authentication complete");
                    } catch (WUTokenHelper.WUTokenException e) {
                        Trace.WriteLine("Authentication failed:\n" + e.ToString());
                        ThemedMessageBox.Show("Failed to authenticate because: " + e.Message, "Authentication failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    } catch (Exception e) {
                        Trace.WriteLine("Authentication failed:\n" + e.ToString());
                        ThemedMessageBox.Show(e.ToString(), "Authentication failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                VersionDownloader.DownloadProgress dlProgressHandler = (current, total) => {
                    var info = v.StateChangeInfo;
                    if (info == null) return;
                    if (info.VersionState != VersionState.Downloading) {
                        Trace.WriteLine("Actual download started");
                        info.VersionState = VersionState.Downloading;
                        if (total.HasValue && total.Value > 0)
                            info.MaxProgress = total.Value;
                    }
                    if (info.MaxProgress == 0 && total.HasValue && total.Value > 0) {
                        info.MaxProgress = total.Value;
                    }
                    info.Progress = current;
                };

                try {
                    if (v.PackageType == PackageType.UWP) {
                        await downloader.DownloadAppx(v.UUID, "1", dlPath, dlProgressHandler, cancelSource.Token);
                    } else if (v.PackageType == PackageType.GDK) {
                        await downloader.DownloadMsixvc(v.DownloadURLs, dlPath, dlProgressHandler, cancelSource.Token);
                    } else {
                        throw new Exception("Unknown package type");
                    }
                    Trace.WriteLine("Download complete");

                    string dirPath = chosenDir;
                    if (Directory.Exists(dirPath))
                        Directory.Delete(dirPath, true);
                    bool extractSuccess = false;
                    if (v.PackageType == PackageType.UWP) {
                        extractSuccess = await ExtractAppx(dlPath, dirPath, v, cancelSource.Token);
                    } else if (v.PackageType == PackageType.GDK) {
                        extractSuccess = await ExtractMsixvc(dlPath, dirPath, v, isPreview: v.VersionType == VersionType.Preview, cancellationToken: cancelSource.Token);
                    } else {
                        throw new Exception("Unknown package type");
                    }
                    if (extractSuccess && UserPrefs.DeleteAppxAfterDownload) {
                        Trace.WriteLine("Deleting package to reduce disk usage");
                        try { if (File.Exists(dlPath)) File.Delete(dlPath); } catch { }
                    } else {
                        Trace.WriteLine("Preserving package beside instance directory: " + dlPath);
                    }

                    if (extractSuccess) {
                        var inst = InstanceManager.Instance.CreateInstance(
                            chosenName,
                            v.Name,
                            v.PackageType,
                            v.VersionType,
                            dirPath,
                            File.Exists(dlPath) ? dlPath : null
                        );

                        Dispatcher.BeginInvoke(() => {
                            var instanceVer = new Version(v.UUID, v.Name, v.VersionType, false, this, v.PackageType, v.DownloadURLs) {
                                InstanceId = inst.Id,
                                CustomInstanceName = inst.Name,
                                GameDirectory = inst.GameDirectory,
                                DataDirectory = inst.DataDirectory,
                                RenameCommand = RenameCommand,
                                DuplicateCommand = DuplicateCommand,
                                UpgradeCommand = UpgradeCommand
                            };
                            instanceVer.CheckCanUpgrade(_versions);
                            _versions.Add(instanceVer);
                            RefreshLists();
                            UpdateQuickPlayStatus();
                        });
                    }
                } catch (BadUpdateIdentityException) {
                    Trace.WriteLine("Download failed due to failure to fetch download URL");
                    string msg = (v.VersionType == VersionType.Beta)
                        ? "Unable to fetch download URL for beta version.\n\nPlease make sure your account is subscribed to the Minecraft beta programme in the Xbox Insider Hub app."
                        : $"Unable to download {v.DisplayName} ({v.Name}).\n\nMicrosoft has removed the download files for this legacy version from Windows Update servers.\nYou can still install and play it by obtaining the .Appx file and clicking '+ Import .appx'.";
                    ThemedMessageBox.Show(msg, "Version Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                } catch (OperationCanceledException) {
                    Trace.WriteLine("Download was cancelled by user.");
                } catch (Exception e) {
                    Trace.WriteLine("Download or extraction failed:\n" + e.ToString());
                    ThemedMessageBox.Show("Operation failed:\n" + e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                } finally {
                    v.StateChangeInfo = null;
                    v.UpdateInstallStatus();
                }
            });
        }

        private void InvokeRename(Version v) {
            string currentName = v.CustomInstanceName ?? v.Name;
            var dialog = new RenameInstanceDialog(currentName) { Owner = this };
            if (dialog.ShowDialog() == true) {
                string newName = dialog.NewName;
                if (string.Equals(currentName, newName, StringComparison.OrdinalIgnoreCase)) return;

                if (string.IsNullOrEmpty(v.InstanceId)) {
                    var info = InstanceManager.Instance.CreateInstance(newName, v.Name, v.PackageType, v.VersionType, v.GameDirectory);
                    v.InstanceId = info.Id;
                    v.CustomInstanceName = info.Name;
                    v.DataDirectory = info.DataDirectory;
                } else {
                    InstanceManager.Instance.RenameInstance(v.InstanceId, newName);
                    v.CustomInstanceName = newName;
                }
                v.UpdateInstallStatus();
                UpdateQuickPlayStatus();
                RefreshLists();
            }
        }

        private async void InvokeDuplicate(Version v) {
            string currentName = v.CustomInstanceName ?? v.Name;
            var dialog = new DuplicateInstanceDialog(currentName, v.Name) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            string newName = dialog.NewInstanceName;
            if (string.IsNullOrEmpty(v.InstanceId)) {
                var info = InstanceManager.Instance.CreateInstance(currentName, v.Name, v.PackageType, v.VersionType, v.GameDirectory);
                v.InstanceId = info.Id;
                v.CustomInstanceName = info.Name;
                v.DataDirectory = info.DataDirectory;
            }

            ShowHeroProgress("CLONING", $"Duplicating \"{currentName}\" to \"{newName}\"...");
            try {
                var newInst = await InstanceManager.Instance.DuplicateInstanceAsync(v.InstanceId, newName, (status, pct) => {
                    UpdateHeroProgress(status, pct, false);
                });

                if (newInst != null) {
                    var clonedVer = new Version(v.UUID, newInst.VersionNumber, newInst.VersionType, false, this, newInst.PackageType, v.DownloadURLs) {
                        InstanceId = newInst.Id,
                        CustomInstanceName = newInst.Name,
                        GameDirectory = newInst.GameDirectory,
                        DataDirectory = newInst.DataDirectory,
                        RenameCommand = RenameCommand,
                        DuplicateCommand = DuplicateCommand,
                        UpgradeCommand = UpgradeCommand
                    };
                    clonedVer.CheckCanUpgrade(_versions);
                    _versions.Add(clonedVer);
                    RefreshLists();
                    UpdateQuickPlayStatus();
                    ThemedMessageBox.Show($"Instance \"{newName}\" created successfully!", "Instance Cloned", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    ThemedMessageBox.Show("Failed to duplicate instance.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            } catch (Exception ex) {
                ThemedMessageBox.Show("Error duplicating instance: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                HideHeroProgress();
            }
        }

        private void InvokeUpgrade(Version v) {
            string currentName = v.CustomInstanceName ?? v.Name;
            string targetVersionName = v.UpgradeTargetVersion ?? "";
            if (string.IsNullOrEmpty(targetVersionName)) {
                var latest = _versions
                    .Where(cand => !cand.IsInstance && cand.VersionType == v.VersionType && cand.PackageType == v.PackageType)
                    .OrderByDescending(cand => cand.Name)
                    .FirstOrDefault();
                if (latest != null) targetVersionName = latest.Name;
            }

            if (string.IsNullOrEmpty(targetVersionName)) {
                ThemedMessageBox.Show("No newer upgrade version found for this instance.", "No Upgrade Available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targetVer = _versions.FirstOrDefault(cand => cand.Name == targetVersionName && cand.PackageType == v.PackageType);
            if (targetVer == null) {
                ThemedMessageBox.Show($"Target version {targetVersionName} was not found in the online repository.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new UpgradeInstanceDialog(currentName, v.Name, targetVersionName) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            bool doBackup = dialog.CreateBackup;

            if (string.IsNullOrEmpty(v.InstanceId)) {
                var info = InstanceManager.Instance.CreateInstance(currentName, v.Name, v.PackageType, v.VersionType, v.GameDirectory);
                v.InstanceId = info.Id;
                v.CustomInstanceName = info.Name;
                v.DataDirectory = info.DataDirectory;
            }

            CancellationTokenSource cancelSource = new CancellationTokenSource();
            v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Initializing);
            v.StateChangeInfo.CancelCommand = new RelayCommand((o) => cancelSource.Cancel());

            ShowHeroProgress("UPGRADING", $"Upgrading {currentName} to {targetVersionName}...");

            Task.Run(async () => {
                try {
                    if (doBackup) {
                        v.StateChangeInfo = new VersionStateChangeInfo(VersionState.MovingData);
                        UpdateHeroProgress("Creating instance backup (.zip)...", 100, isIndeterminate: true);
                        string zipPath = await InstanceManager.Instance.CreateBackupZipAsync(v.InstanceId!, v.Name);
                        Trace.WriteLine("Upgrade backup created at: " + zipPath);
                    }

                    v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Downloading);
                    UpdateHeroProgress($"Downloading {targetVersionName}...", 0, isIndeterminate: false);

                    string safeInstanceName = InstanceManager.SanitizeInstanceName(v.CustomInstanceName ?? v.Name);
                    string ext = targetVer.PackageType == PackageType.UWP ? ".appx" : ".msixvc";
                    string dlPath = Path.Combine(InstanceManager.Instance.InstancesDirectory, $"{safeInstanceName}_{targetVersionName}{ext}");

                    var downloader = targetVer.VersionType == VersionType.Beta ? _userVersionDownloader : _anonVersionDownloader;
                    VersionDownloader.DownloadProgress dlProgress = (cur, tot) => {
                        if (tot.HasValue && tot.Value > 0) {
                            int pct = (int)((double)cur / tot.Value * 100);
                            UpdateHeroProgress($"Downloading {targetVersionName}... {pct}%", pct, false);
                            if (v.StateChangeInfo != null) {
                                v.StateChangeInfo.MaxProgress = tot.Value;
                                v.StateChangeInfo.Progress = cur;
                            }
                        }
                    };

                    if (targetVer.PackageType == PackageType.UWP) {
                        await downloader.DownloadAppx(targetVer.UUID, "1", dlPath, dlProgress, cancelSource.Token);
                    } else {
                        await downloader.DownloadMsixvc(targetVer.DownloadURLs, dlPath, dlProgress, cancelSource.Token);
                    }

                    v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Extracting);
                    UpdateHeroProgress("Upgrading game binaries (preserving data)...", 100, isIndeterminate: true);

                    string localDataDir = Path.Combine(v.GameDirectory, "data");
                    string tempBackupData = Path.Combine(Path.GetTempPath(), "BUGrock_Upgrade_Data_" + Guid.NewGuid().ToString("N"));
                    bool hadData = Directory.Exists(localDataDir);
                    if (hadData) {
                        Directory.Move(localDataDir, tempBackupData);
                    }

                    bool extractSuccess = false;
                    if (targetVer.PackageType == PackageType.UWP) {
                        extractSuccess = await ExtractAppx(dlPath, v.GameDirectory, v, cancelSource.Token);
                    } else {
                        extractSuccess = await ExtractMsixvc(dlPath, v.GameDirectory, v, isPreview: targetVer.VersionType == VersionType.Preview, cancellationToken: cancelSource.Token);
                    }

                    if (hadData) {
                        if (Directory.Exists(localDataDir)) Directory.Delete(localDataDir, true);
                        Directory.Move(tempBackupData, localDataDir);
                    }

                    if (extractSuccess) {
                        if (UserPrefs.DeleteAppxAfterDownload) {
                            try { if (File.Exists(dlPath)) File.Delete(dlPath); } catch { }
                        }

                        var inst = InstanceManager.Instance.GetInstanceById(v.InstanceId!);
                        if (inst != null) {
                            inst.VersionNumber = targetVersionName;
                            InstanceManager.Instance.SaveInstances();
                        }

                        v.Name = targetVersionName;
                        v.CheckCanUpgrade(_versions);
                        v.UpdateInstallStatus();

                        Dispatcher.BeginInvoke(() => {
                            RefreshLists();
                            UpdateQuickPlayStatus();
                            ThemedMessageBox.Show($"Instance \"{currentName}\" upgraded successfully to v{targetVersionName}!", "Upgrade Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    } else {
                        ThemedMessageBox.Show("Upgrade extraction failed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                } catch (OperationCanceledException) {
                    Trace.WriteLine("Upgrade cancelled by user.");
                } catch (Exception ex) {
                    Trace.WriteLine("Upgrade error: " + ex);
                    ThemedMessageBox.Show("Upgrade failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                } finally {
                    v.StateChangeInfo = null;
                    HideHeroProgress();
                }
            });
        }

        private async Task<bool> Remove(Version v) {
            try {
                if (v.IsExternal) {
                    ThemedMessageBox.Show(
                        "This version was installed outside the launcher (Microsoft Store / Windows).\n\n" +
                        "To uninstall it, please use Windows Settings or the Microsoft Store.",
                        "External Minecraft Installation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return false;
                }

                if (v.IsInstance) {
                    bool confirmed = false;
                    Dispatcher.Invoke(() => {
                        var res = ThemedMessageBox.Show(
                            $"Are you sure you want to uninstall and remove instance \"{v.DisplayName}\"?\n\nAll game files and worlds in this instance will be permanently deleted.",
                            "Confirm Delete Instance",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning
                        );
                        confirmed = (res == MessageBoxResult.Yes);
                    });
                    if (!confirmed) return false;

                    InstanceManager.Instance.DeleteInstance(v.InstanceId!, true);
                    Dispatcher.Invoke(() => {
                        _versions.Remove(v);
                        RefreshLists();
                        UpdateQuickPlayStatus();
                    });
                    return true;
                }

                if (UserPrefs?.CustomInstallDirectories != null && UserPrefs.CustomInstallDirectories.ContainsKey(v.Name)) {
                    UserPrefs.CustomInstallDirectories.Remove(v.Name);
                    RewritePrefs();
                }

                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Unregistering);
                Trace.WriteLine("Unregistering version " + v.DisplayName);
                try {
                    int unregistered = 0;
                    foreach (var pkg in new PackageManager().FindPackages(v.GamePackageFamily)) {
                        string location = GetPackagePath(pkg);
                        if (location == "" || Path.GetFullPath(location) == Path.GetFullPath(v.GameDirectory)) {
                            Trace.WriteLine("Removing package: " + pkg.Id.FullName + " " + location);
                            await RemovePackage(pkg, v.GamePackageFamily, v, skipBackup: false);
                            unregistered++;
                        }
                    }
                    if (unregistered == 0) {
                        Trace.WriteLine($"Looks like {v.GameDirectory} is not registered with the system, no unregistering performed");
                    }
                } catch (Exception e) {
                    Trace.WriteLine("Failed unregistering package:\n" + e.ToString());
                    ThemedMessageBox.Show("Failed unregistering package:\n" + e.ToString(), "Uninstall error");
                    return false;
                }
                Trace.WriteLine("Cleaning up game files for version " + v.DisplayName);
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.CleaningUp);
                try {
                    // Use the \\?\ prefix to support long paths
                    Directory.Delete(@"\\?\" + Path.GetFullPath(v.GameDirectory), true);
                } catch (Exception e) {
                    Trace.WriteLine("Failed deleting game directory:\n" + e.ToString());
                    ThemedMessageBox.Show("Failed deleting game directory:\n" + e.ToString(), "Uninstall error");
                    return false;
                }

                if (v.IsImported) {
                    Dispatcher.Invoke(() => _versions.Remove(v));
                    Trace.WriteLine("Removed imported version " + v.DisplayName);
                } else {
                    v.GameDirectory = (v.VersionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + v.Name;
                    v.UpdateInstallStatus();
                    Trace.WriteLine("Removed release version " + v.DisplayName);
                }

                return true;
            } finally { 
                v.StateChangeInfo = null;
            }
        }

        private void InvokeRemove(Version v) {
            Task.Run(async () => await Remove(v));
        }

    }
}
