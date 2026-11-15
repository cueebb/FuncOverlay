using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FuncOverlay.Core;
using FuncOverlay.Tray;

using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaBrush = System.Windows.Media.Brush;
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
        private readonly MediaSessionService _mediaSessionService;
        private readonly DispatcherTimer _overlayToastTimer;
        private readonly Dictionary<string, Border> _dynamicBodyContainers =
            new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PluginRenderPayload> _latestPayloads =
            new Dictionary<string, PluginRenderPayload>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (PluginFolderItem Folder, Border Card)> _pluginCards =
            new Dictionary<string, (PluginFolderItem Folder, Border Card)>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Border> _pluginTitleBars =
            new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBox> _searchInputBoxes =
            new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MediaControllerViewHolder> _mediaViews =
            new Dictionary<string, MediaControllerViewHolder>(StringComparer.OrdinalIgnoreCase);

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
            _mediaSessionService = new MediaSessionService(this.Dispatcher);
            _mediaSessionService.SnapshotUpdated += OnMediaSnapshotUpdated;
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
            _mediaSessionService.Stop();
            foreach (var mv in _mediaViews.Values) mv.CloseSourcePopup();
            _pluginHostService.StopAllOnOverlayClosed();
            AnimateOpacity(0.0);
        }

        public void HideOverlayImmediately()
        {
            _isOverlayVisible = false;
            _mediaSessionService.Stop();
            foreach (var mv in _mediaViews.Values) mv.CloseSourcePopup();
            _pluginHostService.StopAllOnOverlayClosed();
            OverlayRootGrid.BeginAnimation(UIElement.OpacityProperty, null);
            OverlayRootGrid.Opacity = 0;
            this.Topmost = false;
            this.Visibility = Visibility.Collapsed;
        }

        public void StartMediaMonitoring()
        {
            if (_isOverlayVisible)
            {
                _mediaSessionService.Start();
            }
        }

        public void HandleMediaControlIntent(string command, string? sourceId, double? positionSec)
        {
            _ = _mediaSessionService.ControlAsync(command, sourceId, positionSec);
        }

        public void HandleMediaSelectSourceIntent(string sourceId, bool focusWindow)
        {
            _ = _mediaSessionService.SelectSourceAsync(sourceId, focusWindow, _windowHandle);
        }

        private void OnMediaSnapshotUpdated(MediaSnapshot snapshot)
        {
            foreach (var mv in _mediaViews.Values)
            {
                mv.UpdateSnapshot(snapshot);
            }
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
            _mediaSessionService.Stop();
            _pluginHostService.StopAllOnOverlayClosed();
            _dynamicBodyContainers.Clear();
            _pluginCards.Clear();
            _pluginTitleBars.Clear();
            _searchInputBoxes.Clear();
            foreach (var mv in _mediaViews.Values) mv.CloseSourcePopup();
            _mediaViews.Clear();
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
                if (string.Equals(payload.layout, "media_controller", StringComparison.OrdinalIgnoreCase) &&
                    _mediaViews.TryGetValue(pluginId, out MediaControllerViewHolder? existingMediaView) &&
                    ReferenceEquals(bodyHost.Child, existingMediaView.RootElement))
                {
                    existingMediaView.ApplyConfig(payload.mediaConfig);
                    existingMediaView.UpdateSnapshot(_mediaSessionService.LatestSnapshot);
                    return;
                }

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
            bool isShareXShortcuts = string.Equals(plugin.widgetType, "sharex_shortcuts", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(plugin.id, "sharex_shortcuts", StringComparison.OrdinalIgnoreCase);
            bool isPowerToysShortcuts = string.Equals(plugin.widgetType, "powertoys_shortcuts", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(plugin.id, "powertoys_shortcuts", StringComparison.OrdinalIgnoreCase);
            bool isCircleToSearch = string.Equals(plugin.widgetType, "circle_to_search", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(plugin.id, "circle_to_search", StringComparison.OrdinalIgnoreCase);
            bool isMusicController = string.Equals(plugin.widgetType, "music_controller", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(plugin.id, "music_controller", StringComparison.OrdinalIgnoreCase);

            double width = plugin.size.width >= 180
                ? plugin.size.width
                : (isObsShortcuts ? 252 : (isSimpleCalc ? 256 : ((isShareXShortcuts || isPowerToysShortcuts) ? 296 : (isCircleToSearch ? 348 : (isMusicController ? 440 : 300)))));

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
            else if (isShareXShortcuts)
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(plugin.id, CreateDefaultShareXShortcutsPayload());
            }
            else if (isPowerToysShortcuts)
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(plugin.id, CreateDefaultPowerToysShortcutsPayload());
            }
            else if (isCircleToSearch)
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(plugin.id, CreateDefaultCircleToSearchPayload());
            }
            else if (isMusicController)
            {
                bodyHost.Child = BuildInteractiveGrayPluginBody(plugin.id, CreateDefaultMusicControllerPayload());
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

        private static PluginRenderPayload CreateDefaultShareXShortcutsPayload()
        {
            return new PluginRenderPayload
            {
                type = "render",
                style = "gray",
                buttons = new List<PluginRenderButton>
                {
                    new PluginRenderButton
                    {
                        id = "capture_region",
                        tooltip = "Capture Region (Screenshot)",
                        iconPaths = new List<string>
                        {
                            "M4,8 V5 A1,1 0 0 1 5,4 H8 M12,4 H14 M18,4 H19 A1,1 0 0 1 20,5 V8 M4,12 V14 M4,18 V19 A1,1 0 0 0 5,20 H8",
                            "M12.5,12.5 L15.2,21 L17.1,17.6 L20.5,15.7 Z"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "scrolling_capture",
                        tooltip = "Scrolling Capture",
                        iconPaths = new List<string>
                        {
                            "M12,3.5 V20.5",
                            "M7.5,8 L12,3.5 L16.5,8",
                            "M7.5,16 L12,20.5 L16.5,16"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "scan_qr",
                        tooltip = "Scan QR Code (Region)",
                        iconPaths = new List<string>
                        {
                            "M3.5,3.5 H9.5 V9.5 H3.5 Z M14.5,3.5 H20.5 V9.5 H14.5 Z M3.5,14.5 H9.5 V20.5 H3.5 Z",
                            "M6.5,6.5 L6.55,6.5 M17.5,6.5 L17.55,6.5 M6.5,17.5 L6.55,17.5",
                            "M12,4 V7 M12,11 V13 M15,12 H18 M12,16 H15 V19.5 M18.5,14.5 V20 H20.5"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "open_folder",
                        tooltip = "Open Screenshots Folder",
                        iconPaths = new List<string>
                        {
                            "M3,7.5 A2,2 0 0 1 5,5.5 H9.5 L11.5,7.8 H19 A2,2 0 0 1 21,9.8 V17.5 A2,2 0 0 1 19,19.5 H5 A2,2 0 0 1 3,17.5 Z"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "restore_sharex",
                        tooltip = "Open / Restore ShareX",
                        icon = "external_app",
                        iconPaths = new List<string>
                        {
                            "M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A2,2 0 0 1 16,21 H5 A2,2 0 0 1 3,19 V8 A2,2 0 0 1 5,6 H10"
                        }
                    }
                },
                statusItems = new List<PluginStatusItem>()
            };
        }

        private static PluginRenderPayload CreateDefaultPowerToysShortcutsPayload()
        {
            return new PluginRenderPayload
            {
                type = "render",
                style = "gray",
                buttons = new List<PluginRenderButton>
                {
                    new PluginRenderButton
                    {
                        id = "color_picker",
                        tooltip = "Color Picker",
                        iconPaths = new List<string>
                        {
                            "M5,19 L5.8,15.2 L14.5,6.5 L17.5,9.5 L8.8,18.2 Z",
                            "M13,5 L19,11",
                            "M15.2,5.8 L17,4 A2.1,2.1 0 0 1 20,7 L18.2,8.8"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "text_extractor",
                        tooltip = "Text Extractor (OCR)",
                        iconPaths = new List<string>
                        {
                            "M5,5 H19 M12,5 V19 M5,5 V7.2 M19,5 V7.2 M9.5,19 H14.5"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "always_on_top",
                        tooltip = "Toggle Always on Top (Last Active App)",
                        iconPaths = new List<string>
                        {
                            "M9,14 V19 A1,1 0 0 0 10,20 H19 A1,1 0 0 0 20,19 V10 A1,1 0 0 0 19,9 H14",
                            "M4,5 A1,1 0 0 1 5,4 H13 A1,1 0 0 1 14,5 V13 A1,1 0 0 1 13,14 H5 A1,1 0 0 1 4,13 Z"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "screen_ruler",
                        tooltip = "Screen Ruler",
                        iconPaths = new List<string>
                        {
                            "M3,8 A1,1 0 0 1 4,7 H20 A1,1 0 0 1 21,8 V16 A1,1 0 0 1 20,17 H4 A1,1 0 0 1 3,16 Z",
                            "M7,7 V11.5 M10.5,7 V10 M14,7 V11.5 M17.5,7 V10"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "crop_and_lock",
                        tooltip = "Crop and Lock (Screenshot)",
                        iconPaths = new List<string>
                        {
                            "M3.5,5.5 A1.5,1.5 0 0 1 5,4 H19 A1.5,1.5 0 0 1 20.5,5.5 V18.5 A1.5,1.5 0 0 1 19,20 H5 A1.5,1.5 0 0 1 3.5,18.5 Z",
                            "M4,16.5 L9.5,11.5 L13,15 L15.5,12.5 L20,17.5",
                            "M14.6,8.2 A1.4,1.4 0 1 0 17.4,8.2 A1.4,1.4 0 1 0 14.6,8.2 Z"
                        }
                    }
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

        private static PluginRenderPayload CreateDefaultCircleToSearchPayload()
        {
            return new PluginRenderPayload
            {
                type = "render",
                style = "gray",
                layout = "search_bar",
                searchBar = new PluginSearchBarPayload
                {
                    placeholder = "Find in Google",
                    submitAction = "search_text"
                },
                buttons = new List<PluginRenderButton>
                {
                    new PluginRenderButton
                    {
                        id = "search_text",
                        icon = "search",
                        tooltip = "Search in Default Browser (New Tab)",
                        iconPaths = new List<string>
                        {
                            "M10.5,4 A6.5,6.5 0 1 0 10.5,17 A6.5,6.5 0 1 0 10.5,4 Z",
                            "M15.3,15.3 L20,20"
                        }
                    },
                    new PluginRenderButton
                    {
                        id = "photo_search",
                        icon = "photo_search",
                        tooltip = "Visual Search (Select Screen Region)",
                        iconPaths = new List<string>
                        {
                            "M4,8.5 V6.5 A2.5,2.5 0 0 1 6.5,4 H8.5 M15.5,4 H17.5 A2.5,2.5 0 0 1 20,6.5 V8.5",
                            "M4,15.5 V17.5 A2.5,2.5 0 0 0 6.5,20 H8.5 M15.5,20 H17.5 A2.5,2.5 0 0 0 20,17.5 V15.5",
                            "M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 Z",
                            "M16.8,16.8 L16.85,16.8"
                        }
                    }
                },
                statusItems = new List<PluginStatusItem>()
            };
        }

        private static PluginRenderPayload CreateDefaultMusicControllerPayload()
        {
            return new PluginRenderPayload
            {
                type = "render",
                style = "gray",
                layout = "media_controller",
                mediaConfig = new PluginMediaPayload
                {
                    marqueeSpeedPxPerSec = 22.0,
                    focusAppOnSwitch = true,
                    showSourceDropdown = true
                },
                statusItems = new List<PluginStatusItem>()
            };
        }

        private UIElement BuildInteractiveGrayPluginBody(string pluginId, PluginRenderPayload payload)
        {
            if (string.Equals(payload.layout, "media_controller", StringComparison.OrdinalIgnoreCase) ||
                payload.mediaConfig != null)
            {
                return BuildMediaControllerPluginBody(pluginId, payload);
            }

            if (string.Equals(payload.layout, "search_bar", StringComparison.OrdinalIgnoreCase) ||
                payload.searchBar != null)
            {
                return BuildSearchBarPluginBody(pluginId, payload);
            }

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

        private UIElement BuildSearchBarPluginBody(string pluginId, PluginRenderPayload payload)
        {
            PluginSearchBarPayload cfg = payload.searchBar ?? new PluginSearchBarPayload
            {
                placeholder = "Find in Google",
                submitAction = "search_text"
            };
            string placeholder = string.IsNullOrEmpty(cfg.placeholder) ? "Find in Google" : cfg.placeholder;
            string submitAction = string.IsNullOrEmpty(cfg.submitAction) ? "search_text" : cfg.submitAction;

            string preservedText = string.Empty;
            if (cfg.initialText != null)
            {
                preservedText = cfg.initialText;
            }
            else if (_searchInputBoxes.TryGetValue(pluginId, out TextBox? previousBox))
            {
                preservedText = previousBox.Text;
            }

            SolidColorBrush idlePillBg = new SolidColorBrush(MediaColor.FromRgb(45, 45, 51));   // #2D2D33
            SolidColorBrush activePillBg = new SolidColorBrush(MediaColor.FromRgb(54, 54, 62)); // #36363E
            SolidColorBrush pillBorder = new SolidColorBrush(MediaColor.FromRgb(68, 68, 76));   // #44444C

            Border ovalBar = new Border
            {
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = idlePillBg,
                BorderBrush = pillBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.IBeam
            };

            Grid barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid inputArea = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            TextBlock placeholderText = new TextBlock
            {
                Text = placeholder,
                FontFamily = new MediaFontFamily("Segoe UI"),
                FontSize = 13.5,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(146, 146, 156)),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = string.IsNullOrEmpty(preservedText) ? Visibility.Visible : Visibility.Collapsed
            };

            TextBox queryBox = new TextBox
            {
                Text = preservedText,
                FontFamily = new MediaFontFamily("Segoe UI"),
                FontSize = 13.5,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(244, 244, 245)),
                CaretBrush = new SolidColorBrush(MediaColor.FromRgb(244, 244, 245)),
                SelectionBrush = new SolidColorBrush(MediaColor.FromRgb(82, 82, 91)),
                Background = MediaBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                AcceptsReturn = false
            };
            _searchInputBoxes[pluginId] = queryBox;

            queryBox.TextChanged += (s, e) =>
            {
                placeholderText.Visibility = string.IsNullOrEmpty(queryBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            };
            queryBox.GotKeyboardFocus += (s, e) => ovalBar.Background = activePillBg;
            queryBox.LostKeyboardFocus += (s, e) => ovalBar.Background = idlePillBg;
            queryBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    string q = queryBox.Text;
                    DispatchPluginAction(pluginId, submitAction, q);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        queryBox.Text = string.Empty;
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape && _isOverlayVisible)
                {
                    HideOverlayWithAnimation();
                    e.Handled = true;
                }
            };

            inputArea.Children.Add(placeholderText);
            inputArea.Children.Add(queryBox);
            Grid.SetColumn(inputArea, 0);
            barGrid.Children.Add(inputArea);

            StackPanel actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (payload.buttons != null)
            {
                for (int i = 0; i < payload.buttons.Count; i++)
                {
                    PluginRenderButton btn = payload.buttons[i];
                    Border circleBtn = CreateBorderlessRoundIconButton(pluginId, btn, () =>
                    {
                        string q = queryBox.Text;
                        if (string.Equals(btn.id, submitAction, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(q))
                        {
                            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            {
                                queryBox.Text = string.Empty;
                            }));
                        }
                        return q;
                    });
                    if (i > 0)
                    {
                        circleBtn.Margin = new Thickness(2, 0, 0, 0);
                    }
                    actionPanel.Children.Add(circleBtn);
                }
            }

            Grid.SetColumn(actionPanel, 1);
            barGrid.Children.Add(actionPanel);
            ovalBar.Child = barGrid;

            ovalBar.MouseLeftButtonDown += (s, e) =>
            {
                queryBox.Focus();
                Keyboard.Focus(queryBox);
                e.Handled = true;
            };

            return ovalBar;
        }

        private Border CreateBorderlessRoundIconButton(
            string pluginId,
            PluginRenderButton btn,
            Func<string?>? queryProvider = null)
        {
            SolidColorBrush idleBg = MediaBrushes.Transparent;
            SolidColorBrush hoverBg = new SolidColorBrush(MediaColor.FromRgb(68, 68, 78));
            SolidColorBrush pressedBg = new SolidColorBrush(MediaColor.FromRgb(86, 86, 98));

            Border circleBtn = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = idleBg,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = string.IsNullOrEmpty(btn.tooltip) ? null : new System.Windows.Controls.ToolTip
                {
                    Content = btn.tooltip,
                    Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                    Foreground = MediaBrushes.White,
                    BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70))
                }
            };

            MediaBrush iconBrush = ParseHexBrush(btn.iconColor, MediaColor.FromRgb(228, 228, 231));
            double strokeWidth = btn.iconStrokeWidth ?? 1.85;

            if (btn.iconPaths != null && btn.iconPaths.Count > 0)
            {
                circleBtn.Child = BuildVectorIconElement(btn.iconPaths, iconBrush, strokeWidth, size: 16);
            }
            else if (TryGetNamedButtonIconPaths(btn.icon, out string[] namedPaths))
            {
                circleBtn.Child = BuildVectorIconElement(namedPaths, iconBrush, strokeWidth, size: 16);
            }

            circleBtn.MouseEnter += (s, e) => circleBtn.Background = hoverBg;
            circleBtn.MouseLeave += (s, e) => circleBtn.Background = idleBg;
            circleBtn.MouseLeftButtonDown += (s, e) =>
            {
                circleBtn.Background = pressedBg;
                string? q = queryProvider != null ? queryProvider() : null;
                DispatchPluginAction(pluginId, btn.id, q);
                e.Handled = true;
            };
            circleBtn.MouseLeftButtonUp += (s, e) =>
            {
                circleBtn.Background = circleBtn.IsMouseOver ? hoverBg : idleBg;
                e.Handled = true;
            };

            return circleBtn;
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

            if (btn.iconPaths != null && btn.iconPaths.Count > 0)
            {
                circle.Child = BuildVectorIconElement(
                    btn.iconPaths,
                    ParseHexBrush(btn.iconColor, fgColor),
                    btn.iconStrokeWidth ?? 1.8,
                    size: 19);
            }
            else if (string.Equals(btn.icon, "sqrt", StringComparison.OrdinalIgnoreCase))
            {
                // Sleek geometric square root vector glyph (clean tick + V + top vinculum)
                circle.Child = BuildVectorIconElement(
                    new[] { "M3,13.5 L6.2,13.5 L9.8,19.5 L14,5.5 L21,5.5" },
                    fgBrush,
                    1.9,
                    size: 19);
            }
            else if (string.Equals(btn.icon, "backspace", StringComparison.OrdinalIgnoreCase))
            {
                // Clean backspace pentagon + X glyph
                circle.Child = BuildVectorIconElement(
                    new[] { "M20,6 H9.5 L4,12 L9.5,18 H20 A2,2 0 0 0 22,16 V8 A2,2 0 0 0 20,6 Z M12.2,9.8 L16.8,14.2 M16.8,9.8 L12.2,14.2" },
                    fgBrush,
                    1.75,
                    size: 19);
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

            if (btn.iconPaths != null && btn.iconPaths.Count > 0)
            {
                buttonBox.Child = BuildVectorIconElement(
                    btn.iconPaths,
                    ParseHexBrush(btn.iconColor, MediaColor.FromRgb(228, 228, 231)),
                    btn.iconStrokeWidth ?? 1.8,
                    size: 16);
            }
            else if (string.Equals(btn.icon, "external_app", StringComparison.OrdinalIgnoreCase))
            {
                // Vector "Open Another App" icon (box + diagonal launch arrow)
                buttonBox.Child = BuildVectorIconElement(
                    new[] { "M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A2,2 0 0 1 16,21 H5 A2,2 0 0 1 3,19 V8 A2,2 0 0 1 5,6 H10" },
                    new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)),
                    2.0,
                    size: 15);
            }
            else if (TryGetNamedButtonIconPaths(btn.icon, out string[] namedPaths))
            {
                buttonBox.Child = BuildVectorIconElement(
                    namedPaths,
                    ParseHexBrush(btn.iconColor, MediaColor.FromRgb(228, 228, 231)),
                    btn.iconStrokeWidth ?? 1.8,
                    size: 16);
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

        private UIElement BuildMediaControllerPluginBody(string pluginId, PluginRenderPayload payload)
        {
            var holder = new MediaControllerViewHolder(this, pluginId, payload.mediaConfig);
            _mediaViews[pluginId] = holder;
            holder.UpdateSnapshot(_mediaSessionService.LatestSnapshot);
            return holder.RootElement;
        }

        private void DispatchPluginAction(string pluginId, string actionId, string? query = null, double? value = null)
        {
            if (_pluginHostService.SendAction(pluginId, actionId, query, value))
            {
                return;
            }

            // Try restarting plugin host once if it wasn't running yet
            _pluginHostService.StartAllForOpenOverlay(_pluginManager.LoadedPluginFolders);
            if (_pluginHostService.SendAction(pluginId, actionId, query, value))
            {
                return;
            }

            string reason = !string.IsNullOrWhiteSpace(_pluginHostService.LastStartupIssue)
                ? _pluginHostService.LastStartupIssue
                : "Plugin process is not running. Check %LocalAppData%\\FuncOverlay\\plugin_host.log";
            ShowOverlayToast(reason, 4500);
        }

        private static UIElement BuildVectorIconElement(
            IEnumerable<string> pathDataList,
            MediaBrush strokeBrush,
            double strokeThickness,
            double size = 16)
        {
            Viewbox iconBox = new Viewbox
            {
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas iconCanvas = new Canvas { Width = 24, Height = 24 };
            double clampedThickness = Math.Clamp(strokeThickness, 0.8, 4.0);

            foreach (string rawData in pathDataList)
            {
                if (string.IsNullOrWhiteSpace(rawData)) continue;
                try
                {
                    Geometry geom = Geometry.Parse(rawData.Trim());
                    iconCanvas.Children.Add(new System.Windows.Shapes.Path
                    {
                        Stroke = strokeBrush,
                        StrokeThickness = clampedThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Data = geom
                    });
                }
                catch { }
            }

            iconBox.Child = iconCanvas;
            return iconBox;
        }

        private static bool TryGetNamedButtonIconPaths(string? iconName, out string[] paths)
        {
            switch ((iconName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "capture_region":
                    paths = new[]
                    {
                        "M4,8 V5 A1,1 0 0 1 5,4 H8 M12,4 H14 M18,4 H19 A1,1 0 0 1 20,5 V8 M4,12 V14 M4,18 V19 A1,1 0 0 0 5,20 H8",
                        "M12.5,12.5 L15.2,21 L17.1,17.6 L20.5,15.7 Z"
                    };
                    return true;
                case "scrolling_capture":
                    paths = new[]
                    {
                        "M12,3.5 V20.5",
                        "M7.5,8 L12,3.5 L16.5,8",
                        "M7.5,16 L12,20.5 L16.5,16"
                    };
                    return true;
                case "scan_qr":
                    paths = new[]
                    {
                        "M3.5,3.5 H9.5 V9.5 H3.5 Z M14.5,3.5 H20.5 V9.5 H14.5 Z M3.5,14.5 H9.5 V20.5 H3.5 Z",
                        "M6.5,6.5 L6.55,6.5 M17.5,6.5 L17.55,6.5 M6.5,17.5 L6.55,17.5",
                        "M12,4 V7 M12,11 V13 M15,12 H18 M12,16 H15 V19.5 M18.5,14.5 V20 H20.5"
                    };
                    return true;
                case "open_folder":
                case "folder":
                    paths = new[]
                    {
                        "M3,7.5 A2,2 0 0 1 5,5.5 H9.5 L11.5,7.8 H19 A2,2 0 0 1 21,9.8 V17.5 A2,2 0 0 1 19,19.5 H5 A2,2 0 0 1 3,17.5 Z"
                    };
                    return true;
                case "restore_sharex":
                case "maximize_sharex":
                case "maximize":
                    paths = new[]
                    {
                        "M4,9 V4.5 A0.5,0.5 0 0 1 4.5,4 H9 M15,4 H19.5 A0.5,0.5 0 0 1 20,4.5 V9",
                        "M4,15 V19.5 A0.5,0.5 0 0 0 4.5,20 H9 M15,20 H19.5 A0.5,0.5 0 0 0 20,19.5 V15",
                        "M8.5,8.5 H15.5 V15.5 H8.5 Z"
                    };
                    return true;
                case "color_picker":
                    paths = new[]
                    {
                        "M5,19 L5.8,15.2 L14.5,6.5 L17.5,9.5 L8.8,18.2 Z",
                        "M13,5 L19,11",
                        "M15.2,5.8 L17,4 A2.1,2.1 0 0 1 20,7 L18.2,8.8"
                    };
                    return true;
                case "text_extractor":
                    paths = new[]
                    {
                        "M5,5 H19 M12,5 V19 M5,5 V7.2 M19,5 V7.2 M9.5,19 H14.5"
                    };
                    return true;
                case "always_on_top":
                    paths = new[]
                    {
                        "M9,14 V19 A1,1 0 0 0 10,20 H19 A1,1 0 0 0 20,19 V10 A1,1 0 0 0 19,9 H14",
                        "M4,5 A1,1 0 0 1 5,4 H13 A1,1 0 0 1 14,5 V13 A1,1 0 0 1 13,14 H5 A1,1 0 0 1 4,13 Z"
                    };
                    return true;
                case "screen_ruler":
                case "ruler":
                    paths = new[]
                    {
                        "M3,8 A1,1 0 0 1 4,7 H20 A1,1 0 0 1 21,8 V16 A1,1 0 0 1 20,17 H4 A1,1 0 0 1 3,16 Z",
                        "M7,7 V11.5 M10.5,7 V10 M14,7 V11.5 M17.5,7 V10"
                    };
                    return true;
                case "crop_and_lock":
                case "gallery":
                    paths = new[]
                    {
                        "M3.5,5.5 A1.5,1.5 0 0 1 5,4 H19 A1.5,1.5 0 0 1 20.5,5.5 V18.5 A1.5,1.5 0 0 1 19,20 H5 A1.5,1.5 0 0 1 3.5,18.5 Z",
                        "M4,16.5 L9.5,11.5 L13,15 L15.5,12.5 L20,17.5",
                        "M14.6,8.2 A1.4,1.4 0 1 0 17.4,8.2 A1.4,1.4 0 1 0 14.6,8.2 Z"
                    };
                    return true;
                case "search":
                    paths = new[]
                    {
                        "M10.5,4 A6.5,6.5 0 1 0 10.5,17 A6.5,6.5 0 1 0 10.5,4 Z",
                        "M15.3,15.3 L20,20"
                    };
                    return true;
                case "photo_search":
                case "lens":
                    paths = new[]
                    {
                        "M4,8.5 V6.5 A2.5,2.5 0 0 1 6.5,4 H8.5 M15.5,4 H17.5 A2.5,2.5 0 0 1 20,6.5 V8.5",
                        "M4,15.5 V17.5 A2.5,2.5 0 0 0 6.5,20 H8.5 M15.5,20 H17.5 A2.5,2.5 0 0 0 20,17.5 V15.5",
                        "M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 Z",
                        "M16.8,16.8 L16.85,16.8"
                    };
                    return true;
                default:
                    paths = Array.Empty<string>();
                    return false;
            }
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
            _mediaSessionService.Dispose();
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

        private sealed class MediaControllerViewHolder
        {
            private readonly MainWindow _owner;
            private readonly string _pluginId;
            private readonly Grid _rootGrid;
            private readonly System.Windows.Controls.Image _artImage;
            private readonly UIElement _artPlaceholder;
            private readonly Canvas _titleViewport;
            private readonly TextBlock _titleText;
            private readonly Canvas _artistViewport;
            private readonly TextBlock _artistText;
            private readonly Border _sourceButton;
            private readonly Popup _sourcePopup;
            private readonly StackPanel _sourceMenuPanel;
            private readonly Grid _seekContainer;
            private readonly Border _seekTrackFill;
            private readonly Border _seekThumb;
            private readonly TranslateTransform _thumbTransform;
            private readonly TextBlock _currentTimeText;
            private readonly TextBlock _totalTimeText;
            private readonly Border _playPauseCircle;

            private PluginMediaPayload _config;
            private MediaSnapshot _latestSnapshot = new MediaSnapshot();
            private string _lastArtKey = string.Empty;
            private string _lastTitleMarqueeKey = string.Empty;
            private string _lastArtistMarqueeKey = string.Empty;
            private bool _isPlayingState;
            private bool _isDraggingSeek;
            private double _dragFraction;
            private DateTime _sourcePopupClosedAtUtc = DateTime.MinValue;

            public UIElement RootElement => _rootGrid;

            public MediaControllerViewHolder(MainWindow owner, string pluginId, PluginMediaPayload? config)
            {
                _owner = owner;
                _pluginId = pluginId;
                _config = config ?? new PluginMediaPayload();

                _rootGrid = new Grid();
                _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Left 150x150 (1:1) Album Art Container
                Border artFrame = new Border
                {
                    Width = 150,
                    Height = 150,
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(MediaColor.FromRgb(32, 32, 38)),
                    BorderBrush = new SolidColorBrush(MediaColor.FromRgb(54, 54, 62)),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                Grid artClipGrid = new Grid
                {
                    Width = 148,
                    Height = 148,
                    Clip = new RectangleGeometry(new Rect(0, 0, 148, 148), 9, 9)
                };

                _artPlaceholder = BuildVectorIconElement(
                    new[]
                    {
                        "M9,17.5 V6.8 L18.5,5 V15.5",
                        "M6.8,17.5 A2.2,2.2 0 1 0 11.2,17.5 A2.2,2.2 0 1 0 6.8,17.5 Z",
                        "M16.3,15.5 A2.2,2.2 0 1 0 20.7,15.5 A2.2,2.2 0 1 0 16.3,15.5 Z"
                    },
                    new SolidColorBrush(MediaColor.FromRgb(92, 92, 104)),
                    1.8,
                    size: 42);

                _artImage = new System.Windows.Controls.Image
                {
                    Width = 148,
                    Height = 148,
                    Stretch = Stretch.UniformToFill,
                    Visibility = Visibility.Collapsed
                };
                RenderOptions.SetBitmapScalingMode(_artImage, BitmapScalingMode.HighQuality);

                artClipGrid.Children.Add(_artPlaceholder);
                artClipGrid.Children.Add(_artImage);
                artFrame.Child = artClipGrid;
                Grid.SetColumn(artFrame, 0);
                _rootGrid.Children.Add(artFrame);

                // Right Column (Track Info, Scrollbar, Transport Controls, Top-Right Source Dropdown)
                Grid rightGrid = new Grid { Height = 150 };
                rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title + Source button
                rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Artist
                rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Seek bar + times
                rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prev / Play / Next

                // Row 0: Track Title (White, Bold) + Top-Right Audio Source Dropdown Button
                Grid topRow = new Grid();
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _titleViewport = new Canvas
                {
                    Height = 23,
                    ClipToBounds = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                _titleText = new TextBlock
                {
                    Text = "No active media",
                    FontFamily = new MediaFontFamily("Segoe UI"),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 255, 255))
                };
                Canvas.SetLeft(_titleText, 0);
                Canvas.SetTop(_titleText, 1);
                _titleViewport.Children.Add(_titleText);
                _titleViewport.SizeChanged += (s, e) => RefreshTitleMarquee(force: true);

                _sourceButton = CreateSourcePickerButton();
                Grid.SetColumn(_titleViewport, 0);
                Grid.SetColumn(_sourceButton, 1);
                topRow.Children.Add(_titleViewport);
                topRow.Children.Add(_sourceButton);
                Grid.SetRow(topRow, 0);
                rightGrid.Children.Add(topRow);

                // Source Selection Popup anchored under top-right button
                _sourceMenuPanel = new StackPanel { Orientation = Orientation.Vertical };
                Border popupCard = new Border
                {
                    Background = new SolidColorBrush(MediaColor.FromArgb(248, 26, 26, 31)),
                    BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 72)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6),
                    MinWidth = 195,
                    MaxWidth = 270,
                    Child = _sourceMenuPanel
                };
                popupCard.MouseLeftButtonDown += (s, e) => e.Handled = true;

                _sourcePopup = new Popup
                {
                    Child = popupCard,
                    PlacementTarget = _sourceButton,
                    Placement = PlacementMode.Bottom,
                    HorizontalOffset = -168,
                    VerticalOffset = 4,
                    AllowsTransparency = true,
                    StaysOpen = false
                };
                _sourcePopup.Closed += (s, e) => _sourcePopupClosedAtUtc = DateTime.UtcNow;

                _sourceButton.MouseLeftButtonDown += (s, e) =>
                {
                    if ((DateTime.UtcNow - _sourcePopupClosedAtUtc).TotalMilliseconds < 180)
                    {
                        e.Handled = true;
                        return;
                    }
                    RebuildSourcePopupItems();
                    _sourcePopup.IsOpen = !_sourcePopup.IsOpen;
                    e.Handled = true;
                };

                // Row 1: Artist Name (Light Gray, smaller font)
                _artistViewport = new Canvas
                {
                    Height = 19,
                    ClipToBounds = true,
                    Margin = new Thickness(0, 3, 28, 0)
                };
                _artistText = new TextBlock
                {
                    Text = "Open Spotify, Firefox, or any player",
                    FontFamily = new MediaFontFamily("Segoe UI"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.Normal,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(180, 180, 190))
                };
                Canvas.SetLeft(_artistText, 0);
                Canvas.SetTop(_artistText, 0);
                _artistViewport.Children.Add(_artistText);
                _artistViewport.SizeChanged += (s, e) => RefreshArtistMarquee(force: true);
                Grid.SetRow(_artistViewport, 1);
                rightGrid.Children.Add(_artistViewport);

                // Row 2: Seek Bar + Current/Total Time Labels
                StackPanel timelinePanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 2)
                };

                _seekContainer = new Grid
                {
                    Height = 18,
                    Background = MediaBrushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                Border railBg = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(MediaColor.FromRgb(58, 58, 68)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                _seekTrackFill = new Border
                {
                    Height = 4,
                    Width = 0,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(MediaColor.FromRgb(244, 244, 245)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _thumbTransform = new TranslateTransform(0, 0);
                _seekThumb = new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(MediaColor.FromRgb(255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = _thumbTransform,
                    Visibility = Visibility.Collapsed
                };

                _seekContainer.Children.Add(railBg);
                _seekContainer.Children.Add(_seekTrackFill);
                _seekContainer.Children.Add(_seekThumb);

                _seekContainer.SizeChanged += (s, e) => UpdateSeekBarVisual();
                _seekContainer.MouseEnter += (s, e) =>
                {
                    if (_latestSnapshot.durationSec > 0) _seekThumb.Visibility = Visibility.Visible;
                };
                _seekContainer.MouseLeave += (s, e) =>
                {
                    if (!_isDraggingSeek) _seekThumb.Visibility = Visibility.Collapsed;
                };
                _seekContainer.MouseLeftButtonDown += (s, e) =>
                {
                    if (_latestSnapshot.durationSec <= 0)
                    {
                        e.Handled = true;
                        return;
                    }
                    _isDraggingSeek = true;
                    _seekThumb.Visibility = Visibility.Visible;
                    _dragFraction = ComputeSeekFraction(e.GetPosition(_seekContainer).X);
                    _seekContainer.CaptureMouse();
                    UpdateSeekBarVisual();
                    e.Handled = true;
                };
                _seekContainer.MouseMove += (s, e) =>
                {
                    if (!_isDraggingSeek) return;
                    _dragFraction = ComputeSeekFraction(e.GetPosition(_seekContainer).X);
                    UpdateSeekBarVisual();
                };
                _seekContainer.MouseLeftButtonUp += (s, e) =>
                {
                    if (!_isDraggingSeek) return;
                    _dragFraction = ComputeSeekFraction(e.GetPosition(_seekContainer).X);
                    _isDraggingSeek = false;
                    _seekContainer.ReleaseMouseCapture();
                    if (!_seekContainer.IsMouseOver) _seekThumb.Visibility = Visibility.Collapsed;

                    if (_latestSnapshot.durationSec > 0)
                    {
                        double targetSec = _dragFraction * _latestSnapshot.durationSec;
                        _latestSnapshot.positionSec = targetSec;
                        UpdateSeekBarVisual();
                        _owner.DispatchPluginAction(_pluginId, "seek", _latestSnapshot.selectedSourceId, targetSec);
                    }
                    e.Handled = true;
                };

                Grid timeRow = new Grid { Margin = new Thickness(0, 1, 0, 0) };
                timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _currentTimeText = new TextBlock
                {
                    Text = "0:00",
                    FontFamily = new MediaFontFamily("Consolas, Segoe UI"),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216))
                };
                _totalTimeText = new TextBlock
                {
                    Text = "0:00",
                    FontFamily = new MediaFontFamily("Consolas, Segoe UI"),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(_currentTimeText, 0);
                Grid.SetColumn(_totalTimeText, 1);
                timeRow.Children.Add(_currentTimeText);
                timeRow.Children.Add(_totalTimeText);

                timelinePanel.Children.Add(_seekContainer);
                timelinePanel.Children.Add(timeRow);
                Grid.SetRow(timelinePanel, 2);
                rightGrid.Children.Add(timelinePanel);

                // Row 3: Previous, Play/Pause, Next Buttons
                StackPanel transportPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                Border prevBtn = CreateTransportSmallButton("previous", "Previous Track", new[]
                {
                    "M6.5,5.5 V18.5",
                    "M17.5,6 L9,12 L17.5,18 Z"
                }, fillInterior: true);

                _playPauseCircle = CreatePlayPauseButton();

                Border nextBtn = CreateTransportSmallButton("next", "Next Track", new[]
                {
                    "M6.5,6 L15,12 L6.5,18 Z",
                    "M17.5,5.5 V18.5"
                }, fillInterior: true);
                nextBtn.Margin = new Thickness(12, 0, 0, 0);
                _playPauseCircle.Margin = new Thickness(12, 0, 0, 0);

                transportPanel.Children.Add(prevBtn);
                transportPanel.Children.Add(_playPauseCircle);
                transportPanel.Children.Add(nextBtn);
                Grid.SetRow(transportPanel, 3);
                rightGrid.Children.Add(transportPanel);

                Grid.SetColumn(rightGrid, 2);
                _rootGrid.Children.Add(rightGrid);

                ApplyConfig(config);
            }

            public void ApplyConfig(PluginMediaPayload? config)
            {
                _config = config ?? new PluginMediaPayload();
                _sourceButton.Visibility = _config.showSourceDropdown ? Visibility.Visible : Visibility.Collapsed;
                RefreshTitleMarquee(force: true);
                RefreshArtistMarquee(force: true);
            }

            public void CloseSourcePopup()
            {
                if (_sourcePopup.IsOpen)
                {
                    _sourcePopup.IsOpen = false;
                }
            }

            public void UpdateSnapshot(MediaSnapshot snapshot)
            {
                _latestSnapshot = snapshot ?? new MediaSnapshot();

                if (!_latestSnapshot.hasMedia && _latestSnapshot.sources.Count == 0)
                {
                    _titleText.Text = "No active media";
                    _artistText.Text = "Open Spotify, Firefox, or any player";
                }
                else
                {
                    _titleText.Text = string.IsNullOrWhiteSpace(_latestSnapshot.title)
                        ? (_latestSnapshot.selectedSourceName ?? "Media")
                        : _latestSnapshot.title;
                    _artistText.Text = string.IsNullOrWhiteSpace(_latestSnapshot.artist)
                        ? _latestSnapshot.selectedSourceName
                        : _latestSnapshot.artist;
                }

                RefreshTitleMarquee(force: false);
                RefreshArtistMarquee(force: false);
                UpdateAlbumArt(_latestSnapshot.albumArtKey, _latestSnapshot.albumArtBase64);
                UpdatePlayPauseVisual(_latestSnapshot.isPlaying);

                if (!_isDraggingSeek)
                {
                    UpdateSeekBarVisual();
                }

                if (_sourcePopup.IsOpen)
                {
                    RebuildSourcePopupItems();
                }
            }

            private void UpdateAlbumArt(string artKey, string? base64)
            {
                if (string.Equals(_lastArtKey, artKey, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(base64) == (_artImage.Visibility == Visibility.Visible))
                {
                    return;
                }

                _lastArtKey = artKey;
                if (string.IsNullOrWhiteSpace(base64))
                {
                    _artImage.Source = null;
                    _artImage.Visibility = Visibility.Collapsed;
                    _artPlaceholder.Visibility = Visibility.Visible;
                    return;
                }

                try
                {
                    byte[] bytes = Convert.FromBase64String(base64);
                    using var ms = new MemoryStream(bytes);
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    _artImage.Source = bmp;
                    _artImage.Visibility = Visibility.Visible;
                    _artPlaceholder.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    _artImage.Source = null;
                    _artImage.Visibility = Visibility.Collapsed;
                    _artPlaceholder.Visibility = Visibility.Visible;
                }
            }

            private void RefreshTitleMarquee(bool force)
            {
                double vw = _titleViewport.ActualWidth;
                double speed = Math.Clamp(_config.marqueeSpeedPxPerSec, 8.0, 80.0);
                string key = $"{_titleText.Text}|{Math.Round(vw)}|{speed:F1}";
                if (!force && string.Equals(_lastTitleMarqueeKey, key, StringComparison.Ordinal)) return;
                _lastTitleMarqueeKey = key;
                ApplyMarqueeAnimation(_titleViewport, _titleText, speed);
            }

            private void RefreshArtistMarquee(bool force)
            {
                double vw = _artistViewport.ActualWidth;
                double speed = Math.Clamp(_config.marqueeSpeedPxPerSec, 8.0, 80.0);
                string key = $"{_artistText.Text}|{Math.Round(vw)}|{speed:F1}";
                if (!force && string.Equals(_lastArtistMarqueeKey, key, StringComparison.Ordinal)) return;
                _lastArtistMarqueeKey = key;
                ApplyMarqueeAnimation(_artistViewport, _artistText, speed);
            }

            private static void ApplyMarqueeAnimation(Canvas viewport, TextBlock tb, double speedPxPerSec)
            {
                tb.BeginAnimation(Canvas.LeftProperty, null);
                Canvas.SetLeft(tb, 0);

                double viewWidth = viewport.ActualWidth;
                if (viewWidth <= 8) return;

                tb.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = tb.DesiredSize.Width;
                if (textWidth <= viewWidth + 2)
                {
                    return;
                }

                double distance = (textWidth - viewWidth) + 12.0;
                double scrollDurationSec = Math.Max(1.2, distance / speedPxPerSec);
                double returnDurationSec = Math.Max(0.8, scrollDurationSec * 0.5);

                double t1 = 1.4;
                double t2 = t1 + scrollDurationSec;
                double t3 = t2 + 1.2;
                double t4 = t3 + returnDurationSec;

                var anim = new DoubleAnimationUsingKeyFrames
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t1))));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t2))));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t3))));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t4))));

                tb.BeginAnimation(Canvas.LeftProperty, anim);
            }

            private double ComputeSeekFraction(double mouseX)
            {
                double w = _seekContainer.ActualWidth;
                if (w <= 0) return 0;
                return Math.Clamp(mouseX / w, 0.0, 1.0);
            }

            private void UpdateSeekBarVisual()
            {
                double dur = _latestSnapshot.durationSec;
                double pos = _isDraggingSeek && dur > 0
                    ? _dragFraction * dur
                    : _latestSnapshot.positionSec;

                double frac = dur > 0 ? Math.Clamp(pos / dur, 0.0, 1.0) : 0.0;
                double w = _seekContainer.ActualWidth;
                if (w > 0)
                {
                    _seekTrackFill.Width = Math.Max(0, w * frac);
                    double thumbX = Math.Clamp((w * frac) - 5.0, 0.0, Math.Max(0.0, w - 10.0));
                    _thumbTransform.X = thumbX;
                }

                _currentTimeText.Text = FormatDuration(pos);
                _totalTimeText.Text = dur > 0 ? FormatDuration(dur) : "0:00";
            }

            private static string FormatDuration(double seconds)
            {
                if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
                TimeSpan ts = TimeSpan.FromSeconds(seconds);
                return ts.TotalHours >= 1.0
                    ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                    : $"{ts.Minutes}:{ts.Seconds:D2}";
            }

            private Border CreateSourcePickerButton()
            {
                SolidColorBrush idleBg = new SolidColorBrush(MediaColor.FromRgb(38, 38, 45));
                SolidColorBrush hoverBg = new SolidColorBrush(MediaColor.FromRgb(56, 56, 66));

                Border btn = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(6),
                    Background = idleBg,
                    BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 72)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = new System.Windows.Controls.ToolTip
                    {
                        Content = "Choose Audio Source App",
                        Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                        Foreground = MediaBrushes.White,
                        BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70))
                    }
                };

                btn.Child = BuildVectorIconElement(
                    new[]
                    {
                        "M4,7 H14 M4,12 H11 M4,17 H11",
                        "M15,14 L18,17 L21,14"
                    },
                    new SolidColorBrush(MediaColor.FromRgb(212, 212, 216)),
                    1.8,
                    size: 14);

                btn.MouseEnter += (s, e) => btn.Background = hoverBg;
                btn.MouseLeave += (s, e) => btn.Background = idleBg;
                return btn;
            }

            private void RebuildSourcePopupItems()
            {
                _sourceMenuPanel.Children.Clear();
                _sourceMenuPanel.Children.Add(new TextBlock
                {
                    Text = "Audio Sources",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                    Margin = new Thickness(6, 2, 6, 5)
                });

                var sources = _latestSnapshot.sources;
                if (sources == null || sources.Count == 0)
                {
                    _sourceMenuPanel.Children.Add(new TextBlock
                    {
                        Text = "No active media apps detected",
                        FontSize = 11.5,
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(140, 140, 150)),
                        Margin = new Thickness(6, 4, 6, 4)
                    });
                    return;
                }

                SolidColorBrush rowHover = new SolidColorBrush(MediaColor.FromRgb(52, 52, 60));
                SolidColorBrush rowSelected = new SolidColorBrush(MediaColor.FromRgb(42, 42, 50));

                foreach (var src in sources)
                {
                    bool isSelected = string.Equals(src.id, _latestSnapshot.selectedSourceId, StringComparison.OrdinalIgnoreCase);

                    Border row = new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 1, 0, 1),
                        Background = isSelected ? rowSelected : MediaBrushes.Transparent,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    Grid rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    Ellipse statusDot = new Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Fill = src.isPlaying
                            ? new SolidColorBrush(MediaColor.FromRgb(34, 197, 94))
                            : new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };

                    StackPanel labels = new StackPanel { Orientation = Orientation.Vertical };
                    labels.Children.Add(new TextBlock
                    {
                        Text = src.name,
                        FontSize = 12,
                        FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = MediaBrushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    if (!string.IsNullOrWhiteSpace(src.trackSummary))
                    {
                        labels.Children.Add(new TextBlock
                        {
                            Text = src.trackSummary,
                            FontSize = 10,
                            Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxWidth = 170
                        });
                    }

                    TextBlock check = new TextBlock
                    {
                        Text = isSelected ? "✓" : string.Empty,
                        FontSize = 11.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };

                    Grid.SetColumn(statusDot, 0);
                    Grid.SetColumn(labels, 1);
                    Grid.SetColumn(check, 2);
                    rowGrid.Children.Add(statusDot);
                    rowGrid.Children.Add(labels);
                    rowGrid.Children.Add(check);
                    row.Child = rowGrid;

                    row.MouseEnter += (s, e) => row.Background = rowHover;
                    row.MouseLeave += (s, e) => row.Background = isSelected ? rowSelected : MediaBrushes.Transparent;
                    row.MouseLeftButtonDown += (s, e) =>
                    {
                        _sourcePopup.IsOpen = false;
                        _owner.DispatchPluginAction(_pluginId, "select_source", src.id);
                        e.Handled = true;
                    };

                    _sourceMenuPanel.Children.Add(row);
                }
            }

            private Border CreateTransportSmallButton(string actionId, string tooltip, string[] paths, bool fillInterior)
            {
                SolidColorBrush idleBg = MediaBrushes.Transparent;
                SolidColorBrush hoverBg = new SolidColorBrush(MediaColor.FromRgb(56, 56, 65));
                SolidColorBrush pressedBg = new SolidColorBrush(MediaColor.FromRgb(74, 74, 86));

                Border btn = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(17),
                    Background = idleBg,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = new System.Windows.Controls.ToolTip
                    {
                        Content = tooltip,
                        Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                        Foreground = MediaBrushes.White,
                        BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70))
                    }
                };

                btn.Child = BuildFilledVectorIcon(
                    paths,
                    new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)),
                    1.6,
                    size: 16,
                    fill: fillInterior);

                btn.MouseEnter += (s, e) => btn.Background = hoverBg;
                btn.MouseLeave += (s, e) => btn.Background = idleBg;
                btn.MouseLeftButtonDown += (s, e) =>
                {
                    btn.Background = pressedBg;
                    _owner.DispatchPluginAction(_pluginId, actionId, _latestSnapshot.selectedSourceId);
                    e.Handled = true;
                };
                btn.MouseLeftButtonUp += (s, e) =>
                {
                    btn.Background = btn.IsMouseOver ? hoverBg : idleBg;
                    e.Handled = true;
                };
                return btn;
            }

            private Border CreatePlayPauseButton()
            {
                SolidColorBrush idleBg = new SolidColorBrush(MediaColor.FromRgb(244, 244, 245));
                SolidColorBrush hoverBg = new SolidColorBrush(MediaColor.FromRgb(255, 255, 255));
                SolidColorBrush pressedBg = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216));

                Border btn = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(20),
                    Background = idleBg,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = new System.Windows.Controls.ToolTip
                    {
                        Content = "Play / Pause",
                        Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                        Foreground = MediaBrushes.White,
                        BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70))
                    }
                };

                btn.MouseEnter += (s, e) => btn.Background = hoverBg;
                btn.MouseLeave += (s, e) => btn.Background = idleBg;
                btn.MouseLeftButtonDown += (s, e) =>
                {
                    btn.Background = pressedBg;
                    _owner.DispatchPluginAction(_pluginId, "play_pause", _latestSnapshot.selectedSourceId);
                    e.Handled = true;
                };
                btn.MouseLeftButtonUp += (s, e) =>
                {
                    btn.Background = btn.IsMouseOver ? hoverBg : idleBg;
                    e.Handled = true;
                };

                _isPlayingState = false;
                btn.Child = BuildPlayIcon(false);
                return btn;
            }

            private void UpdatePlayPauseVisual(bool isPlaying)
            {
                if (_isPlayingState == isPlaying && _playPauseCircle.Child != null) return;
                _isPlayingState = isPlaying;
                _playPauseCircle.Child = BuildPlayIcon(isPlaying);
            }

            private static UIElement BuildPlayIcon(bool isPlaying)
            {
                MediaBrush darkBrush = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27));
                if (isPlaying)
                {
                    return BuildFilledVectorIcon(
                        new[] { "M7,5.5 H10.2 V18.5 H7 Z M13.8,5.5 H17 V18.5 H13.8 Z" },
                        darkBrush,
                        1.0,
                        size: 17,
                        fill: true);
                }

                return BuildFilledVectorIcon(
                    new[] { "M8.2,5.5 L18.5,12 L8.2,18.5 Z" },
                    darkBrush,
                    1.0,
                    size: 17,
                    fill: true);
            }

            private static UIElement BuildFilledVectorIcon(
                IEnumerable<string> pathDataList,
                MediaBrush brush,
                double strokeThickness,
                double size,
                bool fill)
            {
                Viewbox box = new Viewbox
                {
                    Width = size,
                    Height = size,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas canvas = new Canvas { Width = 24, Height = 24 };

                foreach (string raw in pathDataList)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    try
                    {
                        Geometry geom = Geometry.Parse(raw.Trim());
                        canvas.Children.Add(new System.Windows.Shapes.Path
                        {
                            Fill = fill ? brush : null,
                            Stroke = brush,
                            StrokeThickness = strokeThickness,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round,
                            StrokeLineJoin = PenLineJoin.Round,
                            Data = geom
                        });
                    }
                    catch { }
                }

                box.Child = canvas;
                return box;
            }
        }
    }
}
