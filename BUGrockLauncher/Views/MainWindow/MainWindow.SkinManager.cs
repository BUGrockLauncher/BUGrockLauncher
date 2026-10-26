using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace BUGrockLauncher {
    public partial class MainWindow {
        private double _targetYaw = 0;
        private double _targetPitch = 0;
        private bool _isRenderingHooked = false;

        private void HookHeadRendering() {
            if (_isRenderingHooked) return;
            _isRenderingHooked = true;
            CompositionTarget.Rendering += OnHeadRenderFrame;
        }

        private void OnHeadRenderFrame(object? sender, EventArgs e) {
            if (SteveHeadRotationYaw == null || SteveHeadRotationPitch == null) return;

            double curYaw = SteveHeadRotationYaw.Angle;
            double curPitch = SteveHeadRotationPitch.Angle;

            double diffYaw = _targetYaw - curYaw;
            double diffPitch = _targetPitch - curPitch;

            // If virtually reached target, snap and idle
            if (Math.Abs(diffYaw) < 0.05 && Math.Abs(diffPitch) < 0.05) {
                if (curYaw != _targetYaw) SteveHeadRotationYaw.Angle = _targetYaw;
                if (curPitch != _targetPitch) SteveHeadRotationPitch.Angle = _targetPitch;
                return;
            }

            // Smooth fluid lerp (14% per frame = organic spring/easing)
            SteveHeadRotationYaw.Angle = curYaw + diffYaw * 0.14;
            SteveHeadRotationPitch.Angle = curPitch + diffPitch * 0.14;
        }

        public void ResetHeadToCenter(bool animated = true) {
            _targetYaw = 0;
            _targetPitch = 0;
            if (!animated) {
                if (SteveHeadRotationYaw != null) SteveHeadRotationYaw.Angle = 0;
                if (SteveHeadRotationPitch != null) SteveHeadRotationPitch.Angle = 0;
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e) {
            ResetHeadToCenter(animated: true);
        }

        private void Window_MouseMove(object sender, MouseEventArgs e) {
            if (VersionSelectorPopup != null && VersionSelectorPopup.IsOpen) return;
            if (SteveHeadRotationYaw == null || SteveHeadRotationPitch == null || HeadViewport == null) return;

            // When in-app browser overlay is open and mouse is within the browser area (below top toolbar)
            if (InAppBrowserOverlay != null && InAppBrowserOverlay.Visibility == Visibility.Visible) {
                try {
                    System.Windows.Point ptInOverlay = e.GetPosition(InAppBrowserOverlay);
                    if (ptInOverlay.X >= 0 && ptInOverlay.X <= InAppBrowserOverlay.ActualWidth &&
                        ptInOverlay.Y >= 40 && ptInOverlay.Y <= InAppBrowserOverlay.ActualHeight) {
                        ResetHeadToCenter(animated: true);
                        return;
                    }
                } catch { }
            }

            // When mouse is over servers webview
            if (ServersWebView != null && ServersWebView.Visibility == Visibility.Visible && ServersWebView.IsVisible) {
                try {
                    System.Windows.Point ptInServers = e.GetPosition(ServersWebView);
                    if (ptInServers.X >= 0 && ptInServers.X <= ServersWebView.ActualWidth &&
                        ptInServers.Y >= 0 && ptInServers.Y <= ServersWebView.ActualHeight) {
                        ResetHeadToCenter(animated: true);
                        return;
                    }
                } catch { }
            }

            try {
                System.Windows.Point mousePos = e.GetPosition(HeadViewport);
                double centerX = HeadViewport.ActualWidth / 2.0;
                double centerY = HeadViewport.ActualHeight / 2.0;

                double deltaX = mousePos.X - centerX;
                double deltaY = mousePos.Y - centerY;

                // Yaw: look left/right (limit to +/- 48 degrees)
                _targetYaw = Math.Clamp(deltaX * 0.12, -48.0, 48.0);
                // Pitch: look up/down (deltaY is negative when mouse moves UP, tilting face UP)
                _targetPitch = Math.Clamp(deltaY * 0.12, -34.0, 34.0);
            } catch {
            }
        }

        private void InitSteveHead() {
            HookHeadRendering();
            try {
                if (UserPrefs?.ActiveSkinType == "Custom" && !string.IsNullOrEmpty(UserPrefs?.CustomSkinPath) && File.Exists(UserPrefs.CustomSkinPath)) {
                    var bmp = SteveHeadHelper.LoadSkinFromFile(UserPrefs.CustomSkinPath);
                    string name = string.IsNullOrEmpty(UserPrefs.CustomSkinName) ? "Custom Skin" : UserPrefs.CustomSkinName;
                    ApplySkin(bmp, name, isBugrock: false);
                    UpdateSkinSelectionUI(isCustom: true);
                    return;
                }
            } catch { }

            LoadDefaultBugrockSkin();
            UpdateSkinSelectionUI(isCustom: false);
        }

        private void UpdateSkinSelectionUI(bool isCustom) {
            try {
                if (PresetCustomRing != null) {
                    PresetCustomRing.BorderBrush = isCustom ? new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69)) : Brushes.Transparent;
                }
                if (PresetGlitchedRing != null) {
                    PresetGlitchedRing.BorderBrush = !isCustom ? new SolidColorBrush(Color.FromRgb(0xD0, 0x5C, 0xE3)) : Brushes.Transparent;
                }
            } catch { }
        }

        private void LoadDefaultBugrockSkin() {
            try {
                LoadSkinByPath("pack://application:,,,/Assets/bugrock_skin.png", "BUGrock (Glitched)", isBugrock: true);
            } catch {
                try {
                    string localSkin = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "bugrock_skin.png");
                    if (System.IO.File.Exists(localSkin)) {
                        LoadSkinByPath(localSkin, "BUGrock (Glitched)", isBugrock: true);
                    }
                } catch { }
            }
        }

        private void LoadSkinByPath(string uriOrPath, string displayName, bool isBugrock = false) {
            try {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(uriOrPath, UriKind.RelativeOrAbsolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                ApplySkin(bmp, displayName, isBugrock);
            } catch { }
        }

        private void ApplySkin(BitmapSource skinImage, string displayName, bool isBugrock = false) {
            try {
                if (HeadModelGroup == null || skinImage == null) return;
                HeadModelGroup.Children.Clear();
                var headModel = SteveHeadHelper.CreateHeadModel(skinImage);
                HeadModelGroup.Children.Add(headModel);
                if (SkinNameLabel != null) {
                    SkinNameLabel.Text = displayName;
                    var magenta = TryFindResource("AccentMagenta") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0xD0, 0x5C, 0xE3));
                    var green = TryFindResource("AccentGreen") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
                    SkinNameLabel.Foreground = isBugrock ? magenta : green;
                }
            } catch { }
        }

        private void PresetCustom_Click(object sender, RoutedEventArgs e) {
            try {
                if (!string.IsNullOrEmpty(UserPrefs?.CustomSkinPath) && File.Exists(UserPrefs.CustomSkinPath)) {
                    UserPrefs.ActiveSkinType = "Custom";
                    RewritePrefs();
                    var bmp = SteveHeadHelper.LoadSkinFromFile(UserPrefs.CustomSkinPath);
                    string name = string.IsNullOrEmpty(UserPrefs.CustomSkinName) ? "Custom Skin" : UserPrefs.CustomSkinName;
                    ApplySkin(bmp, name, isBugrock: false);
                    UpdateSkinSelectionUI(isCustom: true);
                } else {
                    UploadSkinButton_Click(sender, e);
                }
            } catch (Exception ex) {
                ThemedMessageBox.Show("Failed to switch skin: " + ex.Message, "Skin Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PresetGlitched_Click(object sender, RoutedEventArgs e) {
            try {
                UserPrefs.ActiveSkinType = "BUGrock";
                RewritePrefs();
                LoadDefaultBugrockSkin();
                UpdateSkinSelectionUI(isCustom: false);
            } catch { }
        }

        private void UploadSkinButton_Click(object sender, RoutedEventArgs e) {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Minecraft Skin (*.png)|*.png|All Files (*.*)|*.*",
                Title = "Select Minecraft Skin"
            };
            if (ofd.ShowDialog() == true) {
                try {
                    var newSkin = SteveHeadHelper.LoadSkinFromFile(ofd.FileName);

                    string cacheDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skin_cache");
                    if (!Directory.Exists(cacheDir)) {
                        Directory.CreateDirectory(cacheDir);
                    }
                    string cachedFile = System.IO.Path.Combine(cacheDir, "cached_skin.png");
                    byte[] skinBytes = File.ReadAllBytes(ofd.FileName);
                    File.WriteAllBytes(cachedFile, skinBytes);

                    string skinName = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
                    if (string.IsNullOrWhiteSpace(skinName)) skinName = "Custom Skin";

                    UserPrefs.ActiveSkinType = "Custom";
                    UserPrefs.CustomSkinPath = cachedFile;
                    UserPrefs.CustomSkinName = skinName;
                    RewritePrefs();

                    ApplySkin(newSkin, skinName, isBugrock: false);
                    UpdateSkinSelectionUI(isCustom: true);
                } catch (Exception ex) {
                    ThemedMessageBox.Show("Failed to load skin image: " + ex.Message, "Skin Upload", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

    }
}
