// Car body: "F1 2026 concept (polygon model)" by Qvist_designs, CC BY 4.0,
// https://sketchfab.com/3d-models/f1-2026-concept-polygon-model-ea3bde709b1e4dc9b0ec8557d106ed42
// Decimated and split by tools/carmodel/build_car.py into Resources/carmodel.json: a 200k-triangle near body
// with the halo as its own material group and separate active-aero flaps, 24k and 5k triangle mid and far bodies, and 9k-triangle wheels.
// Model space is x forward, y up, z lateral, metres, front axle at x = 1.55.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightsOut
{
    public static class CarModel
    {
        [Serializable] public class Part { public float[] lo, hi, at; public string p, i; public int[] g; public float r, w; public bool front, i32; }
        [Serializable] public class HiLevel { public Part body, fFlap, rFlap; }
        [Serializable] public class Data { public HiLevel hi; public Part mid, lo; public Part[] wheels; }

        public class Wheel { public Vector3 At; public float Radius, Width; public bool Front; public Mesh Mesh; }

        public static Data Raw { get; private set; }
        public static Mesh Body, BodyMid, BodyFar, FrontFlap, RearFlap;
        public static Vector3 FrontFlapAt, RearFlapAt;
        public static readonly List<Wheel> Wheels = new List<Wheel>();

        public static bool Load()
        {
            if (Body) return true;
            var txt = Resources.Load<TextAsset>("carmodel"); if (txt == null) return false;
            Raw = JsonUtility.FromJson<Data>(txt.text);
            Body = Decode(Raw.hi.body, "CarBody"); BodyMid = Raw.mid != null && !string.IsNullOrEmpty(Raw.mid.p) ? Decode(Raw.mid, "CarBodyMid") : null; BodyFar = Decode(Raw.lo, "CarBodyFar");
            FrontFlap = Decode(Raw.hi.fFlap, "CarFrontFlap"); RearFlap = Decode(Raw.hi.rFlap, "CarRearFlap");
            FrontFlapAt = M(Raw.hi.fFlap.at); RearFlapAt = M(Raw.hi.rFlap.at);
            Wheels.Clear();
            foreach (var w in Raw.wheels) Wheels.Add(new Wheel { At = M(w.at), Radius = w.r, Width = w.w, Front = w.front, Mesh = Decode(w, "CarWheel") });
            return true;
        }

        // model space -> Unity local (same swap as CarView.M)
        public static Vector3 M(float x, float y, float z) { return new Vector3(z, y, x); }
        static Vector3 M(float[] a) { return M(a[0], a[1], a[2]); }

        const float CosCrease = 0.766f;   // cos 40 degrees: sharper edges keep hard normals

        static Mesh Decode(Part d, string name)
        {
            var qb = Convert.FromBase64String(d.p); var ib = Convert.FromBase64String(d.i);
            int isz = d.i32 ? 4 : 2, nv = qb.Length / 6, nc = ib.Length / isz;
            var P = new Vector3[nv];
            for (int v = 0; v < nv; v++)
            {
                var c = new float[3];
                for (int a = 0; a < 3; a++) { short q = BitConverter.ToInt16(qb, (v * 3 + a) * 2); c[a] = d.lo[a] + (q + 32768) / 65535f * (d.hi[a] - d.lo[a]); }
                P[v] = M(c[0], c[1], c[2]);
            }
            // the axis swap mirrors the geometry, so reverse each triangle to keep faces pointing outward
            var idx = new int[nc];
            for (int k = 0; k < nc; k += 3)
            {
                idx[k] = Index(ib, k, d.i32);
                idx[k + 1] = Index(ib, k + 2, d.i32);
                idx[k + 2] = Index(ib, k + 1, d.i32);
            }
            var FN = new Vector3[nc / 3];
            for (int f = 0; f < nc / 3; f++) { Vector3 a = P[idx[f * 3]], b = P[idx[f * 3 + 1]], c = P[idx[f * 3 + 2]]; FN[f] = Vector3.Cross(b - a, c - a).normalized; }
            // split each vertex into smoothing groups by face normal so creases stay sharp
            var corners = new List<int>[nv]; for (int k = 0; k < nc; k++) { var l = corners[idx[k]]; if (l == null) corners[idx[k]] = l = new List<int>(6); l.Add(k); }
            var oP = new List<Vector3>(nv * 2); var oN = new List<Vector3>(nv * 2); var oI = new int[nc];
            var gN = new List<Vector3>(); var gS = new List<Vector3>(); var gId = new List<int>();
            for (int v = 0; v < nv; v++)
            {
                if (corners[v] == null) continue;
                gN.Clear(); gS.Clear(); gId.Clear();
                foreach (int k in corners[v])
                {
                    var n = FN[k / 3]; int g = -1;
                    for (int j = 0; j < gN.Count; j++) if (Vector3.Dot(gN[j], n) > CosCrease) { g = j; break; }
                    if (g < 0) { g = gN.Count; gN.Add(n); gS.Add(Vector3.zero); gId.Add(oP.Count); oP.Add(P[v]); oN.Add(Vector3.zero); }
                    gS[g] += n; oI[k] = gId[g];
                }
                for (int j = 0; j < gN.Count; j++) oN[gId[j]] = gS[j].normalized;
            }
            // box-projected UVs in metres, so tiled detail textures (carbon weave) have a consistent scale
            var oU = new List<Vector2>(oP.Count);
            for (int k = 0; k < oP.Count; k++)
            {
                Vector3 p = oP[k], n = oN[k]; float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                oU.Add(ax >= ay && ax >= az ? new Vector2(p.z, p.y) : ay >= az ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y));
            }
            var mesh = new Mesh { name = name, indexFormat = oP.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            // the first material group is the paint: project it straight from the side instead (u along the car, v up),
            // so livery lines stay straight whatever the bodywork does underneath
            if (d.g != null && d.g.Length > 1) for (int k = 0; k < d.g[0]; k++) { var p = oP[oI[k]]; oU[oI[k]] = new Vector2((p.z + 3f) / LiveryLength, p.y / LiveryHeight); }
            mesh.SetVertices(oP); mesh.SetNormals(oN); mesh.SetUVs(0, oU);
            if (d.g != null && d.g.Length > 0)
            {
                mesh.subMeshCount = d.g.Length; int o = 0;
                for (int s = 0; s < d.g.Length; s++) { var sub = new int[d.g[s]]; Array.Copy(oI, o, sub, 0, d.g[s]); mesh.SetTriangles(sub, s); o += d.g[s]; }
            }
            else mesh.SetTriangles(oI, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Index(byte[] b, int k, bool wide) { return wide ? (int)BitConverter.ToUInt32(b, k * 4) : BitConverter.ToUInt16(b, k * 2); }

        public const float LiveryLength = 6.4f, LiveryHeight = 1.2f;

        /// Short blocks around part of a ring, in the wheel plane and facing both ways: generic sidewall lettering.
        public static Mesh Marks(float r0, float r1, float from, float to, int count)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k < count; k++)
            {
                float a0 = Mathf.Lerp(from, to, k / (float)count), a1 = a0 + (to - from) / count * (k % 3 == 2 ? .35f : .7f); int b = v.Count;
                foreach (float a in new[] { a0, a1 }) { v.Add(new Vector3(0, Mathf.Cos(a) * r0, Mathf.Sin(a) * r0)); v.Add(new Vector3(0, Mathf.Cos(a) * r1, Mathf.Sin(a) * r1)); }
                t.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3, b, b + 2, b + 1, b + 2, b + 3, b + 1 });
            }
            var m = new Mesh { name = "Marks" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// A flat ring in the wheel plane (Unity local Y-Z), facing both ways: the compound-coloured sidewall band.
        public static Mesh Ring(float r0, float r1, int seg = 40)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k < seg; k++) { float a = k * Mathf.PI * 2 / seg; v.Add(new Vector3(0, Mathf.Cos(a) * r0, Mathf.Sin(a) * r0)); v.Add(new Vector3(0, Mathf.Cos(a) * r1, Mathf.Sin(a) * r1)); }
            for (int k = 0; k < seg; k++)
            {
                int a = k * 2, b = a + 1, c = (k + 1) % seg * 2, d = c + 1;
                t.Add(a); t.Add(b); t.Add(c); t.Add(c); t.Add(b); t.Add(d);
                t.Add(a); t.Add(c); t.Add(b); t.Add(c); t.Add(d); t.Add(b);
            }
            var m = new Mesh { name = "Band" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
