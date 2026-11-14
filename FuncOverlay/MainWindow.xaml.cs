using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FuncOverlay.Core;
using FuncOverlay.Tray;

using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
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
        private readonly PluginIntentRouter _intentRouter;
        private readonly PluginHostService _pluginHostService;
        private readonly DispatcherTimer _overlayToastTimer;
        private readonly Dictionary<string, Border> _dynamicBodyContainers =
            new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PluginRenderPayload> _latestPayloads =
            new Dictionary<string, PluginRenderPayload>(StringComparer.OrdinalIgnoreCase);

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
            _intentRouter = new PluginIntentRouter(_pluginManager, this);
            _pluginHostService = new PluginHostService(this.Dispatcher, _intentRouter);
            _pluginHostService.PluginRenderReceived += OnPluginRenderReceived;
            _pluginHostService.PluginHostNotice += msg => ShowOverlayToast(msg, 4500);

            _overlayToastTimer = new DispatcherTimer();
            _overlayToastTimer.Tick += (s, e) =>
            {
                _overlayToastTimer.Stop();
                OverlayToastBorder.Visibility = Visibility.Collapsed;
            };

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

            // Start active plugins ONLY while overlay is open
            if (_isOverlayVisible)
            {
                _pluginHostService.StartAllForOpenOverlay(_pluginManager.LoadedPluginFolders);
            }

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

        public void ShowOverlayWithAnimation()
        {
            if (_isOverlayVisible) return;
            _isOverlayVisible = true;
            _pluginHostService.StartAllForOpenOverlay(_pluginManager.LoadedPluginFolders);
            AnimateOpacity(1.0);
        }

        public void HideOverlayWithAnimation()
        {
            if (!_isOverlayVisible) return;
            _isOverlayVisible = false;
            _pluginHostService.StopAllOnOverlayClosed();
            AnimateOpacity(0.0);
        }

        public void ToggleOverlayWithAnimation()
        {
            if (_isOverlayVisible)
            {
                HideOverlayWithAnimation();
            }
            else
            {
                ShowOverlayWithAnimation();
            }
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

        public void ShowOverlayToast(string message, int durationMs = 2500)
        {
            OverlayToastText.Text = message;
            OverlayToastBorder.Visibility = Visibility.Visible;
            _overlayToastTimer.Stop();
            _overlayToastTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(durationMs, 800, 8000));
            _overlayToastTimer.Start();
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

        public void NotifyPluginSettingsUpdated(string folderName)
        {
            var folder = _pluginManager.FindFolderByPluginId(folderName);
            if (folder != null)
            {
                _pluginHostService.NotifySettingsUpdated(folder);
            }
        }

        public void SyncSettingsWindowIfOpen()
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                _settingsWindow.RefreshFromMainWindow();
            }
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
            _pluginHostService.StopAllOnOverlayClosed();
            _dynamicBodyContainers.Clear();
            PluginsCanvas.Children.Clear();

            if (_pluginManager.LoadedPluginFolders.Count == 0)
            {
                NoPluginsCenterBorder.Visibility = Visibility.Visible;
                PluginsCanvas.Visibility = Visibility.Collapsed;
                return;
            }

            NoPluginsCenterBorder.Visibility = Visibility.Collapsed;
            PluginsCanvas.Visibility = Visibility.Visible;

            foreach (var folder in _pluginManager.LoadedPluginFolders)
            {
                if (!folder.Metadata.enabled) continue;

                Border widgetCard = CreateOverlayPluginWidget(folder);
                Canvas.SetLeft(widgetCard, Math.Max(16, folder.Metadata.position.x));
                Canvas.SetTop(widgetCard, Math.Max(16, folder.Metadata.position.y));
                PluginsCanvas.Children.Add(widgetCard);
            }

            if (_isOverlayVisible)
            {
                _pluginHostService.StartAllForOpenOverlay(_pluginManager.LoadedPluginFolders);
            }
        }

        private void OnPluginRenderReceived(string pluginId, PluginRenderPayload payload)
        {
            _latestPayloads[pluginId] = payload;
            if (_dynamicBodyContainers.TryGetValue(pluginId, out Border? bodyHost))
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(pluginId, payload);
            }
        }

        private Border CreateOverlayPluginWidget(PluginFolderItem folder)
        {
            PluginMetadata plugin = folder.Metadata;
            bool isObsShortcuts = string.Equals(plugin.widgetType, "obs_shortcuts", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(plugin.id, "obs_shortcuts", StringComparison.OrdinalIgnoreCase);

            double width = plugin.size.width >= 180 ? plugin.size.width : (isObsShortcuts ? 252 : 300);

            // Unified Gray Plugin Style
            Border card = new Border
            {
                Width = width,
                Background = new SolidColorBrush(MediaColor.FromArgb(240, 24, 24, 27)), // #F018181B
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(45, 45, 50)),       // #2D2D32
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true
            };

            // Prevent clicks inside plugin widget from closing the overlay
            card.MouseLeftButtonDown += (s, e) => e.Handled = true;
            card.MouseRightButtonDown += (s, e) => e.Handled = true;

            StackPanel root = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            // Compact Draggable Gray Header
            Border titleBar = new Border
            {
                Height = 32,
                Background = new SolidColorBrush(MediaColor.FromRgb(31, 31, 35)),       // #1F1F23
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(45, 45, 50)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 0, 10, 0),
                Cursor = System.Windows.Input.Cursors.SizeAll
            };

            Grid titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleText = new TextBlock
            {
                Text = plugin.name,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(244, 244, 245)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Border langBadge = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(45, 45, 50)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            langBadge.Child = new TextBlock
            {
                Text = plugin.language.ToUpperInvariant(),
                FontFamily = new MediaFontFamily("Consolas"),
                FontSize = 9,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(180, 180, 188))
            };

            Grid.SetColumn(titleText, 0);
            Grid.SetColumn(langBadge, 1);
            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(langBadge);
            titleBar.Child = titleGrid;

            // Drag Support on Plugin Card Header
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
                double cardHeight = card.ActualHeight > 0 ? card.ActualHeight : 100;
                double nx = Math.Max(8, Math.Min(ActualWidth - card.Width - 8, mousePos.X - dragOffset.X));
                double ny = Math.Max(8, Math.Min(ActualHeight - cardHeight - 8, mousePos.Y - dragOffset.Y));
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

            root.Children.Add(titleBar);

            // Compact Dynamic Body Container (no fixed height -> shrinks cleanly when no bottom status items exist)
            Border bodyHost = new Border
            {
                Padding = new Thickness(10)
            };
            _dynamicBodyContainers[plugin.id] = bodyHost;

            if (_latestPayloads.TryGetValue(plugin.id, out PluginRenderPayload? existingPayload))
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(plugin.id, existingPayload);
            }
            else if (isObsShortcuts)
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(plugin.id, CreateDefaultObsShortcutsPayload());
            }
            else
            {
                StackPanel fallbackBody = new StackPanel();
                fallbackBody.Children.Add(new TextBlock
                {
                    Text = plugin.description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                    TextWrapping = TextWrapping.Wrap
                });
                fallbackBody.Children.Add(new TextBlock
                {
                    Text = plugin.entryPoint,
                    FontFamily = new MediaFontFamily("Consolas"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                    Margin = new Thickness(0, 6, 0, 0)
                });
                bodyHost.Child = fallbackBody;
            }

            root.Children.Add(bodyHost);
            card.Child = root;
            return card;
        }

        private static PluginRenderPayload CreateDefaultObsShortcutsPayload()
        {
            return new PluginRenderPayload
            {
                type = "render",
                style = "gray",
                buttons = new List<PluginRenderButton>
                {
                    new PluginRenderButton { id = "toggle_record", dotColor = "#6a010e", tooltip = "Start Recording" },
                    new PluginRenderButton { id = "toggle_replay", secondaryAction = "save_replay", dotColor = "#015637", tooltip = "Start Replay Buffer (Right-click to Save)" },
                    new PluginRenderButton { id = "toggle_stream", dotColor = "#340f57", tooltip = "Start Broadcast" },
                    new PluginRenderButton { id = "open_obs", icon = "external_app", tooltip = "Open / Restore OBS Studio" }
                },
                statusItems = new List<PluginStatusItem>()
            };
        }

        private UIElement BuildInteractiveGrayPluginBody(string pluginId, PluginRenderPayload payload)
        {
            StackPanel container = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            // Row of Gray Shortcut Buttons (4 buttons in a row)
            if (payload.buttons != null && payload.buttons.Count > 0)
            {
                UniformGrid buttonRow = new UniformGrid
                {
                    Columns = payload.buttons.Count,
                    Rows = 1
                };

                for (int i = 0; i < payload.buttons.Count; i++)
                {
                    PluginRenderButton btn = payload.buttons[i];
                    Thickness margin = i < payload.buttons.Count - 1
                        ? new Thickness(0, 0, 6, 0)
                        : new Thickness(0);

                    Border grayButton = CreateGrayShortcutButton(pluginId, btn, margin);
                    buttonRow.Children.Add(grayButton);
                }

                container.Children.Add(buttonRow);
            }

            // Dynamic Compact Bottom Status Row (completely omitted when nothing is active -> zero blank space)
            if (payload.statusItems != null && payload.statusItems.Count > 0)
            {
                WrapPanel statusPanel = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2, 8, 2, 0)
                };

                foreach (var item in payload.statusItems)
                {
                    StackPanel itemRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 2, 12, 0)
                    };

                    Ellipse statusDot = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = ParseHexBrush(item.dotColor, MediaColor.FromRgb(161, 161, 170)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0)
                    };

                    TextBlock statusText = new TextBlock
                    {
                        Text = item.text,
                        FontFamily = new MediaFontFamily("Consolas"),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    itemRow.Children.Add(statusDot);
                    itemRow.Children.Add(statusText);
                    statusPanel.Children.Add(itemRow);
                }

                container.Children.Add(statusPanel);
            }

            return container;
        }

        private Border CreateGrayShortcutButton(string pluginId, PluginRenderButton btn, Thickness margin)
        {
            SolidColorBrush idleBg = new SolidColorBrush(MediaColor.FromRgb(42, 42, 46));      // #2A2A2E
            SolidColorBrush hoverBg = new SolidColorBrush(MediaColor.FromRgb(58, 58, 64));     // #3A3A40
            SolidColorBrush pressedBg = new SolidColorBrush(MediaColor.FromRgb(74, 74, 82));   // #4A4A52
            SolidColorBrush idleBorder = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70));  // #3F3F46
            SolidColorBrush activeBorder = new SolidColorBrush(MediaColor.FromRgb(92, 92, 102));

            Border buttonBox = new Border
            {
                Height = 38,
                Margin = margin,
                Background = idleBg,
                BorderBrush = btn.active ? activeBorder : idleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = string.IsNullOrEmpty(btn.tooltip) ? null : new System.Windows.Controls.ToolTip
                {
                    Content = btn.tooltip,
                    Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                    Foreground = MediaBrushes.White,
                    BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70))
                }
            };

            if (string.Equals(btn.icon, "external_app", StringComparison.OrdinalIgnoreCase))
            {
                // Vector "Open Another App" icon (box + diagonal launch arrow)
                Viewbox iconBox = new Viewbox
                {
                    Width = 15,
                    Height = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas iconCanvas = new Canvas { Width = 24, Height = 24 };
                System.Windows.Shapes.Path openPath = new System.Windows.Shapes.Path
                {
                    Stroke = new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)),
                    StrokeThickness = 2.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Data = Geometry.Parse("M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A2,2 0 0 1 16,21 H5 A2,2 0 0 1 3,19 V8 A2,2 0 0 1 5,6 H10")
                };
                iconCanvas.Children.Add(openPath);
                iconBox.Child = iconCanvas;
                buttonBox.Child = iconBox;
            }
            else
            {
                Ellipse dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = ParseHexBrush(btn.dotColor, MediaColor.FromRgb(161, 161, 170)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (btn.blinking)
                {
                    DoubleAnimation blinkAnim = new DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.22,
                        Duration = TimeSpan.FromMilliseconds(550),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    dot.BeginAnimation(UIElement.OpacityProperty, blinkAnim);
                }

                buttonBox.Child = dot;
            }

            buttonBox.MouseEnter += (s, e) => buttonBox.Background = hoverBg;
            buttonBox.MouseLeave += (s, e) => buttonBox.Background = idleBg;

            buttonBox.MouseLeftButtonDown += (s, e) =>
            {
                buttonBox.Background = pressedBg;
                DispatchPluginAction(pluginId, btn.id);
                e.Handled = true;
            };
            buttonBox.MouseLeftButtonUp += (s, e) =>
            {
                buttonBox.Background = buttonBox.IsMouseOver ? hoverBg : idleBg;
                e.Handled = true;
            };

            buttonBox.MouseRightButtonDown += (s, e) =>
            {
                if (!string.IsNullOrEmpty(btn.secondaryAction))
                {
                    buttonBox.Background = pressedBg;
                    DispatchPluginAction(pluginId, btn.secondaryAction);
                }
                e.Handled = true;
            };
            buttonBox.MouseRightButtonUp += (s, e) =>
            {
                buttonBox.Background = buttonBox.IsMouseOver ? hoverBg : idleBg;
                e.Handled = true;
            };

            return buttonBox;
        }

        private void DispatchPluginAction(string pluginId, string actionId)
        {
            if (_pluginHostService.SendAction(pluginId, actionId))
            {
                return;
            }

            // Try restarting plugin host once if it wasn't running yet
            _pluginHostService.StartAllForOpenOverlay(_pluginManager.LoadedPluginFolders);
            if (_pluginHostService.SendAction(pluginId, actionId))
            {
                return;
            }

            string reason = !string.IsNullOrWhiteSpace(_pluginHostService.LastStartupIssue)
                ? _pluginHostService.LastStartupIssue
                : "Plugin process is not running. Check %LocalAppData%\\FuncOverlay\\plugin_host.log";
            ShowOverlayToast(reason, 4500);
        }

        private static SolidColorBrush ParseHexBrush(string? hex, MediaColor fallback)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    object parsed = MediaColorConverter.ConvertFromString(hex);
                    if (parsed is MediaColor color)
                    {
                        return new SolidColorBrush(color);
                    }
                }
                catch { }
            }
            return new SolidColorBrush(fallback);
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
            _pluginHostService.Dispose();
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
