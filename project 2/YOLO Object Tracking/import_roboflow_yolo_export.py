from __future__ import annotations

import ast
import random
import shutil
import sys
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
DEFAULT_EXPORT_DIR = Path.home() / "Downloads" / "Artifacts Object Tracking.yolov11"
DATASET_DIR = BASE_DIR / "dataset"
DESIRED_NAMES = ["pyramid", "tutankhamun_mask", "nefertiti_head"]
SPLITS = {"train": 0.7, "val": 0.2, "test": 0.1}
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png"}
RANDOM_SEED = 42
EDGE_EPSILON = 0.000001


def read_export_names(export_dir: Path) -> list[str]:
    data_yaml = export_dir / "data.yaml"
    for line in data_yaml.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped.startswith("names:"):
            value = stripped.split(":", 1)[1].strip()
            names = ast.literal_eval(value)
            if isinstance(names, list):
                return [str(name) for name in names]
    raise ValueError(f"Could not read names from {data_yaml}")


def find_source_dirs(export_dir: Path) -> tuple[Path, Path]:
    candidates = [
        (export_dir / "train" / "images", export_dir / "train" / "labels"),
        (export_dir / "images", export_dir / "labels"),
    ]
    for images_dir, labels_dir in candidates:
        if images_dir.exists() and labels_dir.exists():
            return images_dir, labels_dir
    raise FileNotFoundError("Could not find Roboflow images/labels folders")


def collect_pairs(images_dir: Path, labels_dir: Path) -> list[tuple[Path, Path]]:
    pairs: list[tuple[Path, Path]] = []
    for image_path in sorted(images_dir.iterdir()):
        if not image_path.is_file() or image_path.suffix.lower() not in IMAGE_EXTENSIONS:
            continue
        label_path = labels_dir / f"{image_path.stem}.txt"
        if label_path.exists():
            pairs.append((image_path, label_path))
    return pairs


def split_pairs(pairs: list[tuple[Path, Path]]) -> dict[str, list[tuple[Path, Path]]]:
    shuffled = list(pairs)
    random.shuffle(shuffled)
    total = len(shuffled)
    train_end = int(total * SPLITS["train"])
    val_end = train_end + int(total * SPLITS["val"])
    return {
        "train": shuffled[:train_end],
        "val": shuffled[train_end:val_end],
        "test": shuffled[val_end:],
    }


def clean_dataset() -> None:
    for split in SPLITS:
        for kind in ("images", "labels"):
            target = DATASET_DIR / kind / split
            if target.exists():
                shutil.rmtree(target)
            target.mkdir(parents=True, exist_ok=True)


def convert_line(line: str, class_id_map: dict[int, int]) -> str | None:
    parts = line.strip().split()
    if len(parts) < 5:
        return None

    old_class_id = int(float(parts[0]))
    if old_class_id not in class_id_map:
        return None
    new_class_id = class_id_map[old_class_id]

    values = [float(value) for value in parts[1:]]

    if len(values) == 4:
        raw_x_center, raw_y_center, raw_width, raw_height = values
        x_min = raw_x_center - raw_width / 2
        x_max = raw_x_center + raw_width / 2
        y_min = raw_y_center - raw_height / 2
        y_max = raw_y_center + raw_height / 2
    else:
        if len(values) % 2 != 0:
            return None
        xs = values[0::2]
        ys = values[1::2]

        x_min = min(xs)
        x_max = max(xs)
        y_min = min(ys)
        y_max = max(ys)

    x_min = max(EDGE_EPSILON, min(1.0 - EDGE_EPSILON, x_min))
    x_max = max(EDGE_EPSILON, min(1.0 - EDGE_EPSILON, x_max))
    y_min = max(EDGE_EPSILON, min(1.0 - EDGE_EPSILON, y_min))
    y_max = max(EDGE_EPSILON, min(1.0 - EDGE_EPSILON, y_max))

    width = x_max - x_min
    height = y_max - y_min
    x_center = x_min + width / 2
    y_center = y_min + height / 2

    if width <= 0 or height <= 0:
        return None

    return f"{new_class_id} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}"


def convert_label_file(
    source: Path,
    target: Path,
    class_id_map: dict[int, int],
) -> tuple[int, int]:
    converted: list[str] = []
    skipped = 0
    for line in source.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        converted_line = convert_line(line, class_id_map)
        if converted_line is None:
            skipped += 1
            continue
        converted.append(converted_line)

    target.write_text("\n".join(converted) + ("\n" if converted else ""), encoding="utf-8")
    return len(converted), skipped


def write_data_yaml() -> None:
    (DATASET_DIR / "data.yaml").write_text(
        "\n".join(
            [
                "path: .",
                "train: images/train",
                "val: images/val",
                "test: images/test",
                "",
                "names:",
                "  0: pyramid",
                "  1: tutankhamun_mask",
                "  2: nefertiti_head",
                "",
            ]
        ),
        encoding="utf-8",
    )


def main() -> None:
    export_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_EXPORT_DIR
    export_dir = export_dir.resolve()
    if not export_dir.exists():
        raise FileNotFoundError(f"Roboflow export folder not found: {export_dir}")

    export_names = read_export_names(export_dir)
    class_id_map = {
        old_id: DESIRED_NAMES.index(name)
        for old_id, name in enumerate(export_names)
        if name in DESIRED_NAMES
    }

    print("Roboflow class order:")
    for old_id, name in enumerate(export_names):
        mapped = class_id_map.get(old_id)
        print(f"  {old_id}: {name} -> {mapped}")

    images_dir, labels_dir = find_source_dirs(export_dir)
    pairs = collect_pairs(images_dir, labels_dir)
    print(f"\nAnnotated image/label pairs found: {len(pairs)}")

    random.seed(RANDOM_SEED)
    splits = split_pairs(pairs)
    clean_dataset()

    object_counts = {0: 0, 1: 0, 2: 0}
    skipped_boxes = 0
    for split, split_pairs_list in splits.items():
        for image_path, label_path in split_pairs_list:
            image_target = DATASET_DIR / "images" / split / image_path.name
            label_target = DATASET_DIR / "labels" / split / label_path.name
            shutil.copy2(image_path, image_target)
            converted_count, skipped_count = convert_label_file(
                label_path, label_target, class_id_map
            )
            skipped_boxes += skipped_count
            if converted_count:
                for line in label_target.read_text(encoding="utf-8").splitlines():
                    object_counts[int(line.split()[0])] += 1

    write_data_yaml()

    print("\nLocal dataset imported:")
    for split, split_pairs_list in splits.items():
        print(f"  {split}: {len(split_pairs_list)} images")
    print("\nObject counts after class remap and bbox conversion:")
    print(f"  pyramid: {object_counts[0]}")
    print(f"  tutankhamun_mask: {object_counts[1]}")
    print(f"  nefertiti_head: {object_counts[2]}")
    print(f"\nSkipped invalid boxes: {skipped_boxes}")
    print(f"Output: {DATASET_DIR}")


if __name__ == "__main__":
    main()
