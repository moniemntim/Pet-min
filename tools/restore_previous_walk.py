"""Restore the backed-up pre-refresh walking frames without rescaling them.

The rollback deliberately touches only atlas rows 1 and 2 and LongBody.  A
one-pixel integer vertical registration is allowed to put opaque pixels on the
shared baseline; pixels, colours, alpha and x positions are otherwise copied
byte-for-byte.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


CELL_W, CELL_H = 192, 208
ATLAS_ROWS = 11


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def opaque_bottom(im: Image.Image) -> int | None:
    a = np.asarray(im.convert("RGBA"))[:, :, 3]
    ys = np.where(a > 127)[0]
    return None if not len(ys) else int(ys.max() + 1)


def translate_y(im: Image.Image, dy: int) -> Image.Image:
    if dy not in (-1, 0, 1):
        raise ValueError(f"rollback only permits ±1 y translation, got {dy}")
    arr = np.asarray(im.convert("RGBA"))
    out = np.zeros_like(arr)
    h = arr.shape[0]
    # Never silently discard a visible source pixel at the canvas edge.
    if dy > 0 and np.any(arr[h - dy:, :, 3] > 0):
        raise ValueError("positive rollback translation would clip source alpha")
    if dy < 0 and np.any(arr[: -dy, :, 3] > 0):
        raise ValueError("negative rollback translation would clip source alpha")
    src0, src1 = max(0, -dy), min(h, h - dy)
    dst0, dst1 = src0 + dy, src1 + dy
    out[dst0:dst1] = arr[src0:src1]
    return Image.fromarray(out, "RGBA")


def register(im: Image.Image) -> tuple[Image.Image, dict]:
    bottom = opaque_bottom(im)
    if bottom is None:
        raise ValueError("walk frame has no opaque pixels")
    dy = 170 - bottom
    if dy not in (-1, 0, 1):
        raise ValueError(f"backup walk bottom {bottom} needs forbidden delta {dy}")
    out = translate_y(im, dy)
    installed_bottom = opaque_bottom(out)
    if installed_bottom != 170:
        raise AssertionError(f"registration failed: {bottom} -> {installed_bottom}")
    return out, {"source_opaque_bottom": bottom, "delta_y": dy, "installed_opaque_bottom": installed_bottom}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--source-root", type=Path, default=Path("pet-art/walk-rollback-20261023/source"))
    ap.add_argument("--backup-root", type=Path, default=None, help="explicit bootstrap source from this backup; never used by default")
    ap.add_argument("--installed-root", type=Path, default=Path("src/ShuBingDesktopPet/Assets"))
    args = ap.parse_args()
    source, backup, dest = args.source_root, args.backup_root, args.installed_root
    source.mkdir(parents=True, exist_ok=True)
    strip = source / "previous-walk-rows.png"
    long_source = source / "LongBody"
    if args.backup_root is not None:
        backup = args.backup_root
        if strip.exists() or any(long_source.glob("*/*.png")):
            raise FileExistsError("bootstrap refused: independent source files already exist")
        backup_atlas = Image.open(backup / "spritesheet.png").convert("RGBA")
        if backup_atlas.size != (1536, 2288):
            raise ValueError(f"backup atlas must be 1536x2288, got {backup_atlas.size}")
        rows = Image.new("RGBA", (1536, CELL_H * 2), (0, 0, 0, 0))
        rows.paste(backup_atlas.crop((0, CELL_H, 1536, CELL_H * 3)), (0, 0))
        rows.save(strip, optimize=False)
        for direction in ("Left", "Right"):
            outdir = long_source / direction
            outdir.mkdir(parents=True, exist_ok=True)
            for i in range(8):
                src = backup / "Walking" / "LongBody" / direction / f"{i:02d}.png"
                Image.open(src).convert("RGBA").save(outdir / src.name, optimize=False)
    if not strip.exists():
        raise FileNotFoundError(f"independent source missing: {strip}; pass --backup-root only for explicit bootstrap")
    rows = Image.open(strip).convert("RGBA")
    if rows.size != (1536, 416):
        raise ValueError(f"source strip must be 1536x416, got {rows.size}")
    for direction in ("Left", "Right"):
        for i in range(8):
            p = long_source / direction / f"{i:02d}.png"
            if not p.exists():
                raise FileNotFoundError(p)
            if Image.open(p).size != (256, 208):
                raise ValueError(f"LongBody source must be 256x208: {p}")
    current_atlas = Image.open(dest / "spritesheet.png")
    if current_atlas.size != (1536, 2288) or current_atlas.mode != "RGBA":
        raise ValueError(f"current atlas must be RGBA 1536x2288, got {current_atlas.mode} {current_atlas.size}")

    before_atlas = current_atlas.convert("RGBA")
    before_atlas_bytes = np.asarray(before_atlas).copy()
    food_before = {str(p.relative_to(dest)): sha(p) for p in (dest / "Feeding").rglob("*.png")}
    pet_before = sha(dest / "pet.json")
    installed_atlas = before_atlas.copy()
    metrics = {"source": str(source), "baseline": 170, "frames": [], "nonwalk_unchanged": False, "feeding_unchanged": False, "pet_json_unchanged": False}
    for row in (1, 2):
        for col in range(8):
            frame = rows.crop((col * CELL_W, (row - 1) * CELL_H, (col + 1) * CELL_W, row * CELL_H))
            registered, metric = register(frame)
            installed_atlas.paste(registered, (col * CELL_W, row * CELL_H))
            metric.update({"kind": "atlas", "row": row, "col": col, "source_frame_sha256": hashlib.sha256(frame.tobytes()).hexdigest(), "installed_frame_sha256": hashlib.sha256(registered.tobytes()).hexdigest(), "clipped": False})
            metrics["frames"].append(metric)
    # LongBody originals remain in source; only their permitted baseline shift
    # is applied to installed runtime files.
    for direction in ("Left", "Right"):
        for i in range(8):
            src = long_source / direction / f"{i:02d}.png"
            registered, metric = register(Image.open(src).convert("RGBA"))
            out = dest / "Walking" / "LongBody" / direction / src.name
            registered.save(out, optimize=False)
            metric.update({"kind": "long", "direction": direction, "index": i, "source_frame_sha256": hashlib.sha256(np.asarray(Image.open(src).convert("RGBA")).tobytes()).hexdigest(), "installed_frame_sha256": hashlib.sha256(np.asarray(registered).tobytes()).hexdigest(), "source_sha256": sha(src), "installed_sha256": sha(out), "clipped": False})
            metrics["frames"].append(metric)
    # Verify all other atlas rows are byte-identical to the operation input.
    after = np.asarray(installed_atlas)
    nonwalk = np.concatenate([before_atlas_bytes[:CELL_H], before_atlas_bytes[3 * CELL_H:]])
    nonwalk_after = np.concatenate([after[:CELL_H], after[3 * CELL_H:]])
    metrics["nonwalk_unchanged"] = bool(np.array_equal(nonwalk, nonwalk_after))
    if not metrics["nonwalk_unchanged"]:
        raise AssertionError("non-walking atlas rows changed")
    installed_atlas.save(dest / "spritesheet.png", optimize=False)
    metrics["feeding_unchanged"] = food_before == {str(p.relative_to(dest)): sha(p) for p in (dest / "Feeding").rglob("*.png")}
    metrics["pet_json_unchanged"] = pet_before == sha(dest / "pet.json")
    if not metrics["feeding_unchanged"] or not metrics["pet_json_unchanged"]:
        raise AssertionError("non-walk asset changed")
    metrics["atlas_sha256"] = sha(dest / "spritesheet.png")
    (source.parent / "metrics.json").write_text(json.dumps(metrics, indent=2), encoding="utf-8")
    print(json.dumps({"frames": len(metrics["frames"]), "nonwalk_unchanged": metrics["nonwalk_unchanged"], "feeding_unchanged": metrics["feeding_unchanged"], "pet_json_unchanged": metrics["pet_json_unchanged"], "metrics": str(source.parent / "metrics.json")}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
