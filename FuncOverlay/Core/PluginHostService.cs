using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public bool WriteJsonLine(object payload)
        {
            lock (WriteLock)
            {
                try
                {
                    if (Process.HasExited) return false;
                    string json = JsonSerializer.Serialize(payload);
                    Process.StandardInput.WriteLine(json);
                    Process.StandardInput.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PluginHost WriteJsonLine error ({Folder.FolderName}): {ex.Message}");
                    return false;
                }
            }
        }

        public void StopAndDispose()
        {
            try
            {
                Cts.Cancel();
                WriteJsonLine(new { @event = "overlay_closed" });

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

        private string? _cachedPythonExe;
        private string? _cachedPythonPrefixArgs;
        private bool _reportedMissingPython = false;

        public event Action<string, PluginRenderPayload>? PluginRenderReceived;
        public event Action<string>? PluginHostNotice;

        public string LastStartupIssue { get; private set; } = string.Empty;

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

                if (_running.TryGetValue(folder.Metadata.id, out RunningPluginProcess? existing))
                {
                    if (!existing.Process.HasExited) continue;
                    if (_running.TryRemove(folder.Metadata.id, out RunningPluginProcess? dead))
                    {
                        dead.StopAndDispose();
                    }
                }

                StartPythonPlugin(folder, scriptPath);
            }
        }

        private void StartPythonPlugin(PluginFolderItem folder, string scriptPath)
        {
            Process? proc = TrySpawnPython(scriptPath, folder.FolderPath);
            if (proc == null)
            {
                LastStartupIssue = "Python 3 not found on Windows. Install Python 3 from python.org to run Python plugins.";
                LogHostDiagnostic($"[{folder.FolderName}] {LastStartupIssue}");
                if (!_reportedMissingPython)
                {
                    _reportedMissingPython = true;
                    _dispatcher.InvokeAsync(() => PluginHostNotice?.Invoke(LastStartupIssue));
                }
                return;
            }

            LastStartupIssue = string.Empty;
            var runner = new RunningPluginProcess(folder, proc);
            if (!_running.TryAdd(folder.Metadata.id, runner))
            {
                runner.StopAndDispose();
                return;
            }

            // Start asynchronous stdout & stderr readers
            _ = Task.Run(() => ReadStdoutLoopAsync(runner), runner.Cts.Token);
            _ = Task.Run(() => ReadStderrLoopAsync(runner), runner.Cts.Token);

            // Send initial open handshake with current plugin settings
            runner.WriteJsonLine(new
            {
                @event = "overlay_opened",
                pluginId = folder.Metadata.id,
                folder = folder.FolderName,
                settings = PluginManager.GetMergedSettings(folder.Metadata)
            });
        }

        private Process? TrySpawnPython(string scriptPath, string workingDir)
        {
            if (string.IsNullOrEmpty(_cachedPythonExe))
            {
                ResolveWorkingPython(out _cachedPythonExe, out _cachedPythonPrefixArgs);
            }

            if (!string.IsNullOrEmpty(_cachedPythonExe))
            {
                Process? p = LaunchPythonWithExecutable(_cachedPythonExe, _cachedPythonPrefixArgs ?? string.Empty, scriptPath, workingDir);
                if (p != null) return p;

                // Cached executable failed (e.g., removed), clear cache and re-probe once
                _cachedPythonExe = null;
                _cachedPythonPrefixArgs = null;
                ResolveWorkingPython(out _cachedPythonExe, out _cachedPythonPrefixArgs);
                if (!string.IsNullOrEmpty(_cachedPythonExe))
                {
                    return LaunchPythonWithExecutable(_cachedPythonExe, _cachedPythonPrefixArgs ?? string.Empty, scriptPath, workingDir);
                }
            }

            return null;
        }

        private static Process? LaunchPythonWithExecutable(string exe, string prefixArgs, string scriptPath, string workingDir)
        {
            try
            {
                string fullArgs = string.IsNullOrEmpty(prefixArgs)
                    ? $"-u \"{scriptPath}\""
                    : $"{prefixArgs} -u \"{scriptPath}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = fullArgs,
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

                return Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogHostDiagnostic($"LaunchPythonWithExecutable failed ({exe}): {ex.Message}");
                return null;
            }
        }

        private static void ResolveWorkingPython(out string? resolvedExe, out string? resolvedPrefixArgs)
        {
            resolvedExe = null;
            resolvedPrefixArgs = null;

            var candidates = new List<(string exe, string prefix)>();

            // 1. Windows Python Launcher (py.exe) - ignores Store stub and picks real Python 3
            string windowsPy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe");
            if (File.Exists(windowsPy))
            {
                candidates.Add((windowsPy, "-3"));
            }

            // 2. Scan PATH entries while explicitly skipping the Microsoft Store WindowsApps stub
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string rawDir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string dir = rawDir.Trim().Trim('"');
                if (string.IsNullOrEmpty(dir)) continue;
                if (dir.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                foreach (string name in new[] { "python.exe", "python3.exe", "py.exe" })
                {
                    string candidatePath = Path.Combine(dir, name);
                    if (File.Exists(candidatePath))
                    {
                        candidates.Add((candidatePath, name.Equals("py.exe", StringComparison.OrdinalIgnoreCase) ? "-3" : ""));
                    }
                }
            }

            // 3. Common Windows Python installation directories (even when not added to PATH)
            var searchRoots = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "C:\\"
            };

            foreach (string root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    var pythonDirs = Directory.GetDirectories(root, "Python3*")
                        .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);
                    foreach (string pyDir in pythonDirs)
                    {
                        string exe = Path.Combine(pyDir, "python.exe");
                        if (File.Exists(exe))
                        {
                            candidates.Add((exe, ""));
                        }
                    }
                }
                catch { }
            }

            // 4. Generic fallback names
            candidates.Add(("py", "-3"));
            candidates.Add(("python3", ""));
            candidates.Add(("python", ""));

            // Verify first working Python 3 interpreter cleanly (rejects Store alias / broken shims)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (exe, prefix) in candidates)
            {
                string key = $"{exe}|{prefix}";
                if (!seen.Add(key)) continue;

                if (ProbePythonWorks(exe, prefix))
                {
                    resolvedExe = exe;
                    resolvedPrefixArgs = prefix;
                    LogHostDiagnostic($"Resolved Python interpreter: {exe} {prefix}");
                    return;
                }
            }
        }

        private static bool ProbePythonWorks(string exe, string prefix)
        {
            try
            {
                string args = string.IsNullOrEmpty(prefix)
                    ? "-c \"import sys; print(sys.version_info[0])\""
                    : $"{prefix} -c \"import sys; print(sys.version_info[0])\"";

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using Process? p = Process.Start(psi);
                if (p == null) return false;

                string output = p.StandardOutput.ReadToEnd().Trim();
                if (!p.WaitForExit(1500))
                {
                    try { p.Kill(true); } catch { }
                    return false;
                }

                return p.ExitCode == 0 && output.StartsWith("3");
            }
            catch
            {
                return false;
            }
        }

        private async Task ReadStdoutLoopAsync(RunningPluginProcess runner)
        {
            try
            {
                while (!runner.Cts.IsCancellationRequested)
                {
                    string? line = await runner.Process.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    HandlePluginLine(runner.Folder, line);
                }
            }
            catch (Exception ex)
            {
                LogHostDiagnostic($"[{runner.Folder.FolderName}] stdout loop ended: {ex.Message}");
            }
        }

        private async Task ReadStderrLoopAsync(RunningPluginProcess runner)
        {
            try
            {
                var errLines = new List<string>();
                while (!runner.Cts.IsCancellationRequested)
                {
                    string? line = await runner.Process.StandardError.ReadLineAsync();
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    LogHostDiagnostic($"[{runner.Folder.FolderName} STDERR] {line}");
                    if (errLines.Count < 4) errLines.Add(line);
                }

                if (!runner.Cts.IsCancellationRequested && errLines.Count > 0)
                {
                    LastStartupIssue = errLines[errLines.Count - 1];
                    _dispatcher.InvokeAsync(() => PluginHostNotice?.Invoke($"{runner.Folder.Metadata.name}: {LastStartupIssue}"));
                }
            }
            catch { }
        }

        private static void LogHostDiagnostic(string message)
        {
            try
            {
                Debug.WriteLine(message);
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuncOverlay");
                Directory.CreateDirectory(dir);
                string logPath = Path.Combine(dir, "plugin_host.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
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
                LogHostDiagnostic($"Invalid JSON from plugin {caller.FolderName}: {ex.Message}");
            }
        }

        public bool SendAction(string pluginId, string actionId)
        {
            if (_running.TryGetValue(pluginId, out RunningPluginProcess? runner))
            {
                if (!runner.Process.HasExited)
                {
                    return runner.WriteJsonLine(new
                    {
                        @event = "ui_action",
                        action = actionId
                    });
                }
            }
            return false;
        }

        public void NotifySettingsUpdated(PluginFolderItem folder)
        {
            if (_running.TryGetValue(folder.Metadata.id, out RunningPluginProcess? runner) && !runner.Process.HasExited)
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
