#!/usr/bin/env python3
"""Unify Cat Cafe prop pawn colour and replace warm sticker rims with white.

Targets are derived exclusively from the exported Cat Cafe configuration:
``elements[*].kind == "prop"`` with an asset under ``Pawns/``. Duplicate asset
references are collapsed. The script refuses to run when a selected asset is
also referenced by a non-prop element or when the expected target count differs.

The operation is deterministic and lossless at the file-format level: input
dimensions are retained, output is RGBA PNG, alpha bytes are unchanged, and no
resampling or lossy compression is used.
"""

from __future__ import annotations

import argparse
from collections import deque
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
from typing import Iterable

try:
    import numpy as np
    from PIL import Image
except ImportError as exc:  # pragma: no cover - dependency message for artists
    raise SystemExit(
        "This tool requires Pillow and NumPy. Run it with the Codex bundled "
        "workspace Python runtime or install pillow and numpy."
    ) from exc


DEFAULT_CONFIG = Path("Assets/Resources/GameData/cat_cafe_config.json")
DEFAULT_PAWNS_DIR = Path("Assets/Resources/CatCafe/Pawns")
DEFAULT_MANIFEST = Path("Tools/CatCafeArt/prop_style_manifest.json")
STYLE_MARKER_KEY = "cat_cafe_prop_style"
STYLE_MARKER_VALUE = "neutral-tone-white-rim-v5"
LEGACY_STYLE_MARKER_VALUE = "warm-white-rim-v1"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--pawns-dir", type=Path, default=DEFAULT_PAWNS_DIR)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument(
        "--expected-count",
        type=int,
        default=None,
        help=(
            "Optional safety guard for the number of unique Pawns/ prop assets; "
            "the target list itself always comes from the exported config."
        ),
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Write processed PNGs. Without this flag the script is read-only.",
    )
    return parser.parse_args()


def load_targets(
    config_path: Path, pawns_dir: Path, expected_count: int | None
) -> tuple[list[Path], dict[str, int]]:
    with config_path.open("r", encoding="utf-8") as stream:
        config = json.load(stream)

    elements = config.get("elements")
    if not isinstance(elements, list):
        raise ValueError(f"Missing elements array in {config_path}")

    prop_assets: set[str] = set()
    non_prop_assets: set[str] = set()
    type_counts: dict[str, int] = {}

    for element in elements:
        if not isinstance(element, dict):
            continue
        asset = element.get("asset", "")
        kind = element.get("kind", "")
        if not isinstance(asset, str) or not asset.startswith("Pawns/"):
            continue
        if kind == "prop":
            prop_assets.add(asset)
            type_label = str(element.get("type_label") or "未分类")
            type_counts[type_label] = type_counts.get(type_label, 0) + 1
        else:
            non_prop_assets.add(asset)

    overlap = sorted(prop_assets & non_prop_assets)
    if overlap:
        raise ValueError(
            "Refusing to edit assets shared by prop and non-prop elements: "
            + ", ".join(overlap)
        )
    if expected_count is not None and len(prop_assets) != expected_count:
        raise ValueError(
            f"Expected {expected_count} unique Pawns/ prop assets, "
            f"found {len(prop_assets)}"
        )

    targets = [pawns_dir / f"{asset.removeprefix('Pawns/')}.png" for asset in prop_assets]
    missing = sorted(str(path) for path in targets if not path.is_file())
    if missing:
        raise FileNotFoundError("Missing prop PNGs:\n" + "\n".join(missing))

    return sorted(targets, key=lambda path: path.name), dict(sorted(type_counts.items()))


def manhattan_distance_from_transparency(solid: np.ndarray) -> np.ndarray:
    """Return the exact Manhattan distance from every pixel to transparency."""

    height, width = solid.shape
    limit = height + width + 1
    distance = np.where(solid, limit, 0).astype(np.int32)

    x = np.arange(width, dtype=np.int32)
    left = np.minimum.accumulate(distance - x[None, :], axis=1) + x[None, :]
    right_reversed = (
        np.minimum.accumulate(distance[:, ::-1] - x[None, :], axis=1)
        + x[None, :]
    )
    horizontal = np.minimum(left, right_reversed[:, ::-1])

    y = np.arange(height, dtype=np.int32)
    top = np.minimum.accumulate(horizontal - y[:, None], axis=0) + y[:, None]
    bottom_reversed = (
        np.minimum.accumulate(horizontal[::-1, :] - y[:, None], axis=0)
        + y[:, None]
    )
    return np.minimum(top, bottom_reversed[::-1, :])


def undo_legacy_colour_grade(rgb: np.ndarray) -> np.ndarray:
    """Approximately invert the v1 warm grade before applying the neutral style."""

    graded = rgb.astype(np.float32) / 255.0
    warm_multiplier = np.array([1.025, 1.005, 0.965], dtype=np.float32)
    mixed = ((graded - 0.5) / 0.97 + 0.5) / warm_multiplier
    luminance = (
        mixed[..., 0] * 0.299
        + mixed[..., 1] * 0.587
        + mixed[..., 2] * 0.114
    )[..., None]
    source = (mixed - luminance * 0.16) / 0.84
    return np.clip(np.rint(source * 255.0), 0, 255).astype(np.uint8)


def neutral_colour_grade(rgb: np.ndarray) -> np.ndarray:
    """Remove the source set's yellow cast while preserving painted colour."""

    source = rgb.astype(np.float32)
    # Calibrated against the supplied cat-sticker reference: reduce excess red
    # and restore blue lost to the source set's sepia/warm paper treatment.
    multiplier = np.array([0.94, 1.0, 1.13], dtype=np.float32)
    graded = np.clip(np.rint(source * multiplier), 0, 255).astype(np.uint8)
    neutral = (source.max(axis=2) - source.min(axis=2)) <= 4
    graded[neutral] = rgb[neutral]
    return graded


def exterior_connected_mask(candidate: np.ndarray, seeds: np.ndarray) -> np.ndarray:
    """Flood candidate pixels from the transparent-facing edge only."""

    height, width = candidate.shape
    selected = np.zeros_like(candidate, dtype=bool)
    queue: deque[tuple[int, int]] = deque()
    for y, x in np.argwhere(seeds):
        selected[y, x] = True
        queue.append((int(y), int(x)))

    while queue:
        y, x = queue.popleft()
        for next_y in range(max(0, y - 1), min(height, y + 2)):
            for next_x in range(max(0, x - 1), min(width, x + 2)):
                if candidate[next_y, next_x] and not selected[next_y, next_x]:
                    selected[next_y, next_x] = True
                    queue.append((next_y, next_x))
    return selected


def process_pixels(image: Image.Image) -> tuple[Image.Image, int, int]:
    rgba = np.array(image.convert("RGBA"), dtype=np.uint8)
    original_alpha = rgba[..., 3].copy()
    original_rgb = rgba[..., :3]
    embedded_style = image.info.get(STYLE_MARKER_KEY)
    legacy_v1 = embedded_style == LEGACY_STYLE_MARKER_VALUE
    legacy_white_rim = np.zeros(original_alpha.shape, dtype=bool)
    if legacy_v1:
        legacy_white_rim = (
            (original_rgb.min(axis=2) >= 248)
            & ((original_rgb.max(axis=2) - original_rgb.min(axis=2)) <= 4)
            & (original_alpha > 8)
        )
        original_rgb = undo_legacy_colour_grade(original_rgb)
    rgb = (
        original_rgb.copy()
        if embedded_style == STYLE_MARKER_VALUE
        else neutral_colour_grade(original_rgb)
    )

    solid = original_alpha > 8
    distance = manhattan_distance_from_transparency(solid)
    # Include the full warm paper stock between the subject outline and the
    # transparent edge. Six percent matches the broad neutral rim of the cat
    # sticker reference; colour classification protects painted subject detail.
    shortest_edge = min(image.size)
    border_width = max(16, int(round(shortest_edge * 0.06)))
    outer_band = solid & (distance <= border_width)

    # The sticker stock is geometric, not colour-selected: every solid pixel
    # in the transparent-facing band must be neutral white. This removes the
    # source art's brown/yellow decorative rim consistently in Unity.
    white_rim = outer_band
    # v1 already located the rim before its global warm grade was inverted;
    # retain that proven selection exactly and keep it neutral white.
    white_rim |= legacy_white_rim
    rgb[white_rim] = 255
    # Keep hidden RGB under fully transparent pixels white as well. Bilinear
    # texture sampling can otherwise pull old warm RGB into the visible edge.
    rgb[original_alpha == 0] = 255

    output = np.dstack((rgb, original_alpha))
    return Image.fromarray(output, mode="RGBA"), int(white_rim.sum()), border_width


def validate_output(source: Image.Image, output: Image.Image, path: Path) -> None:
    if output.mode != "RGBA":
        raise ValueError(f"Output is not RGBA: {path}")
    if output.size != source.size:
        raise ValueError(f"Dimensions changed for {path}: {source.size} -> {output.size}")
    source_alpha = np.asarray(source.convert("RGBA"), dtype=np.uint8)[..., 3]
    output_alpha = np.asarray(output, dtype=np.uint8)[..., 3]
    if not np.array_equal(source_alpha, output_alpha):
        raise ValueError(f"Alpha channel changed for {path}")


def stage_outputs(targets: Iterable[Path], stage_dir: Path) -> list[tuple[Path, Path]]:
    staged: list[tuple[Path, Path]] = []
    for target in targets:
        with Image.open(target) as source:
            source.load()
            output, rim_pixels, border_width = process_pixels(source)
            validate_output(source, output, target)
            staged_path = stage_dir / "processed" / target.name
            staged_path.parent.mkdir(parents=True, exist_ok=True)
            output.save(
                staged_path,
                format="PNG",
                # PNG compression is lossless; level 0 also maximizes decoder
                # compatibility and never changes pixel values.
                compress_level=0,
            )

        with Image.open(staged_path) as check:
            check.load()
            if (
                check.mode != "RGBA"
                or check.size != output.size
                or check.info
            ):
                raise ValueError(f"Staged PNG validation failed: {staged_path}")
        print(
            f"STAGED {target.name} {output.width}x{output.height} "
            f"rim={rim_pixels} border_width={border_width}"
        )
        staged.append((target, staged_path))
    return staged


def commit_with_rollback(staged: list[tuple[Path, Path]], stage_dir: Path) -> None:
    originals_dir = stage_dir / "originals"
    originals_dir.mkdir(parents=True, exist_ok=True)
    committed: list[tuple[Path, Path]] = []
    try:
        for target, processed in staged:
            original = originals_dir / target.name
            # Copy bytes over the existing target instead of moving a staged
            # file into Assets. On Windows, os.replace carries the staged
            # file's sandbox DACL with it and can make the asset unreadable to
            # the user's Unity process. copyfile preserves the target ACL.
            shutil.copy2(target, original)
            try:
                shutil.copyfile(processed, target)
            except BaseException:
                shutil.copyfile(original, target)
                raise
            committed.append((target, original))
    except BaseException:
        for target, original in reversed(committed):
            if original.exists():
                shutil.copyfile(original, target)
        raise


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_manifest(path: Path) -> dict[str, object]:
    if not path.is_file():
        return {}
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    return value if isinstance(value, dict) else {}


def write_manifest(path: Path, targets: Iterable[Path], stage_dir: Path) -> None:
    value = {
        "style": STYLE_MARKER_VALUE,
        "files": {target.name: file_sha256(target) for target in targets},
    }
    staged = stage_dir / "prop_style_manifest.json"
    with staged.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
        stream.write("\n")
    path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(staged, path)


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="backslashreplace")
    args = parse_args()
    config_path = args.config.resolve()
    pawns_dir = args.pawns_dir.resolve()
    manifest_path = args.manifest.resolve()
    targets, type_counts = load_targets(
        config_path, pawns_dir, expected_count=args.expected_count
    )

    print(f"TARGET_COUNT={len(targets)}")
    print("TYPE_COUNTS=" + json.dumps(type_counts, ensure_ascii=False, sort_keys=True))
    manifest = load_manifest(manifest_path)
    manifest_files = manifest.get("files", {})
    if not isinstance(manifest_files, dict):
        manifest_files = {}
    pending: list[Path] = []
    for target in targets:
        if (
            manifest.get("style") == STYLE_MARKER_VALUE
            and manifest_files.get(target.name) == file_sha256(target)
        ):
            with Image.open(target) as image:
                image.load()
                print(f"ALREADY {target.name} {image.width}x{image.height}")
            continue
        with Image.open(target) as image:
            image.load()
            if image.mode != "RGBA":
                raise ValueError(f"Input is not RGBA: {target} ({image.mode})")
            if image.format != "PNG":
                raise ValueError(f"Input is not PNG: {target} ({image.format})")
            output, rim_pixels, border_width = process_pixels(image)
            validate_output(image, output, target)
            print(
                f"CHECK {target.name} {image.width}x{image.height} "
                f"rim={rim_pixels} border_width={border_width}"
            )
            pending.append(target)

    if not args.apply:
        print("DRY_RUN_OK (pass --apply to write files)")
        return 0

    project_root = config_path.parents[3]
    stage_dir = Path(
        tempfile.mkdtemp(prefix=".codex_prop_style_", dir=project_root)
    )
    try:
        staged = stage_outputs(pending, stage_dir)
        commit_with_rollback(staged, stage_dir)
        write_manifest(manifest_path, targets, stage_dir)
    finally:
        shutil.rmtree(stage_dir, ignore_errors=True)

    print(f"APPLIED={len(pending)}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc
