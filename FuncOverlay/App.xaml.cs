using System;
using System.Windows;
using FuncOverlay.Tray;
using FuncOverlay.Core;

namespace FuncOverlay
{
    public partial class App : Application
    {
        private MainWindow? _mainWindow;
        private TrayIconManager? _trayManager;
        private PluginManager? _pluginManager;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Initialize Core Plugin Manager
            _pluginManager = new PluginManager();
            _pluginManager.LoadAllPlugins();

            // Initialize MainWindow (Transparent Overlay Window)
            _mainWindow = new MainWindow(_pluginManager);
            _mainWindow.Show();

            // Initialize System Tray (Icon in notification area)
            _trayManager = new TrayIconManager();
            _trayManager.Initialize(
                toggle: () => _mainWindow.ToggleOverlayWithAnimation(),
                openSettings: () => _mainWindow.OpenSettingsWindow(),
                restart: () => RestartApplication()
            );
        }

        private void RestartApplication()
        {
            _pluginManager?.LoadAllPlugins();
            _mainWindow?.ReloadOverlayContent();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _trayManager?.Dispose();
        }
    }
}
