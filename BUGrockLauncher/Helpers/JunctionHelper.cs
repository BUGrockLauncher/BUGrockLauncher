using System;
using System.Diagnostics;
using System.IO;

namespace BUGrockLauncher.Helpers
{
    public static class JunctionHelper
    {
        public static bool IsJunction(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
            try
            {
                var di = new DirectoryInfo(path);
                return di.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                return false;
            }
        }

        public static bool CreateJunction(string junctionPath, string targetPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(junctionPath) || string.IsNullOrWhiteSpace(targetPath)) return false;

                targetPath = Path.GetFullPath(targetPath);
                junctionPath = Path.GetFullPath(junctionPath);

                if (!Directory.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
                }

                if (Directory.Exists(junctionPath))
                {
                    if (IsJunction(junctionPath))
                    {
                        RemoveJunction(junctionPath);
                    }
                    else
                    {
                        // Directory exists as normal folder; cannot create junction over non-empty folder
                        return false;
                    }
                }

                string? parent = Path.GetDirectoryName(junctionPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(4000);

                return IsJunction(junctionPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("JunctionHelper: CreateJunction error: " + ex.Message);
                return false;
            }
        }

        public static void RemoveJunction(string junctionPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(junctionPath) || !Directory.Exists(junctionPath)) return;

                // rmdir removes the reparse point without deleting files inside target
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rmdir \"{junctionPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(4000);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("JunctionHelper: RemoveJunction error: " + ex.Message);
            }
        }
    }
}
