"""Team Ward model for DrakesServerTeams, built from the vanilla guard stone (exported by the owner as guard_stone.glb).

The ward is exported as SEVERAL named parts so the mod can animate them:
  stone_base    the stepped hex plinth                                      (stone material)
(the original guard stone, stone_body + stone_runes, is exported separately as figure_stone.glb: it is the default "figure" in the middle)
  shard_0..3    four big crystals, origin = their base pivot               (stone material body, glow material tip)
  halo_0..5     six small floating diamonds, origin = their own centre     (glow material)
  rune_0..11    twelve rune glyph segments + ring arcs on the plinth top   (glow material), 30 degrees each, rune_0 at 12 o'clock
The mod turns the shards out and the halo on when the ward is on, tucks them in and hides the halo when it is off, tints the
shards with the team color, lights the runes, and sweeps a capture color around the rune ring.

Run in a live Blender (MCP):  sys.argv = ["x", "--", <source guard_stone.glb>, <out_dir>]  then exec this file.
Headless:  blender -b --factory-startup -P make_team_ward.py -- <source guard_stone.glb> <out_dir>
Writes team_ward.glb (the base), figure_stone.glb (the original stone) and team_ward.blend. Materials keep the game's names (Guardstone_Oden_mat / Guardstone_OdenGlow_mat)
so the mod can put the game's own materials on them. Blender Z = up, origin at the bottom centre of the stone.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = args[0] if args else r"C:\Users\house\Documents\guard_stone.glb"
OUT = args[1] if len(args) > 1 else os.path.dirname(os.path.abspath(globals().get("__file__", "x.py")))
os.makedirs(OUT, exist_ok=True)

D = bpy.data
for o in list(D.objects):
    D.objects.remove(o)
for m in list(D.meshes):
    D.meshes.remove(m)
for m in list(D.materials):
    D.materials.remove(m)
for i in list(D.images):
    D.images.remove(i)

bpy.ops.import_scene.gltf(filepath=SRC)
imported = [o for o in D.objects if o.type == "MESH"]
body = next(o for o in imported if any("Oden_mat" in (s.material.name if s.material else "") for s in o.material_slots))
glow = next(o for o in imported if o is not body)
BODY_MAT = next(s.material for s in body.material_slots if s.material)
GLOW_MAT = next(s.material for s in glow.material_slots if s.material)
BODY_MAT.name, GLOW_MAT.name = "Guardstone_Oden_mat", "Guardstone_OdenGlow_mat"
body.name, glow.name = "stone_body", "stone_runes"
body.data.name, glow.data.name = "stone_body", "stone_runes"

STONE, GLOWSLOT = 0, 1


class Builder:
    """One part under construction. Slot 0 is the stone material, slot 1 the glow material."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def _uv(self, face):
        # park new faces on a calm patch of the rock atlas; the mod tints them anyway
        for l in face.loops:
            l[self.uv].uv = (0.66 + 0.08 * ((l.vert.co.x * 1.7) % 1.0), 0.48 + 0.08 * ((l.vert.co.z * 1.3) % 1.0))

    def add(self, faces, slot):
        for f in faces:
            f.material_index = slot
            self._uv(f)

    def crystal(self, center, axis, up, down, radius, sides, glow_tip, twist=0.0):
        """A faceted bipyramid; the top share `glow_tip` of the upper cone is glow material."""
        bm = self.bm
        axis = Vector(axis).normalized()
        ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        a = axis.cross(ref).normalized()
        b = axis.cross(a).normalized()
        c = Vector(center)

        def ring(f, r):
            return [bm.verts.new(c + axis * (up * f) + (a * math.cos(twist + k * math.tau / sides) + b * math.sin(twist + k * math.tau / sides)) * r)
                    for k in range(sides)]

        mid = ring(0.0, radius)
        top = bm.verts.new(c + axis * up)
        low = bm.verts.new(c - axis * down)
        body_faces, glow_faces = [], []
        if glow_tip > 0:
            f = 1.0 - glow_tip
            cut = ring(f, radius * (1.0 - f))
            for k in range(sides):
                n = (k + 1) % sides
                body_faces.append(bm.faces.new([mid[k], mid[n], cut[n], cut[k]]))
                glow_faces.append(bm.faces.new([cut[k], cut[n], top]))
        else:
            for k in range(sides):
                body_faces.append(bm.faces.new([mid[k], mid[(k + 1) % sides], top]))
        for k in range(sides):
            body_faces.append(bm.faces.new([mid[(k + 1) % sides], mid[k], low]))
        self.add(body_faces, STONE)
        self.add(glow_faces, GLOWSLOT)

    def hex_step(self, radius, z0, height, taper=0.96):
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=6, radius1=radius, radius2=radius * taper, depth=height)
        for v in r["verts"]:
            v.co = Vector((v.co.x, v.co.y, v.co.z + z0 + height / 2))
        faces = list({f for v in r["verts"] for f in v.link_faces})
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self.add(faces, STONE)

    def flat_strip(self, p0, p1, width, z):
        """A flat glowing line on the plinth top, facing up."""
        d = Vector((p1[0] - p0[0], p1[1] - p0[1], 0))
        if d.length < 1e-6:
            return
        n = Vector((-d.y, d.x, 0)).normalized() * (width / 2)
        vs = [self.bm.verts.new((p0[0] + n.x, p0[1] + n.y, z)), self.bm.verts.new((p1[0] + n.x, p1[1] + n.y, z)),
              self.bm.verts.new((p1[0] - n.x, p1[1] - n.y, z)), self.bm.verts.new((p0[0] - n.x, p0[1] - n.y, z))]
        f = self.bm.faces.new(vs)
        if f.normal.z < 0:
            f.normal_flip()
        self.add([f], GLOWSLOT)

    def finish(self, location=(0, 0, 0), flat=True):
        me = D.meshes.new(self.name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        me.materials.append(BODY_MAT)
        me.materials.append(GLOW_MAT)
        ob = D.objects.new(self.name, me)
        ob.location = location
        bpy.context.scene.collection.objects.link(ob)
        return ob


# ---- the plinth is the base; the original guard stone (stone_body + stone_runes) is exported as its own figure ----------
plinth = Builder("stone_base")
plinth.hex_step(1.18, 0.0, 0.07)
plinth.hex_step(1.00, 0.07, 0.07)
plinth.hex_step(0.80, 0.14, 0.07)
plinth_ob = plinth.finish()

# ---- four big crystals, each its own part with the origin at its base pivot ------------------------------------
SHARD_BASE_Z = 0.45
for k, (length, lean) in enumerate(((1.45, 10), (1.10, 13), (1.30, 8), (0.95, 14))):
    az = math.radians(45 + 90 * k)
    pivot = Vector((0.80 * math.cos(az), 0.80 * math.sin(az), SHARD_BASE_Z))
    t = math.radians(lean)
    axis = Vector((math.sin(t) * math.cos(az), math.sin(t) * math.sin(az), math.cos(t)))
    s = Builder(f"shard_{k}")
    s.crystal(Vector((0, 0, 0)), axis, length - 0.45, 0.45, 0.19, 6, 0.30, twist=az)
    s.finish(location=pivot)

# ---- six floating diamonds, each origin at its own centre -------------------------------------------------------
for k in range(6):
    az = math.radians(30 + 60 * k)
    centre = Vector((0.62 * math.cos(az), 0.62 * math.sin(az), 2.05 + (0.10 if k % 2 else -0.04)))
    h = Builder(f"halo_{k}")
    h.crystal(Vector((0, 0, 0)), (0, 0, 1), 0.17, 0.17, 0.065, 4, 1.0, twist=az)
    h.finish(location=centre)

# ---- twelve rune segments: ring arcs + a glyph each, rune_0 at 12 o'clock (Unity +Z; Blender -Y), clockwise ----
RUNE_Z = 0.1425
R_IN, R_OUT = 0.845, 0.955
for k in range(12):
    rng = random.Random(1000 + k)
    theta = math.radians(k * 30.0)                  # clockwise from 12 o'clock as seen from above
    # Unity (x_u, z_u) = (-x_b, -y_b); a point at clockwise angle theta from +Z_u: x_u = sin, z_u = cos
    def at(r, tangent):
        """Blender XY of a point at radius r and tangential offset (metres, clockwise) from this segment's centre."""
        ang = theta + tangent / r
        return (-r * math.sin(ang), -r * math.cos(ang))

    seg = Builder(f"rune_{k}")
    steps = 6
    for ring_r in (R_IN, R_OUT):
        for i in range(steps):
            a0 = -0.24 + 0.48 * i / steps
            a1 = -0.24 + 0.48 * (i + 1) / steps
            seg.flat_strip(at(ring_r, a0 * ring_r), at(ring_r, a1 * ring_r), 0.022, RUNE_Z)
    # the glyph: a stave and two or three branches, different for every segment
    mid_r = (R_IN + R_OUT) / 2
    seg.flat_strip(at(mid_r - 0.045, 0.0), at(mid_r + 0.045, 0.0), 0.026, RUNE_Z)
    for _ in range(rng.choice((2, 3))):
        r0 = mid_r + rng.uniform(-0.035, 0.03)
        side = rng.choice((-1, 1))
        seg.flat_strip(at(r0, 0.0), at(r0 + rng.uniform(-0.04, 0.04), side * rng.uniform(0.025, 0.05)), 0.022, RUNE_Z)
    seg.finish()

# ---- export: the base (everything but the stone) and the original stone as figure_stone --------------------------
figure_parts = [body, glow]
base_parts = [o for o in D.objects if o.type == "MESH" and o not in figure_parts]


def export(parts, name):
    for o in D.objects:
        o.select_set(o in parts)
    bpy.context.view_layer.objects.active = parts[0]
    path = os.path.join(OUT, name)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_materials="EXPORT", export_image_format="NONE", export_cameras=False, export_lights=False)
    return path


glb = export(base_parts, "team_ward.glb")
glb2 = export(figure_parts, "figure_stone.glb")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "team_ward.blend"))
print("team ward written:", glb, os.path.getsize(glb), "bytes;", len(base_parts), "base parts:", sorted(o.name for o in base_parts), "; figure_stone", os.path.getsize(glb2), "bytes")
