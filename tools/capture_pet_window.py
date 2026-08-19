#!/usr/bin/env python3
"""Capture a moving desktop-pet window with its physical screen coordinates."""

from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import win32gui
import win32process
from PIL import Image, ImageDraw, ImageGrab


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--duration", type=float, default=12.0)
    parser.add_argument("--interval-ms", type=int, default=100)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--contact-sheet", type=Path, required=True)
    parser.add_argument("--max-contact-frames", type=int, default=48)
    return parser.parse_args()


def find_window(pid: int) -> int | None:
    candidates: list[int] = []

    def visit(hwnd: int, _: object) -> None:
        if not win32gui.IsWindowVisible(hwnd):
            return
        _, window_pid = win32process.GetWindowThreadProcessId(hwnd)
        if window_pid == pid:
            left, top, right, bottom = win32gui.GetWindowRect(hwnd)
            if right > left and bottom > top:
                candidates.append(hwnd)

    win32gui.EnumWindows(visit, None)
    return candidates[0] if candidates else None


def make_contact_sheet(records: list[dict[str, object]], output: Path, limit: int) -> None:
    if not records:
        raise SystemExit("No captured frames are available for the contact sheet.")

    step = max(1, len(records) // max(1, limit))
    selected = records[::step][:limit]
    opened = [Image.open(str(item["file"])).convert("RGB") for item in selected]
    thumb_width = max(image.width for image in opened)
    thumb_height = max(image.height for image in opened)
    label_height = 24
    columns = 8
    rows = (len(opened) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * thumb_width, rows * (thumb_height + label_height)), "#202020")
    draw = ImageDraw.Draw(sheet)
    for index, (record, frame) in enumerate(zip(selected, opened)):
        column = index % columns
        row = index // columns
        x = column * thumb_width
        y = row * (thumb_height + label_height)
        sheet.paste(frame, (x, y))
        dx = int(record["dx"])
        direction = "R" if dx > 0 else ("L" if dx < 0 else "-")
        draw.text((x + 3, y + thumb_height + 3), f"{record['index']:03d} x={record['left']} dx={dx:+d} {direction}", fill="white")
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


def main() -> int:
    args = parse_args()
    if args.duration <= 0 or args.interval_ms <= 0:
        raise SystemExit("duration and interval-ms must be positive.")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    if any(args.output_dir.iterdir()):
        raise SystemExit(f"Output directory must be empty: {args.output_dir}")

    deadline = time.perf_counter() + args.duration
    records: list[dict[str, object]] = []
    previous_left: int | None = None
    while time.perf_counter() < deadline:
        started = time.perf_counter()
        hwnd = find_window(args.pid)
        if hwnd is None:
            time.sleep(args.interval_ms / 1000.0)
            continue

        left, top, right, bottom = win32gui.GetWindowRect(hwnd)
        frame = ImageGrab.grab(bbox=(left, top, right, bottom), all_screens=True).convert("RGB")
        index = len(records)
        destination = args.output_dir / f"{index:03d}.png"
        frame.save(destination, optimize=True)
        dx = 0 if previous_left is None else left - previous_left
        previous_left = left
        records.append({
            "index": index,
            "elapsed_ms": round((time.perf_counter() - (deadline - args.duration)) * 1000),
            "left": left,
            "top": top,
            "right": right,
            "bottom": bottom,
            "dx": dx,
            "file": str(destination.resolve()),
        })

        remaining = (args.interval_ms / 1000.0) - (time.perf_counter() - started)
        if remaining > 0:
            time.sleep(remaining)

    if not records:
        raise SystemExit(f"No visible window found for PID {args.pid}.")

    manifest = {
        "pid": args.pid,
        "duration_seconds": args.duration,
        "interval_ms": args.interval_ms,
        "frame_count": len(records),
        "records": records,
    }
    manifest_path = args.output_dir.parent / f"{args.output_dir.name}-manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    make_contact_sheet(records, args.contact_sheet, args.max_contact_frames)
    print(json.dumps({
        "frame_count": len(records),
        "manifest": str(manifest_path.resolve()),
        "contact_sheet": str(args.contact_sheet.resolve()),
        "min_x": min(int(item["left"]) for item in records),
        "max_x": max(int(item["left"]) for item in records),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
