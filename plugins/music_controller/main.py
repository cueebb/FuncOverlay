#!/usr/bin/env python3
"""
music controller - FuncOverlay Python Plugin
- Runs ONLY while FuncOverlay overlay is open (zero background execution when closed).
- Uses FuncOverlay's 'media_controller' layout and 'media.*' intents backed by Windows SMTC
  (GlobalSystemMediaTransportControlsSessionManager), which is active strictly while subscribed
  during an open overlay session.
"""

import json
import sys
import threading
from pathlib import Path


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


def _to_bool(val, default: bool = True) -> bool:
    if isinstance(val, bool):
        return val
    if val is None:
        return default
    text = str(val).strip().lower()
    if text in ("false", "0", "no", "off", ""):
        return False
    if text in ("true", "1", "yes", "on"):
        return True
    return default


def _to_int(val, default: int, lo: int, hi: int) -> int:
    try:
        parsed = int(float(val))
        return max(lo, min(hi, parsed))
    except Exception:
        return default


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


class MusicControllerPlugin:
    def __init__(self) -> None:
        self.running = True
        self.settings: dict = load_local_metadata_settings()
        self._subscribed = False

    def apply_settings(self, settings: dict) -> None:
        if settings:
            merged = dict(self.settings)
            merged.update(settings)
            self.settings = merged

    def build_render_payload(self) -> dict:
        marquee_speed = _to_int(self.settings.get("marquee_speed"), 22, 10, 80)
        focus_on_switch = _to_bool(self.settings.get("focus_app_on_switch"), True)
        show_dropdown = _to_bool(self.settings.get("show_source_dropdown"), True)
        return {
            "type": "render",
            "style": "gray",
            "layout": "media_controller",
            "mediaConfig": {
                "marqueeSpeedPxPerSec": float(marquee_speed),
                "focusAppOnSwitch": focus_on_switch,
                "showSourceDropdown": show_dropdown,
            },
            "statusItems": [],
        }

    def emit_render(self) -> None:
        send_host(self.build_render_payload())

    def ensure_subscribed(self) -> None:
        if not self._subscribed:
            self._subscribed = True
            send_intent("media.subscribe", {})

    def handle_action(self, action: str, query: str = "", value=None) -> None:
        act = (action or "").strip().lower()
        source_id = (query or "").strip()

        if act in ("play_pause", "play", "pause", "next", "previous", "prev"):
            payload = {"command": act}
            if source_id:
                payload["sourceId"] = source_id
            send_intent("media.control", payload)
            return

        if act == "seek":
            try:
                pos_sec = float(value) if value is not None else 0.0
            except Exception:
                pos_sec = 0.0
            payload: dict = {
                "command": "seek",
                "positionSec": max(0.0, pos_sec),
            }
            if source_id:
                payload["sourceId"] = source_id
            send_intent("media.control", payload)
            return

        if act == "select_source":
            focus_win = _to_bool(self.settings.get("focus_app_on_switch"), True)
            send_intent(
                "media.select_source",
                {
                    "sourceId": source_id,
                    "focusWindow": focus_win,
                },
            )


def main() -> None:
    _configure_stdio()
    plugin = MusicControllerPlugin()
    plugin.emit_render()
    plugin.ensure_subscribed()

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
            plugin.ensure_subscribed()
        elif ev == "settings_updated":
            if isinstance(msg.get("settings"), dict):
                plugin.apply_settings(msg["settings"])
                plugin.emit_render()
        elif ev == "ui_action":
            action = str(msg.get("action") or "")
            query = str(msg.get("query") or "")
            value = msg.get("value")
            plugin.handle_action(action, query, value)
        elif ev == "overlay_closed":
            plugin.running = False
            break


if __name__ == "__main__":
    main()
