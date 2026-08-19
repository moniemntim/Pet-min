#!/usr/bin/env python3
"""Compose realistic walking frames by deterministic cross-fading two RGBA sources.

The script intentionally avoids RGBA non-premultiplied interpolation to prevent
alpha fringes. It blends two same-size frames by alpha-premultiplying RGB first,
then re-derives straight alpha for output.
"""

from __future__ import annotations

from pathlib import Path
from typing import Final

import argparse
import numpy as np
from PIL import Image


_CANVAS_SIZE: Final[tuple[int, int]] = (192, 208)
_WEIGHTS: Final[list[float]] = [0.0, 0.25, 0.5, 0.75, 1.0, 0.75, 0.5, 0.25]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--frame-a", type=Path, required=True, help="First RGBA frame")
    parser.add_argument("--frame-b", type=Path, required=True, help="Second RGBA frame")
    parser.add_argument("--output-dir", type=Path, required=True, help="Output folder for 00..07.png")
    parser.add_argument("--self-check", action="store_true", help="Run smoke validation after composing")
    return parser.parse_args()


def _load_rgba(path: Path, *, expected_size: tuple[int, int]) -> Image.Image:
    if not path.exists():
        raise SystemExit(f"Input file not found: {path}")
    with Image.open(path) as opened:
        image = opened.convert("RGBA")
    if image.size != expected_size:
        raise SystemExit(f"Unexpected frame size for {path}: {image.size}, expected {expected_size}")
    return image


def _to_premultiplied(arr: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Return premultiplied RGB and alpha from a normalized float RGBA array."""
    rgb = arr[:, :, :3]
    alpha = arr[:, :, 3]
    return rgb * alpha[..., None], alpha


def _composite_crossfade(frame_a: Image.Image, frame_b: Image.Image, weight: float) -> Image.Image:
    if weight <= 0.0:
        return frame_a.copy()
    if weight >= 1.0:
        return frame_b.copy()

    arr_a = np.asarray(frame_a, dtype=np.float32) / 255.0
    arr_b = np.asarray(frame_b, dtype=np.float32) / 255.0

    rgb_a_pm, alpha_a = _to_premultiplied(arr_a)
    rgb_b_pm, alpha_b = _to_premultiplied(arr_b)

    out_alpha = alpha_a * (1.0 - weight) + alpha_b * weight
    if np.any(out_alpha < 0.0):
        out_alpha = np.clip(out_alpha, 0.0, 1.0)

    out_rgb_pm = rgb_a_pm * (1.0 - weight) + rgb_b_pm * weight
    out_rgb = np.zeros_like(rgb_a_pm)
    nonzero = out_alpha > 0.0
    out_rgb[nonzero] = out_rgb_pm[nonzero] / out_alpha[nonzero, None]

    out = np.empty_like(arr_a)
    out[:, :, :3] = np.clip(np.rint(out_rgb * 255.0), 0.0, 255.0)
    out[:, :, 3] = np.clip(np.rint(out_alpha * 255.0), 0.0, 255.0)

    return Image.fromarray(out.astype(np.uint8), mode="RGBA")


def _normalize_transparent_rgb(image: Image.Image) -> Image.Image:
    arr = np.asarray(image).copy()
    alpha = arr[:, :, 3] == 0
    arr[alpha, :3] = 0
    return Image.fromarray(arr.astype(np.uint8), mode="RGBA")


def _frame_bbox_top_bottom(arr_alpha: np.ndarray) -> tuple[int, int]:
    ys, xs = np.nonzero(arr_alpha > 0)
    if ys.size == 0:
        raise SystemExit("Frame alpha is empty")
    return int(ys.min()), int(ys.max())


def _equal_with_transparent_rgb_norm(lhs: Image.Image, rhs: Image.Image) -> bool:
    arr_l = np.asarray(lhs).copy()
    arr_r = np.asarray(rhs).copy()
    arr_l[arr_l[:, :, 3] == 0, :3] = 0
    arr_r[arr_r[:, :, 3] == 0, :3] = 0
    return np.array_equal(arr_l, arr_r)


def main() -> int:
    args = parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    frame_a = _load_rgba(args.frame_a, expected_size=_CANVAS_SIZE)
    frame_b = _load_rgba(args.frame_b, expected_size=_CANVAS_SIZE)

    output_paths: list[Path] = []
    for i, weight in enumerate(_WEIGHTS):
        frame = _composite_crossfade(frame_a, frame_b, weight)
        frame = _normalize_transparent_rgb(frame)
        output_path = args.output_dir / f"{i:02d}.png"
        frame.save(output_path, optimize=True)
        output_paths.append(output_path)

    if args.self_check:
        for i, output_path in enumerate(output_paths):
            if not output_path.is_file():
                raise SystemExit(f"Missing output frame after compose: {output_path}")
            with Image.open(output_path) as image:
                composed = image.convert("RGBA")
            arr = np.asarray(composed)
            if composed.size != _CANVAS_SIZE:
                raise SystemExit(f"Frame {i:02d} size mismatch: {composed.size}")
            if composed.getbbox() is None:
                raise SystemExit(f"Frame {i:02d} became fully transparent")
            alpha = arr[:, :, 3]
            if np.all(alpha == 0):
                raise SystemExit(f"Frame {i:02d} alpha is empty")

            if i in (0, 4):
                expected = frame_a if i == 0 else frame_b
                if not _equal_with_transparent_rgb_norm(composed, expected):
                    raise SystemExit(f"Frame {i:02d} does not match expected endpoint input")

        for i in range(len(output_paths) - 1):
            with Image.open(output_paths[i]) as curr:
                with Image.open(output_paths[i + 1]) as nxt:
                    curr_alpha = np.asarray(curr)[:, :, 3]
                    nxt_alpha = np.asarray(nxt)[:, :, 3]
                    curr_top, curr_bottom = _frame_bbox_top_bottom(curr_alpha)
                    nxt_top, nxt_bottom = _frame_bbox_top_bottom(nxt_alpha)
                    if abs(curr_top - nxt_top) > 3:
                        raise SystemExit(
                            f"Alpha bbox top drift too large between frame {i:02d} and {i+1:02d}: "
                            f"{curr_top} -> {nxt_top}"
                        )
                    if abs(curr_bottom - nxt_bottom) > 3:
                        raise SystemExit(
                            f"Alpha bbox bottom drift too large between frame {i:02d} and {i+1:02d}: "
                            f"{curr_bottom} -> {nxt_bottom}"
                        )

        print(f"self-check: ok ({len(output_paths)} frames)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
