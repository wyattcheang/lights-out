// Lights Out (Unity) - shared constants. Units: metres, seconds, m/s, watts, megajoules.
using System;
using System.Collections.Generic;

namespace LightsOut
{
    public static class MathX
    {
        public static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }
        public static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Smooth(float t) { t = Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
        public static float WrapAngle(float a)
        {
            while (a > (float)Math.PI) a -= 2f * (float)Math.PI;
            while (a < -(float)Math.PI) a += 2f * (float)Math.PI;
            return a;
        }
        public static float Hypot(float x, float y) { return (float)Math.Sqrt(x * x + y * y); }
        public static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }
        static readonly Random rng = new Random();
        public static float Rnd(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
        public static float Rnd01() { return (float)rng.NextDouble(); }
        public static string FormatTime(float? t)
        {
            if (t == null || float.IsNaN(t.Value) || float.IsInfinity(t.Value)) return "-:--.---";
            int m = (int)(t.Value / 60f); float s = t.Value - m * 60;
            return m + ":" + (s < 10 ? "0" : "") + s.ToString("0.000");
        }
    }

    public enum Compound { Soft = 0, Medium = 1, Hard = 2 }

    public struct TyreSpec
    {
        public string Name; public float Grip; public float Life; public float R, G, B;
        public TyreSpec(string n, float grip, float life, float r, float g, float b) { Name = n; Grip = grip; Life = life; R = r; G = g; B = b; }
    }

    public static class Config
    {
        public const float Step = 3f;                 // centreline sample spacing (m)
        // 2026-style power unit: ~400 kW engine + up to 350 kW MGU-K, ~4 MJ usable energy store
        public const float Mass = 800f, PIce = 400e3f, PErs = 350e3f;
        public const float DragCorner = .95f, DragStraight = .75f, Traction = 12.5f, Grip = 9f, YawMax = 2.6f;
        public const float Mech = 24f, Aero = .0042f;  // lateral grip = Mech + Aero*v^2  (m/s^2)
        public const float ErsCap = 4f, OvertakeBonus = .5f, HarvestBrake = .35f, HarvestLift = .1f;
        public const float TaperFrom = 80.6f, TaperTo = 96f, OtFull = 93.6f, OtTo = 98.6f; // 290 / 345 / 337 / 355 km/h
        public const float PitSpeed = 80f / 3.6f;
        public const float GapSpeed = 58f;
        public static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
        public static readonly TyreSpec[] Tyres = {
            new TyreSpec("Soft", 1.035f, .5f, .91f, .2f, .18f),
            new TyreSpec("Medium", 1f, .8f, 1f, .82f, .12f),
            new TyreSpec("Hard", .972f, 1.15f, .95f, .94f, .92f)
        };
        public static float LatGrip(float v, bool straight) { return Mech + Aero * v * v * (straight ? .55f : 1f); }
        public static float BrakeDecel(float v) { return 24f + .0035f * v * v; }
        public static readonly HashSet<string> Street = new HashSet<string> { "mc-1929", "sg-2008", "az-2016", "sa-2021", "us-2023", "us-2022", "ca-1978", "es-2026" };
        public static readonly HashSet<string> Night = new HashSet<string> { "sg-2008", "us-2023", "bh-2002", "sa-2021", "qa-2004", "ae-2009" };
        public static readonly string[][] Rivals = {
            new[]{"VAL","#e8322e","#1b1d22"}, new[]{"ORS","#2f6bff","#ffd21f"}, new[]{"KIM","#ff8a1f","#1d2a44"}, new[]{"DUV","#13c4a3","#0d1f2b"},
            new[]{"MAR","#c9ced6","#00a19b"}, new[]{"SEN","#7a3cff","#ffffff"}, new[]{"HAK","#ff4f9a","#1a1a1a"}, new[]{"LOR","#0b6b3a","#e9e3c8"}, new[]{"BRE","#1a1f2b","#e83b3b"}
        };
        public static readonly Dictionary<string, float> Difficulty = new Dictionary<string, float> { { "rookie", .86f }, { "pro", .92f }, { "ace", .975f } };
        public static readonly Dictionary<string, float> IncidentRate = new Dictionary<string, float> { { "off", 0f }, { "real", .035f }, { "chaos", .11f } };
        public static readonly float[] GearTops = { 0, 22, 32, 42, 52, 62, 72, 82, 100 };
    }
}
