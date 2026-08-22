from __future__ import annotations

import json
import os
import subprocess
import sys
import threading
from datetime import datetime, timedelta
from pathlib import Path
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from .build import MonsterBuilder, estimate_build
from .calibration import resolved_step_range
from .certification import certification_summary
from .certification_ui import CalibrationDialog, CertificationResolver
from .chronology import load_catalog, load_steps, selected_steps, step_range
from .media import find_executable, match_catalog, probe_paths, scan_media
from .playlist import active_resolved_steps, write_companion_csv, write_ffconcat, write_xspf
from .project import load_project, mapping_snapshot, media_fingerprint, new_project, save_project
from .timecode import format_timecode, parse_timecode
from .validation import validate_project


BG = "#111714"
PANEL = "#19251f"
GREEN = "#4fb477"
RED = "#e05b5b"
GOLD = "#e2b64d"
TEXT = "#f2f4f2"
MUTED = "#aab7af"


class BoundaryResolver(tk.Toplevel):
    def __init__(self, app: "App"):
        super().__init__(app.root)
        self.app = app
        self.title("Resolve the nine edition-specific tag scenes")
        self.geometry("920x560")
        self.configure(bg=BG)
        self.steps = [s for s in app.steps if s["playback_mode"] == "manual_boundary_required"]
        self.selected: dict | None = None
        left = tk.Frame(self, bg=BG); left.pack(side="left", fill="y", padx=12, pady=12)
        right = tk.Frame(self, bg=PANEL); right.pack(side="right", fill="both", expand=True, padx=(0, 12), pady=12)
        tk.Label(left, text="MANUAL BOUNDARIES", bg=BG, fg=GOLD, font=("Segoe UI", 13, "bold")).pack(anchor="w")
        self.listbox = tk.Listbox(left, width=40, height=24, bg="#0d120f", fg=TEXT, selectbackground="#285b42", font=("Segoe UI", 10))
        self.listbox.pack(fill="y", expand=True, pady=(8, 0))
        for step in self.steps:
            resolved = step_range(step, app.project.get("manual_overrides", {}))[0] is not None
            self.listbox.insert("end", f"{'✓' if resolved else '⏱'} #{step['watch_step']}  {step['parent_title']}")
        self.listbox.bind("<<ListboxSelect>>", self._select)
        self.heading = tk.Label(right, text="Choose one of the nine scenes", bg=PANEL, fg=TEXT, font=("Segoe UI", 17, "bold"), wraplength=540, justify="left")
        self.heading.pack(anchor="w", padx=18, pady=(18, 4))
        self.notes = tk.Label(right, text="", bg=PANEL, fg=MUTED, font=("Segoe UI", 10), wraplength=600, justify="left")
        self.notes.pack(anchor="w", padx=18, pady=(0, 16))
        self.scrub = ttk.Scale(right, from_=0, to=100, orient="horizontal")
        self.scrub.pack(fill="x", padx=18, pady=8)
        values = tk.Frame(right, bg=PANEL); values.pack(fill="x", padx=18, pady=6)
        tk.Label(values, text="START", bg=PANEL, fg=MUTED).grid(row=0, column=0, sticky="w")
        tk.Label(values, text="END", bg=PANEL, fg=MUTED).grid(row=0, column=2, sticky="w", padx=(18, 0))
        self.start_var = tk.StringVar(); self.end_var = tk.StringVar()
        tk.Entry(values, textvariable=self.start_var, width=18, font=("Consolas", 12)).grid(row=1, column=0, sticky="w")
        tk.Button(values, text="SET START ← SCRUBBER", command=self._set_start, bg="#285b42", fg="white").grid(row=1, column=1, padx=8)
        tk.Entry(values, textvariable=self.end_var, width=18, font=("Consolas", 12)).grid(row=1, column=2, sticky="w", padx=(18, 0))
        tk.Button(values, text="SET END ← SCRUBBER", command=self._set_end, bg="#285b42", fg="white").grid(row=1, column=3, padx=8)
        buttons = tk.Frame(right, bg=PANEL); buttons.pack(fill="x", padx=18, pady=22)
        tk.Button(buttons, text="OPEN SOURCE IN VLC", command=self._open_source, padx=12, pady=9).pack(side="left")
        tk.Button(buttons, text="PLAY RANGE", command=self._play_range, padx=12, pady=9).pack(side="left", padx=8)
        tk.Button(buttons, text="DISABLE THIS STEP", command=self._disable, padx=12, pady=9).pack(side="left")
        tk.Button(buttons, text="SAVE BOUNDARY", command=self._save, bg=GREEN, fg="#07120b", font=("Segoe UI", 10, "bold"), padx=16, pady=9).pack(side="right")

    def _select(self, _event=None):
        if not self.listbox.curselection(): return
        self.selected = self.steps[self.listbox.curselection()[0]]
        step = self.selected
        scene = " / ".join(step.get("scene_titles", []))
        if self.app.project["preferences"].get("hide_spoilers"):
            scene = "Scene description hidden"
        self.heading.config(text=f"#{step['watch_step']} — {step['parent_title']} — {scene}")
        self.notes.config(text="\n".join(step.get("notes", [])) + "\n\nSet the exact in/out points for your edition. These are never guessed.")
        override = self.app.project.get("manual_overrides", {}).get(str(step["watch_step"]), {})
        self.start_var.set(format_timecode(override.get("start_seconds"), True))
        self.end_var.set(format_timecode(override.get("end_seconds"), True))
        mapped = self.app.project.get("work_map", {}).get(step["work_id"])
        duration = self.app.probe_for_work(step["work_id"]).get("duration", 100) if mapped else 100
        self.scrub.configure(to=max(1, duration))

    def _set_start(self): self.start_var.set(format_timecode(self.scrub.get(), True))
    def _set_end(self): self.end_var.set(format_timecode(self.scrub.get(), True))

    def _open_source(self): self._launch(None, None)

    def _play_range(self):
        try: self._launch(parse_timecode(self.start_var.get()), parse_timecode(self.end_var.get()))
        except Exception as exc: messagebox.showerror("THE MEDIA IS WRONG", str(exc), parent=self)

    def _launch(self, start, end):
        if not self.selected: return
        media = self.app.project.get("work_map", {}).get(self.selected["work_id"])
        if not media: raise ValueError("Map this work to a media file first")
        vlc = find_executable("vlc", self.app.project.get("executables", {}).get("vlc", ""))
        if not vlc: raise FileNotFoundError("VLC was not found. Select vlc.exe in Settings.")
        command = [vlc]
        if start is not None: command += [f"--start-time={start:g}"]
        if end is not None: command += [f"--stop-time={end:g}", "--play-and-exit"]
        subprocess.Popen(command + [media])

    def _save(self):
        if not self.selected: return
        try:
            start, end = parse_timecode(self.start_var.get()), parse_timecode(self.end_var.get())
            if start is None or end is None or start >= end: raise ValueError("Start and end are required, and start must be earlier than end")
            media = self.app.project.get("work_map", {}).get(self.selected["work_id"])
            if not media: raise ValueError("Map this work to a media file first")
            probe = self.app.probe_for_work(self.selected["work_id"])
            if end > probe.get("duration", 0) + 1: raise ValueError("End is beyond the mapped file's duration")
            self.app.project.setdefault("manual_overrides", {})[str(self.selected["watch_step"])] = {
                "start_seconds": start, "end_seconds": end, "status": "resolved",
                "reason": "edition-specific manual boundary", "media_fingerprint": media_fingerprint(media, probe),
                "scene_ids": self.selected.get("scene_ids", []),
            }
            index = self.steps.index(self.selected)
            self.listbox.delete(index); self.listbox.insert(index, f"✓ #{self.selected['watch_step']}  {self.selected['parent_title']}")
            self.listbox.selection_set(index)
            self.app.dirty = True; self.app.refresh()
        except Exception as exc:
            messagebox.showerror("THE MEDIA IS WRONG", str(exc), parent=self)

    def _disable(self):
        if not self.selected: return
        number = int(self.selected["watch_step"])
        disabled = {int(x) for x in self.app.project.setdefault("disabled_steps", [])}
        disabled.add(number); self.app.project["disabled_steps"] = sorted(disabled)
        self.app.dirty = True; self.app.refresh(); self.destroy()


class App:
    def __init__(self, root: tk.Tk, project_path: str | None = None):
        self.root = root
        self.root.title("MCU ChronoMonster")
        self.root.geometry("1180x790")
        self.root.minsize(980, 660)
        self.root.configure(bg=BG)
        self.project = load_project(project_path) if project_path else new_project()
        self.project_path = Path(project_path).resolve() if project_path else None
        self._load_chronology()
        self.dirty = False; self.builder: MonsterBuilder | None = None
        self._configure_style(); self._build_ui(); self._enable_drop(); self.refresh()
        self.root.protocol("WM_DELETE_WINDOW", self._close)

    def _configure_style(self):
        style = ttk.Style(self.root)
        try: style.theme_use("clam")
        except tk.TclError: pass
        style.configure("Treeview", background="#101713", foreground=TEXT, fieldbackground="#101713", rowheight=30, font=("Segoe UI", 10))
        style.configure("Treeview.Heading", background="#234534", foreground=TEXT, font=("Segoe UI", 10, "bold"))
        style.map("Treeview", background=[("selected", "#32684b")])
        style.configure("TProgressbar", background=GREEN, troughcolor="#0d120f")

    def _build_ui(self):
        top = tk.Frame(self.root, bg="#0b2f1e", height=68); top.pack(fill="x")
        tk.Label(top, text="MCU CHRONOMONSTER", bg="#0b2f1e", fg=TEXT, font=("Segoe UI", 22, "bold")).pack(side="left", padx=18, pady=14)
        tk.Label(top, text="❄  THE TIMELINE WILL BE CONTAINED  ❄", bg="#0b2f1e", fg=GOLD, font=("Segoe UI", 9, "bold")).pack(side="left", padx=8)
        for label, command in (("NEW", self.new), ("OPEN", self.open), ("SAVE", self.save), ("IMPORT MAP", self.import_mapping), ("EXPORT MAP", self.export_mapping), ("SETTINGS", self.settings)):
            tk.Button(top, text=label, command=command, bg="#173e2b", fg=TEXT, relief="flat", padx=12, pady=7).pack(side="right", padx=(0, 6))
        body = tk.Frame(self.root, bg=BG); body.pack(fill="both", expand=True, padx=14, pady=12)
        media = tk.Frame(body, bg=PANEL); media.pack(fill="x", pady=(0, 9))
        tk.Label(media, text="MEDIA", bg=PANEL, fg=GOLD, font=("Segoe UI", 10, "bold")).pack(side="left", padx=12, pady=12)
        self.media_label = tk.Label(media, text="No folders yet — add or drop one here", bg=PANEL, fg=TEXT, anchor="w")
        self.media_label.pack(side="left", fill="x", expand=True, padx=4)
        tk.Button(media, text="ADD FOLDER", command=self.add_folder, padx=10, pady=7).pack(side="right", padx=5)
        tk.Button(media, text="RESCAN + MATCH", command=self.scan, bg=GREEN, fg="#07120b", padx=10, pady=7).pack(side="right", padx=5)
        summary = tk.Frame(body, bg=BG); summary.pack(fill="x", pady=(0, 9))
        self.summary_label = tk.Label(summary, text="", bg=BG, fg=TEXT, font=("Segoe UI", 12, "bold")); self.summary_label.pack(side="left")
        tk.Label(summary, text="Scope:", bg=BG, fg=MUTED).pack(side="left", padx=(25, 5))
        self.scope = ttk.Combobox(summary, state="readonly", width=21, values=["All included", "Core only", "Core + Completionist"])
        self.scope.pack(side="left"); self.scope.bind("<<ComboboxSelected>>", self._scope_changed)
        self.spoiler_var = tk.BooleanVar(value=self.project["preferences"].get("hide_spoilers", False))
        tk.Checkbutton(summary, text="Hide scene descriptions", variable=self.spoiler_var, command=self._spoilers, bg=BG, fg=MUTED, selectcolor=PANEL, activebackground=BG, activeforeground=TEXT).pack(side="right")
        center = tk.PanedWindow(body, orient="horizontal", sashwidth=7, bg=BG, bd=0); center.pack(fill="both", expand=True)
        problems = tk.Frame(center, bg=PANEL); center.add(problems, minsize=560, stretch="always")
        build = tk.Frame(center, bg=PANEL); center.add(build, minsize=360)
        ph = tk.Frame(problems, bg=PANEL); ph.pack(fill="x", padx=10, pady=8)
        tk.Label(ph, text="MEDIA MAP / PROBLEMS", bg=PANEL, fg=GOLD, font=("Segoe UI", 11, "bold")).pack(side="left")
        self.search_var = tk.StringVar(); self.search_var.trace_add("write", lambda *_: self.refresh_table())
        tk.Entry(ph, textvariable=self.search_var, width=24).pack(side="right")
        self.tree = ttk.Treeview(problems, columns=("status", "id", "title", "mapping"), show="headings")
        for col, label, width in (("status", "STATE", 80), ("id", "WORK", 70), ("title", "TITLE", 240), ("mapping", "MAPPED FILE / BEST CANDIDATE", 320)):
            self.tree.heading(col, text=label); self.tree.column(col, width=width, stretch=col in {"title", "mapping"})
        self.tree.pack(fill="both", expand=True, padx=10)
        self.tree.tag_configure("green", foreground="#8ce3a9"); self.tree.tag_configure("yellow", foreground="#f4d071"); self.tree.tag_configure("red", foreground="#ff8888")
        mapbar = tk.Frame(problems, bg=PANEL); mapbar.pack(fill="x", padx=10, pady=9)
        tk.Button(mapbar, text="MAP FILE…", command=self.map_selected, padx=10, pady=7).pack(side="left")
        tk.Button(mapbar, text="USE BEST CANDIDATE", command=self.use_candidate, padx=10, pady=7).pack(side="left", padx=5)
        tk.Button(mapbar, text="REVEAL", command=self.reveal_selected, padx=10, pady=7).pack(side="left")
        tk.Button(mapbar, text="OPEN IN VLC", command=self.preview_selected, padx=10, pady=7).pack(side="left", padx=5)
        tk.Button(mapbar, text="TOGGLE WORK SCOPE", command=self.toggle_work, padx=10, pady=7).pack(side="left")
        tk.Button(mapbar, text="RESOLVE 9", command=lambda: BoundaryResolver(self), bg=GOLD, fg="#1c1604", padx=8, pady=7).pack(side="right")
        tk.Button(mapbar, text="CALIBRATE WORK", command=self.calibrate_selected, bg="#335a47", fg=TEXT, padx=8, pady=7).pack(side="right", padx=5)
        tk.Label(build, text="BUILD THE THING", bg=PANEL, fg=GOLD, font=("Segoe UI", 13, "bold")).pack(anchor="w", padx=14, pady=(14, 4))
        self.runtime_label = tk.Label(build, text="", bg=PANEL, fg=MUTED, justify="left", wraplength=350); self.runtime_label.pack(anchor="w", padx=14, pady=(0, 10))
        tk.Button(build, text="BOUNDARY CERTIFICATION", command=lambda: CertificationResolver(self), bg=GOLD, fg="#1c1604", font=("Segoe UI", 10, "bold"), padx=12, pady=10).pack(fill="x", padx=14, pady=4)
        tk.Button(build, text="VALIDATE EVERYTHING", command=self.validate, bg="#335a47", fg=TEXT, font=("Segoe UI", 10, "bold"), padx=12, pady=10).pack(fill="x", padx=14, pady=4)
        tk.Button(build, text="BUILD VLC PLAYLIST", command=self.build_xspf, bg=GREEN, fg="#07120b", font=("Segoe UI", 11, "bold"), padx=12, pady=13).pack(fill="x", padx=14, pady=4)
        tk.Button(build, text="EXPERIMENTAL STREAM-COPY PLAN", command=self.build_ffconcat, padx=12, pady=7).pack(fill="x", padx=14, pady=4)
        sep = ttk.Separator(build); sep.pack(fill="x", padx=14, pady=10)
        self.build_mode = tk.StringVar(value="one")
        tk.Radiobutton(build, text="ONE GIANT FILE", variable=self.build_mode, value="one", bg=PANEL, fg=TEXT, selectcolor="#143322", activebackground=PANEL).pack(anchor="w", padx=14)
        tk.Radiobutton(build, text="SPLIT INTO LESS ILLEGAL-LOOKING VOLUMES", variable=self.build_mode, value="volumes", bg=PANEL, fg=TEXT, selectcolor="#143322", activebackground=PANEL).pack(anchor="w", padx=14)
        opts = tk.Frame(build, bg=PANEL); opts.pack(fill="x", padx=14, pady=6)
        tk.Label(opts, text="Volume hours", bg=PANEL, fg=MUTED).pack(side="left")
        self.volume_hours = tk.StringVar(value="8"); tk.Entry(opts, textvariable=self.volume_hours, width=5).pack(side="left", padx=6)
        tk.Label(opts, text="Encoder", bg=PANEL, fg=MUTED).pack(side="left", padx=(12, 4))
        self.encoder = ttk.Combobox(opts, state="readonly", width=12, values=["libx264", "h264_nvenc"]); self.encoder.set(self.project["monster_profile"].get("video_codec", "libx264")); self.encoder.pack(side="left")
        self.monster_button = tk.Button(build, text="BUILD THIS ABOMINATION", command=self.build_monster, bg=RED, fg="white", font=("Segoe UI", 12, "bold"), padx=12, pady=15)
        self.monster_button.pack(fill="x", padx=14, pady=6)
        self.cancel_button = tk.Button(build, text="CANCEL SAFELY", command=self.cancel_build, state="disabled", padx=12, pady=7); self.cancel_button.pack(fill="x", padx=14)
        self.progress = ttk.Progressbar(build, mode="determinate"); self.progress.pack(fill="x", padx=14, pady=(12, 3))
        self.progress_label = tk.Label(build, text="Ready.", bg=PANEL, fg=MUTED, justify="left", wraplength=350); self.progress_label.pack(anchor="w", padx=14)
        self.log = tk.Text(body, height=5, bg="#090d0b", fg="#b8c7bd", insertbackground="white", relief="flat", font=("Consolas", 9)); self.log.pack(fill="x", pady=(9, 0))

    def _enable_drop(self):
        try:
            from tkinterdnd2 import DND_FILES
            self.root.drop_target_register(DND_FILES)
            self.root.dnd_bind("<<Drop>>", self._drop)
        except Exception:
            pass

    def _drop(self, event):
        paths = list(self.root.tk.splitlist(event.data))
        selected = self._selected_work()
        for item in paths:
            p = Path(item)
            if p.is_dir():
                self._add_root(p)
            elif p.is_file() and selected:
                self.project.setdefault("work_map", {})[selected] = str(p.resolve()); self.dirty = True
        self.refresh()

    def log_line(self, message: str):
        self.log.insert("end", message.rstrip() + "\n"); self.log.see("end")

    def new(self):
        self.project = new_project(); self.project_path = None; self._load_chronology(); self.dirty = False; self.refresh(); self.log_line("Started a new empty mapping project.")

    def open(self):
        path = filedialog.askopenfilename(filetypes=[("ChronoMonster project", "*.chronomonster.json *.json")])
        if path:
            self.project = load_project(path); self.project_path = Path(path); self._load_chronology(); self.dirty = False; self.refresh(); self.log_line(f"Opened {path}")

    def _load_chronology(self):
        identity = self.project.get("chronology", {})
        self.steps = load_steps(identity.get("manifest_path") or None)
        self.catalog = load_catalog(identity.get("catalog_path") or None)
        self.catalog_by_id = {w["work_id"]: w for w in self.catalog}

    def save(self):
        if not self.project_path:
            path = filedialog.asksaveasfilename(defaultextension=".chronomonster.json", filetypes=[("ChronoMonster project", "*.chronomonster.json")])
            if not path: return
            self.project_path = Path(path)
        save_project(self.project, self.project_path); self.dirty = False; self.log_line(f"Saved {self.project_path}")

    def export_mapping(self):
        path = filedialog.asksaveasfilename(defaultextension=".mapping.json", filetypes=[("Mapping manifest", "*.mapping.json *.json")])
        if path:
            Path(path).write_text(json.dumps(mapping_snapshot(self.project), indent=2, ensure_ascii=False), encoding="utf-8")
            self.log_line(f"Exported mapping manifest to {path}")

    def import_mapping(self):
        path = filedialog.askopenfilename(filetypes=[("Mapping manifest", "*.mapping.json *.json")])
        if not path: return
        try:
            data = json.loads(Path(path).read_text(encoding="utf-8"))
            if data.get("format") != "chronomonster-mapping-v1": raise ValueError("Not a ChronoMonster mapping manifest")
            if data.get("chronology_sha256") != self.project["chronology"].get("sha256"):
                raise ValueError("Mapping chronology hash differs from this project; import was stopped instead of guessing")
            self.project["work_map"] = data.get("work_map", {})
            self.project["manual_overrides"] = data.get("manual_overrides", {})
            for key in ("edition_calibration", "boundary_nudges", "boundary_certifications", "subtitle_match_runs", "subtitle_proposals"):
                self.project[key] = data.get(key, {})
            self.project["disabled_steps"] = data.get("disabled_steps", [])
            self.dirty = True; self.refresh(); self.log_line(f"Imported mapping manifest from {path}")
        except Exception as exc: messagebox.showerror("Mapping import blocked", str(exc))

    def _close(self):
        if self.builder: self.builder.cancel()
        if self.dirty and self.project_path: save_project(self.project, self.project_path)
        self.root.destroy()

    def settings(self):
        dialog = tk.Toplevel(self.root); dialog.title("Executable settings"); dialog.configure(bg=BG); dialog.geometry("700x330")
        entries = {}
        for row, key in enumerate(("ffmpeg", "ffprobe", "vlc")):
            tk.Label(dialog, text=key.upper(), bg=BG, fg=TEXT, width=10).grid(row=row, column=0, padx=10, pady=12)
            value = tk.StringVar(value=self.project["executables"].get(key, "")); entries[key] = value
            tk.Entry(dialog, textvariable=value, width=64).grid(row=row, column=1, padx=4)
            def browse(k=key, v=value):
                path = filedialog.askopenfilename(parent=dialog, filetypes=[("Executable", "*.exe"), ("All files", "*")]);
                if path: v.set(path)
            tk.Button(dialog, text="BROWSE", command=browse).grid(row=row, column=2, padx=8)
        strict = tk.BooleanVar(value=self.project.get("preferences", {}).get("strict_boundary_certification", True))
        tk.Checkbutton(dialog, text="Require zero unverified boundaries for certified playlists/builds", variable=strict, bg=BG, fg=TEXT, selectcolor=PANEL, activebackground=BG, activeforeground=TEXT).grid(row=3, column=1, sticky="w", padx=4, pady=8)
        def done():
            for key, value in entries.items(): self.project["executables"][key] = value.get().strip()
            self.project.setdefault("preferences", {})["strict_boundary_certification"] = strict.get()
            self.dirty = True; dialog.destroy()
        tk.Button(dialog, text="SAVE", command=done, bg=GREEN, padx=18, pady=8).grid(row=4, column=2, pady=16)

    def add_folder(self):
        path = filedialog.askdirectory()
        if path: self._add_root(Path(path)); self.refresh()

    def _add_root(self, path: Path):
        roots = self.project.setdefault("media_roots", [])
        value = str(path.resolve())
        if value not in roots: roots.append(value); self.dirty = True

    def scan(self):
        if not self.project.get("media_roots"):
            return messagebox.showerror("Nothing to scan", "Add or drop a media folder first.")
        self.monster_button.config(state="disabled"); self.progress.config(mode="indeterminate"); self.progress.start(15)
        def worker():
            try:
                files = scan_media(self.project["media_roots"])
                self.root.after(0, lambda: self.log_line(f"Found {len(files)} video files. Probing them now…"))
                probes = probe_paths(files, self.project.setdefault("probe_cache", {}), self.project["executables"].get("ffprobe", ""), lambda i,n,p: self.root.after(0, lambda i=i,n=n,p=p: self.progress_label.config(text=f"Probing {i}/{n}: {Path(p).name}")))
                matches = match_catalog(self.catalog, files, probes); accepted = 0
                self.project["match_candidates"] = matches
                used_paths = {str(Path(p).resolve()).lower() for p in self.project.get("work_map", {}).values() if p}
                for work_id, match in matches.items():
                    if match["status"] == "green" and match["candidates"] and not self.project["work_map"].get(work_id):
                        candidate = match["candidates"][0]["path"]
                        key = str(Path(candidate).resolve()).lower()
                        if key not in used_paths:
                            self.project["work_map"][work_id] = candidate; used_paths.add(key); accepted += 1
                self.dirty = True
                self.root.after(0, lambda: self._scan_done(accepted))
            except Exception as exc: self.root.after(0, lambda exc=exc: self._worker_error(exc))
        threading.Thread(target=worker, daemon=True).start()

    def _scan_done(self, accepted):
        self.progress.stop(); self.progress.config(mode="determinate", value=0); self.monster_button.config(state="normal")
        self.progress_label.config(text=f"Scan complete. {accepted} new high-confidence mappings accepted."); self.log_line(self.progress_label.cget("text")); self.refresh()

    def _worker_error(self, exc):
        self.progress.stop(); self.progress.config(mode="determinate", value=0); self.monster_button.config(state="normal"); self.cancel_button.config(state="disabled")
        self.builder = None; self.progress_label.config(text=str(exc)); self.log_line(f"ERROR: {exc}"); messagebox.showerror("THE MEDIA IS WRONG", str(exc))

    def _selected_work(self):
        selected = self.tree.selection(); return selected[0] if selected else None

    def map_selected(self):
        work_id = self._selected_work()
        if not work_id: return
        path = filedialog.askopenfilename(filetypes=[("Video", "*.mkv *.mp4 *.m4v *.mov *.avi *.webm *.ts *.m2ts"), ("All", "*")])
        if path: self.project["work_map"][work_id] = str(Path(path).resolve()); self.dirty = True; self.refresh()

    def use_candidate(self):
        work_id = self._selected_work()
        candidate = (self.project.get("match_candidates", {}).get(work_id, {}).get("candidates") or [{}])[0].get("path") if work_id else None
        if candidate: self.project["work_map"][work_id] = candidate; self.dirty = True; self.refresh()

    def reveal_selected(self):
        work_id = self._selected_work(); path = self.project.get("work_map", {}).get(work_id, "")
        if not path: return
        if sys.platform == "win32": subprocess.Popen(["explorer", "/select,", str(Path(path))])
        elif sys.platform == "darwin": subprocess.Popen(["open", "-R", path])
        else: subprocess.Popen(["xdg-open", str(Path(path).parent)])

    def preview_selected(self):
        work_id = self._selected_work(); path = self.project.get("work_map", {}).get(work_id, "")
        vlc = find_executable("vlc", self.project["executables"].get("vlc", ""))
        if not path or not vlc: return messagebox.showerror("Cannot preview", "Map the file and select VLC in Settings first.")
        step = next((s for s in selected_steps(self.steps, self.project.get("scope", "all"), self.project.get("disabled_steps", [])) if s["work_id"] == work_id), None)
        command = [vlc]
        if step:
            start, end = resolved_step_range(self.project, step)
            if start is not None: command.append(f"--start-time={start:g}")
            if end is not None: command += [f"--stop-time={min(end, start + 10):g}", "--play-and-exit"]
        subprocess.Popen(command + [path])

    def calibrate_selected(self):
        work_id = self._selected_work()
        if not work_id:
            return messagebox.showerror("Choose a work", "Select a mapped work in the table first.")
        CalibrationDialog(self, work_id)

    def toggle_work(self):
        work_id = self._selected_work()
        if not work_id: return
        numbers = {int(s["watch_step"]) for s in self.steps if s["work_id"] == work_id}
        disabled = {int(x) for x in self.project.setdefault("disabled_steps", [])}
        if numbers and numbers.issubset(disabled): disabled -= numbers
        else: disabled |= numbers
        self.project["disabled_steps"] = sorted(disabled); self.dirty = True; self.refresh()

    def probe_for_work(self, work_id):
        from .media import probe_with_cache
        path = self.project["work_map"].get(work_id)
        if not path: return {}
        probe, _ = probe_with_cache(path, self.project.setdefault("probe_cache", {}), self.project["executables"].get("ffprobe", "")); return probe

    def validate(self):
        try:
            report = validate_project(self.project, self.steps, self.catalog, probe=True)
            self.dirty = True; self.log_line(f"Validation: {report['counts']['red']} red / {report['counts']['yellow']} yellow / certified={report['certified']}")
            for issue in report["issues"][:40]: self.log_line(f"{issue['severity'].upper()} {issue['code']} {issue.get('work_id','')} {issue['message']}")
            self.refresh(); messagebox.showinfo("THE TIMELINE HAS BEEN CONTAINED" if report["certified"] else "THE MEDIA IS WRONG", "Certified and ready." if report["certified"] else f"{report['counts']['red']} blocking problems and {report['counts']['yellow']} warnings. See the log and problem table.")
        except Exception as exc: self._worker_error(exc)

    def build_xspf(self):
        path = filedialog.asksaveasfilename(defaultextension=".xspf", filetypes=[("VLC playlist", "*.xspf")])
        if not path: return
        try:
            report = write_xspf(self.project, self.steps, path); self.project["outputs"]["last_xspf"] = path; self.dirty = True
            self.log_line(f"THE TIMELINE HAS BEEN CONTAINED: {report['entry_count']} entries → {path}")
            messagebox.showinfo("THE TIMELINE HAS BEEN CONTAINED", f"Built {report['entry_count']} VLC entries plus report, mapping snapshot, and readable index.")
        except Exception as exc: messagebox.showerror("Certified playlist blocked", str(exc))

    def build_ffconcat(self):
        path = filedialog.asksaveasfilename(defaultextension=".ffconcat", filetypes=[("FFconcat", "*.ffconcat")])
        if path:
            try: count = write_ffconcat(self.project, self.steps, path); self.log_line(f"Wrote {count}-entry experimental stream-copy plan. Cuts may be GOP-inexact.")
            except Exception as exc: messagebox.showerror("Plan blocked", str(exc))

    def build_monster(self):
        path = filedialog.asksaveasfilename(defaultextension=".mkv", filetypes=[("Matroska", "*.mkv")])
        if not path: return
        self.project["monster_profile"]["video_codec"] = self.encoder.get()
        if "nvenc" in self.encoder.get(): self.project["monster_profile"]["preset"] = "p5"
        elif self.project["monster_profile"].get("preset") == "p5": self.project["monster_profile"]["preset"] = "medium"
        self.monster_button.config(state="disabled"); self.cancel_button.config(state="normal")
        def worker():
            try:
                self.builder = MonsterBuilder(self.project, self.steps, path)
                resolved, probes, estimate = self.builder.preflight()
                self.root.after(0, lambda: self.log_line(estimate["message"] + f" Estimated final: {estimate['runtime_human']}."))
                if not estimate["enough_free_space"]: raise RuntimeError("Preflight predicts insufficient free disk space for segments plus final output")
                result = self.builder.build(volumes=self.build_mode.get() == "volumes", max_volume_seconds=float(self.volume_hours.get()) * 3600, callback=lambda e: self.root.after(0, lambda e=e: self._build_progress(e)))
                self.project["outputs"]["last_monster"] = result.output_files
                self.root.after(0, lambda result=result: self._build_done(result))
            except Exception as exc: self.root.after(0, lambda exc=exc: self._worker_error(exc))
        threading.Thread(target=worker, daemon=True).start()

    def _build_progress(self, event):
        self.progress["value"] = event.get("percent", 0)
        if "step" in event:
            self.progress_label.config(text=f"{event.get('phase','')} — step {event['step']}/{event['total']} — {event.get('title','')} — cache hits {event.get('cache_hits',0)}")
        else: self.progress_label.config(text=f"{event.get('phase','working')} {event.get('volume','')} / {event.get('volumes','')}")

    def _build_done(self, result):
        self.builder = None; self.monster_button.config(state="normal"); self.cancel_button.config(state="disabled"); self.progress["value"] = 100
        self.progress_label.config(text=f"THE TIMELINE HAS BEEN CONTAINED — {result.segments} segments, {result.cache_hits} cache hits")
        self.log_line(self.progress_label.cget("text")); self.dirty = True
        messagebox.showinfo("THE TIMELINE HAS BEEN CONTAINED", "\n".join(result.output_files))

    def cancel_build(self):
        if self.builder: self.builder.cancel(); self.progress_label.config(text="Cancelling safely after the current FFmpeg process stops…")

    def _scope_changed(self, _event=None):
        self.project["scope"] = {0: "all", 1: "core", 2: "core_completionist"}.get(self.scope.current(), "all"); self.dirty = True; self.refresh()

    def _spoilers(self): self.project["preferences"]["hide_spoilers"] = self.spoiler_var.get(); self.dirty = True

    def refresh(self):
        roots = self.project.get("media_roots", []); self.media_label.config(text="  •  ".join(roots) if roots else "No folders yet — add or drop one here")
        self.scope.current({"all": 0, "core": 1, "core_completionist": 2}.get(self.project.get("scope"), 0))
        active = selected_steps(self.steps, self.project.get("scope", "all"), self.project.get("disabled_steps", []))
        active_ids = {s["work_id"] for s in active}; mapped = sum(bool(self.project.get("work_map", {}).get(w)) for w in active_ids)
        unresolved = sum(s["playback_mode"] == "manual_boundary_required" and step_range(s, self.project.get("manual_overrides", {}))[0] is None for s in active)
        yellow = sum(self.project.get("match_candidates", {}).get(w, {}).get("status") == "yellow" and not self.project.get("work_map", {}).get(w) for w in active_ids)
        missing = len(active_ids) - mapped
        self.summary_label.config(text=f"{len(active_ids)} works / {len(active):,} steps     ✓ {mapped} mapped     ⚠ {yellow} review     ✕ {missing} missing     ⏱ {unresolved} boundaries")
        known = sum((s.get("duration_seconds") or 0) for s in active)
        whole = sum(1 for s in active if s["playback_mode"] == "whole_file")
        cert = certification_summary(self.project, self.steps)
        finish = datetime.now() + timedelta(weeks=known / 3600 / 10)
        self.runtime_label.config(text=f"Known timed runtime: {format_timecode(known)}\nPlus {whole} whole files and unresolved enabled tags.\nBoundary ledger: {cert['verified']:,} verified / {cert['unverified']:,} unverified.\nAt 10 hours/week: roughly {finish:%B %Y} (before whole files).")
        self.refresh_table()

    def refresh_table(self):
        query = self.search_var.get().lower().strip() if hasattr(self, "search_var") else ""
        selected = self.tree.selection() if hasattr(self, "tree") else ()
        self.tree.delete(*self.tree.get_children())
        active_ids = {s["work_id"] for s in selected_steps(self.steps, self.project.get("scope", "all"), self.project.get("disabled_steps", []))}
        for work in self.catalog:
            wid = work["work_id"]
            if wid not in active_ids: continue
            title = work["watch_item"]
            mapped = self.project.get("work_map", {}).get(wid)
            match = self.project.get("match_candidates", {}).get(wid, {})
            candidate = (match.get("candidates") or [{}])[0].get("path", "")
            status = "green" if mapped and Path(mapped).is_file() else "yellow" if candidate else "red"
            shown = Path(mapped).name if mapped else ("candidate: " + Path(candidate).name if candidate else "missing")
            if query and query not in f"{wid} {title} {shown}".lower(): continue
            self.tree.insert("", "end", iid=wid, values=({"green":"✓","yellow":"⚠","red":"✕"}[status], wid, title, shown), tags=(status,))
        if selected and self.tree.exists(selected[0]): self.tree.selection_set(selected[0])


def run(project_path: str | None = None):
    root = None
    try:
        from tkinterdnd2 import TkinterDnD
        root = TkinterDnD.Tk()
    except Exception:
        root = tk.Tk()
    App(root, project_path)
    root.mainloop()
