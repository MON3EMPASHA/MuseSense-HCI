"""
Face-based login / signup pipeline.

States
------
SCANNING        – DeepFace scans every N frames for a known face.
SIGNUP_PROMPT   – Brief welcome overlay, then → KEYBOARD.
KEYBOARD        – Full QWERTY; user types name.        → KEYBOARD_AGE
KEYBOARD_AGE    – Numeric pad; user types age.         → KEYBOARD_GENDER
KEYBOARD_GENDER – Two large buttons: Male / Female.    → CAPTURE
CAPTURE         – Countdown, 3 face snaps, enroll.     → DONE
DONE            – Terminal; read .result.
"""
from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Any

import cv2
import numpy as np

from hand_keyboard import HandKeyboard, _is_open_hand, OPEN_HAND_HOLD
from face_recognizer import FaceRecognizer

# ── tunables ──────────────────────────────────────────────────────────────────
SCAN_TIMEOUT       = 8.0
SCAN_INTERVAL_FR   = 20
PROMPT_DURATION    = 2.5
CAPTURE_COUNTDOWN  = 4
CAPTURE_SNAP_TIMES = [1.0, 2.0, 3.0]
FACE_CROP_PAD      = 0.30
MIN_NAME_LEN       = 2

# ── step indicator config ─────────────────────────────────────────────────────
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


def _save_user_to_json(
    users_json: Path,
    name: str,
    img_rel_paths: list[str],
    age: str = "",
    gender: str = "",
) -> None:
    """
    If user already exists → append new image paths and update age/gender.
    If user is new → create a full record including age and gender.
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
            if age:
                user["age"] = age
            if gender:
                user["gender"] = gender
            users_json.parent.mkdir(parents=True, exist_ok=True)
            with users_json.open("w", encoding="utf-8") as f:
                json.dump(users, f, indent=2, ensure_ascii=False)
            return

    # new user
    users.append({
        "name": name.strip(),
        "age": age,
        "gender": gender,
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
        self._keyboard     = HandKeyboard(frame_w=480, frame_h=320)
        self._countdown_start: float | None = None
        self._pending_name: str = ""
        self._pending_age: str = ""
        self._pending_gender: str = ""

        # gender selector state
        _GENDER_OPTIONS = ["Male", "Female"]
        self._gender_options = _GENDER_OPTIONS
        self._gender_hovered: int | None = None
        self._gender_selected: int | None = None
        self._gender_confirmed: bool = False
        self._gender_cancelled: bool = False
        self._gender_last_click: float = 0.0

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
        elif self._state == "KEYBOARD_AGE":
            self._do_keyboard_age(annotated, holistic_results, frame_w, frame_h)
        elif self._state == "KEYBOARD_GENDER":
            self._do_keyboard_gender(annotated, holistic_results, frame_w, frame_h)
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
                self._keyboard = HandKeyboard(mode="num", frame_w=480, frame_h=320)   # fresh keyboard for age
                self._transition("KEYBOARD_AGE")
            else:
                self._keyboard.confirmed = False

    def _do_keyboard_age(
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

        if index_tip is not None:
            cv2.circle(annotated, index_tip, 5, (0, 255, 255), -1)
            cv2.circle(annotated, index_tip, 6, (0, 0, 0), 1)
        if middle_tip is not None:
            cv2.circle(annotated, middle_tip, 4, (255, 120, 0), -1)
            cv2.circle(annotated, middle_tip, 5, (0, 0, 0), 1)

        self._keyboard.update(annotated, index_tip, middle_tip,
                              left_hand_landmarks=holistic_results.left_hand_landmarks)

        cv2.putText(annotated, "Type your age  (digits only, open left hand=confirm  X=cancel)",
                    (4, 14), cv2.FONT_HERSHEY_SIMPLEX, 0.35, (0, 220, 255), 1)

        if self._keyboard.cancelled:
            self.done   = True
            self.result = {"status": "guest"}
            return

        if self._keyboard.confirmed:
            age_text = self._keyboard.text.strip()
            # accept any non-empty input (digits preferred but not enforced)
            if len(age_text) >= 1:
                self._pending_age = age_text
                self._transition("KEYBOARD_GENDER")
            else:
                self._keyboard.confirmed = False

    def _do_keyboard_gender(
        self,
        annotated: np.ndarray,
        holistic_results: Any,
        frame_w: int,
        frame_h: int,
    ) -> None:
        """Gender selector: Male / Female buttons + a Confirm button, all pinch-driven."""
        hand = (holistic_results.right_hand_landmarks
                or holistic_results.left_hand_landmarks)

        index_tip  = _finger_tip(hand, self._IDX_TIP, frame_w, frame_h)
        middle_tip = _finger_tip(hand, self._MID_TIP, frame_w, frame_h)

        h, w = annotated.shape[:2]

        # ── layout ────────────────────────────────────────────────────────
        btn_w, btn_h = 100, 44
        gap          = 20
        gender_opts  = self._gender_options   # ["Male", "Female"]
        total_w      = len(gender_opts) * btn_w + (len(gender_opts) - 1) * gap
        start_x      = (w - total_w) // 2
        btn_y        = h // 2 - btn_h // 2

        gender_rects = []
        for i in range(len(gender_opts)):
            x1 = start_x + i * (btn_w + gap)
            gender_rects.append((x1, btn_y, x1 + btn_w, btn_y + btn_h))

        # Confirm button — centred below gender buttons
        conf_w, conf_h = 90, 36
        conf_x1 = (w - conf_w) // 2
        conf_y1 = btn_y + btn_h + 18
        conf_x2 = conf_x1 + conf_w
        conf_y2 = conf_y1 + conf_h

        # Cancel button — to the right of confirm
        canc_w, canc_h = 70, 36
        canc_x1 = conf_x2 + 14
        canc_y1 = conf_y1
        canc_x2 = canc_x1 + canc_w
        canc_y2 = canc_y1 + canc_h

        all_rects  = gender_rects + [(conf_x1, conf_y1, conf_x2, conf_y2),
                                     (canc_x1, canc_y1, canc_x2, canc_y2)]
        CONFIRM_IDX = len(gender_opts)
        CANCEL_IDX  = len(gender_opts) + 1

        # ── hover detection ───────────────────────────────────────────────
        self._gender_hovered = None
        if index_tip is not None:
            ix, iy = index_tip
            for i, (x1, y1, x2, y2) in enumerate(all_rects):
                if x1 <= ix <= x2 and y1 <= iy <= y2:
                    self._gender_hovered = i

        # ── pinch click ───────────────────────────────────────────────────
        clicked_idx = None
        if (index_tip is not None and middle_tip is not None
                and self._gender_hovered is not None):
            ix, iy = index_tip
            mx, my = middle_tip
            dist = ((ix - mx) ** 2 + (iy - my) ** 2) ** 0.5
            now  = time.monotonic()
            if dist < 18 and now - self._gender_last_click > 0.5:
                clicked_idx = self._gender_hovered
                self._gender_last_click = now

        if clicked_idx is not None:
            if clicked_idx < len(gender_opts):
                self._gender_selected = clicked_idx
            elif clicked_idx == CONFIRM_IDX:
                self._gender_confirmed = True
            elif clicked_idx == CANCEL_IDX:
                self._gender_cancelled = True

        # ── draw ──────────────────────────────────────────────────────────
        _center_text(annotated, "Select your gender",
                     btn_y - 18, 0.6, (0, 220, 255), 2)

        # gender buttons
        for i, (label, (x1, y1, x2, y2)) in enumerate(
                zip(gender_opts, gender_rects)):
            is_hover    = self._gender_hovered == i
            is_selected = self._gender_selected == i

            if is_selected:
                bg, border = (0, 150, 70), (0, 240, 110)
            elif is_hover:
                bg, border = (60, 130, 230), (130, 190, 255)
            else:
                bg, border = (40, 40, 55), (100, 100, 130)

            cv2.rectangle(annotated, (x1, y1), (x2, y2), bg, -1)
            cv2.rectangle(annotated, (x1, y1), (x2, y2), border, 2)
            (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.55, 1)
            cv2.putText(annotated, label,
                        (x1 + (btn_w - tw) // 2, y1 + (btn_h + th) // 2 - 2),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1, cv2.LINE_AA)

        # confirm button
        conf_hover = self._gender_hovered == CONFIRM_IDX
        conf_bg    = (0, 180, 80) if conf_hover else (0, 130, 55)
        conf_brd   = (0, 255, 120) if conf_hover else (0, 200, 90)
        cv2.rectangle(annotated, (conf_x1, conf_y1), (conf_x2, conf_y2), conf_bg, -1)
        cv2.rectangle(annotated, (conf_x1, conf_y1), (conf_x2, conf_y2), conf_brd, 2)
        (tw, th), _ = cv2.getTextSize("Confirm", cv2.FONT_HERSHEY_SIMPLEX, 0.48, 1)
        cv2.putText(annotated, "Confirm",
                    (conf_x1 + (conf_w - tw) // 2, conf_y1 + (conf_h + th) // 2 - 2),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.48, (255, 255, 255), 1, cv2.LINE_AA)

        # cancel button
        canc_hover = self._gender_hovered == CANCEL_IDX
        canc_bg    = (140, 35, 35) if canc_hover else (100, 25, 25)
        canc_brd   = (220, 70, 70) if canc_hover else (170, 50, 50)
        cv2.rectangle(annotated, (canc_x1, canc_y1), (canc_x2, canc_y2), canc_bg, -1)
        cv2.rectangle(annotated, (canc_x1, canc_y1), (canc_x2, canc_y2), canc_brd, 2)
        (tw, th), _ = cv2.getTextSize("Cancel", cv2.FONT_HERSHEY_SIMPLEX, 0.42, 1)
        cv2.putText(annotated, "Cancel",
                    (canc_x1 + (canc_w - tw) // 2, canc_y1 + (canc_h + th) // 2 - 2),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.42, (255, 255, 255), 1, cv2.LINE_AA)

        # hint
        hint = (f"Selected: {gender_opts[self._gender_selected]}  — pinch Confirm"
                if self._gender_selected is not None
                else "Pinch a gender to select, then pinch Confirm")
        _center_text(annotated, hint, canc_y2 + 16, 0.35, (160, 160, 180), 1)

        # cursor dots
        if index_tip is not None:
            cv2.circle(annotated, index_tip, 5, (0, 255, 255), -1)
            cv2.circle(annotated, index_tip, 6, (0, 0, 0), 1)
        if middle_tip is not None:
            cv2.circle(annotated, middle_tip, 4, (255, 120, 0), -1)
            cv2.circle(annotated, middle_tip, 5, (0, 0, 0), 1)

        # ── transitions ───────────────────────────────────────────────────
        if self._gender_cancelled:
            self.done   = True
            self.result = {"status": "guest"}
            return

        if self._gender_confirmed:
            gender = (gender_opts[self._gender_selected]
                      if self._gender_selected is not None else "")
            self._pending_gender = gender
            self._transition("CAPTURE")

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

            _save_user_to_json(
                self._users_json, name, saved_rel,
                age=self._pending_age,
                gender=self._pending_gender,
            )
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
