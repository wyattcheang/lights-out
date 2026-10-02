// Builds the circuit scene from a Track: terrain following the real elevation, asphalt, kerbs,
// gravel traps, run-off, barriers with advertising, catch fences, pit lane and garages,
// grid, start gantry, braking boards, marshal posts, grandstands and scenery.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightsOut
{
    public class TrackBuilder
    {
        public GameObject Root;
        public Material[] GantryLamps = new Material[5];
        public Material[] PostPanels;
        public Texture2D MapTexture;
        Track T;
        float[] terrainH; int terrainN; float tx0, tz0, tCell;

        public void Build(Track t)
        {
            if (Root) UnityEngine.Object.Destroy(Root);
            T = t; Root = new GameObject("Circuit " + t.Src.city);
            var r = Root.transform; float W = t.W, wd = t.WallD;
            BuildTerrain(r);
            if (t.StreetCircuit) { Strip(r, "Verge+", W / 2 - .1f, .03f, wd + .2f, .03f, null, Asphalt(), 8, 1); Strip(r, "Verge-", -W / 2 + .1f, .03f, -wd - .2f, .03f, null, Asphalt(), 8, 1); }
            else
            {
                float rc = Look.Pick(1.25f, .3f); var run = Visuals.Mat("runoff", new Color(rc, rc, rc * 1.03f), Visuals.Asphalt, Look.Pick(.3f, .65f));
                Strip(r, "Runoff+", W / 2 - .1f, .02f, W / 2 + 5, .02f, null, run, 8, 1); Strip(r, "Runoff-", -W / 2 + .1f, .02f, -W / 2 - 5, .02f, null, run, 8, 1);
                float gc = Look.Pick(.85f, .26f); var gv = Visuals.Mat("gravel", new Color(gc, gc, gc * 1.1f), Visuals.Gravel);
                Strip(r, "Gravel+", W / 2 + 1.1f, .035f, W / 2 + 9, .035f, t.GravP, gv, 6, 2); Strip(r, "Gravel-", -W / 2 - 1.1f, .035f, -W / 2 - 9, .035f, t.GravN, gv, 6, 2);
            }
            Strip(r, "Asphalt", -W / 2, .05f, W / 2, .05f, null, Asphalt(), 8, W / 8);
            var kerbM = Visuals.Mat("kerb", Color.white, Visuals.Kerb, .5f, 0, false, null, Look.Hdr(Color.white, 1.7f));
            Strip(r, "Kerb+", W / 2 - .2f, .08f, W / 2 + 1.1f, .06f, t.Kerb, kerbM, 2, 1); Strip(r, "Kerb-", -W / 2 + .2f, .08f, -W / 2 - 1.1f, .06f, t.Kerb, kerbM, 2, 1);
            var line = LineM();
            Strip(r, "Line+", W / 2 - .55f, .07f, W / 2 - .25f, .07f, null, line, 8, 1); Strip(r, "Line-", -W / 2 + .55f, .07f, -W / 2 + .25f, .07f, null, line, 8, 1);
            var bm = Visuals.Mat("barrier", Color.white, Visuals.Barrier, .55f, .2f, false, null, Look.Hdr(Color.white, 2f)); float bh = t.StreetCircuit ? 1.15f : 1.05f;
            Strip(r, "Barrier+", wd, 0, wd, bh, t.WallP, bm, 30, 1, true); Strip(r, "Barrier-", -wd, 0, -wd, bh, t.WallN, bm, 30, 1, true);
            var fm = Visuals.Mat("fence", Look.Pick(new Color(.8f, .82f, .86f), new Color(.7f, .8f, .9f)), Visuals.Fence, .3f, 0, true, null, Look.Hdr(new Color(.25f, .75f, .9f), .3f));
            Strip(r, "Fence+", wd + .05f, bh, wd + .05f, bh + 3.6f, t.WallP, fm, 4, 1, true); Strip(r, "Fence-", -wd - .05f, bh, -wd - .05f, bh + 3.6f, t.WallN, fm, 4, 1, true);
            BuildPitLane(r);
            BuildStart(r);
            BuildBoardsAndPosts(r);
            BuildStands(r);
            BuildScenery(r);
            BuildMap();
        }

        // shared look-dependent materials
        static Material Asphalt() { float c = Look.Pick(.78f, .55f); return Visuals.Mat("asphalt", new Color(c, c, c * 1.08f), Visuals.Asphalt, Look.Pick(.6f, .8f)); }
        static Material LineM() { return Visuals.Mat("line", Look.Pick(new Color(.95f, .94f, .91f), new Color(.55f, .85f, .9f)), null, .3f, 0, false, null, Look.Hdr(Look.Cyan, .9f)); }
        static Material RoofM() { return Visuals.Mat("roof", Look.Pick(new Color(.5f, .52f, .56f), new Color(.13f, .14f, .19f)), null, .6f, .3f); }

        // ---- ribbons along the track (lateral offsets o1/o2, heights y1/y2 above the centreline elevation) ----
        void Strip(Transform parent, string name, float o1, float y1, float o2, float y2, byte[] mask, Material m, float vLen, float uMax, bool vertical = false)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); int n = T.N;
            for (int i = 0; i < n; i++)
            {
                if (mask != null && mask[i] == 0) continue;
                int j = (i + 1) % n; float ei = T.E[i], ej = T.E[j];
                Vector3 a1 = Visuals.World(T.X[i] + T.NX[i] * o1, T.Y[i] + T.NY[i] * o1, ei + y1), a2 = Visuals.World(T.X[i] + T.NX[i] * o2, T.Y[i] + T.NY[i] * o2, ei + y2);
                Vector3 b2 = Visuals.World(T.X[j] + T.NX[j] * o2, T.Y[j] + T.NY[j] * o2, ej + y2), b1 = Visuals.World(T.X[j] + T.NX[j] * o1, T.Y[j] + T.NY[j] * o1, ej + y1);
                float v0 = i * Config.Step / vLen, v1 = (i + 1) * Config.Step / vLen;
                Visuals.Quad(v, uv, tri, a1, a2, b2, b1, new Vector2(0, v0), new Vector2(uMax, v0), new Vector2(uMax, v1), new Vector2(0, v1), !vertical, vertical);
                if (v.Count > 60000) { Visuals.MeshObject(name, parent, v, uv, tri, m); v = new List<Vector3>(); uv = new List<Vector2>(); tri = new List<int>(); }
            }
            if (v.Count > 0) Visuals.MeshObject(name, parent, v, uv, tri, m, vertical);
        }
        void RibbonP(Transform parent, string name, float p0, float p1, Func<float, float> lat, float w1, float w2, float y1, float y2, Material m, float vLen, bool vertical = false)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (float p = p0; p < p1; p += .5f)
            {
                float q = Math.Min(p1, p + .5f), la = lat(p), lb = lat(q), ea = T.ElevAt(p), eb = T.ElevAt(q), x, y;
                T.PosF(p, la + w1, out x, out y); var a1 = Visuals.World(x, y, ea + y1);
                T.PosF(p, la + w2, out x, out y); var a2 = Visuals.World(x, y, ea + y2);
                T.PosF(q, lb + w2, out x, out y); var b2 = Visuals.World(x, y, eb + y2);
                T.PosF(q, lb + w1, out x, out y); var b1 = Visuals.World(x, y, eb + y1);
                Visuals.Quad(v, uv, tri, a1, a2, b2, b1, new Vector2(0, p * Config.Step / vLen), new Vector2(1, p * Config.Step / vLen), new Vector2(1, q * Config.Step / vLen), new Vector2(0, q * Config.Step / vLen), !vertical, vertical);
            }
            Visuals.MeshObject(name, parent, v, uv, tri, m, vertical);
        }

        // ---- terrain: hugs the track's real elevation and rolls away into the landscape ----
        void BuildTerrain(Transform parent)
        {
            float size = Math.Max(T.MaxX - T.MinX, T.MaxY - T.MinY) + 3000, cx = (T.MinX + T.MaxX) / 2, cy = (T.MinY + T.MaxY) / 2;
            int N = T.StreetCircuit ? 140 : 180; terrainN = N; tCell = size / (N - 1); tx0 = cx - size / 2; tz0 = cy - size / 2;
            terrainH = new float[N * N];
            var samp = new List<int>(); for (int i = 0; i < T.N; i += 10) samp.Add(i);
            float amp = T.StreetCircuit ? 3 : (T.HasElevation ? 20 : 9), near = T.W / 2 + 18;
            for (int iz = 0; iz < N; iz++) for (int ix = 0; ix < N; ix++)
                {
                    float x = tx0 + ix * tCell, y = tz0 + iz * tCell; int bi; float d = T.Nearest(x, y, out bi);
                    float sw = 0, sh = 0; foreach (int i in samp) { float dx = T.X[i] - x, dy = T.Y[i] - y, w = 1f / (dx * dx + dy * dy + 260 * 260); sw += w; sh += w * T.E[i]; }
                    float far = sh / sw, nx = x * .0021f, nz = y * .0021f;
                    float noise = (float)(Math.Sin(nx * 1.7 + Math.Sin(nz * 1.3)) * .6 + Math.Sin(nz * 2.3 + nx * .7) * .4 + Math.Sin((nx + nz) * 4.1) * .15);
                    float hn = (d < near ? MinNear(x, y, near, T.E[bi]) : T.E[bi]) - .3f;
                    terrainH[iz * N + ix] = MathX.Lerp(hn, far + noise * amp * MathX.Smooth((d - 120) / 500) - .3f, MathX.Smooth((d - (T.WallD + 6)) / 150));
                }
            var v = new List<Vector3>(N * N); var uv = new List<Vector2>(N * N); var tri = new List<int>((N - 1) * (N - 1) * 6);
            float rep = T.StreetCircuit ? 20 : 26;
            for (int iz = 0; iz < N; iz++) for (int ix = 0; ix < N; ix++) { float x = tx0 + ix * tCell, y = tz0 + iz * tCell; v.Add(Visuals.World(x, y, terrainH[iz * N + ix])); uv.Add(new Vector2(x / rep, y / rep)); }
            for (int iz = 0; iz < N - 1; iz++) for (int ix = 0; ix < N - 1; ix++)
                {
                    int a = iz * N + ix, b = a + 1, c = a + N, d = c + 1;
                    tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d);
                }
            // y is flipped in World(), so fix the winding to face up
            var go = Visuals.MeshObject("Terrain", parent, v, uv, tri, Visuals.Mat(T.StreetCircuit ? "city" : "grass", T.StreetCircuit ? Look.Pick(new Color(.8f, .82f, .86f), new Color(.2f, .2f, .26f)) : Look.Pick(new Color(.6f, .7f, .68f), new Color(.1f, .15f, .17f)), T.StreetCircuit ? Visuals.City : Visuals.Grass, T.StreetCircuit ? Look.Pick(.3f, .6f) : .05f));
            var mesh = go.GetComponent<MeshFilter>().sharedMesh; if (mesh.normals.Length > 0 && mesh.normals[0].y < 0) { var tr = mesh.triangles; for (int i = 0; i < tr.Length; i += 3) { int k = tr[i + 1]; tr[i + 1] = tr[i + 2]; tr[i + 2] = k; } mesh.triangles = tr; mesh.RecalculateNormals(); }
        }
        float MinNear(float x, float y, float r, float fallback)
        {
            float best = fallback; for (int i = 0; i < T.N; i += 2) { float dx = T.X[i] - x, dy = T.Y[i] - y; if (dx * dx + dy * dy < r * r && T.E[i] < best) best = T.E[i]; }
            return best;
        }
        public float TerrainAt(float x, float y)
        {
            float fx = MathX.Clamp((x - tx0) / tCell, 0, terrainN - 1.001f), fz = MathX.Clamp((y - tz0) / tCell, 0, terrainN - 1.001f);
            int ix = (int)fx, iz = (int)fz; float u = fx - ix, w = fz - iz; int N = terrainN;
            return MathX.Lerp(MathX.Lerp(terrainH[iz * N + ix], terrainH[iz * N + ix + 1], u), MathX.Lerp(terrainH[(iz + 1) * N + ix], terrainH[(iz + 1) * N + ix + 1], u), w);
        }

        float PitLat(float p, float lat0)
        {
            if (p < T.PE1) return MathX.Lerp(lat0, T.PitLat, MathX.Smooth((p - T.PE0) / (float)(T.PE1 - T.PE0)));
            if (p < T.PX0) return T.PitLat;
            return MathX.Lerp(T.PitLat, T.RL[T.Wrap(T.PX1)], MathX.Smooth((p - T.PX0) / (float)(T.PX1 - T.PX0)));
        }
        void BuildPitLane(Transform r)
        {
            float lat0 = T.RL[T.Wrap(T.PE0)]; Func<float, float> pl = p => PitLat(p, lat0);
            RibbonP(r, "PitLane", T.PE0, T.PX1, pl, -3.6f, 3.6f, .04f, .04f, Asphalt(), 8);
            var line = LineM();
            RibbonP(r, "PitLine", T.PE0 + 4, T.PX1 - 4, pl, T.PS * 3.4f, T.PS * 3.6f, .06f, .06f, line, 8);
            var box = Visuals.Mat("pitbox", Look.Amber, null, .1f, 0, false, null, Look.Hdr(Look.Amber, 1.4f));
            for (int g = 0; g < 10; g++) { float bi = -44 + g * 6; RibbonP(r, "Box" + g, bi - 1, bi + 1, p => T.PitLat + T.PS * 1.6f, -.12f, .12f, .07f, .07f, box, 8); }
            RibbonP(r, "Garages", T.PE1 + 2, T.PX0 - 2, p => T.PitLat + T.PS * 7.5f, 0, 0, 0, 7.5f, Visuals.Mat("garage", Color.white, Visuals.Garage, .5f, .2f, false, null, Look.Hdr(Color.white, 1.8f)), 48, true);
            RibbonP(r, "PitRoof", T.PE1 + 2, T.PX0 - 2, p => T.PitLat, T.PS * 7.5f, T.PS * 24, 7.5f, 7.5f, RoofM(), 10, true);
        }

        void BuildStart(Transform r)
        {
            float th0 = T.Heading(0);
            var sl = GameObject.CreatePrimitive(PrimitiveType.Quad); UnityEngine.Object.Destroy(sl.GetComponent<Collider>());
            sl.transform.SetParent(r, false); sl.transform.position = Visuals.World(T.X[0], T.Y[0], T.E[0] + .09f);
            sl.transform.rotation = Visuals.Yaw(th0) * Quaternion.Euler(90, 0, 0); sl.transform.localScale = new Vector3(T.W, 1.8f, 1);
            sl.GetComponent<MeshRenderer>().sharedMaterial = Visuals.Mat("check", Color.white, Visuals.Check);
            var gm = LineM();
            var tmp = new RaceSession { T = T };
            for (int s = 0; s < 10; s++)
            {
                var p = tmp.GridPose(s); var grp = new GameObject("Grid" + s); grp.transform.SetParent(r, false);
                grp.transform.position = Visuals.World(p.X, p.Y, T.E[p.I] + .08f); grp.transform.rotation = Visuals.Yaw(p.H);
                Box(grp.transform, new Vector3(0, 0, 3.2f), new Vector3(2.4f, .02f, .25f), gm);
                Box(grp.transform, new Vector3(-1.1f, 0, 2.3f), new Vector3(.2f, .02f, 1.8f), gm); Box(grp.transform, new Vector3(1.1f, 0, 2.3f), new Vector3(.2f, .02f, 1.8f), gm);
            }
            var gan = new GameObject("StartGantry"); gan.transform.SetParent(r, false);
            gan.transform.position = Visuals.World(T.X[2], T.Y[2], T.E[2]); gan.transform.rotation = Visuals.Yaw(th0);
            var steel = Visuals.Mat("steel", Visuals.Hex("#2b2f36"), null, .4f, .6f); float span = T.W + 5;
            Box(gan.transform, new Vector3(-span / 2, 3.8f, 0), new Vector3(.5f, 7.6f, .5f), steel, true); Box(gan.transform, new Vector3(span / 2, 3.8f, 0), new Vector3(.5f, 7.6f, .5f), steel, true);
            Box(gan.transform, new Vector3(0, 7.3f, 0), new Vector3(span + .6f, .9f, .7f), steel, true);
            var neon = Visuals.Neon("neonCyan", Look.Cyan, 3f);
            Box(gan.transform, new Vector3(0, 6.82f, -.36f), new Vector3(span + .6f, .06f, .03f), neon); Box(gan.transform, new Vector3(0, 7.78f, -.36f), new Vector3(span + .6f, .06f, .03f), neon);
            foreach (float gx in new[] { -span / 2, span / 2 }) Box(gan.transform, new Vector3(gx, 3.8f, -.26f), new Vector3(.06f, 7.2f, .03f), Visuals.Neon("neonMagenta", Look.Magenta, 3f));
            for (int k = 0; k < 5; k++)
            {
                Box(gan.transform, new Vector3((k - 2) * .9f, 6.2f, -.1f), new Vector3(.6f, 1.5f, .45f), Visuals.Mat("pod", Visuals.Hex("#111317")));
                GantryLamps[k] = new Material(Visuals.Unlit("lampoff", new Color(.16f, .04f, .04f)));
                foreach (float y in new[] { 6.6f, 6.05f }) { var l = Box(gan.transform, new Vector3((k - 2) * .9f, y, -.34f), new Vector3(.36f, .36f, .02f), GantryLamps[k]); }
            }
        }

        GameObject Box(Transform parent, Vector3 local, Vector3 scale, Material m, bool shadows = false)
        {
            var go = new GameObject("Box"); go.transform.SetParent(parent, false); go.transform.localPosition = local; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = Visuals.Cube; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; return go;
        }

        void BuildBoardsAndPosts(Transform r)
        {
            foreach (var b in T.Boards)
            {
                int i = b.I; float o = b.Side * (T.StreetCircuit ? T.WallD + .6f : T.W / 2 + 5.5f);
                var tex = BoardTex(new[] { "300", "200", "100" }[b.K]);
                var go = Box(r, Visuals.World(T.X[i] + T.NX[i] * o, T.Y[i] + T.NY[i] * o, T.E[i] + (T.StreetCircuit ? 2f : 1.6f)), new Vector3(1.2f, 1.5f, .05f), Visuals.Mat("board" + b.K, Color.white, tex, .3f, 0, false, null, Look.Hdr(Color.white, 1.6f)));
                go.transform.rotation = Visuals.Yaw(T.Heading(i)) * Quaternion.Euler(0, 180, 0);
            }
            PostPanels = new Material[T.MS];
            var hut = Visuals.Mat("post", Look.Pick(Visuals.Hex("#c8651a"), Visuals.Hex("#231a14")), null, .4f);
            for (int s = 0; s < T.MS; s++)
            {
                int i = (s * T.N / T.MS + 4) % T.N; int side = T.WallN[i] == 1 ? -1 : T.WallP[i] == 1 ? 1 : -1; float o = side * (T.WallD + 2.4f);
                var basePos = Visuals.World(T.X[i] + T.NX[i] * o, T.Y[i] + T.NY[i] * o, T.E[i]);
                Box(r, basePos + new Vector3(0, 1.15f, 0), new Vector3(1.4f, 2.3f, 1.4f), hut, true);
                PostPanels[s] = new Material(Visuals.Unlit("paneloff", new Color(.07f, .07f, .07f)));
                var pan = Box(r, basePos + new Vector3(0, 3.4f, 0), new Vector3(1.5f, 1.1f, .08f), PostPanels[s]);
                pan.transform.rotation = Visuals.Yaw(T.Heading(i)) * Quaternion.Euler(0, 180, 0);
            }
        }
        static Texture2D BoardTex(string label)
        {
            // simple 3/2/1 stripe boards (no font dependency): dark board with amber light bars
            int bars = label == "300" ? 3 : label == "200" ? 2 : 1;
            var t = new Texture2D(64, 80); var px = new Color[64 * 80];
            for (int y = 0; y < 80; y++) for (int x = 0; x < 64; x++)
                {
                    bool border = x < 4 || x > 59 || y < 4 || y > 75; bool bar = false;
                    for (int k = 0; k < bars; k++) { int c = (int)(32 + (k - (bars - 1) / 2f) * 14); if (x > c - 4 && x < c + 4 && y > 14 && y < 66) bar = true; }
                    px[y * 64 + x] = border || bar ? Look.Amber : new Color(.03f, .035f, .05f);
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        void BuildStands(Transform r)
        {
            float cc = Look.Pick(1f, .45f); var crowd = Visuals.Mat("crowd", new Color(cc, cc, cc), Visuals.Crowd, .1f, 0, false, null, Look.Hdr(Color.white, .6f)); var roof = RoofM();
            var edge = Visuals.Neon("standEdge", Look.Cyan, 2.2f);
            Action<byte[], int> stand = (mask, sg) =>
            {
                Strip(r, "Stand", sg * (T.WallD + 3), .8f, sg * (T.WallD + 16), 9, mask, crowd, 10, 1);
                Strip(r, "StandRoof", sg * (T.WallD + 3), 12.5f, sg * (T.WallD + 12), 13f, mask, roof, 10, 1, true);   // two-sided: the roof is mostly seen from below
                // light strips along the roof edge and the front of the first row
                Strip(r, "StandLight", sg * (T.WallD + 3), 12.25f, sg * (T.WallD + 3), 12.5f, mask, edge, 10, 1, true);
                Strip(r, "StandLight", sg * (T.WallD + 3), .55f, sg * (T.WallD + 3), .8f, mask, edge, 10, 1, true);
            };
            Func<int, int, int, byte[]> mk = (from, to, sg) =>
            {
                var m = new byte[T.N]; int cnt = 0;
                for (int k = from; k <= to; k++) { int i = T.Wrap(k); float px = T.X[i] + T.NX[i] * sg * (T.WallD + 18), py = T.Y[i] + T.NY[i] * sg * (T.WallD + 18); if (T.Nearest(px, py) > T.WallD + 12 && (sg > 0 ? T.WallP : T.WallN)[i] == 1) { m[i] = 1; cnt++; } }
                return cnt > 8 ? m : null;
            };
            var main = mk(-40, 30, -T.PS); if (main != null) stand(main, -T.PS);
            int n = 0; foreach (var a in T.Apexes) { if (n >= 3) break; int sg = T.KC[a] > 0 ? -1 : 1; var m = mk(a - 30, a + 5, sg); if (m != null) { stand(m, sg); n++; } }
        }

        void BuildScenery(Transform r)
        {
            var rnd = new System.Random(T.Id.GetHashCode());
            Func<float, float, float> R = (a, b) => a + (float)rnd.NextDouble() * (b - a);
            float x0 = T.MinX - 450, x1 = T.MaxX + 450, y0 = T.MinY - 450, y1 = T.MaxY + 450;
            var holder = new GameObject("Scenery"); holder.transform.SetParent(r, false);
            if (T.StreetCircuit)
            {
                string[] day = { "#6f767e", "#857f75", "#5c636b", "#8f949a" }, nightC = { "#0f1118", "#14121a", "#0b0d13", "#161820" }; var mats = new Material[4];
                for (int q = 0; q < 4; q++) mats[q] = Visuals.Mat("bld" + q, Visuals.Hex(Look.Night ? nightC[q] : day[q]), null, .65f, .3f, false, new Vector2(2 + q, 3 + q * 2), Look.Hdr(Color.white, 1.4f), Visuals.Windows);
                int k = 0;
                for (int a = 0; a < 7000 && k < 420; a++)
                {
                    float w = R(12, 40), d = R(12, 40), h = R(10, 80), x = R(x0, x1), y = R(y0, y1);
                    float dist = T.Nearest(x, y); if (dist < T.WallD + 8 + Math.Max(w, d) * .72f || dist > 240) continue;
                    var b = Box(holder.transform, Visuals.World(x, y, TerrainAt(x, y) + h / 2 - 1), new Vector3(w, h, d), mats[k % mats.Length], true);
                    b.transform.rotation = Quaternion.Euler(0, R(0, 180), 0); b.isStatic = true; k++;
                }
            }
            else
            {
                float lk = Look.Pick(.85f, .3f); Color lt = new Color(lk, lk * Look.Pick(.92f, 1f), lk * 1.25f);
                var leaf = new[] { Visuals.Mat("leaf0", Visuals.Hex("#2f5a2a") * lt), Visuals.Mat("leaf1", Visuals.Hex("#3d6b33") * lt), Visuals.Mat("leaf2", Visuals.Hex("#4a7a3a") * lt) };
                var trunk = Visuals.Mat("trunk", Visuals.Hex("#4a3826") * lt);
                int k = 0;
                for (int a = 0; a < 9000 && k < 900; a++)
                {
                    float x = R(x0, x1), y = R(y0, y1), dist = T.Nearest(x, y); if (dist < T.WallD + 6 || dist > 420) continue;
                    float s = R(.7f, 1.5f), gy = TerrainAt(x, y);
                    var tree = new GameObject("Tree"); tree.transform.SetParent(holder.transform, false); tree.transform.position = Visuals.World(x, y, gy); tree.isStatic = true;
                    var t1 = Box(tree.transform, new Vector3(0, 1.2f * s, 0), new Vector3(.5f * s, 2.4f * s, .5f * s), trunk);
                    var crown = new GameObject("Crown"); crown.transform.SetParent(tree.transform, false); crown.transform.localPosition = new Vector3(0, 5f * s, 0); crown.transform.localScale = new Vector3(5f * s, 5.5f * s, 5f * s);
                    crown.AddComponent<MeshFilter>().sharedMesh = Visuals.Sphere; crown.AddComponent<MeshRenderer>().sharedMaterial = leaf[k % 3];
                    k++;
                }
            }
            StaticBatchingUtility.Combine(holder);
        }

        void BuildMap()
        {
            int S = 256; MapTexture = new Texture2D(S, S, TextureFormat.RGBA32, false); var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            float sx = (S - 24) / Math.Max(T.MaxX - T.MinX, T.MaxY - T.MinY);
            for (int i = 0; i < T.N; i++)
            {
                int x = (int)(12 + (T.X[i] - T.MinX) * sx), y = S - 1 - (int)(12 + (T.Y[i] - T.MinY) * sx);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < S && yy < S) px[yy * S + xx] = new Color(Look.Cyan.r, Look.Cyan.g, Look.Cyan.b, .9f); }
            }
            MapTexture.SetPixels(px); MapTexture.Apply();
            MapScale = sx; MapSize = S;
        }
        public float MapScale; public int MapSize;
        public Vector2 MapPoint(float x, float y) { return new Vector2(12 + (x - T.MinX) * MapScale, 12 + (y - T.MinY) * MapScale); }
    }
}
