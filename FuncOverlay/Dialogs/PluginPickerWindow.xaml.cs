using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace FuncOverlay.Dialogs
{
    public partial class PluginPickerWindow : Window
    {
        private string _currentDirectory;
        private Border? _selectedRowBorder;

        public string? SelectedPath { get; private set; }

        public PluginPickerWindow()
        {
            InitializeComponent();

            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");

            _currentDirectory = Directory.Exists(downloads)
                ? downloads
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            LoadDirectory(_currentDirectory);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && !(e.OriginalSource is FrameworkElement fe && IsInsideButton(fe)))
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

        private void LoadDrives()
        {
            _currentDirectory = string.Empty;
            CurrentPathText.Text = "This PC (Drives)";
            SelectedPath = null;
            InstallSelectedButton.IsEnabled = false;
            EntriesPanel.Children.Clear();

            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                AddEntryRow(
                    label: $"{drive.Name} ({drive.VolumeLabel})",
                    tagText: "DRIVE",
                    fullPath: drive.RootDirectory.FullName,
                    isFolder: true,
                    isPluginFolder: false
                );
            }
        }

        private void LoadDirectory(string dirPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
                {
                    LoadDrives();
                    return;
                }

                _currentDirectory = dirPath;
                CurrentPathText.Text = dirPath;
                SelectedPath = null;
                _selectedRowBorder = null;
                InstallSelectedButton.IsEnabled = false;
                EntriesPanel.Children.Clear();

                DirectoryInfo dirInfo = new DirectoryInfo(dirPath);

                foreach (DirectoryInfo subDir in dirInfo.GetDirectories().OrderBy(d => d.Name))
                {
                    if ((subDir.Attributes & FileAttributes.Hidden) != 0) continue;
                    bool hasMetadata = File.Exists(Path.Combine(subDir.FullName, "metadata.json"));
                    AddEntryRow(
                        label: subDir.Name,
                        tagText: hasMetadata ? "PLUGIN FOLDER" : "FOLDER",
                        fullPath: subDir.FullName,
                        isFolder: true,
                        isPluginFolder: hasMetadata
                    );
                }

                foreach (FileInfo file in dirInfo.GetFiles("*.zip").OrderBy(f => f.Name))
                {
                    AddEntryRow(
                        label: file.Name,
                        tagText: ".ZIP",
                        fullPath: file.FullName,
                        isFolder: false,
                        isPluginFolder: false
                    );
                }

                if (EntriesPanel.Children.Count == 0)
                {
                    EntriesPanel.Children.Add(new TextBlock
                    {
                        Text = "No folders or .zip archives in this directory.",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)),
                        Margin = new Thickness(12, 16, 12, 16),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
            }
            catch (Exception)
            {
                EntriesPanel.Children.Clear();
                EntriesPanel.Children.Add(new TextBlock
                {
                    Text = "Unable to access this folder.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(161, 161, 170)),
                    Margin = new Thickness(12)
                });
            }
        }

        private void AddEntryRow(string label, string tagText, string fullPath, bool isFolder, bool isPluginFolder)
        {
            Border row = new Border
            {
                Background = MediaBrushes.Transparent,
                BorderBrush = MediaBrushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(2),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock nameTxt = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = isFolder ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border badge = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = tagText,
                FontFamily = new MediaFontFamily("Consolas"),
                FontSize = 9,
                Foreground = isPluginFolder || !isFolder
                    ? MediaBrushes.White
                    : new SolidColorBrush(MediaColor.FromRgb(161, 161, 170))
            };

            Grid.SetColumn(nameTxt, 0);
            Grid.SetColumn(badge, 1);
            grid.Children.Add(nameTxt);
            grid.Children.Add(badge);
            row.Child = grid;

            row.MouseEnter += (s, e) =>
            {
                if (_selectedRowBorder != row)
                {
                    row.Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27));
                }
            };
            row.MouseLeave += (s, e) =>
            {
                if (_selectedRowBorder != row)
                {
                    row.Background = MediaBrushes.Transparent;
                }
            };

            row.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && isFolder)
                {
                    LoadDirectory(fullPath);
                    return;
                }

                if (_selectedRowBorder != null)
                {
                    _selectedRowBorder.Background = MediaBrushes.Transparent;
                    _selectedRowBorder.BorderBrush = MediaBrushes.Transparent;
                }

                _selectedRowBorder = row;
                row.Background = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42));
                row.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(113, 113, 122));

                if (!isFolder || isPluginFolder || tagText == "FOLDER")
                {
                    SelectedPath = fullPath;
                    InstallSelectedButton.IsEnabled = tagText != "DRIVE";
                }

                if (e.ClickCount == 2 && !isFolder)
                {
                    this.DialogResult = true;
                    this.Close();
                }
            };

            EntriesPanel.Children.Add(row);
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentDirectory)) return;
            DirectoryInfo? parent = Directory.GetParent(_currentDirectory);
            if (parent != null)
            {
                LoadDirectory(parent.FullName);
            }
            else
            {
                LoadDrives();
            }
        }

        private void DownloadsButton_Click(object sender, RoutedEventArgs e)
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            if (Directory.Exists(downloads)) LoadDirectory(downloads);
        }

        private void DesktopButton_Click(object sender, RoutedEventArgs e)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktop)) LoadDirectory(desktop);
        }

        private void DrivesButton_Click(object sender, RoutedEventArgs e)
        {
            LoadDrives();
        }

        private void InstallSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SelectedPath))
            {
                this.DialogResult = true;
                this.Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedPath = null;
            this.DialogResult = false;
            this.Close();
        }
    }
}
