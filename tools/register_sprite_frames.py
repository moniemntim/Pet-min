#!/usr/bin/env python3
"""Normalize transparent sprite frames to one size and ground baseline.

This utility only scales and repositions already-generated artwork.  It does not
draw or synthesize pixels, which keeps repaired animation rows deterministic.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--baseline", type=int, required=True,
                        help="Inclusive y coordinate for the bottom opaque pixel.")
    parser.add_argument("--max-width", type=int, default=192)
    parser.add_argument("--max-height", type=int, default=208)
    parser.add_argument("--canvas-width", type=int, default=192)
    parser.add_argument("--canvas-height", type=int, default=208)
    parser.add_argument(
        "--scale-y",
        type=float,
        default=1.0,
        help="Optional vertical-only multiplier applied after uniform fit; 1.0 means no change.",
    )
    parser.add_argument(
        "--target-height",
        type=int,
        default=None,
        help=(
            "Optional explicit sprite height after uniform fit, before vertical centering. "
            "Takes precedence over --scale-y. Must be > 0."
        ),
    )
    parser.add_argument("--manifest", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.input_dir.is_dir():
        raise SystemExit(f"Input directory does not exist: {args.input_dir}")
    if not (0 <= args.baseline < args.canvas_height):
        raise SystemExit("Baseline must be inside the output canvas.")

    sources = sorted(args.input_dir.glob("*.png"))
    if not sources:
        raise SystemExit(f"No PNG frames found in {args.input_dir}")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    records: list[dict[str, object]] = []

    for source in sources:
        with Image.open(source) as opened:
            frame = opened.convert("RGBA")

        alpha_bbox = frame.getchannel("A").getbbox()
        if alpha_bbox is None:
            raise SystemExit(f"Frame is fully transparent: {source}")

        sprite = frame.crop(alpha_bbox)
        source_width, source_height = sprite.size
        scale = min(
            1.0,
            args.max_width / source_width,
            args.max_height / source_height,
        )
        target_width = max(1, round(source_width * scale))
        uniform_target_height = max(1, round(source_height * scale))

        if args.target_height is not None:
            if args.target_height <= 0:
                raise SystemExit(f"target-height must be > 0: {args.target_height}")
            target_height = min(args.target_height, args.max_height)
            scale_y = 1.0 if uniform_target_height == 0 else target_height / uniform_target_height
        else:
            if args.scale_y <= 0:
                raise SystemExit(f"scale-y must be > 0: {args.scale_y}")
            target_height = round(uniform_target_height * args.scale_y)
            if target_height > args.max_height:
                target_height = args.max_height
            target_height = max(1, target_height)
            scale_y = args.scale_y

        if target_height > args.max_height:
            raise SystemExit(f"target-height must not exceed max-height: {source}")

        if (target_width, target_height) != sprite.size:
            sprite = sprite.resize((target_width, target_height), Image.Resampling.LANCZOS)

        left = (args.canvas_width - target_width) // 2
        top = args.baseline - target_height + 1
        if left < 0 or top < 0 or left + target_width > args.canvas_width:
            raise SystemExit(f"Registered sprite would leave canvas: {source}")

        output = Image.new("RGBA", (args.canvas_width, args.canvas_height), (0, 0, 0, 0))
        output.alpha_composite(sprite, (left, top))
        destination = args.output_dir / source.name
        output.save(destination, optimize=True)
        records.append({
            "source": str(source.resolve()),
            "output": str(destination.resolve()),
            "source_bbox": list(alpha_bbox),
            "scale": scale,
            "scale_y": scale_y,
            "target_height": target_height,
            "registered_bbox": [left, top, left + target_width, top + target_height],
            "baseline": args.baseline,
        })

    manifest = {
        "canvas": [args.canvas_width, args.canvas_height],
        "baseline": args.baseline,
        "max_sprite": [args.max_width, args.max_height],
        "frames": records,
    }
    if args.manifest:
        args.manifest.parent.mkdir(parents=True, exist_ok=True)
        args.manifest.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
