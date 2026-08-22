from __future__ import annotations

from decimal import Decimal, InvalidOperation


def parse_timecode(value: str | int | float | None) -> float | None:
    """Parse HH:MM:SS(.sss), MM:SS(.sss), or raw seconds."""
    if value is None or value == "":
        return None
    if isinstance(value, (int, float)):
        return float(value)
    text = str(value).strip()
    if not text:
        return None
    try:
        parts = text.split(":")
        if len(parts) == 1:
            return float(Decimal(parts[0]))
        if len(parts) == 2:
            minutes, seconds = parts
            return int(minutes) * 60 + float(Decimal(seconds))
        if len(parts) == 3:
            hours, minutes, seconds = parts
            return int(hours) * 3600 + int(minutes) * 60 + float(Decimal(seconds))
    except (ValueError, InvalidOperation) as exc:
        raise ValueError(f"Invalid timecode: {value!r}") from exc
    raise ValueError(f"Invalid timecode: {value!r}")


def format_timecode(seconds: float | int | None, milliseconds: bool = False) -> str:
    if seconds is None:
        return ""
    seconds = max(0.0, float(seconds))
    hours = int(seconds // 3600)
    minutes = int((seconds % 3600) // 60)
    remaining = seconds % 60
    if milliseconds:
        return f"{hours:02d}:{minutes:02d}:{remaining:06.3f}"
    return f"{hours:02d}:{minutes:02d}:{int(round(remaining)):02d}"


def ffmeta_timebase_ms(seconds: float) -> int:
    return int(round(float(seconds) * 1000))

