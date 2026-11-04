using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
        // Official, verified Minecraft application ID with 150+ uploaded Rich Presence assets (overworld, steve, minecraft_server, bedrock, etc.)
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

            _gameStartTimestamp = 0;
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

                    // Allow generous 1200ms timeout for Discord to accept IPC connection
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
                            // Handshake failed or rejected by Discord
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
                    _currentDetails = "Minecraft Bedrock";
                    _currentState = "In Launcher";
                    _largeImage = DISCORD_ICON_URL;
                    _largeText = "BUGrock Launcher";
                    _smallImage = null;
                    _smallText = null;
                    return;
                }

                // Game IS running -> track start time
                _isInGame = true;
                if (_gameStartTimestamp == 0)
                {
                    _gameStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }

                // 1. Detect Player Name (from options.txt or preferences)
                string playerName = DetectPlayerName();

                // 2. Check if player is currently in a local World
                var activeWorld = DetectActiveWorld();
                if (activeWorld.isInWorld)
                {
                    // Playing in Worlds
                    _currentDetails = "Minecraft Bedrock";
                    _currentState = string.IsNullOrWhiteSpace(activeWorld.worldName)
                        ? $"Playing in Worlds • {playerName}"
                        : $"Playing in Worlds: {activeWorld.worldName} • {playerName}";
                    _largeImage = DISCORD_ICON_URL;
                    _largeText = string.IsNullOrWhiteSpace(activeWorld.worldName)
                        ? "Minecraft Bedrock (Worlds)"
                        : $"Worlds: {activeWorld.worldName}";
                    _smallImage = "steve";
                    _smallText = $"Player: {playerName}";
                    return;
                }

                // 3. Check if player is connected to a Server
                var activeServer = DetectActiveServer(mcProcs[0]);
                if (activeServer != null)
                {
                    // Playing on Multiplayer Server
                    _currentDetails = "Minecraft Bedrock";
                    string portSuffix = (activeServer.Port == "19132" || string.IsNullOrEmpty(activeServer.Port)) ? "" : $":{activeServer.Port}";
                    _currentState = $"Playing on {activeServer.Name} ({activeServer.Address}{portSuffix}) • {playerName}";
                    _largeImage = DISCORD_ICON_URL;
                    _largeText = $"Server: {activeServer.Name}";
                    _smallImage = "steve";
                    _smallText = $"Player: {playerName}";
                    return;
                }

                // 4. Fallback: In Main Menu
                _currentDetails = "Minecraft Bedrock";
                _currentState = $"In Main Menu • Player: {playerName}";
                _largeImage = DISCORD_ICON_URL;
                _largeText = "Minecraft Bedrock";
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
                        // Check if level.dat, db/CURRENT, or db/*.log was touched within the last 45 seconds
                        string levelDat = Path.Combine(sub, "level.dat");
                        string dbDir = Path.Combine(sub, "db");

                        bool recentlyModified = false;
                        if (File.Exists(levelDat) && (now - File.GetLastWriteTime(levelDat)).TotalSeconds < 45)
                        {
                            recentlyModified = true;
                        }
                        else if (Directory.Exists(dbDir))
                        {
                            string currentFile = Path.Combine(dbDir, "CURRENT");
                            if (File.Exists(currentFile) && (now - File.GetLastWriteTime(currentFile)).TotalSeconds < 45)
                            {
                                recentlyModified = true;
                            }
                            else
                            {
                                foreach (var logFile in Directory.GetFiles(dbDir, "*.log"))
                                {
                                    if ((now - File.GetLastWriteTime(logFile)).TotalSeconds < 45)
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
                            return (true, wName);
                        }
                    }
                }
                catch { }
            }

            return (false, string.Empty);
        }

        private static ServerPresenceInfo? DetectActiveServer(Process mcProc)
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
                        Port = _manualServerPort ?? "19132"
                    };
                }
            }

            // 2. Read external_servers.txt to inspect known servers
            var servers = GetAllSavedServers();

            // 3. Check for recently added or joined server in external_servers.txt
            if (servers.Count > 0)
            {
                long currentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var recent = servers.FirstOrDefault(s => s.Timestamp > 0 && Math.Abs(currentUnix - s.Timestamp) < 1800);
                if (recent != null)
                {
                    return recent;
                }
            }

            // 4. Featured Partner Bedrock Servers (The Hive, CubeCraft, Lifeboat, etc.)
            var featuredServers = new List<ServerPresenceInfo>
            {
                new ServerPresenceInfo { Name = "The Hive", Address = "geo.hivebedrock.network", Port = "19132" },
                new ServerPresenceInfo { Name = "CubeCraft", Address = "play.cubecraft.net", Port = "19132" },
                new ServerPresenceInfo { Name = "Lifeboat", Address = "play.lbsg.net", Port = "19132" },
                new ServerPresenceInfo { Name = "Mineville", Address = "play.inpvp.net", Port = "19132" },
                new ServerPresenceInfo { Name = "Galaxite", Address = "play.galaxite.net", Port = "19132" }
            };

            // 5. Inspect active TCP connections for Minecraft.Windows.exe
            try
            {
                var activeTcp = GetActiveTcpRemoteEndpoints(mcProc.Id);
                foreach (var ep in activeTcp)
                {
                    string ipStr = ep.Address.ToString();
                    if (IPAddress.IsLoopback(ep.Address) || ipStr.StartsWith("10.") || ipStr.StartsWith("192.168.")) continue;

                    // Match against saved servers or featured servers
                    var match = servers.Concat(featuredServers).FirstOrDefault(s =>
                        s.Address.Equals(ipStr, StringComparison.OrdinalIgnoreCase));

                    if (match != null) return match;

                    return new ServerPresenceInfo
                    {
                        Name = "Multiplayer Server",
                        Address = ipStr,
                        Port = ep.Port.ToString()
                    };
                }
            }
            catch { }

            // 6. If servers exist and Minecraft process has network listeners open, use the latest saved server
            if (servers.Count > 0)
            {
                var latest = servers.OrderByDescending(s => s.Timestamp).First();
                // If it was modified during the game session
                if (latest.Timestamp > 0 && latest.Timestamp >= (_gameStartTimestamp - 60))
                {
                    return latest;
                }
            }

            return null;
        }

        private static List<ServerPresenceInfo> GetAllSavedServers()
        {
            var servers = new List<ServerPresenceInfo>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var paths = new List<string>();
            string gdkUsers = Path.Combine(appData, "Minecraft Bedrock", "Users");
            if (Directory.Exists(gdkUsers))
            {
                foreach (var userDir in Directory.GetDirectories(gdkUsers))
                {
                    string f = Path.Combine(userDir, "games", "com.mojang", "minecraftpe", "external_servers.txt");
                    if (File.Exists(f)) paths.Add(f);
                }
            }
            string uwpServers = Path.Combine(localAppData, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftpe", "external_servers.txt");
            if (File.Exists(uwpServers)) paths.Add(uwpServers);

            foreach (var p in paths)
            {
                try
                {
                    foreach (var line in File.ReadAllLines(p))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 4)
                        {
                            long ts = 0;
                            if (parts.Length >= 5) long.TryParse(parts[4], out ts);
                            servers.Add(new ServerPresenceInfo
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

            return servers.OrderByDescending(s => s.Timestamp).ToList();
        }

        private static List<IPEndPoint> GetActiveTcpRemoteEndpoints(int pid)
        {
            var results = new List<IPEndPoint>();
            try
            {
                var properties = IPGlobalProperties.GetIPGlobalProperties();
                var connections = properties.GetActiveTcpConnections();
                // In standard .NET, GetActiveTcpConnections gives endpoint pairs
                foreach (var conn in connections)
                {
                    if (conn.State == TcpState.Established && !IPAddress.IsLoopback(conn.RemoteEndPoint.Address))
                    {
                        results.Add(conn.RemoteEndPoint);
                    }
                }
            }
            catch { }
            return results;
        }

        #endregion
    }
}
