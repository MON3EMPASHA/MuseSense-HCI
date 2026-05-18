# Project Flow — How It Works

## High-Level Architecture

```
Bluetooth Scan ──→ User Match? ──→ Face Signup/Login ──→ Main Loop
                                                            │
                    ┌───────────────────────────────────────┼───────────────────────────┐
                    │                                       │                           │
               Every Frame                            Every 5 frames              Every 30 frames
                    │                                       │                           │
            ┌───────┴───────┐                    Expression + Gaze               Gesture Recognition
            │               │                    Analysis (every 5th)                  │
       Hand Shape      Dynamic                  ┌──────────┴──────────┐      ┌────────┴────────┐
      Detection     Trajectory                  │                    │      │                │
    (Mute/Dark)    (Index Finger)          ExpressionTracker     GazeTracker   $1 Recognizer  Circle Detector
                                               │                    │              │                │
                                               └────────┬───────────┘              │                │
                                                        │                          │                │
                                                    ContextStore  ◄──────────── ───┴────────────────┘
                                                         │
                                                    C# GUI (socket)
```

## Event Loop (`main.py`)

```
1. Bluetooth Scan (HCIServer.scan_bluetooth)
   ↓
2. Face Signup/Login if no BT match (FaceSignupFlow)
   ↓
3. Main loop while camera is open:
   │
   ├─ Read camera frame
   ├─ Accept C# GUI socket connection (async)
   ├─ Poll socket for incoming messages (TUIO markers, context, CAMERA:ON/OFF)
   ├─ Face login/signup if not yet done
   │
   ├─ Every frame:
   │   ├─ YOLO artifact detection (every 5 frames)
   │   ├─ Draw artifact bounding boxes
   │   ├─ Expression + Gaze analysis (every 5 frames) → ContextStore
   │   ├─ Face detection + recognition (every 18 frames)
   │   ├─ Static hand-shape detection (Mute, DarkMode)
   │   ├─ Dynamic index-finger gesture tracking (every 30 frames)
   │   ├─ Draw MediaPipe landmarks (face mesh, hands, pose)
   │   └─ Send gesture message to C# via socket
   │
   └─ On disconnect: save session reports, reconnect
```

## Modules

### `users.py` — Bluetooth User Matching
- `normalize_mac()` — normalises MAC address format
- `load_users_by_mac()` — reads `users.json` and returns a dict `MAC → user record`

### `face_recognizer.py` — DeepFace Face Recognition
- Uses **DeepFace ArcFace** model + cosine distance
- Loads known faces from `users.json` (images field or Profile field) or by folder scan
- `identify_faces(frame_rgb)` — returns list of `{name, bbox, distance}`

### `face_signup.py` — Face Login / Registration Pipeline
States: `SCANNING 8s → SIGNUP_PROMPT 2.5s → KEYBOARD (name) → KEYBOARD_AGE (numbers) → KEYBOARD_GENDER (male, female) (confirm button) → CAPTURE 3 images => save them users.json → DONE`
- **SCANNING**: runs DeepFace periodically to find a known face
- **KEYBOARD**: virtual QWERTY via `HandKeyboard` (pinch to type, open hand to confirm)
- **KEYBOARD_AGE**: numeric pad for age
- **KEYBOARD_GENDER**: Male/Female buttons
- **CAPTURE**: 3 face snapshots, saves to `objects/`, updates `users.json`

### `expression_tracker.py` — Emotion + Gaze Tracking
Uses **MediaPipe FaceMesh** (`refine_landmarks=True`)

**Emotion** (rule-based, per-user baseline):
- Extracts 5 features: `smile_curve`, `mouth_open`, `mouth_wide`, `brow_height`, `eye_open`
- Rolling baseline (median over ~150 frames, only updates during "neutral")
- Classifies: **happy**, **surprised**, **sad**, **neutral** based on deltas from baseline

**Gaze** (iris tracking + adaptive thresholds):
- Tracks iris center relative to eye corners (horizontal & vertical ratio)
- Per-side adaptive thresholds from peak excursions
- 3×3 grid zones: `{top/center/bottom}_{left/center/right}`
- Head-motion detection (nose-tip displacement) pauses gaze

### `gaze_tracker.py` — Gaze Hit Counter
- Simple 9-zone counter (tracks how many times each zone was looked at)
- `register(gaze_zone)` — increments and returns hit counts

### `gestures.py` — Gesture Recognition Utilities
- `is_circle_like()` — geometry-based circle detection (radius deviation, aspect ratio)
- `is_gesture_significant()` — minimum size filter
- `detect_swipe()` — geometry-based swipe detection (horizontal vs vertical)
- `show_gesture_feedback()` / `draw_gesture_feedback()` — on-screen feedback

### `test_movements.py` — $1 Gesture Templates
- Defines **$1 Recognizer** templates: Mute, SwipeLeft, SwipeRight, Circle, Like, Dislike, DarkMode
- `recognizer` instance used by main loop every 30 frames

### `hand_shape_recognizer.py` — Static Hand Pose Recognition
- `normalize_landmarks()` — wrist-centered, scale-normalised 21-point hand
- `recognize_hand_shape()` — Euclidean distance against JSON templates
- Used for **Mute** and **DarkMode** static gestures

### `hand_keyboard.py` — Virtual Hand-Controlled Keyboard
- **Navigation**: index finger tip (smoothed with EMA)
- **Click**: pinch index + middle fingers together (hold 0.5s)
- **Confirm**: open left hand held for 1 second
- Modes: `"alpha"` (full QWERTY + numbers) or `"num"` (numeric pad)

### `object_tracking.py` — YOLO Artifact Detection
- **YoloTracker**: runs `ultralytics YOLO` to detect 3 trained artifacts: pyramid, tutankhamun_mask, nefertiti_head
- **ArtifactFocusSmoother**: requires N detections before emitting focus change (debounce)

### `context_store.py` — User Context & Scoring
- Persists per-user state in `context_data.json`:
  - `context`: current_artifact, current_category, last_gesture, last_emotion, last_gaze
  - `category_scores` / `artifact_scores`: interest scoring (updated by emotion deltas)
  - `opened_artifacts`: timestamps
  - `lists`: favorites, explore_later, good_to_see
- `apply_gesture_action()`: maps gesture names → actions (add to list, toggle favourite, open edit)

### `event_protocol.py` — Structured Event Logging
- `build_event(type, payload)` — creates versioned event dict
- `event_to_console()` — human-readable terminal output

### `gaze_report.py` — Gaze & Emotion Session Reports
- **GazeSessionLogger**: collects samples (gaze_zone, delta, emotion)
- Generates: PNG report with bar chart + time series, heatmap PNG, JSON summary, combo image
- Emotion report with bar chart

### `session_reports.py` — Combined Session Finalisation
- `save_session_reports()` — creates per-session folder, saves gaze/emotion/artifact reports + summary JSON
- Generates combined PDF from all report PNGs

## Communication

### Socket (Python ↔ C# GUI)
- Python runs a TCP server on `localhost:5000`
- C# GUI connects asynchronously
- Messages sent from Python (JSON payloads): `user_login`, `artifact_focus`, gesture strings, `TRANS:` transcription lines
- Messages received from C#: `CAMERA:ON/OFF`, `TUIO:<id>`, JSON context updates (`artifact_focus`, `context_update`)

### TUIO (reacTIVision ↔ C#)
- Physical markers placed on artifacts
- reacTIVision reads markers → sends TUIO signals → C# GUI forwards as `TUIO:<id>` → main.py maps to artifact name via `artifacts.json`

## Data Flow Summary

```
Camera Frame
    │
    ▼
MediaPipe Holistic (pose, face, hands)
    │
    ├──→ ExpressionTracker ──→ emotion + gaze_zone
    │                              │
    │                              ▼
    │                         ContextStore.update_context()
    │                              │
    │                              ▼
    │                         update_category_score() / update_artifact_score()
    │
    ├──→ YoloTracker ──→ artifact detections
    │                       │
    │                       ▼
    │                  ArtifactFocusSmoother
    │                       │
    │                       ▼
    │                  ContextStore.update_context()
    │
    ├──→ Hand landmarks ──→ static shape (Mute/Dark)
    │                   ──→ dynamic trajectory → $1 Recognizer → gesture name
    │                                                       │
    │                                                       ▼
    │                                             apply_gesture_action()
    │                                                       │
    │                                                       ▼
    │                                             ContextStore
    │
    └──→ Face landmarks ──→ FaceRecognizer.identify_faces()
                                │
                                ▼
                           Bounding boxes drawn on frame

Every 5 seconds: gaze session samples logged to GazeSessionLogger
On disconnect/exit: save_session_reports() → PNG + JSON + PDF
```
