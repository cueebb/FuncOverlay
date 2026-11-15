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
from datetime import datetime
import json
import os
import shutil
import subprocess
import sys
import threading
import time
import traceback
from pathlib import Path

LOG_FILE_PATH = Path(__file__).with_name("powertoys_debug.log")
LOG_MAX_BYTES = 65536
LOG_TRIM_HEADER = "--- log trimmed ---\n"
_log_lock = threading.Lock()


def _log(msg: str) -> None:
    """Append a timestamped diagnostic line to powertoys_debug.log (capped at 64 KB)."""
    ts = datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3]
    line = f"[{ts}] [pid={os.getpid()}] {msg}\n"
    with _log_lock:
        try:
            line_bytes = line.encode("utf-8", errors="replace")
            if len(line_bytes) > LOG_MAX_BYTES:
                line_bytes = line_bytes[-(LOG_MAX_BYTES - 1):] + b"\n"

            existing = LOG_FILE_PATH.read_bytes() if LOG_FILE_PATH.exists() else b""
            if len(existing) + len(line_bytes) > LOG_MAX_BYTES:
                header_bytes = LOG_TRIM_HEADER.encode("utf-8")
                keep_budget = max(0, LOG_MAX_BYTES - len(header_bytes) - len(line_bytes))
                tail = existing[-keep_budget:] if keep_budget > 0 else b""
                nl_idx = tail.find(b"\n")
                if nl_idx != -1:
                    tail = tail[nl_idx + 1:]
                LOG_FILE_PATH.write_bytes(header_bytes + tail + line_bytes)
            else:
                with LOG_FILE_PATH.open("ab") as f:
                    f.write(line_bytes)
        except Exception:
            pass

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


def _unwrap_powertoys_prop(obj: object) -> dict | None:
    """Unwrap PowerToys' optional {'value': {...}} property wrapper."""
    cur = obj
    for _ in range(3):
        if not isinstance(cur, dict):
            return None
        if any(k in cur for k in ("code", "win", "ctrl", "alt", "shift", "key")):
            return cur
        if "value" in cur and isinstance(cur["value"], dict):
            cur = cur["value"]
            continue
        return None
    return None


def _vk_from_key_label(label: object) -> int:
    s = str(label or "").strip().upper()
    if len(s) == 1 and ("A" <= s <= "Z" or "0" <= s <= "9"):
        return ord(s)
    return 0


def _read_powertoys_hotkey(
    module_dirs: list[str],
    property_names: list[str],
    fallback: dict,
    action_label: str = "",
) -> dict:
    """
    Read user-customized shortcut from PowerToys module settings.json.
    Handles PowerToys' {'value': {...}} wrapper, PascalCase/snake_case keys, and key->VK fallback.
    """
    root = _powertoys_config_root()
    wanted_lower = [p.lower() for p in property_names]

    for mod in module_dirs:
        cfg_file = root / mod / "settings.json"
        if not cfg_file.is_file():
            continue
        try:
            data = json.loads(cfg_file.read_text(encoding="utf-8-sig"))
        except Exception as ex:
            _log(f"HotkeyConfig[{action_label}] parse failed for {cfg_file}: {ex}")
            continue

        props = None
        if isinstance(data, dict):
            props = data.get("properties")
            if not isinstance(props, dict):
                props = data.get("Properties")
        if not isinstance(props, dict):
            _log(f"HotkeyConfig[{action_label}] no properties dict in {cfg_file} keys={list(data.keys()) if isinstance(data, dict) else type(data).__name__}")
            continue

        props_by_lower = {str(k).lower(): (k, v) for k, v in props.items()}
        for candidate_lower in wanted_lower:
            if candidate_lower not in props_by_lower:
                continue
            orig_key, raw_entry = props_by_lower[candidate_lower]
            hk = _unwrap_powertoys_prop(raw_entry)
            if not isinstance(hk, dict):
                _log(f"HotkeyConfig[{action_label}] prop '{orig_key}' in {cfg_file} not hotkey dict: {raw_entry!r}")
                continue
            code = int(hk.get("code") or 0)
            if code <= 0:
                code = _vk_from_key_label(hk.get("key"))
            if code > 0:
                parsed = {
                    "win": bool(hk.get("win", False)),
                    "ctrl": bool(hk.get("ctrl", False)),
                    "alt": bool(hk.get("alt", False)),
                    "shift": bool(hk.get("shift", False)),
                    "code": code,
                }
                _log(f"HotkeyConfig[{action_label}] loaded {cfg_file} prop='{orig_key}' raw={raw_entry!r} -> {parsed}")
                return parsed

        _log(
            f"HotkeyConfig[{action_label}] {cfg_file} props={list(props.keys())} "
            f"none matched {property_names} -> fallback={fallback}"
        )
        return dict(fallback)

    _log(f"HotkeyConfig[{action_label}] no settings.json under {[str(root / m) for m in module_dirs]} -> fallback={fallback}")
    return dict(fallback)


def resolve_hotkey_for_action(action: str, crop_lock_mode: str = "screenshot") -> dict | None:
    if action == "color_picker":
        return _read_powertoys_hotkey(
            ["ColorPicker"],
            ["ActivationShortcut", "activation_shortcut", "Hotkey", "hotkey"],
            DEFAULT_SHORTCUTS["color_picker"],
            action_label="color_picker",
        )
    if action == "text_extractor":
        return _read_powertoys_hotkey(
            ["TextExtractor"],
            ["ActivationShortcut", "activation_shortcut", "Hotkey", "hotkey"],
            DEFAULT_SHORTCUTS["text_extractor"],
            action_label="text_extractor",
        )
    if action == "always_on_top":
        return _read_powertoys_hotkey(
            ["AlwaysOnTop"],
            ["hotkey", "Hotkey", "activation_shortcut", "ActivationShortcut"],
            DEFAULT_SHORTCUTS["always_on_top"],
            action_label="always_on_top",
        )
    if action == "screen_ruler":
        return _read_powertoys_hotkey(
            ["Measure Tool", "MeasureTool", "ScreenRuler"],
            ["ActivationShortcut", "activation_shortcut", "Hotkey", "hotkey"],
            DEFAULT_SHORTCUTS["screen_ruler"],
            action_label="screen_ruler",
        )
    if action == "crop_and_lock":
        mode = (crop_lock_mode or "screenshot").strip().lower()
        if mode == "thumbnail":
            return _read_powertoys_hotkey(
                ["CropAndLock"],
                ["thumbnail-hotkey", "thumbnail_hotkey", "ThumbnailHotkey"],
                DEFAULT_SHORTCUTS["crop_and_lock_thumbnail"],
                action_label="crop_and_lock_thumbnail",
            )
        if mode == "reparent":
            return _read_powertoys_hotkey(
                ["CropAndLock"],
                ["reparent-hotkey", "reparent_hotkey", "ReparentHotkey"],
                DEFAULT_SHORTCUTS["crop_and_lock_reparent"],
                action_label="crop_and_lock_reparent",
            )
        return _read_powertoys_hotkey(
            ["CropAndLock"],
            ["screenshot-hotkey", "screenshot_hotkey", "ScreenshotHotkey"],
            DEFAULT_SHORTCUTS["crop_and_lock_screenshot"],
            action_label="crop_and_lock_screenshot",
        )
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


def _snapshot_pid_exe_map() -> dict[int, str]:
    """Return {pid: exe_name_lower} for all running Windows processes."""
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

    pid_map: dict[int, str] = {}
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if not snap or snap == INVALID_HANDLE_VALUE:
        return pid_map

    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(PROCESSENTRY32W)
        ok = kernel32.Process32FirstW(snap, ctypes.byref(entry))
        while ok:
            pid_map[int(entry.th32ProcessID)] = (entry.szExeFile or "").strip().lower()
            ok = kernel32.Process32NextW(snap, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snap)
    return pid_map


def _find_pids_by_exe_names(names: set[str]) -> set[int]:
    pid_map = _snapshot_pid_exe_map()
    return {pid for pid, exe in pid_map.items() if exe in names}


def _is_powertoys_running() -> bool:
    return bool(_find_pids_by_exe_names({"powertoys.exe"}))


def _query_pid_elevation_info(pid: int) -> tuple[bool | None, str]:
    """Return (is_elevated, integrity_label) for a Windows PID; handles UIPI access-denied cleanly."""
    if os.name != "nt" or pid <= 0:
        return None, "n/a"

    from ctypes import wintypes

    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    TOKEN_QUERY = 0x0008
    TokenElevation = 20
    TokenIntegrityLevel = 25

    class SID_AND_ATTRIBUTES(ctypes.Structure):
        _fields_ = [("Sid", ctypes.c_void_p), ("Attributes", wintypes.DWORD)]

    class TOKEN_MANDATORY_LABEL(ctypes.Structure):
        _fields_ = [("Label", SID_AND_ATTRIBUTES)]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)

    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL

    advapi32.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    advapi32.OpenProcessToken.restype = wintypes.BOOL
    advapi32.GetTokenInformation.argtypes = [
        wintypes.HANDLE,
        ctypes.c_int,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
    ]
    advapi32.GetTokenInformation.restype = wintypes.BOOL
    advapi32.GetSidSubAuthorityCount.argtypes = [ctypes.c_void_p]
    advapi32.GetSidSubAuthorityCount.restype = ctypes.POINTER(ctypes.c_ubyte)
    advapi32.GetSidSubAuthority.argtypes = [ctypes.c_void_p, wintypes.DWORD]
    advapi32.GetSidSubAuthority.restype = ctypes.POINTER(wintypes.DWORD)

    hproc = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not hproc:
        err = ctypes.get_last_error()
        if err == 5:
            return True, "High/Protected(AccessDenied)"
        return None, f"openErr={err}"

    htok = wintypes.HANDLE()
    try:
        if not advapi32.OpenProcessToken(hproc, TOKEN_QUERY, ctypes.byref(htok)):
            err = ctypes.get_last_error()
            if err == 5:
                return True, "High(TokAccessDenied)"
            return None, f"tokErr={err}"

        elevated = wintypes.DWORD(0)
        ret_len = wintypes.DWORD(0)
        elev_ok = bool(
            advapi32.GetTokenInformation(
                htok, TokenElevation, ctypes.byref(elevated), ctypes.sizeof(elevated), ctypes.byref(ret_len)
            )
        )
        is_elev = bool(elevated.value) if elev_ok else None

        needed = wintypes.DWORD(0)
        advapi32.GetTokenInformation(htok, TokenIntegrityLevel, None, 0, ctypes.byref(needed))
        integrity = "unknown"
        if needed.value > 0:
            buf = (ctypes.c_ubyte * needed.value)()
            if advapi32.GetTokenInformation(htok, TokenIntegrityLevel, buf, needed.value, ctypes.byref(needed)):
                tml = ctypes.cast(buf, ctypes.POINTER(TOKEN_MANDATORY_LABEL)).contents
                cnt_ptr = advapi32.GetSidSubAuthorityCount(tml.Label.Sid)
                if cnt_ptr and cnt_ptr[0] > 0:
                    rid = int(advapi32.GetSidSubAuthority(tml.Label.Sid, cnt_ptr[0] - 1)[0])
                    if rid < 0x1000:
                        integrity = f"Untrusted(0x{rid:X})"
                    elif rid < 0x2000:
                        integrity = f"Low(0x{rid:X})"
                    elif rid < 0x3000:
                        integrity = f"Medium(0x{rid:X})"
                    elif rid < 0x4000:
                        integrity = f"High(0x{rid:X})"
                    else:
                        integrity = f"System(0x{rid:X})"
        return is_elev, integrity
    finally:
        if htok:
            kernel32.CloseHandle(htok)
        kernel32.CloseHandle(hproc)


def _log_powertoys_environment(action: str) -> None:
    """Log PowerToys running module processes, elevation/UIPI match, and enabled settings."""
    if os.name != "nt":
        return

    my_elev, my_int = _query_pid_elevation_info(os.getpid())
    pid_map = _snapshot_pid_exe_map()
    pt_procs: list[str] = []
    uipi_risk = False
    for pid, exe in sorted(pid_map.items(), key=lambda x: x[1]):
        if "powertoys" in exe or exe == "funcoverlay.exe":
            elev, integ = _query_pid_elevation_info(pid)
            pt_procs.append(f"{exe}#{pid}[elev={elev},{integ}]")
            if "powertoys" in exe and (elev is True and not my_elev):
                uipi_risk = True

    global_cfg = _powertoys_config_root() / "settings.json"
    enabled_summary = "missing"
    if global_cfg.is_file():
        try:
            gdata = json.loads(global_cfg.read_text(encoding="utf-8-sig"))
            enabled = gdata.get("enabled") if isinstance(gdata, dict) else None
            if isinstance(enabled, dict):
                keys_of_interest = [
                    "AlwaysOnTop",
                    "ColorPicker",
                    "CropAndLock",
                    "Measure Tool",
                    "TextExtractor",
                ]
                parts = [f"{k}={enabled.get(k)}" for k in keys_of_interest if k in enabled]
                enabled_summary = ", ".join(parts) if parts else f"keys={list(enabled.keys())[:10]}"
        except Exception as ex:
            enabled_summary = f"err={ex}"

    _log(
        f"Env[{action}]: worker[elev={my_elev},{my_int}] "
        f"enabled({enabled_summary}) procs=[{'; '.join(pt_procs)}]"
    )
    if uipi_risk:
        _log(
            "WARNING UIPI_BLOCKED: One or more PowerToys processes are running Elevated (Admin/High) "
            "while plugin worker is Non-Elevated (Medium). Windows silently drops SendInput across UIPI!"
        )


def _log_input_state(label: str) -> None:
    """Log active keyboard layout and async key states for Win/Ctrl/Shift/Alt."""
    if os.name != "nt":
        return
    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.GetForegroundWindow.argtypes = []
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetKeyboardLayout.argtypes = [wintypes.DWORD]
    user32.GetKeyboardLayout.restype = wintypes.HKL
    user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
    user32.GetAsyncKeyState.restype = ctypes.c_short

    fg = user32.GetForegroundWindow()
    tid = user32.GetWindowThreadProcessId(fg, None) if fg else 0
    hkl = int(user32.GetKeyboardLayout(tid) or 0)
    checks = {
        "LWin": 0x5B,
        "RWin": 0x5C,
        "Ctrl": 0x11,
        "Shift": 0x10,
        "Alt": 0x12,
    }
    down = [name for name, vk in checks.items() if (user32.GetAsyncKeyState(vk) & 0x8000)]
    _log(f"KeyState[{label}]: fg_tid={tid} hkl=0x{hkl:08X} mods_down={down}")


IGNORED_SHELL_CLASSES = {
    "progman",
    "workerw",
    "shell_traywnd",
    "shell_secondarytraywnd",
    "windows.ui.core.corewindow",
    "multitaskingviewframe",
    "tasklistthumbnailwnd",
    "foregroundstaging",
    "xaml_windowedpopupclass",
    "notifyiconoverflowwindow",
}


def _describe_hwnd(hwnd: int, pid_map: dict[int, str] | None = None) -> str:
    if os.name != "nt":
        return f"hwnd={hwnd}"
    if not hwnd:
        return "hwnd=0x0 (NULL)"

    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.IsWindow.argtypes = [wintypes.HWND]
    user32.IsWindow.restype = wintypes.BOOL
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.IsWindowEnabled.argtypes = [wintypes.HWND]
    user32.IsWindowEnabled.restype = wintypes.BOOL
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.IsIconic.restype = wintypes.BOOL
    user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetWindow.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.GetWindowLongW.restype = wintypes.LONG
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetClassNameW.restype = ctypes.c_int
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.restype = ctypes.c_int
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL

    if not user32.IsWindow(hwnd):
        return f"hwnd=0x{hwnd:X} (INVALID)"

    pid = wintypes.DWORD(0)
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    pid_val = int(pid.value)
    if pid_map is None:
        pid_map = _snapshot_pid_exe_map()
    exe_name = pid_map.get(pid_val, "?")

    cls_buf = ctypes.create_unicode_buffer(128)
    user32.GetClassNameW(hwnd, cls_buf, 128)
    title_buf = ctypes.create_unicode_buffer(128)
    user32.GetWindowTextW(hwnd, title_buf, 128)
    title = title_buf.value.replace("\n", " ").replace("\r", " ")[:48]

    ex_style = int(user32.GetWindowLongW(hwnd, -20)) & 0xFFFFFFFF
    vis = int(bool(user32.IsWindowVisible(hwnd)))
    ena = int(bool(user32.IsWindowEnabled(hwnd)))
    ico = int(bool(user32.IsIconic(hwnd)))
    own = int(user32.GetWindow(hwnd, 4) or 0)
    rect = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    return (
        f"hwnd=0x{hwnd:X} pid={pid_val}({exe_name}) cls='{cls_buf.value}' "
        f"title='{title}' vis={vis} ena={ena} ico={ico} owner=0x{own:X} "
        f"ex=0x{ex_style:X} rect=({rect.left},{rect.top},{rect.right},{rect.bottom})"
    )


def _log_foreground_state(label: str, pid_map: dict[int, str] | None = None) -> int:
    if os.name != "nt":
        return 0
    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.GetForegroundWindow.argtypes = []
    user32.GetForegroundWindow.restype = wintypes.HWND
    fg = int(user32.GetForegroundWindow() or 0)
    _log(f"FG[{label}]: {_describe_hwnd(fg, pid_map)}")
    return fg


def _find_last_active_app_hwnd(log_label: str = "zorder") -> int:
    """Walk Windows Z-order from top to bottom and return the top visible non-FuncOverlay app window."""
    if os.name != "nt":
        return 0

    from ctypes import wintypes

    GW_HWNDNEXT = 2
    GW_OWNER = 4
    GWL_EXSTYLE = -20
    WS_EX_TOOLWINDOW = 0x00000080
    WS_EX_TRANSPARENT = 0x00000020
    WS_EX_NOACTIVATE = 0x08000000
    DWMWA_CLOAKED = 14

    pid_map = _snapshot_pid_exe_map()
    func_pids = {pid for pid, exe in pid_map.items() if exe == "funcoverlay.exe"}

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetWindow.restype = wintypes.HWND
    user32.GetTopWindow.argtypes = [wintypes.HWND]
    user32.GetTopWindow.restype = wintypes.HWND
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.IsWindowEnabled.argtypes = [wintypes.HWND]
    user32.IsWindowEnabled.restype = wintypes.BOOL
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

    logged = 0
    hwnd = user32.GetTopWindow(None)
    while hwnd:
        try:
            if user32.IsWindowVisible(hwnd):
                pid = wintypes.DWORD(0)
                user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
                pid_val = int(pid.value)
                ex_style = int(user32.GetWindowLongW(hwnd, GWL_EXSTYLE)) & 0xFFFFFFFF
                cls_buf = ctypes.create_unicode_buffer(256)
                user32.GetClassNameW(hwnd, cls_buf, 256)
                cls_lower = cls_buf.value.strip().lower()
                rect = wintypes.RECT()
                user32.GetWindowRect(hwnd, ctypes.byref(rect))
                w = rect.right - rect.left
                h = rect.bottom - rect.top
                owner = int(user32.GetWindow(hwnd, GW_OWNER) or 0)
                enabled = bool(user32.IsWindowEnabled(hwnd))
                iconic = bool(user32.IsIconic(hwnd))

                cloaked = ctypes.c_int(0)
                if dwmapi is not None:
                    dwmapi.DwmGetWindowAttribute(
                        hwnd, DWMWA_CLOAKED, ctypes.byref(cloaked), ctypes.sizeof(cloaked)
                    )

                skip_reason = ""
                if pid_val in func_pids:
                    skip_reason = "funcoverlay_pid"
                elif not enabled:
                    skip_reason = "disabled"
                elif iconic:
                    skip_reason = "minimized"
                elif owner:
                    skip_reason = "has_owner"
                elif ex_style & WS_EX_TOOLWINDOW:
                    skip_reason = "toolwindow"
                elif ex_style & WS_EX_NOACTIVATE:
                    skip_reason = "noactivate"
                elif ex_style & WS_EX_TRANSPARENT:
                    skip_reason = "transparent"
                elif cls_lower in IGNORED_SHELL_CLASSES:
                    skip_reason = "shell_class"
                elif cloaked.value != 0:
                    skip_reason = f"cloaked={cloaked.value}"
                elif w < 80 or h < 60:
                    skip_reason = f"too_small({w}x{h})"

                if log_label and logged < 8:
                    status = "SELECTED" if not skip_reason else f"SKIP({skip_reason})"
                    _log(f"ZSCAN[{log_label}] #{logged} {status}: {_describe_hwnd(int(hwnd), pid_map)}")
                    logged += 1

                if not skip_reason:
                    return int(hwnd)
        except Exception as ex:
            if log_label:
                _log(f"ZSCAN[{log_label}] error: {ex}")
        hwnd = user32.GetWindow(hwnd, GW_HWNDNEXT)
    return 0


def _is_overlay_still_visible(func_pids: set[int]) -> bool:
    """Check whether FuncOverlay's main overlay window (not SettingsWindow) is still visible."""
    if os.name != "nt" or not func_pids:
        return False
    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [WNDENUMPROC, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.restype = ctypes.c_int

    still_visible = False

    def _cb(hwnd: int, _lp: int) -> bool:
        nonlocal still_visible
        pid = wintypes.DWORD(0)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if int(pid.value) in func_pids and user32.IsWindowVisible(hwnd):
            tbuf = ctypes.create_unicode_buffer(128)
            user32.GetWindowTextW(hwnd, tbuf, 128)
            if tbuf.value.strip().lower() == "funcoverlay":
                still_visible = True
                return False
        return True

    cb = WNDENUMPROC(_cb)
    user32.EnumWindows(cb, 0)
    return still_visible


def _wait_until_funcoverlay_hidden(max_extra_ms: int = 500) -> None:
    """Wait until the main 'FuncOverlay' window collapses AND is no longer foreground (ignoring SettingsWindow)."""
    if os.name != "nt":
        return
    from ctypes import wintypes

    func_pids = _find_pids_by_exe_names({"funcoverlay.exe"})
    if not func_pids:
        _log("WaitHidden: no funcoverlay.exe PID found")
        return

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.GetForegroundWindow.argtypes = []
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.restype = ctypes.c_int

    t0 = time.monotonic()
    deadline = t0 + (max_extra_ms / 1000.0)
    overlay_vis = True
    fg_is_overlay = True
    while time.monotonic() < deadline:
        overlay_vis = _is_overlay_still_visible(func_pids)
        fg = user32.GetForegroundWindow()
        fg_is_overlay = False
        if fg:
            pid = wintypes.DWORD(0)
            user32.GetWindowThreadProcessId(fg, ctypes.byref(pid))
            if int(pid.value) in func_pids:
                tbuf = ctypes.create_unicode_buffer(128)
                user32.GetWindowTextW(fg, tbuf, 128)
                fg_is_overlay = (tbuf.value.strip().lower() == "funcoverlay")

        if not overlay_vis and not fg_is_overlay:
            break
        time.sleep(0.015)

    elapsed_ms = int((time.monotonic() - t0) * 1000)
    _log(f"WaitHidden done in {elapsed_ms}ms: overlay_vis={overlay_vis} fg_is_overlay={fg_is_overlay}")


def _build_sendinput_types():
    from ctypes import wintypes

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

    return INPUT, KEYBDINPUT


def _send_vk_sequence(steps: list[tuple[int, bool, bool]]) -> int:
    """Send a sequence of (vk, extended, key_up) keyboard inputs via Win32 SendInput."""
    if os.name != "nt" or not steps:
        return 0
    from ctypes import wintypes

    INPUT, KEYBDINPUT = _build_sendinput_types()
    INPUT_KEYBOARD = 1
    KEYEVENTF_EXTENDEDKEY = 0x0001
    KEYEVENTF_KEYUP = 0x0002

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.MapVirtualKeyW.argtypes = [wintypes.UINT, wintypes.UINT]
    user32.MapVirtualKeyW.restype = wintypes.UINT
    user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
    user32.SendInput.restype = wintypes.UINT

    seq: list = []
    for vk, extended, key_up in steps:
        scan_ex = user32.MapVirtualKeyW(vk, 4)  # MAPVK_VK_TO_VSC_EX
        scan = (scan_ex & 0xFF) if scan_ex else (user32.MapVirtualKeyW(vk, 0) & 0xFF)
        is_ext = extended or (((scan_ex >> 8) & 0xFF) in (0xE0, 0xE1))
        flags = 0
        if is_ext:
            flags |= KEYEVENTF_EXTENDEDKEY
        if key_up:
            flags |= KEYEVENTF_KEYUP
        inp = INPUT()
        inp.type = INPUT_KEYBOARD
        inp.ki = KEYBDINPUT(wVk=vk, wScan=scan, dwFlags=flags, time=0, dwExtraInfo=0)
        seq.append(inp)

    arr = (INPUT * len(seq))(*seq)
    sent = int(user32.SendInput(len(seq), arr, ctypes.sizeof(INPUT)))
    if sent != len(seq):
        _log(f"SendInput partial/failed: sent={sent}/{len(seq)} err={ctypes.get_last_error()}")
    return sent


def _focus_hwnd(hwnd: int) -> bool:
    """Focus hwnd using Alt-unlock, AttachThreadInput (fg/target), SetForegroundWindow, SwitchToThisWindow."""
    if os.name != "nt" or not hwnd:
        _log(f"FocusHwnd skipped: os={os.name} hwnd={hwnd}")
        return False

    from ctypes import wintypes

    ASFW_ANY = 0xFFFFFFFF
    VK_LMENU = 0xA4

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
    user32.SetActiveWindow.argtypes = [wintypes.HWND]
    user32.SetActiveWindow.restype = wintypes.HWND
    user32.SetFocus.argtypes = [wintypes.HWND]
    user32.SetFocus.restype = wintypes.HWND
    user32.SwitchToThisWindow.argtypes = [wintypes.HWND, wintypes.BOOL]
    user32.SwitchToThisWindow.restype = None
    user32.AllowSetForegroundWindow.argtypes = [wintypes.DWORD]
    user32.AllowSetForegroundWindow.restype = wintypes.BOOL
    kernel32.GetCurrentThreadId.argtypes = []
    kernel32.GetCurrentThreadId.restype = wintypes.DWORD

    if not user32.IsWindow(hwnd) or not user32.IsWindowVisible(hwnd):
        _log(f"FocusHwnd invalid/invisible target: 0x{hwnd:X}")
        return False

    fg = int(user32.GetForegroundWindow() or 0)
    if fg == hwnd:
        _log(f"FocusHwnd target 0x{hwnd:X} is already foreground")
        return True

    asfw_ok = bool(user32.AllowSetForegroundWindow(ASFW_ANY))
    _send_vk_sequence([(VK_LMENU, False, False)])

    cur_tid = kernel32.GetCurrentThreadId()
    fg_tid = user32.GetWindowThreadProcessId(fg, None) if fg else 0
    tgt_tid = user32.GetWindowThreadProcessId(hwnd, None)

    attached_tids: list[int] = []
    try:
        for tid in (fg_tid, tgt_tid):
            if tid and tid != cur_tid and tid not in attached_tids:
                if user32.AttachThreadInput(cur_tid, tid, True):
                    attached_tids.append(tid)

        bwt_ok = bool(user32.BringWindowToTop(hwnd))
        set_fg_ok = bool(user32.SetForegroundWindow(hwnd))
        fg_err = ctypes.get_last_error() if not set_fg_ok else 0
        user32.SetActiveWindow(hwnd)
        user32.SetFocus(hwnd)
        if not set_fg_ok:
            user32.SwitchToThisWindow(hwnd, True)
    finally:
        for tid in reversed(attached_tids):
            user32.AttachThreadInput(cur_tid, tid, False)
        _send_vk_sequence([(VK_LMENU, False, True)])

    new_fg = int(user32.GetForegroundWindow() or 0)
    _log(
        f"FocusHwnd(0x{hwnd:X}): asfw={asfw_ok} cur_tid={cur_tid} fg_tid={fg_tid} "
        f"tgt_tid={tgt_tid} attached={attached_tids} bwt={bwt_ok} "
        f"setFg={set_fg_ok}(err={fg_err}) new_fg=0x{new_fg:X}"
    )
    return True


def _send_hotkey_combo(hotkey: dict) -> bool:
    """Send a PowerToys global hotkey via Win32 SendInput."""
    if os.name != "nt" or not hotkey:
        _log(f"SendHotkey skipped: os={os.name} hotkey={hotkey}")
        return False

    VK_LWIN = 0x5B
    VK_LCONTROL = 0xA2
    VK_LSHIFT = 0xA0
    VK_LMENU = 0xA4

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
        _log(f"SendHotkey invalid vk={target_vk}")
        return False

    _log_input_state("before_send")

    sequence: list[tuple[int, bool, bool]] = []
    for vk, ext in mods:
        sequence.append((vk, ext, False))
    sequence.append((target_vk, False, False))
    sequence.append((target_vk, False, True))
    for vk, ext in reversed(mods):
        sequence.append((vk, ext, True))

    sent = _send_vk_sequence(sequence)
    _log(f"SendHotkey BATCH hotkey={hotkey} mods={[hex(m[0]) for m in mods]} sent={sent}/{len(sequence)}")
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
) -> None:
    """Ensure PowerToys is running, restore focus to target_hwnd for AOT/CAL, and send hotkey."""
    try:
        pt_running = _is_powertoys_running() if os.name == "nt" else False
        _log(
            f"Worker start: action={action} target_hwnd=0x{target_hwnd:X} "
            f"pt_running={pt_running} auto_launch={auto_launch}"
        )
        _log_powertoys_environment(action)

        if os.name == "nt" and auto_launch and not pt_running:
            resolved_exe = resolve_powertoys_exe_path(powertoys_exe)
            _log(f"Auto-launching PowerToys: {resolved_exe}")
            if os.path.isfile(resolved_exe):
                _launch_process_detached([resolved_exe])
                for _ in range(25):
                    time.sleep(0.1)
                    if _is_powertoys_running():
                        time.sleep(0.35)
                        break

        _wait_until_funcoverlay_hidden(max_extra_ms=500)
        _log_foreground_state("post_wait_hidden")

        if action in ("always_on_top", "crop_and_lock"):
            hwnd = target_hwnd or _find_last_active_app_hwnd(log_label="aot_cal_fallback")
            if hwnd:
                _focus_hwnd(hwnd)
                time.sleep(0.04)
            _log_foreground_state("pre_hotkey")

        hotkey = resolve_hotkey_for_action(action, crop_lock_mode)
        if hotkey:
            _send_hotkey_combo(hotkey)
        else:
            _log(f"No hotkey resolved for action={action} mode={crop_lock_mode}")
    except Exception:
        _log(f"Worker exception:\n{traceback.format_exc()}")


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
        powertoys_exe = str(self.settings.get("powertoys_exe_path") or r"C:\Program Files\PowerToys\PowerToys.exe")
        crop_lock_mode = str(self.settings.get("crop_lock_mode") or "screenshot")
        auto_launch = bool(self.settings.get("auto_launch_powertoys", True))

        _log(f"=== ACTION action={action} fade_ms={fade_ms} ===")
        target_hwnd = 0
        if action in ("always_on_top", "crop_and_lock"):
            _log_foreground_state("click_time")
            target_hwnd = _find_last_active_app_hwnd(log_label="aot_cal_click")

        _log(f"Click capture complete: target_hwnd=0x{target_hwnd:X}")

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
        ]
        try:
            _launch_process_detached(helper_cmd)
        except Exception:
            _log(f"Detached spawn failed:\n{traceback.format_exc()}")
            send_intent("overlay.close")
            execute_powertoys_action(action, powertoys_exe, crop_lock_mode, auto_launch, target_hwnd)
            self.running = False
            return

        send_intent("overlay.close")
        self.running = False


def run_delayed_action_cli(argv: list[str]) -> int:
    # argv: [--delayed-action, action, fade_ms, powertoys_exe, crop_lock_mode, auto_launch, target_hwnd]
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

    _log(f"Delayed worker invoked: action={action} fade_ms={fade_ms} target=0x{target_hwnd:X}")
    if fade_ms > 0:
        time.sleep(fade_ms / 1000.0)

    execute_powertoys_action(action, powertoys_exe, crop_lock_mode, auto_launch, target_hwnd)
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
