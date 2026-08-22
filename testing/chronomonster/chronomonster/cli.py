from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .audit import write_audit_xspf
from .build import MonsterBuilder, estimate_build
from .certification import certification_summary, certify_batch
from .chronology import assert_default_invariants, load_catalog, load_steps
from .media import match_catalog, probe_paths, scan_media
from .playlist import active_resolved_steps, write_companion_csv, write_ffconcat, write_xspf
from .project import load_project, new_project, save_project
from .subtitles import accept_subtitle_proposals, propose_subtitle_matches
from .validation import validate_project


def _load(path: str) -> tuple[dict, list[dict], list[dict]]:
    project = load_project(path)
    chronology = project.get("chronology", {})
    return project, load_steps(chronology.get("manifest_path") or None), load_catalog(chronology.get("catalog_path") or None)


def _progress(event: dict) -> None:
    phase = event.get("phase", "working")
    if "step" in event:
        print(f"[{phase}] {event['step']}/{event['total']} {event.get('title','')} {event.get('percent',0):.1f}%", flush=True)
    else:
        print(f"[{phase}] {event}", flush=True)


def cmd_new(args) -> int:
    project = new_project()
    project["media_roots"] = [str(Path(x).resolve()) for x in args.media]
    path = save_project(project, args.project)
    print(path)
    return 0


def cmd_scan(args) -> int:
    project, _, catalog = _load(args.project)
    roots = args.media or project.get("media_roots", [])
    if not roots:
        raise ValueError("Add at least one media folder")
    files = scan_media(roots)
    print(f"Found {len(files)} video files; probing…")
    probes = probe_paths(files, project.setdefault("probe_cache", {}), project.get("executables", {}).get("ffprobe", ""), lambda i, n, p: print(f"probe {i}/{n}: {p}"))
    candidates = match_catalog(catalog, files, probes)
    project["media_roots"] = [str(Path(x).resolve()) for x in roots]
    project["match_candidates"] = candidates
    accepted = 0
    used_paths = {str(Path(p).resolve()).lower() for p in project.get("work_map", {}).values() if p}
    for work_id, result in candidates.items():
        if result["status"] == "green" and result["candidates"]:
            candidate = result["candidates"][0]["path"]
            key = str(Path(candidate).resolve()).lower()
            if key not in used_paths:
                project.setdefault("work_map", {})[work_id] = candidate
                used_paths.add(key); accepted += 1
    save_project(project, args.project)
    print(f"Auto-mapped {accepted} high-confidence works. Review yellow/red rows in the app.")
    return 0


def cmd_validate(args) -> int:
    project, steps, catalog = _load(args.project)
    report = validate_project(project, steps, catalog, probe=not args.no_probe)
    if args.output:
        Path(args.output).write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k not in {"issues", "probes"}}, indent=2))
    for issue in report["issues"]:
        print(f"{issue['severity'].upper():6} {issue['code']}: {issue['message']} {issue.get('work_id','')} {issue.get('watch_step') or ''}")
    save_project(project, args.project)
    return 0 if report["certified"] else 2


def cmd_xspf(args) -> int:
    project, steps, _ = _load(args.project)
    report = write_xspf(project, steps, args.output, args.allow_incomplete)
    print(f"Wrote {report['entry_count']} entries to {args.output}")
    return 0


def cmd_ffconcat(args) -> int:
    project, steps, _ = _load(args.project)
    count = write_ffconcat(project, steps, args.output, args.allow_incomplete)
    print(f"Wrote {count} entries to {args.output} (experimental/keyframe-inexact when stream copied)")
    return 0


def cmd_csv(args) -> int:
    project, steps, _ = _load(args.project)
    print(f"Wrote {write_companion_csv(project, steps, args.output)} rows to {args.output}")
    return 0


def cmd_monster(args) -> int:
    project, steps, _ = _load(args.project)
    if args.encoder:
        project["monster_profile"]["video_codec"] = args.encoder
        if "nvenc" in args.encoder and project["monster_profile"].get("preset") == "medium":
            project["monster_profile"]["preset"] = "p5"
    builder = MonsterBuilder(project, steps, args.output, args.cache)
    resolved, probes, estimate = builder.preflight()
    print(json.dumps(estimate, indent=2))
    if not args.ignore_disk_estimate and not estimate["enough_free_space"]:
        raise RuntimeError("Preflight predicts insufficient free disk space. Use --ignore-disk-estimate only if you have another plan.")
    result = builder.build(volumes=args.volumes, max_volume_seconds=args.volume_hours * 3600, split_on_era=args.split_on_era, callback=_progress)
    save_project(project, args.project)
    print(json.dumps(result.__dict__, indent=2))
    return 0


def cmd_audit(args) -> int:
    project, steps, _ = _load(args.project)
    report = write_audit_xspf(project, steps, args.output, args.context, args.include_verified)
    save_project(project, args.project)
    print(f"Wrote {report['boundary_count']} exhaustive boundary previews to {args.output}")
    return 0


def cmd_certification(args) -> int:
    project, steps, _ = _load(args.project)
    summary = certification_summary(project, steps)
    print(json.dumps({k: v for k, v in summary.items() if k != "unverified_boundaries"}, indent=2))
    if args.output:
        Path(args.output).write_text(json.dumps(summary, indent=2), encoding="utf-8")
    return 0 if summary["unverified"] == 0 else 2


def cmd_certify_audit(args) -> int:
    project, steps, _ = _load(args.project)
    batch = project.get("last_boundary_audit", {})
    if not batch.get("keys"):
        raise ValueError("This project has no exported audit batch")
    count = certify_batch(project, steps, batch["keys"], "human_audit", {"audit_playlist": batch.get("playlist"), "audit_created_at": batch.get("created_at"), "user_attested_complete_review": True})
    save_project(project, args.project)
    print(f"Certified {count} boundaries from the explicitly attested audit batch")
    return 0


def cmd_subtitle_match(args) -> int:
    project, steps, _ = _load(args.project)
    result = propose_subtitle_matches(project, steps, args.work, args.minimum_gap)
    if args.accept:
        result["accepted_certificates"] = accept_subtitle_proposals(project, steps, args.work)
    save_project(project, args.project)
    print(json.dumps(result, indent=2))
    return 0


def cmd_invariants(_args) -> int:
    steps, catalog = load_steps(), load_catalog()
    assert_default_invariants(steps, catalog)
    print("PASS: 2,622 playback steps / 663 works / 45 whole files / 9 manual boundaries / Wakanda golden continuity")
    return 0


def cmd_ui(args) -> int:
    from .ui import run
    run(args.project)
    return 0


def parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(prog="chronomonster", description="Compile the absurdly precise MCU chronology into VLC playlists or exact MKVs.")
    sub = ap.add_subparsers(dest="command")
    p = sub.add_parser("ui", help="Open the desktop app"); p.add_argument("project", nargs="?"); p.set_defaults(func=cmd_ui)
    p = sub.add_parser("new", help="Create a project"); p.add_argument("project"); p.add_argument("--media", action="append", default=[]); p.set_defaults(func=cmd_new)
    p = sub.add_parser("scan", help="Scan, probe, and auto-match media"); p.add_argument("project"); p.add_argument("--media", action="append", default=[]); p.set_defaults(func=cmd_scan)
    p = sub.add_parser("validate", help="Validate mappings, editions, and boundaries"); p.add_argument("project"); p.add_argument("--no-probe", action="store_true"); p.add_argument("--output"); p.set_defaults(func=cmd_validate)
    p = sub.add_parser("xspf", help="Build VLC virtual-cut playlist"); p.add_argument("project"); p.add_argument("output"); p.add_argument("--allow-incomplete", action="store_true"); p.set_defaults(func=cmd_xspf)
    p = sub.add_parser("ffconcat", help="Write experimental stream-copy plan"); p.add_argument("project"); p.add_argument("output"); p.add_argument("--allow-incomplete", action="store_true"); p.set_defaults(func=cmd_ffconcat)
    p = sub.add_parser("csv", help="Write readable active checklist"); p.add_argument("project"); p.add_argument("output"); p.set_defaults(func=cmd_csv)
    p = sub.add_parser("monster", help="Build exact normalized MKV or volumes"); p.add_argument("project"); p.add_argument("output"); p.add_argument("--cache"); p.add_argument("--encoder", choices=["libx264", "h264_nvenc"], default=""); p.add_argument("--volumes", action="store_true"); p.add_argument("--volume-hours", type=float, default=8.0); p.add_argument("--split-on-era", action="store_true"); p.add_argument("--ignore-disk-estimate", action="store_true"); p.set_defaults(func=cmd_monster)
    p = sub.add_parser("audit", help="Write an exhaustive VLC boundary-review playlist"); p.add_argument("project"); p.add_argument("output"); p.add_argument("--context", type=float, default=4.0); p.add_argument("--include-verified", action="store_true"); p.set_defaults(func=cmd_audit)
    p = sub.add_parser("certification", help="Report exact verified/unverified boundary counts"); p.add_argument("project"); p.add_argument("--output"); p.set_defaults(func=cmd_certification)
    p = sub.add_parser("certify-audit", help="Attest that the last exported exhaustive audit was fully reviewed"); p.add_argument("project"); p.set_defaults(func=cmd_certify_audit)
    p = sub.add_parser("subtitle-match", help="Experimentally propose subtitle-gap matches for one mapped work"); p.add_argument("project"); p.add_argument("work"); p.add_argument("--minimum-gap", type=float, default=4.0); p.add_argument("--accept", action="store_true", help="Explicitly accept all exact-rule proposals for this work as experimental certificates"); p.set_defaults(func=cmd_subtitle_match)
    p = sub.add_parser("check-data", help="Verify bundled chronology invariants"); p.set_defaults(func=cmd_invariants)
    return ap


def main(argv: list[str] | None = None) -> int:
    ap = parser()
    args = ap.parse_args(argv)
    if not getattr(args, "command", None):
        args = ap.parse_args(["ui"])
    try:
        return int(args.func(args) or 0)
    except (KeyboardInterrupt, InterruptedError) as exc:
        print(str(exc) or "Cancelled", file=sys.stderr)
        return 130
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
