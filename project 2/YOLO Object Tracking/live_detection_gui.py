from __future__ import annotations

from collections import Counter, deque
from pathlib import Path
from tkinter import Button, DoubleVar, IntVar, Label, StringVar, Tk, ttk

import cv2
from PIL import Image, ImageTk


BASE_DIR = Path(__file__).resolve().parent
MODELS_DIR = BASE_DIR / "models"
YOLO11S_MODEL_PATH = MODELS_DIR / "artifact_yolo11s_best.pt"
YOLO11L_MODEL_PATH = MODELS_DIR / "artifact_yolo11l_best.pt"
DEFAULT_MODEL_PATH = YOLO11S_MODEL_PATH if YOLO11S_MODEL_PATH.exists() else YOLO11L_MODEL_PATH
MODEL_PRESETS = {
    "YOLO11S fast": YOLO11S_MODEL_PATH,
    "YOLO11L accurate": YOLO11L_MODEL_PATH,
}


ARTIFACT_MAP = {
    "pyramid": "Pyramids of Giza",
    "tutankhamun_mask": "Mask of Tutankhamun",
    "nefertiti_head": "Bust of Nefertiti",
}


class LiveDetectionGui:
    def __init__(self) -> None:
        self.root = Tk()
        self.root.title("YOLO11L Artifact Live Test")
        self.root.geometry("1120x740")

        self.model_path = StringVar(value=str(DEFAULT_MODEL_PATH))
        self.model_preset = StringVar(value=self._default_model_preset())
        self.camera_source = StringVar(value="0")
        self.confidence = DoubleVar(value=0.5)
        self.detection_interval = IntVar(value=5)
        self.inference_width = IntVar(value=640)
        self.status = StringVar(value="Ready")
        self.stable_focus = StringVar(value="Stable focus: -")

        self.model = None
        self.cap: cv2.VideoCapture | None = None
        self.current_frame = None
        self.last_detections: list[dict] = []
        self.frame_counter = 0
        self.focus_history: deque[str | None] = deque(maxlen=10)

        self._build_ui()
        self._load_model()
        self._open_camera()
        self._update_frame()

    def _build_ui(self) -> None:
        self.preview = Label(self.root, bg="#111111")
        self.preview.pack(side="left", fill="both", expand=True, padx=10, pady=10)

        panel = ttk.Frame(self.root, padding=12)
        panel.pack(side="right", fill="y")

        ttk.Label(panel, text="Model Path").pack(anchor="w")
        ttk.Combobox(
            panel,
            textvariable=self.model_preset,
            values=tuple(MODEL_PRESETS.keys()),
            state="readonly",
            width=39,
        ).pack(fill="x", pady=(0, 6))
        Button(panel, text="Use Selected Preset", command=self._use_model_preset).pack(
            fill="x", pady=(0, 8)
        )
        ttk.Entry(panel, textvariable=self.model_path, width=42).pack(
            fill="x", pady=(0, 8)
        )
        Button(panel, text="Load Model", command=self._load_model).pack(
            fill="x", pady=(0, 14)
        )

        ttk.Label(panel, text="Camera Source").pack(anchor="w")
        ttk.Entry(panel, textvariable=self.camera_source, width=42).pack(
            fill="x", pady=(0, 6)
        )
        ttk.Label(
            panel,
            text='Use "0" for webcam or DroidCam URL like http://IP:4747/video',
            wraplength=280,
        ).pack(anchor="w", pady=(0, 8))
        Button(panel, text="Open Camera", command=self._restart_camera).pack(
            fill="x", pady=(0, 14)
        )

        ttk.Label(panel, text="Confidence").pack(anchor="w")
        ttk.Spinbox(
            panel,
            from_=0.1,
            to=0.95,
            increment=0.05,
            textvariable=self.confidence,
            width=8,
        ).pack(anchor="w", pady=(0, 12))

        ttk.Label(panel, text="Detection Interval").pack(anchor="w")
        ttk.Spinbox(
            panel,
            from_=1,
            to=30,
            increment=1,
            textvariable=self.detection_interval,
            width=8,
        ).pack(anchor="w", pady=(0, 14))

        ttk.Label(panel, text="Inference Width").pack(anchor="w")
        ttk.Spinbox(
            panel,
            from_=320,
            to=1280,
            increment=160,
            textvariable=self.inference_width,
            width=8,
        ).pack(anchor="w", pady=(0, 14))

        ttk.Separator(panel).pack(fill="x", pady=14)

        ttk.Label(panel, textvariable=self.stable_focus, wraplength=280).pack(
            anchor="w", pady=(0, 8)
        )
        ttk.Label(panel, textvariable=self.status, wraplength=280).pack(anchor="w")

        self.root.protocol("WM_DELETE_WINDOW", self._close)

    def _default_model_preset(self) -> str:
        if YOLO11S_MODEL_PATH.exists():
            return "YOLO11S fast"
        return "YOLO11L accurate"

    def _use_model_preset(self) -> None:
        selected = self.model_preset.get()
        path = MODEL_PRESETS.get(selected)
        if path is None:
            self.status.set(f"Unknown model preset: {selected}")
            return
        self.model_path.set(str(path))
        self._load_model()

    def _load_model(self) -> None:
        path = Path(self.model_path.get().strip())
        if not path.exists():
            self.model = None
            self.status.set(f"Model not found: {path}")
            return

        try:
            from ultralytics import YOLO

            self.model = YOLO(str(path))
            names = getattr(self.model, "names", {})
            self.status.set(f"Loaded model: {path.name} | classes: {names}")
        except Exception as exc:
            self.model = None
            self.status.set(
                "Could not load model. Install torch + ultralytics in this venv. "
                f"Error: {exc}"
            )

    def _resolve_camera_sources(self) -> list[int | str]:
        source = self.camera_source.get().strip()
        if source.isdigit():
            return [int(source)]

        if source.startswith("http://") or source.startswith("https://"):
            source = source.rstrip("/")
            sources = [source]
            if not source.endswith("/video"):
                sources.append(f"{source}/video")
            if not source.endswith("/mjpegfeed"):
                sources.append(f"{source}/mjpegfeed")
            return sources

        return [source]

    def _open_camera(self) -> None:
        self._release_camera()
        for source in self._resolve_camera_sources():
            cap = cv2.VideoCapture(source)
            if cap.isOpened():
                self.cap = cap
                self.camera_source.set(str(source))
                self.status.set(f"Camera opened: {source}")
                return
            cap.release()
        self.status.set("Could not open camera source")

    def _restart_camera(self) -> None:
        self._open_camera()

    def _release_camera(self) -> None:
        if self.cap is not None:
            self.cap.release()
            self.cap = None

    def _update_frame(self) -> None:
        if self.cap is not None and self.cap.isOpened():
            ok, frame = self.cap.read()
            if ok:
                self.current_frame = frame
                self.frame_counter += 1
                interval = max(int(self.detection_interval.get()), 1)
                if self.frame_counter % interval == 0:
                    self.last_detections = self._detect(frame)
                    self._update_stable_focus()
                self._show_preview(frame)

        self.root.after(15, self._update_frame)

    def _detect(self, frame) -> list[dict]:
        if self.model is None:
            return []

        try:
            inference_frame, scale_x, scale_y = self._make_inference_frame(frame)
            results = self.model(
                inference_frame,
                conf=float(self.confidence.get()),
                imgsz=640,
                verbose=False,
            )
        except Exception as exc:
            self.status.set(f"Detection error: {exc}")
            return []

        if not results or results[0].boxes is None:
            return []

        names = results[0].names
        detections = []
        for box in results[0].boxes:
            conf = float(box.conf[0])
            class_id = int(box.cls[0])
            label = str(names.get(class_id, class_id))
            x1, y1, x2, y2 = [
                int(value) for value in box.xyxy[0].tolist()
            ]
            x1 = int(x1 * scale_x)
            x2 = int(x2 * scale_x)
            y1 = int(y1 * scale_y)
            y2 = int(y2 * scale_y)
            detections.append(
                {
                    "label": label,
                    "confidence": conf,
                    "bbox": [x1, y1, x2, y2],
                }
            )
        detections.sort(key=lambda item: item["confidence"], reverse=True)
        return detections

    def _make_inference_frame(self, frame):
        height, width = frame.shape[:2]
        target_width = max(int(self.inference_width.get()), 160)
        if width <= target_width:
            return frame, 1.0, 1.0

        target_height = int(height * (target_width / width))
        resized = cv2.resize(frame, (target_width, target_height))
        return resized, width / target_width, height / target_height

    def _update_stable_focus(self) -> None:
        best_label = self.last_detections[0]["label"] if self.last_detections else None
        self.focus_history.append(best_label)

        counts = Counter(label for label in self.focus_history if label)
        if not counts:
            self.stable_focus.set("Stable focus: -")
            return

        label, hits = counts.most_common(1)[0]
        if hits >= 5:
            artifact_name = ARTIFACT_MAP.get(label, label)
            self.stable_focus.set(f"Stable focus: {artifact_name} ({hits}/10)")
        else:
            self.stable_focus.set(f"Stable focus: warming up ({label}, {hits}/10)")

    def _show_preview(self, frame) -> None:
        preview = frame.copy()
        for detection in self.last_detections:
            x1, y1, x2, y2 = detection["bbox"]
            label = detection["label"]
            confidence = detection["confidence"]
            color = (0, 215, 255)
            cv2.rectangle(preview, (x1, y1), (x2, y2), color, 2)
            cv2.putText(
                preview,
                f"{label} {confidence:.2f}",
                (x1, max(y1 - 8, 20)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.7,
                color,
                2,
            )

        preview = cv2.cvtColor(preview, cv2.COLOR_BGR2RGB)
        image = Image.fromarray(preview)
        image.thumbnail((820, 700))
        photo = ImageTk.PhotoImage(image=image)
        self.preview.configure(image=photo)
        self.preview.image = photo

    def _close(self) -> None:
        self._release_camera()
        self.root.destroy()

    def run(self) -> None:
        self.root.mainloop()


if __name__ == "__main__":
    LiveDetectionGui().run()
