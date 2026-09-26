from __future__ import annotations

import os
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from PySide6.QtCore import QPointF, QRectF, Qt
from PySide6.QtGui import QColor, QImage, QMouseEvent, QPainter, QPen
from PySide6.QtWidgets import (
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFileDialog,
    QFormLayout,
    QGridLayout,
    QGroupBox,
    QHBoxLayout,
    QLabel,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QSizePolicy,
    QSpinBox,
    QVBoxLayout,
    QWidget,
)

IMAGE_FILTER = "Images (*.png *.jpg *.jpeg *.webp *.bmp)"


@dataclass(frozen=True)
class AnyPaintRequest:
    source: str
    mask: str
    prompt: str
    width: int
    height: int
    bbox: tuple[int, int, int, int]
    seed: Optional[int]
    queue: int


class AnyPaintCanvas(QWidget):
    """Small local mask painter. White overlay means AnyPaint may regenerate it."""

    def __init__(self, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self.setMinimumSize(640, 440)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        self.setMouseTracking(True)

        self.source = QImage()
        self.source_mask = QImage()
        self.left_pad = 0
        self.right_pad = 0
        self.top_pad = 0
        self.bottom_pad = 0
        self.brush_size = 64
        self.erase = False
        self._last_source_point: Optional[QPointF] = None

    @property
    def has_source(self) -> bool:
        return not self.source.isNull()

    def set_source(self, path: Path) -> bool:
        image = QImage(str(path))
        if image.isNull():
            return False
        self.source = image.convertToFormat(QImage.Format.Format_RGB32)
        self.source_mask = QImage(self.source.size(), QImage.Format.Format_Grayscale8)
        self.source_mask.fill(0)
        self.update()
        return True

    def set_padding(self, left: int, right: int, top: int, bottom: int) -> None:
        self.left_pad = max(0, left)
        self.right_pad = max(0, right)
        self.top_pad = max(0, top)
        self.bottom_pad = max(0, bottom)
        self.update()

    def canvas_size(self) -> tuple[int, int]:
        if not self.has_source:
            return (0, 0)
        raw_w = self.source.width() + self.left_pad + self.right_pad
        raw_h = self.source.height() + self.top_pad + self.bottom_pad
        width = ((raw_w + 15) // 16) * 16
        height = ((raw_h + 15) // 16) * 16
        return width, height

    def source_bbox(self) -> tuple[int, int, int, int]:
        return (
            self.left_pad,
            self.top_pad,
            self.left_pad + self.source.width(),
            self.top_pad + self.source.height(),
        )

    def generated_mask(self) -> QImage:
        width, height = self.canvas_size()
        if width <= 0 or height <= 0:
            return QImage()
        mask = QImage(width, height, QImage.Format.Format_Grayscale8)
        mask.fill(255)
        painter = QPainter(mask)
        painter.drawImage(self.left_pad, self.top_pad, self.source_mask)
        painter.end()
        return mask

    def has_generated_region(self) -> bool:
        if not self.has_source:
            return False
        if any((self.left_pad, self.right_pad, self.top_pad, self.bottom_pad)):
            return True
        if self.canvas_size() != (self.source.width(), self.source.height()):
            return True
        for y in range(self.source_mask.height()):
            bits = self.source_mask.constScanLine(y)
            if any(bytes(bits[: self.source_mask.bytesPerLine()])):
                return True
        return False

    def clear_mask(self) -> None:
        if not self.source_mask.isNull():
            self.source_mask.fill(0)
            self.update()

    def fill_mask(self) -> None:
        if not self.source_mask.isNull():
            self.source_mask.fill(255)
            self.update()

    def invert_mask(self) -> None:
        if self.source_mask.isNull():
            return
        self.source_mask.invertPixels(QImage.InvertMode.InvertRgb)
        self.update()

    def save_mask(self, path: Path) -> bool:
        mask = self.generated_mask()
        if mask.isNull():
            return False
        path.parent.mkdir(parents=True, exist_ok=True)
        return mask.save(str(path), "PNG")

    def _canvas_rect(self) -> QRectF:
        width, height = self.canvas_size()
        if width <= 0 or height <= 0:
            return QRectF()
        margin = 12.0
        avail_w = max(1.0, self.width() - margin * 2)
        avail_h = max(1.0, self.height() - margin * 2)
        scale = min(avail_w / width, avail_h / height)
        draw_w = width * scale
        draw_h = height * scale
        return QRectF((self.width() - draw_w) / 2, (self.height() - draw_h) / 2, draw_w, draw_h)

    def _widget_to_canvas(self, point: QPointF) -> Optional[QPointF]:
        rect = self._canvas_rect()
        width, height = self.canvas_size()
        if rect.isNull() or not rect.contains(point):
            return None
        x = (point.x() - rect.left()) * width / rect.width()
        y = (point.y() - rect.top()) * height / rect.height()
        return QPointF(x, y)

    def _canvas_to_source(self, point: QPointF) -> Optional[QPointF]:
        x = point.x() - self.left_pad
        y = point.y() - self.top_pad
        if x < 0 or y < 0 or x >= self.source.width() or y >= self.source.height():
            return None
        return QPointF(x, y)

    def _paint_to(self, source_point: QPointF) -> None:
        if self.source_mask.isNull():
            return
        painter = QPainter(self.source_mask)
        level = 0 if self.erase else 255
        pen = QPen(QColor(level, level, level))
        pen.setWidth(self.brush_size)
        pen.setCapStyle(Qt.PenCapStyle.RoundCap)
        pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
        painter.setPen(pen)
        previous = self._last_source_point or source_point
        painter.drawLine(previous, source_point)
        painter.end()
        self._last_source_point = source_point
        self.update()

    def mousePressEvent(self, event: QMouseEvent) -> None:  # type: ignore[override]
        if event.button() not in (Qt.MouseButton.LeftButton, Qt.MouseButton.RightButton):
            return super().mousePressEvent(event)
        canvas_point = self._widget_to_canvas(event.position())
        source_point = self._canvas_to_source(canvas_point) if canvas_point is not None else None
        if source_point is None:
            return
        self._last_source_point = None
        previous_erase = self.erase
        if event.button() == Qt.MouseButton.RightButton:
            self.erase = True
        self._paint_to(source_point)
        self.erase = previous_erase

    def mouseMoveEvent(self, event: QMouseEvent) -> None:  # type: ignore[override]
        if not (event.buttons() & (Qt.MouseButton.LeftButton | Qt.MouseButton.RightButton)):
            return super().mouseMoveEvent(event)
        canvas_point = self._widget_to_canvas(event.position())
        source_point = self._canvas_to_source(canvas_point) if canvas_point is not None else None
        if source_point is None:
            self._last_source_point = None
            return
        previous_erase = self.erase
        if event.buttons() & Qt.MouseButton.RightButton:
            self.erase = True
        self._paint_to(source_point)
        self.erase = previous_erase

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:  # type: ignore[override]
        self._last_source_point = None
        super().mouseReleaseEvent(event)

    def paintEvent(self, _event) -> None:  # type: ignore[override]
        painter = QPainter(self)
        painter.fillRect(self.rect(), self.palette().window())
        if not self.has_source:
            painter.setPen(self.palette().text().color())
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "Choose an image to edit")
            return

        rect = self._canvas_rect()
        canvas_w, canvas_h = self.canvas_size()
        composed = QImage(canvas_w, canvas_h, QImage.Format.Format_RGB32)
        composed.fill(QColor(48, 48, 48))
        base = QPainter(composed)
        base.drawImage(self.left_pad, self.top_pad, self.source)
        base.end()

        painter.drawImage(rect, composed)

        mask = self.generated_mask()
        overlay = QImage(mask.size(), QImage.Format.Format_ARGB32_Premultiplied)
        overlay.fill(QColor(255, 255, 255, 255))
        overlay.setAlphaChannel(mask)
        painter.setOpacity(0.42)
        painter.drawImage(rect, overlay)
        painter.setOpacity(1.0)

        pen = QPen(self.palette().text().color())
        pen.setWidth(1)
        painter.setPen(pen)
        painter.drawRect(rect)


class AnyPaintDialog(QDialog):
    def __init__(
        self,
        krea2_root: Path,
        *,
        initial_source: Optional[Path] = None,
        initial_prompt: str = "",
        initial_seed: str = "Random",
        initial_queue: int = 1,
        parent: Optional[QWidget] = None,
    ) -> None:
        super().__init__(parent)
        self.krea2_root = krea2_root
        self.source_path: Optional[Path] = None
        self.request: Optional[AnyPaintRequest] = None

        self.setWindowTitle("Krea2 AnyPaint — Edit / Inpaint / Outpaint")
        self.resize(1080, 860)
        self.setMinimumSize(860, 700)

        layout = QVBoxLayout(self)

        source_row = QHBoxLayout()
        self.source_label = QLabel("No source image")
        self.source_label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        source_row.addWidget(self.source_label, stretch=1)
        choose = QPushButton("Choose Image…")
        choose.clicked.connect(self.choose_source)
        source_row.addWidget(choose)
        layout.addLayout(source_row)

        self.canvas = AnyPaintCanvas()
        layout.addWidget(self.canvas, stretch=1)

        tools = QHBoxLayout()
        self.paint_button = QPushButton("Paint Generate Mask")
        self.paint_button.setCheckable(True)
        self.paint_button.setChecked(True)
        self.erase_button = QPushButton("Erase Mask")
        self.erase_button.setCheckable(True)
        self.paint_button.clicked.connect(lambda: self._set_tool(False))
        self.erase_button.clicked.connect(lambda: self._set_tool(True))
        tools.addWidget(self.paint_button)
        tools.addWidget(self.erase_button)
        tools.addWidget(QLabel("Brush"))
        self.brush = QSpinBox()
        self.brush.setRange(4, 512)
        self.brush.setSingleStep(4)
        self.brush.setValue(64)
        self.brush.valueChanged.connect(self._brush_changed)
        tools.addWidget(self.brush)
        clear = QPushButton("Clear")
        clear.clicked.connect(self.canvas.clear_mask)
        fill = QPushButton("Mask All")
        fill.clicked.connect(self.canvas.fill_mask)
        invert = QPushButton("Invert")
        invert.clicked.connect(self.canvas.invert_mask)
        tools.addWidget(clear)
        tools.addWidget(fill)
        tools.addWidget(invert)
        tools.addStretch(1)
        tools.addWidget(QLabel("Left drag = current tool; right drag = erase"))
        layout.addLayout(tools)

        outpaint_group = QGroupBox("Outpaint padding (pixels; outside the source is always generated)")
        outpaint = QGridLayout(outpaint_group)
        self.pad_left = self._padding_spin()
        self.pad_right = self._padding_spin()
        self.pad_top = self._padding_spin()
        self.pad_bottom = self._padding_spin()
        outpaint.addWidget(QLabel("Left"), 0, 0)
        outpaint.addWidget(self.pad_left, 1, 0)
        outpaint.addWidget(QLabel("Right"), 0, 1)
        outpaint.addWidget(self.pad_right, 1, 1)
        outpaint.addWidget(QLabel("Top"), 0, 2)
        outpaint.addWidget(self.pad_top, 1, 2)
        outpaint.addWidget(QLabel("Bottom"), 0, 3)
        outpaint.addWidget(self.pad_bottom, 1, 3)
        self.canvas_info = QLabel("Canvas: —")
        outpaint.addWidget(self.canvas_info, 1, 4)
        layout.addWidget(outpaint_group)

        layout.addWidget(QLabel("Prompt — describe the complete desired output image"))
        self.prompt = QPlainTextEdit()
        self.prompt.setPlaceholderText("Describe the complete edited image…")
        self.prompt.setPlainText(initial_prompt)
        self.prompt.setMaximumHeight(110)
        layout.addWidget(self.prompt)

        options = QFormLayout()
        self.seed = QComboBox()
        self.seed.setEditable(True)
        self.seed.addItem("Random")
        self.seed.setCurrentText(initial_seed or "Random")
        self.queue = QSpinBox()
        self.queue.setRange(1, 99)
        self.queue.setValue(max(1, min(initial_queue, 99)))
        options.addRow("Seed", self.seed)
        options.addRow("Images", self.queue)
        layout.addLayout(options)

        hint = QLabel(
            "White overlay = generated/edited. Uncovered source pixels are preserved except for AnyPaint's blend seam."
        )
        hint.setWordWrap(True)
        layout.addWidget(hint)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Cancel)
        self.run_button = buttons.addButton("RUN ANYPAINT", QDialogButtonBox.ButtonRole.AcceptRole)
        self.run_button.setMinimumHeight(42)
        buttons.accepted.connect(self._accept_request)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

        for spin in (self.pad_left, self.pad_right, self.pad_top, self.pad_bottom):
            spin.valueChanged.connect(self._padding_changed)

        if initial_source is not None and initial_source.is_file():
            self.load_source(initial_source)

    def _padding_spin(self) -> QSpinBox:
        spin = QSpinBox()
        spin.setRange(0, 4096)
        spin.setSingleStep(16)
        spin.setSuffix(" px")
        return spin

    def _set_tool(self, erase: bool) -> None:
        self.canvas.erase = erase
        self.paint_button.setChecked(not erase)
        self.erase_button.setChecked(erase)

    def _brush_changed(self, value: int) -> None:
        self.canvas.brush_size = value

    def _padding_changed(self) -> None:
        self.canvas.set_padding(
            self.pad_left.value(),
            self.pad_right.value(),
            self.pad_top.value(),
            self.pad_bottom.value(),
        )
        self._refresh_canvas_info()

    def _refresh_canvas_info(self) -> None:
        width, height = self.canvas.canvas_size()
        if width and height:
            self.canvas_info.setText(f"Canvas: {width}×{height}")
        else:
            self.canvas_info.setText("Canvas: —")

    def choose_source(self) -> None:
        start = str(self.source_path.parent if self.source_path else self.krea2_root / "out")
        selected, _ = QFileDialog.getOpenFileName(self, "Choose source image", start, IMAGE_FILTER)
        if selected:
            self.load_source(Path(selected))

    def load_source(self, path: Path) -> None:
        if not self.canvas.set_source(path):
            QMessageBox.warning(self, "Krea2 AnyPaint", f"Could not load image:\n{path}")
            return
        self.source_path = path
        self.source_label.setText(str(path))
        self._refresh_canvas_info()

    def _accept_request(self) -> None:
        if self.source_path is None or not self.canvas.has_source:
            QMessageBox.warning(self, "Krea2 AnyPaint", "Choose a source image first.")
            return
        prompt = self.prompt.toPlainText().strip()
        if not prompt:
            QMessageBox.warning(self, "Krea2 AnyPaint", "Enter a prompt first.")
            self.prompt.setFocus()
            return
        if not self.canvas.has_generated_region():
            QMessageBox.warning(
                self,
                "Krea2 AnyPaint",
                "Paint at least one area to regenerate, or add outpaint padding.",
            )
            return

        seed_text = self.seed.currentText().strip()
        seed: Optional[int] = None
        if seed_text and seed_text.casefold() != "random":
            try:
                seed = int(seed_text, 10)
            except ValueError:
                QMessageBox.warning(self, "Krea2 AnyPaint", "Seed must be an integer or Random.")
                return
            if seed < 0:
                QMessageBox.warning(self, "Krea2 AnyPaint", "Seed must be zero or greater.")
                return

        temp_dir = self.krea2_root / "tmp" / "anypaint-gui" / f"{int(time.time())}-{os.getpid()}"
        mask_path = temp_dir / "mask.png"
        if not self.canvas.save_mask(mask_path):
            QMessageBox.critical(self, "Krea2 AnyPaint", f"Could not save temporary mask:\n{mask_path}")
            return

        width, height = self.canvas.canvas_size()
        self.request = AnyPaintRequest(
            source=str(self.source_path),
            mask=str(mask_path),
            prompt=prompt,
            width=width,
            height=height,
            bbox=self.canvas.source_bbox(),
            seed=seed,
            queue=self.queue.value(),
        )
        self.accept()
