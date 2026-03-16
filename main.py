import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision as mp_vision
import numpy as np
import os
import socket
import time
import urllib.request
import json
import threading
import queue
from collections import deque

from facenet_pytorch import MTCNN, InceptionResnetV1
import torch
from PIL import Image

# ── Socket ────────────────────────────────────────────────────────────────────
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


threading.Thread(target=accept_connections, daemon=True).start()
old_msg = ""

# ── MediaPipe ─────────────────────────────────────────────────────────────────
print("[INIT] Loading MediaPipe models...")


def download_model(url, path):
    if not os.path.exists(path):
        print(f"[INIT] Downloading {path}...")
        urllib.request.urlretrieve(url, path)


download_model(
    "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task",
    "pose_landmarker.task",
)
download_model(
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task",
    "hand_landmarker.task",
)
download_model(
    "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task",
    "face_landmarker.task",
)

pose_landmarker = mp_vision.PoseLandmarker.create_from_options(
    mp_vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path="pose_landmarker.task"),
        running_mode=mp_vision.RunningMode.IMAGE,
        min_pose_detection_confidence=0.65,
    )
)
hand_landmarker = mp_vision.HandLandmarker.create_from_options(
    mp_vision.HandLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path="hand_landmarker.task"),
        running_mode=mp_vision.RunningMode.IMAGE,
        num_hands=2,
        min_hand_detection_confidence=0.65,
    )
)
face_landmarker = mp_vision.FaceLandmarker.create_from_options(
    mp_vision.FaceLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path="face_landmarker.task"),
        running_mode=mp_vision.RunningMode.IMAGE,
        min_face_detection_confidence=0.65,
    )
)

# ── FaceNet ───────────────────────────────────────────────────────────────────
print("[INIT] Loading FaceNet (MTCNN + InceptionResnetV1)...")
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
print(f"[INIT] Using device: {device}")

mtcnn = MTCNN(
    image_size=160,
    margin=20,
    min_face_size=40,
    thresholds=[0.6, 0.7, 0.7],
    factor=0.709,
    keep_all=False,
    device=device,
)
resnet = InceptionResnetV1(pretrained="vggface2").eval().to(device)
print("[INIT] FaceNet ready.")

# ── Drawing helpers ───────────────────────────────────────────────────────────
POSE_CONNECTIONS = [
    (0, 1),
    (1, 2),
    (2, 3),
    (3, 7),
    (0, 4),
    (4, 5),
    (5, 6),
    (6, 8),
    (9, 10),
    (11, 12),
    (11, 13),
    (13, 15),
    (15, 17),
    (15, 19),
    (15, 21),
    (17, 19),
    (12, 14),
    (14, 16),
    (16, 18),
    (16, 20),
    (16, 22),
    (18, 20),
    (11, 23),
    (12, 24),
    (23, 24),
    (23, 25),
    (24, 26),
    (25, 27),
    (26, 28),
    (27, 29),
    (28, 30),
    (29, 31),
    (30, 32),
    (27, 31),
    (28, 32),
]
HAND_CONNECTIONS = [
    (0, 1),
    (1, 2),
    (2, 3),
    (3, 4),
    (0, 5),
    (5, 6),
    (6, 7),
    (7, 8),
    (5, 9),
    (9, 10),
    (10, 11),
    (11, 12),
    (9, 13),
    (13, 14),
    (14, 15),
    (15, 16),
    (13, 17),
    (17, 18),
    (18, 19),
    (19, 20),
    (0, 17),
]


def draw_landmarks_cv(image, landmarks, connections, color=(0, 255, 0), radius=2):
    h, w = image.shape[:2]
    pts = [(int(lm.x * w), int(lm.y * h)) for lm in landmarks]
    if connections:
        for a, b in connections:
            if a < len(pts) and b < len(pts):
                cv2.line(image, pts[a], pts[b], color, 1)
    for pt in pts:
        cv2.circle(image, pt, radius, color, -1)


# ── Face persistence ──────────────────────────────────────────────────────────
FACES_FILE = "faces/users.json"
os.makedirs("faces", exist_ok=True)

# Each person stores up to MAX_SAMPLES raw embeddings + a mean embedding for fast matching.
# mean_encoding_fn  — averaged & re-normalized embedding used for fast lookup
# encoding_fn       — list of up to MAX_SAMPLES individual embeddings for robustness
MAX_SAMPLES_PER_PERSON = 8  # collect up to 8 embeddings per person
MIN_SAMPLES_FOR_MEAN = 3  # start using mean only after 3 samples


def load_faces_from_disk():
    """Returns (names, mean_encodings, all_sample_encodings)."""
    names, means, samples = [], [], []
    if not os.path.exists(FACES_FILE):
        return names, means, samples
    try:
        with open(FACES_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
        for entry in data:
            if "mean_encoding_fn" not in entry and "encoding_fn" not in entry:
                continue
            names.append(entry["name"])
            # Load per-person sample list
            raw = entry.get("encoding_fn", [])
            if raw and isinstance(raw[0], list):
                person_samples = [np.array(e, dtype=np.float32) for e in raw]
            elif raw:
                person_samples = [np.array(raw, dtype=np.float32)]
            else:
                person_samples = []
            samples.append(person_samples)
            # Load or compute mean
            if "mean_encoding_fn" in entry:
                means.append(np.array(entry["mean_encoding_fn"], dtype=np.float32))
            elif person_samples:
                m = np.mean(person_samples, axis=0)
                m /= np.linalg.norm(m)
                means.append(m)
            else:
                means.append(None)
        print(f"[FACES] Loaded {len(names)} user(s) from {FACES_FILE}")
    except Exception as e:
        print(f"[FACES] Failed to load {FACES_FILE}: {e}")
    return names, means, samples


def save_faces_to_disk(names, means, samples):
    existing = []
    if os.path.exists(FACES_FILE):
        try:
            with open(FACES_FILE, "r", encoding="utf-8") as f:
                existing = json.load(f)
        except Exception:
            existing = []
    entry_map = {e["name"]: e for e in existing}
    for name, mean_enc, person_samples in zip(names, means, samples):
        if name not in entry_map:
            entry_map[name] = {"name": name}
        # Save all individual samples as list-of-lists
        entry_map[name]["encoding_fn"] = [e.tolist() for e in person_samples]
        if mean_enc is not None:
            entry_map[name]["mean_encoding_fn"] = mean_enc.tolist()
    try:
        with open(FACES_FILE, "w", encoding="utf-8") as f:
            json.dump(list(entry_map.values()), f, indent=2)
        print(f"[FACES] Saved {len(entry_map)} user(s) to {FACES_FILE}")
    except Exception as e:
        print(f"[FACES] Failed to save {FACES_FILE}: {e}")


def get_face_embedding(rgb_frame):
    """MTCNN detect + FaceNet embed. Returns L2-normalized 512-d numpy array or None."""
    pil_img = Image.fromarray(rgb_frame)
    face_tensor = mtcnn(pil_img)
    if face_tensor is None:
        return None
    with torch.no_grad():
        emb = resnet(face_tensor.unsqueeze(0).to(device))
    emb = emb / emb.norm(dim=1, keepdim=True)
    return emb.squeeze(0).cpu().numpy()


def build_matrix(means):
    """Stack mean embeddings into (N, 512) matrix for vectorized cosine similarity."""
    valid = [(i, m) for i, m in enumerate(means) if m is not None]
    if not valid:
        return None, []
    idxs = [i for i, _ in valid]
    mat = np.stack([m for _, m in valid], axis=0).astype(np.float32)
    return mat, idxs


known_names, known_means, known_samples = load_faces_from_disk()
next_person_id = len(known_names) + 1
known_matrix, known_matrix_idxs = build_matrix(known_means)

# Recognition threshold — cosine similarity (dot product of L2-normalized vectors).
# 0.72 is a solid balance between strictness and recall for FaceNet VGGFace2.
RECOGNITION_THRESHOLD = 0.72

# ── Smoothing: rolling vote over last N recognition results ───────────────────
VOTE_WINDOW = 5  # frames to smooth over
recent_names: deque = deque(maxlen=VOTE_WINDOW)


def smoothed_name():
    if not recent_names:
        return ""
    counts: dict = {}
    for n in recent_names:
        counts[n] = counts.get(n, 0) + 1
    return max(counts, key=counts.get)


# ── Background face recognition thread ───────────────────────────────────────
# The main loop drops frames into a queue; the worker thread does MTCNN+FaceNet
# without blocking the display/MediaPipe pipeline.
face_queue = queue.Queue(maxsize=1)  # only keep the latest frame
face_result_lock = threading.Lock()
face_result = {"name": "", "sim": 0.0}  # shared result written by worker


def face_worker():
    global known_names, known_means, known_samples, known_matrix, known_matrix_idxs, next_person_id
    while True:
        rgb = face_queue.get()
        if rgb is None:
            break
        try:
            enc = get_face_embedding(rgb)
            if enc is None:
                with face_result_lock:
                    face_result["name"] = ""
                    face_result["sim"] = 0.0
                continue

            match_name = None
            best_sim = 0.0

            if known_matrix is not None and len(known_matrix_idxs) > 0:
                # Vectorized cosine similarity against all mean embeddings at once
                sims = known_matrix @ enc  # shape (N,)
                best_local = int(np.argmax(sims))
                best_sim = float(sims[best_local])
                if best_sim >= RECOGNITION_THRESHOLD:
                    match_name = known_names[known_matrix_idxs[best_local]]
                    person_idx = known_matrix_idxs[best_local]
                    # Add this embedding as a new sample if we haven't hit the cap
                    if len(known_samples[person_idx]) < MAX_SAMPLES_PER_PERSON:
                        known_samples[person_idx].append(enc)
                        # Recompute mean
                        m = np.mean(known_samples[person_idx], axis=0)
                        m /= np.linalg.norm(m)
                        known_means[person_idx] = m
                        known_matrix, known_matrix_idxs = build_matrix(known_means)
                        save_faces_to_disk(known_names, known_means, known_samples)
                        print(
                            f"[FACE] Updated {match_name} — now {len(known_samples[person_idx])} samples, sim={best_sim:.4f}"
                        )

            if match_name is None:
                match_name = "Person " + str(next_person_id)
                next_person_id += 1
                known_names.append(match_name)
                known_samples.append([enc])
                known_means.append(enc.copy())
                known_matrix, known_matrix_idxs = build_matrix(known_means)
                save_faces_to_disk(known_names, known_means, known_samples)
                print(f"[FACE] New face registered as: {match_name}")

            with face_result_lock:
                face_result["name"] = match_name
                face_result["sim"] = best_sim
            print(f"[FACE] Result: {match_name} (sim={best_sim:.4f})")

        except Exception as e:
            print(f"[FACE WORKER] Error: {e}")


threading.Thread(target=face_worker, daemon=True).start()

# ── Camera setup ──────────────────────────────────────────────────────────────
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
        print(f"[INIT] Failed to read camera config: {e}. Using defaults.")
    return default_cfg


camera_cfg = load_camera_config(camera_config_path)
camera_mode = str(camera_cfg.get("camera_mode", "local_webcam")).strip().lower()
phone_ip_url = str(
    camera_cfg.get("phone_ip_url", "http://192.168.1.18:8080/video")
).strip()
local_webcam_index = int(camera_cfg.get("local_webcam_index", 0))

camera_disabled_cfg = camera_cfg.get("camera_disabled", False)
if isinstance(camera_disabled_cfg, str):
    camera_disabled_cfg = camera_disabled_cfg.strip().lower() in (
        "1",
        "true",
        "yes",
        "on",
    )
camera_disabled_env = os.getenv("CAMERA_DISABLED", "").strip().lower()
camera_disabled = bool(camera_disabled_cfg) or camera_disabled_env in (
    "1",
    "true",
    "yes",
    "on",
)

primary_source = phone_ip_url if camera_mode == "phone_ip" else local_webcam_index
primary_source_name = (
    f"IP camera ({primary_source})"
    if camera_mode == "phone_ip"
    else f"local webcam (index {primary_source})"
)

camera_source_env = os.getenv("CAMERA_SOURCE", "").strip()
if camera_source_env:
    primary_source = (
        int(camera_source_env) if camera_source_env.isdigit() else camera_source_env
    )
    primary_source_name = f"{'local webcam' if isinstance(primary_source, int) else 'IP camera'} ({primary_source}) via env"

cap = None
read_fail_streak = 0
max_read_fail_before_reconnect = 15
warning_every_n_failures = 5
reconnect_cycle_count = 0
max_reconnect_cycles = 8
blank_frame_streak = 0
max_blank_frames_before_reconnect = 45
blank_detection_warmup_frames = 20
frames_since_reconnect = 0
camera_unavailable_mode = False
camera_retry_interval_sec = 3.0
last_camera_retry_time = 0.0
sent_face_lost_when_camera_unavailable = False
preferred_webcam_backend = getattr(cv2, "CAP_MSMF", None)

camera_backend_env = os.getenv("CAMERA_BACKEND", "").strip().lower()
if camera_backend_env == "msmf" and hasattr(cv2, "CAP_MSMF"):
    preferred_webcam_backend = cv2.CAP_MSMF
elif camera_backend_env == "dshow" and hasattr(cv2, "CAP_DSHOW"):
    preferred_webcam_backend = cv2.CAP_DSHOW
elif camera_backend_env == "default":
    preferred_webcam_backend = None


def backend_to_name(b):
    if b == getattr(cv2, "CAP_DSHOW", object()):
        return "CAP_DSHOW"
    if b == getattr(cv2, "CAP_MSMF", object()):
        return "CAP_MSMF"
    return "default"


def choose_backend_candidates(source, preferred_backend=None):
    if not isinstance(source, int):
        return [None]
    seen, out = set(), []
    for c in [
        preferred_backend,
        getattr(cv2, "CAP_MSMF", None),
        getattr(cv2, "CAP_DSHOW", None),
        None,
    ]:
        if c not in seen:
            seen.add(c)
            out.append(c)
    return out


def frame_is_effectively_blank(frame):
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    return (
        float(np.mean(gray)) < 8.0
        and float(np.std(gray)) < 4.0
        and int(np.max(gray)) < 28
    )


def open_capture_with_retry(
    source, source_name, retries=3, delay_sec=1.0, preferred_backend=None
):
    for attempt in range(1, retries + 1):
        for backend in choose_backend_candidates(source, preferred_backend):
            print(
                f"[INIT] Attempt {attempt}/{retries} opening {source_name} via {backend_to_name(backend)}..."
            )
            cap_obj = (
                cv2.VideoCapture(source)
                if backend is None
                else cv2.VideoCapture(source, backend)
            )
            if cap_obj.isOpened():
                for _ in range(10):
                    ret, frm = cap_obj.read()
                    if ret and frm is not None and frm.size > 0:
                        print(f"[INIT] Connected to {source_name}.")
                        return cap_obj
                    time.sleep(0.05)
            cap_obj.release()
        time.sleep(delay_sec)
    return None


def reconnect_camera(reason="generic"):
    global cap, preferred_webcam_backend, frames_since_reconnect
    if cap is not None:
        cap.release()
    if reason == "blank" and isinstance(primary_source, int):
        if preferred_webcam_backend == getattr(cv2, "CAP_DSHOW", object()) and hasattr(
            cv2, "CAP_MSMF"
        ):
            preferred_webcam_backend = cv2.CAP_MSMF
        elif preferred_webcam_backend == getattr(cv2, "CAP_MSMF", object()) and hasattr(
            cv2, "CAP_DSHOW"
        ):
            preferred_webcam_backend = cv2.CAP_DSHOW
    cap = open_capture_with_retry(
        primary_source,
        primary_source_name,
        retries=2,
        delay_sec=0.5,
        preferred_backend=preferred_webcam_backend,
    )
    if cap is None and primary_source != 0:
        cap = open_capture_with_retry(
            0,
            "local webcam (index 0)",
            retries=2,
            delay_sec=0.5,
            preferred_backend=preferred_webcam_backend,
        )
    if cap is not None:
        frames_since_reconnect = 0
    return cap is not None


print(f"[INIT] Opening camera source: {primary_source_name}")
if camera_disabled:
    print("[INIT] CAMERA_DISABLED is enabled. Running in socket-only mode.")
    camera_unavailable_mode = True
else:
    cap = open_capture_with_retry(
        primary_source,
        primary_source_name,
        retries=3,
        delay_sec=1.0,
        preferred_backend=preferred_webcam_backend,
    )
    if cap is None and primary_source != 0:
        print(
            "[WARNING] Primary camera unavailable. Falling back to local webcam (index 0)."
        )
        cap = open_capture_with_retry(
            0,
            "local webcam (index 0)",
            retries=2,
            delay_sec=0.5,
            preferred_backend=preferred_webcam_backend,
        )
    if cap is None:
        print("[WARNING] No camera available. Entering socket-only mode.")
        camera_unavailable_mode = True

# ── Main loop ─────────────────────────────────────────────────────────────────
frame_count = 0
last_face_seen_frame = -1
face_lost_sent = False
current_detected_name = ""
show_camera = False
FACE_RECOGNITION_EVERY = 15  # run recognition every N frames (was 30)

print("[LOOP] Starting main loop. Press 'q' to quit.")

while True:
    # ── Camera unavailable mode ──
    if camera_unavailable_mode:
        now = time.time()
        if not sent_face_lost_when_camera_unavailable:
            with conn_lock:
                if conn:
                    try:
                        conn.send("face:lost".encode("utf-8"))
                    except Exception:
                        conn = None
            sent_face_lost_when_camera_unavailable = True
        if camera_disabled:
            time.sleep(0.1)
            continue
        if now - last_camera_retry_time >= camera_retry_interval_sec:
            last_camera_retry_time = now
            if reconnect_camera():
                camera_unavailable_mode = False
                sent_face_lost_when_camera_unavailable = False
                reconnect_cycle_count = read_fail_streak = blank_frame_streak = 0
            else:
                time.sleep(0.2)
                continue
        time.sleep(0.1)
        continue

    if cap is None or not cap.isOpened():
        if not reconnect_camera():
            camera_unavailable_mode = True
            continue

    ret, frame = cap.read()
    if not ret or frame is None or frame.size == 0:
        read_fail_streak += 1
        if read_fail_streak % warning_every_n_failures == 0:
            print(
                f"[WARNING] Failed to read frame ({read_fail_streak} consecutive failures)."
            )
        if read_fail_streak >= max_read_fail_before_reconnect:
            reconnect_cycle_count += 1
            if reconnect_cycle_count > max_reconnect_cycles:
                camera_unavailable_mode = True
                continue
            if not reconnect_camera():
                camera_unavailable_mode = True
                continue
            read_fail_streak = 0
        cv2.waitKey(1)
        continue
    elif read_fail_streak > 0:
        read_fail_streak = 0
        reconnect_cycle_count = 0

    frames_since_reconnect += 1
    msg = ""

    try:
        if show_camera:
            cv2.imshow("Raw Camera", frame)

        frame = cv2.rotate(frame, cv2.ROTATE_90_CLOCKWISE)
        f_frame = cv2.resize(frame, (480, 640))

        if frame_is_effectively_blank(f_frame):
            blank_frame_streak += 1
            if frames_since_reconnect <= blank_detection_warmup_frames:
                cv2.waitKey(1)
                continue
            if blank_frame_streak >= max_blank_frames_before_reconnect:
                reconnect_cycle_count += 1
                if reconnect_cycle_count > max_reconnect_cycles:
                    camera_unavailable_mode = True
                    continue
                if not reconnect_camera(reason="blank"):
                    camera_unavailable_mode = True
                    continue
                blank_frame_streak = 0
            cv2.waitKey(1)
            continue
        elif blank_frame_streak > 0:
            blank_frame_streak = 0
            reconnect_cycle_count = 0

        frame_rgb = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
        frame_count += 1

        # ── Submit frame to background face worker every N frames ──
        if frame_count % FACE_RECOGNITION_EVERY == 0:
            if not face_queue.full():
                face_queue.put_nowait(frame_rgb.copy())

        # ── Read latest result from worker ──
        with face_result_lock:
            worker_name = face_result["name"]
            worker_sim = face_result["sim"]

        if worker_name:
            recent_names.append(worker_name)
            last_face_seen_frame = frame_count
            face_lost_sent = False
        else:
            if (
                not face_lost_sent
                and last_face_seen_frame >= 0
                and (frame_count - last_face_seen_frame) >= 30
            ):
                recent_names.clear()
                face_lost_sent = True
                msg = "face:lost"
                print("[FACE] No face detected for a while. Sending face:lost.")

        current_detected_name = smoothed_name()
        if current_detected_name and not face_lost_sent:
            msg = "face:detected:" + current_detected_name

        # ── MediaPipe landmarks ──
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=frame_rgb)
        pose_result = pose_landmarker.detect(mp_image)
        hand_result = hand_landmarker.detect(mp_image)
        face_result_mp = face_landmarker.detect(mp_image)

        if frame_count % FACE_RECOGNITION_EVERY == 0:
            print(
                f"[HOLISTIC] face={len(face_result_mp.face_landmarks)>0}, "
                f"pose={len(pose_result.pose_landmarks)>0}, "
                f"hands={len(hand_result.hand_landmarks)>0}"
            )

        annotated = f_frame.copy()
        h, w = annotated.shape[:2]

        for face_lms in face_result_mp.face_landmarks:
            for lm in face_lms:
                cv2.circle(
                    annotated, (int(lm.x * w), int(lm.y * h)), 1, (0, 200, 255), -1
                )
        for pose_lms in pose_result.pose_landmarks:
            draw_landmarks_cv(annotated, pose_lms, POSE_CONNECTIONS, color=(0, 255, 0))
        for hand_lms in hand_result.hand_landmarks:
            draw_landmarks_cv(
                annotated, hand_lms, HAND_CONNECTIONS, color=(255, 0, 128)
            )

        if current_detected_name:
            label = f"{current_detected_name} ({worker_sim:.2f})"
            cv2.putText(
                annotated,
                label,
                (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                (0, 255, 0),
                2,
            )

        cv2.imshow("Output", annotated)

        if msg != "" and msg != old_msg:
            print(f"[SOCKET] Sending: '{msg}'")
            with conn_lock:
                if conn:
                    try:
                        conn.send(msg.encode("utf-8"))
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

face_queue.put(None)  # stop worker thread
print("[SHUTDOWN] Releasing camera and closing windows.")
if cap is not None:
    cap.release()
cv2.destroyAllWindows()
