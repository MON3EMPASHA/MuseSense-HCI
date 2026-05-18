import logging
import os
import sys
import warnings
import math
from pathlib import Path

# If launched with anything other than the project's venv interpreter, re-launch
# with the local venv python so dependencies resolve correctly. We keep the
# repo-root .venv as a fallback for existing local setups. We use subprocess
_SCRIPT_DIR = Path(__file__).resolve().parent

def _is_valid_venv(py_path: Path) -> bool:
    """Return True only if python.exe exists AND pyvenv.cfg is present."""
    return py_path.exists() and (py_path.parent.parent / "pyvenv.cfg").exists()

_LOCAL_VENV_PY = _SCRIPT_DIR / ".venv" / "Scripts" / "python.exe"
_ROOT_VENV_PY = _SCRIPT_DIR.parent / ".venv" / "Scripts" / "python.exe"
# Also check "venv" (hidden-less) for setups that predate the rename.
_OLD_VENV_PY = _SCRIPT_DIR / "venv" / "Scripts" / "python.exe"
_CURRENT_PY = Path(sys.executable)
_VENV_PY = _LOCAL_VENV_PY if _is_valid_venv(_LOCAL_VENV_PY) else (
    _ROOT_VENV_PY if _is_valid_venv(_ROOT_VENV_PY) else _OLD_VENV_PY
)
# Keep the currently activated venv when one is already active. Only re-launch
# when running outside a valid venv and a project-local fallback exists.
_RUNNING_IN_VALID_VENV = _is_valid_venv(_CURRENT_PY)
if (not _RUNNING_IN_VALID_VENV) and _is_valid_venv(_VENV_PY) and os.path.normcase(sys.executable) != os.path.normcase(str(_VENV_PY)):
    import subprocess
    print(f"[BOOT] Re-launching under venv python: {_VENV_PY}")
    sys.exit(subprocess.call([str(_VENV_PY), "-u", os.path.abspath(__file__), *sys.argv[1:]]))

os.environ.setdefault("GLOG_minloglevel", "3")
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")
os.environ.setdefault("MEDIAPIPE_DISABLE_GPU", "1")

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

_real_stderr = sys.stderr
sys.stderr = open(os.devnull, "w")
try:
    import cv2
    import mediapipe as mp
    import numpy as np
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
    from test_movements import recognizer
    from hand_shape_recognizer import normalize_landmarks, load_hand_shapes, recognize_hand_shape
    from context_store import ContextStore, apply_gesture_action
    from event_protocol import build_event, event_to_console

    from expression_tracker import ExpressionTracker
    from gaze_tracker import GazeTracker
    from gaze_report import GazeSessionLogger
    from session_reports import save_session_reports
    from face_recognizer import FaceRecognizer
    from face_signup import FaceSignupFlow
    from hand_keyboard import HandKeyboard
    from object_tracking import (
        ArtifactFocusSmoother,
        YoloTracker,
        draw_artifact_detections,
    )
finally:
    # Always restore stderr, even on import failure, so tracebacks are visible.
    try:
        sys.stderr.close()
    except Exception:
        pass
    sys.stderr = _real_stderr

PHONE_CAMERA_URL = ""
PHONE_BT_NAME = "Phone"
USERS_JSON_PATH = Path("TUIO11_NET-master") / "bin" / "Debug" / "users.json"
ARTIFACTS_JSON_PATH = Path("TUIO11_NET-master") / "artifacts.json"
TUIO_PRIORITY_SECONDS = 3.0
CSHARP_CONTEXT_PRIORITY_SECONDS = 10.0
SKIP_CONTEXT_LABELS = {"person", "cell phone", "mobile phone"}
OBJECTS_DIR = Path("TUIO11_NET-master") / "bin" / "Debug" / "objects"
ARTIFACT_YOLO_MODEL_PATH = (
    Path("YOLO Object Tracking") / "models" / "artifact_yolo11s_best.pt"
)
ARTIFACT_YOLO_CONFIDENCE = 0.5
ARTIFACT_YOLO_INTERVAL = 5


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
        connection.setblocking(True)
        connection.sendall(f"{payload}\n".encode("utf-8"))
        connection.setblocking(False)
        return True
    except OSError as exc:
        print(f"[SOCKET] Send failed: {exc}")
        try:
            connection.setblocking(False)
        except OSError:
            pass
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
        # Small sleep to avoid busy-waiting before camera is initialized.
        time.sleep(0.05)


print(f"[SOCKET] Python socket server listening on {hostname}:{port}")
print("[SOCKET] C# GUI will be accepted asynchronously — proceeding with initialization")
conn = None
addr = None
old_msg = ""
mp_holistic = mp.solutions.holistic
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles
holistic = mp_holistic.Holistic(
    static_image_mode=False, min_detection_confidence=0.65, model_complexity=1
)

frame_count = 0
analysis_frame_counter = 0
last_emotion = ""
last_gaze_zone = ""
latest_expression = None
gesture_points = []
circle_points = []
admin_keyboard = None
admin_keyboard_request_id = ""
admin_keyboard_prompt = ""
admin_keyboard_mode = "alpha"
admin_keyboard_initial_text = ""
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
admin_hand_shapes = load_hand_shapes(str(_BASE_DIR / "admin_hand_shapes.json"))
print(
    f"[GESTURE] Loaded {len(admin_hand_shapes)} admin hand shapes: {list(admin_hand_shapes.keys())}"
)


all_macs = []


class HCIServer:
    """Orchestrates Bluetooth discovery and user matching."""

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
artifact_tracker = YoloTracker(
    ARTIFACT_YOLO_MODEL_PATH,
    conf_threshold=ARTIFACT_YOLO_CONFIDENCE,
)
artifact_focus = ArtifactFocusSmoother(window_size=10, min_hits=5)
tuio_artifacts = load_tuio_artifacts(ARTIFACTS_JSON_PATH)
active_user_name = "guest"

# signup_flow is non-None only when BT found no known user
signup_flow: FaceSignupFlow | None = None
# cached login payload — re-sent on reconnect if C# dropped during signup
pending_login_payload: str | None = None

if login_message is not None:
    active_user_name = str(login_message.get("name", "guest")).strip() or "guest"
    context_store.ensure_user(
        active_user_name, str(login_message.get("Profile", "")).strip()
    )
elif address is not None:
    # BT device found but not in users.json try face login / signup
    signup_flow = FaceSignupFlow(face_recognizer, OBJECTS_DIR, USERS_JSON_PATH)
    active_user_name = f"guest_{normalize_mac(address).replace(':', '')}"
    context_store.ensure_user(active_user_name)
else:
    # No BT at all try face login / signup
    signup_flow = FaceSignupFlow(face_recognizer, OBJECTS_DIR, USERS_JSON_PATH)
    context_store.ensure_user(active_user_name)

# Clean up any previously persisted invalid artifacts.
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
failed_camera_reads = 0
last_camera_reset = 0.0

# Adaptive interface: live-feed (OpenCV preview window) visibility
# The C# GUI sends "CAMERA:ON" / "CAMERA:OFF" based on the logged-in user
# age profile (Child & Senior modes hide the window; Teen/Adult show it).
# Default is OFF until the first command arrives so we don't briefly flash a
# window for a Child user.
camera_window_visible = False
_camera_window_created = False


def set_camera_window(visible: bool) -> None:
    """Toggle whether the camera feed is shown on the Output window."""
    global camera_window_visible, _camera_window_created
    if visible and not _camera_window_created:
        cv2.namedWindow("Output", cv2.WINDOW_NORMAL)
        cv2.resizeWindow("Output", 960, 640)
        _camera_window_created = True
    camera_window_visible = visible


# Don't create the window until C# sends CAMERA:ON.


def emit_transcription(connection, text: str) -> None:
    """Send a TRANS: line to the C# GUI for the live-transcription panel."""
    if not text:
        return
    try:
        send_socket_message(connection, "TRANS:" + str(text).strip())
    except Exception:
        pass
user_login = 0
flag_bluetooth = 0
socket_buffer = ""
last_tuio_marker_id = None
tuio_last_seen = 0.0
csharp_context_last_seen = 0.0
artifact_frame_counter = 0
last_artifact_detections = []
while cap.isOpened():
    ret, frame = cap.read()
    if not ret or frame is None:
        # Keep UI responsive and attempt a soft camera reset on repeated failures.
        cv2.waitKey(1)
        failed_camera_reads += 1
        if failed_camera_reads >= 30 and (time.monotonic() - last_camera_reset) > 2.0:
            last_camera_reset = time.monotonic()
            failed_camera_reads = 0
            try:
                cap.release()
            except Exception:
                pass
            cap = cv2.VideoCapture(0)
        else:
            time.sleep(0.01)
        continue
    failed_camera_reads = 0
    msg = ""

    # Accept C# connection asynchronously if not connected yet
    if conn is None:
        try:
            client_connection, client_address = soc.accept()
            conn = client_connection
            addr = client_address
            conn.setblocking(False)
            print(f"[SOCKET] C# GUI connected from {addr[0]}:{addr[1]}")
        except socket.timeout:
            pass

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
        # Re-send login payload if we already completed login/signup before the disconnect
        if pending_login_payload is not None:
            print(f"[LOGIN] C# reconnected — re-sending cached login payload")
            if send_socket_message(conn, pending_login_payload):
                user_login = 1
                print(f"[LOGIN] Re-sent login payload successfully")
            else:
                print(f"[LOGIN] Re-send failed, will retry next iteration")
        continue

    for line in incoming_lines:
        # Adaptive UI command from C#: toggle the OpenCV live-feed window.
        if line.startswith("CAMERA:"):
            value = line.split(":", 1)[1].strip().upper()
            want_visible = value in ("ON", "1", "TRUE", "VISIBLE", "SHOW")
            set_camera_window(want_visible)
            print(f"[UI] Live feed -> {'ON' if want_visible else 'OFF'}")
            emit_transcription(
                conn, f"Live feed {'enabled' if want_visible else 'disabled'} by UI"
            )
            continue
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
                    emit_transcription(conn, f"Marker {marker_id}: {artifact_name}")
                    if marker_id == 110:
                        # Admin marker: skip face signup/login flow in Python.
                        signup_flow = None
                        user_login = 1
                        active_user_name = "admin"
                        context_store.ensure_user(active_user_name, "admin")
                        print("[LOGIN] Admin marker detected — skipping face signup")
        else:
            # Allow the C# client to update context when user opens a single-artifact page.
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
                    elif msg_type in {"admin_login"}:
                        signup_flow = None
                        user_login = 1
                        active_user_name = str(msg_obj.get("name", "admin")).strip()
                        context_store.ensure_user(active_user_name, "admin")
                        print(f"[LOGIN] Admin login via C# button — {active_user_name}")
                    elif msg_type in {"admin_keyboard_request"}:
                        admin_keyboard_request_id = str(msg_obj.get("id", "")).strip()
                        admin_keyboard_prompt = str(msg_obj.get("prompt", "")).strip()
                        admin_keyboard_initial_text = str(msg_obj.get("initial", ""))
                        requested_mode = str(msg_obj.get("mode", "alpha")).strip().lower()
                        admin_keyboard_mode = "num" if requested_mode == "num" else "alpha"
                        admin_keyboard = HandKeyboard(
                            mode=admin_keyboard_mode,
                            frame_w=480,
                            frame_h=320,
                        )
                        admin_keyboard.text = admin_keyboard_initial_text
                        set_camera_window(True)
                        print(
                            f"[ADMIN-KEYBOARD] Open request id={admin_keyboard_request_id} prompt={admin_keyboard_prompt}"
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
                pending_login_payload = message_payload
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
            # Unknown MAC and no face flow send raw MAC as guest
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

    #Face login / signup flow (runs when BT found no known user)
    if user_login == 1 or active_user_name == "admin":
        signup_flow = None

    if signup_flow is not None and not signup_flow.done:
        f_frame_signup = cv2.resize(frame, (640, 480))
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
            annotated_signup, None, fx=1.5, fy=1.5, interpolation=cv2.INTER_LINEAR
        )
        cv2.imshow("Output", display_signup)
        if cv2.waitKey(1) == ord("q"):
            break

        # Handle completion immediately dont wait for next iteration
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
            # payload shape as a Bluetooth login (name, age, gender, Profile, themeMode, favorites, etc.)
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

            # If conn dropped during the capture phase, wait for C# to reconnect
            if conn is None:
                print("[LOGIN] Socket disconnected during signup — waiting for C# to reconnect...")
                conn, addr = wait_for_csharp_client(soc)
                conn.setblocking(False)
                socket_buffer = ""

            ok = send_socket_message(conn, payload_str)
            if not ok:
                print(f"[LOGIN] Send failed for {active_user_name}, retrying once...")
                import time as _t; _t.sleep(0.2)
                ok = send_socket_message(conn, payload_str)
            if ok:
                pending_login_payload = payload_str
                print(f"[LOGIN] Payload sent successfully for {active_user_name}")
                context_store.log_event(
                    build_event("face_login", {"user": active_user_name, "source": status})
                )
            else:
                print(f"[LOGIN] Send failed for {active_user_name} after retry")
        else:
            print("[SIGNUP] User cancelled — continuing as guest")

        signup_flow = None
        user_login  = 1
    try:

        f_frame = cv2.resize(frame, (480, 320))
        frame_rgb = cv2.cvtColor(f_frame, cv2.COLOR_BGR2RGB)
        frame_count += 1
        results = holistic.process(frame_rgb)
        annotated_image = f_frame.copy()
        image_height, image_width, _ = frame_rgb.shape

        if admin_keyboard is not None:
            keyboard_hand = results.right_hand_landmarks or results.left_hand_landmarks
            index_tip = None
            middle_tip = None
            if keyboard_hand is not None:
                lm = keyboard_hand.landmark
                index_tip = (int(lm[8].x * image_width), int(lm[8].y * image_height))
                middle_tip = (int(lm[12].x * image_width), int(lm[12].y * image_height))

            admin_keyboard.update(
                annotated_image,
                index_tip,
                middle_tip,
                results.left_hand_landmarks,
            )

            if admin_keyboard_prompt:
                cv2.putText(
                    annotated_image,
                    admin_keyboard_prompt[:46],
                    (12, 24),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (255, 255, 255),
                    1,
                    cv2.LINE_AA,
                )

            mp_drawing.draw_landmarks(
                annotated_image, results.left_hand_landmarks, mp_holistic.HAND_CONNECTIONS
            )
            mp_drawing.draw_landmarks(
                annotated_image, results.right_hand_landmarks, mp_holistic.HAND_CONNECTIONS
            )

            display_keyboard = cv2.resize(
                annotated_image, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_LINEAR
            )
            cv2.imshow("Output", display_keyboard)

            if admin_keyboard.confirmed:
                payload = json.dumps(
                    {
                        "type": "admin_keyboard_result",
                        "id": admin_keyboard_request_id,
                        "text": admin_keyboard.text,
                        "cancelled": False,
                    }
                )
                send_socket_message(conn, "KEYBOARD_RESULT:" + payload)
                print(f"[ADMIN-KEYBOARD] Result sent id={admin_keyboard_request_id}")
                admin_keyboard = None
                admin_keyboard_request_id = ""
                admin_keyboard_prompt = ""
                admin_keyboard_initial_text = ""

            if cv2.waitKey(1) == ord("q"):
                break
            continue

        artifact_frame_counter += 1
        if artifact_frame_counter >= ARTIFACT_YOLO_INTERVAL:
            artifact_frame_counter = 0
            last_artifact_detections = artifact_tracker.detect_artifacts(f_frame)
            stable_artifact = artifact_focus.update(last_artifact_detections)

            if stable_artifact is not None:
                now = time.monotonic()
                external_focus_recent = (
                    now - tuio_last_seen < TUIO_PRIORITY_SECONDS
                    or now - csharp_context_last_seen < CSHARP_CONTEXT_PRIORITY_SECONDS
                )
                if not external_focus_recent:
                    artifact_name = stable_artifact["artifact"]
                    category = stable_artifact["category"]
                    context_store.update_context(
                        active_user_name,
                        current_artifact=artifact_name,
                        current_category=category,
                        last_object=artifact_name,
                    )
                    context_store.record_artifact_opened(active_user_name, artifact_name)
                    focus_payload = json.dumps(
                        {
                            "type": "artifact_focus",
                            "source": "yolo11s",
                            "artifact": artifact_name,
                            "category": category,
                            "label": stable_artifact["label"],
                            "confidence": stable_artifact["confidence"],
                        }
                    )
                    send_socket_message(conn, focus_payload)
                    print(
                        f"[YOLO11] Artifact focus -> {artifact_name} "
                        f"({stable_artifact['confidence']:.2f})"
                    )
                    emit_transcription(conn, f"Detected artifact: {artifact_name}")

        draw_artifact_detections(annotated_image, last_artifact_detections)

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
                    expression_log_until = time.monotonic() + 0.3
                    emit_transcription(
                        conn, f"Expression: {last_emotion} (gaze: {last_gaze_zone})"
                    )

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

        #Face detection + recognition (DeepFace, no YOLO)
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

        # Static hand-shape detection (Mute / DarkMode)
        active_hand = None
        if results.right_hand_landmarks:
            active_hand = results.right_hand_landmarks
        elif results.left_hand_landmarks:
            active_hand = results.left_hand_landmarks

        if active_hand and time.monotonic() > shape_cooldown_time:
            norm = normalize_landmarks(active_hand)
            user_shape_name, user_shape_score = recognize_hand_shape(
                norm, hand_shapes, threshold=0.45
            )

            # Diagnostic: compute best-match distances even if they dont
            # meet the recognition threshold so we can see how close the
            # current hand is to any admin/user template.
            def _best_dist(points, templates):
                best = float("inf")
                if not points or not templates:
                    return best
                for t in templates.values():
                    if len(t) != len(points):
                        continue
                    d = 0.0
                    for a, b in zip(points, t):
                        d += (a - b) ** 2
                    d = math.sqrt(d)
                    if d < best:
                        best = d
                return best

            best_user_dist = _best_dist(norm, hand_shapes)

            admin_shape_name = None
            admin_shape_score = 0.0
            if admin_hand_shapes:
                # Admin gestures are recognized alongside the normal bank so
                # the dashboard can react even when the user is not in a
                # separate admin-only mode.
                # Try matching the normalized points
                admin_shape_name, admin_shape_score = recognize_hand_shape(
                    norm, admin_hand_shapes, threshold=0.55
                )

                if norm:
                    mirrored = []
                    for i in range(0, len(norm), 2):
                        mirrored.append(-norm[i])
                        mirrored.append(norm[i + 1])
                    m_name, m_score = recognize_hand_shape(
                        mirrored, admin_hand_shapes, threshold=0.55
                    )
                    if m_name and m_score > admin_shape_score:
                        admin_shape_name = m_name
                        admin_shape_score = m_score

                best_admin_dist = _best_dist(norm, admin_hand_shapes)
                if best_admin_dist < 1.5 or best_user_dist < 1.5:
                    print(
                        f"[GESTURE-DEBUG] best_user_dist={best_user_dist:.3f} best_admin_dist={best_admin_dist:.3f} user_match={user_shape_name}:{user_shape_score:.3f} admin_match={admin_shape_name}:{admin_shape_score:.3f}"
                    )

            chosen_kind = None
            chosen_name = None
            chosen_score = 0.0

            if user_shape_name and user_shape_score > 0.35:
                chosen_kind = "user"
                chosen_name = user_shape_name
                chosen_score = user_shape_score

            if admin_shape_name and admin_shape_score > 0.25 and admin_shape_score >= chosen_score:
                chosen_kind = "admin"
                chosen_name = admin_shape_name
                chosen_score = admin_shape_score

            if chosen_name:
                msg = chosen_name
                shape_cooldown_time = time.monotonic() + 1.5
                if chosen_kind == "admin":
                    print(
                        f"[GESTURE] Shape detected (admin): {chosen_name} (score={chosen_score:.2f})"
                    )
                else:
                    print(
                        f"[GESTURE] Shape detected (user): {chosen_name} (score={chosen_score:.2f})"
                    )

        #Dynamic trajectory (Index Finger)
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

        # Update top-right feedback label for any gesture or hand shape that fired
        if msg:
            show_gesture_feedback(msg, duration=3.0)

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
        is_admin_gesture = msg in admin_hand_shapes
        if msg != "" and (msg != old_msg or is_admin_gesture):  # admin shapes repeat after cooldown
            emit_transcription(conn, f"Gesture: {msg}")
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

        if msg == "exit":
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
