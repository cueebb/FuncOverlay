using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FuncOverlay.Core
{
    /// <summary>
    /// Deep integration Intent dispatcher allowing FuncOverlay plugins
    /// to control the overlay window, host settings, plugin states, and system launches.
    /// </summary>
    public class PluginIntentRouter
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT_STRUCT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT_STRUCT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT_STRUCT ptMinPosition;
            public POINT_STRUCT ptMaxPosition;
            public RECT_STRUCT rcNormalPosition;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOWMINIMIZED = 2;
        private const int WPF_RESTORETOMAXIMIZED = 2;
        private const uint GW_OWNER = 4;
        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int ERROR_ACCESS_DENIED = 5;

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

                    case "plugin.set_locked":
                        HandlePluginSetLocked(data, caller);
                        break;

                    case "plugin.move":
                        HandlePluginMove(data, caller);
                        break;

                    case "system.launch_app":
                        HandleLaunchApp(data);
                        break;

                    case "system.focus_or_launch_app":
                        HandleFocusOrLaunchApp(data);
                        break;

                    case "app.toast":
                        HandleToast(data);
                        break;

                    case "app.reload_plugins":
                        _pluginManager.LoadAllPlugins();
                        _mainWindow.ReloadOverlayContent();
                        break;

                    case "media.subscribe":
                        _mainWindow.StartMediaMonitoring();
                        break;

                    case "media.control":
                        HandleMediaControl(data);
                        break;

                    case "media.select_source":
                        HandleMediaSelectSource(data);
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

        private void HandlePluginSetLocked(JsonElement? data, PluginFolderItem caller)
        {
            string folderName = caller.FolderName;
            bool locked = true;

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

                if (TryGetBool(data.Value, "locked", out bool parsedLocked))
                {
                    locked = parsedLocked;
                }
            }

            if (_pluginManager.SetPluginLocked(folderName, locked))
            {
                _mainWindow.UpdatePluginLockState(folderName, locked);
                _mainWindow.SyncSettingsWindowIfOpen();
            }
        }

        private void HandlePluginMove(JsonElement? data, PluginFolderItem caller)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            var obj = data.Value;
            int x = TryGetInt(obj, "x", out int px) ? px : caller.Metadata.position.x;
            int y = TryGetInt(obj, "y", out int py) ? py : caller.Metadata.position.y;
            TryGetString(obj, "anchor", out string? anchor);
            TryGetString(obj, "anchorTarget", out string? anchorTarget);
            TryGetString(obj, "targetAnchor", out string? targetAnchor);

            _pluginManager.UpdatePluginPosition(caller.Metadata.id, x, y, anchor, anchorTarget, targetAnchor);
            _mainWindow.RepositionAllWidgets();
            _mainWindow.SyncSettingsWindowIfOpen();
        }

        private void HandleFocusOrLaunchApp(JsonElement? data)
        {
            string? path = null;
            string? args = null;
            bool closeOverlay = true;
            var processNames = new List<string>();
            var titleHints = new List<string>();

            if (data.HasValue && data.Value.ValueKind == JsonValueKind.Object)
            {
                var obj = data.Value;
                TryGetString(obj, "path", out path);
                TryGetString(obj, "args", out args);
                if (TryGetBool(obj, "closeOverlay", out bool closeVal)) closeOverlay = closeVal;

                if (obj.TryGetProperty("processNames", out JsonElement procArr) && procArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in procArr.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                            processNames.Add(el.GetString()!);
                    }
                }

                if (obj.TryGetProperty("windowTitleHints", out JsonElement titleArr) && titleArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in titleArr.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                            titleHints.Add(el.GetString()!);
                    }
                }
            }

            // Immediately drop Topmost + collapse overlay so it never blocks or covers the restored window
            if (closeOverlay)
            {
                _mainWindow.HideOverlayImmediately();
            }

            // 1. Try restoring an existing main window (handles taskbar-minimized AND tray-hidden windows)
            IntPtr targetHwnd = FindAppWindow(processNames, titleHints);
            if (targetHwnd != IntPtr.Zero)
            {
                bool accessDenied = false;
                RestoreAndFocusWindow(targetHwnd, out accessDenied);
                if (accessDenied)
                {
                    _mainWindow.ShowOverlayWithAnimation();
                    _mainWindow.ShowOverlayToast("OBS is running as Administrator — run FuncOverlay as Administrator to restore it.", 4500);
                }
                return;
            }

            // 2. Otherwise resolve executable and launch directly from C# host (outside plugin tree)
            string? resolved = ResolveExecutablePath(path, processNames);
            if (!string.IsNullOrEmpty(resolved))
            {
                LaunchExecutable(resolved, args);
                return;
            }

            // If we could not locate the app, surface a notice cleanly
            _mainWindow.ShowOverlayWithAnimation();
            _mainWindow.ShowOverlayToast("OBS Studio executable not found. Set OBS Executable Path in plugin settings.", 4000);
        }

        private static void HandleLaunchApp(JsonElement? data)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            if (!TryGetString(data.Value, "path", out string? path) || string.IsNullOrWhiteSpace(path)) return;

            TryGetString(data.Value, "args", out string? args);

            bool isUrl = path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         path.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

            if (isUrl || Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args ?? string.Empty,
                    UseShellExecute = true
                });
                return;
            }

            string? resolved = ResolveExecutablePath(path, null);
            if (!string.IsNullOrEmpty(resolved))
            {
                LaunchExecutable(resolved, args);
            }
        }

        private static IntPtr FindAppWindow(List<string> processNames, List<string> titleHints)
        {
            var matchingPids = new HashSet<uint>();
            IntPtr fallbackMainHandle = IntPtr.Zero;

            foreach (string procName in processNames)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(procName))
                    {
                        using (p)
                        {
                            matchingPids.Add((uint)p.Id);
                            if (fallbackMainHandle == IntPtr.Zero && p.MainWindowHandle != IntPtr.Zero)
                            {
                                fallbackMainHandle = p.MainWindowHandle;
                            }
                        }
                    }
                }
                catch { }
            }

            IntPtr visiblePidMain = IntPtr.Zero;
            IntPtr hiddenPidMain = IntPtr.Zero;
            IntPtr visibleTitleMain = IntPtr.Zero;
            IntPtr hiddenTitleMain = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                // Only match unowned top-level app windows (skips owned Qt docks/popups/dialogs)
                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;

                int style = GetWindowLong(hWnd, GWL_STYLE);
                bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
                if (!hasCaption) return true;

                int len = GetWindowTextLengthW(hWnd);
                if (len <= 0) return true;

                StringBuilder sb = new StringBuilder(len + 1);
                GetWindowTextW(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                bool pidMatched = false;
                if (matchingPids.Count > 0)
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    pidMatched = matchingPids.Contains(pid);
                }

                bool titleMatched = false;
                foreach (string hint in titleHints)
                {
                    if (title.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    {
                        titleMatched = true;
                        break;
                    }
                }

                if (!pidMatched && !titleMatched) return true;

                bool isVisible = IsWindowVisible(hWnd);
                if (pidMatched)
                {
                    if (isVisible && visiblePidMain == IntPtr.Zero) visiblePidMain = hWnd;
                    else if (!isVisible && hiddenPidMain == IntPtr.Zero) hiddenPidMain = hWnd;
                }
                else if (titleMatched)
                {
                    if (isVisible && visibleTitleMain == IntPtr.Zero) visibleTitleMain = hWnd;
                    else if (!isVisible && hiddenTitleMain == IntPtr.Zero) hiddenTitleMain = hWnd;
                }

                return true;
            }, IntPtr.Zero);

            if (visiblePidMain != IntPtr.Zero) return visiblePidMain;
            if (hiddenPidMain != IntPtr.Zero) return hiddenPidMain;
            if (fallbackMainHandle != IntPtr.Zero) return fallbackMainHandle;
            if (visibleTitleMain != IntPtr.Zero) return visibleTitleMain;
            return hiddenTitleMain;
        }

        private static void RestoreAndFocusWindow(IntPtr hWnd, out bool accessDenied)
        {
            accessDenied = false;
            try
            {
                WINDOWPLACEMENT placement = new WINDOWPLACEMENT
                {
                    length = Marshal.SizeOf<WINDOWPLACEMENT>()
                };
                bool hasPlacement = GetWindowPlacement(hWnd, ref placement);
                bool wasMaximized = hasPlacement &&
                    ((placement.flags & WPF_RESTORETOMAXIMIZED) != 0 || placement.showCmd == SW_SHOWMAXIMIZED);

                if (!IsWindowVisible(hWnd))
                {
                    ShowWindow(hWnd, SW_SHOW);
                    ShowWindowAsync(hWnd, SW_SHOW);
                }

                if (IsIconic(hWnd) || (hasPlacement && placement.showCmd == SW_SHOWMINIMIZED))
                {
                    int restoreCmd = wasMaximized ? SW_SHOWMAXIMIZED : SW_RESTORE;
                    ShowWindow(hWnd, restoreCmd);
                    ShowWindowAsync(hWnd, restoreCmd);
                }

                // Force visibility & check UIPI (Access Denied when target is elevated as Administrator)
                if (!SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW))
                {
                    if (Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED)
                    {
                        accessDenied = true;
                        return;
                    }
                }

                IntPtr fgHwnd = GetForegroundWindow();
                uint fgThread = fgHwnd != IntPtr.Zero ? GetWindowThreadProcessId(fgHwnd, out _) : 0;
                uint targetThread = GetWindowThreadProcessId(hWnd, out _);
                uint curThread = GetCurrentThreadId();
                bool attachedFg = false;
                bool attachedTarget = false;

                if (fgThread != 0 && fgThread != curThread)
                {
                    attachedFg = AttachThreadInput(curThread, fgThread, true);
                }
                if (targetThread != 0 && targetThread != curThread && targetThread != fgThread)
                {
                    attachedTarget = AttachThreadInput(curThread, targetThread, true);
                }

                SwitchToThisWindow(hWnd, true);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);

                if (attachedTarget) AttachThreadInput(curThread, targetThread, false);
                if (attachedFg) AttachThreadInput(curThread, fgThread, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreAndFocusWindow error: {ex.Message}");
            }
        }

        private static string? ResolveExecutablePath(string? configuredPath, List<string>? processNames)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            bool looksLikeObs = (!string.IsNullOrWhiteSpace(configuredPath) &&
                                 configuredPath.Contains("obs", StringComparison.OrdinalIgnoreCase)) ||
                                (processNames != null && processNames.Exists(n => n.StartsWith("obs", StringComparison.OrdinalIgnoreCase)));

            if (!looksLikeObs) return null;

            // Probe Windows Registry for OBS Studio install path
            try
            {
                object? regPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\OBS Studio", "", null)
                               ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\OBS Studio", "", null);
                if (regPath is string obsRoot && !string.IsNullOrWhiteSpace(obsRoot))
                {
                    string exe64 = Path.Combine(obsRoot, "bin", "64bit", "obs64.exe");
                    if (File.Exists(exe64)) return exe64;
                    string exe32 = Path.Combine(obsRoot, "bin", "32bit", "obs32.exe");
                    if (File.Exists(exe32)) return exe32;
                }
            }
            catch { }

            string[] commonObsPaths =
            {
                @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
                @"C:\Program Files (x86)\obs-studio\bin\64bit\obs64.exe",
                @"C:\Program Files (x86)\Steam\steamapps\common\OBS Studio\bin\64bit\obs64.exe",
                @"D:\SteamLibrary\steamapps\common\OBS Studio\bin\64bit\obs64.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\obs-studio\bin\64bit\obs64.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), @"OBS Studio\OBS Studio (64bit).lnk")
            };

            foreach (string candidate in commonObsPaths)
            {
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static void LaunchExecutable(string path, string? args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args ?? string.Empty,
                UseShellExecute = true
            };

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                psi.WorkingDirectory = dir;
            }

            Process.Start(psi);
        }

        private void HandleMediaControl(JsonElement? data)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            var obj = data.Value;
            TryGetString(obj, "command", out string? command);
            TryGetString(obj, "sourceId", out string? sourceId);
            double? posSec = TryGetDouble(obj, "positionSec", out double p) ? (double?)p : null;
            if (!string.IsNullOrWhiteSpace(command))
            {
                _mainWindow.HandleMediaControlIntent(command, sourceId, posSec);
            }
        }

        private void HandleMediaSelectSource(JsonElement? data)
        {
            if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object) return;
            var obj = data.Value;
            TryGetString(obj, "sourceId", out string? sourceId);
            bool focusWindow = !TryGetBool(obj, "focusWindow", out bool fw) || fw;
            _mainWindow.HandleMediaSelectSourceIntent(sourceId ?? string.Empty, focusWindow);
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
