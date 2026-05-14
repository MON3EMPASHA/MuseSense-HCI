from __future__ import annotations

from collections import Counter, deque
from pathlib import Path
from typing import Any

import cv2


ARTIFACT_YOLO11S_PATH = (
    Path("YOLO Object Tracking") / "models" / "artifact_yolo11s_best.pt"
)

ARTIFACT_LABELS = {
    "pyramid": {
        "artifact": "Pyramids of Giza",
        "category": "Egypt",
    },
    "tutankhamun_mask": {
        "artifact": "Mask of Tutankhamun",
        "category": "Egypt",
    },
    "nefertiti_head": {
        "artifact": "Bust of Nefertiti",
        "category": "Egypt",
    },
}


class YoloTracker:
    def __init__(
        self,
        model_path: str | Path = ARTIFACT_YOLO11S_PATH,
        conf_threshold: float = 0.5,
        imgsz: int = 640,
    ):
        self.model_path = model_path
        self.conf_threshold = conf_threshold
        self.imgsz = imgsz
        self.model: Any = None
        self.enabled = False

        try:
            from ultralytics import YOLO

            self.model = YOLO(str(model_path))
            self.enabled = True
            print(f"[YOLO] Loaded model: {model_path}")
        except Exception as error:
            self.enabled = False
            self.model = None
            print(f"[YOLO] Disabled ({error})")

    def detect_primary(self, frame) -> dict | None:
        if not self.enabled or self.model is None:
            return None

        try:
            results = self.model(frame, verbose=False)
            if not results:
                return None

            boxes = results[0].boxes
            if boxes is None or len(boxes) == 0:
                return None

            best = None
            best_conf = 0.0

            for box in boxes:
                conf = float(box.conf[0])
                if conf < self.conf_threshold or conf < best_conf:
                    continue

                class_id = int(box.cls[0])
                label = str(results[0].names.get(class_id, class_id))
                x1, y1, x2, y2 = [int(v) for v in box.xyxy[0].tolist()]
                best_conf = conf
                best = {
                    "label": label,
                    "confidence": round(conf, 3),
                    "bbox": [x1, y1, x2, y2],
                    "center": [int((x1 + x2) / 2), int((y1 + y2) / 2)],
                }

            return best
        except Exception as error:
            print(f"[YOLO] Detection error: {error}")
            return None

    def detect_artifacts(self, frame) -> list[dict]:
        """Return YOLO detections for the three trained artifact classes."""
        if not self.enabled or self.model is None:
            return []

        try:
            results = self.model(
                frame,
                conf=self.conf_threshold,
                imgsz=self.imgsz,
                verbose=False,
            )
            if not results:
                return []

            boxes = results[0].boxes
            if boxes is None or len(boxes) == 0:
                return []

            detections = []
            for box in boxes:
                conf = float(box.conf[0])
                if conf < self.conf_threshold:
                    continue

                class_id = int(box.cls[0])
                label = normalize_artifact_label(results[0].names.get(class_id, class_id))
                artifact = ARTIFACT_LABELS.get(label)
                if artifact is None:
                    continue

                x1, y1, x2, y2 = [int(v) for v in box.xyxy[0].tolist()]
                detections.append(
                    {
                        "label": label,
                        "display_label": label.replace("_", " ").title(),
                        "confidence": round(conf, 3),
                        "bbox": [x1, y1, x2, y2],
                        "center": [int((x1 + x2) / 2), int((y1 + y2) / 2)],
                        "artifact": artifact["artifact"],
                        "category": artifact["category"],
                    }
                )

            detections.sort(key=lambda item: item["confidence"], reverse=True)
            return detections
        except Exception as error:
            print(f"[YOLO] detect_artifacts error: {error}")
            return []

    def detect_persons(self, frame) -> list[dict]:
        """Return all detections whose label is 'person'."""
        if not self.enabled or self.model is None:
            return []

        try:
            results = self.model(frame, verbose=False)
            if not results:
                return []

            boxes = results[0].boxes
            if boxes is None or len(boxes) == 0:
                return []

            persons = []
            for box in boxes:
                conf = float(box.conf[0])
                if conf < self.conf_threshold:
                    continue
                class_id = int(box.cls[0])
                label = str(results[0].names.get(class_id, class_id))
                if label.strip().lower() != "person":
                    continue
                x1, y1, x2, y2 = [int(v) for v in box.xyxy[0].tolist()]
                persons.append(
                    {
                        "label": label,
                        "confidence": round(conf, 3),
                        "bbox": [x1, y1, x2, y2],
                    }
                )
            return persons
        except Exception as error:
            print(f"[YOLO] detect_persons error: {error}")
            return []


class ArtifactFocusSmoother:
    """Require repeated detections before changing the focused artifact."""

    def __init__(self, window_size: int = 10, min_hits: int = 5):
        self.history: deque[str | None] = deque(maxlen=window_size)
        self.min_hits = min_hits
        self.active_label: str | None = None

    def update(self, detections: list[dict]) -> dict | None:
        best_label = detections[0]["label"] if detections else None
        self.history.append(best_label)

        counts = Counter(label for label in self.history if label)
        if not counts:
            return None

        label, hits = counts.most_common(1)[0]
        if hits < self.min_hits or label == self.active_label:
            return None

        for detection in detections:
            if detection["label"] == label:
                self.active_label = label
                return detection

        return None


def normalize_artifact_label(label: object) -> str:
    return str(label).strip().lower().replace(" ", "_").replace("-", "_")


def draw_artifact_detections(frame, detections: list[dict]) -> None:
    for detection in detections:
        x1, y1, x2, y2 = detection["bbox"]
        label = f"{detection['display_label']} {detection['confidence']:.2f}"
        color = (0, 215, 255)
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)
        cv2.putText(
            frame,
            label,
            (x1, max(y1 - 8, 18)),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            color,
            2,
        )
