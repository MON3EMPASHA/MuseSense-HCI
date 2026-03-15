import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision as mp_vision
import numpy as np
from deepface import DeepFace
import socket
import time
import urllib.request
import os

# --- Socket setup ---
import threading

print("[INIT] Setting up socket connection...")
soc = socket.socket()
soc.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
hostname = "localhost"
port = 5000
soc.bind((hostname, port))
soc.listen(5)
conn = None
conn_lock = threading.Lock()

def accept_connections():
    global conn
    print(f"[SOCKET] Listening on {hostname}:{port} for C# client...")
    while True:
        try:
            new_conn, addr = soc.accept()
            with conn_lock:
                conn = new_conn
            print(f"[SOCKET] C# client connected from {addr}")
        except Exception as e:
            print(f"[SOCKET] Accept error: {e}")
            break

accept_thread = threading.Thread(target=accept_connections, daemon=True)
accept_thread.start()

old_msg = ''

# --- MediaPipe setup ---
print("[INIT] Loading MediaPipe models...")

# Download model files if not present
def download_model(url, path):
    if not os.path.exists(path):
        print(f"[INIT] Downloading {path}...")
        urllib.request.urlretrieve(url, path)
        print(f"[INIT] Downloaded {path}")

download_model(
    "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task",
    "pose_landmarker.task"
)
download_model(
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task",
    "hand_landmarker.task"
)
download_model(
    "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task",
    "face_landmarker.task"
)

pose_options = mp_vision.PoseLandmarkerOptions(
    base_options=mp_python.BaseOptions(model_asset_path="pose_landmarker.task"),
    running_mode=mp_vision.RunningMode.IMAGE,
    min_pose_detection_confidence=0.65
)
hand_options = mp_vision.HandLandmarkerOptions(
    base_options=mp_python.BaseOptions(model_asset_path="hand_landmarker.task"),
    running_mode=mp_vision.RunningMode.IMAGE,
    num_hands=2,
    min_hand_detection_confidence=0.65
)
face_options = mp_vision.FaceLandmarkerOptions(
    base_options=mp_python.BaseOptions(model_asset_path="face_landmarker.task"),
    running_mode=mp_vision.RunningMode.IMAGE,
    min_face_detection_confidence=0.65
)

pose_landmarker = mp_vision.PoseLandmarker.create_from_options(pose_options)
hand_landmarker = mp_vision.HandLandmarker.create_from_options(hand_options)
face_landmarker = mp_vision.FaceLandmarker.create_from_options(face_options)

print("[INIT] MediaPipe models loaded.")

def draw_landmarks_cv(image, landmarks, connections, color=(0, 255, 0), radius=2):
    """Draw landmarks and connections using plain OpenCV."""
    h, w = image.shape[:2]
    pts = [(int(lm.x * w), int(lm.y * h)) for lm in landmarks]
    if connections:
        for a, b in connections:
            if a < len(pts) and b < len(pts):
                cv2.line(image, pts[a], pts[b], color, 1)
    for pt in pts:
        cv2.circle(image, pt, radius, color, -1)

# Connection sets
POSE_CONNECTIONS = [
    (0,1),(1,2),(2,3),(3,7),(0,4),(4,5),(5,6),(6,8),
    (9,10),(11,12),(11,13),(13,15),(15,17),(15,19),(15,21),(17,19),
    (12,14),(14,16),(16,18),(16,20),(16,22),(18,20),
    (11,23),(12,24),(23,24),(23,25),(24,26),(25,27),(26,28),(27,29),(28,30),(29,31),(30,32),(27,31),(28,32)
]
HAND_CONNECTIONS = [
    (0,1),(1,2),(2,3),(3,4),(0,5),(5,6),(6,7),(7,8),
    (5,9),(9,10),(10,11),(11,12),(9,13),(13,14),(14,15),(15,16),
    (13,17),(17,18),(18,19),(19,20),(0,17)
]

face_ids = {}
frame_count = 0
show_camera = False  # Set to True to show the raw camera window

# --- Camera setup ---
cam_url = "http://192.168.1.100:8080/video"
print(f"[INIT] Opening camera stream: {cam_url}")
cap = cv2.VideoCapture(cam_url)

if not cap.isOpened():
    print("[ERROR] Failed to open camera stream. Check the URL or connection.")
else:
    print("[INIT] Camera stream opened successfully.")

print("[LOOP] Starting main loop. Press 'q' to quit.")

while cap.isOpened():
    ret, frame = cap.read()
    if not ret or frame is None or frame.size == 0:
        print("[WARNING] Failed to read frame from camera. Skipping...")
        cv2.waitKey(1)
        continue

    msg = ''
    try:
        # Show raw camera feed before any processing
        if show_camera:
            cv2.imshow("Raw Camera", frame)

        frame = cv2.rotate(frame, cv2.ROTATE_90_CLOCKWISE)
        f_frame = cv2.resize(frame, (480, 640))

        # Skip blank/black frames
        if cv2.mean(f_frame)[0] < 5:
            print("[WARNING] Frame appears blank, skipping processing.")
            cv2.waitKey(1)
            continue

        frame_rgb = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
        frame_count += 1

        if frame_count % 60 == 0:
            print(f"[FACE] Running face recognition on frame {frame_count}...")
            face_encodings = DeepFace.represent(
                f_frame,
                model_name="Facenet",
                enforce_detection=False
            )
            print(f"[FACE] Detected {len(face_encodings)} face(s).")

            if len(face_encodings) > 0:
                # Filter out false positives: facial area must be at least 5% of frame
                fa = face_encodings[0].get("facial_area", {})
                fa_w = fa.get("w", 0)
                fa_h = fa.get("h", 0)
                frame_area = f_frame.shape[0] * f_frame.shape[1]
                face_area = fa_w * fa_h
                if face_area < frame_area * 0.05:
                    print(f"[FACE] Skipping — detected region too small ({fa_w}x{fa_h}), likely a false positive.")
                else:
                    face_id = tuple(face_encodings[0]["embedding"])
                    match = None
                    for k in face_ids:
                        dist = np.linalg.norm(np.array(face_id) - np.array(k))
                        if dist < 15:
                            match = face_ids[k]
                            msg = "face:detected:" + match
                            print(f"[FACE] Match found: {match} (distance: {dist:.2f})")
                            break
                    if match is None:
                        new_name = "Person " + str(len(face_ids) + 1)
                        face_ids[face_id] = new_name
                        msg = "face:detected:" + new_name
                        print(f"[FACE] New face registered as: {new_name}")
                    else:
                        print(f"[FACE] Known face recognized: {match}")


        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=frame_rgb)
        pose_result = pose_landmarker.detect(mp_image)
        hand_result = hand_landmarker.detect(mp_image)
        face_result = face_landmarker.detect(mp_image)

        has_face = len(face_result.face_landmarks) > 0
        has_pose = len(pose_result.pose_landmarks) > 0
        has_hands = len(hand_result.hand_landmarks) > 0
        if frame_count % 30 == 0:
            print(f"[HOLISTIC] face={has_face}, pose={has_pose}, hands={has_hands}")

        annotated_image = f_frame.copy()

        # Draw face landmarks (just dots, no connections for perf)
        h, w = annotated_image.shape[:2]
        for face_lms in face_result.face_landmarks:
            for lm in face_lms:
                cv2.circle(annotated_image, (int(lm.x * w), int(lm.y * h)), 1, (0, 200, 255), -1)

        # Draw pose landmarks
        for pose_lms in pose_result.pose_landmarks:
            draw_landmarks_cv(annotated_image, pose_lms, POSE_CONNECTIONS, color=(0, 255, 0))

        # Draw hand landmarks
        for hand_lms in hand_result.hand_landmarks:
            draw_landmarks_cv(annotated_image, hand_lms, HAND_CONNECTIONS, color=(255, 0, 128))

        for face_id_key, name in face_ids.items():
            cv2.putText(annotated_image, name, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)

        cv2.imshow("Output", annotated_image)

        # Send message to Unity
        if msg != '' and msg != old_msg:
            print(f"[SOCKET] Sending to Unity: '{msg}'")
            with conn_lock:
                if conn:
                    try:
                        conn.send(msg.encode('utf-8'))
                    except Exception as e:
                        print(f"[SOCKET] Send failed: {e}")
                        conn = None
                else:
                    print("[SOCKET] No C# client connected yet.")
        old_msg = msg

    except Exception as e:
        print(f"[ERROR] Exception on frame {frame_count}: {e}")

    if cv2.waitKey(1) == ord("q"):
        print("[LOOP] 'q' pressed. Exiting...")
        break

print("[SHUTDOWN] Releasing camera and closing windows.")
cap.release()
cv2.destroyAllWindows()
