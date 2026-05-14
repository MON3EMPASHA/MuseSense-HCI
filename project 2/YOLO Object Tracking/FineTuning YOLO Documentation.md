# YOLO Object Tracking Summary

This folder contains the full YOLO artifact-detection task for three physical/3D Egyptian objects:

```text
pyramid
tutankhamun_mask
nefertiti_head
```

The final goal is a separate live video test GUI that detects the objects, draws bounding boxes, and shows a stable artifact focus before any integration with the main app.

## Final Status

Completed:

```text
data collection GUI
raw frame collection
dataset splitting
Roboflow annotation/export
Roboflow import and label conversion
annotation validation
YOLO11L training on Kaggle
YOLO11S training on Kaggle
live detection GUI
model preset switching in GUI
```

Current trained models:

```text
YOLO Object Tracking/models/artifact_yolo11s_best.pt
YOLO Object Tracking/models/artifact_yolo11l_best.pt
```

Use `YOLO11S fast` for live webcam/DroidCam testing. Use `YOLO11L accurate` when comparing accuracy and delay is acceptable.

## Main Files

```text
data_collection_gui.py
prepare_dataset_split.py
import_roboflow_yolo_export.py
validate_annotations.py
live_detection_gui.py
train_yolo11l.ipynb
ancient-egypt-artifact-detector-with-yolo11l.ipynb
dataset/
models/
raw_frames/
raw_videos/
```

## Step 1: Data Collection

Run the collection GUI:

```powershell
python "YOLO Object Tracking/data_collection_gui.py"
```

The GUI supports webcam and DroidCam sources.

Common DroidCam sources:

```text
0
1
http://192.168.1.22:4747/video
http://192.168.1.22:4747/mjpegfeed
```

Use **Start Auto Frames** as the main capture method. Videos are optional backup only.

Recommended auto-frame setting:

```text
Auto Frame FPS: 3
```

Capture views/scenarios:

```text
front
side_left
side_right
top
angled
near
far
different_background
hand_occlusion
multi_object
```

Recommended capture mix:

```text
70% object alone
20% hand near or holding object
10% multiple objects together
```

For multi-object scenes, use:

```text
Class: mixed
View / Scenario: multi_object
```

Do not create a `mixed` training class later. Mixed images are annotated with the real visible object classes.

## Data Collection Audit

Initial audit found the dataset needed more `tutankhamun_mask` and mixed frames. After recapture, the usable raw frame totals became:

```text
pyramid: 225
nefertiti_head: 210
tutankhamun_mask: 247
mixed: 125
total: 807 frames
```

Image checks:

```text
all checked frames were 1280x720
no tiny files under 10 KB
no obvious corrupt image files
```

## Step 2: Dataset Setup

Create train/val/test dataset folders:

```powershell
python "YOLO Object Tracking/prepare_dataset_split.py"
```

The intended YOLO dataset structure is:

```text
YOLO Object Tracking/dataset/
  images/
    train/
    val/
    test/
  labels/
    train/
    val/
    test/
  data.yaml
```

Class order:

```text
0: pyramid
1: tutankhamun_mask
2: nefertiti_head
```

## Step 3: Annotation

Annotation was done with Roboflow.

Rules:

```text
draw tight boxes around visible objects
label partly hidden objects if recognizable
label every target object in mixed images
do not label hands as part of the object
do not create a mixed class
```

Roboflow export was imported locally with:

```powershell
python "YOLO Object Tracking/import_roboflow_yolo_export.py" "C:\Users\msi\Downloads\Artifacts Object Tracking.yolov11"
```

The importer:

```text
copies annotated images and labels into dataset/
splits into train/val/test
remaps Roboflow class IDs to the local class order
converts polygon labels into YOLO detection boxes if needed
```

Validate annotations:

```powershell
python "YOLO Object Tracking/validate_annotations.py"
```

Final validation result:

```text
Images: 724
Label files found: 724
Missing label files: 0
Invalid label files: 0

pyramid: 276 boxes
tutankhamun_mask: 319 boxes
nefertiti_head: 274 boxes
```

Final split:

```text
train: 506 images / 506 labels
val: 144 images / 144 labels
test: 74 images / 74 labels
```

## Step 4: Dataset YAML

Local dataset file:

```text
YOLO Object Tracking/dataset/data.yaml
```

Notebook/training-safe file:

```text
YOLO Object Tracking/dataset/data_train.yaml
```

Class mapping:

```yaml
names:
  0: pyramid
  1: tutankhamun_mask
  2: nefertiti_head
```

## Step 5: Training

Training was done on Kaggle because YOLO11L is heavy for live/local training.

Recommended Kaggle accelerator:

```text
GPU T4 x2
```

Kaggle inputs used:

```text
/kaggle/input/datasets/abdelmonemhatem/egyptian-artifacts-yolo11-dataset
/kaggle/input/models/abdelmonemhatem/yollo11l/pytorch/default/1
```

Notebook:

```text
YOLO Object Tracking/ancient-egypt-artifact-detector-with-yolo11l.ipynb
```

Earlier local notebook:

```text
YOLO Object Tracking/train_yolo11l.ipynb
```

Models trained:

```text
YOLO11L: more accurate, heavier
YOLO11S: faster, better for live video
```

Final downloaded models:

```text
YOLO Object Tracking/models/artifact_yolo11l_best.pt
YOLO Object Tracking/models/artifact_yolo11s_best.pt
```

## Step 6: Live Test GUI

Run:

```powershell
python "YOLO Object Tracking/live_detection_gui.py"
```

The GUI:

```text
opens webcam or DroidCam
loads YOLO11S or YOLO11L
detects pyramid, tutankhamun_mask, and nefertiti_head
draws bounding boxes and confidence
shows stable focus after 5 detections in the last 10 checks
```

Model presets:

```text
YOLO11S fast
YOLO11L accurate
```

Recommended live settings:

```text
Model: YOLO11S fast
Detection Interval: 5 to 10
Inference Width: 480 or 640
Confidence: 0.5
```

Faster:

```text
Detection Interval: 10
Inference Width: 480
```

More accurate but slower:

```text
Detection Interval: 2
Inference Width: 800 or 960
```

## Dependencies

For local GUI/model loading:

```powershell
.\.venv\Scripts\Activate.ps1
python -m pip install torch torchvision torchaudio
python -m pip install ultralytics
```

For local RTX GPU support, install CUDA-enabled PyTorch in the project `.venv` instead of CPU-only PyTorch.

## Next Integration Rule

Do not integrate directly into the main app until the separate live GUI is stable.

Integration should only happen after:

```text
all 3 objects are detected correctly
webcam/DroidCam FPS is acceptable
confidence threshold is tuned
stable focus works
labels match the app artifact mapping
```
