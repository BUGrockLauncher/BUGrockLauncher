using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BUGrockLauncher {

    struct MinecraftPackageFamilies {
        public const string MINECRAFT = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
        public const string MINECRAFT_PREVIEW = "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe";
    }

    namespace WPFDataTypes {

        public class NotifyPropertyChangedBase : INotifyPropertyChanged {

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged(string name) {
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs(name));
            }

        }

        public interface ICommonVersionCommands {

            ICommand LaunchCommand { get; }

            ICommand DownloadCommand { get; }

            ICommand RemoveCommand { get; }

            ICommand? RenameCommand => null;

            ICommand? DuplicateCommand => null;

            ICommand? UpgradeCommand => null;

        }

        public enum VersionType : int {
            Release = 0,
            Beta = 1,
            Preview = 2,
            Imported = 100
        }

        public enum PackageType {
            UWP,
            GDK
        }

        public class Version : NotifyPropertyChangedBase {
            public static readonly string UNKNOWN_UUID = "UNKNOWN";

            public Version(string uuid, string name, VersionType versionType, bool isNew, ICommonVersionCommands commands, PackageType packageType, List<string>? downloadUrls) {
                this.UUID = uuid;
                this._name = name;
                this.VersionType = versionType;
                this._isNew = isNew;
                this.DownloadCommand = commands.DownloadCommand;
                this.LaunchCommand = commands.LaunchCommand;
                this.RemoveCommand = commands.RemoveCommand;
                this.RenameCommand = commands.RenameCommand;
                this.DuplicateCommand = commands.DuplicateCommand;
                this.UpgradeCommand = commands.UpgradeCommand;
                this._gameDirectory = (versionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + name;
                this.PackageType = packageType;
                this.DownloadURLs = downloadUrls ?? new List<string>();
            }
            public Version(string name, string directory, ICommonVersionCommands commands, PackageType packageType) {
                this.UUID = UNKNOWN_UUID;
                this._name = name;
                this.VersionType = VersionType.Imported;
                this.DownloadCommand = commands.DownloadCommand;
                this.LaunchCommand = commands.LaunchCommand;
                this.RemoveCommand = commands.RemoveCommand;
                this.RenameCommand = commands.RenameCommand;
                this.DuplicateCommand = commands.DuplicateCommand;
                this.UpgradeCommand = commands.UpgradeCommand;
                this._gameDirectory = directory;
                this.PackageType = packageType;
                this.DownloadURLs = new List<string>();
            }

            public string UUID { get; set; }

            private string _name = string.Empty;
            public string Name {
                get => _name;
                set {
                    if (_name != value) {
                        _name = value;
                        _versionParts = null;
                        OnPropertyChanged(nameof(Name));
                    }
                }
            }

            public VersionType VersionType { get; set; }
            public bool IsNew {
                get { return _isNew; }
                set {
                    _isNew = value;
                    OnPropertyChanged("IsNew");
                }
            }
            public bool IsImported {
                get => VersionType == VersionType.Imported;
            }

            public bool IsExternal { get; set; } = false;

            public string DisplayVersionType => IsExternal ? "EXTERNAL" : (VersionType switch {
                VersionType.Preview => "PREVIEW",
                VersionType.Beta => "BETA",
                VersionType.Imported => "IMPORTED",
                _ => "RELEASE"
            });

            private string _gameDirectory = string.Empty;
            public string GameDirectory {
                get => _gameDirectory;
                set {
                    if (_gameDirectory != value) {
                        _gameDirectory = value;
                        _isInstalledCached = false;
                        OnPropertyChanged(nameof(GameDirectory));
                    }
                }
            }

            public PackageType PackageType { get; set; }

            public List<string> DownloadURLs { get; set; }

            public string GamePackageFamily {
                get => VersionType == VersionType.Preview ? MinecraftPackageFamilies.MINECRAFT_PREVIEW : MinecraftPackageFamilies.MINECRAFT;
            }

            private bool _isInstalled = false;
            private bool _isInstalledCached = false;

            public bool IsInstalled {
                get {
                    if (!_isInstalledCached) {
                        _isInstalled = !string.IsNullOrEmpty(GameDirectory) && Directory.Exists(GameDirectory);
                        _isInstalledCached = true;
                    }
                    return _isInstalled;
                }
                set {
                    _isInstalled = value;
                    _isInstalledCached = true;
                    OnPropertyChanged(nameof(IsInstalled));
                }
            }

            private int[]? _versionParts = null;
            public int[] VersionParts {
                get {
                    if (_versionParts == null) {
                        _versionParts = ParseVersionParts(Name);
                    }
                    return _versionParts;
                }
            }

            public static int[] ParseVersionParts(string? v) {
                if (string.IsNullOrEmpty(v)) return Array.Empty<int>();
                try {
                    var matches = System.Text.RegularExpressions.Regex.Matches(v, @"\d+");
                    var list = new int[matches.Count];
                    for (int i = 0; i < matches.Count; i++) {
                        int.TryParse(matches[i].Value, out list[i]);
                    }
                    return list;
                } catch {
                    return Array.Empty<int>();
                }
            }

            public string? InstanceId { get; set; }
            public string? CustomInstanceName { get; set; }
            public string? DataDirectory { get; set; }
            public bool IsInstance => !string.IsNullOrEmpty(InstanceId);

            private bool _canUpgrade = false;
            public bool CanUpgrade {
                get => _canUpgrade;
                set {
                    _canUpgrade = value;
                    OnPropertyChanged("CanUpgrade");
                }
            }
            public string? UpgradeTargetVersion { get; set; }

            public ICommand? RenameCommand { get; set; }
            public ICommand? DuplicateCommand { get; set; }
            public ICommand? UpgradeCommand { get; set; }

            public string DisplayName {
                get {
                    if (!string.IsNullOrEmpty(CustomInstanceName)) {
                        return CustomInstanceName;
                    }
                    if (IsExternal) {
                        return Name + " (External)";
                    }
                    string typeTag = "";
                    if (VersionType == VersionType.Beta)
                        typeTag = "(beta)";
                    else if (VersionType == VersionType.Preview)
                        typeTag = "(preview)";
                    string packageTypeTag = "";
                    if (PackageType == PackageType.GDK) {
                        packageTypeTag += "GDK";
                    } else if (PackageType == PackageType.UWP) {
                        packageTypeTag += "UWP";
                    }

                    return Name + " - " + packageTypeTag + (typeTag.Length > 0 ? " " + typeTag : "") + (IsNew ? " (NEW!)" : "");
                }
            }
            public string DisplayInstallStatus {
                get {
                    if (IsExternal) {
                        return "External Installation (Microsoft Store / Windows)";
                    }
                    if (IsInstalled) {
                        if (!string.IsNullOrEmpty(CustomInstanceName)) {
                            return $"Version {Name} • Installed in instances\\{Path.GetFileName(GameDirectory)}";
                        }
                        return "Installed";
                    }
                    return "Not installed";
                }
            }

            public ICommand LaunchCommand { get; set; }
            public ICommand DownloadCommand { get; set; }
            public ICommand RemoveCommand { get; set; }

            private VersionStateChangeInfo? _stateChangeInfo;
            private bool _isNew = false;
            public VersionStateChangeInfo? StateChangeInfo {
                get { return _stateChangeInfo; }
                set { _stateChangeInfo = value; OnPropertyChanged("StateChangeInfo"); OnPropertyChanged("IsStateChanging"); }
            }

            public bool IsStateChanging => StateChangeInfo != null;

            public void UpdateInstallStatus() {
                _isInstalled = !string.IsNullOrEmpty(GameDirectory) && Directory.Exists(GameDirectory);
                _isInstalledCached = true;
                OnPropertyChanged("IsInstalled");
                OnPropertyChanged("DisplayName");
                OnPropertyChanged("DisplayInstallStatus");
            }

            public void CheckCanUpgrade(IEnumerable<Version> allVersions) {
                if (!IsInstalled || IsExternal) {
                    CanUpgrade = false;
                    UpgradeTargetVersion = null;
                    return;
                }
                var newer = allVersions
                    .Where(o => !o.IsInstance && o.VersionType == this.VersionType && o.PackageType == this.PackageType)
                    .Where(o => CompareVersionStrings(o.Name, this.Name) > 0)
                    .OrderByDescending(o => o.Name, Comparer<string>.Create(CompareVersionStrings))
                    .FirstOrDefault();

                if (newer != null) {
                    CanUpgrade = true;
                    UpgradeTargetVersion = newer.Name;
                } else {
                    CanUpgrade = false;
                    UpgradeTargetVersion = null;
                }
            }

            private static int CompareVersionStrings(string v1, string v2) {
                if (string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase)) return 0;
                if (string.IsNullOrEmpty(v1)) return -1;
                if (string.IsNullOrEmpty(v2)) return 1;
                try {
                    var matches1 = System.Text.RegularExpressions.Regex.Matches(v1, @"\d+");
                    var matches2 = System.Text.RegularExpressions.Regex.Matches(v2, @"\d+");
                    int count = Math.Max(matches1.Count, matches2.Count);
                    for (int i = 0; i < count; i++) {
                        int val1 = i < matches1.Count ? int.Parse(matches1[i].Value) : 0;
                        int val2 = i < matches2.Count ? int.Parse(matches2[i].Value) : 0;
                        if (val1 != val2) return val1.CompareTo(val2);
                    }
                    return 0;
                } catch {
                    return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
                }
            }

        }

        public enum VersionState {
            Initializing,
            Downloading,
            Extracting,
            Registering,
            Launching,
            Unregistering,
            CleaningUp,
            Staging,
            Decrypting,
            Moving,
            MovingData
        };

        public class VersionStateChangeInfo : NotifyPropertyChangedBase {

            private VersionState _versionState;

            private long _progress = 0;
            private long _maxProgress = 0;
            private readonly ChromeSpeedCalculator _speedCalculator = new(windowSizeSeconds: 5);
            private string _downloadSpeedText = "";

            private static readonly SolidColorBrush _greenBrush;
            private static readonly SolidColorBrush _greenBadgeBrush;
            private static readonly SolidColorBrush _magentaBrush;
            private static readonly SolidColorBrush _magentaBadgeBrush;

            static VersionStateChangeInfo() {
                _greenBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                _greenBrush.Freeze();
                _greenBadgeBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x2E, 0x22));
                _greenBadgeBrush.Freeze();
                _magentaBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0x5C, 0xE3));
                _magentaBrush.Freeze();
                _magentaBadgeBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x15, 0x30));
                _magentaBadgeBrush.Freeze();
            }

            public VersionStateChangeInfo(VersionState versionState) {
                _versionState = versionState;
                if (_versionState == VersionState.Downloading) {
                    _speedCalculator.Start();
                }
            }

            public VersionState VersionState {
                get { return _versionState; }
                set {
                    _versionState = value;
                    _progress = 0;
                    _maxProgress = 0;
                    if (_versionState == VersionState.Downloading) {
                        _speedCalculator.Start();
                    } else {
                        _speedCalculator.Reset();
                    }
                    _downloadSpeedText = "";
                    OnPropertyChanged("Progress");
                    OnPropertyChanged("MaxProgress");
                    OnPropertyChanged("DownloadSpeedText");
                    OnPropertyChanged("SpeedVisibility");
                    OnPropertyChanged("IsProgressIndeterminate");
                    OnPropertyChanged("DisplayStatus");
                    OnPropertyChanged("StateAccentBrush");
                    OnPropertyChanged("StateBadgeBackgroundBrush");
                }
            }

            public bool IsProgressIndeterminate {
                get {
                    return _maxProgress == 0;
                }
            }

            public string DownloadSpeedText {
                get => _downloadSpeedText;
                private set {
                    if (_downloadSpeedText != value) {
                        _downloadSpeedText = value;
                        OnPropertyChanged("DownloadSpeedText");
                        OnPropertyChanged("SpeedVisibility");
                    }
                }
            }

            public Visibility SpeedVisibility =>
                (_versionState == VersionState.Downloading && !string.IsNullOrEmpty(_downloadSpeedText))
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            public long Progress {
                get { return _progress; }
                set {
                    _progress = value;
                    if (_versionState == VersionState.Downloading) {
                        DownloadSpeedText = _speedCalculator.UpdateProgress(value);
                    }
                    OnPropertyChanged("Progress");
                    OnPropertyChanged("DisplayStatus");
                }
            }

            public long MaxProgress {
                get { return _maxProgress; }
                set {
                    _maxProgress = value;
                    OnPropertyChanged("MaxProgress");
                    OnPropertyChanged("DisplayStatus");
                    OnPropertyChanged("IsProgressIndeterminate");
                }
            }

            public string DisplayStatus {
                get {
                    switch (_versionState) {
                        case VersionState.Initializing: return "Preparing...";
                        case VersionState.Downloading:
                            if (MaxProgress > 0) {
                                double progMB = (double)Progress / (1024.0 * 1024.0);
                                double maxMB = (double)MaxProgress / (1024.0 * 1024.0);
                                int percent = (int)Math.Clamp((Progress * 100.0) / MaxProgress, 0, 100);
                                return $"Downloading... {progMB:0.0} MB / {maxMB:0.0} MB ({percent}%)";
                            }
                            return $"Downloading... {((double)Progress / (1024.0 * 1024.0)):0.0} MB";
                        case VersionState.Extracting:
                            if (MaxProgress > 0) {
                                int percent = (int)Math.Clamp((Progress * 100.0) / MaxProgress, 0, 100);
                                int remaining = 100 - percent;
                                return $"Extracting... {percent}% ({remaining}% remaining)";
                            }
                            return "Extracting...";
                        case VersionState.Registering: return "Registering package...";
                        case VersionState.Launching: return "Launching...";
                        case VersionState.Unregistering: return "Unregistering package...";
                        case VersionState.CleaningUp: return "Cleaning up...";
                        case VersionState.Staging:
                            if (MaxProgress > 0) {
                                int percent = (int)Math.Clamp((Progress * 100.0) / MaxProgress, 0, 100);
                                int remaining = 100 - percent;
                                return $"Staging package... {percent}% ({remaining}% remaining)";
                            }
                            return "Staging package... (this might take a few minutes)";
                        case VersionState.Decrypting: return "Copying decrypted Minecraft.Windows.exe...";
                        case VersionState.Moving: return "Copying other game files...";
                        case VersionState.MovingData: return "Restoring Minecraft worlds...";
                        default: return "Processing...";
                    }
                }
            }

            public Brush StateAccentBrush {
                get {
                    switch (_versionState) {
                        case VersionState.Extracting:
                        case VersionState.Staging:
                        case VersionState.Decrypting:
                        case VersionState.Moving:
                        case VersionState.Registering:
                        case VersionState.Launching:
                            return _greenBrush;
                        default:
                            return _magentaBrush;
                    }
                }
            }

            public Brush StateBadgeBackgroundBrush {
                get {
                    switch (_versionState) {
                        case VersionState.Extracting:
                        case VersionState.Staging:
                        case VersionState.Decrypting:
                        case VersionState.Moving:
                        case VersionState.Registering:
                        case VersionState.Launching:
                            return _greenBadgeBrush;
                        default:
                            return _magentaBadgeBrush;
                    }
                }
            }

            private ICommand? _cancelCommand = null;

            public ICommand? CancelCommand {
                get => _cancelCommand;
                set {
                    _cancelCommand = value;
                    OnPropertyChanged("CancelCommand");
                    OnPropertyChanged("HasCancelCommand");
                }
            }

            public bool HasCancelCommand {
                get => CancelCommand != null;
            }

        }

        public class VersionViewComparer : System.Collections.IComparer {
            public int Compare(object? x, object? y) {
                if (x is not Version v1 || y is not Version v2) return 0;

                // 1. Installed or StateChanging versions come first!
                bool active1 = v1.IsInstalled || v1.IsStateChanging;
                bool active2 = v2.IsInstalled || v2.IsStateChanging;

                if (active1 != active2) {
                    return active1 ? -1 : 1; // Installed / Active versions at the very top
                }

                // Within the active group: Installed comes before StateChanging
                if (v1.IsInstalled != v2.IsInstalled) {
                    return v1.IsInstalled ? -1 : 1;
                }

                // 2. Sort by version number DESCENDING (newest version first) using cached int[] parts
                int vComp = CompareVersionParts(v2.VersionParts, v1.VersionParts);
                if (vComp != 0) return vComp;

                // 3. Tie-breaker by DisplayName / custom instance name
                int nameComp = string.Compare(v1.DisplayName, v2.DisplayName, StringComparison.OrdinalIgnoreCase);
                if (nameComp != 0) return nameComp;

                return v1.PackageType.CompareTo(v2.PackageType);
            }

            public static int CompareVersionParts(int[] p1, int[] p2) {
                int len = Math.Max(p1.Length, p2.Length);
                for (int i = 0; i < len; i++) {
                    int n1 = i < p1.Length ? p1[i] : 0;
                    int n2 = i < p2.Length ? p2[i] : 0;
                    if (n1 != n2) {
                        return n1.CompareTo(n2);
                    }
                }
                return 0;
            }

            public static int CompareVersionStrings(string? v1, string? v2) {
                if (string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase)) return 0;
                if (string.IsNullOrEmpty(v1)) return -1;
                if (string.IsNullOrEmpty(v2)) return 1;

                var p1 = Version.ParseVersionParts(v1);
                var p2 = Version.ParseVersionParts(v2);
                return CompareVersionParts(p1, p2);
            }
        }

    }
}
