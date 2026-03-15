# MuseSense — Project Summary

## Overview

MuseSense is an HCI (Human-Computer Interaction) course project that creates an interactive museum experience using three simultaneous input modalities:

- **TUIO tangible markers** — physical tokens placed on a table surface
- **Hand gesture recognition** — MediaPipe-based gesture tracking
- **Face recognition + emotion analysis** — DeepFace-powered identity and expression detection

The system is designed to replace passive, menu-heavy museum navigation with a natural, multi-modal interaction model that is physical, expressive, and personalized.

---

## Repository Structure

```
project/
├── constantly_run_in_background.ipynb   # Python server: face + gesture detection
├── requirements.txt                     # Python dependencies
├── README_SETUP.md                      # Setup and run guide
├── main_csharp/
│   └── TUIO11_NET-master/               # C# GUI application (TuioDemo)
│       ├── TUIO/                        # TUIO protocol client library
│       ├── OSC.NET/                     # OSC networking layer
│       ├── TuioDemo.cs                  # Main C# application entry point
│       ├── TuioDump.cs                  # TUIO debug/dump utility
│       └── TUIO_CSHARP.sln              # Visual Studio solution
├── reactVision/                         # reacTIVision tracker (pre-built binary)
│   ├── reacTIVision.exe                 # Fiducial marker tracker
│   ├── reacTIVision.xml                 # Tracker configuration
│   ├── symbols/                         # Printable fiducial marker sheets
│   └── calibration/                     # Camera calibration PDFs
└── Docs/
    ├── project_description.txt          # Full project specification
    └── main word file/
        └── Project Documentation Museum .md   # Formatted documentation
```

---

## Architecture

The system runs as three separate processes that communicate over local sockets and the TUIO/OSC protocol:

```
[IP Camera / Webcam]
        |
        v
[Python Jupyter Notebook]  ──socket:5000──>  [C# TuioDemo GUI]
  - OpenCV frame capture                          |
  - MediaPipe Holistic (hands + face mesh)        |
  - DeepFace (Facenet face recognition)           |
                                                  |
[reacTIVision.exe]  ──OSC/UDP:3333──────────────>|
  - Fiducial marker detection                     |
  - Marker ID, position, rotation                 |
                                                  v
                                         [Display / Projector]
```

### Component Breakdown

#### 1. Python Server (`constantly_run_in_background.ipynb`)
- Connects to an IP camera stream (`http://192.168.1.63:8080/video`)
- Runs **MediaPipe Holistic** for real-time hand landmark and face mesh detection
- Runs **DeepFace (Facenet model)** every 60 frames for face recognition
  - Builds an in-memory face registry (`face_ids` dict) using embedding distance threshold of 15
  - Identifies returning visitors vs. new faces
- Sends recognition events over a TCP socket to the C# app on `localhost:5000`
- Displays annotated video feed with landmarks drawn

#### 2. C# GUI Application (`TuioDemo.cs`)
- Listens for TUIO events from reacTIVision over OSC/UDP port 3333
- Listens for face/gesture events from the Python server over TCP port 5000
- Renders the interactive museum UI (Windows Forms / GDI+)
- Maps TUIO marker IDs to artifacts:
  - Marker 0 → Ancient Pottery Collection
  - Marker 1 → Medieval Manuscript
  - Marker 2 → Renaissance Painting
- Shows visual feedback: yellow glow on detected markers, green/red face indicator
- Supports keyboard shortcuts: F1 (fullscreen), ESC (exit), V (verbose)

#### 3. reacTIVision (`reactVision/reacTIVision.exe`)
- Open-source fiducial marker tracker
- Detects printed amoeba/classic markers via camera
- Broadcasts marker ID, X/Y position, and rotation angle over OSC/UDP
- Configured via `reacTIVision.xml` and `camera.xml`

---

## Core Features

### Tangible Artifact Exploration (TUIO)
| Action | Result |
|--------|--------|
| Place marker on surface | Artifact page opens (≤1 second) |
| Rotate token | Rotates 3D model / image |
| Move token closer/farther | Zoom or switch detail level |

### Gesture Recognition (MediaPipe)
| Gesture | Action |
|---------|--------|
| Swipe Right | Save to Favorites |
| Swipe Up | Add to Explore Later |
| Thumbs Up | Mark as "Good to See" |
| Pinch | Zoom artifact |
| Open Palm | Return Home / hide panels |

### Face Login & Emotion Awareness
- Face-based login/registration using Facenet embeddings
- Optional emotion-aware personalization (opt-in with consent)
- Tracks "interest score" per artifact category based on positive affect signals
- Guest mode available (no face storage, temporary session only)

---

## User Personas

| Persona | Needs | How MuseSense Helps |
|---------|-------|---------------------|
| Casual Visitor | Quick, fun exploration | Tokens + gesture saves + short summaries |
| Student Researcher | Deep context, collect items | Saved lists, history, export notes |
| Family / Group | Interactive, playful | Multi-user mode, group favorites |

---

## Data Model

```
User
  ├── userId
  ├── faceTemplateHash (Facenet embedding)
  ├── consentFlags { faceAuth, emotionPersonalization }
  ├── favorites[]
  ├── exploreLater[]
  ├── goodToSee[]
  └── categoryScores { ancient, modern, sculpture, ... }

Artifact
  ├── artifactId
  ├── name, era, category, description
  ├── media (images, 3D model paths)
  └── tuioMarkerId

Events (analytics)
  ├── markerPlaced / markerRemoved
  ├── gestureRecognized
  ├── listAdd / listRemove
  ├── sessionDuration
  └── recommendationClicks
```

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Marker tracking | reacTIVision 1.5.1 + TUIO 1.1 protocol |
| Gesture + face detection | Python, OpenCV, MediaPipe Holistic |
| Face recognition | DeepFace (Facenet model) |
| GUI / display | C# (.NET, Windows Forms) |
| IPC communication | TCP socket (port 5000), OSC/UDP (port 3333) |
| Camera input | IP camera stream (DroidCam or similar) |
| Python environment | venv (Python 3.10) |

---

## How to Run

### Prerequisites
- Python venv (download from Google Drive link in `README_SETUP.md`)
- Visual Studio (to build C# solution)
- Printed TUIO amoeba markers
- Camera (webcam or IP camera app on phone)

### Step 1 — Start Python Server
```powershell
# Activate venv and launch notebook
venv\Scripts\jupyter notebook constantly_run_in_background.ipynb
# Run all cells — server starts listening on port 5000
```

### Step 2 — Start reacTIVision
```powershell
.\reactVision\reacTIVision.exe
# Starts broadcasting TUIO events on UDP port 3333
```

### Step 3 — Run C# Application
```powershell
cd main_csharp\TUIO11_NET-master
msbuild TUIO_CSHARP.sln
.\bin\Debug\TuioDemo.exe
# Or open solution in Visual Studio and run TuioDemo project
```

---

## MVP Requirements (Course Deliverables)

- [ ] TUIO markers load at least 6 artifacts
- [ ] Face login/register (or guest mode — face must be implemented)
- [ ] 5 gestures: swipe right, swipe up, pinch, open palm, thumbs up
- [ ] 3 user lists: Favorites, Explore Later, Good to See
- [ ] Basic category-based recommendations

---

## Non-Functional Requirements

| Requirement | Target |
|-------------|--------|
| Usability | Learnable within 2 minutes |
| Responsiveness | 30 FPS tracking, low-latency feedback |
| Privacy | Minimal biometric storage, consent + delete option |
| Reliability | Graceful fallback when face/gesture fails |
| Security | Encrypted profile storage (if networked) |

---

## Known Issues & Mitigations

| Risk | Mitigation |
|------|-----------|
| Gesture misrecognition | Fallback on-screen buttons |
| Lighting issues for markers | Controlled lighting + reacTIVision calibration |
| Face privacy concerns | Guest mode + opt-in consent + delete profile |
| Complexity creep | MVP locked: TUIO + 3 gestures + basic login |

---

## Python Dependencies (`requirements.txt`)

```
opencv-python     # Camera capture and image processing
mediapipe         # Hand gesture and face mesh detection
numpy             # Numerical operations / embedding math
deepface          # Face recognition (Facenet model)
tensorflow        # DeepFace backend
ipykernel         # Jupyter notebook kernel
```

---

## Project Milestones

| Week | Goal |
|------|------|
| 1 | Requirements + UI wireframes + marker mapping |
| 2 | TUIO integration + artifact viewer |
| 3 | Gesture recognition + list curation |
| 4 | Face login + profile storage |
| 5 | Recommendations + onboarding + accessibility |
| 6 | Evaluation + report + demo polishing |

---

## HCI Evaluation Plan

- 8–15 participants (students/friends)
- Tasks: login, explore 3 artifacts, save to each list, follow a recommendation
- Metrics: task completion rate, time on task, error rate, SUS satisfaction score
- Expected outcome: tangible interaction improves discoverability; gesture saves reduce navigation time
