using System;
using System.Windows;

namespace BUGrockLauncher.Helpers
{
    public static class ModalDialogHelper
    {
        public static Window? ResolveMainWindow(Window? specifiedOwner = null)
        {
            if (specifiedOwner != null && specifiedOwner.IsVisible)
                return specifiedOwner;

            var app = Application.Current;
            if (app?.MainWindow != null && app.MainWindow.IsVisible)
                return app.MainWindow;

            if (app != null)
            {
                foreach (Window win in app.Windows)
                {
                    if (win is MainWindow && win.IsVisible)
                        return win;
                }
            }

            return app?.MainWindow;
        }

        public static void CenterOnOwner(Window dialog)
        {
            if (dialog == null) return;

            Window? owner = dialog.Owner ?? ResolveMainWindow();
            if (owner != null && owner.IsVisible)
            {
                if (dialog.Owner == null)
                {
                    dialog.Owner = owner;
                }

                double ownerLeft = owner.Left;
                double ownerTop = owner.Top;
                double ownerWidth = owner.ActualWidth > 0 ? owner.ActualWidth : (owner.Width > 0 ? owner.Width : 1200);
                double ownerHeight = owner.ActualHeight > 0 ? owner.ActualHeight : (owner.Height > 0 ? owner.Height : 720);

                if (owner.WindowState == WindowState.Maximized)
                {
                    ownerLeft = SystemParameters.WorkArea.Left;
                    ownerTop = SystemParameters.WorkArea.Top;
                    ownerWidth = SystemParameters.WorkArea.Width;
                    ownerHeight = SystemParameters.WorkArea.Height;
                }

                if (dialog.ActualWidth <= 0 || dialog.ActualHeight <= 0)
                {
                    dialog.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                }

                double dialogWidth = dialog.ActualWidth > 0 ? dialog.ActualWidth : (dialog.Width > 0 ? dialog.Width : dialog.DesiredSize.Width);
                double dialogHeight = dialog.ActualHeight > 0 ? dialog.ActualHeight : (dialog.Height > 0 ? dialog.Height : dialog.DesiredSize.Height);

                if (dialogWidth <= 0) dialogWidth = 500;
                if (dialogHeight <= 0) dialogHeight = 220;

                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = Math.Round(ownerLeft + (ownerWidth - dialogWidth) / 2.0);
                dialog.Top = Math.Round(ownerTop + (ownerHeight - dialogHeight) / 2.0);
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        public static void AttachCenterLock(Window dialog)
        {
            if (dialog == null) return;

            dialog.WindowStartupLocation = WindowStartupLocation.Manual;

            void Recenter() => CenterOnOwner(dialog);

            dialog.SourceInitialized += (s, e) => Recenter();
            dialog.Loaded += (s, e) => Recenter();
            dialog.ContentRendered += (s, e) => Recenter();

            EventHandler onOwnerMoved = (s, e) => Recenter();
            SizeChangedEventHandler onOwnerResized = (s, e) => Recenter();

            dialog.Loaded += (s, e) =>
            {
                Window? owner = dialog.Owner ?? ResolveMainWindow();
                if (owner != null)
                {
                    dialog.Owner = owner;
                    owner.LocationChanged += onOwnerMoved;
                    owner.SizeChanged += onOwnerResized;
                    Recenter();
                }
            };

            dialog.Closed += (s, e) =>
            {
                if (dialog.Owner != null)
                {
                    dialog.Owner.LocationChanged -= onOwnerMoved;
                    dialog.Owner.SizeChanged -= onOwnerResized;
                }
            };
        }
    }
}
