"""おさんぽカート A案「里山の椅子」 KH_OsanpoCart. Original procedural model + BaseColor atlas, Blender 5.x.
blender --background --factory-startup --python-exit-code 1 --python scripts/art/osanpo-cart.py
        [-- --out DIR --evidence DIR --report DIR --source DIR --no-render]
Geometry is authored in Unity coordinates (+X right, +Y up, +Z forward, metres, origin = body centre on the ground)
and converted with B(); Unity negates X on FBX import, so B(p)=(-x,-z,y) round-trips to the authored values.
No external images, photos, fonts or purchased assets. Writes FBX + 2 PNG + evidence + report. Never touches Unity.
"""
import bpy, math, json, sys, struct, zlib, pathlib, time
import numpy as np
from mathutils import Vector, Matrix

ROOT = pathlib.Path(__file__).resolve().parents[2]
ARGV = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def opt(name, default): return (pathlib.Path(ARGV[ARGV.index(name) + 1]) if name in ARGV else default).resolve()
OUT = opt('--out', ROOT / 'Assets/KominkaHillside/Models/Cart')
EVID = opt('--evidence', ROOT / 'docs/plans/evidence/2026-10-11-osanpo-cart')
REP = opt('--report', ROOT / 'Temp/osanpo-cart-20261011')
SRC = opt('--source', ROOT / 'art-source/osanpo-cart')
RENDER = '--no-render' not in ARGV
for d in (OUT, EVID, REP, SRC): d.mkdir(parents=True, exist_ok=True)
T0 = time.time()

# ------------------------------------------------------------------ settings
NAME = 'KH_OsanpoCart'
ATLAS = 1024; PAD = 4                     # px; gutter = 2*PAD between islands
SIGN_W, SIGN_H = 512, 256
BUDGET_LOD0, BUDGET_LOD1 = 4000, 1500
FILL_TARGET = (0.75, 0.85)
# sRGB palette. cloth = #bd9a69 desaturated, slightly darker and nudged to grey-green (kept apart from tan fur).
HEX = dict(moss='53634e', cloth='a89c78', wood='92714b', dark='343c35', brand='364e3c', cream='e6dcc3',
           stop='b5735a', burn='3b2a1d', rim='46533f', hub='262b27')
IDS = list(HEX); ID = {k: i for i, k in enumerate(IDS)}
PAL = np.array([[int(HEX[k][i:i + 2], 16) for i in (0, 2, 4)] for k in IDS], np.float32)
# Island texel density in px/cm. fixed=True keeps the near-view value; the rest are scaled together to fill the atlas.
ISLANDS = {
    'armrest': (14, True), 'armrest_cap': (14, True), 'stop_base': (14, True), 'stop_cap': (10, True),
    'button_side': (10, True), 'button_top': (14, True), 'cushion_band': (12, True), 'footrest_band': (10, True),
    'brand': (24, True),
    'chassis_band': (3, False), 'chassis_top': (1.5, False), 'seat_band': (3.5, False), 'cushion_top': (2.5, False),
    'footrest_top': (4, False), 'backboard': (3, False), 'backboard_cap': (3, False), 'backpost': (3, False),
    'backcush_band': (2.5, False), 'backcush_front': (2, False), 'frame': (4, False), 'armpost': (3, False),
    'fender': (4, False), 'skirt': (1, False), 'tire': (2.5, False), 'hub': (3, False), 'dark': (1.5, False),
    'hidden': (.25, False)}
ISL = {k: dict(d=v[0], fixed=v[1], meta={}, w=0., h=0.) for k, v in ISLANDS.items()}

# Dimensions (Unity metres). 仮 = provisional, not in the brief.
WHEEL_R, TIRE_W, WHEEL_X = .14, .085, .3575          # tyre .315-.400, hub cap to .405 -> overall width .82 with fenders
AXLES = {'Front': .42, 'Rear': -.41}
CH = dict(hw=.305, z0=-.60, z1=.60, y0=.10, y1=.20, r=.06)            # chassis slab (tier 1)
SB = dict(hw=.24, z0=-.59, z1=-.04, y0=.195, y1=.425, r=.03)          # seat block (tier 2), inset under the cushion
CU = dict(w=.52, d=.46, zc=-.24, r=.045)                              # seat cushion .52 x .46, crown .5045
FR = dict(w=.54, d=.50, zc=.31, r=.03, y0=.198, y1=.222)              # footrest, floor ~.22
ARM = dict(x=.29, w=.08, t=.045, yc=.7275, z0=-.42, z1=.03)           # armrest top .75
ARMPOST = dict(x=.285, z=(-.38, -.06), r=.014)
STOP = dict(depth=.095, w=.07, zc=-.0225, y=.743, tilt=18., roll=8.)   # wedge on the armrest front, faces the eye
BACK = dict(lean=5., y=.53, z=-.50, w=.55, h=.52, t=.028, sag=.02, post_x=.255, post_r=.016)
BRAND = dict(x0=.17, x1=.22, y0=.042, y1=.056)                        # VAnimal burn mark, lower right seen from behind
SIGNF = dict(w=.32, h=.18, yc=.30, border=.02)                        # embedded frame; plate .28 x .14 (2:1)
FEN = dict(gap=.025, t=.006, rim=.010, a0=.303, a1=.410, arc=(15., 165.))
ANCHORS_FIXED = {  # name: (position, forward)  -- Unity space
    'Anchor_Seat': ((0, .50, -.26), (0, 0, 1)),
    'Anchor_ExitL': ((-.91, 0, -.10), (0, 0, 1)), 'Anchor_ExitR': ((.91, 0, -.10), (0, 0, 1)),
    'Anchor_DogL': ((-.60, 0, .80), (0, 0, 1)), 'Anchor_DogR': ((.60, 0, .80), (0, 0, 1))}

# ------------------------------------------------------------------ small math
def nrm(v): v = np.asarray(v, float); return v / np.linalg.norm(v)
def B(p): return np.array([-p[0], -p[2], p[1]], float)          # Unity -> Blender
X3, Y3, Z3 = np.eye(3)
def rot_axis(axis, deg):
    a = nrm(axis); t = math.radians(deg); c, s = math.cos(t), math.sin(t)
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    return np.eye(3) + s * K + (1 - c) * K @ K
def poly_area(P): x, y = P[:, 0], P[:, 1]; return .5 * float(np.sum(x * np.roll(y, -1) - np.roll(x, -1) * y))

def earclip(P):
    """Ear clipping on a simple CCW polygon; picks the ear with the best minimum angle; never emits zero-area ears."""
    P = np.asarray(P, float); idx = list(range(len(P))); out = []
    def a2(a, b, c): return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    def inside(p, a, b, c):
        return a2(a, b, p) >= -1e-12 and a2(b, c, p) >= -1e-12 and a2(c, a, p) >= -1e-12
    def minang(a, b, c):
        r = []
        for p, q, s in ((a, b, c), (b, c, a), (c, a, b)):
            u, v = q - p, s - p; r.append(math.acos(max(-1, min(1, u @ v / (np.linalg.norm(u) * np.linalg.norm(v))))))
        return min(r)
    while len(idx) > 3:
        best = None
        for k in range(len(idx)):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % len(idx)]
            if a2(P[i0], P[i1], P[i2]) <= 1e-12: continue
            if any(inside(P[m], P[i0], P[i1], P[i2]) for m in idx if m not in (i0, i1, i2)): continue
            q = minang(P[i0], P[i1], P[i2])
            if best is None or q > best[0]: best = (q, k)
        assert best is not None, 'earclip failed'
        k = best[1]; out.append((idx[k - 1], idx[k], idx[(k + 1) % len(idx)])); idx.pop(k)
    assert a2(P[idx[0]], P[idx[1]], P[idx[2]]) > 1e-12, 'earclip: degenerate last triangle'
    out.append(tuple(idx)); return out

def fillet(pts, radii, segs):
    """Round the corners of a CCW polygon. radii/segs per vertex (0 = keep sharp)."""
    pts = np.asarray(pts, float); n = len(pts); out = []
    for i in range(n):
        p, a, b = pts[i], pts[i - 1], pts[(i + 1) % n]; r = radii[i]; sg = max(1, segs[i])
        d1 = nrm(p - a); d2 = nrm(b - p); turn = math.atan2(d1[0] * d2[1] - d1[1] * d2[0], d1 @ d2)
        if r <= 0 or abs(turn) < 1e-6: out.append(p); continue
        t = r * math.tan(abs(turn) / 2); p1 = p - d1 * t
        c = p1 + np.array([-d1[1], d1[0]]) * r * (1 if turn > 0 else -1)
        a1 = math.atan2(*(p1 - c)[::-1])
        for j in range(sg + 1):
            ang = a1 + turn * j / sg; out.append(c + r * np.array([math.cos(ang), math.sin(ang)]))
    res = [out[0]]
    for q in out[1:]:
        if np.linalg.norm(q - res[-1]) > 1e-7: res.append(q)
    if np.linalg.norm(res[0] - res[-1]) < 1e-7: res.pop()
    return np.array(res)

def rrect(w, d, r, seg, xs=()):
    """Symmetric rounded rectangle in (a,b), CCW, starting at the front centre (0, d/2) heading -a.
    Vertex 0 and n/2 lie on a=0 so the outline can be mirrored. xs adds straight-edge vertices at +-x."""
    hw, hd = w / 2, d / 2
    xs = sorted(x for x in xs if 0 < x < hw - r - 1e-6)
    pts, rad = [(0, hd)], [0]
    pts += [(-x, hd) for x in xs]; rad += [0] * len(xs)
    pts += [(-hw, hd)]; rad += [r]
    pts += [(-hw, -hd)]; rad += [r]
    pts += [(-x, -hd) for x in xs[::-1]] + [(0, -hd)] + [(x, -hd) for x in xs]; rad += [0] * (2 * len(xs) + 1)
    pts += [(hw, -hd)]; rad += [r]
    pts += [(hw, hd)]; rad += [r]
    pts += [(x, hd) for x in xs[::-1]]; rad += [0] * len(xs)
    return fillet(pts, rad, [seg] * len(pts))

def roll(h0, h1, r0, r1, s0, s1, extra=()):
    """Loft profile (inward offset, height): rounded bottom/top edges with quarter arcs."""
    p = []
    if r0 > 0: p += [(r0 * (1 - math.sin(math.pi / 2 * k / s0)), h0 + r0 * (1 - math.cos(math.pi / 2 * k / s0))) for k in range(s0 + 1)]
    else: p.append((0, h0))
    p += [(0, e) for e in sorted(extra) if p[-1][1] + 1e-6 < e < h1 - r1 - 1e-6]
    if r1 > 0: p += [(r1 * (1 - math.cos(math.pi / 2 * k / s1)), h1 - r1 + r1 * math.sin(math.pi / 2 * k / s1)) for k in range(s1 + 1)]
    else: p.append((0, h1))
    return p

# ------------------------------------------------------------------ mesh builder (Unity space)
class MB:
    def __init__(s): s.V = []; s.T = []
    def mark(s): return (len(s.V), len(s.T))
    def vert(s, p): s.V.append(np.asarray(p, float)); return len(s.V) - 1
    def tri(s, idx, uv, ns, mat, tag): s.T.append([list(idx), list(uv), [np.asarray(n, float) for n in ns], mat, tag])
    def quad(s, idx, uv, ns, mat, tag):
        P = [s.V[i] for i in idx]
        o = ((0, 1, 2), (0, 2, 3)) if np.linalg.norm(P[0] - P[2]) <= np.linalg.norm(P[1] - P[3]) else ((0, 1, 3), (1, 2, 3))
        for t in o: s.tri([idx[k] for k in t], [uv[k] for k in t], [ns[k] for k in t], mat, tag)
    def xform(s, mk, fp, fn):
        v0, t0 = mk
        for t in s.T[t0:]: t[2] = [fn(s.V[i], n) for i, n in zip(t[0], t[2])]
        for i in range(v0, len(s.V)): s.V[i] = fp(s.V[i])
    def rigid(s, mk, R, T): s.xform(mk, lambda p: R @ p + T, lambda p, n: R @ n)
    def mirror(s, mk):
        v0, t0 = mk; nv, nt = len(s.V) - v0, len(s.T) - t0; off = len(s.V) - v0
        M = np.diag([-1., 1, 1])
        for i in range(nv): s.V.append(M @ s.V[v0 + i])
        for t in s.T[t0:t0 + nt]: s.T.append([[i + off for i in t[0]], list(t[1]), [M @ n for n in t[2]], t[3], t[4]])

def _cap(mb, Q, ids, normal, isl, uvf, mat, tag, mirror_n=None):
    polys = [list(range(len(Q)))] if mirror_n is None else [list(range(0, mirror_n + 1)), list(range(mirror_n, len(Q))) + [0]]
    for poly in polys:
        for t in earclip(Q[poly]):
            k = [poly[i] for i in t]
            mb.tri([ids[i] for i in k], [(isl, *uvf(Q[i])) for i in k], [normal] * 3, mat, tag)

def loft(mb, outline, prof, O, ea, eb, eh, *, isl, tag, mat=0, mirror=False, ow=None, pw=None, along='outline',
         sharp_o=False, sharp_p=None, cap0=None, cap1=None, cap_tag=None, uvs=1.0, override=None):
    """Extrude a CCW (a,b) outline along eh through profile rings (inward offset, height).
    Profile must run counter-clockwise around the material in the (r=-offset, h) plane.
    Band UV: weighted outline arc x weighted profile arc (metres). Caps: planar (|a| when mirrored)."""
    P = np.asarray(outline, float); n = len(P); O, ea, eb, eh = (np.asarray(x, float) for x in (O, ea, eb, eh))
    assert poly_area(P) > 0, 'outline not CCW'
    E = np.roll(P, -1, 0) - P; L = np.linalg.norm(E, axis=1); assert L.min() > 1e-6
    En = np.stack([E[:, 1], -E[:, 0]], 1) / L[:, None]
    Vn = En + np.roll(En, 1, 0); Vn /= np.linalg.norm(Vn, axis=1)[:, None]
    miter = Vn / np.sum(Vn * En, axis=1)[:, None]
    mids = P + E / 2; w = np.asarray(ow(mids), float) if ow else np.ones(n)
    s = np.zeros(n + 1)
    if mirror:
        h = n // 2; assert n % 2 == 0 and abs(P[0, 0]) < 1e-9 and abs(P[h, 0]) < 1e-9
        for j in range(1, h + 1): s[j] = s[j - 1] + L[j - 1] * w[j - 1]
        for j in range(h + 1, n): s[j] = s[n - j]
        s[n] = 0
    else:
        s[1:] = np.cumsum(L * w)
    Pr = np.asarray(prof, float); m = len(Pr); r, hh = -Pr[:, 0], Pr[:, 1]
    dr, dh = np.diff(r), np.diff(hh); sl = np.hypot(dr, dh); assert sl.min() > 1e-7
    wp = np.asarray(pw if pw is not None else np.ones(m - 1), float); t = np.concatenate([[0], np.cumsum(sl * wp)])
    Ns = np.stack([dh, -dr], 1) / sl[:, None]
    Nv = np.zeros((m, 2)); Nv[0], Nv[-1] = Ns[0], Ns[-1]
    for i in range(1, m - 1): Nv[i] = nrm(Ns[i - 1] + Ns[i])
    sharp_p = sharp_p or [False] * m
    rings, Qs = [], []
    for i in range(m):
        Q = P - Pr[i, 0] * miter
        assert poly_area(Q) > 0, ('ring inverted', tag, i)
        assert np.all(np.sum((np.roll(Q, -1, 0) - Q) * E, 1) > 0), ('ring edge flipped', tag, i)
        Qs.append(Q); rings.append([mb.vert(O + q[0] * ea + q[1] * eb + hh[i] * eh) for q in Q])
    def o3(v2): return v2[0] * ea + v2[1] * eb
    for i in range(m - 1):
        for j in range(n):
            j2 = (j + 1) % n
            sj2 = s[j2] if mirror else s[j + 1]
            cells = [(i, j, s[j]), (i, j2, sj2), (i + 1, j2, sj2), (i + 1, j, s[j])]
            ns = []
            for ii, jj, _ in cells:
                on = En[j] if sharp_o else Vn[jj]
                pn = Ns[i] if sharp_p[ii] else Nv[ii]
                ns.append(nrm(pn[0] * o3(on) + pn[1] * eh))
            ov = override(i, j) if override else None
            if ov:
                uv = [ov(Qs[ii][jj][0], Qs[ii][jj][1], hh[ii]) for ii, jj, _ in cells]
            else:
                uv = [(isl, ss * uvs, t[ii] * uvs) if along == 'outline' else (isl, t[ii] * uvs, ss * uvs) for ii, jj, ss in cells]
            tg = tag(i, j) if callable(tag) else tag
            mb.quad([rings[ii][jj] for ii, jj, _ in cells], uv, ns, mat, tg)
    amin, bmin = P[:, 0].min(), P[:, 1].min()
    uvf = (lambda q: (abs(q[0]) * uvs, (q[1] - bmin) * uvs)) if mirror else (lambda q: ((q[0] - amin) * uvs, (q[1] - bmin) * uvs))
    for cap, i, sg in ((cap0, 0, -1), (cap1, m - 1, 1)):
        if cap: _cap(mb, Qs[i], rings[i], sg * eh, cap, uvf, mat, cap_tag or (tag if isinstance(tag, str) else 'cap'), n // 2 if mirror else None)
    return dict(s=s, t=t, P=P, n=n, Pr=Pr, rings=rings)

def lathe(mb, prof, C, ax, e1, e2, segs, *, isl, tag, mat=0, u_len=None, sharp_p=None, cap0=None, cap1=None, uvs=1.0):
    """Revolve (r,h) profile (CCW around the material) about ax. Band UV: angle x profile arc. Caps: planar discs."""
    C, ax, e1, e2 = (np.asarray(x, float) for x in (C, ax, e1, e2))
    Pr = np.asarray(prof, float); m = len(Pr); r, hh = Pr[:, 0], Pr[:, 1]
    dr, dh = np.diff(r), np.diff(hh); sl = np.hypot(dr, dh); assert sl.min() > 1e-7
    t = np.concatenate([[0], np.cumsum(sl)]); Ns = np.stack([dh, -dr], 1) / sl[:, None]
    Nv = np.zeros((m, 2)); Nv[0], Nv[-1] = Ns[0], Ns[-1]
    for i in range(1, m - 1): Nv[i] = nrm(Ns[i - 1] + Ns[i])
    sharp_p = sharp_p or [False] * m
    U = u_len if u_len else 2 * math.pi * r.max()
    dirs = [math.cos(2 * math.pi * j / segs) * e1 + math.sin(2 * math.pi * j / segs) * e2 for j in range(segs)]
    rings = [[mb.vert(C + r[i] * d + hh[i] * ax) for d in dirs] for i in range(m)]
    for i in range(m - 1):
        for j in range(segs):
            j2 = (j + 1) % segs
            cells = [(i, j, j), (i, j2, j + 1), (i + 1, j2, j + 1), (i + 1, j, j)]
            ns = []
            for ii, jj, _ in cells:
                pn = Ns[i] if sharp_p[ii] else Nv[ii]
                ns.append(nrm(pn[0] * dirs[jj] + pn[1] * ax))
            mb.quad([rings[ii][jj] for ii, jj, _ in cells], [(isl, uj / segs * U * uvs, t[ii] * uvs) for ii, jj, uj in cells], ns, mat, tag)
    for cap, i, sg in ((cap0, 0, -1), (cap1, m - 1, 1)):
        if not cap: continue
        R = r[i]; Q = np.array([[R * math.cos(2 * math.pi * j / segs), R * math.sin(2 * math.pi * j / segs)] for j in range(segs)])
        _cap(mb, Q, rings[i], sg * ax, cap, lambda q, R=R: ((q[0] + R) * uvs, (q[1] + R) * uvs), mat, tag)
    return dict(t=t, Pr=Pr)

def sweep(mb, outline, frames, *, isl, tag, mat=0, sharp=None, cap_isl=None):
    """Sweep a CCW (a,b) outline through frames [(O, ea, eb)]. UV: path length (u) x outline arc (v)."""
    P = np.asarray(outline, float); n = len(P); assert poly_area(P) > 0
    E = np.roll(P, -1, 0) - P; L = np.linalg.norm(E, axis=1)
    En = np.stack([E[:, 1], -E[:, 0]], 1) / L[:, None]
    Vn = En + np.roll(En, 1, 0); Vn /= np.linalg.norm(Vn, axis=1)[:, None]
    sharp = sharp or [True] * n
    s = np.concatenate([[0], np.cumsum(L)])
    Os = [np.asarray(f[0], float) for f in frames]; K = len(frames)
    u = np.concatenate([[0], np.cumsum([np.linalg.norm(Os[k + 1] - Os[k]) for k in range(K - 1)])])
    rings = [[mb.vert(f[0] + q[0] * np.asarray(f[1]) + q[1] * np.asarray(f[2])) for q in P] for f in frames]
    for k in range(K - 1):
        for j in range(n):
            j2 = (j + 1) % n
            cells = [(k, j, s[j]), (k, j2, s[j + 1]), (k + 1, j2, s[j + 1]), (k + 1, j, s[j])]
            ns = []
            for kk, jj, _ in cells:
                v2 = En[j] if sharp[jj] else Vn[jj]
                ns.append(nrm(v2[0] * np.asarray(frames[kk][1]) + v2[1] * np.asarray(frames[kk][2])))
            mb.quad([rings[kk][jj] for kk, jj, _ in cells], [(isl, u[kk], ss) for kk, jj, ss in cells], ns, mat, tag)
    amin, bmin = P[:, 0].min(), P[:, 1].min()
    if cap_isl:
        for k, sg in ((0, -1), (K - 1, 1)):
            tng = nrm(Os[min(k + 1, K - 1)] - Os[max(k - 1, 0)])
            _cap(mb, P, rings[k], sg * tng, cap_isl, lambda q: (q[0] - amin + u[-1] + .01, q[1] - bmin), mat, tag)
    return dict(u=u, s=s, P=P)

def box(mb, c, size, isl, tag, uvs=.3):
    hx, hy, hz = np.asarray(size) / 2; c = np.asarray(c, float)
    loft(mb, [(-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)], [(0, -hy), (0, hy)], c, X3, Z3, Y3, isl=isl, tag=tag,
         sharp_o=True, sharp_p=[True, True], cap0=isl, cap1=isl, uvs=uvs)

# ------------------------------------------------------------------ the cart
def edge_tag(P, j, test):
    return test(P[j]) and test(P[(j + 1) % len(P)])

def build(lod):
    hi = lod == 0
    cs, rs = (3, 2) if hi else (1, 1)
    def meta(name, **kw):
        if hi: ISL[name]['meta'].update(kw)
    body, axle = MB(), MB()
    anchors = {}
    # tier 1: chassis slab
    o = rrect(2 * CH['hw'], CH['z1'] - CH['z0'], CH['r'], 3 if hi else 1)
    L = loft(body, o, roll(CH['y0'], CH['y1'], .008, .012, 1, rs), (0, 0, 0), X3, Z3, Y3, isl='chassis_band', tag='chassis',
             mirror=True, cap0='hidden', cap1='chassis_top')
    meta('chassis_band', t=L['t'], half=L['s'][L['n'] // 2])
    # tier 2: seat block (inset under the cushion -> shadow groove) and the dark neck
    o = rrect(2 * SB['hw'], SB['z1'] - SB['z0'], SB['r'], cs)
    L = loft(body, o, roll(SB['y0'], SB['y1'], 0, .010, 0, rs), (0, 0, (SB['z0'] + SB['z1']) / 2), X3, Z3, Y3,
             isl='seat_band', tag='seat_block', mirror=True, cap1='hidden')
    side = [L['s'][j] for j in range(L['n'] // 2 + 1) if abs(abs(L['P'][j, 0]) - SB['hw']) < 1e-6]
    meta('seat_band', t=L['t'], side=(min(side), max(side)), half=L['s'][L['n'] // 2])
    if hi:
        loft(body, rrect(.45, .415, .02, cs), [(0, .42), (0, .45)], (0, 0, -.2425), X3, Z3, Y3, isl='dark', tag='dark_small', mirror=True, uvs=.1)
    # seat cushion: 8-10 mm crown, piping bead, rolled top edge
    prof = ([(.008, .445), (0, .452), (0, .480), (-.0035, .4855), (0, .491), (.006, .4955), (.018, .5005), (.040, .5045)] if hi
            else [(.006, .445), (0, .452), (0, .488), (.018, .500), (.040, .5045)])
    o = rrect(CU['w'], CU['d'], CU['r'], cs)
    lim = CU['d'] / 2 - CU['r'] - 1e-6
    L = loft(body, o, prof, (0, 0, CU['zc']), X3, Z3, Y3, isl='cushion_band', mirror=True,
             ow=lambda M: np.where(M[:, 1] > lim, 1.0, .4),
             tag=lambda i, j, P=o: 'seat_front_edge' if edge_tag(P, j, lambda q: q[1] > lim) else 'cushion',
             cap0='hidden', cap1='cushion_top', cap_tag='cushion_top')
    fe = [L['s'][j] for j in range(L['n'] // 2 + 1) if L['P'][j, 1] > lim]
    meta('cushion_band', t=L['t'], pip=(L['t'][2], L['t'][4]), front_end=max(fe))
    meta('cushion_top', w=CU['w'] / 2, d=CU['d'])
    # footrest: painted green boards (dog-height rule), front edge dense
    o = rrect(FR['w'], FR['d'], FR['r'], cs)
    lim2 = FR['d'] / 2 - FR['r'] - 1e-6
    L = loft(body, o, roll(FR['y0'], FR['y1'], 0, .008, 0, rs), (0, 0, FR['zc']), X3, Z3, Y3, isl='footrest_band', mirror=True,
             ow=lambda M: np.where(M[:, 1] > lim2, 1.0, np.where(M[:, 1] < -lim2, .25, .5)),
             tag=lambda i, j, P=o: 'footrest_edge' if edge_tag(P, j, lambda q: q[1] > lim2) else 'footrest',
             cap0='hidden', cap1='footrest_top', cap_tag='footrest_top')
    fe = [L['s'][j] for j in range(L['n'] // 2 + 1) if L['P'][j, 1] > lim2]
    meta('footrest_band', t=L['t'], front_end=max(fe)); meta('footrest_top', w=FR['w'] / 2, d=FR['d'])
    # ---- right-side set (armrest, stop panel, posts), mirrored to the left
    mk = body.mark()
    hw2, ht = ARM['w'] / 2, ARM['t'] / 2
    o = fillet([(0, -ht), (hw2, -ht), (hw2, ht), (-hw2, ht), (-hw2, -ht)], [0, .010, .013, .013, .010], [0, cs, cs, cs, cs])
    L = loft(body, o, roll(ARM['z0'], ARM['z1'], .008, .008, rs, rs), (ARM['x'], ARM['yc'], 0), X3, Y3, Z3, isl='armrest',
             along='profile', ow=lambda M: np.where(M[:, 1] < -ht + 1e-4, .3, 1.0),
             tag=lambda i, j, P=o: 'armrest_top' if edge_tag(P, j, lambda q: q[1] > ht - .0131) else 'armrest_side',
             cap0='armrest_cap', cap1='armrest_cap', cap_tag='armrest_end')
    top = [L['s'][j] for j in range(L['n']) if abs(L['P'][j, 1] - ht) < 1e-6]
    meta('armrest', top=(min(top), max(top)), bottom=(L['s'][1], L['s'][L['n'] - 1]), vtot=L['s'][L['n']], length=L['t'][-1],
         posts=[z - ARM['z0'] for z in ARMPOST['z']])
    meta('armrest_cap', w=ARM['w'], h=ARM['t'])
    # stop panel wedge, tilted toward the seated eye (normal up-back) and rolled inward; convex mesh button
    dz = STOP['depth'] / 2; tr = .010; tf = tr + STOP['depth'] * math.tan(math.radians(STOP['tilt']))
    o = fillet([(-dz, 0), (dz, 0), (dz, tf), (-dz, tr)], [.002, .002, .005, .005], [1, 1, cs, cs])
    smk = body.mark()
    Ls = loft(body, o, roll(-STOP['w'] / 2, STOP['w'] / 2, .003, .003, 1, 1), (0, 0, 0), Z3, Y3, X3, isl='stop_base', tag='stop_panel',
              cap0='stop_cap', cap1='stop_cap')
    a = math.radians(STOP['tilt']); ntop = np.array([0, math.cos(a), -math.sin(a)]); ftop = np.array([0, math.sin(a), math.cos(a)])
    ctop = np.array([0, (tr + tf) / 2, 0])
    lathe(body, [(.019, -.002), (.019, .004), (.015, .0075)] if hi else [(.019, -.002), (.015, .0075)], ctop, ntop, X3, ftop, 12 if hi else 6, isl='button_side', tag='stop_button',
          cap1='button_top')
    R = rot_axis(Z3, STOP['roll']); Tst = np.array([ARM['x'], STOP['y'], STOP['zc']])
    body.rigid(smk, R, Tst)
    ttop = [Ls['s'][k] for k in range(len(o)) if abs(o[k][1] - (tr + (tf - tr) * (o[k][0] + dz) / (2 * dz))) < 1e-6]
    meta('stop_base', top=(min(ttop), max(ttop)), t=Ls['t'])
    meta('button_top', r=.015)
    if hi:
        p = R @ (ctop + ntop * .0075) + Tst; M = np.array([-1, 1, 1])
        anchors['Anchor_StopR'] = (p, R @ ntop, R @ ftop)
        anchors['Anchor_StopL'] = (p * M, (R @ ntop) * M, (R @ ftop) * M)
    y0p = CH['y1'] - .005; y1p = ARM['yc'] - ht + .003
    for z in ARMPOST['z']:
        lathe(body, [(ARMPOST['r'], 0), (ARMPOST['r'], y1p - y0p)], (ARMPOST['x'], y0p, z), Y3, X3, Z3, 8 if hi else 6, isl='armpost', tag='posts')
        if hi:
            lathe(body, [(.021, -.002), (.017, .006)], (ARMPOST['x'], CH['y1'], z), Y3, X3, Z3, 6, isl='dark', tag='dark_small', cap1='dark', uvs=.5)
            box(body, (ARMPOST['x'], ARM['yc'] - ht - .006, z), (.032, .016, .044), 'dark', 'dark_small')
    meta('armpost', length=y1p - y0p)
    body.mirror(mk)
    # ---- fenders (one built per axle, mirrored): 25 mm gap to the tyre, 10 mm rim, inner skirt plate
    mk = body.mark()
    a0, a1, t_, rim = FEN['a0'], FEN['a1'], FEN['t'], FEN['rim']
    fo = [(a0, 0), (a1 - .006, 0), (a1 - .006, -rim), (a1, -rim), (a1, t_), (a0, t_)] if hi else [(a0, 0), (a1, 0), (a1, t_), (a0, t_)]
    Rin = WHEEL_R + FEN['gap']; nseg = 8 if hi else 4
    for zc in AXLES.values():
        C = np.array([0, WHEEL_R, zc]); frames = []
        for k in range(nseg + 1):
            ph = math.radians(FEN['arc'][0] + (FEN['arc'][1] - FEN['arc'][0]) * k / nseg)
            rh = np.array([0, math.sin(ph), math.cos(ph)]); frames.append((C + Rin * rh, X3, rh))
        Lf = sweep(body, fo, frames, isl='fender', tag='fender', sharp=[True, True, True, True, False, True] if hi else None, cap_isl='fender')
        ybase = CH['y1'] - .004; phb = math.asin((ybase - WHEEL_R) / Rin)
        angs = [FEN['arc'][0] + (FEN['arc'][1] - FEN['arc'][0]) * k / nseg for k in range(nseg + 1)]
        angs = [math.degrees(phb)] + [g for g in angs if math.radians(g) > phb + 1e-3 and math.radians(g) < math.pi - phb - 1e-3] + [180 - math.degrees(phb)]
        pts = [(zc + Rin * math.cos(math.radians(g)), WHEEL_R + Rin * math.sin(math.radians(g))) for g in angs]
        Q = np.array(pts); Q = Q[::-1] if poly_area(Q) < 0 else Q
        for x, sgn in ((.3035, -1), (.3065, 1)):
            ids = [body.vert((x, q[1], q[0])) for q in Q]
            _cap(body, Q, ids, (sgn, 0, 0), 'skirt', lambda q, zc=zc: (q[0] - zc + .2, q[1] - CH['y1'] + .01), 0, 'fender')
    meta('fender', s=Lf['s'], length=Lf['u'][-1])
    body.mirror(mk)
    # ---- backrest, authored flat in its own frame (x, y up the board, d toward the sitter), then bent and leaned
    mk = body.mark()
    hb = BACK['w'] / 2
    o = rrect(BACK['w'], BACK['t'], BACK['t'] / 2, 3 if hi else 1, xs=(.09, .17, .22, .255) if hi else (.17, .22))
    prof = roll(0, BACK['h'], .010, .010, 1, rs, extra=(BRAND['y0'], BRAND['y1']))
    ib = [i for i in range(len(prof)) if abs(prof[i][1] - BRAND['y0']) < 1e-9][0]
    jb = [j for j in range(len(o)) if abs(o[j][0] - BRAND['x0']) < 1e-9 and o[j][1] < 0][0]
    assert abs(o[(jb + 1) % len(o)][0] - BRAND['x1']) < 1e-9
    ovr = lambda i, j: (lambda a_, b_, h_: ('brand', a_ - BRAND['x0'], h_ - BRAND['y0'])) if (i == ib and j == jb) else None
    L = loft(body, o, prof, (0, 0, 0), X3, Z3, Y3, isl='backboard', mirror=True, override=ovr,
             ow=lambda M: np.where((M[:, 1] > 0) & (np.abs(M[:, 0]) < hb - BACK['t'] / 2), .5, 1.0),
             tag=lambda i, j: 'brand' if (i == ib and j == jb) else ('backboard_rear' if edge_tag(o, j, lambda q: q[1] < 0) else 'backboard'),
             cap0='backboard_cap', cap1='backboard_cap', cap_tag='backboard')
    meta('backboard', t=L['t'], half=L['s'][L['n'] // 2], rear0=min(L['s'][j] for j in range(L['n'] // 2 + 1) if o[j][1] < -BACK['t'] / 2 + 1e-6),
         front_end=max(L['s'][j] for j in range(L['n'] // 2 + 1) if o[j][1] > BACK['t'] / 2 - 1e-6), prof=np.asarray(prof))
    meta('brand', w=BRAND['x1'] - BRAND['x0'], h=BRAND['y1'] - BRAND['y0'])
    # embedded sign frame + plate (Sign material); bent with the board so it never floats
    fr = rrect(SIGNF['w'], SIGNF['h'], 0, 0, xs=(.08,) if hi else ())
    fprof = [(0, -.010), (0, .005), (.020, .005), (.020, -.002)]
    L = loft(body, fr, fprof, (0, SIGNF['yc'], -BACK['t'] / 2), X3, Y3, -Z3, isl='frame', tag='frame', mirror=True,
             sharp_p=[True] * len(fprof), sharp_o=True)
    meta('frame', t=L['t'])
    pw2, ph2 = SIGNF['w'] / 2 - SIGNF['border'], SIGNF['h'] / 2 - SIGNF['border']
    cols = 4 if hi else 2; dpl = -BACK['t'] / 2 - .0015
    grid = [[body.vert((-pw2 + 2 * pw2 * c / cols, SIGNF['yc'] - ph2 + 2 * ph2 * rr, dpl)) for c in range(cols + 1)] for rr in (0, 1)]
    for c in range(cols):
        ids = [grid[0][c], grid[0][c + 1], grid[1][c + 1], grid[1][c]]
        uv = [('SIGN', c / cols, 0), ('SIGN', (c + 1) / cols, 0), ('SIGN', (c + 1) / cols, 1), ('SIGN', c / cols, 1)]
        body.quad(ids, uv, [(0, 0, -1)] * 4, 1, 'sign')
    # back cushion (right) and post, mirrored
    bmk = body.mark()
    bprof = ([(.006, .014), (0, .020), (0, .058), (-.003, .0635), (0, .069), (.014, .079), (.034, .084)] if hi
             else [(.006, .014), (0, .066), (.034, .084)])
    L = loft(body, rrect(.24, .47, .045, 2 if hi else 1), bprof, (.13, .25, 0), X3, Y3, Z3, isl='backcush_band', tag='back_cushion', mirror=True,
             cap1='backcush_front')
    meta('backcush_band', t=L['t'], pip=(L['t'][2], L['t'][4]) if hi else None)
    meta('backcush_front', w=.12, h=.47)
    lean = math.radians(BACK['lean'])
    Yl = np.array([0, math.cos(lean), -math.sin(lean)]); Dl = np.array([0, math.sin(lean), math.cos(lean)])
    Ob = np.array([0, BACK['y'], BACK['z']]); Rb = hb ** 2 / (2 * BACK['sag'])
    def bend_p(p):
        x, y, d = p; aa = x / Rb; rr = Rb - d
        return Ob + rr * math.sin(aa) * X3 + y * Yl + (Rb - rr * math.cos(aa)) * Dl
    def bend_n(p, nn):
        aa = p[0] / Rb; c, s_ = math.cos(aa), math.sin(aa)
        return nrm((nn[0] * c - nn[2] * s_) * X3 + nn[1] * Yl + (nn[0] * s_ + nn[2] * c) * Dl)
    dpost = -BACK['t'] / 2 - BACK['post_r'] + .001
    yb = (SB['y1'] - .02 - bend_p((BACK['post_x'], 0, dpost))[1]) / Yl[1]
    lathe(body, [(BACK['post_r'], yb), (BACK['post_r'], .50)], (BACK['post_x'], 0, dpost), Y3, X3, Z3, 8 if hi else 6,
          isl='backpost', tag='posts', cap1='backboard_cap')
    meta('backpost', length=.50 - yb)
    body.mirror(bmk)
    body.xform(mk, bend_p, bend_n)
    if hi:
        ytop = (SB['y1'] - bend_p((BACK['post_x'], 0, dpost))[1]) / Yl[1]
        for sx in (1, -1):
            foot = bend_p((sx * BACK['post_x'], ytop, dpost))
            lathe(body, [(.024, -.002), (.020, .006)], foot, Y3, X3, Z3, 6, isl='dark', tag='dark_small', cap1='dark', uvs=.5)
        anchors['Anchor_SignBack'] = (bend_p((0, SIGNF['yc'], dpl)), bend_n((0, 0, 0), (0, 0, -1)), bend_n((0, 0, 0), (0, 1, 0)))
    # ---- axle: two wheels (16 segments, two-step shoulder, flat dark inner cap, small dark hub) + visible stubs
    tp = [(.140, -.0425), (.140, .024), (.133, .036), (.118, .0425), (.076, .037), (.026, .044)]
    wmk = axle.mark()
    Lt = lathe(axle, tp, (WHEEL_X, 0, 0), X3, Y3, Z3, 16, isl='tire', tag='tire', cap0='hidden', cap1='hub',
               sharp_p=[True, False, False, False, True, True])
    lathe(axle, [(.012, .27), (.012, WHEEL_X - .04)], (0, 0, 0), X3, Y3, Z3, 8, isl='dark', tag='dark_small', uvs=.5)
    axle.mirror(wmk)
    meta('tire', t=Lt['t']); meta('hub', r=.026)
    return body, axle, anchors

# ------------------------------------------------------------------ build + budget
body0, axle0, ANCH = build(0)
body1, _, _ = build(1)
def tag_counts(mb):
    c = {}
    for t in mb.T: c[t[4]] = c.get(t[4], 0) + 1
    return dict(sorted(c.items(), key=lambda kv: -kv[1]))
N_BODY, N_AXLE, N_LOD1 = len(body0.T), len(axle0.T), len(body1.T)
print('TRI body', N_BODY, 'axle', N_AXLE, 'x2 ->', N_BODY + 2 * N_AXLE, '| LOD1 body', N_LOD1)
print('TAGS body', tag_counts(body0)); print('TAGS axle', tag_counts(axle0)); print('TAGS lod1', tag_counts(body1))
assert N_BODY + 2 * N_AXLE <= BUDGET_LOD0, 'LOD0 over budget'
assert N_LOD1 <= BUDGET_LOD1, 'LOD1 over budget'
def geom_checks(mb, name):
    bad = sum(1 for t in mb.T if np.linalg.norm(np.cross(mb.V[t[0][1]] - mb.V[t[0][0]], mb.V[t[0][2]] - mb.V[t[0][0]])) < 1e-10)
    assert bad == 0, (name, 'zero-area 3D triangles', bad)
for mb_, nm in ((body0, 'body'), (axle0, 'axle'), (body1, 'lod1')): geom_checks(mb_, nm)
# island extents from LOD0; LOD1 must stay inside them
for mb_ in (body0, axle0):
    for t in mb_.T:
        for isl, u, v in t[1]:
            if isl == 'SIGN': continue
            assert u >= -1e-9 and v >= -1e-9, (isl, u, v)
            ISL[isl]['w'] = max(ISL[isl]['w'], u); ISL[isl]['h'] = max(ISL[isl]['h'], v)
for t in body1.T:
    for isl, u, v in t[1]:
        if isl != 'SIGN': assert u <= ISL[isl]['w'] + 1e-6 and v <= ISL[isl]['h'] + 1e-6, ('LOD1 outside island', isl, u, v)
unused = [k for k, v in ISL.items() if v['w'] == 0]; assert not unused, unused

# ------------------------------------------------------------------ packing (skyline, bottom-left, rotation allowed)
ORDERS = {
    'maxside': lambda it: (-max(it[2], it[3]), -it[2] * it[3]),
    'area': lambda it: (-it[2] * it[3],),
    'height': lambda it: (-min(it[2], it[3]), -max(it[2], it[3])),
    'perimeter': lambda it: (-(it[2] + it[3]),)}
def pack(k, f=1.0, order='maxside'):
    """Skyline bottom-left packing with rotation. Near islands keep their density (x f, capped at 16 px/cm);
    mid/far islands are scaled by k."""
    items = []
    for name, I in ISL.items():
        s = I['d'] * ((min(f, 16 / I['d']) if I['d'] < 16 else 1) if I['fixed'] else k) * 100
        items.append((name, s, math.ceil(I['w'] * s) + 1, math.ceil(I['h'] * s) + 1))
    items.sort(key=ORDERS[order])
    sky = [[0, ATLAS, 0]]; placed = {}
    for name, s, w0, h0 in items:
        best = None
        for rot in (False, True):
            w, h = (h0, w0) if rot else (w0, h0); w += 2 * PAD; h += 2 * PAD
            for i in range(len(sky)):
                x = sky[i][0]
                if x + w > ATLAS: break
                y, rem, j = 0, w, i
                while rem > 0: y = max(y, sky[j][2]); rem -= sky[j][1]; j += 1
                rem, j, waste = w, i, 0
                while rem > 0: waste += (y - sky[j][2]) * min(rem, sky[j][1]); rem -= sky[j][1]; j += 1
                if y + h > ATLAS: continue
                sc = (y + h, waste, x)
                if best is None or sc < best[0]: best = (sc, x, y, w, h, rot)
        if best is None: return None
        _, x, y, w, h, rot = best
        new = []
        for seg in sky:
            sx, sw, sy = seg
            if sx + sw <= x or sx >= x + w: new.append(seg); continue
            if sx < x: new.append([sx, x - sx, sy])
            if sx + sw > x + w: new.append([x + w, sx + sw - x - w, sy])
        new.append([x, w, y + h]); new.sort(); sky = []
        for seg in new:
            if sky and sky[-1][2] == seg[2] and sky[-1][0] + sky[-1][1] == seg[0]: sky[-1][1] += seg[1]
            else: sky.append(seg)
        placed[name] = dict(x=x + PAD, y=y + PAD, rot=rot, s=s, w=w0, h=h0)
    return placed
def best_k(f, order):
    lo_, hi_ = .3, 6.0
    if not pack(lo_, f, order): return 0
    for _ in range(24):
        mid = (lo_ + hi_) / 2
        if pack(mid, f, order): lo_ = mid
        else: hi_ = mid
    return lo_
def fill_of(pl): return sum(p['w'] * p['h'] for p in pl.values()) / ATLAS ** 2
CANDS = []
for order in ORDERS:
    for f in (1.0, 1.07, 1.14, 1.2, 1.25):
        k = best_k(f, order)
        pl = pack(k * .99, f, order) if k else None
        if pl: CANDS.append((fill_of(pl), k * .99, f, order))
CANDS.sort(reverse=True); _, K_SCALE, NEAR_F, PACK_ORDER = CANDS[0]
PLACE = pack(K_SCALE, NEAR_F, PACK_ORDER); assert PLACE
print('PACK candidates', [(round(c[0], 3), round(c[1], 2), c[2], c[3]) for c in CANDS[:6]])
def uvf(isl, u, v):
    if isl == 'SIGN': return (u, v)
    p = PLACE[isl]; s = p['s']
    return ((p['x'] + v * s) / ATLAS, (p['y'] + u * s) / ATLAS) if p['rot'] else ((p['x'] + u * s) / ATLAS, (p['y'] + v * s) / ATLAS)
print('PACK k=%.3f f=%.2f %s' % (K_SCALE, NEAR_F, PACK_ORDER), {k: (round(p['s'] / 100, 2), p['w'], p['h'], p['rot']) for k, p in PLACE.items()})

# ------------------------------------------------------------------ BaseColor painting (numpy, island-local metres)
# Colour layer = material ID per pixel; value layer = luminance multiplier (grain, wear, non-directional AO).
# No directional light is baked. Lines are >= 3 mm except the 50 mm brand lettering.
def _hash(ix, iy, seed):
    h = (ix.astype(np.int64) * 73856093) ^ (iy.astype(np.int64) * 19349663) ^ (seed * 83492791)
    h &= 0xffffffff; h = ((h ^ (h >> 13)) * 1274126177) & 0xffffffff
    return ((h ^ (h >> 16)) & 0xffff) / 65535.0
def vnoise(u, v, cell, seed=0):
    x, y = u / cell, v / cell; ix, iy = np.floor(x), np.floor(y); fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b, c, d = _hash(ix, iy, seed), _hash(ix + 1, iy, seed), _hash(ix, iy + 1, seed), _hash(ix + 1, iy + 1, seed)
    return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy
def ss(e0, e1, x): t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def line(x, c, w): return ss(w / 2 + .0008, w / 2, np.abs(x - c))
def tone(u, v, seed, a=.03): return 1 + a * (2 * vnoise(u, v, .25, seed) - 1) + .5 * a * (2 * vnoise(u, v, .06, seed + 5) - 1)
def wear(u, v, mask, seed, amt=.12): return 1 + amt * mask * ss(.35, .7, .6 * vnoise(u, v, .03, seed + 9) + .4 * vnoise(u, v, .011, seed + 2))
def full(u, name): return np.full(u.shape, ID[name], np.int32)
def blur(a, r):
    for _ in range(r): a = (a + np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 5
    return a
def wood(al, ac, seed, knots=()):
    """Stylised oiled wood: broad wavy bands (about 3.5 waves per 50 cm), 4-5 mm grain lines, small knots."""
    lam, sp = .14, .032; ph = seed * 2.399
    warp = .24 * np.sin(2 * np.pi * al / lam + ph) + .08 * np.sin(4 * np.pi * al / lam + 1.7 * ph)
    g = ac / sp + warp + .45 * (vnoise(al * .3, ac, .06, seed) - .5)
    f = g - np.floor(g); dist = np.minimum(f, 1 - f) * sp
    lines = ss(.0024, .0015, dist) * (.65 + .35 * vnoise(al, ac, .05, seed + 3))
    early = ss(.2, .4, f) * (1 - ss(.6, .8, f))
    broad = vnoise(al * .25, ac, .09, seed + 11)
    lum = 1 + .07 * (broad - .5) * 2 + .04 * early - .13 * lines
    for ka, kc in knots:
        r = np.hypot((al - ka) * .8, ac - kc)
        lum = lum * (1 - .30 * np.exp(-(r / .0045) ** 2) + .06 * np.exp(-((r - .009) / .002) ** 2))
    return lum
FONT = {  # 7-row original bitmap letters for the name mark (no logo)
    'V': ['10001', '10001', '10001', '10001', '01010', '01010', '00100'],
    'A': ['01110', '10001', '10001', '11111', '10001', '10001', '10001'],
    'n': ['0000', '0000', '1110', '1001', '1001', '1001', '1001'],
    'i': ['1', '0', '1', '1', '1', '1', '1'],
    'm': ['00000', '00000', '11110', '10101', '10101', '10101', '10101'],
    'a': ['0000', '0000', '0111', '0001', '0111', '1001', '0111'],
    'l': ['1', '1', '1', '1', '1', '1', '1']}
def text_bitmap(word):
    rows = ['' for _ in range(7)]
    for k, ch in enumerate(word):
        for r in range(7): rows[r] += FONT[ch][r] + ('0' if k < len(word) - 1 else '')
    return np.array([[int(c) for c in row] for row in rows], float)
BRAND_BITMAP = text_bitmap('VAnimal')

def P_chassis_band(m, u, v):
    t = m['t']; lum = tone(u, v, 1)
    lum *= wear(u, v, line(v, t[-2], .006), 1)
    lum *= 1 - .22 * ss(t[1], 0, v)
    return full(u, 'moss'), lum
def P_chassis_top(m, u, v):
    x, z = u, v + CH['z0']; lum = tone(u, v, 2)
    d1 = np.hypot(np.maximum(x - SB['hw'], 0), np.maximum(np.maximum(SB['z0'] - z, z - SB['z1']), 0))
    d2 = np.hypot(np.maximum(x - FR['w'] / 2, 0), np.maximum(np.maximum(FR['zc'] - FR['d'] / 2 - z, z - FR['zc'] - FR['d'] / 2), 0))
    lum *= (1 - .35 * np.exp(-d1 / .025)) * (1 - .22 * np.exp(-d2 / .012))
    for pz in ARMPOST['z']: lum *= 1 - .3 * np.exp(-np.hypot(x - ARMPOST['x'], z - pz) / .02)
    return full(u, 'moss'), lum
def P_seat_band(m, u, v):
    t = m['t']; vt = t[-1]; u0, u1 = m['side']; lum = tone(u, v, 3)
    lum *= 1 - .45 * ss(vt - .05, vt, v) ** 1.5
    lum *= 1 - .15 * ss(.03, 0, v)
    span = ss(.015, .03, v) * ss(vt - .02, vt - .035, v)
    for f in (1 / 3, 2 / 3):
        c = u0 + (u1 - u0) * f
        lum *= (1 - .35 * line(u, c, .0035) * span) * (1 + .10 * line(u, c + .0035, .003) * span)
    lum *= wear(u, v, np.clip(line(u, u0 - .024, .02) + line(u, u1 + .024, .02), 0, 1), 3, .08)
    lum *= wear(u, v, line(v, (t[-2] + t[-1]) / 2, .005), 4)
    return full(u, 'moss'), lum
def P_cushion_band(m, u, v):
    t = m['t']; p0, p1 = m['pip']; fe = m['front_end']
    ids = full(u, 'cloth'); lum = tone(u, v, 5)
    pip = (v > p0 + .002) & (v < p1 - .002)
    ids = np.where(pip, ID['brand'], ids)
    lum = np.where(pip, lum * (1 + .14 * np.sin(np.clip((v - p0) / (p1 - p0), 0, 1) * np.pi)), lum)
    st = p1 + .0015
    lum *= (1 - .28 * line(v, st + .0015, .003)) * (1 + .10 * line(v, st + .0045, .003))
    lum *= 1 - .30 * ss(t[1] + .004, 0, v)
    lum *= 1 + .05 * (u < fe) * line(v, (t[5] + t[6]) / 2, .008)
    return ids, lum
def P_cushion_top(m, u, v):
    lum = tone(u, v, 7)
    lum *= 1 - .18 * np.exp(-v / .05)
    edge = np.minimum(m['w'] - .04 - u, np.minimum(v, m['d'] - v) - .04)
    lum *= 1 - .06 * np.exp(-np.maximum(edge, 0) / .02)
    return full(u, 'cloth'), lum
def P_footrest_band(m, u, v):
    t = m['t']; fe = m['front_end']; lum = .88 * tone(u, v, 8)
    lum *= 1 - .25 * ss(.006, 0, v)
    lum *= wear(u, v, (u < fe) * ss(t[-3], t[-1], v), 9, .16)
    return full(u, 'moss'), lum
def P_footrest_top(m, u, v):
    d = m['d']; bw = d / 4; vb = v % bw; lum = .88 * tone(u, v, 10)
    lum *= 1 + (wood(u, v, 12) - 1) * .35
    seam = np.clip(line(vb, 0, .0035) + line(vb, bw, .0035), 0, 1) * (v > .01) * (v < d - .01)
    lum *= 1 - .45 * seam
    sc = np.exp(-(((u - .11) / .07) ** 2 + ((v - .24) / .09) ** 2))
    lum *= 1 + .14 * sc * (.55 + .45 * vnoise(u * 1.7, v, .045, 13))
    lum *= wear(u, v, ss(d - .03, d, v), 14, .14)
    return full(u, 'moss'), lum
def P_armrest(m, u, v):
    v0, v1 = m['top']; L = m['length']; b0, b1 = m['bottom']
    lum = wood(u, v, 3, knots=[(L * .37, (v0 + v1) / 2 + .012)])
    topm = ss(v0 - .014, v0 + .002, v) * ss(v1 + .014, v1 - .002, v)
    rub = ss(L - .30, L - .12, u) * (1 - .5 * ss(L - .015, L, u))
    lum *= 1 + .11 * rub * topm
    lum *= 1 + .05 * np.clip(topm - ss(v0, v0 + .01, v) * ss(v1, v1 - .01, v), 0, 1)
    under = (v < b0) | (v > b1)
    lum = np.where(under, lum * .80, lum)
    for p in m['posts']: lum *= 1 - .25 * np.exp(-np.abs(u - p) / .03) * under
    return full(u, 'wood'), lum
def P_armrest_cap(m, u, v):
    f = (np.hypot(u - m['w'] / 2, v + .03) / .010) % 1
    return full(u, 'wood'), .86 * (1 - .18 * ss(.60, .68, f) * ss(.98, .92, f)) * tone(u, v, 15, .02)
def P_stop_base(m, u, v):
    u0, u1 = m['top']; t = m['t']; vlo, vhi = t[1], t[-2]
    ids = full(u, 'dark'); lum = tone(u, v, 16, .02)
    ontop = (u > u0 - .001) & (u < u1 + .001)
    border = ontop & (((u - u0) < .006) | ((u1 - u) < .006) | (v < vlo + .004) | (v > vhi - .004))
    ids = np.where(border, ID['brand'], ids)
    lum = np.where(border, lum * 1.25, np.where(ontop, lum * 1.10, lum))
    return ids, lum
def P_stop_cap(m, u, v): return full(u, 'dark'), tone(u, v, 26, .02)
def P_button_side(m, u, v): return full(u, 'stop'), tone(u, v, 27, .02) * (1 - .25 * ss(.003, 0, v))
def P_button_top(m, u, v):
    R = m['r']; x, y = u - R, v - R
    ids = full(u, 'stop'); lum = tone(u, v, 17, .02) * (1 + .06 * ss(R, 0, np.hypot(x, y)))
    k, rr = .0065, .0015
    sq = np.hypot(np.maximum(np.abs(x) - k + rr, 0), np.maximum(np.abs(y) - k + rr, 0)) < rr
    return np.where(sq, ID['cream'], ids), lum
def P_fender(m, u, v):
    s = m['s']; L = m['length']; lum = tone(u, v, 18)
    under = v < s[1]; rimin = (v >= s[1]) & (v < s[3])
    lum = np.where(under, lum * (.62 + .12 * ss(0, .02, np.minimum(u, L - u))), lum)
    lum = np.where(rimin, lum * .8, lum)
    lum *= 1 + .28 * line(v, s[4], .004)
    lum *= wear(u, v, line(v, s[4], .008) * np.clip(ss(.06, 0, u) + ss(L - .06, L, u), 0, 1), 19, .1)
    return full(u, 'moss'), lum
def P_skirt(m, u, v): return full(u, 'moss'), .5 * tone(u, v, 28) * (1 - .2 * ss(.06, 0, v))
def P_frame(m, u, v):
    t = m['t']; lum = tone(u, v, 20, .02)
    lum *= 1 + .22 * line(v, t[1] + .002, .004)
    return full(u, 'brand'), np.where(v > t[2], lum * .78, lum)
def _bb_straight(m):
    prof = m['prof']; ks = [i for i in range(len(prof)) if abs(prof[i][0]) < 1e-12]
    return ks, m['t'][ks[0]], m['t'][ks[-1]]
def P_backboard(m, u, v):
    t = m['t']; half, r0, fe = m['half'], m['rear0'], m['front_end']
    ks, vlo, vhi = _bb_straight(m)
    lum = wood(u, v, 21, knots=[(half - .09, vlo + .30), (fe * .4, vlo + .16)])
    rear = u > r0 - .001
    near = np.minimum(u - r0, np.minimum(v - vlo, vhi - v))
    border = rear & (near < .024)
    lum = np.where(border, lum * .84, lum)
    lum = np.where(rear & ~border & (near < .028), lum * .74, lum)
    lum = np.where(u < fe, lum * (1 - .25 * ss(vlo + .06, vlo, v)), lum)
    return full(u, 'wood'), lum
def P_brand(m, u, v):
    bm = ISL['backboard']['meta']; ks, vlo, vhi = _bb_straight(bm)
    hs = bm['prof'][ks, 1]; ts = bm['t'][ks]
    ids, lum = P_backboard(bm, bm['half'] - (BRAND['x0'] + u), np.interp(BRAND['y0'] + v, hs, ts))
    cell = .00125; H_, W_ = BRAND_BITMAP.shape
    x0 = (m['w'] - W_ * cell) / 2; y0 = (m['h'] - H_ * cell) / 2
    c = np.floor((u - x0) / cell).astype(int); r = H_ - 1 - np.floor((v - y0) / cell).astype(int)
    ok = (c >= 0) & (c < W_) & (r >= 0) & (r < H_)
    mask = np.zeros(u.shape); mask[ok] = BRAND_BITMAP[r[ok], c[ok]]
    halo = blur(mask, 3)
    lum = lum * (1 - .22 * np.clip(halo * 1.6, 0, 1))
    ids = np.where(mask > .5, ID['burn'], ids)
    lum = np.where(mask > .5, 1 + .08 * (vnoise(u, v, .002, 31) - .5), lum)
    return ids, lum
def P_backboard_cap(m, u, v): return full(u, 'wood'), wood(u, v * 3, 22) * .94
def P_backcush_band(m, u, v):
    t = m['t']; ids = full(u, 'cloth'); lum = tone(u, v, 23)
    if m.get('pip'):
        p0, p1 = m['pip']; pip = (v > p0 + .002) & (v < p1 - .002); ids = np.where(pip, ID['brand'], ids)
        lum = lum * (1 - .25 * line(v, p1 + .003, .003)) * (1 + .10 * line(v, p1 + .006, .003))
    lum = lum * (1 - .35 * ss(t[1] + .01, t[0], v))
    return ids, lum
def P_backcush_front(m, u, v): return full(u, 'cloth'), tone(u, v, 24) * (1 - .12 * ss(.08, 0, v))
def P_backpost(m, u, v): return full(u, 'wood'), wood(v, u * .5, 25) * .96 * (1 - .3 * ss(.05, 0, v))
def P_armpost(m, u, v):
    L = m['length']; return full(u, 'moss'), tone(u, v, 29) * (1 - .35 * ss(.03, 0, v)) * (1 - .4 * ss(L - .04, L, v))
def P_tire(m, u, v):
    t = m['t']; ids = full(u, 'dark'); lum = np.full(u.shape, .92)
    tread = v < t[1]; w = t[1]
    for f in (.22, .5, .78):
        c = w * f; lum = np.where(tread, lum * (1 - .42 * line(v, c, .006)) * (1 + .10 * line(v, c + .0045, .003)), lum)
    lum = np.where((v >= t[1]) & (v < t[3]), lum * 1.10, lum)
    side = (v >= t[3]) & (v < t[4]); lum = np.where(side, lum * (1 - .08 * (v - t[3]) / (t[4] - t[3])), lum)
    rimz = v >= t[4]; ids = np.where(rimz, ID['rim'], ids)
    lum = np.where(rimz, 1 - .15 * np.clip((v - t[4]) / (t[5] - t[4]), 0, 1), lum)
    return ids, lum * (1 - .25 * ss(.006, 0, v))
def P_hub(m, u, v):
    R = m['r']; return full(u, 'hub'), 1 + .15 * ss(R, 0, np.hypot(u - R, v - R))
def P_dark(m, u, v): return full(u, 'dark'), tone(u, v, 30, .02)
def P_hidden(m, u, v): return full(u, 'moss'), np.full(u.shape, .45)
PAINT = {k: globals()['P_' + k] for k in ISL}

def paint_atlas():
    ids = np.full((ATLAS, ATLAS), ID['moss'], np.int32); lum = np.full((ATLAS, ATLAS), .6)
    for name, p in PLACE.items():
        s = p['s']; W_, H_ = (p['h'], p['w']) if p['rot'] else (p['w'], p['h'])
        x0, y0 = max(0, p['x'] - PAD), max(0, p['y'] - PAD); x1, y1 = min(ATLAS, p['x'] + W_ + PAD), min(ATLAS, p['y'] + H_ + PAD)
        X, Y = np.meshgrid(np.arange(x0, x1) + .5, np.arange(y0, y1) + .5)
        if p['rot']: u, v = (Y - p['y']) / s, (X - p['x']) / s
        else: u, v = (X - p['x']) / s, (Y - p['y']) / s
        i_, l_ = PAINT[name](ISL[name]['meta'], u, v)
        ids[y0:y1, x0:x1] = np.broadcast_to(i_, u.shape); lum[y0:y1, x0:x1] = np.broadcast_to(l_, u.shape)
    rgb = np.clip(PAL[ids] * lum[..., None], 0, 255).astype(np.uint8)
    return ids, lum, rgb

def make_sign():
    H_, W_ = SIGN_H, SIGN_W; Y, X = np.mgrid[0:H_, 0:W_] + .5
    cream = np.array([int(HEX['cream'][i:i + 2], 16) for i in (0, 2, 4)], float)
    green = np.array([int(HEX['brand'][i:i + 2], 16) for i in (0, 2, 4)], float)
    img = cream[None, None, :] * (1 + .02 * (2 * vnoise(X / W_, Y / W_, .06, 40) - 1))[..., None]
    d = np.minimum(np.minimum(X - 12, W_ - 12 - X), np.minimum(Y - 12, H_ - 12 - Y))
    mask = (d >= 0) & (d < 5)
    cx, cy = W_ - 64, 58
    paw = ((X - cx) / 17) ** 2 + ((Y - cy) / 14) ** 2 < 1
    for ox, oy in ((-19, 19), (-7, 27), (7, 27), (19, 19)): paw |= (X - cx - ox) ** 2 + (Y - cy - oy) ** 2 < 6.5 ** 2
    img[mask | paw] = green
    return np.clip(img, 0, 255).astype(np.uint8)

def write_png(path, arr):
    """8-bit RGB/Gray PNG with an sRGB chunk. arr row 0 = UV v=0 (bottom); flipped to PNG top-first here."""
    a = np.ascontiguousarray(np.flipud(arr)); h, w = a.shape[:2]; ch = 1 if a.ndim == 2 else a.shape[2]
    raw = b''.join(b'\x00' + a[y].tobytes() for y in range(h))
    def chunk(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, {1: 0, 3: 2}[ch], 0, 0, 0))
    png += chunk(b'sRGB', b'\x00') + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    pathlib.Path(path).write_bytes(png)

ATL_IDS, ATL_LUM, ATL_RGB = paint_atlas()
SIGN_RGB = make_sign()
BASE_PNG = OUT / (NAME + '_BaseColor.png'); SIGN_PNG = OUT / (NAME + '_Sign.png')
write_png(BASE_PNG, ATL_RGB); write_png(SIGN_PNG, SIGN_RGB)
(SRC / 'layers').mkdir(parents=True, exist_ok=True)
write_png(SRC / 'layers/atlas_material_id.png', (ATL_IDS * 20).astype(np.uint8))
write_png(SRC / 'layers/atlas_value.png', np.clip(ATL_LUM * 160, 0, 255).astype(np.uint8))
(SRC / 'layers/palette.json').write_text(json.dumps({'hex': HEX, 'idOrder': IDS, 'idPixelValue': 'id*20',
    'valuePixel': 'luminance*160', 'compose': 'rgb = palette[id] * luminance (sRGB, clipped)'}, indent=2), encoding='utf8')
print('ATLAS painted %.1fs' % (time.time() - T0))

# ------------------------------------------------------------------ measurements on the authored data
def tri_uv_px(t):
    a, b, c = (np.array(uvf(*x)) * ATLAS if x[0] != 'SIGN' else np.array(x[1:]) * [SIGN_W, SIGN_H] for x in t[1])
    return a, b, c
def measure(mb):
    per = {}; minpx = 1e9
    for t in mb.T:
        P = [mb.V[i] for i in t[0]]; a3 = .5 * np.linalg.norm(np.cross(P[1] - P[0], P[2] - P[0])) * 1e4
        a, b, c = tri_uv_px(t); apx = .5 * abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]))
        minpx = min(minpx, apx)
        per.setdefault(t[4], []).append((a3, math.sqrt(apx / a3) if a3 > 0 else 0))
    out = {}
    for k, lst in per.items():
        A = np.array(lst); w = A[:, 0]; d = A[:, 1]; order = np.argsort(d); cw = np.cumsum(w[order]) / w.sum()
        out[k] = dict(areaCm2=round(float(w.sum()), 1), meanPxPerCm=round(float((w * d).sum() / w.sum()), 2),
                      minPxPerCm=round(float(d.min()), 2), medianPxPerCm=round(float(d[order][np.searchsorted(cw, .5)]), 2),
                      maxPxPerCm=round(float(d.max()), 2), triangles=len(lst))
    return out, minpx
TEXEL, MINPX = {}, {}
for nm, mb_ in (('Body', body0), ('Axle', axle0), ('Body_LOD1', body1)):
    TEXEL[nm], MINPX[nm] = measure(mb_)
cover = np.zeros((ATLAS, ATLAS), bool)
for mb_ in (body0, axle0):
    for t in mb_.T:
        if t[1][0][0] == 'SIGN': continue
        a, b, c = tri_uv_px(t)
        x0, x1 = int(max(0, math.floor(min(a[0], b[0], c[0])))), int(min(ATLAS, math.ceil(max(a[0], b[0], c[0]))))
        y0, y1 = int(max(0, math.floor(min(a[1], b[1], c[1])))), int(min(ATLAS, math.ceil(max(a[1], b[1], c[1]))))
        if x1 <= x0 or y1 <= y0: continue
        X, Y = np.meshgrid(np.arange(x0, x1) + .5, np.arange(y0, y1) + .5)
        def e(p, q): return (q[0] - p[0]) * (Y - p[1]) - (q[1] - p[1]) * (X - p[0])
        w0, w1, w2 = e(b, c), e(c, a), e(a, b)
        cover[y0:y1, x0:x1] |= ((w0 >= 0) & (w1 >= 0) & (w2 >= 0)) | ((w0 <= 0) & (w1 <= 0) & (w2 <= 0))
UV_COVERAGE = float(cover.mean())
RECT_FILL = sum(p['w'] * p['h'] for p in PLACE.values()) / ATLAS ** 2
print('UV coverage %.3f  rect fill %.3f' % (UV_COVERAGE, RECT_FILL))
def colour_stats(name):
    sel = ATL_RGB[cover & (ATL_IDS == ID[name])]
    return dict(base='#' + HEX[name], measuredMean='#%02x%02x%02x' % tuple(int(round(x)) for x in sel.mean(0)),
                p10='#%02x%02x%02x' % tuple(int(x) for x in np.percentile(sel, 10, 0)), p90='#%02x%02x%02x' % tuple(int(x) for x in np.percentile(sel, 90, 0)),
                pixels=int(len(sel)))
COLOURS = {k: colour_stats(k) for k in ('moss', 'cloth', 'wood', 'dark', 'brand')}

# ------------------------------------------------------------------ Blender scene
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images): [coll.remove(x) for x in list(coll)]
scene = bpy.context.scene; scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1
def material(name, png, rough):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Roughness'].default_value = rough
    if 'Specular IOR Level' in bs.inputs: bs.inputs['Specular IOR Level'].default_value = .3
    img = bpy.data.images.load(str(png), check_existing=False); img.colorspace_settings.name = 'sRGB'
    tx = m.node_tree.nodes.new('ShaderNodeTexImage'); tx.image = img; tx.interpolation = 'Linear'
    m.node_tree.links.new(tx.outputs['Color'], bs.inputs['Base Color'])
    m.diffuse_color = (*[(c / 255) ** 2.2 for c in PAL[ID['moss']]], 1)
    return m
MAT_BODY = material(NAME + '_Body', BASE_PNG, .78); MAT_SIGN = material(NAME + '_Sign', SIGN_PNG, .85)
def make_obj(name, mb, origin, mats, local=False):
    o = np.asarray(origin, float); verts = [B(p if local else p - o) for p in mb.V]
    faces, uvs, nrms, mi = [], [], [], []
    flips = 0
    for idx, uv, ns, mat, tag in mb.T:
        P = [verts[i] for i in idx]; g = np.cross(P[1] - P[0], P[2] - P[0]); nb = [B(n) for n in ns]
        if g @ (nb[0] + nb[1] + nb[2]) < 0: idx, uv, nb = [idx[0], idx[2], idx[1]], [uv[0], uv[2], uv[1]], [nb[0], nb[2], nb[1]]; flips += 1
        faces.append(idx); uvs += [uvf(*x) for x in uv]; nrms += [tuple(nrm(n)) for n in nb]; mi.append(min(mat, len(mats) - 1))
    me = bpy.data.meshes.new(name); me.from_pydata([tuple(v) for v in verts], [], faces); me.update()
    assert len(me.polygons) == len(faces) and len(me.loops) == 3 * len(faces)
    for m in mats: me.materials.append(m)
    me.polygons.foreach_set('material_index', mi)
    uvl = me.uv_layers.new(name='UVMap'); uvl.data.foreach_set('uv', np.array(uvs, np.float32).ravel())
    me.polygons.foreach_set('use_smooth', [True] * len(faces))
    me.normals_split_custom_set(nrms); me.update()
    ob = bpy.data.objects.new(name, me); scene.collection.objects.link(ob); ob.location = Vector(B(o))
    return ob
OBJ = {}
OBJ['Body'] = make_obj(NAME + '_Body', body0, (0, 0, 0), [MAT_BODY, MAT_SIGN])
OBJ['Body_LOD1'] = make_obj(NAME + '_Body_LOD1', body1, (0, 0, 0), [MAT_BODY, MAT_SIGN])
for k, z in AXLES.items(): OBJ['Axle' + k] = make_obj(NAME + '_Axle' + k, axle0, (0, WHEEL_R, z), [MAT_BODY], local=True)
def empty(name, pos, fwd, up=(0, 1, 0)):
    zb = nrm(B(up)); yb = -nrm(B(fwd)); xb = np.cross(yb, zb); zb = np.cross(xb, yb)
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'ARROWS'; e.empty_display_size = .08
    e.matrix_world = Matrix.Translation(Vector(B(pos))) @ Matrix(np.column_stack([xb, yb, zb]).tolist()).to_4x4()
    scene.collection.objects.link(e); return e
ANCHORS = {}
for nm, (p, f) in ANCHORS_FIXED.items(): ANCHORS[nm] = dict(position=list(p), forward=list(f), up=[0, 1, 0])
for nm, (p, f, u) in ANCH.items(): ANCHORS[nm] = dict(position=[float(x) for x in p], forward=[float(x) for x in nrm(f)], up=[float(x) for x in nrm(u)])
for nm in sorted(ANCHORS): empty(nm, ANCHORS[nm]['position'], ANCHORS[nm]['forward'], ANCHORS[nm]['up'])
EXPORT = [OBJ['Body'], OBJ['AxleFront'], OBJ['AxleRear'], OBJ['Body_LOD1']] + [bpy.data.objects[n] for n in sorted(ANCHORS)]
bpy.ops.object.select_all(action='DESELECT')
for o in EXPORT: o.select_set(True)
bpy.context.view_layer.objects.active = OBJ['Body']
FBX = OUT / (NAME + '.fbx')
bpy.ops.export_scene.fbx(filepath=str(FBX), use_selection=True, object_types={'MESH', 'EMPTY'}, bake_anim=False, add_leaf_bones=False,
                         axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, path_mode='AUTO',
                         mesh_smooth_type='OFF', use_mesh_modifiers=False)
print('FBX written %.1fs' % (time.time() - T0))

# ------------------------------------------------------------------ independent read of the saved FBX (predicts the Unity pose)
from io_scene_fbx import parse_fbx
tree, _ = parse_fbx.parse(str(FBX))
def child(e, name): return next((c for c in e.elems if c.id == name), None)
def props70(e):
    p = child(e, b'Properties70'); return {} if p is None else {c.props[0].decode(): c.props[4:] for c in p.elems}
gs = props70(child(tree, b'GlobalSettings')); unit = gs.get('UnitScaleFactor', [1.0])[0]
k_m = unit / 100.0
objs = child(tree, b'Objects')
models = {}
for e in objs.elems:
    if e.id == b'Model':
        nm = e.props[1].split(b'\x00\x01')[0].decode(); pr = props70(e)
        models[nm] = dict(type=e.props[2].decode(), uid=e.props[0], T=list(pr.get('Lcl Translation', [0, 0, 0])),
                          R=list(pr.get('Lcl Rotation', [0, 0, 0])), S=list(pr.get('Lcl Scaling', [1, 1, 1])),
                          pre='PreRotation' in pr, post='PostRotation' in pr)
geoms = {e.props[0]: e for e in objs.elems if e.id == b'Geometry'}
conns = [(c.props[1], c.props[2]) for c in child(tree, b'Connections').elems]
def rmat(r):
    rx, ry, rz = (math.radians(x) for x in r)
    Rx = np.array([[1, 0, 0], [0, math.cos(rx), -math.sin(rx)], [0, math.sin(rx), math.cos(rx)]])
    Ry = np.array([[math.cos(ry), 0, math.sin(ry)], [0, 1, 0], [-math.sin(ry), 0, math.cos(ry)]])
    Rz = np.array([[math.cos(rz), -math.sin(rz), 0], [math.sin(rz), math.cos(rz), 0], [0, 0, 1]])
    return Rz @ Ry @ Rx
S_ = np.diag([-1., 1, 1])
FBX_CHECK = dict(unitScaleFactor=unit, metresPerFbxUnit=k_m, nulls={}, meshes={})
for nm, md in models.items():
    M = S_ @ rmat(md['R']) @ S_; pos = (S_ @ np.array(md['T'])) * k_m
    if md['type'] == 'Null':
        a = ANCHORS[nm]
        FBX_CHECK['nulls'][nm] = dict(unityPosition=[round(x, 5) for x in pos], unityForward=[round(x, 4) for x in M[:, 2]],
                                      unityUp=[round(x, 4) for x in M[:, 1]], preRotation=md['pre'],
                                      positionError=float(np.linalg.norm(pos - np.array(a['position']))),
                                      forwardDot=float(M[:, 2] @ np.array(a['forward'])))
        assert not md['pre'] and not md['post'], ('PreRotation on null', nm)
        assert FBX_CHECK['nulls'][nm]['positionError'] < 1e-4 and FBX_CHECK['nulls'][nm]['forwardDot'] > .9999, (nm, FBX_CHECK['nulls'][nm])
    elif md['type'] == 'Mesh':
        gid = next(c for c, p in conns if p == md['uid'] and c in geoms)
        raw = np.array(child(geoms[gid], b'Vertices').props[0]).reshape(-1, 3)
        Mf = rmat(md['R']) * np.array(md['S'])[None, :]
        world = ((raw @ Mf.T) + np.array(md['T'])) * k_m * np.array([-1, 1, 1])
        lo, hi = world.min(0), world.max(0)
        FBX_CHECK['meshes'][nm] = dict(unityOrigin=[round(x, 5) for x in pos], unityBoundsMin=[round(x, 4) for x in lo],
                                       unityBoundsMax=[round(x, 4) for x in hi], unitySizeXYZ=[round(x, 4) for x in hi - lo])
assert FBX_CHECK['nulls']['Anchor_StopR']['unityPosition'][0] > 0 and models['Anchor_StopR']['T'][0] < 0
for k, z in AXLES.items():
    ab = FBX_CHECK['meshes'][NAME + '_Axle' + k]
    assert abs(ab['unityBoundsMin'][1]) < 1e-3 and abs(ab['unityBoundsMax'][1] - 2 * WHEEL_R) < 1e-3 and abs((ab['unityBoundsMin'][2] + ab['unityBoundsMax'][2]) / 2 - z) < 1e-3, (k, ab)
bb = FBX_CHECK['meshes'][NAME + '_Body']
print('FBX Unity-predicted body size', bb['unitySizeXYZ'], 'axle origin', FBX_CHECK['meshes'][NAME + '_AxleFront']['unityOrigin'])

# ------------------------------------------------------------------ evidence renders (EEVEE, AO, warm 3-point, plain ochre)
def U2B(p): return Vector(B(p))
RENDERS = {}
if RENDER:
    scene.render.engine = 'BLENDER_EEVEE'; ee = scene.eevee
    ee.taa_render_samples = 64
    if hasattr(ee, 'use_fast_gi'): ee.use_fast_gi = True
    if hasattr(ee, 'fast_gi_method'):
        items = [i.identifier for i in ee.bl_rna.properties['fast_gi_method'].enum_items]
        ee.fast_gi_method = 'AMBIENT_OCCLUSION_ONLY' if 'AMBIENT_OCCLUSION_ONLY' in items else items[0]
    if hasattr(ee, 'fast_gi_distance'): ee.fast_gi_distance = .35
    RENDERS['_settings'] = dict(engine=scene.render.engine, samples=ee.taa_render_samples, use_fast_gi=getattr(ee, 'use_fast_gi', None),
                                fast_gi_method=getattr(ee, 'fast_gi_method', None), fast_gi_distance=getattr(ee, 'fast_gi_distance', None),
                                viewTransform='Standard', lights='warm key 150 W / cool fill 45 W / warm rim 110 W (area)', background='plain light ochre')
    assert RENDERS['_settings']['use_fast_gi'] and RENDERS['_settings']['fast_gi_method'] == 'AMBIENT_OCCLUSION_ONLY', RENDERS['_settings']
    scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'None'
    world = bpy.data.worlds.new('Ochre'); scene.world = world; world.use_nodes = True
    bg = world.node_tree.nodes['Background']; bg.inputs[0].default_value = (.78, .67, .48, 1); bg.inputs[1].default_value = .42
    if hasattr(world, 'light_settings'): world.light_settings.distance = .4
    gm = bpy.data.materials.new('Ground'); gm.use_nodes = True
    gb = gm.node_tree.nodes.get('Principled BSDF'); gb.inputs['Base Color'].default_value = (.60, .51, .36, 1); gb.inputs['Roughness'].default_value = 1
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, 0)); ground = bpy.context.object; ground.data.materials.append(gm)
    for nm, pos, col, energy, size in (('Key', (1.8, 2.4, 2.2), (1.0, .91, .78), 150, 1.6), ('Fill', (-2.4, 1.4, 1.2), (.88, .91, 1.0), 45, 2.5),
                                       ('Rim', (-.6, 2.2, -2.6), (1.0, .93, .82), 110, 1.4)):
        ld = bpy.data.lights.new(nm, 'AREA'); ld.energy = energy; ld.color = col; ld.size = size
        lo_ = bpy.data.objects.new(nm, ld); scene.collection.objects.link(lo_); lo_.location = U2B(pos)
        lo_.rotation_euler = (U2B((0, .5, 0)) - lo_.location).to_track_quat('-Z', 'Y').to_euler()
    cd = bpy.data.cameras.new('Evidence'); cam = bpy.data.objects.new('Evidence', cd); scene.collection.objects.link(cam); scene.camera = cam
    def shot(fname, pos, target, lens=None, ortho=None, up=(0, 1, 0), res=(1024, 768)):
        scene.render.resolution_x, scene.render.resolution_y = res; scene.render.resolution_percentage = 100
        cam.location = U2B(pos); d = U2B(target) - cam.location
        if ortho: cd.type = 'ORTHO'; cd.ortho_scale = ortho
        else: cd.type = 'PERSP'; cd.lens = lens
        cd.clip_start = .02; cd.clip_end = 50
        zc = -d.normalized(); xc = Vector(B(up)).cross(zc).normalized(); yc = zc.cross(xc)
        cam.matrix_world = Matrix.Translation(cam.location) @ Matrix((xc, yc, zc)).transposed().to_4x4()
        scene.render.filepath = str(EVID / fname); bpy.ops.render.render(write_still=True)
        RENDERS[fname] = dict(cameraUnity=[round(x, 3) for x in pos], targetUnity=list(target), lensMm=lens, orthoScale=ortho, resolution=list(res))
    def orbit(dist, az, height, target):
        a = math.radians(az); horiz = math.sqrt(max(dist ** 2 - (height - target[1]) ** 2, 1e-6))
        return (target[0] + horiz * math.sin(a), height, target[2] + horiz * math.cos(a))
    tgt = (0, .52, 0)
    shot('front-quarter.png', orbit(2.5, 38, 1.35, tgt), tgt, lens=42)
    shot('rear-quarter.png', orbit(3.0, 205, 1.30, tgt), tgt, lens=42)
    ap = ANCHORS['Anchor_StopR']['position']; at = (ARM['x'] - .01, .76, -.08)
    eye = np.array(at) + .30 * nrm((-.32, .55, -.42))
    shot('armrest-close.png', tuple(eye), at, lens=24)
    shot('side.png', (4, .55, 0), (0, .55, 0), ortho=1.45)
    shot('top.png', (0, 4, 0), (0, 0, 0), ortho=1.45, up=(-1, 0, 0))
    if '--debug-shots' in ARGV:
        shot('debug-seat-front.png', (.05, .78, .30), (.05, .49, -.01), lens=30)
        bx = (BRAND['x0'] + BRAND['x1']) / 2
        shot('debug-brand.png', (bx, .62, -.80), (bx, .575, -.55), lens=50)
        shot('debug-fender.png', (.75, .30, .42), (.33, .20, .42), lens=35)
    print('RENDERS done %.1fs' % (time.time() - T0))
# UV layout over the atlas (LOD0 body + axle)
lay = (ATL_RGB.astype(np.float32) * .72).astype(np.uint8)
for mb_ in (body0, axle0):
    for t in mb_.T:
        if t[1][0][0] == 'SIGN': continue
        pts = tri_uv_px(t)
        for p, q in ((pts[0], pts[1]), (pts[1], pts[2]), (pts[2], pts[0])):
            n = int(max(abs(q[0] - p[0]), abs(q[1] - p[1]))) + 2
            xs = np.clip(np.linspace(p[0], q[0], n).astype(int), 0, ATLAS - 1); ys = np.clip(np.linspace(p[1], q[1], n).astype(int), 0, ATLAS - 1)
            lay[ys, xs] = (250, 246, 230)
write_png(EVID / 'uv-layout.png', lay)
SRC.mkdir(parents=True, exist_ok=True)
BLEND = SRC / (NAME + '.generated.blend')
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND), copy=True)

# ------------------------------------------------------------------ round trip: fresh scene, import the saved FBX, inspect
PRE = {}
for k, o in OBJ.items():
    me = o.data
    PRE[o.name] = dict(triangles=len(me.polygons), vertices=len(me.vertices), materials=[m.name for m in me.materials],
                       dimensionsUnityM=dict(x=o.dimensions[0], y=o.dimensions[2], z=o.dimensions[1]))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(FBX))
RT = {}
for o in bpy.context.scene.objects:
    if o.type != 'MESH': continue
    me = o.data; me.calc_loop_triangles(); uv = me.uv_layers.active.data
    bad = zero = 0; minA = 1e9; umin = [1e9, 1e9]; umax = [-1e9, -1e9]
    sign_mat = [i for i, m in enumerate(me.materials) if m and m.name.startswith(NAME + '_Sign')]
    for tr in me.loop_triangles:
        a, b, c = (Vector(uv[i].uv) for i in tr.loops)
        A = abs((b - a).cross(c - a)) / 2
        s = (SIGN_W * SIGN_H) if tr.material_index in sign_mat else ATLAS * ATLAS
        minA = min(minA, A * s); zero += A == 0; bad += A * s < 1e-4
        for p in (a, b, c):
            umin = [min(umin[0], p.x), min(umin[1], p.y)]; umax = [max(umax[0], p.x), max(umax[1], p.y)]
    RT[o.name] = dict(triangles=len(me.loop_triangles), vertices=len(me.vertices), materials=[m.name for m in me.materials],
                      degenerateUVTriangles=int(bad), zeroAreaUVTriangles=int(zero), minUVTriangleAreaPx2=round(minA, 4),
                      uvMin=[round(x, 6) for x in umin], uvMax=[round(x, 6) for x in umax],
                      dimensionsBlenderRoundTrip=[round(x, 4) for x in o.dimensions])
    assert bad == 0 and zero == 0 and min(umin) >= 0 and max(umax) <= 1, (o.name, RT[o.name])
empties = sorted(o.name for o in bpy.context.scene.objects if o.type == 'EMPTY')
assert set(ANCHORS) <= set(empties), empties
for nm in (NAME + '_Body', NAME + '_Body_LOD1', NAME + '_AxleFront', NAME + '_AxleRear'):
    assert RT[nm]['triangles'] == PRE[nm]['triangles'], nm
print('ROUNDTRIP ok', {k: (v['triangles'], v['minUVTriangleAreaPx2']) for k, v in RT.items()})

# ------------------------------------------------------------------ report
report = dict(
    asset=NAME, sourceRecipe='scripts/art/osanpo-cart.py', blenderVersion=bpy.app.version_string, executionDate='2026-10-11',
    command='blender --background --factory-startup --python-exit-code 1 --python scripts/art/osanpo-cart.py',
    outputs=dict(fbx=str(FBX.relative_to(ROOT)) if FBX.is_relative_to(ROOT) else str(FBX), baseColor=BASE_PNG.name, sign=SIGN_PNG.name,
                 blend=str(BLEND), evidence=sorted(p.name for p in EVID.glob('*.png'))),
    axes=dict(authoredIn='Unity (+X right, +Y up, +Z forward, metres)', blenderMapping='B(x,y,z)=(-x,-z,y)',
              fbx=dict(axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_unit_scale=True, object_types=['MESH', 'EMPTY']),
              leftRight='L = Unity -X (rider\'s left), R = Unity +X; brand mark on the +X lower corner of the backboard'),
    budget=dict(lod0Limit=BUDGET_LOD0, lod0Triangles=N_BODY + 2 * N_AXLE, body=N_BODY, axleEach=N_AXLE,
                lod1Limit=BUDGET_LOD1, lod1Body=N_LOD1, lod1BodyPlusLod0Axles=N_LOD1 + 2 * N_AXLE,
                materials=[NAME + '_Body', NAME + '_Sign'], trianglesByPart=dict(Body=tag_counts(body0), Axle=tag_counts(axle0), Body_LOD1=tag_counts(body1))),
    meshesPreExport=PRE, fbxRoundTrip=RT, fbxRawCheck=FBX_CHECK,
    anchors=ANCHORS,
    uv=dict(atlasPx=ATLAS, gutterPx=2 * PAD, coverage=round(UV_COVERAGE, 4), rectFill=round(RECT_FILL, 4), fillTarget=list(FILL_TARGET),
            midFarScale=round(K_SCALE, 3), nearScale=NEAR_F, packOrder=PACK_ORDER, minUVTriangleAreaPx2=MINPX,
            islands={k: dict(pxPerCm=round(p['s'] / 100, 2), sizePx=[p['w'], p['h']], originPx=[p['x'], p['y']], rotated=p['rot'],
                             nearFixed=ISL[k]['fixed']) for k, p in PLACE.items()},
            texelPxPerCm=TEXEL),
    images={BASE_PNG.name: dict(size=[ATLAS, ATLAS], colorSpace='sRGB (8-bit RGB PNG with sRGB chunk)', normalMap=False),
            SIGN_PNG.name: dict(size=[SIGN_W, SIGN_H], colorSpace='sRGB (8-bit RGB PNG with sRGB chunk)', text=False)},
    colours=COLOURS, renders=RENDERS,
    unverified=['Unity import (sizes/axes/anchors are predicted from the raw FBX, not read in Unity)', 'in-world lighting, light probes',
                'PCVR / Quest / Android / iOS devices', 'mip / ASTC 6x6 appearance of 3-5 px lines'])
(REP / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False, default=float), encoding='utf8')
print('OSANPO_CART_DONE %.1fs' % (time.time() - T0))
