#!/usr/bin/env python3
"""Compose real-photo feeding frames with deterministic, non-animated compositing."""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Final

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

_CANVAS_SIZE: Final[tuple[int, int]] = (192, 208)
_FEED_STEPS: Final[list[tuple[float, int, int]]] = [
    (0.0, 0, 0),
    (0.35, 1, 1),
    (1.0, 2, 2),
    (0.35, 1, 1),
    (1.0, 2, 2),
    (0.0, 0, 0),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-frame", type=Path, required=True, help="RGBA base hamster frame")
    parser.add_argument("--nibble-frame", type=Path, required=True, help="RGBA frame used for mouth action")
    parser.add_argument("--food-image", type=Path, required=True, help="RGBA real food image")
    parser.add_argument("--output-dir", type=Path, required=True, help="Output folder for 00..05.png")
    parser.add_argument("--canvas-width", type=int, default=_CANVAS_SIZE[0], help="Target canvas width")
    parser.add_argument("--canvas-height", type=int, default=_CANVAS_SIZE[1], help="Target canvas height")
    parser.add_argument("--food-max-width", type=int, default=20, help="Maximum food width in pixels")
    parser.add_argument("--food-max-height", type=int, default=30, help="Maximum food height in pixels")
    parser.add_argument("--food-pos-x", type=int, default=136, help="Food top-left x (canvas coordinates)")
    parser.add_argument("--food-pos-y", type=int, default=157, help="Food top-left y (canvas coordinates)")
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


def _translate_image(image: Image.Image, dx: int, dy: int, canvas_size: tuple[int, int]) -> Image.Image:
    width, height = canvas_size
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    out.alpha_composite(image, (dx, dy))
    return out


def _blend_mouth(
    base_frame: Image.Image,
    nibble_frame: Image.Image,
    mouth_mask: Image.Image,
    blend_amount: float,
) -> Image.Image:
    width, height = base_frame.size
    if nibble_frame.size != (width, height):
        raise SystemExit("Nibble frame size must match base frame.")
    if mouth_mask.size != (width, height):
        raise SystemExit("Mouth mask size must match frame size.")

    base_arr = np.asarray(base_frame).astype(np.float32) / 255.0
    nibble_arr = np.asarray(nibble_frame).astype(np.float32) / 255.0
    mask = np.asarray(mouth_mask).astype(np.float32) / 255.0
    blend = np.clip(mask * blend_amount, 0.0, 1.0)
    if np.all(blend == 0.0):
        return base_frame.copy()

    base_a = base_arr[:, :, 3]
    nibble_a = nibble_arr[:, :, 3]
    base_rgb = base_arr[:, :, :3]
    nibble_rgb = nibble_arr[:, :, :3]

    out_alpha = base_a * (1.0 - blend) + nibble_a * blend

    base_premul = base_rgb * base_a[..., None]
    nibble_premul = nibble_rgb * nibble_a[..., None]
    out_rgb_premul = base_premul * (1.0 - blend[..., None]) + nibble_premul * blend[..., None]

    out_rgb = np.zeros_like(base_rgb)
    nonzero = out_alpha > 0.0
    out_rgb[nonzero] = out_rgb_premul[nonzero] / out_alpha[nonzero, None]

    out = np.empty_like(base_arr)
    out[:, :, :3] = np.clip(np.rint(out_rgb * 255.0), 0.0, 255.0)
    out[:, :, 3] = np.clip(np.rint(out_alpha * 255.0), 0.0, 255.0)
    return Image.fromarray(out.astype(np.uint8), mode="RGBA")


def _composite_frame(
    base_frame: Image.Image,
    nibble_frame: Image.Image,
    food: Image.Image,
    food_position: tuple[int, int],
    mouth_mask: Image.Image,
    frame_idx: int,
) -> Image.Image:
    width, height = base_frame.size
    frame = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    frame.alpha_composite(food, food_position)

    blend_amount, dx, dy = _FEED_STEPS[frame_idx]
    if blend_amount <= 0.0:
        frame.alpha_composite(base_frame)
        return _normalize_transparent_rgb(frame)

    shifted_nibble = _translate_image(nibble_frame, dx, dy, (width, height))
    blended_head = _blend_mouth(base_frame, shifted_nibble, mouth_mask, blend_amount)
    frame.alpha_composite(blended_head)
    return _normalize_transparent_rgb(frame)


def _normalize_transparent_rgb(image: Image.Image) -> Image.Image:
    arr = np.asarray(image).copy()
    arr[arr[:, :, 3] == 0, :3] = 0
    return Image.fromarray(arr.astype(np.uint8), mode="RGBA")


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
    if canvas_size != _CANVAS_SIZE:
        raise SystemExit(f"Canvas must be {_CANVAS_SIZE}, got {canvas_size}")

    base_frame = _load_rgba(args.base_frame, expected_size=canvas_size)
    nibble_frame = _load_rgba(args.nibble_frame, expected_size=canvas_size)
    food = _load_food(args.food_image, max_size=(args.food_max_width, args.food_max_height))
    food_position = (args.food_pos_x, args.food_pos_y)
    mouth_box = (args.mouth_x1, args.mouth_y1, args.mouth_x2, args.mouth_y2)
    mouth_mask = _build_mouth_mask(canvas_size, mouth_box, args.feather_radius)

    output_paths: list[Path] = []
    for i in range(6):
        frame = _composite_frame(
            base_frame,
            nibble_frame,
            food,
            food_position,
            mouth_mask,
            i,
        )
        _validate_output(frame, canvas_size)
        output_path = output_dir / f"{i:02d}.png"
        frame.save(output_path, optimize=True)
        output_paths.append(output_path)

    if args.self_check:
        mouth_alpha = np.asarray(mouth_mask)
        non_mouth = mouth_alpha == 0
        fw, fh = food.size

        if food.size[0] > args.food_max_width or food.size[1] > args.food_max_height:
            raise SystemExit(
                f"Food was scaled beyond maximum bounds: {food.size} > ({args.food_max_width},{args.food_max_height})"
            )

        with Image.open(output_paths[0]) as first_image:
            reference_arr = np.asarray(first_image.convert("RGBA"))

        for i, output_path in enumerate(output_paths):
            if not output_path.is_file():
                raise SystemExit(f"Missing output frame after compose: {output_path}")

            with Image.open(output_path) as image:
                composed = image.convert("RGBA")

            if composed.size != canvas_size:
                raise SystemExit(
                    f"Frame {i:02d} size mismatch after compose: {composed.size}"
                )
            if composed.getbbox() is None:
                raise SystemExit(f"Frame {i:02d} became fully transparent after compose")

            out_arr = np.asarray(composed)
            if non_mouth.any() and not np.array_equal(
                out_arr[non_mouth], reference_arr[non_mouth]
            ):
                raise SystemExit(
                    f"Frame {i:02d} changed outside mouth area; only the head may move."
                )

            y0 = max(0, args.food_pos_y)
            y1 = min(canvas_size[1], args.food_pos_y + fh)
            x0 = max(0, args.food_pos_x)
            x1 = min(canvas_size[0], args.food_pos_x + fw)
            if x0 >= x1 or y0 >= y1:
                raise SystemExit(f"Food position {food_position} is out of bounds")

        food_alpha = np.asarray(food.getchannel("A"))
        base_alpha = np.asarray(base_frame.getchannel("A"))
        paste_h = y1 - y0
        paste_w = x1 - x0
        food_y0 = max(0, -args.food_pos_y)
        food_x0 = max(0, -args.food_pos_x)
        visible_food_pixels = int(
            np.count_nonzero(
                (food_alpha[food_y0:food_y0 + paste_h, food_x0:food_x0 + paste_w] >= 32)
                & (base_alpha[y0:y1, x0:x1] < 16)
            )
        )
        if visible_food_pixels < 20:
            raise SystemExit(
                f"Food is not visibly exposed beside the paws: {visible_food_pixels} pixels < 20"
            )

        print(
            {
                "self_check": "ok",
                "frames": len(output_paths),
                "food_size": food.size,
                "visible_food_pixels": visible_food_pixels,
            }
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
