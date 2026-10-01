// Turns a circuit outline into a racing surface: resampled centreline (3 m), normals, curvature,
// a smoothed racing line, an AI speed profile, elevation/gradient, barriers, gravel, pit lane,
// active-aero straights, braking boards and marshal sectors. Port of the web build's core.js.
using System;
using System.Collections.Generic;

namespace LightsOut
{
    public struct Board { public int I; public int K; public int Side; }
    public struct Zone { public int Start; public int Len; }

    public class Track
    {
        public TrackJson Src;
        public string Id;
        public bool StreetCircuit, NightRace, HasElevation;
        public float W;           // track width
        public int N;             // centreline samples
        public float[] X, Y, E, TX, TY, NX, NY, RL, VP, KC, GR, KV;
        public byte[] Kerb, WallP, WallN, GravP, GravN, Aero;
        public float WallD;
        public List<Board> Boards = new List<Board>();
        public List<int> Apexes = new List<int>();
        public List<Zone> Zones = new List<Zone>();
        public int Det;           // overtake-mode detection index
        public int PE0 = -72, PE1 = -52, PX0 = 24, PX1 = 44; // pit lane (index offsets from the line)
        public int PS;            // pit side (+1/-1 along the normal)
        public float PitLat;
        public int MS;            // marshal sectors
        public float CornerFrac;
        public float MinX, MinY, MaxX, MaxY;

        static readonly Dictionary<string, Track> cache = new Dictionary<string, Track>();

        public static Track Build(TrackJson t)
        {
            Track tr;
            if (cache.TryGetValue(t.id, out tr)) return tr;
            tr = new Track();
            tr.Construct(t);
            cache[t.id] = tr;
            return tr;
        }

        static float[] Curvature(float[] hx, float[] hy, int n)
        {
            var th = new float[n]; var k = new float[n]; var k2 = new float[n];
            for (int i = 0; i < n; i++) { int a = MathX.Mod(i - 1, n), b = (i + 1) % n; th[i] = (float)Math.Atan2(hy[b] - hy[a], hx[b] - hx[a]); }
            for (int i = 0; i < n; i++) k[i] = MathX.WrapAngle(th[(i + 2) % n] - th[MathX.Mod(i - 2, n)]) / (4 * Config.Step);
            for (int i = 0; i < n; i++) { float s = 0; for (int j = -3; j <= 3; j++) s += k[MathX.Mod(i + j, n)]; k2[i] = s / 7f; }
            return k2;
        }

        static void Resample(List<float> px, List<float> py, List<float> pz, out float[] rx, out float[] ry, out float[] rz)
        {
            int D = px.Count; var cum = new double[D + 1];
            for (int i = 1; i <= D; i++) { int a = i - 1, b = i % D; cum[i] = cum[i - 1] + Math.Sqrt((px[b] - px[a]) * (px[b] - px[a]) + (py[b] - py[a]) * (py[b] - py[a])); }
            double total = cum[D]; int m = (int)Math.Round(total / Config.Step); double st = total / m;
            rx = new float[m]; ry = new float[m]; rz = new float[m];
            for (int i = 0, j = 0; i < m; i++)
            {
                double s = i * st; while (j < D - 1 && cum[j + 1] < s) j++;
                int b = (j + 1) % D; double seg = cum[j + 1] - cum[j]; float f = (float)(seg > 0 ? (s - cum[j]) / seg : 0);
                rx[i] = px[j] + (px[b] - px[j]) * f; ry[i] = py[j] + (py[b] - py[j]) * f; rz[i] = pz[j] + (pz[b] - pz[j]) * f;
            }
        }

        static float[] Smooth3(float[] a)
        {
            int n = a.Length; var b = new float[n];
            for (int i = 0; i < n; i++) b[i] = .25f * a[MathX.Mod(i - 1, n)] + .5f * a[i] + .25f * a[(i + 1) % n];
            return b;
        }

        void Construct(TrackJson t)
        {
            Src = t; Id = t.id;
            StreetCircuit = Config.Street.Contains(t.id); NightRace = Config.Night.Contains(t.id);
            HasElevation = t.e != null && t.e.Length > 0;
            W = StreetCircuit ? 13f : 15f;
            int m = t.p.Length / 2;
            var rawX = new float[m]; var rawY = new float[m]; var rawZ = new float[m];
            for (int i = 0; i < m; i++) { rawX[i] = t.p[2 * i]; rawY[i] = t.p[2 * i + 1]; rawZ[i] = HasElevation ? t.e[i] : 0f; }
            // Catmull-Rom densify (~1 m)
            var dx = new List<float>(); var dy = new List<float>(); var dz = new List<float>();
            for (int i = 0; i < m; i++)
            {
                int i0 = MathX.Mod(i - 1, m), i2 = (i + 1) % m, i3 = (i + 2) % m;
                int k = Math.Max(1, (int)Math.Ceiling(MathX.Hypot(rawX[i2] - rawX[i], rawY[i2] - rawY[i])));
                for (int j = 0; j < k; j++)
                {
                    float s = (float)j / k, s2 = s * s, s3 = s2 * s;
                    dx.Add(Cr(rawX[i0], rawX[i], rawX[i2], rawX[i3], s, s2, s3));
                    dy.Add(Cr(rawY[i0], rawY[i], rawY[i2], rawY[i3], s, s2, s3));
                    dz.Add(Cr(rawZ[i0], rawZ[i], rawZ[i2], rawZ[i3], s, s2, s3));
                }
            }
            float[] x, y, e;
            Resample(dx, dy, dz, out x, out y, out e);
            for (int p = 0; p < 3; p++) { x = Smooth3(x); y = Smooth3(y); e = Smooth3(e); }
            // open hairpins tighter than ~16 m radius
            for (int it = 0; it < 30; it++)
            {
                int n0 = x.Length; var k = Curvature(x, y, n0); var flag = new bool[n0]; int hit = 0;
                for (int i = 0; i < n0; i++) if (Math.Abs(k[i]) > 1f / 16f) { hit++; for (int j = -4; j <= 4; j++) flag[MathX.Mod(i + j, n0)] = true; }
                if (hit == 0) break;
                var x2 = (float[])x.Clone(); var y2 = (float[])y.Clone();
                for (int i = 0; i < n0; i++) if (flag[i]) { int a = MathX.Mod(i - 1, n0), b = (i + 1) % n0; x2[i] = .25f * x[a] + .5f * x[i] + .25f * x[b]; y2[i] = .25f * y[a] + .5f * y[i] + .25f * y[b]; }
                Resample(new List<float>(x2), new List<float>(y2), new List<float>(e), out x, out y, out e);
            }
            for (int p = 0; p < 6; p++) e = Smooth3(e);
            int n = x.Length; N = n; X = x; Y = y; E = e;
            TX = new float[n]; TY = new float[n]; NX = new float[n]; NY = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = MathX.Mod(i - 1, n), b = (i + 1) % n; float ddx = X[b] - X[a], ddy = Y[b] - Y[a], l = MathX.Hypot(ddx, ddy); if (l < 1e-6f) l = 1;
                TX[i] = ddx / l; TY[i] = ddy / l; NX[i] = -TY[i]; NY[i] = TX[i];
            }
            GR = new float[n]; KV = new float[n];
            for (int i = 0; i < n; i++) GR[i] = (E[(i + 2) % n] - E[MathX.Mod(i - 2, n)]) / (4 * Config.Step);
            for (int i = 0; i < n; i++) KV[i] = (GR[(i + 4) % n] - GR[MathX.Mod(i - 4, n)]) / (8 * Config.Step);
            KC = Curvature(X, Y, n);
            // racing line: Laplacian smoothing constrained to the track width
            float lim = W / 2 - 1.7f; RL = new float[n];
            for (int it = 0; it < 160; it++)
            {
                var r2 = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int a = MathX.Mod(i - 1, n), b = (i + 1) % n;
                    float mx = (X[a] + NX[a] * RL[a] + X[b] + NX[b] * RL[b]) / 2, my = (Y[a] + NY[a] * RL[a] + Y[b] + NY[b] * RL[b]) / 2;
                    r2[i] = MathX.Clamp((mx - X[i]) * NX[i] + (my - Y[i]) * NY[i], -lim, lim);
                }
                RL = r2;
            }
            var lx = new float[n]; var ly = new float[n];
            for (int i = 0; i < n; i++) { lx[i] = X[i] + NX[i] * RL[i]; ly[i] = Y[i] + NY[i] * RL[i]; }
            var KR = Curvature(lx, ly, n);
            VP = new float[n];
            for (int i = 0; i < n; i++)
            {
                float load = MathX.Clamp(1 + KV[i] * 600, .75f, 1.3f);
                VP[i] = Math.Min(VCorner(KR[i] / load), 1.25f * VCorner(KC[i] / load));
            }
            for (int r = 0; r < 2; r++) for (int i = n - 1; i >= 0; i--) { float vb = VP[(i + 1) % n]; VP[i] = Math.Min(VP[i], (float)Math.Sqrt(vb * vb + 2 * .78f * Config.BrakeDecel(vb) * Config.Step)); }
            MinX = MinY = 1e9f; MaxX = MaxY = -1e9f;
            for (int i = 0; i < n; i++) { MinX = Math.Min(MinX, X[i]); MaxX = Math.Max(MaxX, X[i]); MinY = Math.Min(MinY, Y[i]); MaxY = Math.Max(MaxY, Y[i]); }
            BuildHash();
            WallD = StreetCircuit ? W / 2 + 1.6f : W / 2 + 11f;
            WallP = new byte[n]; WallN = new byte[n];
            for (int i = 0; i < n; i++)
            {
                WallP[i] = (byte)(Nearest(X[i] + NX[i] * WallD, Y[i] + NY[i] * WallD) > WallD - 1.2f ? 1 : 0);
                WallN[i] = (byte)(Nearest(X[i] - NX[i] * WallD, Y[i] - NY[i] * WallD) > WallD - 1.2f ? 1 : 0);
            }
            Kerb = new byte[n]; GravP = new byte[n]; GravN = new byte[n];
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(KC[i]) > 1f / 120) Kerb[i] = 1;
                if (StreetCircuit) continue;
                if (KC[i] > 1f / 160) for (int q = -8; q < 24; q++) GravN[MathX.Mod(i + q, n)] = 1;
                if (KC[i] < -1f / 160) for (int q = -8; q < 24; q++) GravP[MathX.Mod(i + q, n)] = 1;
            }
            int lastApex = -100000;
            for (int i = 0; i < n; i++)
            {
                float v = VP[i]; if (v > 55) continue;
                if (!(v <= VP[MathX.Mod(i - 1, n)] && v < VP[(i + 1) % n])) continue;
                float mx = 0; for (int k = 1; k <= 100; k++) mx = Math.Max(mx, VP[MathX.Mod(i - k, n)]);
                if (mx - v < 22 || i - lastApex < 60) continue;
                int ee = i; for (int k = 0; k < 60; k++) { int q = MathX.Mod(ee - 1, n); if (VP[q] > v + 4) break; ee = q; }
                int side = KC[i] > 0 ? -1 : 1;
                for (int k = 0; k < 3; k++) Boards.Add(new Board { I = MathX.Mod(ee - (int)Math.Round((3 - k) * 100 / Config.Step), n), K = k, Side = side });
                Apexes.Add(i); lastApex = i;
            }
            Det = n - 110; foreach (var a in Apexes) if (a > n * .55f && a < n - 25) Det = a;
            Aero = new byte[n];
            int s0 = 0; while (s0 < n && Math.Abs(KC[s0]) < 1f / 450) s0++;
            for (int k = 0; k < n;)
            {
                int i = (s0 + k) % n; if (!(Math.Abs(KC[i]) < 1f / 450)) { k++; continue; }
                int len = 0; while (len < n && Math.Abs(KC[(s0 + k + len) % n]) < 1f / 450) len++;
                if (len * Config.Step >= 300) { Zones.Add(new Zone { Start = (s0 + k + 6) % n, Len = len - 10 }); for (int q = 6; q < len - 4; q++) Aero[(s0 + k + q) % n] = 1; }
                k += len;
            }
            int freeP = 0, freeN = 0;
            for (int k = PE0; k <= PX1; k += 4)
            {
                int i = MathX.Mod(k, n);
                if (Nearest(X[i] + NX[i] * (WallD + 14), Y[i] + NY[i] * (WallD + 14)) > WallD + 10) freeP++;
                if (Nearest(X[i] - NX[i] * (WallD + 14), Y[i] - NY[i] * (WallD + 14)) > WallD + 10) freeN++;
            }
            PS = freeP > freeN ? 1 : -1; PitLat = PS * (WallD + 4.5f);
            var pm = PS > 0 ? WallP : WallN;
            for (int k = PE0; k <= PE1 + 2; k++) pm[MathX.Mod(k, n)] = 0;
            for (int k = PX0 - 2; k <= PX1; k++) pm[MathX.Mod(k, n)] = 0;
            for (int k = PE1 + 3; k < PX0 - 2; k++) pm[MathX.Mod(k, n)] = 1;
            MS = MathX.Clamp((int)Math.Round(n * Config.Step / 320f), 10, 22);
            int corners = 0; for (int i = 0; i < n; i++) if (Math.Abs(KC[i]) > 1f / 150) corners++;
            CornerFrac = Math.Max(.05f, (float)corners / n);
        }

        static float Cr(float p0, float p1, float p2, float p3, float s, float s2, float s3)
        {
            return .5f * (2 * p1 + (-p0 + p2) * s + (2 * p0 - 5 * p1 + 4 * p2 - p3) * s2 + (-p0 + 3 * p1 - 3 * p2 + p3) * s3);
        }

        static float VCorner(float k)
        {
            k = Math.Abs(k); float mm = .92f * Config.Mech, a = .92f * Config.Aero;
            return k <= a + 1e-4f ? 95f : Math.Min(95f, (float)Math.Sqrt(mm / (k - a)));
        }

        // ---- spatial queries ----
        const float HC = 40f;
        Dictionary<long, List<int>> hash;
        static long Key(int hx, int hy) { return ((long)hx << 32) ^ (uint)hy; }
        void BuildHash()
        {
            hash = new Dictionary<long, List<int>>();
            for (int i = 0; i < N; i++)
            {
                long k = Key((int)Math.Floor(X[i] / HC), (int)Math.Floor(Y[i] / HC));
                List<int> l; if (!hash.TryGetValue(k, out l)) { l = new List<int>(); hash[k] = l; }
                l.Add(i);
            }
        }
        /// Distance to the nearest centreline point (searches up to ~200 m, else brute force).
        public float Nearest(float px, float py) { int bi; return Nearest(px, py, out bi); }
        public float Nearest(float px, float py, out int bestIdx)
        {
            int hx = (int)Math.Floor(px / HC), hy = (int)Math.Floor(py / HC); float best = 1e18f; bestIdx = -1;
            for (int ox = -5; ox <= 5; ox++) for (int oy = -5; oy <= 5; oy++)
                {
                    List<int> l; if (!hash.TryGetValue(Key(hx + ox, hy + oy), out l)) continue;
                    foreach (int i in l) { float ddx = X[i] - px, ddy = Y[i] - py, d = ddx * ddx + ddy * ddy; if (d < best) { best = d; bestIdx = i; } }
                }
            if (bestIdx < 0) for (int i = 0; i < N; i += 2) { float ddx = X[i] - px, ddy = Y[i] - py, d = ddx * ddx + ddy * ddy; if (d < best) { best = d; bestIdx = i; } }
            return (float)Math.Sqrt(best);
        }

        public int Wrap(int i) { return MathX.Mod(i, N); }
        public float Heading(int i) { return (float)Math.Atan2(TY[i], TX[i]); }
        public void PosF(float p, float lat, out float x, out float y)
        {
            int i0 = (int)Math.Floor(p); float f = p - i0; int a = Wrap(i0), b = (a + 1) % N;
            x = MathX.Lerp(X[a] + NX[a] * lat, X[b] + NX[b] * lat, f);
            y = MathX.Lerp(Y[a] + NY[a] * lat, Y[b] + NY[b] * lat, f);
        }
        public float ElevAt(float p) { int i0 = (int)Math.Floor(p); float f = p - i0; int a = Wrap(i0), b = (a + 1) % N; return MathX.Lerp(E[a], E[b], f); }
        public int SectorOf(int i) { return Math.Min(MS - 1, (int)((float)i / N * MS)); }
        public int ZoneId(int i)
        {
            for (int k = 0; k < Zones.Count; k++) { int r = MathX.Mod(i - Zones[k].Start, N); if (r < Zones[k].Len) return k; }
            return -1;
        }
    }
}
