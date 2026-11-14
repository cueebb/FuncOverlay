using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FuncOverlay.Tray
{
    public partial class TrayMenuWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WIN32_POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out WIN32_POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private readonly Action _toggleAction;
        private readonly Action _openSettingsAction;
        private readonly Action _openPluginsAction;
        private readonly Action _restartAction;
        private readonly Action _exitAction;
        private DateTime _lastDeactivatedUtc = DateTime.MinValue;

        public TrayMenuWindow(
            Action toggleAction,
            Action openSettingsAction,
            Action openPluginsAction,
            Action restartAction,
            Action exitAction)
        {
            _toggleAction = toggleAction;
            _openSettingsAction = openSettingsAction;
            _openPluginsAction = openPluginsAction;
            _restartAction = restartAction;
            _exitAction = exitAction;

            InitializeComponent();
            this.Deactivated += TrayMenuWindow_Deactivated;
        }

        private void TrayMenuWindow_Deactivated(object? sender, EventArgs e)
        {
            _lastDeactivatedUtc = DateTime.UtcNow;
            if (this.IsVisible)
            {
                this.Hide();
            }
        }

        public void ToggleMenu(bool isOverlayVisible, string hotkeyDisplay)
        {
            if (this.IsVisible)
            {
                this.Hide();
                return;
            }

            // Prevent immediate reopen if clicking the tray icon deactivated the popup
            if ((DateTime.UtcNow - _lastDeactivatedUtc).TotalMilliseconds < 220)
            {
                return;
            }

            ShowAtCursor(isOverlayVisible, hotkeyDisplay);
        }

        public void ShowAtCursor(bool isOverlayVisible, string hotkeyDisplay)
        {
            OverlayStatusText.Text = isOverlayVisible ? "Active" : "Standby";
            HotkeyBadgeText.Text = string.IsNullOrWhiteSpace(hotkeyDisplay) ? "Alt + Space" : hotkeyDisplay;

            // Ensure native HWND exists and force offscreen first to measure cleanly
            WindowInteropHelper helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.EnsureHandle();

            this.Left = -10000;
            this.Top = -10000;
            this.Show();
            this.UpdateLayout();

            PositionNearCursor();

            // Win32 foreground + topmost promotion required when invoked from WinForms NotifyIcon / taskbar
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            this.Activate();
        }

        private void PositionNearCursor()
        {
            GetCursorPos(out WIN32_POINT cursorPt);

            Matrix transform = Matrix.Identity;
            PresentationSource? source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                transform = source.CompositionTarget.TransformFromDevice;
            }

            System.Windows.Point dipCursor = transform.Transform(new System.Windows.Point(cursorPt.X, cursorPt.Y));
            Rect workArea = SystemParameters.WorkArea;

            double menuW = this.ActualWidth > 0 ? this.ActualWidth : 280;
            double menuH = this.ActualHeight > 0 ? this.ActualHeight : 290;

            double left = dipCursor.X - menuW + 24;
            double top = dipCursor.Y - menuH - 12;

            // Clamp inside screen work area so menu sits cleanly above taskbar
            if (left + menuW > workArea.Right - 8)
                left = workArea.Right - menuW - 8;
            if (left < workArea.Left + 8)
                left = workArea.Left + 8;

            if (top < workArea.Top + 8)
                top = dipCursor.Y + 12;
            if (top + menuH > workArea.Bottom - 4)
                top = workArea.Bottom - menuH - 4;

            this.Left = left;
            this.Top = top;
        }

        private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _toggleAction();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _openSettingsAction();
        }

        private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _openPluginsAction();
        }

        private void RestartApp_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _restartAction();
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _exitAction();
        }
    }
}
