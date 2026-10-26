using System.Collections.Generic;
using Newtonsoft.Json;

namespace BUGrockLauncher {
    public class Preferences {
        public bool ShowInstalledOnly { get; set; } = false;

        public bool DeleteAppxAfterDownload { get; set; } = false;

        public bool AutoFetchDecryptionKeys { get; set; } = true;

        public string WebPerformanceMode { get; set; } = "High";

        public bool FpsUnlockerEnabled { get; set; } = false;

        public int MaxFramerate { get; set; } = 0; // 0 = Unlimited

        public bool WorldAutoBackupEnabled { get; set; } = false;

        public bool DiscordRpcEnabled { get; set; } = false;

        [JsonProperty("VersionsApi")]
        public string VersionsApiUWP { get; set; } = "";

        public string VersionsApiGDK { get; set; } = "";

        public string ActiveSkinType { get; set; } = "BUGrock";

        public string CustomSkinPath { get; set; } = "";

        public string CustomSkinName { get; set; } = "";

        public Dictionary<string, string> CustomInstallDirectories { get; set; } = new Dictionary<string, string>();
    }
}
