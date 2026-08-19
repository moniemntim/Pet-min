from __future__ import annotations

import argparse
import csv
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps


SUPPORTED = {".jpg", ".jpeg", ".png", ".webp"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build deterministic, labeled photo reference boards."
    )
    parser.add_argument("input_dir", type=Path)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument("--columns", type=int, default=3)
    parser.add_argument("--rows", type=int, default=3)
    parser.add_argument("--cell-width", type=int, default=600)
    parser.add_argument("--cell-height", type=int, default=480)
    return parser.parse_args()


def fit_image(source: Image.Image, width: int, height: int) -> Image.Image:
    normalized = ImageOps.exif_transpose(source).convert("RGB")
    return ImageOps.contain(normalized, (width, height), Image.Resampling.LANCZOS)


def main() -> None:
    args = parse_args()
    files = sorted(
        path
        for path in args.input_dir.iterdir()
        if path.is_file() and path.suffix.lower() in SUPPORTED
    )
    if not files:
        raise SystemExit(f"No supported images found in {args.input_dir}")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    font = ImageFont.load_default(size=20)
    label_height = 42
    page_size = args.columns * args.rows
    catalog_rows: list[dict[str, object]] = []

    for page_index in range(0, len(files), page_size):
        board = Image.new(
            "RGB",
            (
                args.columns * args.cell_width,
                args.rows * (args.cell_height + label_height),
            ),
            "white",
        )
        draw = ImageDraw.Draw(board)
        page_files = files[page_index : page_index + page_size]
        for local_index, path in enumerate(page_files):
            row, column = divmod(local_index, args.columns)
            x = column * args.cell_width
            y = row * (args.cell_height + label_height)
            with Image.open(path) as source:
                original_width, original_height = source.size
                fitted = fit_image(source, args.cell_width, args.cell_height)
            paste_x = x + (args.cell_width - fitted.width) // 2
            paste_y = y + (args.cell_height - fitted.height) // 2
            board.paste(fitted, (paste_x, paste_y))
            global_index = page_index + local_index + 1
            label = f"{global_index:02d}  {path.name}"
            draw.rectangle(
                (x, y + args.cell_height, x + args.cell_width, y + args.cell_height + label_height),
                fill="#f2f2f2",
            )
            draw.text((x + 10, y + args.cell_height + 9), label, fill="black", font=font)
            catalog_rows.append(
                {
                    "index": global_index,
                    "filename": path.name,
                    "width": original_width,
                    "height": original_height,
                    "board": page_index // page_size + 1,
                    "board_cell": local_index + 1,
                    "view": "",
                    "pose": "",
                    "visibility": "",
                    "sharpness": "",
                    "notes": "",
                }
            )

        board_path = args.output_dir / f"reference_board_{page_index // page_size + 1:02d}.jpg"
        board.save(board_path, quality=92, optimize=True)

    catalog_path = args.output_dir / "reference_catalog.csv"
    with catalog_path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(catalog_rows[0]))
        writer.writeheader()
        writer.writerows(catalog_rows)

    print(f"Wrote {len(catalog_rows)} catalog rows and {(len(files) + page_size - 1) // page_size} boards")


if __name__ == "__main__":
    main()
