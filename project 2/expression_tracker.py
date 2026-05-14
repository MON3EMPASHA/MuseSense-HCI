from __future__ import annotations

from collections import Counter, deque

import cv2
import mediapipe as mp


# MediaPipe FaceMesh canonical landmark indices used by this tracker.
# (Both "image-left" and "image-right" refer to the eye as it appears in the
# raw camera image. Image-left = the user's right eye, etc.)
#
# Iris landmarks (require refine_landmarks=True):
#   Image-left iris  (user's right eye): 468..472
#   Image-right iris (user's left  eye): 473..477
#
# Eye corners:
#   Image-left eye:  33 = outer (far-left), 133 = inner (nose-side)
#   Image-right eye: 362 = inner (nose-side), 263 = outer (far-right)
#
# Eye top/bottom lids (for openness):
#   Image-left eye:  159 = top,  145 = bottom
#   Image-right eye: 386 = top,  374 = bottom
#
# Nose tip (head-motion proxy): 1


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

        # === Emotion detection state (rewritten) ===
        # Per-feature rolling baselines (samples taken when classification is
        # "neutral" — self-reinforcing). Bootstrap fills these unconditionally
        # for the first ~2 seconds so we get a usable initial baseline.
        self._emo_feature_keys = (
            "smile_curve",   # corners-above-midline / face_height (smile = +)
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
        # Hysteresis: state-of-last classification keeps it stable between frames.
        self._emo_last_classified: str = "neutral"

        # === Gaze state (rewritten v2) ===
        # Rolling baseline samples. The baseline is the running median.
        # Bigger window now (200 samples ≈ 30 s) so a few left/right glances
        # don't shift the median. Window only grows past the bootstrap once
        # the current classification is "center" (self-reinforcing — keeps
        # the baseline locked onto looking-at-screen).
        self._gaze_h_samples: deque[float] = deque(maxlen=200)
        self._gaze_h_baseline: float | None = None
        self._gaze_h_bootstrap_count: int = 0
        self._gaze_h_bootstrap_target: int = 30   # first 30 frames seed baseline unconditionally

        # Light EMA on the delta so the label doesn't flicker.
        self._gaze_h_ema: float = 0.0
        self._gaze_ema_alpha: float = 0.65

        # Need a minimum number of stable samples before we trust the baseline.
        self._gaze_min_samples_to_classify: int = 15

        # Default threshold floor in eye-bbox-ratio units (~5 % of eye width).
        # Per-side adaptive thresholds below will override this once we have
        # enough peak data.
        self._gaze_threshold: float = 0.050
        # Sample is "centered enough" to feed the baseline once |delta| below this:
        self._gaze_center_capture_threshold: float = 0.040

        # Per-side adaptive thresholds — handle anatomic / camera asymmetry
        # where the user can deflect further on one side than the other.
        # Updated continuously from the peak excursion histories below.
        self._gaze_pos_threshold: float = 0.050
        self._gaze_neg_threshold: float = 0.050
        self._gaze_pos_peaks: deque[float] = deque(maxlen=15)
        self._gaze_neg_peaks: deque[float] = deque(maxlen=15)
        # |delta| has to clear this floor to be considered a "real" excursion
        # worth recording in the peak history (filters jitter).
        self._gaze_peak_noise_floor: float = 0.030
        # Bound the adaptive threshold so it never gets ridiculous.
        self._gaze_adapt_thresh_min: float = 0.025
        self._gaze_adapt_thresh_max: float = 0.060
        # Adaptive threshold = fraction × median peak.
        self._gaze_adapt_thresh_factor: float = 0.45

        # Camera mirror compensation. Many webcams (including most laptops)
        # mirror the video before exposing it, so iris-moves-image-left in our
        # ratio actually means the user is looking image-left = their right
        # but the screen they look at is also flipped, so a screen-perspective
        # mapping needs the labels swapped. True here = swap L/R.
        self._gaze_invert_lr: bool = True

        # Head-motion detection: pause baseline updates briefly after a head
        # turn so the median isn't polluted.
        self._last_nose_pos: tuple[float, float] | None = None
        self._head_motion_pause_until: float = 0.0
        self._head_motion_threshold_px: float = 8.0   # frame-to-frame jump

        # Blink filter — eye openness = vertical-extent / horizontal-extent.
        self._min_eye_openness: float = 0.10

        # Debug: log every zone transition to console for diagnosis.
        self._last_logged_gaze_zone: str = ""

    def reset_gaze_calibration(self) -> None:
        self._gaze_h_samples.clear()
        self._gaze_h_baseline = None
        self._gaze_h_bootstrap_count = 0
        self._gaze_h_ema = 0.0
        self._last_nose_pos = None
        self._head_motion_pause_until = 0.0
        self._last_logged_gaze_zone = ""
        self._gaze_pos_peaks.clear()
        self._gaze_neg_peaks.clear()
        self._gaze_pos_threshold = self._gaze_threshold
        self._gaze_neg_threshold = self._gaze_threshold
        # Emotion baselines reset too — they're per-user.
        for d in self._emo_samples.values():
            d.clear()
        self._emo_baselines.clear()
        self._emo_bootstrap_count = 0
        self._emo_last_classified = "neutral"

    # ------------------------------------------------------------------
    # Smoothing helper (unchanged — final 5-frame stabilizer over emotion +
    # gaze). It still has an effect on gaze but the upstream computation is now
    # accurate enough that the majority vote no longer hides the signal.
    # ------------------------------------------------------------------
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
        import time

        results = self.face_mesh.process(frame_rgb)
        if not results.multi_face_landmarks:
            return None

        face_landmarks = results.multi_face_landmarks[0].landmark
        image_height, image_width, _ = frame_rgb.shape

        def point(index: int) -> tuple[float, float]:
            landmark = face_landmarks[index]
            return landmark.x * image_width, landmark.y * image_height

        # =====================================================================
        #                       EMOTION (rewritten)
        # =====================================================================
        # Strategy:
        #   1) Compute five normalized facial features (mouth shape +
        #      eyebrow height + eye openness), all divided by face scale so
        #      the absolute pixel size of the face does not affect results.
        #   2) Maintain a per-feature rolling baseline (median over 150
        #      samples ≈ 5–6 s). Bootstrap: first ~2 s feeds the baseline
        #      unconditionally; after that, only "neutral" frames update it,
        #      so a smile (or shock) won't drift the baseline.
        #   3) Compute deltas from baseline, then classify with simple rules
        #      that combine mouth + eyebrow + eye signals. Each emotion has
        #      its own specific pattern (not just "mouth wide = happy").
        #   4) Hysteresis: small slack on the "stay in current emotion" path
        #      so single-frame jitter doesn't flip the label.
        #
        # Landmarks used (MediaPipe FaceMesh canonical, refine_landmarks=True):
        #   Mouth:    61 (L corner), 291 (R corner), 13 (top), 14 (bottom)
        #   Face:     10 (forehead), 152 (chin), 234 / 454 (L / R cheek)
        #   Brows:    105 (image-L brow centre), 334 (image-R brow centre)
        #   Eyes:     159 / 145 (image-L eye top / bottom)
        #             386 / 374 (image-R eye top / bottom)

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

        # --- mouth ---
        mouth_w_px   = max(mouth_right[0] - mouth_left[0], 1.0)
        mouth_h_px   = max(mouth_bottom[1] - mouth_top[1], 0.0)
        mouth_mid_y  = (mouth_top[1] + mouth_bottom[1]) / 2.0
        corner_avg_y = (mouth_left[1] + mouth_right[1]) / 2.0
        # Image-Y increases downward, so corners ABOVE mid_y → smile, BELOW → frown.
        smile_curve  = (mouth_mid_y - corner_avg_y) / face_height
        mouth_open_v = mouth_h_px / face_height
        mouth_wide_v = mouth_w_px / face_width

        # --- eyebrows (brow centre to eye-top gap, averaged across both brows) ---
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

        # --- eye openness (lid-to-lid gap, averaged across both eyes) ---
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

        # --- update baselines: bootstrap or only-when-neutral ---
        update_baseline_now = (
            self._emo_bootstrap_count < self._emo_bootstrap_target
            or self._emo_last_classified == "neutral"
        )
        if update_baseline_now:
            for k in self._emo_feature_keys:
                self._emo_samples[k].append(cur_features[k])
            if self._emo_bootstrap_count < self._emo_bootstrap_target:
                self._emo_bootstrap_count += 1

        # --- recompute medians ---
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

        # --- classify ---
        # Hysteresis: thresholds for entering a new emotion are stricter than
        # for staying in the current one (loosen by 30 %).
        loosen = 0.7 if self._emo_last_classified != "neutral" else 1.0
        # Standard thresholds (deltas from baseline, in face-normalized units)
        TH_SMILE_HAPPY  = 0.011 * loosen   # corners 1.1% face_h above baseline
        TH_WIDE_HAPPY   = 0.012 * loosen   # mouth 1.2% face_w wider
        TH_SMILE_SAD    = 0.009 * loosen   # corners 0.9% below baseline
        TH_BROW_SAD     = 0.005 * loosen   # brows lowered
        TH_OPEN_SURPR   = 0.030 * loosen   # mouth 3% face_h more open
        TH_BROW_SURPR   = 0.006 * loosen   # brows raised
        TH_EYE_SURPR    = 0.005 * loosen   # eyes wider open

        # Only classify once we have a real baseline (post-bootstrap)
        if self._emo_bootstrap_count < self._emo_bootstrap_target:
            emotion = "neutral"
        # SURPRISED — mouth wide-open + raised brows (mouth_open is the dominant cue)
        elif d_open > TH_OPEN_SURPR and (d_brow > TH_BROW_SURPR or d_eye > TH_EYE_SURPR):
            emotion = "surprised"
        # HAPPY — corners up + mouth widened
        elif d_smile > TH_SMILE_HAPPY and d_wide > TH_WIDE_HAPPY:
            emotion = "happy"
        # SAD — corners down (negative smile_curve delta) + brows lowered
        elif d_smile < -TH_SMILE_SAD and d_brow < -TH_BROW_SAD:
            emotion = "sad"
        # SAD (looser) — strong corner-down even without brow signal
        elif d_smile < -TH_SMILE_SAD * 1.3:
            emotion = "sad"
        else:
            emotion = "neutral"

        self._emo_last_classified = emotion

        # =====================================================================
        #                          GAZE (rewritten)
        # =====================================================================
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
        ) -> tuple[float | None, float | None]:
            """Return (horizontal_ratio_in_eye_bbox, openness_ratio) or (None, None)."""
            if iris is None:
                return None, None
            x_min = min(x_a, x_b)
            x_max = max(x_a, x_b)
            eye_w = max(x_max - x_min, 1.0)
            eye_h = max(abs(y_bot - y_top), 1.0)
            h_r = (iris[0] - x_min) / eye_w
            h_r = max(0.0, min(1.0, h_r))
            return h_r, eye_h / eye_w

        h_r_L, open_L = eye_metrics(iris_imgL, L_outer[0], L_inner[0], L_top[1], L_bot[1])
        h_r_R, open_R = eye_metrics(iris_imgR, R_inner[0], R_outer[0], R_top[1], R_bot[1])

        h_ratios = [r for r in (h_r_L, h_r_R) if r is not None]
        h_ratio = sum(h_ratios) / len(h_ratios) if h_ratios else None

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

        # ---- Baseline sampling rules ----
        # During bootstrap (first N clean frames) take ANY clean sample.
        # After bootstrap, only take samples that look like "looking at screen"
        # (|provisional_delta| < center-capture-threshold). This keeps the
        # baseline locked on the centered gaze instead of drifting toward
        # whatever the user is currently looking at.
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

        # ---- Per-side adaptive thresholds ----
        # Record peak excursions on each side (positive/negative). When the
        # user genuinely looks off-centre, their delta exceeds the noise floor
        # — we collect those peaks and set the threshold to a fraction of the
        # median peak. Handles anatomic asymmetry (e.g. user can flick right
        # only ~0.04 but flick left a full 0.08).
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

        # ---- Classification ----
        # Mirror compensation: if _gaze_invert_lr is True (most laptop cameras
        # mirror the video) positive delta means user-looked-screen-right.
        # Otherwise positive delta means user-looked-screen-left.
        if (
            h_ratio is None
            or n < self._gaze_min_samples_to_classify
            or (eye_openness is not None and eye_openness < self._min_eye_openness)
        ):
            gaze_zone = "center"
        elif h_delta > self._gaze_pos_threshold:
            gaze_zone = "right" if self._gaze_invert_lr else "left"
        elif h_delta < -self._gaze_neg_threshold:
            gaze_zone = "left" if self._gaze_invert_lr else "right"
        else:
            gaze_zone = "center"

        # Log a console line on every zone change so diagnosis is possible even
        # when the camera preview is hidden (Child / Senior modes).
        if gaze_zone != self._last_logged_gaze_zone:
            print(
                f"[GAZE] {self._last_logged_gaze_zone or '-'} -> {gaze_zone}  "
                f"ratio={None if h_ratio is None else round(h_ratio, 3)} "
                f"base={round(h_baseline, 3)} delta={round(h_delta, 4)} "
                f"thr+={round(self._gaze_pos_threshold, 3)} "
                f"thr-={round(self._gaze_neg_threshold, 3)} "
                f"peaks=+{len(self._gaze_pos_peaks)}/-{len(self._gaze_neg_peaks)} "
                f"samples={n} open={None if eye_openness is None else round(eye_openness, 3)}"
            )
            self._last_logged_gaze_zone = gaze_zone

        # =====================================================================
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
        # Emotion debug — show per-baseline deltas (the values the rules use)
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
