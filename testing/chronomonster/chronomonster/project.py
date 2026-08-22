from __future__ import annotations

import json
import os
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path

from . import __version__
from .chronology import chronology_hash


DEFAULT_PROJECT = {
    "format": "chronomonster-project-v1",
    "app_version": __version__,
    "chronology": {
        "name": "MCU strict/continuous 2026-08-22",
        "steps": 2622,
        "works": 663,
        "sha256": "",
        "manifest_path": "",
        "catalog_path": "",
    },
    "scope": "all",
    "disabled_steps": [],
    "media_roots": [],
    "portable_paths": False,
    "work_map": {},
    "match_candidates": {},
    "probe_cache": {},
    "manual_overrides": {},
    "preferences": {
        "audio_language": "eng",
        "audio_stream": "default",
        "subtitle_policy": "none",
        "theme": "christmas",
        "hide_spoilers": False,
    },
    "executables": {"ffmpeg": "", "ffprobe": "", "vlc": ""},
    "monster_profile": {
        "name": "universal-1080p",
        "width": 1920,
        "height": 1080,
        "fps": 30,
        "video_codec": "libx264",
        "crf": 18,
        "preset": "medium",
        "audio_codec": "aac",
        "audio_bitrate": "192k",
        "audio_rate": 48000,
        "audio_channels": 2,
    },
    "outputs": {},
    "resume_position": {"watch_step": 1, "offset_seconds": 0},
    "created_at": "",
    "updated_at": "",
}


def new_project() -> dict:
    project = deepcopy(DEFAULT_PROJECT)
    now = datetime.now(timezone.utc).isoformat()
    project["created_at"] = now
    project["updated_at"] = now
    project["chronology"]["sha256"] = chronology_hash()
    return project


def save_project(project: dict, path: str | Path) -> Path:
    target = Path(path)
    if target.suffix.lower() != ".json":
        target = target.with_suffix(".chronomonster.json")
    target.parent.mkdir(parents=True, exist_ok=True)
    data = deepcopy(project)
    data.pop("_project_path", None)
    if data.get("portable_paths"):
        data["work_map"] = {key: _portable_path(value, target.parent) for key, value in data.get("work_map", {}).items()}
        data["media_roots"] = [_portable_path(value, target.parent) for value in data.get("media_roots", [])]
        for key in ("manifest_path", "catalog_path"):
            if data.get("chronology", {}).get(key):
                data["chronology"][key] = _portable_path(data["chronology"][key], target.parent)
    data["updated_at"] = datetime.now(timezone.utc).isoformat()
    data["app_version"] = __version__
    temp = target.with_suffix(target.suffix + ".tmp")
    temp.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
    os.replace(temp, target)
    project.update(data)
    return target


def load_project(path: str | Path) -> dict:
    target = Path(path)
    loaded = json.loads(target.read_text(encoding="utf-8"))
    if loaded.get("format") != "chronomonster-project-v1":
        raise ValueError("This is not a ChronoMonster v1 project")
    result = new_project()
    _deep_update(result, loaded)
    result["work_map"] = {key: _resolve_project_path(value, target.parent) for key, value in result.get("work_map", {}).items()}
    result["media_roots"] = [_resolve_project_path(value, target.parent) for value in result.get("media_roots", [])]
    for key in ("manifest_path", "catalog_path"):
        if result.get("chronology", {}).get(key):
            result["chronology"][key] = _resolve_project_path(result["chronology"][key], target.parent)
    result["_project_path"] = str(target.resolve())
    return result


def _deep_update(base: dict, incoming: dict) -> None:
    for key, value in incoming.items():
        if isinstance(value, dict) and isinstance(base.get(key), dict):
            _deep_update(base[key], value)
        else:
            base[key] = value


def _looks_windows_absolute(value: str) -> bool:
    return len(value) >= 3 and value[0].isalpha() and value[1] == ":" and value[2] in "\\/"


def _resolve_project_path(value: str, parent: Path) -> str:
    if not value:
        return value
    if Path(value).is_absolute() or _looks_windows_absolute(value) or value.startswith("\\\\"):
        return value
    return str((parent / value).resolve())


def _portable_path(value: str, parent: Path) -> str:
    if not value:
        return value
    try:
        return os.path.relpath(value, parent).replace("\\", "/")
    except ValueError:
        return value


def media_fingerprint(path: str | Path, probe: dict | None = None) -> dict:
    target = Path(path)
    stat = target.stat()
    return {
        "path": str(target.resolve()),
        "size": stat.st_size,
        "mtime_ns": stat.st_mtime_ns,
        "duration": None if not probe else probe.get("duration"),
    }


def fingerprint_matches(path: str | Path, fingerprint: dict | None) -> bool:
    if not fingerprint:
        return False
    target = Path(path)
    if not target.is_file():
        return False
    stat = target.stat()
    return (
        str(target.resolve()).lower() == str(fingerprint.get("path", "")).lower()
        and stat.st_size == fingerprint.get("size")
        and stat.st_mtime_ns == fingerprint.get("mtime_ns")
    )


def mapping_snapshot(project: dict) -> dict:
    return {
        "format": "chronomonster-mapping-v1",
        "chronology_sha256": project["chronology"]["sha256"],
        "work_map": project.get("work_map", {}),
        "manual_overrides": project.get("manual_overrides", {}),
        "disabled_steps": project.get("disabled_steps", []),
    }
