#!/usr/bin/env python3
from __future__ import annotations

import csv
import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from chronomonster.build import MonsterBuilder
from chronomonster.playlist import write_companion_csv, write_ffconcat, write_xspf
from chronomonster.project import new_project, save_project


def make_media(path: Path, color: str, frequency: int):
    subprocess.run([
        shutil.which("ffmpeg"), "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", f"color=c={color}:s=320x180:r=10:d=4",
        "-f", "lavfi", "-i", f"sine=frequency={frequency}:sample_rate=48000:duration=4",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", str(path),
    ], check=True)


def main():
    demo = ROOT / "demo"; media = demo / "media"; output = demo / "output"; cache = demo / "resume-cache"
    media.mkdir(parents=True, exist_ok=True); output.mkdir(exist_ok=True)
    make_media(media / "A-red.mkv", "red", 440); make_media(media / "B-blue.mkv", "blue", 880)
    steps = [
        {"watch_step": 1, "work_id": "DEMO-A", "parent_title": "Synthetic Red A", "medium": "Demo", "series_film": "Synthetic A", "season": "", "episode": "", "playback_mode": "range", "source_start_tc": "00:00:00.200", "source_end_tc": "00:00:01.200", "start_seconds": .2, "end_seconds": 1.2, "duration_seconds": 1, "strict_rank_start": 1, "strict_rank_end": 1, "collapsed_fragment_count": 1, "scene_ids": ["DEMO-1"], "scene_titles": ["Red"], "era_labels": ["Demo"], "recommended_scope": "Core"},
        {"watch_step": 2, "work_id": "DEMO-B", "parent_title": "Synthetic Blue B", "medium": "Demo", "series_film": "Synthetic B", "season": "", "episode": "", "playback_mode": "range", "source_start_tc": "00:00:00.500", "source_end_tc": "00:00:01.500", "start_seconds": .5, "end_seconds": 1.5, "duration_seconds": 1, "strict_rank_start": 2, "strict_rank_end": 2, "collapsed_fragment_count": 1, "scene_ids": ["DEMO-2"], "scene_titles": ["Blue"], "era_labels": ["Demo"], "recommended_scope": "Core"},
        {"watch_step": 3, "work_id": "DEMO-A", "parent_title": "Synthetic Red A Again", "medium": "Demo", "series_film": "Synthetic A", "season": "", "episode": "", "playback_mode": "range", "source_start_tc": "00:00:02.000", "source_end_tc": "00:00:03.000", "start_seconds": 2, "end_seconds": 3, "duration_seconds": 1, "strict_rank_start": 3, "strict_rank_end": 3, "collapsed_fragment_count": 1, "scene_ids": ["DEMO-3"], "scene_titles": ["Red again"], "era_labels": ["Demo"], "recommended_scope": "Core"},
    ]
    manifest = demo / "synthetic_steps.json"; manifest.write_text(json.dumps(steps, indent=2), encoding="utf-8")
    catalog = demo / "synthetic_catalog.csv"
    fields = ["work_id", "watch_item", "medium", "series_film", "season", "episode", "recommended_scope", "canon_tier", "final_status", "scene_count", "required_min_duration_seconds", "required_min_duration_tc", "has_manual_boundary", "has_whole_file_block"]
    with catalog.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields); writer.writeheader()
        writer.writerow({"work_id":"DEMO-A","watch_item":"Synthetic Red A","medium":"Demo","series_film":"Synthetic A","recommended_scope":"Core","scene_count":2,"required_min_duration_seconds":3})
        writer.writerow({"work_id":"DEMO-B","watch_item":"Synthetic Blue B","medium":"Demo","series_film":"Synthetic B","recommended_scope":"Core","scene_count":1,"required_min_duration_seconds":1.5})
    project = new_project(); project["portable_paths"] = True
    project["chronology"].update({"name":"Synthetic A → B → A acceptance demo","steps":3,"works":2,"sha256":"synthetic-a-b-a-demo","manifest_path":str(manifest),"catalog_path":str(catalog)})
    project["media_roots"] = [str(media)]; project["work_map"] = {"DEMO-A": str(media / "A-red.mkv"), "DEMO-B": str(media / "B-blue.mkv")}
    project["monster_profile"].update({"name":"synthetic-test","width":320,"height":180,"fps":10,"crf":28,"preset":"ultrafast","audio_bitrate":"96k"})
    write_xspf(project, steps, output / "synthetic-A-B-A.xspf")
    write_ffconcat(project, steps, output / "synthetic-A-B-A.experimental.ffconcat")
    write_companion_csv(project, steps, output / "synthetic-A-B-A.checklist.csv")
    result = MonsterBuilder(project, steps, output / "synthetic-A-B-A.monster.mkv", cache).build()
    # Prove a full cache-resume pass in the retained receipt/checkpoint.
    second = MonsterBuilder(project, steps, output / "synthetic-A-B-A.monster.mkv", cache).build()
    project["outputs"] = {"last_xspf":"output/synthetic-A-B-A.xspf","last_monster":["output/synthetic-A-B-A.monster.mkv"]}
    save_project(project, demo / "Synthetic_Demo.chronomonster.json")
    (demo / "README.md").write_text(
        "# Synthetic acceptance demo\n\nOpen `Synthetic_Demo.chronomonster.json` in the app. It uses tiny generated red/blue media and a three-step A → B → A chronology. The output folder contains the XSPF, exact three-second monster MKV, three chapters, HTML browser, reports, and receipt. The retained resume cache completed a second pass with three cache hits.\n",
        encoding="utf-8",
    )
    print(json.dumps(second.__dict__, indent=2))


if __name__ == "__main__": main()

