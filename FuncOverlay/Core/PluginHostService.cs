using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FuncOverlay.Core
{
    public class PluginRenderButton
    {
        public string id { get; set; } = string.Empty;
        public string? secondaryAction { get; set; }
        public string? tooltip { get; set; }
        public string? dotColor { get; set; }
        public string? icon { get; set; }
        public bool blinking { get; set; }
        public bool active { get; set; }
    }

    public class PluginStatusItem
    {
        public string id { get; set; } = string.Empty;
        public string dotColor { get; set; } = "#71717A";
        public string text { get; set; } = string.Empty;
        public string? label { get; set; }
    }

    public class PluginRenderPayload
    {
        public string type { get; set; } = "render";
        public string style { get; set; } = "gray";
        public List<PluginRenderButton> buttons { get; set; } = new List<PluginRenderButton>();
        public List<PluginStatusItem> statusItems { get; set; } = new List<PluginStatusItem>();
    }

    internal sealed class RunningPluginProcess : IDisposable
    {
        public PluginFolderItem Folder { get; }
        public Process Process { get; }
        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        public object WriteLock { get; } = new object();

        public RunningPluginProcess(PluginFolderItem folder, Process process)
        {
            Folder = folder;
            Process = process;
        }

        public void WriteJsonLine(object payload)
        {
            lock (WriteLock)
            {
                try
                {
                    if (Process.HasExited) return;
                    string json = JsonSerializer.Serialize(payload);
                    Process.StandardInput.WriteLine(json);
                    Process.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PluginHost WriteJsonLine error ({Folder.FolderName}): {ex.Message}");
                }
            }
        }

        public void StopAndDispose()
        {
            try
            {
                Cts.Cancel();
                WriteJsonLine(new { event_name = "overlay_closed", @event = "overlay_closed" });

                lock (WriteLock)
                {
                    try { Process.StandardInput.Close(); } catch { }
                }

                if (!Process.HasExited && !Process.WaitForExit(140))
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PluginHost Stop error ({Folder.FolderName}): {ex.Message}");
            }
            finally
            {
                try { Process.Dispose(); } catch { }
                try { Cts.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            StopAndDispose();
        }
    }

    /// <summary>
    /// Manages live Python plugin child processes strictly while FuncOverlay is open.
    /// When the overlay closes, all plugin processes are terminated so zero plugins run in the background.
    /// </summary>
    public class PluginHostService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly PluginIntentRouter _intentRouter;
        private readonly ConcurrentDictionary<string, RunningPluginProcess> _running =
            new ConcurrentDictionary<string, RunningPluginProcess>(StringComparer.OrdinalIgnoreCase);

        public event Action<string, PluginRenderPayload>? PluginRenderReceived;

        public PluginHostService(Dispatcher dispatcher, PluginIntentRouter intentRouter)
        {
            _dispatcher = dispatcher;
            _intentRouter = intentRouter;
        }

        public void StartAllForOpenOverlay(IEnumerable<PluginFolderItem> folders)
        {
            foreach (var folder in folders)
            {
                if (!folder.Metadata.enabled) continue;

                string entryPoint = folder.Metadata.entryPoint ?? string.Empty;
                bool isPython = string.Equals(folder.Metadata.language, "python", StringComparison.OrdinalIgnoreCase) ||
                                entryPoint.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
                if (!isPython) continue;

                string scriptPath = Path.Combine(folder.FolderPath, entryPoint);
                if (!File.Exists(scriptPath)) continue;

                if (_running.ContainsKey(folder.Metadata.id)) continue;

                StartPythonPlugin(folder, scriptPath);
            }
        }

        private void StartPythonPlugin(PluginFolderItem folder, string scriptPath)
        {
            Process? proc = TrySpawnPython(scriptPath, folder.FolderPath);
            if (proc == null) return;

            var runner = new RunningPluginProcess(folder, proc);
            if (!_running.TryAdd(folder.Metadata.id, runner))
            {
                runner.StopAndDispose();
                return;
            }

            // Start asynchronous stdout JSON line reader
            _ = Task.Run(() => ReadStdoutLoopAsync(runner), runner.Cts.Token);

            // Send initial open handshake with current plugin settings
            runner.WriteJsonLine(new
            {
                @event = "overlay_opened",
                pluginId = folder.Metadata.id,
                folder = folder.FolderName,
                settings = PluginManager.GetMergedSettings(folder.Metadata)
            });
        }

        private static Process? TrySpawnPython(string scriptPath, string workingDir)
        {
            (string exe, string args)[] candidates =
            {
                ("python", $"-u \"{scriptPath}\""),
                ("py", $"-3 -u \"{scriptPath}\"")
            };

            foreach (var (exe, args) in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = args,
                        WorkingDirectory = workingDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                    psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                    Process? p = Process.Start(psi);
                    if (p != null)
                    {
                        return p;
                    }
                }
                catch
                {
                    // Try next candidate
                }
            }

            return null;
        }

        private async Task ReadStdoutLoopAsync(RunningPluginProcess runner)
        {
            try
            {
                while (!runner.Cts.IsCancellationRequested && !runner.Process.HasExited)
                {
                    string? line = await runner.Process.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    HandlePluginLine(runner.Folder, line);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PluginHost stdout loop ended ({runner.Folder.FolderName}): {ex.Message}");
            }
        }

        private void HandlePluginLine(PluginFolderItem caller, string line)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;

                if (!root.TryGetProperty("type", out JsonElement typeEl) || typeEl.ValueKind != JsonValueKind.String)
                {
                    return;
                }

                string msgType = typeEl.GetString() ?? string.Empty;
                if (string.Equals(msgType, "render", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = JsonSerializer.Deserialize<PluginRenderPayload>(line, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (payload != null)
                    {
                        _dispatcher.InvokeAsync(() => PluginRenderReceived?.Invoke(caller.Metadata.id, payload));
                    }
                }
                else if (string.Equals(msgType, "intent", StringComparison.OrdinalIgnoreCase))
                {
                    string intentName = root.TryGetProperty("intent", out JsonElement intentEl) && intentEl.ValueKind == JsonValueKind.String
                        ? intentEl.GetString() ?? string.Empty
                        : string.Empty;

                    JsonElement? dataClone = null;
                    if (root.TryGetProperty("data", out JsonElement dataEl))
                    {
                        dataClone = dataEl.Clone();
                    }

                    _dispatcher.InvokeAsync(() => _intentRouter.Dispatch(intentName, dataClone, caller));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Invalid JSON from plugin {caller.FolderName}: {ex.Message}");
            }
        }

        public bool SendAction(string pluginId, string actionId)
        {
            if (_running.TryGetValue(pluginId, out RunningPluginProcess? runner))
            {
                runner.WriteJsonLine(new
                {
                    @event = "ui_action",
                    action = actionId
                });
                return true;
            }
            return false;
        }

        public void NotifySettingsUpdated(PluginFolderItem folder)
        {
            if (_running.TryGetValue(folder.Metadata.id, out RunningPluginProcess? runner))
            {
                runner.WriteJsonLine(new
                {
                    @event = "settings_updated",
                    settings = PluginManager.GetMergedSettings(folder.Metadata)
                });
            }
        }

        public void StopAllOnOverlayClosed()
        {
            var keys = new List<string>(_running.Keys);
            foreach (string key in keys)
            {
                if (_running.TryRemove(key, out RunningPluginProcess? runner))
                {
                    runner.StopAndDispose();
                }
            }
        }

        public void Dispose()
        {
            StopAllOnOverlayClosed();
        }
    }
}
