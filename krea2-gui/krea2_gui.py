#!/usr/bin/env python3
from __future__ import annotations

import os
import re
import shutil
import signal
import sys
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Optional

from PySide6.QtCore import (
    QByteArray,
    QElapsedTimer,
    QFileSystemWatcher,
    QObject,
    QProcess,
    QSettings,
    QSize,
    Qt,
    QTimer,
    QUrl,
    Signal,
)
from PySide6.QtGui import QAction, QCloseEvent, QDesktopServices, QIcon, QKeySequence, QPixmap, QShortcut
from PySide6.QtWidgets import (
    QApplication,
    QComboBox,
    QDoubleSpinBox,
    QFileDialog,
    QFormLayout,
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMenu,
    QMessageBox,
    QPlainTextEdit,
    QProgressBar,
    QPushButton,
    QSizePolicy,
    QSpinBox,
    QSplitter,
    QToolButton,
    QVBoxLayout,
    QWidget,
)

APP_NAME = "Krea2 GUI"
APP_ORG = "polskiftw"
DEFAULT_ROOT = Path.home() / "ai" / "krea2"
IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp"}
SEED_RE = re.compile(r"Generating\s+(\d+)\s*/\s*(\d+).*?seed\s+(-?\d+)", re.IGNORECASE)


@dataclass
class GenerationMeta:
    path: str
    prompt: str
    seed: Optional[int]
    lora: Optional[str]
    lora_strength: float
    rebalance: Optional[str]
    queue_index: int
    queue_total: int
    elapsed_seconds: float


class ImagePreview(QLabel):
    open_requested = Signal(str)

    def __init__(self, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self._pixmap = QPixmap()
        self._path: Optional[Path] = None
        self.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.setMinimumSize(480, 360)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        self.setFrameShape(QFrame.Shape.StyledPanel)
        self.setText("Generated image will appear here")

    @property
    def path(self) -> Optional[Path]:
        return self._path

    def set_image(self, path: Path) -> bool:
        pixmap = QPixmap(str(path))
        if pixmap.isNull():
            return False
        self._path = path
        self._pixmap = pixmap
        self.setText("")
        self._rescale()
        return True

    def clear_image(self) -> None:
        self._path = None
        self._pixmap = QPixmap()
        self.clear()
        self.setText("Generated image will appear here")

    def resizeEvent(self, event) -> None:  # type: ignore[override]
        super().resizeEvent(event)
        self._rescale()

    def mouseDoubleClickEvent(self, event) -> None:  # type: ignore[override]
        if self._path is not None and event.button() == Qt.MouseButton.LeftButton:
            self.open_requested.emit(str(self._path))
        super().mouseDoubleClickEvent(event)

    def contextMenuEvent(self, event) -> None:  # type: ignore[override]
        if self._path is None or self._pixmap.isNull():
            return
        menu = QMenu(self)
        copy_action = QAction("Copy Image", self)
        open_action = QAction("Open File", self)
        menu.addAction(copy_action)
        menu.addAction(open_action)
        chosen = menu.exec(event.globalPos())
        if chosen == copy_action:
            QApplication.clipboard().setPixmap(self._pixmap)
        elif chosen == open_action:
            self.open_requested.emit(str(self._path))

    def _rescale(self) -> None:
        if self._pixmap.isNull():
            return
        target = self.size() - QSize(16, 16)
        if target.width() <= 0 or target.height() <= 0:
            return
        scaled = self._pixmap.scaled(
            target,
            Qt.AspectRatioMode.KeepAspectRatio,
            Qt.TransformationMode.SmoothTransformation,
        )
        self.setPixmap(scaled)


class Krea2Window(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.settings = QSettings(APP_ORG, APP_NAME)
        self.krea2_root = Path(os.environ.get("KREA2_ROOT", str(DEFAULT_ROOT))).expanduser()
        self.out_dir = self.krea2_root / "out"
        local_cli = self.krea2_root / "bin" / "krea2"
        self.cli = (
            os.environ.get("KREA2_CLI")
            or (str(local_cli) if local_cli.is_file() else None)
            or shutil.which("krea2")
            or "krea2"
        )

        self.process: Optional[QProcess] = None
        self._process_group_pid: Optional[int] = None
        self._known_output_paths: set[Path] = set()
        self._run_new_paths: set[Path] = set()
        self._run_seeds: list[int] = []
        self._run_prompt = ""
        self._run_lora: Optional[str] = None
        self._run_strength = 1.0
        self._run_rebalance: Optional[str] = None
        self._run_queue_total = 1
        self._cancel_requested = False
        self._selected_meta: Optional[GenerationMeta] = None
        self._elapsed = QElapsedTimer()

        self.watcher = QFileSystemWatcher(self)
        self.watcher.directoryChanged.connect(self.scan_outputs)
        self.output_poll = QTimer(self)
        self.output_poll.setInterval(600)
        self.output_poll.timeout.connect(self.scan_outputs)

        self.elapsed_timer = QTimer(self)
        self.elapsed_timer.setInterval(250)
        self.elapsed_timer.timeout.connect(self.refresh_elapsed_label)

        self._build_ui()
        self._load_loras()
        self._prepare_output_watcher()
        self._restore_window_state()
        self._restore_control_state()
        self._update_lora_enabled()

        self.shortcut_return = QShortcut(QKeySequence("Ctrl+Return"), self)
        self.shortcut_return.activated.connect(self.generate_or_cancel)
        self.shortcut_enter = QShortcut(QKeySequence("Ctrl+Enter"), self)
        self.shortcut_enter.activated.connect(self.generate_or_cancel)

    def _build_ui(self) -> None:
        self.setWindowTitle(APP_NAME)
        self.setMinimumSize(950, 720)
        self.resize(1120, 860)

        central = QWidget(self)
        root_layout = QVBoxLayout(central)
        root_layout.setContentsMargins(12, 12, 12, 12)
        root_layout.setSpacing(10)
        self.setCentralWidget(central)

        self.prompt = QPlainTextEdit()
        self.prompt.setPlaceholderText("Describe what you want Krea2 to generate…")
        self.prompt.setMinimumHeight(120)
        root_layout.addWidget(QLabel("Prompt"))
        root_layout.addWidget(self.prompt)

        controls = QGridLayout()
        controls.setHorizontalSpacing(14)
        controls.setVerticalSpacing(8)

        self.lora = QComboBox()
        self.lora.setEditable(True)
        self.lora.setInsertPolicy(QComboBox.InsertPolicy.NoInsert)
        self.lora.currentTextChanged.connect(self._update_lora_enabled)

        self.strength = QDoubleSpinBox()
        self.strength.setRange(-10.0, 10.0)
        self.strength.setDecimals(2)
        self.strength.setSingleStep(0.05)
        self.strength.setValue(1.0)

        self.seed = QComboBox()
        self.seed.setEditable(True)
        self.seed.setInsertPolicy(QComboBox.InsertPolicy.NoInsert)
        self.seed.addItem("Random")
        self.seed.setCurrentText("Random")
        self.seed.setToolTip("Leave as Random, or type an integer seed.")

        self.random_seed = QPushButton("🎲")
        self.random_seed.setToolTip("Use a random seed")
        self.random_seed.setFixedWidth(44)
        self.random_seed.clicked.connect(lambda: self.seed.setCurrentText("Random"))

        self.queue = QSpinBox()
        self.queue.setRange(1, 999)
        self.queue.setValue(1)
        self.queue.setToolTip("Number of images to generate (-q / --queue)")
        self.queue.valueChanged.connect(self._refresh_generate_text)

        self.rebalance = QComboBox()
        self.rebalance.addItem("None", None)
        self.rebalance.addItem("Subtle", "subtle")
        self.rebalance.addItem("Balanced", "balanced")
        self.rebalance.addItem("Aggressive", "aggressive")
        self.rebalance.setCurrentIndex(0)

        controls.addWidget(QLabel("LoRA"), 0, 0)
        controls.addWidget(QLabel("Strength"), 0, 1)
        controls.addWidget(QLabel("Seed"), 0, 2)
        controls.addWidget(QLabel("Images"), 0, 4)
        controls.addWidget(self.lora, 1, 0)
        controls.addWidget(self.strength, 1, 1)
        controls.addWidget(self.seed, 1, 2)
        controls.addWidget(self.random_seed, 1, 3)
        controls.addWidget(self.queue, 1, 4)
        controls.addWidget(QLabel("Rebalance"), 2, 0)
        controls.addWidget(self.rebalance, 3, 0)
        controls.setColumnStretch(0, 3)
        controls.setColumnStretch(1, 1)
        controls.setColumnStretch(2, 2)
        controls.setColumnStretch(4, 1)
        root_layout.addLayout(controls)

        self.generate_button = QPushButton()
        self.generate_button.setMinimumHeight(48)
        self.generate_button.clicked.connect(self.generate_or_cancel)
        root_layout.addWidget(self.generate_button)
        self._refresh_generate_text()

        self.progress = QProgressBar()
        self.progress.setRange(0, 1)
        self.progress.setValue(0)
        self.progress.setTextVisible(True)
        self.progress.setFormat("Ready")
        root_layout.addWidget(self.progress)

        splitter = QSplitter(Qt.Orientation.Horizontal)
        splitter.setChildrenCollapsible(False)
        root_layout.addWidget(splitter, stretch=1)

        preview_container = QWidget()
        preview_layout = QVBoxLayout(preview_container)
        preview_layout.setContentsMargins(0, 0, 0, 0)
        self.preview = ImagePreview()
        self.preview.open_requested.connect(self.open_path)
        preview_layout.addWidget(self.preview, stretch=1)

        info_row = QHBoxLayout()
        self.image_info = QLabel("No image selected")
        self.image_info.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        info_row.addWidget(self.image_info, stretch=1)

        self.open_file_button = QPushButton("Open File")
        self.open_file_button.clicked.connect(self.open_current_file)
        self.open_folder_button = QPushButton("Open Folder")
        self.open_folder_button.clicked.connect(self.open_current_folder)
        self.copy_seed_button = QPushButton("Copy Seed")
        self.copy_seed_button.clicked.connect(self.copy_selected_seed)
        for button in (self.open_file_button, self.open_folder_button, self.copy_seed_button):
            button.setEnabled(False)
            info_row.addWidget(button)
        preview_layout.addLayout(info_row)
        splitter.addWidget(preview_container)

        history_container = QWidget()
        history_layout = QVBoxLayout(history_container)
        history_layout.setContentsMargins(0, 0, 0, 0)
        history_layout.addWidget(QLabel("This session"))
        self.history = QListWidget()
        self.history.setIconSize(QSize(104, 104))
        self.history.setViewMode(QListWidget.ViewMode.ListMode)
        self.history.setResizeMode(QListWidget.ResizeMode.Adjust)
        self.history.setSpacing(6)
        self.history.currentItemChanged.connect(self.history_selected)
        history_layout.addWidget(self.history)
        splitter.addWidget(history_container)
        splitter.setStretchFactor(0, 1)
        splitter.setStretchFactor(1, 0)
        splitter.setSizes([900, 165])

        self.details_toggle = QToolButton()
        self.details_toggle.setText("Details")
        self.details_toggle.setCheckable(True)
        self.details_toggle.setArrowType(Qt.ArrowType.RightArrow)
        self.details_toggle.toggled.connect(self.toggle_details)
        root_layout.addWidget(self.details_toggle)

        self.details = QPlainTextEdit()
        self.details.setReadOnly(True)
        self.details.setMaximumBlockCount(3000)
        self.details.setPlaceholderText("Krea2 output")
        self.details.setVisible(False)
        self.details.setMaximumHeight(190)
        root_layout.addWidget(self.details)

        self.status = QLabel(f"Ready — {self.cli}")
        self.status.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        root_layout.addWidget(self.status)

    def _load_loras(self) -> None:
        self.lora.clear()
        self.lora.addItem("None")
        candidates: set[Path] = set()
        search_dirs = [self.krea2_root, self.krea2_root / "lora", self.krea2_root / "loras"]
        for directory in search_dirs:
            if not directory.is_dir():
                continue
            try:
                candidates.update(p for p in directory.glob("*.safetensors") if p.is_file())
            except OSError:
                pass

        for path in sorted(candidates, key=lambda p: p.name.casefold()):
            try:
                display = str(path.relative_to(self.krea2_root))
            except ValueError:
                display = str(path)
            self.lora.addItem(display)

    def _prepare_output_watcher(self) -> None:
        try:
            self.out_dir.mkdir(parents=True, exist_ok=True)
        except OSError:
            return
        existing = self.watcher.directories()
        if str(self.out_dir) not in existing:
            self.watcher.addPath(str(self.out_dir))
        self._known_output_paths = set(self._list_output_images())

    def _list_output_images(self) -> list[Path]:
        if not self.out_dir.is_dir():
            return []
        try:
            return [
                p for p in self.out_dir.iterdir()
                if p.is_file() and p.suffix.lower() in IMAGE_SUFFIXES
            ]
        except OSError:
            return []

    def _restore_window_state(self) -> None:
        geometry = self.settings.value("geometry")
        if isinstance(geometry, QByteArray):
            self.restoreGeometry(geometry)

    def _restore_control_state(self) -> None:
        prompt = self.settings.value("controls/prompt", "")
        self.prompt.setPlainText(str(prompt) if prompt is not None else "")

        lora = self.settings.value("controls/lora", "None")
        self.lora.setCurrentText(str(lora) if lora is not None else "None")

        try:
            self.strength.setValue(float(self.settings.value("controls/strength", 1.0)))
        except (TypeError, ValueError):
            self.strength.setValue(1.0)

        seed = self.settings.value("controls/seed", "Random")
        self.seed.setCurrentText(str(seed) if seed is not None else "Random")

        try:
            self.queue.setValue(int(self.settings.value("controls/queue", 1)))
        except (TypeError, ValueError):
            self.queue.setValue(1)

        rebalance = self.settings.value("controls/rebalance", None)
        index = self.rebalance.findData(rebalance)
        self.rebalance.setCurrentIndex(index if index >= 0 else 0)

        details_open = self.settings.value("ui/details_open", False, type=bool)
        self.details_toggle.setChecked(details_open)

    def _save_state(self) -> None:
        self.settings.setValue("geometry", self.saveGeometry())
        self.settings.setValue("controls/prompt", self.prompt.toPlainText())
        self.settings.setValue("controls/lora", self.lora.currentText())
        self.settings.setValue("controls/strength", self.strength.value())
        self.settings.setValue("controls/seed", self.seed.currentText())
        self.settings.setValue("controls/queue", self.queue.value())
        self.settings.setValue("controls/rebalance", self.rebalance.currentData())
        self.settings.setValue("ui/details_open", self.details_toggle.isChecked())
        self.settings.sync()

    def closeEvent(self, event: QCloseEvent) -> None:  # type: ignore[override]
        self._save_state()
        if self.process is not None and self.process.state() != QProcess.ProcessState.NotRunning:
            answer = QMessageBox.question(
                self,
                "Krea2 is running",
                "Cancel the current generation and close?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No,
            )
            if answer != QMessageBox.StandardButton.Yes:
                event.ignore()
                return
            self.cancel_generation()
            if self.process is not None and not self.process.waitForFinished(1500):
                self._force_kill_if_needed()
                self.process.waitForFinished(500)
        event.accept()

    def toggle_details(self, shown: bool) -> None:
        self.details_toggle.setArrowType(Qt.ArrowType.DownArrow if shown else Qt.ArrowType.RightArrow)
        self.details.setVisible(shown)

    def _update_lora_enabled(self) -> None:
        selected = self.lora.currentText().strip()
        enabled = bool(selected and selected.casefold() != "none")
        self.strength.setEnabled(enabled)

    def _refresh_generate_text(self) -> None:
        if self.process is not None and self.process.state() != QProcess.ProcessState.NotRunning:
            self.generate_button.setText("CANCEL QUEUE")
            return
        count = self.queue.value()
        self.generate_button.setText("GENERATE" if count == 1 else f"GENERATE {count} IMAGES")

    def generate_or_cancel(self) -> None:
        if self.process is not None and self.process.state() != QProcess.ProcessState.NotRunning:
            self.cancel_generation()
        else:
            self.start_generation()

    def start_generation(self) -> None:
        prompt = self.prompt.toPlainText().strip()
        if not prompt:
            QMessageBox.warning(self, APP_NAME, "Enter a prompt first.")
            self.prompt.setFocus()
            return

        seed_text = self.seed.currentText().strip()
        explicit_seed: Optional[int] = None
        if seed_text and seed_text.casefold() != "random":
            try:
                explicit_seed = int(seed_text, 10)
            except ValueError:
                QMessageBox.warning(self, APP_NAME, "Seed must be an integer or Random.")
                self.seed.setFocus()
                return
            if explicit_seed < 0:
                QMessageBox.warning(self, APP_NAME, "Seed must be zero or greater.")
                self.seed.setFocus()
                return

        cli_path = shutil.which(self.cli) if os.path.basename(self.cli) == self.cli else self.cli
        if not cli_path or not Path(cli_path).exists():
            QMessageBox.critical(
                self,
                APP_NAME,
                f"Could not find the krea2 CLI: {self.cli}\n\n"
                "Put it in PATH or launch with KREA2_CLI=/path/to/krea2.",
            )
            return

        if not self.krea2_root.is_dir():
            QMessageBox.critical(self, APP_NAME, f"Krea2 directory does not exist:\n{self.krea2_root}")
            return

        args: list[str] = [prompt]
        lora_text = self.lora.currentText().strip()
        lora_value: Optional[str] = None
        strength = self.strength.value()
        if lora_text and lora_text.casefold() != "none":
            lora_value = lora_text
            args.append(lora_text if abs(strength - 1.0) < 1e-9 else f"{lora_text}:{strength:g}")

        if explicit_seed is not None:
            args.extend(["--seed", str(explicit_seed)])

        queue_total = self.queue.value()
        args.extend(["-q", str(queue_total)])

        rebalance_value = self.rebalance.currentData()
        if rebalance_value:
            args.extend(["--rebalance", str(rebalance_value)])

        self._save_state()
        self._known_output_paths = set(self._list_output_images())
        self._run_new_paths.clear()
        self._run_seeds.clear()
        self._run_prompt = prompt
        self._run_lora = lora_value
        self._run_strength = strength
        self._run_rebalance = str(rebalance_value) if rebalance_value else None
        self._run_queue_total = queue_total
        self._cancel_requested = False
        self._elapsed.restart()

        self.details.clear()
        self._append_detail("$ " + self._display_command(str(cli_path), args))
        self.status.setText("Starting Krea2…")
        self.progress.setRange(0, queue_total)
        self.progress.setValue(0)
        self.progress.setFormat(f"0 / {queue_total}")
        self._set_controls_running(True)

        process = QProcess(self)
        process.setProcessChannelMode(QProcess.ProcessChannelMode.MergedChannels)
        process.setWorkingDirectory(str(self.krea2_root))
        process.readyReadStandardOutput.connect(self.process_output)
        process.started.connect(self.process_started)
        process.errorOccurred.connect(self.process_error)
        process.finished.connect(self.process_finished)

        setsid = shutil.which("setsid")
        if setsid:
            process.setProgram(setsid)
            process.setArguments([str(cli_path), *args])
        else:
            process.setProgram(str(cli_path))
            process.setArguments(args)

        self.process = process
        self._process_group_pid = None
        self.output_poll.start()
        self.elapsed_timer.start()
        process.start()

    def _display_command(self, program: str, args: list[str]) -> str:
        import shlex
        return " ".join(shlex.quote(x) for x in [program, *args])

    def process_started(self) -> None:
        if self.process is None:
            return
        pid = int(self.process.processId())
        self._process_group_pid = pid if shutil.which("setsid") else None
        self.status.setText("Krea2 is running…")

    def process_output(self) -> None:
        if self.process is None:
            return
        raw = bytes(self.process.readAllStandardOutput()).decode("utf-8", errors="replace")
        if not raw:
            return
        self._append_detail(raw.rstrip("\n"))
        for line in raw.splitlines():
            clean = line.strip()
            if not clean:
                continue
            seed_match = SEED_RE.search(clean.replace("—", "-"))
            if seed_match:
                index = int(seed_match.group(1))
                total = int(seed_match.group(2))
                seed = int(seed_match.group(3))
                if seed not in self._run_seeds:
                    self._run_seeds.append(seed)
                self.progress.setRange(0, max(total, 1))
                self.progress.setValue(min(index, total))
                self.progress.setFormat(f"{index} / {total} — seed {seed}")
                self.status.setText(clean)
            elif any(word in clean.casefold() for word in ("loading", "decod", "saving", "saved", "generat")):
                self.status.setText(clean)
        self.scan_outputs()

    def process_error(self, error: QProcess.ProcessError) -> None:
        if self.process is None:
            return
        if error == QProcess.ProcessError.FailedToStart:
            self.status.setText("Failed to start Krea2")
            self._append_detail(self.process.errorString())
            self.output_poll.stop()
            self.elapsed_timer.stop()
            self.process = None
            self._process_group_pid = None
            self._set_controls_running(False)

    def process_finished(self, exit_code: int, exit_status: QProcess.ExitStatus) -> None:
        self.scan_outputs()
        self.output_poll.stop()
        self.elapsed_timer.stop()
        elapsed = self._elapsed.elapsed() / 1000.0 if self._elapsed.isValid() else 0.0

        if self._cancel_requested:
            self.status.setText(f"Cancelled after {elapsed:.1f} s")
            self.progress.setFormat("Cancelled")
        elif exit_status == QProcess.ExitStatus.NormalExit and exit_code == 0:
            count = len(self._run_new_paths)
            self.status.setText(f"Done — {count} new image{'s' if count != 1 else ''} — {elapsed:.1f} s")
            maximum = max(self.progress.maximum(), 1)
            self.progress.setRange(0, maximum)
            self.progress.setValue(maximum)
            self.progress.setFormat("Done")
        else:
            self.status.setText(f"Krea2 exited with code {exit_code}")
            self.progress.setFormat(f"Error ({exit_code})")
            if not self.details_toggle.isChecked():
                self.details_toggle.setChecked(True)

        self.process = None
        self._process_group_pid = None
        self._set_controls_running(False)
        self._refresh_generate_text()

    def cancel_generation(self) -> None:
        if self.process is None or self.process.state() == QProcess.ProcessState.NotRunning:
            return
        self._cancel_requested = True
        self.status.setText("Cancelling…")
        pid = self._process_group_pid
        if pid is not None:
            try:
                os.killpg(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            except PermissionError:
                self.process.terminate()
        else:
            self.process.terminate()
        QTimer.singleShot(2000, self._force_kill_if_needed)

    def _force_kill_if_needed(self) -> None:
        if self.process is None or self.process.state() == QProcess.ProcessState.NotRunning:
            return
        pid = self._process_group_pid
        if pid is not None:
            try:
                os.killpg(pid, signal.SIGKILL)
                return
            except (ProcessLookupError, PermissionError):
                pass
        self.process.kill()

    def _set_controls_running(self, running: bool) -> None:
        for widget in (
            self.prompt,
            self.lora,
            self.strength,
            self.seed,
            self.random_seed,
            self.queue,
            self.rebalance,
        ):
            widget.setEnabled(not running)
        if not running:
            self._update_lora_enabled()
        self.generate_button.setText("CANCEL QUEUE" if running else "GENERATE")
        if not running:
            self._refresh_generate_text()

    def scan_outputs(self, *_args) -> None:
        paths = self._list_output_images()
        new_paths = [p for p in paths if p not in self._known_output_paths and p not in self._run_new_paths]
        if not new_paths:
            return
        try:
            new_paths.sort(key=lambda p: (p.stat().st_mtime_ns, p.name))
        except OSError:
            new_paths.sort(key=lambda p: p.name)
        for path in new_paths:
            if self._add_history_image(path):
                self._run_new_paths.add(path)

    def _add_history_image(self, path: Path) -> bool:
        pix = QPixmap(str(path))
        if pix.isNull():
            QTimer.singleShot(350, self.scan_outputs)
            return False

        idx = len(self._run_new_paths) + 1
        seed = self._seed_for_index(idx - 1)
        elapsed = self._elapsed.elapsed() / 1000.0 if self._elapsed.isValid() else 0.0
        meta = GenerationMeta(
            path=str(path),
            prompt=self._run_prompt,
            seed=seed,
            lora=self._run_lora,
            lora_strength=self._run_strength,
            rebalance=self._run_rebalance,
            queue_index=idx,
            queue_total=self._run_queue_total,
            elapsed_seconds=elapsed,
        )
        label = f"{idx}/{self._run_queue_total}"
        if seed is not None:
            label += f"\n{seed}"
        item = QListWidgetItem(QIcon(pix), label)
        item.setData(Qt.ItemDataRole.UserRole, asdict(meta))
        item.setToolTip(path.name)
        self.history.addItem(item)
        self.history.setCurrentItem(item)
        return True

    def _seed_for_index(self, index: int) -> Optional[int]:
        if 0 <= index < len(self._run_seeds):
            return self._run_seeds[index]
        seed_text = self.seed.currentText().strip()
        if seed_text and seed_text.casefold() != "random":
            try:
                return int(seed_text) + index
            except ValueError:
                return None
        return None

    def history_selected(self, current: Optional[QListWidgetItem], _previous: Optional[QListWidgetItem]) -> None:
        if current is None:
            return
        raw = current.data(Qt.ItemDataRole.UserRole)
        if not isinstance(raw, dict):
            return
        meta = GenerationMeta(**raw)
        path = Path(meta.path)
        if not self.preview.set_image(path):
            return
        self._selected_meta = meta
        size = self.preview._pixmap.size()  # local widget state; avoids reopening the image
        seed_text = str(meta.seed) if meta.seed is not None else "unknown"
        self.image_info.setText(
            f"Seed: {seed_text}    {size.width()}×{size.height()}    {meta.elapsed_seconds:.1f} s"
        )
        self.open_file_button.setEnabled(True)
        self.open_folder_button.setEnabled(True)
        self.copy_seed_button.setEnabled(meta.seed is not None)

    def copy_selected_seed(self) -> None:
        if self._selected_meta is None or self._selected_meta.seed is None:
            return
        seed = str(self._selected_meta.seed)
        QApplication.clipboard().setText(seed)
        self.seed.setCurrentText(seed)
        self.status.setText(f"Seed {seed} copied and loaded")

    def open_current_file(self) -> None:
        if self.preview.path is not None:
            self.open_path(str(self.preview.path))

    def open_current_folder(self) -> None:
        if self.preview.path is not None:
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(self.preview.path.parent)))

    def open_path(self, path: str) -> None:
        QDesktopServices.openUrl(QUrl.fromLocalFile(path))

    def refresh_elapsed_label(self) -> None:
        if not self._elapsed.isValid():
            return
        seconds = self._elapsed.elapsed() / 1000.0
        base = self.status.text().split("   •   ", 1)[0]
        self.status.setText(f"{base}   •   {seconds:.1f} s")

    def _append_detail(self, text: str) -> None:
        if not text:
            return
        self.details.appendPlainText(text)
        bar = self.details.verticalScrollBar()
        bar.setValue(bar.maximum())


def main() -> int:
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setOrganizationName(APP_ORG)
    window = Krea2Window()
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
