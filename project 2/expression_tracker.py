from __future__ import annotations

import time
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

        self._emo_feature_keys = (
            "smile_curve",   # corners-above-midline / face_height
            "mouth_open",    # mouth_h / face_h
            "mouth_wide",    # mouth_w / face_w
            "brow_height",   # brow-to-eye-top gap / face_h
            "eye_open",      # eye lid gap / face_h
        )
        self._emo_samples: dict[str, deque[float]] = {
            k: deque(maxlen=150) for k in self._emo_feature_keys
        }
        self._emo_baselines: dict[str, float] = {}
        self._emo_bootstrap_count: int = 0
        self._emo_bootstrap_target: int = 30   # ~2 s before we trust baselines

        self._emo_last_classified: str = "neutral"

        #Gaze state (horizontal)
        self._gaze_h_samples: deque[float] = deque(maxlen=200)
        self._gaze_h_baseline: float | None = None
        self._gaze_h_bootstrap_count: int = 0
        self._gaze_h_bootstrap_target: int = 30

        self._gaze_h_ema: float = 0.0
        self._gaze_ema_alpha: float = 0.80

        self._gaze_min_samples_to_classify: int = 10
        self._gaze_threshold: float = 0.050
        self._gaze_center_capture_threshold: float = 0.020

        self._gaze_pos_threshold: float = 0.050
        self._gaze_neg_threshold: float = 0.050
        self._gaze_pos_peaks: deque[float] = deque(maxlen=15)
        self._gaze_neg_peaks: deque[float] = deque(maxlen=15)
        self._gaze_peak_noise_floor: float = 0.015
        self._gaze_adapt_thresh_min: float = 0.020
        self._gaze_adapt_thresh_max: float = 0.055
        self._gaze_adapt_thresh_factor: float = 0.50

        self._gaze_invert_lr: bool = True

        # Gaze state (vertical)
        self._gaze_v_samples: deque[float] = deque(maxlen=200)
        self._gaze_v_baseline: float | None = None
        self._gaze_v_bootstrap_count: int = 0
        self._gaze_v_bootstrap_target: int = 30
        self._gaze_v_ema: float = 0.0
        self._gaze_v_threshold_up: float = 0.035
        self._gaze_v_threshold_down: float = 0.035

        # Head-motion detection
        self._last_nose_pos: tuple[float, float] | None = None
        self._head_motion_pause_until: float = 0.0
        self._head_motion_threshold_px: float = 8.0

        # Blink filter
        self._min_eye_openness: float = 0.10

        # Debug: log every zone transition
        self._last_logged_gaze_zone: str = ""

    def reset_gaze_calibration(self) -> None:
        self._gaze_h_samples.clear()
        self._gaze_h_baseline = None
        self._gaze_h_bootstrap_count = 0
        self._gaze_h_ema = 0.0
        self._gaze_v_samples.clear()
        self._gaze_v_baseline = None
        self._gaze_v_bootstrap_count = 0
        self._gaze_v_ema = 0.0
        self._last_nose_pos = None
        self._head_motion_pause_until = 0.0
        self._last_logged_gaze_zone = ""
        self._gaze_pos_peaks.clear()
        self._gaze_neg_peaks.clear()
        self._gaze_pos_threshold = self._gaze_threshold
        self._gaze_neg_threshold = self._gaze_threshold
        # Emotion baselines reset too they're per-user.
        for d in self._emo_samples.values():
            d.clear()
        self._emo_baselines.clear()
        self._emo_bootstrap_count = 0
        self._emo_last_classified = "neutral"

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
            return None

        face_landmarks = results.multi_face_landmarks[0].landmark
        image_height, image_width, _ = frame_rgb.shape

        def point(index: int) -> tuple[float, float]:
            landmark = face_landmarks[index]
            return landmark.x * image_width, landmark.y * image_height
        
        mouth_left   = point(61)
        mouth_right  = point(291)
        mouth_top    = point(13)
        mouth_bottom = point(14)
        left_cheek   = point(234)
        right_cheek  = point(454)
        forehead     = point(10)
        chin         = point(152)

        face_width  = max(abs(right_cheek[0] - left_cheek[0]), 1.0)
        face_height = max(abs(chin[1] - forehead[1]), 1.0)

        # mouth
        mouth_w_px   = max(mouth_right[0] - mouth_left[0], 1.0)
        mouth_h_px   = max(mouth_bottom[1] - mouth_top[1], 0.0)
        mouth_mid_y  = (mouth_top[1] + mouth_bottom[1]) / 2.0
        corner_avg_y = (mouth_left[1] + mouth_right[1]) / 2.0
        # Image-Y increases downward, so corners ABOVE mid_y → smile, BELOW → frown.
        smile_curve  = (mouth_mid_y - corner_avg_y) / face_height
        mouth_open_v = mouth_h_px / face_height
        mouth_wide_v = mouth_w_px / face_width

        # eyebrows (brow centre to eye-top gap, averaged across both brows)
        try:
            right_brow    = point(105)
            left_brow     = point(334)
            right_eye_top = point(159)
            left_eye_top  = point(386)
            brow_gap_r    = abs(right_brow[1] - right_eye_top[1])
            brow_gap_l    = abs(left_brow[1]  - left_eye_top[1])
            brow_height_v = ((brow_gap_r + brow_gap_l) / 2.0) / face_height
        except Exception:
            brow_height_v = 0.07

        # eye openness (lid-to-lid gap, averaged across both eyes)
        try:
            right_eye_bot = point(145)
            left_eye_bot  = point(374)
            eye_h_r = abs(right_eye_top[1] - right_eye_bot[1])
            eye_h_l = abs(left_eye_top[1]  - left_eye_bot[1])
            eye_open_v = ((eye_h_r + eye_h_l) / 2.0) / face_height
        except Exception:
            eye_open_v = 0.03

        cur_features = {
            "smile_curve": smile_curve,
            "mouth_open":  mouth_open_v,
            "mouth_wide":  mouth_wide_v,
            "brow_height": brow_height_v,
            "eye_open":    eye_open_v,
        }

        # update baselines: bootstrap or only-when-neutra
        update_baseline_now = (
            self._emo_bootstrap_count < self._emo_bootstrap_target
            or self._emo_last_classified == "neutral"
        )
        if update_baseline_now:
            for k in self._emo_feature_keys:
                self._emo_samples[k].append(cur_features[k])
            if self._emo_bootstrap_count < self._emo_bootstrap_target:
                self._emo_bootstrap_count += 1

        # recompute medians
        for k in self._emo_feature_keys:
            d = self._emo_samples[k]
            if len(d) >= 6:
                ssorted = sorted(d)
                self._emo_baselines[k] = ssorted[len(ssorted) // 2]

        # Defaults if no baseline yet
        defaults = {"smile_curve": 0.0, "mouth_open": 0.03,
                    "mouth_wide": 0.32, "brow_height": 0.07, "eye_open": 0.03}

        def b(key: str) -> float:
            return self._emo_baselines.get(key, defaults[key])

        d_smile = cur_features["smile_curve"] - b("smile_curve")
        d_open  = cur_features["mouth_open"]  - b("mouth_open")
        d_wide  = cur_features["mouth_wide"]  - b("mouth_wide")
        d_brow  = cur_features["brow_height"] - b("brow_height")
        d_eye   = cur_features["eye_open"]    - b("eye_open")

        # classify
        loosen = 0.7 if self._emo_last_classified != "neutral" else 1.0
        # Standard thresholds
        TH_SMILE_HAPPY  = 0.011 * loosen   # corners 1.1% face_h above baseline
        TH_WIDE_HAPPY   = 0.012 * loosen   # mouth 1.2% face_w wider
        TH_SMILE_SAD    = 0.009 * loosen   # corners 0.9% below baseline
        TH_BROW_SAD     = 0.005 * loosen   # brows lowered
        TH_OPEN_SURPR   = 0.030 * loosen   # mouth 3% face_h more open
        TH_BROW_SURPR   = 0.006 * loosen   # brows raised
        TH_EYE_SURPR    = 0.005 * loosen   # eyes wider open

        if self._emo_bootstrap_count < self._emo_bootstrap_target:
            emotion = "neutral"
        # SURPRISED mouth wide-open + raised brows
        elif d_open > TH_OPEN_SURPR and (d_brow > TH_BROW_SURPR or d_eye > TH_EYE_SURPR):
            emotion = "surprised"
        # HAPPY corners up + mouth widened
        elif d_smile > TH_SMILE_HAPPY and d_wide > TH_WIDE_HAPPY:
            emotion = "happy"
        # SAD corners down (negative smile_curve delta) + brows lowered
        elif d_smile < -TH_SMILE_SAD and d_brow < -TH_BROW_SAD:
            emotion = "sad"
        # SAD (looser) strong corner-down even without brow signal
        elif d_smile < -TH_SMILE_SAD * 1.3:
            emotion = "sad"
        else:
            emotion = "neutral"

        self._emo_last_classified = emotion

        # GAZE

        now = time.monotonic()

        def safe_points(indices: list[int]) -> list[tuple[float, float]]:
            pts: list[tuple[float, float]] = []
            for idx in indices:
                if idx < len(face_landmarks):
                    try:
                        pts.append(point(idx))
                    except Exception:
                        continue
            return pts

        def mean_pt(indices: list[int]) -> tuple[float, float] | None:
            pts = safe_points(indices)
            if not pts:
                return None
            return (
                sum(p[0] for p in pts) / len(pts),
                sum(p[1] for p in pts) / len(pts),
            )

        # Iris centers
        iris_imgL = mean_pt([468, 469, 470, 471, 472])  # user's right eye
        iris_imgR = mean_pt([473, 474, 475, 476, 477])  # user's left eye

        # Eye corners (image coords)
        try:
            L_outer = point(33)   # image-LEFT eye, outer corner
            L_inner = point(133)  # image-LEFT eye, inner (nose-side)
            R_inner = point(362)  # image-RIGHT eye, inner (nose-side)
            R_outer = point(263)  # image-RIGHT eye, outer corner
            L_top = point(159)
            L_bot = point(145)
            R_top = point(386)
            R_bot = point(374)
        except Exception:
            return None

        def eye_metrics(
            iris: tuple[float, float] | None,
            x_a: float,
            x_b: float,
            y_top: float,
            y_bot: float,
        ) -> tuple[float | None, float | None, float | None]:
            """Return (horizontal_ratio, vertical_ratio, openness_ratio) or (None, None, None)."""
            if iris is None:
                return None, None, None
            x_min = min(x_a, x_b)
            x_max = max(x_a, x_b)
            eye_w = max(x_max - x_min, 1.0)
            eye_h = max(abs(y_bot - y_top), 1.0)
            h_r = (iris[0] - x_min) / eye_w
            h_r = max(0.0, min(1.0, h_r))
            v_r = (iris[1] - min(y_top, y_bot)) / eye_h
            v_r = max(0.0, min(1.0, v_r))
            return h_r, v_r, eye_h / eye_w

        h_r_L, v_r_L, open_L = eye_metrics(iris_imgL, L_outer[0], L_inner[0], L_top[1], L_bot[1])
        h_r_R, v_r_R, open_R = eye_metrics(iris_imgR, R_inner[0], R_outer[0], R_top[1], R_bot[1])

        h_ratios = [r for r in (h_r_L, h_r_R) if r is not None]
        h_ratio = sum(h_ratios) / len(h_ratios) if h_ratios else None

        v_ratios = [r for r in (v_r_L, v_r_R) if r is not None]
        v_ratio = sum(v_ratios) / len(v_ratios) if v_ratios else None

        opennesses = [o for o in (open_L, open_R) if o is not None]
        eye_openness = sum(opennesses) / len(opennesses) if opennesses else None

        # Head-motion detection (nose tip displacement frame-to-frame)
        try:
            nose = point(1)
        except Exception:
            nose = None
        head_moved = False
        if nose is not None and self._last_nose_pos is not None:
            dx = nose[0] - self._last_nose_pos[0]
            dy = nose[1] - self._last_nose_pos[1]
            if (dx * dx + dy * dy) > (self._head_motion_threshold_px ** 2):
                head_moved = True
                self._head_motion_pause_until = now + 0.7
        if nose is not None:
            self._last_nose_pos = nose

        # Provisional delta using the current baseline (or 0.5 if no baseline yet)
        h_baseline = (
            self._gaze_h_baseline if self._gaze_h_baseline is not None else 0.5
        )
        provisional_delta = (h_ratio - h_baseline) if h_ratio is not None else 0.0

        frame_clean = (
            h_ratio is not None
            and not head_moved
            and now > self._head_motion_pause_until
            and eye_openness is not None
            and eye_openness >= self._min_eye_openness
        )
        if frame_clean:
            if self._gaze_h_bootstrap_count < self._gaze_h_bootstrap_target:
                self._gaze_h_samples.append(h_ratio)
                self._gaze_h_bootstrap_count += 1
            elif abs(provisional_delta) < self._gaze_center_capture_threshold:
                self._gaze_h_samples.append(h_ratio)

        # Recompute baseline (median) over the rolling window
        n = len(self._gaze_h_samples)
        if n >= 5:
            sorted_samples = sorted(self._gaze_h_samples)
            self._gaze_h_baseline = sorted_samples[n // 2]
        h_baseline = (
            self._gaze_h_baseline if self._gaze_h_baseline is not None else 0.5
        )

        # Final delta + EMA smoothing
        h_delta_raw = (h_ratio - h_baseline) if h_ratio is not None else 0.0
        self._gaze_h_ema = (
            self._gaze_ema_alpha * h_delta_raw
            + (1.0 - self._gaze_ema_alpha) * self._gaze_h_ema
        )
        h_delta = self._gaze_h_ema

        if h_delta > self._gaze_peak_noise_floor:
            self._gaze_pos_peaks.append(h_delta)
        elif h_delta < -self._gaze_peak_noise_floor:
            self._gaze_neg_peaks.append(abs(h_delta))

        def _peak_thresh(hist: deque[float]) -> float:
            if len(hist) < 3:
                return self._gaze_threshold
            ssorted = sorted(hist)
            median_peak = ssorted[len(ssorted) // 2]
            return max(
                self._gaze_adapt_thresh_min,
                min(self._gaze_adapt_thresh_max,
                    median_peak * self._gaze_adapt_thresh_factor),
            )

        self._gaze_pos_threshold = _peak_thresh(self._gaze_pos_peaks)
        self._gaze_neg_threshold = _peak_thresh(self._gaze_neg_peaks)

        # Vertical tracking (up/down)
        v_baseline = (
            self._gaze_v_baseline if self._gaze_v_baseline is not None else 0.5
        )
        if frame_clean:
            if self._gaze_v_bootstrap_count < self._gaze_v_bootstrap_target:
                self._gaze_v_samples.append(v_ratio)
                self._gaze_v_bootstrap_count += 1
            elif v_ratio is not None and abs(v_ratio - v_baseline) < self._gaze_center_capture_threshold:
                self._gaze_v_samples.append(v_ratio)
        nv = len(self._gaze_v_samples)
        if nv >= 5:
            sorted_v = sorted(self._gaze_v_samples)
            self._gaze_v_baseline = sorted_v[nv // 2]
        v_baseline = (
            self._gaze_v_baseline if self._gaze_v_baseline is not None else 0.5
        )
        v_delta_raw = (v_ratio - v_baseline) if v_ratio is not None else 0.0
        self._gaze_v_ema = (
            self._gaze_ema_alpha * v_delta_raw
            + (1.0 - self._gaze_ema_alpha) * self._gaze_v_ema
        )
        v_delta = self._gaze_v_ema

        #Combined 3x3 Classification
        valid = (
            h_ratio is not None
            and v_ratio is not None
            and n >= self._gaze_min_samples_to_classify
            and nv >= self._gaze_min_samples_to_classify
            and (eye_openness is None or eye_openness >= self._min_eye_openness)
        )

        if not valid:
            h_zone = "center"
            v_zone = "center"
        else:
            # Horizontal
            if h_delta > self._gaze_pos_threshold:
                h_zone = "right" if self._gaze_invert_lr else "left"
            elif h_delta < -self._gaze_neg_threshold:
                h_zone = "left" if self._gaze_invert_lr else "right"
            else:
                h_zone = "center"
            # Vertical
            if v_delta < -self._gaze_v_threshold_up:
                v_zone = "top"
            elif v_delta > self._gaze_v_threshold_down:
                v_zone = "bottom"
            else:
                v_zone = "center"

        gaze_zone = f"{v_zone}_{h_zone}"

        # Log on every zone change
        if gaze_zone != self._last_logged_gaze_zone:
            print(
                f"[GAZE] {self._last_logged_gaze_zone or '-'} -> {gaze_zone}  "
                f"h_ratio={None if h_ratio is None else round(h_ratio, 3)} "
                f"h_base={round(h_baseline, 3)} h_delta={round(h_delta, 4)} "
                f"v_ratio={None if v_ratio is None else round(v_ratio, 3)} "
                f"v_base={round(v_baseline, 3)} v_delta={round(v_delta, 4)} "
                f"thr+={round(self._gaze_pos_threshold, 3)} "
                f"thr-={round(self._gaze_neg_threshold, 3)} "
                f"samples_h={n} samples_v={nv} "
                f"open={None if eye_openness is None else round(eye_openness, 3)}"
            )
            self._last_logged_gaze_zone = gaze_zone

        if emotion == "happy":
            valence = 0.9
        elif emotion == "surprised":
            valence = 0.7
        elif emotion == "sad":
            valence = 0.2
        else:
            valence = 0.4

        raw_analysis = {
            "emotion": emotion,
            "raw_emotion": emotion,
            # Face-normalized features used by the new emotion detector
            "smile_curve":     round(smile_curve, 4),
            "mouth_open":      round(mouth_open_v, 4),
            "mouth_wide":      round(mouth_wide_v, 4),
            "brow_height":     round(brow_height_v, 4),
            "eye_open":        round(eye_open_v, 4),
            # Deltas from the per-user baseline (what the rules actually fire on)
            "emo_d_smile":     round(d_smile, 4),
            "emo_d_open":      round(d_open, 4),
            "emo_d_wide":      round(d_wide, 4),
            "emo_d_brow":      round(d_brow, 4),
            "emo_d_eye":       round(d_eye, 4),
            "emo_baseline_ok": self._emo_bootstrap_count >= self._emo_bootstrap_target,
            "gaze_ratio": None if h_ratio is None else round(h_ratio, 3),
            "gaze_baseline_ratio": round(h_baseline, 3),
            "gaze_delta": round(h_delta, 4),
            "gaze_v_ratio": None if v_ratio is None else round(v_ratio, 3),
            "gaze_v_baseline_ratio": round(v_baseline, 3),
            "gaze_v_delta": round(v_delta, 4),
            "gaze_threshold_pos": round(self._gaze_pos_threshold, 4),
            "gaze_threshold_neg": round(self._gaze_neg_threshold, 4),
            "gaze_pos_peaks_n": len(self._gaze_pos_peaks),
            "gaze_neg_peaks_n": len(self._gaze_neg_peaks),
            "gaze_baseline_samples": n,
            "gaze_bootstrap_count": self._gaze_h_bootstrap_count,
            "gaze_eye_openness": None if eye_openness is None else round(eye_openness, 3),
            "gaze_head_moved": bool(head_moved),
            "gaze_zone": gaze_zone,
            "raw_gaze_zone": gaze_zone,
            "valence": valence,
            "window_size": 1,
        }

        self._recent_results.append(raw_analysis)
        smoothed_analysis = self._smooth_analysis()
        if smoothed_analysis is not None:
            return smoothed_analysis

        return raw_analysis

    def draw_overlay(self, frame, analysis: dict | None) -> None:
        if analysis is None:
            return

        text = (
            f"Emotion: {analysis['emotion']} | Gaze: {analysis['gaze_zone']}"
            f" | Window: {analysis.get('window_size', 1)}"
        )
        # Emotion debug show per-baseline deltas (the values the rules use)
        baseline_tag = "" if analysis.get("emo_baseline_ok", False) else " [BOOT]"
        detail_text = (
            f"EMO {analysis.get('emotion')}{baseline_tag} "
            f"d_smile={analysis.get('emo_d_smile')} "
            f"d_open={analysis.get('emo_d_open')} "
            f"d_brow={analysis.get('emo_d_brow')} "
            f"d_eye={analysis.get('emo_d_eye')}"
        )
        gaze_debug_text = (
            f"GAZE ratio={analysis.get('gaze_ratio')} "
            f"base={analysis.get('gaze_baseline_ratio')} "
            f"delta={analysis.get('gaze_delta')} "
            f"thr+={analysis.get('gaze_threshold_pos')} "
            f"thr-={analysis.get('gaze_threshold_neg')} "
            f"open={analysis.get('gaze_eye_openness')}"
            f"{' [HEAD MOVED]' if analysis.get('gaze_head_moved') else ''}"
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
        cv2.putText(
            frame,
            gaze_debug_text,
            (20, 152),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (140, 230, 255),
            2,
        )
