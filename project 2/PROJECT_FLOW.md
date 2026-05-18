# Project Flow — Complete System Architecture

## Boot Sequence (startup)

```
main.py starts
  │
  ├── 1. Venv bootstrap: checks if running inside project .venv;
  │      if not, re-launches itself via subprocess under the correct Python.
  │
  ├── 2. Environment variables: suppress glog (GLOG_minloglevel=3),
  │      TensorFlow logs (TF_CPP_MIN_LOG_LEVEL=3), MediaPipe GPU (MEDIAPIPE_DISABLE_GPU=1).
  │
  ├── 3. Redirect stderr → NUL during imports, then restore (swallows
  │      MediaPipe C++ "inference_feedback_manager" noise).
  │
  ├── 4. Imports (inside stderr-silenced block):
  │      cv2, mediapipe, numpy, socket, json, bluetooth, dollarpy
  │      From project:
  │        gestures.py       → is_circle_like, is_gesture_significant, show/draw_gesture_feedback
  │        users.py          → normalize_mac, load_users_by_mac
  │        test_movements.py → recognizer ($1 gesture recognizer instance)
  │        hand_shape_recognizer → normalize_landmarks, load_hand_shapes, recognize_hand_shape
  │        context_store.py  → ContextStore, apply_gesture_action
  │        event_protocol.py → build_event, event_to_console
  │        expression_tracker.py → ExpressionTracker
  │        gaze_tracker.py   → GazeTracker
  │        gaze_report.py    → GazeSessionLogger
  │        session_reports.py → save_session_reports
  │        face_recognizer.py → FaceRecognizer
  │        face_signup.py    → FaceSignupFlow
  │        object_tracking.py → ArtifactFocusSmoother, YoloTracker, draw_artifact_detections
  │
  ├── 5. Constants:
  │      PHONE_CAMERA_URL = ""
  │      PHONE_BT_NAME = "Phone"
  │      USERS_JSON_PATH = TUIO11_NET-master/bin/Debug/users.json
  │      ARTIFACTS_JSON_PATH = TUIO11_NET-master/artifacts.json
  │      TUIO_PRIORITY_SECONDS = 3.0     ← YOLO won't override TUIO for 3s after last TUIO
  │      CSHARP_CONTEXT_PRIORITY_SECONDS = 10.0  ← YOLO won't override C# for 10s after last C# msg
  │      SKIP_CONTEXT_LABELS = {"person", "cell phone", "mobile phone"}
  │      OBJECTS_DIR = TUIO11_NET-master/bin/Debug/objects
  │      ARTIFACT_YOLO_MODEL_PATH = YOLO Object Tracking/models/artifact_yolo11s_best.pt
  │      ARTIFACT_YOLO_CONFIDENCE = 0.5
  │      ARTIFACT_YOLO_INTERVAL = 5   ← YOLO runs every 5 frames
  │
  ├── 6. TCP socket: bind localhost:5000 (listen 5, timeout 1s)
  │
  ├── 7. MediaPipe Holistic init:
  │      mp.solutions.holistic.Holistic(
  │        static_image_mode=False,
  │        min_detection_confidence=0.65,
  │        model_complexity=1
  │      )
  │
  ├── 8. Load hand shapes:
  │      hand_shapes.json (user gestures — Mute, DarkMode)
  │      admin_hand_shapes.json (admin gestures — extra templates)
  │
  ├── 9. Instantiate:
  │      context_store = ContextStore()
  │      expression_tracker = ExpressionTracker()
  │      gaze_tracker = GazeTracker()
  │      face_recognizer = FaceRecognizer(OBJECTS_DIR, users_json=USERS_JSON_PATH)
  │      artifact_tracker = YoloTracker(model_path, conf_threshold=0.5)
  │      artifact_focus = ArtifactFocusSmoother(window_size=10, min_hits=5)
  │      tuio_artifacts = load_tuio_artifacts(ARTIFACTS_JSON_PATH)
  │      active_user_name = "guest"
  │
  └── 10. HDIServer.scan_bluetooth()
```

## Module Details

---

### `main.py` — The Hub

File: `main.py` (1233 lines)

**Key global state** (lines 293–523):
| Variable | Type | Purpose |
|---|---|---|
| `conn, addr` | socket / tuple | C# GUI connection |
| `holistic` | mp.solutions.holistic.Holistic | Reused MediaPipe pipeline instance |
| `frame_count` | int | Every-30-frames gesture timer |
| `analysis_frame_counter` | int | Every-5-frames expression/gaze timer |
| `person_frame_counter` | int | Every-18-frames face recognition timer |
| `artifact_frame_counter` | int | Every-ARTIFACT_YOLO_INTERVAL YOLO timer |
| `gesture_points, circle_points` | list[Point] | Accumulated index-finger trajectory |
| `shape_cooldown_time` | float | Cooldown for static hand-shapes (1.5s) |
| `last_tuio_marker_id` | int \| None | Last TUIO marker to avoid duplicate |
| `tuio_last_seen` | float | monotonic timestamp of last TUIO event |
| `csharp_context_last_seen` | float | monotonic timestamp of last C# context msg |
| `signup_flow` | FaceSignupFlow \| None | Non-None only when BT found no known user |
| `pending_login_payload` | str \| None | Cached JSON for re-send on C# reconnect |
| `camera_window_visible` | bool | Whether OpenCV "Output" window is shown |
| `socket_buffer` | str | Partial line buffer for socket reads |

**Socket helpers** (lines 119–194):

- `send_socket_message(conn, payload)` — temporarily switches to blocking mode, sends payload+\n, switches back to non-blocking
- `poll_socket_lines(conn, buffer)` — non-blocking recv(4096), splits by \n, returns complete lines
- `wait_for_csharp_client(server_socket)` — blocking accept loop with 5s timeout logging

**TUIO artifact loader** (line 148): Reads `artifacts.json` and builds `dict[tuioId → artifact]`

**Main loop** (line 525 onward):

```
while cap.isOpened():
  1. cap.read() — if frame fails 30× in a row, soft-reset camera
  2. Accept C# connection (socket.accept with 1s timeout)
  3. poll_socket_lines → process incoming messages:
     a. "CAMERA:ON/OFF" → toggle OpenCV window
     b. "TUIO:<id>" → look up artifact, update context, set tuio_last_seen
        Special: marker_id == 110 → admin login (skip face signup)
     c. JSON lines → artifact_focus / context_update / admin_login
  4. If C# disconnected:
     - save_session_reports()
     - reset gaze_session, gaze_tracker, expression_tracker calibration
     - wait_for_csharp_client() → reconnect
     - re-send pending_login_payload if available
  5. Login logic (user_login == 0):
     - Send user_login JSON to C# (BT match or face result)
  6. Face signup flow (if signup_flow is active):
     - Process frame through FaceSignupFlow (scanning → keyboard → capture)
  7. Main analysis block (inside try/except):
     a. Resize frame to 480×320, convert BGR→RGB
     b. holistic.process(frame_rgb) — runs all MediaPipe models
     c. YOLO artifact detection (every 5 frames) with focus smoother
     d. Expression + Gaze analysis (every 5 frames) → ContextStore
     e. Face recognition (every 18 frames) via DeepFace
     f. Static hand-shape detection (Mute, DarkMode)
     g. Dynamic index-finger trajectory (every 30 frames) with $1 recognizer + circle check
     h. Draw MediaPipe landmarks (face mesh, hands, pose)
     i. Draw overlay: gesture feedback, expression info, context debug
     j. Send gesture messages to C#; apply_gesture_action()
  # On 'q' key or "exit" gesture → break
```

---

### `users.py` — Bluetooth User Matching

File: `users.py` (51 lines)

**Functions:**

- `normalize_mac(mac)` — uppercase, strip, replace `-` with `:`
- `load_users_by_mac(json_path)` —
  1. Reads users.json (list of user dicts)
  2. For each user: extracts `name`, `mac` (string or list of strings), `age`, `gender`, `Profile`, `themeMode`
  3. Returns `dict[normalized_mac → user_payload]` where payload has `type="user_login"`

**users.json format:**
```json
[
  {
    "name": "Alice",
    "mac": ["AA:BB:CC:DD:EE:FF"],
    "age": "25",
    "gender": "Female",
    "Profile": "objects/alice.png",
    "themeMode": "light"
  }
]
```

---

### `face_recognizer.py` — DeepFace Face Recognition

File: `face_recognizer.py` (241 lines)

**Model**: ArcFace (deepface) with cosine distance, detector_backend = "opencv"

**Default threshold**: 0.68 (cosine distance; lower = stricter)

**Initialization:**
- Tries to import deepface; sets `_ENABLED = False` if unavailable
- Loads embeddings from:
  1. **Primary**: `users.json` — uses `images` list (preferred) or `Profile` fallback per user record
  2. **Fallback**: scans `objects/` directory, groups files by stem name (e.g. "alice.jpg", "alice_1.jpg" → "alice")

**Key methods:**

- `_embed(img_path)` → np.ndarray | None:
  Calls `DeepFace.represent()` with `enforce_detection=False`
  Returns 512-d ArcFace embedding

- `identify_faces(frame_rgb)` → list[dict]:
  1. Calls `DeepFace.represent(frame_rgb, enforce_detection=True)` to detect + embed all faces
  2. For each face: compares embedding against all known embeddings via cosine distance
  3. If distance ≤ threshold → returns that name; else "Unknown"
  4. Returns: `[{name, bbox: (x1,y1,x2,y2), distance}, ...]`

- `enroll_image(img_path, display_name)`:
  Embeds a new image and merges with existing emb (running average)

**Facial area** from DeepFace: `region = {"x", "y", "w", "h"}` → converted to `(x, y, x+w, y+h)`

---

### `face_signup.py` — Face Login / Registration Pipeline

File: `face_signup.py` (551 lines)

**Constants**:
| Constant | Value | Meaning |
|---|---|---|
| SCAN_TIMEOUT | 8.0s | Max time trying to recognise known face |
| SCAN_INTERVAL_FR | 20 | Run DeepFace every N frames during scanning |
| PROMPT_DURATION | 2.5s | How long to show "Welcome" message |
| CAPTURE_COUNTDOWN | 4s | Total time for capture phase |
| CAPTURE_SNAP_TIMES | [1.0, 2.0, 3.0] | Seconds at which to snap photos |
| FACE_CROP_PAD | 0.30 | Padding around detected face bbox |
| MIN_NAME_LEN | 2 | Minimum characters for name |

**State machine** (5 states):

```
SCANNING ──(known face found)──→ DONE [return login]
    │
    └──(timeout after 8s)──→ SIGNUP_PROMPT ──(2.5s)──→ KEYBOARD ──(confirm)──→
                                                                │
                                                                ▼
                                                          KEYBOARD_AGE ──(confirm)──→
                                                                │
                                                                ▼
                                                          KEYBOARD_GENDER ──(confirm)──→
                                                                │
                                                                ▼
                                                          CAPTURE ──(3 snaps)──→ DONE
```

**State details:**

1. **SCANNING** (`_do_scanning`, line 212):
   - Shows "Looking for your face..." overlay + remaining timeout
   - Every SCAN_INTERVAL_FR (20) frames: calls `face_recognizer.identify_faces()`
   - If known face found → `result = {"status": "login", "name": "..."}` + `done = True`
   - On timeout → transition to SIGNUP_PROMPT

2. **SIGNUP_PROMPT** (`_do_prompt`, line 234):
   - Dark overlay + "Welcome, new visitor!" + "Let's get you registered."
   - After PROMPT_DURATION (2.5s) → transition to KEYBOARD

3. **KEYBOARD** (`_do_keyboard`, line 250):
   - Creates `HandKeyboard(mode="alpha")` — full QWERTY + numbers 0-9 + SPC
   - User types name by pinching keys; open left hand → confirm
   - On confirm → transition to KEYBOARD_AGE

4. **KEYBOARD_AGE** (`_do_keyboard_age`, line 278):
   - Creates `HandKeyboard(mode="num")` — numeric pad
   - User types age; confirm → KEYBOARD_GENDER

5. **KEYBOARD_GENDER** (`_do_keyboard_gender`, line 306):
   - Custom UI: 2 gender buttons (Male/Female) + Confirm button
   - Nearest-key hover (index finger) + pinch-to-click
   - Selected gender highlighted green; Confirm button below
   - On confirm → transition to CAPTURE

6. **CAPTURE** (`_do_capture`, line 435):
   - Countdown overlay: "Hold still... 4... 3... 2... 1..."
   - Snaps 3 frames at CAPTURE_SNAP_TIMES [1.0, 2.0, 3.0]
   - After countdown: `_save_and_enroll()`

**`_save_and_enroll()`** (line 470):
   1. Sets `done = True` and `result = {"status": "signup", "name": name}` FIRST
   2. For each captured frame: convert RGB→BGR, try `_crop_face()` (Haar cascade), save to `objects/`
   3. Calls `FaceRecognizer.enroll_image()` for each saved image
   4. Calls `_save_user_to_json()` to persist to users.json with age/gender

**`_center_text()`** utility: centers text horizontally on frame

**Admin login**: When C# sends `{"type": "admin_login"}` → sets `signup_flow = None`, `user_login = 1`

---

### `expression_tracker.py` — Emotion + Gaze Tracking

File: `expression_tracker.py` (636 lines)

Uses MediaPipe **FaceMesh** with `refine_landmarks=True` (gives iris landmarks 468-477).

#### Emotion Detection (lines 166–304)

**Strategy**: Per-user rolling baseline + rule-based classification

**5 facial features** (all normalised by face dimensions):
| Feature | Formula | Meaning |
|---|---|---|
| `smile_curve` | (mouth_mid_y - corner_avg_y) / face_h | + = smile, - = frown |
| `mouth_open` | mouth_h / face_h | How wide mouth is open |
| `mouth_wide` | mouth_w / face_w | How stretched mouth is |
| `brow_height` | brow_to_eye_gap / face_h | Eyebrow raise |
| `eye_open` | lid_gap / face_h | Eye widening |

**Landmarks used:**
- Mouth: 61 (L corner), 291 (R corner), 13 (top), 14 (bottom)
- Face: 10 (forehead), 152 (chin), 234/454 (cheeks)
- Brows: 105 (left), 334 (right)
- Eyes: 159/145 (left eye top/bottom), 386/374 (right eye top/bottom)

**Baseline system:**
- Rolling window: 150 samples (~5-6 seconds)
- Bootstrap: first 30 frames unconditionally feed the baseline
- After bootstrap: only "neutral" frames update the baseline (prevents drift)

**Classification thresholds** (deltas from baseline, with 30% hysteresis when staying in current emotion):
| Emotion | Conditions |
|---|---|
| **surprised** | mouth_open > 0.030 AND (brow_height > 0.006 OR eye_open > 0.005) |
| **happy** | smile_curve > 0.011 AND mouth_wide > 0.012 |
| **sad** | smile_curve < -0.009 AND brow_height < -0.005 (or smile_curve < -0.0117 alone) |
| **neutral** | none of the above |

**Output dict keys:**
`emotion`, `raw_emotion`, `smile_curve`, `mouth_open`, `mouth_wide`, `brow_height`, `eye_open`, `emo_d_smile`, `emo_d_open`, `emo_d_wide`, `emo_d_brow`, `emo_d_eye`, `emo_baseline_ok`

#### Gaze Tracking (lines 306–529)

**Strategy**: Iris center ratio + adaptive per-side thresholds + EMA smoothing

**Iris landmarks** (require `refine_landmarks=True`):
- Left eye (user's right): 468-472 (mean)
- Right eye (user's left): 473-477 (mean)

**Eye corner landmarks:**
- Image-left eye: 33 (outer), 133 (inner)
- Image-right eye: 362 (inner), 263 (outer)

**Eye metrics per eye**: `h_ratio = (iris.x - min_corner.x) / eye_width` (0.0 = inner, 1.0 = outer)

**Baseline** (per-user calibration):
- Rolling window: 200 samples
- Bootstrap: first 30 frames
- After bootstrap: only samples with `|provisional_delta| < 0.020` (center-gaze) update the baseline
- EMA factor: 0.80 (smooths h_delta)

**Adaptive thresholds** (lines 437–460):
- Records peak excursions in separate buffers (pos/neg)
- Sets threshold to 50% of median peak amplitude
- Clamped to [0.020, 0.055]
- Handles anatomic asymmetry (different left/right ranges)

**Vertical gaze** (lines 462–484):
- Same baseline strategy
- Thresholds: 0.035 (up), 0.035 (down)

**Head-motion pause** (lines 379–392):
- Tracks nose tip (landmark 1) displacement frame-to-frame
- If displacement > 8px → pauses gaze updates for 0.7s

**Blink filter** (`_min_eye_openness = 0.10`): discards samples when eyes are too closed

**3×3 zone classification** (lines 486–514):
```
          left         center         right
top     top_left     top_center     top_right
center  center_left  center_center  center_right
bottom  bottom_left  bottom_center  bottom_right
```

**Smoothing** (line 133): Majority vote over last 5 analysis frames for both emotion + gaze; requires ≥3 matching.

**`draw_overlay(frame, analysis)`** (line 584):
- Top-left: "Emotion: X | Gaze: Y | Window: 5"
- Emotion debug: deltas from baseline + [BOOT] tag
- Gaze debug: ratio, base, delta, thresholds, eye openness

---

### `gaze_tracker.py` — Gaze Hit Counter

File: `gaze_tracker.py` (23 lines)

Simple 9-bin counter:
```python
self.hit_counts = {
  "top_left": 0, "top_center": 0, "top_right": 0,
  "center_left": 0, "center_center": 0, "center_right": 0,
  "bottom_left": 0, "bottom_center": 0, "bottom_right": 0,
}
```
- `register(gaze_zone)` — increments and returns `{zone, hit_counts}`
- `top_zone()` — returns zone with highest count

---

### `gestures.py` — Gesture Recognition Helpers

File: `gestures.py` (150 lines)

**`is_circle_like(points)`** — geometry-based circle test:
- Requires ≥18 points
- Width and height ≥ 70px each
- Aspect ratio ≤ 1.5 (not too elongated)
- Path length ≥ max(width,height) × 2.2 (must travel around the circle)
- Start-to-end distance ≤ max(width,height) × 0.45 (must close the loop)
- Average radius deviation / mean radius ≤ 0.35 (must be round)

**`is_gesture_significant(points)`** — minimum gesture filter:
- ≥12 points AND (width ≥ 80 OR height ≥ 80 OR path length ≥ 160)

**`detect_swipe(points)`** — geometry-based swipe:
- ≥8 points, width/height ≥ 1.8, net_x/path_length ≥ 0.4, |net_x| ≥ 60
- Returns "SwipeLeft" or "SwipeRight"

**Feedback system:**
- `show_gesture_feedback(msg, duration=2.0s)` — sets global text + timer
- `draw_gesture_feedback(frame)` — draws top-right label (green when active, grey when fading)

---

### `test_movements.py` — $1 Gesture Templates

File: `test_movements.py` (191 lines)

Defines 7 **$1 Recognizer** templates (each is a `dollarpy.Template` with named Point sequences):
| Template | Points | Purpose |
|---|---|---|
| Mute | 45 pts | Silence the system |
| SwipeLeft | 21 pts | Navigate to previous item |
| SwipeRight | 19 pts | Navigate to next item |
| Circle | 10 pts | Favorite/toggle action |
| Like | 15 pts | Positive feedback |
| Dislike | 30 pts | Negative feedback |
| DarkMode | 37 pts | Toggle dark theme |

Usage: `recognizer = Recognizer([Mute, SwipeLeft, ...])`

Main loop runs every 30 frames: `recognizer.recognize(gesture_points)` → `(name, score)`

---

### `hand_shape_recognizer.py` — Static Hand Pose Recognition

File: `hand_shape_recognizer.py` (89 lines)

**`normalize_landmarks(landmarks)`** — normalises MediaPipe 21-point hand landmarks:
1. Wrist → origin (landmark 0)
2. Subtract wrist xy from all points
3. Find max distance from wrist to any point (scale factor)
4. Divide all coordinates by max distance
5. Returns flat list of 42 floats: [x0, y0, x1, y1, ...]

**`recognize_hand_shape(normalized, templates, threshold)`**:
- Euclidean distance against all templates
- Returns `(best_name, score)` where score = `max(0, 1.0 - distance/threshold)`
- If best distance ≥ threshold → `(None, 0.0)`

**JSON template files:**
- `hand_shapes.json` — user gestures (Mute, DarkMode)
- `admin_hand_shapes.json` — admin-only gestures

---

### `hand_keyboard.py` — Virtual Hand-Controlled Keyboard

File: `hand_keyboard.py` (401 lines)

**Key parameters:**
| Constant | Value | Meaning |
|---|---|---|
| CLICK_COOLDOWN | 0.30s | Min time between key presses |
| PINCH_DIST_PX | 30px | Max distance between index + middle for pinch |
| PINCH_HOLD_TIME | 0.5s | How long pinch must be held |
| OPEN_HAND_HOLD | 1.0s | How long open hand must be held to confirm |
| CURSOR_EMA_ALPHA | 0.45 | Cursor smoothing factor (lower = smoother) |

**Layouts:**

Alpha mode (10 columns × 4 rows):
```
Row 1: 1 2 3 4 5 6 7 8 9 OK
Row 2: Q W E R T Y U I O P
Row 3: A S D F G H J K L DEL
Row 4: Z X C V B N M SPC 0
```

Num mode (3 columns × 4 rows):
```
Row 1: OK 7 8
Row 2: 4 5 6
Row 3: 1 2 3
Row 4: DEL 0 9
```

**Interaction:**
1. **Hover**: index finger tip tracked via MediaPipe; nearest key highlighted (nearest-centroid, not bounding-box check)
2. **Cursor**: smoothed with EMA (alpha=0.45) + crosshair visual
3. **Click**: pinch index + middle fingers within 30px for 0.5s
4. **Confirm**: open left hand (all 5 fingers extended) held for 1.0s → progress bar shown
5. **Input**: max 32 characters; DEL removes last char; OK confirms; SPC adds space

**Rendering:**
- Semi-transparent overlay (40% opacity)
- Rounded-rect keys with distinct colors: hover (blue), click (green), DEL (red), OK (green), numbers (dark)
- Input field with cursor
- Open-hand confirmation progress bar

**`_is_open_hand(hand_landmarks)`**:
- All 4 finger tips have y < their PIP joints (extended)
- Thumb tip x - thumb IP x > 0.04 (thumb stretched sideways)

---

### `object_tracking.py` — YOLO Artifact Detection

File: `object_tracking.py` (224 lines)

**`YoloTracker`** class:
- Uses `ultralytics.YOLO` with artifact_yolo11s_best.pt
- `conf_threshold = 0.5` (configurable)
- `imgsz = 640`

**3 trained artifact classes:**
| YOLO label | Artifact name | Category |
|---|---|---|
| `pyramid` | Pyramids of Giza | Egypt |
| `tutankhamun_mask` | Mask of Tutankhamun | Egypt |
| `nefertiti_head` | Bust of Nefertiti | Egypt |

**Key methods:**

- `detect_artifacts(frame)` → list[dict]:
  1. Runs YOLO inference on frame
  2. Filters by `conf_threshold`
  3. Normalizes labels via `normalize_artifact_label()` (lowercase, spaces→underscores)
  4. Looks up in ARTIFACT_LABELS mapping
  5. Returns sorted (by confidence descending): `{label, display_label, confidence, bbox, center, artifact, category}`

- `detect_primary(frame)` → best single detection (for non-artifact use)
- `detect_persons(frame)` → all "person" detections

**`ArtifactFocusSmoother`** class:
- `window_size = 10`, `min_hits = 5`
- Stores last N detection labels in deque
- Only emits new focus when a label appears ≥ min_hits times
- Prevents flickering between detections

**`draw_artifact_detections(frame, detections)`**:
- Draws orange (0, 215, 255) bounding boxes + labels with confidence

**Priority logic** in main.py (line 858):
YOLO auto-focus only activates when:
- `now - tuio_last_seen ≥ 3.0s` AND
- `now - csharp_context_last_seen ≥ 10.0s`

This gives TUIO markers and C# GUI manual navigation priority over YOLO.

---

### `context_store.py` — User Context & Interest Scoring

File: `context_store.py` (385 lines)

**Storage**: `context_data.json` (auto-created)

**Data structure per user:**
```json
{
  "profile": "",
  "lists": {
    "favorites": [{"item_id": "...", "category": "...", "timestamp": ...}],
    "explore_later": [...],
    "good_to_see": [...]
  },
  "category_scores": {"Egypt": 2.5, "New Kingdom": 1.0, ...},
  "artifact_scores": {"Mask of Tutankhamun": 1.5, ...},
  "opened_artifacts": {"mask of tutankhamun": 1234567890.0, ...},
  "context": {
    "current_artifact": "Mask of Tutankhamun",
    "current_category": "Egypt",
    "last_object": "",
    "last_gesture": "",
    "last_emotion": "happy",
    "last_gaze": "center_center"
  },
  "updated_at": 1234567890
}
```

**Core methods:**
| Method | Purpose |
|---|---|
| `ensure_user(name, profile)` | Creates user entry if missing |
| `update_context(user, **kwargs)` | Sets context fields, auto-saves |
| `get_context_snapshot(user)` | Returns copy of current context dict |
| `update_category_score(user, cat, delta)` | Adds delta to category score, saves |
| `update_artifact_score(user, artifact, delta)` | Adds delta to artifact score, saves |
| `record_artifact_opened(user, artifact)` | Logs timestamp when artifact was opened |
| `create_list_item(user, list_name, item)` | Adds to list (no duplicates by item_id) |
| `delete_list_item(user, list_name, item_id)` | Removes from list |
| `read_lists(user)` | Returns all lists for user |
| `get_context_recommendation(user)` | Returns highest-scoring category |

**Interest scoring** (in main.py, lines 934–953):
| Emotion | delta |
|---|---|
| happy | +1.0 |
| surprised | +0.5 |
| neutral | +0.2 |
| sad | -0.5 |

Delta is applied to both the current category and current artifact.

**`get_context_recommendation(user)`** (line 172):
1. If no scores → return current_category or "general"
2. If current_category score ≥ 80% of top score → recommend current_category
3. Else → recommend top-scored category

#### `apply_gesture_action(store, user, gesture, item_id, category)` (line 203)

| Gesture | Action |
|---|---|
| `create_artifact` | `open_create_artifact` |
| `edit_artifact` | `open_edit_artifact` |
| `delete_artifact` | `open_delete_artifact` |
| `next_artifact` | `open_next_artifact` |
| `prev_artifact` | `open_prev_artifact` |
| `circle` | `toggle_favorite` (add/remove from favorites) |
| `swipeup` | `add_explore_later` (+0.7 explore category score) |
| `thumbsup` / `goodtosee` | `add_good_to_see` (+0.5 positive category score) |

---

### `event_protocol.py` — Structured Event Logging

File: `event_protocol.py` (62 lines)

**Functions:**
- `build_event(event_type, payload)` → `{version: "v1", timestamp: ..., type: ..., payload: ...}`
- `event_to_line(event)` → JSON string (for wire transmission)
- `event_to_console(event)` → human-readable summary (e.g. `[EVENT] type=expression_gaze_update user=alice emotion=happy gaze=center_center`)
- `to_pretty_json(data)` → pretty-printed JSON for debugging

Currently `context_store.log_event()` is disabled (pass), but the event infrastructure is in place.

---

### `gaze_report.py` — Gaze & Emotion Session Reports

File: `gaze_report.py` (883 lines)

**`GazeSample` dataclass:**
```python
@dataclass
class GazeSample:
    t: float          # monotonic timestamp
    gaze_zone: str    # e.g. "center_center"
    gaze_delta: float # horizontal eye deviation
    gaze_ratio: float | None
    gaze_baseline_ratio: float | None
    emotion: str | None  # "happy", "neutral", etc.
```

**`GazeSessionLogger`** collects samples via `add_expression(expression_dict)` and generates reports.

**Report types:**

1. **Gaze PNG** (`_render_png`): 1280×720 image with:
   - Header card: title, timestamp, user name, user profile photo (from users.json)
   - Left card: horizontal bar chart (L/C/R distribution), summary stats (samples, dominant zone, dwell time, mean |delta|, top transitions)
   - Right card: time-series line chart of gaze delta with horizontal zero-line

2. **Gaze JSON** (`_build_summary_payload`):
   - zone_counts, zone_percent, dominant_zone, dwell_seconds, transition matrix, mean/max delta

3. **Gaze Heatmap PNG** (`_render_heatmap`): virtual screen with:
   - Gaussian-blurred Inferno colormap overlay from gaze hits
   - 3×3 grid labels with percentages
   - Scan-path line (grey → lighter over time, green=start, red=end)

4. **Emotion PNG** (`_render_emotion_png`): 5-category bar chart (happy, surprised, neutral, sad, unknown)

5. **Combo PNG** (`_render_combo`): gaze report + heatmap stacked vertically

---

### `session_reports.py` — Combined Session Finalisation

File: `session_reports.py` (271 lines)

**`save_session_reports(reports_root, user_name, gaze_session, context_store, tuio_artifacts)`**:
1. Creates session directory: `reports/{user}_{timestamp}/`
2. Calls `gaze_session.save_report()` → gaze PNG, heatmap, JSON
3. Calls `gaze_session.save_emotion_report()` → emotion PNG, JSON
4. Calls `_build_artifact_report()`:
   - Scores all known artifacts (from artifacts.json) against user's artifact_scores
   - Generates sorted bar chart (opened in green dot, unopened in red dot)
5. Calls `_build_combined_pdf()` → merges all PNGs into single PDF via Pillow
6. Writes `session_summary.json` with paths to all generated reports

**Called on:**
- C# GUI disconnection (mid-session save)
- `q` key press or "exit" gesture (final save)

---

## Socket Communication Protocol

### Python → C# GUI (TCP localhost:5000, line-terminated)

| Payload | Format | When |
|---|---|---|
| User login | `{"type":"user_login","name":"Alice","age":"25","gender":"Female","mac":"AA:BB:...","Profile":"objects/alice.png",...}\n` | After BT match, face login, or face signup |
| Guest MAC | `AA:BB:CC:DD:EE:FF\n` (raw string) | Unknown BT device, no face flow |
| Gesture | `Circle\n` (raw string) | When a gesture is detected |
| YOLO focus | `{"type":"artifact_focus","source":"yolo11s","artifact":"...","category":"...","label":"...","confidence":0.85}\n` | When YOLO detects a new stable artifact |
| Transcription | `TRANS:Expression: happy (gaze: center_center)\n` | Various events emitted to live-transcription panel |

### C# GUI → Python

| Command | Format | Effect |
|---|---|---|
| Toggle camera | `CAMERA:ON\n` or `CAMERA:OFF\n` | Shows/hides OpenCV "Output" window |
| TUIO marker | `TUIO:42\n` | Look up artifact, update context |
| Artifact focus | `{"type":"artifact_focus","artifact":"Ramses II","category":"New Kingdom"}\n` | C# user navigated to artifact detail |
| Context update | `{"type":"context_update","current_artifact":"...","current_category":"..."}\n` | C# updates current browsing context |
| Context clear | `{"type":"context_update","clear":true}\n` | User returned to home screen |
| Admin login | `{"type":"admin_login","name":"admin"}\n` | C# initiated admin login |

## TUIO / reacTIVision Integration

```
Physical marker on artifact
       │
       ▼
reacTIVision (reads markers via camera)
       │
       ▼
TUIO protocol over UDP
       │
       ▼
C# GUI (TUIO11_NET-master) receives TUIO signals
       │
       ▼
C# maps marker ID → artifact, forwards "TUIO:<id>" over TCP to Python
       │
       ▼
main.py: look up tuio_artifacts[marker_id] → artifact name + category
       │
       ▼
ContextStore.update_context(current_artifact, current_category)
```

Special marker ID **110** → admin login (bypasses face signup).

## Data Flow Summary with Timings

```
Camera (30 FPS)
    │
    ▼
Resize 480×320 → RGB
    │
    ▼
MediaPipe Holistic ───→ pose landmarks, face landmarks, hand landmarks
    │
    ├── Every frame:
    │   ├── Draw artifact detections (YOLO bounding boxes)
    │   ├── Hand shape detection (cooldown 1.5s between detections)
    │   └── Index finger trajectory (3px jitter filter)
    │
    ├── Every 5 frames (~6×/second):
    │   ├── Expression + Gaze analysis
    │   │   ├── 5-feature emotion classification
    │   │   ├── Iris-based 3×3 gaze zone
    │   │   ├── GazeSessionLogger.add_expression()
    │   │   └── ContextStore updates (emotion, gaze, scores)
    │   └── YOLO artifact detection (ARTIFACT_YOLO_INTERVAL=5)
    │       └── ArtifactFocusSmoother → ContextStore
    │
    ├── Every 18 frames (~1.7×/second):
    │   └── DeepFace face recognition (identify_faces)
    │
    └── Every 30 frames (~1×/second):
        └── $1 Gesture Recognition + Circle detection
            └── If gesture → apply_gesture_action() → ContextStore → C# socket
```

## Error Handling & Resilience

- **Camera failures**: if 30 consecutive frame reads fail (> 2s), camera is released and re-initialised
- **Socket disconnects**: mid-session reports saved, C# connection re-accepted; login payload re-sent
- **Import failures**: stderr restored so tracebacks visible; graceful degradation (e.g. YOLO disabled if model missing, DeepFace disabled if not installed)
- **Face signup resilience**: `done = True` set before file I/O so login payload is always sent even if save partially fails
