import cv2
import mediapipe as mp
from hand_shape_recognizer import normalize_landmarks, save_hand_shape, load_hand_shapes

mp_holistic = mp.solutions.holistic
mp_drawing = mp.solutions.drawing_utils
holistic = mp_holistic.Holistic(min_detection_confidence=0.65, min_tracking_confidence=0.65)
cap = cv2.VideoCapture(0)

# The static shapes we want the user to record
SHAPES = ["Mute", "DarkMode"]
current_shape_idx = 0
current_shape = SHAPES[current_shape_idx]

all_shapes = load_hand_shapes()
print(f"Loaded {len(all_shapes)} existing hand shapes from hand_shapes.json")

print("Hand Shape Recorder Started.")
print("Hold your hand in the desired shape (e.g. Peace sign, Fist)")
print("Press 'r' to SNAPSHOT the current hand shape.")
print("Press 'n' to move to the next shape.")
print("Press 'q' or 's' to quit.")

while cap.isOpened():
    ret, frame = cap.read()
    if not ret: break
    
    f_frame = cv2.resize(frame, (480, 320))
    RGB = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
    results = holistic.process(RGB)
    
    active_hand = None
    if results.right_hand_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.right_hand_landmarks, mp_holistic.HAND_CONNECTIONS)
        active_hand = results.right_hand_landmarks
    elif results.left_hand_landmarks:
        mp_drawing.draw_landmarks(f_frame, results.left_hand_landmarks, mp_holistic.HAND_CONNECTIONS)
        active_hand = results.left_hand_landmarks
        
    display_image = cv2.resize(f_frame, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR)
    
    status = f"Ready: {current_shape}"
    cv2.putText(display_image, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 165, 255), 2)
    cv2.putText(display_image, "r: snapshot | n: next | q: quit", (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
    
    if current_shape in all_shapes:
        cv2.putText(display_image, "SAVED", (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)

    cv2.imshow("Hand Shape Recorder", display_image)
    
    key = cv2.waitKey(1)
    if key == ord('q') or key == ord('s'):
        break
    elif key == ord('r'):
        if active_hand:
            normalized = normalize_landmarks(active_hand)
            if normalized:
                all_shapes[current_shape] = normalized
                save_hand_shape(current_shape, normalized)
                print(f"Successfully recorded shape for {current_shape}!")
        else:
            print("No hand detected! Please put your hand in frame and try again.")
    elif key == ord('n'):
        current_shape_idx += 1
        if current_shape_idx >= len(SHAPES):
            current_shape_idx = 0
        current_shape = SHAPES[current_shape_idx]

cap.release()
cv2.destroyAllWindows()
