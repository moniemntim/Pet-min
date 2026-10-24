#!/usr/bin/env python3
"""Register generated hamster sheets without changing their authored anatomy.

The input is a JSON description of generated contact sheets.  Every sheet has
one shared scale; individual frames are only translated onto their destination
canvas.  This is deliberately stricter than a "fit each frame" pipeline so a
pose cannot become larger merely because its silhouette is shorter.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import tempfile
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage


ATLAS_SIZE = (1536, 2288)
CELL_SIZE = (192, 208)
LONG_SIZE = (256, 208)
DEFAULT_ROWS = (6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--self-check", action="store_true")
    parser.add_argument("--allow-partial", action="store_true", help="Permit an incomplete atlas/feeding/walking batch for staged QA.")
    return parser.parse_args()


def rgba_clean(image: Image.Image) -> Image.Image:
    array = np.asarray(image.convert("RGBA")).copy()
    low = array[:, :, 3] <= 3
    array[low, :3] = 0
    array[low, 3] = 0
    return Image.fromarray(array, "RGBA")


def alpha_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    alpha = np.asarray(image.getchannel("A"))
    ys, xs = np.where(alpha > 16)
    if len(xs) == 0:
        raise ValueError("frame is fully transparent")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def alpha_bbox_threshold(image: Image.Image, threshold: int) -> tuple[int, int, int, int]:
    alpha = np.asarray(image.getchannel("A") if "A" in image.getbands() else image)
    ys, xs = np.where(alpha > threshold)
    if len(xs) == 0:
        raise ValueError(f"no alpha pixels above threshold {threshold}")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def _max_run(values: np.ndarray) -> int:
    """Length of the longest contiguous true run in a one-dimensional mask."""
    best = current = 0
    for value in values.tolist():
        current = current + 1 if value else 0
        best = max(best, current)
    return best


def boundary_metadata(alpha: np.ndarray, include: np.ndarray, offset: tuple[int, int], source_size: tuple[int, int]) -> dict[str, Any]:
    """Describe contact with the *original* source-sheet boundary.

    ``include`` is the selected frame ownership mask in the local crop.  This
    keeps unused cells from rejecting a sheet while still making selected
    whisker/fur contacts visible in metrics.
    """
    x0, y0 = offset
    source_w, source_h = source_size
    h, w = alpha.shape
    yy, xx = np.indices((h, w))
    edge = (x0 + xx == 0) | (y0 + yy == 0) | (x0 + xx == source_w - 1) | (y0 + yy == source_h - 1)
    edge_alpha = edge & include & (alpha > 3)
    # Treat the source boundary as an outside crop edge: a sufficiently thick
    # body which reaches it retains an eroded core at that edge.
    core = ndimage.binary_erosion(alpha > 127, structure=np.ones((7, 7), dtype=bool), border_value=1)
    core_contact = bool(np.any(edge & include & core))
    edge16 = edge & include & (alpha > 16)
    runs = [_max_run(edge16[0, :]), _max_run(edge16[-1, :]), _max_run(edge16[:, 0]), _max_run(edge16[:, -1])]
    return {
        "boundary_alpha_pixels": int(np.count_nonzero(edge_alpha)),
        "source_fur_edge_contact": bool(np.any(edge_alpha)),
        "boundary_core_contact": core_contact,
        "boundary_thick_run": int(max(runs, default=0)),
    }


def load_sheet(path: Path, grid: tuple[int, int], extraction: str = "cells") -> list[Image.Image]:
    if not path.is_file():
        raise ValueError(f"missing source sheet: {path}")
    with Image.open(path) as opened:
        sheet = rgba_clean(opened)
    full_alpha = np.asarray(sheet.getchannel("A")) > 16
    if not full_alpha.any():
        raise ValueError(f"sheet is fully transparent: {path}")
    if extraction == "components":
        labels, count = ndimage.label(full_alpha, structure=np.ones((3, 3), dtype=np.uint8))
        objects = ndimage.find_objects(labels)
        candidates: list[tuple[int, tuple[slice, slice], int]] = []
        for label_id, bounds in enumerate(objects, start=1):
            if bounds is None:
                continue
            area = int(np.count_nonzero(labels[bounds] == label_id))
            if area >= 1000:
                candidates.append((area, bounds, label_id))
        cols, rows = grid
        if cols <= 0 or rows <= 0 or len(candidates) != cols * rows:
            raise ValueError(f"component extraction expected {cols * rows} main bodies in {path}, found {len(candidates)} (all components={count})")
        components: list[tuple[float, float, int, tuple[slice, slice]]] = []
        candidate_ids: list[int] = []
        for area, bounds, label_id in candidates:
            y_slice, x_slice = bounds
            cy = (y_slice.start + y_slice.stop) / 2
            cx = (x_slice.start + x_slice.stop) / 2
            components.append((cy, cx, label_id, bounds))
            candidate_ids.append(label_id)
        # Split sorted centroids into the configured number of rows, then sort
        # each row by x. This also handles 2x4 long-body sheets.
        components.sort(key=lambda value: value[0])
        component_rows = [components[row * cols:(row + 1) * cols] for row in range(rows)]
        for row_index, component_row in enumerate(component_rows[:-1]):
            if max(item[3][0].stop for item in component_row) >= min(item[3][0].start for item in component_rows[row_index + 1]):
                raise ValueError(f"component rows overlap in {path} at row {row_index}; refusing ambiguous identity assignment")
        ordered = [item for component_row in component_rows for item in sorted(component_row, key=lambda value: value[1])]
        main_mask = np.isin(labels, np.asarray(candidate_ids))
        alpha_array = np.asarray(sheet.getchannel("A"))
        nearest_distance, nearest_indices = ndimage.distance_transform_edt(~main_mask, return_indices=True)
        nearest_main_ids = labels[tuple(nearest_indices)]
        frames: list[Image.Image] = []
        for _, _, component_number, bounds in ordered:
            y_slice, x_slice = bounds
            # Assign each alpha pixel to the nearest main body. This prevents
            # distance-radius fur from leaking across rows while retaining
            # detached feet/tails within 28 source pixels of their owner.
            include = (alpha_array > 3) & (nearest_distance <= 28) & (nearest_main_ids == component_number)
            body_pixels = include & (alpha_array > 127)
            body_bbox_opaque = alpha_bbox_threshold(Image.fromarray(np.where(body_pixels, alpha_array, 0).astype(np.uint8), "L"), 127)
            ys, xs = np.where(include)
            if len(xs) == 0:
                raise ValueError(f"component {component_number} has no fur pixels in {path}")
            left = max(0, int(xs.min()) - 4); top = max(0, int(ys.min()) - 4)
            right = min(sheet.width, int(xs.max()) + 5); bottom = min(sheet.height, int(ys.max()) + 5)
            source_array = np.asarray(sheet).copy()
            output_array = np.zeros((bottom - top, right - left, 4), dtype=np.uint8)
            local_include = include[top:bottom, left:right]
            output_array[local_include] = source_array[top:bottom, left:right][local_include]
            frame = Image.fromarray(output_array, "RGBA")
            frame.info["body_bbox"] = [body_bbox_opaque[0] - left, body_bbox_opaque[1] - top, body_bbox_opaque[2] - left, body_bbox_opaque[3] - top]
            frame.info["fur_bbox"] = [int(xs.min()) - left, int(ys.min()) - top, int(xs.max() + 1) - left, int(ys.max() + 1) - top]
            frame.info.update(boundary_metadata(np.asarray(sheet.getchannel("A"))[top:bottom, left:right], local_include, (left, top), sheet.size))
            frames.append(frame)
        return frames
    if extraction != "cells":
        raise ValueError(f"unsupported extraction mode: {extraction}")
    cols, rows = grid
    if cols <= 0 or rows <= 0 or sheet.width % cols or sheet.height % rows:
        raise ValueError(f"sheet {path} size {sheet.size} is not divisible by grid {grid}")
    cell_w, cell_h = sheet.width // cols, sheet.height // rows
    frames: list[Image.Image] = []
    for index in range(cols * rows):
        x = (index % cols) * cell_w
        y = (index // cols) * cell_h
        frame = sheet.crop((x, y, x + cell_w, y + cell_h))
        local_alpha = np.asarray(frame.getchannel("A"))
        local_include = local_alpha > 3
        frame.info.update(boundary_metadata(local_alpha, local_include, (x, y), sheet.size))
        frames.append(frame)
    return frames


def target_spec(value: str) -> tuple[str, int | str, int | None]:
    """Return kind, path-or-row, column for atlas/feeding/walking targets."""
    parts = value.split(":")
    if len(parts) == 3 and parts[0] == "atlas":
        return "atlas", int(parts[1]), int(parts[2])
    if len(parts) == 3 and parts[0] == "feeding":
        return "feeding", f"{parts[1]}/{parts[2]}", None
    if len(parts) == 4 and parts[0] == "walking":
        return "walking", f"{parts[1]}/{parts[2]}/{parts[3]}", None
    raise ValueError(f"invalid target {value!r}; expected atlas:r:c, feeding:Food:00.png or walking:LongBody:Left:00.png")


def register(frame: Image.Image, scale: float, canvas: tuple[int, int], baseline: int, baseline_offset: int = 0) -> tuple[Image.Image, dict[str, Any]]:
    if not math.isfinite(scale) or scale <= 0:
        raise ValueError(f"scale must be positive and finite, got {scale}")
    cleaned = rgba_clean(frame)
    fur_bbox = tuple(frame.info.get("fur_bbox", alpha_bbox_threshold(cleaned, 3)))
    body_bbox = tuple(frame.info.get("body_bbox", alpha_bbox_threshold(cleaned, 127)))
    cropped = cleaned.crop(fur_bbox)
    width = max(1, round(cropped.width * scale))
    height = max(1, round(cropped.height * scale))
    scaled = resize_premultiplied(cropped, (width, height))
    left = (canvas[0] - width) // 2
    # Register from the actual post-resize output alpha. A second resized
    # body mask can differ by one anti-aliased pixel from the pixels displayed.
    scaled_body_bbox = alpha_bbox_threshold(scaled, 127)
    scaled_body_bottom = scaled_body_bbox[3]
    top = baseline + baseline_offset - scaled_body_bottom
    if left < 0 or top < 0 or left + width > canvas[0] or top + height > canvas[1]:
        raise ValueError(f"registered silhouette leaves canvas: source={fur_bbox}, scale={scale}, canvas={canvas}")
    output = Image.new("RGBA", canvas, (0, 0, 0, 0))
    output.alpha_composite(scaled, (left, top))
    output = rgba_clean(output)
    record = {
        "source_bbox": list(fur_bbox), "body_bbox": list(body_bbox), "scale": scale,
        "registered_bbox": [left, top, left + width, top + height],
        "scaled_body_bottom": scaled_body_bottom,
        "body_contact_baseline": top + scaled_body_bottom,
        "alpha_area": int(np.count_nonzero(np.asarray(output.getchannel("A")) > 16)),
        "baseline": baseline + baseline_offset,
        "canvas": list(canvas),
        "clipped": False,
    }
    return output, record


def resize_premultiplied(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Resize RGBA in premultiplied space to prevent dark/colored matte fringes."""
    source = np.asarray(image.convert("RGBA"), dtype=np.float32)
    alpha = source[:, :, 3:4] / 255.0
    premult = source[:, :, :3] * alpha
    rgb = Image.fromarray(np.clip(premult, 0, 255).astype(np.uint8), "RGB").resize(size, Image.Resampling.LANCZOS)
    alpha_image = Image.fromarray(np.clip(source[:, :, 3], 0, 255).astype(np.uint8), "L").resize(size, Image.Resampling.LANCZOS)
    rgb_array = np.asarray(rgb, dtype=np.float32)
    alpha_array = np.asarray(alpha_image, dtype=np.float32)
    restored = np.divide(rgb_array * 255.0, alpha_array[:, :, None], out=np.zeros_like(rgb_array), where=alpha_array[:, :, None] > 0)
    result = np.dstack((np.clip(restored, 0, 255), alpha_array)).astype(np.uint8)
    return Image.fromarray(result, "RGBA")


def save_contact(images: list[tuple[str, Image.Image]], path: Path, background: tuple[int, int, int], columns: int = 4) -> None:
    if not images:
        return
    thumb_w, thumb_h, label_h = 256, 208, 24
    rows = (len(images) + columns - 1) // columns
    board = Image.new("RGB", (columns * thumb_w, rows * (thumb_h + label_h)), background)
    draw = ImageDraw.Draw(board)
    for i, (label, image) in enumerate(images):
        x, y = (i % columns) * thumb_w, (i // columns) * (thumb_h + label_h)
        preview = qa_frame(image, background)
        board.paste(preview, (x, y)); draw.text((x + 4, y + thumb_h), label, fill=(0, 0, 0))
    path.parent.mkdir(parents=True, exist_ok=True)
    board.save(path, quality=92)


def qa_frame(image: Image.Image, background: tuple[int, int, int] = (45, 45, 45)) -> Image.Image:
    """Put an already-registered frame on a fixed 256x208 QA stage.

    This is horizontal padding only: authored vertical placement, including
    jump offsets and the source baseline, is preserved byte-for-byte.
    """
    source = image.convert("RGBA")
    bbox = source.getchannel("A").getbbox()
    if bbox is None:
        return Image.new("RGB", (256, 208), background)
    if source.width > 256 or source.height > 208:
        raise ValueError(f"QA frame exceeds fixed 256x208 stage: {source.size}")
    x = (256 - source.width) // 2
    y = 0
    stage = Image.new("RGBA", (256, 208), (*background, 255))
    stage.alpha_composite(source, (x, y))
    return stage.convert("RGB")


def save_gif(images: list[Image.Image], path: Path, duration: int = 140, background: tuple[int, int, int] = (45, 45, 45)) -> None:
    if not images:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    frames = [qa_frame(image, background).convert("P", palette=Image.Palette.ADAPTIVE, colors=256) for image in images]
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=duration, loop=0, disposal=2, optimize=False)


def run_self_checks() -> None:
    """Exercise registration invariants without touching the workspace."""
    first = Image.new("RGBA", (100, 100), (0, 0, 0, 0))
    second = Image.new("RGBA", (100, 100), (0, 0, 0, 0))
    ImageDraw.Draw(first).rectangle((30, 20, 69, 79), fill=(180, 120, 80, 255))
    ImageDraw.Draw(second).rectangle((20, 10, 79, 79), fill=(180, 120, 80, 255))
    one, metric_one = register(first, 0.5, CELL_SIZE, 170)
    two, metric_two = register(second, 0.5, CELL_SIZE, 170, 12)
    if metric_one["scale"] != metric_two["scale"] or metric_one["baseline"] != 170 or metric_two["baseline"] != 182:
        raise AssertionError("shared scale or baseline offset invariant failed")
    if one.getchannel("A").getbbox()[3] != 170 or two.getchannel("A").getbbox()[3] != 182:
        raise AssertionError("registered baseline invariant failed")
    edge = Image.new("RGBA", (20, 20), (255, 0, 0, 255))
    if alpha_bbox(edge) != (0, 0, 20, 20):
        raise AssertionError("original-edge detection invariant failed")
    # A one-pixel whisker at the source edge is reported but is not a body
    # clipping failure; a solid body core at that edge is rejected.
    edge_alpha = np.zeros((20, 20), dtype=np.uint8)
    edge_alpha[10, 0] = 255
    whisker = boundary_metadata(edge_alpha, edge_alpha > 3, (0, 0), (20, 20))
    if not whisker["source_fur_edge_contact"] or whisker["boundary_core_contact"]:
        raise AssertionError("isolated edge fur classification failed")
    core_alpha = np.zeros((20, 20), dtype=np.uint8)
    core_alpha[6:14, 0:8] = 255
    core = boundary_metadata(core_alpha, core_alpha > 3, (0, 0), (20, 20))
    if not core["boundary_core_contact"]:
        raise AssertionError("body edge clipping classification failed")
    # A transparent red matte must not bleed into a white opaque pixel after resize.
    matte = Image.new("RGBA", (8, 8), (255, 0, 0, 0))
    ImageDraw.Draw(matte).rectangle((2, 2, 5, 5), fill=(255, 255, 255, 255))
    resized = resize_premultiplied(matte, (32, 32))
    pixels = np.asarray(resized)
    opaque = pixels[:, :, 3] > 240
    if np.any(pixels[:, :, 0][opaque].astype(int) - pixels[:, :, 1][opaque].astype(int) > 2):
        raise AssertionError("transparent red RGB contaminated opaque white body")
    stage = qa_frame(one, (17, 19, 23))
    if stage.size != (256, 208) or stage.mode != "RGB":
        raise AssertionError("QA stage is not a fixed opaque 256x208 canvas")
    # Component ordering must follow a 2x4 sheet's configured rows, not the
    # historical hard-coded two-row split. A detached opaque foot must remain
    # owned by its nearest body for baseline registration.
    synthetic = Image.new("RGBA", (200, 320), (0, 0, 0, 0))
    draw = ImageDraw.Draw(synthetic)
    for row in range(4):
        for col in range(2):
            x, y = col * 100 + 20, row * 80 + 20
            color = (80 + row * 20, 120 + col * 20, 70, 255)
            draw.rectangle((x, y, x + 48, y + 30), fill=color)
            if row == 3 and col == 1:
                draw.rectangle((x + 36, y + 36, x + 42, y + 42), fill=color)
    with tempfile.TemporaryDirectory() as temporary_dir:
        temporary_path = Path(temporary_dir) / "components.png"
        synthetic.save(temporary_path)
        extracted = load_sheet(temporary_path, (2, 4), "components")
    if len(extracted) != 8 or extracted[-1].info["body_bbox"][3] <= 30:
        raise AssertionError("2x4 component ordering or detached-foot ownership failed")
    for index, frame in enumerate(extracted):
        opaque = np.asarray(frame)[:, :, 3] > 127
        pixels = np.asarray(frame)[:, :, :3][opaque]
        expected = np.array((80 + (index // 2) * 20, 120 + (index % 2) * 20, 70), dtype=np.uint8)
        if len(pixels) == 0 or not np.any(np.all(pixels == expected, axis=1)):
            raise AssertionError(f"2x4 component color/order failed at index {index}")
    detached, detached_metric = register(extracted[-1], 1.0, CELL_SIZE, 170)
    if alpha_bbox_threshold(detached, 127)[3] != 170 or detached_metric["body_contact_baseline"] != 170:
        raise AssertionError("detached foot did not register to opaque baseline")


def validate_coverage(records: list[dict[str, Any]], allow_partial: bool) -> None:
    if allow_partial:
        return
    if not records:
        raise ValueError("config contains no frame mappings; use --allow-partial for staged QA")
    targets = [str(record["target"]) for record in records]
    atlas_targets = {target for target in targets if target.startswith("atlas:")}
    if atlas_targets and len(atlas_targets) < sum(DEFAULT_ROWS):
        raise ValueError(f"incomplete atlas mappings: {len(atlas_targets)}/{sum(DEFAULT_ROWS)}")
    for prefix in ("feeding:", "walking:"):
        groups = sorted({target.rsplit(":", 1)[0] for target in targets if target.startswith(prefix)})
        for group in groups:
            count = len({target for target in targets if target.startswith(group + ":")})
            expected = 6 if prefix == "feeding:" else 8
            if count != expected:
                raise ValueError(f"incomplete {group}: {count}/{expected}")


def main() -> int:
    args = parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8"))
    if args.self_check:
        run_self_checks()
    root = args.output_root
    root.mkdir(parents=True, exist_ok=True)
    atlas = Image.new("RGBA", ATLAS_SIZE, (0, 0, 0, 0))
    records: list[dict[str, Any]] = []
    qa_images: dict[str, list[tuple[str, Image.Image]]] = {}
    source_cache: dict[str, list[Image.Image]] = {}

    for sheet in config.get("sheets", []):
        source = Path(sheet["file"])
        grid = tuple(sheet.get("grid", [4, 2]))
        key = str(source.resolve())
        source_cache[key] = load_sheet(source, grid, str(sheet.get("extraction", "cells")))
        scale = float(sheet["scale"])
        for item in sheet.get("frames", []):
            index = int(item["cell"])
            try:
                source_frame = source_cache[key][index]
            except (IndexError, KeyError) as exc:
                raise ValueError(f"cell {index} missing in {source}") from exc
            target = str(item["target"])
            if source_frame.info.get("boundary_core_contact") or source_frame.info.get("boundary_thick_run", 0) >= 8:
                raise ValueError(
                    f"selected frame {target} from {source} has substantial original-boundary contact: "
                    f"core={source_frame.info.get('boundary_core_contact')}, "
                    f"thick_run={source_frame.info.get('boundary_thick_run')}"
                )
            kind, path_or_row, column = target_spec(target)
            if kind == "atlas":
                row = int(path_or_row)
                col = int(column)
                if row < 0 or row >= len(DEFAULT_ROWS) or col < 0 or col >= 8:
                    raise ValueError(f"atlas target outside 11x8 grid: {target}")
                canvas, baseline = CELL_SIZE, int(item.get("baseline", 170))
            elif kind == "feeding":
                canvas, baseline = CELL_SIZE, int(item.get("baseline", 170))
            else:
                canvas, baseline = LONG_SIZE, int(item.get("baseline", 170))
            output, metric = register(source_frame, scale, canvas, baseline, int(item.get("baseline_offset", 0)))
            record = {
                "sheet": str(source), "extraction": str(sheet.get("extraction", "cells")), "cell": index,
                "target": target, "boundary_alpha_pixels": int(source_frame.info.get("boundary_alpha_pixels", 0)),
                "source_fur_edge_contact": bool(source_frame.info.get("source_fur_edge_contact", False)),
                "boundary_core_contact": bool(source_frame.info.get("boundary_core_contact", False)),
                "boundary_thick_run": int(source_frame.info.get("boundary_thick_run", 0)), **metric,
            }
            records.append(record)
            if kind == "atlas":
                atlas.alpha_composite(output, (int(column) * CELL_SIZE[0], int(path_or_row) * CELL_SIZE[1]))
                qa_images.setdefault(f"atlas-row-{path_or_row}", []).append((f"r{path_or_row}c{column}", output))
            else:
                if kind == "feeding":
                    relative_output = str(path_or_row)
                else:
                    # target_spec includes the logical LongBody namespace;
                    # the runtime asset root already is Assets/Walking/LongBody.
                    relative_output = str(path_or_row).split("/", 1)[1]
                destination = root / "Assets" / ("Feeding" if kind == "feeding" else "Walking/LongBody") / relative_output
                destination.parent.mkdir(parents=True, exist_ok=True)
                output.save(destination, optimize=True)
                output_hash = hashlib.sha256(destination.read_bytes()).hexdigest()
                if kind == "feeding":
                    food = str(path_or_row).split("/", 1)[0]
                    qa_key = f"feeding-{food}"
                else:
                    direction = str(path_or_row).split("/", 2)[1]
                    qa_key = f"walking-{direction}"
                qa_images.setdefault(qa_key, []).append((str(path_or_row), output))
            if kind != "atlas":
                record["output_path"] = str(destination)
                record["output_sha256"] = output_hash

    # Explicitly repeat the final authored frame into unused atlas columns.
    for row, count in enumerate(config.get("atlas_rows", DEFAULT_ROWS)):
        if count <= 0 or count > 8:
            raise ValueError(f"invalid atlas row frame count: row={row} count={count}")
        source_col = count - 1
        source = atlas.crop((source_col * 192, row * 208, (source_col + 1) * 192, (row + 1) * 208))
        for col in range(count, 8):
            atlas.alpha_composite(source, (col * 192, row * 208))
    atlas_path = root / "Assets" / "spritesheet.png"
    atlas_path.parent.mkdir(parents=True, exist_ok=True); atlas.save(atlas_path, optimize=True)
    (root / "Assets" / "pet.json").write_text(json.dumps({"id":"shu-bing","displayName":"薯餅","description":"統一尺度的寫實薯餅桌面寵物。","spriteVersionNumber":2,"spritesheetPath":"spritesheet.png"}, ensure_ascii=False, indent=2), encoding="utf-8")
    for name, images in qa_images.items():
        save_contact(images, root / "qa" / f"{name}-light-contact.jpg", (238, 238, 238))
        save_contact(images, root / "qa" / f"{name}-dark-contact.jpg", (45, 45, 45))
        save_gif([image for _, image in images], root / "qa" / f"{name}.gif")
    # One labelled 1:1 frame per action, useful for spotting scale or pattern
    # changes without relying on a thumbnail contact sheet.
    action_samples = [(name, images[0][1]) for name, images in sorted(qa_images.items()) if images]
    save_contact(action_samples, root / "qa" / "all-actions-light-contact.jpg", (238, 238, 238))
    save_contact(action_samples, root / "qa" / "all-actions-dark-contact.jpg", (45, 45, 45))

    def row_frames(row: int, count: int) -> list[Image.Image]:
        return [image for _, image in qa_images.get(f"atlas-row-{row}", [])[:count]]

    idle = row_frames(0, 6)
    normal_walk = row_frames(1, 8)
    broccoli = [image for _, image in qa_images.get("feeding-Broccoli", [])[:6]]
    jump_row = next((index for index, count in enumerate(config.get("atlas_rows", DEFAULT_ROWS)) if count == 5), None)
    jump = row_frames(int(jump_row), 5) if jump_row is not None else []
    transition = idle + normal_walk
    if broccoli:
        # Six authored broccoli frames, with a short middle nibble loop.
        transition += [broccoli[i] for i in (0, 1, 2, 3, 2, 3, 4, 5)]
    transition += jump + idle
    save_gif(transition, root / "qa" / "transition-256x208.gif")
    longwalk = [image for _, image in qa_images.get("walking-Left", [])[:8]]
    if not longwalk:
        longwalk = [image for _, image in qa_images.get("walking-Right", [])[:8]]
    long_transition = idle + longwalk
    if broccoli:
        long_transition += [broccoli[i] for i in (0, 1, 2, 3, 2, 3, 4, 5)]
    long_transition += jump + idle
    save_gif(long_transition, root / "qa" / "transition-longwalk-256x208.gif")
    save_gif(transition, root / "qa" / "transition-256x208-dark.gif", background=(45, 45, 45))
    save_gif(long_transition, root / "qa" / "transition-longwalk-256x208-dark.gif", background=(45, 45, 45))
    for record in records:
        expected_path = record.get("output_path")
        expected_hash = record.get("output_sha256")
        if expected_path and expected_hash:
            output_file = Path(str(expected_path))
            if not output_file.is_file():
                raise ValueError(f"configured selected output missing: {output_file}")
            actual_hash = hashlib.sha256(output_file.read_bytes()).hexdigest()
            if actual_hash != expected_hash:
                raise ValueError(f"configured selected output hash changed: {output_file}")
    (root / "qa" / "metrics.json").parent.mkdir(parents=True, exist_ok=True)
    (root / "qa" / "metrics.json").write_text(json.dumps({"atlas_size": list(ATLAS_SIZE), "cell_size": list(CELL_SIZE), "records": records}, ensure_ascii=False, indent=2), encoding="utf-8")
    validate_coverage(records, args.allow_partial)
    if args.self_check:
        if records and (atlas.size != ATLAS_SIZE or not any(atlas.getchannel("A").getdata())): raise ValueError("atlas is empty")
        scales = {record["scale"] for record in records}
        if len(scales) > 20: raise ValueError("unexpectedly many authored scale values")
        print(json.dumps({"self_check":"ok", "records":len(records), "atlas":str(atlas_path)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ValueError as exc:
        raise SystemExit(f"assemble_refresh_sprites: {exc}")
