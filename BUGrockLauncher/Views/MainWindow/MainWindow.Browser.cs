using BUGrockLauncher.Helpers;



using System;



using System.Diagnostics;



using System.IO;



using System.Net.NetworkInformation;



using System.Threading.Tasks;



using System.Windows;



using System.Windows.Input;



using System.Windows.Media;



using Microsoft.Web.WebView2.Core;



using Newtonsoft.Json.Linq;







namespace BUGrockLauncher {



    public partial class MainWindow {



        private DateTime _lastMinecraftLaunchTime = DateTime.MinValue;



        private void LaunchMinecraftUri(string? uri) {



            if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) return;



            if ((DateTime.Now - _lastMinecraftLaunchTime).TotalSeconds < 2) return;



            _lastMinecraftLaunchTime = DateTime.Now;







            Dispatcher.Invoke(() => {



                try {



                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });



                } catch (Exception ex) {



                    Trace.WriteLine("Failed to launch Minecraft protocol: " + ex.Message);



                    ThemedMessageBox.Show("Could not launch Minecraft Bedrock Edition.\nPlease ensure Minecraft Bedrock is installed.", "Minecraft Launch Error");



                }



            });



        }







        private string? _lastDownloadedFilePath;



        private string? _lastDownloadedFolder;



        private string _currentInAppPlatform = "MCPEDL";







        private void UpdateServersZoom() {



            try {



                if (ServersWebView?.CoreWebView2 == null) return;



                double w = ServersWebView.ActualWidth;



                if (w <= 0) return;



                if (w < 400) ServersWebView.ZoomFactor = 0.65;



                else if (w < 500) ServersWebView.ZoomFactor = 0.72;



                else if (w < 650) ServersWebView.ZoomFactor = 0.80;



                else ServersWebView.ZoomFactor = 0.90;



            } catch { }



        }







        private void UpdateBrowserZoom() {



            try {



                if (BrowserWebView?.CoreWebView2 == null) return;



                double w = BrowserWebView.ActualWidth;



                if (w <= 0) return;



                if (w < 1100) BrowserWebView.ZoomFactor = 0.75;



                else if (w < 1350) BrowserWebView.ZoomFactor = 0.82;



                else if (w < 1600) BrowserWebView.ZoomFactor = 0.90;



                else BrowserWebView.ZoomFactor = 1.0;



            } catch { }



        }











        private TaskCompletionSource<bool> _serversReadyTcs = new TaskCompletionSource<bool>();

        private bool _serversInitialNavStarted = false;

        private bool _isServersFirstLoadCompleted = false;







        public async Task WaitForServersReadyAsync(SplashScreenWindow splash) {

            try {

                splash.SetStatus("Connecting to community servers...");

                if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) {

                    Dispatcher.Invoke(() => {

                        if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;

                        if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Visible;

                    });

                    return;

                }



                // 1. Wait briefly for webview initial navigation to start/complete without blocking on hidden window

                await Task.WhenAny(_serversReadyTcs.Task, Task.Delay(2500));



                // 2. Keep ServersWebView parked off-screen behind the spinning cube overlay until RevealServersAsync() confirms video playback on MainWindow

                Dispatcher.Invoke(() => {

                    if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Collapsed;

                    if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Visible;

                    if (ServersWebView != null) {

                        if (!_serversRevealed) {

                            ServersWebView.Margin = new Thickness(-20000, 0, 20000, 0);

                        }

                        ServersWebView.Visibility = Visibility.Visible;

                    }

                });



                splash.SetStatus("Ready!");

                await Task.Delay(150);

            } catch { }

        }







        private static CoreWebView2Environment? _sharedEnvironment;







        private static async Task<CoreWebView2Environment> GetSharedWebViewEnvironmentAsync() {



            if (_sharedEnvironment != null) return _sharedEnvironment;



            // Unified standard environment for both Servers List and In-App Browser (MCPEDL / CurseForge)



            // Eliminates conflict 0x8007139F / black screen while disabling unused telemetry/features



            var options = new CoreWebView2EnvironmentOptions(

                "--disable-features=Translate,MediaRouter,OptimizationHints " +

                "--enable-features=VaapiVideoDecoder,CanvasOopRasterization " +

                "--enable-gpu-rasterization " +

                "--enable-zero-copy " +

                "--max-connections-per-host=32 " +

                "--enable-quic " +

                "--autoplay-policy=no-user-gesture-required " +

                "--disable-backgrounding-occluded-windows " +

                "--disable-renderer-backgrounding " +

                "--disable-background-timer-throttling " +

                "--disable-background-media-suspend"

            );



            _sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, null, options);



            return _sharedEnvironment;



        }









        private static readonly byte[] Transparent1x1Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");



        private static bool IsHeavyGifOrBannerUri(string uri) {

            if (string.IsNullOrEmpty(uri)) return false;

            return uri.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||

                   uri.Contains(".gif?", StringComparison.OrdinalIgnoreCase) ||

                   uri.Contains(".gif&", StringComparison.OrdinalIgnoreCase) ||

                   uri.Contains("/banner", StringComparison.OrdinalIgnoreCase) ||

                   uri.Contains("banner.", StringComparison.OrdinalIgnoreCase) ||

                   uri.Contains("banner_", StringComparison.OrdinalIgnoreCase);

        }



        private void SetupWebResourceFilter(CoreWebView2? core) {

            if (core == null) return;

            if (UserPrefs.WebPerformanceMode != "Low") return;

            try {

                core.AddWebResourceRequestedFilter("*.gif*", CoreWebView2WebResourceContext.Image);

                core.AddWebResourceRequestedFilter("*banner*", CoreWebView2WebResourceContext.Image);

                core.WebResourceRequested += (sender, args) => {

                    try {

                        if (UserPrefs.WebPerformanceMode == "Low") {

                            var ms = new MemoryStream(Transparent1x1Png);

                            args.Response = ((CoreWebView2)sender).Environment.CreateWebResourceResponse(

                                ms,

                                200,

                                "OK",

                                "Content-Type: image/png\r\nContent-Length: 68\r\nCache-Control: max-age=86400\r\n"

                            );

                        }

                    } catch { }

                };

            } catch { }

        }



        public static string GetPerformanceScript(string mode) {

            string css;

            if (mode == "Low") {

                css = @"

                    img[src*='.gif'], img[src*='banner'], .server-banner img, .server-banner-wrap img, .server-banner, .server-banner-wrap {

                        display: none !important;

                        visibility: hidden !important;

                        height: 0 !important;

                        width: 0 !important;

                    }

                    *, *::before, *::after {

                        animation: none !important;

                        transition: none !important;

                        scroll-behavior: auto !important;

                    }

                ";

            } else if (mode == "Medium") {

                css = @"

                    .server-banner-wrap video, .server-banner video, video.mp4-fluid, video[src*='banner'],

                    .server-banner-wrap img, .server-banner img, .card img, img.server-banner,

                    img[src*='banner'], img[src*='.gif'] {

                        display: block !important;

                        visibility: visible !important;

                        opacity: 1 !important;

                        max-width: 100% !important;

                        height: auto !important;

                    }

                ";

            } else { // High - Lightning Fast Rocket Rendering

                css = @"

                    .server-banner-wrap video, .server-banner video, video.mp4-fluid, video[src*='banner'],

                    .server-banner-wrap img, .server-banner img, .card img, img.server-banner,

                    img[src*='banner'], img[src*='.gif'] {

                        display: block !important;

                        visibility: visible !important;

                        opacity: 1 !important;

                        max-width: 100% !important;

                        height: auto !important;

                        transform: translateZ(0) !important;

                        will-change: transform !important;

                        backface-visibility: hidden !important;

                        image-rendering: -webkit-optimize-contrast !important;

                    }

                    .card, .server-card, .list-group-item {

                        contain: content !important;

                        transform: translateZ(0) !important;

                    }

                    * {

                        -webkit-font-smoothing: antialiased !important;

                    }

                ";

            }



            return $@"(function() {{

                try {{

                    var s = document.getElementById('bugrock-perf-style');

                    if (!s) {{

                        s = document.createElement('style');

                        s.id = 'bugrock-perf-style';

                        (document.head || document.documentElement).appendChild(s);

                    }}

                    s.innerHTML = `{css}`;

                }} catch(e) {{}}

            }})();";

        }



        public async Task ApplyPerformanceScriptAsync(CoreWebView2? core, string mode) {

            if (core == null) return;

            try {

                await core.ExecuteScriptAsync(GetPerformanceScript(mode));

            } catch { }

        }



        public async Task ClearWebViewCacheAsync(CoreWebView2? core) {

            if (core?.Profile == null) return;

            try {

                await core.Profile.ClearBrowsingDataAsync(

                    CoreWebView2BrowsingDataKinds.DiskCache |

                    CoreWebView2BrowsingDataKinds.CacheStorage

                );

            } catch { }

        }





        public async Task InitializeWebViewsAsync() {



            try {



                DDFWManager.EnsureDirectories();







                // 1. Initialize Servers WebView



                if (ServersWebView != null) {



                    try {



                        ServersWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(18, 19, 26);



                    } catch { }







                    ServersWebView.MouseEnter += (s, e) => ResetHeadToCenter(animated: true);



                    var env = await GetSharedWebViewEnvironmentAsync();



                    await ServersWebView.EnsureCoreWebView2Async(env);



                    if (ServersWebView.CoreWebView2 != null) {



                        try {

                            ServersWebView.CoreWebView2.MemoryUsageTargetLevel = (UserPrefs.WebPerformanceMode == "Low")

                                ? CoreWebView2MemoryUsageTargetLevel.Low

                                : CoreWebView2MemoryUsageTargetLevel.Normal;

                        } catch { }

                        SetupWebResourceFilter(ServersWebView.CoreWebView2);



                        ServersWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;



                        ServersWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;



                        ServersWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;







                        ServersWebView.CoreWebView2.NewWindowRequested += (s, e) => {



                            e.Handled = true;



                            string uri = (e.Uri ?? "").ToLowerInvariant();



                            if (uri.StartsWith("minecraft:")) {



                                LaunchMinecraftUri(e.Uri);



                                return;



                            }



                            if (uri.Contains("minecraftpocket-servers.com") || uri.Contains("cloudflare.com")) {



                                ServersWebView.CoreWebView2.Navigate(e.Uri);



                            }



                        };







                        ServersWebView.CoreWebView2.WebMessageReceived += (s, e) => {



                            try {



                                if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var srcUri) ||



                                    (srcUri.Scheme != Uri.UriSchemeHttp && srcUri.Scheme != Uri.UriSchemeHttps)) {



                                    return;



                                }



                                string msg = e.TryGetWebMessageAsString();



                                if (msg == "VIDEO_PLAYING_CONFIRMED" || msg == "SERVERS_VISUALLY_READY" || msg == "CONTENT_READY" || msg == "SERVERS_FRAME_PAINTED" || msg == "SERVERS_DOM_READY") {

                                    _ = RevealServersAsync();

                                    return;

                                }



                                if (!string.IsNullOrEmpty(msg) && msg.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) {



                                    LaunchMinecraftUri(msg);



                                }



                            } catch { }



                        };







                        ServersWebView.SizeChanged += (s, e) => UpdateServersZoom();







                        ServersWebView.NavigationStarting += (s, e) => {



                            string navUri = (e.Uri ?? "").ToLowerInvariant();



                            if (string.IsNullOrWhiteSpace(navUri) || navUri == "about:blank") return;







                            if (navUri.StartsWith("minecraft:")) {



                                e.Cancel = true;



                                LaunchMinecraftUri(e.Uri);



                                Dispatcher.Invoke(() => {

                                    if (_serversRevealed) {

                                        if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;

                                        if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Collapsed;

                                        ServersWebView.Margin = new Thickness(0);

                                        ServersWebView.Visibility = Visibility.Visible;

                                    }

                                });



                                return;



                            }







                            if (navUri.Contains("minecraftpocket-servers.com") || navUri.Contains("cloudflare.com")) {

                                Dispatcher.Invoke(() => {

                                    ParkServersWebViewForLoading();

                                });



                                return;



                            }







                            // Block external navigation outside servers domain silently without popup



                            e.Cancel = true;



                            Dispatcher.Invoke(() => {

                                if (_serversRevealed) {

                                    if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;

                                    if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Collapsed;

                                    ServersWebView.Margin = new Thickness(0);

                                    ServersWebView.Visibility = Visibility.Visible;

                                }

                            });



                        };







                        ServersWebView.NavigationCompleted += (s, e) => {



                            Dispatcher.Invoke(() => {



                                UpdateServersZoom();







                                // If navigation was intentionally cancelled (e.g. minecraft: link or blocked external url),



                                // it is NOT a connection failure; keep current view state and do not show No Internet overlay.



                                if (e.WebErrorStatus == Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.OperationCanceled) {

                                    if (_serversRevealed) {

                                        if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;

                                        if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Collapsed;

                                        ServersWebView.Margin = new Thickness(0);

                                        ServersWebView.Visibility = Visibility.Visible;

                                    }

                                    _serversReadyTcs.TrySetResult(true);



                                    return;



                                }







                                if (!e.IsSuccess) {



                                    ServersWebView.Visibility = Visibility.Collapsed;



                                    if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;



                                    if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Visible;



                                } else {



                                    if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Collapsed;



                                    try {

                                        ServersWebView.CoreWebView2?.ExecuteScriptAsync(ANTI_COOKIE_AND_POPUP_SCRIPT);

                                        ServersWebView.CoreWebView2?.ExecuteScriptAsync(SERVERS_VIEW_POLISH_SCRIPT);

                                        _ = ApplyPerformanceScriptAsync(ServersWebView.CoreWebView2, UserPrefs.WebPerformanceMode);

                                    } catch { }



                                    // Rely exclusively on VIDEO_PLAYING_CONFIRMED from in-page observer script



                                }



                                _isServersFirstLoadCompleted = true;

                                _serversReadyTcs.TrySetResult(true);



                            });



                        };







                        await ServersWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ANTI_COOKIE_AND_POPUP_SCRIPT);



                        await ServersWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(SERVERS_VIEW_POLISH_SCRIPT);









                        ParkServersWebViewForLoading();

                        _serversInitialNavStarted = true;

                        ServersWebView.CoreWebView2.Navigate(AppConfig.ServersHubUrl);



                    }



                }



            } catch (Exception ex) {



                Trace.WriteLine("Failed to initialize ServersWebView: " + ex.Message);



                Dispatcher.Invoke(() => {



                    if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;



                    if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Visible;



                    _serversReadyTcs.TrySetResult(false);



                });



            }



        }







        /* ===================================================================================================================

           CRITICAL ARCHITECTURAL WARNING - DO NOT TOUCH OR MODIFY!

           # STRICTLY FORBIDDEN FOR ANY AI AGENT OR DEVELOPER TO MODIFY, TOUCH, REFACTOR, OR ALTER THIS LOGIC!

           This component (ServersLoadingOverlay, off-screen airspace parking, and frame synchronization)

           was meticulously engineered and fine-tuned to eliminate all WebView2 airspace glitches, white flashes,

           and video layout jumps. NO ONE KNOWS EXACTLY HOW ALL THE PIECES BALANCE TOGETHER, BUT IT WORKS FLAWLESSLY.

           LEAVE IT EXACTLY AS-IS!

           =================================================================================================================== */

        private bool _serversRevealed = false;

        private bool _serversRevealing = false;

        private int _serversRevealGeneration = 0;



        private void ParkServersWebViewForLoading() {

            _serversRevealed = false;

            _serversRevealing = false;

            _serversRevealGeneration++;

            if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Collapsed;

            if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Visible;

            if (ServersWebView != null) {

                ServersWebView.Margin = new Thickness(-20000, 0, 20000, 0);

                ServersWebView.Visibility = Visibility.Visible;

            }

        }



        private async Task RevealServersAsync() {

            if (_serversRevealed || _serversRevealing) return;

            _serversRevealing = true;

            int gen = _serversRevealGeneration;

            try {

                // 1. Ensure MainWindow is actually loaded and visible on screen

                for (int w = 0; w < 120 && (!IsLoaded || !IsVisible); w++) {

                    if (gen != _serversRevealGeneration) return;

                    await Task.Delay(50);

                }



                // 2. Ensure ServersWebView has been measured & laid out by WPF layout pass

                for (int w = 0; w < 80 && (ServersWebView == null || ServersWebView.ActualWidth < 100); w++) {

                    if (gen != _serversRevealGeneration) return;

                    await Task.Delay(50);

                }



                // 3. Apply the exact ZoomFactor while still covered by the solid overlay

                Dispatcher.Invoke(() => UpdateServersZoom());



                // 4. Actively verify in WebView2 (at final window size & zoom) that the video banner is actively playing frames

                if (ServersWebView?.CoreWebView2 != null && UserPrefs.WebPerformanceMode != "Low") {

                    for (int i = 0; i < 120; i++) {

                        if (gen != _serversRevealGeneration) return;

                        try {

                            string status = await ServersWebView.CoreWebView2.ExecuteScriptAsync(@"

                                (function() {

                                    if (document.readyState === 'loading') return 'waiting_dom';

                                    var isDetail = window.location.pathname.indexOf('/server/') !== -1;

                                    var table = document.querySelector('table.servers-table');

                                    if (!isDetail && (!table || document.querySelectorAll('.servers-table tbody tr').length < 1)) {

                                        return 'waiting_table';

                                    }

                                    var videos = Array.from(document.querySelectorAll('.server-banner-wrap video, .server-card video, .servers-table video, video.mp4-fluid, video[src*=""banner""], video[data-src*=""banner""], video[src*="".mp4""], video[data-src*="".mp4""], .content video'));

                                    if (videos.length > 0) {

                                        var topVideos = videos.slice(0, isDetail ? 1 : 2);

                                        for (var k = 0; k < topVideos.length; k++) {

                                            var v = topVideos[k];

                                            var dSrc = v.getAttribute('data-src') || v.getAttribute('data-lazy-src');

                                            if (dSrc && !v.getAttribute('src')) {

                                                v.setAttribute('src', dSrc);

                                                v.load();

                                            }

                                            var sources = v.querySelectorAll('source[data-src]');

                                            var srcLoaded = false;

                                            for (var s = 0; s < sources.length; s++) {

                                                var sSrc = sources[s].getAttribute('data-src');

                                                if (sSrc && !sources[s].getAttribute('src')) {

                                                    sources[s].setAttribute('src', sSrc);

                                                    srcLoaded = true;

                                                }

                                            }

                                            if (srcLoaded) v.load();

                                            v.muted = true;

                                            v.playsInline = true;

                                            v.autoplay = true;

                                            v.preload = 'auto';

                                            if (v.paused && !v.ended && !v.error) {

                                                try {

                                                    var p = v.play();

                                                    if (p && p.catch) p.catch(function(){});

                                                } catch(e) {}

                                            }

                                        }

                                        var firstVid = topVideos[0];

                                        if (firstVid && !firstVid.error) {

                                            var playing = !firstVid.paused && !firstVid.ended && firstVid.readyState >= 2 && firstVid.videoWidth > 0 && firstVid.currentTime > 0;

                                            if (!playing) return 'waiting_video';

                                            return 'video_playing';

                                        }

                                    }

                                    if (document.readyState !== 'complete') return 'waiting_complete';

                                    var imgs = Array.from(document.querySelectorAll('.server-banner-wrap img, .server-card img[src*=""banner""], img.server-banner'));

                                    if (imgs.length > 0) {

                                        var firstImg = imgs[0];

                                        if (firstImg.complete && firstImg.naturalWidth > 0) return 'image_ready';

                                        return 'waiting_image';

                                    }

                                    return 'table_ready';

                                })()

                            ");

                            if (status != null && (status.Contains("video_playing") || status.Contains("image_ready") || status.Contains("table_ready"))) {

                                break;

                            }

                        } catch { }

                        await Task.Delay(80);

                    }

                }



                if (gen != _serversRevealGeneration) return;



                _serversRevealed = true;



                // 5. Seamlessly reveal the fully rendered servers card

                Dispatcher.Invoke(() => {

                    if (ServersWebView != null) {

                        ServersWebView.Margin = new Thickness(0);

                        ServersWebView.Visibility = Visibility.Visible;

                    }

                    if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;

                    if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Collapsed;

                });

            } catch {

            } finally {

                _serversRevealing = false;

            }

        }



        private async Task CheckAndRevealServersAsync() {

            await RevealServersAsync();

        }



        private bool _browserInitialized = false;







        private async Task EnsureBrowserInitializedAsync() {



            if (_browserInitialized || BrowserWebView == null) return;



            try {



                try {



                    BrowserWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(22, 28, 35);



                } catch { }







                var env = await GetSharedWebViewEnvironmentAsync();



                await BrowserWebView.EnsureCoreWebView2Async(env);



                if (BrowserWebView.CoreWebView2 != null) {



                    try {

                        BrowserWebView.CoreWebView2.MemoryUsageTargetLevel = (UserPrefs.WebPerformanceMode == "Low")

                            ? CoreWebView2MemoryUsageTargetLevel.Low

                            : CoreWebView2MemoryUsageTargetLevel.Normal;

                    } catch { }

                    SetupWebResourceFilter(BrowserWebView.CoreWebView2);



                    BrowserWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;



                    BrowserWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;



                    BrowserWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;







                    BrowserWebView.SizeChanged += (s, e) => UpdateBrowserZoom();



                    BrowserWebView.MouseEnter += (s, e) => ResetHeadToCenter(animated: true);



                    try {



                        var cm = BrowserWebView.CoreWebView2.CookieManager;



                        var c1 = cm.CreateCookie("nuxt-color-mode", "dark", ".mcpedl.com", "/");



                        cm.AddOrUpdateCookie(c1);



                        var c2 = cm.CreateCookie("color-mode", "dark", ".mcpedl.com", "/");



                        cm.AddOrUpdateCookie(c2);



                    } catch { }



                    await BrowserWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ANTI_COOKIE_AND_POPUP_SCRIPT);







                    BrowserWebView.CoreWebView2.WebMessageReceived += (s, e) => {



                        try {



                            if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var srcUri) ||



                                (srcUri.Scheme != Uri.UriSchemeHttp && srcUri.Scheme != Uri.UriSchemeHttps)) {



                                return;



                            }



                            string msg = e.TryGetWebMessageAsString();



                            if (msg == "browser_mouseenter") {



                                Dispatcher.Invoke(() => ResetHeadToCenter(animated: true));



                                return;



                            }



                            if (!string.IsNullOrEmpty(msg) && msg.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) {



                                LaunchMinecraftUri(msg);



                            }



                        } catch { }



                    };







                    // Domain Whitelist & Bedrock Guard (100% English warnings)



                    BrowserWebView.CoreWebView2.NavigationStarting += (s, args) => {



                        string navUri = (args.Uri ?? "").ToLowerInvariant();



                        if (string.IsNullOrWhiteSpace(navUri) || navUri == "about:blank") return;







                        if (navUri.StartsWith("minecraft:")) {



                            args.Cancel = true;



                            LaunchMinecraftUri(args.Uri);



                            return;



                        }







                        // Always allow direct file downloads, archives, and Cloudflare challenges



                        if (navUri.Contains("challenges.cloudflare.com") || navUri.Contains("cloudflare.com") || navUri.Contains("turnstile") ||



                            navUri.Contains(".mcaddon") || navUri.Contains(".mcpack") || navUri.Contains(".mcworld") || 



                            navUri.Contains(".mctemplate") || navUri.Contains(".zip") || navUri.Contains(".rar") || navUri.Contains(".7z")) {



                            return;



                        }







                        // Always allow trusted download platforms (MediaFire, TeraBox, Mega, Google Drive, Dropbox, etc.)



                        if (IsAllowedDownloadPlatform(navUri)) {



                            return;



                        }







                        if (_currentInAppPlatform == "CurseForge") {



                            if (navUri.Contains("curseforge.com") || navUri.Contains("forgecdn.net") || navUri.Contains("overwolf.com") || navUri.Contains("twitch.tv")) {



                                if (navUri.Contains("curseforge.com")) {



                                    if (!navUri.Contains("/minecraft-bedrock") && !navUri.Contains("/login") && !navUri.Contains("/auth") && !navUri.Contains("/oauth") && !navUri.Contains("/api/")) {



                                        args.Cancel = true;



                                        Dispatcher.InvokeAsync(() => {



                                            ThemedMessageBox.Show("Notice: URL changed to an unsupported section outside Bedrock.\nThese mods are not compatible with Minecraft Bedrock Edition!\n\nReturning to CurseForge Bedrock home.", "Bedrock Incompatibility Notice");



                                            BrowserWebView.CoreWebView2?.Navigate(AppConfig.CurseForgeBedrockUrl);



                                        });



                                        return;



                                    }



                                }



                                return;



                            }







                            args.Cancel = true;



                            Dispatcher.InvokeAsync(() => {



                                var res = ThemedMessageBox.Show(



                                    $"External Download Link:\n\nThis link points outside of CurseForge:\n{args.Uri}\n\nWould you like to open it in your default web browser?",



                                    "External Link",



                                    MessageBoxButton.YesNo,



                                    MessageBoxImage.Question



                                );



                                if (res == MessageBoxResult.Yes) {



                                    TryOpenExternalHttpLink(args.Uri);



                                }



                            });



                            return;



                        } else if (_currentInAppPlatform == "MCPEDL") {



                            if (navUri.Contains("mcpedl.com") || navUri.Contains("mcpedl.org")) {



                                return;



                            }







                            args.Cancel = true;



                            Dispatcher.InvokeAsync(() => {



                                var res = ThemedMessageBox.Show(



                                    $"External Download Link:\n\nThis download link points outside of MCPEDL:\n{args.Uri}\n\nWould you like to open it in your default web browser?",



                                    "External Download Link",



                                    MessageBoxButton.YesNo,



                                    MessageBoxImage.Question



                                );



                                if (res == MessageBoxResult.Yes) {



                                    TryOpenExternalHttpLink(args.Uri);



                                }



                            });



                            return;



                        } else if (_currentInAppPlatform == "minecraftpocket-servers.com") {



                            if (navUri.Contains("minecraftpocket-servers.com")) {



                                return;



                            }







                            // Block external navigation silently for server list



                            args.Cancel = true;



                            return;



                        }



                    };







                    BrowserWebView.CoreWebView2.SourceChanged += (s, e) => {



                        if (BrowserUrlText != null) {



                            BrowserUrlText.Text = BrowserWebView.Source?.ToString() ?? "";



                        }



                    };







                    BrowserWebView.CoreWebView2.NavigationCompleted += (s, e) => {



                        UpdateBrowserZoom();



                        try {

                            BrowserWebView.CoreWebView2?.ExecuteScriptAsync(ANTI_COOKIE_AND_POPUP_SCRIPT);

                            if ((BrowserWebView.Source?.ToString() ?? "").Contains("minecraftpocket-servers.com")) {

                                BrowserWebView.CoreWebView2?.ExecuteScriptAsync(SERVERS_VIEW_POLISH_SCRIPT);

                            }

                            _ = ApplyPerformanceScriptAsync(BrowserWebView.CoreWebView2, UserPrefs.WebPerformanceMode);

                        } catch { }



                    };







                    BrowserWebView.CoreWebView2.NewWindowRequested += (s, e) => {



                        e.Handled = true;



                        BrowserWebView.CoreWebView2.Navigate(e.Uri);



                    };







                    BrowserWebView.CoreWebView2.DownloadStarting += Browser_DownloadStarting;



                    _browserInitialized = true;



                }



            } catch (Exception ex) {



                Trace.WriteLine("Failed to initialize BrowserWebView: " + ex.Message);



            }



        }







        private CoreWebView2DownloadOperation? _currentDownloadOperation;







        private void Browser_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e) {



            try {



                string fileName = Path.GetFileName(e.ResultFilePath);



                string sourceUri = e.DownloadOperation.Uri ?? "";







                string targetDir = DDFWManager.DetermineTargetFolder(sourceUri, fileName);



                string finalPath = Path.Combine(targetDir, fileName);







                e.ResultFilePath = finalPath;



                e.Handled = true;







                _currentDownloadOperation = e.DownloadOperation;







                long initTotal = (long)(e.DownloadOperation.TotalBytesToReceive ?? 0);



                Dispatcher.InvokeAsync(() => {



                    ShowDownloadProgress(fileName, 0, initTotal, initTotal <= 0);



                });







                e.DownloadOperation.BytesReceivedChanged += (s, ev) => {



                    var op = e.DownloadOperation;



                    long bytesRecv = (long)op.BytesReceived;



                    long totalBytes = (long)(op.TotalBytesToReceive ?? 0);







                    Dispatcher.InvokeAsync(() => {



                        if (op.State == CoreWebView2DownloadState.InProgress) {



                            ShowDownloadProgress(fileName, bytesRecv, totalBytes, totalBytes <= 0);



                        }



                    });



                };







                e.DownloadOperation.StateChanged += (s, ev) => {



                    var op = e.DownloadOperation;



                    if (op.State == CoreWebView2DownloadState.Completed) {



                        Dispatcher.InvokeAsync(() => {



                            try {



                                string recheckDir = DDFWManager.DetermineTargetFolder(sourceUri, fileName, finalPath);



                                if (!string.Equals(targetDir, recheckDir, StringComparison.OrdinalIgnoreCase)) {



                                    string newFinalPath = Path.Combine(recheckDir, fileName);



                                    if (File.Exists(finalPath)) {



                                        File.Move(finalPath, newFinalPath, true);



                                        finalPath = newFinalPath;



                                        targetDir = recheckDir;



                                    }



                                }



                            } catch { }







                            _lastDownloadedFilePath = finalPath;



                            _lastDownloadedFolder = targetDir;



                            string catName = DDFWManager.GetCategoryDisplayName(targetDir);



                            ShowDownloadCompleted(fileName, targetDir, catName, (long)op.BytesReceived);



                        });



                    } else if (op.State == CoreWebView2DownloadState.Interrupted) {



                        Dispatcher.InvokeAsync(() => {



                            ShowDownloadInterrupted(fileName, op.InterruptReason.ToString());



                        });



                    }



                };



            } catch (Exception ex) {



                Trace.WriteLine("Download starting exception: " + ex.Message);



            }



        }







        private void ShowDownloadProgress(string fileName, long bytesReceived, long totalBytes, bool isIndeterminate) {



            if (DownloadToastBar == null) return;







            DownloadToastBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#13151D"));



            DownloadToastBar.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D3748"));



            if (DownloadToastIcon != null) DownloadToastIcon.Text = "ðŸ“¥";



            if (DownloadToastText != null) DownloadToastText.Text = $"Downloading: {fileName}";







            if (DownloadProgressBar != null) {



                if (isIndeterminate || totalBytes <= 0) {



                    DownloadProgressBar.IsIndeterminate = true;



                    if (DownloadToastDetails != null) DownloadToastDetails.Text = $"{FormatBytes(bytesReceived)} downloaded";



                } else {



                    DownloadProgressBar.IsIndeterminate = false;



                    double pct = (double)bytesReceived / totalBytes * 100.0;



                    DownloadProgressBar.Value = Math.Clamp(pct, 0, 100);



                    if (DownloadToastDetails != null) {



                        DownloadToastDetails.Text = $"{FormatBytes(bytesReceived)} / {FormatBytes(totalBytes)} ({pct:F0}%)";



                    }



                }



            }







            if (DownloadCancelBtn != null) DownloadCancelBtn.Visibility = Visibility.Visible;



            if (DownloadShowInFolderBtn != null) DownloadShowInFolderBtn.Visibility = Visibility.Collapsed;



            DownloadToastBar.Visibility = Visibility.Visible;



        }







        private void ShowDownloadCompleted(string fileName, string targetDir, string catName, long totalBytes) {



            if (DownloadToastBar == null) return;







            DownloadToastBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172A1E"));



            DownloadToastBar.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38A169"));



            if (DownloadToastIcon != null) DownloadToastIcon.Text = "âœ…";



            if (DownloadToastText != null) DownloadToastText.Text = $"{fileName} saved to DDFW/{Path.GetFileName(targetDir)} ({catName})";



            if (DownloadToastDetails != null) DownloadToastDetails.Text = totalBytes > 0 ? FormatBytes(totalBytes) : "Completed";







            if (DownloadProgressBar != null) {



                DownloadProgressBar.IsIndeterminate = false;



                DownloadProgressBar.Value = 100;



            }







            if (DownloadCancelBtn != null) DownloadCancelBtn.Visibility = Visibility.Collapsed;



            if (DownloadShowInFolderBtn != null) DownloadShowInFolderBtn.Visibility = Visibility.Visible;



            DownloadToastBar.Visibility = Visibility.Visible;



        }







        private void ShowDownloadInterrupted(string fileName, string reason) {



            if (DownloadToastBar == null) return;







            DownloadToastBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A1717"));



            DownloadToastBar.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53E3E"));



            if (DownloadToastIcon != null) DownloadToastIcon.Text = "âš ï¸";



            if (DownloadToastText != null) DownloadToastText.Text = $"Download stopped: {fileName}";



            if (DownloadToastDetails != null) DownloadToastDetails.Text = reason == "UserCanceled" ? "Cancelled by user" : reason;







            if (DownloadProgressBar != null) {



                DownloadProgressBar.IsIndeterminate = false;



            }







            if (DownloadCancelBtn != null) DownloadCancelBtn.Visibility = Visibility.Collapsed;



            if (DownloadShowInFolderBtn != null) DownloadShowInFolderBtn.Visibility = Visibility.Collapsed;



            DownloadToastBar.Visibility = Visibility.Visible;



        }







        private void DownloadCancel_Click(object sender, RoutedEventArgs e) {



            try {



                _currentDownloadOperation?.Cancel();



            } catch { }



        }







        private static string FormatBytes(long bytes) {



            if (bytes <= 0) return "0 B";



            if (bytes < 1024) return $"{bytes} B";



            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";



            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";



            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";



        }











        private static bool IsAllowedDownloadPlatform(string navUri) {



            if (string.IsNullOrWhiteSpace(navUri)) return false;







            // MediaFire



            if (navUri.Contains("mediafire.com") || navUri.Contains("mfcdn.com")) return true;







            // TeraBox & its mirrors/short domains



            if (navUri.Contains("terabox.com") || navUri.Contains("teraboxapp.com") || 



                navUri.Contains("1024tera.com") || navUri.Contains("terabox.fun") || 



                navUri.Contains("terabox.link") || navUri.Contains("freeterabox.com") ||



                navUri.Contains("4funbox.com") || navUri.Contains("mirrobox.com") || 



                navUri.Contains("nephobox.com") || navUri.Contains("tibibox.com")) return true;







            // Mega



            if (navUri.Contains("mega.nz") || navUri.Contains("mega.io") || navUri.Contains("mega.co.nz")) return true;







            // Google Drive / Google Docs / Google Cloud



            if (navUri.Contains("drive.google.com") || navUri.Contains("docs.google.com") || 



                navUri.Contains("googleusercontent.com") || navUri.Contains("storage.googleapis.com") ||



                navUri.Contains("accounts.google.com")) return true;







            // Dropbox



            if (navUri.Contains("dropbox.com") || navUri.Contains("dropboxusercontent.com")) return true;







            // OneDrive / Box / GitHub / GitLab



            if (navUri.Contains("1drv.ms") || navUri.Contains("onedrive.live.com") || 



                navUri.Contains("box.com") || navUri.Contains("github.com") || 



                navUri.Contains("raw.githubusercontent.com") || navUri.Contains("gitlab.com")) return true;







            // Creator Download Intermediaries & Gateways used on MCPEDL



            if (navUri.Contains("linkvertise.com") || navUri.Contains("linkvertise.net") || 



                navUri.Contains("link-to.net") || navUri.Contains("up-to-down.net") || 



                navUri.Contains("direct-link.net") || navUri.Contains("file-link.net") ||



                navUri.Contains("lootlabs.gg") || navUri.Contains("loot-link.com") || 



                navUri.Contains("loot-links.com") || navUri.Contains("lootlink.org") ||



                navUri.Contains("sub2unlock.com") || navUri.Contains("sub2unlock.io") ||



                navUri.Contains("sub2get.com") || navUri.Contains("boost.ink") || 



                navUri.Contains("rekonise.com")) return true;







            return false;



        }







        private void ToastDismiss_Click(object sender, RoutedEventArgs e) {



            if (DownloadToastBar != null) DownloadToastBar.Visibility = Visibility.Collapsed;



        }







        private void ToastShowInFolder_Click(object sender, RoutedEventArgs e) {



            try {



                string folder = _lastDownloadedFolder ?? DDFWManager.RootFolder;



                if (!string.IsNullOrEmpty(_lastDownloadedFilePath) && File.Exists(_lastDownloadedFilePath)) {



                    Process.Start("explorer.exe", $"/select,\"{_lastDownloadedFilePath}\"");



                } else if (Directory.Exists(folder)) {



                    Process.Start("explorer.exe", $"\"{folder}\"");



                }



            } catch (Exception ex) {



                ThemedMessageBox.Show($"Could not open folder: {ex.Message}", "Error");



            }



        }







        private async void OpenInAppBrowser(string url) {



            if (InAppBrowserOverlay == null || BrowserWebView == null) return;



            ResetHeadToCenter(animated: true);



            if (PlayViewGrid != null) PlayViewGrid.Visibility = Visibility.Collapsed;



            if (ServerCardBorder != null) ServerCardBorder.Visibility = Visibility.Collapsed;



            if (ServersWebView != null) {



                ServersWebView.Visibility = Visibility.Collapsed;



                try {



                    if (ServersWebView.CoreWebView2 != null) {
                        if (UserPrefs.WebPerformanceMode == "Low") {
                            ServersWebView.CoreWebView2.Navigate("about:blank");
                            _ = ServersWebView.CoreWebView2.TrySuspendAsync();
                            ServersWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                            _ = ClearWebViewCacheAsync(ServersWebView.CoreWebView2);
                        } else if (UserPrefs.WebPerformanceMode == "Balanced") {
                            _ = ServersWebView.CoreWebView2.TrySuspendAsync();
                            ServersWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                        }
                        // In "High" performance mode: preserve ServersWebView cached in memory for instant seamless return!



                    }



                } catch { }



            }



            if (AddonsManagerViewGrid != null) AddonsManagerViewGrid.Visibility = Visibility.Collapsed;

                if (SettingsViewGrid != null) SettingsViewGrid.Visibility = Visibility.Collapsed;







            InAppBrowserOverlay.Visibility = Visibility.Visible;



            UpdateBrowserDependentControls(isBrowserOpen: true);



            await EnsureBrowserInitializedAsync();







            if (BrowserWebView.CoreWebView2 != null) {



                try {



                    BrowserWebView.CoreWebView2.Resume();

                    BrowserWebView.CoreWebView2.MemoryUsageTargetLevel = (UserPrefs.WebPerformanceMode == "Low")

                        ? CoreWebView2MemoryUsageTargetLevel.Low

                        : CoreWebView2MemoryUsageTargetLevel.Normal;



                } catch { }



            }







            if (url.Contains("mcpedl.com")) {



                _currentInAppPlatform = "MCPEDL";



                try {



                    if (BrowserWebView.CoreWebView2 != null) {



                        var cm = BrowserWebView.CoreWebView2.CookieManager;



                        cm.AddOrUpdateCookie(cm.CreateCookie("color-mode", "dark", ".mcpedl.com", "/"));



                        cm.AddOrUpdateCookie(cm.CreateCookie("nuxt-color-mode", "dark", ".mcpedl.com", "/"));



                    }



                } catch { }



            } else if (url.Contains("curseforge.com")) {



                _currentInAppPlatform = "CurseForge";



            } else if (url.Contains("minecraftpocket-servers.com")) {



                _currentInAppPlatform = "minecraftpocket-servers.com";



            }







            UpdateBrowserZoom();







            if (BrowserWebView.CoreWebView2 != null) {



                BrowserWebView.CoreWebView2.Navigate(url);



            } else {



                BrowserWebView.Source = new Uri(url);



            }



        }







        private void CardModsSection_Click(object sender, MouseButtonEventArgs e) {



            OpenInAppBrowser(AppConfig.McpedlBaseUrl.TrimEnd('/') + "/category/mods/");



        }







        private void CardTexturesSection_Click(object sender, MouseButtonEventArgs e) {



            OpenInAppBrowser(AppConfig.McpedlBaseUrl.TrimEnd('/') + "/category/texture-packs/");



        }







        private void CardMapsSection_Click(object sender, MouseButtonEventArgs e) {



            OpenInAppBrowser(AppConfig.McpedlBaseUrl.TrimEnd('/') + "/category/maps/");



        }







        private void CardSkinsSection_Click(object sender, MouseButtonEventArgs e) {



            OpenInAppBrowser(AppConfig.McpedlBaseUrl.TrimEnd('/') + "/skins/skin-packs/");



        }







        private void ServersRetryBtn_Click(object sender, RoutedEventArgs e) {



            RetryServersLoad();



        }







        private void RefreshServersBtn_Click(object sender, RoutedEventArgs e) {



            RetryServersLoad();



        }







        private void RetryServersLoad() {



            try {



                ParkServersWebViewForLoading();







                bool isNetworkUp = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();



                if (!isNetworkUp) {



                    Dispatcher.InvokeAsync(async () => {



                        await Task.Delay(500);



                        if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;



                        if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Visible;



                    });



                    return;



                }







                if (ServersWebView?.CoreWebView2 != null) {



                    ServersWebView.Reload();



                } else if (ServersWebView != null) {



                    ServersWebView.CoreWebView2?.Navigate(AppConfig.ServersHubUrl);



                }



            } catch { }



        }







        private void HomeServersBtn_Click(object sender, RoutedEventArgs e) {



            try {



                ParkServersWebViewForLoading();







                bool isNetworkUp = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();



                if (!isNetworkUp) {



                    Dispatcher.InvokeAsync(async () => {



                        await Task.Delay(500);



                        if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;



                        if (ServersNoInternetOverlay != null) ServersNoInternetOverlay.Visibility = Visibility.Visible;



                    });



                    return;



                }







                if (ServersWebView?.CoreWebView2 != null) {



                    ServersWebView.CoreWebView2.Navigate(AppConfig.ServersHubUrl);



                } else if (ServersWebView != null) {



                    ServersWebView.Source = new Uri(AppConfig.ServersHubUrl);



                }



            } catch { }



        }







        private void ExpandServersBtn_Click(object sender, RoutedEventArgs e) {



            OpenInAppBrowser(AppConfig.ServersHubUrl);



        }







        private void BrowserBackBtn_Click(object sender, RoutedEventArgs e) {



            try { if (BrowserWebView?.CanGoBack == true) BrowserWebView.GoBack(); } catch { }



        }







        private void BrowserForwardBtn_Click(object sender, RoutedEventArgs e) {



            try { if (BrowserWebView?.CanGoForward == true) BrowserWebView.GoForward(); } catch { }



        }







        private void BrowserRefreshBtn_Click(object sender, RoutedEventArgs e) {



            try { BrowserWebView?.Reload(); } catch { }



        }







        private void BrowserMCPEDLToggleBtn_Click(object sender, RoutedEventArgs e) {



            OpenInAppBrowser(AppConfig.McpedlBaseUrl);



        }







        private void BrowserCurseForgeToggleBtn_Click(object sender, RoutedEventArgs e) {



            OpenInAppBrowser(AppConfig.CurseForgeBedrockUrl);



        }







        private void OpenDDFWFolderBtn_Click(object sender, RoutedEventArgs e) {



            try {



                DDFWManager.EnsureDirectories();



                Process.Start("explorer.exe", $"\"{DDFWManager.RootFolder}\"");



            } catch (Exception ex) {



                ThemedMessageBox.Show($"Could not open DDFW folder: {ex.Message}", "Error");



            }



        }







        private void UpdateBrowserDependentControls(bool isBrowserOpen) {



            bool enabled = !isBrowserOpen;



            if (TopImportAppxButton != null) {



                TopImportAppxButton.IsEnabled = enabled;



                TopImportAppxButton.Opacity = enabled ? 1.0 : 0.45;



            }



            if (SidebarLocalDataDirButton != null) {



                SidebarLocalDataDirButton.IsEnabled = enabled;



                SidebarLocalDataDirButton.Opacity = enabled ? 1.0 : 0.45;



            }



            if (InstallationsImportAppxButton != null) {



                InstallationsImportAppxButton.IsEnabled = enabled;



                InstallationsImportAppxButton.Opacity = enabled ? 1.0 : 0.45;



            }







        }







        private void CloseInAppBrowser() {



            if (InAppBrowserOverlay != null) {



                InAppBrowserOverlay.Visibility = Visibility.Collapsed;



            }



            if (DownloadToastBar != null) {



                DownloadToastBar.Visibility = Visibility.Collapsed;



            }



            UpdateBrowserDependentControls(isBrowserOpen: false);







            try {



                if (BrowserWebView?.CoreWebView2 != null) {



                    BrowserWebView.CoreWebView2.Navigate("about:blank");

                    _ = BrowserWebView.CoreWebView2.TrySuspendAsync();

                    BrowserWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

                    if (UserPrefs.WebPerformanceMode == "High" || UserPrefs.WebPerformanceMode == "Low") {

                        _ = ClearWebViewCacheAsync(BrowserWebView.CoreWebView2);

                    }



                }



            } catch { }







            _ = Task.Run(() => {



                try {



                    GC.Collect(2, GCCollectionMode.Forced, true, true);



                    GC.WaitForPendingFinalizers();



                    NativeMemoryHelper.TrimWorkingSet();



                } catch { }



            });







            if (TabNavPlay?.IsChecked == true) {



                if (PlayViewGrid != null) PlayViewGrid.Visibility = Visibility.Visible;



                if (ServerCardBorder != null) ServerCardBorder.Visibility = Visibility.Visible;



                if (ServersWebView != null) {
                    if (UserPrefs.WebPerformanceMode == "High") {
                        // High Performance Mode: Never reset, re-navigate, or zero the cache!
                        // Simply unfreeze (Resume) in place instantly without loading overlay!
                        ServersWebView.Margin = new Thickness(0);
                        ServersWebView.Visibility = Visibility.Visible;
                        if (ServersLoadingOverlay != null) ServersLoadingOverlay.Visibility = Visibility.Collapsed;
                        try {
                            ServersWebView.CoreWebView2?.Resume();
                            ServersWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                        } catch { }
                    } else {
                        // Low & Balanced:
                        ParkServersWebViewForLoading();
                        try {
                            if (ServersWebView.CoreWebView2 != null) {
                                ServersWebView.CoreWebView2.Resume();
                                ServersWebView.CoreWebView2.MemoryUsageTargetLevel = (UserPrefs.WebPerformanceMode == "Low")
                                    ? CoreWebView2MemoryUsageTargetLevel.Low
                                    : CoreWebView2MemoryUsageTargetLevel.Normal;
                                ServersWebView.CoreWebView2.Navigate(AppConfig.ServersHubUrl);
                            }
                        } catch { }
                    }
                }
                if (InstallationsViewGrid != null) InstallationsViewGrid.Visibility = Visibility.Collapsed;



                if (AddonsManagerViewGrid != null) AddonsManagerViewGrid.Visibility = Visibility.Collapsed;

                if (SettingsViewGrid != null) SettingsViewGrid.Visibility = Visibility.Collapsed;



            } else if (TabNavInstallations?.IsChecked == true) {



                if (PlayViewGrid != null) PlayViewGrid.Visibility = Visibility.Collapsed;



                if (ServerCardBorder != null) ServerCardBorder.Visibility = Visibility.Collapsed;



                if (ServersWebView != null) ServersWebView.Visibility = Visibility.Collapsed;



                if (InstallationsViewGrid != null) InstallationsViewGrid.Visibility = Visibility.Visible;



                if (AddonsManagerViewGrid != null) AddonsManagerViewGrid.Visibility = Visibility.Collapsed;

                if (SettingsViewGrid != null) SettingsViewGrid.Visibility = Visibility.Collapsed;



            } else if (TabNavAddons?.IsChecked == true) {



                if (PlayViewGrid != null) PlayViewGrid.Visibility = Visibility.Collapsed;



                if (ServerCardBorder != null) ServerCardBorder.Visibility = Visibility.Collapsed;



                if (ServersWebView != null) ServersWebView.Visibility = Visibility.Collapsed;



                if (InstallationsViewGrid != null) InstallationsViewGrid.Visibility = Visibility.Collapsed;



                if (AddonsManagerViewGrid != null) AddonsManagerViewGrid.Visibility = Visibility.Visible;



            }



        }







        private static void TryOpenExternalHttpLink(string? rawUri) {



            try {



                if (Uri.TryCreate(rawUri, UriKind.Absolute, out var uri) &&



                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {



                    Process.Start(new ProcessStartInfo {



                        FileName = uri.AbsoluteUri,



                        UseShellExecute = true



                    });



                }



            } catch { }



        }







        private void CloseBrowserBtn_Click(object sender, RoutedEventArgs e) {



            CloseInAppBrowser();



        }



    }



}



