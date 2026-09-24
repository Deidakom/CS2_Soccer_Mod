"""Generate a face-only candidate from an explicitly reviewed component manifest.

Original DMX and all vertex streams, rigging and UVs are preserved. The source
hash pins component numbering to the exact inspected file. Never overwrites.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re


def _face_exclusions(components, manifest):
    """Return face-set-local indices removed by explicit geometry rules.

    Component filtering removes disconnected gear objects.  Some stock gear,
    such as the integrated waistband, is welded into the clothing component,
    so it needs a second, hash-pinned face-level rule.  Rules operate on the
    inspected triangle geometry and never alter vertices, UVs, weights or
    other DMX data.
    """
    exclusions = {}
    kept = set(manifest["keep_components"])
    for rule in manifest.get("remove_face_rules", []):
        component_id = int(rule["component_id"])
        if component_id not in kept:
            raise ValueError("face-removal rule must target a kept component")
        matches = [c for c in components if c["id"] == component_id]
        if len(matches) != 1:
            raise ValueError(f"face-removal rule targets unknown component {component_id}")
        component = matches[0]
        lower = rule.get("centroid_z_min")
        upper = rule.get("centroid_z_max")
        if lower is None and upper is None:
            raise ValueError("face-removal rule needs a centroid z bound")
        selected = set()
        for face_index, triangle in zip(component["face_indices"], component["triangles"]):
            centroid_z = float(triangle[:, 2].mean())
            if lower is not None and centroid_z < float(lower):
                continue
            if upper is not None and centroid_z > float(upper):
                continue
            selected.add(int(face_index))
        if not selected:
            raise ValueError(f"face-removal rule selected no faces for component {component_id}")
        face_set = component["face_set"]
        exclusions.setdefault(face_set, set()).update(selected)
    return exclusions

spec = importlib.util.spec_from_file_location("inspection", Path(__file__).with_name("inspect-kit-mesh.py"))
inspection = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspection)


def filter_mesh(source, manifest, output):
    if output.exists() or source.resolve() == output.resolve():
        raise ValueError("Refusing to overwrite an existing file")
    raw = source.read_bytes()
    if hashlib.sha256(raw).hexdigest() != manifest["source_sha256"]:
        raise ValueError("Source hash differs from the reviewed mesh")
    components = inspection.inspect(source)
    keep = set(manifest["keep_components"])
    if not keep or not keep.issubset({c["id"] for c in components}):
        raise ValueError("Invalid keep list")
    exclusions = _face_exclusions(components, manifest)
    selections = {}
    for c in components:
        selections.setdefault(c["face_set"], set())
        if c["id"] in keep:
            selections[c["face_set"]].update(c["face_indices"])
    text = raw.decode("utf-8").replace("\r\n", "\n")
    changed = 0

    def replace_block(match):
        nonlocal changed
        block = match[0]
        uid = re.search(r'"id"\s+"elementid"\s+"([^"]+)"', block)[1]
        if uid not in selections:
            return block
        faces = inspection.array(block, "faces")
        if len(faces) % 4 or any(faces[i] != "-1" for i in range(3, len(faces), 4)):
            raise ValueError("Expected triangle face records")
        selected = selections[uid] - exclusions.get(uid, set())
        values = [v for i in sorted(selected) for v in faces[i * 4:i * 4 + 4]]
        changed += (len(faces) - len(values)) // 4
        return re.sub(r'("faces"\s+"int_array"\s*\[).*?(\])',
                      lambda m: m[1] + "\n" + ",\n".join('"' + v + '"' for v in values) + "\n" + m[2],
                      block, flags=re.S)

    result = re.sub(r'^"DmeFaceSet"\s*\n\{\n.*?^\}', replace_block, text, flags=re.M | re.S)
    strip_faces = lambda s: re.sub(r'("faces"\s+"int_array"\s*\[).*?(\])', r'\1\2', s, flags=re.S)
    if strip_faces(text) != strip_faces(result) or changed <= 0:
        raise ValueError("Expected only nonempty face removal")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(result, encoding="utf-8", newline="\n")
    retained = sum(c["face_count"] for c in components if c["id"] in keep) - sum(len(indices) for indices in exclusions.values())
    return {"removed_triangles": changed, "retained_triangles": retained,
            "source_sha256": manifest["source_sha256"], "output_sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
            "unchanged_non_face_data": True}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    print(json.dumps(filter_mesh(args.source, json.loads(args.manifest.read_text()), args.output), indent=2))
