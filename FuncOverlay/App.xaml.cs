using System;
using System.Threading;
using System.Windows;
using FuncOverlay.Tray;
using FuncOverlay.Core;

namespace FuncOverlay
{
    public partial class App : System.Windows.Application
    {
        private const string SingleInstanceMutexName = @"Local\FuncOverlay_SingleInstance_Mutex";
        private const string ShowOverlayEventName = @"Local\FuncOverlay_ShowOverlay_Event";

        private static Mutex? _instanceMutex;
        private EventWaitHandle? _showOverlayEvent;
        private ManualResetEventSlim? _shutdownSignal;
        private MainWindow? _mainWindow;
        private TrayIconManager? _trayManager;
        private PluginManager? _pluginManager;

        private void Application_Startup(object sender, System.Windows.StartupEventArgs e)
        {
            _instanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    using EventWaitHandle existingEvt = EventWaitHandle.OpenExisting(ShowOverlayEventName);
                    existingEvt.Set();
                }
                catch { }

                Shutdown();
                return;
            }

            _showOverlayEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowOverlayEventName);
            _shutdownSignal = new ManualResetEventSlim(false);

            Thread singleInstanceListener = new Thread(() =>
            {
                WaitHandle[] handles = new WaitHandle[] { _showOverlayEvent, _shutdownSignal.WaitHandle };
                while (true)
                {
                    int idx = WaitHandle.WaitAny(handles);
                    if (idx == 0)
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_mainWindow != null)
                            {
                                _mainWindow.ShowOverlayWithAnimation();
                                _mainWindow.Activate();
                            }
                        });
                    }
                    else
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "FuncOverlaySingleInstanceListener"
            };
            singleInstanceListener.Start();

            // Initialize Core Plugin Manager
            _pluginManager = new PluginManager();
            _pluginManager.LoadAllPlugins();

            // Initialize MainWindow (Transparent Overlay Window)
            _mainWindow = new MainWindow(_pluginManager);
            _mainWindow.Show();

            // Initialize System Tray with custom black & white tray popup
            _trayManager = new TrayIconManager();
            _trayManager.Initialize(
                toggle: () => _mainWindow.ToggleOverlayWithAnimation(),
                openSettings: () => _mainWindow.OpenSettingsWindow(),
                restart: () => RestartApplication(),
                isOverlayVisible: () => _mainWindow.IsOverlayVisible,
                getHotkeyDisplay: () => _mainWindow.HotkeyDisplayString
            );

            _mainWindow.SetTrayManager(_trayManager);
        }

        private void RestartApplication()
        {
            _pluginManager?.LoadAllPlugins();
            _mainWindow?.ReloadOverlayContent();
        }

        private void Application_Exit(object sender, System.Windows.ExitEventArgs e)
        {
            _shutdownSignal?.Set();
            _trayManager?.Dispose();
            _showOverlayEvent?.Dispose();
            _shutdownSignal?.Dispose();

            try
            {
                _instanceMutex?.ReleaseMutex();
            }
            catch { }
            _instanceMutex?.Dispose();
            _instanceMutex = null;
        }
    }
}
