using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FuncOverlay.Core
{
    /// <summary>
    /// Deep integration Intent dispatcher allowing FuncOverlay plugins
    /// to control the overlay window, host settings, plugin states, and system launches.
    /// </summary>
    public class PluginIntentRouter
    {
        private readonly PluginManager _pluginManager;
        private readonly MainWindow _mainWindow;

        public PluginIntentRouter(PluginManager pluginManager, MainWindow mainWindow)
        {
            _pluginManager = pluginManager;
            _mainWindow = mainWindow;
        }

        public void Dispatch(string intentName, JsonElement? data, PluginFolderItem caller)
        {
            if (string.IsNullOrWhiteSpace(intentName)) return;

            string normalized = intentName.Trim().ToLowerInvariant();
            try
            {
                switch (normalized)
                {
                    case "overlay.close":
                    case "overlay.hide":
                        _mainWindow.HideOverlayWithAnimation();
                        break;

                    case "overlay.open":
                    case "overlay.show":
                        _mainWindow.ShowOverlayWithAnimation();
                        break;

                    case "overlay.toggle":
                        _mainWindow.ToggleOverlayWithAnimation();
                        break;

                    case "settings.open":
                        _mainWindow.OpenSettingsWindow();
                        break;

                    case "settings.update":
                        HandleSettingsUpdate(data);
                        break;

                    case "plugin.set_enabled":
                        HandlePluginSetEnabled(data, caller);
                        break;

                    case "plugin.update_setting":
                        HandlePluginUpdateSetting(data, caller);
                        break;

                    case "plugin.move":
                        HandlePluginMove(data, caller);
                        break;

                    case "system.launch_app":
                        HandleLaunchApp(data);
                        break;

                    case "app.toast":
                        HandleToast(data);
                        break;

                    case "app.reload_plugins":
                        _pluginManager.LoadAllPlugins();
                        _mainWindow.ReloadOverlayContent();
                        break;

                    case "app.quit":
                        System.Windows.Application.Current?.Shutdown();
                        break;

                    default:
                        Debug.WriteLine($"Unhandled plugin intent '{intentName}' from {caller.FolderName}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing intent '{intentName}': {ex.Message}");
            }
        }

        private void HandleSettingsUpdate(JsonElement? data)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            var obj = data.Value;
            bool changed = false;

            if (TryGetInt(obj, "fadeDurationMs", out int fadeMs))
            {
                _mainWindow.FadeDurationMs = Math.Clamp(fadeMs, 50, 1500);
                changed = true;
            }

            if (TryGetDouble(obj, "overlayOpacity", out double opacity))
            {
                if (opacity > 1.0) opacity /= 100.0;
                _mainWindow.OverlayOpacity = Math.Clamp(opacity, 0.05, 1.0);
                _mainWindow.RefreshBackdropTint();
                changed = true;
            }

            if (TryGetBool(obj, "runOnStartup", out bool startup))
            {
                AppSettingsManager.SetRunOnStartup(startup);
                changed = true;
            }

            if (changed)
            {
                _mainWindow.SaveSettings();
                _mainWindow.SyncSettingsWindowIfOpen();
            }
        }

        private void HandlePluginSetEnabled(JsonElement? data, PluginFolderItem caller)
        {
            string folderName = caller.FolderName;
            bool enabled = true;

            if (data.HasValue && data.Value.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(data.Value, "folderName", out string? targetFolder) && !string.IsNullOrWhiteSpace(targetFolder))
                {
                    folderName = targetFolder;
                }
                else if (TryGetString(data.Value, "pluginId", out string? pluginId) && !string.IsNullOrWhiteSpace(pluginId))
                {
                    var found = _pluginManager.FindFolderByPluginId(pluginId);
                    if (found != null) folderName = found.FolderName;
                }

                if (TryGetBool(data.Value, "enabled", out bool parsedEnabled))
                {
                    enabled = parsedEnabled;
                }
            }

            if (_pluginManager.SetPluginEnabled(folderName, enabled))
            {
                _mainWindow.ReloadOverlayContent();
                _mainWindow.SyncSettingsWindowIfOpen();
            }
        }

        private void HandlePluginUpdateSetting(JsonElement? data, PluginFolderItem caller)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            var obj = data.Value;

            if (!TryGetString(obj, "key", out string? key) || string.IsNullOrWhiteSpace(key)) return;

            string folderName = caller.FolderName;
            if (TryGetString(obj, "folderName", out string? targetFolder) && !string.IsNullOrWhiteSpace(targetFolder))
            {
                folderName = targetFolder;
            }

            object? val = null;
            if (obj.TryGetProperty("value", out JsonElement valEl))
            {
                val = valEl.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when valEl.TryGetInt32(out int i) => i,
                    JsonValueKind.Number when valEl.TryGetDouble(out double d) => d,
                    JsonValueKind.String => valEl.GetString(),
                    _ => valEl.ToString()
                };
            }

            if (_pluginManager.UpdatePluginSetting(folderName, key, val))
            {
                _mainWindow.SyncSettingsWindowIfOpen();
            }
        }

        private void HandlePluginMove(JsonElement? data, PluginFolderItem caller)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            var obj = data.Value;
            if (TryGetInt(obj, "x", out int x) && TryGetInt(obj, "y", out int y))
            {
                _pluginManager.UpdatePluginPosition(caller.Metadata.id, x, y);
                _mainWindow.ReloadOverlayContent();
            }
        }

        private static void HandleLaunchApp(JsonElement? data)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            if (!TryGetString(data.Value, "path", out string? path) || string.IsNullOrWhiteSpace(path)) return;

            TryGetString(data.Value, "args", out string? args);

            bool isUrl = path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         path.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

            if (!isUrl && !File.Exists(path) && !Directory.Exists(path))
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args ?? string.Empty,
                UseShellExecute = true
            };

            if (File.Exists(path))
            {
                psi.WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            }

            Process.Start(psi);
        }

        private void HandleToast(JsonElement? data)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            if (!TryGetString(data.Value, "message", out string? msg) || string.IsNullOrWhiteSpace(msg)) return;
            int duration = TryGetInt(data.Value, "durationMs", out int ms) ? Math.Clamp(ms, 800, 8000) : 2500;
            _mainWindow.ShowOverlayToast(msg, duration);
        }

        private static bool TryGetString(JsonElement obj, string prop, out string? value)
        {
            value = null;
            if (obj.TryGetProperty(prop, out JsonElement el) && el.ValueKind == JsonValueKind.String)
            {
                value = el.GetString();
                return true;
            }
            return false;
        }

        private static bool TryGetInt(JsonElement obj, string prop, out int value)
        {
            value = 0;
            if (obj.TryGetProperty(prop, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        private static bool TryGetDouble(JsonElement obj, string prop, out double value)
        {
            value = 0;
            if (obj.TryGetProperty(prop, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        private static bool TryGetBool(JsonElement obj, string prop, out bool value)
        {
            value = false;
            if (obj.TryGetProperty(prop, out JsonElement el))
            {
                if (el.ValueKind == JsonValueKind.True) { value = true; return true; }
                if (el.ValueKind == JsonValueKind.False) { value = false; return true; }
            }
            return false;
        }
    }
}
