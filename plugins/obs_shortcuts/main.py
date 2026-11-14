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


def focus_or_launch_obs(obs_exe_path: str) -> None:
    """Restore existing OBS (taskbar or system tray) or launch obs64.exe via native host intent."""
    send_intent("system.focus_or_launch_app", {
        "path": obs_exe_path,
        "windowTitleHints": ["OBS ", "OBS Studio"],
        "processNames": ["obs64", "obs32", "obs"],
        "closeOverlay": True,
    })


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
