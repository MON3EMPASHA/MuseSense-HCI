import cv2
import mediapipe as mp
import numpy as np
import socket
import pickle
import socket
import bluetooth
from dollarpy import Point
from movements import recognizer

SERVER_HOST = "0.0.0.0"
SERVER_PORT = 5001
TUIO_NOTE = "Remember to start your TUIO simulator/tracker on port 3333"
PHONE_CAMERA_URL = ""
PHONE_BT_NAME = "Phone"

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

    def scan_bluetooth(self) -> str | None:
        print("[BT] Scanning for Bluetooth devices (8 s)…")
        try:
            devices = bluetooth.discover_devices(lookup_names=True, duration=8)
        except Exception as e:
            print(f"[BT] Error: {e}")
            return None

        if len(devices) > 0:
            print("[BT] Discovered devices:")
            selected_addr = None

            for index, (addr, name) in enumerate(devices, start=1):
                display_name = name if name else "Unknown"
                print(f"  {index}. Name: {display_name} | MAC: {addr}")

                if selected_addr is None and name == PHONE_BT_NAME:
                    selected_addr = addr

            if selected_addr is None:
                selected_addr = devices[0][0]

            print(f"[BT] Selected MAC to send: {selected_addr}")
            return selected_addr

        print("[BT] No devices found")
        return None


server = HCIServer()
address = None
address = server.scan_bluetooth()
cap = cv2.VideoCapture(0)
user_login = 0
flag_bluetooth = 0
while cap.isOpened():
    ret, frame = cap.read()
    if not ret or frame is None:
        continue
    msg = ""
    if user_login == 0 and address is not None:
        print("Sending MAC:", address)

        conn.send(address.encode("utf-8"))
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

        if results.pose_landmarks is not None:
            x = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].x
                * image_width
            )
            y = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].y
                * image_hight
            )
            Allpoints.append(Point(x, y, 1))

            x = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_WRIST].x
                * image_width
            )
            y = int(
                results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_WRIST].y
                * image_hight
            )
            Allpoints.append(Point(x, y, 1))

        if frame_count % 30 == 0:
            frame_count = 0
            if Allpoints:
                result = recognizer.recognize(Allpoints)
                if result[0] != None:
                    print(result)
                    msg += result[0]

            Allpoints.clear()
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
        cv2.imshow("Output", annotated_image)
        # logic to send msg to unity
        if msg != "" and msg != old_msg:  # only send when there's actually something
            conn.send(msg.encode("utf-8"))

        old_msg = msg

        if msg == pickle.dumps("exit"):
            break
    except Exception as e:
        print(e)
    if cv2.waitKey(1) == ord("q"):
        break

cap.release()
cv2.destroyAllWindows()
