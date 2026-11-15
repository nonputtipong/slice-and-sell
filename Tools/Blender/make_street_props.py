"""Builds the low-poly street props around the Slice & Sell fruit stall (one GLB per prop).

Run:  Blender -b --python Tools/Blender/make_street_props.py [-- --render <dir>] [--only Name,Name]
Writes Assets/_Project/Models/Street/<Prop>.glb and props.json, re-imports each GLB to check it,
and optionally renders a contact sheet per 8 props plus two assembled stall shots.

All geometry is authored in Unity space (x right, y up, z front, metres) and converted with U()
when the mesh is finished. Every prop is made of closed solids so normals can be recalculated
outward and any hole shows up as a non-manifold edge.
"""
import bpy, bmesh, json, math, os, random, struct, sys
from contextlib import contextmanager
from mathutils import Vector, Matrix

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "Street")
SMALL, MEDIUM, LARGE = 600, 1500, 3000


# glTFast mirrors X when importing (glTF is right-handed), and Blender's exporter maps
# Blender (x, y, z) -> glTF (x, z, -y). So Unity (x, y, z) == Blender (-x, -z, y).
def U(x, y, z):
    return Vector((-x, -z, y))


def to_unity(v):
    return [-v.x, v.z, -v.y]


def V(x, y, z):
    return Vector((x, y, z))


def lin(hexstr):
    c = [int(hexstr[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [1.0]


# One palette for all props, so the same colour always has the same material name.
PAL = {
    "WoodLight": "#C99A63", "WoodDark": "#7A5032", "CartPaint": "#6E8796",
    "MetalSteel": "#A3AAAF", "MetalDark": "#4B5257", "BlackRubber": "#242426",
    "FabricRed": "#D63A2F", "FabricOrange": "#F07F23", "FabricYellow": "#F5C51F",
    "FabricGreen": "#3FA34D", "FabricBlue": "#2F78C9", "FabricPurple": "#8A4FB5",
    "BulbWarmGlow": "#FFD08A", "BulbRedGlow": "#FF4A3D", "BulbYellowGlow": "#FFD84A",
    "BulbGreenGlow": "#59E06A", "BulbBlueGlow": "#4A8CFF", "BulbPinkGlow": "#FF6FC0",
    "LanternRedGlow": "#E8382C", "LanternOrangeGlow": "#F5862A", "LanternGreenGlow": "#4CBF5A",
    "LanternTealGlow": "#2FC0B4", "LanternPurpleGlow": "#9B5BD6", "LanternCap": "#2A1C17",
    "Gold": "#D8AA36", "PaintWhite": "#F4F2EC", "NeedleRed": "#D3302A", "ScaleBody": "#3D8C63",
    "TubPlastic": "#DDE2E5", "Ice": "#C4E9F7", "PlasticBagWhite": "#EEEEEA", "StoolPlastic": "#D8372E",
    "AirConBody": "#DAD8CF", "AirConGrille": "#3B3F43", "Copper": "#B87333", "PipeInsulation": "#C9C6BD",
    "Concrete": "#A8A49B", "Insulator": "#DCD6C6", "TransformerGrey": "#8D9398",
    "MeterBox": "#C4C8C1", "MeterGlass": "#2D3A42",
    "SignFrame": "#A8392C", "SignPanel": "#F2E8CC", "ShutterMetal": "#A9AFB3", "ShutterBox": "#7F868B",
    "SpiritWhite": "#EFEAE0", "SpiritRed": "#B3262A",
    "CrateYellow": "#F2C230", "CrateGreen": "#3E9E4A", "CrateRed": "#D2392E", "CrateBlue": "#2E6FC0",
    "CoolerMetal": "#9DAFB3", "Rust": "#8C5634",
    "BowlPurple": "#8E52C8", "NotePink": "#E9A3BE", "NoteGreen": "#93C99C", "NoteBlue": "#8DB6E2",
    "CoinSilver": "#C8CCD0", "TrayOrange": "#F08A2A",
}


def make_material(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    col = lin(PAL[name])
    bsdf.inputs["Base Color"].default_value = col
    bsdf.inputs["Roughness"].default_value = 0.7
    if name.endswith("Glow"):
        bsdf.inputs["Emission Color"].default_value = col
        bsdf.inputs["Emission Strength"].default_value = 1.0
    m.diffuse_color = col
    return m


# ---------------------------------------------------------------- mesh builder (Unity space)

class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []
        self.M = Matrix.Identity(4)

    def mi(self, mat):
        if mat not in self.mats:
            assert mat in PAL, mat
            self.mats.append(mat)
        return self.mats.index(mat)

    @contextmanager
    def at(self, M):
        old = self.M
        self.M = old @ M
        yield
        self.M = old

    def mesh(self, pts, faces, mat):
        vs = [self.bm.verts.new(self.M @ Vector(p)) for p in pts]
        for k, f in enumerate(faces):
            face = self.bm.faces.new([vs[i] for i in f])
            face.material_index = self.mi(mat(k) if callable(mat) else mat)

    def hexa(self, c, mat):
        """8 corners: bottom ring then top ring, same order."""
        self.mesh(c, [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], mat)

    def box(self, lo, hi, mat):
        (x0, y0, z0), (x1, y1, z1) = lo, hi
        assert x1 > x0 and y1 > y0 and z1 > z0, (lo, hi)
        self.hexa([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1),
                   (x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], mat)

    def beam(self, a, b, w, mat, h=None, up=V(0, 1, 0)):
        a, b = Vector(a), Vector(b)
        h = h or w
        d = (b - a).normalized()
        ref = up if abs(d.dot(up)) < 0.95 else V(1, 0, 0)
        sx = d.cross(ref).normalized() * w / 2
        sy = sx.cross(d).normalized() * h / 2
        ring = lambda p: [p - sx - sy, p + sx - sy, p + sx + sy, p - sx + sy]
        self.hexa(ring(a) + ring(b), mat)

    def lathe(self, prof, n, mat, o=V(0, 0, 0), axis=V(0, 1, 0), loop=False, phase=0.0, rmod=None):
        """Revolve (radius, height) points around `axis` through `o`. r = 0 makes a pole.
        mat: name or f(profile_segment, angle_segment)."""
        axis = Vector(axis).normalized()
        o = Vector(o)
        ref = V(1, 0, 0) if abs(axis.x) < 0.9 else V(0, 0, 1)
        u = axis.cross(ref).normalized()
        v = axis.cross(u)
        pts, rings = [], []
        for i, (r, h) in enumerate(prof):
            if r < 1e-9:
                rings.append(len(pts))
                pts.append(o + axis * h)
                continue
            ring = []
            for j in range(n):
                t = 2 * math.pi * (j + phase) / n
                rr = r * (rmod(i, j) if rmod else 1)
                ring.append(len(pts))
                pts.append(o + axis * h + (u * math.cos(t) + v * math.sin(t)) * rr)
            rings.append(ring)
        faces, fm = [], []
        pairs = list(zip(range(len(prof)), range(1, len(prof)))) + ([(len(prof) - 1, 0)] if loop else [])
        for i, k in pairs:
            a, b = rings[i], rings[k]
            if isinstance(a, int) and isinstance(b, int):
                continue
            for j in range(n):
                j1 = (j + 1) % n
                if isinstance(a, int):
                    faces.append((a, b[j1], b[j]))
                elif isinstance(b, int):
                    faces.append((a[j], a[j1], b))
                else:
                    faces.append((a[j], a[j1], b[j1], b[j]))
                fm.append(mat(i, j) if callable(mat) else mat)
        self.mesh(pts, faces, lambda k: fm[k])

    def cyl(self, a, b, r, n, mat, r1=None, phase=0.0):
        a, b = Vector(a), Vector(b)
        L = (b - a).length
        self.lathe([(0, 0), (r, 0), (r if r1 is None else r1, L), (0, L)], n, mat, o=a, axis=b - a, phase=phase)

    def tube(self, path, r, n, mat, phase=0.0):
        """Closed tube along a polyline, parallel-transported frame."""
        path = [Vector(p) for p in path]
        tans = []
        for i in range(len(path)):
            t = path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]
            tans.append(t.normalized())
        ref = V(0, 1, 0) if abs(tans[0].y) < 0.9 else V(1, 0, 0)
        nrm = (ref - tans[0] * ref.dot(tans[0])).normalized()
        pts, faces = [], []
        for i, (p, t) in enumerate(zip(path, tans)):
            nrm = (nrm - t * nrm.dot(t)).normalized()
            bi = t.cross(nrm)
            for j in range(n):
                a = 2 * math.pi * (j + phase) / n
                pts.append(p + (nrm * math.cos(a) + bi * math.sin(a)) * r)
        for i in range(len(path) - 1):
            for j in range(n):
                j1 = (j + 1) % n
                faces.append((i * n + j, i * n + j1, (i + 1) * n + j1, (i + 1) * n + j))
        faces.append(tuple(range(n))[::-1])
        faces.append(tuple((len(path) - 1) * n + j for j in range(n)))
        self.mesh(pts, faces, mat)

    def prism(self, poly, d, mat, cap_mat=None):
        """Planar polygon extruded by vector d; caps get cap_mat."""
        poly = [Vector(p) for p in poly]
        n = len(poly)
        pts = poly + [p + Vector(d) for p in poly]
        faces = [tuple(range(n))[::-1], tuple(range(n, 2 * n))]
        faces += [(j, (j + 1) % n, n + (j + 1) % n, n + j) for j in range(n)]
        cm = cap_mat or mat
        self.mesh(pts, faces, lambda k: cm if k < 2 else mat)

    def finish(self):
        bm = self.bm
        for v in bm.verts:
            v.co = U(*v.co)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")
        for f in bm.faces:
            f.smooth = False
        bad = [e for e in bm.edges if len(e.link_faces) != 2]
        assert not bad, f"{self.name}: {len(bad)} open edges"
        vol = sum(f.verts[0].co.dot(f.verts[1].co.cross(f.verts[2].co)) for f in bm.faces) / 6
        assert vol > 0, f"{self.name}: normals point inward"
        return self


def rotY(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Y")


def T(x, y, z):
    return Matrix.Translation(V(x, y, z))


def sag(t, s):
    return -4 * s * t * (1 - t)


# ---------------------------------------------------------------- props

def push_cart():
    p = Part("PushCart")
    # deck: fruit sits on y = 0.90; 2 cm lip rim around it
    p.box((-0.70, 0.86, -0.30), (0.70, 0.90, 0.30), "WoodLight")
    p.box((-0.70, 0.90, 0.27), (0.70, 0.92, 0.30), "WoodDark")
    p.box((-0.70, 0.90, -0.30), (0.70, 0.92, -0.27), "WoodDark")
    for s in (-1, 1):
        p.box((min(s * 0.67, s * 0.70), 0.90, -0.27), (max(s * 0.67, s * 0.70), 0.92, 0.27), "WoodDark")
    # painted skirts: customer side (+Z) closed, ends closed, player side (-Z) only an apron
    p.box((-0.68, 0.25, 0.265), (0.68, 0.86, 0.285), "CartPaint")
    for s in (-1, 1):
        p.box((min(s * 0.66, s * 0.68), 0.25, -0.285), (max(s * 0.66, s * 0.68), 0.86, 0.285), "CartPaint")
    p.box((-0.68, 0.72, -0.285), (0.68, 0.86, -0.265), "CartPaint")
    # battens on the skirts
    for y0, y1 in ((0.25, 0.29), (0.81, 0.85)):
        p.box((-0.68, y0, 0.285), (0.68, y1, 0.295), "WoodDark")
    p.box((-0.02, 0.29, 0.285), (0.02, 0.81, 0.295), "WoodDark")
    p.box((-0.68, 0.72, -0.295), (0.68, 0.75, -0.285), "WoodDark")
    for s in (-1, 1):
        for y0, y1 in ((0.25, 0.29), (0.81, 0.85)):
            p.box((min(s * 0.68, s * 0.69), y0, -0.285), (max(s * 0.68, s * 0.69), y1, 0.285), "WoodDark")
    # open lower shelf on the player side, with a small lip
    p.box((-0.66, 0.28, -0.285), (0.66, 0.30, 0.265), "WoodLight")
    p.box((-0.66, 0.30, -0.285), (0.66, 0.33, -0.272), "WoodDark")
    # corner legs to the floor
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box((sx * 0.655 - 0.02, 0.0, sz * 0.26 - 0.02), (sx * 0.655 + 0.02, 0.86, sz * 0.26 + 0.02), "WoodDark")
    # axle and two spoked wheels (dia 0.5) at the X ends
    p.cyl((-0.765, 0.25, 0), (0.765, 0.25, 0), 0.015, 6, "MetalSteel")
    for sx in (-1, 1):
        c = V(sx * 0.74, 0.25, 0)
        tyre = {0: "BlackRubber", 1: "BlackRubber", 2: "MetalSteel", 3: "BlackRubber"}
        p.lathe([(0.25, -0.022), (0.25, 0.022), (0.205, 0.022), (0.205, -0.022)], 16,
                lambda i, j: tyre[i], o=c, axis=V(1, 0, 0), loop=True)
        p.cyl(c - V(0.025, 0, 0), c + V(0.025, 0, 0), 0.045, 8, "MetalDark")
        for k in range(8):
            a = 2 * math.pi * (k + 0.25) / 8
            d = V(0, math.cos(a), math.sin(a))
            p.beam(c + d * 0.04, c + d * 0.21, 0.016, "MetalSteel", up=V(1, 0, 0))
    # push handle on the player side, just under the deck, 4.5 cm proud of the -Z edge
    p.cyl((-0.56, 0.83, -0.33), (0.56, 0.83, -0.33), 0.015, 8, "MetalSteel")
    for sx in (-1, 1):
        p.beam((sx * 0.50, 0.83, -0.33), (sx * 0.50, 0.83, -0.285), 0.02, "MetalSteel")
    # thin metal frame for string lights: corner posts up to 2.05 and top rails
    px, pz, t = 0.6875, 0.2875, 0.0125
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box((sx * px - t, 0.86, sz * pz - t), (sx * px + t, 2.05, sz * pz + t), "MetalSteel")
    for sz in (-1, 1):
        p.box((-px - t, 2.025, sz * pz - t), (px + t, 2.05, sz * pz + t), "MetalSteel")
    for sx in (-1, 1):
        p.box((sx * px - t, 2.025, -pz + t), (sx * px + t, 2.05, pz - t), "MetalSteel")
    return p


FABRIC = ["FabricRed", "FabricOrange", "FabricYellow", "FabricGreen", "FabricBlue", "FabricPurple"]


def rainbow_umbrella():
    p = Part("RainbowUmbrella")
    cols = [FABRIC[k % 6] for k in range(8)]
    # canopy: thin closed shell (top skin, valance, under skin), 8 panels x 2 segments
    prof = [(0, 2.6), (0.6, 2.5), (1.15, 2.3), (1.15, 2.22), (1.14, 2.22), (1.14, 2.29), (0.6, 2.485), (0, 2.585)]
    p.lathe(prof, 16, lambda i, j: cols[j // 2])
    # ribs and stretchers under the canopy, on the panel seams
    ax = V(0, 1, 0)
    ref = V(1, 0, 0)
    u = ax.cross(ref).normalized()
    v = ax.cross(u)
    for k in range(8):
        a = 2 * math.pi * k / 8
        d = u * math.cos(a) + v * math.sin(a)
        r0, r1, r2 = V(0, 2.572, 0) + d * 0.04, V(0, 2.472, 0) + d * 0.6, V(0, 2.275, 0) + d * 1.13
        p.beam(r0, r1, 0.012, "MetalSteel")
        p.beam(r1, r2, 0.012, "MetalSteel")
        p.beam(V(0, 2.15, 0) + d * 0.02, V(0, 2.46, 0) + d * 0.5, 0.01, "MetalSteel")
    p.cyl((0, 0, 0), (0, 2.6, 0), 0.015, 8, "MetalSteel")
    p.cyl((0, 2.12, 0), (0, 2.18, 0), 0.026, 8, "MetalDark")
    p.lathe([(0, 2.598), (0.03, 2.6), (0.035, 2.625), (0.022, 2.655), (0, 2.69)], 8, "MetalDark")
    return p


BULB_STRING = 1.0


def bulb_string(name, mats):
    p = Part(name)
    s = 0.06
    path = [V(BULB_STRING * t, sag(t, s), 0) for t in (i / 12 for i in range(13))]
    p.tube(path, 0.002, 4, "BlackRubber", phase=0.5)
    for i in range(6):
        t = (i + 0.5) / 6
        x, y = BULB_STRING * t, sag(t, s)
        p.cyl((x, y + 0.002, 0), (x, y - 0.022, 0), 0.0065, 6, "BlackRubber")
        top = y - 0.02
        p.lathe([(0, top), (0.008, top - 0.002), (0.014, top - 0.011), (0.015, top - 0.02),
                 (0.011, top - 0.029), (0, top - 0.034)], 8, mats[i % len(mats)], o=V(x, 0, 0))
    return p


def lantern(colour):
    p = Part(f"Lantern{colour}")
    glow = f"Lantern{colour}Glow"
    p.cyl((0, 0, 0), (0, -0.034, 0), 0.003, 4, "BlackRubber")
    p.lathe([(0, -0.03), (0.028, -0.03), (0.05, -0.045), (0.05, -0.058), (0, -0.058)], 12, "LanternCap")
    body = [(0, -0.055), (0.045, -0.055), (0.08, -0.074), (0.104, -0.108), (0.11, -0.16), (0.104, -0.212),
            (0.08, -0.246), (0.045, -0.265), (0, -0.265)]
    p.lathe(body, 16, glow, rmod=lambda i, j: 0.94 if (j % 2 and 2 <= i <= 6) else 1.0)
    p.lathe([(0, -0.262), (0.05, -0.262), (0.05, -0.272), (0.032, -0.28), (0, -0.28)], 12, "LanternCap")
    p.lathe([(0, -0.278), (0.008, -0.279), (0.006, -0.29), (0.017, -0.32), (0, -0.32)], 6, "Gold")
    return p


def scale():
    p = Part("Scale")
    # body: front face vertical (z = 0.10), sides and back taper in
    p.hexa([(-0.11, 0, -0.10), (0.11, 0, -0.10), (0.11, 0, 0.10), (-0.11, 0, 0.10),
            (-0.09, 0.15, -0.07), (0.09, 0.15, -0.07), (0.09, 0.15, 0.10), (-0.09, 0.15, 0.10)], "ScaleBody")
    c = V(0, 0.075, 0.10)
    p.cyl(c, c + V(0, 0, 0.009), 0.058, 16, "PaintWhite")
    p.lathe([(0.056, 0), (0.066, 0), (0.066, 0.014), (0.056, 0.014)], 16, "MetalSteel", o=c, axis=V(0, 0, 1), loop=True)
    for k in range(8):
        a = 2 * math.pi * k / 8
        d = V(math.cos(a), math.sin(a), 0)
        p.beam(c + d * 0.042 + V(0, 0, 0.009), c + d * 0.052 + V(0, 0, 0.009), 0.004, "BlackRubber", h=0.002, up=V(0, 0, 1))
    a = math.radians(60)
    p.beam(c + V(0, 0, 0.0105), c + V(math.cos(a), math.sin(a), 0) * 0.048 + V(0, 0, 0.0105), 0.004, "NeedleRed",
           h=0.002, up=V(0, 0, 1))
    p.cyl(c + V(0, 0, 0.009), c + V(0, 0, 0.0135), 0.006, 6, "BlackRubber")
    # pan
    p.cyl((0, 0.15, 0), (0, 0.178, 0), 0.02, 8, "MetalSteel")
    p.lathe([(0, 0.175), (0.07, 0.175), (0.115, 0.205), (0.12, 0.235), (0.114, 0.235), (0.108, 0.21),
             (0.065, 0.183), (0, 0.183)], 16, "MetalSteel")
    return p


def ice_tray():
    p = Part("IceTray")
    X, Z, H, t = 0.18, 0.13, 0.12, 0.012
    p.box((-X, 0, -Z), (X, t, Z), "TubPlastic")
    p.box((-X, 0, Z - t), (X, H, Z), "TubPlastic")
    p.box((-X, 0, -Z), (X, H, -Z + t), "TubPlastic")
    for s in (-1, 1):
        p.box((min(s * X, s * (X - t)), 0, -Z + t), (max(s * X, s * (X - t)), H, Z - t), "TubPlastic")
    # rolled lip
    L = 0.006
    p.box((-X - L, H - 0.012, Z), (X + L, H, Z + L), "TubPlastic")
    p.box((-X - L, H - 0.012, -Z - L), (X + L, H, -Z), "TubPlastic")
    for s in (-1, 1):
        p.box((min(s * X, s * (X + L)), H - 0.012, -Z), (max(s * X, s * (X + L)), H, Z), "TubPlastic")
    # ice bed and a heap of cubes
    p.box((-X + t, t, -Z + t), (X - t, 0.075, Z - t), "Ice")
    rnd = random.Random(3)
    for _ in range(26):
        x, z = rnd.uniform(-0.145, 0.145), rnd.uniform(-0.095, 0.095)
        y = 0.07 + 0.045 * (1 - (x / 0.17) ** 2) * (1 - (z / 0.12) ** 2) + rnd.uniform(-0.01, 0.0)
        e = 0.026 + rnd.uniform(-0.004, 0.004)
        R = Matrix.Rotation(rnd.uniform(0, 6.3), 4, "Y") @ Matrix.Rotation(rnd.uniform(-0.6, 0.6), 4, "X") @ \
            Matrix.Rotation(rnd.uniform(-0.6, 0.6), 4, "Z")
        with p.at(T(x, y, z) @ R):
            p.box((-e / 2, -e / 2, -e / 2), (e / 2, e / 2, e / 2), "Ice")
    return p


def rounded_ring(hx, hz, y, n=12, jit=0.0, rnd=None, e=0.6):
    pts = []
    for j in range(n):
        a = 2 * math.pi * j / n
        c, s = math.cos(a), math.sin(a)
        # superellipse: boxy but soft
        x = hx * math.copysign(abs(c) ** e, c)
        z = hz * math.copysign(abs(s) ** e, s)
        if rnd:
            x += rnd.uniform(-jit, jit)
            z += rnd.uniform(-jit, jit)
        pts.append(V(x, y, z))
    return pts


def plastic_bag():
    p = Part("PlasticBag")
    rnd = random.Random(9)
    rings = [(0.07, 0.01, -0.08), (0.095, 0.03, -0.105), (0.11, 0.045, -0.15), (0.115, 0.05, -0.22),
             (0.112, 0.048, -0.268), (0.10, 0.04, -0.28)]
    n = 12
    pts = [V(0, -0.074, 0), V(0, -0.283, 0)]
    for hx, hz, y in rings:
        pts += [q + V(0, rnd.uniform(-0.004, 0.004), 0) for q in rounded_ring(hx, hz, y, n, 0.004, rnd, e=0.4)]
    faces = []
    for j in range(n):
        j1 = (j + 1) % n
        faces.append((0, 2 + j1, 2 + j))
        faces.append((1, 2 + (len(rings) - 1) * n + j, 2 + (len(rings) - 1) * n + j1))
        for i in range(len(rings) - 1):
            a, b = 2 + i * n, 2 + (i + 1) * n
            faces.append((a + j, a + j1, b + j1, b + j))
    p.mesh(pts, faces, "PlasticBagWhite")
    # two handles gathered at the hook point (origin)
    for sz in (-1, 1):
        path = []
        for k in range(9):
            s = -1 + 2 * k / 8
            path.append(V(0.068 * s, -0.006 - 0.085 * abs(s) ** 1.4, sz * (0.004 + 0.008 * abs(s))))
        p.tube(path, 0.006, 4, "PlasticBagWhite", phase=0.5)
    return p


def plastic_bag_flat():
    p = Part("PlasticBagFlat")
    p.box((-0.10, 0, -0.07), (0.10, 0.006, 0.07), "PlasticBagWhite")
    with p.at(T(-0.035, 0.006, 0) @ Matrix.Rotation(math.radians(-3), 4, "Z")):
        p.box((-0.065, 0, -0.066), (0.065, 0.005, 0.066), "PlasticBagWhite")
    path = [V(0.09 + 0.045 * math.sin(math.pi * k / 8), 0.004, -0.04 + 0.08 * k / 8 - 0.0 * k) for k in range(9)]
    p.tube(path, 0.004, 4, "PlasticBagWhite", phase=0.5)
    return p


def plastic_stool():
    p = Part("PlasticStool")
    m = "StoolPlastic"
    p.lathe([(0, 0.425), (0.14, 0.425), (0.15, 0.435), (0.15, 0.45), (0, 0.45)], 16, m)
    p.lathe([(0.146, 0.36), (0.146, 0.426), (0.134, 0.426), (0.134, 0.36)], 16, m, loop=True)
    p.lathe([(0.148, 0.10), (0.160, 0.10), (0.160, 0.125), (0.148, 0.125)], 16, m, loop=True, phase=0.5)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        d = V(math.cos(a), 0, math.sin(a))
        p.beam(d * 0.123 + V(0, 0.42, 0), d * 0.17 + V(0, 0.0013, 0), 0.05, m, h=0.022, up=d)  # flat on the floor
    return p


def air_con():
    p = Part("AirCon")
    with p.at(Matrix.Scale(-1, 4, (1, 0, 0))):  # authored fan-right; mirrored so the fan is on the viewer's left
        air_con_parts(p)
    return p


def air_con_parts(p):
    p.box((-0.40, 0, 0), (0.40, 0.55, 0.28), "AirConBody")
    # round fan grille
    c = V(-0.13, 0.275, 0.28)
    p.cyl(c - V(0, 0, 0.004), c + V(0, 0, 0.004), 0.20, 16, "AirConGrille")
    p.lathe([(0.198, 0), (0.215, 0), (0.215, 0.014), (0.198, 0.014)], 16, "AirConBody", o=c, axis=V(0, 0, 1), loop=True)
    for k in range(4):
        a = math.pi * k / 4
        d = V(math.cos(a), math.sin(a), 0)
        p.beam(c - d * 0.199 + V(0, 0, 0.007), c + d * 0.199 + V(0, 0, 0.007), 0.008, "AirConBody", h=0.006, up=V(0, 0, 1))
    p.lathe([(0.10, 0), (0.11, 0), (0.11, 0.008), (0.10, 0.008)], 12, "AirConBody", o=c + V(0, 0, 0.003),
            axis=V(0, 0, 1), loop=True)
    p.cyl(c, c + V(0, 0, 0.012), 0.035, 8, "AirConBody")
    # side vent with slats
    p.box((0.13, 0.08, 0.28), (0.36, 0.47, 0.283), "AirConGrille")
    for k in range(8):
        y = 0.10 + k * 0.05
        p.box((0.13, y, 0.28), (0.36, y + 0.012, 0.29), "AirConBody")
    # wall bracket below the unit (back at z = 0 is the wall)
    for sx in (-1, 1):
        x = sx * 0.30
        p.box((x - 0.015, -0.03, 0.0), (x + 0.015, 0.0, 0.31), "MetalDark")
        p.box((x - 0.015, -0.30, 0.0), (x + 0.015, -0.03, 0.025), "MetalDark")
        p.beam((x, -0.28, 0.015), (x, -0.03, 0.27), 0.02, "MetalDark", up=V(1, 0, 0))
    # pipes into the wall
    p.tube([(0.38, 0.09, 0.08), (0.45, 0.09, 0.08), (0.45, 0.09, 0.0)], 0.009, 6, "Copper")
    p.tube([(0.38, 0.14, 0.12), (0.47, 0.14, 0.12), (0.47, 0.14, 0.0)], 0.014, 6, "PipeInsulation")


def electric_pole():
    p = Part("ElectricPole")
    H = 7.5
    hw = lambda y: 0.12 - 0.045 * y / H
    # square tapered concrete shaft (4-sided lathe, faces on the axes)
    p.lathe([(0, 0), (0.12 * 2 ** 0.5, 0), (0.075 * 2 ** 0.5, H), (0, H)], 4, "Concrete", phase=0.5)
    for y, L in ((7.0, 0.85), (6.4, 0.65)):
        p.box((-L, y - 0.045, -0.045 + hw(y) + 0.045), (L, y + 0.045, 0.045 + hw(y) + 0.045), "MetalDark")
        for s in (-1, 1):
            p.beam((s * 0.45, y - 0.04, hw(y) + 0.045), (s * hw(y - 0.4), y - 0.4, hw(y - 0.4) + 0.02), 0.03, "MetalDark",
                   up=V(0, 0, 1))
        xs = (-0.75, -0.35, 0.35, 0.75) if y == 7.0 else (-0.55, 0.0, 0.55)
        for x in xs:
            b = y + 0.045
            p.lathe([(0, b), (0.028, b), (0.045, b + 0.025), (0.024, b + 0.04), (0.04, b + 0.06), (0.018, b + 0.085),
                     (0, b + 0.09)], 6, "Insulator", o=V(x, 0, hw(y) + 0.045))
    # transformer box on the front, on two brackets
    z0 = hw(5.0) + 0.03
    for y in (4.75, 5.2):
        p.box((-0.18, y - 0.03, hw(y) - 0.01), (0.18, y + 0.03, z0 + 0.02), "MetalDark")
    p.box((-0.25, 4.6, z0), (0.25, 5.3, z0 + 0.36), "TransformerGrey")
    p.box((-0.27, 5.3, z0 - 0.02), (0.27, 5.34, z0 + 0.38), "MetalDark")
    for k in range(5):
        x = -0.2 + k * 0.1
        p.box((x - 0.012, 4.65, z0 + 0.36), (x + 0.012, 5.2, z0 + 0.42), "TransformerGrey")
    for x in (-0.14, 0.0, 0.14):
        b = 5.34
        p.lathe([(0, b), (0.025, b), (0.035, b + 0.03), (0.02, b + 0.05), (0.03, b + 0.07), (0, b + 0.1)], 6,
                "Insulator", o=V(x, 0, z0 + 0.18))
    # two meter boxes at ~2 m with cables running up the pole
    for sx in (-1, 1):
        x = sx * 0.10
        zf = hw(2.0)
        p.box((x - 0.075, 1.86, zf), (x + 0.075, 2.12, zf + 0.10), "MeterBox")
        p.box((x - 0.045, 1.98, zf + 0.10), (x + 0.045, 2.07, zf + 0.108), "MeterGlass")
        p.tube([(x, 2.12, zf + 0.05), (x * 0.8, 2.6, hw(2.6) + 0.03), (x * 0.6, 6.3, hw(6.3) + 0.03),
                (sx * 0.3, 6.9, hw(6.9) + 0.04)], 0.008, 4, "BlackRubber", phase=0.5)
    # coil of spare cable strapped to the pole, with a dangling loop
    path = []
    cx, cy, R = 0.12, 3.3, 0.26
    zc = hw(3.3) + 0.05
    for k in range(37):
        a = -math.pi / 2 + 2 * math.pi * k / 12
        rr = R * (1 + 0.05 * math.sin(k * 1.3))
        path.append(V(cx + rr * math.cos(a), cy + rr * math.sin(a), zc + 0.012 * k / 12))
    end = path[-1]
    for k in range(1, 8):
        s = k / 7
        path.append(V(end.x + 0.22 * s, end.y - 0.75 * math.sin(math.pi * s * 0.5) - 0.0, end.z + 0.04 * s))
    for k in range(1, 6):
        s = k / 5
        last = path[-1]
        path.append(V(last.x + 0.03, last.y + 0.12 * s, last.z + 0.005))
    p.tube(path, 0.008, 4, "BlackRubber", phase=0.5)
    return p


def wire_bundle():
    p = Part("WireBundle")
    sags = [0.9, 0.84, 0.95, 0.88, 0.98]
    rnd = random.Random(4)
    for k, s in enumerate(sags):
        dz = (k - 2) * 0.014
        w = rnd.uniform(-0.05, 0.05)
        dy = rnd.uniform(-0.02, 0.02)
        path = []
        for i in range(21):
            t = i / 20
            bump = math.sin(math.pi * t)
            path.append(V(10 * t, sag(t, s) + dy * bump, dz + w * bump + 0.02 * math.sin(t * 9 + k) * bump))
        p.tube(path, 0.0075, 4, "BlackRubber", phase=0.5)
    return p


def sign_board():
    p = Part("SignBoard")
    # board 1 mm inside the frame's outline so no faces are coplanar (z-fighting)
    p.box((-0.599, 0.001, 0), (0.599, 0.399, 0.025), "SignPanel")
    f = 0.035
    p.box((-0.6, 0.4 - f, 0.02), (0.6, 0.4, 0.04), "SignFrame")
    p.box((-0.6, 0, 0.02), (0.6, f, 0.04), "SignFrame")
    for s in (-1, 1):
        p.box((min(s * 0.6, s * (0.6 - f)), f, 0.02), (max(s * 0.6, s * (0.6 - f)), 0.4 - f, 0.04), "SignFrame")
    return p


def roller_shutter():
    p = Part("RollerShutter")
    y0, y1, pitch = 0.06, 2.15, 0.075
    nrib = round((y1 - y0) / pitch)
    pitch = (y1 - y0) / nrib
    prof = [(y0, 0.015)]
    for k in range(nrib):
        b = y0 + k * pitch
        prof += [(b, 0.03), (b + 0.012, 0.046), (b + pitch - 0.012, 0.046)]
    prof += [(y1, 0.03), (y1, 0.015)]
    poly = [V(-1.16, y, z) for y, z in prof]
    p.prism(poly, V(2.32, 0, 0), "ShutterMetal")
    p.box((-1.16, 0, 0.012), (1.16, y0, 0.058), "MetalDark")
    for x in (-0.06, 0.06):
        p.beam((x, 0.03, 0.058), (x, 0.03, 0.085), 0.015, "MetalDark")
    p.box((-0.08, 0.022, 0.08), (0.08, 0.038, 0.095), "MetalDark")
    for s in (-1, 1):
        p.box((min(s * 1.14, s * 1.2), 0, 0), (max(s * 1.14, s * 1.2), y1, 0.075), "MetalDark")
    box = [(2.15, 0.0), (2.15, 0.22), (2.20, 0.30), (2.36, 0.30), (2.40, 0.26), (2.40, 0.0)]
    p.prism([V(-1.2, y, z) for y, z in box], V(2.4, 0, 0), "ShutterBox")
    return p


def spirit_house():
    p = Part("SpiritHouse")
    W, G, R = "SpiritWhite", "Gold", "SpiritRed"
    p.box((-0.17, 0, -0.17), (0.17, 0.08, 0.17), W)
    p.box((-0.175, 0.08, -0.175), (0.175, 0.10, 0.175), G)
    p.box((-0.06, 0.10, -0.06), (0.06, 0.90, 0.06), W)
    p.box((-0.09, 0.88, -0.09), (0.09, 0.95, 0.09), G)
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.beam((sx * 0.05, 0.72, sz * 0.05), (sx * 0.24, 0.945, sz * 0.19), 0.025, W)
    p.box((-0.30, 0.95, -0.25), (0.30, 1.0, 0.25), R)
    # gold railing around the platform
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box((sx * 0.285 - 0.012, 1.0, sz * 0.235 - 0.012), (sx * 0.285 + 0.012, 1.07, sz * 0.235 + 0.012), G)
    for sz in (-1, 1):
        p.box((-0.285, 1.05, sz * 0.235 - 0.006), (0.285, 1.062, sz * 0.235 + 0.006), G)
    for sx in (-1, 1):
        p.box((sx * 0.285 - 0.006, 1.05, -0.235), (sx * 0.285 + 0.006, 1.062, 0.235), G)
    # house with gold columns, door and windows
    p.box((-0.15, 1.0, -0.12), (0.15, 1.245, 0.12), W)
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box((sx * 0.18 - 0.013, 1.0, sz * 0.15 - 0.013), (sx * 0.18 + 0.013, 1.245, sz * 0.15 + 0.013), G)
    p.box((-0.05, 1.02, 0.12), (0.05, 1.19, 0.128), G)
    for sx in (-1, 1):
        p.box((min(sx * 0.15, sx * 0.158), 1.08, -0.04), (max(sx * 0.15, sx * 0.158), 1.17, 0.04), G)
    # three stacked gable roofs: red slopes, gold gable ends, chofa horns on the ridges
    for b, hw, top, hz in ((1.24, 0.25, 1.40, 0.21), (1.33, 0.19, 1.47, 0.155), (1.43, 0.13, 1.54, 0.10)):
        p.prism([V(-hw, b, -hz), V(hw, b, -hz), V(0, top, -hz)], V(0, 0, 2 * hz), R, cap_mat=G)
        for sz in (-1, 1):
            p.beam((0, top - 0.01, sz * (hz - 0.005)), (0, top + 0.05, sz * (hz + 0.03)), 0.014, G)
    p.lathe([(0, 1.52), (0.035, 1.52), (0.03, 1.56), (0.013, 1.60), (0, 1.66)], 6, G)
    return p


def basket_stack():
    p = Part("BasketStack")
    cols = ["CrateYellow", "CrateGreen", "CrateRed", "CrateBlue"]
    offs = [(0, 0, 0), (0.02, -0.015, 3), (-0.015, 0.01, -2.5), (0.012, 0.02, 4)]
    X, Z, H, t, L = 0.25, 0.175, 0.15, 0.012, 0.006
    for k, (c, (dx, dz, yaw)) in enumerate(zip(cols, offs)):
        with p.at(T(dx, k * H, dz) @ rotY(yaw)):
            p.box((-X, 0, -Z), (X, t, Z), c)
            p.box((-X, 0, Z - t), (X, H, Z), c)
            p.box((-X, 0, -Z), (X, H, -Z + t), c)
            for s in (-1, 1):
                p.box((min(s * X, s * (X - t)), 0, -Z + t), (max(s * X, s * (X - t)), H, Z - t), c)
            for y0, y1 in ((H - 0.016, H), (0.068, 0.082)):
                p.box((-X - L, y0, Z), (X + L, y1, Z + L), c)
                p.box((-X - L, y0, -Z - L), (X + L, y1, -Z), c)
                for s in (-1, 1):
                    p.box((min(s * X, s * (X + L)), y0, -Z), (max(s * X, s * (X + L)), y1, Z), c)
            for x in (-0.09, 0.09):
                for s in (-1, 1):
                    p.box((x - 0.008, 0.012, min(s * Z, s * (Z + L))), (x + 0.008, H - 0.016, max(s * Z, s * (Z + L))), c)
    return p


def cooler_box():
    p = Part("CoolerBox")
    M, D = "CoolerMetal", "MetalDark"
    p.box((-0.30, 0.03, -0.20), (0.30, 0.33, 0.20), M)
    for sz in (-1, 1):
        p.box((-0.29, 0, sz * 0.15 - 0.02), (0.29, 0.03, sz * 0.15 + 0.02), D)
    p.box((-0.305, 0.33, -0.205), (0.305, 0.395, 0.205), M)
    p.box((-0.31, 0.33, -0.21), (0.31, 0.345, 0.21), D)
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box((sx * 0.30 - 0.018, 0.03, sz * 0.20 - 0.018), (sx * 0.30 + 0.018, 0.33, sz * 0.20 + 0.018), D)
    p.box((-0.29, 0.17, 0.20), (0.29, 0.19, 0.206), D)
    # latch and lid handle
    p.box((-0.03, 0.29, 0.205), (0.03, 0.36, 0.215), D)
    for sx in (-1, 1):
        p.beam((sx * 0.08, 0.395, 0), (sx * 0.08, 0.40, 0), 0.02, D)
    p.box((-0.09, 0.395, -0.012), (0.09, 0.40, 0.012), D)
    # end handles
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.beam((sx * 0.30, 0.26, sz * 0.08), (sx * 0.335, 0.26, sz * 0.08), 0.018, D)
        p.cyl((sx * 0.335, 0.26, -0.09), (sx * 0.335, 0.26, 0.09), 0.011, 6, D)
    # rust patches
    for lo, hi in (((-0.25, 0.04, 0.20), (-0.12, 0.09, 0.202)), ((0.14, 0.21, 0.20), (0.22, 0.27, 0.202)),
                   ((0.30, 0.05, -0.12), (0.302, 0.12, 0.02))):
        p.box(lo, hi, "Rust")
    return p


def money_bowl():
    p = Part("MoneyBowl")
    p.lathe([(0, 0), (0.07, 0), (0.075, 0.006), (0.11, 0.05), (0.12, 0.08), (0.114, 0.08), (0.104, 0.052),
             (0.066, 0.012), (0, 0.012)], 16, "BowlPurple")
    for col, y, yaw, tilt in (("NotePink", 0.034, 10, 6), ("NoteGreen", 0.041, -35, -5), ("NoteBlue", 0.048, 70, 4)):
        with p.at(T(0, y, 0) @ rotY(yaw) @ Matrix.Rotation(math.radians(tilt), 4, "Z")):
            p.box((-0.072, 0, -0.034), (0.072, 0.002, 0.034), col)
    for x, z, c, y in ((0.02, 0.01, "CoinSilver", 0.051), (-0.03, 0.02, "Gold", 0.052), (0.0, -0.03, "CoinSilver", 0.053),
                       (0.04, -0.02, "Gold", 0.051), (-0.01, 0.045, "CoinSilver", 0.05)):
        p.cyl((x, y, z), (x, y + 0.0022, z), 0.012, 8, c)
    return p


def bunting():
    p = Part("Bunting")
    L, s = 3.0, 0.15
    p.tube([V(L * t, sag(t, s), 0) for t in (i / 24 for i in range(25))], 0.003, 4, "BlackRubber", phase=0.5)
    cols = ["FabricRed", "FabricYellow", "FabricBlue", "FabricGreen", "FabricOrange", "FabricPurple"]
    for k in range(10):
        xc = 0.15 + 0.3 * k
        xa, xb = xc - 0.11, xc + 0.11
        ya, yb, yc = sag(xa / L, s), sag(xb / L, s), sag(xc / L, s)
        p.prism([V(xa, ya, -0.0015), V(xb, yb, -0.0015), V(xc, yc - 0.25, -0.0015)], V(0, 0, 0.003), cols[k % 6])
    return p


def price_sign():
    p = Part("PriceSign")
    p.cyl((0, 0, 0), (0, 0.25, 0), 0.004, 6, "WoodLight")
    p.box((-0.08, 0.155, 0.004), (0.08, 0.255, 0.010), "PaintWhite")
    return p


def service_bell():
    """Counter bell: dark base, brass dome, steel plunger on top (Ø 9 cm, 6 cm tall). The poke target in the game."""
    p = Part("ServiceBell")
    p.lathe([(0, 0), (0.045, 0), (0.045, 0.01), (0.04, 0.014), (0, 0.014)], 20, "MetalDark")
    p.lathe([(0, 0.013), (0.04, 0.013), (0.038, 0.024), (0.03, 0.04), (0.017, 0.049), (0, 0.052)], 20, "Gold")
    p.cyl((0, 0.05, 0), (0, 0.062, 0), 0.006, 8, "MetalSteel")
    return p


def handover_tray():
    """The hand-over tray in front of the customer: orange plastic, 22 x 18 cm, 2.5 cm rim, so it reads as a place
    to put something down (the game writes HAND OVER on its floor)."""
    p = Part("HandoverTray")
    hx, hz, t, h = 0.11, 0.09, 0.012, 0.025
    p.box((-hx, 0, -hz), (hx, 0.006, hz), "TrayOrange")
    p.box((-hx, 0, -hz), (hx, h, -hz + t), "TrayOrange")
    p.box((-hx, 0, hz - t), (hx, h, hz), "TrayOrange")
    p.box((-hx, 0, -hz + t), (-hx + t, h, hz - t), "TrayOrange")
    p.box((hx - t, 0, -hz + t), (hx, h, hz - t), "TrayOrange")
    return p


# name -> builder, tri budget, origin note, expected Unity bounds (axis: (min, max), None = unchecked)
BASE = "centre of the base at y = 0"
PROPS = [
    ("PushCart", push_cart, LARGE, "floor under the counter centre; counter top (inside the 2 cm lip) at y = 0.90; "
     "player side is -Z (open shelf, push handle 4.5 cm proud at y = 0.83), customers +Z",
     {"x": (-0.765, 0.765), "y": (0, 2.05), "z": (-0.345, 0.30)}),
    ("RainbowUmbrella", rainbow_umbrella, LARGE, "pole base", {"y": (0, 2.69), "x": (-1.15, 1.15)}),
    ("BulbStringWarm", lambda: bulb_string("BulbStringWarm", ["BulbWarmGlow"]), MEDIUM,
     "left hang point; wire runs along +X to x = 1.0, hang points at y = 0, sag 6 cm", {"x": (-0.002, 1.002)}),
    ("BulbStringColour", lambda: bulb_string("BulbStringColour", ["BulbRedGlow", "BulbYellowGlow", "BulbGreenGlow",
                                                                   "BulbBlueGlow", "BulbPinkGlow"]), MEDIUM,
     "left hang point; wire runs along +X to x = 1.0, hang points at y = 0, sag 6 cm", {"x": (-0.002, 1.002)}),
] + [
    (f"Lantern{c}", (lambda c=c: lantern(c)), SMALL, "top hang point; lantern hangs below y = 0",
     {"y": (-0.32, 0), "x": (-0.11, 0.11)}) for c in ("Red", "Orange", "Green", "Teal", "Purple")
] + [
    ("Scale", scale, MEDIUM, BASE + "; dial faces +Z", {"y": (0, 0.235)}),
    ("IceTray", ice_tray, SMALL, BASE, {"y": (0, None), "x": (-0.186, 0.186)}),
    ("PlasticBag", plastic_bag, SMALL, "handle top (hook point); bag hangs below y = 0", {"y": (-0.284, 0.001)}),
    ("PlasticBagFlat", plastic_bag_flat, SMALL, BASE, {"y": (0, None)}),
    ("PlasticStool", plastic_stool, SMALL, BASE, {"y": (0, 0.45)}),
    ("AirCon", air_con, MEDIUM, "back face centre, bottom of the unit at y = 0 (bracket hangs to y = -0.30); "
     "back on the wall at z = 0, front faces +Z; fan on the viewer's left (+X), pipes on the right (-X)", {"z": (0, None), "x": (None, 0.40)}),
    ("ElectricPole", electric_pole, LARGE, "pole base; transformer and meter boxes face +Z", {"y": (0, 7.5)}),
    ("WireBundle", wire_bundle, MEDIUM, "x = 0 hang point; 5 cables to x = 10, y = 0 at both ends, sags 0.84-0.98 m",
     {"x": (-0.0075, 10.0075)}),
    ("SignBoard", sign_board, SMALL, "back-centre-bottom; front faces +Z (text goes on the panel face at z = 0.025)",
     {"x": (-0.6, 0.6), "y": (0, 0.4), "z": (0, 0.04)}),
    ("RollerShutter", roller_shutter, MEDIUM, "back-centre-bottom; front faces +Z",
     {"x": (-1.2, 1.2), "y": (0, 2.4), "z": (0, 0.30)}),
    ("SpiritHouse", spirit_house, MEDIUM, BASE + "; door faces +Z", {"y": (0, 1.66)}),
    ("BasketStack", basket_stack, MEDIUM, BASE + " (bottom crate centre)", {"y": (0, 0.60)}),
    ("CoolerBox", cooler_box, SMALL, BASE + "; latch faces +Z", {"y": (0, 0.40), "z": (-0.218, 0.218)}),
    ("MoneyBowl", money_bowl, SMALL, BASE, {"y": (0, 0.08), "x": (-0.12, 0.12)}),
    ("Bunting", bunting, MEDIUM, "left hang point; string runs along +X to x = 3.0, sag 15 cm", {"x": (-0.003, 3.003)}),
    ("ServiceBell", service_bell, SMALL, BASE, {"y": (0, 0.062), "x": (-0.045, 0.045)}),
    ("HandoverTray", handover_tray, SMALL, BASE + "; floor top at y = 0.006, rim to 0.025",
     {"y": (0, 0.025), "x": (-0.11, 0.11), "z": (-0.09, 0.09)}),
    ("PriceSign", price_sign, SMALL, "stick bottom; board faces +Z (text on the face at z = 0.010)",
     {"y": (0, 0.255)}),
]


# ---------------------------------------------------------------- export + verify

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(part, path):
    reset()
    me = bpy.data.meshes.new(part.name)
    part.bm.to_mesh(me)
    for m in part.mats:
        me.materials.append(make_material(m))
    ob = bpy.data.objects.new(part.name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.uv_layers.new(name="UVMap")
    me.uv_layers.new(name="Lightmap")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    for layer, margin in (("UVMap", 0.0), ("Lightmap", 0.02)):
        me.uv_layers.active = me.uv_layers[layer]
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_yup=True, export_apply=True,
                              export_texcoords=True, export_normals=True, export_animations=False)


def glb_doc(path):
    with open(path, "rb") as fh:
        data = fh.read()
    return json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]]), len(data)


def r4(v):
    return [round(x, 4) for x in v]


def verify(name, path, budget, expect):
    doc, size = glb_doc(path)
    mats = [m["name"] for m in doc["materials"]]
    glow = [m["name"] for m in doc["materials"] if any(c > 0 for c in m.get("emissiveFactor", [0, 0, 0]))]
    assert glow == [m for m in mats if m.endswith("Glow")], (glow, mats)
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            assert "TEXCOORD_1" in prim["attributes"], f"{name}: no lightmap UVs"
            assert "TEXCOORD_0" in prim["attributes"] and "NORMAL" in prim["attributes"]
    reset()
    bpy.ops.import_scene.gltf(filepath=path)
    dg = bpy.context.evaluated_depsgraph_get()
    tris, pts = 0, []
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        me = o.evaluated_get(dg).to_mesh()
        me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        pts += [o.matrix_world @ v.co for v in me.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    a, b = to_unity(lo), to_unity(hi)
    mn = [min(x, y) for x, y in zip(a, b)]
    mx = [max(x, y) for x, y in zip(a, b)]
    print(f"== {name}.glb ({size} B) tris={tris}/{budget} unity_min={r4(mn)} unity_max={r4(mx)} mats={mats}")
    assert tris <= budget, f"{name}: {tris} tris > {budget}"
    for ax, (e0, e1) in expect.items():
        i = "xyz".index(ax)
        if e0 is not None:
            assert abs(mn[i] - e0) < 0.006, f"{name}: min {ax} {mn[i]:.4f} != {e0}"
        if e1 is not None:
            assert abs(mx[i] - e1) < 0.006, f"{name}: max {ax} {mx[i]:.4f} != {e1}"
    return {"tris": tris, "min": r4(mn), "max": r4(mx), "size": r4([y - x for x, y in zip(mn, mx)]),
            "materials": mats, "emissive": glow}


# ---------------------------------------------------------------- render

def look_setup(res):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 16
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x, scene.render.resolution_y = res
    for m in bpy.data.materials:
        m.use_backface_culling = True  # flipped faces would show up as holes
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 2.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    scene.world = bpy.data.worlds.new("W")
    scene.world.use_nodes = True
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
    r = scene.render
    for attr in dir(r):
        if attr.startswith("use_stamp_"):
            try:
                setattr(r, attr, False)
            except (AttributeError, TypeError):
                pass
    r.use_stamp = True
    r.use_stamp_note = True
    r.stamp_font_size = 14
    return scene


def flat_mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    return m


def add_box(name, lo, hi, mat):
    """Blender-space box (render helpers only)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    for v in bm.verts:
        v.co = Vector([(l + h) / 2 + c * (h - l) for c, l, h in zip(v.co, lo, hi)])
    bm.to_mesh(me)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def checker_floor(lo, hi):
    m = bpy.data.materials.new("Floor")
    m.use_nodes = True
    nt = m.node_tree
    ch = nt.nodes.new("ShaderNodeTexChecker")
    ch.inputs["Scale"].default_value = 5.0  # 10 cm squares in object (= world) space
    ch.inputs["Color1"].default_value = (0.32, 0.32, 0.30, 1)
    ch.inputs["Color2"].default_value = (0.22, 0.22, 0.21, 1)
    nt.links.new(nt.nodes.new("ShaderNodeTexCoord").outputs["Object"], ch.inputs["Vector"])
    nt.links.new(ch.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    return add_box("Floor", (lo[0], lo[1], -0.002), (hi[0], hi[1], -0.0005), m)


def scale_bar(x, y):
    """1.7 m human-height bar: orange to 1.0 m, white above."""
    add_box("Bar1", (x - 0.03, y - 0.03, 0), (x + 0.03, y + 0.03, 1.0), flat_mat("BarO", (0.9, 0.35, 0.05)))
    add_box("Bar2", (x - 0.03, y - 0.03, 1.0), (x + 0.03, y + 0.03, 1.7), flat_mat("BarW", (0.9, 0.9, 0.9)))


def aim(cam, target, d, pts, ortho=True, margin=1.12):
    f = -d.normalized()
    right = f.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(f)
    xs = [(p - target).dot(right) for p in pts]
    ys = [(p - target).dot(up) for p in pts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    c = target + right * cx + up * cy
    cam.location = c + d.normalized() * 30
    cam.rotation_euler = f.to_track_quat("-Z", "Y").to_euler()
    cam.data.type = "ORTHO"
    cam.data.clip_end = 100
    cam.data.ortho_scale = max(max(xs) - min(xs), max(ys) - min(ys)) * margin


def render_tiles(name, path, outdir):
    reset()
    bpy.ops.import_scene.gltf(filepath=path)
    obs = [o for o in bpy.data.objects if o.type == "MESH"]
    pts = [o.matrix_world @ Vector(c) for o in obs for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    shift = Vector(((lo.x + hi.x) / -2, (lo.y + hi.y) / -2, -lo.z))  # hanging props are set on the floor
    for o in obs:
        o.location += shift
    lo, hi = lo + shift, hi + shift
    scene = look_setup((480, 480))
    ext = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)
    frame = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    if hi.z > 0.6:
        bx = lo.x - 0.25
        scale_bar(bx, 0)
        frame += [Vector((bx, 0, 1.7)), Vector((bx, 0, 0))]
    else:
        add_box("Ref10", (lo.x - 0.14, -0.05, 0), (lo.x - 0.04, 0.05, 0.1), flat_mat("RefW", (0.85, 0.85, 0.85)))
        frame += [Vector((lo.x - 0.14, 0, 0))]
    checker_floor(lo - Vector((0.3 + ext * 0.2, 0.3 + ext * 0.2, 0)), hi + Vector((0.3 + ext * 0.2, 0.3 + ext * 0.2, 0)))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    tgt = (lo + hi) / 2
    files = []
    size = f"{hi.x - lo.x:.2f} x {hi.z - lo.z:.2f} x {hi.y - lo.y:.2f} m"
    # Unity +Z (front) is Blender -Y
    for view, d in (("front", U(0.7, 0.55, 1)), ("back", U(-0.7, 0.6, -1))):
        aim(cam, tgt, d, frame)
        scene.render.stamp_note_text = f"{name} {view}  {size}"
        f = os.path.join(outdir, "tiles", f"{name}_{view}.png")
        scene.render.filepath = f
        bpy.ops.render.render(write_still=True)
        files.append(f)
    return files


def montage(files, cols, out):
    import numpy as np
    imgs = []
    for f in files:
        im = bpy.data.images.load(f)
        w, h = im.size
        a = np.empty(w * h * 4, dtype=np.float32)
        im.pixels.foreach_get(a)
        imgs.append(a.reshape(h, w, 4))
    h, w = imgs[0].shape[:2]
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.zeros((rows * h, cols * w, 4), dtype=np.float32)
    sheet[..., 3] = 1
    for k, a in enumerate(imgs):
        r, c = k // cols, k % cols
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = a  # Blender images are bottom-up
    im = bpy.data.images.new("Sheet", cols * w, rows * h, alpha=True)
    im.pixels.foreach_set(sheet.ravel())
    im.filepath_raw = out
    im.file_format = "PNG"
    im.save()


def place(name, pos, yaw=0.0):
    """Import a prop GLB at a Unity position (yaw in degrees about Unity Y)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(OUT, f"{name}.glb"))
    for o in set(bpy.data.objects) - before:
        if o.parent is None:
            o.location = U(*pos)
            o.rotation_mode = "XYZ"
            o.rotation_euler = (0, 0, math.radians(-yaw))


def render_assembly(outdir):
    reset()
    place("PushCart", (0, 0, 0))
    place("RainbowUmbrella", (-0.95, 0, 0.35))
    place("BulbStringWarm", (-0.6875, 2.03, -0.2875))
    place("BulbStringColour", (-0.3, 2.03, 0.2875))
    for k, c in enumerate(("Red", "Orange", "Green", "Teal", "Purple")):
        place(f"Lantern{c}", (-0.6 + k * 0.3, 2.025, 0.2875))
    place("PlasticBag", (0.6875, 2.025, -0.2875))
    place("Scale", (0.45, 0.90, 0.08))
    place("IceTray", (-0.35, 0.90, 0.05))
    place("MoneyBowl", (0.1, 0.90, 0.16))
    place("PriceSign", (-0.1, 0.90, 0.25))
    place("PlasticBagFlat", (0.1, 0.30, -0.1))
    place("PlasticStool", (0.35, 0, 0.85))
    place("BasketStack", (1.3, 0, 0.2), yaw=8)
    place("CoolerBox", (-1.5, 0, -0.1), yaw=-10)
    # shophouse wall behind the customers (props face the stall, -Z)
    place("RollerShutter", (0.4, 0, 3.0), yaw=180)
    place("SignBoard", (0.4, 2.55, 3.0), yaw=180)
    place("AirCon", (2.2, 2.4, 3.0), yaw=180)
    place("SpiritHouse", (-2.4, 0, 2.2), yaw=180)
    place("ElectricPole", (3.5, 0, 2.0))
    place("WireBundle", (-6.5, 6.9, 2.1))
    place("Bunting", (-1.5, 3.0, 1.8))
    scene = look_setup((1600, 1000))
    wall = flat_mat("Wall", (0.55, 0.45, 0.35))
    add_box("Wall", (-4, -3.3, 0), (4, -3.0, 5), wall)  # Unity z 3.0..3.3
    checker_floor(Vector((-5, -4, 0)), Vector((5, 3, 0)))
    scale_bar(*U(0.0, 0, -0.75)[:2])
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 22
    cam.data.clip_end = 200
    out = []
    for view, eye, tgt in (("player", (0.0, 1.6, -2.6), (0.2, 1.5, 1.5)),
                           ("street", (4.5, 2.2, -4.5), (0.3, 1.6, 0.8))):
        e, t = U(*eye), U(*tgt)
        cam.location = e
        cam.rotation_euler = (t - e).to_track_quat("-Z", "Y").to_euler()
        scene.render.stamp_note_text = f"assembly {view}"
        f = os.path.join(outdir, f"assembly_{view}.png")
        scene.render.filepath = f
        bpy.ops.render.render(write_still=True)
        out.append(f)
    return out


# ---------------------------------------------------------------- main

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render_dir = argv[argv.index("--render") + 1] if "--render" in argv else None
    only = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
    os.makedirs(OUT, exist_ok=True)
    jpath = os.path.join(OUT, "props.json")
    old = {}
    if os.path.exists(jpath):
        with open(jpath) as fh:
            old = {p["name"]: p for p in json.load(fh)["props"]}
    metas, tiles = [], []
    for name, build, budget, origin, expect in PROPS:
        path = os.path.join(OUT, f"{name}.glb")
        if only is None or name in only:
            export(build().finish(), path)
            info = verify(name, path, budget, expect)
            meta = {"name": name, "file": f"{name}.glb", "origin": origin, "budget": budget, **info}
        else:
            meta = old[name]
        metas.append(meta)
        if render_dir and (only is None or name in only):
            tiles += render_tiles(name, path, render_dir)
    with open(jpath, "w") as fh:
        json.dump({"units": "metres, Unity space (+Y up, front +Z)", "props": metas}, fh, indent=2)
        fh.write("\n")
    if render_dir:
        sheets = []
        per = 16  # 8 props x 2 views
        for k in range(0, len(tiles), per):
            out = os.path.join(render_dir, f"sheet_{k // per + 1}.png")
            montage(tiles[k:k + per], 4, out)
            sheets.append(out)
        if only is None:
            sheets += render_assembly(render_dir)
        print("RENDERS", sheets)


main()
