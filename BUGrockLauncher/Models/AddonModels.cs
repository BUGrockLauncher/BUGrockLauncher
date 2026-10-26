using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace BUGrockLauncher
{
    public class DownloadedAddonItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "Behavior Pack";
        public string FileSizeFormatted { get; set; } = string.Empty;
        public DateTime DateDownloaded { get; set; }
        public string FormattedDate => DateDownloaded.ToString("yyyy-MM-dd HH:mm");
        public string MinEngineVersion { get; set; } = "Unknown";
        public ImageSource? PackIcon { get; set; }
        public bool HasPackIcon => PackIcon != null;
        public string Extension { get; set; } = string.Empty;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                    OnPropertyChanged(nameof(DisplayDescription));
                    OnPropertyChanged(nameof(ExpandIconText));
                    OnPropertyChanged(nameof(ExpandTooltip));
                    OnPropertyChanged(nameof(CanExpandVisibility));
                }
            }
        }

        public string FirstLineDescription
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Description)) return "No description available.";
                using var reader = new StringReader(Description);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        return line;
                    }
                }
                return Description.Trim();
            }
        }

        public string DisplayDescription
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Description)) return "No description available.";
                return IsExpanded ? Description : FirstLineDescription;
            }
        }

        public string ExpandIconText => IsExpanded ? "▲" : "▼";
        public string ExpandTooltip => IsExpanded ? "Collapse description" : "Expand full description";

        public bool CanExpand
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Description)) return false;
                return Description.Contains('\n') || Description.Contains('\r') || Description.Trim().Length > 65;
            }
        }

        public Visibility CanExpandVisibility => CanExpand ? Visibility.Visible : Visibility.Collapsed;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TargetMinecraftVersion : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string VersionString { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public bool IsActiveOrExternal { get; set; }
        public bool IsExtractedDirectory { get; set; }
        public string DisplayType { get; set; } = "Extracted Version";
        public string Subtitle { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => Name;
    }

    public class InstalledItemInfo : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string ItemPath { get; set; } = string.Empty;
        public string ItemType { get; set; } = "ResourcePack";
        public string SizeFormatted { get; set; } = string.Empty;
        public ImageSource? Icon { get; set; }
        public bool HasIcon => Icon != null;
        public DateTime DateModified { get; set; }
        public string FormattedDate => DateModified.ToString("yyyy-MM-dd HH:mm");
        public ManagedVersionItem? OwnerVersion { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class InstalledServerInfo : INotifyPropertyChanged
    {
        public int Index { get; set; } = 1;
        public string ServerName { get; set; } = string.Empty;
        public string ServerAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 19132;
        public string RawLine { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public ManagedVersionItem? OwnerVersion { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ManagedVersionItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string VersionString { get; set; } = string.Empty;
        public string DisplayType { get; set; } = "System Bedrock (com.mojang)";
        public string DirectoryPath { get; set; } = string.Empty;
        public DateTime CreationDate { get; set; }
        public string FormattedCreationDate => $"Created: {CreationDate:yyyy-MM-dd HH:mm}";
        public bool IsActiveOrExternal { get; set; }
        public bool IsExtractedDirectory { get; set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                    OnPropertyChanged(nameof(ExpandedVisibility));
                    OnPropertyChanged(nameof(ExpandIconText));
                    OnPropertyChanged(nameof(ExpandTooltip));
                    if (_isExpanded && (ResourcePacks.Count == 0 && BehaviorPacks.Count == 0 && SkinPacks.Count == 0 && Worlds.Count == 0 && Servers.Count == 0))
                    {
                        LoadDetails();
                    }
                }
            }
        }

        public Visibility ExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        public string ExpandIconText => IsExpanded ? "▲" : "▼";
        public string ExpandTooltip => IsExpanded ? "Collapse version details" : "Expand version details";

        public ObservableCollection<InstalledItemInfo> ResourcePacks { get; } = new ObservableCollection<InstalledItemInfo>();
        public ObservableCollection<InstalledItemInfo> BehaviorPacks { get; } = new ObservableCollection<InstalledItemInfo>();
        public ObservableCollection<InstalledItemInfo> SkinPacks { get; } = new ObservableCollection<InstalledItemInfo>();
        public ObservableCollection<InstalledItemInfo> Worlds { get; } = new ObservableCollection<InstalledItemInfo>();
        public ObservableCollection<InstalledServerInfo> Servers { get; } = new ObservableCollection<InstalledServerInfo>();

        public int ResourcePacksCount => ResourcePacks.Count;
        public int BehaviorPacksCount => BehaviorPacks.Count;
        public int SkinPacksCount => SkinPacks.Count;
        public int WorldsCount => Worlds.Count;
        public int ServersCount => Servers.Count;

        public string ResourcePacksSummary => $"🎨 {ResourcePacksCount} Packs";
        public string BehaviorPacksSummary => $"⚙️ {BehaviorPacksCount} Behaviors";
        public string SkinPacksSummary => $"👕 {SkinPacksCount} Skins";
        public string WorldsSummary => $"🗺️ {WorldsCount} Worlds";
        public string ServersSummary => $"🌐 {ServersCount} Servers";

        public void NotifyCounts()
        {
            OnPropertyChanged(nameof(ResourcePacksCount));
            OnPropertyChanged(nameof(BehaviorPacksCount));
            OnPropertyChanged(nameof(SkinPacksCount));
            OnPropertyChanged(nameof(WorldsCount));
            OnPropertyChanged(nameof(ServersCount));
            OnPropertyChanged(nameof(ResourcePacksSummary));
            OnPropertyChanged(nameof(BehaviorPacksSummary));
            OnPropertyChanged(nameof(SkinPacksSummary));
            OnPropertyChanged(nameof(WorldsSummary));
            OnPropertyChanged(nameof(ServersSummary));
        }

        public void LoadDetails()
        {
            ResourcePacks.Clear();
            BehaviorPacks.Clear();
            SkinPacks.Clear();
            Worlds.Clear();
            Servers.Clear();

            try
            {
                if (IsActiveOrExternal)
                {
                    var rpDirs = new List<string>();
                    string sharedMojang = DirectoryPath;
                    string sharedRP = Path.Combine(sharedMojang, "resource_packs");
                    if (Directory.Exists(sharedRP)) rpDirs.Add(sharedRP);
                    string sharedDevRP = Path.Combine(sharedMojang, "development_resource_packs");
                    if (Directory.Exists(sharedDevRP)) rpDirs.Add(sharedDevRP);

                    string? userMojang = AddonManagerService.GetActiveGDKUserMojangPath();
                    if (!string.IsNullOrEmpty(userMojang) && !userMojang.Equals(sharedMojang, StringComparison.OrdinalIgnoreCase))
                    {
                        string userRP = Path.Combine(userMojang, "resource_packs");
                        if (Directory.Exists(userRP)) rpDirs.Add(userRP);
                        string userDevRP = Path.Combine(userMojang, "development_resource_packs");
                        if (Directory.Exists(userDevRP)) rpDirs.Add(userDevRP);
                    }

                    var seenRp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rpDir in rpDirs)
                    {
                        foreach (var sub in Directory.GetDirectories(rpDir))
                        {
                            var di = new DirectoryInfo(sub);
                            if (seenRp.Add(di.Name))
                            {
                                ResourcePacks.Add(ScanPackFolder(sub, "ResourcePack", this));
                            }
                        }
                    }

                    var bpDirs = new List<string>();
                    string sharedBP = Path.Combine(sharedMojang, "behavior_packs");
                    if (Directory.Exists(sharedBP)) bpDirs.Add(sharedBP);
                    string sharedDevBP = Path.Combine(sharedMojang, "development_behavior_packs");
                    if (Directory.Exists(sharedDevBP)) bpDirs.Add(sharedDevBP);
                    if (!string.IsNullOrEmpty(userMojang) && !userMojang.Equals(sharedMojang, StringComparison.OrdinalIgnoreCase))
                    {
                        string userBP = Path.Combine(userMojang, "behavior_packs");
                        if (Directory.Exists(userBP)) bpDirs.Add(userBP);
                        string userDevBP = Path.Combine(userMojang, "development_behavior_packs");
                        if (Directory.Exists(userDevBP)) bpDirs.Add(userDevBP);
                    }

                    var seenBp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var bpDir in bpDirs)
                    {
                        foreach (var sub in Directory.GetDirectories(bpDir))
                        {
                            var di = new DirectoryInfo(sub);
                            if (seenBp.Add(di.Name))
                            {
                                BehaviorPacks.Add(ScanPackFolder(sub, "BehaviorPack", this));
                            }
                        }
                    }

                    var spDirs = new List<string>();
                    string sharedSP = Path.Combine(sharedMojang, "skin_packs");
                    if (Directory.Exists(sharedSP)) spDirs.Add(sharedSP);
                    string sharedDevSP = Path.Combine(sharedMojang, "development_skin_packs");
                    if (Directory.Exists(sharedDevSP)) spDirs.Add(sharedDevSP);
                    if (!string.IsNullOrEmpty(userMojang) && !userMojang.Equals(sharedMojang, StringComparison.OrdinalIgnoreCase))
                    {
                        string userSP = Path.Combine(userMojang, "skin_packs");
                        if (Directory.Exists(userSP)) spDirs.Add(userSP);
                        string userDevSP = Path.Combine(userMojang, "development_skin_packs");
                        if (Directory.Exists(userDevSP)) spDirs.Add(userDevSP);
                    }

                    var seenSp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var spDir in spDirs)
                    {
                        foreach (var sub in Directory.GetDirectories(spDir))
                        {
                            var di = new DirectoryInfo(sub);
                            if (seenSp.Add(di.Name))
                            {
                                SkinPacks.Add(ScanPackFolder(sub, "SkinPack", this));
                            }
                        }
                    }

                    var wDirs = new List<string>();
                    if (!string.IsNullOrEmpty(userMojang))
                    {
                        string userW = Path.Combine(userMojang, "minecraftWorlds");
                        if (Directory.Exists(userW)) wDirs.Add(userW);
                    }
                    string sharedW = Path.Combine(sharedMojang, "minecraftWorlds");
                    if (Directory.Exists(sharedW)) wDirs.Add(sharedW);

                    var seenWorlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var wDir in wDirs)
                    {
                        foreach (var sub in Directory.GetDirectories(wDir))
                        {
                            var di = new DirectoryInfo(sub);
                            if (seenWorlds.Add(di.Name))
                            {
                                Worlds.Add(ScanWorldFolder(sub, this));
                            }
                        }
                    }

                    string serverFile = string.Empty;
                    if (!string.IsNullOrEmpty(userMojang))
                    {
                        string candidate = Path.Combine(userMojang, "minecraftpe", "external_servers.txt");
                        if (File.Exists(candidate)) serverFile = candidate;
                    }
                    if (string.IsNullOrEmpty(serverFile))
                    {
                        string candidate = Path.Combine(sharedMojang, "minecraftpe", "external_servers.txt");
                        if (File.Exists(candidate)) serverFile = candidate;
                    }
                    if (string.IsNullOrEmpty(serverFile))
                    {
                        string baseUsers = Path.GetFullPath(Path.Combine(sharedMojang, "..", "..", ".."));
                        if (Directory.Exists(baseUsers))
                        {
                            foreach (var u in Directory.GetDirectories(baseUsers))
                            {
                                string candidate = Path.Combine(u, "games", "com.mojang", "minecraftpe", "external_servers.txt");
                                if (File.Exists(candidate))
                                {
                                    serverFile = candidate;
                                    break;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(serverFile) && File.Exists(serverFile))
                    {
                        LoadServersFromFile(serverFile, Servers, this);
                    }
                }
                else
                {
                    string dataDir = Path.Combine(DirectoryPath, "data");
                    string effectiveData = Directory.Exists(dataDir) ? dataDir : DirectoryPath;

                    string rpDir = Path.Combine(effectiveData, "resource_packs");
                    if (Directory.Exists(rpDir))
                    {
                        foreach (var sub in Directory.GetDirectories(rpDir))
                        {
                            ResourcePacks.Add(ScanPackFolder(sub, "ResourcePack", this));
                        }
                    }
                    string devRpDir = Path.Combine(effectiveData, "development_resource_packs");
                    if (Directory.Exists(devRpDir))
                    {
                        foreach (var sub in Directory.GetDirectories(devRpDir))
                        {
                            if (!ResourcePacks.Any(p => Path.GetFileName(p.ItemPath).Equals(Path.GetFileName(sub), StringComparison.OrdinalIgnoreCase)))
                                ResourcePacks.Add(ScanPackFolder(sub, "ResourcePack", this));
                        }
                    }

                    string bpDir = Path.Combine(effectiveData, "behavior_packs");
                    if (Directory.Exists(bpDir))
                    {
                        foreach (var sub in Directory.GetDirectories(bpDir))
                        {
                            BehaviorPacks.Add(ScanPackFolder(sub, "BehaviorPack", this));
                        }
                    }
                    string devBpDir = Path.Combine(effectiveData, "development_behavior_packs");
                    if (Directory.Exists(devBpDir))
                    {
                        foreach (var sub in Directory.GetDirectories(devBpDir))
                        {
                            if (!BehaviorPacks.Any(p => Path.GetFileName(p.ItemPath).Equals(Path.GetFileName(sub), StringComparison.OrdinalIgnoreCase)))
                                BehaviorPacks.Add(ScanPackFolder(sub, "BehaviorPack", this));
                        }
                    }

                    string spDir = Path.Combine(effectiveData, "skin_packs");
                    if (Directory.Exists(spDir))
                    {
                        foreach (var sub in Directory.GetDirectories(spDir))
                        {
                            SkinPacks.Add(ScanPackFolder(sub, "SkinPack", this));
                        }
                    }
                    string devSpDir = Path.Combine(effectiveData, "development_skin_packs");
                    if (Directory.Exists(devSpDir))
                    {
                        foreach (var sub in Directory.GetDirectories(devSpDir))
                        {
                            if (!SkinPacks.Any(p => Path.GetFileName(p.ItemPath).Equals(Path.GetFileName(sub), StringComparison.OrdinalIgnoreCase)))
                                SkinPacks.Add(ScanPackFolder(sub, "SkinPack", this));
                        }
                    }

                    string wDir = Path.Combine(effectiveData, "minecraftWorlds");
                    if (Directory.Exists(wDir))
                    {
                        foreach (var sub in Directory.GetDirectories(wDir))
                        {
                            Worlds.Add(ScanWorldFolder(sub, this));
                        }
                    }

                    string serverFile = Path.Combine(effectiveData, "minecraftpe", "external_servers.txt");
                    if (!File.Exists(serverFile)) serverFile = Path.Combine(effectiveData, "external_servers.txt");
                    if (File.Exists(serverFile))
                    {
                        LoadServersFromFile(serverFile, Servers, this);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"LoadDetails error: {ex.Message}");
            }

            NotifyCounts();
        }

        private static InstalledItemInfo ScanPackFolder(string folderPath, string type, ManagedVersionItem owner)
        {
            var di = new DirectoryInfo(folderPath);
            string name = di.Name;
            ImageSource? icon = null;

            try
            {
                string manifestPath = Path.Combine(folderPath, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    string json = File.ReadAllText(manifestPath);
                    var obj = JObject.Parse(json);
                    string? rawName = obj["header"]?["name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(rawName))
                    {
                        if (rawName.Contains(".") || rawName.StartsWith("pack.") || rawName.StartsWith("skinpack."))
                        {
                            string lang = Path.Combine(folderPath, "texts", "en_US.lang");
                            if (File.Exists(lang))
                            {
                                foreach (var line in File.ReadAllLines(lang))
                                {
                                    var trimmed = line.Trim();
                                    if (trimmed.StartsWith(rawName + "=", StringComparison.OrdinalIgnoreCase) ||
                                        trimmed.StartsWith("skinpack." + rawName + "=", StringComparison.OrdinalIgnoreCase) ||
                                        trimmed.StartsWith("pack.name=", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int eqIdx = trimmed.IndexOf('=');
                                        if (eqIdx >= 0)
                                        {
                                            name = AddonManagerService.CleanMinecraftFormattingCodes(trimmed.Substring(eqIdx + 1));
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                name = AddonManagerService.CleanMinecraftFormattingCodes(rawName);
                            }
                        }
                        else
                        {
                            name = AddonManagerService.CleanMinecraftFormattingCodes(rawName);
                        }
                    }
                }

                // If name is still folder name and this is a skin pack, check skins.json
                if (name == di.Name)
                {
                    string skinsJson = Path.Combine(folderPath, "skins.json");
                    if (File.Exists(skinsJson))
                    {
                        try
                        {
                            var sObj = JObject.Parse(File.ReadAllText(skinsJson));
                            string? locName = sObj["localization_name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(locName)) name = locName;
                        }
                        catch { }
                    }
                }

                string iconPath = Path.Combine(folderPath, "pack_icon.png");
                if (!File.Exists(iconPath))
                {
                    try
                    {
                        iconPath = Directory.GetFiles(folderPath, "*.png").FirstOrDefault() ?? "";
                    }
                    catch { }
                }

                if (File.Exists(iconPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(iconPath);
                    bmp.EndInit();
                    bmp.Freeze();
                    icon = bmp;
                }
            }
            catch { }

            long size = 0;
            try
            {
                size = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            }
            catch { }

            return new InstalledItemInfo
            {
                Name = name,
                FolderName = di.Name,
                ItemPath = folderPath,
                ItemType = type,
                SizeFormatted = AddonManagerService.FormatBytes(size),
                DateModified = di.LastWriteTime,
                Icon = icon,
                OwnerVersion = owner
            };
        }

        private static InstalledItemInfo ScanWorldFolder(string folderPath, ManagedVersionItem owner)
        {
            var di = new DirectoryInfo(folderPath);
            string name = di.Name;
            ImageSource? icon = null;

            try
            {
                string ln = Path.Combine(folderPath, "levelname.txt");
                if (File.Exists(ln))
                {
                    string content = File.ReadAllText(ln).Trim();
                    if (!string.IsNullOrEmpty(content)) name = content;
                }

                string iconJpeg = Path.Combine(folderPath, "world_icon.jpeg");
                if (!File.Exists(iconJpeg)) iconJpeg = Path.Combine(folderPath, "world_icon.png");
                if (File.Exists(iconJpeg))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(iconJpeg);
                    bmp.EndInit();
                    bmp.Freeze();
                    icon = bmp;
                }
            }
            catch { }

            long size = 0;
            try
            {
                size = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            }
            catch { }

            return new InstalledItemInfo
            {
                Name = name,
                FolderName = di.Name,
                ItemPath = folderPath,
                ItemType = "World",
                SizeFormatted = AddonManagerService.FormatBytes(size),
                DateModified = di.LastWriteTime,
                Icon = icon,
                OwnerVersion = owner
            };
        }

        private static void LoadServersFromFile(string filePath, ObservableCollection<InstalledServerInfo> list, ManagedVersionItem owner)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    var parts = trimmed.Split(':');
                    if (parts.Length >= 4)
                    {
                        int.TryParse(parts[0], out int idx);
                        string sName = parts[1];
                        string sAddr = parts[2];
                        int.TryParse(parts[3], out int port);
                        if (port == 0) port = 19132;

                        list.Add(new InstalledServerInfo
                        {
                            Index = idx,
                            ServerName = sName,
                            ServerAddress = sAddr,
                            Port = port,
                            RawLine = trimmed,
                            FilePath = filePath,
                            OwnerVersion = owner
                        });
                    }
                }
            }
            catch { }
        }

        public string? GetServersFilePath()
        {
            if (Servers.Count > 0 && !string.IsNullOrEmpty(Servers[0].FilePath)) return Servers[0].FilePath;

            if (IsActiveOrExternal)
            {
                string? userMojang = AddonManagerService.GetActiveGDKUserMojangPath();
                if (!string.IsNullOrEmpty(userMojang))
                {
                    string p = Path.Combine(userMojang, "minecraftpe");
                    Directory.CreateDirectory(p);
                    return Path.Combine(p, "external_servers.txt");
                }
                string pShared = Path.Combine(DirectoryPath, "minecraftpe");
                Directory.CreateDirectory(pShared);
                return Path.Combine(pShared, "external_servers.txt");
            }
            else
            {
                string dataDir = Path.Combine(DirectoryPath, "data");
                string effectiveData = Directory.Exists(dataDir) ? dataDir : DirectoryPath;
                string p = Path.Combine(effectiveData, "minecraftpe");
                Directory.CreateDirectory(p);
                return Path.Combine(p, "external_servers.txt");
            }
        }

        public string GetSkinPacksDirectoryPath()
        {
            if (IsActiveOrExternal)
            {
                string? userMojang = AddonManagerService.GetActiveGDKUserMojangPath();
                if (!string.IsNullOrEmpty(userMojang))
                {
                    string p = Path.Combine(userMojang, "skin_packs");
                    Directory.CreateDirectory(p);
                    return p;
                }
                string pShared = Path.Combine(DirectoryPath, "skin_packs");
                Directory.CreateDirectory(pShared);
                return pShared;
            }
            else
            {
                string dataDir = Path.Combine(DirectoryPath, "data");
                string effectiveData = Directory.Exists(dataDir) ? dataDir : DirectoryPath;
                string p = Path.Combine(effectiveData, "skin_packs");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
