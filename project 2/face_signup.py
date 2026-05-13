"""
Face-based login / signup pipeline.

States
------
SCANNING   – DeepFace scans the frame every N frames looking for a known face.
             If found  → emit LOGIN result and done.
             If no BT and no face match after SCAN_TIMEOUT → move to SIGNUP_PROMPT.
SIGNUP_PROMPT – Show "Welcome! Let's register you." overlay for a moment, then
                send C# a signup_start message, then move to KEYBOARD.
KEYBOARD   – Virtual keyboard is active; user types their name with hand gestures.
             On confirm → move to CAPTURE.
             On cancel  → move to DONE (guest).
CAPTURE    – Countdown 3-2-1, capture face photo, save to objects_dir, enroll in
             FaceRecognizer, save user to users.json, send C# user_login, → DONE.
DONE       – Terminal state; result available via .result property.
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
CAPTURE_COUNTDOWN = 3      # seconds countdown before photo snap
MIN_NAME_LEN      = 2


# ── helpers ───────────────────────────────────────────────────────────────────

def _finger_tip(hand_landmarks, lm_id: int, w: int, h: int) -> tuple[int, int] | None:
    if hand_landmarks is None:
        return None
    lm = hand_landmarks.landmark[lm_id]
    return int(lm.x * w), int(lm.y * h)


def _save_user_to_json(users_json: Path, name: str, img_rel_path: str) -> None:
    """
    If user already exists → append img_rel_path to their images array.
    If user is new → create a minimal record with the image.
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
            if img_rel_path not in imgs:
                imgs.append(img_rel_path)
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
        "Profile": img_rel_path,
        "images": [img_rel_path],
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

    # mediapipe landmark indices
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
        self._captured_frame: np.ndarray | None = None

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
        """Mutates `annotated` with overlays. Sets self.done / self.result when finished."""
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
        elapsed = time.monotonic() - self._state_start
        remaining = max(0.0, SCAN_TIMEOUT - elapsed)

        # overlay
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
        import mediapipe as mp
        mp_hands = mp.solutions.hands

        # prefer right hand, fall back to left
        hand = (holistic_results.right_hand_landmarks
                or holistic_results.left_hand_landmarks)

        index_tip  = _finger_tip(hand, self._IDX_TIP, frame_w, frame_h)
        middle_tip = _finger_tip(hand, self._MID_TIP, frame_w, frame_h)

        # draw cursor dot
        if index_tip is not None:
            cv2.circle(annotated, index_tip, 6, (0, 255, 255), -1)
        if middle_tip is not None:
            cv2.circle(annotated, middle_tip, 5, (255, 100, 0), -1)

        self._keyboard.update(annotated, index_tip, middle_tip)

        # header
        cv2.putText(annotated, "Type your name:",
                    (4, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 220, 255), 1)

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
                # reset confirm flag, keep typing
                self._keyboard.confirmed = False

    def _do_capture(self, frame_rgb: np.ndarray, annotated: np.ndarray) -> None:
        if self._countdown_start is None:
            self._countdown_start = time.monotonic()

        elapsed   = time.monotonic() - self._countdown_start
        remaining = CAPTURE_COUNTDOWN - elapsed

        if remaining > 0:
            # countdown overlay
            overlay = annotated.copy()
            cv2.rectangle(overlay, (0, 0), (annotated.shape[1], annotated.shape[0]),
                          (0, 0, 0), -1)
            cv2.addWeighted(overlay, 0.4, annotated, 0.6, 0, annotated)
            _center_text(annotated, f"Hold still... {int(remaining) + 1}",
                         annotated.shape[0] // 2, 1.0, (0, 220, 255), 2)
            _center_text(annotated, f"Registering as: {self._pending_name}",
                         annotated.shape[0] // 2 + 40, 0.55, (200, 200, 200), 1)
        else:
            # snap
            self._captured_frame = frame_rgb.copy()
            self._save_and_enroll()

    def _save_and_enroll(self) -> None:
        name = self._pending_name
        img_bgr = cv2.cvtColor(self._captured_frame, cv2.COLOR_RGB2BGR)

        # save image to objects_dir
        self._objects_dir.mkdir(parents=True, exist_ok=True)
        img_filename = f"{name}.jpg"
        img_path = self._objects_dir / img_filename
        idx = 1
        while img_path.exists():
            img_filename = f"{name}{idx}.jpg"
            img_path = self._objects_dir / img_filename
            idx += 1
        cv2.imwrite(str(img_path), img_bgr)
        print(f"[SIGNUP] Saved face image: {img_path}")

        # enroll in live recognizer using the full display name
        try:
            self._recognizer.enroll_image(str(img_path), name)
            print(f"[SIGNUP] Enrolled '{name}' in face recognizer")
        except Exception as exc:
            print(f"[SIGNUP] Enroll warning: {exc}")

        # relative path stored in users.json (relative to bin/Debug/)
        img_rel = f"objects/{img_filename}"
        _save_user_to_json(self._users_json, name, img_rel)
        print(f"[SIGNUP] Saved '{name}' to users.json")

        self.done   = True
        self.result = {"status": "signup", "name": name}

    # ── helpers ───────────────────────────────────────────────────────────────

    def _transition(self, new_state: str) -> None:
        self._state       = new_state
        self._state_start = time.monotonic()
        print(f"[SIGNUP] → {new_state}")


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
