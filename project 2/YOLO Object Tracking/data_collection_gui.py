from __future__ import annotations

import time
from datetime import datetime
from pathlib import Path
from tkinter import BooleanVar, Button, DoubleVar, IntVar, Label, StringVar, Tk, ttk

import cv2
from PIL import Image, ImageTk


BASE_DIR = Path(__file__).resolve().parent
RAW_VIDEOS_DIR = BASE_DIR / "raw_videos"
RAW_FRAMES_DIR = BASE_DIR / "raw_frames"
CLASSES = ("pyramid", "tutankhamun_mask", "nefertiti_head", "mixed")
VIEWS = (
    "front",
    "side_left",
    "side_right",
    "top",
    "angled",
    "near",
    "far",
    "different_background",
    "hand_occlusion",
    "multi_object",
)
VIDEO_ENCODERS = (
    ("wmv", "WMV2"),
    ("avi", "XVID"),
    ("avi", "MJPG"),
)
VIDEO_FPS = 20.0


class DataCollectionGui:
    def __init__(self) -> None:
        self.root = Tk()
        self.root.title("YOLO Artifact Data Collector")
        self.root.geometry("1040x720")

        self.selected_class = StringVar(value=CLASSES[0])
        self.selected_view = StringVar(value=VIEWS[0])
        self.camera_index = IntVar(value=0)
        self.camera_source = StringVar(value="0")
        self.capture_fps = DoubleVar(value=3.0)
        self.recording = BooleanVar(value=False)
        self.auto_capture = BooleanVar(value=False)
        self.status = StringVar(value="Ready")

        self.cap: cv2.VideoCapture | None = None
        self.writer: cv2.VideoWriter | None = None
        self.current_frame = None
        self.video_path: Path | None = None
        self.last_auto_capture_at = 0.0
        self.frames_saved = 0
        self.videos_saved = 0
        self.count_rows: list[ttk.Label] = []

        self._build_dirs()
        self._build_ui()
        self._open_camera()
        self._update_frame()

    def _build_dirs(self) -> None:
        for class_name in CLASSES:
            for view_name in VIEWS:
                (RAW_VIDEOS_DIR / class_name / view_name).mkdir(
                    parents=True, exist_ok=True
                )
                (RAW_FRAMES_DIR / class_name / view_name).mkdir(
                    parents=True, exist_ok=True
                )

    def _build_ui(self) -> None:
        self.preview = Label(self.root, bg="#111111")
        self.preview.pack(side="left", fill="both", expand=True, padx=10, pady=10)

        panel = ttk.Frame(self.root, padding=12)
        panel.pack(side="right", fill="y")

        ttk.Label(panel, text="Class").pack(anchor="w")
        class_menu = ttk.Combobox(
            panel,
            textvariable=self.selected_class,
            values=CLASSES,
            state="readonly",
            width=24,
        )
        class_menu.pack(fill="x", pady=(0, 12))
        class_menu.bind("<<ComboboxSelected>>", lambda _event: self._refresh_counts())

        ttk.Label(panel, text="View / Scenario").pack(anchor="w")
        view_menu = ttk.Combobox(
            panel,
            textvariable=self.selected_view,
            values=VIEWS,
            state="readonly",
            width=24,
        )
        view_menu.pack(fill="x", pady=(0, 12))
        view_menu.bind("<<ComboboxSelected>>", lambda _event: self._refresh_counts())

        ttk.Label(panel, text="Camera Source").pack(anchor="w")
        camera_entry = ttk.Entry(panel, textvariable=self.camera_source, width=24)
        camera_entry.pack(fill="x", pady=(0, 6))
        ttk.Label(
            panel,
            text='Use "0" for webcam or DroidCam URL',
            wraplength=240,
        ).pack(anchor="w", pady=(0, 8))

        ttk.Label(panel, text="Quick Webcam Index").pack(anchor="w")
        camera_spin = ttk.Spinbox(
            panel,
            from_=0,
            to=5,
            textvariable=self.camera_index,
            width=8,
            command=self._use_camera_index,
        )
        camera_spin.pack(anchor="w", pady=(0, 6))
        Button(panel, text="Open Camera Source", command=self._restart_camera).pack(
            fill="x", pady=(0, 12)
        )

        ttk.Label(panel, text="Auto Frame FPS").pack(anchor="w")
        fps_spin = ttk.Spinbox(
            panel,
            from_=0.5,
            to=10.0,
            increment=0.5,
            textvariable=self.capture_fps,
            width=8,
        )
        fps_spin.pack(anchor="w", pady=(0, 16))

        Button(panel, text="Save Frame", command=self._save_frame).pack(
            fill="x", pady=4
        )
        Button(panel, text="Start Auto Frames", command=self._start_auto_capture).pack(
            fill="x", pady=4
        )
        Button(panel, text="Stop Auto Frames", command=self._stop_auto_capture).pack(
            fill="x", pady=4
        )

        ttk.Separator(panel).pack(fill="x", pady=16)

        Button(panel, text="Start Recording", command=self._start_recording).pack(
            fill="x", pady=4
        )
        Button(panel, text="Stop Recording", command=self._stop_recording).pack(
            fill="x", pady=4
        )

        ttk.Separator(panel).pack(fill="x", pady=16)

        ttk.Label(panel, text="Collection Tips").pack(anchor="w")
        tips = (
            "Pick one view, capture 20-40 sec.\n"
            "Move slowly to avoid blur.\n"
            "Use mixed + multi_object for scenes\n"
            "with more than one object.\n"
            "Delete blurry frames later."
        )
        ttk.Label(panel, text=tips, justify="left").pack(anchor="w", pady=(4, 16))

        ttk.Label(panel, text="Current Class Counts").pack(anchor="w")
        counts_frame = ttk.Frame(panel)
        counts_frame.pack(fill="x", pady=(4, 12))
        for view_name in VIEWS:
            row = ttk.Label(counts_frame, text="")
            row.pack(anchor="w")
            self.count_rows.append(row)

        self.counter_label = ttk.Label(panel, text="Frames: 0 | Videos: 0")
        self.counter_label.pack(anchor="w", pady=(0, 8))

        ttk.Label(panel, textvariable=self.status, wraplength=250).pack(
            anchor="w", pady=(8, 0)
        )

        self.root.protocol("WM_DELETE_WINDOW", self._close)
        self._refresh_counts()

    def _open_camera(self) -> None:
        self._release_camera()
        sources = self._resolve_camera_sources()
        opened_source = None
        for source in sources:
            cap = cv2.VideoCapture(source)
            if cap.isOpened():
                self.cap = cap
                opened_source = source
                break
            cap.release()

        if self.cap is None or opened_source is None:
            self.current_frame = None
            self.preview.configure(image="", text="Camera source could not be opened")
            self.status.set(
                "Could not open camera source. Tried: "
                + ", ".join(str(source) for source in sources)
            )
            return

        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
        self.camera_source.set(str(opened_source))
        self.status.set(f"Camera source opened: {opened_source}")

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

    def _use_camera_index(self) -> None:
        self.camera_source.set(str(self.camera_index.get()))
        self._restart_camera()

    def _restart_camera(self) -> None:
        self._stop_recording()
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
                self._write_video_frame(frame)
                self._maybe_auto_capture(frame)
                self._show_preview(frame)

        self.root.after(15, self._update_frame)

    def _show_preview(self, frame) -> None:
        preview = frame.copy()
        class_name = self.selected_class.get()
        view_name = self.selected_view.get()
        mode = []
        if self.recording.get():
            mode.append("REC")
        if self.auto_capture.get():
            mode.append("AUTO")
        mode_text = " | ".join(mode) if mode else "IDLE"

        cv2.putText(
            preview,
            f"{class_name} / {view_name}  {mode_text}",
            (18, 34),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.75,
            (0, 220, 255),
            2,
        )

        preview = cv2.cvtColor(preview, cv2.COLOR_BGR2RGB)
        image = Image.fromarray(preview)
        image.thumbnail((760, 680))
        photo = ImageTk.PhotoImage(image=image)
        self.preview.configure(image=photo)
        self.preview.image = photo

    def _make_stem(self, class_name: str) -> str:
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S_%f")[:-3]
        return f"{class_name}_{self.selected_view.get()}_{timestamp}"

    def _save_frame(self) -> None:
        if self.current_frame is None:
            self.status.set("No camera frame available yet")
            return

        self._save_frame_to_disk(self.current_frame)

    def _save_frame_to_disk(self, frame) -> None:
        class_name = self.selected_class.get()
        view_name = self.selected_view.get()
        path = RAW_FRAMES_DIR / class_name / view_name / f"{self._make_stem(class_name)}.jpg"
        cv2.imwrite(str(path), frame)
        self.frames_saved += 1
        self._update_counts()
        self._refresh_counts()
        self.status.set(f"Saved frame: {path.relative_to(BASE_DIR)}")

    def _start_auto_capture(self) -> None:
        self.auto_capture.set(True)
        self.last_auto_capture_at = 0.0
        self.status.set("Auto frame capture started")

    def _stop_auto_capture(self) -> None:
        self.auto_capture.set(False)
        self.status.set("Auto frame capture stopped")

    def _maybe_auto_capture(self, frame) -> None:
        if not self.auto_capture.get():
            return

        fps = max(float(self.capture_fps.get()), 0.1)
        now = time.monotonic()
        if now - self.last_auto_capture_at >= 1.0 / fps:
            self.last_auto_capture_at = now
            self._save_frame_to_disk(frame)

    def _start_recording(self) -> None:
        if self.current_frame is None:
            self.status.set("No camera frame available yet")
            return
        if self.recording.get():
            return

        class_name = self.selected_class.get()
        view_name = self.selected_view.get()
        stem = self._make_stem(class_name)
        height, width = self.current_frame.shape[:2]
        output_dir = RAW_VIDEOS_DIR / class_name / view_name

        writer = None
        selected_path = None
        selected_codec = None
        for extension, codec in VIDEO_ENCODERS:
            candidate_path = output_dir / f"{stem}.{extension}"
            fourcc = cv2.VideoWriter_fourcc(*codec)
            candidate_writer = cv2.VideoWriter(
                str(candidate_path),
                fourcc,
                VIDEO_FPS,
                (width, height),
            )
            if candidate_writer.isOpened():
                writer = candidate_writer
                selected_path = candidate_path
                selected_codec = codec
                break
            candidate_writer.release()

        if writer is None or selected_path is None:
            self.writer = None
            self.video_path = None
            self.status.set("Could not start video writer with WMV2, XVID, or MJPG")
            return

        self.writer = writer
        self.video_path = selected_path

        self.recording.set(True)
        self.status.set(
            f"Recording {selected_codec}: {self.video_path.relative_to(BASE_DIR)}"
        )

    def _write_video_frame(self, frame) -> None:
        if self.recording.get() and self.writer is not None:
            self.writer.write(frame)

    def _stop_recording(self) -> None:
        if self.writer is not None:
            self.writer.release()
            self.writer = None

        if self.recording.get():
            self.recording.set(False)
            self.videos_saved += 1
            self._update_counts()
            self._refresh_counts()
            if self.video_path is not None:
                self.status.set(f"Saved video: {self.video_path.relative_to(BASE_DIR)}")

        self.video_path = None

    def _update_counts(self) -> None:
        self.counter_label.configure(
            text=f"Frames: {self.frames_saved} | Videos: {self.videos_saved}"
        )

    def _refresh_counts(self) -> None:
        class_name = self.selected_class.get()
        for view_name, row in zip(VIEWS, self.count_rows):
            frame_count = len(list((RAW_FRAMES_DIR / class_name / view_name).glob("*.jpg")))
            video_count = sum(
                len(list((RAW_VIDEOS_DIR / class_name / view_name).glob(pattern)))
                for pattern in ("*.wmv", "*.avi", "*.mp4")
            )
            marker = "*" if view_name == self.selected_view.get() else " "
            row.configure(
                text=f"{marker} {view_name}: {frame_count} frames, {video_count} videos"
            )

    def _close(self) -> None:
        self._stop_recording()
        self._release_camera()
        self.root.destroy()

    def run(self) -> None:
        self.root.mainloop()


if __name__ == "__main__":
    DataCollectionGui().run()
