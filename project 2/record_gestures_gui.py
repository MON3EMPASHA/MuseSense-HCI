import cv2
import mediapipe as mp
import time
from dollarpy import Point
import os

mp_pose = mp.solutions.pose
mp_drawing = mp.solutions.drawing_utils

pose = mp_pose.Pose(min_detection_confidence=0.65, min_tracking_confidence=0.65)
cap = cv2.VideoCapture(0)

# The gestures we need to record
GESTURES = ["SwipeLeft", "SwipeRight", "Mute", "DarkMode", "Circle", "Like", "Dislike"]
current_gesture_idx = 0
current_gesture = GESTURES[current_gesture_idx]

recording = False
current_points = []
all_templates = {}

try:
    import re
    if os.path.exists("test_movements.py"):
        with open("test_movements.py", "r") as f:
            content = f.read()
        
        template_blocks = re.findall(r'(\w+)\s*=\s*Template\([^\[]+\[(.*?)\]\)', content, re.DOTALL)
        for name, points_str in template_blocks:
            point_matches = re.findall(r'Point\((\d+),\s*(\d+),\s*\d+\)', points_str)
            pts = [Point(int(x), int(y), 1) for x, y in point_matches]
            if pts:
                all_templates[name] = pts
        print(f"Loaded {len(all_templates)} existing gestures from test_movements.py")
    else:
        print("No test_movements.py found, starting fresh.")
except Exception as e:
    print(f"Error loading test_movements.py: {e}")

print("Gesture Recorder Started.")
print("Press 'r' to start/stop recording the current gesture.")
print("Press 'n' to move to the next gesture.")
print("Press 's' to save and quit.")
print("Press 'q' to quit without saving.")

while cap.isOpened():
    ret, frame = cap.read()
    if not ret: break
    
    f_frame = cv2.resize(frame, (480, 320))
    image_height, image_width, _ = f_frame.shape
    RGB = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
    results = pose.process(RGB)
    
    if results.pose_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.pose_landmarks, mp_pose.POSE_CONNECTIONS)
        
        right_index = results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_INDEX]
        left_index = results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_INDEX]
        
        use_right = right_index.visibility >= left_index.visibility
        finger = right_index if use_right else left_index
        
        if finger.visibility >= 0.4:
            finger_x = int(finger.x * image_width)
            finger_y = int(finger.y * image_height)
            
            if recording:
                current_points.append(Point(finger_x, finger_y, 1))
                cv2.circle(f_frame, (finger_x, finger_y), 8, (0, 255, 0), -1)
            else:
                cv2.circle(f_frame, (finger_x, finger_y), 8, (0, 0, 255), -1)
                
    display_image = cv2.resize(f_frame, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR)
    
    status = f"Recording: {current_gesture}" if recording else f"Ready: {current_gesture}"
    color = (0, 255, 0) if recording else (0, 165, 255)
    cv2.putText(display_image, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2)
    cv2.putText(display_image, "r: start/stop | n: next | s: save | q: quit", (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
    
    if current_gesture in all_templates:
        cv2.putText(display_image, "SAVED", (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)

    cv2.imshow("Gesture Recorder", display_image)
    
    key = cv2.waitKey(1)
    if key == ord('q') or key == ord('s'):
        break
    elif key == ord('r'):
        if recording:
            # Stop recording
            recording = False
            if len(current_points) > 5:
                all_templates[current_gesture] = current_points
                print(f"Saved {current_gesture} with {len(current_points)} points.")
            else:
                print(f"Gesture {current_gesture} too short, ignoring.")
            current_points = []
        else:
            # Start recording
            recording = True
            current_points = []
            print(f"Recording {current_gesture}...")
    elif key == ord('n'):
        if recording:
            recording = False
            if len(current_points) > 5:
                all_templates[current_gesture] = current_points
                print(f"Saved {current_gesture} with {len(current_points)} points.")
            else:
                print(f"Gesture {current_gesture} too short, ignoring.")
            current_points = []
        current_gesture_idx += 1
        if current_gesture_idx >= len(GESTURES):
            current_gesture_idx = 0
        current_gesture = GESTURES[current_gesture_idx]

cap.release()
cv2.destroyAllWindows()

if all_templates:
    print("Auto-saving your recorded gestures to test_movements.py...")
    with open("test_movements.py", "w") as f:
        f.write("from dollarpy import Recognizer, Template, Point\n\n")
        for name, pts in all_templates.items():
            f.write(f'{name} = Template("{name}", [\n')
            for p in pts:
                f.write(f'    Point({p.x}, {p.y}, 1),\n')
            f.write("])\n")
        
        rec_str = ", ".join(all_templates.keys())
        f.write(f"\nrecognizer = Recognizer([{rec_str}])\n")
    print("Done! You can now run test_movements_gui.py to test your own templates.")
else:
    print("No valid gestures were recorded, nothing to save.")
