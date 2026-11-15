using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FuncOverlay.Core;
using FuncOverlay.Dialogs;

using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace FuncOverlay
{
    public partial class SettingsWindow : Window
    {
        private readonly PluginManager _pluginManager;
        private readonly MainWindow _mainWindow;
        private readonly DispatcherTimer _toastTimer;
        private bool _isRecordingKeybind = false;
        private bool _isInitializing = true;
        private string? _pendingDeleteFolder;

        public SettingsWindow(PluginManager pluginManager, MainWindow mainWindow)
        {
            _pluginManager = pluginManager;
            _mainWindow = mainWindow;
            InitializeComponent();

            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                StatusToastBorder.Visibility = Visibility.Collapsed;
            };

            // Load current General settings
            RunOnStartupCheckBox.IsChecked = AppSettingsManager.GetRunOnStartup();

            TransitionSlider.Value = _mainWindow.FadeDurationMs;
            TransitionValueText.Text = $"{_mainWindow.FadeDurationMs} ms";

            AlphaSlider.Value = Math.Round(_mainWindow.OverlayOpacity * 100);
            AlphaValueText.Text = $"{Math.Round(_mainWindow.OverlayOpacity * 100)}%";

            HotkeyTextBox.Text = _mainWindow.HotkeyDisplayString;

            _isInitializing = false;

            // Load and display installed plugins
            RefreshInstalledPluginsList();

            // Key listener for keybind recording
            this.PreviewKeyDown += SettingsWindow_PreviewKeyDown;
            this.Closing += SettingsWindow_Closing;
        }

        private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Commit any active input focus and persist latest settings cleanly
            Keyboard.ClearFocus();
            _mainWindow?.SaveSettings();
        }

        public void RefreshFromMainWindow()
        {
            _isInitializing = true;
            RunOnStartupCheckBox.IsChecked = AppSettingsManager.GetRunOnStartup();
            TransitionSlider.Value = _mainWindow.FadeDurationMs;
            TransitionValueText.Text = $"{_mainWindow.FadeDurationMs} ms";
            AlphaSlider.Value = Math.Round(_mainWindow.OverlayOpacity * 100);
            AlphaValueText.Text = $"{Math.Round(_mainWindow.OverlayOpacity * 100)}%";
            HotkeyTextBox.Text = _mainWindow.HotkeyDisplayString;
            _isInitializing = false;

            RefreshInstalledPluginsList();
        }

        private void ShowToast(string message)
        {
            StatusToastText.Text = message;
            StatusToastBorder.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && !IsInsideButton(e.OriginalSource as DependencyObject))
            {
                this.DragMove();
            }
        }

        private static bool IsInsideButton(DependencyObject? dep)
        {
            while (dep != null)
            {
                if (dep is System.Windows.Controls.Button) return true;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void RunOnStartupCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool enable = RunOnStartupCheckBox.IsChecked == true;
            bool ok = AppSettingsManager.SetRunOnStartup(enable);
            if (!ok)
            {
                RunOnStartupCheckBox.IsChecked = !enable;
                ShowToast("Failed to update Windows startup setting.");
            }
            else
            {
                ShowToast(enable ? "Run on startup enabled." : "Run on startup disabled.");
            }
        }

        private void TransitionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TransitionValueText == null) return;
            int ms = (int)Math.Round(e.NewValue);
            TransitionValueText.Text = $"{ms} ms";
            if (_mainWindow != null && !_isInitializing)
            {
                _mainWindow.FadeDurationMs = ms;
                _mainWindow.SaveSettings();
            }
        }

        private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AlphaValueText == null) return;
            int pct = (int)Math.Round(e.NewValue);
            AlphaValueText.Text = $"{pct}%";
            if (_mainWindow != null && !_isInitializing)
            {
                _mainWindow.OverlayOpacity = pct / 100.0;
                _mainWindow.RefreshBackdropTint();
                _mainWindow.SaveSettings();
            }
        }

        private void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mainWindow != null && !_isInitializing)
            {
                _mainWindow.SaveSettings();
            }
        }

        private void ChangeKeybindButton_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingKeybind = true;
            KeybindRecordingHint.Visibility = Visibility.Visible;
            ChangeKeybindButton.Content = "Listening...";
            HotkeyTextBox.Focus();
        }

        private void ResetKeybindButton_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingKeybind = false;
            KeybindRecordingHint.Visibility = Visibility.Collapsed;
            ChangeKeybindButton.Content = "Change Keybind";
            HotkeyTextBox.Text = "Alt + Space";
            _mainWindow.UpdateHotkey("Alt + Space", ModifierKeys.Alt, Key.Space);
            ShowToast("Hotkey reset to Alt + Space.");
        }

        private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isRecordingKeybind) return;

            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                _isRecordingKeybind = false;
                KeybindRecordingHint.Visibility = Visibility.Collapsed;
                ChangeKeybindButton.Content = "Change Keybind";
                return;
            }

            ModifierKeys modifiers = Keyboard.Modifiers;
            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // Skip bare modifier key presses
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            string modStr = "";
            if (modifiers.HasFlag(ModifierKeys.Control)) modStr += "Ctrl + ";
            if (modifiers.HasFlag(ModifierKeys.Alt)) modStr += "Alt + ";
            if (modifiers.HasFlag(ModifierKeys.Shift)) modStr += "Shift + ";

            string keyName = key.ToString();
            if (key == Key.Space) keyName = "Space";
            if (key == Key.Return) keyName = "Enter";

            string finalCombo = modStr + keyName;
            HotkeyTextBox.Text = finalCombo;

            _isRecordingKeybind = false;
            KeybindRecordingHint.Visibility = Visibility.Collapsed;
            ChangeKeybindButton.Content = "Change Keybind";

            _mainWindow.UpdateHotkey(finalCombo, modifiers, key);
            ShowToast($"Hotkey set to {finalCombo}.");
        }

        private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(_pluginManager.PluginsDirectory))
            {
                Directory.CreateDirectory(_pluginManager.PluginsDirectory);
            }
            Process.Start("explorer.exe", _pluginManager.PluginsDirectory);
        }

        private void RestartFuncOverlay_Click(object sender, RoutedEventArgs e)
        {
            _pluginManager.LoadAllPlugins();
            _mainWindow.ReloadOverlayContent();
            RefreshInstalledPluginsList();
            ShowToast("FuncOverlay plugins reloaded successfully.");
        }

        private void InstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            // Custom monochrome WPF picker (zero default Windows shell OpenFileDialog)
            PluginPickerWindow picker = new PluginPickerWindow
            {
                Owner = this
            };

            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedPath))
            {
                bool success = _pluginManager.InstallFromPath(picker.SelectedPath);
                if (success)
                {
                    _mainWindow.ReloadOverlayContent();
                    RefreshInstalledPluginsList();
                    ShowToast("Plugin installed into plugins directory.");
                }
                else
                {
                    ShowToast("Failed to install selected plugin.");
                }
            }
        }

        private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
        {
            _pendingDeleteFolder = null;
            ConfirmDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void ConfirmAccept_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pendingDeleteFolder))
            {
                _pluginManager.DeletePlugin(_pendingDeleteFolder);
                _mainWindow.ReloadOverlayContent();
                RefreshInstalledPluginsList();
                ShowToast("Plugin deleted.");
            }
            _pendingDeleteFolder = null;
            ConfirmDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void RefreshInstalledPluginsList()
        {
            InstalledPluginsPanel.Children.Clear();

            if (_pluginManager.LoadedPluginFolders.Count == 0)
            {
                NoPluginsText.Visibility = Visibility.Visible;
                InstalledPluginsPanel.Children.Add(NoPluginsText);
                return;
            }

            NoPluginsText.Visibility = Visibility.Collapsed;

            foreach (var plugin in _pluginManager.LoadedPluginFolders)
            {
                Border card = CreatePluginCard(plugin);
                InstalledPluginsPanel.Children.Add(card);
            }
        }

        private Border CreatePluginCard(PluginFolderItem plugin)
        {
            Border border = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(15, 15, 18)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel container = new StackPanel();

            // Top Row: Name & Description on left, Enabled/Disabled slider + Buttons stacked vertically on right
            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Name & Description
            StackPanel infoPanel = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            StackPanel nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            TextBlock nameText = new TextBlock
            {
                Text = plugin.Metadata.name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = MediaBrushes.White
            };
            Border langBadge = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock langText = new TextBlock
            {
                Text = plugin.Metadata.entryPoint,
                FontFamily = new MediaFontFamily("Consolas"),
                FontSize = 9,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170))
            };
            langBadge.Child = langText;
            nameRow.Children.Add(nameText);
            nameRow.Children.Add(langBadge);

            TextBlock descText = new TextBlock
            {
                Text = plugin.Metadata.description,
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                TextWrapping = TextWrapping.Wrap
            };
            infoPanel.Children.Add(nameRow);
            infoPanel.Children.Add(descText);
            Grid.SetColumn(infoPanel, 0);
            topRow.Children.Add(infoPanel);

            // Right: Enabled / Disabled on/off slider ABOVE Open Folder & Delete Plugin buttons
            StackPanel rightControls = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };

            // Enabled / Disabled Row (above Delete Plugin & Open Folder)
            StackPanel toggleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 8)
            };

            TextBlock stateText = new TextBlock
            {
                Text = plugin.Metadata.enabled ? "Enabled" : "Disabled",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = plugin.Metadata.enabled
                    ? MediaBrushes.White
                    : new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            CheckBox enabledToggle = new CheckBox
            {
                Style = (Style)FindResource("ToggleSwitchStyle"),
                IsChecked = plugin.Metadata.enabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            enabledToggle.Click += (s, e) =>
            {
                bool isNowEnabled = enabledToggle.IsChecked == true;
                plugin.Metadata.enabled = isNowEnabled;
                stateText.Text = isNowEnabled ? "Enabled" : "Disabled";
                stateText.Foreground = isNowEnabled
                    ? MediaBrushes.White
                    : new SolidColorBrush(MediaColor.FromRgb(161, 161, 170));
                _pluginManager.SetPluginEnabled(plugin.FolderName, isNowEnabled);
                _mainWindow.ReloadOverlayContent();
            };

            toggleRow.Children.Add(stateText);
            toggleRow.Children.Add(enabledToggle);
            rightControls.Children.Add(toggleRow);

            // Secondary Buttons Row (Black with white outline)
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button openFolderBtn = new Button
            {
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Content = "Open Folder",
                Margin = new Thickness(0, 0, 6, 0)
            };
            openFolderBtn.Click += (s, e) =>
            {
                if (Directory.Exists(plugin.FolderPath)) Process.Start("explorer.exe", plugin.FolderPath);
            };

            Button deleteBtn = new Button
            {
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Content = "Delete Plugin"
            };
            deleteBtn.Click += (s, e) =>
            {
                _pendingDeleteFolder = plugin.FolderName;
                ConfirmDialogTitle.Text = "Delete Plugin";
                ConfirmDialogMessage.Text = $"Are you sure you want to remove '{plugin.Metadata.name}' from the plugins directory?";
                ConfirmDialogOverlay.Visibility = Visibility.Visible;
            };

            buttonPanel.Children.Add(openFolderBtn);
            buttonPanel.Children.Add(deleteBtn);
            rightControls.Children.Add(buttonPanel);

            Grid.SetColumn(rightControls, 1);
            topRow.Children.Add(rightControls);

            container.Children.Add(topRow);

            // Position, 9-Point Anchor (Screen or Other Plugin) & Lock Position
            container.Children.Add(CreatePositionAndAnchorBox(plugin));

            // Bottom: Plugin Settings (Defined by metadata.json: string, int, bool, select)
            if (plugin.Metadata.settingsSchema != null && plugin.Metadata.settingsSchema.Count > 0)
            {
                Border settingsBox = new Border
                {
                    Background = new SolidColorBrush(MediaColor.FromRgb(0, 0, 0)),
                    BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 12, 0, 0)
                };

                StackPanel settingsPanel = new StackPanel();
                TextBlock settingsHeader = new TextBlock
                {
                    Text = "PLUGIN SETTINGS (metadata.json)",
                    FontSize = 10,
                    FontFamily = new MediaFontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                settingsPanel.Children.Add(settingsHeader);

                foreach (var def in plugin.Metadata.settingsSchema)
                {
                    Grid settingRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    settingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    settingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    StackPanel labelPanel = new StackPanel();
                    labelPanel.Children.Add(new TextBlock { Text = def.label, FontSize = 12, Foreground = new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)) });
                    if (!string.IsNullOrEmpty(def.description))
                    {
                        labelPanel.Children.Add(new TextBlock { Text = def.description, FontSize = 10, Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)) });
                    }
                    Grid.SetColumn(labelPanel, 0);
                    settingRow.Children.Add(labelPanel);

                    // Dynamic control based on def.type (bool, int, string, select)
                    if (def.type == "bool")
                    {
                        CheckBox chk = new CheckBox
                        {
                            Style = (Style)FindResource("ToggleSwitchStyle"),
                            IsChecked = ParseBoolSetting(def.value ?? def.defaultValue),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        chk.Click += (s, e) =>
                        {
                            _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, chk.IsChecked == true);
                            _mainWindow.NotifyPluginSettingsUpdated(plugin.FolderName);
                        };
                        Grid.SetColumn(chk, 1);
                        settingRow.Children.Add(chk);
                    }
                    else if (def.type == "int")
                    {
                        TextBox txt = new TextBox
                        {
                            Style = (Style)FindResource("MonochromeTextBoxStyle"),
                            Text = Convert.ToString(def.value ?? def.defaultValue ?? 0),
                            Width = 80,
                            TextAlignment = TextAlignment.Right
                        };
                        txt.TextChanged += (s, e) =>
                        {
                            if (int.TryParse(txt.Text, out int parsed))
                            {
                                _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, parsed);
                                _mainWindow.NotifyPluginSettingsUpdated(plugin.FolderName);
                            }
                        };
                        Grid.SetColumn(txt, 1);
                        settingRow.Children.Add(txt);
                    }
                    else if (def.type == "select" && def.options != null)
                    {
                        ComboBox cb = new ComboBox
                        {
                            Style = (Style)FindResource("MonochromeComboBoxStyle"),
                            ItemsSource = def.options,
                            SelectedItem = Convert.ToString(def.value ?? def.defaultValue),
                            Width = 136
                        };
                        cb.SelectionChanged += (s, e) =>
                        {
                            if (cb.SelectedItem != null)
                            {
                                _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, cb.SelectedItem.ToString()!);
                                _mainWindow.NotifyPluginSettingsUpdated(plugin.FolderName);
                            }
                        };
                        Grid.SetColumn(cb, 1);
                        settingRow.Children.Add(cb);
                    }
                    else // string default
                    {
                        TextBox txt = new TextBox
                        {
                            Style = (Style)FindResource("MonochromeTextBoxStyle"),
                            Text = Convert.ToString(def.value ?? def.defaultValue ?? ""),
                            Width = 150
                        };
                        txt.TextChanged += (s, e) =>
                        {
                            _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, txt.Text ?? "");
                            _mainWindow.NotifyPluginSettingsUpdated(plugin.FolderName);
                        };
                        Grid.SetColumn(txt, 1);
                        settingRow.Children.Add(txt);
                    }

                    settingsPanel.Children.Add(settingRow);
                }

                settingsBox.Child = settingsPanel;
                container.Children.Add(settingsBox);
            }

            border.Child = container;
            return border;
        }

        private Border CreatePositionAndAnchorBox(PluginFolderItem plugin)
        {
            PluginPosition pos = plugin.Metadata.position ?? new PluginPosition();
            string currentAnchor = PluginManager.NormalizeAnchor(pos.anchor);
            string currentTarget = string.IsNullOrWhiteSpace(pos.anchorTarget) ? "screen" : pos.anchorTarget.Trim();
            string currentTargetAnchor = PluginManager.NormalizeAnchor(
                string.IsNullOrWhiteSpace(pos.targetAnchor) ? currentAnchor : pos.targetAnchor);
            int currentX = pos.x;
            int currentY = pos.y;

            Border posBox = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(0, 0, 0)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 12, 0, 0)
            };

            StackPanel posPanel = new StackPanel();
            posPanel.Children.Add(new TextBlock
            {
                Text = "POSITION & ANCHOR",
                FontSize = 10,
                FontFamily = new MediaFontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // 1. Lock Position toggle row
            Grid lockRow = new Grid { Margin = new Thickness(0, 2, 0, 10) };
            lockRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lockRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel lockLabelPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            lockLabelPanel.Children.Add(new TextBlock
            {
                Text = "Lock Position",
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(228, 228, 231))
            });
            lockLabelPanel.Children.Add(new TextBlock
            {
                Text = "Prevent dragging on the overlay; metadata.json, Settings, and intents still apply.",
                FontSize = 10,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(lockLabelPanel, 0);
            lockRow.Children.Add(lockLabelPanel);

            CheckBox lockToggle = new CheckBox
            {
                Style = (Style)FindResource("ToggleSwitchStyle"),
                IsChecked = plugin.Metadata.locked,
                VerticalAlignment = VerticalAlignment.Center
            };
            lockToggle.Click += (s, e) =>
            {
                bool isLocked = lockToggle.IsChecked == true;
                plugin.Metadata.locked = isLocked;
                _pluginManager.SetPluginLocked(plugin.FolderName, isLocked);
                _mainWindow.UpdatePluginLockState(plugin.Metadata.id, isLocked);
            };
            Grid.SetColumn(lockToggle, 1);
            lockRow.Children.Add(lockToggle);
            posPanel.Children.Add(lockRow);

            posPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            // 2. Anchor Target (Screen vs Another Plugin) & Target Corner
            Grid targetRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel targetCol = new StackPanel();
            targetCol.Children.Add(new TextBlock
            {
                Text = "Anchor To",
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            ComboBox targetCombo = new ComboBox
            {
                Style = (Style)FindResource("MonochromeComboBoxStyle")
            };

            ComboBoxItem screenItem = new ComboBoxItem
            {
                Content = "Screen (Display)",
                Tag = "screen"
            };
            targetCombo.Items.Add(screenItem);
            ComboBoxItem selectedTargetItem = screenItem;

            foreach (var other in _pluginManager.LoadedPluginFolders)
            {
                if (string.Equals(other.Metadata.id, plugin.Metadata.id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var item = new ComboBoxItem
                {
                    Content = $"{other.Metadata.name} ({other.Metadata.id})",
                    Tag = other.Metadata.id
                };
                targetCombo.Items.Add(item);
                if (string.Equals(currentTarget, other.Metadata.id, StringComparison.OrdinalIgnoreCase))
                {
                    selectedTargetItem = item;
                }
            }

            if (!string.Equals(currentTarget, "screen", StringComparison.OrdinalIgnoreCase) && selectedTargetItem == screenItem)
            {
                var customItem = new ComboBoxItem
                {
                    Content = currentTarget,
                    Tag = currentTarget
                };
                targetCombo.Items.Add(customItem);
                selectedTargetItem = customItem;
            }

            targetCombo.SelectedItem = selectedTargetItem;
            targetCol.Children.Add(targetCombo);
            Grid.SetColumn(targetCol, 0);
            targetRow.Children.Add(targetCol);

            StackPanel targetCornerCol = new StackPanel
            {
                Visibility = string.Equals(currentTarget, "screen", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Collapsed
                    : Visibility.Visible
            };
            targetCornerCol.Children.Add(new TextBlock
            {
                Text = "Target Plugin Corner",
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            ComboBox targetAnchorCombo = new ComboBox
            {
                Style = (Style)FindResource("MonochromeComboBoxStyle")
            };
            ComboBoxItem? selectedCornerItem = null;
            foreach (string key in PluginManager.AnchorOptions)
            {
                var cornerItem = new ComboBoxItem
                {
                    Content = FormatAnchorLabel(key),
                    Tag = key
                };
                targetAnchorCombo.Items.Add(cornerItem);
                if (string.Equals(key, currentTargetAnchor, StringComparison.OrdinalIgnoreCase))
                {
                    selectedCornerItem = cornerItem;
                }
            }
            if (selectedCornerItem != null)
            {
                targetAnchorCombo.SelectedItem = selectedCornerItem;
            }
            targetCornerCol.Children.Add(targetAnchorCombo);
            Grid.SetColumn(targetCornerCol, 2);
            targetRow.Children.Add(targetCornerCol);
            posPanel.Children.Add(targetRow);

            // 3. 9-Point Widget Anchor Matrix + Signed X/Y Offsets
            Grid matrixAndOffsetGrid = new Grid();
            matrixAndOffsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            matrixAndOffsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            matrixAndOffsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel anchorMatrixCol = new StackPanel();
            anchorMatrixCol.Children.Add(new TextBlock
            {
                Text = "Widget Anchor (9-Point)",
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            Border matrixFrame = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(9, 9, 11)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            Grid matrixGrid = new Grid { Width = 72, Height = 72 };
            for (int i = 0; i < 3; i++)
            {
                matrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                matrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            TextBlock activeAnchorText = new TextBlock
            {
                Text = FormatAnchorLabel(currentAnchor),
                FontSize = 10,
                FontFamily = new MediaFontFamily("Consolas"),
                Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var anchorButtons = new System.Collections.Generic.Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);

            void RefreshAnchorMatrixVisuals()
            {
                activeAnchorText.Text = FormatAnchorLabel(currentAnchor);
                foreach (var kv in anchorButtons)
                {
                    bool isSel = string.Equals(kv.Key, currentAnchor, StringComparison.OrdinalIgnoreCase);
                    kv.Value.Background = isSel
                        ? new SolidColorBrush(MediaColor.FromRgb(255, 255, 255))
                        : new SolidColorBrush(MediaColor.FromRgb(24, 24, 27));
                    kv.Value.BorderBrush = isSel
                        ? new SolidColorBrush(MediaColor.FromRgb(255, 255, 255))
                        : new SolidColorBrush(MediaColor.FromRgb(63, 63, 70));
                }
            }

            void CommitPositionAndAnchor()
            {
                _pluginManager.UpdatePluginPosition(
                    plugin.Metadata.id,
                    currentX,
                    currentY,
                    currentAnchor,
                    currentTarget,
                    currentTargetAnchor);
                _mainWindow.RepositionAllWidgets();
            }

            for (int idx = 0; idx < PluginManager.AnchorOptions.Length; idx++)
            {
                string anchorKey = PluginManager.AnchorOptions[idx];
                int row = idx / 3;
                int col = idx % 3;

                Border pointBtn = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = new ToolTip
                    {
                        Content = FormatAnchorLabel(anchorKey),
                        Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                        Foreground = MediaBrushes.White,
                        BorderBrush = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70))
                    }
                };
                pointBtn.MouseLeftButtonDown += (s, e) =>
                {
                    currentAnchor = anchorKey;
                    RefreshAnchorMatrixVisuals();
                    CommitPositionAndAnchor();
                    e.Handled = true;
                };
                Grid.SetRow(pointBtn, row);
                Grid.SetColumn(pointBtn, col);
                matrixGrid.Children.Add(pointBtn);
                anchorButtons[anchorKey] = pointBtn;
            }

            RefreshAnchorMatrixVisuals();
            matrixFrame.Child = matrixGrid;
            anchorMatrixCol.Children.Add(matrixFrame);
            anchorMatrixCol.Children.Add(activeAnchorText);
            Grid.SetColumn(anchorMatrixCol, 0);
            matrixAndOffsetGrid.Children.Add(anchorMatrixCol);

            // Right: Signed X / Y inputs
            StackPanel offsetsCol = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            Grid xyGrid = new Grid();
            xyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            xyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            xyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel xCol = new StackPanel();
            xCol.Children.Add(new TextBlock
            {
                Text = "Offset X (px)",
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            TextBox xBox = new TextBox
            {
                Style = (Style)FindResource("MonochromeTextBoxStyle"),
                Text = currentX.ToString(),
                TextAlignment = TextAlignment.Right
            };
            xBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(xBox.Text, out int parsedX))
                {
                    currentX = parsedX;
                    CommitPositionAndAnchor();
                }
            };
            xCol.Children.Add(xBox);
            Grid.SetColumn(xCol, 0);
            xyGrid.Children.Add(xCol);

            StackPanel yCol = new StackPanel();
            yCol.Children.Add(new TextBlock
            {
                Text = "Offset Y (px)",
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(212, 212, 216)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            TextBox yBox = new TextBox
            {
                Style = (Style)FindResource("MonochromeTextBoxStyle"),
                Text = currentY.ToString(),
                TextAlignment = TextAlignment.Right
            };
            yBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(yBox.Text, out int parsedY))
                {
                    currentY = parsedY;
                    CommitPositionAndAnchor();
                }
            };
            yCol.Children.Add(yBox);
            Grid.SetColumn(yCol, 2);
            xyGrid.Children.Add(yCol);

            offsetsCol.Children.Add(xyGrid);
            offsetsCol.Children.Add(new TextBlock
            {
                Text = "Moves the widget's selected anchor point from the anchor target (negative values supported).",
                FontSize = 10,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });

            Grid.SetColumn(offsetsCol, 2);
            matrixAndOffsetGrid.Children.Add(offsetsCol);
            posPanel.Children.Add(matrixAndOffsetGrid);

            targetCombo.SelectionChanged += (s, e) =>
            {
                if (targetCombo.SelectedItem is ComboBoxItem sel && sel.Tag is string tagVal)
                {
                    currentTarget = tagVal;
                    targetCornerCol.Visibility = string.Equals(currentTarget, "screen", StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                    CommitPositionAndAnchor();
                }
            };

            targetAnchorCombo.SelectionChanged += (s, e) =>
            {
                if (targetAnchorCombo.SelectedItem is ComboBoxItem sel && sel.Tag is string tagVal)
                {
                    currentTargetAnchor = tagVal;
                    CommitPositionAndAnchor();
                }
            };

            posBox.Child = posPanel;
            return posBox;
        }

        private static string FormatAnchorLabel(string anchor)
        {
            return PluginManager.NormalizeAnchor(anchor) switch
            {
                "top-left" => "Top Left",
                "top-center" => "Top Center",
                "top-right" => "Top Right",
                "center-left" => "Center Left",
                "center" => "Center",
                "center-right" => "Center Right",
                "bottom-left" => "Bottom Left",
                "bottom-center" => "Bottom Center",
                "bottom-right" => "Bottom Right",
                _ => "Top Left"
            };
        }

        private static bool ParseBoolSetting(object? raw)
        {
            if (raw == null) return false;
            if (raw is bool b) return b;
            if (raw is System.Text.Json.JsonElement elem)
            {
                if (elem.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                if (elem.ValueKind == System.Text.Json.JsonValueKind.False) return false;
            }
            return bool.TryParse(raw.ToString(), out bool parsed) && parsed;
        }
    }
}
