import cv2
import mediapipe as mp
import time
import os
from dollarpy import Point
from test_movements import recognizer
from gestures import is_gesture_significant, is_circle_like
from hand_shape_recognizer import normalize_landmarks, load_hand_shapes, recognize_hand_shape

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

mp_holistic = mp.solutions.holistic
mp_pose = mp.solutions.pose
mp_drawing = mp.solutions.drawing_utils

holistic = mp_holistic.Holistic(min_detection_confidence=0.65, min_tracking_confidence=0.65)
cap = cv2.VideoCapture(0)

# Load custom static hand shapes
hand_shapes = load_hand_shapes(os.path.join(BASE_DIR, "hand_shapes.json"))
print(f"Loaded {len(hand_shapes)} static hand shapes.")

gesture_points = []
circle_points = []
frame_count = 0

last_detected = ""
last_score = 0.0
message_clear_time = 0
shape_cooldown_time = 0

print("Starting Unified Test GUI. Press 'q' to quit.")

while cap.isOpened():
    ret, frame = cap.read()
    if not ret:
        break
    
    f_frame = cv2.resize(frame, (480, 320))
    image_height, image_width, _ = f_frame.shape
    
    RGB = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
    results = holistic.process(RGB)
    
    active_hand = None
    
    # 1. Process Static Hand Shapes (if hand is clearly visible)
    if results.right_hand_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.right_hand_landmarks, mp_holistic.HAND_CONNECTIONS)
        active_hand = results.right_hand_landmarks
    elif results.left_hand_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.left_hand_landmarks, mp_holistic.HAND_CONNECTIONS)
        active_hand = results.left_hand_landmarks
        
    if active_hand and hand_shapes and time.time() > shape_cooldown_time:
        normalized = normalize_landmarks(active_hand)
        shape_name, shape_score = recognize_hand_shape(normalized, hand_shapes, threshold=0.45)
        
        if shape_name and shape_score > 0.55:
            last_detected = f"Detected Shape: {shape_name}"
            last_score = shape_score
            message_clear_time = time.time() + 2.0
            shape_cooldown_time = time.time() + 1.5
            print(f"{last_detected} (Score: {last_score:.2f})")
            
    # 2. Process Dynamic Trajectory (Index Finger Tracking)
    if results.pose_landmarks:
        # We draw basic pose connections
        mp_drawing.draw_landmarks(f_frame, results.pose_landmarks, mp_holistic.POSE_CONNECTIONS)
        
        right_index = results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_INDEX]
        left_index = results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_INDEX]
        
        use_right = right_index.visibility >= left_index.visibility
        finger = right_index if use_right else left_index
        
        if finger.visibility >= 0.4:
            finger_x = int(finger.x * image_width)
            finger_y = int(finger.y * image_height)
            # Only add if finger moved more than 3px (filters jitter)
            if not gesture_points or abs(finger_x - gesture_points[-1].x) > 3 or abs(finger_y - gesture_points[-1].y) > 3:
                p = Point(finger_x, finger_y, 1)
                gesture_points.append(p)
                circle_points.append(p)
            
            # Draw tracking circle for the active index finger
            cv2.circle(f_frame, (finger_x, finger_y), 8, (0, 0, 255), -1)
            
    frame_count += 1
    if frame_count % 30 == 0:
        frame_count = 0

        if gesture_points and is_gesture_significant(gesture_points):
            result = recognizer.recognize(gesture_points)
            if result[0] is not None:
                gesture_name = str(result[0])
                score = float(result[1])

                if score < 0.5:
                    last_detected = f"Ignored: {gesture_name}"
                    last_score = score
                    message_clear_time = time.time() + 1.5
                    print(f"Ignored: {gesture_name} (Score: {score:.2f})")
                elif gesture_name == "Circle" and not is_circle_like(circle_points):
                    last_detected = "Ignored: Weak Circle"
                    message_clear_time = time.time() + 1.5
                    print("Ignored: Weak Circle")
                else:
                    last_detected = f"Detected: {gesture_name}"
                    last_score = score
                    message_clear_time = time.time() + 2.0
                    print(f"{last_detected} (Score: {last_score:.2f})")

        if not last_detected and is_circle_like(circle_points):
            last_detected = "Detected: Circle (Motion)"
            last_score = 1.0
            message_clear_time = time.time() + 2.0
            print(last_detected)
            
        gesture_points.clear()
        circle_points.clear()

    # Clear message
    if time.time() > message_clear_time:
        last_detected = ""
        last_score = 0.0

    if last_detected:
        color = (0, 255, 0) if "Detected" in last_detected else (0, 165, 255)
        cv2.putText(f_frame, last_detected, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2)
        if last_score > 0:
            cv2.putText(f_frame, f"Score: {last_score:.2f}", (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)
    else:
        cv2.putText(f_frame, "Waiting for gesture...", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (200, 200, 200), 2)
        
    display_image = cv2.resize(f_frame, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR)
    cv2.imshow("Gesture Test GUI", display_image)
    
    if cv2.waitKey(1) == ord('q'):
        break
        
cap.release()
cv2.destroyAllWindows()
