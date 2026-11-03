using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

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

    public static class WorldBackupHelper
    {
        public static string BackupsDirectory
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups", "Worlds");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static List<string> FindAllWorldsDirectories()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. GDK Shared & User-specific (Active primary Minecraft Bedrock locations)
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string usersRoot = Path.Combine(appData, "Minecraft Bedrock", "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(usersRoot))
                    {
                        string userWorlds = Path.Combine(userDir, "games", "com.mojang", "minecraftWorlds");
                        if (Directory.Exists(userWorlds)) results.Add(userWorlds);
                    }
                }

                string sharedWorlds = Path.Combine(appData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(sharedWorlds)) results.Add(sharedWorlds);

                // 2. GDK Preview
                string previewUsersRoot = Path.Combine(appData, "Minecraft Bedrock Preview", "Users");
                if (Directory.Exists(previewUsersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(previewUsersRoot))
                    {
                        string userWorlds = Path.Combine(userDir, "games", "com.mojang", "minecraftWorlds");
                        if (Directory.Exists(userWorlds)) results.Add(userWorlds);
                    }
                }

                // 3. UWP Minecraft
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string uwpWorlds = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(uwpWorlds)) results.Add(uwpWorlds);

                // 4. UWP Preview / Beta
                string uwpBetaWorlds = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(uwpBetaWorlds)) results.Add(uwpBetaWorlds);

                // 5. Temporary migration dir
                string tmpWorlds = Path.Combine(localAppData, "TmpMinecraftLocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(tmpWorlds)) results.Add(tmpWorlds);

                // 6. Instances directory inside launcher
                string instancesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Instances");
                if (Directory.Exists(instancesDir))
                {
                    foreach (var inst in Directory.GetDirectories(instancesDir))
                    {
                        string instWorlds = Path.Combine(inst, "data", "games", "com.mojang", "minecraftWorlds");
                        if (Directory.Exists(instWorlds)) results.Add(instWorlds);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FindAllWorldsDirectories error: " + ex.Message);
            }

            // Order so the folder containing the MOST worlds comes first!
            return results
                .OrderByDescending(d => SafeGetDirectoryCount(d))
                .ToList();
        }

        private static int SafeGetDirectoryCount(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return 0;
                return Directory.GetDirectories(path).Length;
            }
            catch { return 0; }
        }

        public static List<WorldInfo> GetAllDetectedWorldsInfo()
        {
            var worldInfos = new List<WorldInfo>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var dirs = FindAllWorldsDirectories();
            foreach (var wDir in dirs)
            {
                if (!Directory.Exists(wDir)) continue;
                try
                {
                    foreach (var sub in Directory.GetDirectories(wDir))
                    {
                        if (!seenPaths.Add(sub)) continue;

                        string fName = Path.GetFileName(sub);
                        string dName = GetWorldFriendlyName(sub);
                        long size = CalculateDirectorySize(sub);
                        DateTime lastMod = Directory.GetLastWriteTime(sub);

                        worldInfos.Add(new WorldInfo
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

            return worldInfos.OrderByDescending(w => w.LastModified).ToList();
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

        private static long CalculateDirectorySize(string path)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            }
            catch { return 0; }
        }

        public static (int worldsCount, long totalBytes, string backupPath) BackupWorldsNow(IProgress<string>? progress = null)
        {
            int totalWorlds = 0;
            string destDir = BackupsDirectory;
            string fileName = $"Worlds_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
            string destZip = Path.Combine(destDir, fileName);

            var worlds = GetAllDetectedWorldsInfo();
            if (worlds.Count == 0)
            {
                return (0, 0, string.Empty);
            }

            // Stream directly into ZipArchive with lightning-fast write buffer
            using (var zipStream = new FileStream(destZip, FileMode.Create, FileAccess.Write, FileShare.None, 131072))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false))
            {
                for (int i = 0; i < worlds.Count; i++)
                {
                    var w = worlds[i];
                    progress?.Report($"({i + 1}/{worlds.Count}) {w.DisplayName}");

                    // Clean folder name inside zip: "WorldName_FolderName"
                    string safeDisplayName = string.Join("_", w.DisplayName.Split(Path.GetInvalidFileNameChars())).Trim();
                    if (string.IsNullOrEmpty(safeDisplayName)) safeDisplayName = w.FolderName;
                    string zipRoot = $"{safeDisplayName}_{w.FolderName}";

                    AddWorldDirectoryToZip(archive, w.FullPath, zipRoot);
                    totalWorlds++;
                }
            }

            long size = new FileInfo(destZip).Length;
            return (totalWorlds, size, destZip);
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
                        // Compressing them with Deflate wastes 95% CPU time and causes huge delays.
                        // Storing them uncompressed takes milliseconds and produces the exact same archive size!
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
                if (!Directory.Exists(BackupsDirectory)) return (0, "No backups yet");
                var files = new DirectoryInfo(BackupsDirectory).GetFiles("*.zip")
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
                // Pick the folder that has the MOST worlds
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
