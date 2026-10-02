// 2026-style single-seater: the CarModel mesh when Resources/carmodel.json is present, otherwise a procedural fallback
// Procedural 2026-style single-seater (lofted monocoque, sidepods, halo, suspension, wings with
// active-aero flaps, compound-coloured tyre sidewalls) and its per-frame sync from the sim.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightsOut
{
    public class CarView : MonoBehaviour
    {
        public Transform[] FrontPivots = new Transform[2];
        public Transform[] Spins = new Transform[4];
        public Transform FrontFlap, RearFlap, Helmet, SteeringWheel;
        public Material Band, RainLight;
        Compound shownTyre = (Compound)(-1);

        // ---------- construction ----------
        public static CarView Create(string name, Color paint, Color accent, bool isPlayer)
        {
            var root = new GameObject(name);
            var v = root.AddComponent<CarView>();
            var mPaint = new Material(Visuals.Mat("paint", Color.white, null, .8f, .4f)); Visuals.SetColor(mPaint, paint);
            var mAcc = new Material(Visuals.Mat("paint", Color.white, null, .8f, .4f)); Visuals.SetColor(mAcc, accent);
            var mCarbon = Visuals.Mat("carbon", new Color(.09f, .1f, .11f), null, .45f, .2f);
            var mTyre = Visuals.Mat("tyre", new Color(.07f, .07f, .07f), null, .1f);
            var mRim = Visuals.Mat("rim", new Color(.35f, .37f, .4f), null, .6f, .8f);
            bool model = CarModel.Load();
            v.Band = new Material(Visuals.Unlit("band", Color.yellow));
            if (model) BuildModel(root.transform, v, mPaint, mAcc, mCarbon, mTyre, mRim);
            else
            {
                var P = new List<CombineInstance>(); var A = new List<CombineInstance>(); var C = new List<CombineInstance>();
                // monocoque / nose / engine cover
                P.Add(CI(Loft(new[] { S(3.02f, .09f, .07f, .26f), S(2.78f, .2f, .13f, .28f), S(2.2f, .3f, .21f, .32f), S(1.5f, .42f, .3f, .39f), S(.95f, .64f, .42f, .45f), S(.4f, .8f, .46f, .48f), S(-.35f, .84f, .48f, .48f), S(-.85f, .8f, .66f, .56f), S(-1.5f, .62f, .56f, .55f), S(-2.1f, .4f, .4f, .48f), S(-2.48f, .26f, .28f, .44f) }, 20, 3), Matrix4x4.identity));
                foreach (int s in new[] { -1, 1 }) P.Add(CI(Loft(new[] { S(.42f, .08f, .28f, .36f, .62f * s), S(.22f, .42f, .44f, .38f, .6f * s), S(-.5f, .46f, .46f, .38f, .58f * s), S(-1.2f, .34f, .38f, .35f, .5f * s), S(-1.9f, .14f, .24f, .32f, .36f * s) }, 16, 3.4f), Matrix4x4.identity));
                C.Add(CI(Loft(new[] { S(-.22f, .18f, .12f, .88f), S(-.42f, .34f, .3f, .9f), S(-.9f, .3f, .32f, .84f), S(-1.6f, .1f, .22f, .7f), S(-2.25f, .04f, .14f, .6f) }, 14, 2.6f), Matrix4x4.identity));
                C.Add(BoxCI(-.45f, .1f, 0, 3.9f, .045f, 1.5f));                     // floor
                C.Add(BoxCI(2.88f, .11f, 0, .34f, .035f, 1.98f));                  // front wing main plane
                foreach (int s in new[] { -1, 1 }) { A.Add(BoxCI(2.78f, .2f, .99f * s, .6f, .27f, .03f)); A.Add(BoxCI(-2.5f, .82f, .52f * s, .72f, .6f, .03f)); }
                C.Add(BoxCI(-2.56f, .88f, 0, .28f, .04f, 1.02f));                  // rear wing main plane
                C.Add(BoxCI(-2.42f, .44f, 0, .2f, .03f, .82f));                    // beam wing
                A.Add(BoxCI(-.45f, 1.07f, 0, .18f, .06f, .07f));                   // T-cam
                // halo: centre pillar plus a hoop of short segments
                C.Add(BoxCI(.6f, .92f, 0, .03f, .52f, .045f, 0, 0, 16));
                for (int k = 0; k <= 10; k++) { float a = Mathf.PI * (k / 10f - .5f); C.Add(BoxCI(.05f + Mathf.Cos(a) * .5f, 1.17f, Mathf.Sin(a) * .5f, .05f, .04f, .17f, 0, a * Mathf.Rad2Deg, 0)); }
                // suspension arms
                foreach (int s in new[] { -1, 1 })
                {
                    C.Add(Rod(new Vector3(1.25f, .47f, .18f * s), new Vector3(1.55f, .46f, .7f * s))); C.Add(Rod(new Vector3(1.85f, .45f, .18f * s), new Vector3(1.55f, .46f, .7f * s)));
                    C.Add(Rod(new Vector3(1.3f, .3f, .2f * s), new Vector3(1.55f, .28f, .72f * s))); C.Add(Rod(new Vector3(-1.45f, .5f, .3f * s), new Vector3(-1.8f, .45f, .66f * s)));
                    C.Add(Rod(new Vector3(-1.5f, .3f, .3f * s), new Vector3(-1.8f, .3f, .66f * s)));
                    A.Add(BoxCI(.52f, .71f, .58f * s, .05f, .055f, .13f));      // mirrors
                }
                AddMesh(root.transform, "Paint", P, mPaint, true); AddMesh(root.transform, "Accent", A, mAcc, true); AddMesh(root.transform, "Carbon", C, mCarbon, true);
                // active-aero flaps
                v.FrontFlap = Pivot(root.transform, "FrontFlap", new Vector3(2.72f, .19f, 0)); Part(v.FrontFlap, new Vector3(-.1f, 0, 0), new Vector3(.22f, .03f, 1.78f), mPaint);
                v.RearFlap = Pivot(root.transform, "RearFlap", new Vector3(-2.42f, 1f, 0)); Part(v.RearFlap, new Vector3(-.12f, 0, 0), new Vector3(.24f, .04f, 1.02f), mAcc);
                int w = 0;
                foreach (int s in new[] { -1, 1 })
                {
                    v.FrontPivots[w] = Pivot(root.transform, "FrontWheel", new Vector3(1.55f, .36f, .86f * s));
                    v.Spins[w] = Wheel(v.FrontPivots[w], .36f, .37f, mTyre, mRim, v.Band);
                    var rear = Pivot(root.transform, "RearWheel", new Vector3(-1.8f, .38f, .84f * s));
                    v.Spins[2 + w] = Wheel(rear, .38f, .45f, mTyre, mRim, v.Band); w++;
                }
            }
            // driver
            v.Helmet = Pivot(root.transform, "Helmet", model ? new Vector3(-.1f, .82f, 0) : new Vector3(-.05f, .8f, 0));
            var hm = new GameObject("Shell"); hm.transform.SetParent(v.Helmet, false); hm.transform.localScale = Vector3.one * (model ? .3f : .33f);
            hm.AddComponent<MeshFilter>().sharedMesh = Visuals.Sphere; hm.AddComponent<MeshRenderer>().sharedMaterial = isPlayer ? Visuals.Mat("helmetW", Color.white, null, .8f) : mAcc;
            Part(v.Helmet, new Vector3(.12f, .02f, 0), new Vector3(.08f, .07f, .24f), Visuals.Mat("visor", new Color(.03f, .04f, .05f), null, .95f, .6f));
            v.RainLight = new Material(Visuals.Unlit("rainlight", new Color(.2f, 0, 0)));
            Part(root.transform, model ? new Vector3(-2.6f, .39f, 0) : new Vector3(-2.63f, .5f, 0), new Vector3(.04f, .08f, .14f), v.RainLight);
            // emissive-only lighting: headlight bars, tail light strips and a neon line along each floor edge
            var mHead = Visuals.Neon("headlight", new Color(.8f, .95f, 1f), 3.5f); var mTail = Visuals.Neon("taillight", new Color(1f, .06f, .12f), 3.5f);
            var mLine = Visuals.Neon(isPlayer ? "carlineP" : "carline", isPlayer ? Look.Magenta : Look.Cyan, 2.6f);
            foreach (int s in new[] { -1, 1 })
            {
                if (model)
                {
                    Part(root.transform, new Vector3(2.62f, .12f, .62f * s), new Vector3(.02f, .025f, .3f), mHead);
                    Part(root.transform, new Vector3(-2.56f, .62f, .47f * s), new Vector3(.02f, .4f, .03f), mTail);
                    Part(root.transform, new Vector3(-.3f, .085f, .77f * s), new Vector3(3.4f, .02f, .02f), mLine);
                }
                else
                {
                    Part(root.transform, new Vector3(3.06f, .12f, .6f * s), new Vector3(.02f, .03f, .5f), mHead);
                    Part(root.transform, new Vector3(-2.87f, .6f, .52f * s), new Vector3(.02f, .46f, .035f), mTail);
                    Part(root.transform, new Vector3(-.45f, .125f, .76f * s), new Vector3(3.5f, .02f, .02f), mLine);
                }
            }
            if (isPlayer)
            {
                v.SteeringWheel = Pivot(root.transform, "SteeringWheel", model ? new Vector3(.28f, .72f, 0) : new Vector3(.4f, .72f, 0));
                Part(v.SteeringWheel, Vector3.zero, new Vector3(.04f, .13f, .27f), mCarbon);
                Part(v.SteeringWheel, new Vector3(-.022f, .005f, 0), new Vector3(.01f, .055f, .11f), Visuals.Unlit("screen", new Color(.1f, .45f, .25f)));
            }
            return v;
        }

        // Body from CarModel (near and far detail levels), active-aero flaps on their leading-edge pivots,
        // and the model's right-hand wheels, mirrored for the left side.
        static void BuildModel(Transform root, CarView v, Material paint, Material acc, Material carbon, Material tyre, Material rim)
        {
            var near = MeshPart(root, "Body", CarModel.Body, new[] { paint, acc, carbon }, true);
            var far = MeshPart(root, "BodyFar", CarModel.BodyFar, new[] { paint, acc, carbon }, true);
            v.FrontFlap = new GameObject("FrontFlap").transform; v.FrontFlap.SetParent(root, false); v.FrontFlap.localPosition = CarModel.FrontFlapAt;
            var ff = MeshPart(v.FrontFlap, "Flap", CarModel.FrontFlap, new[] { carbon }, true);
            v.RearFlap = new GameObject("RearFlap").transform; v.RearFlap.SetParent(root, false); v.RearFlap.localPosition = CarModel.RearFlapAt;
            var rf = MeshPart(v.RearFlap, "Flap", CarModel.RearFlap, new[] { acc }, true);
            var lod = root.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.12f, new Renderer[] { near, ff, rf }), new LOD(.004f, new Renderer[] { far }) });
            if (bandMesh == null) bandMesh = CarModel.Ring(.27f, .305f);
            int fi = 0, ri = 2;
            foreach (var W in CarModel.Wheels)
                foreach (int s in new[] { -1, 1 })
                {
                    var piv = new GameObject(W.Front ? "FrontWheel" : "RearWheel").transform; piv.SetParent(root, false);
                    piv.localPosition = new Vector3(W.At.x * s, W.At.y, W.At.z);
                    var spin = new GameObject("Spin").transform; spin.SetParent(piv, false);
                    var wm = MeshPart(spin, "Wheel", W.Mesh, new[] { tyre, rim }, true);
                    if (s < 0) wm.transform.localScale = new Vector3(-1, 1, 1);
                    foreach (int k in new[] { -1, 1 })
                    {
                        var band = new GameObject("Band"); band.transform.SetParent(piv, false); band.transform.localPosition = new Vector3(k * (W.Width / 2 + .003f), 0, 0);
                        band.AddComponent<MeshFilter>().sharedMesh = bandMesh; var br = band.AddComponent<MeshRenderer>(); br.sharedMaterial = v.Band; br.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    if (W.Front) { if (fi < 2) { v.FrontPivots[fi] = piv; v.Spins[fi] = spin; fi++; } }
                    else if (ri < 4) v.Spins[ri++] = spin;
                }
        }
        static Mesh bandMesh;
        static MeshRenderer MeshPart(Transform parent, string name, Mesh mesh, Material[] mats, bool shadows)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return mr;
        }

        struct Sec { public float X, W, H, Yc, Zc; }
        static Sec S(float x, float w, float h, float yc, float zc = 0) { return new Sec { X = x, W = w, H = h, Yc = yc, Zc = zc }; }
        // model space: x forward, y up, z lateral  ->  Unity local: (z, y, x)
        static Vector3 M(float x, float y, float z) { return new Vector3(z, y, x); }

        static Mesh Loft(Sec[] secs, int seg, float pw)
        {
            var ring = new Vector2[seg];
            for (int k = 0; k < seg; k++) { float a = k / (float)seg * Mathf.PI * 2, c = Mathf.Cos(a), s = Mathf.Sin(a); ring[k] = new Vector2(Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2 / pw), Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2 / pw)); }
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var sc in secs) foreach (var r in ring) v.Add(M(sc.X, sc.Yc + r.y * sc.H / 2, sc.Zc + r.x * sc.W / 2));
            for (int i = 0; i < secs.Length - 1; i++) for (int k = 0; k < seg; k++)
                {
                    int a = i * seg + k, b = i * seg + (k + 1) % seg, c = (i + 1) * seg + k, d = (i + 1) * seg + (k + 1) % seg;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
                }
            int L = secs.Length - 1, cs = v.Count;
            v.Add(M(secs[0].X, secs[0].Yc, secs[0].Zc)); v.Add(M(secs[L].X, secs[L].Yc, secs[L].Zc));
            for (int k = 0; k < seg; k++) { t.Add(cs); t.Add((k + 1) % seg); t.Add(k); t.Add(cs + 1); t.Add(L * seg + k); t.Add(L * seg + (k + 1) % seg); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals();
            // the axis swap mirrors the winding; make faces point outward
            if (Vector3.Dot(m.normals[0], v[0] - M(secs[0].X, secs[0].Yc, secs[0].Zc)) < 0) { var tr = m.triangles; for (int i = 0; i < tr.Length; i += 3) { int q = tr[i + 1]; tr[i + 1] = tr[i + 2]; tr[i + 2] = q; } m.triangles = tr; m.RecalculateNormals(); }
            return m;
        }
        static CombineInstance CI(Mesh m, Matrix4x4 tr) { return new CombineInstance { mesh = m, transform = tr }; }
        static CombineInstance BoxCI(float x, float y, float z, float lx, float ly, float lz, float rx = 0, float ry = 0, float rzDeg = 0)
        {
            // rotation given in model space: rzDeg pitches about the lateral axis, ry yaws
            var rot = Quaternion.Euler(-rzDeg, ry, rx);
            return CI(Visuals.Cube, Matrix4x4.TRS(M(x, y, z), rot, new Vector3(lz, ly, lx)));
        }
        static CombineInstance Rod(Vector3 a, Vector3 b)
        {
            Vector3 A = M(a.x, a.y, a.z), B = M(b.x, b.y, b.z), mid = (A + B) / 2; float len = Vector3.Distance(A, B);
            return CI(Visuals.Cylinder, Matrix4x4.TRS(mid, Quaternion.FromToRotation(Vector3.up, (B - A).normalized), new Vector3(.03f, len / 2, .03f)));
        }
        static void AddMesh(Transform parent, string name, List<CombineInstance> parts, Material m, bool shadows)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(parts.ToArray(), true, true); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        static Transform Pivot(Transform parent, string name, Vector3 modelPos) { var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = M(modelPos.x, modelPos.y, modelPos.z); return g.transform; }
        static GameObject Part(Transform parent, Vector3 modelPos, Vector3 modelSize, Material m)
        {
            var g = new GameObject("Part"); g.transform.SetParent(parent, false); g.transform.localPosition = M(modelPos.x, modelPos.y, modelPos.z); g.transform.localScale = new Vector3(modelSize.z, modelSize.y, modelSize.x);
            g.AddComponent<MeshFilter>().sharedMesh = Visuals.Cube; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; return g;
        }
        static Transform Wheel(Transform pivot, float r, float w, Material tyre, Material rim, Material band)
        {
            var spin = new GameObject("Spin").transform; spin.SetParent(pivot, false);
            Disc(spin, r, w, tyre, 0, true);
            Disc(spin, .305f, w + .006f, band, 0, false);
            Disc(spin, .24f, w + .012f, rim, 0, false);
            // spokes so rotation is visible
            for (int k = 0; k < 5; k++) { var s = Part(spin, Vector3.zero, new Vector3(.4f, .05f, w + .016f), rim); s.transform.localRotation = Quaternion.Euler(k * 36f, 0, 0); }
            return spin;
        }
        static void Disc(Transform parent, float r, float w, Material m, float x, bool shadows)
        {
            var g = new GameObject("Disc"); g.transform.SetParent(parent, false);
            g.transform.localRotation = Quaternion.Euler(0, 0, 90); g.transform.localScale = new Vector3(r * 2, w / 2, r * 2);
            g.AddComponent<MeshFilter>().sharedMesh = Visuals.Cylinder; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        // ---------- per-frame sync ----------
        public void Sync(Car c, Track t, float dt, bool cockpitOfThisCar, float time)
        {
            gameObject.SetActive(!c.Hidden);
            if (c.Hidden) return;
            float y = t.ElevAt(c.PF);
            float pitch = Mathf.Atan(t.GR[c.Idx] * Mathf.Cos(c.H - t.Heading(c.Idx))) * Mathf.Rad2Deg;
            transform.position = Visuals.World(c.X, c.Y, y);
            transform.rotation = Visuals.Yaw(c.H) * Quaternion.Euler(-pitch + c.Brk * .35f, 0, 0);
            foreach (var p in FrontPivots) if (p) p.localRotation = Quaternion.Euler(0, c.Steer * 20f, 0);
            float spin = c.VF * dt / .37f * Mathf.Rad2Deg; foreach (var s in Spins) if (s) s.Rotate(spin, 0, 0, Space.Self);
            if (shownTyre != c.Tyre) { shownTyre = c.Tyre; var ts = Config.Tyres[(int)c.Tyre]; Visuals.SetColor(Band, new Color(ts.R, ts.G, ts.B)); }
            float open = c.StraightMode ? 1 : 0;
            FrontFlap.localRotation = Quaternion.Slerp(FrontFlap.localRotation, Quaternion.Euler((CarModel.Body ? -8f : 16f) * open, 0, 0), dt * 10);
            RearFlap.localRotation = Quaternion.Slerp(RearFlap.localRotation, Quaternion.Euler((CarModel.Body ? -26f : -32f) * open, 0, 0), dt * 10);
            bool flash = (c.Harv > 0 && c.Thr > .9f) || c.Pit != null;
            Visuals.SetColor(RainLight, flash && ((int)(time * 6)) % 2 == 0 ? new Color(1, .15f, .1f) : new Color(.2f, 0, 0));
            if (Helmet) Helmet.gameObject.SetActive(!cockpitOfThisCar);
            if (SteeringWheel) SteeringWheel.localRotation = Quaternion.Euler(0, 0, -c.Steer * 80f);
        }
    }
}
