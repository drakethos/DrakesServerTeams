"""Ward figures for DrakesServerTeams: low-poly stone animals that stand on the Team Ward plinth in place of the guard stone.

Wolf (sitting upright, paws forward), dragon (sitting upright, wings folded) and stag (standing). Each is ONE body mesh with two material slots with the flow channels cut out of it, plus plug_* parts (stone, fill the holes, hidden while the ward is on) and glow_* parts (the light in the holes):
slot 0 the ward's stone material (Guardstone_Oden_mat), slot 1 its glow material (Guardstone_OdenGlow_mat), used for the eyes. Faces are box-projected onto a
calm patch of the guard stone's texture atlas so the game's own ward materials (and any swapped stone material) map cleanly.
Coordinates match team_ward: Blender Z up, the plinth top is at z = 0.21, the figure faces Blender -Y (Unity +Z), everything fits inside r = 0.75, height <= 2.0.

Run in a live Blender (MCP):  sys.argv = ["x", "--", <out_dir>]  then exec this file.
Headless:  blender -b --factory-startup -P make_team_figures.py -- <out_dir>
Writes figure_wolf.glb, figure_dragon.glb, figure_stag.glb (no textures) and team_figures.blend (all three side by side, for looking at).
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = args[0] if args else os.path.dirname(os.path.abspath(globals().get("__file__", "x.py")))
os.makedirs(OUT, exist_ok=True)
D = bpy.data
Z0 = 0.21   # plinth top
STONE, GLOW = 0, 1

for o in list(D.objects):
    D.objects.remove(o)
for m in list(D.meshes):
    D.meshes.remove(m)
for m in list(D.materials):
    D.materials.remove(m)


def make_mat(name, rgb, emit=None):
    m = D.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1)
        bsdf.inputs["Emission Strength"].default_value = 3.0
    return m


STONE_MAT = make_mat("Guardstone_Oden_mat", (0.20, 0.20, 0.24))
GLOW_MAT = make_mat("Guardstone_OdenGlow_mat", (0.1, 0.1, 0.1), emit=(1.0, 0.25, 0.1))


class Fig:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.slots = {}

    def _faces(self, verts):
        return list({f for v in verts for f in v.link_faces})

    def ball(self, c, r, rot=(0, 0, 0), slot=STONE, u=7, v=5):
        """A faceted ellipsoid: centre c, radii r (x, y, z), rotation in degrees."""
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=1.0)
        m = Matrix.Translation(c) @ Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4() @ Matrix.Diagonal((*r, 1.0))
        bmesh.ops.transform(self.bm, matrix=m, verts=res["verts"])
        for f in self._faces(res["verts"]):
            f.material_index = slot

    def seg(self, p0, p1, r0, r1, sides=5, slot=STONE, cap=True):
        """A tapered stick from p0 (radius r0) to p1 (radius r1): legs, horns, antlers, tail."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=sides, radius1=r0, radius2=r1, depth=d.length)
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation((p0 + p1) / 2) @ rot, verts=res["verts"])
        for f in self._faces(res["verts"]):
            f.material_index = slot

    def panel(self, a, b, c, t=0.035, slot=STONE):
        """A thin triangular slab: a, b, c and the same corners pushed out along the normal by t."""
        a, b, c = Vector(a), Vector(b), Vector(c)
        n = (b - a).cross(c - a).normalized() * t
        v = [self.bm.verts.new(p) for p in (a, b, c, a + n, b + n, c + n)]
        for idx in ((0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)):
            self.bm.faces.new([v[i] for i in idx]).material_index = slot

    def mirror(self, fn):
        """Call fn(sign) for the left (+1) and right (-1) side."""
        fn(1)
        fn(-1)

    def finish(self, location=(0, 0, 0)):
        bm = self.bm
        uv = bm.loops.layers.uv.new("UVMap")
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for f in bm.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                p = l.vert.co
                a, b = [(p.y, p.z), (p.x, p.z), (p.x, p.y)][ax]
                # box projection, 3.5 m per texture patch, parked on a calm rock area of the atlas
                l[uv].uv = (0.60 + 0.12 * ((a / 3.5) % 1.0), 0.42 + 0.12 * ((b / 3.5) % 1.0))
        me = D.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(STONE_MAT)
        me.materials.append(GLOW_MAT)
        for p in me.polygons:
            p.use_smooth = False
        ob = D.objects.new(self.name, me)
        ob.location = location
        bpy.context.scene.collection.objects.link(ob)
        return ob


def eyes(f, y, z, x=0.075, size=(0.03, 0.012, 0.022)):
    f.mirror(lambda s: f.ball((s * x, y, z), size, slot=GLOW, u=5, v=3))


def wolf():
    f = Fig("figure_wolf")
    z = Z0
    f.ball((0, 0.12, z + 0.30), (0.36, 0.38, 0.30))                      # haunches
    f.mirror(lambda s: f.ball((s * 0.27, 0.00, z + 0.24), (0.13, 0.30, 0.21)))   # folded hind legs
    f.ball((0, 0.02, z + 0.80), (0.27, 0.26, 0.46), rot=(-6, 0, 0))      # chest and back
    f.ball((0, -0.07, z + 1.12), (0.31, 0.26, 0.24))                      # ruff
    f.ball((0, -0.09, z + 1.33), (0.17, 0.17, 0.17))                      # neck
    f.ball((0, -0.13, z + 1.50), (0.19, 0.21, 0.16))                      # head
    f.ball((0, -0.34, z + 1.44), (0.085, 0.15, 0.075), rot=(8, 0, 0))     # muzzle
    f.ball((0, -0.48, z + 1.44), (0.04, 0.03, 0.035), u=5, v=3)           # nose
    f.mirror(lambda s: f.seg((s * 0.11, -0.08, z + 1.62), (s * 0.15, -0.04, z + 1.88), 0.075, 0.01, sides=4))  # ears
    f.mirror(lambda s: f.seg((s * 0.13, -0.20, z + 0.98), (s * 0.13, -0.22, z + 0.04), 0.095, 0.07, sides=5))  # front legs
    f.mirror(lambda s: f.ball((s * 0.13, -0.29, z + 0.04), (0.09, 0.15, 0.045)))                              # front paws
    f.mirror(lambda s: f.ball((s * 0.35, -0.16, z + 0.04), (0.10, 0.18, 0.045)))                              # hind paws
    f.ball((0.34, 0.43, z + 0.07), (0.10, 0.34, 0.07), rot=(0, 0, 25))                                         # tail
    eyes(f, -0.31, z + 1.55)
    return f


def dragon():
    f = Fig("figure_dragon")
    z = Z0
    f.ball((0, 0.14, z + 0.30), (0.36, 0.40, 0.30))                      # haunches
    f.mirror(lambda s: f.ball((s * 0.27, 0.02, z + 0.24), (0.13, 0.30, 0.21)))
    f.ball((0, 0.02, z + 0.84), (0.27, 0.26, 0.48), rot=(-8, 0, 0))      # body
    f.ball((0, -0.06, z + 1.30), (0.19, 0.18, 0.22), rot=(-12, 0, 0))    # neck, two thick bends
    f.ball((0, -0.14, z + 1.52), (0.15, 0.15, 0.18), rot=(-22, 0, 0))
    f.ball((0, -0.28, z + 1.68), (0.15, 0.22, 0.12))                      # head
    f.ball((0, -0.49, z + 1.62), (0.09, 0.14, 0.07))                      # snout
    f.ball((0, -0.40, z + 1.52), (0.08, 0.15, 0.04))                      # jaw
    f.mirror(lambda s: f.seg((s * 0.09, -0.20, z + 1.76), (s * 0.15, 0.10, z + 2.02), 0.055, 0.008, sides=4))   # horns, swept back from the skull
    f.mirror(lambda s: f.ball((s * 0.15, -0.38, z + 1.78), (0.04, 0.05, 0.035), u=5, v=3))                    # brow ridge
    # folded wings: a leading bone from the shoulder, a membrane hanging from it
    f.mirror(lambda s: f.seg((s * 0.22, 0.08, z + 1.10), (s * 0.50, 0.26, z + 2.02), 0.05, 0.012, sides=4))
    f.mirror(lambda s: f.panel((s * 0.22, 0.08, z + 1.10), (s * 0.50, 0.26, z + 2.02), (s * 0.30, 0.34, z + 0.60), t=s * 0.04))
    f.mirror(lambda s: f.seg((s * 0.30, 0.34, z + 0.60), (s * 0.50, 0.26, z + 2.02), 0.02, 0.01, sides=4))
    for k in range(4):                                                      # back spikes
        t = k / 3.0
        f.seg((0, 0.16 + 0.04 * t, z + 1.10 - 0.70 * t), (0, 0.24 + 0.06 * t, z + 1.26 - 0.70 * t), 0.05, 0.006, sides=4)
    f.mirror(lambda s: f.seg((s * 0.13, -0.20, z + 0.98), (s * 0.13, -0.22, z + 0.06), 0.10, 0.07, sides=5))   # front legs
    f.mirror(lambda s: f.ball((s * 0.13, -0.28, z + 0.04), (0.09, 0.13, 0.045)))
    f.mirror(lambda s: [f.seg((s * 0.13 + dx, -0.38, z + 0.045), (s * 0.13 + dx, -0.46, z + 0.04), 0.022, 0.004, sides=4) for dx in (-0.045, 0.0, 0.045)])  # claws
    f.mirror(lambda s: f.ball((s * 0.35, -0.16, z + 0.04), (0.10, 0.18, 0.045)))
    for (x, y, r) in ((0.18, 0.48, 0.13), (0.38, 0.58, 0.10), (0.54, 0.48, 0.075), (0.58, 0.30, 0.055)):    # tail curling round the base
        f.ball((x, y, z + 0.07), (r, r, 0.07))
    f.seg((0.58, 0.28, z + 0.07), (0.50, 0.08, z + 0.07), 0.05, 0.006, sides=4)
    eyes(f, -0.42, z + 1.74, x=0.09, size=(0.03, 0.012, 0.022))
    return f


def stag():
    f = Fig("figure_stag")
    z = Z0
    f.ball((0, 0.02, z + 0.92), (0.22, 0.44, 0.23))                      # body
    f.ball((0, 0.34, z + 0.92), (0.21, 0.20, 0.23))                      # rump
    f.ball((0, -0.30, z + 0.98), (0.20, 0.22, 0.23))                     # chest
    f.mirror(lambda s: f.seg((s * 0.12, 0.34, z + 0.95), (s * 0.12, 0.40, z + 0.52), 0.12, 0.06, sides=5))    # hind thigh
    f.mirror(lambda s: f.seg((s * 0.12, 0.40, z + 0.52), (s * 0.12, 0.34, z + 0.04), 0.06, 0.045, sides=5))   # hind shin
    f.mirror(lambda s: f.seg((s * 0.11, -0.32, z + 0.90), (s * 0.11, -0.34, z + 0.04), 0.085, 0.045, sides=5))  # front legs
    f.mirror(lambda s: f.ball((s * 0.12, 0.34, z + 0.03), (0.055, 0.09, 0.03), u=5, v=3))                      # hooves
    f.mirror(lambda s: f.ball((s * 0.11, -0.35, z + 0.03), (0.055, 0.09, 0.03), u=5, v=3))
    f.seg((0, -0.38, z + 1.02), (0, -0.50, z + 1.50), 0.19, 0.11, sides=6)                                     # neck
    f.ball((0, -0.54, z + 1.56), (0.10, 0.18, 0.11), rot=(-20, 0, 0))                                          # head
    f.ball((0, -0.68, z + 1.47), (0.06, 0.10, 0.06), rot=(-20, 0, 0))                                          # muzzle
    f.mirror(lambda s: f.seg((s * 0.07, -0.50, z + 1.62), (s * 0.22, -0.48, z + 1.72), 0.05, 0.008, sides=4))  # ears
    def antler(s):
        f.seg((s * 0.06, -0.50, z + 1.64), (s * 0.17, -0.56, z + 1.86), 0.040, 0.028, sides=4)               # main beam in three bends
        f.seg((s * 0.17, -0.56, z + 1.86), (s * 0.27, -0.48, z + 2.00), 0.028, 0.02, sides=4)
        f.seg((s * 0.27, -0.48, z + 2.00), (s * 0.28, -0.58, z + 2.16), 0.02, 0.005, sides=4)
        f.seg((s * 0.17, -0.56, z + 1.86), (s * 0.12, -0.68, z + 2.00), 0.022, 0.005, sides=4)               # brow tine
        f.seg((s * 0.27, -0.48, z + 2.00), (s * 0.38, -0.42, z + 2.14), 0.018, 0.005, sides=4)               # back tine
    f.mirror(antler)
    f.ball((0, 0.52, z + 1.06), (0.06, 0.08, 0.08), u=5, v=3)                                                  # tail tuft
    eyes(f, -0.63, z + 1.60, x=0.07, size=(0.024, 0.012, 0.02))
    return f


# ---- flow channels: glowing lines carved along the body, like the runes on the guard stone -------------------------------------
# Each channel is its own part (channel_<figure>_NN) in the glow material, so the mod can send a pulse along them: they are numbered from the
# bottom of the body to the top. Points hug the body ellipsoids (see on()).
def surf(c, r, theta, phi, lift=1.05):
    """A point on an ellipsoid: theta around Z (0 = +X, 270 = the front, -Y), phi from the top (0) to the bottom (180)."""
    t, p = math.radians(theta), math.radians(phi)
    return Vector((c[0] + r[0] * lift * math.sin(p) * math.cos(t), c[1] + r[1] * lift * math.sin(p) * math.sin(t), c[2] + r[2] * lift * math.cos(p)))


def on(c, r, pairs, lift=1.05, n=4):
    pts = []
    for (t0, p0), (t1, p1) in zip(pairs, pairs[1:]):
        for i in range(n):
            f = i / n
            pts.append(surf(c, r, t0 + (t1 - t0) * f, p0 + (p1 - p0) * f, lift))
    pts.append(surf(c, r, pairs[-1][0], pairs[-1][1], lift))
    return pts


def mirror_x(pts):
    return [Vector((-p.x, p.y, p.z)) for p in pts]


def flip_theta(pairs):
    return [(180 - t, p) for t, p in pairs]


def wolf_channels():
    z = Z0
    torso, ruff, hip, head = ((0, 0.02, z + 0.80), (0.27, 0.26, 0.46)), ((0, -0.07, z + 1.12), (0.31, 0.26, 0.24)), ((0, 0.12, z + 0.30), (0.36, 0.38, 0.30)), ((0, -0.13, z + 1.50), (0.19, 0.21, 0.16))
    out = []
    swirl = [(-15, 150), (15, 120), (-5, 95), (20, 70)]
    haunch = [(-25, 105), (5, 85), (35, 65), (65, 55)]
    for sgn in (1, -1):
        out.append([Vector((sgn * 0.13, -0.30, z + 0.10)), Vector((sgn * 0.13, -0.30, z + 0.50)), Vector((sgn * 0.13, -0.30, z + 0.92))])   # front leg
        pts = on(*hip, haunch if sgn > 0 else flip_theta(haunch))
        out.append(pts)
        pts = on(*torso, swirl if sgn > 0 else flip_theta(swirl))
        out.append(pts)
    out.append(on(*torso, [(270, 150), (270, 110), (270, 70)]))                    # chest
    out.append(on(*torso, [(90, 150), (90, 105), (90, 60)]))                       # back
    out.append(on(*ruff, [(205, 90), (240, 95), (270, 100), (300, 95), (335, 90)]))  # collar
    out.append(on(*head, [(270, 115), (270, 80), (270, 45)]))                      # forehead
    return out


def dragon_channels():
    z = Z0
    body = ((0, 0.02, z + 0.84), (0.27, 0.26, 0.48))
    neck1 = ((0, -0.06, z + 1.30), (0.19, 0.18, 0.22))
    neck2 = ((0, -0.14, z + 1.52), (0.15, 0.15, 0.18))
    hip = ((0, 0.14, z + 0.30), (0.36, 0.40, 0.30))
    head = ((0, -0.28, z + 1.68), (0.15, 0.22, 0.12))
    out = []
    haunch = [(-25, 105), (5, 85), (35, 65), (65, 55)]
    for sgn in (1, -1):
        out.append([Vector((sgn * 0.13, -0.30, z + 0.10)), Vector((sgn * 0.13, -0.30, z + 0.50)), Vector((sgn * 0.13, -0.30, z + 0.92))])
        out.append(on(*hip, haunch if sgn > 0 else flip_theta(haunch)))
        out.append([Vector((sgn * 0.27, 0.12, z + 1.15)), Vector((sgn * 0.40, 0.19, z + 1.50)), Vector((sgn * 0.53, 0.26, z + 1.96))])         # along the wing bone
        out.append([Vector((sgn * 0.33, 0.30, z + 0.65)), Vector((sgn * 0.38, 0.28, z + 1.00)), Vector((sgn * 0.42, 0.26, z + 1.40))])         # across the membrane
    out.append(on(*body, [(270, 150), (270, 110), (270, 70)]) + on(*neck1, [(270, 110), (270, 80)])[1:])                                      # belly, chest and throat
    out.append(on(*neck2, [(270, 120), (270, 85), (270, 50)]))
    out.append(on(*body, [(90, 150), (90, 105), (90, 60)]) + on(*neck1, [(90, 100), (90, 55)])[1:])                                             # spine
    out.append(on(*head, [(270, 100), (270, 75), (270, 50)]))
    return out


def stag_channels():
    z = Z0
    out = []
    for sgn in (1, -1):
        out.append([Vector((sgn * 0.11, -0.39, z + 0.10)), Vector((sgn * 0.11, -0.40, z + 0.50)), Vector((sgn * 0.11, -0.41, z + 0.88))])       # front leg
        out.append([Vector((sgn * 0.17, 0.36, z + 0.10)), Vector((sgn * 0.18, 0.40, z + 0.50)), Vector((sgn * 0.235, 0.34, z + 0.92))])          # hind leg
        out.append([Vector((sgn * 0.235, -0.35, z + 0.98)), Vector((sgn * 0.23, -0.10, z + 1.03)), Vector((sgn * 0.23, 0.15, z + 1.00)), Vector((sgn * 0.22, 0.38, z + 0.95))])   # flank
        out.append([Vector((sgn * 0.215, -0.30, z + 0.82)), Vector((sgn * 0.22, 0.00, z + 0.80)), Vector((sgn * 0.21, 0.34, z + 0.78))])        # belly line
    out.append([Vector((0, -0.30, z + 1.22)), Vector((0, 0.00, z + 1.16)), Vector((0, 0.40, z + 1.14))])                                         # back
    out.append([Vector((0, -0.585, z + 1.08)), Vector((0, -0.60, z + 1.45))])                                                                    # neck
    out.append([Vector((0, -0.62, z + 1.64)), Vector((0, -0.66, z + 1.56))])                                                                     # nose bridge
    return out


CHANNELS = {"wolf": wolf_channels, "dragon": dragon_channels, "stag": stag_channels}


GROOVE_DEPTH = 0.04     # how far the channels are cut into the body
PLUG_SHRINK = 0.006     # the plug is a little narrower than the hole it fills


def channel_paths(bvh, pts, width=0.075, tick=0.17, tick_len=0.09):
    """The channel and its rune ticks as separate snapped paths [(width, [(point, normal), ...]), ...]."""
    dense = []
    for a, b in zip(pts, pts[1:]):
        n = max(1, int((b - a).length / 0.04))
        for i in range(n):
            dense.append(a.lerp(b, i / n))
    dense.append(pts[-1])
    snapped = []
    for p in dense:
        loc, nor, _, _ = bvh.find_nearest(p)
        if loc is not None:
            snapped.append((loc, nor))
    paths = [(width, snapped)]
    dist, flag = 0.0, 1
    for i in range(1, len(snapped)):
        dist += (snapped[i][0] - snapped[i - 1][0]).length
        if dist >= tick:
            dist = 0.0
            flag = -flag
            (p, n), (q, _) = snapped[i], snapped[i - 1]
            t = p - q
            if t.length < 1e-5:
                continue
            end = p + n.cross(t).normalized() * flag * tick_len + t.normalized() * 0.02
            loc, nor, _, _ = bvh.find_nearest(end)
            if loc is not None:
                paths.append((width * 0.8, [(p, n), (loc, nor)]))
    return paths


def prism(bm, uvl, path, w, top, bottom, slot):
    """A closed bar along a snapped path: `top` above the surface, `bottom` below it, w wide. Used for the cutter, the plug and (thin) the glow."""
    rows = []
    for i, (p, n) in enumerate(path):
        t = path[min(i + 1, len(path) - 1)][0] - path[max(i - 1, 0)][0]
        if t.length < 1e-6:
            continue
        side = n.cross(t).normalized() * (w / 2)
        rows.append([bm.verts.new(p + n * top - side), bm.verts.new(p + n * top + side), bm.verts.new(p + n * bottom + side), bm.verts.new(p + n * bottom - side)])
    faces = []
    for r0, r1 in zip(rows, rows[1:]):
        for (i, j) in ((0, 1), (1, 2), (2, 3), (3, 0)):
            try:
                faces.append(bm.faces.new([r0[i], r0[j], r1[j], r1[i]]))
            except ValueError:
                pass
    if rows:
        for r in (rows[0], rows[-1]):
            try:
                faces.append(bm.faces.new(r))
            except ValueError:
                pass
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    for f in faces:
        f.material_index = slot
        for l in f.loops:
            l[uvl].uv = (0.70, 0.52) if slot == GLOW else (0.66, 0.50)


def make_object(name, bm, slotted=True):
    me = D.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(STONE_MAT)
    me.materials.append(GLOW_MAT)
    for poly in me.polygons:
        poly.use_smooth = False
    o = D.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def carve(figure, body, lines, bvh):
    """Cut every channel out of the body and return (plugs, glows): plug_<figure>_NN fills the hole (stone, same material as the body),
    glow_<figure>_NN is the light at the bottom of it. The mod hides the plugs while the ward is on, like the original guard stone."""
    cutter_bm = bmesh.new()
    cuv = cutter_bm.loops.layers.uv.new("UVMap")
    plugs, glows = [], []
    for k, pts in enumerate(lines):
        pbm, gbm = bmesh.new(), bmesh.new()
        puv, guv = pbm.loops.layers.uv.new("UVMap"), gbm.loops.layers.uv.new("UVMap")
        for width, path in channel_paths(bvh, pts):
            if len(path) < 2:
                continue
            prism(cutter_bm, cuv, path, width, 0.03, -GROOVE_DEPTH, STONE)
            prism(pbm, puv, path, width - PLUG_SHRINK, 0.003, -(GROOVE_DEPTH - 0.012), STONE)
            prism(gbm, guv, path, width - PLUG_SHRINK, 0.002, -0.014, GLOW)      # flush with the surface, exactly the hole, like the original's glow layer
        plugs.append(make_object(f"plug_{figure}_{k:02d}", pbm))
        glows.append(make_object(f"glow_{figure}_{k:02d}", gbm))
    cutter = make_object(f"cutter_{figure}", cutter_bm)
    mod = body.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    mod.use_self = True
    for o in D.objects:
        o.select_set(False)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier="cut")
    D.objects.remove(cutter)
    for poly in body.data.polygons:
        poly.use_smooth = False
    return plugs, glows


made, plugs_of, glows_of = {}, {}, {}
for fn, name in ((wolf, "wolf"), (dragon, "dragon"), (stag, "stag")):
    body = fn().finish()
    made[name] = body
    bpy.context.view_layer.update()
    bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    lines = sorted(CHANNELS[name](), key=lambda pts: sum(p.z for p in pts) / len(pts))   # bottom to top
    plugs_of[name], glows_of[name] = carve(name, body, lines, bvh)

report = {}
for name, ob in made.items():
    chosen = [ob] + plugs_of[name] + glows_of[name]
    for o in D.objects:
        o.select_set(o in chosen)
    bpy.context.view_layer.objects.active = ob
    glb = os.path.join(OUT, f"figure_{name}.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_materials="EXPORT", export_image_format="NONE", export_cameras=False, export_lights=False)
    me = ob.data
    zs = [v.co.z for v in me.vertices]
    report[name] = {"body_tris": sum(len(p.vertices) - 2 for p in me.polygons), "channels": len(glows_of[name]), "bytes": os.path.getsize(glb),
                    "z": (round(min(zs), 2), round(max(zs), 2)), "max_radius": round(max(math.hypot(v.co.x, v.co.y) for v in me.vertices), 2)}

# side by side for looking at: a plinth under each
for i, (name, ob) in enumerate(made.items()):
    for c in plugs_of[name] + glows_of[name]:
        c.location = (i * 2.6 - 2.6, 0, 0)
    ob.location = (i * 2.6 - 2.6, 0, 0)
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=1.0, depth=0.21, location=(ob.location.x, 0, 0.105))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "team_figures.blend"))
result = report
print(report)
