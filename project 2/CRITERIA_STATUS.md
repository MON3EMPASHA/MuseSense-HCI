# MuseSense Criteria Status

This file maps the grading criteria to the current Project 2 implementation.

## Current fit with the project
MuseSense is currently implemented as a Python-based museum interaction engine with:
- face-based login / identity mapping
- gesture recognition using DollarPy
- context-aware CRUD and recommendations
- optional YOLO object tracking
- expression and gaze adaptation heuristics
- event logging for evaluation

Unity AR is still deferred, so the current codebase focuses on the sensing and context engine first.

## Criteria mapping

1. Display AR Object
- Not in the Python backend yet.
- This will require the Unity AR Mobile Template later.

2. Utilizing Object Tracking with YOLO
- Implemented in `object_tracking.py`.
- Integrated into `main.py` with overlay and event logging.

3. CRUD Content based on the context
- Implemented in `context_store.py`.
- Supports list creation, deletion, reads, and score updates.

4. Applying context awareness service (scenario)
- Implemented as a rule-based recommendation flow.
- Context is derived from gesture, object, expression, and gaze events.

5. Classifying trajectories (Skeleton/Object/Laser) using 1 dollar in scenario
- Existing DollarPy gesture recognizer remains in use.
- Gesture results now trigger project actions instead of only console output.

6. Face Identification scenario
- Implemented through the current face/Bluetooth login flow.
- Can be extended later with stronger face embedding logic if needed.

7. Facial Expressions monitor and developing adaptive interface
- Implemented with MediaPipe FaceMesh heuristics in `expression_tracker.py`.
- Used to adapt feedback and category scores.

8. Gaze Tracking reports or hits, analysis and adapting interface
- Implemented with `gaze_tracker.py`.
- Gaze hits are counted and logged in events.

9. Evaluation Experiment
- Prepared through `EVALUATION_PROTOCOL.md`.
- Needs real participant testing.

10. Writing Results
- Prepared through `RESULTS_AND_FEEDBACK_TEMPLATE.md`.
- Needs actual metrics from the experiment.

11. Evaluation Feedback
- Included in the evaluation template.
- Needs a final participant summary.

12. Bonus enrich idea
- Recommended bonus: session summary export with favorites and recommendations.

## Implementation files
- `main.py`
- `context_store.py`
- `event_protocol.py`
- `object_tracking.py`
- `expression_tracker.py`
- `gaze_tracker.py`
- `movements.py`
