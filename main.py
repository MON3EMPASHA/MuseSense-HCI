import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision as mp_vision
import numpy as np
import os

# Keep TensorFlow oneDNN optimizations deterministic/noisy-log free unless the user sets it explicitly.
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")

from deepface import DeepFace
import socket
import time
import urllib.request
import json

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
camera_config_path = "camera_config.json"

def load_camera_config(path):
    default_cfg = {
        "camera_mode": "local_webcam",
        "phone_ip_url": "http://192.168.1.18:8080/video",
        "local_webcam_index": 0,
    }
    try:
        with open(path, "r", encoding="utf-8") as f:
            user_cfg = json.load(f)
        if isinstance(user_cfg, dict):
            default_cfg.update(user_cfg)
    except FileNotFoundError:
        print(f"[INIT] Camera config not found at {path}. Using defaults.")
    except Exception as e:
        print(f"[INIT] Failed to read camera config at {path}: {e}. Using defaults.")
    return default_cfg

camera_cfg = load_camera_config(camera_config_path)
camera_mode = str(camera_cfg.get("camera_mode", "local_webcam")).strip().lower()
phone_ip_url = str(camera_cfg.get("phone_ip_url", "http://192.168.1.18:8080/video")).strip()
local_webcam_index = int(camera_cfg.get("local_webcam_index", 0))

camera_disabled_cfg = camera_cfg.get("camera_disabled", False)
if isinstance(camera_disabled_cfg, str):
    camera_disabled_cfg = camera_disabled_cfg.strip().lower() in ("1", "true", "yes", "on")
else:
    camera_disabled_cfg = bool(camera_disabled_cfg)

# Optional hard override from terminal: CAMERA_DISABLED=1
camera_disabled_env = os.getenv("CAMERA_DISABLED", "").strip().lower()
camera_disabled = camera_disabled_cfg or camera_disabled_env in ("1", "true", "yes", "on")

if camera_mode == "phone_ip":
    primary_source = phone_ip_url
    primary_source_name = f"IP camera ({primary_source})"
else:
    primary_source = local_webcam_index
    primary_source_name = f"local webcam (index {primary_source})"

# Optional override for quick testing from terminal, e.g. CAMERA_SOURCE=0 or CAMERA_SOURCE=http://.../video
camera_source_env = os.getenv("CAMERA_SOURCE", "").strip()
if camera_source_env:
    if camera_source_env.isdigit():
        primary_source = int(camera_source_env)
        primary_source_name = f"local webcam (index {primary_source}) via env"
    else:
        primary_source = camera_source_env
        primary_source_name = f"IP camera ({primary_source}) via env"

print(f"[INIT] Opening camera source: {primary_source_name}")
cap = None
read_fail_streak = 0
max_read_fail_before_reconnect = 15
warning_every_n_failures = 5
reconnect_cycle_count = 0
max_reconnect_cycles = 8
blank_frame_streak = 0
max_blank_frames_before_reconnect = 45
warning_every_n_blank_frames = 10
blank_detection_warmup_frames = 20
frames_since_reconnect = 0
camera_unavailable_mode = False
camera_retry_interval_sec = 3.0
last_camera_retry_time = 0.0
sent_face_lost_when_camera_unavailable = False

# Alternate webcam backends on repeated blank-frame reconnects.
preferred_webcam_backend = getattr(cv2, "CAP_MSMF", None) if hasattr(cv2, "CAP_MSMF") else None

# Optional override: CAMERA_BACKEND=msmf|dshow|default
camera_backend_env = os.getenv("CAMERA_BACKEND", "").strip().lower()
if camera_backend_env == "msmf" and hasattr(cv2, "CAP_MSMF"):
    preferred_webcam_backend = cv2.CAP_MSMF
elif camera_backend_env == "dshow" and hasattr(cv2, "CAP_DSHOW"):
    preferred_webcam_backend = cv2.CAP_DSHOW
elif camera_backend_env == "default":
    preferred_webcam_backend = None

def backend_to_name(backend):
    if backend == getattr(cv2, "CAP_DSHOW", object()):
        return "CAP_DSHOW"
    if backend == getattr(cv2, "CAP_MSMF", object()):
        return "CAP_MSMF"
    return "default"

def choose_backend_candidates(source, preferred_backend=None):
    if not isinstance(source, int):
        return [None]

    # Prefer MSMF first on Windows because it is often less exclusive than DirectShow.
    candidates = []
    for candidate in [preferred_backend, getattr(cv2, "CAP_MSMF", None), getattr(cv2, "CAP_DSHOW", None), None]:
        if candidate not in candidates:
            candidates.append(candidate)
    return candidates

def frame_is_effectively_blank(frame):
    """Detect near-black / near-uniform frames while avoiding false positives in dim scenes."""
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    mean_luma = float(np.mean(gray))
    std_luma = float(np.std(gray))
    max_luma = int(np.max(gray))
    return mean_luma < 8.0 and std_luma < 4.0 and max_luma < 28

def open_capture_with_retry(source, source_name, retries=3, delay_sec=1.0, preferred_backend=None):
    """Try to open a capture source multiple times before giving up.

    For local webcams on Windows, try common backends and require one valid frame
    so we do not treat an unusable capture handle as a successful open.
    """
    backend_candidates = choose_backend_candidates(source, preferred_backend=preferred_backend)

    def open_and_probe(backend):
        cap_obj = cv2.VideoCapture(source) if backend is None else cv2.VideoCapture(source, backend)
        if not cap_obj.isOpened():
            cap_obj.release()
            return None

        # Warm up and verify at least one real frame is readable.
        for _ in range(10):
            ret, frame = cap_obj.read()
            if ret and frame is not None and frame.size > 0:
                return cap_obj
            time.sleep(0.05)

        cap_obj.release()
        return None

    for attempt in range(1, retries + 1):
        for backend in backend_candidates:
            backend_name = backend_to_name(backend)

            print(f"[INIT] Attempt {attempt}/{retries} opening {source_name} via {backend_name}...")
            c = open_and_probe(backend)
            if c is not None:
                print(f"[INIT] Connected to {source_name} via {backend_name}.")
                return c
        time.sleep(delay_sec)
    return None

def reconnect_camera(reason="generic"):
    global cap, preferred_webcam_backend, frames_since_reconnect
    if cap is not None:
        cap.release()

    backend_for_attempt = preferred_webcam_backend
    if reason == "blank" and isinstance(primary_source, int):
        if preferred_webcam_backend == getattr(cv2, "CAP_DSHOW", object()) and hasattr(cv2, "CAP_MSMF"):
            preferred_webcam_backend = cv2.CAP_MSMF
        elif preferred_webcam_backend == getattr(cv2, "CAP_MSMF", object()) and hasattr(cv2, "CAP_DSHOW"):
            preferred_webcam_backend = cv2.CAP_DSHOW
        backend_for_attempt = preferred_webcam_backend
        print(f"[CAMERA] Blank-frame recovery: switching preferred backend to {backend_to_name(backend_for_attempt)}")

    print(f"[CAMERA] Reconnecting primary source: {primary_source_name}")
    cap = open_capture_with_retry(
        primary_source,
        primary_source_name,
        retries=2,
        delay_sec=0.5,
        preferred_backend=backend_for_attempt
    )
    if cap is None and primary_source != 0:
        print("[CAMERA] Primary reconnect failed. Trying local webcam fallback (index 0).")
        cap = open_capture_with_retry(
            0,
            "local webcam (index 0)",
            retries=2,
            delay_sec=0.5,
            preferred_backend=backend_for_attempt
        )
    if cap is not None:
        frames_since_reconnect = 0
    return cap is not None

# Prefer selected source first, then fallback to local webcam.
if camera_disabled:
    print("[INIT] CAMERA_DISABLED is enabled. Running in TUIO/socket-only mode.")
    print("[INIT] Python will not open or retry any camera device.")
    camera_unavailable_mode = True
else:
    cap = open_capture_with_retry(
        primary_source,
        primary_source_name,
        retries=3,
        delay_sec=1.0,
        preferred_backend=preferred_webcam_backend
    )
    if cap is None and primary_source != 0:
        print("[WARNING] IP camera is unavailable. Falling back to local webcam (index 0).")
        cap = open_capture_with_retry(
            0,
            "local webcam (index 0)",
            retries=2,
            delay_sec=0.5,
            preferred_backend=preferred_webcam_backend
        )

    if cap is None:
        print("[WARNING] Failed to open both IP camera and local webcam.")
        print("[WARNING] Entering socket-only mode so C# + TUIO can still run.")
        camera_unavailable_mode = True

print("[LOOP] Starting main loop. Press 'q' to quit.")

while True:
    if camera_unavailable_mode:
        now = time.time()

        if not sent_face_lost_when_camera_unavailable:
            lost_msg = "face:lost"
            print("[SOCKET] Camera unavailable. Sending fallback status: 'face:lost'")
            with conn_lock:
                if conn:
                    try:
                        conn.send(lost_msg.encode('utf-8'))
                    except Exception as e:
                        print(f"[SOCKET] Send failed: {e}")
                        conn = None
            sent_face_lost_when_camera_unavailable = True

        if camera_disabled:
            time.sleep(0.1)
            continue

        if now - last_camera_retry_time >= camera_retry_interval_sec:
            last_camera_retry_time = now
            print("[CAMERA] Socket-only mode: retrying camera connection...")
            if reconnect_camera(reason="generic"):
                camera_unavailable_mode = False
                sent_face_lost_when_camera_unavailable = False
                reconnect_cycle_count = 0
                read_fail_streak = 0
                blank_frame_streak = 0
                print("[CAMERA] Camera recovered. Resuming vision pipeline.")
            else:
                time.sleep(0.2)
                continue

        time.sleep(0.1)
        continue

    if cap is None or not cap.isOpened():
        if not reconnect_camera():
            print("[WARNING] Camera is unavailable after reconnect attempts.")
            print("[WARNING] Switching to socket-only mode.")
            camera_unavailable_mode = True
            continue

    ret, frame = cap.read()
    if not ret or frame is None or frame.size == 0:
        read_fail_streak += 1
        if read_fail_streak % warning_every_n_failures == 0:
            print(f"[WARNING] Failed to read frame from camera ({read_fail_streak} consecutive failures).")
        if read_fail_streak >= max_read_fail_before_reconnect:
            print("[CAMERA] Too many read failures. Attempting reconnect...")
            reconnect_cycle_count += 1
            if reconnect_cycle_count > max_reconnect_cycles:
                print("[ERROR] Camera keeps opening but cannot deliver stable frames.")
                print("[WARNING] Switching to socket-only mode so C# + TUIO can continue.")
                camera_unavailable_mode = True
                continue
            if not reconnect_camera():
                print("[WARNING] Reconnect failed after repeated frame read failures.")
                print("[WARNING] Switching to socket-only mode.")
                camera_unavailable_mode = True
                continue
            read_fail_streak = 0
        cv2.waitKey(1)
        continue
    elif read_fail_streak > 0:
        print(f"[CAMERA] Frame stream recovered after {read_fail_streak} failed read(s).")
        read_fail_streak = 0
        reconnect_cycle_count = 0

    frames_since_reconnect += 1

    msg = ''
    try:
        # Show raw camera feed before any processing
        if show_camera:
            cv2.imshow("Raw Camera", frame)

        frame = cv2.rotate(frame, cv2.ROTATE_90_CLOCKWISE)
        f_frame = cv2.resize(frame, (480, 640))

        # Skip and recover from near-black / effectively blank frames.
        if frame_is_effectively_blank(f_frame):
            blank_frame_streak += 1
            if blank_frame_streak % warning_every_n_blank_frames == 0:
                print(
                    f"[WARNING] Frame appears blank ({blank_frame_streak} consecutive frames, "
                    f"{frames_since_reconnect} frames since reconnect)."
                )

            if frames_since_reconnect <= blank_detection_warmup_frames:
                cv2.waitKey(1)
                continue

            if blank_frame_streak >= max_blank_frames_before_reconnect:
                reconnect_cycle_count += 1
                print("[CAMERA] Too many blank frames. Attempting reconnect...")
                if reconnect_cycle_count > max_reconnect_cycles:
                    print("[ERROR] Webcam stream stays blank after multiple reconnects.")
                    print("[WARNING] Switching to socket-only mode so C# + TUIO can continue.")
                    camera_unavailable_mode = True
                    continue
                if not reconnect_camera(reason="blank"):
                    print("[WARNING] Reconnect failed after repeated blank frames.")
                    print("[WARNING] Switching to socket-only mode.")
                    camera_unavailable_mode = True
                    continue
                blank_frame_streak = 0

            cv2.waitKey(1)
            continue
        elif blank_frame_streak > 0:
            print(f"[CAMERA] Frame brightness recovered after {blank_frame_streak} blank frame(s).")
            blank_frame_streak = 0
            reconnect_cycle_count = 0

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
if cap is not None:
    cap.release()
cv2.destroyAllWindows()
