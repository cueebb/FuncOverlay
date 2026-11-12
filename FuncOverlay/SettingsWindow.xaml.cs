using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using FuncOverlay.Core;

namespace FuncOverlay
{
    public partial class SettingsWindow : Window
    {
        private readonly PluginManager _pluginManager;
        private readonly MainWindow _mainWindow;
        private bool _isRecordingKeybind = false;

        public SettingsWindow(PluginManager pluginManager, MainWindow mainWindow)
        {
            _pluginManager = pluginManager;
            _mainWindow = mainWindow;
            InitializeComponent();

            // Set current values from MainWindow
            TransitionSlider.Value = _mainWindow.FadeDurationMs;
            TransitionValueText.Text = $"{_mainWindow.FadeDurationMs} ms";

            AlphaSlider.Value = Math.Round(_mainWindow.OverlayOpacity * 100);
            AlphaValueText.Text = $"{Math.Round(_mainWindow.OverlayOpacity * 100)}%";

            HotkeyTextBox.Text = _mainWindow.HotkeyDisplayString;

            // Load and display installed plugins
            RefreshInstalledPluginsList();

            // Key listener for keybind recording
            this.PreviewKeyDown += SettingsWindow_PreviewKeyDown;
        }

        private void TransitionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TransitionValueText == null) return;
            int ms = (int)e.NewValue;
            TransitionValueText.Text = $"{ms} ms";
            if (_mainWindow != null)
            {
                _mainWindow.FadeDurationMs = ms;
            }
        }

        private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AlphaValueText == null) return;
            int pct = (int)e.NewValue;
            AlphaValueText.Text = $"{pct}%";
            if (_mainWindow != null)
            {
                _mainWindow.OverlayOpacity = pct / 100.0;
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
            System.Windows.MessageBox.Show("FuncOverlay plugins reloaded successfully.", "FuncOverlay", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void InstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Select Plugin Archive (.zip) or Folder",
                Filter = "Plugin Archive (*.zip)|*.zip|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                bool success = _pluginManager.ExtractPluginArchive(dlg.FileName);
                if (success)
                {
                    _mainWindow.ReloadOverlayContent();
                    RefreshInstalledPluginsList();
                    System.Windows.MessageBox.Show("Plugin extracted and installed into plugins directory.", "FuncOverlay", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("Failed to extract plugin archive.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
                Background = new SolidColorBrush(Color.FromRgb(15, 15, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel container = new StackPanel();

            // Top Row: Name & Description on left, Buttons on right
            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Name & Description
            StackPanel infoPanel = new StackPanel();
            StackPanel nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            TextBlock nameText = new TextBlock
            {
                Text = plugin.Metadata.name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = Brushes.White
            };
            Border langBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock langText = new TextBlock
            {
                Text = plugin.Metadata.entryPoint,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170))
            };
            langBadge.Child = langText;
            nameRow.Children.Add(nameText);
            nameRow.Children.Add(langBadge);

            TextBlock descText = new TextBlock
            {
                Text = plugin.Metadata.description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                TextWrapping = TextWrapping.Wrap
            };
            infoPanel.Children.Add(nameRow);
            infoPanel.Children.Add(descText);
            Grid.SetColumn(infoPanel, 0);
            topRow.Children.Add(infoPanel);

            // Right: Secondary Buttons (Black with white outline)
            StackPanel buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };

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
                var res = System.Windows.MessageBox.Show($"Are you sure you want to delete plugin '{plugin.Metadata.name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    _pluginManager.DeletePlugin(plugin.FolderName);
                    _mainWindow.ReloadOverlayContent();
                    RefreshInstalledPluginsList();
                }
            };

            buttonPanel.Children.Add(openFolderBtn);
            buttonPanel.Children.Add(deleteBtn);
            Grid.SetColumn(buttonPanel, 1);
            topRow.Children.Add(buttonPanel);

            container.Children.Add(topRow);

            // Bottom: Plugin Settings (Defined by metadata.json: string, int, bool, select)
            if (plugin.Metadata.settingsSchema != null && plugin.Metadata.settingsSchema.Count > 0)
            {
                Border settingsBox = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
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
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                settingsPanel.Children.Add(settingsHeader);

                foreach (var def in plugin.Metadata.settingsSchema)
                {
                    Grid settingRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    settingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    settingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    StackPanel labelPanel = new StackPanel();
                    labelPanel.Children.Add(new TextBlock { Text = def.label, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231)) });
                    if (!string.IsNullOrEmpty(def.description))
                    {
                        labelPanel.Children.Add(new TextBlock { Text = def.description, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)) });
                    }
                    Grid.SetColumn(labelPanel, 0);
                    settingRow.Children.Add(labelPanel);

                    // Dynamic control based on def.type (bool, int, string, select)
                    if (def.type == "bool")
                    {
                        CheckBox chk = new CheckBox
                        {
                            IsChecked = Convert.ToBoolean(def.value ?? def.defaultValue ?? false),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        chk.Click += (s, e) =>
                        {
                            _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, chk.IsChecked == true);
                        };
                        Grid.SetColumn(chk, 1);
                        settingRow.Children.Add(chk);
                    }
                    else if (def.type == "int")
                    {
                        TextBox txt = new TextBox
                        {
                            Text = Convert.ToString(def.value ?? def.defaultValue ?? 0),
                            Width = 70,
                            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                            TextAlignment = TextAlignment.Right,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11,
                            Padding = new Thickness(4, 2, 4, 2)
                        };
                        txt.LostFocus += (s, e) =>
                        {
                            if (int.TryParse(txt.Text, out int parsed))
                            {
                                _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, parsed);
                            }
                        };
                        Grid.SetColumn(txt, 1);
                        settingRow.Children.Add(txt);
                    }
                    else if (def.type == "select" && def.options != null)
                    {
                        ComboBox cb = new ComboBox
                        {
                            ItemsSource = def.options,
                            SelectedItem = Convert.ToString(def.value ?? def.defaultValue),
                            Width = 120,
                            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
                            Foreground = Brushes.White,
                            FontSize = 11
                        };
                        cb.SelectionChanged += (s, e) =>
                        {
                            if (cb.SelectedItem != null)
                            {
                                _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, cb.SelectedItem.ToString()!);
                            }
                        };
                        Grid.SetColumn(cb, 1);
                        settingRow.Children.Add(cb);
                    }
                    else // string default
                    {
                        TextBox txt = new TextBox
                        {
                            Text = Convert.ToString(def.value ?? def.defaultValue ?? ""),
                            Width = 140,
                            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11,
                            Padding = new Thickness(4, 2, 4, 2)
                        };
                        txt.LostFocus += (s, e) =>
                        {
                            _pluginManager.UpdatePluginSetting(plugin.FolderName, def.key, txt.Text);
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
