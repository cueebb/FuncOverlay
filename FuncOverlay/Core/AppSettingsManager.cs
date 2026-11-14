using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FuncOverlay.Core
{
    public class AppSettingsData
    {
        public int FadeDurationMs { get; set; } = 250;
        public double OverlayOpacity { get; set; } = 0.40;
        public string HotkeyDisplay { get; set; } = "Alt + Space";
        public uint HotkeyModifiers { get; set; } = 0x0001; // MOD_ALT
        public uint HotkeyVk { get; set; } = 0x20;          // VK_SPACE
    }

    public static class AppSettingsManager
    {
        private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupAppName = "FuncOverlay";
        private static readonly object _ioLock = new object();

        private static string AppDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuncOverlay");

        private static string SettingsFilePath =>
            Path.Combine(AppDataDirectory, "settings.json");

        private static string LegacySettingsFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettingsData Load()
        {
            lock (_ioLock)
            {
                try
                {
                    string? pathToRead = null;
                    if (File.Exists(SettingsFilePath))
                    {
                        pathToRead = SettingsFilePath;
                    }
                    else if (File.Exists(LegacySettingsFilePath))
                    {
                        pathToRead = LegacySettingsFilePath;
                    }

                    if (pathToRead != null)
                    {
                        string json = File.ReadAllText(pathToRead);
                        var data = JsonSerializer.Deserialize<AppSettingsData>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (data != null)
                        {
                            if (data.FadeDurationMs < 50 || data.FadeDurationMs > 2000) data.FadeDurationMs = 250;
                            if (data.OverlayOpacity <= 0.0 || data.OverlayOpacity > 1.0) data.OverlayOpacity = 0.40;
                            if (string.IsNullOrWhiteSpace(data.HotkeyDisplay) || data.HotkeyVk == 0)
                            {
                                data.HotkeyDisplay = "Alt + Space";
                                data.HotkeyModifiers = 0x0001;
                                data.HotkeyVk = 0x20;
                            }
                            return data;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load settings.json: {ex.Message}");
                }
                return new AppSettingsData();
            }
        }

        public static void Save(AppSettingsData data)
        {
            lock (_ioLock)
            {
                try
                {
                    if (!Directory.Exists(AppDataDirectory))
                    {
                        Directory.CreateDirectory(AppDataDirectory);
                    }

                    string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    string tempPath = SettingsFilePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, SettingsFilePath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to save settings.json: {ex.Message}");
                }
            }
        }

        public static bool GetRunOnStartup()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
                object? val = key?.GetValue(StartupAppName);
                return val != null && !string.IsNullOrWhiteSpace(val.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read startup registry key: {ex.Message}");
                return false;
            }
        }

        public static bool SetRunOnStartup(bool enable)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath);

                if (key == null) return false;

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrWhiteSpace(exePath))
                    {
                        exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FuncOverlay.exe");
                    }
                    key.SetValue(StartupAppName, $"\"{exePath}\"");
                }
                else
                {
                    if (key.GetValue(StartupAppName) != null)
                    {
                        key.DeleteValue(StartupAppName, throwOnMissingValue: false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update startup registry key: {ex.Message}");
                return false;
            }
        }
    }
}
