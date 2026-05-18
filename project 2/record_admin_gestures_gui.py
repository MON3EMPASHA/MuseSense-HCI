import json
import os
import re

import cv2
import mediapipe as mp
from dollarpy import Point

from hand_shape_recognizer import normalize_landmarks, save_hand_shape, load_hand_shapes

mp_holistic = mp.solutions.holistic
mp_pose = mp.solutions.pose
mp_drawing = mp.solutions.drawing_utils

holistic = mp_holistic.Holistic(min_detection_confidence=0.65, min_tracking_confidence=0.65)
cap = cv2.VideoCapture(0)

# Admin actions to record.
# You can record either:
# - movement: index-finger trajectory ($1 templates)  -> admin_movements.py
# - shape:    static hand pose snapshot              -> admin_hand_shapes.json
GESTURES = [
    "AdminCreateArtifact",
    "AdminEditArtifact",
    "AdminDeleteArtifact",
    "AdminNextArtifact",
    "AdminPrevArtifact",
]
current_idx = 0
current_name = GESTURES[current_idx]

MOVEMENTS_FILE = "admin_movements.py"
SHAPES_FILE = "admin_hand_shapes.json"


def _load_existing_draw() -> dict[str, list[Point]]:
    if not os.path.exists(MOVEMENTS_FILE):
        return {}
    with open(MOVEMENTS_FILE, "r", encoding="utf-8") as f:
        content = f.read()

    blocks = re.findall(r'(\w+)\s*=\s*Template\([^\[]+\[(.*?)\]\)', content, re.DOTALL)
    out: dict[str, list[Point]] = {}
    for name, pts_str in blocks:
        pts = re.findall(r"Point\((\d+),\s*(\d+),\s*\d+\)", pts_str)
        parsed = [Point(int(x), int(y), 1) for x, y in pts]
        if parsed:
            out[name] = parsed
    return out


def _save_draw_templates(templates: dict[str, list[Point]]) -> None:
    with open(MOVEMENTS_FILE, "w", encoding="utf-8") as f:
        f.write("from dollarpy import Recognizer, Template, Point\n\n")
        for name, pts in templates.items():
            f.write(f'{name} = Template("{name}", [\n')
            for p in pts:
                f.write(f"    Point({p.x}, {p.y}, 1),\n")
            f.write("])\n")

        rec_str = ", ".join(templates.keys()) if templates else ""
        f.write(f"\nrecognizer = Recognizer([{rec_str}])\n")


draw_templates = _load_existing_draw()
shape_templates = load_hand_shapes(SHAPES_FILE)
print(f"Loaded {len(draw_templates)} admin movement templates from {MOVEMENTS_FILE}")
print(f"Loaded {len(shape_templates)} admin shape templates from {SHAPES_FILE}")

print("Admin Recorder Started.")
print("Press 'r' to record movement (start/stop).")
print("Press 'h' to snapshot a hand shape for the current action.")
print("Press 'n' to move to the next action.")
print("Press 's' to save movement templates and quit.")
print("Press 'q' to quit without saving movement templates.")

recording = False
current_points: list[Point] = []
status_msg = ""
save_movements = True

while cap.isOpened():
    ret, frame = cap.read()
    if not ret:
        break

    f_frame = cv2.resize(frame, (480, 320))
    image_height, image_width, _ = f_frame.shape
    rgb = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
    results = holistic.process(rgb)

    # Prefer whichever hand is present for shape snapshots.
    active_hand = None
    if results.right_hand_landmarks:
        active_hand = results.right_hand_landmarks
    elif results.left_hand_landmarks:
        active_hand = results.left_hand_landmarks

    if results.pose_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.pose_landmarks, mp_holistic.POSE_CONNECTIONS)

        r_idx = results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_INDEX]
        l_idx = results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_INDEX]
        finger = r_idx if r_idx.visibility >= l_idx.visibility else l_idx

        if finger.visibility >= 0.4:
            fx = int(finger.x * image_width)
            fy = int(finger.y * image_height)
            if recording:
                if not current_points or abs(fx - current_points[-1].x) > 3 or abs(
                    fy - current_points[-1].y
                ) > 3:
                    current_points.append(Point(fx, fy, 1))
                cv2.circle(f_frame, (fx, fy), 8, (0, 255, 0), -1)
            else:
                cv2.circle(f_frame, (fx, fy), 8, (0, 0, 255), -1)

    if active_hand:
        mp_drawing.draw_landmarks(
            f_frame, active_hand, mp_holistic.HAND_CONNECTIONS
        )

    display = cv2.resize(f_frame, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR)

    saved_move = current_name in draw_templates
    saved_shape = current_name in shape_templates
    saved_label = "SAVED: move+shape" if (saved_move and saved_shape) else (
        "SAVED: move" if saved_move else ("SAVED: shape" if saved_shape else "")
    )

    header = f"{'REC' if recording else 'Ready'}: {current_name}"
    cv2.putText(display, header, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7,
                (0, 255, 0) if recording else (0, 165, 255), 2)
    cv2.putText(
        display,
        "r: move | h: shape | n: next | s: save | q: quit",
        (10, 60),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.6,
        (255, 255, 255),
        2,
    )
    if saved_label:
        cv2.putText(display, saved_label, (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
    if status_msg:
        cv2.putText(display, status_msg, (10, 120), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 220, 255), 2)

    cv2.imshow("Admin Recorder", display)

    key = cv2.waitKey(1) & 0xFF

    if key == ord("q"):
        # Quit without saving movement templates; keep loaded templates in memory
        # so we don't accidentally drop them from the save step below.
        save_movements = False
        break
    if key == ord("s"):
        break

    if key == ord("r"):
        if not recording:
            recording = True
            current_points = []
            status_msg = ""
            print(f"Recording movement for {current_name}...")
        else:
            recording = False
            if len(current_points) > 10:
                draw_templates[current_name] = current_points[:]
                status_msg = f"Movement saved ({len(current_points)} pts)"
                print(f"Saved movement: {current_name} ({len(current_points)} pts)")
            else:
                status_msg = "Too short - try again"
                print("Movement too short, ignored.")
            current_points = []

    if key == ord("h"):
        if active_hand:
            normalized = normalize_landmarks(active_hand)
            if normalized:
                shape_templates[current_name] = normalized
                save_hand_shape(current_name, normalized, filename=SHAPES_FILE)
                status_msg = "Shape saved"
                print(f"Saved shape: {current_name}")
            else:
                status_msg = "Normalization failed"
        else:
            status_msg = "No hand detected"
            print("No hand detected - show your hand clearly.")

    if key == ord("n"):
        # If still recording, auto-stop & save if long enough.
        if recording:
            recording = False
            if len(current_points) > 10:
                draw_templates[current_name] = current_points[:]
                print(f"Auto-saved movement: {current_name} ({len(current_points)} pts)")
            current_points = []
        current_idx = (current_idx + 1) % len(GESTURES)
        current_name = GESTURES[current_idx]
        status_msg = ""

cap.release()
cv2.destroyAllWindows()

if save_movements and draw_templates:
    print(f"Saving admin movement templates to {MOVEMENTS_FILE}...")
    _save_draw_templates(draw_templates)
    print(f"Done! Saved {len(draw_templates)} admin movement templates.")
else:
    print("No admin movement templates were saved.")

if shape_templates:
    with open(SHAPES_FILE, "w", encoding="utf-8") as f:
        json.dump(shape_templates, f, indent=4)
    print(f"Saved {len(shape_templates)} admin shape templates to {SHAPES_FILE}.")
else:
    print("No admin shape templates were saved.")
