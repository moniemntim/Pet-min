#!/usr/bin/env python3
"""Create deterministic horizontal mirrors of transparent sprite frames."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageOps


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--manifest", type=Path)
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def main() -> int:
    args = parse_args()
    if not args.input_dir.is_dir():
        raise SystemExit(f"Input directory does not exist: {args.input_dir}")

    sources = sorted(args.input_dir.glob("*.png"))
    if not sources:
        raise SystemExit(f"No PNG frames found in {args.input_dir}")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    records: list[dict[str, object]] = []
    for source in sources:
        with Image.open(source) as opened:
            frame = opened.convert("RGBA")
        mirrored = ImageOps.mirror(frame)
        destination = args.output_dir / source.name
        mirrored.save(destination, optimize=True)
        records.append({
            "source": str(source.resolve()),
            "output": str(destination.resolve()),
            "size": list(frame.size),
            "source_sha256": sha256(source),
            "output_sha256": sha256(destination),
        })

    manifest = {
        "operation": "horizontal-mirror",
        "input_dir": str(args.input_dir.resolve()),
        "output_dir": str(args.output_dir.resolve()),
        "frame_count": len(records),
        "frames": records,
    }
    if args.manifest:
        args.manifest.parent.mkdir(parents=True, exist_ok=True)
        args.manifest.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
