import logging
import os
import warnings

# Suppress MediaPipe / absl C++ stderr noise before any imports load the libs.
os.environ.setdefault("GLOG_minloglevel", "3")          # suppress glog INFO/WARNING/ERROR
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")
os.environ.setdefault("MEDIAPIPE_DISABLE_GPU", "1")

# Redirect stderr to devnull for the duration of the import phase so that
# MediaPipe's C++ backend "inference_feedback_manager" warnings are swallowed.
import sys
import io

_real_stderr = sys.stderr
sys.stderr = open(os.devnull, "w")

logging.getLogger("tensorflow").setLevel(logging.ERROR)
logging.getLogger("absl").setLevel(logging.ERROR)

warnings.filterwarnings(
    "ignore",
    message=r"SymbolDatabase\.GetPrototype\(\) is deprecated\. Please use message_factory\.GetMessageClass\(\) instead\..*",
    category=UserWarning,
    module=r"google\.protobuf\.symbol_database",
)
warnings.filterwarnings(
    "ignore",
    message=r".*tf\.losses\.sparse_softmax_cross_entropy is deprecated.*",
)
import cv2
import mediapipe as mp
import numpy as np
import socket
import pickle
import socket
import json
import bluetooth
import time
from dollarpy import Point
from gestures import (
    is_circle_like,
    is_gesture_significant,
    show_gesture_feedback,
    draw_gesture_feedback,
)
from pathlib import Path
from users import normalize_mac, load_users_by_mac
from movements import recognizer as _movements_recognizer  # kept for fallback
from test_movements import recognizer
from hand_shape_recognizer import normalize_landmarks, load_hand_shapes, recognize_hand_shape
from context_store import ContextStore, apply_gesture_action
from event_protocol import build_event, event_to_console, to_pretty_json

from expression_tracker import ExpressionTracker
from gaze_tracker import GazeTracker
from gaze_report import GazeSessionLogger
from session_reports import save_session_reports
from face_recognizer import FaceRecognizer
from face_signup import FaceSignupFlow

# Restore stderr now that all noisy imports are done.
sys.stderr.close()
sys.stderr = _real_stderr

SERVER_HOST = "0.0.0.0"
SERVER_PORT = 5001
TUIO_NOTE = "Remember to start your TUIO simulator/tracker on port 3333"
PHONE_CAMERA_URL = ""
PHONE_BT_NAME = "Phone"
USERS_JSON_PATH = Path("TUIO11_NET-master") / "bin" / "Debug" / "users.json"
ARTIFACTS_JSON_PATH = Path("TUIO11_NET-master") / "artifacts.json"
TUIO_PRIORITY_SECONDS = 3.0
CSHARP_CONTEXT_PRIORITY_SECONDS = 10.0
SKIP_CONTEXT_LABELS = {"person", "cell phone", "mobile phone"}
OBJECTS_DIR = Path("TUIO11_NET-master") / "bin" / "Debug" / "objects"


# user / bluetooth helpers moved to users.py


def close_csharp_connection(connection: socket.socket | None) -> None:
    if connection is None:
        return

    try:
        connection.close()
    except OSError:
        pass


def send_socket_message(connection: socket.socket | None, payload: str) -> bool:
    if connection is None or not payload:
        return False

    try:
        connection.sendall(f"{payload}\n".encode("utf-8"))
        return True
    except OSError as exc:
        print(f"[SOCKET] Send failed: {exc}")
        return False


def load_tuio_artifacts(path: Path) -> dict[int, dict]:
    if not path.exists():
        return {}

    try:
        with path.open("r", encoding="utf-8") as file:
            data = json.load(file)
        artifacts = data.get("artifacts", []) if isinstance(data, dict) else []
        mapping: dict[int, dict] = {}
        for artifact in artifacts:
            try:
                tuio_id = int(artifact.get("tuioId"))
            except (TypeError, ValueError):
                continue
            mapping[tuio_id] = artifact
        return mapping
    except Exception:
        return {}


def poll_socket_lines(
    connection: socket.socket | None, buffer: str
) -> tuple[socket.socket | None, str, list[str], bool]:
    lines: list[str] = []
    if connection is None:
        return None, buffer, lines, False

    try:
        data = connection.recv(4096)
        if not data:
            print("[SOCKET] C# GUI disconnected. Waiting for another instance...")
            close_csharp_connection(connection)
            return None, "", lines, False
        if data:
            buffer += data.decode("utf-8", errors="ignore")
            while "\n" in buffer:
                line, buffer = buffer.split("\n", 1)
                line = line.strip()
                if line:
                    lines.append(line)
    except BlockingIOError:
        pass
    except Exception as exc:
        print(f"[SOCKET] C# GUI disconnected: {exc}")
        close_csharp_connection(connection)
        return None, "", lines, False
    return connection, buffer, lines, True


def is_valid_context_label(label: str | None) -> bool:
    if not label:
        return False
    return label.strip().lower() not in SKIP_CONTEXT_LABELS


def draw_context_debug(
    frame,
    store: ContextStore,
    user_name: str,
    last_emotion: str,
    last_delta: float,
) -> None:
    user_data = store.data.get("users", {}).get(user_name)
    if not isinstance(user_data, dict):
        return

    context = (
        user_data.get("context", {})
        if isinstance(user_data.get("context"), dict)
        else {}
    )
    current_artifact = str(context.get("current_artifact") or "")
    current_category = str(context.get("current_category") or "")
    category_scores = (
        user_data.get("category_scores", {})
        if isinstance(user_data.get("category_scores"), dict)
        else {}
    )
    artifact_scores = (
        user_data.get("artifact_scores", {})
        if isinstance(user_data.get("artifact_scores"), dict)
        else {}
    )

    category_score = (
        float(category_scores.get(current_category, 0.0)) if current_category else 0.0
    )
    artifact_score = (
        float(artifact_scores.get(current_artifact, 0.0)) if current_artifact else 0.0
    )

    line1 = f"Artifact: {current_artifact or '-'}"
    line2 = f"Category: {current_category or '-'}"
    line3 = f"CatScore: {category_score:.2f} | ArtScore: {artifact_score:.2f}"
    line4 = f"LastEmotion: {last_emotion or '-'} | Delta: {last_delta:+.2f}"

    cv2.putText(
        frame, line1, (20, 160), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 2
    )
    cv2.putText(
        frame, line2, (20, 185), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 2
    )
    cv2.putText(
        frame, line3, (20, 210), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 2
    )
    cv2.putText(
        frame, line4, (20, 235), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 2
    )


# gesture helper functions moved to gestures.py


soc = socket.socket()
hostname = "localhost"
port = 5000
soc.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
soc.bind((hostname, port))
soc.listen(5)
soc.settimeout(1.0)
Allpoints = []
mp_pose = mp.solutions.pose


def wait_for_csharp_client(server_socket: socket.socket) -> tuple[socket.socket, tuple]:
    print(f"[SOCKET] Python socket server listening on {hostname}:{port}")
    print("[SOCKET] Waiting for C# GUI to connect...")
    last_wait_log = 0.0

    while True:
        try:
            client_connection, client_address = server_socket.accept()
            print(
                f"[SOCKET] C# GUI connected from {client_address[0]}:{client_address[1]}"
            )
            return client_connection, client_address
        except socket.timeout:
            if time.monotonic() - last_wait_log >= 5.0:
                print("[SOCKET] Still waiting for C# GUI on port 5000...")
                last_wait_log = time.monotonic()


conn, addr = wait_for_csharp_client(soc)
conn.setblocking(False)
old_msg = ""
mp_holistic = mp.solutions.holistic
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles
holistic = mp_holistic.Holistic(
    static_image_mode=False, min_detection_confidence=0.65, model_complexity=1
)

face_ids = {}
frame_count = 0
analysis_frame_counter = 0
last_emotion = ""
last_gaze_zone = ""
latest_expression = None
gesture_points = []
circle_points = []
expression_log_until = 0.0
last_expression_signature = ""
last_interest_emotion = ""
last_interest_delta = 0.0
person_frame_counter = 0
last_person_faces: list[dict] = []
shape_cooldown_time = 0.0

# Load custom static hand shapes for Mute / DarkMode
_BASE_DIR = Path(__file__).parent
hand_shapes = load_hand_shapes(str(_BASE_DIR / "hand_shapes.json"))
print(f"[GESTURE] Loaded {len(hand_shapes)} static hand shapes: {list(hand_shapes.keys())}")


all_macs = []


class HCIServer:
    """Main server class — one thread per connected client."""

    def __init__(self):
        self.face_cascade = cv2.CascadeClassifier(
            cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
        )
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._sock.bind((SERVER_HOST, SERVER_PORT))
        self._sock.listen(5)
        print(f"[SERVER] Listening on {SERVER_HOST}:{SERVER_PORT}")
        print(f"[SERVER] {TUIO_NOTE}\n")

    # Step 2 · Bluetooth Scan

    def scan_bluetooth(self) -> tuple[str | None, dict | None]:
        print("\n" + "=" * 60)
        print("[BT] ========== BLUETOOTH DISCOVERY START ==========")
        print("[BT] Scanning for Bluetooth devices (3 seconds)…")
        users_by_mac = load_users_by_mac(USERS_JSON_PATH)
        print(f"[BT] Known users in system: {len(users_by_mac)}")

        try:
            print("[BT] Scanning for devices...")
            # Suppress MediaPipe/absl C++ stderr noise that fires during BT scan
            _bt_stderr = open(os.devnull, "w")
            _saved = sys.stderr
            sys.stderr = _bt_stderr
            try:
                devices = bluetooth.discover_devices(lookup_names=True, duration=3)
            finally:
                sys.stderr = _saved
                _bt_stderr.close()
            print(f"[BT] Scan complete. Found {len(devices)} device(s)")
        except Exception as e:
            print(f"[BT] ERROR during scan: {type(e).__name__}")
            print(f"[BT] Error message: {e}")
            print(
                "[BT] (Make sure Bluetooth adapter is enabled and PyBluez is installed)"
            )
            return None, None

        if len(devices) > 0:
            print("[BT] " + "-" * 56)
            print("[BT] DISCOVERED DEVICES:")
            print("[BT] " + "-" * 56)
            selected_addr = None

            for index, (addr, name) in enumerate(devices, start=1):
                display_name = name if name else "Unknown"
                normalized_mac = normalize_mac(addr)
                print(f"[BT]   {index}. Name: '{display_name}'")
                print(f"[BT]      MAC:  {addr} (normalized: {normalized_mac})")

                matched_user = users_by_mac.get(normalized_mac)
                if matched_user:
                    print(
                        f"[BT]      ✓ MATCH FOUND: User '{matched_user['name']}' (Profile: {matched_user.get('Profile', 'N/A')})"
                    )
                    print("[BT] " + "-" * 56)
                    print(f"[BT] ========== RETURNING MATCHED USER ==========\n")
                    return addr, matched_user
                else:
                    print(f"[BT]      ✗ No user match for this MAC")

                if selected_addr is None and name == PHONE_BT_NAME:
                    print(f"[BT]      → Selected as PHONE_BT_NAME candidate")
                    selected_addr = addr

            print("[BT] " + "-" * 56)
            if selected_addr is None:
                selected_addr = devices[0][0]
                print(f"[BT] No phone name match, using first device: {selected_addr}")
            else:
                print(f"[BT] Selected device (phone match): {selected_addr}")

            print("[BT] " + "-" * 56)
            print(f"[BT] ========== RETURNING SELECTED MAC ==========\n")
            return selected_addr, None

        else:
            print("[BT] " + "-" * 56)
            print("[BT] NO DEVICES FOUND")
            print("[BT] Possible reasons:")
            print("[BT]   - No Bluetooth devices nearby")
            print("[BT]   - Devices are in pairing mode or hidden")
            print("[BT]   - Bluetooth adapter may be disabled")
            print("[BT] " + "-" * 56)
            print(
                "[BT] ========== BLUETOOTH DISCOVERY COMPLETE (NO DEVICES) ==========\n"
            )
            return None, None


server = HCIServer()
address = None
login_message = None
address, login_message = server.scan_bluetooth()
context_store = ContextStore()
expression_tracker = ExpressionTracker()
gaze_tracker = GazeTracker()
face_recognizer = FaceRecognizer(OBJECTS_DIR, users_json=USERS_JSON_PATH)
tuio_artifacts = load_tuio_artifacts(ARTIFACTS_JSON_PATH)
active_user_name = "guest"

# signup_flow is non-None only when BT found no known user
signup_flow: FaceSignupFlow | None = None

if login_message is not None:
    active_user_name = str(login_message.get("name", "guest")).strip() or "guest"
    context_store.ensure_user(
        active_user_name, str(login_message.get("Profile", "")).strip()
    )
elif address is not None:
    # BT device found but not in users.json — try face login / signup
    signup_flow = FaceSignupFlow(face_recognizer, OBJECTS_DIR, USERS_JSON_PATH)
    active_user_name = f"guest_{normalize_mac(address).replace(':', '')}"
    context_store.ensure_user(active_user_name)
else:
    # No BT at all — try face login / signup
    signup_flow = FaceSignupFlow(face_recognizer, OBJECTS_DIR, USERS_JSON_PATH)
    context_store.ensure_user(active_user_name)

# Clean up any previously persisted invalid artifacts (e.g. "person").
try:
    snapshot = context_store.get_context_snapshot(active_user_name)
    current_artifact = str(snapshot.get("current_artifact") or "").strip().lower()
    if current_artifact in SKIP_CONTEXT_LABELS:
        context_store.update_context(
            active_user_name, current_artifact="", current_category=""
        )

    user_data = context_store.data.get("users", {}).get(active_user_name, {})
    if isinstance(user_data, dict):
        changed = False
        category_scores = user_data.get("category_scores")
        if isinstance(category_scores, dict):
            for bad in list(category_scores.keys()):
                if str(bad).strip().lower() in SKIP_CONTEXT_LABELS:
                    category_scores.pop(bad, None)
                    changed = True
        artifact_scores = user_data.get("artifact_scores")
        if isinstance(artifact_scores, dict):
            for bad in list(artifact_scores.keys()):
                if str(bad).strip().lower() in SKIP_CONTEXT_LABELS:
                    artifact_scores.pop(bad, None)
                    changed = True
        if changed:
            context_store.save()
except Exception:
    pass

gaze_session = GazeSessionLogger(active_user_name)
reports_dir = Path("reports")

cap = cv2.VideoCapture(0)
cv2.namedWindow("Output", cv2.WINDOW_NORMAL)
cv2.resizeWindow("Output", 960, 640)
user_login = 0
flag_bluetooth = 0
socket_buffer = ""
last_tuio_marker_id = None
tuio_last_seen = 0.0
csharp_context_last_seen = 0.0
while cap.isOpened():
    ret, frame = cap.read()
    if not ret or frame is None:
        continue
    msg = ""

    conn, socket_buffer, incoming_lines, connection_alive = poll_socket_lines(
        conn, socket_buffer
    )
    if not connection_alive:
        try:
            result = save_session_reports(
                reports_dir,
                active_user_name,
                gaze_session,
                context_store,
                tuio_artifacts,
            )
            print(f"[GAZE] Saved session reports: {result.get('session_dir')}")
        except Exception as exc:
            print(f"[GAZE] Failed to save reports: {exc}")

        gaze_session.reset(active_user_name)
        gaze_tracker = GazeTracker()
        try:
            expression_tracker.reset_gaze_calibration()
        except Exception:
            pass

        conn, addr = wait_for_csharp_client(soc)
        conn.setblocking(False)
        socket_buffer = ""
        old_msg = ""
        user_login = 0
        continue

    for line in incoming_lines:
        if line.startswith("TUIO:"):
            parts = line.split(":", 1)
            if len(parts) == 2:
                try:
                    marker_id = int(parts[1])
                except ValueError:
                    marker_id = None
                if marker_id is not None and marker_id != last_tuio_marker_id:
                    artifact = tuio_artifacts.get(marker_id, {})
                    artifact_name = artifact.get("name") or f"marker_{marker_id}"
                    category = (
                        artifact.get("country")
                        or artifact.get("era")
                        or artifact.get("origin")
                        or "general"
                    )
                    context_store.update_context(
                        active_user_name,
                        current_artifact=artifact_name,
                        current_category=category,
                        last_object=artifact_name,
                    )
                    context_store.record_artifact_opened(
                        active_user_name, artifact_name
                    )
                    last_tuio_marker_id = marker_id
                    tuio_last_seen = time.monotonic()
                    print(f"[TUIO] Marker {marker_id} -> {artifact_name}")
        else:
            # Allow the C# client to update context when user opens a single-artifact page.
            # Expected examples:
            #   {"type":"artifact_focus","artifact":"Ramses II","category":"New Kingdom"}
            #   {"type":"context_update","current_artifact":"Ramses II","current_category":"New Kingdom"}
            if line.startswith("{") and line.endswith("}"):
                try:
                    msg_obj = json.loads(line)
                except Exception:
                    msg_obj = None
                if isinstance(msg_obj, dict):
                    msg_type = str(msg_obj.get("type", "")).strip().lower()
                    if msg_type in {
                        "artifact_focus",
                        "single_artifact",
                        "artifact_details",
                    }:
                        artifact_name = str(
                            msg_obj.get("artifact")
                            or msg_obj.get("artifact_name")
                            or msg_obj.get("name")
                            or ""
                        ).strip()
                        category = str(
                            msg_obj.get("category")
                            or msg_obj.get("artifact_category")
                            or msg_obj.get("current_category")
                            or ""
                        ).strip()
                        if artifact_name:
                            csharp_context_last_seen = time.monotonic()
                            context_store.update_context(
                                active_user_name,
                                current_artifact=artifact_name,
                                current_category=category or "general",
                                last_object=artifact_name,
                            )
                            context_store.record_artifact_opened(
                                active_user_name, artifact_name
                            )
                            print(
                                f"[CONTEXT] Focus -> {artifact_name} ({category or 'general'})"
                            )
                    elif msg_type in {"context_update"}:
                        artifact_name = str(msg_obj.get("current_artifact", "")).strip()
                        category = str(msg_obj.get("current_category", "")).strip()
                        if bool(msg_obj.get("clear")):
                            csharp_context_last_seen = time.monotonic()
                            context_store.update_context(
                                active_user_name,
                                current_artifact="",
                                current_category="",
                            )
                            print("[CONTEXT] Cleared focus (C# home)")
                        elif artifact_name or category:
                            csharp_context_last_seen = time.monotonic()
                            context_store.update_context(
                                active_user_name,
                                current_artifact=artifact_name or None,
                                current_category=category or None,
                                last_object=artifact_name or None,
                            )
                            if artifact_name:
                                context_store.record_artifact_opened(
                                    active_user_name, artifact_name
                                )
                            if artifact_name or category:
                                print(
                                    f"[CONTEXT] Update -> artifact={artifact_name or '-'} category={category or '-'}"
                                )
    if user_login == 0:
        if login_message is not None:
            if isinstance(login_message, dict):
                login_payload = dict(login_message)
            else:
                login_payload = {"name": str(login_message)}

            login_payload["type"] = "user_login"
            if address:
                login_payload["mac"] = normalize_mac(address)

            message_payload = json.dumps(login_payload)
            print(f"[LOGIN] BT match → {active_user_name}")
            if send_socket_message(conn, message_payload):
                context_store.log_event(
                    build_event(
                        "face_login",
                        {
                            "user": active_user_name,
                            "source": "bluetooth_match",
                        },
                    )
                )
                user_login = 1
        elif address is not None and signup_flow is None:
            # Unknown MAC and no face flow — send raw MAC as guest
            print("Sending MAC:", address)
            if send_socket_message(conn, address):
                context_store.log_event(
                    build_event(
                        "guest_session",
                        {
                            "user": active_user_name,
                            "mac": normalize_mac(address),
                        },
                    )
                )
                user_login = 1

    # ── Face login / signup flow (runs when BT found no known user) ───────────
    if signup_flow is not None and not signup_flow.done:
        f_frame_signup = cv2.resize(frame, (480, 320))
        frame_rgb_signup = cv2.cvtColor(f_frame_signup, cv2.COLOR_BGR2RGB)
        results_signup = holistic.process(frame_rgb_signup)
        annotated_signup = f_frame_signup.copy()
        h_s, w_s = f_frame_signup.shape[:2]

        signup_flow.process(
            frame_rgb_signup,
            annotated_signup,
            results_signup,
            w_s,
            h_s,
        )

        display_signup = cv2.resize(
            annotated_signup, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR
        )
        cv2.imshow("Output", display_signup)
        if cv2.waitKey(1) == ord("q"):
            break

        # Handle completion immediately — don't wait for next iteration
        if not signup_flow.done:
            continue

    if signup_flow is not None and signup_flow.done:
        result_s = signup_flow.result
        status   = result_s.get("status", "guest")
        name     = result_s.get("name", "guest") or "guest"
        print(f"[SIGNUP] Flow done — status={status} name={name}")

        if status in ("login", "signup"):
            active_user_name = name
            context_store.ensure_user(active_user_name, "visitor")

            # Look up the full user record from users.json so C# gets the same
            # payload shape as a Bluetooth login (name, age, gender, Profile,
            # themeMode, favorites, etc.)
            full_record: dict = {}
            try:
                with USERS_JSON_PATH.open("r", encoding="utf-8") as _f:
                    _all_users: list = json.load(_f)
                for _u in _all_users:
                    if str(_u.get("name", "")).strip().lower() == name.strip().lower():
                        full_record = dict(_u)
                        break
            except Exception as _exc:
                print(f"[SIGNUP] Could not read users.json for payload: {_exc}")

            login_payload = full_record if full_record else {"name": name}
            login_payload["type"] = "user_login"
            login_payload["source"] = "face_signup" if status == "signup" else "face_login"

            payload_str = json.dumps(login_payload)
            print(f"[LOGIN] Face {status} → {active_user_name}")
            ok = send_socket_message(conn, payload_str)
            if not ok:
                print(f"[LOGIN] Send failed for {active_user_name}")
            if ok:
                context_store.log_event(
                    build_event("face_login", {"user": active_user_name, "source": status})
                )
        else:
            print("[SIGNUP] User cancelled — continuing as guest")

        signup_flow = None
        user_login  = 1
    # ─────────────────────────────────────────────────────────────────────────
    try:

        f_frame = cv2.resize(frame, (480, 320))
        frame_rgb = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
        frame_count += 1
        # if frame_count % 60 == 0:
        #     face_encodings = DeepFace.represent(
        #         f_frame,
        #         model_name="Facenet",
        #         enforce_detection=False
        #     )
        #     if len(face_encodings) > 0:
        #         face_id = tuple(face_encodings[0]["embedding"])
        #         match = None
        #         for k in face_ids:
        #             if np.linalg.norm(np.array(face_id) - np.array(k)) < 15:
        #                 match = face_ids[k]
        #                 msg = "Known face recognized: " + match
        #                 break
        #         if match is None:
        #             face_ids[face_id] = "Person " + str(len(face_ids) + 1)
        #             msg = "New face detected: " + face_ids[face_id]
        #             print("New face detected: " + face_ids[face_id])
        #         else:
        #             print("Known face recognized: " + match)
        results = holistic.process(frame_rgb)
        annotated_image = f_frame.copy()
        image_height, image_width, _ = frame_rgb.shape

        analysis_frame_counter += 1
        if analysis_frame_counter % 5 == 0:
            analysis_frame_counter = 0
            expression = expression_tracker.analyze(frame_rgb)
            if expression is not None:
                gaze_session.add_expression(expression, time.monotonic())
                latest_expression = expression

                current_signature = f"{expression['emotion']}:{expression['gaze_zone']}"
                can_log_expression = (
                    time.monotonic() >= expression_log_until
                    and expression.get("window_size", 1) >= 3
                    and current_signature != last_expression_signature
                )

                if can_log_expression:
                    last_emotion = expression["emotion"]
                    last_gaze_zone = expression["gaze_zone"]
                    last_expression_signature = current_signature
                    expression_log_until = time.monotonic() + 2.0

                    context_store.update_context(
                        active_user_name,
                        last_emotion=expression["emotion"],
                        last_gaze=expression["gaze_zone"],
                    )

                    gaze_hit = gaze_tracker.register(expression["gaze_zone"])

                    # Score interest against the currently focused artifact/category (not emotion name).
                    # This is used later for "bonus/enrich" summary reporting (PDF/QR).
                    context_snapshot = context_store.get_context_snapshot(
                        active_user_name
                    )
                    focused_artifact = context_snapshot.get("current_artifact") or ""
                    focused_category = context_snapshot.get("current_category") or ""

                    emotion_key = str(expression.get("emotion", "")).strip().lower()
                    if emotion_key == "happy":
                        interest_delta = 1.0
                    elif emotion_key == "surprised":
                        interest_delta = 0.5
                    elif emotion_key == "neutral":
                        interest_delta = 0.2
                    elif emotion_key == "sad":
                        interest_delta = -0.5
                    else:
                        interest_delta = 0.0

                    if focused_category:
                        context_store.update_category_score(
                            active_user_name, focused_category, interest_delta
                        )
                    if focused_artifact:
                        context_store.update_artifact_score(
                            active_user_name, focused_artifact, interest_delta
                        )

                    last_interest_emotion = expression.get("emotion", "")
                    last_interest_delta = interest_delta

                    adaptive_event = build_event(
                        "expression_gaze_update",
                        {
                            "user": active_user_name,
                            "expression": expression,
                            "gaze": gaze_hit,
                            "recommended": context_store.get_context_recommendation(
                                active_user_name
                            ),
                        },
                    )
                    context_store.log_event(adaptive_event)
                    print(event_to_console(adaptive_event))

        # ── Face detection + recognition (DeepFace, no YOLO) ─────────────
        person_frame_counter += 1
        if person_frame_counter % 18 == 0:
            person_frame_counter = 0
            last_person_faces = face_recognizer.identify_faces(frame_rgb)

        # Draw person bounding boxes with identified names every frame.
        for person_face in last_person_faces:
            px1, py1, px2, py2 = person_face["bbox"]
            name = person_face["name"]
            color = (0, 255, 0) if name != "Unknown" else (0, 0, 255)
            cv2.rectangle(annotated_image, (px1, py1), (px2, py2), color, 2)
            label_bg_y = max(py1 - 10, 14)
            cv2.putText(
                annotated_image,
                name,
                (px1, label_bg_y),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.65,
                color,
                2,
            )
        # ─────────────────────────────────────────────────────────────────

        # ── Static hand-shape detection (Mute / DarkMode) ─────────────────
        active_hand = None
        if results.right_hand_landmarks:
            active_hand = results.right_hand_landmarks
        elif results.left_hand_landmarks:
            active_hand = results.left_hand_landmarks

        if active_hand and hand_shapes and time.monotonic() > shape_cooldown_time:
            norm = normalize_landmarks(active_hand)
            shape_name, shape_score = recognize_hand_shape(norm, hand_shapes, threshold=0.45)
            if shape_name and shape_score > 0.55:
                msg = shape_name
                shape_cooldown_time = time.monotonic() + 1.5
                print(f"[GESTURE] Shape detected: {shape_name} (score={shape_score:.2f})")

        # ── Dynamic trajectory (Index Finger) ──────────────────────────────
        if results.pose_landmarks is not None:
            right_index = results.pose_landmarks.landmark[
                mp_pose.PoseLandmark.RIGHT_INDEX
            ]
            left_index = results.pose_landmarks.landmark[
                mp_pose.PoseLandmark.LEFT_INDEX
            ]
            use_right = right_index.visibility >= left_index.visibility
            finger = right_index if use_right else left_index

            if finger.visibility >= 0.4:
                finger_x = int(finger.x * image_width)
                finger_y = int(finger.y * image_height)
                # Only append if finger has actually moved (3px jitter filter)
                if not gesture_points or abs(finger_x - gesture_points[-1].x) > 3 or abs(finger_y - gesture_points[-1].y) > 3:
                    gesture_points.append(Point(finger_x, finger_y, 1))
                    circle_points.append(Point(finger_x, finger_y, 1))

        if frame_count % 30 == 0:
            frame_count = 0

            if gesture_points and is_gesture_significant(gesture_points):
                result = recognizer.recognize(gesture_points)
                if result[0] is not None:
                    recognized_gesture = str(result[0]).strip()
                    recognized_score = float(result[1])
                    print(f"[GESTURE] {recognized_gesture} score={recognized_score:.2f}")

                    if recognized_score < 0.5:
                        print(
                            f"[GESTURE] Low score {recognized_score:.2f} for {recognized_gesture}, ignored"
                        )
                    elif recognized_gesture == "Circle":
                        if is_circle_like(circle_points):
                            msg = recognized_gesture
                        else:
                            print("[GESTURE] Ignored weak Circle-like path")
                    else:
                        msg = recognized_gesture

            if not msg and is_circle_like(circle_points):
                msg = "Circle"
                print("[GESTURE] Circle detected from motion path")

            gesture_points.clear()
            circle_points.clear()

            # Update top-right feedback label for any gesture that fired
            if msg:
                show_gesture_feedback(msg, duration=3.0)
        # x=int(results.pose.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].x * image_width)
        # y=int(results.pose.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].y * image_height)
        for face_id_key, name in face_ids.items():
            cv2.putText(
                annotated_image,
                name,
                (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX,
                1,
                (0, 255, 0),
                2,
            )
        mp_drawing.draw_landmarks(
            annotated_image, results.left_hand_landmarks, mp_holistic.HAND_CONNECTIONS
        )
        mp_drawing.draw_landmarks(
            annotated_image, results.right_hand_landmarks, mp_holistic.HAND_CONNECTIONS
        )
        mp_drawing.draw_landmarks(
            annotated_image,
            results.face_landmarks,
            mp_holistic.FACEMESH_TESSELATION,
            landmark_drawing_spec=None,
            connection_drawing_spec=mp_drawing_styles.get_default_face_mesh_tesselation_style(),
        )
        mp_drawing.draw_landmarks(
            annotated_image,
            results.pose_landmarks,
            mp_holistic.POSE_CONNECTIONS,
            landmark_drawing_spec=mp_drawing_styles.get_default_pose_landmarks_style(),
        )
        display_image = cv2.resize(
            annotated_image, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR
        )
        draw_gesture_feedback(display_image)
        expression_tracker.draw_overlay(display_image, latest_expression)
        draw_context_debug(
            display_image,
            context_store,
            active_user_name,
            last_interest_emotion,
            last_interest_delta,
        )
        cv2.imshow("Output", display_image)
        # logic to send msg to unity
        if msg != "" and msg != old_msg:  # only send when there's actually something
            context_snapshot = context_store.get_context_snapshot(active_user_name)
            current_item_id = (
                context_snapshot.get("current_artifact") or "current_artifact"
            )
            current_category = context_snapshot.get("current_category") or "general"
            action_result = apply_gesture_action(
                context_store,
                active_user_name,
                msg,
                item_id=current_item_id,
                category=current_category,
            )
            context_snapshot = context_store.update_context(
                active_user_name,
                last_gesture=msg,
            )
            recommendation = context_store.get_context_recommendation(active_user_name)
            context_event = build_event(
                "gesture_context_update",
                {
                    "user": active_user_name,
                    "gesture": msg,
                    "action": action_result,
                    "recommendation": recommendation,
                    "context": context_snapshot,
                },
            )
            context_store.log_event(context_event)
            print(event_to_console(context_event))

            if send_socket_message(conn, msg):
                if msg == "Circle":
                    feedback_message = "Circle detected: favorite request sent"
                    print(f"[GESTURE] {feedback_message}")
                    show_gesture_feedback(feedback_message)
                elif action_result.get("action") != "none":
                    feedback_message = f"Action: {action_result.get('action')} ({action_result.get('result')})"
                    print(f"[CONTEXT] {feedback_message}")
                    show_gesture_feedback(feedback_message)

        old_msg = msg

        if msg == pickle.dumps("exit"):
            break
    except Exception as e:
        print(e)
    if cv2.waitKey(1) == ord("q"):
        break

try:
    result = save_session_reports(
        reports_dir, active_user_name, gaze_session, context_store, tuio_artifacts
    )
    print(f"[GAZE] Saved session reports: {result.get('session_dir')}")
except Exception as exc:
    print(f"[GAZE] Failed to save reports on exit: {exc}")

cap.release()
cv2.destroyAllWindows()
