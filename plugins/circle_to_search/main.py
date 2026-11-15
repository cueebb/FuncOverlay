#!/usr/bin/env python3
"""
circle to search - FuncOverlay Python Plugin
- Runs ONLY while FuncOverlay overlay is open (zero background execution when closed).
- Search bar UI uses FuncOverlay's generic 'search_bar' layout with:
  1. Text Search button (opens query in a new tab of the system default browser: Firefox, Chrome, Edge, etc.)
  2. Photo Search button (closes overlay cleanly after FadeDurationMs, shows a borderless desktop region
     selector, and on LMB release uploads the cropped region to Google Lens in a new browser tab).
"""

import base64
import ctypes
import json
import os
import re
import struct
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import webbrowser
import zlib
from pathlib import Path

ICON_PATHS = {
    "search": [
        "M10.5,4 A6.5,6.5 0 1 0 10.5,17 A6.5,6.5 0 1 0 10.5,4 Z",
        "M15.3,15.3 L20,20",
    ],
    "photo_search": [
        "M4,8.5 V6.5 A2.5,2.5 0 0 1 6.5,4 H8.5 M15.5,4 H17.5 A2.5,2.5 0 0 1 20,6.5 V8.5",
        "M4,15.5 V17.5 A2.5,2.5 0 0 0 6.5,20 H8.5 M15.5,20 H17.5 A2.5,2.5 0 0 0 20,17.5 V15.5",
        "M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 Z",
        "M16.8,16.8 L16.85,16.8",
    ],
}

_FIREFOX_FAMILY_EXES = {
    "firefox.exe",
    "librewolf.exe",
    "floorp.exe",
    "waterfox.exe",
    "zen.exe",
    "palemoon.exe",
}

_CHROMIUM_EXES = {
    "chrome.exe",
    "msedge.exe",
    "brave.exe",
    "opera.exe",
    "vivaldi.exe",
    "browser.exe",
    "chromium.exe",
    "arc.exe",
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


def _split_win_cmdline(cmdline: str) -> list[str]:
    """Parse a Windows shell command template string into argv tokens cleanly."""
    tokens: list[str] = []
    cur: list[str] = []
    in_quotes = False
    i = 0
    n = len(cmdline)
    while i < n:
        ch = cmdline[i]
        if ch == '"':
            in_quotes = not in_quotes
        elif ch.isspace() and not in_quotes:
            if cur:
                tokens.append("".join(cur))
                cur.clear()
        else:
            cur.append(ch)
        i += 1
    if cur:
        tokens.append("".join(cur))
    return tokens


def _get_default_https_browser_command() -> tuple[str, list[str]]:
    """Inspect Windows UserChoice for HTTPS and return (prog_id, argv_tokens)."""
    if os.name != "nt":
        return "", []
    try:
        import winreg

        prog_id = ""
        for scheme in ("https", "http"):
            try:
                subkey = rf"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice"
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, subkey) as key:
                    val, _ = winreg.QueryValueEx(key, "ProgId")
                    if val:
                        prog_id = str(val).strip()
                        break
            except OSError:
                continue

        if not prog_id:
            return "", []

        cmd_str = ""
        for root, path in (
            (winreg.HKEY_CURRENT_USER, rf"Software\Classes\{prog_id}\shell\open\command"),
            (winreg.HKEY_CLASSES_ROOT, rf"{prog_id}\shell\open\command"),
            (winreg.HKEY_LOCAL_MACHINE, rf"SOFTWARE\Classes\{prog_id}\shell\open\command"),
        ):
            try:
                with winreg.OpenKey(root, path) as key:
                    val, _ = winreg.QueryValueEx(key, "")
                    if val:
                        cmd_str = os.path.expandvars(str(val).strip())
                        break
            except OSError:
                continue

        if not cmd_str:
            return prog_id, []

        tokens = _split_win_cmdline(cmd_str)
        return prog_id, tokens
    except Exception:
        return "", []


def open_url_in_new_tab(url: str) -> bool:
    """
    Open a URL in a NEW TAB of the system's default browser (never forcing a new window).
    Explicitly handles Firefox (-new-tab), Chromium browsers, and other default Windows browsers.
    Also safely opens file:/// URLs in the default HTTPS browser rather than a text editor.
    """
    if not url:
        return False

    if os.name == "nt":
        prog_id, tokens = _get_default_https_browser_command()
        exe_path = tokens[0] if tokens else ""
        exe_name = Path(exe_path).name.lower() if exe_path else ""
        prog_lower = prog_id.lower()

        # 1. Firefox / Gecko browsers: '-new-tab <url>' guarantees a tab in the active window
        if (exe_name in _FIREFOX_FAMILY_EXES or "firefoxurl" in prog_lower) and exe_path and os.path.isfile(exe_path):
            try:
                _launch_process_detached([exe_path, "-new-tab", url])
                return True
            except Exception:
                pass

        # 2. For standard http(s) URLs, Windows ShellExecuteW (os.startfile) opens a new tab
        #    cleanly in Chrome, Edge, Brave, Opera, Vivaldi, Firefox, etc. without touching CLI quoting.
        if url.lower().startswith(("http://", "https://")):
            try:
                os.startfile(url)  # type: ignore[attr-defined]
                return True
            except Exception:
                pass

        # 3. For file:/// fallback URLs (or if os.startfile failed), invoke the resolved HTTPS browser
        #    executable directly so .html files never open in Notepad/VS Code.
        if exe_path and os.path.isfile(exe_path):
            try:
                if exe_name in _CHROMIUM_EXES:
                    # Keep profile/single-argument flags if present, omit --new-window, replace %1
                    cleaned_args: list[str] = []
                    replaced = False
                    for tok in tokens[1:]:
                        if tok.lower() in ("--new-window", "-new-window"):
                            continue
                        if "%1" in tok:
                            cleaned_args.append(tok.replace("%1", url))
                            replaced = True
                        elif "%l" in tok.lower():
                            cleaned_args.append(re.sub(r"%[lL]", url, tok))
                            replaced = True
                        else:
                            cleaned_args.append(tok)
                    if not replaced:
                        cleaned_args.append(url)
                    _launch_process_detached([exe_path, *cleaned_args])
                    return True

                cmd_args: list[str] = []
                replaced = False
                for tok in tokens[1:]:
                    if "%1" in tok:
                        cmd_args.append(tok.replace("%1", url))
                        replaced = True
                    else:
                        cmd_args.append(tok)
                if not replaced:
                    cmd_args.append(url)
                _launch_process_detached([exe_path, *cmd_args])
                return True
            except Exception:
                pass

    try:
        return bool(webbrowser.open(url, new=2))
    except Exception:
        return False


def build_text_search_url(query: str, template: str) -> str:
    q = (query or "").strip()
    tmpl = (template or "").strip() or "https://www.google.com/search?q={query}"
    encoded = urllib.parse.quote_plus(q)
    if "{query}" in tmpl:
        return tmpl.replace("{query}", encoded)
    sep = "&" if "?" in tmpl else "?"
    return f"{tmpl}{sep}q={encoded}"


def encode_png(rgb_or_bgra: bytes, width: int, height: int, is_bgra: bool = True) -> bytes:
    """Encode raw pixel buffer (top-down RGB or BGRA) into valid PNG bytes using stdlib zlib."""
    if width <= 0 or height <= 0:
        raise ValueError("Invalid image dimensions")

    raw_rows: list[bytes] = []
    if is_bgra:
        stride = width * 4
        for y in range(height):
            row = rgb_or_bgra[y * stride : (y + 1) * stride]
            rgb = bytearray(width * 3)
            rgb[0::3] = row[2::4]
            rgb[1::3] = row[1::4]
            rgb[2::3] = row[0::4]
            raw_rows.append(b"\x00" + bytes(rgb))
    else:
        stride = width * 3
        for y in range(height):
            row = rgb_or_bgra[y * stride : (y + 1) * stride]
            raw_rows.append(b"\x00" + row)

    compressed = zlib.compress(b"".join(raw_rows), level=3)

    def _chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack("!I", len(data)) + tag + data + struct.pack("!I", crc)

    ihdr = struct.pack("!IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + _chunk(b"IHDR", ihdr)
        + _chunk(b"IDAT", compressed)
        + _chunk(b"IEND", b"")
    )


def _enable_per_monitor_dpi() -> None:
    if os.name != "nt":
        return
    try:
        user32 = ctypes.WinDLL("user32", use_last_error=True)
        if hasattr(user32, "SetProcessDpiAwarenessContext"):
            user32.SetProcessDpiAwarenessContext.argtypes = [ctypes.c_void_p]
            user32.SetProcessDpiAwarenessContext.restype = ctypes.c_int
            if user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4)):
                return
    except Exception:
        pass
    try:
        shcore = ctypes.WinDLL("shcore", use_last_error=True)
        shcore.SetProcessDpiAwareness(2)
        return
    except Exception:
        pass
    try:
        ctypes.WinDLL("user32", use_last_error=True).SetProcessDPIAware()
    except Exception:
        pass


def capture_and_select_region_png() -> bytes | None:
    """
    Freeze the virtual desktop across all monitors, display a dimmed borderless fullscreen
    selection overlay, let the user drag a bright rectangle with LMB, and return cropped PNG
    bytes upon LMB release (or None if cancelled with Esc / right-click / tiny drag).
    """
    if os.name != "nt":
        return None

    from ctypes import wintypes

    _enable_per_monitor_dpi()

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    msimg32 = None
    try:
        msimg32 = ctypes.WinDLL("msimg32", use_last_error=True)
    except Exception:
        pass

    LRESULT = ctypes.c_ssize_t
    WPARAM = ctypes.c_size_t
    LPARAM = ctypes.c_ssize_t
    HGDIOBJ = ctypes.c_void_p
    WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wintypes.HWND, wintypes.UINT, WPARAM, LPARAM)

    class BITMAPINFOHEADER(ctypes.Structure):
        _fields_ = [
            ("biSize", wintypes.DWORD),
            ("biWidth", wintypes.LONG),
            ("biHeight", wintypes.LONG),
            ("biPlanes", wintypes.WORD),
            ("biBitCount", wintypes.WORD),
            ("biCompression", wintypes.DWORD),
            ("biSizeImage", wintypes.DWORD),
            ("biXPelsPerMeter", wintypes.LONG),
            ("biYPelsPerMeter", wintypes.LONG),
            ("biClrUsed", wintypes.DWORD),
            ("biClrImportant", wintypes.DWORD),
        ]

    class BITMAPINFO(ctypes.Structure):
        _fields_ = [
            ("bmiHeader", BITMAPINFOHEADER),
            ("bmiColors", wintypes.DWORD * 3),
        ]

    class WNDCLASSEXW(ctypes.Structure):
        _fields_ = [
            ("cbSize", wintypes.UINT),
            ("style", wintypes.UINT),
            ("lpfnWndProc", WNDPROC),
            ("cbClsExtra", ctypes.c_int),
            ("cbWndExtra", ctypes.c_int),
            ("hInstance", wintypes.HINSTANCE),
            ("hIcon", wintypes.HICON),
            ("hCursor", wintypes.HANDLE),
            ("hbrBackground", wintypes.HBRUSH),
            ("lpszMenuName", wintypes.LPCWSTR),
            ("lpszClassName", wintypes.LPCWSTR),
            ("hIconSm", wintypes.HICON),
        ]

    class PAINTSTRUCT(ctypes.Structure):
        _fields_ = [
            ("hdc", wintypes.HDC),
            ("fErase", wintypes.BOOL),
            ("rcPaint", wintypes.RECT),
            ("fRestore", wintypes.BOOL),
            ("fIncUpdate", wintypes.BOOL),
            ("rgbReserved", ctypes.c_byte * 32),
        ]

    class BLENDFUNCTION(ctypes.Structure):
        _fields_ = [
            ("BlendOp", ctypes.c_ubyte),
            ("BlendFlags", ctypes.c_ubyte),
            ("SourceConstantAlpha", ctypes.c_ubyte),
            ("AlphaFormat", ctypes.c_ubyte),
        ]

    # Argtypes / Restypes (64-bit handle safety)
    kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
    kernel32.GetModuleHandleW.restype = wintypes.HINSTANCE

    user32.GetSystemMetrics.argtypes = [ctypes.c_int]
    user32.GetSystemMetrics.restype = ctypes.c_int
    user32.GetDC.argtypes = [wintypes.HWND]
    user32.GetDC.restype = wintypes.HDC
    user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
    user32.ReleaseDC.restype = ctypes.c_int
    user32.LoadCursorW.argtypes = [wintypes.HINSTANCE, ctypes.c_void_p]
    user32.LoadCursorW.restype = wintypes.HANDLE
    user32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEXW)]
    user32.RegisterClassExW.restype = wintypes.ATOM
    user32.UnregisterClassW.argtypes = [wintypes.LPCWSTR, wintypes.HINSTANCE]
    user32.UnregisterClassW.restype = wintypes.BOOL
    user32.CreateWindowExW.argtypes = [
        wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD,
        ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
        wintypes.HWND, wintypes.HMENU, wintypes.HINSTANCE, wintypes.LPVOID,
    ]
    user32.CreateWindowExW.restype = wintypes.HWND
    user32.DestroyWindow.argtypes = [wintypes.HWND]
    user32.DestroyWindow.restype = wintypes.BOOL
    user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.ShowWindow.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.SetFocus.argtypes = [wintypes.HWND]
    user32.SetFocus.restype = wintypes.HWND
    user32.SetCapture.argtypes = [wintypes.HWND]
    user32.SetCapture.restype = wintypes.HWND
    user32.ReleaseCapture.argtypes = []
    user32.ReleaseCapture.restype = wintypes.BOOL
    user32.InvalidateRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT), wintypes.BOOL]
    user32.InvalidateRect.restype = wintypes.BOOL
    user32.BeginPaint.argtypes = [wintypes.HWND, ctypes.POINTER(PAINTSTRUCT)]
    user32.BeginPaint.restype = wintypes.HDC
    user32.EndPaint.argtypes = [wintypes.HWND, ctypes.POINTER(PAINTSTRUCT)]
    user32.EndPaint.restype = wintypes.BOOL
    user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, WPARAM, LPARAM]
    user32.DefWindowProcW.restype = LRESULT
    user32.PostQuitMessage.argtypes = [ctypes.c_int]
    user32.PostQuitMessage.restype = None
    user32.GetMessageW.argtypes = [ctypes.POINTER(wintypes.MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT]
    user32.GetMessageW.restype = wintypes.BOOL
    user32.TranslateMessage.argtypes = [ctypes.POINTER(wintypes.MSG)]
    user32.TranslateMessage.restype = wintypes.BOOL
    user32.DispatchMessageW.argtypes = [ctypes.POINTER(wintypes.MSG)]
    user32.DispatchMessageW.restype = LRESULT
    user32.FillRect.argtypes = [wintypes.HDC, ctypes.POINTER(wintypes.RECT), wintypes.HBRUSH]
    user32.FillRect.restype = ctypes.c_int
    user32.DrawTextW.argtypes = [wintypes.HDC, wintypes.LPCWSTR, ctypes.c_int, ctypes.POINTER(wintypes.RECT), wintypes.UINT]
    user32.DrawTextW.restype = ctypes.c_int

    gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
    gdi32.CreateCompatibleDC.restype = wintypes.HDC
    gdi32.CreateCompatibleBitmap.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int]
    gdi32.CreateCompatibleBitmap.restype = wintypes.HBITMAP
    gdi32.CreateDIBSection.argtypes = [
        wintypes.HDC, ctypes.POINTER(BITMAPINFO), wintypes.UINT,
        ctypes.POINTER(ctypes.c_void_p), wintypes.HANDLE, wintypes.DWORD,
    ]
    gdi32.CreateDIBSection.restype = wintypes.HBITMAP
    gdi32.SelectObject.argtypes = [wintypes.HDC, HGDIOBJ]
    gdi32.SelectObject.restype = HGDIOBJ
    gdi32.BitBlt.argtypes = [
        wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
        wintypes.HDC, ctypes.c_int, ctypes.c_int, wintypes.DWORD,
    ]
    gdi32.BitBlt.restype = wintypes.BOOL
    gdi32.DeleteDC.argtypes = [wintypes.HDC]
    gdi32.DeleteDC.restype = wintypes.BOOL
    gdi32.DeleteObject.argtypes = [HGDIOBJ]
    gdi32.DeleteObject.restype = wintypes.BOOL
    gdi32.CreatePen.argtypes = [ctypes.c_int, ctypes.c_int, wintypes.COLORREF]
    gdi32.CreatePen.restype = wintypes.HPEN
    gdi32.CreateSolidBrush.argtypes = [wintypes.COLORREF]
    gdi32.CreateSolidBrush.restype = wintypes.HBRUSH
    gdi32.GetStockObject.argtypes = [ctypes.c_int]
    gdi32.GetStockObject.restype = HGDIOBJ
    gdi32.Rectangle.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
    gdi32.Rectangle.restype = wintypes.BOOL
    gdi32.RoundRect.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
    gdi32.RoundRect.restype = wintypes.BOOL
    gdi32.CreateFontW.argtypes = [
        ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
        wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
        wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.LPCWSTR,
    ]
    gdi32.CreateFontW.restype = wintypes.HFONT
    gdi32.SetBkMode.argtypes = [wintypes.HDC, ctypes.c_int]
    gdi32.SetBkMode.restype = ctypes.c_int
    gdi32.SetTextColor.argtypes = [wintypes.HDC, wintypes.COLORREF]
    gdi32.SetTextColor.restype = wintypes.COLORREF

    if msimg32 is not None:
        msimg32.AlphaBlend.argtypes = [
            wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
            wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
            BLENDFUNCTION,
        ]
        msimg32.AlphaBlend.restype = wintypes.BOOL

    vx = user32.GetSystemMetrics(76)   # SM_XVIRTUALSCREEN
    vy = user32.GetSystemMetrics(77)   # SM_YVIRTUALSCREEN
    vw = user32.GetSystemMetrics(78)   # SM_CXVIRTUALSCREEN
    vh = user32.GetSystemMetrics(79)   # SM_CYVIRTUALSCREEN
    px = user32.GetSystemMetrics(0)    # Primary screen width
    if vw <= 0 or vh <= 0:
        return None

    SRCCOPY = 0x00CC0020
    CAPTUREBLT = 0x40000000
    DIB_RGB_COLORS = 0
    BI_RGB = 0
    HOLLOW_BRUSH = 5
    PS_SOLID = 0
    TRANSPARENT_BK = 1

    hdc_screen = user32.GetDC(None)
    if not hdc_screen:
        return None

    hdc_orig = None
    hdc_dim = None
    hdc_back = None
    hdc_black = None
    hbmp_orig = None
    hbmp_dim = None
    hbmp_back = None
    hbmp_black = None
    old_orig = None
    old_dim = None
    old_back = None
    old_black = None
    ppv_bits = ctypes.c_void_p()

    white_pen = None
    dark_pen = None
    corner_pen = None
    pill_brush = None
    pill_border_pen = None
    hint_font = None

    hwnd = None
    class_name = f"FuncOverlayCtSRegion_{os.getpid()}_{int(time.time() * 1000)}"
    h_inst = kernel32.GetModuleHandleW(None)

    try:
        bmi = BITMAPINFO()
        bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        bmi.bmiHeader.biWidth = vw
        bmi.bmiHeader.biHeight = -vh  # top-down DIB
        bmi.bmiHeader.biPlanes = 1
        bmi.bmiHeader.biBitCount = 32
        bmi.bmiHeader.biCompression = BI_RGB

        hdc_orig = gdi32.CreateCompatibleDC(hdc_screen)
        hbmp_orig = gdi32.CreateDIBSection(hdc_screen, ctypes.byref(bmi), DIB_RGB_COLORS, ctypes.byref(ppv_bits), None, 0)
        if not hdc_orig or not hbmp_orig or not ppv_bits.value:
            return None
        old_orig = gdi32.SelectObject(hdc_orig, hbmp_orig)
        gdi32.BitBlt(hdc_orig, 0, 0, vw, vh, hdc_screen, vx, vy, SRCCOPY | CAPTUREBLT)

        hdc_dim = gdi32.CreateCompatibleDC(hdc_screen)
        hbmp_dim = gdi32.CreateCompatibleBitmap(hdc_screen, vw, vh)
        old_dim = gdi32.SelectObject(hdc_dim, hbmp_dim)
        gdi32.BitBlt(hdc_dim, 0, 0, vw, vh, hdc_orig, 0, 0, SRCCOPY)

        # Darken dimmed background rapidly via 1x1 black AlphaBlend
        if msimg32 is not None:
            hdc_black = gdi32.CreateCompatibleDC(hdc_screen)
            hbmp_black = gdi32.CreateCompatibleBitmap(hdc_screen, 1, 1)
            old_black = gdi32.SelectObject(hdc_black, hbmp_black)
            black_brush = gdi32.GetStockObject(4)  # BLACK_BRUSH
            rc1 = wintypes.RECT(0, 0, 1, 1)
            user32.FillRect(hdc_black, ctypes.byref(rc1), black_brush)
            bf = BLENDFUNCTION(0, 0, 128, 0)
            msimg32.AlphaBlend(hdc_dim, 0, 0, vw, vh, hdc_black, 0, 0, 1, 1, bf)

        hdc_back = gdi32.CreateCompatibleDC(hdc_screen)
        hbmp_back = gdi32.CreateCompatibleBitmap(hdc_screen, vw, vh)
        old_back = gdi32.SelectObject(hdc_back, hbmp_back)

        white_pen = gdi32.CreatePen(PS_SOLID, 2, 0x00FFFFFF)
        dark_pen = gdi32.CreatePen(PS_SOLID, 1, 0x0027272A)
        corner_pen = gdi32.CreatePen(PS_SOLID, 3, 0x00FFFFFF)
        pill_brush = gdi32.CreateSolidBrush(0x001B1818)
        pill_border_pen = gdi32.CreatePen(PS_SOLID, 1, 0x00463F3F)
        hint_font = gdi32.CreateFontW(-15, 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI")

        state = {
            "dragging": False,
            "x0": 0,
            "y0": 0,
            "x1": 0,
            "y1": 0,
            "cancelled": False,
            "rect": None,
        }

        def _signed_xy(lp: int) -> tuple[int, int]:
            x = ctypes.c_short(lp & 0xFFFF).value
            y = ctypes.c_short((lp >> 16) & 0xFFFF).value
            return (max(0, min(vw, x)), max(0, min(vh, y)))

        def _norm_rect() -> tuple[int, int, int, int]:
            l = max(0, min(state["x0"], state["x1"]))
            t = max(0, min(state["y0"], state["y1"]))
            r = min(vw, max(state["x0"], state["x1"]))
            b = min(vh, max(state["y0"], state["y1"]))
            return (l, t, r, b)

        WM_DESTROY = 0x0002
        WM_PAINT = 0x000F
        WM_ERASEBKGND = 0x0014
        WM_KEYDOWN = 0x0100
        WM_MOUSEMOVE = 0x0200
        WM_LBUTTONDOWN = 0x0201
        WM_LBUTTONUP = 0x0202
        WM_RBUTTONDOWN = 0x0204
        VK_ESCAPE = 0x1B

        def _wnd_proc(h: int, msg: int, wp: int, lp: int) -> int:
            if msg == WM_ERASEBKGND:
                return 1
            if msg == WM_PAINT:
                ps = PAINTSTRUCT()
                paint_dc = user32.BeginPaint(h, ctypes.byref(ps))
                try:
                    gdi32.BitBlt(hdc_back, 0, 0, vw, vh, hdc_dim, 0, 0, SRCCOPY)

                    l, t, r, b = _norm_rect()
                    if state["dragging"] and (r - l) >= 2 and (b - t) >= 2:
                        # Reveal crisp full-brightness original pixels inside the selection
                        gdi32.BitBlt(hdc_back, l, t, r - l, b - t, hdc_orig, l, t, SRCCOPY)

                        hollow = gdi32.GetStockObject(HOLLOW_BRUSH)
                        prev_b = gdi32.SelectObject(hdc_back, hollow)

                        prev_p = gdi32.SelectObject(hdc_back, dark_pen)
                        gdi32.Rectangle(hdc_back, max(0, l - 1), max(0, t - 1), min(vw, r + 1), min(vh, b + 1))

                        gdi32.SelectObject(hdc_back, white_pen)
                        gdi32.Rectangle(hdc_back, l, t, r, b)

                        gdi32.SelectObject(hdc_back, prev_p)
                        gdi32.SelectObject(hdc_back, prev_b)
                    elif not state["dragging"]:
                        # Subtle centered prompt pill at top of primary monitor before dragging
                        pill_w, pill_h = 310, 34
                        cx = (-vx + px // 2) if (vx <= 0 and (-vx + px) <= vw) else (vw // 2)
                        pl = max(12, cx - pill_w // 2)
                        pt = max(24, (-vy + 24) if vy < 0 else 24)
                        prev_b = gdi32.SelectObject(hdc_back, pill_brush)
                        prev_p = gdi32.SelectObject(hdc_back, pill_border_pen)
                        gdi32.RoundRect(hdc_back, pl, pt, pl + pill_w, pt + pill_h, 34, 34)
                        prev_f = gdi32.SelectObject(hdc_back, hint_font)
                        gdi32.SetBkMode(hdc_back, TRANSPARENT_BK)
                        gdi32.SetTextColor(hdc_back, 0x00F5F4F4)
                        rc_txt = wintypes.RECT(pl, pt, pl + pill_w, pt + pill_h)
                        user32.DrawTextW(hdc_back, "Drag to search area  •  Esc to cancel", -1, ctypes.byref(rc_txt), 0x25)
                        gdi32.SelectObject(hdc_back, prev_f)
                        gdi32.SelectObject(hdc_back, prev_p)
                        gdi32.SelectObject(hdc_back, prev_b)

                    gdi32.BitBlt(paint_dc, 0, 0, vw, vh, hdc_back, 0, 0, SRCCOPY)
                finally:
                    user32.EndPaint(h, ctypes.byref(ps))
                return 0

            if msg == WM_LBUTTONDOWN:
                x, y = _signed_xy(lp)
                state["dragging"] = True
                state["x0"] = x
                state["y0"] = y
                state["x1"] = x
                state["y1"] = y
                user32.SetCapture(h)
                user32.InvalidateRect(h, None, False)
                return 0

            if msg == WM_MOUSEMOVE:
                if state["dragging"]:
                    x, y = _signed_xy(lp)
                    state["x1"] = x
                    state["y1"] = y
                    user32.InvalidateRect(h, None, False)
                return 0

            if msg == WM_LBUTTONUP:
                if state["dragging"]:
                    x, y = _signed_xy(lp)
                    state["x1"] = x
                    state["y1"] = y
                    state["dragging"] = False
                    user32.ReleaseCapture()
                    state["rect"] = _norm_rect()
                    user32.PostQuitMessage(0)
                return 0

            if msg == WM_RBUTTONDOWN or (msg == WM_KEYDOWN and wp == VK_ESCAPE):
                if state["dragging"]:
                    state["dragging"] = False
                    user32.ReleaseCapture()
                state["cancelled"] = True
                user32.PostQuitMessage(0)
                return 0

            if msg == WM_DESTROY:
                user32.PostQuitMessage(0)
                return 0

            return user32.DefWindowProcW(h, msg, wp, lp)

        wnd_proc_cb = WNDPROC(_wnd_proc)
        cursor = user32.LoadCursorW(None, ctypes.c_void_p(32515))  # IDC_CROSS
        wc = WNDCLASSEXW()
        wc.cbSize = ctypes.sizeof(WNDCLASSEXW)
        wc.style = 0x0003  # CS_HREDRAW | CS_VREDRAW
        wc.lpfnWndProc = wnd_proc_cb
        wc.hInstance = h_inst
        wc.hCursor = cursor
        wc.lpszClassName = class_name

        atom = user32.RegisterClassExW(ctypes.byref(wc))
        if not atom:
            return None

        WS_EX_TOPMOST = 0x00000008
        WS_EX_TOOLWINDOW = 0x00000080
        WS_POPUP = 0x80000000
        WS_VISIBLE = 0x10000000

        hwnd = user32.CreateWindowExW(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            class_name,
            "FuncOverlay Region Search",
            WS_POPUP | WS_VISIBLE,
            vx,
            vy,
            vw,
            vh,
            None,
            None,
            h_inst,
            None,
        )
        if not hwnd:
            return None

        user32.ShowWindow(hwnd, 5)
        user32.SetForegroundWindow(hwnd)
        user32.SetFocus(hwnd)

        msg_struct = wintypes.MSG()
        while user32.GetMessageW(ctypes.byref(msg_struct), None, 0, 0) > 0:
            user32.TranslateMessage(ctypes.byref(msg_struct))
            user32.DispatchMessageW(ctypes.byref(msg_struct))

        if state["cancelled"] or not state["rect"]:
            return None

        l, t, r, b = state["rect"]
        cw, ch = r - l, b - t
        if cw < 6 or ch < 6:
            return None

        # Copy cropped BGRA rows before GDI cleanup
        total_bytes = vw * vh * 4
        dib_arr = (ctypes.c_ubyte * total_bytes).from_address(ppv_bits.value)
        crop_bgra = bytearray(cw * ch * 4)
        row_bytes = cw * 4
        for idx, row_y in enumerate(range(t, b)):
            src_off = (row_y * vw + l) * 4
            dst_off = idx * row_bytes
            crop_bgra[dst_off : dst_off + row_bytes] = dib_arr[src_off : src_off + row_bytes]

        return encode_png(bytes(crop_bgra), cw, ch, is_bgra=True), cw, ch
    finally:
        if hwnd:
            user32.DestroyWindow(hwnd)
        try:
            user32.UnregisterClassW(class_name, h_inst)
        except Exception:
            pass
        for h_obj in (white_pen, dark_pen, corner_pen, pill_brush, pill_border_pen, hint_font):
            if h_obj:
                gdi32.DeleteObject(h_obj)
        if hdc_back:
            if old_back:
                gdi32.SelectObject(hdc_back, old_back)
            gdi32.DeleteDC(hdc_back)
        if hbmp_back:
            gdi32.DeleteObject(hbmp_back)
        if hdc_black:
            if old_black:
                gdi32.SelectObject(hdc_black, old_black)
            gdi32.DeleteDC(hdc_black)
        if hbmp_black:
            gdi32.DeleteObject(hbmp_black)
        if hdc_dim:
            if old_dim:
                gdi32.SelectObject(hdc_dim, old_dim)
            gdi32.DeleteDC(hdc_dim)
        if hbmp_dim:
            gdi32.DeleteObject(hbmp_dim)
        if hdc_orig:
            if old_orig:
                gdi32.SelectObject(hdc_orig, old_orig)
            gdi32.DeleteDC(hdc_orig)
        if hbmp_orig:
            gdi32.DeleteObject(hbmp_orig)
        if hdc_screen:
            user32.ReleaseDC(None, hdc_screen)


def _build_multipart_body(
    fields: dict[str, str],
    file_field: str,
    filename: str,
    file_bytes: bytes,
    content_type: str = "image/png",
) -> tuple[bytes, str]:
    boundary = f"----FuncOverlayCtS{uuid.uuid4().hex}"
    chunks: list[bytes] = []
    for k, v in fields.items():
        chunks.append(
            (
                f"--{boundary}\r\n"
                f'Content-Disposition: form-data; name="{k}"\r\n\r\n'
                f"{v}\r\n"
            ).encode("utf-8")
        )
    chunks.append(
        (
            f"--{boundary}\r\n"
            f'Content-Disposition: form-data; name="{file_field}"; filename="{filename}"\r\n'
            f"Content-Type: {content_type}\r\n\r\n"
        ).encode("utf-8")
        + file_bytes
        + f"\r\n--{boundary}--\r\n".encode("utf-8")
    )
    return b"".join(chunks), boundary


def _normalize_tmpfiles_direct_url(raw_url: str) -> str:
    parsed = urllib.parse.urlparse((raw_url or "").strip())
    if not parsed.netloc:
        return ""
    scheme = "https"
    path = parsed.path or ""
    if "tmpfiles.org" in parsed.netloc.lower() and path and not path.startswith("/dl/"):
        path = "/dl" + (path if path.startswith("/") else "/" + path)
    return urllib.parse.urlunparse((scheme, parsed.netloc, path, "", parsed.query, ""))


def _upload_png_litterbox(png_bytes: bytes) -> str | None:
    body, boundary = _build_multipart_body(
        {"reqtype": "fileupload", "time": "1h"},
        "fileToUpload",
        "capture.png",
        png_bytes,
    )
    req = urllib.request.Request(
        "https://litterbox.catbox.moe/resources/internals/api.php",
        data=body,
        method="POST",
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "Content-Length": str(len(body)),
            "User-Agent": "FuncOverlay-CtS/1.0",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=7) as resp:
            txt = resp.read(2048).decode("utf-8", errors="ignore").strip()
            if txt.startswith(("https://", "http://")):
                return txt
    except Exception:
        pass
    return None


def _upload_png_tmpfiles(png_bytes: bytes) -> str | None:
    body, boundary = _build_multipart_body(
        {},
        "file",
        "capture.png",
        png_bytes,
    )
    req = urllib.request.Request(
        "https://tmpfiles.org/api/v1/upload",
        data=body,
        method="POST",
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "Content-Length": str(len(body)),
            "User-Agent": "FuncOverlay-CtS/1.0",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=7) as resp:
            data = json.loads(resp.read(8192).decode("utf-8", errors="ignore"))
            raw_url = str((data.get("data") or {}).get("url") or "").strip()
            direct = _normalize_tmpfiles_direct_url(raw_url)
            if direct.startswith("https://"):
                return direct
    except Exception:
        pass
    return None


def _upload_png_uguu(png_bytes: bytes) -> str | None:
    body, boundary = _build_multipart_body(
        {},
        "files[]",
        "capture.png",
        png_bytes,
    )
    req = urllib.request.Request(
        "https://uguu.se/upload",
        data=body,
        method="POST",
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "Content-Length": str(len(body)),
            "User-Agent": "FuncOverlay-CtS/1.0",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=7) as resp:
            data = json.loads(resp.read(8192).decode("utf-8", errors="ignore"))
            files = data.get("files") or []
            if files and isinstance(files[0], dict):
                url = str(files[0].get("url") or "").strip()
                if url.startswith(("https://", "http://")):
                    return url
    except Exception:
        pass
    return None


def _upload_temp_public_png(png_bytes: bytes) -> str | None:
    """Upload cropped image to an auto-expiring public host so Google Lens can load it via uploadbyurl."""
    for uploader in (_upload_png_litterbox, _upload_png_tmpfiles, _upload_png_uguu):
        url = uploader(png_bytes)
        if url:
            return url
    return None


def _create_browser_lens_submit_html(png_bytes: bytes, width: int = 0, height: int = 0) -> str:
    """
    Fallback: create a temporary local HTML page that submits the captured PNG directly from
    the user's browser tab via multipart POST with processed_image_dimensions.
    """
    temp_dir = Path(tempfile.gettempdir()) / "FuncOverlay_CtS"
    temp_dir.mkdir(parents=True, exist_ok=True)

    now = time.time()
    for old_file in temp_dir.glob("lens_*.html"):
        try:
            if (now - old_file.stat().st_mtime) > 120:
                old_file.unlink(missing_ok=True)
        except Exception:
            pass

    st_ms = int(now * 1000)
    action_url = f"https://lens.google.com/v3/upload?hl=en&re=df&stcs={st_ms}&vpw=1280&vph=800&ep=gsbubu"
    b64 = base64.b64encode(png_bytes).decode("ascii")
    dim_input = (
        f'<input type="hidden" name="processed_image_dimensions" value="{width},{height}" />'
        if width > 0 and height > 0
        else ""
    )

    page_html = f"""<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<title>Searching Image...</title>
<style>
  html, body {{
    background: #18181b;
    color: #e4e4e7;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100vh;
    margin: 0;
  }}
</style>
</head>
<body>
<div>Searching area in Google Lens...</div>
<form id="f" method="POST" action="{action_url}" enctype="multipart/form-data">
  <input id="inp" type="file" name="encoded_image" style="display:none" />
  {dim_input}
</form>
<script>
(function() {{
  try {{
    var bin = atob("{b64}");
    var len = bin.length;
    var arr = new Uint8Array(len);
    for (var i = 0; i < len; i++) arr[i] = bin.charCodeAt(i);
    var file = new File([arr], "capture.png", {{ type: "image/png" }});
    var dt = new DataTransfer();
    dt.items.add(file);
    var inp = document.getElementById("inp");
    inp.files = dt.files;
    document.getElementById("f").submit();
  }} catch (err) {{
    document.body.textContent = "Could not submit image to Google Lens: " + err;
  }}
}})();
</script>
</body>
</html>
"""
    out_file = temp_dir / f"lens_{os.getpid()}_{st_ms}.html"
    out_file.write_text(page_html, encoding="utf-8")
    return out_file.as_uri()


def search_image_in_new_tab(png_bytes: bytes, width: int = 0, height: int = 0) -> bool:
    # Opening lens.google.com/uploadbyurl?url=<public_url> runs inside the browser's own session
    # and cookies as a normal top-level GET, avoiding 'Expired visual search' session mismatch.
    public_url = _upload_temp_public_png(png_bytes)
    if public_url:
        encoded = urllib.parse.quote(public_url, safe="")
        lens_url = f"https://lens.google.com/uploadbyurl?url={encoded}"
        return open_url_in_new_tab(lens_url)

    fallback_uri = _create_browser_lens_submit_html(png_bytes, width, height)
    return open_url_in_new_tab(fallback_uri)


def run_region_search_cli(argv: list[str]) -> int:
    try:
        fade_ms = max(0, min(5000, int(argv[1]))) if len(argv) > 1 else 250
    except Exception:
        fade_ms = 250

    if fade_ms > 0:
        time.sleep((fade_ms + 35) / 1000.0)
    else:
        time.sleep(0.035)

    captured = capture_and_select_region_png()
    if captured:
        png_bytes, cw, ch = captured
        search_image_in_new_tab(png_bytes, cw, ch)
    return 0


class CircleToSearchPlugin:
    def __init__(self):
        self.running = True
        self.settings: dict = load_local_metadata_settings()

    def apply_settings(self, settings: dict) -> None:
        if settings:
            merged = dict(self.settings)
            merged.update(settings)
            self.settings = merged

    def build_render_payload(self) -> dict:
        placeholder = str(self.settings.get("placeholder_text") or "Find in Google").strip() or "Find in Google"
        return {
            "type": "render",
            "style": "gray",
            "layout": "search_bar",
            "searchBar": {
                "placeholder": placeholder,
                "submitAction": "search_text",
            },
            "buttons": [
                {
                    "id": "search_text",
                    "icon": "search",
                    "iconPaths": ICON_PATHS["search"],
                    "tooltip": "Search in Default Browser (New Tab)",
                },
                {
                    "id": "photo_search",
                    "icon": "photo_search",
                    "iconPaths": ICON_PATHS["photo_search"],
                    "tooltip": "Photo Search (Select Area)",
                },
            ],
            "statusItems": [],
        }

    def emit_render(self) -> None:
        send_host(self.build_render_payload())

    def handle_action(self, action: str, query: str = "") -> None:
        if action == "search_text":
            q = (query or "").strip()
            if not q:
                send_intent("app.toast", {"message": "Type something to search"})
                return

            template = str(self.settings.get("search_url_template") or "https://www.google.com/search?q={query}")
            url = build_text_search_url(q, template)
            open_url_in_new_tab(url)

            close_raw = self.settings.get("close_on_text_search", True)
            should_close = close_raw if isinstance(close_raw, bool) else str(close_raw).lower() != "false"
            if should_close:
                send_intent("overlay.close")
                self.running = False
            return

        if action == "photo_search":
            fade_ms = read_fade_duration_ms(default_ms=250)
            helper_cmd = [
                _resolve_worker_python(),
                os.path.abspath(__file__),
                "--region-search",
                str(fade_ms),
            ]
            try:
                _launch_process_detached(helper_cmd)
            except Exception:
                send_intent("overlay.close")
                self.running = False
                return

            send_intent("overlay.close")
            self.running = False


def main() -> None:
    if len(sys.argv) >= 2 and sys.argv[1] == "--region-search":
        raise SystemExit(run_region_search_cli(sys.argv[1:]))

    _configure_stdio()
    plugin = CircleToSearchPlugin()
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
                plugin.emit_render()
        elif ev == "ui_action":
            action = str(msg.get("action") or "")
            query = str(msg.get("query") or "")
            plugin.handle_action(action, query)
        elif ev == "overlay_closed":
            plugin.running = False
            break


if __name__ == "__main__":
    main()
