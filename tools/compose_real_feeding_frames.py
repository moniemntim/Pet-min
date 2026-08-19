#!/usr/bin/env python3
"""Compose real-photo feeding frames with deterministic, non-animated compositing."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-frame", type=Path, required=True, help="RGBA base hamster frame")
    parser.add_argument("--nibble-frame", type=Path, required=True, help="RGBA frame used for mouth action")
    parser.add_argument("--food-image", type=Path, required=True, help="RGBA real food image")
    parser.add_argument("--output-dir", type=Path, required=True, help="Output folder for 00..05.png")
    parser.add_argument("--canvas-width", type=int, default=192, help="Target canvas width")
    parser.add_argument("--canvas-height", type=int, default=208, help="Target canvas height")
    parser.add_argument("--food-max-width", type=int, default=17, help="Maximum food width in pixels")
    parser.add_argument("--food-max-height", type=int, default=25, help="Maximum food height in pixels")
    parser.add_argument("--food-pos-x", type=int, default=138, help="Food top-left x (canvas coordinates)")
    parser.add_argument("--food-pos-y", type=int, default=151, help="Food top-left y (canvas coordinates)")
    parser.add_argument("--feather-radius", type=float, default=6.0, help="Feather radius for nibble mouth mask")
    parser.add_argument("--mouth-x1", type=int, default=94, help="Mouth blend box x1 (inclusive)")
    parser.add_argument("--mouth-y1", type=int, default=76, help="Mouth blend box y1 (inclusive)")
    parser.add_argument("--mouth-x2", type=int, default=181, help="Mouth blend box x2 (inclusive)")
    parser.add_argument("--mouth-y2", type=int, default=175, help="Mouth blend box y2 (inclusive)")
    parser.add_argument("--self-check", action="store_true", help="Run deterministic smoke validation after composing")
    return parser.parse_args()


def _load_rgba(path: Path, *, expected_size: tuple[int, int]) -> Image.Image:
    if not path.exists():
        raise SystemExit(f"Input file not found: {path}")

    with Image.open(path) as opened:
        image = opened.convert("RGBA")

    if image.mode != "RGBA":
        raise SystemExit(f"Input is not RGBA: {path}")
    if image.size != expected_size:
        raise SystemExit(
            f"Unexpected frame size for {path}: {image.size}, expected {expected_size}"
        )
    return image


def _load_food(path: Path, max_size: tuple[int, int]) -> Image.Image:
    if not path.exists():
        raise SystemExit(f"Input file not found: {path}")

    with Image.open(path) as opened:
        food = opened.convert("RGBA")

    if food.mode != "RGBA":
        raise SystemExit(f"Input is not RGBA: {path}")
    max_w, max_h = max_size
    fw, fh = food.size
    scale = min(1.0, max_w / fw, max_h / fh)
    if scale <= 0:
        raise SystemExit(f"Invalid food image size: {food.size}")

    target_w = max(1, round(fw * scale))
    target_h = max(1, round(fh * scale))
    if target_w > fw or target_h > fh:
        raise SystemExit("Food image was scaled up, which is not allowed.")
    if (target_w, target_h) != food.size:
        food = food.resize((target_w, target_h), Image.Resampling.LANCZOS)
    return food


def _build_mouth_mask(size: tuple[int, int], mouth_box: tuple[int, int, int, int], feather_radius: float) -> Image.Image:
    width, height = size
    x1, y1, x2, y2 = mouth_box
    if x1 < 0 or y1 < 0 or x2 < x1 or y2 < y1 or x2 >= width or y2 >= height:
        raise SystemExit(f"Invalid mouth box {mouth_box} for canvas {size}")

    # Convert inclusive bounds to Pillow's half-open crop convention.
    ellipse = Image.new("L", size, 0)
    draw = ImageDraw.Draw(ellipse)
    draw.ellipse((x1, y1, x2 + 1, y2 + 1), fill=255)
    if feather_radius > 0:
        ellipse = ellipse.filter(ImageFilter.GaussianBlur(radius=feather_radius))
    return ellipse


def _composite_frame(
    base_frame: Image.Image,
    nibble_frame: Image.Image,
    food: Image.Image,
    food_position: tuple[int, int],
    mouth_mask: Image.Image | None,
) -> Image.Image:
    width, height = base_frame.size
    frame = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    frame.alpha_composite(food, food_position)

    if mouth_mask is None:
        frame.alpha_composite(base_frame)
        return frame

    nibble_alpha = nibble_frame.getchannel("A")
    nibble_mask = ImageChops.multiply(nibble_alpha, mouth_mask)
    blended_head = Image.composite(nibble_frame, base_frame, nibble_mask)
    frame.alpha_composite(blended_head)
    return frame


def _validate_output(frame: Image.Image, expected_size: tuple[int, int]) -> None:
    if frame.size != expected_size:
        raise SystemExit(
            f"Unexpected output size: {frame.size}, expected {expected_size}"
        )
    if frame.getbbox() is None:
        raise SystemExit("Output frame is fully transparent")
    alpha = frame.getchannel("A").getbbox()
    if alpha is None:
        raise SystemExit("Output frame has no alpha content")


def main() -> int:
    args = parse_args()
    output_dir = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)

    canvas_size = (args.canvas_width, args.canvas_height)
    base_frame = _load_rgba(args.base_frame, expected_size=canvas_size)
    nibble_frame = _load_rgba(args.nibble_frame, expected_size=canvas_size)
    food = _load_food(args.food_image, max_size=(args.food_max_width, args.food_max_height))
    food_position = (args.food_pos_x, args.food_pos_y)
    mouth_box = (args.mouth_x1, args.mouth_y1, args.mouth_x2, args.mouth_y2)
    mouth_mask = _build_mouth_mask(canvas_size, mouth_box, args.feather_radius)

    output_paths: list[Path] = []
    for i in range(6):
        should_nibble = i in (2, 4)
        frame = _composite_frame(
            base_frame,
            nibble_frame,
            food,
            food_position,
            mouth_mask if should_nibble else None,
        )
        _validate_output(frame, canvas_size)
        output_path = output_dir / f"{i:02d}.png"
        frame.save(output_path, optimize=True)
        output_paths.append(output_path)

    if args.self_check:
        for i, output_path in enumerate(output_paths):
            if not output_path.is_file():
                raise SystemExit(f"Missing output frame after compose: {output_path}")
            with Image.open(output_path) as image:
                composed = image.convert("RGBA")
            if composed.size != (args.canvas_width, args.canvas_height):
                raise SystemExit(
                    f"Frame {i:02d} size mismatch after compose: {composed.size}"
                )
            if composed.getbbox() is None:
                raise SystemExit(f"Frame {i:02d} became fully transparent after compose")

        if food.size[0] > args.food_max_width or food.size[1] > args.food_max_height:
            raise SystemExit(
                f"Food was scaled beyond maximum bounds: {food.size} > ({args.food_max_width},{args.food_max_height})"
            )

        print("self-check: ok")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
