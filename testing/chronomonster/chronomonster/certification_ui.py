from __future__ import annotations

import subprocess
import threading
from pathlib import Path
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from .audit import write_audit_xspf
from .calibration import reference_boundary_key, set_calibration
from .certification import boundary_descriptors, certification_summary, certification_valid, certify_batch, certify_boundary
from .media import find_executable
from .subtitles import propose_subtitle_matches
from .timecode import format_timecode, parse_timecode


BG = "#111714"
PANEL = "#19251f"
GREEN = "#4fb477"
GOLD = "#e2b64d"
TEXT = "#f2f4f2"
MUTED = "#aab7af"


class CalibrationDialog(tk.Toplevel):
    def __init__(self, app, work_id: str):
        super().__init__(app.root)
        self.app, self.work_id = app, work_id
        title = app.catalog_by_id.get(work_id, {}).get("watch_item", work_id)
        self.title(f"Calibrate copy — {title}")
        self.geometry("720x520"); self.configure(bg=BG)
        tk.Label(self, text=f"CALIBRATE {title}", bg=BG, fg=GOLD, font=("Segoe UI", 15, "bold"), wraplength=680, justify="left").pack(anchor="w", padx=18, pady=(18, 6))
        tk.Label(self, text="Enter one anchor for a constant offset, two for offset + drift, or three or more for a different cut. Each line is REFERENCE = YOUR COPY.", bg=BG, fg=TEXT, wraplength=680, justify="left").pack(anchor="w", padx=18)
        modebar = tk.Frame(self, bg=BG); modebar.pack(fill="x", padx=18, pady=12)
        tk.Label(modebar, text="Model", bg=BG, fg=MUTED).pack(side="left")
        self.mode = ttk.Combobox(modebar, state="readonly", values=["affine", "piecewise"], width=14); self.mode.pack(side="left", padx=8)
        existing = app.project.get("edition_calibration", {}).get(work_id, {})
        self.mode.set(existing.get("mode", "affine"))
        self.anchors = tk.Text(self, height=12, bg="#090d0b", fg=TEXT, insertbackground="white", font=("Consolas", 11))
        self.anchors.pack(fill="both", expand=True, padx=18)
        for anchor in existing.get("anchors", []):
            self.anchors.insert("end", f"{format_timecode(anchor['reference_seconds'], True)} = {format_timecode(anchor['local_seconds'], True)}\n")
        tk.Label(self, text="Examples: 00:10:00 = 00:10:07 or 600 = 607. Piecewise mode interpolates separately between every adjacent pair.", bg=BG, fg=MUTED, wraplength=680, justify="left").pack(anchor="w", padx=18, pady=8)
        controls = tk.Frame(self, bg=BG); controls.pack(fill="x", padx=18, pady=(0, 18))
        tk.Button(controls, text="REMOVE CALIBRATION", command=self._remove, padx=12, pady=8).pack(side="left")
        tk.Button(controls, text="SAVE CALIBRATION", command=self._save, bg=GREEN, fg="#07120b", font=("Segoe UI", 10, "bold"), padx=16, pady=8).pack(side="right")

    def _parse(self) -> list[dict]:
        anchors = []
        for number, raw in enumerate(self.anchors.get("1.0", "end").splitlines(), 1):
            if not raw.strip():
                continue
            left, separator, right = raw.partition("=")
            if not separator:
                raise ValueError(f"Line {number} must contain =")
            reference, local = parse_timecode(left.strip()), parse_timecode(right.strip())
            if reference is None or local is None:
                raise ValueError(f"Line {number} needs two timestamps")
            anchors.append({"reference_seconds": reference, "local_seconds": local})
        return anchors

    def _save(self):
        try:
            set_calibration(self.app.project, self.work_id, self._parse(), self.mode.get())
            self.app.dirty = True; self.app.refresh(); self.app.log_line(f"Calibration changed for {self.work_id}; its prior boundary certificates are now invalid until reverified.")
            self.destroy()
        except Exception as exc:
            messagebox.showerror("Calibration blocked", str(exc), parent=self)

    def _remove(self):
        self.app.project.setdefault("edition_calibration", {}).pop(self.work_id, None)
        self.app.dirty = True; self.app.refresh(); self.destroy()


class CertificationResolver(tk.Toplevel):
    def __init__(self, app):
        super().__init__(app.root)
        self.app = app
        self.title("Exhaustive boundary certification")
        self.geometry("1180x720"); self.configure(bg=BG)
        self.boundaries = []
        header = tk.Frame(self, bg=BG); header.pack(fill="x", padx=12, pady=10)
        tk.Label(header, text="EXHAUSTIVE BOUNDARY CERTIFICATION", bg=BG, fg=GOLD, font=("Segoe UI", 14, "bold")).pack(side="left")
        self.summary = tk.Label(header, text="", bg=BG, fg=TEXT, font=("Segoe UI", 11, "bold")); self.summary.pack(side="right")
        tk.Label(self, text="No confidence buckets: each unique timestamp is intrinsic, explicitly certified, or unverified. Space verifies the selected boundary and advances; P previews it.", bg=BG, fg=MUTED, justify="left").pack(anchor="w", padx=12)
        body = tk.PanedWindow(self, orient="horizontal", sashwidth=7, bg=BG, bd=0); body.pack(fill="both", expand=True, padx=12, pady=10)
        left = tk.Frame(body, bg=PANEL); body.add(left, minsize=650, stretch="always")
        right = tk.Frame(body, bg=PANEL); body.add(right, minsize=390)
        self.tree = ttk.Treeview(left, columns=("state", "key", "title", "time"), show="headings")
        for col, label, width in (("state", "STATE", 95), ("key", "BOUNDARY", 200), ("title", "WORK", 260), ("time", "LOCAL TIME", 120)):
            self.tree.heading(col, text=label); self.tree.column(col, width=width, stretch=col == "title")
        self.tree.pack(fill="both", expand=True, padx=8, pady=8); self.tree.bind("<<TreeviewSelect>>", self._selected_changed)
        self.tree.tag_configure("verified", foreground="#8ce3a9"); self.tree.tag_configure("unverified", foreground="#ff8888")
        self.detail = tk.Label(right, text="Select a boundary", bg=PANEL, fg=TEXT, justify="left", wraplength=360, font=("Segoe UI", 11)); self.detail.pack(anchor="w", padx=12, pady=(14, 8))
        self.local_var = tk.StringVar(); row = tk.Frame(right, bg=PANEL); row.pack(fill="x", padx=12, pady=6)
        tk.Label(row, text="Local boundary", bg=PANEL, fg=MUTED).pack(side="left")
        tk.Entry(row, textvariable=self.local_var, width=18, font=("Consolas", 11)).pack(side="right")
        tk.Button(right, text="APPLY LOCAL NUDGE", command=self._nudge, padx=12, pady=8).pack(fill="x", padx=12, pady=4)
        tk.Button(right, text="P — PLAY 4s BEFORE + 4s AFTER", command=self._play, padx=12, pady=9).pack(fill="x", padx=12, pady=4)
        tk.Button(right, text="SPACE — HUMAN VERIFIED + NEXT", command=self._verify, bg=GREEN, fg="#07120b", font=("Segoe UI", 10, "bold"), padx=12, pady=10).pack(fill="x", padx=12, pady=4)
        ttk.Separator(right).pack(fill="x", padx=12, pady=12)
        tk.Button(right, text="TRY SUBTITLE MATCH FOR THIS WORK", command=self._subtitle, bg=GOLD, fg="#1c1604", padx=12, pady=9).pack(fill="x", padx=12, pady=4)
        tk.Button(right, text="ACCEPT THIS SUBTITLE PROPOSAL", command=self._accept_subtitle, padx=12, pady=8).pack(fill="x", padx=12, pady=4)
        self.subtitle_detail = tk.Label(right, text="", bg=PANEL, fg=MUTED, justify="left", wraplength=360); self.subtitle_detail.pack(anchor="w", padx=12, pady=6)
        ttk.Separator(right).pack(fill="x", padx=12, pady=10)
        tk.Button(right, text="BUILD EXHAUSTIVE AUDIT PLAYLIST", command=self._audit, padx=12, pady=8).pack(fill="x", padx=12, pady=4)
        tk.Button(right, text="CERTIFY LAST AUDIT AS FULLY WATCHED", command=self._certify_audit, padx=12, pady=8).pack(fill="x", padx=12, pady=4)
        self.bind("<space>", lambda _e: self._verify()); self.bind("<KeyPress-p>", lambda _e: self._play())
        self.refresh()

    def refresh(self, keep_key: str | None = None):
        selected_key = keep_key or self._selected_key()
        self.boundaries = boundary_descriptors(self.app.project, self.app.steps)
        summary = certification_summary(self.app.project, self.app.steps)
        self.summary.config(text=f"{summary['verified']:,} verified / {summary['unverified']:,} unverified / {summary['required']:,} required")
        self.tree.delete(*self.tree.get_children())
        for index, boundary in enumerate(self.boundaries):
            valid, reason = certification_valid(self.app.project, boundary)
            self.tree.insert("", "end", iid=str(index), values=(reason if valid else "UNVERIFIED", boundary["key"], boundary["title"], format_timecode(boundary["local_seconds"], True)), tags=("verified" if valid else "unverified",))
            if boundary["key"] == selected_key:
                self.tree.selection_set(str(index)); self.tree.see(str(index))
        if not self.tree.selection() and self.boundaries:
            first = next((str(i) for i, b in enumerate(self.boundaries) if not certification_valid(self.app.project, b)[0]), "0")
            self.tree.selection_set(first); self.tree.see(first)
        self._selected_changed()
        self.app.refresh()

    def _selected_index(self) -> int | None:
        selected = self.tree.selection()
        return int(selected[0]) if selected else None

    def _selected(self) -> dict | None:
        index = self._selected_index()
        return self.boundaries[index] if index is not None and index < len(self.boundaries) else None

    def _selected_key(self) -> str | None:
        boundary = self._selected()
        return boundary["key"] if boundary else None

    def _selected_changed(self, _event=None):
        boundary = self._selected()
        if not boundary:
            return
        valid, reason = certification_valid(self.app.project, boundary)
        self.local_var.set(format_timecode(boundary["local_seconds"], True))
        self.detail.config(text=f"{boundary['title']}\n{boundary['key']}\nSource steps: {boundary['watch_steps']}\nSides: {boundary['sides']}\nStatus: {reason if valid else 'UNVERIFIED — ' + reason}")
        proposal = self.app.project.get("subtitle_proposals", {}).get(boundary["key"])
        self.subtitle_detail.config(text=(f"Experimental proposal: {format_timecode(proposal['proposed_seconds'], True)}\nGap {proposal['gap_duration']:.3f}s\nRule: {proposal['rule']}" if proposal else "No subtitle proposal for this boundary."))

    def _play(self):
        boundary = self._selected()
        if not boundary:
            return
        media = self.app.project.get("work_map", {}).get(boundary["work_id"])
        vlc = find_executable("vlc", self.app.project.get("executables", {}).get("vlc", ""))
        if not media or not vlc:
            return messagebox.showerror("Cannot preview", "Map the file and select VLC in Settings first.", parent=self)
        local = float(boundary["local_seconds"])
        subprocess.Popen([vlc, f"--start-time={max(0, local - 4):g}", f"--stop-time={local + 4:g}", "--play-and-exit", media])

    def _verify(self):
        boundary = self._selected()
        if not boundary:
            return
        try:
            certify_boundary(self.app.project, boundary, "human", {"workflow": "interactive_boundary_review"})
            self.app.dirty = True
            current = self._selected_index() or 0
            self.refresh(boundary["key"])
            for index in range(current + 1, len(self.boundaries)):
                if not certification_valid(self.app.project, self.boundaries[index])[0]:
                    self.tree.selection_set(str(index)); self.tree.see(str(index)); self._selected_changed(); break
        except Exception as exc:
            messagebox.showerror("Certification blocked", str(exc), parent=self)

    def _nudge(self):
        boundary = self._selected()
        if not boundary:
            return
        if boundary.get("reference_seconds") is None:
            return messagebox.showerror("Use manual resolver", "This boundary has no reference timestamp. Adjust it in Resolve 9 Boundaries.", parent=self)
        try:
            value = parse_timecode(self.local_var.get())
            if value is None or value < 0:
                raise ValueError("Enter a non-negative timestamp")
            key = reference_boundary_key(boundary["work_id"], boundary["reference_seconds"])
            self.app.project.setdefault("boundary_nudges", {})[key] = float(value)
            self.app.dirty = True; self.refresh(key)
        except Exception as exc:
            messagebox.showerror("Nudge blocked", str(exc), parent=self)

    def _subtitle(self):
        boundary = self._selected()
        if not boundary:
            return
        work_id = boundary["work_id"]
        self.subtitle_detail.config(text=f"Extracting and matching subtitles for {work_id}…")
        def worker():
            try:
                result = propose_subtitle_matches(self.app.project, self.app.steps, work_id)
                self.app.dirty = True
                self.after(0, lambda: (self.refresh(boundary["key"]), self.app.log_line(f"Subtitle experiment {work_id}: {len(result['proposals'])} exact-rule proposals from {result['eligible_boundaries']} eligible boundaries.")))
            except Exception as exc:
                self.after(0, lambda exc=exc: messagebox.showerror("Subtitle match failed", str(exc), parent=self))
        threading.Thread(target=worker, daemon=True).start()

    def _accept_subtitle(self):
        boundary = self._selected()
        if not boundary:
            return
        proposal = self.app.project.get("subtitle_proposals", {}).get(boundary["key"])
        if not proposal or not proposal.get("rule_pass"):
            return messagebox.showerror("No exact-rule proposal", "Run the subtitle matcher first. Only proposals that satisfy the displayed rule can be accepted.", parent=self)
        try:
            if boundary.get("reference_seconds") is not None:
                self.app.project.setdefault("boundary_nudges", {})[boundary["key"]] = float(proposal["proposed_seconds"])
            refreshed = next(b for b in boundary_descriptors(self.app.project, self.app.steps, boundary["work_id"]) if b["key"] == boundary["key"])
            certify_boundary(self.app.project, refreshed, "subtitle_experimental", proposal)
            self.app.dirty = True; self.refresh(boundary["key"])
        except Exception as exc:
            messagebox.showerror("Subtitle certification blocked", str(exc), parent=self)

    def _audit(self):
        path = filedialog.asksaveasfilename(parent=self, defaultextension=".xspf", filetypes=[("VLC playlist", "*.xspf")])
        if not path:
            return
        try:
            report = write_audit_xspf(self.app.project, self.app.steps, path)
            self.app.dirty = True; self.app.log_line(f"Boundary audit playlist: {report['boundary_count']} unverified timestamps → {path}")
            messagebox.showinfo("Audit playlist ready", f"Watch all {report['boundary_count']:,} entries. Any bad boundary remains available here for nudging. Only use the certification button after the complete playlist has been reviewed.", parent=self)
        except Exception as exc:
            messagebox.showerror("Audit export blocked", str(exc), parent=self)

    def _certify_audit(self):
        batch = self.app.project.get("last_boundary_audit", {})
        keys = batch.get("keys", [])
        if not keys:
            return messagebox.showerror("No audit batch", "Build and fully watch an audit playlist first.", parent=self)
        count = certify_batch(self.app.project, self.app.steps, keys, "human_audit", {"audit_playlist": batch.get("playlist"), "audit_created_at": batch.get("created_at"), "user_attested_complete_review": True})
        self.app.dirty = True; self.refresh(); self.app.log_line(f"Certified {count} boundaries from the explicitly attested exhaustive audit batch.")

