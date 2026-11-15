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
        private readonly Dictionary<string, (PluginFolderItem Folder, Border Card)> _pluginCards =
            new Dictionary<string, (PluginFolderItem Folder, Border Card)>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Border> _pluginTitleBars =
            new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);

        private TrayIconManager? _trayManager;
        private bool _isOverlayVisible = true;
        private IntPtr _windowHandle;
        private SettingsWindow? _settingsWindow;
        private string? _draggingPluginId;

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
            PluginsCanvas.SizeChanged += (s, e) => RepositionAllWidgets();
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
            if (_isOverlayVisible)
            {
                this.Topmost = true;
                this.Activate();
                return;
            }
            _isOverlayVisible = true;
            this.Visibility = Visibility.Visible;
            this.Topmost = true;
            _pluginHostService.StartAllForOpenOverlay(_pluginManager.LoadedPluginFolders);
            AnimateOpacity(1.0);
            this.Activate();
        }

        public void HideOverlayWithAnimation()
        {
            if (!_isOverlayVisible) return;
            _isOverlayVisible = false;
            _pluginHostService.StopAllOnOverlayClosed();
            AnimateOpacity(0.0);
        }

        public void HideOverlayImmediately()
        {
            _isOverlayVisible = false;
            _pluginHostService.StopAllOnOverlayClosed();
            OverlayRootGrid.BeginAnimation(UIElement.OpacityProperty, null);
            OverlayRootGrid.Opacity = 0;
            this.Topmost = false;
            this.Visibility = Visibility.Collapsed;
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
            _pluginCards.Clear();
            _pluginTitleBars.Clear();
            _draggingPluginId = null;
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
                _pluginCards[folder.Metadata.id] = (folder, widgetCard);
                PluginsCanvas.Children.Add(widgetCard);
                widgetCard.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            RepositionAllWidgets();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RepositionAllWidgets));

            if (_isOverlayVisible)
            {
                _pluginHostService.StartAllForOpenOverlay(_pluginManager.LoadedPluginFolders);
            }
        }

        private (double Width, double Height) GetCanvasSize()
        {
            double w = PluginsCanvas.ActualWidth > 0
                ? PluginsCanvas.ActualWidth
                : (this.ActualWidth > 0 ? this.ActualWidth : SystemParameters.PrimaryScreenWidth);
            double h = PluginsCanvas.ActualHeight > 0
                ? PluginsCanvas.ActualHeight
                : (this.ActualHeight > 0 ? this.ActualHeight : SystemParameters.PrimaryScreenHeight);
            return (w, h);
        }

        private static (double Width, double Height) GetCardSize(PluginFolderItem folder, Border card)
        {
            double w = card.ActualWidth > 0
                ? card.ActualWidth
                : (!double.IsNaN(card.Width) && card.Width > 0
                    ? card.Width
                    : (folder.Metadata.size.width > 0 ? folder.Metadata.size.width : 280));
            double h = card.ActualHeight > 0
                ? card.ActualHeight
                : (card.DesiredSize.Height > 0
                    ? card.DesiredSize.Height
                    : (folder.Metadata.size.height > 0 ? folder.Metadata.size.height : 96));
            return (w, h);
        }

        private static (string VAlign, string HAlign) SplitAnchor(string? rawAnchor)
        {
            string norm = PluginManager.NormalizeAnchor(rawAnchor);
            if (norm == "center") return ("center", "center");
            string[] parts = norm.Split('-');
            if (parts.Length == 2) return (parts[0], parts[1]);
            return ("top", "left");
        }

        private static (double X, double Y) GetPointOnRect(double left, double top, double width, double height, string? anchor)
        {
            var (v, h) = SplitAnchor(anchor);
            double x = h switch
            {
                "center" => left + (width / 2.0),
                "right" => left + width,
                _ => left
            };
            double y = v switch
            {
                "center" => top + (height / 2.0),
                "bottom" => top + height,
                _ => top
            };
            return (x, y);
        }

        private (double Left, double Top, double Width, double Height) ResolveWidgetBounds(
            string pluginId,
            Dictionary<string, (double Left, double Top, double Width, double Height)> resolved,
            HashSet<string> visiting)
        {
            if (resolved.TryGetValue(pluginId, out var existing))
            {
                return existing;
            }

            if (!_pluginCards.TryGetValue(pluginId, out var entry))
            {
                return (0, 0, 280, 96);
            }

            var (folder, card) = entry;
            var (w, h) = GetCardSize(folder, card);

            // If currently being dragged interactively, preserve its live canvas coordinates
            if (string.Equals(_draggingPluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                double dragL = Canvas.GetLeft(card);
                double dragT = Canvas.GetTop(card);
                if (double.IsNaN(dragL)) dragL = 0;
                if (double.IsNaN(dragT)) dragT = 0;
                var dragBounds = (dragL, dragT, w, h);
                resolved[pluginId] = dragBounds;
                return dragBounds;
            }

            var (canvasW, canvasH) = GetCanvasSize();
            PluginPosition pos = folder.Metadata.position ?? new PluginPosition();
            string wAnchor = PluginManager.NormalizeAnchor(pos.anchor);
            string targetId = (pos.anchorTarget ?? "screen").Trim();

            double ax, ay;
            if (!string.IsNullOrEmpty(targetId) &&
                !string.Equals(targetId, "screen", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetId, pluginId, StringComparison.OrdinalIgnoreCase) &&
                _pluginCards.ContainsKey(targetId) &&
                !visiting.Contains(targetId))
            {
                visiting.Add(pluginId);
                var targetBounds = ResolveWidgetBounds(targetId, resolved, visiting);
                visiting.Remove(pluginId);

                string tAnchor = string.IsNullOrWhiteSpace(pos.targetAnchor)
                    ? wAnchor
                    : PluginManager.NormalizeAnchor(pos.targetAnchor);
                (ax, ay) = GetPointOnRect(targetBounds.Left, targetBounds.Top, targetBounds.Width, targetBounds.Height, tAnchor);
            }
            else
            {
                (ax, ay) = GetPointOnRect(0, 0, canvasW, canvasH, wAnchor);
            }

            var (v, hAlign) = SplitAnchor(wAnchor);
            double left = hAlign switch
            {
                "center" => ax + pos.x - (w / 2.0),
                "right" => ax - pos.x - w,
                _ => ax + pos.x
            };
            double top = v switch
            {
                "center" => ay + pos.y - (h / 2.0),
                "bottom" => ay - pos.y - h,
                _ => ay + pos.y
            };

            var bounds = (left, top, w, h);
            resolved[pluginId] = bounds;
            return bounds;
        }

        public void RepositionAllWidgets()
        {
            if (_pluginCards.Count == 0) return;

            var resolved = new Dictionary<string, (double Left, double Top, double Width, double Height)>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in _pluginCards)
            {
                if (string.Equals(_draggingPluginId, kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bounds = ResolveWidgetBounds(kv.Key, resolved, visiting);
                Canvas.SetLeft(kv.Value.Card, bounds.Left);
                Canvas.SetTop(kv.Value.Card, bounds.Top);
            }
        }

        public void UpdatePluginLockState(string pluginIdOrFolder, bool locked)
        {
            var folder = _pluginManager.FindFolderByPluginId(pluginIdOrFolder);
            string id = folder?.Metadata.id ?? pluginIdOrFolder;
            if (folder != null)
            {
                folder.Metadata.locked = locked;
            }
            if (_pluginTitleBars.TryGetValue(id, out Border? tb))
            {
                tb.Cursor = locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll;
            }
        }

        private (int X, int Y) ComputeAnchorOffsetFromTopLeft(string pluginId, double left, double top)
        {
            if (!_pluginCards.TryGetValue(pluginId, out var entry))
            {
                return ((int)Math.Round(left), (int)Math.Round(top));
            }

            var (folder, card) = entry;
            var (w, h) = GetCardSize(folder, card);
            var (canvasW, canvasH) = GetCanvasSize();

            PluginPosition pos = folder.Metadata.position ?? new PluginPosition();
            string wAnchor = PluginManager.NormalizeAnchor(pos.anchor);
            string targetId = (pos.anchorTarget ?? "screen").Trim();

            double ax, ay;
            if (!string.IsNullOrEmpty(targetId) &&
                !string.Equals(targetId, "screen", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetId, pluginId, StringComparison.OrdinalIgnoreCase) &&
                _pluginCards.TryGetValue(targetId, out var targetEntry))
            {
                double tL = Canvas.GetLeft(targetEntry.Card);
                double tT = Canvas.GetTop(targetEntry.Card);
                if (double.IsNaN(tL)) tL = 0;
                if (double.IsNaN(tT)) tT = 0;
                var (tW, tH) = GetCardSize(targetEntry.Folder, targetEntry.Card);

                string tAnchor = string.IsNullOrWhiteSpace(pos.targetAnchor)
                    ? wAnchor
                    : PluginManager.NormalizeAnchor(pos.targetAnchor);
                (ax, ay) = GetPointOnRect(tL, tT, tW, tH, tAnchor);
            }
            else
            {
                (ax, ay) = GetPointOnRect(0, 0, canvasW, canvasH, wAnchor);
            }

            var (v, hAlign) = SplitAnchor(wAnchor);
            double x = hAlign switch
            {
                "center" => (left + (w / 2.0)) - ax,
                "right" => ax - (left + w),
                _ => left - ax
            };
            double y = v switch
            {
                "center" => (top + (h / 2.0)) - ay,
                "bottom" => ay - (top + h),
                _ => top - ay
            };

            return ((int)Math.Round(x), (int)Math.Round(y));
        }

        private void OnPluginRenderReceived(string pluginId, PluginRenderPayload payload)
        {
            _latestPayloads[pluginId] = payload;
            if (_dynamicBodyContainers.TryGetValue(pluginId, out Border? bodyHost))
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(pluginId, payload);
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RepositionAllWidgets));
            }
        }

        private Border CreateOverlayPluginWidget(PluginFolderItem folder)
        {
            PluginMetadata plugin = folder.Metadata;
            bool isObsShortcuts = string.Equals(plugin.widgetType, "obs_shortcuts", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(plugin.id, "obs_shortcuts", StringComparison.OrdinalIgnoreCase);
            bool isSimpleCalc = string.Equals(plugin.widgetType, "simple_calc", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(plugin.id, "simple_calc", StringComparison.OrdinalIgnoreCase);

            double width = plugin.size.width >= 180
                ? plugin.size.width
                : (isObsShortcuts ? 252 : (isSimpleCalc ? 256 : 300));

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

            // Keep anchors & dependent widgets aligned whenever card height changes dynamically
            card.SizeChanged += (s, e) => RepositionAllWidgets();

            // Prevent clicks inside plugin widget from closing the overlay
            card.MouseLeftButtonDown += (s, e) => e.Handled = true;
            card.MouseRightButtonDown += (s, e) => e.Handled = true;

            StackPanel root = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            // Compact Draggable Gray Header (unless position is locked)
            Border titleBar = new Border
            {
                Height = 32,
                Background = new SolidColorBrush(MediaColor.FromRgb(31, 31, 35)),       // #1F1F23
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(45, 45, 50)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 0, 10, 0),
                Cursor = plugin.locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll
            };
            _pluginTitleBars[plugin.id] = titleBar;

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

            // Drag Support on Plugin Card Header (blocked when plugin.locked is true)
            bool isDragging = false;
            System.Windows.Point dragOffset = new System.Windows.Point();
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (folder.Metadata.locked)
                {
                    e.Handled = true;
                    return;
                }
                isDragging = true;
                _draggingPluginId = plugin.id;
                System.Windows.Point mousePos = e.GetPosition(PluginsCanvas);
                double curLeft = Canvas.GetLeft(card);
                double curTop = Canvas.GetTop(card);
                if (double.IsNaN(curLeft)) curLeft = 0;
                if (double.IsNaN(curTop)) curTop = 0;
                dragOffset = new System.Windows.Point(mousePos.X - curLeft, mousePos.Y - curTop);
                titleBar.CaptureMouse();
                e.Handled = true;
            };
            titleBar.MouseMove += (s, e) =>
            {
                if (!isDragging || folder.Metadata.locked) return;
                System.Windows.Point mousePos = e.GetPosition(PluginsCanvas);
                var (canvasW, canvasH) = GetCanvasSize();
                double cardHeight = card.ActualHeight > 0 ? card.ActualHeight : 100;
                double nx = Math.Max(8, Math.Min(Math.Max(8, canvasW - card.Width - 8), mousePos.X - dragOffset.X));
                double ny = Math.Max(8, Math.Min(Math.Max(8, canvasH - cardHeight - 8), mousePos.Y - dragOffset.Y));
                Canvas.SetLeft(card, nx);
                Canvas.SetTop(card, ny);
                RepositionAllWidgets();
            };
            titleBar.MouseLeftButtonUp += (s, e) =>
            {
                if (!isDragging) return;
                isDragging = false;
                _draggingPluginId = null;
                titleBar.ReleaseMouseCapture();

                double finalLeft = Canvas.GetLeft(card);
                double finalTop = Canvas.GetTop(card);
                if (double.IsNaN(finalLeft)) finalLeft = 0;
                if (double.IsNaN(finalTop)) finalTop = 0;
                var (offsetX, offsetY) = ComputeAnchorOffsetFromTopLeft(plugin.id, finalLeft, finalTop);
                _pluginManager.UpdatePluginPosition(plugin.id, offsetX, offsetY);
                RepositionAllWidgets();
                SyncSettingsWindowIfOpen();
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
            else if (isSimpleCalc)
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(plugin.id, CreateDefaultSimpleCalcPayload());
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

        private static PluginRenderPayload CreateDefaultSimpleCalcPayload()
        {
            return new PluginRenderPayload
            {
                type = "render",
                style = "gray",
                layout = "calculator",
                display = new PluginDisplayPayload { primary = "0", secondary = string.Empty },
                keypad = new List<List<PluginKeypadButton>>
                {
                    new List<PluginKeypadButton>
                    {
                        new PluginKeypadButton { id = "ac", label = "AC", variant = "operator" },
                        new PluginKeypadButton { id = "parens", label = "()", variant = "operator" },
                        new PluginKeypadButton { id = "sqrt", icon = "sqrt", label = "√", variant = "operator" },
                        new PluginKeypadButton { id = "div", label = "÷", variant = "operator" }
                    },
                    new List<PluginKeypadButton>
                    {
                        new PluginKeypadButton { id = "7", label = "7", variant = "digit" },
                        new PluginKeypadButton { id = "8", label = "8", variant = "digit" },
                        new PluginKeypadButton { id = "9", label = "9", variant = "digit" },
                        new PluginKeypadButton { id = "mul", label = "×", variant = "operator" }
                    },
                    new List<PluginKeypadButton>
                    {
                        new PluginKeypadButton { id = "4", label = "4", variant = "digit" },
                        new PluginKeypadButton { id = "5", label = "5", variant = "digit" },
                        new PluginKeypadButton { id = "6", label = "6", variant = "digit" },
                        new PluginKeypadButton { id = "sub", label = "−", variant = "operator" }
                    },
                    new List<PluginKeypadButton>
                    {
                        new PluginKeypadButton { id = "1", label = "1", variant = "digit" },
                        new PluginKeypadButton { id = "2", label = "2", variant = "digit" },
                        new PluginKeypadButton { id = "3", label = "3", variant = "digit" },
                        new PluginKeypadButton { id = "add", label = "+", variant = "operator" }
                    },
                    new List<PluginKeypadButton>
                    {
                        new PluginKeypadButton { id = "0", label = "0", variant = "digit" },
                        new PluginKeypadButton { id = "dot", label = ".", variant = "digit" },
                        new PluginKeypadButton { id = "backspace", icon = "backspace", label = "⌫", variant = "digit" },
                        new PluginKeypadButton { id = "equals", label = "=", variant = "equals" }
                    }
                }
            };
        }

        private UIElement BuildInteractiveGrayPluginBody(string pluginId, PluginRenderPayload payload)
        {
            if (string.Equals(payload.layout, "calculator", StringComparison.OrdinalIgnoreCase) ||
                (payload.keypad != null && payload.keypad.Count > 0))
            {
                return BuildCalculatorPluginBody(pluginId, payload);
            }

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

        private UIElement BuildCalculatorPluginBody(string pluginId, PluginRenderPayload payload)
        {
            StackPanel root = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            PluginDisplayPayload disp = payload.display ?? new PluginDisplayPayload { primary = "0" };
            string primary = string.IsNullOrEmpty(disp.primary) ? "0" : disp.primary;
            double primaryFontSize = primary.Length <= 9
                ? 28
                : (primary.Length <= 13 ? 22 : (primary.Length <= 18 ? 17.5 : 14.5));

            // Compact Two-Line Display Area (keeps fixed height so keypad never shifts on '=')
            Grid displayGrid = new Grid
            {
                Height = 60,
                Margin = new Thickness(4, 2, 4, 10)
            };
            displayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            displayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            TextBlock primaryText = new TextBlock
            {
                Text = primary,
                FontFamily = new MediaFontFamily("Segoe UI, Consolas"),
                FontSize = primaryFontSize,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(244, 244, 245)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(primaryText, 0);
            displayGrid.Children.Add(primaryText);

            // Secondary / Mixed Fraction line (only visible after '=' when result has a fraction)
            if (!string.IsNullOrEmpty(disp.fractionNum) && !string.IsNullOrEmpty(disp.fractionDen))
            {
                SolidColorBrush fracBrush = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216));
                StackPanel fractionRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (string.Equals(disp.fractionWhole, "-", StringComparison.Ordinal))
                {
                    fractionRow.Children.Add(new TextBlock
                    {
                        Text = "−",
                        FontSize = 15.5,
                        Foreground = fracBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 2, 0)
                    });
                }
                else if (!string.IsNullOrEmpty(disp.fractionWhole))
                {
                    fractionRow.Children.Add(new TextBlock
                    {
                        Text = disp.fractionWhole,
                        FontSize = 16.5,
                        Foreground = fracBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 4, 0)
                    });
                }

                fractionRow.Children.Add(new TextBlock
                {
                    Text = disp.fractionNum,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = fracBrush,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 1, 0)
                });
                fractionRow.Children.Add(new TextBlock
                {
                    Text = "/",
                    FontSize = 13.5,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 1, 0)
                });
                fractionRow.Children.Add(new TextBlock
                {
                    Text = disp.fractionDen,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = fracBrush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 1)
                });

                Grid.SetRow(fractionRow, 1);
                displayGrid.Children.Add(fractionRow);
            }
            else if (!string.IsNullOrEmpty(disp.secondary))
            {
                TextBlock secondaryText = new TextBlock
                {
                    Text = disp.secondary,
                    FontSize = 15.5,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(secondaryText, 1);
                displayGrid.Children.Add(secondaryText);
            }

            root.Children.Add(displayGrid);

            // 5x4 Circular Button Keypad
            var rows = payload.keypad ?? CreateDefaultSimpleCalcPayload().keypad!;
            StackPanel keypadPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            for (int r = 0; r < rows.Count; r++)
            {
                var rowButtons = rows[r];
                Grid rowGrid = new Grid
                {
                    Margin = new Thickness(0, 0, 0, r < rows.Count - 1 ? 6 : 0)
                };
                for (int c = 0; c < rowButtons.Count; c++)
                {
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Border keyBtn = CreateCalculatorCircleButton(pluginId, rowButtons[c]);
                    Grid.SetColumn(keyBtn, c);
                    rowGrid.Children.Add(keyBtn);
                }
                keypadPanel.Children.Add(rowGrid);
            }

            root.Children.Add(keypadPanel);
            return root;
        }

        private Border CreateCalculatorCircleButton(string pluginId, PluginKeypadButton btn)
        {
            string variant = (btn.variant ?? "digit").ToLowerInvariant();

            MediaColor idleColor;
            MediaColor hoverColor;
            MediaColor pressedColor;
            MediaColor fgColor;

            if (variant == "equals")
            {
                idleColor = MediaColor.FromRgb(207, 212, 232);    // #CFD4E8
                hoverColor = MediaColor.FromRgb(220, 224, 240);   // #DCE0F0
                pressedColor = MediaColor.FromRgb(184, 190, 214); // #B8BED6
                fgColor = MediaColor.FromRgb(24, 24, 27);         // #18181B
            }
            else if (variant == "operator")
            {
                idleColor = MediaColor.FromRgb(58, 61, 73);       // #3A3D49
                hoverColor = MediaColor.FromRgb(72, 76, 90);      // #484C5A
                pressedColor = MediaColor.FromRgb(86, 90, 106);   // #565A6A
                fgColor = MediaColor.FromRgb(244, 244, 245);      // #F4F4F5
            }
            else
            {
                idleColor = MediaColor.FromRgb(43, 44, 48);       // #2B2C30
                hoverColor = MediaColor.FromRgb(56, 58, 64);      // #383A40
                pressedColor = MediaColor.FromRgb(70, 72, 80);    // #464850
                fgColor = MediaColor.FromRgb(244, 244, 245);      // #F4F4F5
            }

            SolidColorBrush idleBg = new SolidColorBrush(idleColor);
            SolidColorBrush hoverBg = new SolidColorBrush(hoverColor);
            SolidColorBrush pressedBg = new SolidColorBrush(pressedColor);
            SolidColorBrush fgBrush = new SolidColorBrush(fgColor);

            Border circle = new Border
            {
                Width = 52,
                Height = 52,
                Background = idleBg,
                CornerRadius = new CornerRadius(26),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            if (string.Equals(btn.icon, "sqrt", StringComparison.OrdinalIgnoreCase))
            {
                // Sleek geometric square root vector glyph (clean tick + V + top vinculum)
                Viewbox iconBox = new Viewbox
                {
                    Width = 19,
                    Height = 19,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas iconCanvas = new Canvas { Width = 24, Height = 24 };
                System.Windows.Shapes.Path sqrtPath = new System.Windows.Shapes.Path
                {
                    Stroke = fgBrush,
                    StrokeThickness = 1.9,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Data = Geometry.Parse("M3,13.5 L6.2,13.5 L9.8,19.5 L14,5.5 L21,5.5")
                };
                iconCanvas.Children.Add(sqrtPath);
                iconBox.Child = iconCanvas;
                circle.Child = iconBox;
            }
            else if (string.Equals(btn.icon, "backspace", StringComparison.OrdinalIgnoreCase))
            {
                // Clean backspace pentagon + X glyph
                Viewbox iconBox = new Viewbox
                {
                    Width = 19,
                    Height = 19,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas iconCanvas = new Canvas { Width = 24, Height = 24 };
                System.Windows.Shapes.Path bsPath = new System.Windows.Shapes.Path
                {
                    Stroke = fgBrush,
                    StrokeThickness = 1.75,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Data = Geometry.Parse("M20,6 H9.5 L4,12 L9.5,18 H20 A2,2 0 0 0 22,16 V8 A2,2 0 0 0 20,6 Z M12.2,9.8 L16.8,14.2 M16.8,9.8 L12.2,14.2")
                };
                iconCanvas.Children.Add(bsPath);
                iconBox.Child = iconCanvas;
                circle.Child = iconBox;
            }
            else
            {
                double fontSize = btn.label == "AC"
                    ? 17
                    : (btn.label == "()" ? 18.5 : (variant == "operator" || variant == "equals" ? 22 : 20));
                circle.Child = new TextBlock
                {
                    Text = btn.label,
                    FontFamily = new MediaFontFamily("Segoe UI, Arial"),
                    FontSize = fontSize,
                    FontWeight = variant == "equals" ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = fgBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                };
            }

            circle.MouseEnter += (s, e) => circle.Background = hoverBg;
            circle.MouseLeave += (s, e) => circle.Background = idleBg;
            circle.MouseLeftButtonDown += (s, e) =>
            {
                circle.Background = pressedBg;
                DispatchPluginAction(pluginId, btn.id);
                e.Handled = true;
            };
            circle.MouseLeftButtonUp += (s, e) =>
            {
                circle.Background = circle.IsMouseOver ? hoverBg : idleBg;
                e.Handled = true;
            };

            return circle;
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
