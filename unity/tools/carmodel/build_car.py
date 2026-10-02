"""Convert the "F1 2026 concept (polygon model)" by Qvist_designs (CC BY 4.0,
https://sketchfab.com/3d-models/f1-2026-concept-polygon-model-ea3bde709b1e4dc9b0ec8557d106ed42)
into web/src/carmodel.js: decimated, split into paint / accent / carbon / tyre / rim parts and the
active-aero flaps, in game axes (x forward, y up, metres, front axle at x=1.55, ground at y=0).

Usage: CAR_JSON=unity/LightsOutUnity/Assets/LightsOut/Resources/carmodel.json python unity/tools/carmodel/build_car.py <dir with scene.gltf + scene.bin>
(without CAR_JSON it writes web/src/carmodel.js).
Normals are rebuilt in the browser (render3d.js creaseGeo). Needs numpy, scipy and `npm i meshoptimizer` (resolved from the repo's node_modules)."""
import base64, json, os, subprocess, sys, tempfile
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'web', 'src', 'carmodel.js')
# triangle budgets: near body, mid body, far body, each wheel (Unity uses all levels; the web build keeps its lighter set)
HI, MID, LO, WHEEL = (200000, 24000, 5000, 9000) if os.environ.get('CAR_JSON') else (24000, 0, 5000, 1400)       
FRONT_AXLE_X = 1.55                        # matches the old procedural car and the cockpit camera


def load(d):
    g = json.load(open(os.path.join(d, 'scene.gltf')))
    b = open(os.path.join(d, g['buffers'][0]['uri']), 'rb').read()

    def acc(i, n):
        a = g['accessors'][i]; bv = g['bufferViews'][a['bufferView']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0); dt = np.uint32 if a['componentType'] == 5125 else np.float32
        st = bv.get('byteStride', n * 4)
        raw = np.frombuffer(b, np.uint8, (a['count'] - 1) * st + n * 4, off)
        raw = np.lib.stride_tricks.as_strided(raw, (a['count'], n * 4), (st, 1))
        return np.ascontiguousarray(raw).view(dt).reshape(-1, n)

    V, F, o = [], [], 0
    for m in g['meshes']:
        p = m['primitives'][0]
        v = acc(p['attributes']['POSITION'], 3); f = acc(p['indices'], 1).reshape(-1, 3)
        V.append(v); F.append(f + o); o += len(v)
    V = np.vstack(V).astype(np.float64); F = np.vstack(F).astype(np.int64)
    # Sketchfab root node is Z-up millimetres with the nose towards -X: convert to Y-up metres, nose +X
    V = np.c_[-V[:, 0], V[:, 2], V[:, 1]] / 1000
    # weld the per-chunk vertex copies
    key = np.round(V * 1e5).astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    V, F = V[first], inv.reshape(-1)[F]
    F = F[(F[:, 0] != F[:, 1]) & (F[:, 1] != F[:, 2]) & (F[:, 0] != F[:, 2])]
    return V, F


def components(V, F):
    e = np.r_[F[:, [0, 1]], F[:, [1, 2]]]
    n, lab = connected_components(coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(len(V), len(V))), directed=False)
    return lab[F[:, 0]]


def simplify(V, F, tgt):
    with tempfile.TemporaryDirectory() as t:
        V.astype(np.float32).tofile(f'{t}/v'); F.astype(np.uint32).tofile(f'{t}/f')
        subprocess.run(['node', os.path.join(HERE, 'simplify.mjs'), f'{t}/v', f'{t}/f', str(tgt), f'{t}/o'], check=True, cwd=HERE)
        return np.fromfile(f'{t}/o', np.uint32).reshape(-1, 3).astype(np.int64)


def segment(V, F):
    """0 paint, 1 accent (endplates), 2 carbon, 3 front flaps, 4 rear flap. The halo stays in paint."""
    C = V[F].mean(1); x, y, z = C[:, 0], C[:, 1], C[:, 2]; az = np.abs(z)
    lab = np.zeros(len(F), int)
    lab[y < 0.075] = 2                                                   # floor and plank
    lab[(x > 1.95) & (y < 0.36) & (az > 0.17)] = 2                       # front wing
    lab[(x > 1.95) & (az > 0.84)] = 1                                    # front wing endplates
    lab[(x > 2.06) & (x < 2.48) & (az > 0.17) & (az < 0.86) & (y > 0.135) & ((x > 2.18) | (y > 0.23))] = 3
    lab[(x < -1.95) & (y > 0.38)] = 2                                    # rear and beam wing
    lab[(x < -2.0) & (y > 0.3) & (az > 0.42)] = 1                        # rear wing endplates
    lab[(x < -2.33) & (y > 0.72) & (az < 0.46)] = 4                      # rear wing upper flap
    lab[(x > 1.1) & (x < 2.1) & (az > 0.25) & (y > 0.15) & (y < 0.55)] = 2   # front suspension
    lab[(x < -1.3) & (x > -2.2) & (az > 0.42) & (y > 0.12) & (y < 0.6)] = 2  # rear suspension
    return lab


def pack(V, F, groups=None, origin=(0, 0, 0)):
    used = np.unique(F); remap = np.zeros(len(V), np.int64); remap[used] = np.arange(len(used))
    V, F = V[used] - np.array(origin), remap[F]
    lo, hi = V.min(0), V.max(0); q = np.round((V - lo) / (hi - lo + 1e-12) * 65535 - 32768).astype(np.int16)
    b64 = lambda a: base64.b64encode(np.ascontiguousarray(a).tobytes()).decode()
    d = {'lo': np.round(lo, 5).tolist(), 'hi': np.round(hi, 5).tolist(), 'p': b64(q), 'i': b64(F.astype(np.uint16 if len(V) < 65536 else np.uint32))}
    if len(V) >= 65536: d['i32'] = True
    if groups: d['g'] = groups
    return d


def body_level(V, F, tgt, flaps):
    Fs = simplify(V, F, tgt); lab = segment(V, Fs)
    if not flaps: lab[lab == 3] = 2; lab[lab == 4] = 1          # far level keeps the flaps fixed
    o = np.argsort(lab, kind='stable'); Fs, lab = Fs[o], lab[o]
    out = {}
    body = lab < 3; cnt = np.bincount(lab[body], minlength=3)
    out['body'] = pack(V, Fs[body], groups=[int(c) * 3 for c in cnt])
    if flaps:
        for k, name in ((3, 'fFlap'), (4, 'rFlap')):
            Ff = Fs[lab == k]; P = V[np.unique(Ff)]
            # pivot on the flap's leading (front, lower) edge
            piv = [float(P[:, 0].max()), float(P[P[:, 0] > P[:, 0].max() - 0.03, 1].mean()), 0.0]
            out[name] = pack(V, Ff, origin=piv); out[name]['at'] = [round(c, 4) for c in piv]
    return out, Fs, lab


def main(src):
    V, F = load(src)
    comp = components(V, F)
    sizes = np.bincount(comp)
    # wheels: the four large components that sit outboard of the body
    cents = {c: V[F[comp == c].ravel()].mean(0) for c in np.argsort(-sizes)[:8]}
    wheels = [c for c, p in cents.items() if abs(p[2]) > 0.6]
    assert len(wheels) == 4, wheels
    wv = V[np.concatenate([F[comp == c].ravel() for c in wheels])]
    front = [c for c in wheels if cents[c][0] > V[:, 0].mean()]
    fx = np.mean([(V[F[comp == c].ravel()][:, 0].min() + V[F[comp == c].ravel()][:, 0].max()) / 2 for c in front])
    ground = wv[:, 1].min()
    V = V + np.array([FRONT_AXLE_X - fx, -ground, 0])
    junk = sizes[comp] < 50
    bodyF = F[~np.isin(comp, wheels) & ~junk]
    print('body', len(bodyF), 'tris')
    hi, *_ = body_level(V, bodyF, HI, True)
    lo, *_ = body_level(V, bodyF, LO, False)
    data = {'hi': hi, 'lo': lo['body'], 'wheels': []}
    if MID: data['mid'] = body_level(V, bodyF, MID, False)[0]['body']
    for c in sorted([c for c in wheels if cents[c][2] > 0], key=lambda c: -cents[c][0]):
        Fw = F[comp == c]; P = V[np.unique(Fw)]
        ctr = (P.min(0) + P.max(0)) / 2; R = (P[:, 1].max() - P[:, 1].min()) / 2
        Fw = simplify(V, Fw, WHEEL)
        r = np.hypot(*(V[Fw].mean(1)[:, :2] - ctr[:2]).T)
        tyre = r > R * 0.66; o = np.argsort(~tyre, kind='stable'); Fw = Fw[o]
        tz = V[np.unique(Fw[:tyre.sum()])][:, 2]
        tw = P[np.hypot(*(P[:, :2] - ctr[:2]).T) > R * 0.85][:, 2]
        tyre_ctr = (tw.min() + tw.max()) / 2
        ctr[2] = tyre_ctr
        w = pack(V, Fw, groups=[int(tyre.sum()) * 3, int((~tyre).sum()) * 3], origin=ctr)
        w.update(at=[round(float(c), 4) for c in ctr], r=round(float(R), 4), w=round(float(tw.max() - tw.min()), 4), front=bool(ctr[0] > 0))
        data['wheels'].append(w)
        print('wheel', w['at'], 'r', w['r'], 'width', w['w'])
    out = os.environ.get('CAR_JSON')
    if out:
        js = json.dumps(data, separators=(',', ':')); open(out, 'w').write(js); print('wrote', out, len(js), 'bytes'); return
    js = ('/* Car model: "F1 2026 concept (polygon model)" by Qvist_designs, CC BY 4.0,\n'
          '   https://sketchfab.com/3d-models/f1-2026-concept-polygon-model-ea3bde709b1e4dc9b0ec8557d106ed42\n'
          '   Decimated and split by tools/carmodel/build_car.py. Generated file: do not edit. */\n'
          'const CAR_MODEL=' + json.dumps(data, separators=(',', ':')) + ';\n')
    open(OUT, 'w').write(js)
    print('wrote', OUT, len(js), 'bytes')


if __name__ == '__main__':
    main(sys.argv[1])
