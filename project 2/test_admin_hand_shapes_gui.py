import os
import time
import warnings

# Reduce noisy startup logs from TF/MediaPipe (harmless, but distracting).
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")  # hide INFO + WARNING from TF C++ backend
os.environ.setdefault("GLOG_minloglevel", "2")      # hide some glog INFO/WARNING
warnings.filterwarnings(
    "ignore",
    message=r"SymbolDatabase\.GetPrototype\(\) is deprecated\.",
    category=UserWarning,
)

import cv2
import mediapipe as mp

from hand_shape_recognizer import normalize_landmarks, load_hand_shapes

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

mp_holistic = mp.solutions.holistic
mp_drawing = mp.solutions.drawing_utils

holistic = mp_holistic.Holistic(min_detection_confidence=0.65, min_tracking_confidence=0.65)
cap = cv2.VideoCapture(0)

SHAPES_FILE = os.path.join(BASE_DIR, "admin_hand_shapes.json")
hand_shapes = load_hand_shapes(SHAPES_FILE)
shape_names = sorted(hand_shapes.keys())

print(f"Loaded {len(hand_shapes)} admin hand shapes from {SHAPES_FILE}")
print("Admin Hand Shape Test GUI")
print("  n: next target shape")
print("  t: toggle target-only / detect-any")
print("  -/+: decrease/increase sensitivity")
print("  q: quit")

target_only = True
current_idx = 0
current_target = shape_names[current_idx] if shape_names else ""

last_message = ""
last_score = 0.0
message_clear_time = 0.0
shape_cooldown_time = 0.0

# Recognition tuning:
# Lower `distance_threshold` => stricter matching.
# Higher `min_score` => require more confidence before "Matched/Detected".
distance_threshold = 0.75
min_score = 0.45


def set_message(message: str, score: float, duration: float = 2.0) -> None:
    global last_message, last_score, message_clear_time
    last_message = message
    last_score = float(score)
    message_clear_time = time.time() + duration


def _distance_to_score(dist: float, threshold: float) -> float:
    if threshold <= 0:
        return 0.0
    return max(0.0, 1.0 - (dist / threshold))


def best_shape_match(
    normalized_points: list[float], templates: dict
) -> tuple[str | None, float, bool]:
    """
    Returns (best_name, best_score, mirrored_used).
    Implements the same distance logic as hand_shape_recognizer.recognize_hand_shape,
    but always returns the best candidate (even if low confidence) and also tries a
    mirrored-X version to handle left-vs-right hand flips.
    """
    if not normalized_points or not templates:
        return None, 0.0, False

    # Mirror-invariant matching: try original and X-flipped.
    mirrored = normalized_points[:]
    for i in range(0, len(mirrored), 2):
        mirrored[i] = -mirrored[i]

    def best_dist(points: list[float]) -> tuple[str | None, float]:
        best_name = None
        best_dist_val = float("inf")
        for name, template_points in templates.items():
            if len(template_points) != len(points):
                continue
            dist = 0.0
            for p1, p2 in zip(points, template_points):
                dist += (p1 - p2) ** 2
            dist = dist ** 0.5
            if dist < best_dist_val:
                best_dist_val = dist
                best_name = name
        return best_name, best_dist_val

    name_a, dist_a = best_dist(normalized_points)
    name_b, dist_b = best_dist(mirrored)

    # Prefer whichever gives the smaller distance.
    if dist_b < dist_a:
        return name_b, _distance_to_score(dist_b, distance_threshold), True
    return name_a, _distance_to_score(dist_a, distance_threshold), False


while cap.isOpened():
    ret, frame = cap.read()
    if not ret:
        break

    f_frame = cv2.resize(frame, (480, 320))
    rgb = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
    results = holistic.process(rgb)

    active_hand = None
    if results.right_hand_landmarks:
        active_hand = results.right_hand_landmarks
    elif results.left_hand_landmarks:
        active_hand = results.left_hand_landmarks

    if active_hand:
        mp_drawing.draw_landmarks(f_frame, active_hand, mp_holistic.HAND_CONNECTIONS)

    best_name = None
    best_score = 0.0
    mirrored_used = False

    if active_hand and hand_shapes and time.time() > shape_cooldown_time:
        normalized = normalize_landmarks(active_hand)
        best_name, best_score, mirrored_used = best_shape_match(normalized, hand_shapes)

        if best_name and best_score >= min_score:
            if target_only and current_target:
                if best_name == current_target:
                    set_message(f"Matched: {best_name}", best_score, duration=2.2)
                else:
                    set_message(
                        f"Detected: {best_name} (not target)", best_score, duration=1.6
                    )
            else:
                set_message(f"Detected: {best_name}", best_score, duration=2.0)

            shape_cooldown_time = time.time() + 1.0
            print(f"{last_message} (Score: {best_score:.2f})")

    if time.time() > message_clear_time:
        last_message = ""
        last_score = 0.0

    header = "Admin Hand Shape Test"
    cv2.putText(f_frame, header, (10, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (230, 230, 230), 2)

    mode_label = "Target-only" if target_only else "Detect-any"
    target_label = current_target if current_target else "(no shapes loaded)"
    cv2.putText(
        f_frame,
        f"Mode: {mode_label} | Target: {target_label}",
        (10, 56),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.55,
        (200, 200, 200),
        2,
    )
    cv2.putText(
        f_frame,
        f"Sensitivity: {distance_threshold:.2f} | MinScore: {min_score:.2f}",
        (10, 78),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.55,
        (200, 200, 200),
        2,
    )

    if last_message:
        if last_message.startswith("Matched:"):
            color = (0, 255, 0)
        elif last_message.startswith("Detected:") and "not target" in last_message:
            color = (0, 165, 255)
        else:
            color = (0, 255, 0)

        cv2.putText(f_frame, last_message, (10, 88), cv2.FONT_HERSHEY_SIMPLEX, 0.65, color, 2)
        cv2.putText(f_frame, f"Score: {last_score:.2f}", (10, 118), cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)
    else:
        cv2.putText(
            f_frame,
            "Show your hand to test...",
            (10, 88),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.65,
            (180, 180, 180),
            2,
        )
        # Show "best guess" even when it's below threshold, to help tune.
        if best_name:
            extra = f"Best: {best_name} ({best_score:.2f})"
            if mirrored_used:
                extra += " [mirrored]"
            cv2.putText(
                f_frame,
                extra,
                (10, 118),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (140, 140, 140),
                2,
            )

    display_image = cv2.resize(f_frame, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR)
    cv2.imshow("Admin Hand Shape Test GUI", display_image)

    key = cv2.waitKey(1) & 0xFF
    if key == ord("q"):
        break
    if key == ord("t"):
        target_only = not target_only
        set_message(f"Mode: {'Target-only' if target_only else 'Detect-any'}", 0.0, duration=1.2)
    if key == ord("n"):
        if shape_names:
            current_idx = (current_idx + 1) % len(shape_names)
            current_target = shape_names[current_idx]
            set_message(f"Target: {current_target}", 0.0, duration=1.2)
    if key in (ord("+"), ord("=")):
        distance_threshold = min(2.0, distance_threshold + 0.05)
        set_message(f"Sensitivity: {distance_threshold:.2f}", 0.0, duration=1.0)
    if key in (ord("-"), ord("_")):
        distance_threshold = max(0.2, distance_threshold - 0.05)
        set_message(f"Sensitivity: {distance_threshold:.2f}", 0.0, duration=1.0)

cap.release()
cv2.destroyAllWindows()
