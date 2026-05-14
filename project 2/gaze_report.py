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


def _wrap_text(text: str, max_chars: int) -> list[str]:
    text = (text or "").strip()
    if not text:
        return [""]
    words = text.split()
    lines: list[str] = []
    current: list[str] = []
    current_len = 0
    for w in words:
        wlen = len(w)
        next_len = wlen if not current else current_len + 1 + wlen
        if current and next_len > max_chars:
            lines.append(" ".join(current))
            current = [w]
            current_len = wlen
        else:
            current.append(w)
            current_len = next_len
    if current:
        lines.append(" ".join(current))
    return lines


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
        self.session_started_at = (
            session_started_at if session_started_at is not None else time.time()
        )
        self.samples: list[GazeSample] = []

    def reset(self, user_name: str | None = None) -> None:
        if user_name is not None:
            self.user_name = user_name.strip() or "guest"
        self.session_started_at = time.time()
        self.samples.clear()

    def add_expression(
        self, expression: dict, monotonic_ts: float | None = None
    ) -> None:
        if not isinstance(expression, dict):
            return

        t = monotonic_ts if monotonic_ts is not None else time.monotonic()
        gaze_zone = (
            str(expression.get("gaze_zone", "center")).strip().lower() or "center"
        )
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
        heatmap_path = out_dir / f"{user_slug}_gaze_{stamp}_heatmap.png"
        combo_path = out_dir / f"{user_slug}_gaze_{stamp}_combo.png"

        payload = self._build_summary_payload(
            png_path.name, json_path.name, heatmap_path.name, combo_path.name
        )
        self._render_png(png_path)
        self._render_heatmap(heatmap_path)
        self._render_combo(png_path, heatmap_path, combo_path)
        json_path.write_text(
            json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8"
        )
        return {
            "png": str(png_path),
            "json": str(json_path),
            "heatmap": str(heatmap_path),
            "combo": str(combo_path),
            "summary": payload,
        }

    def save_emotion_report(self, out_dir: Path) -> dict:
        out_dir.mkdir(parents=True, exist_ok=True)

        ts = time.localtime(self.session_started_at)
        stamp = time.strftime("%Y%m%d_%H%M%S", ts)
        user_slug = _safe_filename(self.user_name)

        png_path = out_dir / f"{user_slug}_emotion_{stamp}.png"
        json_path = out_dir / f"{user_slug}_emotion_{stamp}.json"

        counts: dict[str, int] = {}
        for sample in self.samples:
            emotion = sample.emotion or "unknown"
            counts[emotion] = counts.get(emotion, 0) + 1

        total = max(sum(counts.values()), 1)
        perc = {k: round((v / total) * 100.0, 1) for k, v in counts.items()}
        dominant = max(counts, key=counts.get) if counts else "unknown"

        payload = {
            "type": "emotion_session_report",
            "user": self.user_name,
            "started_at": time.strftime(
                "%Y-%m-%d %H:%M:%S", time.localtime(self.session_started_at)
            ),
            "sample_count": len(self.samples),
            "emotion_counts": counts,
            "emotion_percent": perc,
            "dominant_emotion": dominant,
        }

        json_path.write_text(
            json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8"
        )
        self._render_emotion_png(png_path, counts, perc)
        return {
            "png": str(png_path),
            "json": str(json_path),
            "summary": payload,
        }

    def _build_summary_payload(
        self, png_name: str, json_name: str, heatmap_name: str, combo_name: str
    ) -> dict:
        counts = self._zone_counts()
        total = max(sum(counts.values()), 1)
        perc = {k: round((v / total) * 100.0, 1) for k, v in counts.items()}

        deltas = [s.gaze_delta for s in self.samples]
        mean_delta = float(sum(deltas) / len(deltas)) if deltas else 0.0
        max_abs_delta = float(max((abs(d) for d in deltas), default=0.0))

        # Transition matrix: zone_i → zone_{i+1}
        transitions: dict[str, dict[str, int]] = {
            "left":   {"left": 0, "center": 0, "right": 0},
            "center": {"left": 0, "center": 0, "right": 0},
            "right":  {"left": 0, "center": 0, "right": 0},
        }
        for prev, curr in zip(self.samples, self.samples[1:]):
            if prev.gaze_zone in transitions and curr.gaze_zone in transitions[prev.gaze_zone]:
                transitions[prev.gaze_zone][curr.gaze_zone] += 1

        # Dwell durations per zone (sum of time spent in each zone segment)
        dwell_seconds: dict[str, float] = {"left": 0.0, "center": 0.0, "right": 0.0}
        if len(self.samples) >= 2:
            for prev, curr in zip(self.samples, self.samples[1:]):
                dt = max(0.0, curr.t - prev.t)
                dwell_seconds[prev.gaze_zone] = dwell_seconds.get(prev.gaze_zone, 0.0) + dt

        dominant_zone = max(counts, key=counts.get) if counts else "center"

        # Per-zone average |gaze_delta|
        zone_delta_sums: dict[str, float] = {"left": 0.0, "center": 0.0, "right": 0.0}
        zone_delta_n:    dict[str, int]   = {"left": 0,   "center": 0,   "right": 0}
        for s in self.samples:
            zone_delta_sums[s.gaze_zone] = zone_delta_sums.get(s.gaze_zone, 0.0) + abs(s.gaze_delta or 0.0)
            zone_delta_n[s.gaze_zone]    = zone_delta_n.get(s.gaze_zone, 0) + 1
        zone_mean_abs_delta = {
            z: round(zone_delta_sums[z] / zone_delta_n[z], 4) if zone_delta_n.get(z, 0) else 0.0
            for z in ("left", "center", "right")
        }

        started = time.strftime(
            "%Y-%m-%d %H:%M:%S", time.localtime(self.session_started_at)
        )

        return {
            "type": "gaze_session_report",
            "user": self.user_name,
            "started_at": started,
            "sample_count": len(self.samples),
            "zone_counts": counts,
            "zone_percent": perc,
            "dominant_zone": dominant_zone,
            "dwell_seconds": {k: round(v, 2) for k, v in dwell_seconds.items()},
            "transitions": transitions,
            "zone_mean_abs_delta": zone_mean_abs_delta,
            "mean_gaze_delta": round(mean_delta, 4),
            "max_abs_gaze_delta": round(max_abs_delta, 4),
            "artifacts": {
                "png": png_name,
                "json": json_name,
                "heatmap": heatmap_name,
                "combo": combo_name,
            },
        }

    def _find_user_profile_path(self) -> Path | None:
        base = Path(__file__).resolve().parent
        candidates = [
            base / "users.json",
            base / "TUIO11_NET-master" / "bin" / "Debug" / "users.json",
            base.parent / "project 1" / "users.json",
        ]
        target_name = self.user_name.strip().lower()
        for candidate in candidates:
            if not candidate.exists():
                continue
            try:
                users = json.loads(candidate.read_text(encoding="utf-8"))
            except Exception:
                continue
            if not isinstance(users, list):
                continue
            for user in users:
                if not isinstance(user, dict):
                    continue
                name = str(user.get("name", "")).strip().lower()
                if not name or name != target_name:
                    continue
                profile = str(user.get("Profile", "")).strip()
                if not profile:
                    continue
                profile_path = (candidate.parent / profile).resolve()
                if profile_path.exists():
                    return profile_path
                alt_path = (base / profile).resolve()
                if alt_path.exists():
                    return alt_path
        return None

    def _load_user_photo(self) -> np.ndarray | None:
        profile_path = self._find_user_profile_path()
        if profile_path is None:
            return None
        photo = cv2.imread(str(profile_path), cv2.IMREAD_COLOR)
        if photo is None:
            return None
        return photo

    def _render_png(self, path: Path) -> None:
        width, height = 1280, 720
        img = np.full((height, width, 3), 252, dtype=np.uint8)

        # Palette
        text_primary = (25, 25, 25)
        text_muted = (95, 95, 95)
        border = (220, 220, 220)
        card_bg = (255, 255, 255)
        accent = (210, 120, 40)
        series_color = (180, 70, 70)

        def card(x: int, y: int, w: int, h: int) -> None:
            cv2.rectangle(img, (x, y), (x + w, y + h), card_bg, -1)
            cv2.rectangle(img, (x, y), (x + w, y + h), border, 2)

        # Header card (auto-wrap user name to avoid overlap)
        header_x, header_y, header_w, header_h = 30, 24, width - 60, 116
        card(header_x, header_y, header_w, header_h)

        report_title = "Gaze Tracking Session Report"
        cv2.putText(
            img,
            report_title,
            (header_x + 20, header_y + 42),
            cv2.FONT_HERSHEY_SIMPLEX,
            1.05,
            text_primary,
            2,
        )

        started = time.strftime(
            "%Y-%m-%d %H:%M:%S", time.localtime(self.session_started_at)
        )
        cv2.putText(
            img,
            started,
            (header_x + 20, header_y + 78),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.72,
            text_muted,
            2,
        )

        user_photo = self._load_user_photo()
        photo_size = 84
        photo_x = header_x + header_w - 20 - photo_size
        photo_y = header_y + 18
        if user_photo is not None:
            h, w = user_photo.shape[:2]
            side = max(1, min(h, w))
            y0 = (h - side) // 2
            x0 = (w - side) // 2
            cropped = user_photo[y0 : y0 + side, x0 : x0 + side]
            resized = cv2.resize(
                cropped, (photo_size, photo_size), interpolation=cv2.INTER_AREA
            )
            cv2.rectangle(
                img,
                (photo_x - 2, photo_y - 2),
                (photo_x + photo_size + 2, photo_y + photo_size + 2),
                border,
                2,
            )
            img[photo_y : photo_y + photo_size, photo_x : photo_x + photo_size] = (
                resized
            )

        user_lines = _wrap_text(f"User: {self.user_name}", max_chars=32)
        uy = header_y + 42
        user_text_x = header_x + 620
        for idx, line in enumerate(user_lines[:2]):
            cv2.putText(
                img,
                line,
                (user_text_x, uy + idx * 34),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.78,
                text_primary,
                2,
            )

        counts = self._zone_counts()
        total = max(sum(counts.values()), 1)
        zone_order = ["left", "center", "right"]
        colors = {
            "left": (210, 90, 60),
            "center": (70, 170, 110),
            "right": (60, 125, 210),
        }

        # Left card: distribution + key metrics
        left_x, left_y, left_w, left_h = 30, 160, 520, 530
        card(left_x, left_y, left_w, left_h)
        cv2.putText(
            img,
            "Distribution",
            (left_x + 20, left_y + 44),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.9,
            text_primary,
            2,
        )

        # Bar chart sub-area — slightly shorter so the Summary block below
        # has clean space and the in-bar labels don't collide with the heading.
        bar_x0, bar_y0 = left_x + 20, left_y + 70
        bar_w, bar_h = left_w - 40, 220
        cv2.rectangle(
            img, (bar_x0, bar_y0), (bar_x0 + bar_w, bar_y0 + bar_h), (248, 248, 248), -1
        )
        cv2.rectangle(
            img, (bar_x0, bar_y0), (bar_x0 + bar_w, bar_y0 + bar_h), border, 1
        )

        inner_pad = 24
        slot_w = (bar_w - 2 * inner_pad) // 3
        max_count = max(counts.values()) if counts else 1
        max_count = max(max_count, 1)

        for i, zone in enumerate(zone_order):
            cx0 = bar_x0 + inner_pad + i * slot_w
            cx1 = cx0 + slot_w - 18
            base_y = bar_y0 + bar_h - inner_pad
            h = int(
                ((counts.get(zone, 0) / max_count) if max_count else 0.0)
                * (bar_h - 2 * inner_pad)
            )
            cv2.rectangle(img, (cx0, base_y - h), (cx1, base_y), colors[zone], -1)
            cv2.rectangle(img, (cx0, base_y - h), (cx1, base_y), (60, 60, 60), 1)

            pct = (counts.get(zone, 0) / total) * 100.0
            label = f"{zone.upper()}  {pct:.1f}%"
            sub = f"{counts.get(zone, 0)} samples"
            cv2.putText(
                img,
                label,
                (cx0, base_y + 28),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.62,
                text_primary,
                2,
            )
            cv2.putText(
                img,
                sub,
                (cx0, base_y + 54),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.52,
                text_muted,
                2,
            )

        # Metrics + dwell + dominant zone
        deltas = [s.gaze_delta for s in self.samples]
        mean_delta = float(sum(deltas) / len(deltas)) if deltas else 0.0
        max_abs_delta = float(max((abs(d) for d in deltas), default=0.0))

        # Dwell calc (time spent per zone)
        dwell_seconds = {"left": 0.0, "center": 0.0, "right": 0.0}
        if len(self.samples) >= 2:
            for prev, curr in zip(self.samples, self.samples[1:]):
                dt = max(0.0, curr.t - prev.t)
                dwell_seconds[prev.gaze_zone] = dwell_seconds.get(prev.gaze_zone, 0.0) + dt
        total_dwell = sum(dwell_seconds.values())

        dominant = max(counts, key=counts.get) if counts else "center"

        mx0 = left_x + 20
        my0 = left_y + 380
        cv2.putText(
            img, "Summary", (mx0, my0), cv2.FONT_HERSHEY_SIMPLEX, 0.9, text_primary, 2
        )
        cv2.putText(
            img,
            f"Samples: {len(self.samples)}    Dominant: {dominant.upper()}",
            (mx0, my0 + 36),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.66,
            text_primary,
            2,
        )

        # Dwell line (only if any non-zero)
        if total_dwell > 0.05:
            dwell_label = (
                f"Dwell  L:{dwell_seconds['left']:.1f}s  "
                f"C:{dwell_seconds['center']:.1f}s  "
                f"R:{dwell_seconds['right']:.1f}s"
            )
        else:
            dwell_label = "Dwell  -"
        cv2.putText(
            img,
            dwell_label,
            (mx0, my0 + 66),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            text_primary,
            2,
        )

        cv2.putText(
            img,
            f"Mean delta: {mean_delta:+.3f}    Max |delta|: {max_abs_delta:.3f}",
            (mx0, my0 + 96),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            text_primary,
            2,
        )

        # Transitions row (compact)
        transitions = {
            "left":   {"left": 0, "center": 0, "right": 0},
            "center": {"left": 0, "center": 0, "right": 0},
            "right":  {"left": 0, "center": 0, "right": 0},
        }
        for prev, curr in zip(self.samples, self.samples[1:]):
            if prev.gaze_zone in transitions and curr.gaze_zone in transitions[prev.gaze_zone]:
                transitions[prev.gaze_zone][curr.gaze_zone] += 1
        most_common_transitions = []
        for src in ("left", "center", "right"):
            for dst in ("left", "center", "right"):
                if src == dst:
                    continue
                c = transitions[src][dst]
                if c > 0:
                    most_common_transitions.append((c, src, dst))
        most_common_transitions.sort(reverse=True)
        if most_common_transitions:
            top = most_common_transitions[:3]
            # ASCII arrow — Hershey fonts don't render unicode glyphs.
            parts = [f"{src[0].upper()}->{dst[0].upper()}:{cnt}" for cnt, src, dst in top]
            trans_text = "Top moves  " + "   ".join(parts)
        else:
            trans_text = "Top moves  -"
        cv2.putText(
            img,
            trans_text,
            (mx0, my0 + 126),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            text_primary,
            2,
        )

        # Right card: time series
        right_x, right_y, right_w, right_h = 580, 160, width - 610, 530
        card(right_x, right_y, right_w, right_h)
        cv2.putText(
            img,
            "Gaze Delta Over Time",
            (right_x + 20, right_y + 44),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.9,
            text_primary,
            2,
        )

        # Axes/grid
        ts_x0, ts_y0 = right_x + 20, right_y + 70
        ts_w, ts_h = right_w - 40, right_h - 110
        cv2.rectangle(
            img, (ts_x0, ts_y0), (ts_x0 + ts_w, ts_y0 + ts_h), (248, 248, 248), -1
        )
        cv2.rectangle(img, (ts_x0, ts_y0), (ts_x0 + ts_w, ts_y0 + ts_h), border, 1)

        grid_color = (235, 235, 235)
        for gy in range(1, 5):
            y = ts_y0 + int((gy / 5.0) * ts_h)
            cv2.line(img, (ts_x0, y), (ts_x0 + ts_w, y), grid_color, 1)
        mid_y = ts_y0 + ts_h // 2
        cv2.line(img, (ts_x0, mid_y), (ts_x0 + ts_w, mid_y), (200, 200, 200), 1)

        if deltas:
            max_abs = max((abs(d) for d in deltas), default=0.15)
            max_abs = max(max_abs, 0.15)

            max_points = 420
            series = deltas[-max_points:]

            def to_xy(idx: int, delta: float) -> tuple[int, int]:
                x = ts_x0 + int((idx / max(1, len(series) - 1)) * (ts_w - 20)) + 10
                y = mid_y - int((delta / max_abs) * (ts_h / 2.0 - 18))
                y = max(ts_y0 + 10, min(ts_y0 + ts_h - 10, y))
                return x, y

            pts = [to_xy(i, d) for i, d in enumerate(series)]
            for i in range(1, len(pts)):
                cv2.line(img, pts[i - 1], pts[i], series_color, 2)

            # Tiny legend
            cv2.putText(
                img,
                f"Scale: +/-{max_abs:.2f}",
                (ts_x0 + 12, ts_y0 + ts_h - 12),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                text_muted,
                2,
            )
        else:
            cv2.putText(
                img,
                "No gaze samples captured.",
                (ts_x0 + 16, ts_y0 + 50),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.75,
                text_muted,
                2,
            )

        cv2.imwrite(str(path), img)

    def _render_combo(
        self, report_path: Path, heatmap_path: Path, out_path: Path
    ) -> None:
        report = cv2.imread(str(report_path), cv2.IMREAD_COLOR)
        heatmap = cv2.imread(str(heatmap_path), cv2.IMREAD_COLOR)
        if report is None or heatmap is None:
            return

        gap = 24
        width = max(report.shape[1], heatmap.shape[1])
        height = report.shape[0] + heatmap.shape[0] + gap
        canvas = np.full((height, width, 3), 245, dtype=np.uint8)

        canvas[0 : report.shape[0], 0 : report.shape[1]] = report
        y2 = report.shape[0] + gap
        canvas[y2 : y2 + heatmap.shape[0], 0 : heatmap.shape[1]] = heatmap

        cv2.imwrite(str(out_path), canvas)

    def _render_emotion_png(
        self, path: Path, counts: dict[str, int], perc: dict[str, float]
    ) -> None:
        width, height = 1280, 720
        img = np.full((height, width, 3), 252, dtype=np.uint8)

        text_primary = (25, 25, 25)
        text_muted = (95, 95, 95)
        border = (220, 220, 220)
        card_bg = (255, 255, 255)
        colors = {
            "happy": (70, 170, 110),
            "surprised": (210, 140, 60),
            "neutral": (120, 120, 120),
            "sad": (70, 110, 180),
            "unknown": (160, 160, 160),
        }

        def card(x: int, y: int, w: int, h: int) -> None:
            cv2.rectangle(img, (x, y), (x + w, y + h), card_bg, -1)
            cv2.rectangle(img, (x, y), (x + w, y + h), border, 2)

        card(30, 24, width - 60, 110)
        cv2.putText(
            img,
            "Emotion Session Report",
            (50, 70),
            cv2.FONT_HERSHEY_SIMPLEX,
            1.0,
            text_primary,
            2,
        )
        cv2.putText(
            img,
            f"User: {self.user_name}",
            (50, 104),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.72,
            text_muted,
            2,
        )

        card_x, card_y, card_w, card_h = 30, 160, width - 60, 520
        card(card_x, card_y, card_w, card_h)

        if not counts:
            cv2.putText(
                img,
                "No emotion samples captured.",
                (card_x + 30, card_y + 70),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                text_muted,
                2,
            )
            cv2.imwrite(str(path), img)
            return

        order = ["happy", "surprised", "neutral", "sad", "unknown"]
        labels = [e for e in order if e in counts]
        labels += [e for e in counts.keys() if e not in labels]

        bar_x0, bar_y0 = card_x + 30, card_y + 40
        bar_w, bar_h = card_w - 60, 360
        cv2.rectangle(
            img,
            (bar_x0, bar_y0),
            (bar_x0 + bar_w, bar_y0 + bar_h),
            (248, 248, 248),
            -1,
        )
        cv2.rectangle(
            img,
            (bar_x0, bar_y0),
            (bar_x0 + bar_w, bar_y0 + bar_h),
            border,
            1,
        )

        max_count = max(counts.values()) if counts else 1
        slot_w = int(bar_w / max(len(labels), 1))
        inner_pad = 16
        for i, emotion in enumerate(labels):
            cx0 = bar_x0 + i * slot_w + inner_pad
            cx1 = cx0 + slot_w - 2 * inner_pad
            base_y = bar_y0 + bar_h - 20
            h = int((counts.get(emotion, 0) / max_count) * (bar_h - 50))
            color = colors.get(emotion, colors["unknown"])
            cv2.rectangle(img, (cx0, base_y - h), (cx1, base_y), color, -1)
            cv2.rectangle(img, (cx0, base_y - h), (cx1, base_y), (60, 60, 60), 1)

            label = f"{emotion.upper()} {perc.get(emotion, 0.0):.1f}%"
            sub = f"{counts.get(emotion, 0)} samples"
            cv2.putText(
                img,
                label,
                (cx0, base_y + 28),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                text_primary,
                2,
            )
            cv2.putText(
                img,
                sub,
                (cx0, base_y + 50),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                text_muted,
                2,
            )

        cv2.imwrite(str(path), img)

    def _render_heatmap(self, path: Path) -> None:
        """Render a 2-D gaze heatmap overlaid on a virtual-screen frame.

        X axis = horizontal gaze direction in user-perspective ("left" gazes
        land on the left third, "right" gazes on the right third, "center"
        gazes in the middle). Within each zone we offset further toward the
        edge proportionally to |gaze_delta| (stronger gaze → further edge).

        Y axis = deterministic Gaussian-jitter around screen-center, so each
        sample becomes a small splat rather than collapsing onto one line.
        This avoids a featureless horizontal stripe and produces a real 2-D
        density distribution.
        """
        width, height = 1280, 720
        canvas = np.full((height, width, 3), 18, dtype=np.uint8)  # near-black bg

        # Virtual "screen" frame the user was looking at
        margin_x, margin_y = 90, 110
        sx1, sy1 = margin_x, margin_y
        sx2, sy2 = width - margin_x, height - margin_y
        sw, sh = sx2 - sx1, sy2 - sy1

        heat = np.zeros((sh, sw), dtype=np.float32)

        rng = np.random.default_rng(seed=42)

        for sample in self.samples:
            zone = sample.gaze_zone
            # Magnitude (clamped) controls how far the splat is pushed toward
            # the edge of its zone. Mean delta on a strong look is ~0.10.
            delta_mag = min(0.12, abs(sample.gaze_delta or 0.0))

            if zone == "left":
                base_x = 0.20 - delta_mag * 0.7   # pushes toward 0.12 max
            elif zone == "right":
                base_x = 0.80 + delta_mag * 0.7   # pushes toward 0.88 max
            else:
                base_x = 0.50

            # Add a small natural jitter on both axes
            jit_x = float(rng.normal(0.0, 0.018))
            jit_y = float(rng.normal(0.0, 0.16))    # bigger to fill vertical

            fx = max(0.02, min(0.98, base_x + jit_x))
            fy = max(0.05, min(0.95, 0.5 + jit_y))

            x = int(fx * (sw - 1))
            y = int(fy * (sh - 1))
            heat[y, x] += 1.0

        # Convert hits into a smooth coloured heatmap
        if heat.max() > 0:
            heat = cv2.GaussianBlur(heat, (0, 0), 22)
            heat = heat / heat.max()
            heat_u8 = np.uint8(heat * 255)
            colour = cv2.applyColorMap(heat_u8, cv2.COLORMAP_INFERNO)
            roi = canvas[sy1:sy2, sx1:sx2]
            canvas[sy1:sy2, sx1:sx2] = cv2.addWeighted(roi, 0.10, colour, 0.90, 0.0)

        # Virtual screen border
        cv2.rectangle(canvas, (sx1, sy1), (sx2, sy2), (190, 190, 190), 2)

        # Zone-divider hairlines (visually mark the thirds)
        third_l = sx1 + sw // 3
        third_r = sx1 + 2 * sw // 3
        for x in (third_l, third_r):
            for y_seg in range(sy1, sy2, 16):
                cv2.line(canvas, (x, y_seg), (x, y_seg + 8), (90, 90, 90), 1)

        # Title block
        cv2.putText(
            canvas, "Gaze Heatmap",
            (30, 50), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (240, 240, 240), 2,
        )
        cv2.putText(
            canvas,
            f"Samples: {len(self.samples)}",
            (30, 82), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (180, 180, 180), 2,
        )
        cv2.putText(
            canvas,
            "(X = gaze direction, user perspective)",
            (30, height - 30), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (140, 140, 140), 1,
        )

        # Zone labels with percentages directly under each third
        counts = self._zone_counts()
        total = max(sum(counts.values()), 1)
        zone_centers = [
            sx1 + sw // 6,
            sx1 + sw // 2,
            sx1 + 5 * sw // 6,
        ]
        zone_colors = {
            "left":   (90, 130, 255),
            "center": (110, 220, 130),
            "right":  (255, 140, 90),
        }
        label_y = sy2 + 42
        for i, zone in enumerate(["left", "center", "right"]):
            pct = (counts.get(zone, 0) / total) * 100.0
            label = f"{zone.upper()}   {pct:.1f}%"
            size = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.72, 2)[0]
            tx = zone_centers[i] - size[0] // 2
            # Coloured dot
            cv2.circle(canvas, (tx - 18, label_y - 8), 7, zone_colors[zone], -1)
            cv2.putText(
                canvas, label,
                (tx, label_y),
                cv2.FONT_HERSHEY_SIMPLEX, 0.72, (235, 235, 235), 2,
            )
            sub = f"{counts.get(zone, 0)} samples"
            sub_size = cv2.getTextSize(sub, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)[0]
            cv2.putText(
                canvas, sub,
                (zone_centers[i] - sub_size[0] // 2, label_y + 24),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (160, 160, 160), 1,
            )

        # Scan path — connect consecutive samples in time order so the viewer
        # can see how the gaze moved during the session. Drawn behind the
        # heatmap for context; kept thin and translucent so it doesn't fight
        # the density colours.
        if len(self.samples) >= 2:
            pts: list[tuple[int, int]] = []
            for i, sample in enumerate(self.samples):
                zone = sample.gaze_zone
                delta_mag = min(0.12, abs(sample.gaze_delta or 0.0))
                if zone == "left":
                    bx = 0.20 - delta_mag * 0.7
                elif zone == "right":
                    bx = 0.80 + delta_mag * 0.7
                else:
                    bx = 0.50
                by = 0.5 + 0.32 * math.sin(i * 0.42)  # gentle vertical weave
                px = int(max(0.02, min(0.98, bx)) * (sw - 1)) + sx1
                py = int(max(0.05, min(0.95, by)) * (sh - 1)) + sy1
                pts.append((px, py))
            for i in range(1, len(pts)):
                alpha = i / max(1, len(pts) - 1)   # fade darker→lighter over time
                grey = int(80 + 100 * alpha)
                cv2.line(canvas, pts[i - 1], pts[i], (grey, grey, grey), 1)
            # Start / end markers
            cv2.circle(canvas, pts[0],  6, (120, 220, 120), -1)   # green start
            cv2.circle(canvas, pts[-1], 6, (90, 100, 255), -1)    # red-ish end

        cv2.imwrite(str(path), canvas)
