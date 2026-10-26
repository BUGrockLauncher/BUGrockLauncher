using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using BUGrockLauncher.WPFDataTypes;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Version = BUGrockLauncher.WPFDataTypes.Version;

namespace BUGrockLauncher {
    public partial class MainWindow {
        private Version? _selectedQuickPlayVersion = null;

        public class LocalVersionCandidate {
            public string Name { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string? PackageFilePath { get; set; }
            public string? InstalledDirectory { get; set; }
            public PackageType PackageType { get; set; }
            public VersionType VersionType { get; set; }
            public Version? VersionEntry { get; set; }
            public bool IsInstalled => !string.IsNullOrEmpty(InstalledDirectory) && Directory.Exists(InstalledDirectory);
            public bool IsActive { get; set; }
            public bool IsExternal { get; set; }
            public string? InstanceId { get; set; }
        }

        private string GetPackageVersionName(Package pkg, string location) {
            try {
                string resPacks = Path.Combine(location, "data", "resource_packs");
                if (Directory.Exists(resPacks)) {
                    var vanillaDirs = Directory.GetDirectories(resPacks, "vanilla_*");
                    string? bestVer = null;
                    foreach (var d in vanillaDirs) {
                        string name = Path.GetFileName(d);
                        var match = Regex.Match(name, @"^vanilla_(\d+(?:\.\d+)+)$");
                        if (match.Success) {
                            string ver = match.Groups[1].Value;
                            if (bestVer == null || CompareVersionStrings(ver, bestVer) > 0) {
                                bestVer = ver;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(bestVer)) {
                        return bestVer;
                    }
                }
            } catch {
            }

            try {
                var v = pkg.Id.Version;
                if (v.Revision > 0) {
                    return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                }
                return $"{v.Major}.{v.Minor}.{v.Build}";
            } catch {
                return "1.0.0";
            }
        }

        private Version? GetOrCreateExternalVersion(Package pkg, string family) {
            try {
                string loc = GetPackagePath(pkg);
                if (string.IsNullOrEmpty(loc) || !Directory.Exists(loc)) return null;

                var match = _versions.FirstOrDefault(v => !v.IsExternal && string.Equals(Path.GetFullPath(v.GameDirectory), Path.GetFullPath(loc), StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (Path.GetFullPath(loc).StartsWith(Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase)) {
                    return null;
                }

                string verName = GetPackageVersionName(pkg, loc);
                var verType = family == MinecraftPackageFamilies.MINECRAFT_PREVIEW ? VersionType.Preview : VersionType.Release;

                var existingExt = _versions.FirstOrDefault(v => v.IsExternal && v.Name == verName && v.VersionType == verType);
                if (existingExt != null) {
                    existingExt.GameDirectory = loc;
                    return existingExt;
                }

                var extVer = new Version(verName, loc, this, PackageType.UWP) {
                    VersionType = verType,
                    IsExternal = true
                };

                if (Dispatcher.CheckAccess()) {
                    _versions.Add(extVer);
                } else {
                    Dispatcher.Invoke(() => _versions.Add(extVer));
                }

                return extVer;
            } catch (Exception ex) {
                Trace.WriteLine("GetOrCreateExternalVersion error: " + ex);
                return null;
            }
        }

        private Version? GetCurrentlyActiveVersion() {
            if (_selectedQuickPlayVersion != null && _selectedQuickPlayVersion.IsInstalled) {
                return _selectedQuickPlayVersion;
            }

            try {
                foreach (var family in new[] { MinecraftPackageFamilies.MINECRAFT, MinecraftPackageFamilies.MINECRAFT_PREVIEW }) {
                    foreach (var pkg in new PackageManager().FindPackages(family)) {
                        string loc = GetPackagePath(pkg);
                        if (!string.IsNullOrEmpty(loc)) {
                            var match = _versions.FirstOrDefault(v => !v.IsExternal && string.Equals(Path.GetFullPath(v.GameDirectory), Path.GetFullPath(loc), StringComparison.OrdinalIgnoreCase));
                            if (match != null) {
                                return match;
                            }

                            var ext = GetOrCreateExternalVersion(pkg, family);
                            if (ext != null) {
                                return ext;
                            }
                        }
                    }
                }
            } catch {
            }

            return _versions.FirstOrDefault(v => v.IsInstalled);
        }

        private CancellationTokenSource? _heroCancelSource = null;
        private bool _isHeroOperationRunning = false;

        private void ShowHeroProgress(string action, string initialStatus) {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.BeginInvoke(() => ShowHeroProgress(action, initialStatus));
                return;
            }
            _isHeroOperationRunning = true;
            if (HeroStatusAction != null) HeroStatusAction.Text = action;
            if (HeroStatusBadge != null) {
                if (action == "DOWNGRADING") {
                    HeroStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x25, 0x18));
                    HeroStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0x9E, 0x2E));
                    if (HeroStatusAction != null) HeroStatusAction.Foreground = new SolidColorBrush(Color.FromRgb(0xF6, 0xAD, 0x55));
                } else {
                    HeroStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x2E, 0x22));
                    HeroStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                    if (HeroStatusAction != null) HeroStatusAction.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                }
            }
            if (HeroStatusDetail != null) HeroStatusDetail.Text = initialStatus;
            if (HeroProgressBar != null) {
                HeroProgressBar.Value = 0;
                HeroProgressBar.IsIndeterminate = false;
            }
            if (HeroCancelButton != null) HeroCancelButton.IsEnabled = true;
            if (HeroProgressContainer != null) HeroProgressContainer.Visibility = Visibility.Visible;
            if (QuickPlayButton != null) QuickPlayButton.IsEnabled = false;
        }

        private void UpdateHeroProgress(string statusDetail, double percent, bool isIndeterminate = false) {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.BeginInvoke(() => UpdateHeroProgress(statusDetail, percent, isIndeterminate));
                return;
            }
            if (!_isHeroOperationRunning) return;
            if (HeroStatusDetail != null) HeroStatusDetail.Text = statusDetail;
            if (HeroProgressBar != null) {
                HeroProgressBar.IsIndeterminate = isIndeterminate;
                if (!isIndeterminate) {
                    HeroProgressBar.Value = Math.Clamp(percent, 0, 100);
                }
            }
        }

        private void HideHeroProgress() {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.BeginInvoke(HideHeroProgress);
                return;
            }
            _isHeroOperationRunning = false;
            if (_heroCancelSource != null) {
                try { _heroCancelSource.Dispose(); } catch { }
                _heroCancelSource = null;
            }
            if (HeroProgressContainer != null) HeroProgressContainer.Visibility = Visibility.Collapsed;
            if (QuickPlayButton != null) QuickPlayButton.IsEnabled = true;
        }

        private void HeroCancelButton_Click(object sender, RoutedEventArgs e) {
            if (_heroCancelSource != null && !_heroCancelSource.IsCancellationRequested) {
                try {
                    if (HeroStatusDetail != null) HeroStatusDetail.Text = "Cancelling...";
                    if (HeroCancelButton != null) HeroCancelButton.IsEnabled = false;
                    _heroCancelSource.Cancel();
                } catch (Exception ex) {
                    Trace.WriteLine("Hero cancel error: " + ex);
                }
            }
        }

        private void QuickPlayButton_Click(object sender, RoutedEventArgs e) {
            if (_isHeroOperationRunning) return;
            var active = GetCurrentlyActiveVersion();
            if (active != null) {
                ShowHeroProgress("LAUNCHING", "Preparing launch...");
                InvokeLaunch(active);
            } else {
                ThemedMessageBox.Show("No version is currently installed. Please select a version in the Installations tab or choose a package from the version selector to install it.", "BUGrock Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                TabNavInstallations.IsChecked = true;
            }
        }

        private void UpdateQuickPlayStatus() {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.InvokeAsync(UpdateQuickPlayStatus);
                return;
            }
            if (QuickPlayVersionLabel == null) return;
            var current = GetCurrentlyActiveVersion();
            if (current != null) {
                if (current.IsExternal) {
                    QuickPlayVersionLabel.Text = $"{current.Name} (External)";
                    QuickPlayVersionLabel.ToolTip = $"External Minecraft detected: {current.Name}";
                } else {
                    string verTypeStr = current.VersionType == VersionType.Preview ? "Preview" : "Release";
                    if (!string.IsNullOrEmpty(current.CustomInstanceName)) {
                        QuickPlayVersionLabel.Text = $"{current.CustomInstanceName} ({current.Name})";
                        QuickPlayVersionLabel.ToolTip = $"{current.CustomInstanceName} • v{current.Name} ({verTypeStr})";
                    } else {
                        QuickPlayVersionLabel.Text = $"{current.Name} {verTypeStr}";
                        QuickPlayVersionLabel.ToolTip = $"{current.Name} ({verTypeStr})";
                    }
                }
            } else {
                QuickPlayVersionLabel.Text = "Select Version";
                QuickPlayVersionLabel.ToolTip = null;
            }
        }

        private static string ExtractVersionNameFromFileName(string fileName) {
            var match = Regex.Match(fileName, @"(?:Minecraft-(?:Preview-)?|Minecraft\.Windows-)?([0-9]+(?:\.[0-9]+)+)");
            if (match.Success) {
                return match.Groups[1].Value;
            }
            string noExt = Path.GetFileNameWithoutExtension(fileName);
            noExt = Regex.Replace(noExt, @"^(Minecraft-Preview-|Minecraft-|Minecraft\.Windows-)", "", RegexOptions.IgnoreCase);
            return noExt;
        }

        private int CompareVersionStrings(string v1, string? v2) {
            if (string.IsNullOrEmpty(v2)) return 1;
            if (string.IsNullOrEmpty(v1)) return -1;
            var matches1 = Regex.Matches(v1, @"\d+");
            var matches2 = Regex.Matches(v2, @"\d+");
            int count = Math.Max(matches1.Count, matches2.Count);
            for (int i = 0; i < count; i++) {
                int val1 = i < matches1.Count ? int.Parse(matches1[i].Value) : 0;
                int val2 = i < matches2.Count ? int.Parse(matches2[i].Value) : 0;
                if (val1 != val2) return val1.CompareTo(val2);
            }
            return 0;
        }

        private Version GetOrCreateVersionEntry(LocalVersionCandidate candidate) {
            if (candidate.VersionEntry != null) return candidate.VersionEntry;

            var existing = _versions.FirstOrDefault(v => v.Name == candidate.Name && v.PackageType == candidate.PackageType);
            if (existing != null) {
                candidate.VersionEntry = existing;
                return existing;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetDir = candidate.InstalledDirectory ?? Path.Combine(baseDir, (candidate.VersionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + candidate.Name);
            var newVer = new WPFDataTypes.Version(candidate.Name, targetDir, this, candidate.PackageType) {
                VersionType = candidate.VersionType
            };
            _versions.Add(newVer);
            candidate.VersionEntry = newVer;
            return newVer;
        }

        private List<LocalVersionCandidate> ScanLocalVersions() {
            var candidates = new Dictionary<string, LocalVersionCandidate>(StringComparer.OrdinalIgnoreCase);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string curDir = Directory.GetCurrentDirectory();
            var dirsToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseDir, curDir };

            var currentActive = GetCurrentlyActiveVersion();

            // 0. Include external version if active
            if (currentActive != null && currentActive.IsExternal) {
                string extKey = $"ext_{currentActive.Name}_{currentActive.VersionType}";
                candidates[extKey] = new LocalVersionCandidate {
                    Name = currentActive.Name,
                    DisplayName = $"{currentActive.Name} (External)",
                    InstalledDirectory = currentActive.GameDirectory,
                    PackageType = PackageType.UWP,
                    VersionType = currentActive.VersionType,
                    VersionEntry = currentActive,
                    IsExternal = true,
                    IsActive = true
                };
            }

            // 1. Scan for package files (*.appx, *.msixvc)
            foreach (var dir in dirsToScan) {
                if (!Directory.Exists(dir)) continue;
                try {
                    var files = Directory.EnumerateFiles(dir)
                        .Where(f => f.EndsWith(".appx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".msixvc", StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files) {
                        string fileName = Path.GetFileName(file);
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        var pkgType = ext == ".msixvc" ? PackageType.GDK : PackageType.UWP;
                        var verType = fileName.Contains("Preview", StringComparison.OrdinalIgnoreCase) ? VersionType.Preview : VersionType.Release;

                        string verName = ExtractVersionNameFromFileName(fileName);
                        if (string.IsNullOrEmpty(verName)) continue;

                        string candKey = $"pkg_{verName}_{pkgType}_{verType}";

                        string expectedDir = Path.Combine(dir, (verType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + verName);
                        if (!Directory.Exists(expectedDir)) {
                            string altDir = Path.Combine(baseDir, (verType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + verName);
                            if (Directory.Exists(altDir)) expectedDir = altDir;
                        }

                        var candidate = new LocalVersionCandidate {
                            Name = verName,
                            DisplayName = $"{verName} ({(verType == VersionType.Preview ? "Preview" : "Release")})",
                            PackageFilePath = file,
                            InstalledDirectory = Directory.Exists(expectedDir) ? expectedDir : null,
                            PackageType = pkgType,
                            VersionType = verType
                        };

                        var match = _versions.FirstOrDefault(v => v.Name == verName && v.PackageType == pkgType);
                        if (match != null) {
                            candidate.VersionEntry = match;
                            if (match.IsInstalled && string.IsNullOrEmpty(candidate.InstalledDirectory)) {
                                candidate.InstalledDirectory = match.GameDirectory;
                            }
                        }

                        candidates[candKey] = candidate;
                    }
                } catch (Exception ex) {
                    Trace.WriteLine("Error scanning packages: " + ex.Message);
                }
            }

            // 2. Scan installed versions and instances from _versions
            foreach (var v in _versions) {
                if (v.IsInstalled) {
                    string key = !string.IsNullOrEmpty(v.InstanceId) 
                        ? $"inst_{v.InstanceId}" 
                        : (v.IsExternal ? $"ext_{v.Name}_{v.VersionType}" : $"pkg_{v.Name}_{v.PackageType}_{v.VersionType}");

                    if (candidates.TryGetValue(key, out var existing)) {
                        existing.VersionEntry = v;
                        existing.IsExternal = v.IsExternal;
                        if (!string.IsNullOrEmpty(v.GameDirectory)) existing.InstalledDirectory = v.GameDirectory;
                        continue;
                    }

                    string disp = !string.IsNullOrEmpty(v.CustomInstanceName)
                        ? $"{v.CustomInstanceName} (v{v.Name} - {v.PackageType})"
                        : (v.IsExternal ? $"{v.Name} (External)" : $"{v.Name} ({(v.VersionType == VersionType.Preview ? "Preview" : "Release")}) [{v.PackageType}]");

                    candidates[key] = new LocalVersionCandidate {
                        Name = v.Name,
                        DisplayName = disp,
                        InstalledDirectory = v.GameDirectory,
                        PackageType = v.PackageType,
                        VersionType = v.VersionType,
                        VersionEntry = v,
                        IsExternal = v.IsExternal,
                        InstanceId = v.InstanceId
                    };
                }
            }

            // 3. Mark current active
            if (currentActive != null) {
                foreach (var cand in candidates.Values) {
                    if (!string.IsNullOrEmpty(currentActive.InstanceId) && cand.InstanceId == currentActive.InstanceId) {
                        cand.IsActive = true;
                    } else if (string.IsNullOrEmpty(currentActive.InstanceId) && string.IsNullOrEmpty(cand.InstanceId) &&
                               string.Equals(cand.Name, currentActive.Name, StringComparison.OrdinalIgnoreCase) &&
                               cand.PackageType == currentActive.PackageType &&
                               cand.IsExternal == currentActive.IsExternal) {
                        cand.IsActive = true;
                    }
                }
            }

            return candidates.Values
                .GroupBy(c => new { c.Name, c.IsExternal, c.PackageType, c.VersionType, c.InstanceId, c.InstalledDirectory })
                .Select(g => g.First())
                .OrderByDescending(c => c.IsActive)
                .ThenByDescending(c => c.Name, Comparer<string>.Create((a, b) => CompareVersionStrings(a, b)))
                .ToList();
        }

        private bool _wasPopupOpenOnMouseDown = false;

        private void SelectedVersionBox_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left) {
                _wasPopupOpenOnMouseDown = (VersionSelectorPopup != null && VersionSelectorPopup.IsOpen);
            }
        }

        private void SelectedVersionBox_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;

            if (_isHeroOperationRunning) return;

            if (_wasPopupOpenOnMouseDown) {
                if (VersionSelectorPopup != null) VersionSelectorPopup.IsOpen = false;
                return;
            }

            PopulateVersionDropdown();
            if (VersionSelectorPopup != null) VersionSelectorPopup.IsOpen = true;
        }

        private void PopulateVersionDropdown() {
            VersionSelectorItemsPanel.Children.Clear();

            var header = new TextBlock {
                Text = "AVAILABLE LOCAL VERSIONS",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(6, 4, 6, 8)
            };
            VersionSelectorItemsPanel.Children.Add(header);

            var candidates = ScanLocalVersions();
            if (candidates.Count == 0) {
                VersionSelectorItemsPanel.Children.Add(new TextBlock {
                    Text = "No local .appx or .msixvc packages found in launcher directory.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4)),
                    FontSize = 11,
                    Margin = new Thickness(6, 12, 6, 12),
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            foreach (var candidate in candidates) {
                bool isCurrent = candidate.IsActive;

                var itemBorder = new Border {
                    Background = isCurrent ? new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x24)) : new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x2A)),
                    BorderBrush = isCurrent ? new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69)) : new SolidColorBrush(Color.FromRgb(0x2D, 0x30, 0x42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 2, 0, 2),
                    Cursor = Cursors.Hand
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new Image {
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                try {
                    icon.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/bugrock_block.png"));
                } catch {
                }
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var titleText = new TextBlock {
                    Text = candidate.Name,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13
                };
                var subText = new TextBlock {
                    Text = candidate.IsExternal
                        ? $"{(candidate.VersionType == VersionType.Preview ? "Preview" : "Release")} • External (System)"
                        : $"{(candidate.VersionType == VersionType.Preview ? "Preview" : "Release")} • {candidate.PackageType}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4)),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                textPanel.Children.Add(titleText);
                textPanel.Children.Add(subText);
                Grid.SetColumn(textPanel, 1);
                grid.Children.Add(textPanel);

                string badgeText;
                Color badgeBg;
                Color badgeFg;
                Color badgeBorder;

                if (isCurrent) {
                    badgeText = "CURRENT";
                    badgeBg = Color.FromRgb(0x1C, 0x2E, 0x22);
                    badgeFg = Color.FromRgb(0x38, 0xA1, 0x69);
                    badgeBorder = Color.FromRgb(0x38, 0xA1, 0x69);
                } else if (candidate.IsExternal) {
                    badgeText = "EXTERNAL";
                    badgeBg = Color.FromRgb(0x1A, 0x27, 0x36);
                    badgeFg = Color.FromRgb(0x38, 0xBD, 0xF8);
                    badgeBorder = Color.FromRgb(0x02, 0x84, 0xC7);
                } else if (candidate.IsInstalled) {
                    badgeText = "READY";
                    badgeBg = Color.FromRgb(0x1E, 0x24, 0x33);
                    badgeFg = Color.FromRgb(0x7E, 0xA5, 0xF8);
                    badgeBorder = Color.FromRgb(0x3A, 0x4D, 0x7A);
                } else {
                    badgeText = "PACKAGE";
                    badgeBg = Color.FromRgb(0x2E, 0x22, 0x18);
                    badgeFg = Color.FromRgb(0xF6, 0xAD, 0x55);
                    badgeBorder = Color.FromRgb(0x7A, 0x50, 0x25);
                }

                var badge = new Border {
                    Background = new SolidColorBrush(badgeBg),
                    BorderBrush = new SolidColorBrush(badgeBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Child = new TextBlock {
                        Text = badgeText,
                        Foreground = new SolidColorBrush(badgeFg),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold
                    }
                };
                Grid.SetColumn(badge, 2);
                grid.Children.Add(badge);

                itemBorder.Child = grid;

                itemBorder.MouseEnter += (s, ev) => {
                    if (!isCurrent) {
                        itemBorder.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x3A));
                        itemBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                    }
                };
                itemBorder.MouseLeave += (s, ev) => {
                    if (!isCurrent) {
                        itemBorder.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x2A));
                        itemBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x30, 0x42));
                    }
                };

                var candCopy = candidate;
                itemBorder.PreviewMouseLeftButtonUp += (s, ev) => {
                    ev.Handled = true;
                    if (VersionSelectorPopup != null) VersionSelectorPopup.IsOpen = false;
                    HandleVersionSelected(candCopy);
                };

                VersionSelectorItemsPanel.Children.Add(itemBorder);
            }
        }

        private async void HandleVersionSelected(LocalVersionCandidate candidate) {
            try {
                var current = GetCurrentlyActiveVersion();
                bool isSame = (current != null && candidate.IsInstalled && ((!string.IsNullOrEmpty(current.InstanceId) && current.InstanceId == candidate.InstanceId) || (string.IsNullOrEmpty(current.InstanceId) && string.IsNullOrEmpty(candidate.InstanceId) && string.Equals(current.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))));

                var targetVer = candidate.VersionEntry ?? GetOrCreateVersionEntry(candidate);

                if (isSame) {
                    _selectedQuickPlayVersion = targetVer;
                    UpdateQuickPlayStatus();
                    ShowHeroProgress("LAUNCHING", "Launching Minecraft...");
                    InvokeLaunch(targetVer);
                    return;
                }

                // If currently running an external Minecraft and user is switching away to a different version
                if (current != null && current.IsExternal && (!candidate.IsExternal || candidate.Name != current.Name)) {
                    var warnRes = ThemedMessageBox.Show(
                        $"Warning: An externally installed Minecraft ({current.Name}) was detected.\n\n" +
                        "Installing or switching to this version will replace your external Minecraft installation. " +
                        "You will NOT be able to restore it unless you have its .appx package or re-download it from the Microsoft Store.\n\n" +
                        "Do you want to proceed?",
                        "External Minecraft Detected",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    );

                    if (warnRes != MessageBoxResult.Yes) {
                        UpdateQuickPlayStatus();
                        return;
                    }
                    _externalSwitchWarningConfirmed = true;
                }

                int cmp = CompareVersionStrings(candidate.Name, current?.Name);
                string actionType = current == null ? "INSTALLING" : (cmp > 0 ? "UPDATING" : "DOWNGRADING");
                string msg;
                string title;
                if (current == null) {
                    msg = $"Installing Minecraft {candidate.Name}...";
                    title = "Install Version";
                } else if (cmp > 0) {
                    msg = $"Updating Minecraft from {current.Name} to {candidate.Name}...";
                    title = "Update Version";
                } else {
                    msg = $"Downgrading Minecraft from {current.Name} to {candidate.Name}...";
                    title = "Downgrade Version";
                }

                ThemedMessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);

                _selectedQuickPlayVersion = targetVer;
                if (QuickPlayVersionLabel != null) {
                    QuickPlayVersionLabel.Text = $"{candidate.Name} ({actionType})";
                }

                if (!candidate.IsInstalled) {
                    if (string.IsNullOrEmpty(candidate.PackageFilePath) || !File.Exists(candidate.PackageFilePath)) {
                        ThemedMessageBox.Show("The package file for this version could not be found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        UpdateQuickPlayStatus();
                        return;
                    }

                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string targetDir = Path.Combine(baseDir, (candidate.VersionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + candidate.Name);
                    targetVer.GameDirectory = targetDir;

                    _heroCancelSource = new CancellationTokenSource();
                    ShowHeroProgress(actionType, "Preparing package...");

                    bool extractSuccess = false;
                    try {
                        if (candidate.PackageType == PackageType.UWP) {
                            extractSuccess = await ExtractAppx(candidate.PackageFilePath, targetDir, targetVer, _heroCancelSource.Token);
                        } else if (candidate.PackageType == PackageType.GDK) {
                            extractSuccess = await ExtractMsixvc(candidate.PackageFilePath, targetDir, targetVer, isPreview: candidate.VersionType == VersionType.Preview, cancellationToken: _heroCancelSource.Token);
                        }
                    } catch (OperationCanceledException) {
                        Trace.WriteLine("HandleVersionSelected: Operation cancelled by user.");
                        extractSuccess = false;
                    }

                    if (!extractSuccess) {
                        if (_heroCancelSource != null && _heroCancelSource.IsCancellationRequested) {
                            ThemedMessageBox.Show("Operation was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        HideHeroProgress();
                        UpdateQuickPlayStatus();
                        return;
                    }

                    targetVer.UpdateInstallStatus();
                    candidate.InstalledDirectory = targetDir;
                }

                ShowHeroProgress(actionType, "Launching Minecraft...");
                InvokeLaunch(targetVer);
                UpdateQuickPlayStatus();
            } catch (Exception ex) {
                HideHeroProgress();
                Trace.WriteLine("HandleVersionSelected error: " + ex);
                ThemedMessageBox.Show("An error occurred: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
