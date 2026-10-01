#!/usr/bin/env python3
"""Single-file VPK v2 helper: list <vpk> | merge <out> <base.vpk> <overlay.vpk> (overlay wins)."""
import struct, sys, zlib, hashlib
def read(path):
    f = open(path, "rb"); sig, ver, tree_size = struct.unpack("<III", f.read(12))
    assert sig == 0x55aa1234 and ver == 2, path
    f.read(16); tree = f.read(tree_size); data_start = 28 + tree_size
    ents, p = {}, 0
    def cstr():
        nonlocal p
        e = tree.index(b"\0", p); s = tree[p:e].decode("latin1"); p = e + 1; return s
    while True:
        ext = cstr()
        if not ext: break
        while True:
            d = cstr()
            if not d: break
            while True:
                n = cstr()
                if not n: break
                crc, preload, arch, off, length, term = struct.unpack("<IHHIIH", tree[p:p+18]); p += 18
                pre = tree[p:p+preload]; p += preload
                assert arch == 0x7fff, (path, arch)
                name = (("" if d == " " else d + "/") + n + "." + ext)
                ents[name] = (path, data_start + off, length, pre)
    return ents
def data(e):
    path, off, length, pre = e
    with open(path, "rb") as f: f.seek(off); return pre + f.read(length)
if sys.argv[1] == "list":
    for n, e in sorted(read(sys.argv[2]).items()): print(n, e[2] + len(e[3]))
elif sys.argv[1] == "merge":
    out, base, over = sys.argv[2:5]; a, b = read(base), read(over)
    same = [n for n in b if n in a]
    for n in same:
        if hashlib.md5(data(a[n])).digest() != hashlib.md5(data(b[n])).digest(): print("differs, overlay wins:", n)
    a.update(b); tree = {}
    for n in a:
        ext = n.rsplit(".", 1)[1]; base_ = n.rsplit(".", 1)[0]; d = base_.rsplit("/", 1)[0] if "/" in base_ else " "; nm = base_.rsplit("/", 1)[-1]
        tree.setdefault(ext, {}).setdefault(d, []).append((nm, n))
    parts, off, order = [], 0, []
    for ext in sorted(tree):
        parts.append(ext.encode("latin1") + b"\0")
        for d in sorted(tree[ext]):
            parts.append(d.encode("latin1") + b"\0")
            for nm, n in sorted(tree[ext][d]):
                blob = data(a[n]); parts.append(nm.encode("latin1") + b"\0" + struct.pack("<IHHIIH", zlib.crc32(blob) & 0xffffffff, 0, 0x7fff, off, len(blob), 0xffff)); order.append(n); off += len(blob)
            parts.append(b"\0")
        parts.append(b"\0")
    parts.append(b"\0"); t = b"".join(parts)
    with open(out, "wb") as f:
        f.write(struct.pack("<IIIIIII", 0x55aa1234, 2, len(t), off, 0, 0, 0)); f.write(t)
        for n in order: f.write(data(a[n]))
    print(f"{out}: {len(a)} files ({len(same)} in both), {off/1048576:.1f} MB")
