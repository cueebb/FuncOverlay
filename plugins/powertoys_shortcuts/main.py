#!/usr/bin/env python3
"""
powertoys shortcuts - FuncOverlay Python Plugin
- Runs ONLY while FuncOverlay overlay is open (zero background execution when closed).
- All buttons close the overlay and sleep for FuncOverlay's FadeDurationMs until the
  overlay disappears before firing PowerToys actions.
- Captures the last active non-FuncOverlay window before closing so Always on Top and
  Crop and Lock target the user's previous active application cleanly.
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
    # 1. Color Picker: Pipette / Eyedropper
    "color_picker": [
        "M5,19 L5.8,15.2 L14.5,6.5 L17.5,9.5 L8.8,18.2 Z",
        "M13,5 L19,11",
        "M15.2,5.8 L17,4 A2.1,2.1 0 0 1 20,7 L18.2,8.8",
    ],
    # 2. Text Extractor: Big "T"
    "text_extractor": [
        "M5,5 H19 M12,5 V19 M5,5 V7.2 M19,5 V7.2 M9.5,19 H14.5",
    ],
    # 3. Always on Top: One square cleanly stacked on top of another
    "always_on_top": [
        "M9,14 V19 A1,1 0 0 0 10,20 H19 A1,1 0 0 0 20,19 V10 A1,1 0 0 0 19,9 H14",
        "M4,5 A1,1 0 0 1 5,4 H13 A1,1 0 0 1 14,5 V13 A1,1 0 0 1 13,14 H5 A1,1 0 0 1 4,13 Z",
    ],
    # 4. Screen Ruler: Horizontal ruler with tick marks
    "screen_ruler": [
        "M3,8 A1,1 0 0 1 4,7 H20 A1,1 0 0 1 21,8 V16 A1,1 0 0 1 20,17 H4 A1,1 0 0 1 3,16 Z",
        "M7,7 V11.5 M10.5,7 V10 M14,7 V11.5 M17.5,7 V10",
    ],
    # 5. Crop and Lock (Screenshot): Gallery / Photos landscape icon (frame + mountains + sun)
    "crop_and_lock": [
        "M3.5,5.5 A1.5,1.5 0 0 1 5,4 H19 A1.5,1.5 0 0 1 20.5,5.5 V18.5 A1.5,1.5 0 0 1 19,20 H5 A1.5,1.5 0 0 1 3.5,18.5 Z",
        "M4,16.5 L9.5,11.5 L13,15 L15.5,12.5 L20,17.5",
        "M14.6,8.2 A1.4,1.4 0 1 0 17.4,8.2 A1.4,1.4 0 1 0 14.6,8.2 Z",
    ],
}

DEFAULT_SHORTCUTS = {
    "color_picker": {"win": True, "ctrl": False, "alt": False, "shift": True, "code": 0x43},      # Win+Shift+C
    "text_extractor": {"win": True, "ctrl": False, "alt": False, "shift": True, "code": 0x54},    # Win+Shift+T
    "always_on_top": {"win": True, "ctrl": True, "alt": False, "shift": False, "code": 0x54},     # Win+Ctrl+T
    "screen_ruler": {"win": True, "ctrl": True, "alt": False, "shift": True, "code": 0x4D},       # Win+Ctrl+Shift+M
    "crop_and_lock_screenshot": {"win": True, "ctrl": True, "alt": False, "shift": True, "code": 0x53},  # Win+Ctrl+Shift+S
    "crop_and_lock_thumbnail": {"win": True, "ctrl": True, "alt": False, "shift": True, "code": 0x54},   # Win+Ctrl+Shift+T
    "crop_and_lock_reparent": {"win": True, "ctrl": True, "alt": False, "shift": True, "code": 0x52},    # Win+Ctrl+Shift+R
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
    """Read FadeDurationMs from FuncOverlay's settings.json."""
    try:
        local_app = os.environ.get("LOCALAPPDATA")
        settings_path = (
            Path(local_app) / "FuncOverlay" / "settings.json"
            if local_app
            else Path.home() / "AppData" / "Local" / "FuncOverlay" / "settings.json"
        )
        if settings_path.exists():
            raw = json.loads(settings_path.read_text(encoding="utf-8-sig"))
            if isinstance(raw, dict) and "FadeDurationMs" in raw:
                return max(0, min(5000, int(raw["FadeDurationMs"])))
    except Exception:
        pass
    return default_ms


def _powertoys_config_root() -> Path:
    local_app = os.environ.get("LOCALAPPDATA")
    if local_app:
        return Path(local_app) / "Microsoft" / "PowerToys"
    return Path.home() / "AppData" / "Local" / "Microsoft" / "PowerToys"


def _read_powertoys_hotkey(module_dirs: list[str], property_name: str, fallback: dict) -> dict:
    """Read user-customized shortcut from PowerToys module settings.json, falling back to PowerToys defaults."""
    root = _powertoys_config_root()
    for mod in module_dirs:
        cfg_file = root / mod / "settings.json"
        if not cfg_file.is_file():
            continue
        try:
            data = json.loads(cfg_file.read_text(encoding="utf-8-sig"))
            props = data.get("properties") if isinstance(data, dict) else None
            if isinstance(props, dict):
                hk = props.get(property_name)
                if isinstance(hk, dict) and int(hk.get("code") or 0) > 0:
                    return {
                        "win": bool(hk.get("win", False)),
                        "ctrl": bool(hk.get("ctrl", False)),
                        "alt": bool(hk.get("alt", False)),
                        "shift": bool(hk.get("shift", False)),
                        "code": int(hk["code"]),
                    }
        except Exception:
            continue
    return dict(fallback)


def resolve_hotkey_for_action(action: str, crop_lock_mode: str = "screenshot") -> dict | None:
    if action == "color_picker":
        return _read_powertoys_hotkey(["ColorPicker"], "ActivationShortcut", DEFAULT_SHORTCUTS["color_picker"])
    if action == "text_extractor":
        return _read_powertoys_hotkey(["TextExtractor"], "ActivationShortcut", DEFAULT_SHORTCUTS["text_extractor"])
    if action == "always_on_top":
        return _read_powertoys_hotkey(["AlwaysOnTop"], "Hotkey", DEFAULT_SHORTCUTS["always_on_top"])
    if action == "screen_ruler":
        return _read_powertoys_hotkey(["Measure Tool", "MeasureTool", "ScreenRuler"], "ActivationShortcut", DEFAULT_SHORTCUTS["screen_ruler"])
    if action == "crop_and_lock":
        mode = (crop_lock_mode or "screenshot").strip().lower()
        if mode == "thumbnail":
            return _read_powertoys_hotkey(["CropAndLock"], "ThumbnailHotkey", DEFAULT_SHORTCUTS["crop_and_lock_thumbnail"])
        if mode == "reparent":
            return _read_powertoys_hotkey(["CropAndLock"], "ReparentHotkey", DEFAULT_SHORTCUTS["crop_and_lock_reparent"])
        # Default: Crop and Lock Screenshot mode (falls back to ScreenshotHotkey or ThumbnailHotkey if absent)
        return _read_powertoys_hotkey(["CropAndLock"], "ScreenshotHotkey", DEFAULT_SHORTCUTS["crop_and_lock_screenshot"])
    return None


def resolve_powertoys_exe_path(configured_path: str) -> str:
    """Locate PowerToys.exe via configured path, registry, standard install dirs, or PATH."""
    raw = os.path.expandvars((configured_path or "").strip().strip('"'))
    if raw and os.path.isfile(raw):
        return raw

    if os.name == "nt":
        try:
            import winreg

            reg_locations = [
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\PowerToys.exe", ""),
                (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\PowerToys.exe", ""),
            ]
            for root, subkey, val_name in reg_locations:
                try:
                    with winreg.OpenKey(root, subkey) as key:
                        val, _ = winreg.QueryValueEx(key, val_name)
                        val = str(val or "").strip().strip('"')
                        if val and os.path.isfile(val):
                            return val
                except OSError:
                    continue
        except ImportError:
            pass

        pf = os.environ.get("ProgramFiles", r"C:\Program Files")
        pfx86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
        local_app = os.environ.get("LOCALAPPDATA", "")
        candidates = [
            os.path.join(pf, "PowerToys", "PowerToys.exe"),
            os.path.join(pfx86, "PowerToys", "PowerToys.exe"),
        ]
        if local_app:
            candidates.append(os.path.join(local_app, "PowerToys", "PowerToys.exe"))
        for cand in candidates:
            if os.path.isfile(cand):
                return cand

    found = shutil.which("PowerToys.exe") or shutil.which("PowerToys")
    if found:
        return found

    return raw or r"C:\Program Files\PowerToys\PowerToys.exe"


def _find_pids_by_exe_names(names: set[str]) -> set[int]:
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
            if (entry.szExeFile or "").strip().lower() in names:
                pids.add(int(entry.th32ProcessID))
            ok = kernel32.Process32NextW(snap, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snap)
    return pids


def _is_powertoys_running() -> bool:
    return bool(_find_pids_by_exe_names({"powertoys.exe"}))


def _find_last_active_app_hwnd() -> int:
    """
    Walk Windows Z-order from top to bottom and return the highest visible, non-minimized,
    non-cloaked application window that does not belong to FuncOverlay or the desktop shell.
    """
    if os.name != "nt":
        return 0

    from ctypes import wintypes

    GW_HWNDFIRST = 0
    GW_HWNDNEXT = 2
    GW_OWNER = 4
    GWL_EXSTYLE = -20
    WS_EX_TOOLWINDOW = 0x00000080
    WS_EX_NOACTIVATE = 0x08000000
    DWMWA_CLOAKED = 14

    func_pids = _find_pids_by_exe_names({"funcoverlay.exe"})

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.GetDesktopWindow.argtypes = []
    user32.GetDesktopWindow.restype = wintypes.HWND
    user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetWindow.restype = wintypes.HWND
    user32.GetTopWindow.argtypes = [wintypes.HWND]
    user32.GetTopWindow.restype = wintypes.HWND
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.IsIconic.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.GetWindowLongW.restype = wintypes.LONG
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetClassNameW.restype = ctypes.c_int
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL

    try:
        dwmapi = ctypes.WinDLL("dwmapi")
        dwmapi.DwmGetWindowAttribute.argtypes = [
            wintypes.HWND, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD
        ]
        dwmapi.DwmGetWindowAttribute.restype = ctypes.c_long
    except Exception:
        dwmapi = None

    ignored_classes = {
        "progman",
        "workerw",
        "shell_traywnd",
        "shell_secondarytraywnd",
        "windows.ui.core.corewindow",
        "multitaskingviewframe",
        "tasklistthumbnailwnd",
        "foregroundstaging",
    }

    hwnd = user32.GetTopWindow(None)
    while hwnd:
        try:
            if user32.IsWindowVisible(hwnd) and not user32.IsIconic(hwnd) and not user32.GetWindow(hwnd, GW_OWNER):
                pid = wintypes.DWORD(0)
                user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
                if int(pid.value) not in func_pids:
                    ex_style = int(user32.GetWindowLongW(hwnd, GWL_EXSTYLE)) & 0xFFFFFFFF
                    if not (ex_style & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)):
                        cls_buf = ctypes.create_unicode_buffer(256)
                        user32.GetClassNameW(hwnd, cls_buf, 256)
                        cls_lower = cls_buf.value.strip().lower()
                        if cls_lower not in ignored_classes:
                            cloaked = ctypes.c_int(0)
                            if dwmapi is not None:
                                dwmapi.DwmGetWindowAttribute(
                                    hwnd, DWMWA_CLOAKED, ctypes.byref(cloaked), ctypes.sizeof(cloaked)
                                )
                            if cloaked.value == 0:
                                rect = wintypes.RECT()
                                if user32.GetWindowRect(hwnd, ctypes.byref(rect)):
                                    if (rect.right - rect.left) >= 80 and (rect.bottom - rect.top) >= 60:
                                        return int(hwnd)
        except Exception:
            pass
        hwnd = user32.GetWindow(hwnd, GW_HWNDNEXT)

    return 0


def _wait_until_funcoverlay_hidden(max_extra_ms: int = 120) -> None:
    """Verify FuncOverlay is no longer the foreground window after the fade sleep."""
    if os.name != "nt":
        return
    from ctypes import wintypes

    func_pids = _find_pids_by_exe_names({"funcoverlay.exe"})
    if not func_pids:
        return

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.GetForegroundWindow.argtypes = []
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD

    deadline = time.monotonic() + (max_extra_ms / 1000.0)
    while time.monotonic() < deadline:
        fg = user32.GetForegroundWindow()
        if not fg:
            break
        pid = wintypes.DWORD(0)
        user32.GetWindowThreadProcessId(fg, ctypes.byref(pid))
        if int(pid.value) not in func_pids:
            break
        time.sleep(0.01)


def _focus_hwnd(hwnd: int) -> bool:
    """Bring the target application window cleanly to the foreground."""
    if os.name != "nt" or not hwnd:
        return False

    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    user32.IsWindow.argtypes = [wintypes.HWND]
    user32.IsWindow.restype = wintypes.BOOL
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
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
    kernel32.GetCurrentThreadId.argtypes = []
    kernel32.GetCurrentThreadId.restype = wintypes.DWORD

    if not user32.IsWindow(hwnd) or not user32.IsWindowVisible(hwnd):
        return False

    fg = user32.GetForegroundWindow()
    if int(fg or 0) == hwnd:
        return True

    cur_tid = kernel32.GetCurrentThreadId()
    fg_tid = user32.GetWindowThreadProcessId(fg, None) if fg else 0
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

    return True


def _send_hotkey_combo(hotkey: dict) -> bool:
    """Send a PowerToys global hotkey via Win32 SendInput with hardware scan codes."""
    if os.name != "nt" or not hotkey:
        return False

    from ctypes import wintypes

    INPUT_KEYBOARD = 1
    KEYEVENTF_EXTENDEDKEY = 0x0001
    KEYEVENTF_KEYUP = 0x0002

    VK_LWIN = 0x5B
    VK_LCONTROL = 0xA2
    VK_LSHIFT = 0xA0
    VK_LMENU = 0xA4

    class MOUSEINPUT(ctypes.Structure):
        _fields_ = [
            ("dx", wintypes.LONG),
            ("dy", wintypes.LONG),
            ("mouseData", wintypes.DWORD),
            ("dwFlags", wintypes.DWORD),
            ("time", wintypes.DWORD),
            ("dwExtraInfo", ctypes.c_size_t),
        ]

    class KEYBDINPUT(ctypes.Structure):
        _fields_ = [
            ("wVk", wintypes.WORD),
            ("wScan", wintypes.WORD),
            ("dwFlags", wintypes.DWORD),
            ("time", wintypes.DWORD),
            ("dwExtraInfo", ctypes.c_size_t),
        ]

    class HARDWAREINPUT(ctypes.Structure):
        _fields_ = [
            ("uMsg", wintypes.DWORD),
            ("wParamL", wintypes.WORD),
            ("wParamH", wintypes.WORD),
        ]

    class _INPUT_UNION(ctypes.Union):
        _fields_ = [
            ("mi", MOUSEINPUT),
            ("ki", KEYBDINPUT),
            ("hi", HARDWAREINPUT),
        ]

    class INPUT(ctypes.Structure):
        _anonymous_ = ("u",)
        _fields_ = [
            ("type", wintypes.DWORD),
            ("u", _INPUT_UNION),
        ]

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.MapVirtualKeyW.argtypes = [wintypes.UINT, wintypes.UINT]
    user32.MapVirtualKeyW.restype = wintypes.UINT
    user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
    user32.SendInput.restype = wintypes.UINT

    mods: list[tuple[int, bool]] = []
    if hotkey.get("win"):
        mods.append((VK_LWIN, True))
    if hotkey.get("ctrl"):
        mods.append((VK_LCONTROL, False))
    if hotkey.get("shift"):
        mods.append((VK_LSHIFT, False))
    if hotkey.get("alt"):
        mods.append((VK_LMENU, False))

    target_vk = int(hotkey.get("code") or 0)
    if target_vk <= 0:
        return False

    def _make_key_input(vk: int, extended: bool, key_up: bool) -> INPUT:
        flags = 0
        if extended:
            flags |= KEYEVENTF_EXTENDEDKEY
        if key_up:
            flags |= KEYEVENTF_KEYUP
        scan = user32.MapVirtualKeyW(vk, 0) & 0xFFFF
        inp = INPUT()
        inp.type = INPUT_KEYBOARD
        inp.ki = KEYBDINPUT(wVk=vk, wScan=scan, dwFlags=flags, time=0, dwExtraInfo=0)
        return inp

    sequence: list[INPUT] = []
    for vk, ext in mods:
        sequence.append(_make_key_input(vk, ext, False))
    sequence.append(_make_key_input(target_vk, False, False))
    sequence.append(_make_key_input(target_vk, False, True))
    for vk, ext in reversed(mods):
        sequence.append(_make_key_input(vk, ext, True))

    arr = (INPUT * len(sequence))(*sequence)
    sent = user32.SendInput(len(sequence), arr, ctypes.sizeof(INPUT))
    return sent == len(sequence)


def _resolve_worker_python() -> str:
    exe = sys.executable or "python"
    if os.name == "nt":
        p = Path(exe)
        if p.name.lower() == "python.exe":
            pw = p.with_name("pythonw.exe")
            if pw.is_file():
                return str(pw)
    return exe


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


def execute_powertoys_action(
    action: str,
    powertoys_exe: str,
    crop_lock_mode: str,
    auto_launch: bool,
    target_hwnd: int,
    additional_sleep_ms: int = 100,
) -> None:
    """Ensure PowerToys is running, wait additional_sleep_ms + focus target window for AOT/CAL, and send hotkey."""
    if os.name == "nt" and auto_launch and not _is_powertoys_running():
        resolved_exe = resolve_powertoys_exe_path(powertoys_exe)
        if os.path.isfile(resolved_exe):
            _launch_process_detached([resolved_exe])
            for _ in range(25):
                time.sleep(0.1)
                if _is_powertoys_running():
                    time.sleep(0.35)
                    break

    _wait_until_funcoverlay_hidden(max_extra_ms=100)

    # Extra wait + active app restoration ONLY for Always on Top (AOT) and Crop and Lock (CAL)
    if action in ("always_on_top", "crop_and_lock"):
        if additional_sleep_ms > 0:
            time.sleep(additional_sleep_ms / 1000.0)
        if target_hwnd:
            _focus_hwnd(target_hwnd)
            time.sleep(0.03)
        else:
            fallback_hwnd = _find_last_active_app_hwnd()
            if fallback_hwnd:
                _focus_hwnd(fallback_hwnd)
                time.sleep(0.03)

    hotkey = resolve_hotkey_for_action(action, crop_lock_mode)
    if hotkey:
        _send_hotkey_combo(hotkey)


class PowerToysShortcutsPlugin:
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
                "id": "color_picker",
                "tooltip": "Color Picker",
                "icon": "color_picker",
                "iconPaths": ICON_PATHS["color_picker"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "text_extractor",
                "tooltip": "Text Extractor (OCR)",
                "icon": "text_extractor",
                "iconPaths": ICON_PATHS["text_extractor"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "always_on_top",
                "tooltip": "Toggle Always on Top (Last Active App)",
                "icon": "always_on_top",
                "iconPaths": ICON_PATHS["always_on_top"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "screen_ruler",
                "tooltip": "Screen Ruler",
                "icon": "screen_ruler",
                "iconPaths": ICON_PATHS["screen_ruler"],
                "blinking": False,
                "active": False,
            },
            {
                "id": "crop_and_lock",
                "tooltip": "Crop and Lock (Screenshot)",
                "icon": "crop_and_lock",
                "iconPaths": ICON_PATHS["crop_and_lock"],
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
            "color_picker",
            "text_extractor",
            "always_on_top",
            "screen_ruler",
            "crop_and_lock",
        }
        if action not in valid_actions:
            return

        fade_ms = read_fade_duration_ms(default_ms=250)
        target_hwnd = _find_last_active_app_hwnd()
        powertoys_exe = str(self.settings.get("powertoys_exe_path") or r"C:\Program Files\PowerToys\PowerToys.exe")
        crop_lock_mode = str(self.settings.get("crop_lock_mode") or "screenshot")
        auto_launch = bool(self.settings.get("auto_launch_powertoys", True))
        try:
            additional_sleep_ms = max(0, min(5000, int(self.settings.get("additional_sleep_ms", 100))))
        except Exception:
            additional_sleep_ms = 100

        helper_cmd = [
            _resolve_worker_python(),
            os.path.abspath(__file__),
            "--delayed-action",
            action,
            str(fade_ms),
            powertoys_exe,
            crop_lock_mode,
            "1" if auto_launch else "0",
            str(target_hwnd),
            str(additional_sleep_ms),
        ]
        try:
            _launch_process_detached(helper_cmd)
        except Exception:
            send_intent("overlay.close")
            execute_powertoys_action(action, powertoys_exe, crop_lock_mode, auto_launch, target_hwnd, additional_sleep_ms)
            self.running = False
            return

        send_intent("overlay.close")
        self.running = False


def run_delayed_action_cli(argv: list[str]) -> int:
    # argv: [--delayed-action, action, fade_ms, powertoys_exe, crop_lock_mode, auto_launch, target_hwnd, additional_sleep_ms]
    action = argv[1] if len(argv) > 1 else ""
    try:
        fade_ms = max(0, min(5000, int(argv[2]))) if len(argv) > 2 else 250
    except Exception:
        fade_ms = 250
    powertoys_exe = argv[3] if len(argv) > 3 else ""
    crop_lock_mode = argv[4] if len(argv) > 4 else "screenshot"
    auto_launch = (argv[5] == "1") if len(argv) > 5 else True
    try:
        target_hwnd = int(argv[6]) if len(argv) > 6 else 0
    except Exception:
        target_hwnd = 0
    try:
        additional_sleep_ms = max(0, min(5000, int(argv[7]))) if len(argv) > 7 else 100
    except Exception:
        additional_sleep_ms = 100

    if fade_ms > 0:
        time.sleep(fade_ms / 1000.0)

    execute_powertoys_action(action, powertoys_exe, crop_lock_mode, auto_launch, target_hwnd, additional_sleep_ms)
    return 0


def main() -> None:
    if len(sys.argv) >= 2 and sys.argv[1] == "--delayed-action":
        raise SystemExit(run_delayed_action_cli(sys.argv[1:]))

    _configure_stdio()
    plugin = PowerToysShortcutsPlugin()
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
