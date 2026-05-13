"""
Virtual on-screen keyboard driven by hand landmarks.

Navigation : index finger tip (1 finger up)
Click       : index + middle finger tips close together (pinch / 2-finger point)
"""
from __future__ import annotations

import time
import cv2
import numpy as np

# ── Layout ────────────────────────────────────────────────────────────────────
ROWS: list[list[str]] = [
    ["1","2","3","4","5","6","7","8","9","0"],
    ["Q","W","E","R","T","Y","U","I","O","P"],
    ["A","S","D","F","G","H","J","K","L","⌫"],
    ["Z","X","C","V","B","N","M"," ","✓","✗"],
]

KEY_W = 44          # key width  (px, on the 480-wide frame)
KEY_H = 40          # key height
KEY_PAD = 4         # gap between keys
KEYBOARD_TOP = 160  # y offset from top of frame
KEYBOARD_LEFT = 4   # x offset

# colours
COL_BG      = (40,  40,  40)
COL_HOVER   = (80, 160, 255)
COL_CLICK   = (0,  220,  80)
COL_TEXT    = (255, 255, 255)
COL_SPECIAL = (200,  80,  80)   # backspace / cancel
COL_CONFIRM = (0,  200, 100)    # confirm key

CLICK_COOLDOWN = 0.45   # seconds between accepted clicks
PINCH_DIST_PX  = 28     # index-middle distance threshold for "click"


def _key_rect(row: int, col: int) -> tuple[int, int, int, int]:
    """Return (x1, y1, x2, y2) for a key cell."""
    x1 = KEYBOARD_LEFT + col * (KEY_W + KEY_PAD)
    y1 = KEYBOARD_TOP  + row * (KEY_H + KEY_PAD)
    return x1, y1, x1 + KEY_W, y1 + KEY_H


class HandKeyboard:
    def __init__(self) -> None:
        self.text: str = ""
        self._last_click_time: float = 0.0
        self._hovered: tuple[int, int] | None = None   # (row, col)
        self._clicked_key: str | None = None
        self._click_flash_until: float = 0.0
        self.confirmed: bool = False   # True when user presses ✓
        self.cancelled: bool = False   # True when user presses ✗

    # ── Public API ────────────────────────────────────────────────────────────

    def update(
        self,
        frame: np.ndarray,
        index_tip: tuple[int, int] | None,
        middle_tip: tuple[int, int] | None,
    ) -> None:
        """
        Call every frame.
        index_tip / middle_tip: pixel coords on the *same resolution* as frame,
                                or None if not visible.
        """
        self._hovered = None
        clicked = False

        if index_tip is not None:
            ix, iy = index_tip
            # find which key is hovered
            for r, row in enumerate(ROWS):
                for c, _ in enumerate(row):
                    x1, y1, x2, y2 = _key_rect(r, c)
                    if x1 <= ix <= x2 and y1 <= iy <= y2:
                        self._hovered = (r, c)

            # detect click: index + middle tips close together
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
            self._click_flash_until = time.monotonic() + 0.25
            self._handle_key(key)

        self._draw(frame)

    def _handle_key(self, key: str) -> None:
        if key == "⌫":
            self.text = self.text[:-1]
        elif key == "✓":
            self.confirmed = True
        elif key == "✗":
            self.cancelled = True
        else:
            if len(self.text) < 32:
                self.text += key

    def _draw(self, frame: np.ndarray) -> None:
        now = time.monotonic()
        for r, row in enumerate(ROWS):
            for c, key in enumerate(row):
                x1, y1, x2, y2 = _key_rect(r, c)
                is_hover  = self._hovered == (r, c)
                is_flash  = (self._clicked_key == key and now < self._click_flash_until)

                if is_flash:
                    bg = COL_CLICK
                elif is_hover:
                    bg = COL_HOVER
                elif key in ("⌫", "✗"):
                    bg = COL_SPECIAL
                elif key == "✓":
                    bg = COL_CONFIRM
                else:
                    bg = COL_BG

                cv2.rectangle(frame, (x1, y1), (x2, y2), bg, -1)
                cv2.rectangle(frame, (x1, y1), (x2, y2), (100, 100, 100), 1)

                font_scale = 0.45 if key == " " else 0.5
                label = "SPC" if key == " " else key
                (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, font_scale, 1)
                tx = x1 + (KEY_W - tw) // 2
                ty = y1 + (KEY_H + th) // 2
                cv2.putText(frame, label, (tx, ty),
                            cv2.FONT_HERSHEY_SIMPLEX, font_scale, COL_TEXT, 1, cv2.LINE_AA)

        # ── text preview bar ──────────────────────────────────────────────
        bar_y = KEYBOARD_TOP - 30
        cv2.rectangle(frame, (KEYBOARD_LEFT, bar_y - 22),
                      (KEYBOARD_LEFT + len(ROWS[0]) * (KEY_W + KEY_PAD), bar_y + 4),
                      (20, 20, 20), -1)
        display_text = self.text[-28:] if len(self.text) > 28 else self.text
        cv2.putText(frame, display_text + "|",
                    (KEYBOARD_LEFT + 4, bar_y),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 180), 1, cv2.LINE_AA)

        # ── instruction hint ──────────────────────────────────────────────
        cv2.putText(frame, "1 finger=hover  2 fingers=select",
                    (KEYBOARD_LEFT, KEYBOARD_TOP - 38),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.38, (180, 180, 180), 1, cv2.LINE_AA)
