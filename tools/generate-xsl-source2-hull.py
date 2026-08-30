#!/usr/bin/env python3
"""Generate a Source 2 physics-hull model from XSL B1's VPhysics collision.

The Source 1 BSP remains the source of truth.  The required debug-mesh input
must come from VPhysicsCollision007/CreateDebugMesh, so the generated hull
preserves the facets that influence the original ball's low-speed behaviour.
The BSP render mesh is retained only as a diagnostic comparison.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import uuid
from dataclasses import dataclass
from pathlib import Path


HEADER_SIZE = 4 + 4 + (64 * 16) + 4
LUMP_VERTEXES = 3
LUMP_FACES = 7
LUMP_EDGES = 12
LUMP_SURFEDGES = 13
LUMP_MODELS = 14
LUMP_PHYSCOLLIDE = 29

VERTEX = struct.Struct("<3f")
FACE = struct.Struct("<HBBihhhh4Bif5iHHI")
EDGE = struct.Struct("<2H")
SURFEDGE = struct.Struct("<i")
MODEL = struct.Struct("<9f3i")
PHYSICS_HEADER = struct.Struct("<4i")

MODEL_DOC_HEADER = (
    "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} "
    "format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->"
)
DMX_HEADER = "<!-- dmx encoding keyvalues2 4 format model 22 -->"
ID_NAMESPACE = uuid.UUID("c4782384-5f92-4f69-b34c-d6492432bce4")


@dataclass(frozen=True)
class Lump:
    offset: int
    length: int


class Bsp:
    def __init__(self, path: Path):
        self.path = path
        self.raw = path.read_bytes()
        if len(self.raw) < HEADER_SIZE or self.raw[:4] != b"VBSP":
            raise ValueError(f"{path} is not a Source BSP")

        self.version = struct.unpack_from("<i", self.raw, 4)[0]
        self.lumps: list[Lump] = []
        for lump_id in range(64):
            offset, length = struct.unpack_from("<ii", self.raw, 8 + lump_id * 16)
            self.lumps.append(Lump(offset, length))

    def records(self, lump_id: int, record: struct.Struct) -> list[tuple]:
        lump = self.lumps[lump_id]
        data = self.raw[lump.offset : lump.offset + lump.length]
        if len(data) % record.size:
            raise ValueError(
                f"BSP lump {lump_id} length {len(data)} is not divisible by "
                f"record size {record.size}"
            )
        return list(record.iter_unpack(data))

    def lump(self, lump_id: int) -> bytes:
        lump = self.lumps[lump_id]
        return self.raw[lump.offset : lump.offset + lump.length]


def extract_physics_blob(bsp: Bsp, model_index: int) -> tuple[bytes, bytes, int]:
    data = bsp.lump(LUMP_PHYSCOLLIDE)
    offset = 0
    while offset + PHYSICS_HEADER.size <= len(data):
        record_model, data_size, keydata_size, solid_count = PHYSICS_HEADER.unpack_from(
            data, offset
        )
        if record_model == -1:
            break
        if data_size < 0 or keydata_size < 0:
            raise ValueError("physics-collision record has a negative size")
        payload_start = offset + PHYSICS_HEADER.size
        data_end = payload_start + data_size
        record_end = data_end + keydata_size
        if record_end > len(data):
            raise ValueError("physics-collision record extends past its lump")
        if record_model == model_index:
            return data[payload_start:data_end], data[data_end:record_end], solid_count
        offset = record_end
    raise ValueError(f"inline model *{model_index} has no physics-collision record")


def stable_id(label: str) -> str:
    return str(uuid.uuid5(ID_NAMESPACE, label))


def extract_model_mesh(
    bsp: Bsp, model_index: int
) -> tuple[list[tuple[float, float, float]], list[list[int]], dict[str, object]]:
    vertices = bsp.records(LUMP_VERTEXES, VERTEX)
    faces = bsp.records(LUMP_FACES, FACE)
    edges = bsp.records(LUMP_EDGES, EDGE)
    surfedges = bsp.records(LUMP_SURFEDGES, SURFEDGE)
    models = bsp.records(LUMP_MODELS, MODEL)

    if model_index < 0 or model_index >= len(models):
        raise ValueError(f"inline model *{model_index} does not exist")

    model = models[model_index]
    first_face = model[10]
    face_count = model[11]
    if first_face < 0 or first_face + face_count > len(faces):
        raise ValueError(f"inline model *{model_index} has an invalid face range")

    global_polygons: list[list[int]] = []
    used_vertices: set[int] = set()
    for face_index in range(first_face, first_face + face_count):
        face = faces[face_index]
        first_edge = face[3]
        edge_count = face[4]
        if edge_count < 3:
            continue

        polygon: list[int] = []
        for surfedge_index in range(first_edge, first_edge + edge_count):
            signed_edge = surfedges[surfedge_index][0]
            edge_index = abs(signed_edge)
            if edge_index >= len(edges):
                raise ValueError(f"face {face_index} references invalid edge {edge_index}")
            edge = edges[edge_index]
            vertex_index = edge[0] if signed_edge >= 0 else edge[1]
            if not polygon or polygon[-1] != vertex_index:
                polygon.append(vertex_index)

        if len(polygon) > 2 and polygon[0] == polygon[-1]:
            polygon.pop()
        if len(set(polygon)) < 3:
            continue
        global_polygons.append(polygon)
        used_vertices.update(polygon)

    ordered_global_vertices = sorted(used_vertices)
    vertex_map = {
        global_index: local_index
        for local_index, global_index in enumerate(ordered_global_vertices)
    }
    local_vertices = [vertices[index] for index in ordered_global_vertices]
    local_polygons = [
        [vertex_map[index] for index in polygon] for polygon in global_polygons
    ]

    mins = tuple(min(vertex[axis] for vertex in local_vertices) for axis in range(3))
    maxs = tuple(max(vertex[axis] for vertex in local_vertices) for axis in range(3))
    dimensions = tuple(maxs[axis] - mins[axis] for axis in range(3))
    signed_volume = 0.0
    for polygon in local_polygons:
        p0 = local_vertices[polygon[0]]
        for index in range(1, len(polygon) - 1):
            p1 = local_vertices[polygon[index]]
            p2 = local_vertices[polygon[index + 1]]
            cross = (
                p1[1] * p2[2] - p1[2] * p2[1],
                p1[2] * p2[0] - p1[0] * p2[2],
                p1[0] * p2[1] - p1[1] * p2[0],
            )
            signed_volume += (
                p0[0] * cross[0] + p0[1] * cross[1] + p0[2] * cross[2]
            ) / 6.0

    report: dict[str, object] = {
        "bspVersion": bsp.version,
        "modelIndex": model_index,
        "sourceModelMins": list(model[0:3]),
        "sourceModelMaxs": list(model[3:6]),
        "meshMins": list(mins),
        "meshMaxs": list(maxs),
        "meshDimensions": list(dimensions),
        "vertexCount": len(local_vertices),
        "faceCount": len(local_polygons),
        "triangulatedFaceCount": sum(len(face) - 2 for face in local_polygons),
        "signedMeshVolume": signed_volume,
        "absoluteMeshVolume": abs(signed_volume),
    }
    return local_vertices, local_polygons, report


def mesh_metrics(
    vertices: list[tuple[float, float, float]], polygons: list[list[int]]
) -> dict[str, object]:
    if not vertices or not polygons:
        raise ValueError("physics debug mesh is empty")
    mins = tuple(min(vertex[axis] for vertex in vertices) for axis in range(3))
    maxs = tuple(max(vertex[axis] for vertex in vertices) for axis in range(3))
    dimensions = tuple(maxs[axis] - mins[axis] for axis in range(3))
    signed_volume = 0.0
    for polygon in polygons:
        if len(polygon) < 3 or any(index < 0 or index >= len(vertices) for index in polygon):
            raise ValueError("physics debug mesh contains an invalid face")
        p0 = vertices[polygon[0]]
        for index in range(1, len(polygon) - 1):
            p1 = vertices[polygon[index]]
            p2 = vertices[polygon[index + 1]]
            cross = (
                p1[1] * p2[2] - p1[2] * p2[1],
                p1[2] * p2[0] - p1[0] * p2[2],
                p1[0] * p2[1] - p1[1] * p2[0],
            )
            signed_volume += (
                p0[0] * cross[0] + p0[1] * cross[1] + p0[2] * cross[2]
            ) / 6.0
    return {
        "mins": list(mins),
        "maxs": list(maxs),
        "dimensions": list(dimensions),
        "vertexCount": len(vertices),
        "faceCount": len(polygons),
        "triangulatedFaceCount": sum(len(face) - 2 for face in polygons),
        "signedMeshVolume": signed_volume,
        "absoluteMeshVolume": abs(signed_volume),
    }


def quote_lines(values: list[str], indent: str) -> str:
    return ",\n".join(f'{indent}"{value}"' for value in values)


def render_hull_dmx(
    vertices: list[tuple[float, float, float]], polygons: list[list[int]]
) -> str:
    ids = {name: stable_id(name) for name in (
        "root", "model", "model-transform", "mesh-dag", "mesh-transform",
        "mesh", "bind", "faces", "material", "base-transforms",
        "base-transform", "axis", "export-tags",
    )}
    face_values = [str(index) for polygon in polygons for index in (*polygon, -1)]
    vertex_values = ["{:.9g} {:.9g} {:.9g}".format(*vertex) for vertex in vertices]
    index_values = [str(index) for index in range(len(vertices))]

    return f'''{DMX_HEADER}
"DmElement"
{{
    "id" "elementid" "{ids['root']}"
    "name" "string" "root"
    "skeleton" "element" "{ids['model']}"
    "model" "element" "{ids['model']}"
    "exportTags" "DmeExportTags"
    {{
        "id" "elementid" "{ids['export-tags']}"
        "name" "string" "exportTags"
        "source" "string" "Generated from the compiled XSL B1 inline ball by cs2-soccermod"
    }}
}}

"DmeModel"
{{
    "id" "elementid" "{ids['model']}"
    "transform" "DmeTransform"
    {{
        "id" "elementid" "{ids['model-transform']}"
        "position" "vector3" "0 0 0"
        "orientation" "quaternion" "0 0 0 1"
    }}
    "shape" "element" ""
    "visible" "bool" "1"
    "children" "element_array"
    [
        "element" "{ids['mesh-dag']}"
    ]
    "jointList" "element_array"
    [
        "element" "{ids['mesh-dag']}"
    ]
    "baseStates" "element_array"
    [
        "DmeTransformsList"
        {{
            "id" "elementid" "{ids['base-transforms']}"
            "transforms" "element_array"
            [
                "DmeTransform"
                {{
                    "id" "elementid" "{ids['base-transform']}"
                    "position" "vector3" "0 0 0"
                    "orientation" "quaternion" "0 0 0 1"
                }}
            ]
        }}
    ]
    "axisSystem" "DmeAxisSystem"
    {{
        "id" "elementid" "{ids['axis']}"
        "upAxis" "int" "3"
        "forwardParity" "int" "1"
        "coordSys" "int" "0"
    }}
}}

"DmeDag"
{{
    "id" "elementid" "{ids['mesh-dag']}"
    "transform" "DmeTransform"
    {{
        "id" "elementid" "{ids['mesh-transform']}"
        "position" "vector3" "0 0 0"
        "orientation" "quaternion" "0 0 0 1"
    }}
    "shape" "DmeMesh"
    {{
        "id" "elementid" "{ids['mesh']}"
        "bindState" "element" ""
        "currentState" "element" "{ids['bind']}"
        "baseStates" "element_array"
        [
            "element" "{ids['bind']}"
        ]
        "deltaStates" "element_array"
        [
        ]
        "faceSets" "element_array"
        [
            "DmeFaceSet"
            {{
                "id" "elementid" "{ids['faces']}"
                "name" "string" "xsl ball hull faces"
                "faces" "int_array"
                [
{quote_lines(face_values, '                    ')}
                ]
                "material" "DmeMaterial"
                {{
                    "id" "elementid" "{ids['material']}"
                    "name" "string" "material"
                    "mtlName" "string" "$glass"
                }}
            }}
        ]
        "deltaStateWeights" "vector2_array"
        [
        ]
        "deltaStateWeightsLagged" "vector2_array"
        [
        ]
        "visible" "bool" "1"
    }}
    "visible" "bool" "1"
    "children" "element_array"
    [
    ]
}}

"DmeVertexData"
{{
    "id" "elementid" "{ids['bind']}"
    "name" "string" "bind"
    "vertexFormat" "string_array"
    [
        "position$0"
    ]
    "jointCount" "int" "0"
    "flipVCoordinates" "bool" "0"
    "position$0" "vector3_array"
    [
{quote_lines(vertex_values, '        ')}
    ]
    "position$0Indices" "int_array"
    [
{quote_lines(index_values, '        ')}
    ]
}}
'''


def render_model_doc(hull_resource_path: str) -> str:
    return f'''{MODEL_DOC_HEADER}
{{
    rootNode =
    {{
        _class = "RootNode"
        children =
        [
            {{
                _class = "BoneMarkupList"
                children = [ ]
                bone_cull_type = "None"
            }},
            {{
                _class = "PhysicsShapeList"
                children =
                [
                    {{
                        _class = "PhysicsHullFile"
                        filename = "{hull_resource_path}"
                        parent_bone = ""
                        surface_prop = "glass"
                        collision_tags = ""
                        name = "xsl_b1_exact_hull"
                        faceMergeAngle = 0.0
                        maxHullVertices = 256
                        optimization_algorithm = "Exact"
                    }},
                ]
            }},
        ]
    }}
}}
'''


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_bsp", type=Path)
    parser.add_argument("output_directory", type=Path)
    parser.add_argument("--model-index", type=int, default=106)
    parser.add_argument(
        "--physics-debug-mesh",
        type=Path,
        required=True,
        help=(
            "JSON exported from the Source 1 CPhysCollide through "
            "VPhysicsCollision007/CreateDebugMesh"
        ),
    )
    parser.add_argument(
        "--resource-prefix",
        default="models/soccermod",
        help="Source 2 content-relative directory used inside the generated VMDL",
    )
    args = parser.parse_args()

    bsp = Bsp(args.source_bsp)
    render_vertices, render_polygons, render_report = extract_model_mesh(
        bsp, args.model_index
    )
    source1_physics, source1_keydata, solid_count = extract_physics_blob(
        bsp, args.model_index
    )
    debug_mesh = json.loads(args.physics_debug_mesh.read_text(encoding="utf-8-sig"))
    if debug_mesh.get("schema") != "cs2-soccermod.source1-vphysics-debug-mesh/1":
        raise ValueError("unsupported Source 1 physics-debug-mesh schema")
    vertices = [tuple(float(value) for value in vertex) for vertex in debug_mesh["vertices"]]
    polygons = [[int(value) for value in face] for face in debug_mesh["triangles"]]
    exact_metrics = mesh_metrics(vertices, polygons)
    engine_volume = float(debug_mesh["volume"])
    mesh_volume = float(exact_metrics["absoluteMeshVolume"])
    if abs(mesh_volume - engine_volume) > 0.05:
        raise ValueError(
            "debug triangle mesh volume does not reproduce CollideVolume: "
            f"mesh={mesh_volume:.6f}, engine={engine_volume:.6f}"
        )
    args.output_directory.mkdir(parents=True, exist_ok=True)

    hull_path = args.output_directory / "xsl_b1_ball_hull.dmx"
    model_path = args.output_directory / "xsl_b1_ball_physics.vmdl"
    report_path = args.output_directory / "xsl_b1_ball_geometry.json"
    source1_physics_path = args.output_directory / "xsl_b1_ball_source1.vphys.bin"
    source1_vcollide_path = args.output_directory / "xsl_b1_ball_source1.vcollide.bin"
    # Source 1's CreateDebugMesh triangles have clockwise/inward winding when
    # interpreted by ModelDoc's DMX importer (their signed volume is negative).
    # Keep the extracted indices unchanged in the audit report, but reverse
    # each face for Source 2 so ResourceCompiler sees an outward closed hull.
    source2_polygons = [polygon[:1] + list(reversed(polygon[1:])) for polygon in polygons]
    if float(mesh_metrics(vertices, source2_polygons)["signedMeshVolume"]) <= 0.0:
        raise ValueError("Source 2 hull winding is not outward")
    hull_text = render_hull_dmx(vertices, source2_polygons)
    model_text = render_model_doc(
        f"{args.resource_prefix.rstrip('/')}/xsl_b1_ball_hull.dmx"
    )
    hull_path.write_text(hull_text, encoding="utf-8", newline="\n")
    model_path.write_text(model_text, encoding="utf-8", newline="\n")
    source1_physics_path.write_bytes(source1_physics)
    source1_vcollide_path.write_bytes(source1_physics + source1_keydata)

    report: dict[str, object] = {
        "sourceBsp": str(args.source_bsp.resolve()),
        "sourceBspSha256": hashlib.sha256(args.source_bsp.read_bytes()).hexdigest(),
        "sourcePhysicsDebugMesh": str(args.physics_debug_mesh.resolve()),
        "sourcePhysicsDebugMeshSha256": hashlib.sha256(
            args.physics_debug_mesh.read_bytes()
        ).hexdigest(),
        "source1PhysicsMesh": exact_metrics,
        "source1CollideVolume": engine_volume,
        "source1CollideSurfaceArea": float(debug_mesh["surfaceArea"]),
        "source1CollideMins": debug_mesh["mins"],
        "source1CollideMaxs": debug_mesh["maxs"],
        "source1CollideMassCenter": debug_mesh["massCenter"],
        "sourceRenderMeshDiagnosticOnly": render_report,
        "hullDmxSha256": hashlib.sha256(hull_text.encode("utf-8")).hexdigest(),
        "source2HullWinding": "outward (Source 1 debug triangles reversed)",
        "physicsSurface": "glass",
        "source1PhysicsSolidCount": solid_count,
        "source1PhysicsDataSize": len(source1_physics),
        "source1PhysicsKeydataSize": len(source1_keydata),
        "source1PhysicsDataSha256": hashlib.sha256(source1_physics).hexdigest(),
        "source1PhysicsKeydataSha256": hashlib.sha256(source1_keydata).hexdigest(),
        "source1PhysicsKeydata": source1_keydata.decode("ascii", errors="replace"),
        "generatedHull": str(hull_path.resolve()),
        "generatedModel": str(model_path.resolve()),
        "extractedSource1Physics": str(source1_physics_path.resolve()),
        "extractedSource1VCollide": str(source1_vcollide_path.resolve()),
    }
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
