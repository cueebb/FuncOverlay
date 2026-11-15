#!/usr/bin/env python3
"""
obs shortcuts - FuncOverlay Python Plugin
- Runs ONLY while FuncOverlay overlay is open (zero background execution when closed).
- Connects to OBS Studio WebSocket v5 (pure Python stdlib RFC 6455 client).
- Reads replay buffer duration directly from OBS Studio profile Basic.ini settings.
- Emits FuncOverlay Intents (e.g., overlay.close + system.launch_app when restoring OBS).
"""

import base64
import configparser
import ctypes
import hashlib
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import threading
import time
from pathlib import Path

# Colors specified for the unified gray plugin theme & OBS dots
DOT_RECORD = "#6a010e"
DOT_REPLAY = "#015637"
DOT_REPLAY_SAVING = "#111b4f"
DOT_STREAM = "#340f57"

STATUS_DOT_RECORD = "#6a010e"
STATUS_DOT_REPLAY = "#015637"
STATUS_DOT_REPLAY_SAVING = "#111b4f"
STATUS_DOT_STREAM = "#340f57"


def _configure_stdio() -> None:
    for stream in (sys.stdin, sys.stdout):
        try:
            if hasattr(stream, "reconfigure"):
                stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


_stdout_lock = threading.Lock()


def send_host(payload: dict) -> None:
    line = json.dumps(payload, ensure_ascii=False) + "\n"
    with _stdout_lock:
        try:
            sys.stdout.write(line)
            sys.stdout.flush()
        except Exception:
            pass


def send_intent(intent: str, data: dict | None = None) -> None:
    send_host({
        "type": "intent",
        "intent": intent,
        "data": data or {},
    })


def load_local_metadata_settings() -> dict:
    """Load initial settings directly from local metadata.json next to main.py."""
    merged: dict = {}
    try:
        meta_path = Path(__file__).with_name("metadata.json")
        if not meta_path.exists():
            return merged
        meta = json.loads(meta_path.read_text(encoding="utf-8-sig"))
        if not isinstance(meta, dict):
            return merged

        schema = meta.get("settingsSchema") or []
        raw_settings = meta.get("settings") or {}

        for item in schema:
            if not isinstance(item, dict):
                continue
            key = item.get("key")
            if not key:
                continue
            default_val = item.get("defaultValue")
            schema_val = item.get("value", default_val)
            dict_val = raw_settings.get(key) if isinstance(raw_settings, dict) else None

            if dict_val is not None and dict_val != default_val:
                merged[key] = dict_val
            elif schema_val is not None and schema_val != default_val:
                merged[key] = schema_val
            elif dict_val is not None:
                merged[key] = dict_val
            else:
                merged[key] = schema_val

        if isinstance(raw_settings, dict):
            for k, v in raw_settings.items():
                if k not in merged:
                    merged[k] = v
    except Exception:
        pass
    return merged


def normalize_ws_endpoint(raw_host: str, raw_port) -> tuple[str, int]:
    host = str(raw_host or "127.0.0.1").strip()
    for prefix in ("ws://", "wss://", "http://", "https://"):
        if host.lower().startswith(prefix):
            host = host[len(prefix):]
    host = host.split("/", 1)[0].strip()

    try:
        port = int(str(raw_port or 4455).strip())
    except Exception:
        port = 4455

    if ":" in host and not host.startswith("["):
        parts = host.rsplit(":", 1)
        if parts[1].isdigit():
            host = parts[0].strip()
            port = int(parts[1])

    if not host:
        host = "127.0.0.1"
    return host, port


def format_duration(seconds: int) -> str:
    seconds = max(0, int(seconds))
    hrs, rem = divmod(seconds, 3600)
    mins, secs = divmod(rem, 60)
    if hrs > 0:
        return f"{hrs:02d}:{mins:02d}:{secs:02d}"
    return f"{mins:02d}:{secs:02d}"


def format_buffer_seconds(seconds: int) -> str:
    seconds = max(1, int(seconds))
    if seconds >= 60 and seconds % 60 == 0:
        return f"{seconds // 60}m"
    if seconds > 60:
        return f"{seconds // 60}m {seconds % 60}s"
    return f"{seconds}s"


def read_obs_replay_buffer_seconds(fallback_sec: int = 30) -> int:
    """Read Replay Buffer duration (RecRBTime) from OBS Studio's active profile config."""
    try:
        appdata = os.environ.get("APPDATA")
        if not appdata:
            return fallback_sec

        obs_dir = Path(appdata) / "obs-studio"
        global_ini = obs_dir / "global.ini"
        if not global_ini.exists():
            return fallback_sec

        global_cfg = configparser.ConfigParser(interpolation=None)
        global_cfg.read(global_ini, encoding="utf-8-sig")

        profile_name = None
        if global_cfg.has_section("BasicWindow") and global_cfg.has_option("BasicWindow", "Profile"):
            profile_name = global_cfg.get("BasicWindow", "Profile").strip()

        profiles_dir = obs_dir / "basic" / "profiles"
        if not profiles_dir.exists():
            return fallback_sec

        candidate_dirs = []
        if profile_name:
            candidate_dirs.append(profiles_dir / profile_name)
        for p in profiles_dir.iterdir():
            if p.is_dir() and p not in candidate_dirs:
                candidate_dirs.append(p)

        for prof_dir in candidate_dirs:
            basic_ini = prof_dir / "basic.ini"
            if not basic_ini.exists():
                continue
            prof_cfg = configparser.ConfigParser(interpolation=None)
            prof_cfg.read(basic_ini, encoding="utf-8-sig")

            mode = "Simple"
            if prof_cfg.has_option("Output", "Mode"):
                mode = prof_cfg.get("Output", "Mode").strip()

            section = "AdvOut" if mode.lower().startswith("adv") else "SimpleOutput"
            if prof_cfg.has_option(section, "RecRBTime"):
                val = int(float(prof_cfg.get(section, "RecRBTime").strip()))
                if val > 0:
                    return val

            for fallback_sec_name in ("SimpleOutput", "AdvOut"):
                if prof_cfg.has_option(fallback_sec_name, "RecRBTime"):
                    val = int(float(prof_cfg.get(fallback_sec_name, "RecRBTime").strip()))
                    if val > 0:
                        return val
    except Exception:
        pass
    return fallback_sec


def _obs_exe_priority(exe_name: str) -> int:
    """Return match priority (>0) for processes with 'obs' in their exe name, excluding helper subprocesses."""
    name = (exe_name or "").strip().lower()
    if not name.endswith(".exe"):
        return 0
    excluded_tokens = (
        "obs-browser",
        "obs-ffmpeg",
        "obs-nvenc",
        "obs-amf",
        "obs-qsv",
        "obs-vst",
        "obs-websocket",
        "obs-scripting",
        "crash",
        "updater",
        "funcoverlay",
    )
    if any(tok in name for tok in excluded_tokens):
        return 0
    if name in ("obs64.exe", "obs32.exe", "obs.exe"):
        return 3
    if name.startswith("obs"):
        return 2
    if "obs" in name:
        return 1
    return 0


def _find_obs_pids_by_exe_name() -> dict[int, int]:
    """Find running Windows PIDs whose executable name contains 'obs', mapped to priority."""
    if os.name != "nt":
        return {}

    from ctypes import wintypes

    TH32CS_SNAPPROCESS = 0x00000002
    INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ProcessID", wintypes.DWORD),
            ("th32DefaultHeapID", ctypes.c_size_t),  # ULONG_PTR
            ("th32ModuleID", wintypes.DWORD),
            ("cntThreads", wintypes.DWORD),
            ("th32ParentProcessID", wintypes.DWORD),
            ("pcPriClassBase", wintypes.LONG),
            ("dwFlags", wintypes.DWORD),
            ("szExeFile", wintypes.WCHAR * 260),
        ]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
    kernel32.Process32FirstW.restype = wintypes.BOOL
    kernel32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
    kernel32.Process32NextW.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL

    pids: dict[int, int] = {}
    self_pid = os.getpid()
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if not snap or snap == INVALID_HANDLE_VALUE:
        return pids

    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(PROCESSENTRY32W)
        ok = kernel32.Process32FirstW(snap, ctypes.byref(entry))
        while ok:
            pid = int(entry.th32ProcessID)
            if pid != 0 and pid != self_pid:
                prio = _obs_exe_priority(entry.szExeFile)
                if prio > 0:
                    pids[pid] = prio
            ok = kernel32.Process32NextW(snap, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snap)

    return pids


def _find_obs_hwnd(obs_pids: dict[int, int]) -> int:
    """Locate the main OBS top-level HWND (including minimized or hidden-in-tray windows)."""
    if os.name != "nt" or not obs_pids:
        return 0

    from ctypes import wintypes

    GW_OWNER = 4
    GWL_STYLE = -16
    GWL_EXSTYLE = -20
    WS_CAPTION = 0x00C00000
    WS_SYSMENU = 0x00080000
    WS_MINIMIZEBOX = 0x00020000
    WS_MAXIMIZEBOX = 0x00010000
    WS_EX_TRANSPARENT = 0x00000020
    WS_EX_TOOLWINDOW = 0x00000080
    WS_EX_LAYERED = 0x00080000
    WS_EX_NOACTIVATE = 0x08000000

    class WINDOWPLACEMENT(ctypes.Structure):
        _fields_ = [
            ("length", wintypes.UINT),
            ("flags", wintypes.UINT),
            ("showCmd", wintypes.UINT),
            ("ptMinPosition", wintypes.POINT),
            ("ptMaxPosition", wintypes.POINT),
            ("rcNormalPosition", wintypes.RECT),
        ]

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    user32.EnumWindows.argtypes = [WNDENUMPROC, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetWindow.restype = wintypes.HWND
    user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
    user32.GetWindowTextLengthW.restype = ctypes.c_int
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.restype = ctypes.c_int
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetClassNameW.restype = ctypes.c_int
    user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.GetWindowLongW.restype = wintypes.LONG
    user32.GetWindowPlacement.argtypes = [wintypes.HWND, ctypes.POINTER(WINDOWPLACEMENT)]
    user32.GetWindowPlacement.restype = wintypes.BOOL
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL

    ignored_classes = (
        "ime",
        "msctfime ui",
        "gdi+ window",
        "olemainthreadwndclass",
        "qtrayiconmessagewindow",
        "qeventdispatcherwin32",
    )

    best_hwnd = 0
    best_score = 0

    def _enum_cb(hwnd: int, _lparam: int) -> bool:
        nonlocal best_hwnd, best_score
        pid = wintypes.DWORD(0)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        prio = obs_pids.get(int(pid.value), 0)
        if prio <= 0:
            return True

        # Top-level unowned windows only
        if user32.GetWindow(hwnd, GW_OWNER):
            return True

        cls_buf = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, cls_buf, 256)
        cls_lower = cls_buf.value.strip().lower()
        if any(tok in cls_lower for tok in ignored_classes):
            return True

        style = int(user32.GetWindowLongW(hwnd, GWL_STYLE)) & 0xFFFFFFFF
        ex_style = int(user32.GetWindowLongW(hwnd, GWL_EXSTYLE)) & 0xFFFFFFFF
        if ex_style & (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE):
            return True

        tlen = user32.GetWindowTextLengthW(hwnd)
        title = ""
        if tlen > 0:
            tbuf = ctypes.create_unicode_buffer(min(tlen + 1, 512))
            user32.GetWindowTextW(hwnd, tbuf, len(tbuf))
            title = tbuf.value.strip()

        wp = WINDOWPLACEMENT()
        wp.length = ctypes.sizeof(WINDOWPLACEMENT)
        has_size = False
        if user32.GetWindowPlacement(hwnd, ctypes.byref(wp)):
            rc = wp.rcNormalPosition
            has_size = (rc.right - rc.left) >= 200 and (rc.bottom - rc.top) >= 150

        score = prio * 10
        title_lower = title.lower()
        if title_lower.startswith("obs"):
            score += 100
        elif "obs" in title_lower:
            score += 50
        if title:
            score += 25
        if (style & WS_CAPTION) == WS_CAPTION:
            score += 40
        if style & WS_SYSMENU:
            score += 20
        if style & (WS_MINIMIZEBOX | WS_MAXIMIZEBOX):
            score += 15
        if not (ex_style & WS_EX_TOOLWINDOW):
            score += 15
        if not (ex_style & WS_EX_LAYERED):
            score += 10
        if has_size:
            score += 20
        if user32.IsWindowVisible(hwnd):
            score += 10

        if score >= 80 and score > best_score:
            best_score = score
            best_hwnd = int(hwnd)
        return True

    cb = WNDENUMPROC(_enum_cb)
    user32.EnumWindows(cb, 0)
    return best_hwnd


def _find_obs_tray_hwnds(obs_pids: dict[int, int]) -> list[int]:
    """Locate Qt's hidden system tray message window(s) owned by running OBS PIDs."""
    if os.name != "nt" or not obs_pids:
        return []

    from ctypes import wintypes

    HWND_MESSAGE = ctypes.c_void_p(-3)
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    user32.EnumWindows.argtypes = [WNDENUMPROC, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    user32.FindWindowExW.argtypes = [wintypes.HWND, wintypes.HWND, wintypes.LPCWSTR, wintypes.LPCWSTR]
    user32.FindWindowExW.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetClassNameW.restype = ctypes.c_int
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.restype = ctypes.c_int

    matches: list[tuple[int, int]] = []
    seen: set[int] = set()

    def _inspect_hwnd(hwnd: int) -> None:
        if not hwnd or hwnd in seen:
            return
        pid = wintypes.DWORD(0)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if int(pid.value) not in obs_pids:
            return

        cls_buf = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, cls_buf, 256)
        cls_lower = cls_buf.value.strip().lower()

        title_buf = ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(hwnd, title_buf, 256)
        title_lower = title_buf.value.strip().lower()

        combined = f"{cls_lower} {title_lower}"
        if "trayicon" in combined:
            seen.add(hwnd)
            prio = 2 if "qtrayiconmessagewindow" in combined else 1
            matches.append((prio, int(hwnd)))

    def _enum_cb(hwnd: int, _lparam: int) -> bool:
        _inspect_hwnd(int(hwnd))
        return True

    cb = WNDENUMPROC(_enum_cb)
    user32.EnumWindows(cb, 0)

    # Also scan HWND_MESSAGE message-only windows in case Qt created it as message-only
    child = user32.FindWindowExW(HWND_MESSAGE, None, None, None)
    while child:
        _inspect_hwnd(int(child))
        child = user32.FindWindowExW(HWND_MESSAGE, child, None, None)

    matches.sort(key=lambda item: item[0], reverse=True)
    return [hwnd for _, hwnd in matches]


def _wake_obs_via_tray_icon(user32, obs_pids: dict[int, int], main_hwnd: int) -> bool:
    """
    Simulate clicking OBS's Qt system tray icon so OBSBasic::IconActivated / SetShowing(true)
    restores the window natively with intact Qt/D3D11 rendering.
    ONLY call when main_hwnd is currently hidden (!IsWindowVisible).
    """
    if user32.IsWindowVisible(main_hwnd):
        return False

    tray_hwnds = _find_obs_tray_hwnds(obs_pids)
    if not tray_hwnds:
        return False

    WM_USER = 0x0400
    WM_APP = 0x8000
    WM_LBUTTONUP = 0x0202
    WM_LBUTTONDBLCLK = 0x0203
    NIN_SELECT = 0x0400

    probe_msgs = (
        (WM_APP + 101, 0, WM_LBUTTONUP),      # Qt5/Qt6 MYWM_NOTIFYICON -> Trigger
        (WM_APP + 101, 0, NIN_SELECT),        # Qt6 NOTIFYICON_VERSION_4 select -> Trigger
        (WM_APP + 101, 0, WM_LBUTTONDBLCLK),  # DoubleClick fallback
        (WM_USER + 1, 0, WM_LBUTTONUP),       # Alt Qt offset
        (WM_USER + 101, 0, WM_LBUTTONUP),     # Alt tray offset
    )

    for msg, wp, lp in probe_msgs:
        for tray_hwnd in tray_hwnds:
            user32.PostMessageW(tray_hwnd, msg, wp, lp)
        for _ in range(10):
            time.sleep(0.01)
            if user32.IsWindowVisible(main_hwnd):
                # Allow OBSBasic::SetShowing(true) and initial Qt frame expose to finish
                time.sleep(0.02)
                return True

    return False


def _restore_and_focus_hwnd(hwnd: int, obs_pids: dict[int, int] | None = None) -> tuple[bool, bool]:
    """
    Restore an OBS HWND from taskbar-minimized or system-tray-hidden state and force a clean repaint.
    Returns (success, access_denied_uipi).
    """
    if os.name != "nt" or not hwnd:
        return False, False

    from ctypes import wintypes

    SW_HIDE = 0
    SW_SHOWNORMAL = 1
    SW_SHOWMAXIMIZED = 3
    SW_SHOW = 5
    SW_RESTORE = 9

    WPF_RESTORETOMAXIMIZED = 0x0002
    WM_SYSCOMMAND = 0x0112
    SC_MAXIMIZE = 0xF030
    SC_RESTORE = 0xF120

    HWND_TOPMOST = ctypes.c_void_p(-1)
    HWND_NOTOPMOST = ctypes.c_void_p(-2)
    SWP_NOSIZE = 0x0001
    SWP_NOMOVE = 0x0002
    SWP_NOZORDER = 0x0004
    SWP_NOACTIVATE = 0x0010
    SWP_FRAMECHANGED = 0x0020
    ERROR_ACCESS_DENIED = 5

    RDW_INVALIDATE = 0x0001
    RDW_ERASE = 0x0004
    RDW_ALLCHILDREN = 0x0080
    RDW_UPDATENOW = 0x0100
    RDW_FRAME = 0x0400

    class WINDOWPLACEMENT(ctypes.Structure):
        _fields_ = [
            ("length", wintypes.UINT),
            ("flags", wintypes.UINT),
            ("showCmd", wintypes.UINT),
            ("ptMinPosition", wintypes.POINT),
            ("ptMaxPosition", wintypes.POINT),
            ("rcNormalPosition", wintypes.RECT),
        ]

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    user32.IsWindow.argtypes = [wintypes.HWND]
    user32.IsWindow.restype = wintypes.BOOL
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.IsIconic.restype = wintypes.BOOL
    user32.GetWindowPlacement.argtypes = [wintypes.HWND, ctypes.POINTER(WINDOWPLACEMENT)]
    user32.GetWindowPlacement.restype = wintypes.BOOL
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL
    user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.ShowWindow.restype = wintypes.BOOL
    user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user32.PostMessageW.restype = wintypes.BOOL
    user32.SetWindowPos.argtypes = [
        wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT
    ]
    user32.SetWindowPos.restype = wintypes.BOOL
    user32.RedrawWindow.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT), wintypes.HANDLE, wintypes.UINT]
    user32.RedrawWindow.restype = wintypes.BOOL
    user32.SwitchToThisWindow.argtypes = [wintypes.HWND, wintypes.BOOL]
    user32.SwitchToThisWindow.restype = None
    user32.GetForegroundWindow.argtypes = []
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
    user32.AttachThreadInput.restype = wintypes.BOOL
    user32.BringWindowToTop.argtypes = [wintypes.HWND]
    user32.BringWindowToTop.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.SetActiveWindow.argtypes = [wintypes.HWND]
    user32.SetActiveWindow.restype = wintypes.HWND
    kernel32.GetCurrentThreadId.argtypes = []
    kernel32.GetCurrentThreadId.restype = wintypes.DWORD

    if not user32.IsWindow(hwnd):
        return False, False

    wp = WINDOWPLACEMENT()
    wp.length = ctypes.sizeof(WINDOWPLACEMENT)
    user32.GetWindowPlacement(hwnd, ctypes.byref(wp))

    was_visible = bool(user32.IsWindowVisible(hwnd))
    is_iconic = bool(user32.IsIconic(hwnd))
    was_hidden = (not was_visible) or (wp.showCmd == SW_HIDE)
    was_maximized = (wp.showCmd == SW_SHOWMAXIMIZED) or bool(wp.flags & WPF_RESTORETOMAXIMIZED)

    # Probe UIPI permissions WITHOUT touching WS_VISIBLE (never pass SWP_SHOWWINDOW here,
    # otherwise ShowWindow skips WM_SHOWWINDOW and Qt leaves a white/transparent window).
    ctypes.set_last_error(0)
    ok = user32.SetWindowPos(
        hwnd,
        None,
        0,
        0,
        0,
        0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE,
    )
    if not ok and ctypes.get_last_error() == ERROR_ACCESS_DENIED:
        return False, True

    # Perform clean hidden -> visible / minimized -> restored transitions
    woke_via_tray = False
    if was_hidden:
        if obs_pids:
            woke_via_tray = _wake_obs_via_tray_icon(user32, obs_pids, hwnd)

        if woke_via_tray:
            if user32.IsIconic(hwnd):
                user32.ShowWindow(hwnd, SW_SHOWMAXIMIZED if was_maximized else SW_RESTORE)
        else:
            user32.ShowWindow(hwnd, SW_SHOW)
            if is_iconic:
                user32.ShowWindow(hwnd, SW_RESTORE)
                user32.PostMessageW(hwnd, WM_SYSCOMMAND, SC_MAXIMIZE if was_maximized else SC_RESTORE, 0)
            if was_maximized:
                if not is_iconic:
                    user32.ShowWindow(hwnd, SW_RESTORE)
                time.sleep(0.025)
                user32.ShowWindow(hwnd, SW_SHOWMAXIMIZED)
            else:
                user32.ShowWindow(hwnd, SW_SHOWNORMAL)
                time.sleep(0.025)
                rect = wintypes.RECT()
                if user32.GetWindowRect(hwnd, ctypes.byref(rect)):
                    w = rect.right - rect.left
                    h = rect.bottom - rect.top
                    if w > 100 and h > 100:
                        user32.SetWindowPos(hwnd, None, 0, 0, w + 1, h, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE)
                        user32.SetWindowPos(hwnd, None, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE)
    else:
        if is_iconic:
            user32.ShowWindow(hwnd, SW_RESTORE)
            user32.PostMessageW(hwnd, WM_SYSCOMMAND, SC_MAXIMIZE if was_maximized else SC_RESTORE, 0)
            if was_maximized:
                time.sleep(0.02)
                user32.ShowWindow(hwnd, SW_SHOWMAXIMIZED)
        elif was_maximized:
            user32.ShowWindow(hwnd, SW_SHOWMAXIMIZED)
        else:
            user32.ShowWindow(hwnd, SW_SHOWNORMAL)

    # Lift briefly above overlay and refresh frame/DWM composition
    user32.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE)
    user32.SetWindowPos(
        hwnd,
        HWND_NOTOPMOST,
        0,
        0,
        0,
        0,
        SWP_NOMOVE | SWP_NOSIZE if woke_via_tray else (SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED),
    )

    # Bring to foreground
    fg_hwnd = user32.GetForegroundWindow()
    cur_tid = kernel32.GetCurrentThreadId()
    fg_tid = user32.GetWindowThreadProcessId(fg_hwnd, None) if fg_hwnd else 0
    tgt_tid = user32.GetWindowThreadProcessId(hwnd, None)

    attached_fg = False
    attached_tgt = False
    try:
        if fg_tid and fg_tid != cur_tid:
            attached_fg = bool(user32.AttachThreadInput(cur_tid, fg_tid, True))
        if tgt_tid and tgt_tid != cur_tid and tgt_tid != fg_tid:
            attached_tgt = bool(user32.AttachThreadInput(cur_tid, tgt_tid, True))

        user32.BringWindowToTop(hwnd)
        user32.SetForegroundWindow(hwnd)
        user32.SetActiveWindow(hwnd)
    finally:
        if attached_tgt:
            user32.AttachThreadInput(cur_tid, tgt_tid, False)
        if attached_fg:
            user32.AttachThreadInput(cur_tid, fg_tid, False)

    user32.SwitchToThisWindow(hwnd, True)

    if not woke_via_tray:
        # Force Qt + child docks + non-client frame to repaint cleanly on raw ShowWindow fallback
        user32.RedrawWindow(
            hwnd,
            None,
            None,
            RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_UPDATENOW | RDW_ALLCHILDREN,
        )
    return True, False


def _resolve_obs_exe_path(configured_path: str) -> str:
    """Find obs64.exe / obs32.exe / obs.exe via configured path, registry, or standard install folders."""
    raw = os.path.expandvars((configured_path or "").strip().strip('"'))
    if raw and os.path.isfile(raw):
        return raw

    if os.name == "nt":
        try:
            import winreg

            reg_locations = [
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\OBS Studio", ""),
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\OBS Studio", ""),
                (winreg.HKEY_CURRENT_USER, r"SOFTWARE\OBS Studio", ""),
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\obs64.exe", ""),
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\obs32.exe", ""),
            ]
            for root, subkey, val_name in reg_locations:
                try:
                    with winreg.OpenKey(root, subkey) as key:
                        val, _ = winreg.QueryValueEx(key, val_name)
                        val = str(val or "").strip().strip('"')
                        if not val:
                            continue
                        if os.path.isfile(val):
                            return val
                        for rel in (
                            os.path.join("bin", "64bit", "obs64.exe"),
                            os.path.join("bin", "32bit", "obs32.exe"),
                            "obs64.exe",
                        ):
                            cand = os.path.join(val, rel)
                            if os.path.isfile(cand):
                                return cand
                except OSError:
                    continue
        except ImportError:
            pass

        pf = os.environ.get("ProgramFiles", r"C:\Program Files")
        pfx86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
        local_app = os.environ.get("LOCALAPPDATA", "")
        candidates = [
            os.path.join(pf, "obs-studio", "bin", "64bit", "obs64.exe"),
            os.path.join(pfx86, "obs-studio", "bin", "64bit", "obs64.exe"),
            os.path.join(pfx86, "obs-studio", "bin", "32bit", "obs32.exe"),
            os.path.join(pfx86, "Steam", "steamapps", "common", "OBS Studio", "bin", "64bit", "obs64.exe"),
            os.path.join(pf, "Steam", "steamapps", "common", "OBS Studio", "bin", "64bit", "obs64.exe"),
        ]
        if local_app:
            candidates.append(os.path.join(local_app, "Programs", "obs-studio", "bin", "64bit", "obs64.exe"))
        for cand in candidates:
            if os.path.isfile(cand):
                return cand

    for exe_name in ("obs64.exe", "obs32.exe", "obs.exe", "obs"):
        found = shutil.which(exe_name)
        if found:
            return found

    return raw or r"C:\Program Files\obs-studio\bin\64bit\obs64.exe"


def focus_or_launch_obs(obs_exe_path: str) -> None:
    """Find running OBS process by .exe name to restore/focus from tray or taskbar, or launch if closed."""
    obs_pids = _find_obs_pids_by_exe_name()
    if obs_pids:
        hwnd = _find_obs_hwnd(obs_pids)
        if hwnd:
            restored, access_denied = _restore_and_focus_hwnd(hwnd, obs_pids)
            if access_denied:
                send_intent("app.toast", {
                    "message": "OBS is running as Administrator — Windows blocks restoring it unless FuncOverlay also runs as Administrator.",
                    "durationMs": 4500,
                })
                return
            if restored:
                send_intent("overlay.close")
                return

        # Fallback if a running OBS exe exists but had no restorable HWND match yet
        send_intent("system.focus_or_launch_app", {
            "path": _resolve_obs_exe_path(obs_exe_path),
            "windowTitleHints": ["OBS ", "OBS Studio"],
            "processNames": ["obs64", "obs32", "obs"],
            "closeOverlay": True,
        })
        return

    # OBS is not running — launch via host intent (outside plugin process tree) and close overlay
    resolved_exe = _resolve_obs_exe_path(obs_exe_path)
    send_intent("system.launch_app", {"path": resolved_exe})
    send_intent("overlay.close")


class ObsWebSocketV5Client:
    """Minimal pure-stdlib RFC 6455 WebSocket + OBS WebSocket v5 client."""

    def __init__(self, host: str, port: int, password: str):
        self.host = host
        self.port = port
        self.password = password or ""
        self._sock: socket.socket | None = None
        self._send_lock = threading.Lock()
        self._req_counter = 0
        self.connected = False
        self.last_error: str | None = None

    def connect(self, timeout: float = 1.5) -> bool:
        self.close()
        self.last_error = None

        hosts_to_try = [self.host]
        if self.host.lower() == "localhost":
            hosts_to_try.append("127.0.0.1")

        sock = None
        last_exc = None
        for target_host in hosts_to_try:
            try:
                sock = socket.create_connection((target_host, self.port), timeout=timeout)
                break
            except Exception as ex:
                last_exc = ex
                sock = None

        if sock is None:
            self.last_error = f"Cannot reach OBS WebSocket at {self.host}:{self.port} ({last_exc})"
            return False

        try:
            sock.settimeout(1.5)

            ws_key = base64.b64encode(os.urandom(16)).decode("ascii")
            handshake = (
                f"GET / HTTP/1.1\r\n"
                f"Host: {self.host}:{self.port}\r\n"
                f"Upgrade: websocket\r\n"
                f"Connection: Upgrade\r\n"
                f"Sec-WebSocket-Key: {ws_key}\r\n"
                f"Sec-WebSocket-Version: 13\r\n"
                f"Sec-WebSocket-Protocol: obswebsocket.json\r\n\r\n"
            )
            sock.sendall(handshake.encode("ascii"))

            resp = b""
            while b"\r\n\r\n" not in resp:
                chunk = sock.recv(4096)
                if not chunk:
                    sock.close()
                    self.last_error = "OBS WebSocket closed during HTTP upgrade"
                    return False
                resp += chunk

            if b"101 " not in resp.split(b"\r\n", 1)[0]:
                sock.close()
                self.last_error = "OBS WebSocket rejected HTTP upgrade"
                return False

            self._sock = sock

            # Wait for Hello (op 0)
            hello = self._recv_json()
            if not hello or hello.get("op") != 0:
                self.last_error = "Did not receive OBS WebSocket Hello (op 0)"
                self.close()
                return False

            identify_data: dict = {
                "rpcVersion": 1,
                "eventSubscriptions": 64,  # Outputs events (Record/Stream/ReplayBuffer)
            }

            auth_info = hello.get("d", {}).get("authentication")
            if auth_info:
                salt = auth_info["salt"]
                challenge = auth_info["challenge"]
                secret = base64.b64encode(
                    hashlib.sha256((self.password + salt).encode("utf-8")).digest()
                ).decode("ascii")
                auth_resp = base64.b64encode(
                    hashlib.sha256((secret + challenge).encode("utf-8")).digest()
                ).decode("ascii")
                identify_data["authentication"] = auth_resp

            self._send_json({"op": 1, "d": identify_data})
            identified = self._recv_json()
            if not identified or identified.get("op") != 2:
                self.last_error = "auth_failed" if auth_info else "OBS WebSocket Identify rejected"
                self.close()
                return False

            # Shorter timeout during active loop so 1s recording timer updates smoothly
            self._sock.settimeout(0.45)
            self.connected = True
            return True
        except Exception as ex:
            self.last_error = str(ex)
            self.close()
            return False

    def close(self) -> None:
        self.connected = False
        if self._sock is not None:
            try:
                self._sock.close()
            except Exception:
                pass
            self._sock = None

    def send_request(self, request_type: str, request_data: dict | None = None) -> None:
        if not self.connected:
            return
        self._req_counter += 1
        req_id = f"req_{self._req_counter}"
        payload = {
            "op": 6,
            "d": {
                "requestType": request_type,
                "requestId": req_id,
            },
        }
        if request_data:
            payload["d"]["requestData"] = request_data
        self._send_json(payload)

    def recv_message(self) -> dict | None:
        return self._recv_json()

    def _send_json(self, obj: dict) -> None:
        if not self._sock:
            return
        data = json.dumps(obj).encode("utf-8")
        frame = bytearray([0x81])  # FIN + text frame
        length = len(data)
        if length <= 125:
            frame.append(0x80 | length)
        elif length <= 65535:
            frame.append(0x80 | 126)
            frame.extend(struct.pack("!H", length))
        else:
            frame.append(0x80 | 127)
            frame.extend(struct.pack("!Q", length))

        mask = os.urandom(4)
        frame.extend(mask)
        masked = bytes(b ^ mask[i % 4] for i, b in enumerate(data))
        frame.extend(masked)

        with self._send_lock:
            try:
                self._sock.sendall(bytes(frame))
            except Exception:
                self.close()

    def _recv_exact(self, n: int) -> bytes:
        assert self._sock is not None
        buf = bytearray()
        while len(buf) < n:
            chunk = self._sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("WebSocket closed")
            buf.extend(chunk)
        return bytes(buf)

    def _recv_json(self) -> dict | None:
        if not self._sock:
            return None
        try:
            while True:
                header = self._recv_exact(2)
                opcode = header[0] & 0x0F
                masked = (header[1] & 0x80) != 0
                length = header[1] & 0x7F

                if length == 126:
                    length = struct.unpack("!H", self._recv_exact(2))[0]
                elif length == 127:
                    length = struct.unpack("!Q", self._recv_exact(8))[0]

                mask_key = self._recv_exact(4) if masked else b""
                payload = self._recv_exact(length) if length > 0 else b""
                if masked:
                    payload = bytes(b ^ mask_key[i % 4] for i, b in enumerate(payload))

                if opcode == 0x8:  # Close
                    self.close()
                    return None
                if opcode == 0x9:  # Ping -> send Pong
                    self._send_pong(payload)
                    continue
                if opcode == 0xA:  # Pong
                    continue
                if opcode == 0x1:
                    return json.loads(payload.decode("utf-8"))
        except socket.timeout:
            return {"_timeout": True}
        except Exception:
            self.close()
            return None

    def _send_pong(self, data: bytes) -> None:
        if not self._sock:
            return
        frame = bytearray([0x8A, 0x80 | min(len(data), 125)])
        mask = os.urandom(4)
        frame.extend(mask)
        frame.extend(bytes(b ^ mask[i % 4] for i, b in enumerate(data[:125])))
        with self._send_lock:
            try:
                self._sock.sendall(bytes(frame))
            except Exception:
                self.close()


class ObsShortcutsPlugin:
    def __init__(self):
        self.running = True
        self.settings: dict = load_local_metadata_settings()
        self.obs_client: ObsWebSocketV5Client | None = None
        self._ws_config: tuple[str, int, str] = self._extract_ws_config(self.settings)
        self._force_reconnect = False
        self._auth_toast_sent = False

        self.is_recording = False
        self.record_seconds = 0
        self.is_replay_active = False
        self.replay_saving_until = 0.0
        self.replay_duration_sec = 30
        self.is_streaming = False
        self.stream_seconds = 0

        self._state_lock = threading.Lock()
        self.apply_settings(self.settings)

    @staticmethod
    def _extract_ws_config(settings: dict) -> tuple[str, int, str]:
        host, port = normalize_ws_endpoint(
            settings.get("obs_ws_host", "127.0.0.1"),
            settings.get("obs_ws_port", 4455),
        )
        password = str(settings.get("obs_ws_password") or "")
        return host, port, password

    def apply_settings(self, settings: dict) -> None:
        if settings:
            merged = dict(self.settings)
            merged.update(settings)
            self.settings = merged

        new_ws = self._extract_ws_config(self.settings)
        if new_ws != self._ws_config:
            self._ws_config = new_ws
            self._force_reconnect = True
            self._auth_toast_sent = False
            if self.obs_client is not None:
                self.obs_client.close()

        try:
            fallback = int(self.settings.get("replay_buffer_fallback_sec", 30) or 30)
        except Exception:
            fallback = 30
        self.replay_duration_sec = read_obs_replay_buffer_seconds(fallback)

    def build_render_payload(self) -> dict:
        with self._state_lock:
            now = time.monotonic()
            saving_replay = now < self.replay_saving_until
            replay_dot = DOT_REPLAY_SAVING if saving_replay else DOT_REPLAY
            status_replay_dot = STATUS_DOT_REPLAY_SAVING if saving_replay else STATUS_DOT_REPLAY

            buttons = [
                {
                    "id": "toggle_record",
                    "tooltip": "Stop Recording" if self.is_recording else "Start Recording",
                    "dotColor": DOT_RECORD,
                    "blinking": self.is_recording,
                    "active": self.is_recording,
                },
                {
                    "id": "toggle_replay",
                    "secondaryAction": "save_replay",
                    "tooltip": "Stop Replay Buffer (Right-click to Save)" if self.is_replay_active else "Start Replay Buffer",
                    "dotColor": replay_dot,
                    "blinking": self.is_replay_active and not saving_replay,
                    "active": self.is_replay_active,
                },
                {
                    "id": "toggle_stream",
                    "tooltip": "Stop Broadcast" if self.is_streaming else "Start Broadcast",
                    "dotColor": DOT_STREAM,
                    "blinking": self.is_streaming,
                    "active": self.is_streaming,
                },
                {
                    "id": "open_obs",
                    "tooltip": "Open / Restore OBS Studio",
                    "icon": "external_app",
                    "blinking": False,
                    "active": False,
                },
            ]

            status_items = []
            if self.is_recording:
                status_items.append({
                    "id": "rec_status",
                    "dotColor": STATUS_DOT_RECORD,
                    "text": format_duration(self.record_seconds),
                    "label": "Recording",
                })
            if self.is_replay_active:
                status_items.append({
                    "id": "replay_status",
                    "dotColor": status_replay_dot,
                    "text": "Saving..." if saving_replay else format_buffer_seconds(self.replay_duration_sec),
                    "label": "Replay",
                })
            if self.is_streaming:
                status_items.append({
                    "id": "stream_status",
                    "dotColor": STATUS_DOT_STREAM,
                    "text": "Live",
                    "label": "Broadcast",
                })

            return {
                "type": "render",
                "style": "gray",
                "buttons": buttons,
                "statusItems": status_items,
            }

    def emit_render(self) -> None:
        send_host(self.build_render_payload())

    def handle_action(self, action: str) -> None:
        if action == "open_obs":
            obs_exe = str(self.settings.get("obs_exe_path") or r"C:\Program Files\obs-studio\bin\64bit\obs64.exe")
            focus_or_launch_obs(obs_exe)
            return

        if self.obs_client and self.obs_client.connected:
            if action == "toggle_record":
                self.obs_client.send_request("ToggleRecord")
            elif action == "toggle_replay":
                self.obs_client.send_request("ToggleReplayBuffer")
            elif action == "save_replay":
                if self.is_replay_active:
                    with self._state_lock:
                        self.replay_saving_until = time.monotonic() + 2.0
                    self.emit_render()
                    self.obs_client.send_request("SaveReplayBuffer")
            elif action == "toggle_stream":
                self.obs_client.send_request("ToggleStream")
        else:
            # Fallback local toggle feedback when OBS WebSocket is offline
            with self._state_lock:
                if action == "toggle_record":
                    self.is_recording = not self.is_recording
                    if not self.is_recording:
                        self.record_seconds = 0
                elif action == "toggle_replay":
                    self.is_replay_active = not self.is_replay_active
                elif action == "save_replay" and self.is_replay_active:
                    self.replay_saving_until = time.monotonic() + 2.0
                elif action == "toggle_stream":
                    self.is_streaming = not self.is_streaming
            self.emit_render()

    def obs_worker_loop(self) -> None:
        """Maintains real-time OBS state ONLY while this plugin process is open."""
        last_tick = time.monotonic()
        last_reconnect = 0.0

        while self.running:
            try:
                now = time.monotonic()
                host, port, password = self._ws_config

                if self.obs_client is None or not self.obs_client.connected:
                    if self._force_reconnect or (now - last_reconnect >= 1.8):
                        self._force_reconnect = False
                        last_reconnect = now
                        client = ObsWebSocketV5Client(host, port, password)
                        if client.connect(timeout=1.0):
                            self.obs_client = client
                            self._auth_toast_sent = False
                            client.send_request("GetRecordStatus")
                            client.send_request("GetReplayBufferStatus")
                            client.send_request("GetStreamStatus")
                        elif client.last_error == "auth_failed" and not self._auth_toast_sent:
                            self._auth_toast_sent = True
                            send_intent("app.toast", {
                                "message": "OBS WebSocket authentication failed — check password in obs shortcuts settings.",
                                "durationMs": 4000,
                            })
                    time.sleep(0.2)
                else:
                    msg = self.obs_client.recv_message()
                    if msg and not msg.get("_timeout"):
                        self._process_obs_message(msg)

                # 1-second real-time timer update while FuncOverlay is open
                now = time.monotonic()
                if now - last_tick >= 1.0:
                    elapsed = int(now - last_tick)
                    last_tick = now
                    changed = False
                    with self._state_lock:
                        if self.is_recording:
                            self.record_seconds += elapsed
                            changed = True
                        if self.is_streaming:
                            self.stream_seconds += elapsed
                        if self.replay_saving_until > 0 and now >= self.replay_saving_until:
                            self.replay_saving_until = 0.0
                            changed = True
                    if changed:
                        self.emit_render()
            except Exception:
                time.sleep(0.3)

    def _process_obs_message(self, msg: dict) -> None:
        op = msg.get("op")
        d = msg.get("d", {})
        changed = False

        if op == 7:  # RequestResponse
            req_type = d.get("requestType")
            resp = d.get("responseData") or {}
            with self._state_lock:
                if req_type == "GetRecordStatus":
                    self.is_recording = bool(resp.get("outputActive", False))
                    self.record_seconds = int((resp.get("outputDuration", 0) or 0) / 1000)
                    changed = True
                elif req_type == "GetReplayBufferStatus":
                    self.is_replay_active = bool(resp.get("outputActive", False))
                    changed = True
                elif req_type == "GetStreamStatus":
                    self.is_streaming = bool(resp.get("outputActive", False))
                    self.stream_seconds = int((resp.get("outputDuration", 0) or 0) / 1000)
                    changed = True
        elif op == 5:  # Event
            event_type = d.get("eventType")
            ev = d.get("eventData") or {}
            with self._state_lock:
                if event_type == "RecordStateChanged":
                    self.is_recording = bool(ev.get("outputActive", False))
                    if not self.is_recording:
                        self.record_seconds = 0
                    changed = True
                elif event_type == "ReplayBufferStateChanged":
                    self.is_replay_active = bool(ev.get("outputActive", False))
                    changed = True
                elif event_type == "ReplayBufferSaved":
                    self.replay_saving_until = time.monotonic() + 2.0
                    changed = True
                elif event_type == "StreamStateChanged":
                    self.is_streaming = bool(ev.get("outputActive", False))
                    if not self.is_streaming:
                        self.stream_seconds = 0
                    changed = True

        if changed:
            self.emit_render()

    def run(self) -> None:
        _configure_stdio()
        self.emit_render()

        worker = threading.Thread(target=self.obs_worker_loop, daemon=True)
        worker.start()

        while self.running:
            try:
                raw_line = sys.stdin.readline()
            except Exception:
                break
            if not raw_line:
                break

            line = raw_line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except Exception:
                continue

            event = msg.get("event")
            if event in ("overlay_opened", "settings_updated"):
                self.apply_settings(msg.get("settings") or {})
                self.emit_render()
            elif event == "ui_action":
                self.handle_action(str(msg.get("action") or ""))
            elif event == "overlay_closed":
                self.running = False
                break

        self.running = False
        if self.obs_client:
            self.obs_client.close()


if __name__ == "__main__":
    ObsShortcutsPlugin().run()
