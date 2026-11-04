using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BUGrockLauncher.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BUGrockLauncher.Services
{
    public class ServerPresenceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Port { get; set; } = "19132";
        public long Timestamp { get; set; }
    }

    public static class DiscordRpcService
    {
        // Official, verified Minecraft application ID with 150+ uploaded Rich Presence assets
        public const string DEFAULT_CLIENT_ID = "450485984333660181";

        // Official high-resolution BUGrock Glitch Cube Discord presence icon
        public const string DISCORD_ICON_URL = "https://raw.githubusercontent.com/BUGrockLauncher/BUGrockLauncher/master/BUGrockLauncher/Assets/bugrock_discord_cube.png";

        private static NamedPipeClientStream? _pipe;
        private static bool _isRunning = false;
        private static bool _isEnabled = true;
        private static CancellationTokenSource? _cts;

        private static string _clientId = DEFAULT_CLIENT_ID;
        private static long _gameStartTimestamp = 0;
        private static long _launcherStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static string _currentDetails = "Minecraft Bedrock";
        private static string _currentState = "In Launcher";
        private static string _largeImage = DISCORD_ICON_URL;
        private static string _largeText = "BUGrock Launcher";
        private static string? _smallImage = null;
        private static string? _smallText = null;
        private static bool _isInGame = false;

        private static string? _manualServerName = null;
        private static string? _manualServerIp = null;
        private static string? _manualServerPort = null;
        private static DateTime _manualServerSetTime = DateTime.MinValue;

        private static string? _lastActiveWorldName = null;
        private static DateTime _lastActiveWorldTime = DateTime.MinValue;

        public static bool IsConnected => _pipe != null && _pipe.IsConnected;

        public static void Initialize(bool enabled, string? customClientId = null)
        {
            _isEnabled = enabled;
            if (!string.IsNullOrWhiteSpace(customClientId))
            {
                _clientId = customClientId.Trim();
            }
            else
            {
                _clientId = DEFAULT_CLIENT_ID;
            }

            if (!_isEnabled || _isRunning) return;

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _launcherStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Task.Run(() => WorkerLoop(_cts.Token));
        }

        public static void SetEnabled(bool enabled, string? customClientId = null)
        {
            _isEnabled = enabled;
            if (!string.IsNullOrWhiteSpace(customClientId))
            {
                _clientId = customClientId.Trim();
            }

            if (!enabled)
            {
                Shutdown();
            }
            else
            {
                Initialize(true, customClientId);
            }
        }

        public static void SetTargetServer(string name, string address, string port = "19132")
        {
            _manualServerName = name;
            _manualServerIp = address;
            _manualServerPort = port;
            _manualServerSetTime = DateTime.UtcNow;
        }

        public static void ClearTargetServer()
        {
            _manualServerName = null;
            _manualServerIp = null;
            _manualServerPort = null;
        }

        public static void UpdatePresence(string details, string state, bool inGame = false)
        {
            _currentDetails = details;
            _currentState = state;
            _isInGame = inGame;

            if (inGame && _gameStartTimestamp == 0)
            {
                _gameStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            else if (!inGame)
            {
                _gameStartTimestamp = 0;
            }

            if (IsConnected)
            {
                _ = Task.Run(SendCurrentPresenceAsync);
            }
        }

        public static void Shutdown()
        {
            _isRunning = false;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch { }

            try
            {
                _pipe?.Dispose();
                _pipe = null;
            }
            catch { }

            _isInGame = false;
            _gameStartTimestamp = 0;
            _lastActiveWorldName = null;
        }

        private static async Task WorkerLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning && _isEnabled)
            {
                try
                {
                    // 1. Ensure Discord IPC connection
                    if (_pipe == null || !_pipe.IsConnected)
                    {
                        await TryConnectAsync(token);
                    }

                    // 2. Poll active game state and detect Player Name, Worlds, or Server
                    PollGameState();

                    // 3. Send current presence frame
                    if (_pipe != null && _pipe.IsConnected)
                    {
                        await SendCurrentPresenceAsync();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("DiscordRpc loop error: " + ex.Message);
                    try
                    {
                        _pipe?.Dispose();
                        _pipe = null;
                    }
                    catch { }
                }

                try
                {
                    await Task.Delay(3500, token);
                }
                catch
                {
                    break;
                }
            }
        }

        private static async Task TryConnectAsync(CancellationToken token)
        {
            for (int i = 0; i < 10; i++)
            {
                if (token.IsCancellationRequested || !_isRunning || !_isEnabled) return;
                try
                {
                    string pipeName = $"discord-ipc-{i}";
                    var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                    await pipe.ConnectAsync(1200, token);

                    if (pipe.IsConnected)
                    {
                        _pipe = pipe;

                        // Handshake: Opcode 0
                        var handshakeObj = new { v = 1, client_id = _clientId };
                        await WriteFrameAsync(0, JsonConvert.SerializeObject(handshakeObj));

                        // Read response: expect Opcode 1 with READY
                        var (op, responseJson) = await ReadFrameAsync();
                        if (op == 1 && !string.IsNullOrEmpty(responseJson))
                        {
                            Trace.WriteLine($"Discord RPC successfully connected on {pipeName}");
                            await SendCurrentPresenceAsync();
                            return;
                        }
                        else
                        {
                            pipe.Dispose();
                            _pipe = null;
                        }
                    }
                }
                catch
                {
                    // Pipe not available, continue to next
                }
            }
        }

        private static async Task SendCurrentPresenceAsync()
        {
            if (_pipe == null || !_pipe.IsConnected) return;

            try
            {
                var activity = new JObject
                {
                    ["name"] = "BUGrock launcher",
                    ["details"] = _currentDetails,
                    ["state"] = _currentState
                };

                // Elapsed Playtime timestamps: when in game, display game duration
                if (_isInGame && _gameStartTimestamp > 0)
                {
                    activity["timestamps"] = new JObject
                    {
                        ["start"] = _gameStartTimestamp
                    };
                }
                else
                {
                    activity["timestamps"] = new JObject
                    {
                        ["start"] = _launcherStartTimestamp
                    };
                }

                // Assets: Large Image + Small Image with custom tooltips
                var assets = new JObject
                {
                    ["large_image"] = !string.IsNullOrEmpty(_largeImage) ? _largeImage : DISCORD_ICON_URL,
                    ["large_text"] = !string.IsNullOrEmpty(_largeText) ? _largeText : "BUGrock Launcher"
                };

                if (!string.IsNullOrEmpty(_smallImage))
                {
                    assets["small_image"] = _smallImage;
                    if (!string.IsNullOrEmpty(_smallText))
                    {
                        assets["small_text"] = _smallText;
                    }
                }

                activity["assets"] = assets;

                var payload = new JObject
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["args"] = new JObject
                    {
                        ["pid"] = Process.GetCurrentProcess().Id,
                        ["activity"] = activity
                    },
                    ["nonce"] = Guid.NewGuid().ToString("N")
                };

                await WriteFrameAsync(1, payload.ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                Trace.WriteLine("SendCurrentPresence error: " + ex.Message);
            }
        }

        private static async Task WriteFrameAsync(int opcode, string json)
        {
            if (_pipe == null || !_pipe.IsConnected) return;

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[8];

            BitConverter.GetBytes(opcode).CopyTo(header, 0);
            BitConverter.GetBytes(jsonBytes.Length).CopyTo(header, 4);

            await _pipe.WriteAsync(header, 0, header.Length);
            await _pipe.WriteAsync(jsonBytes, 0, jsonBytes.Length);
            await _pipe.FlushAsync();
        }

        private static async Task<(int opcode, string json)> ReadFrameAsync()
        {
            if (_pipe == null || !_pipe.IsConnected) return (-1, string.Empty);

            byte[] header = new byte[8];
            int readHeader = await _pipe.ReadAsync(header, 0, 8);
            if (readHeader < 8) return (-1, string.Empty);

            int opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);

            if (length <= 0 || length > 65536) return (opcode, string.Empty);

            byte[] buffer = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await _pipe.ReadAsync(buffer, totalRead, length - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }

            string json = Encoding.UTF8.GetString(buffer, 0, totalRead);
            return (opcode, json);
        }

                #region Game State Detection (Player Name, Worlds, Server, Playtime)

        private static string? _lastActiveWorldName = null;
        private static DateTime _lastActiveWorldTime = DateTime.MinValue;
        private static long _lastActiveWorldTimestamp = 0;

        private static void PollGameState()
        {
            try
            {
                var mcProcs = Process.GetProcessesByName("Minecraft.Windows");
                bool isMcRunning = mcProcs.Length > 0;

                if (!isMcRunning)
                {
                    // Game is not running -> In Launcher
                    _isInGame = false;
                    _gameStartTimestamp = 0;
                    _lastActiveWorldName = null;
                    _lastActiveWorldTimestamp = 0;
                    _currentDetails = "Minecraft Bedrock";
                    _currentState = "In Launcher";
                    _largeImage = DISCORD_ICON_URL;
                    _largeText = "BUGrock Launcher";
                    _smallImage = null;
                    _smallText = null;
                    return;
                }

                // Game IS running -> track start time accurately
                _isInGame = true;
                if (_gameStartTimestamp == 0)
                {
                    try
                    {
                        _gameStartTimestamp = new DateTimeOffset(mcProcs[0].StartTime.ToUniversalTime()).ToUnixTimeSeconds();
                    }
                    catch
                    {
                        _gameStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    }
                }

                // 1. Detect Player Name (from options.txt or preferences)
                string playerName = DetectPlayerName();

                // 2. Check Singleplayer World
                var activeWorld = DetectActiveWorld();

                // 3. Check Multiplayer Server or Featured Experience
                var activeServer = DetectActiveServer();

                // If both seem active, compare timestamps:
                // If server join timestamp is newer than the last world write timestamp, user transitioned to server
                if (activeWorld.isInWorld && activeServer != null)
                {
                    if (activeServer.Timestamp > _lastActiveWorldTimestamp)
                    {
                        activeWorld = (false, string.Empty);
                    }
                }

                if (activeWorld.isInWorld)
                {
                    _currentDetails = "Minecraft Bedrock";
                    _currentState = string.IsNullOrWhiteSpace(activeWorld.worldName)
                        ? $"{playerName} • Playing Singleplayer World"
                        : $"{playerName} • Playing Singleplayer World: {activeWorld.worldName}";
                    _largeImage = DISCORD_ICON_URL;
                    _largeText = string.IsNullOrWhiteSpace(activeWorld.worldName)
                        ? "Singleplayer World"
                        : $"Singleplayer World: {activeWorld.worldName}";
                    _smallImage = "steve";
                    _smallText = $"Player: {playerName}";
                    return;
                }

                if (activeServer != null)
                {
                    _currentDetails = "Minecraft Bedrock";
                    string addrDisplay = "";
                    if (!string.IsNullOrWhiteSpace(activeServer.Address))
                    {
                        addrDisplay = activeServer.Address;
                        if (!string.IsNullOrEmpty(activeServer.Port) && activeServer.Port != "19132" && !addrDisplay.Contains(":"))
                        {
                            addrDisplay += $":{activeServer.Port}";
                        }
                    }

                    _currentState = !string.IsNullOrEmpty(addrDisplay)
                        ? $"{playerName} • Playing on {activeServer.Name} ({addrDisplay})"
                        : $"{playerName} • Playing on {activeServer.Name}";

                    _largeImage = DISCORD_ICON_URL;
                    _largeText = !string.IsNullOrEmpty(addrDisplay)
                        ? $"Server: {activeServer.Name} ({addrDisplay})"
                        : $"Server: {activeServer.Name}";
                    _smallImage = "steve";
                    _smallText = $"Player: {playerName}";
                    return;
                }

                // 4. Default / Fallback: In Main Menu
                _currentDetails = "Minecraft Bedrock";
                _currentState = $"{playerName} • In Main Menu";
                _largeImage = DISCORD_ICON_URL;
                _largeText = "Minecraft Bedrock (Menu)";
                _smallImage = "steve";
                _smallText = $"Player: {playerName}";
            }
            catch (Exception ex)
            {
                Trace.WriteLine("PollGameState error: " + ex.Message);
            }
        }

        public static string DetectPlayerName()
        {
            var candidates = new List<(string name, DateTime modified)>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 1. GDK Bedrock Users
            string gdkUsers = Path.Combine(appData, "Minecraft Bedrock", "Users");
            if (Directory.Exists(gdkUsers))
            {
                foreach (var userDir in Directory.GetDirectories(gdkUsers))
                {
                    string opt = Path.Combine(userDir, "games", "com.mojang", "minecraftpe", "options.txt");
                    CheckOptionsFile(opt, candidates);
                }
            }

            // 2. UWP Bedrock LocalState
            string uwpOpt = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftpe", "options.txt");
            CheckOptionsFile(uwpOpt, candidates);

            // 3. Instances folder
            string instancesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances");
            if (Directory.Exists(instancesFolder))
            {
                foreach (var inst in Directory.GetDirectories(instancesFolder))
                {
                    string instOpt = Path.Combine(inst, "data", "games", "com.mojang", "minecraftpe", "options.txt");
                    CheckOptionsFile(instOpt, candidates);
                }
            }

            // Select best: prefer non-generic player names (real Xbox Gamertag), then newest
            var best = candidates
                .OrderByDescending(c => !c.name.Equals("Player", StringComparison.OrdinalIgnoreCase) && !c.name.Equals("Steve", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.modified)
                .FirstOrDefault();

            return !string.IsNullOrWhiteSpace(best.name) ? best.name : "Player";
        }

        private static void CheckOptionsFile(string path, List<(string name, DateTime modified)> candidates)
        {
            if (!File.Exists(path)) return;
            try
            {
                DateTime mod = File.GetLastWriteTime(path);
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("mp_username:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line.Substring("mp_username:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            candidates.Add((val, mod));
                        }
                    }
                }
            }
            catch { }
        }

        private static (bool isInWorld, string worldName) DetectActiveWorld()
        {
            DateTime now = DateTime.Now;
            var worldDirs = WorldBackupHelper.FindAllWorldsDirectories();

            foreach (var wDir in worldDirs)
            {
                if (!Directory.Exists(wDir)) continue;
                try
                {
                    foreach (var sub in Directory.GetDirectories(wDir))
                    {
                        // Check if level.dat, db/CURRENT, db/LOG, or db/*.log was touched within the last 75 seconds
                        string levelDat = Path.Combine(sub, "level.dat");
                        string dbDir = Path.Combine(sub, "db");

                        bool recentlyModified = false;
                        if (File.Exists(levelDat) && (now - File.GetLastWriteTime(levelDat)).TotalSeconds < 75)
                        {
                            recentlyModified = true;
                        }
                        else if (Directory.Exists(dbDir))
                        {
                            string currentFile = Path.Combine(dbDir, "CURRENT");
                            string logFile = Path.Combine(dbDir, "LOG");
                            if ((File.Exists(currentFile) && (now - File.GetLastWriteTime(currentFile)).TotalSeconds < 75) ||
                                (File.Exists(logFile) && (now - File.GetLastWriteTime(logFile)).TotalSeconds < 75))
                            {
                                recentlyModified = true;
                            }
                            else
                            {
                                foreach (var logFileItem in Directory.GetFiles(dbDir, "*.log"))
                                {
                                    if ((now - File.GetLastWriteTime(logFileItem)).TotalSeconds < 75)
                                    {
                                        recentlyModified = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (recentlyModified)
                        {
                            string wName = WorldBackupHelper.GetWorldFriendlyName(sub);
                            _lastActiveWorldName = wName;
                            _lastActiveWorldTime = now;
                            _lastActiveWorldTimestamp = new DateTimeOffset(now).ToUnixTimeSeconds();
                            return (true, wName);
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(_lastActiveWorldName) && (now - _lastActiveWorldTime).TotalSeconds < 60)
            {
                return (true, _lastActiveWorldName);
            }

            return (false, string.Empty);
        }

        private static ServerPresenceInfo? DetectActiveServer()
        {
            // 1. If user recently triggered a server launch from the launcher
            if (!string.IsNullOrEmpty(_manualServerName) && !string.IsNullOrEmpty(_manualServerIp))
            {
                if ((DateTime.UtcNow - _manualServerSetTime).TotalMinutes < 60)
                {
                    return new ServerPresenceInfo
                    {
                        Name = _manualServerName,
                        Address = _manualServerIp,
                        Port = _manualServerPort ?? "19132",
                        Timestamp = new DateTimeOffset(_manualServerSetTime).ToUnixTimeSeconds()
                    };
                }
            }

            // 2. Collect server and experience candidates across all game locations
            var candidates = new List<ServerPresenceInfo>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var mojangRoots = new List<string>();

            // GDK Bedrock Users
            string gdkUsers = Path.Combine(appData, "Minecraft Bedrock", "Users");
            if (Directory.Exists(gdkUsers))
            {
                foreach (var userDir in Directory.GetDirectories(gdkUsers))
                {
                    string peDir = Path.Combine(userDir, "games", "com.mojang", "minecraftpe");
                    if (Directory.Exists(peDir)) mojangRoots.Add(peDir);
                }
            }

            // UWP LocalState
            string uwpPe = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftpe");
            if (Directory.Exists(uwpPe)) mojangRoots.Add(uwpPe);

            // Custom instances
            string instancesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances");
            if (Directory.Exists(instancesFolder))
            {
                foreach (var inst in Directory.GetDirectories(instancesFolder))
                {
                    string peDir = Path.Combine(inst, "data", "games", "com.mojang", "minecraftpe");
                    if (Directory.Exists(peDir)) mojangRoots.Add(peDir);
                }
            }

            foreach (var peDir in mojangRoots)
            {
                // A. Inspect recentlyPlayed/*.json (Featured Partner Servers & Marketplace Experiences)
                string recentlyPlayedDir = Path.Combine(peDir, "recentlyPlayed");
                if (Directory.Exists(recentlyPlayedDir))
                {
                    try
                    {
                        foreach (var jsonFile in Directory.GetFiles(recentlyPlayedDir, "*.json"))
                        {
                            try
                            {
                                string content = File.ReadAllText(jsonFile);
                                var obj = JObject.Parse(content);
                                var entries = obj["entries"] as JArray;
                                if (entries != null)
                                {
                                    foreach (var entry in entries)
                                    {
                                        string targetType = entry["target"]?["type"]?.ToString() ?? "";
                                        if (targetType.Equals("LocalWorld", StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }

                                        string displayName = entry["displayName"]?.ToString() ?? "";
                                        long ts = entry["lastSuccessfulJoinTimeEpochSeconds"]?.Value<long>() ?? 0;

                                        if (!string.IsNullOrWhiteSpace(displayName) && ts > 0)
                                        {
                                            var (cleanName, address) = ResolveServerInfo(displayName);
                                            candidates.Add(new ServerPresenceInfo
                                            {
                                                Name = cleanName,
                                                Address = address,
                                                Port = "19132",
                                                Timestamp = ts
                                            });
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // B. Inspect external_servers.txt (Custom multiplayer servers added by user)
                string extFile = Path.Combine(peDir, "external_servers.txt");
                if (File.Exists(extFile))
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(extFile))
                        {
                            var parts = line.Split(':');
                            if (parts.Length >= 4)
                            {
                                long ts = 0;
                                if (parts.Length >= 5) long.TryParse(parts[4], out ts);
                                candidates.Add(new ServerPresenceInfo
                                {
                                    Name = parts[1].Trim(),
                                    Address = parts[2].Trim(),
                                    Port = parts[3].Trim(),
                                    Timestamp = ts
                                });
                            }
                        }
                    }
                    catch { }
                }
            }

            // 3. Filter candidates to current game session
            if (candidates.Count > 0 && _gameStartTimestamp > 0)
            {
                var sessionServer = candidates
                    .Where(s => s.Timestamp > 0 && s.Timestamp >= (_gameStartTimestamp - 120))
                    .OrderByDescending(s => s.Timestamp)
                    .FirstOrDefault();

                if (sessionServer != null)
                {
                    return sessionServer;
                }
            }

            // Strictly return null if not connected to a verified server.
            // NEVER guess or inspect random OS TCP connections to prevent IP leaks!
            return null;
        }

        private static (string name, string address) ResolveServerInfo(string rawName)
        {
            string lower = rawName.Trim().ToLowerInvariant();
            if (lower.Contains("cubecraft"))
            {
                return ("CubeCraft", "play.cubecraft.net");
            }
            if (lower.Contains("hive"))
            {
                return ("The Hive", "geo.hivebedrock.network");
            }
            if (lower.Contains("lifeboat"))
            {
                return ("Lifeboat", "play.lbsg.net");
            }
            if (lower.Contains("mineville") || lower.Contains("inpvp"))
            {
                return ("Mineville", "play.inpvp.net");
            }
            if (lower.Contains("galaxite"))
            {
                return ("Galaxite", "play.galaxite.net");
            }
            if (lower.Contains("pixel paradise"))
            {
                return ("Pixel Paradise", "play.pixelparadise.gg");
            }

            return (rawName.Trim(), "");
        }

        #endregion
    }
}

