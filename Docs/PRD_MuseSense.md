# Product Requirements Document (PRD)

## Product Name

MuseSense - Tangible + Vision Interactive Museum Guide

## Version

1.0 (Baseline from current codebase)

## Date

2026-03-22

## Authors

HCI Project Team

---

## 1. Executive Summary

MuseSense is an interactive museum exploration system that combines:

- Tangible interaction via TUIO fiducial markers
- Vision-based face recognition and session identity
- Hand, pose, and face landmark detection for future gesture and engagement features
- A C# WinForms 3D artifact experience with narration and user favorites

The current implementation already delivers a working end-to-end loop:

1. Python captures camera input and recognizes faces.
2. Python sends status updates over socket to C#.
3. C# desktop app receives TUIO marker events and opens artifact detail views.
4. Logged-in users can save favorites, persisted to JSON.

This PRD defines the current baseline and the target product requirements for a complete, demo-ready HCI museum experience.

---

## 2. Product Vision

Create a playful, low-friction museum interface where visitors can discover artifacts naturally through physical markers and real-time computer vision, while receiving personalized content and maintaining a lightweight personal collection.

---

## 3. Problem Statement

Traditional museum interfaces are often passive and menu-heavy. Visitors spend time navigating controls instead of engaging with artifacts. MuseSense addresses this by making interaction physical (markers), immediate (auto-recognition), and personal (face-based profile + favorites).

---

## 4. Goals and Success Criteria

### 4.1 Goals

- Reduce interaction friction for first-time users.
- Increase engagement through tangible and visual interaction.
- Provide lightweight personalization through per-user favorites.
- Support immersive artifact learning with 3D model rendering and narration.

### 4.2 Success Metrics

- First successful artifact open via marker in under 60 seconds for new users.
- Marker-to-artifact navigation latency under 1 second on target hardware.
- Face-to-login UI update under 2 seconds after stable detection.
- Favorites save success rate above 95 percent in local JSON persistence tests.
- App remains responsive (target 25-30 FPS UI refresh on artifact screen).

---

## 5. Users and Personas

- Casual Visitor: wants quick exploration and visual storytelling.
- Student/Research User: wants artifact context, repeat access, and saved list.
- Guide/Facilitator: needs stable demo flow and fast switching between artifacts.

---

## 6. In-Scope and Out-of-Scope

### 6.1 In Scope (Current + Required)

- TUIO marker-driven artifact selection.
- Face-detected login identity mapping to local user profile.
- Artifact browsing (Explore, Artifact Detail, Favourites screens).
- 3D model rendering in C# (software rasterizer), including texture support.
- Artifact narration playback and volume controls.
- Per-user favorites persistence in JSON.

### 6.2 Out of Scope (For current PRD release)

- Cloud backend and remote multi-device sync.
- Full NLP chatbot or voice assistant.
- Multi-language localization.
- Formal role-based admin panel.

### 6.3 Planned Next Scope (Post-MVP)

- Gesture-to-action mapping (Swipe Right, Swipe Up, Thumbs Up, Open Palm, Pinch).
- Consent UX for optional emotion-aware personalization.
- Explore Later and Good to See lists.
- Session summary export (QR/PDF).

---

## 7. Current System Baseline (As Implemented)

### 7.1 Python Runtime (main.py)

- Opens camera from configurable source (IP camera or local webcam).
- Loads MediaPipe pose/hand/face landmarker models.
- Loads FaceNet stack (MTCNN + InceptionResnetV1).
- Performs asynchronous face embedding and recognition in worker thread.
- Maintains known user embeddings in local JSON (faces/users.json).
- Sends socket messages to localhost:5000 in format:
  - face:detected:<name>
  - face:lost
- Includes robust reconnect logic for camera failures and blank frames.
- Displays annotated output preview window.

### 7.2 C# Desktop Runtime (TuioDemo.cs)

- .NET Framework 4.8 WinForms app.
- Implements TuioListener for marker/cursor/blob callbacks.
- Connects to TUIO stream (default port 3333).
- Connects to Python socket endpoint localhost:5000.
- Supports screens:
  - Explore
  - Favourites (requires logged-in user)
  - Artifact Detail
- Marker add/update events map symbol IDs to artifact index and open/drive artifact view.
- Artifact detail includes:
  - Software-rendered 3D model
  - Metadata: name, birth date, era, origin, description
  - Narration text and audio controls
  - Favorite toggle/add button
- Favorites persisted to faces/favorites.json.
- Face lost event clears login session and exits favorites if active.

### 7.3 Data and Assets

- Artifact catalog: main_csharp/TUIO11_NET-master/artifacts.json.
- Supported artifact IDs currently include at least 0-6 in catalog.
- Face persistence: faces/users.json.
- Favorites persistence: faces/favorites.json.
- Audio narration files under audio/.
- 3D assets under main_csharp/TUIO11_NET-master/bin/Debug/3d models/.

### 7.4 Known Gaps in Current State

- No explicit consent UI for face or emotion processing.
- Gesture actions are detected at landmark level but not mapped to product actions yet.
- users.json schema appears inconsistent in stored key naming in existing data.
- No automated test suite for integration stability.
- No formal analytics/event logging pipeline.

---

## 8. Functional Requirements

### FR-01 Marker Detection and Artifact Activation

- System shall accept TUIO object events and map symbol ID to artifact.
- On marker add, system shall open corresponding artifact detail view.
- On marker update, system shall update artifact orientation control.
- On marker remove, system shall stop marker-controlled orientation.

### FR-02 Explore and Navigation UI

- System shall provide Explore, Artifact, and Favourites screens.
- System shall allow click and keyboard navigation in Explore carousel.
- System shall keep interaction responsive during model loading.

### FR-03 3D Artifact Rendering

- System shall load OBJ/MTL meshes and textures.
- System shall render artifact in software rasterizer with z-buffer and shading.
- System shall cache generated frames/thumbnails to reduce CPU overhead.

### FR-04 Artifact Metadata and Narration

- System shall display artifact name, date, era, origin, and description.
- System shall support narration playback, pause/resume, and volume control.
- System shall stop narration when leaving artifact screen.

### FR-05 Face Detection and Login

- Python process shall detect and recognize faces from camera feed.
- Python process shall emit face status messages to C# over localhost socket.
- C# app shall set active user session from face:detected:<name>.
- C# app shall clear active user on face:lost.

### FR-06 Favorites Management

- System shall allow adding current artifact to favorites for logged-in user.
- System shall prevent duplicate favorite entries.
- System shall persist favorites to local JSON.
- System shall load favorites at startup and on user login.

### FR-07 Camera and Runtime Resilience

- Python process shall attempt reconnect on camera frame/read failures.
- Python process shall support disabled-camera mode for socket-only operation.
- C# app shall retry socket connection to Python when disconnected.

### FR-08 Artifact Catalog Management

- System shall load artifact definitions from artifacts.json where available.
- System shall support fallback catalog defaults if file is missing/unreadable.
- Artifact definition shall include id, tuioId, name, metadata, objPath, audioPath, color.

---

## 9. Non-Functional Requirements

### NFR-01 Performance

- UI target: smooth interaction at approx. 25-30 FPS on demo hardware.
- Marker-to-screen response under 1 second.

### NFR-02 Reliability

- Graceful behavior when camera is unavailable.
- Graceful behavior when Python socket is disconnected.
- No fatal crash on missing model/texture/audio asset.

### NFR-03 Usability

- New user should understand core interaction (marker place -> artifact opens) in under 2 minutes.
- Visual status labels must communicate Python and face connection state.

### NFR-04 Privacy and Ethics

- Face processing is local-only by default.
- Future release must include explicit consent and guest mode.
- Documented data deletion flow for local biometric embeddings.

### NFR-05 Maintainability

- Artifact additions should be configuration-driven via artifacts.json.
- Marker mapping should avoid hardcoded artifact names in UI logic.

---

## 10. User Flows

### 10.1 Primary Visitor Flow

1. System launches Python and C# processes.
2. User appears in camera.
3. Face recognized -> C# user session updates.
4. User places TUIO marker on table.
5. Artifact detail opens with 3D model + metadata + narration.
6. User saves artifact to favorites.
7. User opens Favourites screen to revisit saved artifacts.

### 10.2 Face Lost Flow

1. Camera no longer sees user face for threshold duration.
2. Python sends face:lost.
3. C# clears session and favorites context.
4. If on Favourites page, app returns to Explore.

### 10.3 Failure Recovery Flow

1. Camera read fails repeatedly.
2. Python retries source/backends and fallback camera.
3. If unavailable, enters socket-only mode and continues retry loop.
4. Once camera reconnects, normal detection resumes.

---

## 11. System Architecture

### 11.1 Components

- Vision Service (Python): camera input, MediaPipe, FaceNet, socket server.
- Presentation App (C# WinForms): UI, rendering, TUIO listener, audio, persistence.
- Tracking Input (reacTIVision/TUIO): marker events via UDP to C# client.
- Local Data Store (JSON): users, favorites, artifact catalog.

### 11.2 Runtime Interfaces

- Python -> C#: TCP localhost:5000, UTF-8 plain text messages.
- TUIO -> C#: TuioClient on configurable port (default 3333).
- C# -> File system: artifact, model, audio, favorites, users JSON files.

---

## 12. Data Model

### 12.1 Artifact

- id: integer
- tuioId: integer
- name: string
- birthDate: string
- era: string
- origin: string
- description: string
- narration: string
- objPath: string
- audioPath: string
- color: hex string

### 12.2 User

- name: string
- face embeddings: stored by Python in faces/users.json
- favourites: list of artifact names in faces/favorites.json

### 12.3 Protocol Events

- face:detected:<name>
- face:lost

---

## 13. Dependencies and Platforms

### 13.1 Python Stack

- OpenCV
- MediaPipe Tasks
- facenet-pytorch
- PyTorch
- NumPy
- Pillow

### 13.2 C# Stack

- .NET Framework 4.8
- WinForms
- Built-in System.Web.Extensions JSON serializer
- TUIO/OSC.NET project sources in solution

### 13.3 Hardware Assumptions

- Camera (USB webcam or phone IP camera).
- Table/surface with TUIO marker tracking source.
- Display/kiosk machine capable of running WinForms rendering.

---

## 14. Release Plan

### Milestone 1: Stabilize Existing Baseline

- Validate startup scripts for both Python and C# app.
- Normalize JSON schema for users and favorites.
- Add error messaging for missing assets.

### Milestone 2: Gesture Productization

- Define gesture classifier outputs and confidence thresholds.
- Map gestures to product actions and UI feedback.
- Add undo and debounce logic.

### Milestone 3: Privacy and Consent

- Add consent/guest onboarding screen.
- Add local data clear flow.
- Add privacy note in UI.

### Milestone 4: Demo Readiness

- End-to-end scripted test run.
- Final usability pass and fallback paths.
- Evaluation run with participants.

---

## 15. Acceptance Criteria

- Marker scan opens correct artifact in under 1 second.
- Face detection updates login label and favorites context.
- Favorite save persists and survives app restart.
- Narration playback controls operate correctly per artifact.
- App remains functional when camera disconnects and reconnects.
- No crash when one or more artifact assets are missing.

---

## 16. Risks and Mitigations

- Risk: Face false positives/negatives.
  - Mitigation: smoothing window, similarity threshold tuning, guest mode.

- Risk: Camera instability on Windows drivers.
  - Mitigation: backend cycling (MSMF/DSHOW), reconnect logic.

- Risk: High CPU load from software rendering.
  - Mitigation: thumbnail and frame caching, capped internal render size.

- Risk: Data inconsistency in local JSON files.
  - Mitigation: schema validation and migration utility on startup.

- Risk: Privacy concerns around biometrics.
  - Mitigation: explicit consent UI and local delete controls.

---

## 17. Open Questions

- Should face login require explicit confirmation before session switch?
- Should favorites be keyed by artifact ID instead of artifact name?
- What minimum hardware profile should be officially supported?
- Should gesture actions be enabled by default or behind a calibration step?
- Is emotion-based personalization required in MVP or post-MVP?

---

## 18. Traceability to Current Repository

Primary implementation references used for this PRD:

- main.py
- main_csharp/TUIO11_NET-master/TuioDemo.cs
- main_csharp/TUIO11_NET-master/artifacts.json
- faces/users.json
- faces/favorites.json
- Docs/project_description.txt
- Docs/CSharp_Form_Documentation.md

This PRD is intentionally grounded in the existing code and structured to support immediate implementation planning.
