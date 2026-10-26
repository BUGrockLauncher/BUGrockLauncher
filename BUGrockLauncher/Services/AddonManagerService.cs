using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace BUGrockLauncher
{
    public static partial class AddonManagerService
    {
        public static List<string> GetAllActiveMojangPaths()
        {
            var paths = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddIfValid(string? p)
            {
                if (!string.IsNullOrWhiteSpace(p) && visited.Add(Path.GetFullPath(p)))
                {
                    paths.Add(Path.GetFullPath(p));
                }
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 1. Primary: Modern GDK Minecraft Bedrock (Users\Shared\games\com.mojang)
            string gdkShared = Path.Combine(appData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang");
            if (Directory.Exists(Path.Combine(appData, "Minecraft Bedrock")))
            {
                AddIfValid(gdkShared);
            }

            // 2. Legacy UWP Bedrock (Packages\Microsoft.MinecraftUWP_...\LocalState\games\com.mojang)
            string uwpPath = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang");
            if (Directory.Exists(Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe")))
            {
                AddIfValid(uwpPath);
            }

            // Fallback
            if (paths.Count == 0)
            {
                paths.Add(gdkShared);
            }

            return paths;
        }

        public static string? GetActiveGDKUserMojangPath()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string gdkUsers = Path.Combine(appData, "Minecraft Bedrock", "Users");
                if (!Directory.Exists(gdkUsers)) return null;

                var userDirs = Directory.GetDirectories(gdkUsers)
                    .Where(d => !Path.GetFileName(d).Equals("Shared", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => Directory.GetLastWriteTime(d));

                foreach (var d in userDirs)
                {
                    string candidate = Path.Combine(d, "games", "com.mojang");
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            catch { }
            return null;
        }

        public static string GetComMojangPath()
        {
            var all = GetAllActiveMojangPaths();
            return all.FirstOrDefault() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang");
        }

        public static List<DownloadedAddonItem> ScanDownloadedAddons()
        {
            var result = new List<DownloadedAddonItem>();
            DDFWManager.EnsureDirectories();

            string root = DDFWManager.RootFolder;
            if (!Directory.Exists(root)) return result;

            var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mcpack", ".mcaddon", ".mcworld", ".zip"
            };

            try
            {
                var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => supportedExts.Contains(Path.GetExtension(f)));

                foreach (var file in files)
                {
                    try
                    {
                        var item = ParseAddonFile(file);
                        if (item != null)
                        {
                            result.Add(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error parsing addon {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error scanning DDFW addons: {ex.Message}");
            }

            return result.OrderByDescending(x => x.DateDownloaded).ToList();
        }

        public static DownloadedAddonItem? ParseAddonFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            var fi = new FileInfo(filePath);
            string ext = fi.Extension.ToLowerInvariant();
            string fileName = fi.Name;

            string displayName = Path.GetFileNameWithoutExtension(fileName);
            string description = string.Empty;
            string minEngineVersion = "Unknown";
            string category = DetermineCategoryByExtensionAndPath(filePath, ext);
            ImageSource? packIcon = null;

            try
            {
                using var archive = ZipFile.OpenRead(filePath);

                // 1. Find pack icon (pack_icon.png or world_icon.jpeg)
                var iconEntry = archive.Entries.FirstOrDefault(e =>
                    e.Name.Equals("pack_icon.png", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.Equals("world_icon.jpeg", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.Equals("world_icon.png", StringComparison.OrdinalIgnoreCase));

                if (iconEntry != null)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        using var s = iconEntry.Open();
                        s.CopyTo(ms);
                        ms.Position = 0;

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        packIcon = bmp;
                    }
                    catch { }
                }

                // 2. Read manifest.json
                var manifestEntry = archive.Entries.FirstOrDefault(e =>
                    e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));

                if (manifestEntry != null)
                {
                    try
                    {
                        using var reader = new StreamReader(manifestEntry.Open());
                        string jsonStr = reader.ReadToEnd();
                        var json = JObject.Parse(jsonStr);

                        var header = json["header"] as JObject;
                        if (header != null)
                        {
                            string? rawName = header["name"]?.ToString();
                            displayName = ResolvePackNameOrFallback(archive, rawName, fileName, manifestEntry);

                            string? rawDesc = header["description"]?.ToString();
                            description = ResolvePackDescriptionOrFallback(archive, rawDesc, manifestEntry);

                            var minVerToken = header["min_engine_version"];
                            if (minVerToken is JArray minVerArr && minVerArr.Count > 0)
                            {
                                minEngineVersion = string.Join(".", minVerArr.Select(t => t.ToString()));
                            }
                        }

                        // Determine category from modules if .mcpack or .zip
                        if (ext == ".mcpack" || ext == ".zip")
                        {
                            var modules = json["modules"] as JArray;
                            if (modules != null)
                            {
                                foreach (var m in modules)
                                {
                                    string? mType = m["type"]?.ToString()?.ToLowerInvariant();
                                    if (mType == "data" || mType == "script")
                                    {
                                        category = "Behavior Pack";
                                        break;
                                    }
                                    if (mType == "resources")
                                    {
                                        category = "Resource Pack";
                                        break;
                                    }
                                    if (mType == "skin_pack")
                                    {
                                        category = "Skin Pack";
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not open zip archive for {filePath}: {ex.Message}");
            }

            return new DownloadedAddonItem
            {
                FilePath = filePath,
                FileName = fileName,
                DisplayName = displayName,
                Description = description,
                Category = category,
                FileSizeFormatted = FormatBytes(fi.Length),
                DateDownloaded = fi.LastWriteTime,
                MinEngineVersion = minEngineVersion,
                PackIcon = packIcon,
                Extension = ext
            };
        }

        private static string DetermineCategoryByExtensionAndPath(string filePath, string ext)
        {
            if (ext == ".mcworld") return "World / Map";
            if (ext == ".mcaddon") return "Addon (Behavior + Resource)";
            string lowerPath = filePath.ToLowerInvariant();
            if (lowerPath.Contains("texture") || lowerPath.Contains("shader")) return "Resource Pack";
            if (lowerPath.Contains("skin")) return "Skin Pack";
            if (lowerPath.Contains("map") || lowerPath.Contains("world")) return "World / Map";
            return "Behavior Pack";
        }

        public static string CleanMinecraftFormattingCodes(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Regex.Replace(text, @"§[0-9a-fk-or]", "", RegexOptions.IgnoreCase).Trim();
        }

        private static string ResolvePackNameOrFallback(ZipArchive archive, string? rawName, string fileName, ZipArchiveEntry? manifestEntry = null)
        {
            string clean = CleanMinecraftFormattingCodes(rawName ?? "");
            if (!string.IsNullOrWhiteSpace(clean) && !clean.Equals("pack.name", StringComparison.OrdinalIgnoreCase))
            {
                return clean;
            }

            try
            {
                string prefix = "";
                if (manifestEntry != null && manifestEntry.FullName.Contains('/'))
                {
                    prefix = manifestEntry.FullName.Substring(0, manifestEntry.FullName.LastIndexOf('/') + 1);
                }

                var langEntry = archive.Entries.FirstOrDefault(e =>
                    (e.FullName.StartsWith(prefix + "texts/", StringComparison.OrdinalIgnoreCase) ||
                     e.FullName.StartsWith("texts/", StringComparison.OrdinalIgnoreCase)) &&
                    (e.Name.Equals("en_US.lang", StringComparison.OrdinalIgnoreCase) ||
                     e.Name.Equals("en_GB.lang", StringComparison.OrdinalIgnoreCase) ||
                     e.Name.EndsWith(".lang", StringComparison.OrdinalIgnoreCase)));

                if (langEntry != null)
                {
                    using var reader = new StreamReader(langEntry.Open());
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("pack.name=", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = line.Substring("pack.name=".Length).Trim();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                return CleanMinecraftFormattingCodes(val);
                            }
                        }
                    }
                }
            }
            catch { }

            return Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');
        }

        private static string ResolvePackDescriptionOrFallback(ZipArchive archive, string? rawDesc, ZipArchiveEntry? manifestEntry = null)
        {
            string clean = CleanMinecraftFormattingCodes(rawDesc ?? "");
            if (!string.IsNullOrWhiteSpace(clean) && !clean.Equals("pack.description", StringComparison.OrdinalIgnoreCase))
            {
                return clean.Replace("\\n", "\n").Trim();
            }

            try
            {
                string prefix = "";
                if (manifestEntry != null && manifestEntry.FullName.Contains('/'))
                {
                    prefix = manifestEntry.FullName.Substring(0, manifestEntry.FullName.LastIndexOf('/') + 1);
                }

                var langEntry = archive.Entries.FirstOrDefault(e =>
                    (e.FullName.StartsWith(prefix + "texts/", StringComparison.OrdinalIgnoreCase) ||
                     e.FullName.StartsWith("texts/", StringComparison.OrdinalIgnoreCase)) &&
                    (e.Name.Equals("en_US.lang", StringComparison.OrdinalIgnoreCase) ||
                     e.Name.Equals("en_GB.lang", StringComparison.OrdinalIgnoreCase) ||
                     e.Name.EndsWith(".lang", StringComparison.OrdinalIgnoreCase)));

                if (langEntry != null)
                {
                    using var reader = new StreamReader(langEntry.Open());
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("pack.description=", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = line.Substring("pack.description=".Length).Trim();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                return CleanMinecraftFormattingCodes(val).Replace("\\n", "\n").Trim();
                            }
                        }
                    }
                }
            }
            catch { }

            return clean.Equals("pack.description", StringComparison.OrdinalIgnoreCase) ? string.Empty : clean.Replace("\\n", "\n").Trim();
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        public static List<TargetMinecraftVersion> ScanTargetVersions(BUGrockLauncher.WPFDataTypes.Version? activeVersion, IEnumerable<BUGrockLauncher.WPFDataTypes.Version>? knownVersions)
        {
            var list = new List<TargetMinecraftVersion>();
            string comMojang = GetComMojangPath();

            // 1. Active / System Bedrock
            bool comMojangExists = Directory.Exists(comMojang);
            string activeVerName = activeVersion != null ? activeVersion.Name : (comMojangExists ? "System Bedrock" : "None");
            if (comMojangExists)
            {
                list.Add(new TargetMinecraftVersion
                {
                    Name = $"Active Minecraft ({activeVerName})",
                    VersionString = ExtractNumericVersion(activeVerName),
                    DirectoryPath = comMojang,
                    IsActiveOrExternal = true,
                    IsExtractedDirectory = false,
                    DisplayType = "System Bedrock (com.mojang)",
                    Subtitle = "Direct install to active game data folder"
                });
            }

            // 2. Extracted directories in Launcher base directory and known versions
            var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (comMojangExists) visitedDirs.Add(comMojang);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidateDirs = new List<string>();

            if (Directory.Exists(baseDir))
            {
                candidateDirs.AddRange(Directory.GetDirectories(baseDir, "Minecraft-*"));
            }

            if (knownVersions != null)
            {
                foreach (var kv in knownVersions)
                {
                    if (kv.IsInstalled && !string.IsNullOrEmpty(kv.GameDirectory) && Directory.Exists(kv.GameDirectory))
                    {
                        candidateDirs.Add(kv.GameDirectory);
                    }
                }
            }

            foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (dir.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) != -1) continue;
                if (!visitedDirs.Add(dir)) continue;

                string dirName = Path.GetFileName(dir);
                string verNumber = ExtractNumericVersion(dirName);
                if (string.IsNullOrEmpty(verNumber)) verNumber = "1.0.0";

                list.Add(new TargetMinecraftVersion
                {
                    Name = dirName,
                    VersionString = verNumber,
                    DirectoryPath = dir,
                    IsActiveOrExternal = false,
                    IsExtractedDirectory = true,
                    DisplayType = "Extracted Launcher Version",
                    Subtitle = $"Files located at: {dir}"
                });
            }

            return list;
        }

        public static List<ManagedVersionItem> ScanManagedVersions(BUGrockLauncher.WPFDataTypes.Version? activeVersion, IEnumerable<BUGrockLauncher.WPFDataTypes.Version>? knownVersions)
        {
            var list = new List<ManagedVersionItem>();
            string comMojang = GetComMojangPath();

            if (Directory.Exists(comMojang))
            {
                var activeDir = new DirectoryInfo(comMojang);
                string activeName = activeVersion != null ? activeVersion.Name : "System Bedrock";
                var item = new ManagedVersionItem
                {
                    Name = $"Active Minecraft ({activeName})",
                    VersionString = ExtractNumericVersion(activeName),
                    DisplayType = "System Bedrock (com.mojang)",
                    DirectoryPath = comMojang,
                    CreationDate = activeDir.CreationTime < activeDir.LastWriteTime ? activeDir.CreationTime : activeDir.LastWriteTime,
                    IsActiveOrExternal = true,
                    IsExtractedDirectory = false
                };
                item.LoadDetails();
                list.Add(item);
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidateDirs = new List<string>();

            if (Directory.Exists(baseDir))
            {
                candidateDirs.AddRange(Directory.GetDirectories(baseDir, "Minecraft-*"));
            }

            string impDir = Path.Combine(baseDir, "imported_versions");
            if (Directory.Exists(impDir))
            {
                candidateDirs.AddRange(Directory.GetDirectories(impDir));
            }

            if (knownVersions != null)
            {
                foreach (var kv in knownVersions)
                {
                    if (kv.IsInstalled && !string.IsNullOrEmpty(kv.GameDirectory) && Directory.Exists(kv.GameDirectory))
                    {
                        candidateDirs.Add(kv.GameDirectory);
                    }
                }
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(comMojang)) visited.Add(comMojang);

            foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (dir.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) != -1) continue;
                if (!visited.Add(dir)) continue;

                var di = new DirectoryInfo(dir);
                string dirName = di.Name;
                string verNumber = ExtractNumericVersion(dirName);
                if (string.IsNullOrEmpty(verNumber)) verNumber = dirName;

                var item = new ManagedVersionItem
                {
                    Name = dirName,
                    VersionString = verNumber,
                    DisplayType = "Extracted Appx Version",
                    DirectoryPath = dir,
                    CreationDate = di.CreationTime,
                    IsActiveOrExternal = false,
                    IsExtractedDirectory = true
                };
                item.LoadDetails();
                list.Add(item);
            }

            list.Sort((a, b) => {
                if (a.IsActiveOrExternal != b.IsActiveOrExternal) return a.IsActiveOrExternal ? -1 : 1;
                int vComp = BUGrockLauncher.WPFDataTypes.VersionViewComparer.CompareVersionStrings(b.VersionString, a.VersionString);
                if (vComp != 0) return vComp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        public static bool DeleteInstalledItem(InstalledItemInfo item)
        {
            try
            {
                bool deletedAny = false;
                string packFolderName = Path.GetFileName(item.ItemPath);

                // 1. Delete the primary item path
                if (Directory.Exists(item.ItemPath))
                {
                    try { Directory.Delete(item.ItemPath, recursive: true); deletedAny = true; } catch { }
                }
                else if (File.Exists(item.ItemPath))
                {
                    try { File.Delete(item.ItemPath); deletedAny = true; } catch { }
                }

                // 2. Also purge corresponding folders across all com.mojang paths (e.g. skin_packs AND development_skin_packs)
                if (!string.IsNullOrEmpty(packFolderName))
                {
                    if (item.ItemType == "SkinPack" || item.ItemPath.Contains("skin_packs"))
                    {
                        PurgePackAcrossAllLocations(packFolderName, "skin_packs", "development_skin_packs");
                        deletedAny = true;
                    }
                    else if (item.ItemType == "ResourcePack" || item.ItemPath.Contains("resource_packs"))
                    {
                        PurgePackAcrossAllLocations(packFolderName, "resource_packs", "development_resource_packs");
                        deletedAny = true;
                    }
                    else if (item.ItemType == "BehaviorPack" || item.ItemPath.Contains("behavior_packs"))
                    {
                        PurgePackAcrossAllLocations(packFolderName, "behavior_packs", "development_behavior_packs");
                        deletedAny = true;
                    }
                }

                // Invalidate Bedrock manifest cache so Bedrock immediately recognizes removal
                ClearBedrockManifestCache();

                return deletedAny;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DeleteInstalledItem failed for {item.ItemPath}: {ex.Message}");
            }
            return false;
        }

        private static void PurgePackAcrossAllLocations(string folderName, string mainDirName, string devDirName)
        {
            try
            {
                var candidateDirs = new List<string>();

                // GDK roaming paths
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string gdkRoot = Path.Combine(appData, "Minecraft Bedrock");
                if (Directory.Exists(gdkRoot))
                {
                    candidateDirs.Add(Path.Combine(gdkRoot, "games", "com.mojang", mainDirName, folderName));
                    candidateDirs.Add(Path.Combine(gdkRoot, "games", "com.mojang", devDirName, folderName));

                    string usersDir = Path.Combine(gdkRoot, "Users");
                    if (Directory.Exists(usersDir))
                    {
                        foreach (var userFolder in Directory.GetDirectories(usersDir))
                        {
                            candidateDirs.Add(Path.Combine(userFolder, "games", "com.mojang", mainDirName, folderName));
                            candidateDirs.Add(Path.Combine(userFolder, "games", "com.mojang", devDirName, folderName));
                        }
                    }
                }

                // UWP paths
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string uwpRoot = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang");
                if (Directory.Exists(uwpRoot))
                {
                    candidateDirs.Add(Path.Combine(uwpRoot, mainDirName, folderName));
                    candidateDirs.Add(Path.Combine(uwpRoot, devDirName, folderName));
                }
                string uwpBetaRoot = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", "LocalState", "games", "com.mojang");
                if (Directory.Exists(uwpBetaRoot))
                {
                    candidateDirs.Add(Path.Combine(uwpBetaRoot, mainDirName, folderName));
                    candidateDirs.Add(Path.Combine(uwpBetaRoot, devDirName, folderName));
                }

                foreach (var dir in candidateDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }
            }
            catch { }
        }

        private static void ClearBedrockManifestCache()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string gdkCache = Path.Combine(appData, "Minecraft Bedrock", "PackManifestFactoryCache");
                if (File.Exists(gdkCache))
                {
                    try { File.Delete(gdkCache); } catch { }
                }
            }
            catch { }
        }

        public static bool DeleteInstalledServer(InstalledServerInfo server)
        {
            try
            {
                if (!string.IsNullOrEmpty(server.FilePath) && File.Exists(server.FilePath))
                {
                    var lines = File.ReadAllLines(server.FilePath).ToList();
                    lines.RemoveAll(l => l.Trim().Equals(server.RawLine.Trim(), StringComparison.OrdinalIgnoreCase));
                    File.WriteAllLines(server.FilePath, lines);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DeleteInstalledServer failed: {ex.Message}");
            }
            return false;
        }

        public static bool AddInstalledServer(ManagedVersionItem version, string serverName, string serverAddress, int port)
        {
            try
            {
                string? filePath = version.GetServersFilePath();
                if (string.IsNullOrEmpty(filePath)) return false;

                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                int nextIdx = 1;
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 0 && int.TryParse(parts[0], out int idx))
                        {
                            if (idx >= nextIdx) nextIdx = idx + 1;
                        }
                    }
                }

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string newLine = $"{nextIdx}:{serverName.Trim()}:{serverAddress.Trim()}:{port}:{timestamp}";
                File.AppendAllLines(filePath, new[] { newLine });
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AddInstalledServer failed: {ex.Message}");
            }
            return false;
        }

        public static string ExtractNumericVersion(string text)
        {
            var match = Regex.Match(text, @"\b(\d+(?:\.\d+)+)\b");
            return match.Success ? match.Groups[1].Value : text;
        }

        public static int CompareVersions(string v1, string v2)
        {
            if (string.IsNullOrEmpty(v2)) return 1;
            if (string.IsNullOrEmpty(v1)) return -1;
            var m1 = Regex.Matches(v1, @"\d+");
            var m2 = Regex.Matches(v2, @"\d+");
            int count = Math.Max(m1.Count, m2.Count);
            for (int i = 0; i < count; i++)
            {
                int val1 = i < m1.Count ? int.Parse(m1[i].Value) : 0;
                int val2 = i < m2.Count ? int.Parse(m2[i].Value) : 0;
                if (val1 != val2) return val1.CompareTo(val2);
            }
            return 0;
        }

        public static (bool IsCompatible, string TargetVer, string MinVer) CheckCompatibility(TargetMinecraftVersion target, DownloadedAddonItem addon)
        {
            if (string.IsNullOrEmpty(addon.MinEngineVersion) || addon.MinEngineVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return (true, target.VersionString, "Unknown");
            }

            string targetVer = target.VersionString;
            string minVer = addon.MinEngineVersion;

            int cmp = CompareVersions(targetVer, minVer);
            bool compatible = (cmp >= 0);
            return (compatible, targetVer, minVer);
        }
    }
}
