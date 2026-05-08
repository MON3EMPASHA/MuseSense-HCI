import logging
import os
import warnings

# Configure TensorFlow logging before importing libraries that may initialize it.
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")

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
from movements import recognizer
from context_store import ContextStore, apply_gesture_action
from event_protocol import build_event, event_to_line
from object_tracking import YoloTracker
from expression_tracker import ExpressionTracker
from gaze_tracker import GazeTracker

SERVER_HOST = "0.0.0.0"
SERVER_PORT = 5001
TUIO_NOTE = "Remember to start your TUIO simulator/tracker on port 3333"
PHONE_CAMERA_URL = ""
PHONE_BT_NAME = "Phone"
USERS_JSON_PATH = Path("TUIO11_NET-master") / "bin" / "Debug" / "users.json"


# user / bluetooth helpers moved to users.py


def send_socket_message(connection: socket.socket, payload: str) -> None:
    if not payload:
        return

    connection.sendall(f"{payload}\n".encode("utf-8"))


# gesture helper functions moved to gestures.py


soc = socket.socket()
hostname = "localhost"
port = 5000
soc.bind((hostname, port))
soc.listen(5)
Allpoints = []
mp_pose = mp.solutions.pose
conn, addr = soc.accept()
print("Device Connected")
old_msg = ""
mp_holistic = mp.solutions.holistic
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles
holistic = mp_holistic.Holistic(
    static_image_mode=False, min_detection_confidence=0.65, model_complexity=1
)

face_ids = {}
frame_count = 0
object_frame_counter = 0
last_object_label = ""
analysis_frame_counter = 0
last_emotion = ""
last_gaze_zone = ""
latest_expression = None
gesture_points = []
circle_points = []
expression_log_until = 0.0
last_expression_signature = ""


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
        print("[BT] Scanning for Bluetooth devices (8 seconds)…")
        users_by_mac = load_users_by_mac(USERS_JSON_PATH)
        print(f"[BT] Known users in system: {len(users_by_mac)}")

        try:
            print("[BT] Calling bluetooth.discover_devices()...")
            devices = bluetooth.discover_devices(lookup_names=True, duration=8)
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
yolo_tracker = YoloTracker()
expression_tracker = ExpressionTracker()
gaze_tracker = GazeTracker()
active_user_name = "guest"

if login_message is not None:
    active_user_name = str(login_message.get("name", "guest")).strip() or "guest"
    context_store.ensure_user(
        active_user_name, str(login_message.get("Profile", "")).strip()
    )
elif address is not None:
    active_user_name = f"guest_{normalize_mac(address).replace(':', '')}"
    context_store.ensure_user(active_user_name)

cap = cv2.VideoCapture(0)
cv2.namedWindow("Output", cv2.WINDOW_NORMAL)
cv2.resizeWindow("Output", 960, 640)
user_login = 0
flag_bluetooth = 0
while cap.isOpened():
    ret, frame = cap.read()
    if not ret or frame is None:
        continue
    msg = ""
    if user_login == 0:
        if login_message is not None:
            message_payload = json.dumps(login_message)
            print("Sending login payload:", message_payload)
            send_socket_message(conn, message_payload)
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
        elif address is not None:
            print("Sending MAC:", address)
            send_socket_message(conn, address)
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
        image_hight, image_width, _ = frame.shape

        analysis_frame_counter += 1
        if analysis_frame_counter % 15 == 0:
            analysis_frame_counter = 0
            expression = expression_tracker.analyze(frame_rgb)
            if expression is not None:
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
                    context_store.update_category_score(
                        active_user_name, expression["emotion"], expression["valence"]
                    )

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
                    print("[EVENT]", event_to_line(adaptive_event))

        object_frame_counter += 1
        if object_frame_counter % 18 == 0:
            object_frame_counter = 0
            detection = yolo_tracker.detect_primary(f_frame)
            if detection is not None:
                x1, y1, x2, y2 = detection["bbox"]
                label = str(detection["label"])
                confidence = detection["confidence"]
                cv2.rectangle(annotated_image, (x1, y1), (x2, y2), (0, 180, 255), 2)
                cv2.putText(
                    annotated_image,
                    f"YOLO: {label} {confidence}",
                    (x1, max(y1 - 10, 20)),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    (0, 180, 255),
                    2,
                )

                if label != last_object_label:
                    last_object_label = label
                    context_store.update_context(
                        active_user_name,
                        current_artifact=label,
                        current_category=label,
                        last_object=label,
                    )
                    object_event = build_event(
                        "object_tracking",
                        {
                            "user": active_user_name,
                            "object": detection,
                        },
                    )
                    context_store.log_event(object_event)
                    print("[EVENT]", event_to_line(object_event))

        if results.pose_landmarks is not None:
            right_wrist_x = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].x
                * image_width
            )
            right_wrist_y = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].y
                * image_hight
            )
            left_wrist_x = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_WRIST].x
                * image_width
            )
            left_wrist_y = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_WRIST].y
                * image_hight
            )

            gesture_points.append(Point(right_wrist_x, right_wrist_y, 1))
            gesture_points.append(Point(left_wrist_x, left_wrist_y, 1))
            circle_points.append(Point(right_wrist_x, right_wrist_y, 1))

        if frame_count % 30 == 0:
            frame_count = 0
            if gesture_points and is_gesture_significant(gesture_points):
                result = recognizer.recognize(gesture_points)
                if result[0] is not None:
                    recognized_gesture = str(result[0]).strip()
                    recognized_score = float(result[1])
                    print(result)

                    if recognized_gesture == "Circle":
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
        cv2.imshow("Output", display_image)
        # logic to send msg to unity
        if msg != "" and msg != old_msg:  # only send when there's actually something
            context_snapshot = context_store.get_context_snapshot(active_user_name)
            current_item_id = (
                context_snapshot.get("current_artifact")
                or context_snapshot.get("last_object")
                or "current_artifact"
            )
            current_category = (
                context_snapshot.get("current_category")
                or context_snapshot.get("last_object")
                or "general"
            )
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
            print("[EVENT]", event_to_line(context_event))

            send_socket_message(conn, msg)
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

cap.release()
cv2.destroyAllWindows()
