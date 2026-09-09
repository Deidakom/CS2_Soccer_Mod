"""Read-only topology inspection of Valve-converted keyvalues2 model DMX.

Never edits the input. Reports connected face components with welded position
seams; optional orthographic plots help distinguish gear from the body.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

import numpy as np


def array(text, name):
    found = re.search(r'"' + re.escape(name) + r'"\s+"\w+_array"\s*\[(.*?)\]', text, re.S)
    if found is None:
        raise ValueError(f"Missing DMX array: {name}")
    return re.findall(r'"([^"]*)"', found[1])


def inspect(path):
    text = path.read_text(encoding="utf-8")
    if not text.startswith("<!-- dmx encoding keyvalues2_flat "):
        raise ValueError("Convert to keyvalues2_flat with dmxconvert and an explicit output path first")
    blocks = {}
    for match in re.finditer(r'^"([^"\n]+)"\s*\n\{\n(.*?)^\}', text, re.M | re.S):
        uid = re.search(r'"id"\s+"elementid"\s+"([^"]+)"', match[2])[1]
        blocks[uid] = (match[1], match[2])

    def ref(block, name):
        return re.search(r'"' + name + r'"\s+"element"\s+"([^"]*)"', block)[1]

    face_sets = []
    for kind, mesh in blocks.values():
        if kind != "DmeMesh":
            continue
        state = blocks[ref(mesh, "currentState")][1]
        for face_id in array(mesh, "faceSets"):
            if face_id == "element":
                continue
            block = blocks[face_id][1]
            material_block = blocks[ref(block, "material")][1]
            material = re.search(r'"mtlName"\s+"string"\s+"([^"]+)"', material_block)[1]
            face_sets.append((face_id, block, material, state))
    components = []
    for face_id, block, material, state in face_sets:
        pos = np.array([[float(v) for v in row.split()] for row in array(state, "position$0")])
        indices = np.array(array(state, "position$0Indices"), dtype=int)
        _, welded = np.unique(np.round(pos, 5), axis=0, return_inverse=True)
        faces, face = [], []
        for value in array(block, "faces"):
            index = int(value)
            if index == -1:
                if len(face) != 3:
                    raise ValueError("Only triangular inspection is supported")
                faces.append(face)
                face = []
            else:
                face.append(index)
        if face:
            raise ValueError("Unterminated face")
        if not faces:
            continue
        faces = np.array(faces, dtype=int)
        parent = list(range(len(faces)))

        def root(n):
            while parent[n] != n:
                parent[n] = parent[parent[n]]
                n = parent[n]
            return n

        seen = {}
        for i, corners in enumerate(welded[indices[faces]]):
            for corner in corners:
                corner = int(corner)
                if corner in seen:
                    parent[root(i)] = root(seen[corner])
                else:
                    seen[corner] = i
        groups = {}
        for i in range(len(faces)):
            groups.setdefault(root(i), []).append(i)
        for selected in sorted(groups.values(), key=len, reverse=True):
            triangles = pos[indices[faces[selected]]]
            verts = triangles.reshape(-1, 3)
            uv = np.array([[float(v) for v in row.split()] for row in array(state, "texcoord$0")])
            uv_indices = np.array(array(state, "texcoord$0Indices"), dtype=int)
            # Order-independent position/UV signature, for checking whether a
            # retained clothing surface actually shares an atlas with another base.
            corners = np.concatenate((triangles, uv[uv_indices[faces[selected]]]), axis=2).reshape(-1, 5)
            canonical = sorted(tuple(row) for row in np.round(corners, 5))
            components.append({
                "id": len(components), "material": material, "face_set": face_id,
                "face_count": len(selected), "face_indices": selected,
                "bounds_min": verts.min(axis=0).tolist(),
                "bounds_max": verts.max(axis=0).tolist(),
                "position_uv_sha256": hashlib.sha256(repr(canonical).encode()).hexdigest(),
                "uv_sha256": hashlib.sha256(repr(sorted(tuple(row) for row in np.round(corners[:, 3:], 5))).encode()).hexdigest(),
                "triangles": triangles,
                "uv_triangles": uv[uv_indices[faces[selected]]],
            })
    return components


def render(components, path, textures=None):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection
    fig, axes = plt.subplots(1, 4, figsize=(16, 9), layout="constrained")
    colors = plt.get_cmap("tab20")
    for ax, (horizontal, depth, reverse, title) in zip(axes, [
        (1, 0, False, "Front / X+"), (1, 0, True, "Back / X-"),
        (0, 1, False, "Side / Y+"), (0, 1, True, "Side / Y-"),
    ]):
        all_tri, all_color = [], []
        for component in components:
            tri = component["triangles"]
            all_tri.extend(tri)
            texture = (textures or {}).get(component["material"])
            if texture is not None:
                from PIL import Image
                pixels = np.asarray(Image.open(texture).convert("RGB")) / 255.0
                uv = component["uv_triangles"].mean(axis=1) % 1
                xy = (uv * [pixels.shape[1], pixels.shape[0]]).astype(int)
                all_color.extend(pixels[xy[:, 1], xy[:, 0]])
            elif textures:
                all_color.extend([[0.65, 0.48, 0.36]] * len(tri))
            else:
                all_color.extend([colors(component["id"] % 20)] * len(tri))
        tri = np.array(all_tri)
        order = np.argsort(tri[:, :, depth].mean(axis=1))
        if reverse:
            order = order[::-1]
        projected = tri[order][:, :, [horizontal, 2]]
        ax.add_collection(PolyCollection(projected, facecolors=np.array(all_color)[order], edgecolors="none"))
        for component in components:
            if textures:
                continue
            if component["face_count"] < 20:
                continue
            centre = (np.array(component["bounds_min"]) + component["bounds_max"]) / 2
            ax.text(centre[horizontal], centre[2], str(component["id"]), fontsize=7,
                    ha="center", color="black", bbox={"facecolor": "white", "alpha": .6, "pad": .3})
        ax.autoscale_view()
        ax.set_aspect("equal")
        ax.set_title(title)
    fig.savefig(path, dpi=130)
    plt.close(fig)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("report", type=Path)
    parser.add_argument("--render", type=Path)
    parser.add_argument("--textures", type=Path, help="Optional material-to-color-image JSON for diagnostic per-face color sampling")
    args = parser.parse_args()
    components = inspect(args.input)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps([
        {k: v for k, v in c.items() if k not in ("triangles", "uv_triangles")} for c in components
    ], indent=2))
    for c in components:
        if c["face_count"] >= 20:
            print(c["id"], c["face_count"], Path(c["material"]).name,
                  np.round(c["bounds_min"], 2), np.round(c["bounds_max"], 2))
    if args.render:
        render(components, args.render, json.loads(args.textures.read_text()) if args.textures else None)


if __name__ == "__main__":
    main()
