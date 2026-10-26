using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BUGrockLauncher.Helpers;
using BUGrockLauncher.WPFDataTypes;
using Newtonsoft.Json;

namespace BUGrockLauncher.Services
{
    public class InstanceInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public PackageType PackageType { get; set; } = PackageType.GDK;
        public VersionType VersionType { get; set; } = VersionType.Release;
        public string GameDirectory { get; set; } = "";
        public string DataDirectory { get; set; } = "";
        public string? AppxFilePath { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastPlayedDate { get; set; } = DateTime.MinValue;
    }

    public class InstanceManager
    {
        private static InstanceManager? _instance;
        public static InstanceManager Instance => _instance ??= new InstanceManager();

        public string InstancesDirectory { get; }
        public string BackupsDirectory { get; }
        public string InstancesJsonPath { get; }

        private readonly List<InstanceInfo> _instances = new List<InstanceInfo>();
        private readonly object _lock = new object();

        public InstanceManager()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            InstancesDirectory = Path.Combine(baseDir, "instances");
            BackupsDirectory = Path.Combine(baseDir, "Backups instances");
            InstancesJsonPath = Path.Combine(InstancesDirectory, "instances.json");

            try
            {
                if (!Directory.Exists(InstancesDirectory)) Directory.CreateDirectory(InstancesDirectory);
                if (!Directory.Exists(BackupsDirectory)) Directory.CreateDirectory(BackupsDirectory);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("InstanceManager initialization error: " + ex.Message);
            }

            LoadInstances();
        }

        public List<InstanceInfo> GetInstances()
        {
            lock (_lock)
            {
                return new List<InstanceInfo>(_instances);
            }
        }

        public InstanceInfo? GetInstanceById(string id)
        {
            lock (_lock)
            {
                return _instances.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        public InstanceInfo? GetInstanceByGameDir(string dir)
        {
            lock (_lock)
            {
                string full = Path.GetFullPath(dir);
                return _instances.FirstOrDefault(i => string.Equals(Path.GetFullPath(i.GameDirectory), full, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void LoadInstances()
        {
            lock (_lock)
            {
                _instances.Clear();
                if (File.Exists(InstancesJsonPath))
                {
                    try
                    {
                        string json = File.ReadAllText(InstancesJsonPath);
                        var list = JsonConvert.DeserializeObject<List<InstanceInfo>>(json);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                if (Directory.Exists(item.GameDirectory))
                                {
                                    if (string.IsNullOrWhiteSpace(item.DataDirectory))
                                    {
                                        item.DataDirectory = Path.Combine(item.GameDirectory, "data", "com.mojang");
                                    }
                                    _instances.Add(item);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Failed to load instances.json: " + ex.Message);
                    }
                }

                // Detect legacy & existing folder installations
                DetectLegacyAndFolderInstances();
                SaveInstances();
            }
        }

        public void SaveInstances()
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(InstancesDirectory)) Directory.CreateDirectory(InstancesDirectory);
                    string json = JsonConvert.SerializeObject(_instances, Formatting.Indented);
                    File.WriteAllText(InstancesJsonPath, json);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Failed to save instances.json: " + ex.Message);
                }
            }
        }

        public static string SanitizeInstanceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Instance";
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            string clean = Regex.Replace(name.Trim(), "[" + Regex.Escape(invalid) + "]", "_");
            return string.IsNullOrWhiteSpace(clean) ? "Instance" : clean;
        }

        public InstanceInfo CreateInstance(string name, string versionNumber, PackageType packageType, VersionType versionType, string gameDirectory, string? appxFilePath = null)
        {
            lock (_lock)
            {
                string dataDir = Path.Combine(gameDirectory, "data", "com.mojang");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

                var existing = _instances.FirstOrDefault(i => string.Equals(Path.GetFullPath(i.GameDirectory), Path.GetFullPath(gameDirectory), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Name = name;
                    existing.VersionNumber = versionNumber;
                    existing.PackageType = packageType;
                    existing.VersionType = versionType;
                    existing.AppxFilePath = appxFilePath;
                    existing.DataDirectory = dataDir;
                    SaveInstances();
                    return existing;
                }

                var instance = new InstanceInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    VersionNumber = versionNumber,
                    PackageType = packageType,
                    VersionType = versionType,
                    GameDirectory = Path.GetFullPath(gameDirectory),
                    DataDirectory = dataDir,
                    AppxFilePath = appxFilePath,
                    CreatedDate = DateTime.Now,
                    LastPlayedDate = DateTime.MinValue
                };

                _instances.Add(instance);
                SaveInstances();
                return instance;
            }
        }

        public bool RenameInstance(string instanceId, string newName)
        {
            lock (_lock)
            {
                var instance = _instances.FirstOrDefault(i => i.Id == instanceId);
                if (instance == null) return false;

                instance.Name = newName.Trim();
                SaveInstances();
                return true;
            }
        }

        public async Task<InstanceInfo?> DuplicateInstanceAsync(string instanceId, string newName, Action<string, int>? progressCallback = null)
        {
            InstanceInfo? source;
            lock (_lock)
            {
                source = _instances.FirstOrDefault(i => i.Id == instanceId);
            }
            if (source == null || !Directory.Exists(source.GameDirectory)) return null;

            string safeNewName = SanitizeInstanceName(newName);
            string newGameDir = Path.Combine(InstancesDirectory, safeNewName);

            // If folder already exists, find unique name
            int count = 1;
            while (Directory.Exists(newGameDir))
            {
                newGameDir = Path.Combine(InstancesDirectory, $"{safeNewName}_{count++}");
            }

            progressCallback?.Invoke("Copying instance files...", 10);

            await Task.Run(() =>
            {
                CopyDirectoryRecursive(source.GameDirectory, newGameDir, progressCallback);
            });

            lock (_lock)
            {
                var newInstance = new InstanceInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = newName.Trim(),
                    VersionNumber = source.VersionNumber,
                    PackageType = source.PackageType,
                    VersionType = source.VersionType,
                    GameDirectory = Path.GetFullPath(newGameDir),
                    DataDirectory = Path.Combine(newGameDir, "data", "com.mojang"),
                    AppxFilePath = source.AppxFilePath,
                    CreatedDate = DateTime.Now,
                    LastPlayedDate = DateTime.MinValue
                };

                _instances.Add(newInstance);
                SaveInstances();
                return newInstance;
            }
        }

        public bool DeleteInstance(string instanceId, bool deleteFiles = true)
        {
            lock (_lock)
            {
                var instance = _instances.FirstOrDefault(i => i.Id == instanceId);
                if (instance == null) return false;

                if (deleteFiles && Directory.Exists(instance.GameDirectory))
                {
                    try
                    {
                        Directory.Delete(instance.GameDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Failed to delete instance folder: " + ex.Message);
                    }
                }

                _instances.Remove(instance);
                SaveInstances();
                return true;
            }
        }

        public async Task<string> CreateBackupZipAsync(string instanceId, string oldVersion, Action<string, int>? progressCallback = null)
        {
            InstanceInfo? instance;
            lock (_lock)
            {
                instance = _instances.FirstOrDefault(i => i.Id == instanceId);
            }
            if (instance == null || !Directory.Exists(instance.GameDirectory))
            {
                throw new DirectoryNotFoundException("Instance directory does not exist for backup.");
            }

            if (!Directory.Exists(BackupsDirectory)) Directory.CreateDirectory(BackupsDirectory);

            string safeName = SanitizeInstanceName(instance.Name);
            // Formatted as: name_v<version>-yy-MM-dd-HH-mm.zip
            string dateStamp = DateTime.Now.ToString("yy-MM-dd-HH-mm");
            string zipFileName = $"{safeName}_v{oldVersion}-{dateStamp}.zip";
            string zipFilePath = Path.Combine(BackupsDirectory, zipFileName);

            progressCallback?.Invoke("Creating compressed backup archive...", 20);

            await Task.Run(() =>
            {
                if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
                ZipFile.CreateFromDirectory(instance.GameDirectory, zipFilePath, CompressionLevel.Optimal, false);
            });

            progressCallback?.Invoke("Backup complete!", 100);
            return zipFilePath;
        }

        public bool ApplyDataIsolation(InstanceInfo instance, string packageFamily)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(instance.DataDirectory))
                {
                    instance.DataDirectory = Path.Combine(instance.GameDirectory, "data", "com.mojang");
                }

                if (!Directory.Exists(instance.DataDirectory))
                {
                    Directory.CreateDirectory(instance.DataDirectory);
                }

                var targetPaths = GetMinecraftActiveDataPaths(packageFamily, instance.PackageType);

                foreach (var targetMojang in targetPaths)
                {
                    try
                    {
                        if (JunctionHelper.IsJunction(targetMojang))
                        {
                            JunctionHelper.RemoveJunction(targetMojang);
                            JunctionHelper.CreateJunction(targetMojang, instance.DataDirectory);
                        }
                        else if (Directory.Exists(targetMojang))
                        {
                            // If normal folder exists, check if instance data is empty
                            bool instanceEmpty = !Directory.EnumerateFileSystemEntries(instance.DataDirectory).Any();
                            bool targetHasData = Directory.EnumerateFileSystemEntries(targetMojang).Any();
                            if (targetHasData)
                            {
                                if (instanceEmpty)
                                {
                                    // Seed initial instance data from existing folder
                                    Trace.WriteLine($"Seeding instance data from {targetMojang} to {instance.DataDirectory}");
                                    CopyDirectoryRecursive(targetMojang, instance.DataDirectory);
                                }
                                else
                                {
                                    // Preserve pre-existing un-isolated system data in BackupsDirectory before replacing with junction
                                    string backupFolder = Path.Combine(BackupsDirectory, $"SystemData_Backup_{DateTime.Now:yy-MM-dd_HH-mm-ss_fff}");
                                    Trace.WriteLine($"Backing up existing system data from {targetMojang} to {backupFolder}");
                                    CopyDirectoryRecursive(targetMojang, backupFolder);
                                }
                            }

                            // Delete normal folder and replace with junction
                            try
                            {
                                Directory.Delete(targetMojang, true);
                                JunctionHelper.CreateJunction(targetMojang, instance.DataDirectory);
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine("Fallback to direct data sync: " + ex.Message);
                                CopyDirectoryRecursive(instance.DataDirectory, targetMojang);
                            }
                        }
                        else
                        {
                            string? parent = Path.GetDirectoryName(targetMojang);
                            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }
                            JunctionHelper.CreateJunction(targetMojang, instance.DataDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error applying isolation for path {targetMojang}: {ex.Message}");
                    }
                }

                instance.LastPlayedDate = DateTime.Now;
                SaveInstances();
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ApplyDataIsolation general error: " + ex.Message);
                return false;
            }
        }

        private List<string> GetMinecraftActiveDataPaths(string packageFamily, PackageType packageType)
        {
            var results = new List<string>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (packageType == PackageType.UWP)
            {
                string uwpData = Path.Combine(localAppData, "Packages", packageFamily, "LocalState", "games", "com.mojang");
                results.Add(uwpData);
            }
            else
            {
                string infix = packageFamily == MinecraftPackageFamilies.MINECRAFT_PREVIEW ? "Minecraft Bedrock Preview" : "Minecraft Bedrock";
                string gdkRoot = Path.Combine(appData, infix);
                string gdkBaseData = Path.Combine(gdkRoot, "games", "com.mojang");
                results.Add(gdkBaseData);

                string usersDir = Path.Combine(gdkRoot, "Users");
                if (Directory.Exists(usersDir))
                {
                    foreach (var userDir in Directory.GetDirectories(usersDir))
                    {
                        results.Add(Path.Combine(userDir, "games", "com.mojang"));
                    }
                }
            }

            return results;
        }

        private void DetectLegacyAndFolderInstances()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // 1. Scan inside instances directory for uncataloged subfolders
                if (Directory.Exists(InstancesDirectory))
                {
                    foreach (var dir in Directory.GetDirectories(InstancesDirectory))
                    {
                        string folderName = Path.GetFileName(dir);
                        if (string.Equals(folderName, "Backups instances", StringComparison.OrdinalIgnoreCase)) continue;

                        if (!_instances.Any(i => string.Equals(Path.GetFullPath(i.GameDirectory), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)))
                        {
                            bool isGdk = File.Exists(Path.Combine(dir, "MicrosoftGame.Config")) || File.Exists(Path.Combine(dir, "Minecraft.Windows.exe"));
                            string ver = ExtractVersionNumber(folderName);
                            _instances.Add(new InstanceInfo
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Name = folderName,
                                VersionNumber = ver,
                                PackageType = isGdk ? PackageType.GDK : PackageType.UWP,
                                VersionType = folderName.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ? VersionType.Preview : VersionType.Release,
                                GameDirectory = Path.GetFullPath(dir),
                                DataDirectory = Path.Combine(dir, "data", "com.mojang"),
                                CreatedDate = Directory.GetCreationTime(dir),
                                LastPlayedDate = DateTime.MinValue
                            });
                        }
                    }
                }

                // 2. Scan base directory for legacy Minecraft-* installations
                if (Directory.Exists(baseDir))
                {
                    var legacyDirs = Directory.GetDirectories(baseDir)
                        .Where(d => {
                            string fn = Path.GetFileName(d);
                            return fn.StartsWith("Minecraft-", StringComparison.OrdinalIgnoreCase) ||
                                   fn.StartsWith("Minecraft.Windows-", StringComparison.OrdinalIgnoreCase);
                        });

                    foreach (var dir in legacyDirs)
                    {
                        string folderName = Path.GetFileName(dir);
                        if (!_instances.Any(i => string.Equals(Path.GetFullPath(i.GameDirectory), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)))
                        {
                            bool isGdk = File.Exists(Path.Combine(dir, "MicrosoftGame.Config")) || File.Exists(Path.Combine(dir, "Minecraft.Windows.exe"));
                            string ver = ExtractVersionNumber(folderName);
                            _instances.Add(new InstanceInfo
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Name = folderName.Replace("Minecraft-Preview-", "").Replace("Minecraft-", ""),
                                VersionNumber = ver,
                                PackageType = isGdk ? PackageType.GDK : PackageType.UWP,
                                VersionType = folderName.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ? VersionType.Preview : VersionType.Release,
                                GameDirectory = Path.GetFullPath(dir),
                                DataDirectory = Path.Combine(dir, "data", "com.mojang"),
                                CreatedDate = Directory.GetCreationTime(dir),
                                LastPlayedDate = DateTime.MinValue
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("DetectLegacyAndFolderInstances error: " + ex.Message);
            }
        }

        private static string ExtractVersionNumber(string text)
        {
            var match = Regex.Match(text, @"(?:^|[^\d])([0-9]+\.[0-9]+(?:\.[0-9]+)*)");
            if (match.Success) return match.Groups[1].Value;
            return text.Replace("Minecraft-Preview-", "").Replace("Minecraft-", "").Trim();
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir, Action<string, int>? progressCallback = null)
        {
            Directory.CreateDirectory(targetDir);

            var files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
            int total = files.Length;
            int count = 0;

            foreach (string file in files)
            {
                string relPath = Path.GetRelativePath(sourceDir, file);
                string destFile = Path.Combine(targetDir, relPath);
                string? destFolder = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFolder) && !Directory.Exists(destFolder))
                {
                    Directory.CreateDirectory(destFolder);
                }
                File.Copy(file, destFile, true);
                count++;
                if (total > 0 && count % 20 == 0)
                {
                    int pct = 10 + (int)((float)count / total * 85);
                    progressCallback?.Invoke($"Copying ({count}/{total})...", pct);
                }
            }
        }
    }
}
