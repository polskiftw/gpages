from __future__ import annotations

import json
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest

from chronomonster.build import MonsterBuilder, build_segment_command
from chronomonster.audit import write_audit_xspf
from chronomonster.calibration import resolved_step_range, set_calibration, transform_time
from chronomonster.certification import boundary_descriptors, certification_summary, certify_boundary
from chronomonster.chapters import chapter_rows
from chronomonster.chronology import assert_default_invariants, load_catalog, load_steps, merge_continuous_rows, selected_steps
from chronomonster.media import normalize_title, score_candidate
from chronomonster.playlist import VLC, XSPF, file_uri, write_xspf
from chronomonster.project import load_project, new_project, save_project
from chronomonster.timecode import format_timecode, parse_timecode
from chronomonster.validation import validate_project
from chronomonster.subtitles import parse_srt_intervals, subtitle_gaps


def step(number=1, mode="range", start=1, end=3, scope="Core", work_id="WTEST"):
    return {
        "watch_step": number, "work_id": work_id, "parent_title": "A & B <Test>",
        "playback_mode": mode, "start_seconds": start, "end_seconds": end,
        "source_start_tc": "00:00:01" if start is not None else "", "source_end_tc": "00:00:03" if end is not None else "",
        "strict_rank_start": number, "strict_rank_end": number, "recommended_scope": scope,
        "era_labels": ["Test"], "scene_ids": [f"S{number}"],
    }


def test_bundled_invariants():
    assert_default_invariants(load_steps(), load_catalog())


def test_timecode_parse_and_format():
    assert parse_timecode("01:02:03.500") == 3723.5
    assert parse_timecode("2:03") == 123
    assert parse_timecode("12.25") == 12.25
    assert format_timecode(3723.5, True) == "01:02:03.500"
    with pytest.raises(ValueError): parse_timecode("nope")


def test_exact_continuity_merge_rule():
    rows = [
        {"Work ID": "A", "Source Start TC": "00:00:00", "Source End TC": "00:01:00"},
        {"Work ID": "A", "Source Start TC": "00:01:00", "Source End TC": "00:02:00"},
        {"Work ID": "A", "Source Start TC": "00:02:01", "Source End TC": "00:03:00"},
        {"Work ID": "B", "Source Start TC": "00:03:00", "Source End TC": "00:04:00"},
    ]
    assert [len(g) for g in merge_continuous_rows(rows)] == [2, 1, 1]


def test_windows_and_unc_file_uri():
    assert file_uri(r"C:\MCU\A Movie #1.mkv") == "file:///C:/MCU/A%20Movie%20%231.mkv"
    assert file_uri(r"\\server\share\A Movie.mkv") == "file://server/share/A%20Movie.mkv"


def test_xspf_modes_and_xml_escaping(tmp_path):
    media = tmp_path / "A #1.mkv"; media.touch()
    project = new_project(); project["work_map"] = {"WTEST": str(media)}; project["preferences"]["strict_boundary_certification"] = False
    report = write_xspf(project, [step()], tmp_path / "out.xspf")
    assert report["entry_count"] == 1
    root = ET.parse(tmp_path / "out.xspf").getroot()
    title = root.find(f".//{{{XSPF}}}track/{{{XSPF}}}title").text
    options = [n.text for n in root.findall(f".//{{{VLC}}}option")]
    assert "A & B <Test>" in title
    assert options == ["start-time=1", "stop-time=3"]
    whole = step(mode="whole_file", start=None, end=None)
    write_xspf(project, [whole], tmp_path / "whole.xspf")
    assert not ET.parse(tmp_path / "whole.xspf").getroot().findall(f".//{{{VLC}}}option")
    manual = step(mode="manual_boundary_required", start=None, end=None)
    with pytest.raises(ValueError, match="unresolved manual boundaries"):
        write_xspf(project, [manual], tmp_path / "manual.xspf")
    project["manual_overrides"] = {"1": {"start_seconds": 5, "end_seconds": 7}}
    assert write_xspf(project, [manual], tmp_path / "resolved.xspf")["entry_count"] == 1


def test_scope_filtering():
    steps = [step(1, scope="Core"), step(2, scope="Completionist"), step(3, scope="Optional")]
    assert len(selected_steps(steps, "all")) == 3
    assert len(selected_steps(steps, "core")) == 1
    assert len(selected_steps(steps, "core_completionist")) == 2
    assert [s["watch_step"] for s in selected_steps(steps, "all", [2])] == [1, 3]


def test_matcher_normalization_and_episode():
    work = {"watch_item": "Loki — S01E03", "series_film": "Loki", "season": "1", "episode": "3", "required_min_duration_seconds": "1000"}
    right = score_candidate(work, "Loki.S01E03.2160p.WEB-DL.x265.mkv", 3000)
    wrong = score_candidate(work, "Loki.S01E04.2160p.mkv", 3000)
    assert normalize_title("Spider-Man: No Way Home [2160p]") == "spider man no way home"
    assert right > wrong


def test_project_roundtrip(tmp_path):
    project = new_project(); project["work_map"]["W1"] = "D:/MCU/Test.mkv"; project["disabled_steps"] = [42]
    path = save_project(project, tmp_path / "test.chronomonster.json")
    loaded = load_project(path)
    assert loaded["work_map"] == project["work_map"] and loaded["disabled_steps"] == [42]


def test_duration_validation_without_probe(tmp_path):
    media = tmp_path / "a.mkv"; media.touch()
    project = new_project(); project["work_map"] = {"WTEST": str(media)}
    bad = step(start=4, end=2)
    report = validate_project(project, [bad], [{"work_id": "WTEST"}], probe=False)
    assert not report["certified"]
    assert any(i["code"] == "invalid_range" for i in report["issues"])


def test_calibration_affine_piecewise_and_nudge():
    project = new_project()
    set_calibration(project, "WTEST", [{"reference_seconds": 10, "local_seconds": 17}], "affine")
    assert transform_time(project, "WTEST", 50) == 57
    set_calibration(project, "WTEST", [{"reference_seconds": 0, "local_seconds": 5}, {"reference_seconds": 100, "local_seconds": 103}], "affine")
    assert transform_time(project, "WTEST", 50) == 54
    set_calibration(project, "WTEST", [{"reference_seconds": 0, "local_seconds": 0}, {"reference_seconds": 50, "local_seconds": 50}, {"reference_seconds": 100, "local_seconds": 110}], "piecewise")
    assert transform_time(project, "WTEST", 75) == 80
    project["boundary_nudges"]["WTEST@ref:75.000000"] = 81.25
    assert resolved_step_range(project, step(start=75, end=90))[0] == 81.25


def test_strict_boundary_ledger_and_invalidation(tmp_path):
    media = tmp_path / "a.mkv"; media.write_bytes(b"media")
    project = new_project(); project["work_map"] = {"WTEST": str(media)}
    steps = [step(start=1, end=3)]
    summary = certification_summary(project, steps)
    assert summary["required"] == 2 and summary["unverified"] == 2
    for boundary in boundary_descriptors(project, steps):
        certify_boundary(project, boundary, "human")
    assert certification_summary(project, steps)["unverified"] == 0
    set_calibration(project, "WTEST", [{"reference_seconds": 1, "local_seconds": 2}], "affine")
    assert certification_summary(project, steps)["unverified"] == 2


def test_exhaustive_audit_deduplicates_shared_boundaries(tmp_path):
    media = tmp_path / "a.mkv"; media.write_bytes(b"media")
    project = new_project(); project["work_map"] = {"WTEST": str(media)}
    steps = [step(1, start=1, end=3), step(2, start=3, end=5)]
    report = write_audit_xspf(project, steps, tmp_path / "audit.xspf")
    assert report["boundary_count"] == 3
    assert (tmp_path / "audit.audit.json").is_file()


def test_subtitle_gap_parser():
    srt = """1\n00:00:01,000 --> 00:00:02,000\na\n\n2\n00:00:08,000 --> 00:00:09,000\nb\n"""
    intervals = parse_srt_intervals(srt)
    assert intervals == [(1.0, 2.0), (8.0, 9.0)]
    assert subtitle_gaps(intervals, 4.0) == [{"start": 2.0, "end": 8.0, "duration": 6.0, "midpoint": 5.0}]


def test_chapter_cumulative_timing():
    steps = [dict(step(1, start=1, end=3), media_path="a"), dict(step(2, mode="whole_file", start=None, end=None), media_path="b")]
    for s in steps:
        s["resolved_start_seconds"], s["resolved_end_seconds"] = s["start_seconds"], s["end_seconds"]
    rows = chapter_rows(steps, {"WTEST": 5})
    assert [(x["start_seconds"], x["end_seconds"]) for x in rows] == [(0, 2), (2, 7)]


def test_ffmpeg_command_exact_cut_and_normalization(tmp_path):
    s = dict(step(start=1.25, end=2.75), media_path="in.mkv", resolved_start_seconds=1.25, resolved_end_seconds=2.75)
    command = build_segment_command("ffmpeg", s, tmp_path / "out.mkv", new_project()["monster_profile"], 1)
    assert command[command.index("-ss") + 1] == "1.250000"
    assert command[command.index("-t") + 1] == "1.500000"
    assert "force_original_aspect_ratio=decrease" in command[command.index("-vf") + 1]
    assert command[command.index("-map") + 1] == "0:v:0"


def test_volume_grouping_never_splits_steps():
    steps = []
    for i in range(3):
        s = dict(step(i + 1, start=0, end=4), resolved_start_seconds=0, resolved_end_seconds=4)
        steps.append(s)
    assert MonsterBuilder._volume_groups(steps, {"WTEST": 99}, 7, False) == [[0], [1], [2]]
