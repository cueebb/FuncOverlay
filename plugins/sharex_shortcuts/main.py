#!/usr/bin/env python3
"""
sharex shortcuts - FuncOverlay Python Plugin
- Runs ONLY while FuncOverlay overlay is open (zero background execution when closed).
- All buttons close the overlay and wait for FuncOverlay's FadeDurationMs before
  triggering the ShareX action so screen captures never include the fading overlay.
- Supports a '--delayed-action' one-shot CLI mode spawned detached right before exit
  so waiting longer than the host's 140ms shutdown grace period never loses the action.
"""

import ctypes
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

ICON_PATHS = {
    # Cursor selecting a square/dashed capture region
    "capture_region": [
        "M4,8 V5 A1,1 0 0 1 5,4 H8 M12,4 H14 M18,4 H19 A1,1 0 0 1 20,5 V8 M4,12 V14 M4,18 V19 A1,1 0 0 0 5,20 H8",
        "M12.5,12.5 L15.2,21 L17.1,17.6 L20.5,15.7 Z",
    ],
    # Up and down scrolling arrows
    "scrolling_capture": [
        "M12,3.5 V20.5",
        "M7.5,8 L12,3.5 L16.5,8",
        "M7.5,16 L12,20.5 L16.5,16",
    ],
    # QR code pattern
    "scan_qr": [
        "M3.5,3.5 H9.5 V9.5 H3.5 Z M14.5,3.5 H20.5 V9.5 H14.5 Z M3.5,14.5 H9.5 V20.5 H3.5 Z",
        "M6.5,6.5 L6.55,6.5 M17.5,6.5 L17.55,6.5 M6.5,17.5 L6.55,17.5",
        "M12,4 V7 M12,11 V13 M15,12 H18 M12,16 H15 V19.5 M18.5,14.5 V20 H20.5",
    ],
    # Folder icon
    "open_folder": [
        "M3,7.5 A2,2 0 0 1 5,5.5 H9.5 L11.5,7.8 H19 A2,2 0 0 1 21,9.8 V17.5 A2,2 0 0 1 19,19.5 H5 A2,2 0 0 1 3,17.5 Z",
    ],
    # Open / Restore window icon (matches OBS external_app restore style)
    "restore_sharex": [
        "M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A2,2 0 0 1 16,21 H5 A2,2 0 0 1 3,19 V8 A2,2 0 0 1 5,6 H10",
    ],
}


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
    """Load initial settings from local metadata.json next to main.py."""
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


def read_fade_duration_ms(default_ms: int = 250) -> int:
    """Read FadeDurationMs directly from FuncOverlay's AppSettingsManager settings.json."""
    try:
        local_app = os.environ.get("LOCALAPPDATA")
        candidates = []
        if local_app:
            candidates.append(Path(local_app) / "FuncOverlay" / "settings.json")
        else:
            candidates.append(Path.home() / "AppData" / "Local" / "FuncOverlay" / "settings.json")

        for settings_path in candidates:
            if settings_path.exists():
                raw = json.loads(settings_path.read_text(encoding="utf-8-sig"))
                if isinstance(raw, dict) and "FadeDurationMs" in raw:
                    val = int(raw["FadeDurationMs"])
                    return max(0, min(5000, val))
    except Exception:
        pass
    return default_ms


def resolve_sharex_exe_path(configured_path: str) -> str:
    """Locate ShareX.exe via configured path, registry, standard install dirs, or PATH."""
    raw = os.path.expandvars((configured_path or "").strip().strip('"'))
    if raw and os.path.isfile(raw):
        return raw

    if os.name == "nt":
        try:
            import winreg

            reg_locations = [
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ShareX.exe", ""),
                (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ShareX.exe", ""),
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\ShareX", "InstallLocation"),
                (winreg.HKEY_CURRENT_USER, r"SOFTWARE\ShareX", "InstallLocation"),
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
                        cand = os.path.join(val, "ShareX.exe")
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
            os.path.join(pf, "ShareX", "ShareX.exe"),
            os.path.join(pfx86, "ShareX", "ShareX.exe"),
            os.path.join(pfx86, "Steam", "steamapps", "common", "ShareX", "ShareX.exe"),
            os.path.join(pf, "Steam", "steamapps", "common", "ShareX", "ShareX.exe"),
        ]
        if local_app:
            candidates.append(os.path.join(local_app, "Programs", "ShareX", "ShareX.exe"))
        for cand in candidates:
            if os.path.isfile(cand):
                return cand

    found = shutil.which("ShareX.exe") or shutil.which("ShareX")
    if found:
        return found

    return raw or r"C:\Program Files\ShareX\ShareX.exe"


def resolve_sharex_screenshots_folder(configured_folder: str, sharex_exe: str) -> str | None:
    """Resolve the ShareX Screenshots folder from override, ShareX ApplicationConfig.json, or default Documents."""
    override = os.path.expandvars(os.path.expanduser((configured_folder or "").strip().strip('"')))
    if override and os.path.isdir(override):
        return override

    config_dirs: list[Path] = []
    if sharex_exe:
        exe_dir = Path(sharex_exe).parent
        # Portable mode ShareX folder
        if (exe_dir / "ShareX").is_dir():
            config_dirs.append(exe_dir / "ShareX")
        if (exe_dir / "ApplicationConfig.json").is_file():
            config_dirs.append(exe_dir)

    user_docs = Path.home() / "Documents" / "ShareX"
    config_dirs.append(user_docs)
    local_app = os.environ.get("LOCALAPPDATA")
    if local_app:
        config_dirs.append(Path(local_app) / "ShareX")
    appdata = os.environ.get("APPDATA")
    if appdata:
        config_dirs.append(Path(appdata) / "ShareX")

    personal_path: Path | None = None
    for cfg_dir in config_dirs:
        cfg_file = cfg_dir / "ApplicationConfig.json"
        if cfg_file.is_file():
            try:
                cfg = json.loads(cfg_file.read_text(encoding="utf-8-sig"))
                if isinstance(cfg, dict):
                    if cfg.get("UseCustomScreenshotsPath") and cfg.get("CustomScreenshotsPath"):
                        custom = os.path.expandvars(str(cfg["CustomScreenshotsPath"]).strip())
                        if custom and os.path.isdir(custom):
                            return custom
                    if cfg.get("PersonalPath"):
                        p = Path(os.path.expandvars(str(cfg["PersonalPath"]).strip()))
                        if (p / "Screenshots").is_dir():
                            return str(p / "Screenshots")
                        personal_path = p
            except Exception:
                pass

        if (cfg_dir / "Screenshots").is_dir():
            return str(cfg_dir / "Screenshots")

    if personal_path and personal_path.is_dir():
        return str(personal_path)

    fallback = user_docs / "Screenshots"
    if fallback.is_dir():
        return str(fallback)
    if user_docs.is_dir():
        return str(user_docs)

    return None


def _find_sharex_pids() -> set[int]:
    """Find running Windows PIDs for ShareX.exe."""
    if os.name != "nt":
        return set()

    from ctypes import wintypes

    TH32CS_SNAPPROCESS = 0x00000002
    INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ProcessID", wintypes.DWORD),
            ("th32DefaultHeapID", ctypes.c_size_t),
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

    pids: set[int] = set()
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if not snap or snap == INVALID_HANDLE_VALUE:
        return pids

    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(PROCESSENTRY32W)
        ok = kernel32.Process32FirstW(snap, ctypes.byref(entry))
        while ok:
            if (entry.szExeFile or "").strip().lower() == "sharex.exe":
                pids.add(int(entry.th32ProcessID))
            ok = kernel32.Process32NextW(snap, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snap)
    return pids


def _find_sharex_main_hwnd(pids: set[int]) -> int:
    """Locate ShareX's main application window HWND (visible, minimized, or hidden in tray)."""
    if os.name != "nt" or not pids:
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

    best_hwnd = 0
    best_score = 0

    def _enum_cb(hwnd: int, _lparam: int) -> bool:
        nonlocal best_hwnd, best_score
        pid = wintypes.DWORD(0)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if int(pid.value) not in pids:
            return True

        if user32.GetWindow(hwnd, GW_OWNER):
            return True

        cls_buf = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, cls_buf, 256)
        cls_lower = cls_buf.value.strip().lower()
        if any(tok in cls_lower for tok in ("ime", "msctfime", "gdi+ window", "olemainthreadwndclass", "tooltips")):
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
            has_size = (rc.right - rc.left) >= 260 and (rc.bottom - rc.top) >= 180

        score = 0
        title_lower = title.lower()
        if "sharex" in title_lower:
            score += 100
        elif title:
            score += 20
        if (style & WS_CAPTION) == WS_CAPTION:
            score += 40
        if style & WS_SYSMENU:
            score += 20
        if style & (WS_MINIMIZEBOX | WS_MAXIMIZEBOX):
            score += 20
        if not (ex_style & WS_EX_TOOLWINDOW):
            score += 15
        if has_size:
            score += 25
        if user32.IsWindowVisible(hwnd):
            score += 10

        if score >= 80 and score > best_score:
            best_score = score
            best_hwnd = int(hwnd)
        return True

    cb = WNDENUMPROC(_enum_cb)
    user32.EnumWindows(cb, 0)
    return best_hwnd


def _restore_and_focus_hwnd(hwnd: int) -> bool:
    """Restore a ShareX window to its normal/previous state (without forcing maximize) and focus it."""
    if os.name != "nt" or not hwnd:
        return False

    from ctypes import wintypes

    SW_SHOWNORMAL = 1
    SW_SHOWMAXIMIZED = 3
    SW_SHOW = 5
    SW_RESTORE = 9
    WPF_RESTORETOMAXIMIZED = 0x0002
    HWND_TOPMOST = ctypes.c_void_p(-1)
    HWND_NOTOPMOST = ctypes.c_void_p(-2)
    SWP_NOMOVE = 0x0001
    SWP_NOSIZE = 0x0002

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
    user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.ShowWindow.restype = wintypes.BOOL
    user32.SetWindowPos.argtypes = [
        wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT
    ]
    user32.SetWindowPos.restype = wintypes.BOOL
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
    user32.SwitchToThisWindow.argtypes = [wintypes.HWND, wintypes.BOOL]
    user32.SwitchToThisWindow.restype = None
    kernel32.GetCurrentThreadId.argtypes = []
    kernel32.GetCurrentThreadId.restype = wintypes.DWORD

    if not user32.IsWindow(hwnd):
        return False

    wp = WINDOWPLACEMENT()
    wp.length = ctypes.sizeof(WINDOWPLACEMENT)
    user32.GetWindowPlacement(hwnd, ctypes.byref(wp))
    was_maximized = (wp.showCmd == SW_SHOWMAXIMIZED) or bool(wp.flags & WPF_RESTORETOMAXIMIZED)

    if not user32.IsWindowVisible(hwnd):
        user32.ShowWindow(hwnd, SW_SHOW)
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, SW_SHOWMAXIMIZED if was_maximized else SW_RESTORE)
    elif not was_maximized:
        user32.ShowWindow(hwnd, SW_SHOWNORMAL)

    user32.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE)
    user32.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE)

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
    finally:
        if attached_tgt:
            user32.AttachThreadInput(cur_tid, tgt_tid, False)
        if attached_fg:
            user32.AttachThreadInput(cur_tid, fg_tid, False)

    user32.SwitchToThisWindow(hwnd, True)
    return True


def _resolve_worker_python() -> str:
    """Prefer pythonw.exe alongside sys.executable on Windows so detached workers never flash a console."""
    exe = sys.executable or "python"
    if os.name == "nt":
        p = Path(exe)
        if p.name.lower() == "python.exe":
            pw = p.with_name("pythonw.exe")
            if pw.is_file():
                return str(pw)
    return exe


def _is_hwnd_visible(hwnd: int) -> bool:
    if os.name != "nt" or not hwnd:
        return False
    from ctypes import wintypes
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    return bool(user32.IsWindowVisible(hwnd))


def _launch_process_detached(cmd: list[str]) -> None:
    if not cmd:
        return
    creationflags = 0
    if os.name == "nt":
        creationflags = (
            getattr(subprocess, "DETACHED_PROCESS", 0x00000008)
            | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0x00000200)
            | getattr(subprocess, "CREATE_NO_WINDOW", 0x08000000)
        )
    subprocess.Popen(
        cmd,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        close_fds=True,
        creationflags=creationflags,
    )


def execute_sharex_action(
    action: str,
    sharex_exe: str,
    screenshots_folder: str = "",
    qr_cli_arg: str = "-QRCodeScanRegion",
) -> None:
    """Execute the target ShareX action (called after waiting for overlay fade-out)."""
    resolved_exe = resolve_sharex_exe_path(sharex_exe)

    if action == "capture_region":
        if os.path.isfile(resolved_exe):
            _launch_process_detached([resolved_exe, "-RectangleRegion"])
        return

    if action == "scrolling_capture":
        if os.path.isfile(resolved_exe):
            _launch_process_detached([resolved_exe, "-ScrollingCapture"])
        return

    if action == "scan_qr":
        arg = (qr_cli_arg or "-QRCodeScanRegion").strip()
        if not arg.startswith("-"):
            arg = "-" + arg
        if os.path.isfile(resolved_exe):
            _launch_process_detached([resolved_exe, arg])
        return

    if action == "open_folder":
        folder = resolve_sharex_screenshots_folder(screenshots_folder, resolved_exe)
        if folder and os.path.isdir(folder):
            if os.name == "nt":
                try:
                    os.startfile(folder)  # type: ignore[attr-defined]
                    return
                except Exception:
                    _launch_process_detached(["explorer.exe", folder])
                    return
        elif os.path.isfile(resolved_exe):
            _launch_process_detached([resolved_exe, "-OpenScreenshotsFolder"])
        return

    if action in ("restore_sharex", "maximize_sharex"):
        pids = _find_sharex_pids()
        if pids:
            hwnd = _find_sharex_main_hwnd(pids)
            if hwnd and _is_hwnd_visible(hwnd) and _restore_and_focus_hwnd(hwnd):
                return

        if os.path.isfile(resolved_exe):
            # Launching ShareX.exe without -silent wakes an existing tray instance cleanly via
            # ShareX's single-instance handler (or launches ShareX if closed) without forcing maximize.
            _launch_process_detached([resolved_exe])
            if os.name == "nt":
                for _ in range(30):
                    time.sleep(0.1)
                    pids = _find_sharex_pids()
                    if pids:
                        hwnd = _find_sharex_main_hwnd(pids)
                        if hwnd and _is_hwnd_visible(hwnd):
                            _restore_and_focus_hwnd(hwnd)
                            return
                if pids:
                    hwnd = _find_sharex_main_hwnd(pids)
                    if hwnd:
                        _restore_and_focus_hwnd(hwnd)


class ShareXShortcutsPlugin:
    def __init__(self):
        self.running = True
        self.settings: dict = load_local_metadata_settings()

    def apply_settings(self, settings: dict) -> None:
        if settings:
            merged = dict(self.settings)
            merged.update(settings)
            self.settings = merged

    def build_render_payload(self) -> dict:
        buttons = [
            {
                "id": "capture_region",
                "tooltip": "Capture Region (Screenshot)",
                "icon": "capture_region",
                "iconPaths": ICON_PATHS["capture_region"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "scrolling_capture",
                "tooltip": "Scrolling Capture",
                "icon": "scrolling_capture",
                "iconPaths": ICON_PATHS["scrolling_capture"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "scan_qr",
                "tooltip": "Scan QR Code (Region)",
                "icon": "scan_qr",
                "iconPaths": ICON_PATHS["scan_qr"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "open_folder",
                "tooltip": "Open Screenshots Folder",
                "icon": "open_folder",
                "iconPaths": ICON_PATHS["open_folder"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "restore_sharex",
                "tooltip": "Open / Restore ShareX",
                "icon": "external_app",
                "iconPaths": ICON_PATHS["restore_sharex"],
                "blinking": False,
                "active": False,
            },
        ]

        return {
            "type": "render",
            "style": "gray",
            "buttons": buttons,
            "statusItems": [],
        }

    def emit_render(self) -> None:
        send_host(self.build_render_payload())

    def handle_action(self, action: str) -> None:
        valid_actions = {
            "capture_region",
            "scrolling_capture",
            "scan_qr",
            "open_folder",
            "restore_sharex",
            "maximize_sharex",
        }
        if action not in valid_actions:
            return

        fade_ms = read_fade_duration_ms(default_ms=250)
        sharex_exe = str(self.settings.get("sharex_exe_path") or r"C:\Program Files\ShareX\ShareX.exe")
        screenshots_folder = str(self.settings.get("screenshots_folder") or "")
        qr_cli_arg = str(self.settings.get("qr_scan_cli_arg") or "-QRCodeScanRegion")

        # Spawn detached one-shot helper BEFORE closing overlay, then immediately exit so the
        # host sees Process.HasExited == true and does not kill our child during its 140ms shutdown.
        # The detached helper sleeps FadeDurationMs and then executes ShareX cleanly.
        helper_cmd = [
            _resolve_worker_python(),
            os.path.abspath(__file__),
            "--delayed-action",
            action,
            str(fade_ms),
            sharex_exe,
            screenshots_folder,
            qr_cli_arg,
        ]
        try:
            _launch_process_detached(helper_cmd)
        except Exception:
            # Fallback: close overlay and execute directly if detached spawn fails
            send_intent("overlay.close")
            execute_sharex_action(action, sharex_exe, screenshots_folder, qr_cli_arg)
            self.running = False
            return

        send_intent("overlay.close")
        self.running = False


def run_delayed_action_cli(argv: list[str]) -> int:
    # argv: [--delayed-action, action, fade_ms, sharex_exe, screenshots_folder, qr_cli_arg]
    action = argv[1] if len(argv) > 1 else ""
    try:
        fade_ms = max(0, min(5000, int(argv[2]))) if len(argv) > 2 else 250
    except Exception:
        fade_ms = 250
    sharex_exe = argv[3] if len(argv) > 3 else ""
    screenshots_folder = argv[4] if len(argv) > 4 else ""
    qr_cli_arg = argv[5] if len(argv) > 5 else "-QRCodeScanRegion"

    if fade_ms > 0:
        time.sleep(fade_ms / 1000.0)

    execute_sharex_action(action, sharex_exe, screenshots_folder, qr_cli_arg)
    return 0


def main() -> None:
    if len(sys.argv) >= 2 and sys.argv[1] == "--delayed-action":
        raise SystemExit(run_delayed_action_cli(sys.argv[1:]))

    _configure_stdio()
    plugin = ShareXShortcutsPlugin()
    plugin.emit_render()

    while plugin.running:
        try:
            raw_line = sys.stdin.readline()
        except Exception:
            break

        if not raw_line:
            break

        raw_line = raw_line.strip()
        if not raw_line:
            continue

        try:
            msg = json.loads(raw_line)
        except Exception:
            continue

        if not isinstance(msg, dict):
            continue

        ev = msg.get("event")
        if ev == "overlay_opened":
            if isinstance(msg.get("settings"), dict):
                plugin.apply_settings(msg["settings"])
            plugin.emit_render()
        elif ev == "settings_updated":
            if isinstance(msg.get("settings"), dict):
                plugin.apply_settings(msg["settings"])
        elif ev == "ui_action":
            action = str(msg.get("action") or "")
            plugin.handle_action(action)
        elif ev == "overlay_closed":
            plugin.running = False
            break


if __name__ == "__main__":
    main()
