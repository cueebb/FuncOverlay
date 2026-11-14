using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FuncOverlay.Core;
using FuncOverlay.Tray;

using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace FuncOverlay
{
    public partial class MainWindow : Window
    {
        private const int HOTKEY_ID = 9000;
        private uint _currentModifiers = 0x0001; // MOD_ALT
        private uint _currentVk = 0x20;          // VK_SPACE

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly PluginManager _pluginManager;
        private TrayIconManager? _trayManager;
        private bool _isOverlayVisible = true;
        private IntPtr _windowHandle;
        private SettingsWindow? _settingsWindow;

        public bool IsOverlayVisible => _isOverlayVisible;
        public int FadeDurationMs { get; set; } = 250;
        public double OverlayOpacity { get; set; } = 0.40;
        public string HotkeyDisplayString { get; set; } = "Alt + Space";
        public string PathHintText { get; set; } = "{FuncOverlay_folder}/plugins/{plugin_folder}";

        public MainWindow(PluginManager pluginManager)
        {
            _pluginManager = pluginManager;

            // Load persisted settings from %LocalAppData%\FuncOverlay\settings.json
            AppSettingsData saved = AppSettingsManager.Load();
            FadeDurationMs = saved.FadeDurationMs;
            OverlayOpacity = saved.OverlayOpacity;
            HotkeyDisplayString = saved.HotkeyDisplay;
            _currentModifiers = saved.HotkeyModifiers;
            _currentVk = saved.HotkeyVk != 0 ? saved.HotkeyVk : 0x20u;

            InitializeComponent();
            DataContext = this;
            RefreshBackdropTint();
            ReloadOverlayContent();
        }

        public void SetTrayManager(TrayIconManager trayManager)
        {
            _trayManager = trayManager;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(_windowHandle, HOTKEY_ID, _currentModifiers, _currentVk);

            HwndSource source = HwndSource.FromHwnd(_windowHandle);
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

        private void BackdropTint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isOverlayVisible)
            {
                HideOverlayWithAnimation();
                e.Handled = true;
            }
        }

        private void BackdropTint_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isOverlayVisible)
            {
                _trayManager?.ShowTrayMenuAtCursor(IsOverlayVisible, HotkeyDisplayString);
                e.Handled = true;
            }
        }

        public void RefreshBackdropTint()
        {
            byte alphaByte = (byte)Math.Clamp(OverlayOpacity * 255, 1, 255);
            BackdropTint.Background = new SolidColorBrush(MediaColor.FromArgb(alphaByte, 0, 0, 0));
        }

        public void HideOverlayWithAnimation()
        {
            if (!_isOverlayVisible) return;
            _isOverlayVisible = false;
            AnimateOpacity(0.0);
        }

        public void ToggleOverlayWithAnimation()
        {
            _isOverlayVisible = !_isOverlayVisible;
            AnimateOpacity(_isOverlayVisible ? 1.0 : 0.0);
        }

        private void AnimateOpacity(double targetOpacity)
        {
            RefreshBackdropTint();

            DoubleAnimation fade = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(FadeDurationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            fade.Completed += (s, e) =>
            {
                if (targetOpacity == 0 && !_isOverlayVisible)
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

        public void SaveSettings()
        {
            AppSettingsManager.Save(new AppSettingsData
            {
                FadeDurationMs = this.FadeDurationMs,
                OverlayOpacity = this.OverlayOpacity,
                HotkeyDisplay = this.HotkeyDisplayString,
                HotkeyModifiers = _currentModifiers,
                HotkeyVk = _currentVk
            });
        }

        public void UpdateHotkey(string display, ModifierKeys modifiers, Key key)
        {
            HotkeyDisplayString = display;

            uint fsMod = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) fsMod |= 0x0001;
            if (modifiers.HasFlag(ModifierKeys.Control)) fsMod |= 0x0002;
            if (modifiers.HasFlag(ModifierKeys.Shift)) fsMod |= 0x0004;

            int vkCode = KeyInterop.VirtualKeyFromKey(key);
            uint vk = (uint)vkCode;

            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_ID);
                bool success = RegisterHotKey(_windowHandle, HOTKEY_ID, fsMod, vk);
                if (success)
                {
                    _currentModifiers = fsMod;
                    _currentVk = vk;
                }
            }

            SaveSettings();
        }

        public void ReloadOverlayContent()
        {
            PluginsCanvas.Children.Clear();

            if (_pluginManager.LoadedPluginFolders.Count == 0)
            {
                NoPluginsCenterBorder.Visibility = Visibility.Visible;
                PluginsCanvas.Visibility = Visibility.Collapsed;
                return;
            }

            NoPluginsCenterBorder.Visibility = Visibility.Collapsed;
            PluginsCanvas.Visibility = Visibility.Visible;

            foreach (var plugin in _pluginManager.LoadedPlugins)
            {
                Border widgetCard = CreateOverlayPluginWidget(plugin);
                Canvas.SetLeft(widgetCard, Math.Max(16, plugin.position.x));
                Canvas.SetTop(widgetCard, Math.Max(16, plugin.position.y));
                PluginsCanvas.Children.Add(widgetCard);
            }
        }

        private Border CreateOverlayPluginWidget(PluginMetadata plugin)
        {
            double width = plugin.size.width > 160 ? plugin.size.width : 320;
            double height = plugin.size.height > 120 ? plugin.size.height : 220;

            Border card = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(MediaColor.FromArgb(238, 9, 9, 11)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                ClipToBounds = true
            };

            // Prevent clicks inside plugin widget from closing the overlay
            card.MouseLeftButtonDown += (s, e) => e.Handled = true;
            card.MouseRightButtonDown += (s, e) => e.Handled = true;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            // Draggable Plugin Title Bar
            Border titleBar = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 0, 12, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            Grid titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleText = new TextBlock
            {
                Text = plugin.name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = MediaBrushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Border langBadge = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            langBadge.Child = new TextBlock
            {
                Text = plugin.language.ToUpperInvariant(),
                FontFamily = new MediaFontFamily("Consolas"),
                FontSize = 9,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216))
            };

            Grid.SetColumn(titleText, 0);
            Grid.SetColumn(langBadge, 1);
            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(langBadge);
            titleBar.Child = titleGrid;

            // Drag Support on Plugin Card Title Bar
            bool isDragging = false;
            System.Windows.Point dragOffset = new System.Windows.Point();
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                isDragging = true;
                System.Windows.Point mousePos = e.GetPosition(PluginsCanvas);
                dragOffset = new System.Windows.Point(mousePos.X - Canvas.GetLeft(card), mousePos.Y - Canvas.GetTop(card));
                titleBar.CaptureMouse();
                e.Handled = true;
            };
            titleBar.MouseMove += (s, e) =>
            {
                if (!isDragging) return;
                System.Windows.Point mousePos = e.GetPosition(PluginsCanvas);
                double nx = Math.Max(8, Math.Min(ActualWidth - card.Width - 8, mousePos.X - dragOffset.X));
                double ny = Math.Max(8, Math.Min(ActualHeight - card.Height - 8, mousePos.Y - dragOffset.Y));
                Canvas.SetLeft(card, nx);
                Canvas.SetTop(card, ny);
            };
            titleBar.MouseLeftButtonUp += (s, e) =>
            {
                if (!isDragging) return;
                isDragging = false;
                titleBar.ReleaseMouseCapture();
                _pluginManager.UpdatePluginPosition(plugin.id, (int)Canvas.GetLeft(card), (int)Canvas.GetTop(card));
                e.Handled = true;
            };

            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            // Plugin Body
            StackPanel body = new StackPanel { Margin = new Thickness(14) };
            body.Children.Add(new TextBlock
            {
                Text = plugin.description,
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // Plugin Footer
            Border footer = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromArgb(160, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 0, 12, 0)
            };
            footer.Child = new TextBlock
            {
                Text = plugin.entryPoint,
                FontFamily = new MediaFontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            card.Child = root;
            return card;
        }

        public void OpenSettingsWindow()
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                _settingsWindow.Show();
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(_pluginManager, this);
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_ID);
            }
            if (_settingsWindow != null)
            {
                _settingsWindow.Close();
                _settingsWindow = null;
            }
            base.OnClosed(e);
        }
    }
}
