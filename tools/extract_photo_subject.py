#!/usr/bin/env python3
"""Extract a subject alpha mask from a source photo using a checkerboard reference.

Workflow:
1. Detect checkerboard-like background in the reference image and derive a foreground mask.
2. Register the reference foreground mask onto the source image with ORB + affine/homography.
3. Seed GrabCut with that warped mask (and optional foreground rectangle), then output RGBA.
4. Emit diagnostic artefacts for debugging registration and mask quality.

The script preserves source RGB pixels and only changes alpha.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Sequence

import cv2
import numpy as np
from PIL import Image


@dataclass
class RegistrationResult:
    kind: str
    matrix: Optional[np.ndarray]
    source_to_reference: bool
    inliers: int
    match_count: int
    good_match_count: int
    source_points: list[list[float]]
    reference_points: list[list[float]]
    used_fallback_points: bool


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract subject from source using checkerboard reference."
    )
    parser.add_argument("--source", type=Path, required=False, help="Raw source JPG.")
    parser.add_argument(
        "--reference",
        type=Path,
        required=False,
        help="Reference image with checkerboard-like background.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        required=False,
        help="Output RGBA PNG.",
    )
    parser.add_argument(
        "--diagnostics-dir",
        type=Path,
        default=None,
        help="Directory for reference-mask.png / warped-mask.png / final-mask.png / overlay.jpg / registration.json",
    )
    parser.add_argument(
        "--foreground-rect",
        type=_parse_int4,
        default=None,
        help="Optional x,y,w,h in source pixel coordinates.",
    )
    parser.add_argument(
        "--grabcut-iterations",
        type=int,
        default=5,
        help="GrabCut iterations (default: 5).",
    )
    parser.add_argument(
        "--keep-components",
        type=int,
        default=1,
        help="Keep largest connected component count from detected subject mask. "
        "Increase for feeding if food is disconnected from body.",
    )
    parser.add_argument(
        "--reference-points",
        type=_parse_point_pairs,
        default=None,
        help="Fallback manual points in reference coordinates: x,y;x,y;x,y.",
    )
    parser.add_argument(
        "--source-points",
        type=_parse_point_pairs,
        default=None,
        help="Fallback manual points in source coordinates: x,y;x,y;x,y.",
    )
    parser.add_argument(
        "--max-side",
        type=int,
        default=2000,
        help="Maximum side length for registration/GrabCut working images.",
    )
    parser.add_argument(
        "--self-check",
        action="store_true",
        help="Run an internal synthetic self-check and exit.",
    )
    args = parser.parse_args()
    if not args.self_check and (args.source is None or args.reference is None or args.output is None):
        raise SystemExit(
            "需要同時指定 --source、--reference 與 --output；若只要做自檢請加 --self-check。"
        )
    return args


def _parse_int4(text: str) -> tuple[int, int, int, int]:
    parts = [p.strip() for p in text.split(",") if p.strip()]
    if len(parts) != 4:
        raise argparse.ArgumentTypeError(
            "foreground-rect must be x,y,w,h with comma-separated integers."
        )
    try:
        values = tuple(int(p) for p in parts)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("foreground-rect requires integers.") from exc
    return values  # type: ignore[return-value]


def _parse_point_pairs(text: str) -> list[tuple[float, float]]:
    pts: list[tuple[float, float]] = []
    for pair in text.split(";"):
        pair = pair.strip()
        if not pair:
            continue
        try:
            x_str, y_str = [item.strip() for item in pair.split(",")]
            pts.append((float(x_str), float(y_str)))
        except ValueError as exc:
            raise argparse.ArgumentTypeError(
                "point format must be x,y;x,y;x,y"
            ) from exc
    if len(pts) < 3:
        raise argparse.ArgumentTypeError("at least 3 points are required.")
    return pts


def _read_rgb_bgr(path: Path) -> np.ndarray:
    try:
        with Image.open(path) as opened:
            # The project photos contain an inconsistent EXIF orientation tag;
            # their stored pixel layout is the intended landscape composition.
            rgb = np.array(opened.convert("RGB"))
    except (OSError, ValueError) as exc:
        raise RuntimeError(f"無法讀取影像: {path}") from exc
    return cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)


def _resize_to_limit(image: np.ndarray, max_side: int) -> tuple[np.ndarray, float]:
    h, w = image.shape[:2]
    if max_side <= 0:
        return image, 1.0
    scale = min(1.0, float(max_side) / float(max(h, w)))
    if scale >= 1.0:
        return image, 1.0
    new_w, new_h = int(round(w * scale)), int(round(h * scale))
    resized = cv2.resize(image, (new_w, new_h), interpolation=cv2.INTER_AREA)
    return resized, scale


def _largest_components(mask: np.ndarray, keep: int) -> np.ndarray:
    num, labels, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    if num <= 1:
        return mask
    areas = []
    for i in range(1, num):
        areas.append((stats[i, cv2.CC_STAT_AREA], i))
    if not areas:
        return mask
    areas.sort(reverse=True)
    keep_ids = {idx for _, idx in areas[: max(1, keep)]}
    output = np.zeros_like(mask)
    for comp_id in keep_ids:
        output[labels == comp_id] = 255
    return output


def _detect_checkerboard_subject(reference_bgr: np.ndarray, keep_components: int) -> np.ndarray:
    channels = reference_bgr.astype(np.int16)
    channel_spread = channels.max(axis=2) - channels.min(axis=2)
    brightness = channels.mean(axis=2)
    # ImageGen's baked checkerboard uses almost exactly neutral gray tiles.
    # A tight channel-spread test preserves the hamster's naturally blue/tan
    # white fur, which a broad HSV saturation threshold would erase.
    neutral_gray = channel_spread <= 6
    bright = brightness > 210
    bg = (neutral_gray & bright).astype(np.uint8) * 255
    bg = cv2.medianBlur(bg, 5)
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
    bg = cv2.morphologyEx(bg, cv2.MORPH_OPEN, kernel)
    bg = cv2.morphologyEx(bg, cv2.MORPH_CLOSE, kernel)

    h, w = bg.shape
    num, labels, stats, _ = cv2.connectedComponentsWithStats(bg, 8)
    border_area = np.zeros_like(bg)
    total = h * w
    for comp_id in range(1, num):
        x = stats[comp_id, cv2.CC_STAT_LEFT]
        y = stats[comp_id, cv2.CC_STAT_TOP]
        ww = stats[comp_id, cv2.CC_STAT_WIDTH]
        hh = stats[comp_id, cv2.CC_STAT_HEIGHT]
        a = stats[comp_id, cv2.CC_STAT_AREA]
        touches = x == 0 or y == 0 or x + ww >= w or y + hh >= h
        if touches and a > 0.002 * total:
            border_area[labels == comp_id] = 255

    if border_area.sum() < 0.02 * total:
        # Fallback if border components are weak: keep all low-sat/bright regions touching border.
        border_area = bg.copy()

    border_area = cv2.morphologyEx(border_area, cv2.MORPH_DILATE, kernel, iterations=1)
    subject = cv2.bitwise_not(border_area)

    subject = cv2.morphologyEx(subject, cv2.MORPH_OPEN, kernel, iterations=1)
    subject = cv2.morphologyEx(subject, cv2.MORPH_CLOSE, kernel, iterations=2)
    subject = _largest_components(subject, keep_components)
    if cv2.countNonZero(subject) == 0:
        raise RuntimeError("Checkerboard 前景偵測失敗：未偵測到明確主體遮罩。")
    return subject


def _rescale_points(points: Sequence[tuple[float, float]], scale: float) -> np.ndarray:
    return np.array([(x * scale, y * scale) for x, y in points], dtype=np.float32)


def _estimate_transform(
    source_bgr: np.ndarray,
    reference_bgr: np.ndarray,
    reference_mask: np.ndarray,
    ref_pts: Optional[np.ndarray],
    src_pts: Optional[np.ndarray],
) -> RegistrationResult:
    used_fallback_points = False
    source_gray = cv2.cvtColor(source_bgr, cv2.COLOR_BGR2GRAY)
    reference_gray = cv2.cvtColor(reference_bgr, cv2.COLOR_BGR2GRAY)

    matcher = cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=False)
    orb = cv2.ORB_create(nfeatures=6000)
    skp, sdes = orb.detectAndCompute(source_gray, None)
    rkp, rdes = orb.detectAndCompute(reference_gray, reference_mask)

    if sdes is None or rdes is None or len(skp) < 8 or len(rkp) < 8:
        if ref_pts is None or src_pts is None:
            raise RuntimeError("ORB 特徵不足，且未提供 --reference-points / --source-points fallback。")
        used_fallback_points = True
        return _build_transform_from_points(src_pts, ref_pts)

    matches = matcher.knnMatch(sdes, rdes, k=2)
    good: list[cv2.DMatch] = []
    for pair in matches:
        if len(pair) < 2:
            continue
        m, n = pair
        if m.distance < 0.75 * n.distance:
            good.append(m)

    if len(good) < 8:
        if ref_pts is None or src_pts is None:
            raise RuntimeError(
                "ORB 匹配不足。請提供 --reference-points 與 --source-points 進行手動 3 點對齊。"
            )
        used_fallback_points = True
        return _build_transform_from_points(src_pts, ref_pts)

    src_points = np.float32([skp[g.queryIdx].pt for g in good]).reshape(-1, 1, 2)
    ref_points = np.float32([rkp[g.trainIdx].pt for g in good]).reshape(-1, 1, 2)

    affine_matrix, inlier_mask = cv2.estimateAffinePartial2D(
        src_points,
        ref_points,
        method=cv2.RANSAC,
        ransacReprojThreshold=3.0,
        maxIters=3000,
        confidence=0.995,
    )
    kind = "affine_partial2d"
    if affine_matrix is None:
        homography, inlier_mask = cv2.findHomography(
            src_points,
            ref_points,
            cv2.RANSAC,
            3.0,
        )
        kind = "homography"
        if homography is None:
            if ref_pts is None or src_pts is None:
                raise RuntimeError("自動配準失敗。請補提供 --reference-points / --source-points。")
            used_fallback_points = True
            return _build_transform_from_points(src_pts, ref_pts)
        return RegistrationResult(
            kind=kind,
            matrix=homography,
            source_to_reference=True,
            inliers=int(inlier_mask.sum()) if inlier_mask is not None else 0,
            match_count=len(matches),
            good_match_count=len(good),
            source_points=[(float(x), float(y)) for x, y in _rescale_points([kp.pt for kp in skp[:3]], 1.0)],
            reference_points=[(float(x), float(y)) for x, y in _rescale_points([kp.pt for kp in rkp[:3]], 1.0)],
            used_fallback_points=used_fallback_points,
        )
    inliers = int(inlier_mask.sum()) if inlier_mask is not None else 0
    return RegistrationResult(
        kind=kind,
        matrix=affine_matrix,
        source_to_reference=True,
        inliers=inliers,
        match_count=len(matches),
        good_match_count=len(good),
        source_points=[(float(x), float(y)) for x, y in _rescale_points([kp.pt for kp in skp[:3]], 1.0)],
        reference_points=[(float(x), float(y)) for x, y in _rescale_points([kp.pt for kp in rkp[:3]], 1.0)],
        used_fallback_points=used_fallback_points,
    )


def _build_transform_from_points(
    source_points: Sequence[tuple[float, float]],
    reference_points: Sequence[tuple[float, float]],
) -> RegistrationResult:
    if len(source_points) < 3 or len(reference_points) < 3:
        raise RuntimeError("fallback 3 點配準至少需要 3 對對應點。")
    ref = np.float32(reference_points[:3])
    src = np.float32(source_points[:3])
    matrix = cv2.getAffineTransform(ref, src)
    if matrix is None:
        raise RuntimeError("Fallback 3 點配準失敗。")
    return RegistrationResult(
        kind="affine_partial2d_from_points",
        matrix=matrix,
        source_to_reference=False,
        inliers=3,
        match_count=0,
        good_match_count=3,
        source_points=[(float(x), float(y)) for x, y in source_points[:3]],
        reference_points=[(float(x), float(y)) for x, y in reference_points[:3]],
        used_fallback_points=True,
    )


def _warp_mask(
    reference_mask: np.ndarray,
    source_shape: tuple[int, int],
    registration: RegistrationResult,
) -> np.ndarray:
    if registration.matrix is None:
        raise RuntimeError("缺少配準矩陣，無法 warp mask。")
    h, w = source_shape
    if registration.kind.startswith("homography"):
        if registration.source_to_reference:
            matrix = cv2.invert(registration.matrix)[1]
        else:
            matrix = registration.matrix
        return cv2.warpPerspective(
            reference_mask,
            matrix,
            (w, h),
            flags=cv2.INTER_NEAREST,
            borderValue=0,
        )
    if registration.source_to_reference:
        matrix = cv2.invertAffineTransform(registration.matrix)
    else:
        matrix = registration.matrix
    return cv2.warpAffine(
        reference_mask,
        matrix,
        (source_shape[1], source_shape[0]),
        flags=cv2.INTER_NEAREST,
        borderValue=0,
    )


def _build_grabcut_init(
    source_shape: tuple[int, int], warped_mask: np.ndarray, foreground_rect: Optional[tuple[int, int, int, int]]
) -> np.ndarray:
    h, w = source_shape
    init = np.full((h, w), cv2.GC_BGD, dtype=np.uint8)
    sure_fg = cv2.erode((warped_mask > 0).astype(np.uint8), np.ones((3, 3), np.uint8), iterations=1) * 255
    probable_fg = cv2.dilate((warped_mask > 0).astype(np.uint8), np.ones((9, 9), np.uint8), iterations=2) * 255
    sure_bg = cv2.dilate((warped_mask > 0).astype(np.uint8), np.ones((25, 25), np.uint8), iterations=2)
    sure_bg = (sure_bg == 0)
    if sure_bg.any():
        init[sure_bg] = cv2.GC_BGD
    init[probable_fg > 0] = cv2.GC_PR_FGD
    init[sure_fg > 0] = cv2.GC_FGD

    if foreground_rect is not None:
        x, y, ww, hh = foreground_rect
        x2 = max(0, min(x, w - 1))
        y2 = max(0, min(y, h - 1))
        xw = max(0, min(ww, w - x2))
        yh = max(0, min(hh, h - y2))
        if xw > 0 and yh > 0:
            rect = np.zeros_like(init, dtype=bool)
            rect[y2 : y2 + yh, x2 : x2 + xw] = True
            init[~rect] = cv2.GC_BGD
            if np.count_nonzero(warped_mask[rect] > 0) == 0:
                init[rect] = cv2.GC_PR_FGD

    return init


def _feather_alpha(mask: np.ndarray, radius: int = 1) -> np.ndarray:
    if radius <= 0:
        return mask
    alpha = mask.astype(np.float32) / 255.0
    k = 2 * radius + 1
    alpha = cv2.GaussianBlur(alpha, (k, k), 0)
    return np.clip(alpha * 255.0, 0, 255).astype(np.uint8)


def extract_subject(
    source_path: Path,
    reference_path: Path,
    output_path: Path,
    diagnostics_dir: Optional[Path],
    max_side: int,
    keep_components: int = 1,
    foreground_rect: Optional[tuple[int, int, int, int]] = None,
    grabcut_iterations: int = 5,
    reference_points: Optional[Sequence[tuple[float, float]]] = None,
    source_points: Optional[Sequence[tuple[float, float]]] = None,
) -> dict[str, object]:
    source_bgr_full = _read_rgb_bgr(source_path)
    reference_bgr_full = _read_rgb_bgr(reference_path)

    source_bgr, source_scale = _resize_to_limit(source_bgr_full, max_side)
    reference_bgr, reference_scale = _resize_to_limit(reference_bgr_full, max_side)

    if source_scale <= 0.0 or reference_scale <= 0.0:
        raise RuntimeError("影像縮放比例異常。")

    reference_mask_work = _detect_checkerboard_subject(reference_bgr, keep_components)

    if reference_points is not None and source_points is not None:
        if len(reference_points) < 3 or len(source_points) < 3:
            raise RuntimeError("--reference-points 與 --source-points 至少需各三點。")
        source_points_work = _rescale_points(source_points, source_scale)
        reference_points_work = _rescale_points(reference_points, reference_scale)
    else:
        source_points_work = None
        reference_points_work = None

    if reference_points_work is not None and source_points_work is not None:
        reg = _build_transform_from_points(source_points_work, reference_points_work)
    else:
        reg = _estimate_transform(
            source_bgr,
            reference_bgr,
            reference_mask_work,
            None,
            None,
        )

    warped_mask_work = _warp_mask(reference_mask_work, source_bgr.shape[:2], reg)

    if np.count_nonzero(warped_mask_work) == 0:
        raise RuntimeError("配準後遮罩為空，請調整參考點或素材。")

    if foreground_rect is not None:
        rect = (
            int(round(foreground_rect[0] * source_scale)),
            int(round(foreground_rect[1] * source_scale)),
            int(round(foreground_rect[2] * source_scale)),
            int(round(foreground_rect[3] * source_scale)),
        )
    else:
        rect = None

    init = _build_grabcut_init(source_bgr.shape[:2], warped_mask_work, rect)
    gc_mask = init.copy()
    bgd_model = np.zeros((1, 65), np.float64)
    fgd_model = np.zeros((1, 65), np.float64)

    cv2.grabCut(
        source_bgr,
        gc_mask,
        None,
        bgd_model,
        fgd_model,
        grabcut_iterations,
        cv2.GC_INIT_WITH_MASK,
    )
    final_mask_work = np.where((gc_mask == cv2.GC_FGD) | (gc_mask == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
    final_mask_work = _largest_components(final_mask_work, keep_components)

    if int(final_mask_work.mean()) == 0:
        raise RuntimeError("GrabCut 失敗：輸出全透明，請調整參考點/前景矩形。")

    source_rgb_full = cv2.cvtColor(source_bgr_full, cv2.COLOR_BGR2RGB)
    final_alpha = cv2.resize(
        final_mask_work,
        (source_bgr_full.shape[1], source_bgr_full.shape[0]),
        interpolation=cv2.INTER_NEAREST,
    )
    final_alpha = _feather_alpha(final_alpha, radius=1)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    out = np.dstack((source_rgb_full, final_alpha))
    Image.fromarray(out, "RGBA").save(str(output_path), optimize=True)

    diagnostics = {
        "source": str(source_path.resolve()),
        "reference": str(reference_path.resolve()),
        "output": str(output_path.resolve()),
        "source_scale": source_scale,
        "reference_scale": reference_scale,
        "registration": {
            "kind": reg.kind,
            "matches": reg.match_count,
            "good_matches": reg.good_match_count,
            "inliers": reg.inliers,
            "used_fallback_points": reg.used_fallback_points,
            "matrix": reg.matrix.tolist() if reg.matrix is not None else None,
            "source_points_used": reg.source_points,
            "reference_points_used": reg.reference_points,
        },
        "kept_components": keep_components,
        "final_alpha_area": int(final_mask_work.astype(np.int64).sum() // 255),
    }

    if diagnostics_dir is not None:
        diagnostics_dir.mkdir(parents=True, exist_ok=True)
        ref_diag = cv2.cvtColor(reference_mask_work, cv2.COLOR_GRAY2BGR)
        cv2.imwrite(str(diagnostics_dir / "reference-mask.png"), ref_diag)

        src_diag_ref = cv2.cvtColor(reference_mask_work, cv2.COLOR_GRAY2BGR)
        warped_diag = cv2.resize(
            warped_mask_work,
            (source_bgr.shape[1], source_bgr.shape[0]),
            interpolation=cv2.INTER_NEAREST,
        )
        warped_diag = cv2.cvtColor(warped_diag, cv2.COLOR_GRAY2BGR)
        cv2.imwrite(str(diagnostics_dir / "warped-mask.png"), warped_diag)
        final_diag = cv2.cvtColor(final_mask_work, cv2.COLOR_GRAY2BGR)
        cv2.imwrite(str(diagnostics_dir / "final-mask.png"), final_diag)

        overlay_rgb = source_rgb_full.copy()
        overlay_alpha = np.stack([final_alpha] * 3, axis=-1)
        tint = np.full_like(source_rgb_full, (255, 0, 0), dtype=np.uint8)
        overlay = cv2.addWeighted(overlay_rgb, 0.8, tint, 0.2, 0)
        blended = np.where(overlay_alpha > 120, overlay, source_rgb_full)
        Image.fromarray(blended.astype(np.uint8), "RGB").save(
            str(diagnostics_dir / "overlay.jpg"), quality=95
        )
        (diagnostics_dir / "registration.json").write_text(
            json.dumps(diagnostics, ensure_ascii=False, indent=2), encoding="utf-8"
        )

    return diagnostics


def _self_check() -> int:
    import tempfile

    h, w = 240, 320
    yy, xx = np.indices((h, w))
    checker = (((xx // 16) % 2) ^ ((yy // 16) % 2)) * 255
    checker = checker.astype(np.uint8)
    reference = cv2.cvtColor(checker, cv2.COLOR_GRAY2BGR)
    reference[:, :, :] = np.where((xx % 2)[:, :, None] == 0, [245, 245, 245], [230, 230, 230]).astype(np.uint8)
    cv2.circle(reference, (160, 120), 50, (140, 90, 80), -1)

    source = reference.copy()
    source = cv2.GaussianBlur(source, (3, 3), 0)

    with tempfile.TemporaryDirectory() as tmp:
        t = Path(tmp)
        src_p = t / "source.png"
        ref_p = t / "reference.png"
        out_p = t / "out.png"
        diag_p = t / "diag"
        Image.fromarray(cv2.cvtColor(source, cv2.COLOR_BGR2RGB)).save(src_p)
        Image.fromarray(cv2.cvtColor(reference, cv2.COLOR_BGR2RGB)).save(ref_p)
        stats = extract_subject(
            src_p,
            ref_p,
            out_p,
            diag_p,
            max_side=2000,
            keep_components=1,
            grabcut_iterations=3,
            reference_points=[(80, 120), (160, 60), (240, 120)],
            source_points=[(80, 120), (160, 60), (240, 120)],
        )
        with Image.open(out_p) as im:
            out_rgba = np.array(im.convert("RGBA"))
        out_rgb = out_rgba[:, :, :3]
        src_rgb = cv2.cvtColor(source, cv2.COLOR_BGR2RGB)
        same_rgb = np.array_equal(out_rgb, src_rgb)
        alpha_sum = int(out_rgba[:, :, 3].sum() / 255)
        if not same_rgb:
            raise RuntimeError("Self-check 失敗：輸出 RGB 已被改動。")
        if alpha_sum < 100:
            raise RuntimeError("Self-check 失敗：前景 alpha 欄位過小。")
    print(json.dumps({"self_check": True, "status": "pass", "result": stats}, ensure_ascii=False, indent=2))
    return 0


def main() -> int:
    args = parse_args()
    if args.self_check:
        return _self_check()
    try:
        stats = extract_subject(
            source_path=args.source,
            reference_path=args.reference,
            output_path=args.output,
            diagnostics_dir=args.diagnostics_dir,
            max_side=args.max_side,
            keep_components=max(1, args.keep_components),
            foreground_rect=args.foreground_rect,
            grabcut_iterations=args.grabcut_iterations,
            reference_points=args.reference_points,
            source_points=args.source_points,
        )
        print(json.dumps(stats, ensure_ascii=False, indent=2))
        return 0
    except Exception as exc:  # pragma: no cover - CLI-facing error path
        print(f"錯誤: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
