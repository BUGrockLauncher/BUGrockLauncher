using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BUGrockLauncher.Helpers
{
    public static class FpsUnlockerHelper
    {
        public static List<string> FindAllOptionsFiles()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. UWP com.mojang location
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string uwpOptions = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftpe", "options.txt");
                if (File.Exists(uwpOptions)) results.Add(uwpOptions);

                // 2. GDK Shared com.mojang location
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string sharedOptions = Path.Combine(appData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang", "minecraftpe", "options.txt");
                if (File.Exists(sharedOptions)) results.Add(sharedOptions);

                // 3. User-specific GDK profiles
                string usersRoot = Path.Combine(appData, "Minecraft Bedrock", "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(usersRoot))
                    {
                        string userOpt = Path.Combine(userDir, "games", "com.mojang", "minecraftpe", "options.txt");
                        if (File.Exists(userOpt)) results.Add(userOpt);
                    }
                }

                // 4. Launcher base directory / extracted versions
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var dir in Directory.GetDirectories(baseDir, "Minecraft-*", SearchOption.TopDirectoryOnly))
                {
                    string cand1 = Path.Combine(dir, "data", "minecraftpe", "options.txt");
                    if (File.Exists(cand1)) results.Add(cand1);
                    string cand2 = Path.Combine(dir, "minecraftpe", "options.txt");
                    if (File.Exists(cand2)) results.Add(cand2);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FindAllOptionsFiles error: " + ex.Message);
            }

            return new List<string>(results);
        }

        public static bool CheckCurrentStatus(out bool vsyncOff, out int maxFps)
        {
            vsyncOff = false;
            maxFps = 0;

            var files = FindAllOptionsFiles();
            if (files.Count == 0) return false;

            try
            {
                string text = File.ReadAllText(files[0]);
                var vsyncMatch = Regex.Match(text, @"^gfx_vsync:(\d+)", RegexOptions.Multiline);
                if (vsyncMatch.Success && vsyncMatch.Groups[1].Value == "0")
                {
                    vsyncOff = true;
                }

                var fpsMatch = Regex.Match(text, @"^gfx_max_framerate:(\d+)", RegexOptions.Multiline);
                if (fpsMatch.Success && int.TryParse(fpsMatch.Groups[1].Value, out int parsedFps))
                {
                    maxFps = parsedFps;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int ApplyFpsUnlock(bool disableVsync, int targetFramerate)
        {
            int modifiedCount = 0;
            var files = FindAllOptionsFiles();

            // If no options file exists yet, create one in shared GDK com.mojang and UWP if directories exist
            if (files.Count == 0)
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string sharedDir = Path.Combine(appData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang", "minecraftpe");
                Directory.CreateDirectory(sharedDir);
                string sharedFile = Path.Combine(sharedDir, "options.txt");
                File.WriteAllText(sharedFile, "gfx_vsync:0\ngfx_max_framerate:0\n");
                files.Add(sharedFile);
            }

            foreach (var file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    bool changed = false;

                    // 1. Update gfx_vsync
                    string targetVsyncStr = disableVsync ? "0" : "1";
                    if (Regex.IsMatch(content, @"^gfx_vsync:\d+", RegexOptions.Multiline))
                    {
                        content = Regex.Replace(content, @"^gfx_vsync:\d+", "gfx_vsync:" + targetVsyncStr, RegexOptions.Multiline);
                        changed = true;
                    }
                    else
                    {
                        content += "\ngfx_vsync:" + targetVsyncStr;
                        changed = true;
                    }

                    // 2. Update gfx_max_framerate
                    string targetFpsStr = targetFramerate.ToString();
                    if (Regex.IsMatch(content, @"^gfx_max_framerate:\d+", RegexOptions.Multiline))
                    {
                        content = Regex.Replace(content, @"^gfx_max_framerate:\d+", "gfx_max_framerate:" + targetFpsStr, RegexOptions.Multiline);
                        changed = true;
                    }
                    else
                    {
                        content += "\ngfx_max_framerate:" + targetFpsStr;
                        changed = true;
                    }

                    if (changed)
                    {
                        File.WriteAllText(file, content);
                        modifiedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error modifying {file}: {ex.Message}");
                }
            }

            return modifiedCount;
        }

        public static async Task CheckAndApplyOnStartupAsync(SplashScreenWindow? splash, bool isEnabled, int targetFps)
        {
            if (!isEnabled) return;

            try
            {
                splash?.SetStatus("Checking performance tweaks & unlocking FPS...");
                await Task.Run(() =>
                {
                    var files = FindAllOptionsFiles();
                    foreach (var file in files)
                    {
                        try
                        {
                            string content = File.ReadAllText(file);
                            bool needsUpdate = false;

                            var vMatch = Regex.Match(content, @"^gfx_vsync:(\d+)", RegexOptions.Multiline);
                            if (!vMatch.Success || vMatch.Groups[1].Value != "0")
                            {
                                needsUpdate = true;
                            }

                            var fMatch = Regex.Match(content, @"^gfx_max_framerate:(\d+)", RegexOptions.Multiline);
                            if (!fMatch.Success || fMatch.Groups[1].Value != targetFps.ToString())
                            {
                                needsUpdate = true;
                            }

                            if (needsUpdate)
                            {
                                ApplyFpsUnlock(disableVsync: true, targetFps);
                            }
                        }
                        catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine("CheckAndApplyOnStartupAsync error: " + ex.Message);
            }
        }
    }
}
