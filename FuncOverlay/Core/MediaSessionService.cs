using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace FuncOverlay.Core
{
    public class MediaSourceInfo
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public bool isPlaying { get; set; }
        public bool isSystemCurrent { get; set; }
        public string trackSummary { get; set; } = string.Empty;
    }

    public class MediaSnapshot
    {
        public bool hasMedia { get; set; }
        public string selectedSourceId { get; set; } = string.Empty;
        public string selectedSourceName { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public string artist { get; set; } = string.Empty;
        public string? albumArtBase64 { get; set; }
        public string albumArtKey { get; set; } = string.Empty;
        public bool isPlaying { get; set; }
        public double positionSec { get; set; }
        public double durationSec { get; set; }
        public bool canSeek { get; set; }
        public bool canSkipNext { get; set; }
        public bool canSkipPrevious { get; set; }
        public bool canPlayPause { get; set; }
        public List<MediaSourceInfo> sources { get; set; } = new List<MediaSourceInfo>();
    }

    /// <summary>
    /// Reads and controls Windows Global System Media Transport Controls (SMTC) sessions
    /// strictly while subscribed during an open overlay session.
    /// </summary>
    public sealed class MediaSessionService : IDisposable
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0001;
        private const uint SWP_NOSIZE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly Dictionary<string, string> KnownAppNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["spotify"] = "Spotify",
                ["spotifyab.spotifymusic"] = "Spotify",
                ["firefox"] = "Firefox",
                ["308046b0af4a39cb"] = "Firefox",
                ["chrome"] = "Google Chrome",
                ["msedge"] = "Microsoft Edge",
                ["brave"] = "Brave",
                ["opera"] = "Opera",
                ["vivaldi"] = "Vivaldi",
                ["vlc"] = "VLC",
                ["aimp"] = "AIMP",
                ["foobar2000"] = "foobar2000",
                ["telegram"] = "Telegram",
                ["discord"] = "Discord",
                ["microsoft.zunemusic"] = "Media Player",
                ["applemusic"] = "Apple Music",
                ["itunes"] = "iTunes",
                ["yandexmusic"] = "Yandex Music",
                ["yandex.music"] = "Yandex Music",
                ["musicbee"] = "MusicBee",
                ["mpc-hc64"] = "MPC-HC",
                ["mpv"] = "mpv",
                ["potplayermini64"] = "PotPlayer"
            };

        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _pollTimer;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, (string CacheKey, string Base64)> _artCache =
            new Dictionary<string, (string CacheKey, string Base64)>(StringComparer.OrdinalIgnoreCase);

        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private string? _selectedSourceId;
        private bool _isActive;
        private int _refreshInFlight;

        public event Action<MediaSnapshot>? SnapshotUpdated;

        public MediaSnapshot LatestSnapshot { get; private set; } = new MediaSnapshot();

        public MediaSessionService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _pollTimer.Tick += async (s, e) => await RefreshAsync();
        }

        public void Start()
        {
            _isActive = true;
            if (!_pollTimer.IsEnabled)
            {
                _pollTimer.Start();
            }
            _ = RefreshAsync();
        }

        public void Stop()
        {
            _isActive = false;
            _pollTimer.Stop();
        }

        public async Task RefreshAsync()
        {
            if (!_isActive) return;
            if (Interlocked.Exchange(ref _refreshInFlight, 1) == 1) return;

            try
            {
                var mgr = await EnsureManagerAsync().ConfigureAwait(false);
                if (mgr == null || !_isActive) return;

                MediaSnapshot snap = await BuildSnapshotAsync(mgr).ConfigureAwait(false);
                LatestSnapshot = snap;

                if (_isActive)
                {
                    await _dispatcher.InvokeAsync(() => SnapshotUpdated?.Invoke(snap));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaSessionService RefreshAsync error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInFlight, 0);
            }
        }

        public async Task<bool> ControlAsync(string command, string? sourceId = null, double? positionSec = null)
        {
            var mgr = await EnsureManagerAsync().ConfigureAwait(false);
            if (mgr == null) return false;

            var sessions = mgr.GetSessions();
            if (sessions == null || sessions.Count == 0) return false;

            string? targetId = !string.IsNullOrWhiteSpace(sourceId) ? sourceId : _selectedSourceId;
            var target = ResolveTargetSession(mgr, sessions, targetId);
            if (target == null) return false;

            bool ok = false;
            try
            {
                switch ((command ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "play_pause":
                    case "toggle":
                        var pb = target.GetPlaybackInfo();
                        if (pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            ok = await target.TryPauseAsync();
                            if (!ok) ok = await target.TryTogglePlayPauseAsync();
                        }
                        else
                        {
                            ok = await target.TryPlayAsync();
                            if (!ok) ok = await target.TryTogglePlayPauseAsync();
                        }
                        break;

                    case "play":
                        ok = await target.TryPlayAsync();
                        break;

                    case "pause":
                        ok = await target.TryPauseAsync();
                        break;

                    case "next":
                        ok = await target.TrySkipNextAsync();
                        break;

                    case "previous":
                    case "prev":
                        ok = await target.TrySkipPreviousAsync();
                        break;

                    case "seek":
                        if (positionSec.HasValue)
                        {
                            var tl = target.GetTimelineProperties();
                            long targetTicks = tl.StartTime.Ticks + TimeSpan.FromSeconds(Math.Max(0, positionSec.Value)).Ticks;
                            if (tl.EndTime > tl.StartTime)
                            {
                                targetTicks = Math.Clamp(targetTicks, tl.StartTime.Ticks, tl.EndTime.Ticks);
                            }
                            ok = await target.TryChangePlaybackPositionAsync(targetTicks);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaSessionService ControlAsync({command}) error: {ex.Message}");
            }

            if (_isActive)
            {
                await Task.Delay(70).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
            return ok;
        }

        public async Task SelectSourceAsync(string sourceId, bool focusWindow, IntPtr overlayHwnd)
        {
            _selectedSourceId = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim();

            if (focusWindow && !string.IsNullOrEmpty(_selectedSourceId))
            {
                TryFocusSourceAppWindow(_selectedSourceId, overlayHwnd);
            }

            if (_isActive)
            {
                await RefreshAsync().ConfigureAwait(false);
            }
        }

        private async Task<GlobalSystemMediaTransportControlsSessionManager?> EnsureManagerAsync()
        {
            if (_manager != null) return _manager;

            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_manager == null)
                {
                    _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                }
                return _manager;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaSessionService RequestAsync failed: {ex.Message}");
                return null;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task<MediaSnapshot> BuildSnapshotAsync(GlobalSystemMediaTransportControlsSessionManager mgr)
        {
            var snap = new MediaSnapshot();
            var sessions = mgr.GetSessions();
            if (sessions == null || sessions.Count == 0)
            {
                _selectedSourceId = null;
                return snap;
            }

            var current = mgr.GetCurrentSession();
            string currentId = current?.SourceAppUserModelId ?? string.Empty;

            var sessionList = new List<GlobalSystemMediaTransportControlsSession>(sessions.Count);
            foreach (var s in sessions)
            {
                if (s == null) continue;
                sessionList.Add(s);

                string sid = s.SourceAppUserModelId ?? string.Empty;
                var pb = s.GetPlaybackInfo();
                bool playing = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                string trackSummary = string.Empty;
                try
                {
                    var props = await s.TryGetMediaPropertiesAsync();
                    if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                    {
                        trackSummary = props.Title.Trim();
                    }
                }
                catch { }

                snap.sources.Add(new MediaSourceInfo
                {
                    id = sid,
                    name = FormatFriendlyAppName(sid),
                    isPlaying = playing,
                    isSystemCurrent = string.Equals(sid, currentId, StringComparison.OrdinalIgnoreCase),
                    trackSummary = trackSummary
                });
            }

            var selected = ResolveTargetSession(mgr, sessionList, _selectedSourceId);
            if (selected == null)
            {
                return snap;
            }

            _selectedSourceId = selected.SourceAppUserModelId;
            snap.selectedSourceId = selected.SourceAppUserModelId ?? string.Empty;
            snap.selectedSourceName = FormatFriendlyAppName(snap.selectedSourceId);

            GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProps = null;
            try
            {
                mediaProps = await selected.TryGetMediaPropertiesAsync();
            }
            catch { }

            string title = mediaProps?.Title?.Trim() ?? string.Empty;
            string artist = mediaProps?.Artist?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(artist))
            {
                artist = mediaProps?.AlbumArtist?.Trim() ?? string.Empty;
            }

            var playback = selected.GetPlaybackInfo();
            bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var controls = playback?.Controls;

            var timeline = selected.GetTimelineProperties();
            double durationSec = timeline.EndTime > timeline.StartTime
                ? (timeline.EndTime - timeline.StartTime).TotalSeconds
                : 0;
            double posSec = Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);
            if (isPlaying && timeline.LastUpdatedTime != DateTimeOffset.MinValue)
            {
                double elapsed = (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds;
                if (elapsed > 0 && elapsed < 3600)
                {
                    posSec += elapsed;
                }
            }
            if (durationSec > 0)
            {
                posSec = Math.Clamp(posSec, 0, durationSec);
            }

            string artCacheKey = $"{snap.selectedSourceId}|{title}|{artist}|{mediaProps?.AlbumTitle ?? string.Empty}";
            string? artBase64 = null;
            if (_artCache.TryGetValue(snap.selectedSourceId, out var cached) &&
                string.Equals(cached.CacheKey, artCacheKey, StringComparison.Ordinal))
            {
                artBase64 = cached.Base64;
            }
            else if (mediaProps?.Thumbnail != null)
            {
                artBase64 = await ReadThumbnailBase64Async(mediaProps.Thumbnail).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(artBase64))
                {
                    _artCache[snap.selectedSourceId] = (artCacheKey, artBase64);
                }
                else
                {
                    _artCache.Remove(snap.selectedSourceId);
                }
            }
            else
            {
                _artCache.Remove(snap.selectedSourceId);
            }

            snap.hasMedia = !string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(artist) || isPlaying || durationSec > 0;
            snap.title = string.IsNullOrEmpty(title) ? snap.selectedSourceName : title;
            snap.artist = artist;
            snap.albumArtBase64 = artBase64;
            snap.albumArtKey = artCacheKey;
            snap.isPlaying = isPlaying;
            snap.positionSec = posSec;
            snap.durationSec = durationSec;
            snap.canSeek = (controls?.IsPlaybackPositionEnabled ?? false) && durationSec > 0;
            snap.canSkipNext = controls?.IsNextEnabled ?? true;
            snap.canSkipPrevious = controls?.IsPreviousEnabled ?? true;
            snap.canPlayPause = (controls?.IsPlayEnabled ?? false) || (controls?.IsPauseEnabled ?? false) || (controls?.IsPlayPauseToggleEnabled ?? true);

            return snap;
        }

        private static GlobalSystemMediaTransportControlsSession? ResolveTargetSession(
            GlobalSystemMediaTransportControlsSessionManager mgr,
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions,
            string? preferredSourceId)
        {
            if (sessions.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(preferredSourceId))
            {
                foreach (var s in sessions)
                {
                    if (string.Equals(s.SourceAppUserModelId, preferredSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return s;
                    }
                }
            }

            var current = mgr.GetCurrentSession();
            if (current != null) return current;

            foreach (var s in sessions)
            {
                var pb = s.GetPlaybackInfo();
                if (pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return s;
                }
            }

            return sessions[0];
        }

        private static async Task<string?> ReadThumbnailBase64Async(IRandomAccessStreamReference thumbRef)
        {
            try
            {
                using IRandomAccessStreamWithContentType stream = await thumbRef.OpenReadAsync();
                ulong size = stream.Size;
                if (size == 0 || size > 8 * 1024 * 1024) return null;

                using DataReader reader = new DataReader(stream.GetInputStreamAt(0));
                uint loaded = await reader.LoadAsync((uint)size);
                if (loaded == 0) return null;

                byte[] bytes = new byte[loaded];
                reader.ReadBytes(bytes);
                return Convert.ToBase64String(bytes);
            }
            catch
            {
                return null;
            }
        }

        public static string FormatFriendlyAppName(string? rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return "Media";

            string cleaned = rawId.Trim();
            int bangIdx = cleaned.IndexOf('!');
            if (bangIdx > 0)
            {
                string afterBang = cleaned.Substring(bangIdx + 1).Trim();
                string beforeBang = cleaned.Substring(0, bangIdx).Trim();
                int underIdx = beforeBang.IndexOf('_');
                string pkg = underIdx > 0 ? beforeBang.Substring(0, underIdx) : beforeBang;

                if (KnownAppNames.TryGetValue(pkg, out string? knownPkg)) return knownPkg;
                if (KnownAppNames.TryGetValue(afterBang, out string? knownAfter)) return knownAfter;

                if (!string.Equals(afterBang, "App", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(afterBang, "Application", StringComparison.OrdinalIgnoreCase))
                {
                    return afterBang;
                }
                cleaned = pkg;
            }

            if (cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 4);
            }

            int lastDot = cleaned.LastIndexOf('.');
            string leaf = lastDot >= 0 && lastDot < cleaned.Length - 1 ? cleaned.Substring(lastDot + 1) : cleaned;

            if (KnownAppNames.TryGetValue(cleaned, out string? knownFull)) return knownFull;
            if (KnownAppNames.TryGetValue(leaf, out string? knownLeaf)) return knownLeaf;

            if (leaf.Length <= 1) return leaf.ToUpperInvariant();
            return char.ToUpperInvariant(leaf[0]) + leaf.Substring(1);
        }

        private static void TryFocusSourceAppWindow(string sourceId, IntPtr overlayHwnd)
        {
            try
            {
                string friendly = FormatFriendlyAppName(sourceId);
                string token = sourceId;
                int bangIdx = token.IndexOf('!');
                if (bangIdx > 0) token = token.Substring(0, bangIdx);
                if (token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring(0, token.Length - 4);

                IntPtr targetHwnd = IntPtr.Zero;
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        try
                        {
                            if (p.MainWindowHandle == IntPtr.Zero) continue;
                            string pname = p.ProcessName;
                            string title = p.MainWindowTitle ?? string.Empty;
                            bool match = string.Equals(pname, token, StringComparison.OrdinalIgnoreCase) ||
                                         pname.Contains(friendly, StringComparison.OrdinalIgnoreCase) ||
                                         friendly.Contains(pname, StringComparison.OrdinalIgnoreCase) ||
                                         title.Contains(friendly, StringComparison.OrdinalIgnoreCase);
                            if (match)
                            {
                                targetHwnd = p.MainWindowHandle;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (targetHwnd != IntPtr.Zero)
                {
                    if (IsIconic(targetHwnd))
                    {
                        ShowWindow(targetHwnd, SW_RESTORE);
                    }
                    SetForegroundWindow(targetHwnd);
                    if (overlayHwnd != IntPtr.Zero)
                    {
                        SetWindowPos(overlayHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | SWP_NOACTIVATE);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryFocusSourceAppWindow error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _artCache.Clear();
            _initLock.Dispose();
        }
    }
}
