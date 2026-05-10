"""
Face recognition against a folder of known-person images.
Uses DeepFace with ArcFace + cosine distance.

Naming convention for reference images (all case-insensitive):
    Soltan.png, soltan1.jpeg, soltan2.jpeg, Soltan_1.png  →  all grouped as "Soltan"
    Monem.png                                              →  "Monem"
    Taha.png                                               →  "Taha"
"""
from __future__ import annotations

import re
from pathlib import Path
from typing import Any

import cv2
import numpy as np

_ENABLED = False
_deepface: Any = None

MODEL_NAME = "ArcFace"
DETECTOR_BACKEND = "opencv"

try:
    from deepface import DeepFace as _deepface  # type: ignore
    _ENABLED = True
    print(f"[FACE] DeepFace loaded. Model={MODEL_NAME}")
except ImportError:
    print("[FACE] DeepFace not installed. Run: pip install deepface")


def _cosine(a: np.ndarray, b: np.ndarray) -> float:
    a = a / (np.linalg.norm(a) + 1e-10)
    b = b / (np.linalg.norm(b) + 1e-10)
    return float(1.0 - np.dot(a, b))


def _parse_person_name(stem: str) -> str:
    """
    Extract the base person name from a filename stem, ignoring trailing digits.
    Examples:
        'Soltan'   → 'soltan'
        'soltan1'  → 'soltan'
        'soltan2'  → 'soltan'
        'Soltan_1' → 'soltan'
        'Monem'    → 'monem'
    All returned lowercase so 'Soltan.png' and 'soltan1.jpeg' group together.
    """
    base = re.sub(r'[_\s]*\d+$', '', stem)  # strip trailing _1, 1, _12, etc.
    return base.strip('_').lower() or stem.lower()


class FaceRecognizer:
    SUPPORTED_EXTS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
    DEFAULT_THRESHOLD = 0.68  # ArcFace cosine — DeepFace's calibrated default

    def __init__(self, objects_dir: str | Path, threshold: float = DEFAULT_THRESHOLD):
        self.objects_dir = Path(objects_dir)
        self.threshold = threshold
        self.enabled = _ENABLED
        # lowercase_key → (display_name, mean_embedding)
        self.known: dict[str, tuple[str, np.ndarray]] = {}

        if self.enabled:
            self._load_known_faces()

    def _load_known_faces(self) -> None:
        if not self.objects_dir.exists():
            print(f"[FACE] Objects folder not found: {self.objects_dir}")
            return

        # key=lowercase_name, value=(display_name, [embeddings])
        grouped: dict[str, tuple[str, list[np.ndarray]]] = {}

        for img_path in sorted(self.objects_dir.iterdir()):
            if img_path.suffix.lower() not in self.SUPPORTED_EXTS:
                continue
            try:
                result = _deepface.represent(
                    img_path=str(img_path),
                    model_name=MODEL_NAME,
                    enforce_detection=False,
                    detector_backend=DETECTOR_BACKEND,
                )
                if not result:
                    print(f"[FACE] No face found in: {img_path.name}")
                    continue

                emb = np.array(result[0]["embedding"], dtype=np.float32)
                key = _parse_person_name(img_path.stem)

                if key not in grouped:
                    # Use the stem of the first file as display name (title-cased)
                    display = re.sub(r'[_\s]*\d+$', '', img_path.stem).strip('_')
                    display = display if display else img_path.stem
                    grouped[key] = (display, [])

                grouped[key][1].append(emb)
                print(f"[FACE] Loaded: {img_path.name} → '{grouped[key][0]}'")

            except Exception as exc:
                print(f"[FACE] Failed {img_path.name}: {exc}")

        for key, (display, embs) in grouped.items():
            mean_emb = np.mean(embs, axis=0).astype(np.float32)
            self.known[key] = (display, mean_emb)
            print(f"[FACE] '{display}' enrolled with {len(embs)} image(s)")

        print(f"[FACE] {len(self.known)} person(s) ready")

    def identify_faces(self, frame_rgb: np.ndarray) -> list[dict]:
        """
        Returns [{"name": str, "bbox": (x1,y1,x2,y2), "distance": float}, ...]
        """
        if not self.enabled or _deepface is None or not self.known:
            return []

        try:
            face_objs = _deepface.represent(
                img_path=frame_rgb,
                model_name=MODEL_NAME,
                enforce_detection=False,
                detector_backend=DETECTOR_BACKEND,
            )
        except Exception as exc:
            print(f"[FACE] represent error: {exc}")
            return []

        results: list[dict] = []

        for face_obj in face_objs:
            emb = np.array(face_obj.get("embedding", []), dtype=np.float32)
            if emb.size == 0:
                continue

            region = face_obj.get("facial_area", {})
            x = int(region.get("x", 0))
            y = int(region.get("y", 0))
            w = int(region.get("w", 0))
            h = int(region.get("h", 0))

            if w < 20 or h < 20:
                continue

            # Find closest known person
            best_name = "Unknown"
            best_dist = float("inf")
            for key, (display, known_emb) in self.known.items():
                dist = _cosine(emb, known_emb)
                if dist < best_dist:
                    best_dist = dist
                    if dist <= self.threshold:
                        best_name = display

            print(f"[FACE] dist={best_dist:.4f} → {best_name}")

            results.append({
                "name": best_name,
                "bbox": (x, y, x + w, y + h),
                "distance": round(best_dist, 3),
            })

        return results
