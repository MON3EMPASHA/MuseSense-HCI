from __future__ import annotations

import random
import shutil
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
RAW_FRAMES_DIR = BASE_DIR / "raw_frames"
DATASET_DIR = BASE_DIR / "dataset"

CLASSES = ("pyramid", "tutankhamun_mask", "nefertiti_head")
SPLITS = {
    "train": 0.7,
    "val": 0.2,
    "test": 0.1,
}
RANDOM_SEED = 42
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png"}


def collect_images() -> dict[str, list[Path]]:
    images_by_class: dict[str, list[Path]] = {class_name: [] for class_name in CLASSES}

    for class_name in CLASSES:
        class_dir = RAW_FRAMES_DIR / class_name
        if not class_dir.exists():
            continue
        images_by_class[class_name] = [
            path
            for path in class_dir.rglob("*")
            if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
        ]

    mixed_dir = RAW_FRAMES_DIR / "mixed"
    if mixed_dir.exists():
        mixed_images = [
            path
            for path in mixed_dir.rglob("*")
            if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
        ]
        images_by_class.setdefault("mixed", []).extend(mixed_images)

    return images_by_class


def clean_dataset_images() -> None:
    for split in SPLITS:
        images_dir = DATASET_DIR / "images" / split
        labels_dir = DATASET_DIR / "labels" / split
        if images_dir.exists():
            shutil.rmtree(images_dir)
        if labels_dir.exists():
            shutil.rmtree(labels_dir)
        images_dir.mkdir(parents=True, exist_ok=True)
        labels_dir.mkdir(parents=True, exist_ok=True)


def split_paths(paths: list[Path]) -> dict[str, list[Path]]:
    shuffled = list(paths)
    random.shuffle(shuffled)

    total = len(shuffled)
    train_end = int(total * SPLITS["train"])
    val_end = train_end + int(total * SPLITS["val"])

    return {
        "train": shuffled[:train_end],
        "val": shuffled[train_end:val_end],
        "test": shuffled[val_end:],
    }


def safe_name(path: Path, prefix: str) -> str:
    relative = path.relative_to(RAW_FRAMES_DIR)
    stem = "_".join(relative.with_suffix("").parts)
    return f"{prefix}_{stem}{path.suffix.lower()}"


def copy_split(images_by_group: dict[str, list[Path]]) -> dict[str, int]:
    copied_counts = {split: 0 for split in SPLITS}

    for group_name, paths in images_by_group.items():
        for split, split_images in split_paths(paths).items():
            for source in split_images:
                target_name = safe_name(source, group_name)
                target = DATASET_DIR / "images" / split / target_name
                shutil.copy2(source, target)
                copied_counts[split] += 1

    return copied_counts


def write_data_yaml() -> None:
    data_yaml = DATASET_DIR / "data.yaml"
    data_yaml.write_text(
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
    random.seed(RANDOM_SEED)
    images_by_group = collect_images()

    print("Raw frame counts:")
    for group_name, paths in sorted(images_by_group.items()):
        print(f"  {group_name}: {len(paths)}")

    clean_dataset_images()
    copied_counts = copy_split(images_by_group)
    write_data_yaml()

    print("\nDataset split created:")
    for split, count in copied_counts.items():
        print(f"  {split}: {count}")
    print(f"\nOutput: {DATASET_DIR}")
    print("Next step: annotate images inside dataset/images/* and save YOLO labels into dataset/labels/*.")


if __name__ == "__main__":
    main()
