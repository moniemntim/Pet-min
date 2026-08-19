from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image


FRAME_WIDTH = 192
FRAME_HEIGHT = 208
ICON_SIZE = 256
ICON_MARGIN = 12
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def build_icon(spritesheet_path: Path, row: int, column: int) -> tuple[Image.Image, dict[str, object]]:
    sheet = Image.open(spritesheet_path).convert("RGBA")
    if sheet.width % FRAME_WIDTH or sheet.height % FRAME_HEIGHT:
        raise ValueError(f"Unexpected spritesheet dimensions: {sheet.size}")

    columns = sheet.width // FRAME_WIDTH
    rows = sheet.height // FRAME_HEIGHT
    if not 0 <= row < rows or not 0 <= column < columns:
        raise ValueError(f"Frame ({row}, {column}) is outside the {columns}x{rows} atlas")

    frame = sheet.crop(
        (
            column * FRAME_WIDTH,
            row * FRAME_HEIGHT,
            (column + 1) * FRAME_WIDTH,
            (row + 1) * FRAME_HEIGHT,
        )
    )
    alpha_bbox = frame.getchannel("A").getbbox()
    if alpha_bbox is None:
        raise ValueError(f"Frame ({row}, {column}) is fully transparent")

    pet = frame.crop(alpha_bbox)
    available = ICON_SIZE - 2 * ICON_MARGIN
    scale = min(available / pet.width, available / pet.height)
    target_size = (
        max(1, round(pet.width * scale)),
        max(1, round(pet.height * scale)),
    )
    pet = pet.convert("RGBa").resize(target_size, Image.Resampling.LANCZOS).convert("RGBA")

    icon = Image.new("RGBA", (ICON_SIZE, ICON_SIZE), (0, 0, 0, 0))
    position = ((ICON_SIZE - pet.width) // 2, (ICON_SIZE - pet.height) // 2)
    icon.alpha_composite(pet, position)

    metrics: dict[str, object] = {
        "source": str(spritesheet_path.resolve()),
        "source_frame": {"row": row, "column": column},
        "source_alpha_bbox": list(alpha_bbox),
        "source_crop_size": [alpha_bbox[2] - alpha_bbox[0], alpha_bbox[3] - alpha_bbox[1]],
        "icon_canvas": [ICON_SIZE, ICON_SIZE],
        "pet_render_size": list(target_size),
        "pet_position": list(position),
        "ico_sizes": [size[0] for size in ICO_SIZES],
    }
    return icon, metrics


def main() -> None:
    parser = argparse.ArgumentParser(description="Build the ShuBing Windows icon from an approved atlas frame.")
    parser.add_argument("--spritesheet", type=Path, required=True)
    parser.add_argument("--output-ico", type=Path, required=True)
    parser.add_argument("--preview-png", type=Path, required=True)
    parser.add_argument("--metrics-json", type=Path, required=True)
    parser.add_argument("--row", type=int, default=0)
    parser.add_argument("--column", type=int, default=0)
    args = parser.parse_args()

    icon, metrics = build_icon(args.spritesheet, args.row, args.column)
    for output in (args.output_ico, args.preview_png, args.metrics_json):
        output.parent.mkdir(parents=True, exist_ok=True)

    icon.save(args.preview_png, format="PNG", optimize=True)
    icon.save(args.output_ico, format="ICO", sizes=ICO_SIZES, bitmap_format="png")
    args.metrics_json.write_text(json.dumps(metrics, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metrics, ensure_ascii=False))


if __name__ == "__main__":
    main()
