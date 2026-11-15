"""Builds the Slice & Sell fruit models (Guava, Mango, Pineapple, Watermelon) with all cut stages.

Run:  Blender -b --python Tools/Blender/make_fruits.py [-- --render <dir>]
Writes Assets/_Project/Models/Fruits/{Guava,Mango,Pineapple,Watermelon}.glb and fruits.json,
re-imports each GLB to check it, and optionally renders exploded stage strips.
"""
import bpy, bmesh, json, math, os, random, struct, sys
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "Fruits")
MAX_TRIS = 800


# glTFast mirrors X when importing (glTF is right-handed), and Blender's exporter maps
# Blender (x, y, z) -> glTF (x, z, -y). So Unity (x, y, z) == Blender (-x, -z, y).
def U(x, y, z):
    return Vector((-x, -z, y))


def to_unity(v):
    return [-v.x, v.z, -v.y]


def lin(hexstr):
    c = [int(hexstr[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [1.0]


def make_material(name, hexstr):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = lin(hexstr)
    bsdf.inputs["Roughness"].default_value = 0.65
    m.diffuse_color = lin(hexstr)
    return m


# ---------------------------------------------------------------- base shapes

def loft(bm, bottom, rings, top, mat=0):
    """Closed mesh: bottom pole, list of equal-length rings, top pole."""
    bv = bm.verts.new(bottom)
    tv = bm.verts.new(top)
    vr = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    faces = []
    for j in range(n):
        faces.append(bm.faces.new((bv, vr[0][(j + 1) % n], vr[0][j])))
        faces.append(bm.faces.new((tv, vr[-1][j], vr[-1][(j + 1) % n])))
        for i in range(len(vr) - 1):
            faces.append(bm.faces.new((vr[i][j], vr[i][(j + 1) % n], vr[i + 1][(j + 1) % n], vr[i + 1][j])))
    for f in faces:
        f.material_index = mat
    return faces


def guava():
    rnd = random.Random(7)
    # (radius, height) from bottom to top: fullest low down, tapering to a neck = pear-ish
    prof = [(0.013, 0.0), (0.026, 0.005), (0.035, 0.014), (0.0395, 0.027), (0.0398, 0.041),
            (0.035, 0.056), (0.027, 0.068), (0.018, 0.077), (0.009, 0.083),
            (0.0028, 0.0835), (0.0024, 0.091)]
    segs = 14
    rings = []
    for k, (r, h) in enumerate(prof):
        ring = []
        for j in range(segs):
            a = 2 * math.pi * (j + 0.5 * (k % 2)) / segs
            jit = 0 if k >= len(prof) - 2 else rnd.uniform(-0.0012, 0.0012)
            ring.append(Vector(((r + jit) * math.cos(a), (r + jit) * math.sin(a), h)))
        rings.append(ring)
    bm = bmesh.new()
    loft(bm, (0, 0, 0.003), rings, (0, 0, 0.092))
    for f in bm.faces:  # stem nub
        if min(v.co.z for v in f.verts) > 0.083:
            f.material_index = 2
    return bm


def mango():
    rnd = random.Random(11)
    L, a, b = 0.055, 0.0375, 0.039
    R, S = 10, 12

    def spine(s):
        # broad rounded stem end (-z), narrower tip (+z) that hooks sideways and droops: a mango "beak"
        xc = 0.005 * (1 - s * s) - 0.014 * max(0.0, s - 0.35) ** 2
        yc = -0.010 * max(0.0, s - 0.3) ** 2
        return xc, yc

    def flatten(y):  # flattened cheek so it rests on the board
        return y if y >= -0.026 else -0.026 + (y + 0.026) * 0.3

    pts = []
    for i in range(1, R):
        s = -math.cos(math.pi * i / R)
        k = math.sqrt(1 - s * s)
        taper = 1 - 0.2 * max(0.0, s) + 0.04 * min(0.0, s)
        xc, yc = spine(s)
        ring = []
        for j in range(S):
            p = 2 * math.pi * j / S
            x = a * k * taper * math.cos(p) + xc
            y = flatten(b * k * taper * math.sin(p) + yc)
            ring.append([x + rnd.uniform(-0.0008, 0.0008), y, L * s])
        pts.append(ring)
    poles = [[*spine(-1), -L], [*spine(1), L]]
    allp = [p for r in pts for p in r] + poles
    ymin = min(p[1] for p in allp)
    cx = (min(p[0] for p in allp) + max(p[0] for p in allp)) / 2
    conv = lambda p: U(p[0] - cx, p[1] - ymin, p[2])
    bm = bmesh.new()
    loft(bm, conv(poles[0]), [[conv(p) for p in r] for r in pts], conv(poles[1]))
    return bm


PINE_TOP = 0.15  # crown cut height


def pineapple_body():
    """Barrel of raised diamond 'eyes' (each diamond is a 4-sided pyramid)."""
    N, K = 8, 9
    z0, z1 = 0.007, 0.147
    dz = (z1 - z0) / (K - 1)

    def rad(z):
        return 0.054 * (1 - 0.28 * ((z - 0.075) / 0.075) ** 2)

    def ang(i, j):
        return 2 * math.pi * (j + 0.5 * (i % 2)) / N

    bm = bmesh.new()
    grid = [[bm.verts.new((rad(z0 + i * dz) * math.cos(ang(i, j)), rad(z0 + i * dz) * math.sin(ang(i, j)), z0 + i * dz))
             for j in range(N)] for i in range(K)]
    G = lambda i, j: grid[i][j % N]
    for i in range(K - 2):
        for j in range(N):
            lo, hi = G(i, j), G(i + 2, j)
            left = G(i + 1, j - (1 - i % 2))
            right = G(i + 1, j - (1 - i % 2) + 1)
            c = (lo.co + hi.co + left.co + right.co) / 4
            z = c.z
            c.xy = c.xy.normalized() * (rad(z) + 0.0045)
            cv = bm.verts.new(c)
            for e in ((lo, right), (right, hi), (hi, left), (left, lo)):
                bm.faces.new((e[0], e[1], cv))
    for r, r2 in ((0, 1), (K - 1, K - 2)):  # zigzag border triangles
        for j in range(N):
            bm.faces.new((G(r, j), G(r, j + 1), G(r2, j + (r % 2))))
    # flat bottom and a domed shoulder that the crown sits in
    bot = [bm.verts.new((0.85 * rad(0) * math.cos(ang(0, j)), 0.85 * rad(0) * math.sin(ang(0, j)), 0)) for j in range(N)]
    sh = [bm.verts.new((0.024 * math.cos(ang(K - 1, j)), 0.024 * math.sin(ang(K - 1, j)), 0.159)) for j in range(N)]
    bc, tc = bm.verts.new((0, 0, 0)), bm.verts.new((0, 0, 0.162))
    for j in range(N):
        bm.faces.new((bot[j], bot[(j + 1) % N], G(0, j + 1), G(0, j)))
        bm.faces.new((bc, bot[(j + 1) % N], bot[j]))
        bm.faces.new((G(K - 1, j), G(K - 1, j + 1), sh[(j + 1) % N], sh[j]))
        bm.faces.new((tc, sh[j], sh[(j + 1) % N]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    return bm


def pineapple_crown(bm):
    """Leaves as thin closed blades fanning out of the shoulder; material 2."""
    rnd = random.Random(5)
    layers = [(5, 0.115, 12, 0.0), (7, 0.09, 32, 0.3), (8, 0.06, 55, 0.65)]  # count, length, tilt deg, phase
    for count, length, tilt, phase in layers:
        for n in range(count):
            az = 2 * math.pi * (n + phase) / count + rnd.uniform(-0.15, 0.15)
            t = math.radians(tilt + rnd.uniform(-5, 5))
            ln = length * rnd.uniform(0.9, 1.08)
            out = Vector((math.cos(az), math.sin(az), 0))
            side = Vector((-math.sin(az), math.cos(az), 0))
            base = Vector((0, 0, 0.154)) + out * 0.006
            up = Vector((0, 0, 1))
            d0 = (up * math.cos(t) + out * math.sin(t)).normalized()
            # blade bends further outward toward the tip
            t2 = t + math.radians(12)
            d1 = (up * math.cos(t2) + out * math.sin(t2)).normalized()
            mid = base + d0 * ln * 0.5
            tip = mid + d1 * ln * 0.5
            w, th = 0.0075, 0.0022
            nrm0 = d0.cross(side).normalized()
            sec = lambda c, s: [bm.verts.new(c + side * w * s), bm.verts.new(c - side * w * s), bm.verts.new(c + nrm0 * th * s)]
            b, m = sec(base, 1.0), sec(mid, 0.8)
            tv = bm.verts.new(tip)
            fs = [bm.faces.new((b[0], b[1], b[2]))]
            for k in range(3):
                fs.append(bm.faces.new((b[k], m[k], m[(k + 1) % 3], b[(k + 1) % 3])))
                fs.append(bm.faces.new((m[k], tv, m[(k + 1) % 3])))
            for f in fs:
                f.material_index = 2


def watermelon():
    """Baby watermelon lying on its side, long axis along Unity X. Stripes run pole to pole:
    around the barrel, each wide dark segment (material 0) is followed by a narrow light one (material 2)."""
    A, B, C = 0.11, 0.075, 0.08   # semi-axes: Unity X, Z, Y (Y gets flattened ~1 cm at the belly)
    R, N = 10, 8                   # rings along X (none on a cut plane), stripes around
    ang = []
    for j in range(N):
        ang += [2 * math.pi * j / N, 2 * math.pi * (j + 0.7) / N]
    rings = []
    for i in range(1, R + 1):
        s = -math.cos(math.pi * i / (R + 1))
        k = math.sqrt(1 - s * s)
        wave = 0.06 * math.sin(i * 1.7)  # stripes wobble a little along the length
        ring = []
        for a in ang:
            y = C + C * k * math.sin(a + wave)
            y = y if y >= 0.012 else 0.012 + (y - 0.012) * 0.3  # flattened belly so it rests on the board
            ring.append(U(A * s, y, B * k * math.cos(a + wave)))
        rings.append(ring)
    bm = bmesh.new()
    faces = loft(bm, U(-A, C, 0), rings, U(A, C, 0))
    per = 2 + R - 1  # loft adds this many faces per segment, segment by segment
    for i, f in enumerate(faces):
        f.material_index = 2 if (i // per) % 2 else 0
    zmin = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= zmin
    return bm


# ---------------------------------------------------------------- cutting

def cut(base, keeps, cap_mat=1, inset_for=None):
    """Copy `base` and keep the side of each plane given in `keeps` = [(normal, offset, keep_positive)].
    Every cut hole is closed with a flesh cap; caps on a plane whose offset matches
    `inset_for` = (normal, offset or None for any, thickness, mat) get an inset core/seed."""
    bm = base.copy()
    for n, off, pos in keeps:
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-6,
                                     plane_co=n * off, plane_no=n, clear_inner=pos, clear_outer=not pos)
        border = [e for e in bm.edges if e.is_boundary]
        if not border:
            continue
        caps = bmesh.ops.holes_fill(bm, edges=border, sides=0)["faces"]
        for f in caps:
            f.material_index = cap_mat
        if inset_for and (inset_for[0] - n).length < 1e-6 and (inset_for[1] is None or abs(inset_for[1] - off) < 1e-6):
            ring = bmesh.ops.inset_region(bm, faces=caps, thickness=inset_for[2], depth=0, use_even_offset=True)["faces"]
            for f in caps:
                f.material_index = inset_for[3]
            for f in ring:
                f.material_index = cap_mat
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")
    for f in bm.faces:
        f.smooth = False
    assert all(len(e.link_faces) == 2 for e in bm.edges), "piece is not closed"
    assert signed_volume(bm) > 0, "piece normals point inward"
    return bm


def signed_volume(bm):
    return sum(f.verts[0].co.dot(f.verts[1].co.cross(f.verts[2].co)) for f in bm.faces) / 6


def slices(base, n, offsets, extra=(), inset_for=None):
    """Pieces between consecutive parallel planes, ordered along n (Unity -x side first)."""
    offs = sorted(offsets)
    bounds = [None] + offs + [None]
    pieces = []
    for lo, hi in zip(bounds, bounds[1:]):
        keeps = list(extra)
        if lo is not None:
            keeps.append((n, lo, True))
        if hi is not None:
            keeps.append((n, hi, False))
        pieces.append(cut(base, keeps, inset_for=inset_for))
    return pieces


def section_height(base, n, off):
    bm = base.copy()
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-6,
                           plane_co=n * off, plane_no=n, clear_inner=True)
    zs = [v.co.z for e in bm.edges if e.is_boundary for v in e.verts]
    bm.free()
    return min(zs), max(zs)


# ---------------------------------------------------------------- export

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def bbox(bms):
    vs = [v.co for bm in bms for v in bm.verts]
    lo = Vector([min(v[i] for v in vs) for i in range(3)])
    hi = Vector([max(v[i] for v in vs) for i in range(3)])
    return lo, hi


def unity_box(lo, hi):
    a, b = to_unity(lo), to_unity(hi)
    mn = [min(x, y) for x, y in zip(a, b)]
    mx = [max(x, y) for x, y in zip(a, b)]
    return mn, mx


def export(name, mats, stages, path):
    """stages: list of [(piece_name, bmesh)]. Blender names must be unique, so objects are
    called 'StageK|PieceN' and the prefix is stripped from the GLB afterwards."""
    reset()
    materials = [make_material(m, c) for m, c in mats]
    for k, pieces in enumerate(stages):
        st = bpy.data.objects.new(f"Stage{k}", None)
        bpy.context.scene.collection.objects.link(st)
        for pname, bm in pieces:
            me = bpy.data.meshes.new(f"{name}{k}|{pname}")
            bm.to_mesh(me)
            for m in materials:
                me.materials.append(m)
            ob = bpy.data.objects.new(f"Stage{k}|{pname}", me)
            ob.parent = st
            bpy.context.scene.collection.objects.link(ob)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_yup=True, export_apply=True,
                              export_texcoords=False, export_animations=False)
    strip_prefixes(path)


def strip_prefixes(path):
    with open(path, "rb") as fh:
        data = fh.read()
    jlen = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + jlen])
    for key in ("nodes", "meshes"):
        for item in doc.get(key, []):
            item["name"] = item["name"].split("|")[-1]
    js = json.dumps(doc, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    rest = data[20 + jlen:]
    out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + len(rest)) + struct.pack("<II", len(js), 0x4E4F534A) + js + rest
    with open(path, "wb") as fh:
        fh.write(out)


def r4(v):
    return [round(x, 4) for x in v]


# ---------------------------------------------------------------- fruits

def build_guava():
    base = guava()
    nx = U(1, 0, 0)
    xs = [-0.015, 0.015]
    stages = [[("Piece0", cut(base, []))]]
    for k in (1, 2):
        stages.append([(f"Piece{i}", p) for i, p in enumerate(slices(base, nx, xs[:k], inset_for=(nx, None, 0.014, 3)))])
    lo, hi = bbox([base])
    cuts = []
    for x in xs:
        z0, z1 = section_height(base, nx, x)
        cuts.append({"normal": [1, 0, 0], "point": r4([x, (z0 + z1) / 2, 0]), "depth": round(z1 - z0, 4)})
    mn, mx = unity_box(lo, hi)
    size = [b - a for a, b in zip(mn, mx)]
    d = max(size)
    meta = {"name": "Guava", "file": "Guava.glb", "price": 20, "juiceColor": "#F59A92", "size": r4(size),
            "cutVolume": {"type": "sphere", "center": r4([(a + b) / 2 for a, b in zip(mn, mx)]), "size": r4([d, d, d])},
            "stages": ["Stage0", "Stage1", "Stage2"], "cuts": cuts}
    mats = [("GuavaSkin", "#8CC63E"), ("GuavaFlesh", "#F2857A"), ("GuavaStem", "#6B4A2B"), ("GuavaCore", "#F7AFA3")]
    return mats, stages, meta


def build_mango():
    base = mango()
    lo, hi = bbox([base])
    h = hi.z - lo.z
    un = Vector((math.cos(math.radians(30)), 0, math.sin(math.radians(30))))
    n = U(*un)
    ds = [-0.025, 0.0, 0.025]
    seed = (n, 0.0, 0.016, 2)
    stages = [[("Piece0", cut(base, []))]]
    for k in (1, 2, 3):
        pieces = []
        offs = sorted(ds[:k])
        bounds = [None] + offs + [None]
        for a, b in zip(bounds, bounds[1:]):
            keeps = ([(n, a, True)] if a is not None else []) + ([(n, b, False)] if b is not None else [])
            pieces.append(cut(base, keeps, inset_for=seed))
        stages.append([(f"Piece{i}", p) for i, p in enumerate(pieces)])
    cuts = []
    for d in ds:
        z0, z1 = section_height(base, n, d)
        cuts.append({"normal": r4(un), "point": r4([un.x * d, h / 2, un.z * d]), "depth": round(z1 - z0, 4)})
    mn, mx = unity_box(lo, hi)
    size = [b - a for a, b in zip(mn, mx)]
    meta = {"name": "Mango", "file": "Mango.glb", "price": 25, "juiceColor": "#EAF3AC", "size": r4(size),
            "cutVolume": {"type": "box", "center": r4([(a + b) / 2 for a, b in zip(mn, mx)]), "size": r4(size)},
            "stages": ["Stage0", "Stage1", "Stage2", "Stage3"], "cuts": cuts}
    mats = [("MangoSkin", "#4FA83D"), ("MangoFlesh", "#E3EF8E"), ("MangoSeed", "#FBF6E6")]
    return mats, stages, meta


def build_pineapple():
    body = pineapple_body()
    blo, bhi = bbox([body])
    base = body.copy()
    pineapple_crown(base)
    up = Vector((0, 0, 1))
    nx = U(1, 0, 0)
    xs = [-0.03, 0.0, 0.03]
    below = (up, PINE_TOP, False)
    stages = [[("Piece0", cut(base, []))],
              [("Piece0", cut(base, [below])), ("Crown", cut(base, [(up, PINE_TOP, True)]))]]
    for k in (1, 2, 3):
        stages.append([(f"Piece{i}", p) for i, p in enumerate(slices(base, nx, xs[:k], extra=[below]))])
    lo, hi = bbox([base])
    bmn, bmx = unity_box(blo, bhi)
    body_d = max(bmx[0] - bmn[0], bmx[2] - bmn[2])
    cuts = [{"normal": [0, 1, 0], "point": [0, PINE_TOP, 0], "depth": round(body_d, 4)}]
    cuts += [{"normal": [1, 0, 0], "point": [x, 0.075, 0], "depth": 0.15} for x in xs]
    mn, mx = unity_box(lo, hi)
    size = [b - a for a, b in zip(mn, mx)]
    meta = {"name": "Pineapple", "file": "Pineapple.glb", "price": 35, "juiceColor": "#FFE066", "size": r4(size),
            "cutVolume": {"type": "box", "center": [0, 0.08, 0], "size": r4([bmx[0] - bmn[0], 0.16, bmx[2] - bmn[2]])},
            "stages": [f"Stage{k}" for k in range(5)], "cuts": cuts}
    mats = [("PineappleSkin", "#B97A2A"), ("PineappleFlesh", "#FFD53D"), ("PineappleCrown", "#4C9A3C")]
    return mats, stages, meta


def build_watermelon():
    base = watermelon()
    nx = U(1, 0, 0)
    xs = [0.0, -0.055, 0.055]  # halve it, then halve each half
    rind = (nx, None, 0.009, 3)  # pale rind ring (cap material 1) around red flesh (3)
    stages = [[("Piece0", cut(base, []))]]
    for k in (1, 2, 3):
        stages.append([(f"Piece{i}", p) for i, p in enumerate(slices(base, nx, xs[:k], inset_for=rind))])
    lo, hi = bbox([base])
    cuts = []
    for x in xs:
        z0, z1 = section_height(base, nx, x)
        cuts.append({"normal": [1, 0, 0], "point": r4([x, (z0 + z1) / 2, 0]), "depth": round(z1 - z0, 4)})
    mn, mx = unity_box(lo, hi)
    size = [b - a for a, b in zip(mn, mx)]
    meta = {"name": "Watermelon", "file": "Watermelon.glb", "price": 40, "juiceColor": "#F2545B", "cleaver": True,
            "size": r4(size),
            "cutVolume": {"type": "box", "center": r4([(a + b) / 2 for a, b in zip(mn, mx)]), "size": r4(size)},
            "stages": ["Stage0", "Stage1", "Stage2", "Stage3"], "cuts": cuts}
    mats = [("WatermelonSkin", "#1F5A2A"), ("WatermelonRind", "#E4F2C8"), ("WatermelonStripe", "#5E9E3E"),
            ("WatermelonFlesh", "#E8394A")]
    return mats, stages, meta


# ---------------------------------------------------------------- verify + render

def verify(path, meta):
    with open(path, "rb") as fh:
        data = fh.read()
    doc = json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]])
    roots = [doc["nodes"][i]["name"] for i in doc["scenes"][0]["nodes"]]
    assert roots == meta["stages"], roots
    reset()
    bpy.ops.import_scene.gltf(filepath=path)
    print(f"== {os.path.basename(path)}  ({len(data)} bytes)")
    for st in sorted((o for o in bpy.data.objects if o.parent is None), key=lambda o: o.name):
        kids = sorted(st.children, key=lambda o: o.name)
        dg = bpy.context.evaluated_depsgraph_get()
        tris, pts = 0, []
        for o in kids:
            me = o.evaluated_get(dg).to_mesh()
            me.calc_loop_triangles()
            tris += len(me.loop_triangles)
            pts += [o.matrix_world @ v.co for v in me.vertices]
        lo = Vector([min(p[i] for p in pts) for i in range(3)])
        hi = Vector([max(p[i] for p in pts) for i in range(3)])
        mn, mx = unity_box(lo, hi)
        names = [o.name.split(".")[0] for o in kids]
        print(f"  {st.name}: {names} tris={tris} unity_min={r4(mn)} unity_max={r4(mx)}")
        assert tris <= MAX_TRIS, f"{st.name} has {tris} tris"
        assert abs(mn[1]) < 1e-4, "bottom not at y=0"


def render(path, outdir):
    reset()
    bpy.ops.import_scene.gltf(filepath=path)
    scene = bpy.context.scene
    stages = sorted((o for o in bpy.data.objects if o.parent is None), key=lambda o: o.name)
    for m in bpy.data.materials:
        if m.node_tree and "Principled BSDF" in m.node_tree.nodes:
            m.diffuse_color = m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value
    bpy.context.view_layer.update()
    spacing = 0.0
    for st in stages:
        pts = [o.matrix_world @ Vector(c) for o in st.children for c in o.bound_box]
        spacing = max(spacing, max(p.x for p in pts) - min(p.x for p in pts), max(p.y for p in pts) - min(p.y for p in pts))
    spacing *= 1.9
    top = max((o.matrix_world @ Vector(c)).z for o in stages[0].children for c in o.bound_box)
    for st in stages:
        cen = sum((o.matrix_world @ (sum((Vector(c) for c in o.bound_box), Vector()) / 8) for o in st.children), Vector()) / len(st.children)
        for o in st.children:
            c = o.matrix_world @ (sum((Vector(c) for c in o.bound_box), Vector()) / 8)
            o.matrix_world.translation += (c - cen) * 1.2
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = spacing * len(stages) * 1.02
    scene.camera = cam
    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x, scene.render.resolution_y = 2000, 760
    for m in bpy.data.materials:
        m.use_backface_culling = True  # flipped faces would show up as holes
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 2.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    scene.world = bpy.data.worlds.new("W")
    scene.world.use_nodes = True
    # neutral ambient for lighting, but a dark blue backdrop so holes in the mesh stand out
    nt = scene.world.node_tree
    amb = nt.nodes["Background"]
    amb.inputs["Color"].default_value = (0.55, 0.55, 0.55, 1)
    back = nt.nodes.new("ShaderNodeBackground")
    back.inputs["Color"].default_value = (0.02, 0.03, 0.08, 1)
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(nt.nodes.new("ShaderNodeLightPath").outputs["Is Camera Ray"], mix.inputs[0])
    nt.links.new(amb.outputs[0], mix.inputs[1])
    nt.links.new(back.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], nt.nodes["World Output"].inputs["Surface"])
    name = os.path.splitext(os.path.basename(path))[0]
    last = sorted(stages[-1].children, key=lambda o: o.name)
    c0, c1 = (o.matrix_world @ (sum((Vector(c) for c in o.bound_box), Vector()) / 8) for o in (last[0], last[-1]))
    axis = Vector((c1.x - c0.x, c1.y - c0.y, 0)).normalized()
    side = Vector((-axis.y, axis.x, 0))
    views = (("right", Vector((0.55, -1, 0.75))), ("left", Vector((-0.55, -1, 0.75))),
             ("capsA", axis + side + Vector((0, 0, 0.9))), ("capsB", -axis + side + Vector((0, 0, 0.9))))
    for view, d in views:
        # lay the stages out across the screen for this view direction
        perp = Vector((-d.y, d.x, 0)).normalized()
        for k, st in enumerate(stages):
            st.location = perp * (k - (len(stages) - 1) / 2) * spacing
        target = Vector((0, 0, top / 2))
        cam.location = target + d.normalized() * 2
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(outdir, f"{name}_{view}.png")
        bpy.ops.render.render(write_still=True)

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render_dir = argv[argv.index("--render") + 1] if "--render" in argv else None
    os.makedirs(OUT, exist_ok=True)
    metas = []
    for build in (build_guava, build_mango, build_pineapple, build_watermelon):
        mats, stages, meta = build()
        path = os.path.join(OUT, meta["file"])
        export(meta["name"], mats, stages, path)
        verify(path, meta)
        if render_dir:
            os.makedirs(render_dir, exist_ok=True)
            render(path, render_dir)
        metas.append(meta)
    with open(os.path.join(OUT, "fruits.json"), "w") as fh:
        json.dump({"fruits": metas}, fh, indent=2)
        fh.write("\n")
    print(json.dumps({"fruits": metas}, indent=2))


main()
