using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

namespace FuncOverlay.Tray
{
    public class TrayIconManager : IDisposable
    {
        private NotifyIcon? _notifyIcon;

        public void Initialize(Action toggle, Action openSettings, Action restart)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Toggle Overlay (Alt+Space)", null, (s, e) => toggle());
            menu.Items.Add("Settings...", null, (s, e) => openSettings());
            menu.Items.Add("Open 'plugins' Folder", null, (s, e) => OpenPluginsFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Restart Application", null, (s, e) => restart());
            menu.Items.Add("Exit FuncOverlay", null, (s, e) => System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));

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
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => toggle();
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
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
