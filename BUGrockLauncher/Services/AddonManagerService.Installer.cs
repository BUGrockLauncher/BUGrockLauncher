using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace BUGrockLauncher
{
    public static partial class AddonManagerService
    {
        public static (bool Success, string Message) InstallAddon(DownloadedAddonItem addon, TargetMinecraftVersion target, Action<int, int, string>? progressCallback = null)
        {
            try
            {
                if (!File.Exists(addon.FilePath))
                {
                    return (false, "Source file was not found.");
                }

                string ext = addon.Extension.ToLowerInvariant();
                string safePackName = Regex.Replace(Path.GetFileNameWithoutExtension(addon.FileName), @"[^\w\-\.]+", "_");

                if (target.IsActiveOrExternal)
                {
                    // Target active com.mojang paths (Primary: GDK Shared; if UWP exists, also UWP)
                    var mojangDirs = GetAllActiveMojangPaths();
                    if (!mojangDirs.Contains(target.DirectoryPath, StringComparer.OrdinalIgnoreCase))
                    {
                        mojangDirs.Insert(0, target.DirectoryPath);
                    }

                    // For worlds, also ensure active user profile folder is included
                    if (ext == ".mcworld")
                    {
                        string? activeUserMojang = GetActiveGDKUserMojangPath();
                        if (!string.IsNullOrEmpty(activeUserMojang) && !mojangDirs.Contains(activeUserMojang, StringComparer.OrdinalIgnoreCase))
                        {
                            mojangDirs.Add(activeUserMojang);
                        }
                    }

                    foreach (var comMojang in mojangDirs)
                    {
                        try
                        {
                            Directory.CreateDirectory(comMojang);

                            if (ext == ".mcworld")
                            {
                                string worldsDir = Path.Combine(comMojang, "minecraftWorlds");
                                Directory.CreateDirectory(worldsDir);
                                string destDir = Path.Combine(worldsDir, safePackName);
                                ExtractArchiveToDirectory(addon.FilePath, destDir, progressCallback);
                            }
                            else if (ext == ".mcaddon")
                            {
                                InstallMcAddonToMojang(addon.FilePath, comMojang, safePackName, addon.DisplayName, progressCallback);
                            }
                            else
                            {
                                InstallPackOrZipToMojang(addon, comMojang, safePackName, progressCallback);
                            }
                        }
                        catch { }
                    }

                    if (ext == ".mcworld")
                    {
                        return (true, $"World '{addon.DisplayName}' installed successfully into Minecraft Worlds!");
                    }
                    else if (ext == ".mcaddon")
                    {
                        return (true, $"Addon '{addon.DisplayName}' installed successfully into behavior & resource packs!");
                    }
                    else
                    {
                        return (true, $"Pack '{addon.DisplayName}' installed successfully into {addon.Category}!");
                    }
                }
                else
                {
                    // Target is extracted launcher version folder
                    string versionDir = target.DirectoryPath;
                    string dataDir = Path.Combine(versionDir, "data");
                    if (!Directory.Exists(dataDir))
                    {
                        Directory.CreateDirectory(dataDir);
                    }

                    if (ext == ".mcaddon")
                    {
                        return InstallMcAddonToExtractedData(addon.FilePath, dataDir, safePackName, addon.DisplayName, progressCallback);
                    }

                    return InstallPackOrZipToExtractedData(addon, dataDir, safePackName, progressCallback);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"InstallAddon error: {ex}");
                return (false, $"Installation failed: {ex.Message}");
            }
        }

        private static (bool Success, string Message) InstallMcAddonToMojang(string archivePath, string comMojang, string basePackName, string displayName, Action<int, int, string>? progressCallback = null)
        {
            string bpDir = Path.Combine(comMojang, "behavior_packs");
            string rpDir = Path.Combine(comMojang, "resource_packs");
            Directory.CreateDirectory(bpDir);
            Directory.CreateDirectory(rpDir);

            using var archive = ZipFile.OpenRead(archivePath);

            // Check if mcaddon contains nested .mcpack entries
            var nestedPacks = archive.Entries.Where(e => e.Name.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase)).ToList();
            if (nestedPacks.Count > 0)
            {
                int installedCount = 0;
                foreach (var np in nestedPacks)
                {
                    string tempPack = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_" + np.Name);
                    try
                    {
                        np.ExtractToFile(tempPack, overwrite: true);
                        var tempItem = ParseAddonFile(tempPack);
                        if (tempItem != null)
                        {
                            string subName = basePackName + "_" + Path.GetFileNameWithoutExtension(np.Name);
                            InstallPackOrZipToMojang(tempItem, comMojang, subName, progressCallback);
                            installedCount++;
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempPack)) try { File.Delete(tempPack); } catch { }
                    }
                }
                return (true, $"Addon '{displayName}' ({installedCount} sub-packs) installed successfully!");
            }

            // Direct extraction: examine root manifest
            string targetFolder = Path.Combine(bpDir, basePackName + "_BP");
            ExtractArchiveToDirectory(archivePath, targetFolder, progressCallback);
            return (true, $"Addon '{displayName}' extracted to behavior packs!");
        }

        private static (bool Success, string Message) InstallPackOrZipToMojang(DownloadedAddonItem addon, string comMojang, string safePackName, Action<int, int, string>? progressCallback = null)
        {
            string category = addon.Category;
            string destParent;

            if (category == "Resource Pack")
            {
                destParent = Path.Combine(comMojang, "resource_packs");
            }
            else if (category == "Skin Pack")
            {
                destParent = Path.Combine(comMojang, "skin_packs");
            }
            else if (category == "World / Map")
            {
                destParent = Path.Combine(comMojang, "minecraftWorlds");
            }
            else
            {
                destParent = Path.Combine(comMojang, "behavior_packs");
            }

            Directory.CreateDirectory(destParent);

            // Check if archive contains multiple sub-packs with their own manifest.json
            using (var checkArchive = ZipFile.OpenRead(addon.FilePath))
            {
                var manifestEntries = checkArchive.Entries
                    .Where(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (manifestEntries.Count > 1)
                {
                    int installedSubPacks = 0;
                    foreach (var mEntry in manifestEntries)
                    {
                        string prefix = "";
                        int lastSlash = mEntry.FullName.LastIndexOf('/');
                        if (lastSlash >= 0)
                        {
                            prefix = mEntry.FullName.Substring(0, lastSlash + 1);
                        }
                        string subFolder = prefix.TrimEnd('/');
                        if (subFolder.Contains('/')) subFolder = subFolder.Substring(subFolder.LastIndexOf('/') + 1);
                        if (string.IsNullOrWhiteSpace(subFolder)) subFolder = safePackName + "_" + installedSubPacks;

                        string subDest = Path.Combine(destParent, subFolder);
                        ExtractArchiveSubfolder(checkArchive, prefix, subDest);
                        EnsureValidPackManifest(subDest, subFolder);
                        installedSubPacks++;
                    }
                    return (true, $"Pack '{addon.DisplayName}' ({installedSubPacks} sub-packs) installed successfully into {Path.GetFileName(destParent)}!");
                }
            }

            string destDir = Path.Combine(destParent, safePackName);
            ExtractArchiveToDirectory(addon.FilePath, destDir, progressCallback);
            EnsureValidPackManifest(destDir, safePackName);

            // For skin packs, also copy to development_skin_packs so Bedrock loads custom skins without DRM restrictions
            if (category == "Skin Pack")
            {
                try
                {
                    string devParent = Path.Combine(comMojang, "development_skin_packs");
                    Directory.CreateDirectory(devParent);
                    string devDest = Path.Combine(devParent, safePackName);
                    CopyDirectoryContents(destDir, devDest);
                }
                catch { }
            }

            return (true, $"Pack '{addon.DisplayName}' installed successfully into {Path.GetFileName(destParent)}!");
        }

        private static void EnsureValidPackManifest(string packDir, string fallbackName)
        {
            try
            {
                string manifestPath = Path.Combine(packDir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    var sub = Directory.GetFiles(packDir, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(sub))
                    {
                        // Flatten nested manifest
                        string subDir = Path.GetDirectoryName(sub)!;
                        if (!subDir.Equals(packDir, StringComparison.OrdinalIgnoreCase))
                        {
                            CopyDirectoryContents(subDir, packDir);
                            try { Directory.Delete(subDir, recursive: true); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, file);
                string dest = Path.Combine(targetDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
        }

        private static void ExtractArchiveSubfolder(ZipArchive archive, string prefix, string destinationDirectory)
        {
            if (Directory.Exists(destinationDirectory))
            {
                try { Directory.Delete(destinationDirectory, recursive: true); } catch { }
            }
            Directory.CreateDirectory(destinationDirectory);

            string fullDestDir = Path.GetFullPath(destinationDirectory);
            if (!fullDestDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullDestDir += Path.DirectorySeparatorChar;
            }

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string rel = entry.FullName.Substring(prefix.Length);
                if (string.IsNullOrWhiteSpace(rel)) continue;

                string destPath = Path.GetFullPath(Path.Combine(destinationDirectory, rel));
                if (!destPath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase)) continue;

                string? dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        private static (bool Success, string Message) InstallMcAddonToExtractedData(string archivePath, string dataDir, string basePackName, string displayName, Action<int, int, string>? progressCallback = null)
        {
            string bpDir = Path.Combine(dataDir, "behavior_packs");
            string rpDir = Path.Combine(dataDir, "resource_packs");
            Directory.CreateDirectory(bpDir);
            Directory.CreateDirectory(rpDir);

            using var archive = ZipFile.OpenRead(archivePath);
            var nestedPacks = archive.Entries.Where(e => e.Name.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase)).ToList();

            if (nestedPacks.Count > 0)
            {
                foreach (var np in nestedPacks)
                {
                    string tempPack = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_" + np.Name);
                    try
                    {
                        np.ExtractToFile(tempPack, overwrite: true);
                        var tempItem = ParseAddonFile(tempPack);
                        if (tempItem != null)
                        {
                            string subName = basePackName + "_" + Path.GetFileNameWithoutExtension(np.Name);
                            InstallPackOrZipToExtractedData(tempItem, dataDir, subName, progressCallback);
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempPack)) try { File.Delete(tempPack); } catch { }
                    }
                }
                return (true, $"Addon '{displayName}' installed into version data folder!");
            }

            string targetFolder = Path.Combine(bpDir, basePackName);
            ExtractArchiveToDirectory(archivePath, targetFolder, progressCallback);
            return (true, $"Addon '{displayName}' extracted to version behavior packs!");
        }

        private static (bool Success, string Message) InstallPackOrZipToExtractedData(DownloadedAddonItem addon, string dataDir, string safePackName, Action<int, int, string>? progressCallback = null)
        {
            string destParent = (addon.Category == "Resource Pack")
                ? Path.Combine(dataDir, "resource_packs")
                : Path.Combine(dataDir, "behavior_packs");

            Directory.CreateDirectory(destParent);
            string destDir = Path.Combine(destParent, safePackName);

            ExtractArchiveToDirectory(addon.FilePath, destDir, progressCallback);
            return (true, $"Pack '{addon.DisplayName}' installed into version {Path.GetFileName(destParent)}!");
        }

        public static void ExtractArchiveToDirectory(string archivePath, string destinationDirectory, Action<int, int, string>? progressCallback = null)
        {
            if (Directory.Exists(destinationDirectory))
            {
                try { Directory.Delete(destinationDirectory, recursive: true); } catch { }
            }
            Directory.CreateDirectory(destinationDirectory);

            using var archive = ZipFile.OpenRead(archivePath);

            var validEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            int total = validEntries.Count;
            if (total == 0) return;

            // Check if archive has a single top-level folder wrapping manifest.json
            string commonPrefix = string.Empty;
            var manifestEntry = validEntries.FirstOrDefault(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry != null && manifestEntry.FullName.Contains('/'))
            {
                int slashIdx = manifestEntry.FullName.IndexOf('/');
                string possibleRoot = manifestEntry.FullName.Substring(0, slashIdx + 1);
                if (validEntries.All(e => e.FullName.StartsWith(possibleRoot, StringComparison.OrdinalIgnoreCase) ||
                                          e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                                          e.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                          e.Name.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ||
                                          e.Name.StartsWith(".")))
                {
                    commonPrefix = possibleRoot;
                }
            }

            string fullDestDir = Path.GetFullPath(destinationDirectory);
            if (!fullDestDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullDestDir += Path.DirectorySeparatorChar;
            }

            int count = 0;
            foreach (var entry in validEntries)
            {
                count++;
                string relativePath = entry.FullName;
                if (!string.IsNullOrEmpty(commonPrefix) && relativePath.StartsWith(commonPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath.Substring(commonPrefix.Length);
                }

                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                string destPath = Path.GetFullPath(Path.Combine(destinationDirectory, relativePath));
                if (!destPath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Path traversal prevention
                }

                string? dirName = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dirName))
                {
                    Directory.CreateDirectory(dirName);
                }

                entry.ExtractToFile(destPath, overwrite: true);

                if (count % 20 == 0 || count == total)
                {
                    progressCallback?.Invoke(count, total, entry.Name);
                }
            }
        }

        /// <summary>
        /// Ensures the default built-in skin packs (e.g. BUG Guy Family) are installed
        /// into the target version data folder and all active com.mojang paths.
        /// </summary>
        public static void EnsureDefaultSkinPacksInstalled(string? targetVersionDir = null)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string sourceFolder = Path.Combine(baseDir, "Assets", "DefaultPacks", "BUG_Guy_Family");
                string sourceMcpack = Path.Combine(baseDir, "Assets", "DefaultPacks", "BUG_Guy_Family.mcpack");

                if (!Directory.Exists(sourceFolder))
                {
                    string ddfwPack = Path.Combine(baseDir, "DDFW", "SkinPacks", "BUG_Guy_Family.mcpack");
                    if (File.Exists(ddfwPack))
                    {
                        sourceMcpack = ddfwPack;
                    }
                }

                // If target version directory is provided, install into version folders
                if (!string.IsNullOrEmpty(targetVersionDir) && Directory.Exists(targetVersionDir))
                {
                    try
                    {
                        // 1. Bedrock data/skin_packs (standard built-in location)
                        string dataSkinPacks = Path.Combine(targetVersionDir, "data", "skin_packs", "BUG_Guy_Family");
                        InstallSkinPackToFolder(sourceFolder, sourceMcpack, dataSkinPacks);

                        // 2. Top-level skin_packs if directory exists
                        string topSkinPacks = Path.Combine(targetVersionDir, "skin_packs");
                        if (Directory.Exists(topSkinPacks))
                        {
                            InstallSkinPackToFolder(sourceFolder, sourceMcpack, Path.Combine(topSkinPacks, "BUG_Guy_Family"));
                        }

                        // 3. Legacy resource_packs/skins/skinpacks if exists
                        string legacySkinpacks = Path.Combine(targetVersionDir, "data", "resource_packs", "skins", "skinpacks");
                        if (Directory.Exists(legacySkinpacks))
                        {
                            InstallSkinPackToFolder(sourceFolder, sourceMcpack, Path.Combine(legacySkinpacks, "BUG_Guy_Family"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[DefaultSkinPack] Error installing to version: {ex.Message}");
                    }
                }

                // Deploy to active com.mojang paths
                InstallDefaultSkinPackToComMojang(sourceFolder, sourceMcpack);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DefaultSkinPack] EnsureDefaultSkinPacksInstalled failed: {ex.Message}");
            }
        }

        private static void InstallSkinPackToFolder(string sourceFolder, string sourceMcpack, string destinationFolder)
        {
            try
            {
                if (Directory.Exists(destinationFolder))
                {
                    string existingManifest = Path.Combine(destinationFolder, "manifest.json");
                    if (File.Exists(existingManifest))
                    {
                        return; // Already installed
                    }
                }

                Directory.CreateDirectory(destinationFolder);

                if (Directory.Exists(sourceFolder) && File.Exists(Path.Combine(sourceFolder, "manifest.json")))
                {
                    CopyDirectoryContents(sourceFolder, destinationFolder);
                    return;
                }

                if (File.Exists(sourceMcpack))
                {
                    ExtractArchiveToDirectory(sourceMcpack, destinationFolder);
                    return;
                }

                // Fallback: extract from embedded assembly resource
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var resStream = asm.GetManifestResourceStream("BUGrockLauncher.Assets.DefaultPacks.BUG_Guy_Family.mcpack");
                if (resStream != null)
                {
                    using var archive = new ZipArchive(resStream, ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string destinationPath = Path.Combine(destinationFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                        string? dir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                    return;
                }


            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DefaultSkinPack] InstallSkinPackToFolder error: {ex.Message}");
            }
        }

        private static void InstallDefaultSkinPackToComMojang(string sourceFolder, string sourceMcpack)
        {
            try
            {
                var targetDirs = new List<string>();

                // GDK roaming paths
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string gdkRoot = Path.Combine(appData, "Minecraft Bedrock");
                if (Directory.Exists(gdkRoot))
                {
                    targetDirs.Add(Path.Combine(gdkRoot, "games", "com.mojang", "skin_packs", "BUG_Guy_Family"));
                    targetDirs.Add(Path.Combine(gdkRoot, "games", "com.mojang", "development_skin_packs", "BUG_Guy_Family"));

                    string usersDir = Path.Combine(gdkRoot, "Users");
                    if (Directory.Exists(usersDir))
                    {
                        foreach (var userFolder in Directory.GetDirectories(usersDir))
                        {
                            targetDirs.Add(Path.Combine(userFolder, "games", "com.mojang", "skin_packs", "BUG_Guy_Family"));
                            targetDirs.Add(Path.Combine(userFolder, "games", "com.mojang", "development_skin_packs", "BUG_Guy_Family"));
                        }
                    }
                }

                // UWP paths
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string uwpRoot = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang");
                if (Directory.Exists(uwpRoot))
                {
                    targetDirs.Add(Path.Combine(uwpRoot, "skin_packs", "BUG_Guy_Family"));
                    targetDirs.Add(Path.Combine(uwpRoot, "development_skin_packs", "BUG_Guy_Family"));
                }

                string uwpBetaRoot = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", "LocalState", "games", "com.mojang");
                if (Directory.Exists(uwpBetaRoot))
                {
                    targetDirs.Add(Path.Combine(uwpBetaRoot, "skin_packs", "BUG_Guy_Family"));
                    targetDirs.Add(Path.Combine(uwpBetaRoot, "development_skin_packs", "BUG_Guy_Family"));
                }

                foreach (var dir in targetDirs)
                {
                    InstallSkinPackToFolder(sourceFolder, sourceMcpack, dir);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DefaultSkinPack] InstallDefaultSkinPackToComMojang error: {ex.Message}");
            }
        }
    }
}
