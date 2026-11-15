#!/usr/bin/env python3
"""
simple calc - FuncOverlay Python Plugin
- Runs ONLY while FuncOverlay overlay is open (stdin/stdout JSON-line IPC).
- Evaluates arithmetic expressions with parentheses, square roots, and exact fractions.
- Displays single-line expression before '=' and decimal + mixed fraction after '='.
"""

from decimal import Decimal, localcontext, ROUND_HALF_UP
from fractions import Fraction
import json
import math
import os
from pathlib import Path
import re
import sys
import threading

OPERATORS = ("÷", "×", "−", "+")
OP_ALIASES = {
    "/": "÷",
    "*": "×",
    "-": "−",
    "+": "+",
    "÷": "÷",
    "×": "×",
    "−": "−",
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


def _get_state_file_path() -> Path:
    local_app = os.environ.get("LOCALAPPDATA")
    if local_app:
        d = Path(local_app) / "FuncOverlay"
    else:
        d = Path(__file__).resolve().parent
    return d / "simple_calc_state.json"


# ---------------------------------------------------------------------------
# Exact Rational + Float Expression Evaluator
# ---------------------------------------------------------------------------

class CalcValue:
    """Represents either an exact Fraction or an irrational float."""
    __slots__ = ("frac", "flt", "is_exact")

    def __init__(self, value):
        if isinstance(value, Fraction):
            self.frac: Fraction | None = value
            self.flt: float = float(value)
            self.is_exact: bool = True
        else:
            self.frac = None
            self.flt = float(value)
            self.is_exact = False

    def __add__(self, other: "CalcValue") -> "CalcValue":
        if self.is_exact and other.is_exact:
            return CalcValue(self.frac + other.frac)
        return CalcValue(self.flt + other.flt)

    def __sub__(self, other: "CalcValue") -> "CalcValue":
        if self.is_exact and other.is_exact:
            return CalcValue(self.frac - other.frac)
        return CalcValue(self.flt - other.flt)

    def __mul__(self, other: "CalcValue") -> "CalcValue":
        if self.is_exact and other.is_exact:
            return CalcValue(self.frac * other.frac)
        return CalcValue(self.flt * other.flt)

    def __truediv__(self, other: "CalcValue") -> "CalcValue":
        if other.is_exact:
            if other.frac == 0:
                raise ZeroDivisionError("Division by zero")
        elif other.flt == 0.0:
            raise ZeroDivisionError("Division by zero")

        if self.is_exact and other.is_exact:
            return CalcValue(self.frac / other.frac)
        return CalcValue(self.flt / other.flt)

    def negate(self) -> "CalcValue":
        if self.is_exact:
            return CalcValue(-self.frac)
        return CalcValue(-self.flt)

    def sqrt(self) -> "CalcValue":
        if self.is_exact:
            assert self.frac is not None
            if self.frac < 0:
                raise ValueError("Square root of negative number")
            num = self.frac.numerator
            den = self.frac.denominator
            sn = math.isqrt(num)
            sd = math.isqrt(den)
            if sn * sn == num and sd * sd == den:
                return CalcValue(Fraction(sn, sd))
            return CalcValue(math.sqrt(self.flt))
        if self.flt < 0:
            raise ValueError("Square root of negative number")
        return CalcValue(math.sqrt(self.flt))


def _tokenize(expr: str) -> list[tuple[str, str]]:
    tokens: list[tuple[str, str]] = []
    i = 0
    n = len(expr)
    while i < n:
        ch = expr[i]
        if ch.isspace():
            i += 1
            continue
        if ch.isdigit() or ch == ".":
            start = i
            has_dot = ch == "."
            i += 1
            while i < n and (expr[i].isdigit() or (expr[i] == "." and not has_dot)):
                if expr[i] == ".":
                    has_dot = True
                i += 1
            num_str = expr[start:i]
            if num_str == ".":
                num_str = "0"
            tokens.append(("NUM", num_str))
            continue
        if ch in OP_ALIASES:
            tokens.append(("OP", OP_ALIASES[ch]))
            i += 1
            continue
        if ch == "(":
            tokens.append(("LPAREN", "("))
            i += 1
            continue
        if ch == ")":
            tokens.append(("RPAREN", ")"))
            i += 1
            continue
        if ch == "√":
            tokens.append(("SQRT", "√"))
            i += 1
            continue
        raise ValueError(f"Unexpected character: {ch}")

    # Insert implicit multiplication (e.g. 2(3), (2)(3), 2√(9), )2)
    expanded: list[tuple[str, str]] = []
    for idx, tok in enumerate(tokens):
        if idx > 0:
            prev_kind = tokens[idx - 1][0]
            curr_kind = tok[0]
            if (prev_kind in ("NUM", "RPAREN") and curr_kind in ("LPAREN", "SQRT")) or (
                prev_kind == "RPAREN" and curr_kind == "NUM"
            ):
                expanded.append(("OP", "×"))
        expanded.append(tok)
    return expanded


class _Parser:
    def __init__(self, tokens: list[tuple[str, str]]):
        self.tokens = tokens
        self.pos = 0

    def peek(self) -> tuple[str, str] | None:
        if self.pos < len(self.tokens):
            return self.tokens[self.pos]
        return None

    def consume(self) -> tuple[str, str]:
        tok = self.tokens[self.pos]
        self.pos += 1
        return tok

    def parse(self) -> CalcValue:
        if not self.tokens:
            return CalcValue(Fraction(0))
        val = self.parse_expr()
        if self.pos < len(self.tokens):
            raise ValueError("Unexpected trailing tokens")
        return val

    def parse_expr(self) -> CalcValue:
        left = self.parse_term()
        while (tok := self.peek()) is not None and tok[0] == "OP" and tok[1] in ("+", "−"):
            self.consume()
            right = self.parse_term()
            left = left + right if tok[1] == "+" else left - right
        return left

    def parse_term(self) -> CalcValue:
        left = self.parse_unary()
        while (tok := self.peek()) is not None and tok[0] == "OP" and tok[1] in ("×", "÷"):
            self.consume()
            right = self.parse_unary()
            left = left * right if tok[1] == "×" else left / right
        return left

    def parse_unary(self) -> CalcValue:
        tok = self.peek()
        if tok is None:
            raise ValueError("Unexpected end of expression")
        if tok[0] == "OP" and tok[1] == "−":
            self.consume()
            return self.parse_unary().negate()
        if tok[0] == "OP" and tok[1] == "+":
            self.consume()
            return self.parse_unary()
        if tok[0] == "SQRT":
            self.consume()
            return self.parse_unary().sqrt()
        return self.parse_primary()

    def parse_primary(self) -> CalcValue:
        tok = self.peek()
        if tok is None:
            raise ValueError("Unexpected end of expression")
        if tok[0] == "NUM":
            self.consume()
            return CalcValue(Fraction(tok[1]))
        if tok[0] == "LPAREN":
            self.consume()
            val = self.parse_expr()
            if (nxt := self.peek()) is not None and nxt[0] == "RPAREN":
                self.consume()
            return val
        raise ValueError(f"Unexpected token {tok}")


def evaluate_expression(raw_expr: str) -> CalcValue:
    expr = (raw_expr or "").strip()
    if not expr:
        return CalcValue(Fraction(0))

    # Strip incomplete trailing binary operators if user hits '=' right after an operator
    while expr and expr[-1] in ("÷", "×", "−", "+", "/", "*", "-"):
        expr = expr[:-1]
    if not expr:
        return CalcValue(Fraction(0))

    # Balance any unclosed '(' automatically
    open_parens = expr.count("(") - expr.count(")")
    if open_parens > 0:
        expr += ")" * open_parens

    tokens = _tokenize(expr)
    return _Parser(tokens).parse()


def format_decimal_value(val: CalcValue, max_decimals: int = 15) -> str:
    max_decimals = max(1, min(18, int(max_decimals)))
    if val.is_exact and val.frac is not None:
        frac = val.frac
        if frac.denominator == 1:
            return str(frac.numerator)

        with localcontext() as ctx:
            ctx.prec = max(40, max_decimals + 24)
            dec = Decimal(frac.numerator) / Decimal(frac.denominator)
            quant = Decimal(10) ** (-max_decimals)
            rounded = dec.quantize(quant, rounding=ROUND_HALF_UP)
            text = format(rounded, "f")
            if "." in text:
                text = text.rstrip("0").rstrip(".")
            if text == "-0":
                text = "0"
            return text

    # Irrational float fallback
    if math.isnan(val.flt) or math.isinf(val.flt):
        raise ValueError("Overflow")
    text = f"{val.flt:.{max_decimals}f}"
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    if text == "-0":
        text = "0"
    return text


def format_fraction_parts(val: CalcValue) -> dict | None:
    if not val.is_exact or val.frac is None:
        return None
    frac = val.frac
    if frac.denominator == 1 or frac.denominator > 100000:
        return None

    neg = frac < 0
    abs_f = abs(frac)
    whole = abs_f.numerator // abs_f.denominator
    rem_num = abs_f.numerator % abs_f.denominator
    den = abs_f.denominator

    if whole > 0:
        whole_str = f"-{whole}" if neg else str(whole)
    else:
        whole_str = "-" if neg else ""

    return {
        "whole": whole_str,
        "num": str(rem_num),
        "den": str(den),
    }


def _comma_group_digits(int_digits: str) -> str:
    if len(int_digits) <= 3:
        return int_digits
    parts: list[str] = []
    while len(int_digits) > 3:
        parts.append(int_digits[-3:])
        int_digits = int_digits[:-3]
    parts.append(int_digits)
    return ",".join(reversed(parts))


def format_number_str_commas(num_str: str) -> str:
    if not num_str:
        return num_str
    sign = ""
    body = num_str
    if body[0] in ("-", "−"):
        sign = body[0]
        body = body[1:]
    if "." in body:
        int_part, dec_part = body.split(".", 1)
        return f"{sign}{_comma_group_digits(int_part)}.{dec_part}"
    return f"{sign}{_comma_group_digits(body)}"


def format_expression_commas(expr: str) -> str:
    return re.sub(r"\d+(?:\.\d*)?", lambda m: format_number_str_commas(m.group(0)), expr)


# ---------------------------------------------------------------------------
# Plugin State & Interactive Keypad Handling
# ---------------------------------------------------------------------------

KEYPAD_LAYOUT = [
    [
        {"id": "ac", "label": "AC", "variant": "operator"},
        {"id": "parens", "label": "()", "variant": "operator"},
        {"id": "sqrt", "icon": "sqrt", "label": "√", "variant": "operator"},
        {"id": "div", "label": "÷", "variant": "operator"},
    ],
    [
        {"id": "7", "label": "7", "variant": "digit"},
        {"id": "8", "label": "8", "variant": "digit"},
        {"id": "9", "label": "9", "variant": "digit"},
        {"id": "mul", "label": "×", "variant": "operator"},
    ],
    [
        {"id": "4", "label": "4", "variant": "digit"},
        {"id": "5", "label": "5", "variant": "digit"},
        {"id": "6", "label": "6", "variant": "digit"},
        {"id": "sub", "label": "−", "variant": "operator"},
    ],
    [
        {"id": "1", "label": "1", "variant": "digit"},
        {"id": "2", "label": "2", "variant": "digit"},
        {"id": "3", "label": "3", "variant": "digit"},
        {"id": "add", "label": "+", "variant": "operator"},
    ],
    [
        {"id": "0", "label": "0", "variant": "digit"},
        {"id": "dot", "label": ".", "variant": "digit"},
        {"id": "backspace", "icon": "backspace", "label": "⌫", "variant": "digit"},
        {"id": "equals", "label": "=", "variant": "equals"},
    ],
]


class SimpleCalcPlugin:
    def __init__(self):
        self.running = True
        self.settings: dict = load_local_metadata_settings()
        self.expression: str = ""
        self.result_text: str = ""
        self.fraction_parts: dict | None = None
        self.just_evaluated: bool = False
        self.has_error: bool = False
        self._lock = threading.Lock()
        self._load_persisted_state()

    def _setting_bool(self, key: str, default: bool) -> bool:
        val = self.settings.get(key, default)
        if isinstance(val, bool):
            return val
        if isinstance(val, str):
            return val.strip().lower() in ("true", "1", "yes")
        return bool(val)

    def _setting_int(self, key: str, default: int) -> int:
        try:
            return int(self.settings.get(key, default))
        except Exception:
            return default

    def _load_persisted_state(self) -> None:
        if not self._setting_bool("remember_state", True):
            return
        try:
            path = _get_state_file_path()
            if path.exists():
                data = json.loads(path.read_text(encoding="utf-8"))
                if isinstance(data, dict):
                    self.expression = str(data.get("expression") or "")
                    self.result_text = str(data.get("result_text") or "")
                    fp = data.get("fraction_parts")
                    self.fraction_parts = fp if isinstance(fp, dict) else None
                    self.just_evaluated = bool(data.get("just_evaluated", False))
                    self.has_error = bool(data.get("has_error", False))
        except Exception:
            pass

    def _save_persisted_state(self) -> None:
        if not self._setting_bool("remember_state", True):
            return
        try:
            path = _get_state_file_path()
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                json.dumps({
                    "expression": self.expression,
                    "result_text": self.result_text,
                    "fraction_parts": self.fraction_parts,
                    "just_evaluated": self.just_evaluated,
                    "has_error": self.has_error,
                }),
                encoding="utf-8",
            )
        except Exception:
            pass

    def apply_settings(self, new_settings: dict) -> None:
        if not new_settings:
            return
        with self._lock:
            merged = dict(self.settings)
            merged.update(new_settings)
            self.settings = merged
            if self.just_evaluated and not self.has_error and self.expression:
                self._evaluate_current_locked()

    def _evaluate_current_locked(self) -> None:
        try:
            val = evaluate_expression(self.expression)
            max_dec = self._setting_int("max_decimals", 15)
            self.result_text = format_decimal_value(val, max_dec)
            self.fraction_parts = (
                format_fraction_parts(val)
                if self._setting_bool("show_fraction", True)
                else None
            )
            self.has_error = False
        except Exception:
            self.result_text = "Error"
            self.fraction_parts = None
            self.has_error = True
        self.just_evaluated = True

    def _current_number_segment_has_dot(self) -> bool:
        for ch in reversed(self.expression):
            if ch == ".":
                return True
            if ch in ("÷", "×", "−", "+", "(", ")", "√"):
                return False
        return False

    def handle_action(self, action: str) -> None:
        act = (action or "").strip().lower()
        with self._lock:
            if act == "ac":
                self.expression = ""
                self.result_text = ""
                self.fraction_parts = None
                self.just_evaluated = False
                self.has_error = False

            elif act == "backspace":
                if self.has_error:
                    self.expression = ""
                    self.result_text = ""
                    self.fraction_parts = None
                    self.just_evaluated = False
                    self.has_error = False
                elif self.just_evaluated:
                    # Step back from '=' result into the expression cleanly
                    self.just_evaluated = False
                    self.result_text = ""
                    self.fraction_parts = None
                else:
                    if self.expression.endswith("√("):
                        self.expression = self.expression[:-2]
                    elif self.expression:
                        self.expression = self.expression[:-1]

            elif act in ("0", "1", "2", "3", "4", "5", "6", "7", "8", "9"):
                if self.just_evaluated or self.has_error:
                    self.expression = act
                    self.result_text = ""
                    self.fraction_parts = None
                    self.just_evaluated = False
                    self.has_error = False
                elif self.expression == "0":
                    self.expression = act
                else:
                    self.expression += act

            elif act == "dot":
                if self.just_evaluated or self.has_error:
                    self.expression = "0."
                    self.result_text = ""
                    self.fraction_parts = None
                    self.just_evaluated = False
                    self.has_error = False
                elif not self._current_number_segment_has_dot():
                    if not self.expression or self.expression[-1] in ("÷", "×", "−", "+", "(", "√"):
                        self.expression += "0."
                    elif self.expression[-1] == ")":
                        self.expression += "×0."
                    else:
                        self.expression += "."

            elif act in ("div", "mul", "sub", "add"):
                op_char = {"div": "÷", "mul": "×", "sub": "−", "add": "+"}[act]
                if self.has_error:
                    self.expression = "0" + op_char if op_char != "−" else "−"
                    self.result_text = ""
                    self.fraction_parts = None
                    self.just_evaluated = False
                    self.has_error = False
                else:
                    if self.just_evaluated:
                        self.expression = self.result_text.replace("-", "−")
                        self.result_text = ""
                        self.fraction_parts = None
                        self.just_evaluated = False

                    if not self.expression:
                        if op_char == "−":
                            self.expression = "−"
                        else:
                            self.expression = "0" + op_char
                    elif self.expression[-1] == "(":
                        if op_char == "−":
                            self.expression += "−"
                    elif self.expression[-1] in ("÷", "×", "−", "+"):
                        # Allow unary minus after × or ÷ (e.g. 5 × -2)
                        if op_char == "−" and self.expression[-1] in ("÷", "×"):
                            self.expression += "−"
                        elif len(self.expression) == 1 and self.expression == "−":
                            if op_char == "−":
                                pass
                        elif (
                            len(self.expression) >= 2
                            and self.expression[-1] == "−"
                            and self.expression[-2] in ("÷", "×", "(")
                        ):
                            pass
                        else:
                            self.expression = self.expression[:-1] + op_char
                    else:
                        self.expression += op_char

            elif act == "parens":
                if self.just_evaluated or self.has_error:
                    self.expression = "("
                    self.result_text = ""
                    self.fraction_parts = None
                    self.just_evaluated = False
                    self.has_error = False
                elif not self.expression or self.expression == "0":
                    self.expression = "("
                else:
                    open_balance = self.expression.count("(") - self.expression.count(")")
                    last = self.expression[-1]
                    if last in ("÷", "×", "−", "+", "(", "√"):
                        self.expression += "("
                    elif open_balance > 0 and (last.isdigit() or last == ")"):
                        self.expression += ")"
                    else:
                        self.expression += "("

            elif act == "sqrt":
                if self.just_evaluated or self.has_error:
                    self.expression = "√("
                    self.result_text = ""
                    self.fraction_parts = None
                    self.just_evaluated = False
                    self.has_error = False
                elif not self.expression or self.expression == "0":
                    self.expression = "√("
                else:
                    self.expression += "√("

            elif act == "equals":
                if not self.expression:
                    self.expression = "0"
                self._evaluate_current_locked()

            self._save_persisted_state()

        self.emit_render()

    def build_render_payload(self) -> dict:
        with self._lock:
            use_commas = self._setting_bool("thousands_separator", True)
            if self.just_evaluated:
                primary = self.result_text or "0"
                fp = self.fraction_parts if self._setting_bool("show_fraction", True) else None
            else:
                primary = self.expression if self.expression else "0"
                fp = None

            if use_commas and primary and primary != "Error":
                primary = format_expression_commas(primary)

            secondary = ""
            frac_whole = ""
            frac_num = ""
            frac_den = ""
            if fp:
                frac_whole = fp.get("whole", "")
                frac_num = fp.get("num", "")
                frac_den = fp.get("den", "")
                if use_commas:
                    if frac_whole and frac_whole != "-":
                        frac_whole = format_number_str_commas(frac_whole)
                    if frac_num:
                        frac_num = format_number_str_commas(frac_num)
                    if frac_den:
                        frac_den = format_number_str_commas(frac_den)
                prefix = f"{frac_whole} " if frac_whole and frac_whole != "-" else ("-" if frac_whole == "-" else "")
                secondary = f"{prefix}{frac_num}/{frac_den}"

            return {
                "type": "render",
                "style": "gray",
                "layout": "calculator",
                "display": {
                    "primary": primary,
                    "secondary": secondary,
                    "fractionWhole": frac_whole,
                    "fractionNum": frac_num,
                    "fractionDen": frac_den,
                },
                "keypad": KEYPAD_LAYOUT,
            }

    def emit_render(self) -> None:
        send_host(self.build_render_payload())

    def stdin_loop(self) -> None:
        for raw_line in sys.stdin:
            line = raw_line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except Exception:
                continue

            evt = msg.get("event")
            if evt == "overlay_opened":
                self.apply_settings(msg.get("settings") or {})
                self.emit_render()
            elif evt == "settings_updated":
                self.apply_settings(msg.get("settings") or {})
                self.emit_render()
            elif evt == "ui_action":
                self.handle_action(str(msg.get("action") or ""))
            elif evt == "overlay_closed":
                self.running = False
                break

        self.running = False

    def run(self) -> None:
        _configure_stdio()
        self.emit_render()
        self.stdin_loop()


if __name__ == "__main__":
    plugin = SimpleCalcPlugin()
    plugin.run()
