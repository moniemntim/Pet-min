#!/usr/bin/env python3
"""Assemble a six-frame feeding cycle from registered real-photo poses."""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Final

import numpy as np
from PIL import Image


_CANVAS_SIZE: Final[tuple[int, int]] = (192, 208)
_SEQUENCE: Final[tuple[int, ...]] = (0, 1, 2, 1, 0, 1)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--input-dir",
        type=Path,
        required=True,
        help="Folder containing registered real-photo poses 00.png..02.png",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        required=True,
        help="Output folder for 00.png..05.png",
    )
    parser.add_argument("--self-check", action="store_true")
    return parser.parse_args()


def _load_frame(path: Path) -> Image.Image:
    if not path.is_file():
        raise SystemExit(f"Missing registered feeding pose: {path}")
    with Image.open(path) as opened:
        frame = opened.convert("RGBA")
    if frame.size != _CANVAS_SIZE:
        raise SystemExit(f"Unexpected frame size for {path}: {frame.size}")
    if frame.getchannel("A").getbbox() is None:
        raise SystemExit(f"Feeding pose is fully transparent: {path}")
    return frame


def _normalize_transparent_rgb(frame: Image.Image) -> Image.Image:
    arr = np.asarray(frame).copy()
    arr[arr[:, :, 3] == 0, :3] = 0
    return Image.fromarray(arr, mode="RGBA")


def main() -> int:
    args = parse_args()
    source_frames = [
        _load_frame(args.input_dir / f"{index:02d}.png") for index in range(3)
    ]
    args.output_dir.mkdir(parents=True, exist_ok=True)

    output_paths: list[Path] = []
    for output_index, source_index in enumerate(_SEQUENCE):
        frame = _normalize_transparent_rgb(source_frames[source_index])
        output_path = args.output_dir / f"{output_index:02d}.png"
        frame.save(output_path, optimize=True)
        output_paths.append(output_path)

    if args.self_check:
        boxes: list[tuple[int, int, int, int]] = []
        for output_path in output_paths:
            with Image.open(output_path) as opened:
                frame = opened.convert("RGBA")
            box = frame.getchannel("A").getbbox()
            if frame.size != _CANVAS_SIZE or box is None:
                raise SystemExit(f"Invalid composed feeding frame: {output_path}")
            arr = np.asarray(frame)
            if np.any(arr[arr[:, :, 3] == 0, :3] != 0):
                raise SystemExit(f"Transparent RGB residue found: {output_path}")
            boxes.append(box)

        for current, following in zip(boxes, boxes[1:]):
            if abs(current[1] - following[1]) > 5:
                raise SystemExit(
                    f"Feeding top edge jumps too far: {current[1]} -> {following[1]}"
                )
            if abs(current[3] - following[3]) > 1:
                raise SystemExit(
                    f"Feeding baseline jumps: {current[3]} -> {following[3]}"
                )

        print({"self_check": "ok", "sequence": _SEQUENCE, "bboxes": boxes})

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
