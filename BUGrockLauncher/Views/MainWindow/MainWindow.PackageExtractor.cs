using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using System.Xml.Linq;
using BUGrockLauncher.WPFDataTypes;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Windows.System;
using Version = BUGrockLauncher.WPFDataTypes.Version;

namespace BUGrockLauncher {
    public partial class MainWindow {
        private async void RefreshVersionsOnline_Click(object sender, RoutedEventArgs e) {
            if (_isLoadingVersionList) return;
            try {
                if (InstallationsRefreshVersionsButton != null) {
                    InstallationsRefreshVersionsButton.IsEnabled = false;
                }
                await LoadVersionListAsync();
                ThemedMessageBox.Show(
                    $"Minecraft version catalog has been refreshed from the internet!\n\nTotal versions available: {_versions.Count}",
                    "Versions Refreshed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            } catch (Exception ex) {
                ThemedMessageBox.Show(
                    $"Failed to refresh versions online:\n{ex.Message}",
                    "Refresh Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            } finally {
                if (InstallationsRefreshVersionsButton != null) {
                    InstallationsRefreshVersionsButton.IsEnabled = true;
                }
            }
        }

        private async void ImportButtonClicked(object sender, RoutedEventArgs e) {
            Microsoft.Win32.OpenFileDialog openFileDlg = new Microsoft.Win32.OpenFileDialog();
            openFileDlg.Filter = "XVC and APPX packages (*.msixvc, *.appx)|*.msixvc;*.appx|APPX packages (*.appx)|*.appx|XVC packages (*.msixvc)|*.msixvc|All Files|*.*";
            Nullable<bool> result = openFileDlg.ShowDialog();
            if (result == true) {
                Directory.CreateDirectory(IMPORTED_VERSIONS_PATH);
                string directory = Path.Combine(IMPORTED_VERSIONS_PATH, openFileDlg.SafeFileName);
                if (Directory.Exists(directory)) {
                    var found = false;
                    foreach (var version in _versions) {
                        if (version.IsImported && version.GameDirectory == directory) {
                            if (version.IsStateChanging) {
                                ThemedMessageBox.Show("A version with the same name was already imported, and is currently being modified. Please wait a few moments and try again.", "Error");
                                return;
                            }
                            MessageBoxResult messageBoxResult = ThemedMessageBox.Show("A version with the same name was already imported. Do you want to delete it ?", "Delete Confirmation", System.Windows.MessageBoxButton.YesNo);
                            if (messageBoxResult == MessageBoxResult.Yes) {
                                var uninstallResult = await Remove(version);
                                if (!uninstallResult) {
                                    ThemedMessageBox.Show("Failed to remove existing version. Import aborted.", "Error");
                                    return;
                                }
                                found = true;
                                break;
                            } else {
                                return;
                            }
                        }
                    }
                    if (!found) {
                        ThemedMessageBox.Show("The destination path for importing already exists and doesn't contain a Minecraft installation known to the launcher. To avoid loss of data, importing was aborted. Please remove the files manually.", "Error");
                        return;
                    }
                }

                var extension = Path.GetExtension(openFileDlg.FileName).ToLowerInvariant();
                PackageType packageType;
                if (extension == ".msixvc") {
                    packageType = PackageType.GDK;
                } else if (extension == ".appx") {
                    packageType = PackageType.UWP;
                } else {
                    ThemedMessageBox.Show("Unsupported file extension: " + extension, "Import failure");
                    return;
                }


                var versionEntry = _versions.AddEntry(openFileDlg.SafeFileName, directory, packageType);
                VersionTabs.SelectedItem = ImportedTab;

                bool success = false;

                if (packageType == PackageType.UWP) {
                    success = await ExtractAppx(openFileDlg.FileName, directory, versionEntry);
                } else if (packageType == PackageType.GDK) {
                    success = await ExtractMsixvc(openFileDlg.FileName, directory, versionEntry, isPreview: false);
                } else {
                    Debug.Assert(false);
                }

                if (success) {
                    versionEntry.StateChangeInfo = null;
                    versionEntry.UpdateInstallStatus();
                } else {
                    _versions.Remove(versionEntry);
                }
            }
        }

        private void InstallError(string userMessage, string debug, string fileName, Exception? ex) {
            string exceptionMessage = "none";
            if (ex != null) {
                Trace.WriteLine(debug + ": " + ex.ToString());
                exceptionMessage = ex.Message;
            } else {
                Trace.WriteLine(debug);
            }

            ThemedMessageBox.Show(
                "Failed to import file: " + fileName + "\n\n" +
                userMessage +
                (ex != null ? "\n\nException message: " + exceptionMessage : "") +
                "\n\nCheck the log file if you need more information (File -> Open log file).", "Import failure"
            );
        }

        private async Task<bool> ExtractAppx(string filePath, string directory, Version versionEntry, CancellationToken cancellationToken = default) {
            versionEntry.StateChangeInfo = new VersionStateChangeInfo(VersionState.Extracting);
            try {
                await Task.Run(() => {
                    using (var archive = ZipFile.OpenRead(filePath)) {
                        int totalEntries = archive.Entries.Count;
                        Dispatcher.BeginInvoke(() => {
                            if (versionEntry.StateChangeInfo != null) {
                                versionEntry.StateChangeInfo.MaxProgress = totalEntries;
                            }
                        });

                        int current = 0;
                        string fullTargetDir = Path.GetFullPath(directory);
                        string fullTargetDirWithSep = fullTargetDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                            ? fullTargetDir
                            : fullTargetDir + Path.DirectorySeparatorChar;
                        Directory.CreateDirectory(fullTargetDir);

                        foreach (var entry in archive.Entries) {
                            cancellationToken.ThrowIfCancellationRequested();

                            string destinationPath = Path.GetFullPath(Path.Combine(fullTargetDir, entry.FullName));
                            if (!destinationPath.StartsWith(fullTargetDirWithSep, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(destinationPath, fullTargetDir, StringComparison.OrdinalIgnoreCase)) {
                                continue;
                            }

                            if (string.IsNullOrEmpty(entry.Name)) {
                                Directory.CreateDirectory(destinationPath);
                            } else {
                                string? parentDir = Path.GetDirectoryName(destinationPath);
                                if (!string.IsNullOrEmpty(parentDir)) {
                                    Directory.CreateDirectory(parentDir);
                                }
                                entry.ExtractToFile(destinationPath, overwrite: true);
                            }

                            current++;
                            if (current % 15 == 0 || current == totalEntries) {
                                int progressVal = current;
                                int percent = totalEntries > 0 ? (int)((long)current * 100 / totalEntries) : 0;
                                int remaining = 100 - percent;
                                Dispatcher.BeginInvoke(() => {
                                    if (versionEntry.StateChangeInfo != null) {
                                        versionEntry.StateChangeInfo.Progress = progressVal;
                                    }
                                });
                                UpdateHeroProgress($"Extracting... {percent}% ({remaining}% remaining)", percent, false);
                            }
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    string sigPath = Path.Combine(directory, "AppxSignature.p7x");
                    if (File.Exists(sigPath)) {
                        File.Delete(sigPath);
                    }
                }, cancellationToken);

                AddonManagerService.EnsureDefaultSkinPacksInstalled(directory);
                versionEntry.UpdateInstallStatus();

                return true;
            } catch (OperationCanceledException) {
                Trace.WriteLine("ExtractAppx was cancelled by user.");
                try {
                    if (Directory.Exists(directory)) {
                        Directory.Delete(directory, recursive: true);
                    }
                } catch { }
                return false;
            } catch (InvalidDataException ex) {
                InstallError(
                    "File seems to be corrupted or not an APPX file",
                    "Failed extracting appx",
                    filePath,
                    ex
                );
                return false;
            } finally {
                versionEntry.StateChangeInfo = null;
            }
        }

        private static bool IsValidPeExecutable(string filePath) {
            try {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < 64) return false;
                byte[] buffer = new byte[2];
                if (fs.Read(buffer, 0, 2) != 2) return false;
                return buffer[0] == 0x4D && buffer[1] == 0x5A; // 'M', 'Z'
            } catch {
                return false;
            }
        }

        private async Task<bool> CheckGdkPrerequisitesAndWarn(bool isPreview, CancellationToken cancellationToken = default) {
            if (!UserPrefs.AutoFetchDecryptionKeys) {
                var res = ThemedMessageBox.Show(
                    "Auto-fetch decryption keys is currently disabled in Options.\n\n" +
                    "Modern Minecraft versions (1.21+) require GDK decryption keys to run.\n" +
                    "If these keys are not present on your system, this installation will fail to decrypt and cannot start.\n\n" +
                    "Would you like to enable Auto-fetch and proceed?",
                    "Decryption Keys Required",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning
                );

                if (res == MessageBoxResult.Yes) {
                    UserPrefs.AutoFetchDecryptionKeys = true;
                    if (SettingsAutoKeysToggle != null) {
                        SettingsAutoKeysToggle.IsChecked = true;
                    }
                    RewritePrefs();
                } else if (res == MessageBoxResult.Cancel) {
                    return false;
                }
            }

            if (UserPrefs.AutoFetchDecryptionKeys) {
                await EnsureGDKDecryptionKeys(isPreview, cancellationToken);
            }

            return true;
        }

        private async Task<bool> EnsureGDKDecryptionKeys(bool isPreview, CancellationToken cancellationToken = default) {
            try {
                var family = isPreview ? MinecraftPackageFamilies.MINECRAFT_PREVIEW : MinecraftPackageFamilies.MINECRAFT;
                var pkgManager = new PackageManager();
                foreach (var pkg in pkgManager.FindPackages(family)) {
                    if (!pkg.IsDevelopmentMode) {
                        Trace.WriteLine("Store package detected on system, GDK keys should be available.");
                        return true;
                    }
                }

                Trace.WriteLine("Acquiring GDK decryption keys in background...");
                UpdateHeroProgress("Acquiring decryption keys in background...", 0, isIndeterminate: true);

                string productId = isPreview ? "9P5X4QVLC2XR" : "9NBLGGH4279M";

                try {
                    var psi = new ProcessStartInfo {
                        FileName = "winget.exe",
                        Arguments = $"install --id {productId} --source msstore --accept-package-agreements --accept-source-agreements",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null) {
                        using var reg = cancellationToken.Register(() => {
                            try { proc.Kill(); } catch { }
                        });

                        await proc.WaitForExitAsync(cancellationToken);
                        Trace.WriteLine($"winget key acquisition finished with exit code {proc.ExitCode}");
                        if (proc.ExitCode == 0) {
                            return true;
                        }
                    }
                } catch (Exception ex) {
                    Trace.WriteLine("winget acquisition attempt error: " + ex.Message);
                }

                return false;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Trace.WriteLine("EnsureGDKDecryptionKeys error: " + ex.Message);
                return false;
            }
        }


        private void FixGDKManifest(string path) {
            XDocument doc = XDocument.Load(path);
            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            XNamespace rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

            var apps = doc.Descendants(ns + "Application");
            foreach (var app in apps) {
                var executable = app.Attribute("Executable");
                //install from older versions will reference the minecraft exe directly, so we need to patch these
                if (executable != null && (executable.Value == "GameLaunchHelper.exe" || executable.Value == "Minecraft.Windows.exe")) {
                    executable.Value = GDK_SHIM_NAME;
                }
            }

            var extensions = doc.Root.Elements(ns + "Extensions").ToList();
            foreach (var ext in extensions) {
                ext.Remove();
            }

            var capabilities = doc.Descendants(ns + "Capabilities");
            var customInstall = capabilities
                .Elements(rescap + "Capability")
                .Where(c => c.Attribute("Name")?.Value == "customInstallActions")
                .ToList();
            foreach (var cap in customInstall) {
                cap.Remove();
            }

            var settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false); //no BOM
            settings.Indent = true;
            using (XmlWriter w = XmlWriter.Create(path, settings)) {
                doc.Save(w);
            }
        }


        private void RecursiveCopyDirectory(string from, string to, HashSet<string> skip) {
            Directory.CreateDirectory(to);
            foreach (var source in Directory.EnumerateFiles(from)) {
                if (skip.Contains(source)) {
                    continue;
                }
                string destination = Path.Combine(to, Path.GetFileName(source));
                Trace.WriteLine(source + " -> " + destination);
                File.Copy(source, destination, true);
            }
            foreach (var source in Directory.EnumerateDirectories(from)) {
                string destination = Path.Combine(to, Path.GetFileName(source));
                RecursiveCopyDirectory(source, destination, skip);
            }
        }

        private static string SafeReadAllText(string path) {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            for (int retry = 0; retry < 10; retry++) {
                try {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs)) {
                        return sr.ReadToEnd();
                    }
                } catch {
                    System.Threading.Thread.Sleep(100);
                }
            }
            return "";
        }

        private async Task<bool> ExtractMsixvc(string filePath, string directory, Version versionEntry, bool isPreview, CancellationToken cancellationToken = default) {
            if (_hasGdkExtractTask) {
                InstallError(
                    "Can't install multiple MSIXVC packages at the same time. Please wait for the current installation to finish before starting a new one.",
                    "Concurrent MSIXVC installation attempt",
                    filePath,
                    null
                );
                return false;
            }
            _hasGdkExtractTask = true;
            try {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await CheckGdkPrerequisitesAndWarn(isPreview, cancellationToken)) {
                    return false;
                }
                directory = Path.GetFullPath(directory);
                // XVC are encrypted containers, I don't currently know of any way to extract them to an arbitrary directory
                // For now we just stage the package in XboxGames, and then move the files to the launcher data directory

                versionEntry.StateChangeInfo = new VersionStateChangeInfo(VersionState.Staging) {
                    MaxProgress = 100,
                    Progress = 0
                };

                var packageManager = new PackageManager();

                //make sure XboxGames is cleared
                Trace.WriteLine("Clearing existing XboxGames Minecraft installation");
                try {
                    await UnregisterPackage(versionEntry.GamePackageFamily, versionEntry, skipBackup: false);
                } catch (Exception ex) {
                    InstallError(
                        "The existing XboxGames Minecraft installation could not be removed. Please make sure Minecraft is not running and try again.",
                        "Failed clearing XboxGames Minecraft installation",
                        filePath,
                        ex
                    );
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                try {
                    await DeploymentProgressWrapper(packageManager.StagePackageAsync(new Uri(filePath), null), versionEntry, cancellationToken);
                } catch (OperationCanceledException) {
                    Trace.WriteLine("StagePackageAsync cancelled by user.");
                    try {
                        await UnregisterPackage(versionEntry.GamePackageFamily, versionEntry, skipBackup: true);
                    } catch { }
                    return false;
                } catch (Exception ex) {
                    InstallError(
                        "Failed to stage package.\n" +
                            "This may mean that the file is damaged, not an MSIXVC file. Please check the integrity of the file.\n\n" +
                            "However, this error might also happen if you've never installed a GDK version of Minecraft from the Store before,\n" +
                            "as the launcher relies on the Store to install the keys needed to decrypt the installation package.\n" +
                            "Please ensure that you've installed " + (isPreview ? "Minecraft Preview" : "Minecraft") + " from the Store before installing GDK versions using the launcher.",
                        "Failed staging MSIXVC",
                        filePath,
                        ex
                    );
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                string installPath = "";
                foreach (var pkg in new PackageManager().FindPackages(versionEntry.GamePackageFamily)) {
                    if (installPath != "") {
                        InstallError(
                            "Minecraft is installed in multiple places, and the launcher doesn't know where to copy files from.\n" +
                            "This is probably because another user has the game installed.",
                            "Multiple locations found for staged MSIXVC: " + installPath + ", " + pkg.InstalledLocation.Path,
                            filePath,
                            null
                        );
                        return false;
                    }
                    installPath = pkg.InstalledLocation.Path;
                }
                Trace.WriteLine("Detected staging path: " + installPath);
                string resolvedPath = LinkResolver.Resolve(installPath);
                Trace.WriteLine("Symlink resolved as " + resolvedPath);
                installPath = resolvedPath;

                var exeSrcPath = Path.Combine(installPath, "Minecraft.Windows.exe");
                if (!Directory.Exists(installPath)) {
                    InstallError(
                        "Didn't find installation expected at " + installPath + "\nMaybe your XboxGames folder is in a different location?",
                        "Expected XboxGames Minecraft directory not found" + installPath,
                        filePath,
                        null
                    );
                    return false;
                }
                if (!File.Exists(exeSrcPath)) {
                    InstallError(
                        "Didn't find Minecraft executable at " + exeSrcPath,
                        "Expected XboxGames Minecraft executable not found: " + exeSrcPath,
                        filePath,
                        null
                    );
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                versionEntry.StateChangeInfo.VersionState = VersionState.Decrypting;
                UpdateHeroProgress("Decrypting Minecraft.Windows.exe...", 100, isIndeterminate: true);

                var exeTmpDir = Path.GetTempPath();
                var uuid = Guid.NewGuid().ToString();
                //Use a different tmp path to make sure we don't copy half-done files
                //UUID makes sure we don't copy the leftovers of a different, failed installation
                var exeTmpPath = Path.Combine(exeTmpDir, "Minecraft.Windows_" + uuid + ".exe");
                var donePath = exeTmpPath + ".done";

                var exeDstPath = Path.Combine(Path.GetFullPath(directory), "Minecraft.Windows.exe");
                var decryptHelperLogFile = Path.GetTempFileName();

                try {
                    var helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GDK_DECRYPT_HELPER_NAME);

                    static string EscapePsSingleQuoted(string s) => s.Replace("'", "''");
                    var innerArgs = $"\"{EscapePsSingleQuoted(exeSrcPath)}\" \"{EscapePsSingleQuoted(exeTmpPath)}\" \"{EscapePsSingleQuoted(decryptHelperLogFile)}\" \"{EscapePsSingleQuoted(donePath)}\"";

                    // Use Base64 EncodedCommand to prevent any quoting or space issues in paths
                    var psScript = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{EscapePsSingleQuoted(versionEntry.GamePackageFamily)}' -App Game -Command '{EscapePsSingleQuoted(helperPath)}' -Args '{innerArgs}'";
                    var encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
                    Trace.WriteLine("Decrypt command (raw): " + psScript);

                    var processInfo = new ProcessStartInfo {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    };

                    Trace.WriteLine("Copying decrypted exe");
                    try {
                        using var process = Process.Start(processInfo);
                        if (process != null) {
                            using var reg = cancellationToken.Register(() => {
                                try { process.Kill(); } catch { }
                            });
                            await process.WaitForExitAsync(cancellationToken);
                            Trace.WriteLine("Process output:" + (await process.StandardOutput.ReadToEndAsync()));
                            Trace.WriteLine("Process errors:" + (await process.StandardError.ReadToEndAsync()));
                        }
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        Trace.WriteLine("Decrypt helper log output: " + SafeReadAllText(decryptHelperLogFile));
                        InstallError(
                            "Failed to run PowerShell to copy the Minecraft executable out of the staged package",
                            "Failed running PowerShell for exe extraction",
                            filePath,
                            ex
                        );
                        return false;
                    }

                    for (int i = 0; i < 300 && !File.Exists(donePath); i++) {
                        cancellationToken.ThrowIfCancellationRequested();
                        //Give it up to 30 seconds to copy the file
                        //We can't block on the outcome of Invoke-CommandInDesktopPackage, so we have to poll for the file
                        await Task.Delay(100, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    Trace.WriteLine("Decrypt helper log output: " + SafeReadAllText(decryptHelperLogFile));

                    if (!File.Exists(exeTmpPath)) {
                        Trace.WriteLine("Src path: " + exeSrcPath);
                        Trace.WriteLine("Tmp path: " + exeTmpPath);
                        InstallError(
                            "The Minecraft executable could not be copied out of the staged package.\n" +
                                "This is usually due to the game license not being installed for your Windows user account.\n\n" +
                                "Please ensure that you've installed " + (isPreview ? "Minecraft Preview" : "Minecraft") + " from the Store before using this launcher.",
                            "PowerShell subprocess didn't seem to copy the exe in time",
                            filePath,
                            null
                        );
                        return false;
                    }

                    if (!IsValidPeExecutable(exeTmpPath)) {
                        Trace.WriteLine("Decrypted exe failed PE header check: still encrypted ciphertext.");
                        var storePrompt = ThemedMessageBox.Show(
                            "Minecraft.Windows.exe is encrypted and requires an active game license from Microsoft Store.\n\n" +
                            "Since your account already owns or has access to Minecraft, clicking 'Yes' will open the Store to register the decryption keys on this PC.\n\n" +
                            "Would you like to open Microsoft Store now to activate it?",
                            "Decryption Keys Required",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning
                        );
                        if (storePrompt == MessageBoxResult.Yes) {
                            try {
                                string storeUri = isPreview ? "ms-windows-store://pdp/?ProductId=9P5X4QVLC2XR" : "ms-windows-store://pdp/?ProductId=9NBLGGH4279M";
                                Process.Start(new ProcessStartInfo {
                                    FileName = storeUri,
                                    UseShellExecute = true
                                });
                            } catch { }
                        }
                        return false;
                    }

                    Trace.WriteLine("Minecraft executable decrypted successfully");

                    cancellationToken.ThrowIfCancellationRequested();

                    versionEntry.StateChangeInfo.VersionState = VersionState.Moving;
                    UpdateHeroProgress("Finalizing installation files...", 100, isIndeterminate: true);


                    try {
                        Trace.WriteLine("Moving staged files: " + installPath + " -> " + directory);
                        if (Path.GetPathRoot(installPath) == Path.GetPathRoot(directory)) {
                            Trace.WriteLine("Destination for extraction is on the same drive as the installation location - moving files for speed");
                            Directory.Move(installPath, directory);
                        } else {
                            Trace.WriteLine("Destination for extraction is on a different drive than staged - copying files");
                            //Minecraft.Windows.exe can't be copied directly due to permissions
                            HashSet<string> skip = new HashSet<string>();
                            skip.Add(exeSrcPath);
                            RecursiveCopyDirectory(installPath, directory, skip);
                        }


                        Trace.WriteLine("Moving decrypted exe into place");
                        if (File.Exists(exeDstPath)) {
                            File.Delete(exeDstPath);
                        }
                        if (File.Exists(exeTmpPath)) {
                            File.Move(exeTmpPath, exeDstPath);
                        }
                    } catch (Exception ex) {
                        InstallError(
                            "Failed copying/moving game files to the destination folder",
                            "Failed moving game files to destination",
                            filePath,
                            ex
                        );
                        return false;
                    }
                } finally {
                    try { if (File.Exists(exeTmpPath)) File.Delete(exeTmpPath); } catch { }
                    try { if (File.Exists(donePath)) File.Delete(donePath); } catch { }
                    try { if (File.Exists(decryptHelperLogFile)) File.Delete(decryptHelperLogFile); } catch { }
                }

                Trace.WriteLine("Cleaning up XboxGames");
                //we already created a backup earlier, so a new attempt would just get in the way
                await UnregisterPackage(versionEntry.GamePackageFamily, versionEntry, skipBackup: true);

                AddonManagerService.EnsureDefaultSkinPacksInstalled(directory);

                Trace.WriteLine("Done importing msixvc: " + filePath);
                return true;

            } catch (OperationCanceledException) {
                Trace.WriteLine("ExtractMsixvc cancelled by user.");
                try {
                    await UnregisterPackage(versionEntry.GamePackageFamily, versionEntry, skipBackup: true);
                } catch { }
                try {
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                } catch { }
                return false;
            } finally {
                _hasGdkExtractTask = false;
            }
        }
    }
}
