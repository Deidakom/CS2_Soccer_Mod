# Offline renders of the arena (CS2 only draws in the foreground): stadium + world shells + placed
# props from tools/arena/export-scene.mjs, flat material colours, sun light, Eevee.
#   blender -b --factory-startup -P tools/blender/render-map.py -- <scene.json> <out dir> <shots.json>
# shots.json: [{"name": "tunnel", "pos": [x, y, z], "look": [x, y, z], "lens": 18}, ...]
import bpy, json, math, os, sys
from mathutils import Vector, Matrix

scene_file, out_dir, shots_file = sys.argv[sys.argv.index("--") + 1:][:3]
S = json.load(open(scene_file))
shots = json.load(open(shots_file))
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene


def material(name, rgb):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*[c ** 2.2 for c in rgb], 1)
    b.inputs["Roughness"].default_value = 0.7
    if any(k in name for k in ("light", "letters", "glow", "strip_", "led", "illum", "videowall", "portal")):
        b.inputs["Emission Color"].default_value = (*[c ** 2.2 for c in rgb], 1)
        b.inputs["Emission Strength"].default_value = 3
    return m


def mesh_from(name, tris_by_mat, colours):
    verts, faces, mats, mat_index = [], [], [], []
    for mname, flat in tris_by_mat.items():
        mats.append(material(mname, colours.get(mname, (0.5, 0.5, 0.5))))
        mi = len(mats) - 1
        for i in range(0, len(flat), 9):
            base = len(verts)
            verts += [tuple(flat[i:i + 3]), tuple(flat[i + 3:i + 6]), tuple(flat[i + 6:i + 9])]
            faces.append((base, base + 1, base + 2)); mat_index.append(mi)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", mat_index)
    me.update()
    return me


# stadium and world shells, plus a plain green pitch and ground
groups = {k.split("/")[-1].replace(".vmat", ""): v for k, v in S["groups"].items()}
colours = {k.split("/")[-1].replace(".vmat", ""): v for k, v in S["colour"].items()}
ob = bpy.data.objects.new("arena", mesh_from("arena", groups, colours)); sc.collection.objects.link(ob)
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, -32.2)); p = bpy.context.object; p.scale = (2560, 3328, 1)
p.data.materials.append(material("pitch_preview", (0.2, 0.45, 0.18)))

# props: one mesh per model (variants = material swaps), linked copies placed with origin / yaw
cache, pc = {}, S["propColour"]
jsons = {f[:-5]: json.load(open(os.path.join(S["propsDir"], f))) for f in os.listdir(S["propsDir"]) if f.endswith(".json")}
def prop_mesh(model):
    if model in cache:
        return cache[model]
    base, swap = model, {}
    if model not in jsons:
        for b, j in jsons.items():
            for suf, sw in j.get("variants", {}).items():
                if model == f"{b}_{suf}":
                    base, swap = b, sw
    j = jsons[base]
    by = {}
    for f in j["faces"]:
        m = swap.get(f["material"], f["material"])
        by.setdefault(m, []).extend([c for p in f["pts"] for c in p])
    cache[model] = mesh_from(model, by, pc)
    return cache[model]
for p in S["placed"]:
    o = bpy.data.objects.new(p["model"], prop_mesh(p["model"]))
    o.location = p["origin"]; o.rotation_euler = (0, 0, math.radians(p["yaw"]))
    sc.collection.objects.link(o)

# light: sun 45/45 like the map, sky-ish world
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 4; sun.rotation_euler = (math.radians(45), 0, math.radians(45))
sc.world = bpy.data.worlds.new("w"); sc.world.use_nodes = True
sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.7, 0.9, 1)
sc.world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1280, 720
sc.view_settings.view_transform = "Standard"
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.clip_start, cam.data.clip_end = 2, 30000
for s in shots:
    cam.location = Vector(s["pos"]); cam.data.lens = s.get("lens", 18)
    cam.rotation_euler = (Vector(s["look"]) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = os.path.join(out_dir, s["name"] + ".png")
    bpy.ops.render.render(write_still=True)
    print("SHOT", s["name"])
