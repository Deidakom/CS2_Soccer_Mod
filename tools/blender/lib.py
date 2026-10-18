# Shared helpers for the prop scripts in tools/blender/props/.
# Props are modelled in Source units (1 Blender unit = 1 game unit, Z up, +X forward) and
# exported as triangle JSON that tools/blender/build-props.mjs turns into DMX + vmdl.
#
#   blender -b --factory-startup -P tools/blender/props/<prop>.py -- <out.json>
# Material names are vmat stems under materials/soccermod_world/ (see build-props.mjs MATERIALS).
# Objects named COL_* are collision only (not rendered); without any, the prop has no collision.
import bpy, bmesh, json, math, sys
from mathutils import Vector, Matrix


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def _link(ob, material, bevel=0.0, segments=2):
    bpy.context.collection.objects.link(ob)
    if material:
        ob.data.materials.append(mat(material))
    if bevel > 0:
        m = ob.modifiers.new("bevel", "BEVEL")
        m.width, m.segments, m.limit_method = bevel, segments, "ANGLE"
    return ob


def box(material, size, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, segments=2, name="box"):
    """Axis box of size (x, y, z) whose centre is loc; rot in degrees."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    ob.location, ob.rotation_euler = loc, [math.radians(a) for a in rot]
    return _link(ob, material, bevel, segments)


def cyl(material, radius, depth, loc=(0, 0, 0), rot=(0, 0, 0), verts=24, bevel=0.0, name="cyl", radius2=None):
    """Cylinder (or cone with radius2) along Z, centred on loc."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=radius, radius2=radius if radius2 is None else radius2, depth=depth)
    bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    ob.location, ob.rotation_euler = loc, [math.radians(a) for a in rot]
    return _link(ob, material, bevel)


def sphere(material, radius, loc=(0, 0, 0), scale=(1, 1, 1), segments=16, rings=8, name="sphere"):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    ob.location, ob.scale = loc, scale
    return _link(ob, material)


def text(material, body, size, loc=(0, 0, 0), rot=(90, 0, 0), depth=1.0, align="CENTER", font=None):
    """Extruded lettering as real geometry (no texture needed). Default faces -Y."""
    cu = bpy.data.curves.new("text", "FONT")
    cu.body, cu.size, cu.extrude, cu.align_x, cu.align_y = body, size, depth / 2, align, "CENTER"
    if font:
        cu.font = bpy.data.fonts.load(font)
    ob = bpy.data.objects.new("text", cu)
    ob.location, ob.rotation_euler = loc, [math.radians(a) for a in rot]
    bpy.context.collection.objects.link(ob)
    if material:
        cu.materials.append(mat(material))
    return ob


def profile(material, pts, width, loc=(0, 0, 0), bevel=0.0, segments=3, name="profile", smooth=False):
    """Side profile [(x, z), ...] (counter-clockwise seen from -Y) extruded `width` along Y,
    centred on loc - car bodies, kiosk roofs, signs."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = [bm.verts.new((x, -width / 2, z)) for x, z in pts]
    face = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    bmesh.ops.translate(bm, vec=(0, width, 0), verts=[v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    ob.location = loc
    return _link(ob, material, bevel, segments)


def tube(material, pts, radius, closed=False, resolution=12, bevel_res=4, name="tube"):
    """Smooth round tube through 3D points (pretzels, rails, cables, hoops)."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions, cu.bevel_depth, cu.bevel_resolution, cu.resolution_u = "3D", radius, bevel_res, resolution
    cu.use_fill_caps = True
    sp = cu.splines.new("NURBS")
    sp.points.add(len(pts) - 1)
    for p, q in zip(sp.points, pts):
        p.co = (*q, 1)
    sp.use_cyclic_u, sp.use_endpoint_u, sp.order_u = closed, not closed, 4
    ob = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(ob)
    if material:
        cu.materials.append(mat(material))
    return ob


def collision(size, loc=(0, 0, 0), rot=(0, 0, 0)):
    return box(None, size, loc, rot, name="COL_box")


def array(ob, count, offset):
    """Copies of ob, each moved by offset (x, y, z) from the last; returns all of them."""
    out = [ob]
    for i in range(1, count):
        c = ob.copy(); c.data = ob.data
        c.location = ob.location + Vector(offset) * i
        bpy.context.collection.objects.link(c)
        out.append(c)
    return out


# ---- export -----------------------------------------------------------------------------------
UV_SCALE = 64.0   # world units per texture repeat (box projection)


def _box_uv(p, n):
    a = max(range(3), key=lambda k: abs(n[k]))
    if a == 0:
        u, v = p[1] * (1 if n[0] > 0 else -1), p[2]
    elif a == 1:
        u, v = -p[0] * (1 if n[1] > 0 else -1), p[2]
    else:
        u, v = p[0], p[1] * (1 if n[2] > 0 else -1)
    return [round(u / UV_SCALE, 5), round(-v / UV_SCALE, 5)]


def export(path=None, variants=None):
    """variants: {suffix: {material: replacement}} -> extra models <name>_<suffix> with the same
    geometry and those materials swapped (car colours, team colours)."""
    path = path or sys.argv[sys.argv.index("--") + 1]
    dg = bpy.context.evaluated_depsgraph_get()
    faces, col = [], []
    for ob in list(bpy.context.scene.objects):
        if ob.type not in {"MESH", "FONT", "CURVE"}:
            continue
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        mw, nm = ob.matrix_world, ob.matrix_world.to_3x3().inverted().transposed()
        is_col = ob.name.startswith("COL_")
        names = [s.material.name if s.material else "default" for s in ob.material_slots] or ["default"]
        smooth_split = me.corner_normals if hasattr(me, "corner_normals") else None
        for tri in me.loop_triangles:
            pts = [list(mw @ me.vertices[v].co) for v in tri.vertices]
            fn = (nm @ tri.normal).normalized()
            if smooth_split is not None and tri.use_smooth:
                ns = [list((nm @ smooth_split[l].vector).normalized()) for l in tri.loops]
            else:
                ns = [list(fn)] * 3
            r = lambda q: [round(x, 4) for x in q]
            f = {"pts": [r(p) for p in pts], "ns": [r(n) for n in ns], "n": r(fn)}
            if is_col:
                col.append(f)
            else:
                f["material"] = names[min(tri.material_index, len(names) - 1)]
                f["uvs"] = [_box_uv(p, fn) for p in pts]
                faces.append(f)
        ev.to_mesh_clear()
    lo = [min(p[k] for f in faces for p in f["pts"]) for k in range(3)]
    hi = [max(p[k] for f in faces for p in f["pts"]) for k in range(3)]
    with open(path, "w") as fh:
        json.dump({"faces": faces, "collision": col, "bounds": [lo, hi], "variants": variants or {}}, fh)
    print(f"EXPORT {path}: {len(faces)} tris, {len(col)} collision tris, bounds {lo} .. {hi}")
    argv = sys.argv[sys.argv.index("--") + 1:]
    if len(argv) > 1:
        preview(argv[1], lo, hi)


# Quick look: solid-shaded render from the front-left, material colours from PREVIEW_COLOURS
PREVIEW_COLOURS = {}


def preview(png, lo, hi):
    for ob in list(bpy.context.scene.objects):
        if ob.name.startswith("COL_"):
            ob.hide_render = True
    for m in bpy.data.materials:
        m.diffuse_color = (*PREVIEW_COLOURS.get(m.name, (0.6, 0.6, 0.6)), 1)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    c = Vector([(a + b) / 2 for a, b in zip(lo, hi)]); size = max(b - a for a, b in zip(lo, hi))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.collection.objects.link(cam)
    cam.location = c + Vector((-0.9, -1.6, 0.55)) * size
    cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sc.render.filepath = png
    bpy.ops.render.render(write_still=True)
    print(f"PREVIEW {png}")
