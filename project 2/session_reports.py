from __future__ import annotations

import json
import re
import time
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from context_store import ContextStore
from gaze_report import GazeSessionLogger


def _safe_filename(value: str) -> str:
    value = value.strip() or "guest"
    value = re.sub(r"[^\w\- ]+", "", value, flags=re.UNICODE)
    value = re.sub(r"\s+", "_", value).strip("_")
    return value[:80] or "guest"


def _build_session_dir(reports_root: Path, user_name: str, started_at: float) -> Path:
    reports_root.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%d_%H%M%S", time.localtime(started_at))
    slug = _safe_filename(user_name)
    session_dir = reports_root / f"{slug}_{stamp}"
    session_dir.mkdir(parents=True, exist_ok=True)
    return session_dir


def _render_artifact_png(
    path: Path, title: str, items: list[dict], opened_count: int, total_count: int
) -> None:
    width = 1280
    header_h = 130
    chart_y = 160
    row_h = 32
    chart_h = max(row_h * max(len(items), 1) + 20, 260)
    height = chart_y + chart_h + 40
    img = np.full((height, width, 3), 250, dtype=np.uint8)

    text_primary = (30, 30, 30)
    text_muted = (95, 95, 95)
    border = (220, 220, 220)
    bar_color = (80, 140, 210)
    opened_color = (80, 170, 90)
    unopened_color = (60, 60, 200)

    cv2.rectangle(img, (24, 24), (width - 24, header_h), (255, 255, 255), -1)
    cv2.rectangle(img, (24, 24), (width - 24, header_h), border, 2)
    cv2.putText(img, title, (44, 70), cv2.FONT_HERSHEY_SIMPLEX, 0.95, text_primary, 2)
    cv2.putText(
        img,
        f"Opened: {opened_count} / {total_count}",
        (44, 108),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.72,
        text_muted,
        2,
    )

    chart_x = 40
    chart_w = width - 80
    cv2.rectangle(
        img,
        (chart_x, chart_y),
        (chart_x + chart_w, chart_y + chart_h),
        (255, 255, 255),
        -1,
    )
    cv2.rectangle(
        img,
        (chart_x, chart_y),
        (chart_x + chart_w, chart_y + chart_h),
        border,
        2,
    )

    if not items:
        cv2.putText(
            img,
            "No artifact scores available.",
            (chart_x + 24, chart_y + 60),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.8,
            text_muted,
            2,
        )
        cv2.imwrite(str(path), img)
        return

    max_abs = max(abs(item["score"]) for item in items) or 1.0
    for idx, item in enumerate(items):
        name = item["name"]
        score = float(item["score"])
        opened = bool(item.get("opened"))
        y0 = chart_y + idx * row_h
        y_mid = y0 + row_h // 2
        bar_len = int((abs(score) / max_abs) * (chart_w * 0.55))
        bar_x0 = chart_x + 360
        bar_x1 = bar_x0 + bar_len
        cv2.putText(
            img,
            name[:28],
            (chart_x + 16, y_mid + 6),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.62,
            text_primary,
            2,
        )
        cv2.rectangle(img, (bar_x0, y_mid - 10), (bar_x1, y_mid + 10), bar_color, -1)
        score_text = f"{score:.2f}"
        cv2.putText(
            img,
            score_text,
            (bar_x1 + 12, y_mid + 6),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            text_muted,
            2,
        )
        (text_w, text_h), _ = cv2.getTextSize(
            score_text, cv2.FONT_HERSHEY_SIMPLEX, 0.55, 2
        )
        circle_x = bar_x1 + 12 + text_w + 16
        circle_y = y_mid
        circle_color = opened_color if opened else unopened_color
        cv2.circle(img, (circle_x, circle_y), 6, circle_color, -1)

    cv2.imwrite(str(path), img)


def _build_artifact_report(
    out_dir: Path,
    user_name: str,
    store: ContextStore,
    artifacts: list[dict],
    session_started_at: float,
) -> dict:
    user_data = store.data.get("users", {}).get(user_name, {})
    artifact_scores = user_data.get("artifact_scores", {})
    opened_artifacts = user_data.get("opened_artifacts", {})

    artifacts_by_name: dict[str, dict] = {}
    for artifact in artifacts:
        name = str(artifact.get("name", "")).strip()
        if name:
            artifacts_by_name[name] = artifact

    all_names = list(artifacts_by_name.keys())
    opened_names: set[str] = set()
    if isinstance(opened_artifacts, dict):
        for name, opened_at in opened_artifacts.items():
            if not name:
                continue
            try:
                opened_time = float(opened_at)
            except (TypeError, ValueError):
                continue
            if opened_time >= session_started_at:
                opened_names.add(str(name).strip().lower())

    def to_item(name: str) -> dict:
        meta = artifacts_by_name.get(name, {})
        return {
            "name": name,
            "score": float(artifact_scores.get(name, 0.0)),
            "opened": name.strip().lower() in opened_names,
            "country": meta.get("country") or meta.get("origin") or "",
            "era": meta.get("era", ""),
        }

    ranked = [to_item(name) for name in all_names]
    ranked.sort(key=lambda item: item["score"], reverse=True)

    opened = [item for item in ranked if item["opened"]]
    unopened = [item for item in ranked if not item["opened"]]

    png_path = out_dir / "artifact_report.png"
    json_path = out_dir / "artifact_report.json"

    payload = {
        "type": "artifact_session_report",
        "user": user_name,
        "total_artifacts": len(all_names),
        "opened_count": len(opened),
        "opened_artifacts": opened,
        "unopened_artifacts": unopened,
        "ranked_artifacts": ranked,
        "generated_at": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime()),
    }

    json_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    _render_artifact_png(
        png_path, "Artifact Scores", ranked, len(opened), len(all_names)
    )

    return {
        "png": png_path.name,
        "json": json_path.name,
        "summary": payload,
    }


def _build_combined_pdf(out_dir: Path) -> str | None:
    image_paths = sorted(out_dir.glob("*.png"))
    if not image_paths:
        return None

    pages: list[Image.Image] = []
    for path in image_paths:
        try:
            with Image.open(path) as img:
                pages.append(img.convert("RGB"))
        except Exception:
            continue

    if not pages:
        return None

    pdf_path = out_dir / "session_reports.pdf"
    first, rest = pages[0], pages[1:]
    first.save(pdf_path, "PDF", save_all=True, append_images=rest)
    return pdf_path.name


def save_session_reports(
    reports_root: Path,
    user_name: str,
    gaze_session: GazeSessionLogger,
    context_store: ContextStore,
    tuio_artifacts: dict[int, dict],
) -> dict:
    session_dir = _build_session_dir(
        reports_root, user_name, gaze_session.session_started_at
    )

    gaze_result = gaze_session.save_report(session_dir)
    emotion_result = gaze_session.save_emotion_report(session_dir)
    artifact_result = _build_artifact_report(
        session_dir,
        user_name,
        context_store,
        list(tuio_artifacts.values()),
        gaze_session.session_started_at,
    )

    combined_pdf = _build_combined_pdf(session_dir)

    summary_path = session_dir / "session_summary.json"
    summary = {
        "type": "session_summary",
        "user": user_name,
        "started_at": time.strftime(
            "%Y-%m-%d %H:%M:%S", time.localtime(gaze_session.session_started_at)
        ),
        "ended_at": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime()),
        "reports": {
            "gaze": gaze_result,
            "emotion": emotion_result,
            "artifacts": artifact_result,
            "combined_pdf": combined_pdf,
        },
    }
    summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    return {
        "session_dir": str(session_dir),
        "summary": summary,
    }
