"""
Virtual on-screen keyboard driven by hand landmarks.

Navigation  : index finger tip (1 finger up)
Click       : index + middle finger tips close together (pinch)
Confirm     : open LEFT hand held for 1 second
Cancel      : (cancel key removed)

Modes
-----
mode="alpha"  – full QWERTY + numbers + DEL / OK
mode="num"    – numeric pad  (0-9, DEL, OK)
"""
from __future__ import annotations

import time
import cv2
import numpy as np

#Layouts
_ROWS_ALPHA: list[list[str]] = [
    ["1","2","3","4","5","6","7","8","9","OK"],
    ["Q","W","E","R","T","Y","U","I","O","P"],
    ["A","S","D","F","G","H","J","K","L","DEL"],
    ["Z","X","C","V","B","N","M","SPC","0"],
]

_ROWS_NUM: list[list[str]] = [
    ["OK","7","8"],
    ["4","5","6"],
    ["1","2","3"],
    ["DEL","0","9"],
]

_ACTION_KEYS = {"DEL", "OK", "SPC"}

#Colours (BGR)
_C = {
    "panel_bg":    (18,  18,  24),
    "panel_border":(55,  55,  75),
    "key_bg":      (38,  38,  52),
    "key_border":  (70,  70,  95),
    "key_text":    (230, 230, 240),
    "hover_bg":    (55, 120, 230),
    "hover_border":(110, 175, 255),
    "click_bg":    (30, 190,  80),
    "click_border":(80, 255, 130),
    "del_bg":      (140,  35,  35),
    "del_border":  (200,  70,  70),
    "ok_bg":       (25, 140,  65),
    "ok_border":   (60, 210, 110),
    "num_bg":      (30,  30,  48),
    "input_bg":    (12,  12,  18),
    "input_text":  (0,  230, 160),
    "input_border":(55,  55,  75),
    "confirm_bar": (30, 190,  80),
    "confirm_text":(255, 255, 255),
    "hint_text":   (130, 130, 150),
}

# Timing
CLICK_COOLDOWN   = 0.30   # seconds between key presses (faster = smoother typing)
PINCH_DIST_PX    = 30     # index-middle distance threshold for a click
PINCH_HOLD_TIME  = 0.5    # seconds pinch must be held to fire a click
OPEN_HAND_HOLD   = 1.0    # seconds open-hand must be held to confirm

#Cursor smoothing
CURSOR_EMA_ALPHA = 0.45   # lower = smoother but more lag (0..1)

#MediaPipe landmark indices
_FINGER_TIPS = [8, 12, 16, 20]
_FINGER_PIPS = [6, 10, 14, 18]
_THUMB_TIP   = 4
_THUMB_IP    = 3


def _is_open_hand(hand_landmarks) -> bool:
    if hand_landmarks is None:
        return False
    lm = hand_landmarks.landmark
    fingers_open = all(lm[t].y < lm[p].y for t, p in zip(_FINGER_TIPS, _FINGER_PIPS))
    thumb_open   = abs(lm[_THUMB_TIP].x - lm[_THUMB_IP].x) > 0.04
    return fingers_open and thumb_open


def _rounded_rect(img: np.ndarray, x1: int, y1: int, x2: int, y2: int,
                  color: tuple, radius: int = 6, thickness: int = -1) -> None:
    """Draw a filled or outlined rounded rectangle."""
    if thickness == -1:
        # filled
        cv2.rectangle(img, (x1 + radius, y1), (x2 - radius, y2), color, -1)
        cv2.rectangle(img, (x1, y1 + radius), (x2, y2 - radius), color, -1)
        for cx, cy in [(x1+radius, y1+radius), (x2-radius, y1+radius),
                       (x1+radius, y2-radius), (x2-radius, y2-radius)]:
            cv2.circle(img, (cx, cy), radius, color, -1)
    else:
        cv2.rectangle(img, (x1 + radius, y1), (x2 - radius, y1), color, thickness)
        cv2.rectangle(img, (x1 + radius, y2), (x2 - radius, y2), color, thickness)
        cv2.rectangle(img, (x1, y1 + radius), (x1, y2 - radius), color, thickness)
        cv2.rectangle(img, (x2, y1 + radius), (x2, y2 - radius), color, thickness)
        for cx, cy, a1, a2 in [
            (x1+radius, y1+radius, 180, 270),
            (x2-radius, y1+radius, 270, 360),
            (x1+radius, y2-radius,  90, 180),
            (x2-radius, y2-radius,   0,  90),
        ]:
            cv2.ellipse(img, (cx, cy), (radius, radius), 0, a1, a2, color, thickness)


def _draw_key_box(frame: np.ndarray, x1: int, y1: int, x2: int, y2: int,
                  bg: tuple, border: tuple, label: str) -> None:
    """Draw a single key with rounded corners and centred label."""
    _rounded_rect(frame, x1, y1, x2, y2, bg, radius=5)
    _rounded_rect(frame, x1, y1, x2, y2, border, radius=5, thickness=1)

    # subtle bottom-edge shadow
    shadow = tuple(max(c - 35, 0) for c in bg)
    cv2.line(frame, (x1 + 6, y2 - 1), (x2 - 6, y2 - 1), shadow, 1)

    kw, kh = x2 - x1, y2 - y1
    fs = 0.42
    if len(label) > 2:
        fs = 0.30
    (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, fs, 1)
    tx = x1 + (kw - tw) // 2
    ty = y1 + (kh + th) // 2 - 1
    cv2.putText(frame, label, (tx, ty),
                cv2.FONT_HERSHEY_SIMPLEX, fs, _C["key_text"], 1, cv2.LINE_AA)


class HandKeyboard:
    """
    Parameters
    ----------
    mode : "alpha" | "num"
        "alpha" shows full QWERTY; "num" shows a numeric pad.
    frame_w, frame_h : int
        Dimensions of the frame the keyboard will be drawn on.
        The keyboard is sized and positioned relative to these.
    """

    def __init__(self, mode: str = "alpha",
                 frame_w: int = 960, frame_h: int = 640) -> None:
        self.text: str      = ""
        self.confirmed: bool = False
        self.cancelled: bool = False
        self._mode           = mode
        self._fw             = frame_w
        self._fh             = frame_h

        self._rows = _ROWS_ALPHA if mode == "alpha" else _ROWS_NUM
        self._compute_layout()

        self._last_click_time: float  = 0.0
        self._pinch_since: float | None = None
        self._hovered: tuple[int,int] | None = None
        self._clicked_key: str | None = None
        self._click_flash_until: float = 0.0

        self._open_hand_since: float | None = None
        self._confirm_flash_until: float    = 0.0

        # cursor smoothing state
        self._smooth_cx: float | None = None
        self._smooth_cy: float | None = None
        self._smooth_mx: float | None = None
        self._smooth_my: float | None = None
        self._cursor_valid: bool = False

    #layout

    def _compute_layout(self) -> None:
        """Compute key sizes and keyboard origin based on frame dimensions."""
        fw, fh = self._fw, self._fh

        if self._mode == "alpha":
            cols = 10
            total_w = int(fw * 0.86)
            pad     = max(3, total_w // 120)
            self._kw  = (total_w - (cols - 1) * pad) // cols
            self._kh  = max(32, int(fh * 0.08))
            self._pad = pad
            rows      = len(self._rows)
            kb_w      = cols * (self._kw + pad) - pad
            kb_h      = rows * (self._kh + pad) - pad
            self._ox  = (fw - kb_w) // 2
            self._oy  = int(fh * 0.2)
        else:
            cols = 3
            self._kw  = int(fw * 0.14)
            self._kh  = int(fh * 0.11)
            self._pad = 8
            rows      = len(self._rows)
            kb_w      = cols * (self._kw + self._pad) - self._pad
            kb_h      = rows * (self._kh + self._pad) - self._pad
            self._ox  = (fw - kb_w) // 2
            self._oy  = int(fh * 0.2)

    def _key_rect(self, r: int, c: int) -> tuple[int,int,int,int]:
        x1 = self._ox + c * (self._kw + self._pad)
        y1 = self._oy + r * (self._kh + self._pad)
        return x1, y1, x1 + self._kw, y1 + self._kh

    # public API

    def update(self, frame: np.ndarray,
               index_tip: tuple[int,int] | None,
               middle_tip: tuple[int,int] | None,
               left_hand_landmarks=None) -> None:
        self._hovered = None
        clicked       = False

        if index_tip is not None:
            ix_raw, iy_raw = index_tip

            # EMA cursor smoothing
            if self._smooth_cx is None:
                self._smooth_cx = float(ix_raw)
                self._smooth_cy = float(iy_raw)
            else:
                self._smooth_cx += (ix_raw - self._smooth_cx) * CURSOR_EMA_ALPHA
                self._smooth_cy += (iy_raw - self._smooth_cy) * CURSOR_EMA_ALPHA
            ix = int(round(self._smooth_cx))
            iy = int(round(self._smooth_cy))
            self._cursor_valid = True

            #nearest-key hover (no more bounding-box overwrite)
            best_dist_sq = float("inf")
            best_key = None
            for r, row in enumerate(self._rows):
                for c in range(len(row)):
                    x1, y1, x2, y2 = self._key_rect(r, c)
                    ckx = (x1 + x2) // 2
                    cky = (y1 + y2) // 2
                    dsq = (ix - ckx) ** 2 + (iy - cky) ** 2
                    if dsq < best_dist_sq:
                        best_dist_sq = dsq
                        best_key = (r, c)
            self._hovered = best_key

            #pinch click with smoothed middle finger
            if middle_tip is not None:
                mx_raw, my_raw = middle_tip
                if self._smooth_mx is None:
                    self._smooth_mx = float(mx_raw)
                    self._smooth_my = float(my_raw)
                else:
                    self._smooth_mx += (mx_raw - self._smooth_mx) * CURSOR_EMA_ALPHA
                    self._smooth_my += (my_raw - self._smooth_my) * CURSOR_EMA_ALPHA
                mx = int(round(self._smooth_mx))
                my = int(round(self._smooth_my))
                dist = ((ix - mx) ** 2 + (iy - my) ** 2) ** 0.5
                now = time.monotonic()
                if dist < PINCH_DIST_PX:
                    if self._pinch_since is None:
                        self._pinch_since = now
                    elif (now - self._pinch_since >= PINCH_HOLD_TIME
                          and now - self._last_click_time > CLICK_COOLDOWN):
                        clicked = True
                        self._last_click_time = now
                        self._pinch_since = None
                else:
                    self._pinch_since = None
        else:
            self._cursor_valid = False

        if clicked and self._hovered is not None:
            r, c  = self._hovered
            key   = self._rows[r][c]
            self._clicked_key       = key
            self._click_flash_until = time.monotonic() + 0.25
            self._handle_key(key)

        # open-hand confirm
        if not self.confirmed:
            if _is_open_hand(left_hand_landmarks):
                if self._open_hand_since is None:
                    self._open_hand_since = time.monotonic()
                elif time.monotonic() - self._open_hand_since >= OPEN_HAND_HOLD:
                    self.confirmed            = True
                    self._confirm_flash_until = time.monotonic() + 0.5
                    self._open_hand_since     = None
            else:
                self._open_hand_since = None

        self._draw(frame)

    #input handling

    def _handle_key(self, key: str) -> None:
        if key == "DEL":
            self.text = self.text[:-1]
        elif key == "OK":
            self.confirmed = True
        elif key == "SPC":
            if len(self.text) < 32:
                self.text += " "
        else:
            if self._mode == "num" and not key.isdigit():
                return
            if len(self.text) < 32:
                self.text += key

    #drawing

    def _draw(self, frame: np.ndarray) -> None:
        now  = time.monotonic()
        rows = self._rows
        cols = max(len(r) for r in rows)

        kb_w = cols * (self._kw + self._pad) - self._pad
        kb_h = len(rows) * (self._kh + self._pad) - self._pad

        # transparent overlay for semi-transparent keys & input
        overlay = frame.copy()

        # panel background (very subtle)
        pad_x, pad_y = 12, 8
        input_h      = 36
        px1 = self._ox - pad_x
        py1 = self._oy - input_h - 14 - pad_y
        px2 = self._ox + kb_w + pad_x
        py2 = self._oy + kb_h + pad_y
        _rounded_rect(overlay, px1, py1, px2, py2, _C["panel_bg"], radius=10)
        cv2.addWeighted(overlay, 0.4, frame, 0.6, 0, frame)

        # input field (semi-transparent)
        inp_x1 = self._ox
        inp_y1 = self._oy - input_h - 8
        inp_x2 = self._ox + kb_w
        inp_y2 = self._oy - 8
        inp_overlay = frame.copy()
        _rounded_rect(inp_overlay, inp_x1, inp_y1, inp_x2, inp_y2, _C["input_bg"], radius=5)
        _rounded_rect(inp_overlay, inp_x1, inp_y1, inp_x2, inp_y2, _C["input_border"], radius=5, thickness=1)
        cv2.addWeighted(inp_overlay, 0.4, frame, 0.6, 0, frame)

        display = (self.text[-38:] if len(self.text) > 38 else self.text) + "|"
        cv2.putText(frame, display, (inp_x1 + 8, inp_y2 - 9),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.48, _C["input_text"], 1, cv2.LINE_AA)

        # keys (semi-transparent)
        key_overlay = frame.copy()
        for r, row in enumerate(rows):
            row_w = len(row) * (self._kw + self._pad) - self._pad
            row_ox = self._ox + (kb_w - row_w) // 2

            for c, key in enumerate(row):
                x1 = row_ox + c * (self._kw + self._pad)
                y1 = self._oy + r * (self._kh + self._pad)
                x2, y2 = x1 + self._kw, y1 + self._kh

                is_hover = self._hovered == (r, c)
                is_flash = (self._clicked_key == key and now < self._click_flash_until)

                if is_flash:
                    bg, border = _C["click_bg"], _C["click_border"]
                elif is_hover:
                    bg, border = _C["hover_bg"], _C["hover_border"]
                elif key == "DEL":
                    bg, border = _C["del_bg"], _C["del_border"]
                elif key == "OK":
                    bg, border = _C["ok_bg"], _C["ok_border"]
                elif r == 0 and self._mode == "alpha":
                    bg, border = _C["num_bg"], _C["key_border"]
                else:
                    bg, border = _C["key_bg"], _C["key_border"]

                _draw_key_box(key_overlay, x1, y1, x2, y2, bg, border, key)
        cv2.addWeighted(key_overlay, 0.4, frame, 0.6, 0, frame)

        #smoothed cursor crosshair
        if self._cursor_valid and self._smooth_cx is not None:
            cx = int(round(self._smooth_cx))
            cy = int(round(self._smooth_cy))
            # outer glow
            cv2.circle(frame, (cx, cy), 14, (0, 0, 0), 2)
            cv2.circle(frame, (cx, cy), 10, (0, 200, 255), 2)
            cv2.circle(frame, (cx, cy), 4, (0, 255, 255), -1)
            # crosshair lines
            cv2.line(frame, (cx - 18, cy), (cx - 8, cy), (255, 255, 255), 1)
            cv2.line(frame, (cx + 8, cy), (cx + 18, cy), (255, 255, 255), 1)
            cv2.line(frame, (cx, cy - 18), (cx, cy - 8), (255, 255, 255), 1)
            cv2.line(frame, (cx, cy + 8), (cx, cy + 18), (255, 255, 255), 1)

        # open-hand confirm progress
        bar_y1 = py1 - 14
        bar_y2 = py1 - 6
        if now < self._confirm_flash_until:
            _rounded_rect(frame, px1, bar_y1, px2, bar_y2, _C["confirm_bar"], radius=3)
            cv2.putText(frame, "Confirmed!",
                        (px1 + 6, bar_y1 - 4),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.45, _C["confirm_bar"], 1, cv2.LINE_AA)
        elif self._open_hand_since is not None:
            elapsed  = now - self._open_hand_since
            progress = min(elapsed / OPEN_HAND_HOLD, 1.0)
            bar_fill = px1 + int((px2 - px1) * progress)
            cv2.rectangle(frame, (px1, bar_y1), (px2, bar_y2), (40, 40, 55), -1)
            _rounded_rect(frame, px1, bar_y1, bar_fill, bar_y2, _C["confirm_bar"], radius=3)
            cv2.putText(frame, "Hold open hand to confirm...",
                        (px1 + 4, bar_y1 - 4),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.36, _C["hint_text"], 1, cv2.LINE_AA)
