import time
from typing import List

import numpy as np
from dollarpy import Point

gesture_feedback_text = ""
gesture_feedback_until = 0.0


def show_gesture_feedback(message: str, duration: float = 2.0) -> None:
    global gesture_feedback_text, gesture_feedback_until
    gesture_feedback_text = message
    gesture_feedback_until = time.monotonic() + duration


def draw_gesture_feedback(frame: np.ndarray) -> None:
    import cv2

    h, w = frame.shape[:2]

    # Persistent last-gesture label (top-right)
    if gesture_feedback_text:
        still_active = time.monotonic() < gesture_feedback_until
        label = gesture_feedback_text
        color = (0, 255, 128) if still_active else (120, 120, 120)
        font = cv2.FONT_HERSHEY_SIMPLEX
        scale = 0.75
        thickness = 2
        (tw, th), _ = cv2.getTextSize(label, font, scale, thickness)
        x = w - tw - 16
        y = 36
        # semi-transparent background
        cv2.rectangle(frame, (x - 8, y - th - 6), (x + tw + 8, y + 8), (0, 0, 0), -1)
        cv2.putText(frame, label, (x, y), font, scale, color, thickness)


def gesture_path_stats(points: List[Point]) -> dict[str, float]:
    xs = [point.x for point in points]
    ys = [point.y for point in points]
    min_x = min(xs)
    max_x = max(xs)
    min_y = min(ys)
    max_y = max(ys)
    width = max_x - min_x
    height = max_y - min_y
    path_length = 0.0

    for index in range(1, len(points)):
        dx = points[index].x - points[index - 1].x
        dy = points[index].y - points[index - 1].y
        path_length += (dx * dx + dy * dy) ** 0.5

    return {
        "width": float(width),
        "height": float(height),
        "path_length": float(path_length),
        "start_end_distance": float(
            ((points[0].x - points[-1].x) ** 2 + (points[0].y - points[-1].y) ** 2)
            ** 0.5
        ),
        "center_x": float(sum(xs) / len(xs)),
        "center_y": float(sum(ys) / len(ys)),
    }


def is_circle_like(points: List[Point]) -> bool:
    if len(points) < 18:
        return False

    stats = gesture_path_stats(points)
    width = stats["width"]
    height = stats["height"]
    if width < 70 or height < 70:
        return False

    smaller = min(width, height)
    larger = max(width, height)
    if smaller <= 0 or larger / smaller > 1.5:
        return False

    if stats["path_length"] < max(width, height) * 2.2:
        return False

    if stats["start_end_distance"] > max(width, height) * 0.45:
        return False

    radii = []
    for point in points:
        dx = point.x - stats["center_x"]
        dy = point.y - stats["center_y"]
        radii.append((dx * dx + dy * dy) ** 0.5)

    mean_radius = sum(radii) / len(radii)
    if mean_radius <= 0:
        return False

    average_deviation = sum(abs(radius - mean_radius) for radius in radii) / len(radii)
    if average_deviation / mean_radius > 0.35:
        return False

    return True


def is_gesture_significant(points: List[Point]) -> bool:
    if len(points) < 12:
        return False

    stats = gesture_path_stats(points)
    return stats["width"] >= 80 or stats["height"] >= 80 or stats["path_length"] >= 160


def detect_swipe(points: List[Point]) -> str | None:
    """
    Geometry-based swipe detector — does NOT rely on $1 templates.
    Returns 'SwipeLeft', 'SwipeRight', or None.

    Criteria:
    - At least 8 points
    - Net horizontal displacement >= 60px  (strong directional movement)
    - Width / Height ratio >= 1.8          (gesture is wider than it is tall)
    - Net X displacement covers >= 40% of total path length (not too squiggly)
    """
    if len(points) < 8:
        return None

    stats = gesture_path_stats(points)
    width = stats["width"]
    height = stats["height"]
    path_length = stats["path_length"]

    if height == 0 or path_length == 0:
        return None

    # Must be predominantly horizontal
    if width / height < 1.8:
        return None

    # Net X displacement (start → end)
    net_x = points[-1].x - points[0].x

    # Net displacement must be significant fraction of path (not back-and-forth)
    if abs(net_x) / path_length < 0.40:
        return None

    # Minimum absolute displacement
    if abs(net_x) < 60:
        return None

    return "SwipeLeft" if net_x < 0 else "SwipeRight"
