from __future__ import annotations

from collections import Counter, deque

import cv2
import mediapipe as mp


class ExpressionTracker:
    def __init__(self):
        self.face_mesh = mp.solutions.face_mesh.FaceMesh(
            static_image_mode=False,
            max_num_faces=1,
            refine_landmarks=True,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5,
        )
        self.mp_face_mesh = mp.solutions.face_mesh
        self._recent_results: deque[dict] = deque(maxlen=5)
        self._last_output: dict | None = None

    def _smooth_analysis(self) -> dict | None:
        if len(self._recent_results) < 3:
            return None

        emotion_counts = Counter(item["emotion"] for item in self._recent_results)
        gaze_counts = Counter(item["gaze_zone"] for item in self._recent_results)

        stable_emotion, emotion_count = emotion_counts.most_common(1)[0]
        stable_gaze, gaze_count = gaze_counts.most_common(1)[0]

        if emotion_count < 3 and gaze_count < 3:
            return None

        latest = dict(self._recent_results[-1])
        latest["emotion"] = stable_emotion
        latest["gaze_zone"] = stable_gaze
        latest["emotion_stability"] = round(emotion_count / len(self._recent_results), 3)
        latest["gaze_stability"] = round(gaze_count / len(self._recent_results), 3)
        latest["window_size"] = len(self._recent_results)
        return latest

    def analyze(self, frame_rgb) -> dict | None:
        results = self.face_mesh.process(frame_rgb)
        if not results.multi_face_landmarks:
            return self._last_output

        face_landmarks = results.multi_face_landmarks[0].landmark
        image_height, image_width, _ = frame_rgb.shape

        def point(index: int) -> tuple[float, float]:
            landmark = face_landmarks[index]
            return landmark.x * image_width, landmark.y * image_height

        mouth_left = point(61)
        mouth_right = point(291)
        mouth_top = point(13)
        mouth_bottom = point(14)
        left_cheek = point(234)
        right_cheek = point(454)
        forehead = point(10)
        chin = point(152)

        mouth_width = max(mouth_right[0] - mouth_left[0], 1.0)
        mouth_open = max(mouth_bottom[1] - mouth_top[1], 0.0)
        face_width = max(abs(right_cheek[0] - left_cheek[0]), 1.0)
        face_height = max(abs(chin[1] - forehead[1]), 1.0)
        mouth_curve = ((mouth_top[1] + mouth_bottom[1]) / 2.0) - ((mouth_left[1] + mouth_right[1]) / 2.0)

        mouth_width_ratio = mouth_width / face_width
        mouth_open_ratio = mouth_open / face_height
        mouth_curve_ratio = mouth_curve / face_height

        if mouth_open_ratio >= 0.065:
            emotion = "surprised"
        elif mouth_width_ratio >= 0.295 and mouth_curve_ratio >= 0.0:
            emotion = "happy"
        elif mouth_width_ratio <= 0.285 and mouth_open_ratio <= 0.045:
            emotion = "neutral"
        else:
            emotion = "focused"

        left_eye_outer = point(33)
        left_eye_inner = point(133)
        right_eye_outer = point(362)
        right_eye_inner = point(263)

        left_iris = point(468)
        right_iris = point(473)

        left_eye_center_x = (left_eye_outer[0] + left_eye_inner[0]) / 2.0
        right_eye_center_x = (right_eye_outer[0] + right_eye_inner[0]) / 2.0
        eye_center_x = (left_eye_center_x + right_eye_center_x) / 2.0
        iris_center_x = (left_iris[0] + right_iris[0]) / 2.0

        gaze_delta = (iris_center_x - eye_center_x) / max(image_width, 1)

        if gaze_delta < -0.02:
            gaze_zone = "left"
        elif gaze_delta > 0.02:
            gaze_zone = "right"
        else:
            gaze_zone = "center"

        if emotion == "happy":
            valence = 0.9
        elif emotion == "surprised":
            valence = 0.7
        elif emotion == "focused":
            valence = 0.5
        else:
            valence = 0.2

        raw_analysis = {
            "emotion": emotion,
            "raw_emotion": emotion,
            "mouth_width_ratio": round(mouth_width_ratio, 3),
            "mouth_curve_ratio": round(mouth_curve_ratio, 3),
            "mouth_open_ratio": round(mouth_open_ratio, 3),
            "mouth_open": round(mouth_open, 3),
            "mouth_curve": round(mouth_curve, 3),
            "gaze_zone": gaze_zone,
            "raw_gaze_zone": gaze_zone,
            "valence": valence,
            "window_size": 1,
        }

        self._recent_results.append(raw_analysis)
        smoothed_analysis = self._smooth_analysis()
        if smoothed_analysis is not None:
            self._last_output = smoothed_analysis
            return smoothed_analysis

        self._last_output = raw_analysis
        return raw_analysis

    def draw_overlay(self, frame, analysis: dict | None) -> None:
        if analysis is None:
            return

        text = (
            f"Emotion: {analysis['emotion']} | Gaze: {analysis['gaze_zone']}"
            f" | Window: {analysis.get('window_size', 1)}"
        )
        detail_text = (
            f"Raw: {analysis.get('raw_emotion', analysis['emotion'])} / "
            f"{analysis.get('raw_gaze_zone', analysis['gaze_zone'])}"
            f" | WidthR: {analysis.get('mouth_width_ratio', 0)}"
            f" | OpenR: {analysis.get('mouth_open_ratio', 0)}"
        )
        cv2.putText(
            frame,
            text,
            (20, 95),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.65,
            (255, 200, 0),
            2,
        )
        cv2.putText(
            frame,
            detail_text,
            (20, 125),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            (255, 230, 120),
            2,
        )

    def draw_overlay(self, frame, analysis: dict | None) -> None:
        if analysis is None:
            return

        text = (
            f"Emotion: {analysis['emotion']} | Gaze: {analysis['gaze_zone']}"
            f" | Window: {analysis.get('window_size', 1)}"
        )
        detail_text = (
            f"Raw: {analysis.get('raw_emotion', analysis['emotion'])} / "
            f"{analysis.get('raw_gaze_zone', analysis['gaze_zone'])}"
            f" | Smile: {analysis['smile_ratio']} | Curve: {analysis.get('mouth_curve', 0)}"
            f" | Signal: {analysis.get('smile_signal', analysis['smile_ratio'])}"
        )
        cv2.putText(
            frame,
            text,
            (20, 95),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.65,
            (255, 200, 0),
            2,
        )
        cv2.putText(
            frame,
            detail_text,
            (20, 125),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            (255, 230, 120),
            2,
        )
