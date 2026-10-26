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

        public static (int worldsCount, long totalBytes, string backupPath) BackupWorldsNow()
        {
            int totalWorlds = 0;
            string destDir = BackupsDirectory;
            string fileName = $"Worlds_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
            string destZip = Path.Combine(destDir, fileName);

            string tempStaging = Path.Combine(Path.GetTempPath(), "BUGrock_WorldBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempStaging);

            try
            {
                var worldDirs = FindAllWorldsDirectories();
                var copiedWorlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var wDir in worldDirs)
                {
                    foreach (var sub in Directory.GetDirectories(wDir))
                    {
                        string wName = Path.GetFileName(sub);
                        if (copiedWorlds.Add(wName))
                        {
                            string targetSub = Path.Combine(tempStaging, wName);
                            CopyDirectory(sub, targetSub);
                            totalWorlds++;
                        }
                    }
                }

                if (totalWorlds == 0)
                {
                    return (0, 0, string.Empty);
                }

                ZipFile.CreateFromDirectory(tempStaging, destZip, CompressionLevel.Fastest, false);
                long size = new FileInfo(destZip).Length;
                return (totalWorlds, size, destZip);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempStaging)) Directory.Delete(tempStaging, true);
                }
                catch { }
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

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(destinationDir);

            foreach (var file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (var subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }
    }
}
