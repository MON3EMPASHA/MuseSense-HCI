import cv2
import mediapipe as mp
import re
import os
import json
from dollarpy import Point
from hand_shape_recognizer import normalize_landmarks, save_hand_shape, load_hand_shapes

mp_holistic = mp.solutions.holistic
mp_pose = mp.solutions.pose
mp_drawing = mp.solutions.drawing_utils

holistic = mp_holistic.Holistic(min_detection_confidence=0.65, min_tracking_confidence=0.65)
cap = cv2.VideoCapture(0)

# Gesture list
GESTURES = [
    ("SwipeLeft",  "draw"),
    ("SwipeRight", "draw"),
    ("Circle",     "draw"),
    ("Like",       "draw"),
    ("Dislike",    "draw"),
    ("Mute",       "hold"),
    ("DarkMode",   "hold"),
]
current_idx = 0

#Load existing data
draw_templates = {}   # name → list[Point]
hold_shapes    = load_hand_shapes()

MOVEMENTS_FILE = "test_movements.py"

def _load_existing_draw():
    if not os.path.exists(MOVEMENTS_FILE):
        return {}
    with open(MOVEMENTS_FILE, "r") as f:
        content = f.read()
    blocks = re.findall(r'(\w+)\s*=\s*Template\([^\[]+\[(.*?)\]\)', content, re.DOTALL)
    out = {}
    for name, pts_str in blocks:
        pts = re.findall(r'Point\((\d+),\s*(\d+),\s*\d+\)', pts_str)
        parsed = [Point(int(x), int(y), 1) for x, y in pts]
        if parsed:
            out[name] = parsed
    return out

draw_templates = _load_existing_draw()
print(f"Loaded {len(draw_templates)} draw templates, {len(hold_shapes)} hold shapes.")

#Save helpers
def save_all():
    # Save draw templates
    with open(MOVEMENTS_FILE, "w") as f:
        f.write("from dollarpy import Recognizer, Template, Point\n\n")
        for name, pts in draw_templates.items():
            f.write(f'{name} = Template("{name}", [\n')
            for p in pts:
                f.write(f'    Point({p.x}, {p.y}, 1),\n')
            f.write("])\n")
        rec_str = ", ".join(draw_templates.keys()) if draw_templates else ""
        f.write(f"\nrecognizer = Recognizer([{rec_str}])\n")
    # Save hold shapes
    with open("hand_shapes.json", "w") as f:
        json.dump(hold_shapes, f, indent=4)
    print(f"Auto-saved: {len(draw_templates)} draw templates, {len(hold_shapes)} hold shapes.")

#state
recording      = False
current_points = []
status_msg     = ""

print("\n─── Gesture Recorder ───────────────────────────────────────────")
print("  r  → start / stop recording (draw gestures: swipe, circle…)")
print("  h  → snapshot current hand shape  (hold gestures: mute, …)")
print("  n  → next gesture")
print("  q  → quit & auto-save")
print("────────────────────────────────────────────────────────────────\n")

while cap.isOpened():
    ret, frame = cap.read()
    if not ret:
        break

    f_frame = cv2.resize(frame, (480, 320))
    image_height, image_width, _ = f_frame.shape
    RGB = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
    results = holistic.process(RGB)

    active_hand = None

    # Draw hand landmarks
    if results.right_hand_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.right_hand_landmarks, mp_holistic.HAND_CONNECTIONS)
        active_hand = results.right_hand_landmarks
    elif results.left_hand_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.left_hand_landmarks, mp_holistic.HAND_CONNECTIONS)
        active_hand = results.left_hand_landmarks

    # Draw pose landmarks & track index finger for draw mode
    if results.pose_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.pose_landmarks, mp_holistic.POSE_CONNECTIONS)

        r_idx = results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_INDEX]
        l_idx = results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_INDEX]
        finger = r_idx if r_idx.visibility >= l_idx.visibility else l_idx

        if finger.visibility >= 0.4:
            fx = int(finger.x * image_width)
            fy = int(finger.y * image_height)
            if recording:
                # Only save the point if the finger has actually moved (>3px threshold)
                if not current_points or abs(fx - current_points[-1].x) > 3 or abs(fy - current_points[-1].y) > 3:
                    current_points.append(Point(fx, fy, 1))
                cv2.circle(f_frame, (fx, fy), 8, (0, 255, 0), -1)
            else:
                cv2.circle(f_frame, (fx, fy), 8, (0, 0, 255), -1)

    #Display
    display = cv2.resize(f_frame, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR)

    g_name, g_type = GESTURES[current_idx]
    saved_draw = g_name in draw_templates
    saved_hold = g_name in hold_shapes
    saved      = (saved_draw if g_type == "draw" else saved_hold)
    type_label = "DRAW (r: start/stop)" if g_type == "draw" else "HOLD (h: snapshot)"

    color = (0, 255, 0) if recording else (0, 165, 255)
    header = f"{'● REC' if recording else 'Ready'}: {g_name}  [{type_label}]"
    cv2.putText(display, header,  (10, 30),  cv2.FONT_HERSHEY_SIMPLEX, 0.65, color, 2)
    cv2.putText(display, "r: draw | h: hold | n: next | q: quit",
                (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1)
    if saved:
        cv2.putText(display, "✓ SAVED", (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 255, 0), 2)
    if status_msg:
        cv2.putText(display, status_msg, (10, 120), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 220, 255), 2)

    cv2.imshow("Gesture Recorder", display)

    #Key handling
    key = cv2.waitKey(1) & 0xFF

    if key == ord('q') or key == ord('s'):
        if recording:
            # auto-stop & save before quitting
            if len(current_points) > 5:
                draw_templates[g_name] = current_points[:]
        break

    elif key == ord('r'):
        # Toggle trajectory recording
        if not recording:
            recording = True
            current_points = []
            status_msg = ""
            print(f"Recording trajectory for {g_name}…")
        else:
            recording = False
            if len(current_points) > 5:
                draw_templates[g_name] = current_points[:]
                status_msg = f"Saved {g_name} ({len(current_points)} pts)"
                print(f"Saved draw: {g_name} ({len(current_points)} pts)")
            else:
                status_msg = "Too short – try again"
                print(f"Gesture too short, ignored.")
            current_points = []

    elif key == ord('h'):
        # Snapshot static hand shape
        if active_hand:
            norm = normalize_landmarks(active_hand)
            if norm:
                hold_shapes[g_name] = norm
                save_hand_shape(g_name, norm)
                status_msg = f"Shape saved: {g_name}"
                print(f"Saved hold shape: {g_name}")
            else:
                status_msg = "Normalisation failed, try again"
        else:
            status_msg = "No hand visible!"
            print("No hand detected – show your hand clearly.")

    elif key == ord('n'):
        # Move to next gesture (save current draw if still recording)
        if recording:
            recording = False
            if len(current_points) > 5:
                draw_templates[g_name] = current_points[:]
                print(f"Auto-saved draw: {g_name}")
            current_points = []
        current_idx = (current_idx + 1) % len(GESTURES)
        status_msg = ""

cap.release()
cv2.destroyAllWindows()
save_all()
