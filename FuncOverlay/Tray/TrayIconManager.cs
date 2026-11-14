using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;

namespace FuncOverlay.Tray
{
    public class TrayIconManager : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private TrayMenuWindow? _trayMenuWindow;

        public void Initialize(
            Action toggle,
            Action openSettings,
            Action restart,
            Func<bool> isOverlayVisible,
            Func<string> getHotkeyDisplay)
        {
            // Custom black & white WPF Tray popup window (no default Windows ContextMenuStrip)
            _trayMenuWindow = new TrayMenuWindow(
                toggleAction: toggle,
                openSettingsAction: openSettings,
                openPluginsAction: OpenPluginsFolder,
                restartAction: restart,
                exitAction: () => System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown())
            );

            // Load custom application icon if present
            Icon trayIcon = SystemIcons.Application;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                try
                {
                    trayIcon = new Icon(iconPath);
                }
                catch
                {
                    trayIcon = SystemIcons.Application;
                }
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = trayIcon,
                Text = "FuncOverlay",
                Visible = true
            };

            // Both left-click and right-click open the custom monochrome FuncOverlay tray popup
            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(() =>
                        {
                            _trayMenuWindow?.ToggleMenu(isOverlayVisible(), getHotkeyDisplay());
                        }));
                }
            };
        }

        public void ShowTrayMenuAtCursor(bool isOverlayVisible, string hotkeyDisplay)
        {
            _trayMenuWindow?.ShowAtCursor(isOverlayVisible, hotkeyDisplay);
        }

        private void OpenPluginsFolder()
        {
            string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            if (!Directory.Exists(pluginsPath))
            {
                Directory.CreateDirectory(pluginsPath);
            }
            Process.Start("explorer.exe", pluginsPath);
        }

        public void Dispose()
        {
            if (_trayMenuWindow != null)
            {
                _trayMenuWindow.Close();
                _trayMenuWindow = null;
            }
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
