using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BUGrockLauncher.Helpers
{
    public static class AnimationHelper
    {
        private static bool _areAnimationsEnabled = true;
        private static bool _isLowPerformanceMode = false;

        private static readonly List<WeakReference<ScaleTransform>> _trackedScales = new List<WeakReference<ScaleTransform>>();

        public static bool AreAnimationsEnabled
        {
            get => _areAnimationsEnabled;
            set
            {
                if (_areAnimationsEnabled != value)
                {
                    _areAnimationsEnabled = value;
                    if (!IsActive) ResetAllTrackedScales();
                    AnimationsEnabledChanged?.Invoke(IsActive);
                }
            }
        }

        public static bool IsLowPerformanceMode
        {
            get => _isLowPerformanceMode;
            set
            {
                if (_isLowPerformanceMode != value)
                {
                    _isLowPerformanceMode = value;
                    if (!IsActive) ResetAllTrackedScales();
                    AnimationsEnabledChanged?.Invoke(IsActive);
                }
            }
        }

        /// <summary>
        /// True only when Smooth Animations are enabled AND the launcher is NOT in Low Performance Mode.
        /// </summary>
        public static bool IsActive => _areAnimationsEnabled && !_isLowPerformanceMode;

        public static event Action<bool>? AnimationsEnabledChanged;

        public static readonly IEasingFunction DefaultEaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction DefaultEaseInOut = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        public static readonly IEasingFunction SoftBackEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };

        private static void RegisterScale(ScaleTransform st)
        {
            _trackedScales.Add(new WeakReference<ScaleTransform>(st));
        }

        public static void ResetAllTrackedScales()
        {
            for (int i = _trackedScales.Count - 1; i >= 0; i--)
            {
                if (_trackedScales[i].TryGetTarget(out var st))
                {
                    try
                    {
                        st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        st.ScaleX = 1.0;
                        st.ScaleY = 1.0;
                    }
                    catch { }
                }
                else
                {
                    _trackedScales.RemoveAt(i);
                }
            }
        }

        // -------------------------------------------------------------
        // Attached Property: EnableInteractiveAnimations (Hover & Click Scale)
        // -------------------------------------------------------------
        public static readonly DependencyProperty EnableInteractiveAnimationsProperty =
            DependencyProperty.RegisterAttached(
                "EnableInteractiveAnimations",
                typeof(bool),
                typeof(AnimationHelper),
                new PropertyMetadata(false, OnEnableInteractiveAnimationsChanged));

        public static bool GetEnableInteractiveAnimations(DependencyObject obj) => (bool)obj.GetValue(EnableInteractiveAnimationsProperty);
        public static void SetEnableInteractiveAnimations(DependencyObject obj, bool value) => obj.SetValue(EnableInteractiveAnimationsProperty, value);

        private static void OnEnableInteractiveAnimationsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                AttachInteractiveScale(element);
            }
        }

        public static void AttachInteractiveScale(FrameworkElement element, double hoverScale = 1.025, double pressScale = 0.965)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            ScaleTransform st;
            if (element.RenderTransform is ScaleTransform existing && !existing.IsFrozen)
            {
                st = existing;
            }
            else
            {
                st = new ScaleTransform(1.0, 1.0);
                element.RenderTransform = st;
            }
            RegisterScale(st);

            element.MouseEnter += (s, e) =>
            {
                if (!IsActive)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = 1.0;
                    st.ScaleY = 1.0;
                    return;
                }
                AnimateScale(st, hoverScale, 110);
            };

            element.MouseLeave += (s, e) =>
            {
                if (!IsActive)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = 1.0;
                    st.ScaleY = 1.0;
                    return;
                }
                AnimateScale(st, 1.0, 130);
            };

            element.PreviewMouseDown += (s, e) =>
            {
                if (!IsActive)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = 1.0;
                    st.ScaleY = 1.0;
                    return;
                }
                AnimateScale(st, pressScale, 50);
            };

            element.PreviewMouseUp += (s, e) =>
            {
                if (!IsActive)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = 1.0;
                    st.ScaleY = 1.0;
                    return;
                }
                double target = element.IsMouseOver ? hoverScale : 1.0;
                AnimateScale(st, target, 110);
            };
        }

        private static void AnimateScale(ScaleTransform st, double target, double durationMs)
        {
            var anim = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = DefaultEaseOut
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        // -------------------------------------------------------------
        // Attached Property: EnableCardHover (Subtle border/elevation glow)
        // -------------------------------------------------------------
        public static readonly DependencyProperty EnableCardHoverProperty =
            DependencyProperty.RegisterAttached(
                "EnableCardHover",
                typeof(bool),
                typeof(AnimationHelper),
                new PropertyMetadata(false, OnEnableCardHoverChanged));

        public static bool GetEnableCardHover(DependencyObject obj) => (bool)obj.GetValue(EnableCardHoverProperty);
        public static void SetEnableCardHover(DependencyObject obj, bool value) => obj.SetValue(EnableCardHoverProperty, value);

        private static void OnEnableCardHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Border border && (bool)e.NewValue)
            {
                AttachCardHoverEffects(border);
            }
        }

                public static void AttachCardHoverEffects(Border border)
        {
            border.RenderTransformOrigin = new Point(0.5, 0.5);
            ScaleTransform st;
            if (border.RenderTransform is ScaleTransform existing && !existing.IsFrozen)
            {
                st = existing;
            }
            else
            {
                st = new ScaleTransform(1.0, 1.0);
                border.RenderTransform = st;
            }
            RegisterScale(st);

            border.MouseEnter += (s, e) =>
            {
                if (!IsActive)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = 1.0;
                    st.ScaleY = 1.0;
                    return;
                }
                AnimateScale(st, 1.012, 140);
            };

            border.MouseLeave += (s, e) =>
            {
                if (!IsActive)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = 1.0;
                    st.ScaleY = 1.0;
                    return;
                }
                AnimateScale(st, 1.0, 140);
            };
        }

        // -------------------------------------------------------------
        // Attached Property: EnablePillAnimation (Modern Switch Glide)
        // -------------------------------------------------------------
        public static readonly DependencyProperty EnablePillAnimationProperty =
            DependencyProperty.RegisterAttached(
                "EnablePillAnimation",
                typeof(bool),
                typeof(AnimationHelper),
                new PropertyMetadata(false, OnEnablePillAnimationChanged));

        public static bool GetEnablePillAnimation(DependencyObject obj) => (bool)obj.GetValue(EnablePillAnimationProperty);
        public static void SetEnablePillAnimation(DependencyObject obj, bool value) => obj.SetValue(EnablePillAnimationProperty, value);

        private static void OnEnablePillAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CheckBox checkBox && (bool)e.NewValue)
            {
                AttachPillAnimation(checkBox);
            }
        }

        public static void AttachPillAnimation(CheckBox checkBox)
        {
            checkBox.Initialized += (s, e) => UpdatePillState(checkBox, animate: false);
            checkBox.Loaded += (s, e) => UpdatePillState(checkBox, animate: false);
            checkBox.Checked += (s, e) => UpdatePillState(checkBox, animate: IsActive);
            checkBox.Unchecked += (s, e) => UpdatePillState(checkBox, animate: IsActive);
        }

        public static void UpdatePillState(CheckBox checkBox, bool animate)
        {
            if (checkBox.Template == null) return;
            checkBox.ApplyTemplate();
            var thumb = checkBox.Template.FindName("Thumb", checkBox) as FrameworkElement;
            var track = checkBox.Template.FindName("Track", checkBox) as Border;
            var onLabel = checkBox.Template.FindName("OnLabel", checkBox) as FrameworkElement;
            var offLabel = checkBox.Template.FindName("OffLabel", checkBox) as FrameworkElement;

            if (thumb == null || track == null) return;

            // Ensure Thumb Margin is strictly 4,0,0,0 so TranslateTransform moves it cleanly within the 58px track
            thumb.Margin = new Thickness(4, 0, 0, 0);

            TranslateTransform tt;
            if (thumb.RenderTransform is TranslateTransform existing && !existing.IsFrozen)
            {
                tt = existing;
            }
            else
            {
                tt = new TranslateTransform();
                thumb.RenderTransform = tt;
            }

            bool isChecked = checkBox.IsChecked == true;
            double targetX = isChecked ? 30.0 : 0.0;
            Color targetBg = isChecked ? Color.FromRgb(0x10, 0xB9, 0x81) : Color.FromRgb(0x22, 0x24, 0x33);
            Color targetBorder = isChecked ? Color.FromRgb(0x34, 0xD3, 0x99) : Color.FromRgb(0x32, 0x36, 0x4A);

            if (onLabel != null) onLabel.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
            if (offLabel != null) offLabel.Visibility = isChecked ? Visibility.Collapsed : Visibility.Visible;

            if (!animate || !IsActive)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.X = targetX;
                track.Background = new SolidColorBrush(targetBg);
                track.BorderBrush = new SolidColorBrush(targetBorder);
                if (onLabel != null)
                {
                    onLabel.BeginAnimation(UIElement.OpacityProperty, null);
                    onLabel.Opacity = isChecked ? 1.0 : 0.0;
                }
                if (offLabel != null)
                {
                    offLabel.BeginAnimation(UIElement.OpacityProperty, null);
                    offLabel.Opacity = isChecked ? 0.0 : 1.0;
                }
                return;
            }

            // Smooth 150ms Cubic glide inside the pill track
            var slideAnim = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = DefaultEaseOut
            };
            tt.BeginAnimation(TranslateTransform.XProperty, slideAnim);

            Color currentBg = (track.Background is SolidColorBrush scb1) ? scb1.Color : Color.FromRgb(0x22, 0x24, 0x33);
            var bgAnim = new ColorAnimation
            {
                From = currentBg,
                To = targetBg,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = DefaultEaseOut
            };
            var trackBgBrush = new SolidColorBrush(currentBg);
            track.Background = trackBgBrush;
            trackBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);

            Color currentBorder = (track.BorderBrush is SolidColorBrush scb2) ? scb2.Color : Color.FromRgb(0x32, 0x36, 0x4A);
            var borderAnim = new ColorAnimation
            {
                From = currentBorder,
                To = targetBorder,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = DefaultEaseOut
            };
            var trackBorderBrush = new SolidColorBrush(currentBorder);
            track.BorderBrush = trackBorderBrush;
            trackBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);

            if (onLabel != null)
            {
                var labelAnim = new DoubleAnimation
                {
                    To = isChecked ? 1.0 : 0.0,
                    Duration = TimeSpan.FromMilliseconds(120),
                    EasingFunction = DefaultEaseOut
                };
                onLabel.BeginAnimation(UIElement.OpacityProperty, labelAnim);
            }

            if (offLabel != null)
            {
                var labelAnim = new DoubleAnimation
                {
                    To = isChecked ? 0.0 : 1.0,
                    Duration = TimeSpan.FromMilliseconds(120),
                    EasingFunction = DefaultEaseOut
                };
                offLabel.BeginAnimation(UIElement.OpacityProperty, labelAnim);
            }
        }

        // -------------------------------------------------------------
        // Page Transitions (Fluid Slide & Fade)
        // -------------------------------------------------------------
        public static void AnimatePageIn(FrameworkElement? element, double fromOffsetY = 12, double durationMs = 200)
        {
            if (element == null) return;

            if (!IsActive)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1.0;
                element.Visibility = Visibility.Visible;
                if (element.RenderTransform is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                    tt.Y = 0;
                }
                return;
            }

            element.Visibility = Visibility.Visible;

            TranslateTransform transform;
            if (element.RenderTransform is TranslateTransform existing && !existing.IsFrozen)
            {
                transform = existing;
            }
            else
            {
                transform = new TranslateTransform();
                element.RenderTransform = transform;
            }

            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = DefaultEaseOut
            };

            var slideAnim = new DoubleAnimation
            {
                From = fromOffsetY,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = DefaultEaseOut
            };

            element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            transform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }

        public static void AnimateFadeOut(FrameworkElement? element, Action? onCompleted = null, double durationMs = 120)
        {
            if (element == null) return;

            if (!IsActive || element.Visibility != Visibility.Visible)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Visibility = Visibility.Collapsed;
                element.Opacity = 1.0;
                onCompleted?.Invoke();
                return;
            }

            var opacityAnim = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = DefaultEaseInOut
            };

            opacityAnim.Completed += (s, e) =>
            {
                element.Visibility = Visibility.Collapsed;
                element.Opacity = 1.0;
                onCompleted?.Invoke();
            };

            element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        public static void AnimateFadeIn(UIElement? element, double durationMs = 180)
        {
            if (element == null) return;
            if (!IsActive)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1.0;
                element.Visibility = Visibility.Visible;
                return;
            }

            element.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = DefaultEaseOut
            };
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // -------------------------------------------------------------
        // Dialog Pop-In Animation (Scale + Fade)
        // -------------------------------------------------------------
        public static void AnimateDialogIn(Window? window, double durationMs = 170)
        {
            if (window == null) return;
            if (!IsActive)
            {
                window.Opacity = 1.0;
                return;
            }

            window.Opacity = 0.0;
            window.RenderTransformOrigin = new Point(0.5, 0.5);
            var st = new ScaleTransform(0.94, 0.94);
            window.RenderTransform = st;

            window.Loaded += (s, e) =>
            {
                if (!IsActive)
                {
                    window.Opacity = 1.0;
                    st.ScaleX = 1.0;
                    st.ScaleY = 1.0;
                    return;
                }

                var opacityAnim = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = DefaultEaseOut
                };

                var scaleAnim = new DoubleAnimation
                {
                    From = 0.94,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = SoftBackEase
                };

                window.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
                st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            };
        }
    }
}
