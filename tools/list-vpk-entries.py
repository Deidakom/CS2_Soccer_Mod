#!/usr/bin/env python3
"""List or extract an exact file stored in a Valve VPK directory index.

This small read-only helper supports the VPK v1/v2 directory tree format used
by Source and Source 2. Extraction writes only the explicitly supplied output
path and never modifies the archive.
"""

from __future__ import annotations

import argparse
import re
import struct
from dataclasses import dataclass
from pathlib import Path


VPK_SIGNATURE = 0x55AA1234


@dataclass(frozen=True)
class VpkEntry:
    name: str
    preload: bytes
    archive_index: int
    offset: int
    length: int
    file_data_offset: int


def read_cstring(data: bytes, offset: int) -> tuple[str, int]:
    end = data.find(b"\0", offset)
    if end < 0:
        raise ValueError("unterminated string in VPK directory tree")
    return data[offset:end].decode("utf-8", errors="surrogateescape"), end + 1


def entries(path: Path) -> list[VpkEntry]:
    data = path.read_bytes()
    if len(data) < 12:
        raise ValueError(f"{path} is too small to be a VPK directory")

    signature, version, tree_size = struct.unpack_from("<III", data, 0)
    if signature != VPK_SIGNATURE:
        raise ValueError(f"{path} has an invalid VPK signature")
    if version not in (1, 2):
        raise ValueError(f"unsupported VPK version: {version}")

    header_size = 12 if version == 1 else 28
    tree_end = header_size + tree_size
    if tree_end > len(data):
        raise ValueError("VPK directory tree extends beyond the file")

    found: list[VpkEntry] = []
    cursor = header_size
    while cursor < tree_end:
        extension, cursor = read_cstring(data, cursor)
        if not extension:
            break
        while cursor < tree_end:
            directory, cursor = read_cstring(data, cursor)
            if not directory:
                break
            while cursor < tree_end:
                stem, cursor = read_cstring(data, cursor)
                if not stem:
                    break
                if cursor + 18 > tree_end:
                    raise ValueError("truncated VPK file entry")
                _, preload_bytes, archive_index, entry_offset, entry_length = struct.unpack_from(
                    "<IHHII", data, cursor
                )
                cursor += 16
                terminator = struct.unpack_from("<H", data, cursor)[0]
                cursor += 2
                if terminator != 0xFFFF:
                    raise ValueError("invalid VPK file-entry terminator")
                preload = data[cursor:cursor + preload_bytes]
                cursor += preload_bytes
                if cursor > tree_end:
                    raise ValueError("VPK preload data extends beyond the directory tree")

                prefix = "" if directory in ("", " ") else directory.rstrip("/") + "/"
                suffix = "" if extension in ("", " ") else "." + extension
                found.append(VpkEntry(
                    name=prefix + stem + suffix,
                    preload=preload,
                    archive_index=archive_index,
                    offset=entry_offset,
                    length=entry_length,
                    file_data_offset=tree_end,
                ))

    return found


def read_entry_payload(directory_path: Path, entry: VpkEntry) -> bytes:
    if entry.archive_index == 0x7FFF:
        archive_path = directory_path
        payload_offset = entry.file_data_offset + entry.offset
    else:
        stem = directory_path.stem
        base = stem[:-4] if stem.endswith("_dir") else stem
        archive_path = directory_path.with_name(f"{base}_{entry.archive_index:03d}.vpk")
        payload_offset = entry.offset

    with archive_path.open("rb") as stream:
        stream.seek(payload_offset)
        payload = stream.read(entry.length)
    if len(payload) != entry.length:
        raise ValueError(
            f"short read for {entry.name}: expected {entry.length}, got {len(payload)}"
        )
    return entry.preload + payload


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("vpk", type=Path, help="path to an *_dir.vpk file")
    parser.add_argument("pattern", nargs="?", default="", help="optional regex filter")
    parser.add_argument("--extract", help="extract one exact entry name")
    parser.add_argument("--output", type=Path, help="output path used with --extract")
    args = parser.parse_args()

    all_entries = entries(args.vpk)
    if args.extract:
        if not args.output:
            parser.error("--output is required with --extract")
        matches = [entry for entry in all_entries if entry.name == args.extract]
        if len(matches) != 1:
            raise ValueError(
                f"expected one exact entry named {args.extract!r}, found {len(matches)}"
            )
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(read_entry_payload(args.vpk, matches[0]))
        print(f"extracted {matches[0].name} -> {args.output}")
        return

    matcher = re.compile(args.pattern, re.IGNORECASE) if args.pattern else None
    for entry in all_entries:
        if matcher is None or matcher.search(entry.name):
            print(entry.name)


if __name__ == "__main__":
    main()
