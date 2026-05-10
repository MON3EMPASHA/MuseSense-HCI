from __future__ import annotations

from typing import Any


class YoloTracker:
    def __init__(self, model_path: str = "yolov8s.pt", conf_threshold: float = 0.3):
        self.model_path = model_path
        self.conf_threshold = conf_threshold
        self.model: Any = None
        self.enabled = False

        try:
            from ultralytics import YOLO

            self.model = YOLO(model_path)
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
