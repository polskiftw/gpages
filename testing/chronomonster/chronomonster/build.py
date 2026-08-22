from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import threading
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable

from .chapters import chapter_rows, write_chapter_sidecars
from .certification import certification_summary
from .chronology import step_range
from .media import choose_audio_stream, find_executable, probe_media, probe_with_cache
from .playlist import active_resolved_steps
from .timecode import format_timecode


Progress = Callable[[dict], None]


def _emit(callback: Progress | None, **payload) -> None:
    if callback:
        callback(payload)


def ffmpeg_version(executable: str) -> str:
    completed = subprocess.run([executable, "-version"], capture_output=True, text=True, errors="replace")
    return (completed.stdout or completed.stderr).splitlines()[0] if (completed.stdout or completed.stderr) else "unknown"


def ffmpeg_has_encoder(executable: str, encoder: str) -> bool:
    completed = subprocess.run([executable, "-hide_banner", "-encoders"], capture_output=True, text=True, errors="replace")
    return completed.returncode == 0 and encoder in completed.stdout


def _segment_identity(step: dict, profile: dict, probe: dict, ffmpeg_ver: str) -> str:
    payload = {
        "source": {"path": probe["path"], "size": probe["size"], "mtime_ns": probe["mtime_ns"]},
        "watch_step": step["watch_step"], "start": step.get("resolved_start_seconds"),
        "end": step.get("resolved_end_seconds"), "profile": profile, "ffmpeg": ffmpeg_ver,
    }
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def build_segment_command(ffmpeg: str, step: dict, output: Path, profile: dict, audio_stream_index: int) -> list[str]:
    width = int(profile.get("width", 1920))
    height = int(profile.get("height", 1080))
    fps = profile.get("fps", 30)
    vf = (
        f"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,"
        f"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps={fps},format=yuv420p"
    )
    command = [ffmpeg, "-hide_banner", "-nostdin", "-y", "-i", step["media_path"]]
    start = step.get("resolved_start_seconds")
    end = step.get("resolved_end_seconds")
    if start is not None:
        command += ["-ss", f"{start:.6f}"]
    if start is not None and end is not None:
        command += ["-t", f"{end - start:.6f}"]
    command += [
        "-map", "0:v:0", "-map", f"0:{audio_stream_index}", "-vf", vf,
        "-af", f"aresample={int(profile.get('audio_rate', 48000))}",
    ]
    codec = profile.get("video_codec", "libx264")
    command += ["-c:v", codec]
    if "nvenc" in codec:
        command += ["-preset", str(profile.get("preset", "p5")), "-cq", str(profile.get("crf", 19)), "-b:v", "0"]
    else:
        command += ["-preset", str(profile.get("preset", "medium")), "-crf", str(profile.get("crf", 18))]
    command += [
        "-pix_fmt", "yuv420p", "-c:a", profile.get("audio_codec", "aac"),
        "-b:a", str(profile.get("audio_bitrate", "192k")), "-ar", str(profile.get("audio_rate", 48000)),
        "-ac", str(profile.get("audio_channels", 2)), "-avoid_negative_ts", "make_zero",
        "-progress", "pipe:1", "-nostats", str(output),
    ]
    return command


def _run_progress_process(command: list[str], expected_duration: float, cancel: threading.Event | None, callback: Progress | None, base: dict) -> None:
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace")
    speed = ""
    try:
        assert process.stdout is not None
        for line in process.stdout:
            if cancel and cancel.is_set():
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                raise InterruptedError("Build cancelled safely. Completed segment cache was retained.")
            key, _, value = line.strip().partition("=")
            if key == "speed":
                speed = value
            if key in {"out_time_us", "out_time_ms"}:
                raw = float(value or 0)
                seconds = raw / 1_000_000.0
                percent = min(100.0, seconds / max(expected_duration, 0.001) * 100)
                _emit(callback, **base, phase="encoding", percent=percent, speed=speed)
        stderr = process.stderr.read() if process.stderr else ""
        code = process.wait()
        if code:
            raise RuntimeError(f"FFmpeg segment encode failed ({code}):\n{stderr[-5000:]}")
    finally:
        if process.poll() is None:
            process.kill()


def _concat_escape(path: Path) -> str:
    return str(path.resolve()).replace("\\", "/").replace("'", "'\\''")


def estimate_build(resolved: list[dict], probes: dict[str, dict], profile: dict, free_at: str | Path | None = None) -> dict:
    total = 0.0
    unknown = 0
    for step in resolved:
        start, end = step.get("resolved_start_seconds"), step.get("resolved_end_seconds")
        if start is not None and end is not None:
            total += end - start
        else:
            duration = probes.get(step["work_id"], {}).get("duration")
            if duration:
                total += duration
            else:
                unknown += 1
    # A conservative estimate: target video Mbps + selected audio and 20% segment/mux headroom.
    width, height, fps = int(profile.get("width", 1920)), int(profile.get("height", 1080)), float(profile.get("fps", 30))
    video_mbps = max(2.5, min(30.0, width * height * fps / 20_000_000 * 2.2))
    audio_mbps = 0.192
    final_bytes = int(total * (video_mbps + audio_mbps) * 1_000_000 / 8)
    working_bytes = int(final_bytes * 2.2)
    free = shutil.disk_usage(Path(free_at or ".").resolve()).free
    return {
        "runtime_seconds": total, "runtime_human": format_timecode(total), "unknown_durations": unknown,
        "estimated_final_bytes": final_bytes, "estimated_working_bytes": working_bytes,
        "free_bytes": free, "enough_free_space": free >= working_bytes,
        "message": f"This may consume {_human_bytes(working_bytes)} while building. You did ask for this.",
    }


def _human_bytes(value: int) -> str:
    size = float(value)
    for unit in ("B", "KB", "MB", "GB", "TB", "PB"):
        if size < 1024 or unit == "PB":
            return f"{size:.1f} {unit}"
        size /= 1024
    return f"{size:.1f} PB"


@dataclass
class BuildResult:
    output_files: list[str]
    segments: int
    cache_hits: int
    runtime_seconds: float
    checkpoint: str
    receipt: str
    volume_index: str | None = None


class MonsterBuilder:
    def __init__(self, project: dict, steps: list[dict], output: str | Path, cache_dir: str | Path | None = None):
        self.project = project
        self.steps = steps
        self.output = Path(output).resolve()
        self.cache_dir = Path(cache_dir).resolve() if cache_dir else self.output.parent / ".chronomonster-cache"
        self.cancel_event = threading.Event()
        self.ffmpeg = find_executable("ffmpeg", project.get("executables", {}).get("ffmpeg", ""))
        self.ffprobe = find_executable("ffprobe", project.get("executables", {}).get("ffprobe", ""))
        if not self.ffmpeg or not self.ffprobe:
            raise FileNotFoundError("FFmpeg and ffprobe are required for monster builds")

    def cancel(self) -> None:
        self.cancel_event.set()

    def preflight(self) -> tuple[list[dict], dict[str, dict], dict]:
        resolved = active_resolved_steps(self.project, self.steps, allow_unresolved=False)
        codec = self.project["monster_profile"].get("video_codec", "libx264")
        if not ffmpeg_has_encoder(self.ffmpeg, codec):
            raise ValueError(f"The selected FFmpeg does not provide encoder {codec}. Choose libx264 or install a build with the requested hardware encoder.")
        probes: dict[str, dict] = {}
        cache = self.project.setdefault("probe_cache", {})
        for step in resolved:
            work_id = step["work_id"]
            if work_id not in probes:
                probe, _ = probe_with_cache(step["media_path"], cache, self.ffprobe)
                if not probe.get("video"):
                    raise ValueError(f"{step['parent_title']} has no video stream")
                if not probe.get("audio"):
                    raise ValueError(f"{step['parent_title']} has no audio stream; exact monster mode requires audio")
                probes[work_id] = probe
            start, end = step.get("resolved_start_seconds"), step.get("resolved_end_seconds")
            if end is not None and end > probes[work_id]["duration"] + 1.0:
                raise ValueError(f"Source step {step['watch_step']} ends after the mapped file")
        estimate = estimate_build(resolved, probes, self.project["monster_profile"], self.output.parent)
        certification = certification_summary(self.project, self.steps, probes)
        if self.project.get("preferences", {}).get("strict_boundary_certification", True) and certification["unverified"]:
            raise ValueError(f"Exact build blocked: {certification['unverified']} of {certification['required']} unique boundaries are unverified")
        if estimate["unknown_durations"]:
            raise ValueError("One or more whole-file steps have unknown duration")
        return resolved, probes, estimate

    def build(self, volumes: bool = False, max_volume_seconds: float = 8 * 3600, split_on_era: bool = False, callback: Progress | None = None) -> BuildResult:
        self.output.parent.mkdir(parents=True, exist_ok=True)
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        segments_dir = self.cache_dir / "segments"
        segments_dir.mkdir(exist_ok=True)
        resolved, probes, estimate = self.preflight()
        profile = self.project["monster_profile"]
        version = ffmpeg_version(self.ffmpeg)
        checkpoint_path = self.cache_dir / "checkpoint.json"
        receipt_path = self.output.with_suffix(".build-receipt.json")
        commands = []
        completed = []
        cache_hits = 0
        finished_runtime = 0.0
        for ordinal, step in enumerate(resolved, 1):
            if self.cancel_event.is_set():
                raise InterruptedError("Build cancelled safely. Completed segment cache was retained.")
            probe = probes[step["work_id"]]
            identity = _segment_identity(step, profile, probe, version)
            segment = segments_dir / f"{ordinal:04d}-{identity[:16]}.mkv"
            start, end = step.get("resolved_start_seconds"), step.get("resolved_end_seconds")
            expected = (end - start) if start is not None and end is not None else float(probe["duration"])
            valid_cache = False
            if segment.is_file():
                try:
                    cached_probe = probe_media(segment, self.ffprobe)
                    valid_cache = bool(cached_probe.get("video") and cached_probe.get("audio") and abs(cached_probe["duration"] - expected) <= max(0.25, 1 / float(profile.get("fps", 30)) * 2))
                except Exception:
                    valid_cache = False
            audio_index = choose_audio_stream(probe, self.project.get("preferences", {}).get("audio_language", "eng"), self.project.get("preferences", {}).get("audio_stream", "default"))
            if audio_index is None:
                raise ValueError(f"No usable audio stream for {step['parent_title']}")
            command = build_segment_command(self.ffmpeg, step, segment, profile, audio_index)
            commands.append(command)
            base = {"step": ordinal, "total": len(resolved), "title": step["parent_title"], "source_step": step["watch_step"], "cache_hits": cache_hits, "finished_runtime": finished_runtime}
            if valid_cache:
                cache_hits += 1
                base["cache_hits"] = cache_hits
                _emit(callback, **base, phase="cache_hit", percent=100.0)
            else:
                partial = segment.with_suffix(".partial.mkv")
                command[-1] = str(partial)
                _run_progress_process(command, expected, self.cancel_event, callback, base)
                verified = probe_media(partial, self.ffprobe)
                if not verified.get("video") or not verified.get("audio") or abs(verified["duration"] - expected) > max(0.25, 2 / float(profile.get("fps", 30))):
                    raise RuntimeError(f"Segment {ordinal} failed duration/stream verification: expected {expected:.3f}s, got {verified.get('duration')}")
                os.replace(partial, segment)
            completed.append({"ordinal": ordinal, "watch_step": step["watch_step"], "identity": identity, "path": str(segment), "duration": expected, "sha256": _sha256_file(segment)})
            finished_runtime += expected
            checkpoint = {
                "format": "chronomonster-checkpoint-v1", "updated_at": datetime.now(timezone.utc).isoformat(),
                "output": str(self.output), "profile": profile, "completed": completed, "cache_hits": cache_hits,
            }
            checkpoint_path.write_text(json.dumps(checkpoint, indent=2), encoding="utf-8")
        duration_lookup = {wid: float(p["duration"]) for wid, p in probes.items()}
        groups = self._volume_groups(resolved, duration_lookup, max_volume_seconds, split_on_era) if volumes else [list(range(len(resolved)))]
        output_files = []
        volume_records = []
        for volume_number, indices in enumerate(groups, 1):
            volume_steps = [resolved[i] for i in indices]
            volume_segments = [Path(completed[i]["path"]) for i in indices]
            chapters = chapter_rows(volume_steps, duration_lookup)
            if volumes:
                target = self.output.with_name(f"{self.output.stem}.volume-{volume_number:03d}{self.output.suffix or '.mkv'}")
            else:
                target = self.output if self.output.suffix else self.output.with_suffix(".mkv")
            base = target.with_suffix("")
            ffmeta, chapters_json, chapters_html = write_chapter_sidecars(chapters, base)
            concat_file = base.with_suffix(".segments.ffconcat")
            concat_file.write_text("ffconcat version 1.0\n" + "\n".join(f"file '{_concat_escape(p)}'" for p in volume_segments) + "\n", encoding="utf-8")
            mux_command = [self.ffmpeg, "-hide_banner", "-nostdin", "-y", "-f", "concat", "-safe", "0", "-i", str(concat_file), "-i", str(ffmeta), "-map", "0:v:0", "-map", "0:a:0", "-map_metadata", "1", "-map_chapters", "1", "-c", "copy", str(target)]
            commands.append(mux_command)
            _emit(callback, phase="muxing", volume=volume_number, volumes=len(groups), target=str(target), percent=0)
            completed_mux = subprocess.run(mux_command, capture_output=True, text=True, encoding="utf-8", errors="replace")
            if completed_mux.returncode:
                raise RuntimeError(f"Final mux failed:\n{completed_mux.stderr[-5000:]}")
            final_probe = probe_media(target, self.ffprobe)
            expected_total = sum(c["end_seconds"] - c["start_seconds"] for c in chapters)
            if not final_probe.get("video") or not final_probe.get("audio") or abs(final_probe["duration"] - expected_total) > max(0.5, len(chapters) / float(profile.get("fps", 30)) * 0.5):
                raise RuntimeError(f"Final output verification failed for {target}")
            output_files.append(str(target))
            volume_records.append({"volume": volume_number, "path": str(target), "steps": len(indices), "first_source_step": volume_steps[0]["watch_step"], "last_source_step": volume_steps[-1]["watch_step"], "duration_seconds": expected_total})
            _emit(callback, phase="volume_complete", volume=volume_number, volumes=len(groups), target=str(target), percent=100)
        volume_index_path = None
        if volumes:
            volume_index_path = str(self.output.with_suffix(".volumes.json"))
            Path(volume_index_path).write_text(json.dumps({"format": "chronomonster-volumes-v1", "volumes": volume_records}, indent=2), encoding="utf-8")
            self.output.with_suffix(".volumes.txt").write_text("\n".join(f"Volume {v['volume']:03d}: {format_timecode(v['duration_seconds'])} — source steps {v['first_source_step']}–{v['last_source_step']} — {v['path']}" for v in volume_records) + "\n", encoding="utf-8")
        receipt = {
            "format": "chronomonster-build-receipt-v1", "generated_at": datetime.now(timezone.utc).isoformat(),
            "chronology_sha256": self.project["chronology"]["sha256"], "ffmpeg_version": version,
            "profile": profile, "estimate": estimate, "segments": completed, "commands": commands,
            "boundary_certification": certification_summary(self.project, self.steps, probes),
            "boundary_certificates": self.project.get("boundary_certifications", {}),
            "outputs": output_files, "volumes": volume_records,
        }
        receipt_path.write_text(json.dumps(receipt, indent=2, ensure_ascii=False), encoding="utf-8")
        return BuildResult(output_files, len(completed), cache_hits, estimate["runtime_seconds"], str(checkpoint_path), str(receipt_path), volume_index_path)

    @staticmethod
    def _volume_groups(steps: list[dict], durations: dict[str, float], max_seconds: float, split_on_era: bool) -> list[list[int]]:
        groups: list[list[int]] = []
        current: list[int] = []
        runtime = 0.0
        current_era = None
        for index, step in enumerate(steps):
            start, end = step.get("resolved_start_seconds"), step.get("resolved_end_seconds")
            duration = (end - start) if start is not None and end is not None else durations[step["work_id"]]
            era = (step.get("era_labels") or [""])[0]
            boundary = bool(current and ((runtime + duration > max_seconds) or (split_on_era and current_era and era != current_era)))
            if boundary:
                groups.append(current)
                current, runtime = [], 0.0
            current.append(index)
            runtime += duration
            current_era = era
        if current:
            groups.append(current)
        return groups
