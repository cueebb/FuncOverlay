using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using FuncOverlay.Core;

namespace FuncOverlay
{
    public partial class MainWindow : Window
    {
        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint VK_RETURN = 0x0D;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly PluginManager _pluginManager;
        private bool _isOverlayVisible = true;

        public string PathHintText { get; set; } = "{FuncOverlay_folder}/plugins/{plugin_folder}";

        public MainWindow(PluginManager pluginManager)
        {
            _pluginManager = pluginManager;
            InitializeComponent();
            DataContext = this;
            ReloadOverlayContent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HOTKEY_ID, MOD_ALT, VK_RETURN);

            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(HwndHook);

            // Initially fade in overlay
            AnimateOpacity(1.0);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleOverlayWithAnimation();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void ToggleOverlayWithAnimation()
        {
            _isOverlayVisible = !_isOverlayVisible;
            AnimateOpacity(_isOverlayVisible ? 1.0 : 0.0);
        }

        private void AnimateOpacity(double targetOpacity)
        {
            DoubleAnimation fade = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromSeconds(0.25),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            fade.Completed += (s, e) =>
            {
                if (targetOpacity == 0)
                {
                    this.Visibility = Visibility.Collapsed;
                }
            };

            if (targetOpacity > 0)
            {
                this.Visibility = Visibility.Visible;
            }

            OverlayRootGrid.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public void ReloadOverlayContent()
        {
            if (_pluginManager.LoadedPlugins.Count == 0)
            {
                NoPluginsCenterBorder.Visibility = Visibility.Visible;
                PluginsCanvas.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoPluginsCenterBorder.Visibility = Visibility.Collapsed;
                PluginsCanvas.Visibility = Visibility.Visible;
            }
        }

        public void OpenSettingsWindow()
        {
            System.Windows.MessageBox.Show("FuncOverlay Settings\n\nGlobal Hotkey: Alt+Enter\nTransition: 0.25s\nPlugins Root: " + _pluginManager.PluginsDirectory,
                "FuncOverlay Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnClosed(EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID);
            base.OnClosed(e);
        }
    }
}
