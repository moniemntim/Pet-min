#!/usr/bin/env python3
"""Replace selected rows in an 8-column RGBA sprite atlas."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image


FRAME_WIDTH = 192
FRAME_HEIGHT = 208
COLUMNS = 8


def parse_replacement(value: str) -> tuple[int, Path]:
    row_text, separator, directory_text = value.partition("=")
    if not separator:
        raise argparse.ArgumentTypeError("replacement must be ROW=FRAME_DIRECTORY")
    try:
        row = int(row_text)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("row must be an integer") from exc
    return row, Path(directory_text)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--atlas", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--replace", type=parse_replacement, action="append", required=True)
    parser.add_argument("--manifest", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    with Image.open(args.atlas) as opened:
        atlas = opened.convert("RGBA")

    expected_width = FRAME_WIDTH * COLUMNS
    if atlas.width != expected_width or atlas.height % FRAME_HEIGHT != 0:
        raise SystemExit(f"Unexpected atlas dimensions: {atlas.size}")

    row_count = atlas.height // FRAME_HEIGHT
    records: list[dict[str, object]] = []
    for row, frames_dir in args.replace:
        if not (0 <= row < row_count):
            raise SystemExit(f"Row {row} is outside the {row_count}-row atlas")
        frames = sorted(frames_dir.glob("*.png"))
        if not frames or len(frames) > COLUMNS:
            raise SystemExit(f"Row {row} needs 1-{COLUMNS} PNG frames: {frames_dir}")

        clear = Image.new("RGBA", (expected_width, FRAME_HEIGHT), (0, 0, 0, 0))
        atlas.paste(clear, (0, row * FRAME_HEIGHT))
        for column, frame_path in enumerate(frames):
            with Image.open(frame_path) as opened:
                frame = opened.convert("RGBA")
            if frame.size != (FRAME_WIDTH, FRAME_HEIGHT):
                raise SystemExit(f"Frame must be {FRAME_WIDTH}x{FRAME_HEIGHT}: {frame_path}")
            atlas.alpha_composite(frame, (column * FRAME_WIDTH, row * FRAME_HEIGHT))
        records.append({"row": row, "frames_dir": str(frames_dir.resolve()), "frame_count": len(frames)})

    args.output.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(args.output, optimize=True)
    manifest = {
        "source_atlas": str(args.atlas.resolve()),
        "output_atlas": str(args.output.resolve()),
        "frame_size": [FRAME_WIDTH, FRAME_HEIGHT],
        "columns": COLUMNS,
        "rows": row_count,
        "replacements": records,
    }
    if args.manifest:
        args.manifest.parent.mkdir(parents=True, exist_ok=True)
        args.manifest.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
