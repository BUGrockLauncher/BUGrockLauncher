using BUGrockLauncher.Helpers;
using BUGrockLauncher.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace BUGrockLauncher {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {

        protected override async void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            Trace.Listeners.Add(new TextWriterTraceListener("Log.txt"));
            Trace.AutoFlush = true;

            DispatcherUnhandledException += (sender, args) => {
                Trace.WriteLine("Unhandled Dispatcher Exception:\n" + args.Exception);
                try {
                    ThemedMessageBox.Show("An unexpected error occurred:\n" + args.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                } catch { }
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
                Trace.WriteLine("Unhandled Domain Exception:\n" + args.ExceptionObject);
            };

            var splash = new SplashScreenWindow();
            splash.Show();

            var main = new MainWindow();

            // 1. Startup check for FPS Unlocker (unclogs 60 FPS / VSync cap)
            if (main.UserPrefs.FpsUnlockerEnabled) {
                await FpsUnlockerHelper.CheckAndApplyOnStartupAsync(splash, true, main.UserPrefs.MaxFramerate);
            }

            // 2. Initialize Discord Rich Presence
            if (main.UserPrefs.DiscordRpcEnabled) {
                DiscordRpcService.Initialize(true);
            }

            // 3. Preload Community Servers during splash screen
            splash.SetStatus("Loading community servers...");
            await main.WaitForServersReadyAsync(splash);

            splash.SetStatus("Ready!");
            await Task.Delay(150);

            // Display fully loaded MainWindow and close splash
            Current.MainWindow = main;
            main.Show();
            splash.Close();
        }
    }
}
