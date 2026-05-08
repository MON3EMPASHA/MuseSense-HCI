from __future__ import annotations

import json
import math
import re
import time
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np


def _safe_filename(value: str) -> str:
    value = value.strip() or "guest"
    value = re.sub(r"[^\w\- ]+", "", value, flags=re.UNICODE)
    value = re.sub(r"\s+", "_", value).strip("_")
    return value[:80] or "guest"


@dataclass
class GazeSample:
    t: float
    gaze_zone: str
    gaze_delta: float
    gaze_ratio: float | None
    gaze_baseline_ratio: float | None
    emotion: str | None


class GazeSessionLogger:
    def __init__(self, user_name: str, session_started_at: float | None = None):
        self.user_name = user_name.strip() or "guest"
        self.session_started_at = session_started_at if session_started_at is not None else time.time()
        self.samples: list[GazeSample] = []

    def reset(self, user_name: str | None = None) -> None:
        if user_name is not None:
            self.user_name = user_name.strip() or "guest"
        self.session_started_at = time.time()
        self.samples.clear()

    def add_expression(self, expression: dict, monotonic_ts: float | None = None) -> None:
        if not isinstance(expression, dict):
            return

        t = monotonic_ts if monotonic_ts is not None else time.monotonic()
        gaze_zone = str(expression.get("gaze_zone", "center")).strip().lower() or "center"
        if gaze_zone not in {"left", "center", "right"}:
            gaze_zone = "center"

        def to_float(v) -> float | None:
            try:
                return float(v)
            except Exception:
                return None

        gaze_delta = to_float(expression.get("gaze_delta"))
        gaze_ratio = to_float(expression.get("gaze_ratio"))
        gaze_baseline_ratio = to_float(expression.get("gaze_baseline_ratio"))
        emotion = str(expression.get("emotion", "")).strip().lower() or None

        self.samples.append(
            GazeSample(
                t=t,
                gaze_zone=gaze_zone,
                gaze_delta=float(gaze_delta or 0.0),
                gaze_ratio=gaze_ratio,
                gaze_baseline_ratio=gaze_baseline_ratio,
                emotion=emotion,
            )
        )

    def _zone_counts(self) -> dict[str, int]:
        counts = {"left": 0, "center": 0, "right": 0}
        for s in self.samples:
            counts[s.gaze_zone] = counts.get(s.gaze_zone, 0) + 1
        return counts

    def save_report(self, out_dir: Path) -> dict:
        out_dir.mkdir(parents=True, exist_ok=True)

        ts = time.localtime(self.session_started_at)
        stamp = time.strftime("%Y%m%d_%H%M%S", ts)
        user_slug = _safe_filename(self.user_name)

        png_path = out_dir / f"{user_slug}_gaze_{stamp}.png"
        json_path = out_dir / f"{user_slug}_gaze_{stamp}.json"

        payload = self._build_summary_payload(png_path.name, json_path.name)
        self._render_png(png_path)
        json_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        return {"png": str(png_path), "json": str(json_path), "summary": payload}

    def _build_summary_payload(self, png_name: str, json_name: str) -> dict:
        counts = self._zone_counts()
        total = max(sum(counts.values()), 1)
        perc = {k: round((v / total) * 100.0, 1) for k, v in counts.items()}

        deltas = [s.gaze_delta for s in self.samples]
        mean_delta = float(sum(deltas) / len(deltas)) if deltas else 0.0
        max_abs_delta = float(max((abs(d) for d in deltas), default=0.0))

        started = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(self.session_started_at))

        return {
            "type": "gaze_session_report",
            "user": self.user_name,
            "started_at": started,
            "sample_count": len(self.samples),
            "zone_counts": counts,
            "zone_percent": perc,
            "mean_gaze_delta": round(mean_delta, 4),
            "max_abs_gaze_delta": round(max_abs_delta, 4),
            "artifacts": {"png": png_name, "json": json_name},
        }

    def _render_png(self, path: Path) -> None:
        width, height = 1100, 650
        img = np.full((height, width, 3), 245, dtype=np.uint8)

        # Header
        title = f"Gaze Session Report - {self.user_name}"
        cv2.putText(img, title, (30, 55), cv2.FONT_HERSHEY_SIMPLEX, 1.05, (20, 20, 20), 2)
        cv2.putText(
            img,
            time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(self.session_started_at)),
            (30, 90),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.7,
            (70, 70, 70),
            2,
        )

        counts = self._zone_counts()
        total = max(sum(counts.values()), 1)
        zone_order = ["left", "center", "right"]
        colors = {"left": (40, 80, 230), "center": (40, 180, 80), "right": (230, 140, 40)}

        # Bar chart area
        bar_x0, bar_y0 = 40, 130
        bar_w, bar_h = 460, 230
        cv2.rectangle(img, (bar_x0, bar_y0), (bar_x0 + bar_w, bar_y0 + bar_h), (210, 210, 210), 2)
        cv2.putText(img, "Zone Distribution", (bar_x0 + 10, bar_y0 - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (30, 30, 30), 2)

        inner_pad = 24
        slot_w = (bar_w - 2 * inner_pad) // 3
        max_count = max(counts.values()) if counts else 1
        max_count = max(max_count, 1)

        for i, zone in enumerate(zone_order):
            cx0 = bar_x0 + inner_pad + i * slot_w
            cx1 = cx0 + slot_w - 18
            base_y = bar_y0 + bar_h - inner_pad
            h = int(((counts.get(zone, 0) / max_count) if max_count else 0.0) * (bar_h - 2 * inner_pad))
            cv2.rectangle(img, (cx0, base_y - h), (cx1, base_y), colors[zone], -1)
            cv2.rectangle(img, (cx0, base_y - h), (cx1, base_y), (60, 60, 60), 1)

            pct = (counts.get(zone, 0) / total) * 100.0
            label = f"{zone.upper()}: {pct:.1f}% ({counts.get(zone, 0)})"
            cv2.putText(img, label, (cx0, base_y + 28), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (35, 35, 35), 2)

        # Time series area
        ts_x0, ts_y0 = 540, 130
        ts_w, ts_h = 520, 420
        cv2.rectangle(img, (ts_x0, ts_y0), (ts_x0 + ts_w, ts_y0 + ts_h), (210, 210, 210), 2)
        cv2.putText(img, "Gaze Delta Over Time", (ts_x0 + 10, ts_y0 - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (30, 30, 30), 2)

        # Axes/grid
        grid_color = (225, 225, 225)
        for gy in range(1, 5):
            y = ts_y0 + int((gy / 5.0) * ts_h)
            cv2.line(img, (ts_x0, y), (ts_x0 + ts_w, y), grid_color, 1)
        mid_y = ts_y0 + ts_h // 2
        cv2.line(img, (ts_x0, mid_y), (ts_x0 + ts_w, mid_y), (180, 180, 180), 1)

        deltas = [s.gaze_delta for s in self.samples]
        if deltas:
            max_abs = max((abs(d) for d in deltas), default=0.1)
            max_abs = max(max_abs, 0.1)
            # Map last N samples to width
            max_points = 260
            series = deltas[-max_points:]

            def to_xy(idx: int, delta: float) -> tuple[int, int]:
                x = ts_x0 + int((idx / max(1, len(series) - 1)) * (ts_w - 20)) + 10
                y = mid_y - int((delta / max_abs) * (ts_h / 2.0 - 18))
                y = max(ts_y0 + 10, min(ts_y0 + ts_h - 10, y))
                return x, y

            pts = [to_xy(i, d) for i, d in enumerate(series)]
            for i in range(1, len(pts)):
                cv2.line(img, pts[i - 1], pts[i], (60, 60, 200), 2)

            # Legend numbers
            mean_delta = sum(series) / len(series)
            cv2.putText(
                img,
                f"mean={mean_delta:+.3f}  max|d|={max_abs:.3f}  samples={len(self.samples)}",
                (ts_x0 + 12, ts_y0 + ts_h + 34),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (50, 50, 50),
                2,
            )
        else:
            cv2.putText(img, "No samples captured.", (ts_x0 + 12, ts_y0 + 50), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (80, 80, 80), 2)

        # Footer hint
        cv2.putText(
            img,
            "Tip: For best accuracy, face camera, keep eyes visible, avoid strong side head turns.",
            (40, height - 25),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            (70, 70, 70),
            2,
        )

        cv2.imwrite(str(path), img)

