"""Build a fresh dynamic-jersey source addon with authored torso sockets.

This tool is intentionally conservative: it copies an existing accepted
gearless source tree into a new directory, verifies each source model against
the calibration manifest, and edits only the modeldoc AttachmentList. It
never overwrites an output directory and never touches a live/game addon
unless the caller explicitly chooses that directory.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path
from typing import Any


NAME_SOCKET = "soccermod_jersey_name"
NUMBER_SOCKET = "soccermod_jersey_number"
SOCKET_ROLES = ("name", "number")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def _matching_delimiter(text: str, start: int, opening: str, closing: str) -> int:
    depth = 0
    quoted = False
    escaped = False
    for index in range(start, len(text)):
        character = text[index]
        if quoted:
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                quoted = False
            continue
        if character == '"':
            quoted = True
        elif character == opening:
            depth += 1
        elif character == closing:
            depth -= 1
            if depth == 0:
                return index
    raise ValueError(f"unclosed {opening}{closing} block")


def _attachment_list_bounds(source: str) -> tuple[int, int]:
    attachment_class = re.search(r'_class\s*=\s*"AttachmentList"', source)
    if attachment_class is None:
        raise ValueError("model has no AttachmentList")
    children = re.search(r'\bchildren\s*=\s*\[', source[attachment_class.end():])
    if children is None:
        raise ValueError("AttachmentList has no children array")
    opening = attachment_class.end() + children.end() - 1
    return opening, _matching_delimiter(source, opening, "[", "]")


def _top_level_brace_blocks(source: str) -> list[str]:
    blocks: list[str] = []
    depth = 0
    start: int | None = None
    quoted = False
    escaped = False
    for index, character in enumerate(source):
        if quoted:
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                quoted = False
            continue
        if character == '"':
            quoted = True
        elif character == "{":
            if depth == 0:
                start = index
            depth += 1
        elif character == "}":
            depth -= 1
            if depth < 0:
                raise ValueError("unbalanced attachment block")
            if depth == 0 and start is not None:
                blocks.append(source[start:index + 1])
                start = None
    if depth != 0:
        raise ValueError("unclosed attachment block")
    return blocks


def _attachment_name(block: str) -> str | None:
    match = re.search(r'(?m)^\s*name\s*=\s*"([^"]+)"\s*$', block)
    return match.group(1) if match else None


def _canonical(text: str) -> str:
    return re.sub(r"\s+", " ", text.strip().rstrip(","))


def _float(value: Any) -> str:
    number = float(value)
    if number == 0:
        number = 0.0
    return f"{number:.6f}"


def _expected_attachment_block(
    role: str,
    config: dict[str, Any],
    newline: str,
    indent: str,
) -> str:
    socket = NAME_SOCKET if role == "name" else NUMBER_SOCKET
    if config.get("name") != socket:
        raise ValueError(f"{role} calibration has unexpected socket name")
    origin = config.get("relative_origin")
    angles = config.get("relative_angles")
    if not isinstance(origin, list) or len(origin) != 3:
        raise ValueError(f"{role} calibration must have three origin values")
    if not isinstance(angles, list) or len(angles) != 3:
        raise ValueError(f"{role} calibration must have three angle values")
    lines = [
        f"{indent}{{",
        f"{indent}\t_class = \"Attachment\"",
        f"{indent}\tname = \"{socket}\"",
        f"{indent}\tignore_rotation = false",
        f"{indent}\tparent_bone = \"{config['parent_bone']}\"",
        f"{indent}\trelative_origin = [ {', '.join(_float(value) for value in origin)} ]",
        f"{indent}\trelative_angles = [ {', '.join(_float(value) for value in angles)} ]",
        f"{indent}\tweight = 1.0",
        f"{indent}}}",
    ]
    return newline.join(lines)


def add_jersey_attachments(source: str, model_config: dict[str, Any]) -> str:
    """Return *source* with the two calibrated sockets added.

    Re-running against the exact generated output is a no-op. Any partial,
    duplicate, or changed dynamic socket is rejected instead of being merged.
    """

    torso_bone = model_config.get("torso_bone")
    if not isinstance(torso_bone, str) or not torso_bone:
        raise ValueError("model calibration has no torso bone")
    if not re.search(rf'(?m)^\s*name\s*=\s*"{re.escape(torso_bone)}"\s*$', source):
        raise ValueError(f"model skeleton does not contain torso bone {torso_bone!r}")

    list_start, list_end = _attachment_list_bounds(source)
    body = source[list_start + 1:list_end]
    blocks = _top_level_brace_blocks(body)
    if not blocks:
        raise ValueError("AttachmentList has no attachment entries")

    names = [_attachment_name(block) for block in blocks]
    weapon_blocks = [block for block, name in zip(blocks, names) if name == "weapon"]
    if len(weapon_blocks) != 1:
        raise ValueError("expected exactly one existing weapon attachment")
    if re.search(r'(?m)^\s*parent_bone\s*=\s*"wpn"\s*$', weapon_blocks[0]) is None:
        raise ValueError("existing weapon attachment no longer uses parent_bone wpn")

    newline = "\r\n" if "\r\n" in source else "\n"
    indent_match = re.search(r"\r?\n([ \t]*)\{", body)
    indent = indent_match.group(1) if indent_match else "\t\t\t\t\t"
    calibration = model_config.get("attachments")
    if not isinstance(calibration, dict):
        raise ValueError("model calibration has no attachments")

    target_names = {NAME_SOCKET, NUMBER_SOCKET}
    target_blocks = {
        name: block for block, name in zip(blocks, names) if name in target_names
    }
    target_counts = {target: names.count(target) for target in target_names}
    if any(count > 1 for count in target_counts.values()):
        raise ValueError("duplicate dynamic jersey attachment")

    expected = {
        role: _expected_attachment_block(role, calibration[role], newline, indent)
        for role in SOCKET_ROLES
    }
    if any(target_counts[target] for target in target_names):
        if target_counts[NAME_SOCKET] != 1 or target_counts[NUMBER_SOCKET] != 1:
            raise ValueError("partial dynamic jersey attachment set")
        for role, socket in (("name", NAME_SOCKET), ("number", NUMBER_SOCKET)):
            if _canonical(target_blocks[socket]) != _canonical(expected[role]):
                raise ValueError(f"existing {socket} attachment differs from calibration")
        return source

    trailing_match = re.search(r"\s*$", body)
    trailing = trailing_match.group(0) if trailing_match else ""
    prefix = body[:len(body) - len(trailing)] if trailing else body
    generated = expected["name"] + "," + newline + expected["number"]
    separator = "" if prefix.rstrip().endswith(",") else ","
    if prefix.strip():
        replacement_body = prefix + separator + newline + generated.replace(newline, newline) + "," + trailing
    else:
        replacement_body = newline + generated + "," + trailing
    return source[:list_start + 1] + replacement_body + source[list_end:]


def _load_manifest(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    if not isinstance(manifest, dict) or not isinstance(manifest.get("models"), list):
        raise ValueError("calibration manifest must contain a models list")
    return manifest


def prepare_candidate(
    source_dir: Path,
    output_dir: Path,
    manifest_path: Path,
    accepted_package: Path | None = None,
    report_path: Path | None = None,
) -> dict[str, Any]:
    manifest = _load_manifest(manifest_path)
    source_dir = source_dir.resolve()
    output_dir = output_dir.resolve()
    if not source_dir.is_dir():
        raise ValueError(f"source directory does not exist: {source_dir}")
    if output_dir.exists():
        raise ValueError(f"refusing to overwrite existing output directory: {output_dir}")
    try:
        output_dir.relative_to(source_dir)
    except ValueError:
        pass
    else:
        raise ValueError("output directory must be a fresh sibling/outside the source directory")

    package = manifest.get("accepted_package")
    if accepted_package is not None:
        if not accepted_package.is_file():
            raise ValueError(f"accepted package does not exist: {accepted_package}")
        if isinstance(package, dict):
            expected_size = package.get("size_bytes")
            expected_hash = package.get("sha256")
            actual_size = accepted_package.stat().st_size
            actual_hash = _sha256(accepted_package)
            if expected_size is not None and actual_size != expected_size:
                raise ValueError("accepted package size does not match calibration manifest")
            if expected_hash and actual_hash != str(expected_hash).upper():
                raise ValueError("accepted package hash does not match calibration manifest")

    models = manifest["models"]
    source_relatives = [str(model.get("source_relative", "")) for model in models if isinstance(model, dict)]
    if len(source_relatives) != len(set(source_relatives)):
        raise ValueError("calibration manifest contains duplicate model sources")
    prepared: list[dict[str, Any]] = []
    for model in models:
        if not isinstance(model, dict):
            raise ValueError("model entry must be an object")
        relative = Path(str(model["source_relative"]))
        source_model = source_dir / relative
        if not source_model.is_file():
            raise ValueError(f"model source is missing: {source_model}")
        expected_hash = str(model.get("source_sha256", "")).upper()
        if expected_hash and _sha256(source_model) != expected_hash:
            raise ValueError(f"source hash mismatch: {source_model}")
        prepared.append(model)

    shutil.copytree(source_dir, output_dir)
    output_hashes: dict[str, str] = {}
    for model in prepared:
        relative = Path(str(model["source_relative"]))
        destination = output_dir / relative
        with destination.open("r", encoding="utf-8", newline="") as handle:
            original = handle.read()
        transformed = add_jersey_attachments(original, model)
        with destination.open("w", encoding="utf-8", newline="") as handle:
            handle.write(transformed)
        output_hashes[relative.as_posix()] = _sha256(destination)

    report = {
        "manifest": manifest_path.name,
        "source_dir": str(source_dir),
        "output_dir": str(output_dir),
        "models": output_hashes,
        "socket_names": {"name": NAME_SOCKET, "number": NUMBER_SOCKET},
    }
    if report_path is not None:
        report_path.parent.mkdir(parents=True, exist_ok=True)
        with report_path.open("w", encoding="utf-8", newline="\n") as handle:
            json.dump(report, handle, indent=2, sort_keys=True)
            handle.write("\n")
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "docs/jerseys/jersey-attachment-calibration.json",
    )
    parser.add_argument("--accepted-package", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    report = prepare_candidate(
        args.source_dir,
        args.output_dir,
        args.manifest,
        accepted_package=args.accepted_package,
        report_path=args.report,
    )
    print(json.dumps(report, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
