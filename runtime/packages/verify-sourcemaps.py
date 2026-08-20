#!/usr/bin/env python3
"""Verify emitted runtime-package Source Map v3 files against their PHP and Tyhp sources."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
VLQ_DECODE = {ch: i for i, ch in enumerate(ALPHABET)}
# Only declaration-start lines (not doc comments that mention "class already" / "class wins").
DECLARATION = re.compile(
    r"^\s*(?:(?:final|abstract|readonly|public|protected|private|static)\s+)*"
    r"(?:(?:class|interface|trait|enum)\s+([A-Za-z_][A-Za-z0-9_]*)"
    r"|function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\()"
)


def decode_vlq_values(segment: str) -> list[int]:
    values: list[int] = []
    result = 0
    shift = 0
    for ch in segment:
        digit = VLQ_DECODE.get(ch)
        if digit is None:
            raise ValueError(f"invalid VLQ character {ch!r}")
        result += (digit & 31) << shift
        if digit & 32:
            shift += 5
            continue
        sign = result & 1
        magnitude = result >> 1
        values.append(-magnitude if sign else magnitude)
        result = 0
        shift = 0
    if shift != 0:
        raise ValueError("truncated VLQ segment")
    return values


def decode_mappings(mappings: str) -> list[tuple[int, int, int, int, int, int | None]]:
    """Return (gen_line, gen_col, src_idx, orig_line, orig_col, name_idx)."""
    decoded: list[tuple[int, int, int, int, int, int | None]] = []
    gen_line = 0
    prev_gen_col = 0
    prev_src = 0
    prev_orig_line = 0
    prev_orig_col = 0
    prev_name = 0
    for group in mappings.split(";"):
        prev_gen_col = 0
        if group:
            for segment in group.split(","):
                if not segment:
                    continue
                values = decode_vlq_values(segment)
                if len(values) not in (1, 4, 5):
                    raise ValueError(f"illegal VLQ field count {len(values)} in {segment!r}")
                gen_col = prev_gen_col + values[0]
                prev_gen_col = gen_col
                if len(values) == 1:
                    continue
                src_idx = prev_src + values[1]
                orig_line = prev_orig_line + values[2]
                orig_col = prev_orig_col + values[3]
                prev_src = src_idx
                prev_orig_line = orig_line
                prev_orig_col = orig_col
                name_idx = None
                if len(values) == 5:
                    name_idx = prev_name + values[4]
                    prev_name = name_idx
                decoded.append((gen_line, gen_col, src_idx, orig_line, orig_col, name_idx))
        gen_line += 1
    return decoded


def php_lines_without_map_url(php: str) -> list[str]:
    return [line for line in php.split("\n") if "sourceMappingURL=" not in line]


def resolve_source_text(
    source_root: str,
    source: str,
    sources_content: list[str | None] | None,
    source_index: int,
    package_dir: Path,
) -> str | None:
    if sources_content is not None and source_index < len(sources_content):
        embedded = sources_content[source_index]
        if embedded is not None:
            return embedded
    rel = (source_root + source).lstrip("/")
    candidate = (package_dir / rel).resolve()
    if candidate.is_file():
        return candidate.read_text(encoding="utf-8")
    return None


def check_php_file(php_path: Path, package_dir: Path) -> list[str]:
    errors: list[str] = []
    map_path = Path(str(php_path) + ".map")
    php = php_path.read_text(encoding="utf-8")
    if not map_path.is_file():
        return [f"{php_path}: missing {map_path.name}"]
    if f"//# sourceMappingURL={php_path.name}.map" not in php:
        errors.append(f"{php_path}: missing sourceMappingURL comment")

    try:
        data = json.loads(map_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        return [f"{map_path}: invalid JSON ({exc})"]

    if data.get("version") != 3:
        errors.append(f"{map_path}: version is {data.get('version')!r}, expected 3")
    if data.get("file") != php_path.name:
        errors.append(f"{map_path}: file is {data.get('file')!r}, expected {php_path.name!r}")

    sources = data.get("sources")
    mappings = data.get("mappings")
    source_root = data.get("sourceRoot") or ""
    if not isinstance(sources, list) or not sources:
        errors.append(f"{map_path}: sources must be a non-empty array")
        return errors
    if not isinstance(mappings, str) or mappings == "":
        errors.append(f"{map_path}: mappings must be a non-empty string")
        return errors

    try:
        decoded = decode_mappings(mappings)
    except ValueError as exc:
        errors.append(f"{map_path}: VLQ decode failed ({exc})")
        return errors
    if not decoded:
        errors.append(f"{map_path}: decoded mappings are empty")
        return errors

    sources_content = data.get("sourcesContent")
    if sources_content is not None:
        if not isinstance(sources_content, list) or len(sources_content) != len(sources):
            errors.append(f"{map_path}: sourcesContent length does not match sources")
            sources_content = None

    source_texts: list[str | None] = []
    for i, source in enumerate(sources):
        if not isinstance(source, str) or source == "":
            errors.append(f"{map_path}: sources[{i}] is empty")
            source_texts.append(None)
            continue
        text = resolve_source_text(
            source_root if isinstance(source_root, str) else "",
            source,
            sources_content if isinstance(sources_content, list) else None,
            i,
            package_dir,
        )
        if text is None:
            errors.append(f"{map_path}: cannot resolve source {source_root}{source}")
        source_texts.append(text)

    php_lines = php_lines_without_map_url(php)
    for gen_line, _gen_col, src_idx, orig_line, _orig_col, _name in decoded:
        if gen_line >= len(php_lines):
            errors.append(
                f"{map_path}: mapping generated line {gen_line} is past PHP ({len(php_lines)} lines)"
            )
            break
        if src_idx < 0 or src_idx >= len(sources):
            errors.append(f"{map_path}: source index {src_idx} out of range")
            break
        src_text = source_texts[src_idx]
        if src_text is None:
            continue
        src_lines = src_text.split("\n")
        if orig_line < 0 or orig_line >= len(src_lines):
            errors.append(
                f"{map_path}: original line {orig_line} out of range for {sources[src_idx]} "
                f"({len(src_lines)} lines)"
            )
            break

    # Spot-check: a class/function declaration in PHP should map back to a Tyhp line
    # that names the original identifier. Compiler-generated helpers (__tyhp*, new_Tyhp_*)
    # correctly map to the owning type and are skipped.
    mapped_by_gen = {}
    for gen_line, _gen_col, src_idx, orig_line, _orig_col, _name in decoded:
        mapped_by_gen.setdefault(gen_line, []).append((src_idx, orig_line))
    checked = 0
    for gen_line, line in enumerate(php_lines):
        match = DECLARATION.match(line)
        if match is None:
            continue
        name = match.group(1) or match.group(2)
        if "__tyhp" in name or name.startswith("new_Tyhp_"):
            continue
        hits = mapped_by_gen.get(gen_line)
        if not hits:
            continue
        src_idx, orig_line = hits[0]
        src_text = source_texts[src_idx]
        if src_text is None:
            continue
        src_lines = src_text.split("\n")
        if orig_line >= len(src_lines):
            continue
        original = src_lines[orig_line]
        window = "".join(src_lines[max(0, orig_line - 1) : orig_line + 2])
        if name not in original and name not in window:
            # Compiler-injected members (e.g. PHP 8.2/8.3 property-hook polyfill
            # constructors) map to the owning type declaration.
            if re.search(r"\b(?:class|interface|trait|enum)\b", original):
                continue
            errors.append(
                f"{map_path}: generated '{name}' on PHP line {gen_line + 1} mapped to "
                f"{sources[src_idx]}:{orig_line + 1} which does not contain that name "
                f"({original.strip()!r})"
            )
        checked += 1
        if checked >= 8:
            break

    return errors


def source_xy(composer_json: Path) -> str:
    raw = str(json.loads(composer_json.read_text(encoding="utf-8")).get("version", ""))
    match = re.fullmatch(r"(\d+)\.(\d+)(?:\.(\d+)(?:-([0-9A-Za-z.-]+))?)?", raw)
    if not match:
        raise ValueError(f"invalid version in {composer_json}: {raw!r}")
    if match.group(3) is None:
        xy = f"{match.group(1)}.{match.group(2)}"
    else:
        xy = f"{match.group(2)}.{match.group(3)}"
        if match.group(4):
            xy += f"-{match.group(4)}"
    return xy


def iter_php_files(dist_root: Path) -> list[tuple[Path, Path]]:
    pairs: list[tuple[Path, Path]] = []
    packages_root = dist_root.parent
    for composer_json in sorted(packages_root.glob("*/composer.json")):
        package_dir = composer_json.parent
        pkg_name = package_dir.name
        if pkg_name == "php":
            continue
        xy = source_xy(composer_json)
        for src_dir in sorted(dist_root.glob(f"tyhp-{pkg_name}/*.{xy}/src")):
            if not src_dir.is_dir():
                continue
            for php_path in sorted(src_dir.rglob("*.php")):
                pairs.append((php_path, package_dir))
    return pairs


def main() -> int:
    dist_root = Path(__file__).resolve().parent / "dist"
    if not dist_root.is_dir():
        print(f"dist directory not found: {dist_root}", file=sys.stderr)
        return 1

    pairs = iter_php_files(dist_root)
    if not pairs:
        print(f"no emitted PHP files under {dist_root}", file=sys.stderr)
        return 1

    errors: list[str] = []
    for php_path, package_dir in pairs:
        errors.extend(check_php_file(php_path, package_dir))

    if errors:
        print(f"Source map verification failed ({len(errors)} issue(s), {len(pairs)} PHP files):")
        for err in errors:
            print(f"  {err}")
        return 1

    print(f"Source maps OK: {len(pairs)} PHP files, each with a valid v3 map.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
