"""
Face recognition against users.json.

Each user record may have:
  "images": ["objects/Soltan.png", "objects/soltan1.jpeg", ...]   ← preferred
  "Profile": "objects/Monem.png"                                  ← fallback

The user's "name" field is the identity label — not the filename stem.
Uses DeepFace ArcFace + cosine distance.
"""
from __future__ import annotations

import json
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
    """Fallback key from filename stem (used only for enroll_image)."""
    base = re.sub(r'[_\s]*\d+$', '', stem)
    return base.strip('_').lower() or stem.lower()


class FaceRecognizer:
    SUPPORTED_EXTS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
    DEFAULT_THRESHOLD = 0.68  # ArcFace cosine calibrated default

    def __init__(
        self,
        objects_dir: str | Path,
        users_json: str | Path | None = None,
        threshold: float = DEFAULT_THRESHOLD,
    ):
        self.objects_dir = Path(objects_dir)
        self.users_json  = Path(users_json) if users_json else None
        self.threshold   = threshold
        self.enabled     = _ENABLED

        # name (display) → mean embedding
        self.known: dict[str, np.ndarray] = {}

        if self.enabled:
            self._load()

    #loading

    def _load(self) -> None:
        """
        Primary: load from users.json using images/Profile fields.
        Fallback: scan objects_dir by filename stem (old behaviour).
        """
        if self.users_json and self.users_json.exists():
            self._load_from_users_json()
        else:
            print("[FACE] users.json not found, falling back to folder scan")
            self._load_from_folder()

    def _load_from_users_json(self) -> None:
        try:
            with self.users_json.open("r", encoding="utf-8") as f:
                users: list[dict] = json.load(f)
        except Exception as exc:
            print(f"[FACE] Failed to read users.json: {exc}")
            self._load_from_folder()
            return

        base_dir = self.users_json.parent  # bin/Debug/

        for user in users:
            name = str(user.get("name", "")).strip()
            if not name:
                continue

            # collect image paths: prefer "images" list, fall back to "Profile"
            raw_paths: list[str] = []
            images_field = user.get("images")
            if isinstance(images_field, list) and images_field:
                raw_paths = [str(p) for p in images_field if p]
            elif user.get("Profile"):
                raw_paths = [str(user["Profile"])]

            if not raw_paths:
                continue

            embeddings: list[np.ndarray] = []
            for rel_path in raw_paths:
                img_path = base_dir / rel_path
                if not img_path.exists():
                    # also try relative to objects_dir
                    img_path = self.objects_dir / Path(rel_path).name
                if not img_path.exists():
                    print(f"[FACE] Image not found: {rel_path}")
                    continue
                emb = self._embed(str(img_path))
                if emb is not None:
                    embeddings.append(emb)
                    print(f"[FACE] Loaded {img_path.name} → '{name}'")

            if embeddings:
                self.known[name] = np.mean(embeddings, axis=0).astype(np.float32)
                print(f"[FACE] '{name}' enrolled with {len(embeddings)} image(s)")

        print(f"[FACE] {len(self.known)} person(s) ready")

    def _load_from_folder(self) -> None:
        """Legacy: group by filename stem."""
        if not self.objects_dir.exists():
            print(f"[FACE] Objects folder not found: {self.objects_dir}")
            return

        grouped: dict[str, tuple[str, list[np.ndarray]]] = {}
        for img_path in sorted(self.objects_dir.iterdir()):
            if img_path.suffix.lower() not in self.SUPPORTED_EXTS:
                continue
            emb = self._embed(str(img_path))
            if emb is None:
                continue
            key = _parse_person_name(img_path.stem)
            if key not in grouped:
                display = re.sub(r'[_\s]*\d+$', '', img_path.stem).strip('_') or img_path.stem
                grouped[key] = (display, [])
            grouped[key][1].append(emb)
            print(f"[FACE] Loaded {img_path.name} → '{grouped[key][0]}'")

        for _key, (display, embs) in grouped.items():
            self.known[display] = np.mean(embs, axis=0).astype(np.float32)
            print(f"[FACE] '{display}' enrolled with {len(embs)} image(s)")

        print(f"[FACE] {len(self.known)} person(s) ready")

    def _embed(self, img_path: str) -> np.ndarray | None:
        """Return ArcFace embedding for the first face in an image, or None."""
        if not self.enabled or _deepface is None:
            return None
        try:
            result = _deepface.represent(
                img_path=img_path,
                model_name=MODEL_NAME,
                enforce_detection=False,
                detector_backend=DETECTOR_BACKEND,
            )
            if not result:
                print(f"[FACE] No face found in: {Path(img_path).name}")
                return None
            return np.array(result[0]["embedding"], dtype=np.float32)
        except Exception as exc:
            print(f"[FACE] Failed {Path(img_path).name}: {exc}")
            return None

    #runtime enroll

    def enroll_image(self, img_path: str, display_name: str) -> None:
        """Add or update a person from a saved image at runtime."""
        if not self.enabled or _deepface is None:
            return
        emb = self._embed(img_path)
        if emb is None:
            print(f"[FACE] enroll_image: no face found in {img_path}")
            return
        if display_name in self.known:
            emb = ((self.known[display_name] + emb) / 2.0).astype(np.float32)
        self.known[display_name] = emb
        print(f"[FACE] Enrolled '{display_name}'")

    #identification

    def identify_faces(self, frame_rgb: np.ndarray) -> list[dict]:
        """
        Detect all faces in frame_rgb and match each against known persons.
        Returns [{"name": str, "bbox": (x1,y1,x2,y2), "distance": float}, ...]
        """
        if not self.enabled or _deepface is None:
            return []

        try:
            face_objs = _deepface.represent(
                img_path=frame_rgb,
                model_name=MODEL_NAME,
                enforce_detection=True,
                detector_backend=DETECTOR_BACKEND,
            )
        except ValueError:
            return []
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

            best_name = "Unknown"
            best_dist = float("inf")
            for name, known_emb in self.known.items():
                dist = _cosine(emb, known_emb)
                if dist < best_dist:
                    best_dist = dist
                    if dist <= self.threshold:
                        best_name = name

            print(f"[FACE] dist={best_dist:.4f} → {best_name}")
            results.append({
                "name": best_name,
                "bbox": (x, y, x + w, y + h),
                "distance": round(best_dist, 3),
            })

        return results
