"""
Face-based login / signup pipeline.

States
------
SCANNING      – DeepFace scans the frame every N frames looking for a known face.
                If found  → emit LOGIN result and done.
                If no face match after SCAN_TIMEOUT → move to SIGNUP_PROMPT.
SIGNUP_PROMPT – Show "Welcome! Let's register you." overlay, then move to KEYBOARD.
KEYBOARD      – Virtual keyboard active; user types their name with hand gestures.
                On confirm → move to CAPTURE.
                On cancel  → move to DONE (guest).
CAPTURE       – Countdown 3-2-1, capture 3 cropped face photos at different moments,
                enroll all in FaceRecognizer, save user to users.json → DONE.
DONE          – Terminal state; result available via .result property.
"""
from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Any

import cv2
import numpy as np

from hand_keyboard import HandKeyboard
from face_recognizer import FaceRecognizer

# ── tunables ──────────────────────────────────────────────────────────────────
SCAN_TIMEOUT      = 8.0    # seconds to try face recognition before giving up
SCAN_INTERVAL_FR  = 20     # run DeepFace every N frames during scanning
PROMPT_DURATION   = 2.5    # seconds to show the "let's register" message
CAPTURE_COUNTDOWN = 4      # total seconds for capture phase
# Snap 3 images at these elapsed-second marks within the capture phase
CAPTURE_SNAP_TIMES = [1.0, 2.0, 3.0]
FACE_CROP_PAD     = 0.30   # fractional padding around detected face bbox
MIN_NAME_LEN      = 2


# ── helpers ───────────────────────────────────────────────────────────────────

def _finger_tip(hand_landmarks, lm_id: int, w: int, h: int) -> tuple[int, int] | None:
    if hand_landmarks is None:
        return None
    lm = hand_landmarks.landmark[lm_id]
    return int(lm.x * w), int(lm.y * h)


def _crop_face(frame_bgr: np.ndarray) -> np.ndarray | None:
    """
    Detect the largest face in frame_bgr using OpenCV Haar cascade and return
    a padded crop.  Returns None if no face is found (caller saves full frame).
    """
    gray = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)
    cascade_path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
    cascade = cv2.CascadeClassifier(cascade_path)
    faces = cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(40, 40))
    if len(faces) == 0:
        return None

    # pick largest face
    x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
    ih, iw = frame_bgr.shape[:2]
    pad_x = int(w * FACE_CROP_PAD)
    pad_y = int(h * FACE_CROP_PAD)
    x1 = max(0, x - pad_x)
    y1 = max(0, y - pad_y)
    x2 = min(iw, x + w + pad_x)
    y2 = min(ih, y + h + pad_y)
    return frame_bgr[y1:y2, x1:x2]


def _save_user_to_json(users_json: Path, name: str, img_rel_paths: list[str]) -> None:
    """
    If user already exists → append new image paths to their images array.
    If user is new → create a minimal record.
    """
    users: list[dict] = []
    if users_json.exists():
        try:
            with users_json.open("r", encoding="utf-8") as f:
                users = json.load(f)
        except Exception:
            pass

    for user in users:
        if str(user.get("name", "")).strip().lower() == name.strip().lower():
            imgs = user.setdefault("images", [])
            for p in img_rel_paths:
                if p not in imgs:
                    imgs.append(p)
            if img_rel_paths and not user.get("Profile"):
                user["Profile"] = img_rel_paths[0]
            users_json.parent.mkdir(parents=True, exist_ok=True)
            with users_json.open("w", encoding="utf-8") as f:
                json.dump(users, f, indent=2, ensure_ascii=False)
            return

    # new user
    users.append({
        "name": name.strip(),
        "age": "",
        "gender": "",
        "mac": [],
        "Profile": img_rel_paths[0] if img_rel_paths else "",
        "images": img_rel_paths,
        "themeMode": "light",
    })
    users_json.parent.mkdir(parents=True, exist_ok=True)
    with users_json.open("w", encoding="utf-8") as f:
        json.dump(users, f, indent=2, ensure_ascii=False)


# ── main class ────────────────────────────────────────────────────────────────

class FaceSignupFlow:
    """
    Instantiate once after BT scan returns no known user.
    Call .process(frame_rgb, annotated_frame, holistic_results, frame_w, frame_h)
    every main-loop iteration.

    When .done is True, read .result:
        {"status": "login",  "name": <str>}   – existing user recognised
        {"status": "signup", "name": <str>}   – new user registered
        {"status": "guest"}                   – user cancelled
    """

    _IDX_TIP = 8    # index finger tip
    _MID_TIP = 12   # middle finger tip

    def __init__(
        self,
        face_recognizer: FaceRecognizer,
        objects_dir: Path,
        users_json: Path,
    ) -> None:
        self._recognizer   = face_recognizer
        self._objects_dir  = Path(objects_dir)
        self._users_json   = Path(users_json)

        self._state        = "SCANNING"
        self._state_start  = time.monotonic()
        self._scan_counter = 0
        self._keyboard     = HandKeyboard()
        self._countdown_start: float | None = None
        self._pending_name: str = ""

        # multi-capture state
        self._snap_done: list[bool] = [False] * len(CAPTURE_SNAP_TIMES)
        self._captured_frames: list[np.ndarray] = []
        self._saving: bool = False   # guard: prevent _save_and_enroll being called twice

        self.done   = False
        self.result: dict = {}

    # ── public entry point ────────────────────────────────────────────────────

    def process(
        self,
        frame_rgb: np.ndarray,
        annotated: np.ndarray,
        holistic_results: Any,
        frame_w: int,
        frame_h: int,
    ) -> None:
        if self.done:
            return

        if self._state == "SCANNING":
            self._do_scanning(frame_rgb, annotated)
        elif self._state == "SIGNUP_PROMPT":
            self._do_prompt(annotated)
        elif self._state == "KEYBOARD":
            self._do_keyboard(annotated, holistic_results, frame_w, frame_h)
        elif self._state == "CAPTURE":
            self._do_capture(frame_rgb, annotated)

    # ── state handlers ────────────────────────────────────────────────────────

    def _do_scanning(self, frame_rgb: np.ndarray, annotated: np.ndarray) -> None:
        elapsed   = time.monotonic() - self._state_start
        remaining = max(0.0, SCAN_TIMEOUT - elapsed)

        cv2.putText(annotated, "Looking for your face...",
                    (10, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 220, 255), 2)
        cv2.putText(annotated, f"({remaining:.1f}s)",
                    (10, 42), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (180, 180, 180), 1)

        self._scan_counter += 1
        if self._scan_counter % SCAN_INTERVAL_FR == 0:
            faces = self._recognizer.identify_faces(frame_rgb)
            known = [f for f in faces if f["name"] != "Unknown"]
            if known:
                best = min(known, key=lambda f: f["distance"])
                self.done   = True
                self.result = {"status": "login", "name": best["name"]}
                return

        if elapsed >= SCAN_TIMEOUT:
            self._transition("SIGNUP_PROMPT")

    def _do_prompt(self, annotated: np.ndarray) -> None:
        elapsed = time.monotonic() - self._state_start

        overlay = annotated.copy()
        cv2.rectangle(overlay, (0, 0), (annotated.shape[1], annotated.shape[0]),
                      (20, 20, 20), -1)
        cv2.addWeighted(overlay, 0.55, annotated, 0.45, 0, annotated)

        _center_text(annotated, "Welcome, new visitor!", annotated.shape[0] // 2 - 20,
                     0.7, (0, 220, 255), 2)
        _center_text(annotated, "Let's get you registered.",
                     annotated.shape[0] // 2 + 10, 0.55, (200, 200, 200), 1)

        if elapsed >= PROMPT_DURATION:
            self._transition("KEYBOARD")

    def _do_keyboard(
        self,
        annotated: np.ndarray,
        holistic_results: Any,
        frame_w: int,
        frame_h: int,
    ) -> None:
        hand = (holistic_results.right_hand_landmarks
                or holistic_results.left_hand_landmarks)

        index_tip  = _finger_tip(hand, self._IDX_TIP, frame_w, frame_h)
        middle_tip = _finger_tip(hand, self._MID_TIP, frame_w, frame_h)

        # draw cursor dots with outline for visibility on any background
        if index_tip is not None:
            cv2.circle(annotated, index_tip, 5, (0, 255, 255), -1)
            cv2.circle(annotated, index_tip, 6, (0, 0, 0), 1)
        if middle_tip is not None:
            cv2.circle(annotated, middle_tip, 4, (255, 120, 0), -1)
            cv2.circle(annotated, middle_tip, 5, (0, 0, 0), 1)

        self._keyboard.update(annotated, index_tip, middle_tip,
                              left_hand_landmarks=holistic_results.left_hand_landmarks)

        cv2.putText(annotated, "Type your name  (open left hand=confirm  X=cancel)",
                    (4, 14), cv2.FONT_HERSHEY_SIMPLEX, 0.38, (0, 220, 255), 1)

        if self._keyboard.cancelled:
            self.done   = True
            self.result = {"status": "guest"}
            return

        if self._keyboard.confirmed:
            name = self._keyboard.text.strip()
            if len(name) >= MIN_NAME_LEN:
                self._pending_name = name
                self._transition("CAPTURE")
            else:
                self._keyboard.confirmed = False

    def _do_capture(self, frame_rgb: np.ndarray, annotated: np.ndarray) -> None:
        if self._countdown_start is None:
            self._countdown_start = time.monotonic()

        elapsed   = time.monotonic() - self._countdown_start
        remaining = CAPTURE_COUNTDOWN - elapsed

        # snap frames at scheduled times
        for i, snap_t in enumerate(CAPTURE_SNAP_TIMES):
            if not self._snap_done[i] and elapsed >= snap_t:
                self._captured_frames.append(frame_rgb.copy())
                self._snap_done[i] = True
                print(f"[SIGNUP] Snap {i+1}/{len(CAPTURE_SNAP_TIMES)} at {elapsed:.1f}s")

        if remaining > 0:
            overlay = annotated.copy()
            cv2.rectangle(overlay, (0, 0), (annotated.shape[1], annotated.shape[0]),
                          (0, 0, 0), -1)
            cv2.addWeighted(overlay, 0.4, annotated, 0.6, 0, annotated)

            snaps_taken = sum(self._snap_done)
            _center_text(annotated, f"Hold still... {int(remaining) + 1}",
                         annotated.shape[0] // 2 - 15, 0.9, (0, 220, 255), 2)
            _center_text(annotated, f"Registering as: {self._pending_name}",
                         annotated.shape[0] // 2 + 20, 0.5, (200, 200, 200), 1)
            _center_text(annotated, f"Photos: {snaps_taken}/{len(CAPTURE_SNAP_TIMES)}",
                         annotated.shape[0] // 2 + 40, 0.45, (0, 200, 120), 1)
        else:
            # Only call once — _saving guard prevents re-entry on subsequent frames
            if not self._saving:
                self._saving = True
                if not self._captured_frames:
                    self._captured_frames.append(frame_rgb.copy())
                self._save_and_enroll()

    def _save_and_enroll(self) -> None:
        name = self._pending_name

        # Set done + result FIRST so main.py always picks up the result,
        # even if the file I/O below partially fails.
        self.done   = True
        self.result = {"status": "signup", "name": name}
        print(f"[SIGNUP] Registration complete for '{name}' — sending login payload")

        try:
            self._objects_dir.mkdir(parents=True, exist_ok=True)

            saved_paths: list[str] = []
            saved_rel: list[str]   = []

            for idx, frame_rgb in enumerate(self._captured_frames):
                try:
                    img_bgr = cv2.cvtColor(frame_rgb, cv2.COLOR_RGB2BGR)

                    # try to crop face; fall back to full frame
                    cropped = _crop_face(img_bgr)
                    save_img = cropped if cropped is not None else img_bgr
                    if cropped is None:
                        print(f"[SIGNUP] Snap {idx+1}: no face detected, saving full frame")

                    # unique filename
                    suffix = f"_{idx+1}" if idx > 0 else ""
                    img_filename = f"{name}{suffix}.jpg"
                    img_path = self._objects_dir / img_filename
                    counter = 1
                    while img_path.exists():
                        img_filename = f"{name}{suffix}_{counter}.jpg"
                        img_path = self._objects_dir / img_filename
                        counter += 1

                    cv2.imwrite(str(img_path), save_img)
                    print(f"[SIGNUP] Saved face image: {img_path}")
                    saved_paths.append(str(img_path))
                    saved_rel.append(f"objects/{img_filename}")
                except Exception as exc:
                    print(f"[SIGNUP] Failed to save snap {idx+1}: {exc}")

            # enroll all captured images
            for img_path in saved_paths:
                try:
                    self._recognizer.enroll_image(img_path, name)
                except Exception as exc:
                    print(f"[SIGNUP] Enroll warning: {exc}")
            print(f"[SIGNUP] Enrolled '{name}' with {len(saved_paths)} image(s)")

            _save_user_to_json(self._users_json, name, saved_rel)
            print(f"[SIGNUP] Saved '{name}' to users.json")

        except Exception as exc:
            print(f"[SIGNUP] Error during save/enroll (login payload still sent): {exc}")

    # ── helpers ───────────────────────────────────────────────────────────────

    def _transition(self, new_state: str) -> None:
        self._state       = new_state
        self._state_start = time.monotonic()
        print(f"[SIGNUP] -> {new_state}")


# ── drawing util ──────────────────────────────────────────────────────────────

def _center_text(
    frame: np.ndarray,
    text: str,
    y: int,
    scale: float,
    color: tuple,
    thickness: int,
) -> None:
    (tw, _), _ = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, scale, thickness)
    x = (frame.shape[1] - tw) // 2
    cv2.putText(frame, text, (x, y),
                cv2.FONT_HERSHEY_SIMPLEX, scale, color, thickness, cv2.LINE_AA)
