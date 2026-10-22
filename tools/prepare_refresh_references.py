#!/usr/bin/env python3
"""Build reproducible photo/video reference boards for the 2026-10 refresh.

This tool only prepares inspection material. It does not segment, redraw, or
replace runtime assets.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

import cv2
from PIL import Image, ImageDraw, ImageOps


ROOT = Path(__file__).resolve().parents[1]
OLD_PHOTO_ROOT = ROOT / "圖"
NEW_PHOTO_ROOT = Path(r"C:\Users\787\Downloads")
NEW_PREFIX = "LINE_ALBUM_薯餅_261022_"
NEW_VIDEO = NEW_PHOTO_ROOT / "LINE_ALBUM_薯餅_261022_1.mp4"
OLD_VIDEO = ROOT / "影" / "LINE_ALBUM_薯餅_260820_1.mp4"
OUT = ROOT / "pet-art" / "refresh-20261023"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def image_info(path: Path) -> dict[str, object]:
    with Image.open(path) as image:
        exif = image.getexif()
        orientation = int(exif.get(274, 1) or 1)
        normalized = ImageOps.exif_transpose(image)
        width, height = normalized.size
    return {
        "path": str(path), "filename": path.name, "sha256": sha256(path),
        "width": width, "height": height, "orientation": orientation,
    }


def fit_thumb(path: Path, size: tuple[int, int]) -> Image.Image:
    with Image.open(path) as source:
        image = ImageOps.exif_transpose(source).convert("RGB")
    image.thumbnail(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", size, "#eeeeee")
    canvas.paste(image, ((size[0] - image.width) // 2, (size[1] - image.height) // 2))
    return canvas


def make_contact(paths: list[Path], output: Path, title: str, columns: int = 9,
                 cell: tuple[int, int] = (240, 205)) -> None:
    label_h = 28
    rows = (len(paths) + columns - 1) // columns
    board = Image.new("RGB", (columns * cell[0], rows * (cell[1] + label_h) + 42), "white")
    draw = ImageDraw.Draw(board)
    draw.text((8, 8), title, fill="#111111")
    for index, path in enumerate(paths):
        row, col = divmod(index, columns)
        x, y = col * cell[0], 42 + row * (cell[1] + label_h)
        board.paste(fit_thumb(path, cell), (x, y))
        draw.text((x + 5, y + cell[1] + 5), path.stem[:34], fill="#111111")
    output.parent.mkdir(parents=True, exist_ok=True)
    board.save(output, quality=92)


def make_contact_pages(paths: list[Path], output_dir: Path, title: str,
                       page_size: int = 9) -> None:
    """Write deterministic nine-image pages for detailed inspection."""
    output_dir.mkdir(parents=True, exist_ok=True)
    for page_index in range(0, len(paths), page_size):
        page_paths = paths[page_index:page_index + page_size]
        make_contact(
            page_paths,
            output_dir / f"page-{page_index // page_size + 1:02d}.jpg",
            f"{title} — page {page_index // page_size + 1:02d}",
            columns=3,
            cell=(420, 315),
        )


def load_old_catalog() -> tuple[list[dict[str, str]], dict[str, Path]]:
    catalog_path = ROOT / "pet-art" / "references" / "reference_catalog.csv"
    with catalog_path.open(encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    paths = {path.name: path for path in OLD_PHOTO_ROOT.glob("*.jpg")}
    return rows, paths


def choose_old(rows: list[dict[str, str]], paths: dict[str, Path]) -> list[Path]:
    # Stable identity set: neutral left/right profiles, front, rear/dorsal and
    # a feeding pose. Catalog notes are the source of truth for this selection.
    preferred = [
        "LINE_ALBUM_薯餅_260819_1.jpg",   # neutral left profile
        "LINE_ALBUM_薯餅_260819_2.jpg",   # front three-quarter
        "LINE_ALBUM_薯餅_260814_27.jpg",  # frontal face
        "LINE_ALBUM_薯餅_260819_15.jpg",  # right stretched profile
        "LINE_ALBUM_薯餅_260819_13.jpg",  # rear coat map
        "LINE_ALBUM_薯餅_260819_24.jpg",  # eating posture
    ]
    return [paths[name] for name in preferred if name in paths]


def video_board(video: Path, output: Path, title: str, rotate: bool) -> dict[str, object]:
    capture = cv2.VideoCapture(str(video))
    fps = float(capture.get(cv2.CAP_PROP_FPS) or 0)
    frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    width = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH) or 0)
    height = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT) or 0)
    duration = frames / fps if fps > 0 else 0.0
    times = [0.0, min(2.0, duration), min(5.0, duration), min(10.0, duration),
             min(20.0, duration), min(30.0, duration)]
    thumbs: list[Image.Image] = []
    for time_s in times:
        capture.set(cv2.CAP_PROP_POS_MSEC, time_s * 1000)
        ok, frame = capture.read()
        if not ok:
            continue
        if rotate:
            # The source portrait video is sideways in its container. A
            # clockwise quarter-turn makes the hamster upright; the previous
            # counter-clockwise turn displayed it upside down.
            frame = cv2.rotate(frame, cv2.ROTATE_90_CLOCKWISE)
        frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = Image.fromarray(frame)
        image.thumbnail((300, 250), Image.Resampling.LANCZOS)
        canvas = Image.new("RGB", (320, 285), "#eeeeee")
        canvas.paste(image, ((320 - image.width) // 2, 22 + (250 - image.height) // 2))
        ImageDraw.Draw(canvas).text((6, 4), f"{time_s:.2f}s", fill="#111111")
        thumbs.append(canvas)
    capture.release()
    board = Image.new("RGB", (len(thumbs) * 320, 285), "white")
    for index, thumb in enumerate(thumbs):
        board.paste(thumb, (index * 320, 0))
    output.parent.mkdir(parents=True, exist_ok=True)
    board.save(output, quality=92)
    return {"path": str(video), "fps": fps, "frames": frames, "width": width,
            "height": height, "duration_seconds": duration, "rotated": rotate}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=OUT)
    args = parser.parse_args()
    output = args.output.resolve()
    refs = output / "references"
    refs.mkdir(parents=True, exist_ok=True)

    old_rows, old_paths = load_old_catalog()
    old_paths_list = sorted(old_paths.values(), key=lambda p: p.name.casefold())
    new_paths = sorted(NEW_PHOTO_ROOT.glob(f"{NEW_PREFIX}*.jpg"), key=lambda p: p.name.casefold())
    all_paths = old_paths_list + new_paths
    records = [dict(source="old", **image_info(path)) for path in old_paths_list]
    records += [dict(source="new", **image_info(path)) for path in new_paths]
    (refs / "image-manifest.json").write_text(json.dumps({
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "count": len(records), "images": records,
    }, ensure_ascii=False, indent=2), encoding="utf-8")

    make_contact(old_paths_list, refs / "old-81-contact.jpg", "Original 81 photo references")
    make_contact_pages(old_paths_list, refs / "old-contact-pages", "Original 81 photo references")
    make_contact(new_paths, refs / "new-16-contact.jpg", "New 16 photo references", columns=8,
                 cell=(300, 225))
    make_contact_pages(new_paths, refs / "new-contact-pages", "New 16 photo references")
    selected = choose_old(old_rows, old_paths)
    selected_new_names = [f"{NEW_PREFIX}{i}.jpg" for i in (4, 7, 9, 11, 13, 15, 16)]
    selected_new = [path for path in new_paths if path.name in selected_new_names]
    make_contact(selected + selected_new, refs / "identity-reference-board.jpg",
                 "Identity references: original + new photos", columns=5, cell=(300, 230))

    videos = {}
    if OLD_VIDEO.exists():
        videos["old"] = video_board(OLD_VIDEO, refs / "old-video-samples.jpg", "Old video", False)
    if NEW_VIDEO.exists():
        videos["new-raw"] = video_board(NEW_VIDEO, refs / "new-video-samples-raw.jpg", "New video (raw orientation)", False)
        videos["new-rotated"] = video_board(NEW_VIDEO, refs / "new-video-samples-rotated.jpg", "New video (rotated upright)", True)
    (refs / "video-manifest.json").write_text(json.dumps(videos, ensure_ascii=False, indent=2), encoding="utf-8")

    before = output / "before" / "Assets"
    source_assets = ROOT / "src" / "ShuBingDesktopPet" / "Assets"
    if not before.exists():
        before.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(source_assets, before)
    (output / "README.md").write_text(
        "# 2026-10-23 reference refresh\n\n"
        f"Images indexed: {len(records)} (81 original + {len(new_paths)} new).\n"
        "Identity board: `references/identity-reference-board.jpg`.\n"
        "Video boards: `references/new-video-samples-raw.jpg` and "
        "`references/new-video-samples-rotated.jpg`.\n"
        "The `before/Assets` tree is a non-overwriting runtime asset backup.\n",
        encoding="utf-8")


if __name__ == "__main__":
    main()
