from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

import pytest

from chronomonster.build import MonsterBuilder
from chronomonster.media import probe_media
from chronomonster.playlist import write_xspf
from chronomonster.project import new_project


pytestmark = pytest.mark.skipif(not (shutil.which("ffmpeg") and shutil.which("ffprobe")), reason="FFmpeg is not installed")


def make_media(path: Path, color: str, frequency: int):
    command = [
        shutil.which("ffmpeg"), "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", f"color=c={color}:s=320x180:r=10:d=4",
        "-f", "lavfi", "-i", f"sine=frequency={frequency}:sample_rate=48000:duration=4",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", str(path),
    ]
    subprocess.run(command, check=True)


def fake_step(number, work, title, start, end):
    return {
        "watch_step": number, "work_id": work, "parent_title": title, "playback_mode": "range",
        "start_seconds": start, "end_seconds": end, "source_start_tc": "", "source_end_tc": "",
        "strict_rank_start": number, "strict_rank_end": number, "recommended_scope": "Core",
        "era_labels": ["Demo"], "scene_ids": [f"DEMO-{number}"],
    }


def test_exact_a_b_a_build_chapters_and_resume(tmp_path):
    a, b = tmp_path / "A.mkv", tmp_path / "B.mkv"
    make_media(a, "red", 440); make_media(b, "blue", 880)
    steps = [fake_step(1, "A", "Red A", 0.2, 1.2), fake_step(2, "B", "Blue B", 0.5, 1.5), fake_step(3, "A", "Red A again", 2.0, 3.0)]
    project = new_project(); project["chronology"]["sha256"] = "synthetic-demo"
    project["work_map"] = {"A": str(a), "B": str(b)}
    project["monster_profile"].update({"name": "test", "width": 320, "height": 180, "fps": 10, "crf": 28, "preset": "ultrafast", "audio_bitrate": "96k"})
    xspf = tmp_path / "demo.xspf"
    assert write_xspf(project, steps, xspf)["entry_count"] == 3
    out = tmp_path / "monster.mkv"
    builder = MonsterBuilder(project, steps, out, tmp_path / "cache")
    first = builder.build()
    assert first.cache_hits == 0 and first.segments == 3
    probe = probe_media(out)
    assert abs(probe["duration"] - 3.0) <= 0.30
    raw = subprocess.run([shutil.which("ffprobe"), "-v", "error", "-show_chapters", "-of", "json", str(out)], capture_output=True, text=True, check=True)
    chapters = json.loads(raw.stdout)["chapters"]
    assert len(chapters) == 3
    assert chapters[0]["tags"]["title"].startswith("#0001")
    second = MonsterBuilder(project, steps, out, tmp_path / "cache").build()
    assert second.cache_hits == 3
    volumes = MonsterBuilder(project, steps, tmp_path / "volumes.mkv", tmp_path / "cache").build(volumes=True, max_volume_seconds=1.5)
    assert len(volumes.output_files) == 3 and Path(volumes.volume_index).is_file()

