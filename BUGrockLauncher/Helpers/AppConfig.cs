using System;
using System.Collections.Generic;
using System.IO;

namespace BUGrockLauncher
{
    public static class AppConfig
    {
        private static readonly Dictionary<string, string> _envValues = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        static AppConfig()
        {
            LoadEnvFile();
        }

        public static void LoadEnvFile()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
                if (File.Exists(envPath))
                {
                    foreach (string rawLine in File.ReadAllLines(envPath))
                    {
                        string line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

                        int eqIndex = line.IndexOf('=');
                        if (eqIndex <= 0) continue;

                        string key = line.Substring(0, eqIndex).Trim();
                        string value = line.Substring(eqIndex + 1).Trim();

                        if ((value.StartsWith('"') && value.EndsWith('"')) ||
                            (value.StartsWith('\'') && value.EndsWith('\'')))
                        {
                            value = value.Substring(1, value.Length - 2);
                        }

                        if (!string.IsNullOrEmpty(key) && !_envValues.ContainsKey(key))
                        {
                            _envValues[key] = value;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static string Get(string key, string defaultValue = "")
        {
            LoadEnvFile();

            if (_envValues.TryGetValue(key, out string? val) && !string.IsNullOrWhiteSpace(val))
            {
                return val;
            }

            string? osEnv = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(osEnv))
            {
                return osEnv;
            }

            return defaultValue;
        }

        public static string VersionsApiUwp => Get(
            "VERSIONS_API_UWP",
            "https://raw.githubusercontent.com/BUGrockLauncher/BUGrockLauncher/master/uwp_versions.json");

        public static string VersionsApiGdk => Get(
            "VERSIONS_API_GDK",
            "https://raw.githubusercontent.com/MinecraftBedrockArchiver/GdkLinks/refs/heads/master/urls.min.json");

        public static string WuSecuredUrl => Get(
            "WU_SECURED_URL",
            "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured");

        public static string ServersHubUrl => Get(
            "SERVERS_HUB_URL",
            "https://minecraftpocket-servers.com/");

        public static string McpedlBaseUrl => Get(
            "MCPEDL_BASE_URL",
            "https://mcpedl.com");

        public static string CurseForgeBedrockUrl => Get(
            "CURSEFORGE_BEDROCK_URL",
            "https://www.curseforge.com/minecraft-bedrock");
    }
}
