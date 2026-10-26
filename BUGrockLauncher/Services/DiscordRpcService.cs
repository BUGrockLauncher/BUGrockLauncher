using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BUGrockLauncher.Services
{
    public static class DiscordRpcService
    {
        private const string CLIENT_ID = "1344400593444634735";
        private static NamedPipeClientStream? _pipe;
        private static bool _isRunning = false;
        private static bool _isEnabled = true;
        private static CancellationTokenSource? _cts;
        private static long _startTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static string _currentDetails = "In Launcher";
        private static string _currentState = "Browsing Bedrock Versions";
        private static bool _isInGame = false;

        public static bool IsConnected => _pipe != null && _pipe.IsConnected;

        public static void Initialize(bool enabled)
        {
            _isEnabled = enabled;
            if (!_isEnabled || _isRunning) return;

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _startTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Task.Run(() => WorkerLoop(_cts.Token));
        }

        public static void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (!enabled)
            {
                Shutdown();
            }
            else
            {
                Initialize(true);
            }
        }

        public static void UpdatePresence(string details, string state, bool inGame = false)
        {
            _currentDetails = details;
            _currentState = state;
            _isInGame = inGame;

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
        }

        private static async Task WorkerLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning && _isEnabled)
            {
                try
                {
                    if (_pipe == null || !_pipe.IsConnected)
                    {
                        await TryConnectAsync(token);
                    }
                    else
                    {
                        await SendCurrentPresenceAsync();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("DiscordRpc loop info: " + ex.Message);
                    try
                    {
                        _pipe?.Dispose();
                        _pipe = null;
                    }
                    catch { }
                }

                try
                {
                    await Task.Delay(15000, token);
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
                    await pipe.ConnectAsync(100, token);

                    if (pipe.IsConnected)
                    {
                        _pipe = pipe;
                        // Send Handshake (Opcode 0)
                        var handshakeObj = new { v = 1, client_id = CLIENT_ID };
                        await WriteFrameAsync(0, JsonConvert.SerializeObject(handshakeObj));

                        // Read response
                        var (op, responseJson) = await ReadFrameAsync();
                        Trace.WriteLine($"Discord RPC Connected to {pipeName}");

                        await SendCurrentPresenceAsync();
                        return;
                    }
                }
                catch
                {
                    // Pipe not available, try next
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
                    ["details"] = _currentDetails,
                    ["state"] = _currentState,
                    ["timestamps"] = new JObject
                    {
                        ["start"] = _startTimestamp
                    },
                    ["assets"] = new JObject
                    {
                        ["large_image"] = "bugrock_logo",
                        ["large_text"] = "BUGrock Launcher",
                        ["small_image"] = _isInGame ? "minecraft" : "bedrock",
                        ["small_text"] = _isInGame ? "Playing Minecraft Bedrock" : "BUGrock Launcher"
                    }
                };

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
    }
}
