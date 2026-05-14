"""
Virtual on-screen keyboard driven by hand landmarks.

Navigation  : index finger tip (1 finger up)
Click       : index + middle finger tips close together (pinch / 2-finger point)
Confirm     : show full open LEFT hand for 1 second → triggers confirm
"""
from __future__ import annotations

import time
import cv2
import numpy as np

# ── Layout ────────────────────────────────────────────────────────────────────
ROWS: list[list[str]] = [
    ["1","2","3","4","5","6","7","8","9","0"],
    ["Q","W","E","R","T","Y","U","I","O","P"],
    ["A","S","D","F","G","H","J","K","L","DEL"],
    ["Z","X","C","V","B","N","M","SPC","OK","X"],
]

# Special key labels that map to actions
_ACTION_MAP = {"DEL": "⌫", "OK": "✓", "X": "✗", "SPC": " "}

KEY_W = 44
KEY_H = 38
KEY_PAD = 3

_KB_COLS = 10
_KB_ROWS = 4
_KB_TOTAL_W = _KB_COLS * (KEY_W + KEY_PAD) - KEY_PAD   # 467 px
_KB_TOTAL_H = _KB_ROWS * (KEY_H + KEY_PAD) - KEY_PAD   # 155 px
KEYBOARD_LEFT = (480 - _KB_TOTAL_W) // 2
KEYBOARD_TOP  = (320 - _KB_TOTAL_H) // 2 + 20

# colours
COL_BG      = ( 50,  50,  60)
COL_BORDER  = (120, 120, 140)
COL_HOVER   = ( 60, 140, 255)
COL_CLICK   = (  0, 210,  80)
COL_TEXT    = (255, 255, 255)
COL_SPECIAL = (180,  50,  50)
COL_CONFIRM = ( 30, 160,  80)
COL_NUM     = ( 60,  60,  80)

CLICK_COOLDOWN   = 0.5   # seconds between key clicks
PINCH_DIST_PX    = 18    # index-middle pinch threshold
OPEN_HAND_HOLD   = 1.0   # seconds to hold open hand before confirm fires

# MediaPipe landmark indices used for open-hand detection
# tip ids:  thumb=4, index=8, middle=12, ring=16, pinky=20
# pip ids:  thumb=3, index=6, middle=10, ring=14, pinky=18  (one joint below tip)
_FINGER_TIPS = [8, 12, 16, 20]   # index, middle, ring, pinky
_FINGER_PIPS = [6, 10, 14, 18]
_THUMB_TIP   = 4
_THUMB_IP    = 3   # thumb IP joint (used instead of PIP for thumb)


def _is_open_hand(hand_landmarks) -> bool:
    """
    Return True when all 5 fingers are extended (open palm).
    Uses the rule: tip.y < pip.y for index–pinky (tip is higher = smaller y),
    and thumb tip.x further from palm than thumb IP (works for both hands).
    """
    if hand_landmarks is None:
        return False
    lm = hand_landmarks.landmark

    # index, middle, ring, pinky — tip must be above (smaller y) than PIP
    fingers_open = all(lm[tip].y < lm[pip].y
                       for tip, pip in zip(_FINGER_TIPS, _FINGER_PIPS))

    # thumb — tip.x should differ from IP.x by more than a small threshold
    # (works regardless of hand orientation)
    thumb_open = abs(lm[_THUMB_TIP].x - lm[_THUMB_IP].x) > 0.04

    return fingers_open and thumb_open


def _key_rect(row: int, col: int) -> tuple[int, int, int, int]:
    x1 = KEYBOARD_LEFT + col * (KEY_W + KEY_PAD)
    y1 = KEYBOARD_TOP  + row * (KEY_H + KEY_PAD)
    return x1, y1, x1 + KEY_W, y1 + KEY_H


def _draw_key(frame: np.ndarray, x1: int, y1: int, x2: int, y2: int,
              bg: tuple, border: tuple, label: str, font_scale: float) -> None:
    cv2.rectangle(frame, (x1 + 1, y1 + 1), (x2 - 1, y2 - 1), bg, -1)
    cv2.rectangle(frame, (x1, y1), (x2, y2), border, 1)
    cv2.line(frame, (x1 + 1, y2), (x2 - 1, y2),
             (max(bg[0]-30,0), max(bg[1]-30,0), max(bg[2]-30,0)), 1)
    w = x2 - x1
    h = y2 - y1
    (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, font_scale, 1)
    tx = x1 + (w - tw) // 2
    ty = y1 + (h + th) // 2 - 1
    cv2.putText(frame, label, (tx, ty),
                cv2.FONT_HERSHEY_SIMPLEX, font_scale, COL_TEXT, 1, cv2.LINE_AA)


class HandKeyboard:
    def __init__(self) -> None:
        self.text: str = ""
        self._last_click_time: float = 0.0
        self._hovered: tuple[int, int] | None = None
        self._clicked_key: str | None = None
        self._click_flash_until: float = 0.0
        self.confirmed: bool = False
        self.cancelled: bool = False

        # open-hand confirm state
        self._open_hand_since: float | None = None   # when the open hand started
        self._confirm_flash_until: float = 0.0       # brief green flash after confirm

    # ── Public API ────────────────────────────────────────────────────────────

    def update(
        self,
        frame: np.ndarray,
        index_tip: tuple[int, int] | None,
        middle_tip: tuple[int, int] | None,
        left_hand_landmarks=None,   # full MediaPipe hand landmarks for open-hand detect
    ) -> None:
        """
        Call every frame.
        index_tip / middle_tip : pixel coords on the same resolution as frame, or None.
        left_hand_landmarks    : holistic_results.left_hand_landmarks (or None).
        """
        self._hovered = None
        clicked = False

        if index_tip is not None:
            ix, iy = index_tip
            for r, row in enumerate(ROWS):
                for c, _ in enumerate(row):
                    x1, y1, x2, y2 = _key_rect(r, c)
                    if x1 <= ix <= x2 and y1 <= iy <= y2:
                        self._hovered = (r, c)

            if middle_tip is not None:
                mx, my = middle_tip
                dist = ((ix - mx) ** 2 + (iy - my) ** 2) ** 0.5
                if dist < PINCH_DIST_PX:
                    now = time.monotonic()
                    if now - self._last_click_time > CLICK_COOLDOWN:
                        clicked = True
                        self._last_click_time = now

        if clicked and self._hovered is not None:
            r, c = self._hovered
            key = ROWS[r][c]
            self._clicked_key = key
            self._click_flash_until = time.monotonic() + 0.3
            self._handle_key(key)

        # ── open-hand confirm detection ───────────────────────────────────
        if not self.confirmed:
            if _is_open_hand(left_hand_landmarks):
                if self._open_hand_since is None:
                    self._open_hand_since = time.monotonic()
                elif time.monotonic() - self._open_hand_since >= OPEN_HAND_HOLD:
                    self.confirmed = True
                    self._confirm_flash_until = time.monotonic() + 0.6
                    self._open_hand_since = None
            else:
                self._open_hand_since = None   # reset if hand closes

        self._draw(frame)

    def _handle_key(self, key: str) -> None:
        action = _ACTION_MAP.get(key, key)
        if action == "⌫":
            self.text = self.text[:-1]
        elif action == "✓":
            self.confirmed = True
        elif action == "✗":
            self.cancelled = True
        elif action == " ":
            if len(self.text) < 32:
                self.text += " "
        else:
            if len(self.text) < 32:
                self.text += key

    def _draw(self, frame: np.ndarray) -> None:
        now = time.monotonic()
        overlay = frame.copy()

        # background panel
        kx1 = KEYBOARD_LEFT - 4
        ky1 = KEYBOARD_TOP - 32
        kx2 = KEYBOARD_LEFT + _KB_TOTAL_W + 4
        ky2 = KEYBOARD_TOP + _KB_TOTAL_H + 4
        cv2.rectangle(overlay, (kx1, ky1), (kx2, ky2), (20, 20, 25), -1)
        cv2.rectangle(overlay, (kx1, ky1), (kx2, ky2), (80, 80, 100), 1)

        for r, row in enumerate(ROWS):
            for c, key in enumerate(row):
                x1, y1, x2, y2 = _key_rect(r, c)
                is_hover = self._hovered == (r, c)
                is_flash = (self._clicked_key == key and now < self._click_flash_until)

                if is_flash:
                    bg, border = COL_CLICK, (0, 255, 100)
                elif is_hover:
                    bg, border = COL_HOVER, (150, 200, 255)
                elif key in ("DEL", "X"):
                    bg, border = COL_SPECIAL, (220, 80, 80)
                elif key == "OK":
                    bg, border = COL_CONFIRM, (60, 200, 100)
                elif r == 0:
                    bg, border = COL_NUM, COL_BORDER
                else:
                    bg, border = COL_BG, COL_BORDER

                fs = 0.32 if key in ("DEL", "SPC") else 0.38
                _draw_key(overlay, x1, y1, x2, y2, bg, border, key, fs)

        # text preview bar
        bar_x1 = KEYBOARD_LEFT
        bar_x2 = KEYBOARD_LEFT + _KB_TOTAL_W
        bar_y1 = KEYBOARD_TOP - 28
        bar_y2 = KEYBOARD_TOP - 6
        cv2.rectangle(overlay, (bar_x1, bar_y1), (bar_x2, bar_y2), (15, 15, 20), -1)
        cv2.rectangle(overlay, (bar_x1, bar_y1), (bar_x2, bar_y2), (80, 80, 100), 1)
        display_text = self.text[-36:] if len(self.text) > 36 else self.text
        cv2.putText(overlay, display_text + "|",
                    (bar_x1 + 4, bar_y2 - 4),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.38, (0, 255, 180), 1, cv2.LINE_AA)

        # 50% transparent blend
        cv2.addWeighted(overlay, 0.5, frame, 0.5, 0, frame)

        # ── open-hand progress indicator (drawn on top, fully opaque) ────
        if now < self._confirm_flash_until:
            # brief green "Confirmed!" flash
            cv2.putText(frame, "Confirmed!",
                        (KEYBOARD_LEFT, ky1 - 6),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 100), 2, cv2.LINE_AA)
        elif self._open_hand_since is not None:
            elapsed  = now - self._open_hand_since
            progress = min(elapsed / OPEN_HAND_HOLD, 1.0)
            # progress bar above the keyboard panel
            bar_w    = int(_KB_TOTAL_W * progress)
            bar_top  = ky1 - 10
            bar_bot  = ky1 - 4
            cv2.rectangle(frame, (KEYBOARD_LEFT, bar_top),
                          (KEYBOARD_LEFT + _KB_TOTAL_W, bar_bot), (40, 40, 40), -1)
            cv2.rectangle(frame, (KEYBOARD_LEFT, bar_top),
                          (KEYBOARD_LEFT + bar_w, bar_bot), (0, 220, 80), -1)
            cv2.putText(frame, "Open hand to confirm...",
                        (KEYBOARD_LEFT, bar_top - 3),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.35, (0, 220, 80), 1, cv2.LINE_AA)
