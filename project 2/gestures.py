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
    if time.monotonic() >= gesture_feedback_until or not gesture_feedback_text:
        return

    import cv2

    cv2.putText(
        frame,
        gesture_feedback_text,
        (20, 60),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.8,
        (0, 255, 255),
        2,
    )


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
