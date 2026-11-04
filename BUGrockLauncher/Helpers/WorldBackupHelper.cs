using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using BUGrockLauncher.Services;

namespace BUGrockLauncher.Helpers
{
    public class WorldInfo
    {
        public string FolderName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string ParentFolder { get; set; } = string.Empty;
        public long TotalSizeBytes { get; set; }
        public DateTime LastModified { get; set; }

        public string SizeFormatted => (TotalSizeBytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
    }

    public class VersionWorldGroup
    {
        public string VersionName { get; set; } = string.Empty;
        public string VersionNumber { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // "Instance", "Bedrock GDK", "Bedrock Preview", "Minecraft UWP", etc.
        public List<string> SourceDirectories { get; set; } = new List<string>();
        public List<WorldInfo> Worlds { get; set; } = new List<WorldInfo>();

        public string SafeFolderName
        {
            get
            {
                string namePart = WorldBackupHelper.SanitizeFileName(VersionName);
                string verPart = WorldBackupHelper.SanitizeFileName(VersionNumber);

                if (string.IsNullOrWhiteSpace(verPart))
                {
                    return string.IsNullOrWhiteSpace(namePart) ? "Minecraft_Worlds" : namePart;
                }

                if (!verPart.StartsWith("v", StringComparison.OrdinalIgnoreCase) && !namePart.EndsWith(verPart, StringComparison.OrdinalIgnoreCase))
                {
                    verPart = "v" + verPart;
                }

                if (namePart.Contains(verPart, StringComparison.OrdinalIgnoreCase))
                {
                    return namePart;
                }

                return $"{namePart}_{verPart}".Trim('_');
            }
        }
    }

    public static class WorldBackupHelper
    {
        public static string BackupsDirectory
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            cleaned = Regex.Replace(cleaned, @"\s+", "_");
            cleaned = Regex.Replace(cleaned, @"_+", "_");
            return cleaned.Trim('_', '.');
        }

        public static void CleanAndOrganizeBackupsDirectory()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string rootBackups = BackupsDirectory;

                // 1. Migrate legacy "Backups instances" folder if it exists
                string legacyInstancesDir = Path.Combine(baseDir, "Backups instances");
                if (Directory.Exists(legacyInstancesDir))
                {
                    var legacyZips = Directory.GetFiles(legacyInstancesDir, "*.zip", SearchOption.AllDirectories);
                    foreach (var z in legacyZips)
                    {
                        try
                        {
                            string target = Path.Combine(rootBackups, Path.GetFileName(z));
                            if (!File.Exists(target)) File.Move(z, target);
                        }
                        catch { }
                    }

                    try
                    {
                        if (Directory.GetFileSystemEntries(legacyInstancesDir).Length == 0)
                        {
                            Directory.Delete(legacyInstancesDir, true);
                        }
                    }
                    catch { }
                }

                // 2. Migrate legacy "Backups\Worlds" folder if it exists
                string legacyWorldsDir = Path.Combine(rootBackups, "Worlds");
                if (Directory.Exists(legacyWorldsDir))
                {
                    var legacyZips = Directory.GetFiles(legacyWorldsDir, "*.zip");
                    foreach (var z in legacyZips)
                    {
                        try
                        {
                            string target = Path.Combine(rootBackups, Path.GetFileName(z));
                            if (!File.Exists(target)) File.Move(z, target);
                        }
                        catch { }
                    }

                    try
                    {
                        if (Directory.GetFileSystemEntries(legacyWorldsDir).Length == 0)
                        {
                            Directory.Delete(legacyWorldsDir, true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("CleanAndOrganizeBackupsDirectory error: " + ex.Message);
            }
        }

        public static string DetectInstalledPackageVersion(string packageFamily)
        {
            try
            {
                var pkgManager = new Windows.Management.Deployment.PackageManager();
                var pkgs = pkgManager.FindPackages(packageFamily);
                foreach (var pkg in pkgs)
                {
                    var v = pkg.Id.Version;
                    return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                }
            }
            catch { }
            return string.Empty;
        }

        public static List<VersionWorldGroup> GetDetectedVersionGroups()
        {
            CleanAndOrganizeBackupsDirectory();
            var groups = new List<VersionWorldGroup>();
            var seenWorldPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Launcher Instances (from InstanceManager & disk)
            try
            {
                var instances = InstanceManager.Instance.GetInstances();
                var instanceMap = new Dictionary<string, InstanceInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var inst in instances)
                {
                    if (!string.IsNullOrEmpty(inst.Id)) instanceMap[inst.Id] = inst;
                    if (!string.IsNullOrEmpty(inst.Name)) instanceMap[inst.Name] = inst;
                }

                string instancesFolder = Path.Combine(baseDir, "instances");
                if (Directory.Exists(instancesFolder))
                {
                    foreach (var instDir in Directory.GetDirectories(instancesFolder))
                    {
                        string dirName = Path.GetFileName(instDir);
                        instanceMap.TryGetValue(dirName, out var info);

                        string vName = info != null && !string.IsNullOrWhiteSpace(info.Name) ? info.Name : dirName;
                        string vNum = info != null && !string.IsNullOrWhiteSpace(info.VersionNumber) ? info.VersionNumber : "";

                        // Possible worlds locations in instance
                        var possibleWorldsPaths = new List<string>
                        {
                            Path.Combine(instDir, "data", "games", "com.mojang", "minecraftWorlds"),
                            Path.Combine(instDir, "data", "com.mojang", "minecraftWorlds"),
                            Path.Combine(instDir, "data", "minecraftWorlds")
                        };
                        if (info != null && !string.IsNullOrWhiteSpace(info.DataDirectory))
                        {
                            possibleWorldsPaths.Insert(0, Path.Combine(info.DataDirectory, "minecraftWorlds"));
                            possibleWorldsPaths.Insert(0, Path.Combine(info.DataDirectory, "games", "com.mojang", "minecraftWorlds"));
                            possibleWorldsPaths.Insert(0, info.DataDirectory);
                        }

                        var validDirs = possibleWorldsPaths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var worlds = ScanWorldsInDirectories(validDirs, seenWorldPaths);

                        if (worlds.Count > 0)
                        {
                            groups.Add(new VersionWorldGroup
                            {
                                VersionName = vName,
                                VersionNumber = vNum,
                                Category = "Instance",
                                SourceDirectories = validDirs,
                                Worlds = worlds
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error grouping instance worlds: " + ex.Message);
            }

            // 2. GDK Minecraft Bedrock (Retail Active Installation)
            try
            {
                string gdkVersion = DetectInstalledPackageVersion(MinecraftPackageFamilies.MINECRAFT);
                if (string.IsNullOrEmpty(gdkVersion)) gdkVersion = "Latest";

                var gdkDirs = new List<string>();
                string usersRoot = Path.Combine(appData, "Minecraft Bedrock", "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(usersRoot))
                    {
                        string wPath = Path.Combine(userDir, "games", "com.mojang", "minecraftWorlds");
                        if (Directory.Exists(wPath)) gdkDirs.Add(wPath);
                    }
                }
                string sharedWorlds = Path.Combine(appData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(sharedWorlds)) gdkDirs.Add(sharedWorlds);

                var gdkWorlds = ScanWorldsInDirectories(gdkDirs, seenWorldPaths);
                if (gdkWorlds.Count > 0)
                {
                    groups.Add(new VersionWorldGroup
                    {
                        VersionName = "Minecraft_Bedrock_GDK",
                        VersionNumber = gdkVersion,
                        Category = "Bedrock GDK",
                        SourceDirectories = gdkDirs,
                        Worlds = gdkWorlds
                    });
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error grouping GDK worlds: " + ex.Message);
            }

            // 3. GDK Bedrock Preview
            try
            {
                string previewVersion = DetectInstalledPackageVersion(MinecraftPackageFamilies.MINECRAFT_PREVIEW);
                if (string.IsNullOrEmpty(previewVersion)) previewVersion = "Preview";

                var previewDirs = new List<string>();
                string previewUsersRoot = Path.Combine(appData, "Minecraft Bedrock Preview", "Users");
                if (Directory.Exists(previewUsersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(previewUsersRoot))
                    {
                        string wPath = Path.Combine(userDir, "games", "com.mojang", "minecraftWorlds");
                        if (Directory.Exists(wPath)) previewDirs.Add(wPath);
                    }
                }

                var previewWorlds = ScanWorldsInDirectories(previewDirs, seenWorldPaths);
                if (previewWorlds.Count > 0)
                {
                    groups.Add(new VersionWorldGroup
                    {
                        VersionName = "Minecraft_Bedrock_Preview",
                        VersionNumber = previewVersion,
                        Category = "Bedrock Preview",
                        SourceDirectories = previewDirs,
                        Worlds = previewWorlds
                    });
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error grouping Preview worlds: " + ex.Message);
            }

            // 4. Legacy UWP Minecraft
            try
            {
                string uwpWorldsDir = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(uwpWorldsDir))
                {
                    var uwpDirs = new List<string> { uwpWorldsDir };
                    var uwpWorlds = ScanWorldsInDirectories(uwpDirs, seenWorldPaths);
                    if (uwpWorlds.Count > 0)
                    {
                        string uwpVer = DetectInstalledPackageVersion(MinecraftPackageFamilies.MINECRAFT);
                        if (string.IsNullOrEmpty(uwpVer)) uwpVer = "UWP";

                        groups.Add(new VersionWorldGroup
                        {
                            VersionName = "Minecraft_UWP_Release",
                            VersionNumber = uwpVer,
                            Category = "Minecraft UWP",
                            SourceDirectories = uwpDirs,
                            Worlds = uwpWorlds
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error grouping UWP worlds: " + ex.Message);
            }

            // 5. Legacy UWP Beta
            try
            {
                string uwpBetaWorldsDir = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(uwpBetaWorldsDir))
                {
                    var betaDirs = new List<string> { uwpBetaWorldsDir };
                    var betaWorlds = ScanWorldsInDirectories(betaDirs, seenWorldPaths);
                    if (betaWorlds.Count > 0)
                    {
                        groups.Add(new VersionWorldGroup
                        {
                            VersionName = "Minecraft_UWP_Beta",
                            VersionNumber = "Beta",
                            Category = "Minecraft UWP Beta",
                            SourceDirectories = betaDirs,
                            Worlds = betaWorlds
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error grouping UWP Beta worlds: " + ex.Message);
            }

            // 6. Tmp Migration Directory
            try
            {
                string tmpWorldsDir = Path.Combine(localAppData, "TmpMinecraftLocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(tmpWorldsDir))
                {
                    var tmpDirs = new List<string> { tmpWorldsDir };
                    var tmpWorlds = ScanWorldsInDirectories(tmpDirs, seenWorldPaths);
                    if (tmpWorlds.Count > 0)
                    {
                        groups.Add(new VersionWorldGroup
                        {
                            VersionName = "Minecraft_Migration_State",
                            VersionNumber = "Archived",
                            Category = "Migration",
                            SourceDirectories = tmpDirs,
                            Worlds = tmpWorlds
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error grouping Tmp Migration worlds: " + ex.Message);
            }

            return groups;
        }

        private static List<WorldInfo> ScanWorldsInDirectories(IEnumerable<string> directories, HashSet<string> seenWorldPaths)
        {
            var worlds = new List<WorldInfo>();
            foreach (var wDir in directories)
            {
                if (!Directory.Exists(wDir)) continue;
                try
                {
                    foreach (var sub in Directory.GetDirectories(wDir))
                    {
                        if (!seenWorldPaths.Add(sub)) continue;

                        string fName = Path.GetFileName(sub);
                        string dName = GetWorldFriendlyName(sub);
                        long size = CalculateDirectorySize(sub);
                        DateTime lastMod = Directory.GetLastWriteTime(sub);

                        worlds.Add(new WorldInfo
                        {
                            FolderName = fName,
                            DisplayName = dName,
                            FullPath = sub,
                            ParentFolder = wDir,
                            TotalSizeBytes = size,
                            LastModified = lastMod
                        });
                    }
                }
                catch { }
            }
            return worlds.OrderByDescending(w => w.LastModified).ToList();
        }

        public static List<string> FindAllWorldsDirectories()
        {
            var groups = GetDetectedVersionGroups();
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                foreach (var d in g.SourceDirectories)
                {
                    if (Directory.Exists(d)) dirs.Add(d);
                }
            }
            return dirs.OrderByDescending(d => SafeGetDirectoryCount(d)).ToList();
        }

        public static List<WorldInfo> GetAllDetectedWorldsInfo()
        {
            var groups = GetDetectedVersionGroups();
            var all = new List<WorldInfo>();
            foreach (var g in groups)
            {
                all.AddRange(g.Worlds);
            }
            return all.OrderByDescending(w => w.LastModified).ToList();
        }

        public static string GetWorldFriendlyName(string worldDirectory)
        {
            try
            {
                string levelNamePath = Path.Combine(worldDirectory, "levelname.txt");
                if (File.Exists(levelNamePath))
                {
                    string name = File.ReadAllText(levelNamePath).Trim();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }

            return Path.GetFileName(worldDirectory);
        }

        public static long CalculateDirectorySize(string path)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            }
            catch { return 0; }
        }

        public static int SafeGetDirectoryCount(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return 0;
                return Directory.GetDirectories(path).Length;
            }
            catch { return 0; }
        }

        public class OrganizedBackupResult
        {
            public int TotalWorlds { get; set; }
            public int TotalVersions { get; set; }
            public long TotalBytes { get; set; }
            public List<string> CreatedArchives { get; set; } = new List<string>();
        }

        /// <summary>
        /// Backs up all worlds organized by version.
        /// Each version receives a dedicated ZIP archive named with its name and version number,
        /// placed inside an organized subfolder in Backups\.
        /// </summary>
        public static OrganizedBackupResult BackupAllVersionsOrganized(IProgress<string>? progress = null)
        {
            var result = new OrganizedBackupResult();
            var groups = GetDetectedVersionGroups().Where(g => g.Worlds.Count > 0).ToList();

            if (groups.Count == 0) return result;

            string rootBackups = BackupsDirectory;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            for (int gIdx = 0; gIdx < groups.Count; gIdx++)
            {
                var group = groups[gIdx];
                string folderName = group.SafeFolderName;
                string versionDir = Path.Combine(rootBackups, folderName);
                if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

                string zipName = $"{folderName}_Worlds_{timestamp}.zip";
                string zipPath = Path.Combine(versionDir, zipName);

                progress?.Report($"[{gIdx + 1}/{groups.Count}] Packaging {folderName} ({group.Worlds.Count} worlds)...");

                int worldsInZip = CreateZipFromWorlds(zipPath, group.Worlds, progress);
                if (worldsInZip > 0 && File.Exists(zipPath))
                {
                    long zipSize = new FileInfo(zipPath).Length;
                    result.TotalWorlds += worldsInZip;
                    result.TotalVersions++;
                    result.TotalBytes += zipSize;
                    result.CreatedArchives.Add(zipPath);
                }
            }

            return result;
        }

        /// <summary>
        /// Backs up worlds for a specific version being launched.
        /// </summary>
        public static (int count, long size, string zipPath) BackupWorldsForSpecificVersion(
            string versionName,
            string versionNumber,
            string? specificDataDir = null,
            IProgress<string>? progress = null)
        {
            CleanAndOrganizeBackupsDirectory();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetDirs = new List<string>();

            if (!string.IsNullOrWhiteSpace(specificDataDir))
            {
                var candidates = new[]
                {
                    Path.Combine(specificDataDir, "minecraftWorlds"),
                    Path.Combine(specificDataDir, "games", "com.mojang", "minecraftWorlds"),
                    specificDataDir
                };
                foreach (var c in candidates)
                {
                    if (Directory.Exists(c)) targetDirs.Add(c);
                }
            }

            // Fallback to active GDK if no specific directory supplied
            if (targetDirs.Count == 0)
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string usersRoot = Path.Combine(appData, "Minecraft Bedrock", "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(usersRoot))
                    {
                        string wPath = Path.Combine(userDir, "games", "com.mojang", "minecraftWorlds");
                        if (Directory.Exists(wPath)) targetDirs.Add(wPath);
                    }
                }
                string sharedWorlds = Path.Combine(appData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(sharedWorlds)) targetDirs.Add(sharedWorlds);
            }

            var worlds = ScanWorldsInDirectories(targetDirs, seen);
            if (worlds.Count == 0) return (0, 0, string.Empty);

            var group = new VersionWorldGroup
            {
                VersionName = versionName,
                VersionNumber = versionNumber,
                Worlds = worlds
            };

            string folderName = group.SafeFolderName;
            string versionDir = Path.Combine(BackupsDirectory, folderName);
            if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string zipName = $"{folderName}_Worlds_{timestamp}.zip";
            string zipPath = Path.Combine(versionDir, zipName);

            int count = CreateZipFromWorlds(zipPath, worlds, progress);
            long size = File.Exists(zipPath) ? new FileInfo(zipPath).Length : 0;
            return (count, size, zipPath);
        }

        /// <summary>
        /// Backward-compatible method that performs the organized backup across all versions.
        /// </summary>
        public static (int worldsCount, long totalBytes, string backupPath) BackupWorldsNow(IProgress<string>? progress = null)
        {
            var res = BackupAllVersionsOrganized(progress);
            string firstPath = res.CreatedArchives.FirstOrDefault() ?? string.Empty;
            return (res.TotalWorlds, res.TotalBytes, firstPath);
        }

        private static int CreateZipFromWorlds(string destZipPath, List<WorldInfo> worlds, IProgress<string>? progress)
        {
            if (worlds.Count == 0) return 0;
            int count = 0;

            // Stream directly into ZipArchive with fast 128KB buffer
            using (var zipStream = new FileStream(destZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 131072))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false))
            {
                for (int i = 0; i < worlds.Count; i++)
                {
                    var w = worlds[i];
                    progress?.Report($"({i + 1}/{worlds.Count}) {w.DisplayName}");

                    // Clean folder name inside zip: "WorldName_FolderName"
                    string safeDisplayName = SanitizeFileName(w.DisplayName);
                    if (string.IsNullOrEmpty(safeDisplayName)) safeDisplayName = w.FolderName;
                    string zipRoot = $"{safeDisplayName}_{w.FolderName}";

                    AddWorldDirectoryToZip(archive, w.FullPath, zipRoot);
                    count++;
                }
            }

            return count;
        }

        private static void AddWorldDirectoryToZip(ZipArchive archive, string sourceDirPath, string rootInZip)
        {
            var dirInfo = new DirectoryInfo(sourceDirPath);
            if (!dirInfo.Exists) return;

            int rootLength = dirInfo.FullName.Length;
            var dirs = new Stack<DirectoryInfo>();
            dirs.Push(dirInfo);

            byte[] buffer = new byte[131072]; // 128KB fast stream buffer

            while (dirs.Count > 0)
            {
                var currentDir = dirs.Pop();

                try
                {
                    foreach (var sub in currentDir.GetDirectories())
                    {
                        dirs.Push(sub);
                    }
                }
                catch { }

                FileInfo[] files;
                try
                {
                    files = currentDir.GetFiles();
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    try
                    {
                        string relPath = file.FullName.Substring(rootLength).TrimStart('\\', '/');
                        string entryName = string.IsNullOrEmpty(relPath)
                            ? $"{rootInZip}/{file.Name}"
                            : $"{rootInZip}/{relPath.Replace('\\', '/')}";

                        // PERFORMANCE CRITICAL:
                        // LevelDB files (.ldb, .log) are already internally compressed by Bedrock using Snappy.
                        // Compressing them with Deflate wastes CPU time and causes huge delays.
                        // Storing them uncompressed takes milliseconds and produces the exact same archive size.
                        bool isPreCompressed = file.Extension.Equals(".ldb", StringComparison.OrdinalIgnoreCase) ||
                                               file.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase);

                        var compression = isPreCompressed ? CompressionLevel.NoCompression : CompressionLevel.Fastest;

                        var entry = archive.CreateEntry(entryName, compression);
                        using (var inStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 131072))
                        using (var outStream = entry.Open())
                        {
                            int bytesRead;
                            while ((bytesRead = inStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                outStream.Write(buffer, 0, bytesRead);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Skipped locked file {file.Name}: {ex.Message}");
                    }
                }
            }
        }

        public static (int count, string lastBackupFormatted) GetBackupsStatus()
        {
            try
            {
                CleanAndOrganizeBackupsDirectory();
                if (!Directory.Exists(BackupsDirectory)) return (0, "No backups yet");

                var files = new DirectoryInfo(BackupsDirectory).GetFiles("*.zip", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                if (files.Count == 0) return (0, "No backups yet");
                return (files.Count, $"{files[0].CreationTime:yyyy-MM-dd HH:mm} ({files.Count} backups)");
            }
            catch
            {
                return (0, "Unknown");
            }
        }

        public static void OpenBackupsFolder()
        {
            try
            {
                CleanAndOrganizeBackupsDirectory();
                string dir = BackupsDirectory;
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public static void OpenPrimaryWorldsFolder()
        {
            try
            {
                var dirs = FindAllWorldsDirectories();
                string? target = dirs.FirstOrDefault(d => SafeGetDirectoryCount(d) > 0) ?? dirs.FirstOrDefault(Directory.Exists);
                if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    target = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang");
                    if (!Directory.Exists(target))
                    {
                        target = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    }
                }
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Trace.WriteLine("OpenPrimaryWorldsFolder error: " + ex.Message);
            }
        }
    }
}
