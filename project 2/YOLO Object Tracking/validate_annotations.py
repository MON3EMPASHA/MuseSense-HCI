from __future__ import annotations

from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
DATASET_DIR = BASE_DIR / "dataset"
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png"}
SPLITS = ("train", "val", "test")
CLASS_IDS = {"0", "1", "2"}


def image_paths(split: str) -> list[Path]:
    images_dir = DATASET_DIR / "images" / split
    return sorted(
        path
        for path in images_dir.rglob("*")
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    )


def label_path_for(image_path: Path, split: str) -> Path:
    return DATASET_DIR / "labels" / split / f"{image_path.stem}.txt"


def validate_label_file(label_path: Path) -> list[str]:
    issues: list[str] = []
    try:
        lines = label_path.read_text(encoding="utf-8").splitlines()
    except Exception as exc:
        return [f"could not read label file: {exc}"]

    if not lines:
        issues.append("empty label file")
        return issues

    for line_number, line in enumerate(lines, start=1):
        stripped = line.strip()
        if not stripped:
            issues.append(f"line {line_number}: blank line")
            continue

        parts = stripped.split()
        if len(parts) != 5:
            issues.append(f"line {line_number}: expected 5 values, got {len(parts)}")
            continue

        class_id = parts[0]
        if class_id not in CLASS_IDS:
            issues.append(f"line {line_number}: invalid class id {class_id}")

        try:
            values = [float(value) for value in parts[1:]]
        except ValueError:
            issues.append(f"line {line_number}: non-numeric box values")
            continue

        x_center, y_center, width, height = values
        for name, value in zip(("x_center", "y_center", "width", "height"), values):
            if value < 0.0 or value > 1.0:
                issues.append(f"line {line_number}: {name} out of range: {value}")

        if width <= 0.0 or height <= 0.0:
            issues.append(f"line {line_number}: width/height must be positive")

        if x_center - width / 2 < 0.0 or x_center + width / 2 > 1.0:
            issues.append(f"line {line_number}: box exceeds image width")
        if y_center - height / 2 < 0.0 or y_center + height / 2 > 1.0:
            issues.append(f"line {line_number}: box exceeds image height")

    return issues


def main() -> None:
    total_images = 0
    total_labels = 0
    missing_labels: list[Path] = []
    invalid_labels: list[tuple[Path, list[str]]] = []
    class_counts = {"0": 0, "1": 0, "2": 0}

    for split in SPLITS:
        split_images = image_paths(split)
        total_images += len(split_images)

        for image_path in split_images:
            label_path = label_path_for(image_path, split)
            if not label_path.exists():
                missing_labels.append(image_path)
                continue

            total_labels += 1
            issues = validate_label_file(label_path)
            if issues:
                invalid_labels.append((label_path, issues))
                continue

            for line in label_path.read_text(encoding="utf-8").splitlines():
                class_id = line.strip().split()[0]
                class_counts[class_id] += 1

    print("Annotation validation")
    print("=====================")
    print(f"Images: {total_images}")
    print(f"Label files found: {total_labels}")
    print(f"Missing label files: {len(missing_labels)}")
    print(f"Invalid label files: {len(invalid_labels)}")
    print()
    print("Object counts:")
    print(f"  pyramid: {class_counts['0']}")
    print(f"  tutankhamun_mask: {class_counts['1']}")
    print(f"  nefertiti_head: {class_counts['2']}")

    if missing_labels:
        print("\nMissing labels, first 20:")
        for path in missing_labels[:20]:
            print(f"  {path.relative_to(BASE_DIR)}")

    if invalid_labels:
        print("\nInvalid labels, first 20:")
        for label_path, issues in invalid_labels[:20]:
            print(f"  {label_path.relative_to(BASE_DIR)}")
            for issue in issues[:5]:
                print(f"    - {issue}")

    if missing_labels or invalid_labels:
        raise SystemExit(1)

    print("\nOK: annotations look valid.")


if __name__ == "__main__":
    main()
