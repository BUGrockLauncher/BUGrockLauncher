using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BUGrockLauncher.Helpers
{
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
                // 1. UWP
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string uwpWorlds = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(uwpWorlds)) results.Add(uwpWorlds);

                // 2. GDK Shared
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string sharedWorlds = Path.Combine(appData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(sharedWorlds)) results.Add(sharedWorlds);

                // 3. User-specific
                string usersRoot = Path.Combine(appData, "Minecraft Bedrock", "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(usersRoot))
                    {
                        string userWorlds = Path.Combine(userDir, "games", "com.mojang", "minecraftWorlds");
                        if (Directory.Exists(userWorlds)) results.Add(userWorlds);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FindAllWorldsDirectories error: " + ex.Message);
            }

            return results.ToList();
        }

        public static (int worldsCount, long totalBytes, string backupPath) BackupWorldsNow(IProgress<string>? progress = null)
        {
            int totalWorlds = 0;
            string destDir = BackupsDirectory;
            string fileName = $"Worlds_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
            string destZip = Path.Combine(destDir, fileName);

            // Collect all unique world directories
            var worldDirs = FindAllWorldsDirectories();
            var worldsToBackup = new List<(string worldName, string fullPath)>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var wDir in worldDirs)
            {
                if (!Directory.Exists(wDir)) continue;
                foreach (var sub in Directory.GetDirectories(wDir))
                {
                    string wName = Path.GetFileName(sub);
                    if (seenNames.Add(wName))
                    {
                        worldsToBackup.Add((wName, sub));
                    }
                }
            }

            if (worldsToBackup.Count == 0)
            {
                return (0, 0, string.Empty);
            }

            // Stream directly into ZipArchive with fastest compression - ZERO intermediate temp files!
            using (var zipStream = new FileStream(destZip, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false))
            {
                for (int i = 0; i < worldsToBackup.Count; i++)
                {
                    var (worldName, fullPath) = worldsToBackup[i];
                    progress?.Report($"World {i + 1}/{worldsToBackup.Count} ({worldName})");

                    AddDirectoryToZip(archive, fullPath, worldName);
                    totalWorlds++;
                }
            }

            long size = new FileInfo(destZip).Length;
            return (totalWorlds, size, destZip);
        }

        private static void AddDirectoryToZip(ZipArchive archive, string sourceDirPath, string rootInZip)
        {
            var dirInfo = new DirectoryInfo(sourceDirPath);
            if (!dirInfo.Exists) return;

            int rootLength = dirInfo.FullName.Length;
            var dirs = new Stack<DirectoryInfo>();
            dirs.Push(dirInfo);

            byte[] buffer = new byte[65536]; // 64KB fast buffer

            while (dirs.Count > 0)
            {
                var currentDir = dirs.Pop();

                // Push subdirectories
                try
                {
                    foreach (var sub in currentDir.GetDirectories())
                    {
                        dirs.Push(sub);
                    }
                }
                catch { }

                // Add files
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

                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        using (var inStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536))
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
                        Trace.WriteLine($"Skipping locked world file {file.Name}: {ex.Message}");
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
                string? target = dirs.FirstOrDefault(Directory.Exists);
                if (string.IsNullOrEmpty(target))
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
