import cv2
import mediapipe as mp
import numpy as np
import socket
import threading
import json
import os
import time
import re
from urllib.parse import urlparse, urlunparse

face_recognition = None
RecognizerClass = None
TemplateClass = None
PointClass = None

try:
    import face_recognition
except Exception:
    face_recognition = None

try:
    from dollarpy import Point, Recognizer, Template

    RecognizerClass = Recognizer
    TemplateClass = Template
    PointClass = Point
except Exception:
    RecognizerClass = None
    TemplateClass = None
    PointClass = None

try:
    import bluetooth
except Exception:
    bluetooth = None


print("[INIT] Starting MuseSense...")


def resolve_mediapipe_solutions():
    try:
        return mp.solutions
    except Exception:
        pass

    try:
        from mediapipe.python import solutions as mp_solutions

        return mp_solutions
    except Exception:
        return None


mp_solutions = resolve_mediapipe_solutions()
if mp_solutions is None:
    print("[ERROR] Incompatible MediaPipe install: 'solutions' API not available.")
    print("[ERROR] Install a compatible build, e.g. pip install mediapipe==0.10.14")
    raise SystemExit

mp_holistic = mp_solutions.holistic
mp_drawing = mp_solutions.drawing_utils
mp_drawing_styles = mp_solutions.drawing_styles


# Socket
soc = socket.socket()
soc.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
hostname = "localhost"
port = 5000
soc.bind((hostname, port))
soc.listen(5)

conn = None
bluetooth_user_id = ""
bluetooth_sent = False


def _parse_discovered_device(entry):
    """Normalize discover_devices tuple shapes into (addr, name)."""
    if entry is None:
        return "", ""

    if isinstance(entry, tuple) or isinstance(entry, list):
        if len(entry) >= 2:
            addr = "" if entry[0] is None else str(entry[0]).strip()
            name = "" if entry[1] is None else str(entry[1]).strip()
            return addr, name
        if len(entry) == 1:
            addr = "" if entry[0] is None else str(entry[0]).strip()
            return addr, ""

    text = str(entry).strip()
    return text, ""


def _pick_best_bluetooth_device(entries):
    """Pick the most useful discovery candidate (prefer named phones)."""
    best_addr = ""
    best_name = ""
    best_score = -1

    for entry in entries:
        addr, name = _parse_discovered_device(entry)
        if addr == "":
            continue

        normalized_name = name.strip()
        has_name = normalized_name != "" and normalized_name.lower() != "unknown"
        score = 0

        # Named devices are much more useful than anonymous MAC-only hits.
        if has_name:
            score += 5

        # Prefer realistic MAC addresses.
        if addr.count(":") == 5:
            score += 2

        vendor_hint = normalized_name.lower()
        if (
            "samsung" in vendor_hint
            or "galaxy" in vendor_hint
            or "realme" in vendor_hint
            or "xiaomi" in vendor_hint
            or "iphone" in vendor_hint
            or "pixel" in vendor_hint
        ):
            score += 1

        if score > best_score:
            best_score = score
            best_addr = addr
            best_name = normalized_name

    if best_addr == "":
        return ""

    if best_name == "":
        return best_addr + "|Unknown"

    return best_addr + "|" + best_name


def accept_client():
    global conn, bluetooth_sent, old_msg
    print(f"[SOCKET] Listening on {hostname}:{port}...")
    while True:
        try:
            client, addr = soc.accept()
            conn = client
            print(f"[SOCKET] C# client connected from {addr}")
            # Re-send state after reconnect (Bluetooth + face events).
            bluetooth_sent = False
            old_msg = ""
        except Exception as e:
            print(f"[SOCKET] Accept error: {e}")
            break


def send_socket_message(msg):
    global conn
    if conn is None:
        return False

    try:
        # Newline-delimited messages make C# parsing reliable even when packets are merged.
        wire_msg = msg if msg.endswith("\n") else msg + "\n"
        conn.sendall(wire_msg.encode("utf-8"))
        return True
    except Exception as e:
        print(f"[SOCKET] Send failed: {e}")
        conn = None
        return False


threading.Thread(target=accept_client, daemon=True).start()


def capture_bluetooth_user_id():
    if bluetooth is None:
        return ""

    try:
        print("Scanning for nearby devices...")
        try:
            nearby_devices = bluetooth.discover_devices(duration=8, lookup_names=True, flush_cache=True)
        except TypeError:
            nearby_devices = bluetooth.discover_devices(duration=8, lookup_names=True)
        print(f"Found {len(nearby_devices)} devices.")

        for entry in nearby_devices:
            addr, name = _parse_discovered_device(entry)
            if name:
                print(f"  Device Name: {name}, MAC Address: {addr}")
            else:
                print(f"  Device Name: Unknown, MAC Address: {addr}")

        chosen = _pick_best_bluetooth_device(nearby_devices)
        if chosen != "":
            return chosen
    except Exception as e:
        print(f"[BLUETOOTH] Scan failed: {e}")

    return ""


# Camera config
def load_camera_config(path):
    default_cfg = {
        "camera_mode": "local_webcam",
        "phone_ip_url": "http://192.168.1.18:8080/video",
        "local_webcam_index": 0,
        "camera_rotation": "none",
        "mirror_frame": True,
        "processing_scale": 0.75,
        "holistic_every_n_frames": 2,
        "holistic_model_complexity": 0,
        "ip_frame_drop_grabs": 2,
        "face_check_every": 12,
        "movement_check_every": 12,
    }

    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            user_cfg = json.load(f)
        if isinstance(user_cfg, dict):
            default_cfg.update(user_cfg)
    except Exception:
        pass

    return default_cfg


def unique_keep_order(items):
    seen = set()
    output = []
    for item in items:
        if item in seen:
            continue
        seen.add(item)
        output.append(item)
    return output


def normalize_ip_camera_urls(raw_url):
    url = str(raw_url).strip()
    if url == "":
        return []

    candidates = [url]

    try:
        parsed = urlparse(url)
        if parsed.scheme in ["http", "https"]:
            path = parsed.path.strip()
            if path in ["", "/"]:
                candidates.append(urlunparse(parsed._replace(path="/video")))
            elif not path.endswith("/video"):
                candidates.append(url.rstrip("/") + "/video")
    except Exception:
        pass

    return unique_keep_order(candidates)


def try_open_source(source, is_ip_source):
    backend_attempts = []

    if is_ip_source:
        backend_attempts.append(("default", None))
        if hasattr(cv2, "CAP_FFMPEG"):
            backend_attempts.append(("ffmpeg", cv2.CAP_FFMPEG))
    else:
        if isinstance(source, int) and hasattr(cv2, "CAP_DSHOW"):
            backend_attempts.append(("dshow", cv2.CAP_DSHOW))
        backend_attempts.append(("default", None))

    errors = []
    for backend_name, backend in backend_attempts:
        try:
            cap_obj = (
                cv2.VideoCapture(source, backend)
                if backend is not None
                else cv2.VideoCapture(source)
            )

            if not cap_obj.isOpened():
                errors.append(f"{backend_name}: open failed")
                continue

            cap_obj.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            ok, first_frame = cap_obj.read()
            if ok and first_frame is not None:
                return cap_obj, ""

            cap_obj.release()
            errors.append(f"{backend_name}: opened but no frames")
        except Exception as e:
            errors.append(f"{backend_name}: {e}")

    return None, "; ".join(errors)


def open_camera_from_config(cfg):
    camera_mode = str(cfg.get("camera_mode", "local_webcam")).strip().lower()
    local_index = int(cfg.get("local_webcam_index", 0))

    raw_url = ""
    for key in ["phone_ip_url", "ip_webcam_url", "camera_url", "url", "source"]:
        value = cfg.get(key)
        if isinstance(value, str) and value.strip() != "":
            raw_url = value.strip()
            break

    ip_urls = normalize_ip_camera_urls(raw_url)

    sources_to_try = []
    ip_modes = ["phone_ip", "ip_webcam", "ip", "network"]

    if camera_mode in ip_modes:
        for ip_url in ip_urls:
            sources_to_try.append((ip_url, True, "ip"))
        sources_to_try.append((local_index, False, "local_fallback"))
        if local_index != 0:
            sources_to_try.append((0, False, "local_fallback"))
    else:
        sources_to_try.append((local_index, False, "local"))
        if local_index != 0:
            sources_to_try.append((0, False, "local"))
        for ip_url in ip_urls:
            sources_to_try.append((ip_url, True, "ip_fallback"))

    print(
        f"[CAMERA] Mode={camera_mode}, local_index={local_index}, ip_url={raw_url if raw_url else 'N/A'}"
    )

    failures = []
    for source, is_ip, tag in sources_to_try:
        print(f"[CAMERA] Trying {tag} source: {source}")
        cap_obj, error_msg = try_open_source(source, is_ip)
        if cap_obj is not None:
            print(f"[CAMERA] Connected to source: {source}")
            return cap_obj, is_ip
        failures.append(f"{source} -> {error_msg}")

    details = " | ".join(failures)
    raise RuntimeError(
        "Camera not available from configured sources. "
        + "Check phone and PC are on same network, IP Webcam app is running, and URL is correct. "
        + f"Tried: {details}"
    )


def parse_rotation_value(value):
    text = str(value).strip().lower()
    if text in ["left", "ccw", "90ccw", "-90", "270"]:
        return "left"
    if text in ["right", "cw", "90", "+90", "90cw"]:
        return "right"
    if text in ["180", "flip", "upsidedown", "upside_down"]:
        return "180"
    return "none"


def apply_frame_rotation(frame, rotation_mode):
    if rotation_mode == "left":
        return cv2.rotate(frame, cv2.ROTATE_90_COUNTERCLOCKWISE)
    if rotation_mode == "right":
        return cv2.rotate(frame, cv2.ROTATE_90_CLOCKWISE)
    if rotation_mode == "180":
        return cv2.rotate(frame, cv2.ROTATE_180)
    return frame


camera_cfg = load_camera_config("camera_config.json")
camera_rotation = parse_rotation_value(camera_cfg.get("camera_rotation", "none"))
mirror_frame = bool(camera_cfg.get("mirror_frame", True))

processing_scale = float(camera_cfg.get("processing_scale", 0.75))
if processing_scale < 0.3:
    processing_scale = 0.3
if processing_scale > 1.0:
    processing_scale = 1.0

holistic_every_n_frames = int(camera_cfg.get("holistic_every_n_frames", 2))
if holistic_every_n_frames < 1:
    holistic_every_n_frames = 1

holistic_model_complexity = int(camera_cfg.get("holistic_model_complexity", 0))
if holistic_model_complexity not in [0, 1, 2]:
    holistic_model_complexity = 0

ip_frame_drop_grabs = int(camera_cfg.get("ip_frame_drop_grabs", 2))
if ip_frame_drop_grabs < 0:
    ip_frame_drop_grabs = 0
if ip_frame_drop_grabs > 8:
    ip_frame_drop_grabs = 8

FACE_CHECK_EVERY = int(camera_cfg.get("face_check_every", 12))
if FACE_CHECK_EVERY < 1:
    FACE_CHECK_EVERY = 1

MOVEMENT_CHECK_EVERY = int(camera_cfg.get("movement_check_every", 12))
if MOVEMENT_CHECK_EVERY < 1:
    MOVEMENT_CHECK_EVERY = 1

print(f"[CAMERA] Rotation={camera_rotation}, mirror={mirror_frame}")
print(
    f"[PERF] scale={processing_scale}, holistic_every={holistic_every_n_frames}, "
    + f"model_complexity={holistic_model_complexity}, ip_grab_drop={ip_frame_drop_grabs}"
)

try:
    cap, is_ip_camera = open_camera_from_config(camera_cfg)
except Exception as e:
    print(f"[ERROR] {e}")
    soc.close()
    raise SystemExit


# Face persistence
USERS_FILE = "users.json"
USERS_IMAGES_DIR = "users"
os.makedirs(USERS_IMAGES_DIR, exist_ok=True)

known_face_encodings = []
known_face_names = []
user_profiles = {}
last_saved_image_time = {}


def sanitize_user_folder_name(name):
    # Keep folder names cross-platform safe while preserving readable identity.
    cleaned = re.sub(r"[<>:\"/\\|?*]", "_", str(name)).strip()
    return cleaned if cleaned != "" else "unknown"


def normalize_favourites(raw_values):
    if not isinstance(raw_values, list):
        return []

    output = []
    for value in raw_values:
        if not isinstance(value, str):
            continue
        text = value.strip()
        if text != "" and text not in output:
            output.append(text)
    return output


def normalize_image_paths(raw_values):
    if not isinstance(raw_values, list):
        return []

    output = []
    for value in raw_values:
        if not isinstance(value, str):
            continue
        path = value.strip().replace("\\", "/")
        if path != "" and path not in output:
            output.append(path)
    return output


def ensure_profile_record(name):
    if name not in user_profiles:
        user_profiles[name] = {
            "name": name,
            "favourites": [],
            "images": [],
        }
    return user_profiles[name]


def ensure_user_image_folder(name):
    folder_name = sanitize_user_folder_name(name)
    user_dir = os.path.join(USERS_IMAGES_DIR, folder_name)
    os.makedirs(user_dir, exist_ok=True)
    return user_dir


def maybe_save_user_image(name, frame, box, interval_seconds):
    if name == "" or name.lower() == "unknown" or box is None:
        return False

    now = time.time()
    last = last_saved_image_time.get(name, 0.0)
    if (now - last) < interval_seconds:
        return False

    left, top, right, bottom = box
    h, w = frame.shape[:2]
    left = max(0, min(left, w - 1))
    right = max(0, min(right, w))
    top = max(0, min(top, h - 1))
    bottom = max(0, min(bottom, h))

    if right <= left or bottom <= top:
        return False

    crop = frame[top:bottom, left:right]
    if crop.size == 0:
        return False

    user_dir = ensure_user_image_folder(name)
    file_name = time.strftime("%Y%m%d_%H%M%S") + ".jpg"
    abs_path = os.path.join(user_dir, file_name)

    if not cv2.imwrite(abs_path, crop):
        return False

    rel_path = os.path.relpath(abs_path, ".").replace("\\", "/")
    profile = ensure_profile_record(name)
    images = profile.get("images", [])
    if rel_path not in images:
        images.append(rel_path)
        profile["images"] = images

    last_saved_image_time[name] = now
    return True


def load_known_faces():
    if not os.path.exists(USERS_FILE):
        return

    try:
        with open(USERS_FILE, "r", encoding="utf-8-sig") as f:
            data = json.load(f)

        if not isinstance(data, list):
            data = []

        for item in data:
            if not isinstance(item, dict):
                continue

            name = str(item.get("name", "")).strip()
            if name == "":
                continue

            profile = ensure_profile_record(name)
            profile["favourites"] = normalize_favourites(
                item.get(
                    "favourites", item.get("favorites", profile.get("favourites", []))
                )
            )
            profile["images"] = normalize_image_paths(
                item.get("images", item.get("image_paths", profile.get("images", [])))
            )

            encoding = item.get("encoding")

            if encoding is None:
                encoding = item.get("encoding_if")

            if encoding is None:
                encoding = item.get("encoding_fn")

            if isinstance(encoding, list) and len(encoding) == 128:
                profile["encoding"] = encoding
                known_face_names.append(name)
                known_face_encodings.append(np.array(encoding))

        print(f"[FACES] Loaded {len(known_face_names)} face(s) from {USERS_FILE}")

    except Exception as e:
        print(f"[FACES] Load failed: {e}")


def save_known_faces():
    for name, encoding in zip(known_face_names, known_face_encodings):
        profile = ensure_profile_record(name)
        profile["encoding"] = encoding.tolist()

    data = []
    for name, profile in user_profiles.items():
        item = {
            "name": name,
            "favourites": normalize_favourites(profile.get("favourites", [])),
            "images": normalize_image_paths(profile.get("images", [])),
        }

        encoding = profile.get("encoding")
        if isinstance(encoding, list) and len(encoding) == 128:
            item["encoding"] = encoding
        data.append(item)

    try:
        with open(USERS_FILE, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
    except Exception as e:
        print(f"[FACES] Save failed: {e}")


load_known_faces()


# MediaPipe Holistic
# Already resolved at startup.


# Main loop
old_msg = ""
current_name = ""
current_box = None
last_face_seen_frame = -1
frame_count = 0
movement_name = "idle"
bluetooth_user_id = ""

FACE_LOST_FRAMES = 30
FACE_TOLERANCE = float(camera_cfg.get("face_tolerance", 0.55))
if FACE_TOLERANCE < 0.35:
    FACE_TOLERANCE = 0.35
if FACE_TOLERANCE > 0.7:
    FACE_TOLERANCE = 0.7

USER_IMAGE_SAVE_INTERVAL_SEC = float(
    camera_cfg.get("user_image_save_interval_sec", 15.0)
)
if USER_IMAGE_SAVE_INTERVAL_SEC < 3.0:
    USER_IMAGE_SAVE_INTERVAL_SEC = 3.0
if USER_IMAGE_SAVE_INTERVAL_SEC > 300.0:
    USER_IMAGE_SAVE_INTERVAL_SEC = 300.0
POSE_IDS = [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 23, 24]
MOVEMENT_WINDOW_FRAMES = 20


def create_simple_movement_templates():
    idle_points = []
    left_points = []
    right_points = []

    for step in range(MOVEMENT_WINDOW_FRAMES):
        shift = step * 0.01
        for pid in range(1, 13):
            base_x = 0.45 + (pid % 3) * 0.05
            base_y = 0.30 + (pid // 3) * 0.05
            idle_points.append(PointClass(base_x, base_y, pid))

            left_x = base_x - (shift if pid in [1, 3, 5, 7, 9] else 0)
            left_y = base_y - (shift if pid in [3, 5, 7, 9] else 0)
            left_points.append(PointClass(left_x, left_y, pid))

            right_x = base_x + (shift if pid in [2, 4, 6, 8, 10] else 0)
            right_y = base_y - (shift if pid in [4, 6, 8, 10] else 0)
            right_points.append(PointClass(right_x, right_y, pid))

    return [
        TemplateClass("idle", idle_points),
        TemplateClass("left_move", left_points),
        TemplateClass("right_move", right_points),
    ]


movement_templates = []
movement_recognizer = None
if RecognizerClass is not None and TemplateClass is not None and PointClass is not None:
    movement_templates = create_simple_movement_templates()
    movement_recognizer = RecognizerClass(movement_templates)

movement_points = []


def add_pose_points_for_movement(results):
    global movement_points

    if PointClass is None:
        return

    if results.pose_landmarks is None:
        return

    pose_landmarks = results.pose_landmarks.landmark
    selected = []
    for landmark_index in POSE_IDS:
        if landmark_index < len(pose_landmarks):
            selected.append(pose_landmarks[landmark_index])

    if len(selected) != 12:
        return

    point_id = 1
    for landmark in selected:
        movement_points.append(PointClass(landmark.x, landmark.y, point_id))
        point_id += 1

    max_len = MOVEMENT_WINDOW_FRAMES * 12
    if len(movement_points) > max_len:
        movement_points = movement_points[-max_len:]


def classify_movement():
    if movement_recognizer is None:
        return "idle"

    if len(movement_points) < MOVEMENT_WINDOW_FRAMES * 12:
        return "idle"

    try:
        result = movement_recognizer.recognize(movement_points)
        if result is not None and len(result) > 0:
            return str(result[0])
    except Exception:
        pass

    return "idle"


def bluetooth_worker():
    global bluetooth_user_id, bluetooth_sent

    last_seen = ""
    while True:
        # scan nearby devices and keep the latest ID.
        # when a new ID is seen, reset the sent flag so it will send to C#.
        scanned = capture_bluetooth_user_id()
        if scanned != "" and scanned != last_seen:
            bluetooth_user_id = scanned
            bluetooth_sent = False
            last_seen = scanned
            print(f"[BLUETOOTH] User ID: {bluetooth_user_id}")
        elif scanned == "":
            print("[BLUETOOTH] No usable device detected in this scan window.")

        time.sleep(8)


threading.Thread(target=bluetooth_worker, daemon=True).start()

print("[LOOP] Running. Press 'q' to quit.")

cached_results = None

with mp_holistic.Holistic(
    static_image_mode=False,
    model_complexity=holistic_model_complexity,
    min_detection_confidence=0.6,
    min_tracking_confidence=0.6,
) as holistic:
    while cap.isOpened():
        if is_ip_camera and ip_frame_drop_grabs > 0:
            dropped = 0
            for _ in range(ip_frame_drop_grabs):
                if cap.grab():
                    dropped += 1
                else:
                    break

        ret, frame = cap.read()
        if not ret or frame is None:
            continue

        frame = apply_frame_rotation(frame, camera_rotation)
        if mirror_frame:
            frame = cv2.flip(frame, 1)

        if processing_scale < 0.999:
            proc_frame = cv2.resize(
                frame, (0, 0), fx=processing_scale, fy=processing_scale
            )
        else:
            proc_frame = frame

        rgb = cv2.cvtColor(proc_frame, cv2.COLOR_BGR2RGB)
        frame_count += 1

        if bluetooth_user_id != "" and not bluetooth_sent:
            # just a login signal to know its working.
            bt_msg = "bluetooth:id:" + bluetooth_user_id
            print(f"[SOCKET] Sending: {bt_msg}")
            if send_socket_message(bt_msg):
                bluetooth_sent = True

        if frame_count % FACE_CHECK_EVERY == 0 and face_recognition is not None:
            # Face recognition runs on a downscaled image to keep latency manageable.
            small_rgb = cv2.resize(rgb, (0, 0), fx=0.25, fy=0.25)
            face_locations = face_recognition.face_locations(small_rgb)
            face_encodings = face_recognition.face_encodings(small_rgb, face_locations)

            msg = ""

            if len(face_encodings) > 0:
                face_encoding = face_encodings[0]
                top, right, bottom, left = face_locations[0]
                box_scale = 4.0 / processing_scale
                current_box = (
                    int(left * box_scale),
                    int(top * box_scale),
                    int(right * box_scale),
                    int(bottom * box_scale),
                )

                name = "unknown"
                if len(known_face_encodings) > 0:
                    # Compare with saved users and pick the best known match.
                    distances = face_recognition.face_distance(
                        known_face_encodings, face_encoding
                    )
                    best_index = int(np.argmin(distances))
                    if distances[best_index] < FACE_TOLERANCE:
                        name = known_face_names[best_index]

                current_name = name
                last_face_seen_frame = frame_count
                msg = "face:detected:" + current_name

                if name != "unknown":
                    image_saved = maybe_save_user_image(
                        name,
                        frame,
                        current_box,
                        USER_IMAGE_SAVE_INTERVAL_SEC,
                    )
                    if image_saved:
                        save_known_faces()
            else:
                if (
                    last_face_seen_frame >= 0
                    and (frame_count - last_face_seen_frame) >= FACE_LOST_FRAMES
                ):
                    current_name = ""
                    current_box = None
                    msg = "face:lost"

            if msg != "" and msg != old_msg:
                print(f"[SOCKET] Sending: {msg}")
                send_socket_message(msg)
                old_msg = msg

        run_holistic = cached_results is None or (
            frame_count % holistic_every_n_frames == 0
        )
        if run_holistic:
            cached_results = holistic.process(rgb)

        results = cached_results
        if results is not None:
            add_pose_points_for_movement(results)

        if frame_count % MOVEMENT_CHECK_EVERY == 0:
            # gesture/movement classify and stream updates to C#.
            new_movement = classify_movement()
            if new_movement != movement_name:
                movement_name = new_movement
                movement_msg = "movement:" + movement_name
                print(f"[SOCKET] Sending: {movement_msg}")
                send_socket_message(movement_msg)

        annotated = frame.copy()

        if results is not None:
            mp_drawing.draw_landmarks(
                annotated,
                results.left_hand_landmarks,
                mp_holistic.HAND_CONNECTIONS,
            )
            mp_drawing.draw_landmarks(
                annotated,
                results.right_hand_landmarks,
                mp_holistic.HAND_CONNECTIONS,
            )
            mp_drawing.draw_landmarks(
                annotated,
                results.face_landmarks,
                mp_holistic.FACEMESH_TESSELATION,
                landmark_drawing_spec=None,
                connection_drawing_spec=mp_drawing_styles.get_default_face_mesh_tesselation_style(),
            )
            mp_drawing.draw_landmarks(
                annotated,
                results.pose_landmarks,
                mp_holistic.POSE_CONNECTIONS,
                landmark_drawing_spec=mp_drawing_styles.get_default_pose_landmarks_style(),
            )

        if current_box is not None:
            left, top, right, bottom = current_box
            cv2.rectangle(annotated, (left, top), (right, bottom), (0, 255, 0), 2)

            if current_name != "":
                cv2.putText(
                    annotated,
                    current_name,
                    (left, max(20, top - 10)),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.8,
                    (0, 255, 0),
                    2,
                )

        cv2.putText(
            annotated,
            "Move: " + movement_name,
            (10, 30),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.7,
            (255, 255, 0),
            2,
        )

        if bluetooth_user_id != "":
            cv2.putText(
                annotated,
                "BT ID: " + bluetooth_user_id,
                (10, 60),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (0, 255, 255),
                2,
            )

        cv2.imshow("Output", annotated)

        if cv2.waitKey(1) == ord("q"):
            print("[LOOP] Exiting...")
            break


cap.release()
cv2.destroyAllWindows()
soc.close()
print("[SHUTDOWN] Done.")
