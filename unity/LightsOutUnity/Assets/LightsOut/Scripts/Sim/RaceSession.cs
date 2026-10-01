// The race simulation: physics, 2026 energy and active aero, tyres, AI, pit stops, flags,
// VSC / Safety Car, incidents, penalties and classification. Engine-agnostic (no UnityEngine).
using System;
using System.Collections.Generic;
using System.Linq;

namespace LightsOut
{
    public enum SessionMode { Attract, Race, TimeTrial }

    public struct DriverInput { public float Steer, Throttle, Brake; public bool Handbrake, Boost; }

    public class Neutral { public string Type; public string Phase; public float T, Clear, EndAt; public bool PenaltyGiven; public int RestartLap = -999; }

    public class SafetyCar { public float PF, V, X, Y, H, Lat, PP; public bool Lights = true, InPit, Gone; }

    public class Yellow { public int Level; public float Until; }

    public class RaceSession
    {
        public Track T;
        public SessionMode Mode;
        public List<Car> Cars = new List<Car>();
        public Car Player;
        public float Time, RaceStart, LightsHold;
        public bool Started, Over;
        public int Laps = 5; public string Difficulty = "pro", Incidents = "real";
        public float RaceDist = 1f;
        public Neutral Neutral; public SafetyCar SC; public Yellow[] Yel;
        public float GreenUntil, BlackWhiteUntil;
        public bool Authority = true;                 // false on online guests: race control comes from the host
        public DriverInput Input;
        public event Action<string, string> Radio;    // message, tone (yellow/red/green/blue/"")
        public event Action<Car> PlayerFinished;

        void Say(string m, string tone) { if (Radio != null) Radio(m, tone); }

        // ---------------- setup ----------------
        public void Setup(Track t, SessionMode mode, int field, Compound startTyre)
        {
            T = t; Mode = mode; Cars.Clear(); Player = null; Time = 0; Over = false; Neutral = null; SC = null; GreenUntil = 0;
            Yel = new Yellow[T.MS]; for (int i = 0; i < T.MS; i++) Yel[i] = new Yellow();
            Started = mode != SessionMode.Race; RaceStart = 0; LightsHold = 4.6f + MathX.Rnd(0, 1.2f);
            RaceDist = (mode == SessionMode.Race ? Math.Max(3, Laps) : 6) * T.N * Config.Step;
            if (mode == SessionMode.TimeTrial)
            {
                var p = Pose(-(int)Math.Round(160 / Config.Step), 0);
                var me = NewCar(p); me.IsPlayer = true; me.Garage = 3; me.SetTyre(startTyre); me.Soc = Config.ErsCap; Cars.Add(me); Player = me;
                return;
            }
            int n = mode == SessionMode.Race ? field : 8, meSlot = mode == SessionMode.Race ? n / 2 : -1;
            float baseSkill = mode == SessionMode.Race ? Config.Difficulty[Difficulty] : .93f; int r = 0;
            for (int s = 0; s < n; s++)
            {
                var p = GridPose(s);
                if (s == meSlot) { var me = NewCar(p); me.IsPlayer = true; me.Garage = s; me.SetTyre(startTyre); me.NextTyre = startTyre == Compound.Hard ? Compound.Medium : Compound.Hard; Cars.Add(me); Player = me; continue; }
                var riv = Config.Rivals[r++ % Config.Rivals.Length];
                var c = NewCar(p); c.IsAI = true; c.Code = riv[0]; c.Color = riv[1]; c.Accent = riv[2]; c.Garage = s;
                c.Skill = MathX.Clamp(baseSkill - s * .005f + MathX.Rnd(0, .012f), .7f, 1f); c.LaneBase = MathX.Rnd(-.6f, .6f); c.Lane = c.LaneBase;
                float roll = MathX.Rnd01(); c.SetTyre(roll < .35f ? Compound.Soft : roll < .85f ? Compound.Medium : Compound.Hard);
                c.NextTyre = c.Tyre == Compound.Hard ? Compound.Medium : Compound.Hard;
                float la = Config.Tyres[(int)c.Tyre].Life, lb = Config.Tyres[(int)c.NextTyre].Life;
                c.PlanLap = MathX.Clamp((int)Math.Round(Laps * la / (la + lb) * MathX.Rnd(.8f, 1.2f)), 1, Math.Max(1, Laps - 1));
                Cars.Add(c);
            }
        }

        public Car NewCar(GridSpot p)
        {
            var c = new Car { X = p.X, Y = p.Y, H = p.H, Idx = p.I, Prog = p.I - T.N, PF = p.I - T.N };
            return c;
        }

        public struct GridSpot { public int I; public float X, Y, H; }
        public GridSpot Pose(int i, float lat)
        {
            i = T.Wrap(i);
            return new GridSpot { I = i, X = T.X[i] + T.NX[i] * lat, Y = T.Y[i] + T.NY[i] * lat, H = T.Heading(i) };
        }
        public GridSpot GridPose(int slot) { float back = 10 + slot * 8; return Pose(-(int)Math.Round(back / Config.Step), (slot % 2 == 1 ? -1 : 1) * T.W * .22f); }

        // ---------------- helpers ----------------
        public void Locate(Car c)
        {
            int n = T.N; float best = 1e18f; int bi = c.Idx;
            for (int k = -12; k <= 40; k++) { int i = T.Wrap(c.Idx + k); float dx = c.X - T.X[i], dy = c.Y - T.Y[i], d = dx * dx + dy * dy; if (d < best) { best = d; bi = i; } }
            int delta = bi - c.Idx; if (delta > n / 2) delta -= n; if (delta < -n / 2) delta += n;
            c.Idx = bi; c.Prog += delta;
            float ddx = c.X - T.X[bi], ddy = c.Y - T.Y[bi];
            c.D = ddx * T.NX[bi] + ddy * T.NY[bi];
            c.PF = c.Prog + (ddx * T.TX[bi] + ddy * T.TY[bi]) / Config.Step;
        }
        static readonly float[] SurfGrip = { 1f, .85f, .55f, .42f }, SurfDrag = { 0f, .04f, .55f, 1.15f };
        int SurfaceAt(Car c)
        {
            float ad = Math.Abs(c.D), hw = T.W / 2;
            if (ad <= hw + 1.2f || T.StreetCircuit) return 0;
            if ((c.D > 0 ? T.GravP : T.GravN)[c.Idx] == 1 && ad < hw + 9) return 3;
            return ad < hw + 5 ? 1 : 2;
        }
        public float TyreGrip(Car c)
        {
            float w = c.Wear;
            float f = w < .6f ? 1 - .05f * w / .6f : w < 1 ? .95f - .2f * (w - .6f) / .4f : .75f - .08f * Math.Min(1, (w - 1) / .3f);
            return Config.Tyres[(int)c.Tyre].Grip * f * (.955f + .045f * c.Warm);
        }
        public int SectorYellow(int i) { var y = Yel[T.SectorOf(i)]; return y.Until > Time ? y.Level : 0; }
        public void SetYellow(int i, int lvl, float dur)
        {
            int s = T.SectorOf(i); var y = Yel[s];
            if (y.Until < Time || y.Level < lvl) y.Level = lvl; y.Until = Math.Max(y.Until, Time + dur);
            var p = Yel[MathX.Mod(s - 1, T.MS)]; if (p.Until < Time + dur) { p.Level = Math.Max(1, p.Until > Time ? p.Level : 1); p.Until = Time + dur; }
        }
        public List<Car> Order()
        {
            var l = new List<Car>(Cars);
            l.Sort((a, b) =>
            {
                if (a.Retired != b.Retired) return a.Retired ? 1 : -1;
                if (a.Finished && b.Finished) return (a.FinishTime.Value + a.Penalty).CompareTo(b.FinishTime.Value + b.Penalty);
                if (a.Finished) return -1; if (b.Finished) return 1;
                return b.PF.CompareTo(a.PF);
            });
            return l;
        }
        Car CarAhead(Car c, out float dist)
        {
            int n = T.N; Car best = null; float bd = 1e9f;
            foreach (var o in Cars)
            {
                if (o == c || o.Retired || o.Hidden || o.Pit != null) continue;
                float dp = o.PF - c.PF; dp = ((dp % n) + n) % n; if (dp < bd) { bd = dp; best = o; }
            }
            dist = bd * Config.Step; return best;
        }
        bool IsLeader(Car c) { foreach (var o in Order()) if (!o.Retired && !o.Finished) return o == c; return false; }
        float VscRef(int i) { return Math.Max(16f, T.VP[i] * .6f); }

        // ---------------- physics ----------------
        void ErsStep(Car c, float v, out float dep, out float harv, out float clip)
        {
            dep = 0; harv = 0; clip = 0; bool neutral = Neutral != null;
            if (c.Thr > .9f && Started)
            {
                if (c.Soc > .02f)
                {
                    float frac = (c.OtActive || c.Boost) ? 1f : c.RechargeMode ? .25f : .55f * MathX.Clamp((c.Soc - .3f) / 1.2f, .35f, 1f);
                    if (neutral) frac = Math.Min(frac, .3f);
                    float taper = c.OtActive ? MathX.Clamp((Config.OtTo - v) / (Config.OtTo - Config.OtFull), 0, 1) : MathX.Clamp((Config.TaperTo - v) / (Config.TaperTo - Config.TaperFrom), 0, 1);
                    dep = Config.PErs * frac * taper;
                }
                if (!c.Boost && !c.OtActive)
                {
                    if (c.RechargeMode && v > 60) { harv = .25f; clip = harv * 1e6f; }
                    else if (v > 78) { harv = .15f; clip = harv * 1e6f; }
                }
            }
            else if (c.Brk > .05f && v > 8) harv = Config.HarvestBrake * Math.Min(1, c.Brk * 1.3f) * Math.Min(1, v / 30);
            else if (c.Thr < .15f && v > 12) harv = Config.HarvestLift;
        }

        void Physics(Car c, float dt)
        {
            int surf = SurfaceAt(c); float grip = TyreGrip(c), g = SurfGrip[surf] * grip;
            float ch = (float)Math.Cos(c.H), sh = (float)Math.Sin(c.H);
            float vf = c.VX * ch + c.VY * sh, sp = Math.Abs(vf);
            float load = MathX.Clamp(1 + T.KV[c.Idx] * sp * sp / 9.81f, .55f, 1.45f); c.Load = load;
            float lat = Config.LatGrip(sp, c.StraightMode) * g * load * (1 - .12f * c.Damage);
            float yaw = c.Steer * Math.Min(Config.YawMax, lat / Math.Max(sp, 6)) * Math.Min(1, sp / 3) * (vf >= 0 ? 1 : -1);
            if (c.Handbrake) yaw *= 1.35f;
            c.H = MathX.WrapAngle(c.H + yaw * dt);
            ch = (float)Math.Cos(c.H); sh = (float)Math.Sin(c.H);
            vf = c.VX * ch + c.VY * sh; float vl = -c.VX * sh + c.VY * ch, v = Math.Max(0, vf);
            float dep, harv, clip; ErsStep(c, v, out dep, out harv, out clip);
            float a = 0;
            if (c.Thr > 0 && vf > -.5f) { float pw = Math.Max(0, Config.PIce * c.Thr - clip) + dep; a += Math.Min(pw / (Config.Mass * Math.Max(v, 4)), Config.Traction * g) * (surf >= 2 ? .75f : 1); }
            if (c.Brk > 0) { if (vf > .5f) a -= c.Brk * Config.BrakeDecel(v) * g * Math.Min(1.2f, load); else if (vf > -12 && c.Thr == 0) a -= c.Brk * 6; }
            if (c.Handbrake && vf > 0) a -= 8;
            a -= (c.StraightMode ? Config.DragStraight : Config.DragCorner) * vf * Math.Abs(vf) / Config.Mass + .15f * Math.Sign(vf);
            a -= vf * SurfDrag[surf];
            a -= 9.81f * T.GR[c.Idx] * (float)Math.Cos(c.H - T.Heading(c.Idx));
            float pv = vf; vf += a * dt;
            if (c.Brk > 0 && pv > 0 && vf < 0) vf = 0;
            if (Math.Abs(vf) < .05f && c.Thr == 0) vf = 0;
            vl *= (float)Math.Exp(-Config.Grip * g * (c.Handbrake ? .28f : 1f) * dt);
            c.VX = vf * ch - vl * sh; c.VY = vf * sh + vl * ch;
            c.X += c.VX * dt; c.Y += c.VY * dt;
            c.VF = vf; c.VL = vl; c.Surf = surf; c.Dep = dep; c.Harv = harv;
            c.Soc = MathX.Clamp(c.Soc + (harv - dep / 1e6f) * dt, 0, c.SocCap);
            if (c.SocCap > Config.ErsCap && c.Soc <= Config.ErsCap) c.SocCap = Config.ErsCap;
            float ds = Math.Abs(vf) * dt, latUse = Math.Min(1.5f, Math.Abs(vf * yaw) / Math.Max(lat, 1));
            c.Wear += ds / (Config.Tyres[(int)c.Tyre].Life * RaceDist) * (.7f + .9f * latUse * latUse + (Math.Abs(vl) > 2 ? 1.4f : 0) + (c.Brk > .9f && v > 30 ? .4f : 0));
            if (c.Warm < 1) c.Warm = Math.Min(1, c.Warm + ds / 600);
        }

        float Walls(Car c)
        {
            float lim = T.WallD - 1f, ad = Math.Abs(c.D);
            if (ad <= lim || ad > lim + 5) return 0;
            if ((c.D > 0 ? T.WallP : T.WallN)[c.Idx] == 0) return 0;
            int s = Math.Sign(c.D), i = c.Idx; float nx = T.NX[i] * s, ny = T.NY[i] * s, ex = ad - lim;
            c.X -= nx * ex; c.Y -= ny * ex; c.D = s * lim;
            float vn = c.VX * nx + c.VY * ny;
            if (vn > 0)
            {
                c.VX -= nx * vn * 1.25f; c.VY -= ny * vn * 1.25f;
                float loss = 1 - Math.Min(.45f, vn / 35); c.VX *= loss; c.VY *= loss;
                float th = T.Heading(i), fw = Math.Cos(c.H - th) >= 0 ? th : th + (float)Math.PI;
                c.H += MathX.WrapAngle(fw - c.H) * .15f;
                return vn;
            }
            return 0;
        }

        public void RecoverCar(Car c)
        {
            var p = Pose(c.Idx, T.RL[c.Idx]);
            c.X = p.X; c.Y = p.Y; c.H = p.H; c.VX = c.VY = c.VF = c.VL = 0; c.Steer = 0; c.Recover = 1.2f; c.Stuck = 0;
            Locate(c);
        }

        void AeroStep(Car c)
        {
            if (c.Pit != null || c.Retired) { c.StraightMode = false; return; }
            bool z = T.Aero[c.Idx] == 1;
            if (c.StraightMode) { if (!z || c.Brk > .1f || Math.Abs(c.Steer) > .5f || Neutral != null || SectorYellow(c.Idx) > 1) c.StraightMode = false; }
            else if (z && c.Thr > .9f && Math.Abs(c.Steer) < .3f && Neutral == null && Started)
            {
                int zid = T.ZoneId(c.Idx); if (zid != c.AeroZone) { c.StraightMode = true; c.AeroZone = zid; }
            }
            if (!z) c.AeroZone = -1;
        }

        // ---------------- AI ----------------
        void AiDrive(Car c, float dt)
        {
            int n = T.N; float v = Math.Max(0, c.VF), halfW = T.W / 2; var N = Neutral;
            bool noPass = N != null || SectorYellow(c.Idx) > 0;
            float want = c.MechT >= 0 ? c.Lane : c.LaneBase, capV = 1e9f; bool wantBoost = false; c.Blocked = false;
            if (c.Blue) want = (c.D >= 0 ? 1 : -1) * (halfW - 2) - T.RL[c.Idx];
            foreach (var o in Cars)
            {
                if (o == c || o.Recover > 0 || o.Pit != null || o.Hidden) continue;
                float dp = o.PF - c.PF; dp = ((dp % n) + n + n / 2f) % n - n / 2f; float dm = dp * Config.Step;
                if (dm > 0 && dm < 34 && Math.Abs(o.D - c.D) < 2.7f)
                {
                    float side = c.D >= o.D ? 1 : -1;
                    if (o.Retired || o.Spin != null) { want = o.D + side * 3.6f - T.RL[c.Idx]; continue; }
                    if (noPass) { capV = Math.Min(capV, o.VF + (dm - 12) * .5f); continue; }
                    want = Math.Abs(o.D + side * 3.4f) > halfW - 1.3f ? o.D - side * 3.4f - T.RL[c.Idx] : o.D + side * 3.4f - T.RL[c.Idx];
                    if (dm < 10 && v > o.VF) { c.Blocked = true; capV = Math.Min(capV, o.VF - 1); }
                    if (T.Aero[c.Idx] == 1 && c.Soc > 1.2f) wantBoost = true;
                }
            }
            c.Lane += MathX.Clamp(want - c.Lane, -2.2f * dt, 2.2f * dt);
            int la = Math.Max(3, (int)Math.Round((9 + v * .32f) / Config.Step)), j = (c.Idx + la) % n;
            float off = MathX.Clamp(T.RL[j] + c.Lane, -(halfW - 1.2f) - (c.MechT >= 0 ? 4 : 0), halfW - 1.2f + (c.MechT >= 0 ? 4 : 0));
            float tx = T.X[j] + T.NX[j] * off, ty = T.Y[j] + T.NY[j] * off, ddx = tx - c.X, ddy = ty - c.Y, L = Math.Max(MathX.Hypot(ddx, ddy), 1e-3f);
            float ang = MathX.WrapAngle((float)Math.Atan2(ddy, ddx) - c.H);
            float k = 2 * (float)Math.Sin(ang) / L, vs = Math.Max(v, 6), maxYaw = Math.Min(Config.YawMax, Config.LatGrip(vs, c.StraightMode) * c.Load / vs);
            c.Steer = MathX.Clamp(vs * k / maxYaw, -1, 1);
            float target = 1e9f; int qe = (int)Math.Round(v * .12f / Config.Step) + 3;
            for (int q = 0; q <= qe; q++) target = Math.Min(target, T.VP[(c.Idx + q) % n]);
            target *= c.Skill * (float)Math.Sqrt(TyreGrip(c)) * (1 - .06f * c.Damage);
            if (Math.Abs(c.D) > halfW) target = Math.Min(target, 30);
            int yl = SectorYellow(c.Idx); if (yl == 1) target *= .9f; else if (yl == 2) target *= .72f;
            if (c.Blue) target *= .93f;
            if (N != null)
            {
                if (N.Type == "VSC") target = Math.Min(target, VscRef(c.Idx) * .97f);
                else
                {
                    float gd; var ahead = CarAhead(c, out gd); float tgtGap = 14; float? lv = ahead != null ? (float?)ahead.VF : null;
                    if (SC != null && !SC.InPit) { float sg = (((SC.PF - c.PF) % n) + n) % n * Config.Step; if (sg < gd) { gd = sg; lv = SC.V; tgtGap = 22; } }
                    if (N.Phase == "restart" && IsLeader(c)) { float toLine = (n - c.Idx) * Config.Step; if (toLine >= 160) target = Math.Min(target, T.VP[c.Idx] * .55f); }
                    else if (lv.HasValue && gd < 220) target = Math.Min(target, Math.Max(0, lv.Value + (gd - tgtGap) * .6f));
                    else target = Math.Min(target, T.VP[c.Idx] * .62f);
                }
            }
            if (capV > 0) target = Math.Min(target, capV);
            if (c.MechT > 5) target = 0;
            if (c.BoxRequest) { float toPit = MathX.Mod(n + T.PE0 - c.Idx, n) * Config.Step; if (toPit < 160) target = Math.Min(target, Config.PitSpeed + toPit * .25f); }
            if (v < target - .5f) { c.Thr = 1; c.Brk = 0; }
            else if (v > target + 1.5f) { c.Thr = 0; c.Brk = MathX.Clamp((v - target) / 8, 0, 1); }
            else { c.Thr = .35f; c.Brk = 0; }
            c.Handbrake = false;
            c.Boost = wantBoost && T.Aero[c.Idx] == 1 && c.Soc > .8f && N == null;
            c.RechargeMode = c.Soc < .8f;
            if (c.OtArmed && T.Aero[c.Idx] == 1 && !c.OtActive && N == null) ActivateOvertake(c);
        }

        public bool ActivateOvertake(Car c)
        {
            if (!c.OtArmed || c.OtActive || Neutral != null) return false;
            c.OtArmed = false; c.OtActive = true; c.SocCap = Config.ErsCap + Config.OvertakeBonus; c.Soc = Math.Min(c.SocCap, c.Soc + Config.OvertakeBonus);
            if (c.IsPlayer) Say("Overtake mode on. +0.5 MJ, full power to 337 km/h.", "green");
            return true;
        }

        void PlayerControl(Car c, float dt)
        {
            float tgt = Input.Steer, rate = Math.Abs(tgt) < .01f ? 7 : (c.Steer != 0 && Math.Sign(tgt) != Math.Sign(c.Steer) ? 10 : 4.5f);
            c.Steer += MathX.Clamp(tgt - c.Steer, -rate * dt, rate * dt);
            c.Thr = Input.Throttle; c.Brk = Input.Brake; c.Handbrake = Input.Handbrake; c.Boost = Input.Boost && Neutral == null;
        }

        void Collide(Car a, Car b)
        {
            if (a.Recover > 0 || b.Recover > 0 || a.Pit != null || b.Pit != null || a.Hidden || b.Hidden) return;
            if (a.Kinematic && b.Kinematic) return;
            float dx0 = b.X - a.X, dy0 = b.Y - a.Y; if (dx0 * dx0 + dy0 * dy0 > 49) return;
            float[] pa = { 1.5f, -1.5f }; const float R = 2.1f;
            foreach (var fa in pa) foreach (var fb in pa)
                {
                    float ax = a.X + (float)Math.Cos(a.H) * fa, ay = a.Y + (float)Math.Sin(a.H) * fa, bx = b.X + (float)Math.Cos(b.H) * fb, by = b.Y + (float)Math.Sin(b.H) * fb;
                    float dx = bx - ax, dy = by - ay, d = MathX.Hypot(dx, dy);
                    if (d < R && d > 1e-4f)
                    {
                        dx /= d; dy /= d; float ov = (R - d) / 2;
                        float ma = a.Parked || a.Kinematic ? 0 : 1, mb = b.Parked || b.Kinematic ? 0 : 1, sum = ma + mb; if (sum == 0) sum = 1;
                        a.X -= dx * ov * 2 * ma / sum; a.Y -= dy * ov * 2 * ma / sum; b.X += dx * ov * 2 * mb / sum; b.Y += dy * ov * 2 * mb / sum;
                        float rel = (b.VX - a.VX) * dx + (b.VY - a.VY) * dy;
                        if (rel < 0) { float j = -1.3f * rel / sum; if (ma > 0) { a.VX -= dx * j; a.VY -= dy * j; } if (mb > 0) { b.VX += dx * j; b.VY += dy * j; } }
                    }
                }
        }

        // ---------------- pit lane ----------------
        public float PitLatAt(float p, float lat0)
        {
            if (p < T.PE1) return MathX.Lerp(lat0, T.PitLat, MathX.Smooth((p - T.PE0) / (float)(T.PE1 - T.PE0)));
            if (p < T.PX0) return T.PitLat;
            return MathX.Lerp(T.PitLat, T.RL[T.Wrap(T.PX1)], MathX.Smooth((p - T.PX0) / (float)(T.PX1 - T.PX0)));
        }
        int BoxIdx(Car c) { return -44 + c.Garage * 6; }
        void EnterPit(Car c, float rel)
        {
            c.Pit = new PitState { P = T.PE0 + rel, Phase = "in", Lat0 = c.D, V = Math.Max(Config.PitSpeed, c.VF) };
            c.OtActive = false; c.StraightMode = false; c.Boost = false;
            if (c.IsPlayer) Say("Pit limiter on. Box, box.", "");
        }
        void PitStep(Car c, float dt)
        {
            var P = c.Pit; int bx = BoxIdx(c);
            if (P.Phase == "in" || P.Phase == "lane")
            {
                float toBox = (bx - P.P) * Config.Step, vt = Config.PitSpeed;
                if (toBox < 22) vt = Math.Min(vt, (float)Math.Sqrt(Math.Max(0, 2 * 7 * toBox)));
                P.V += MathX.Clamp(vt - P.V, -35 * dt, 8 * dt);
                if (P.P > T.PE1) P.Phase = "lane";
                if (toBox < .3f && P.V < 1.2f)
                {
                    P.Phase = "stop"; P.V = 0; P.P = bx; P.StopTime = MathX.Rnd(2.1f, 2.9f) + (c.Damage > .05f ? 4.5f : 0);
                    float owe = c.Penalty - c.PenaltyServed; if (owe > 0) { P.StopTime += owe; c.PenaltyServed = c.Penalty; P.Served = owe; }
                    if (c.IsPlayer) Say(P.Served > 0 ? "Serving " + P.Served + " s penalty first." : "Stationary.", "");
                }
            }
            else if (P.Phase == "stop")
            {
                P.Stop += dt; P.V = 0;
                if (P.Stop >= P.StopTime) { c.SetTyre(c.NextTyre); c.Warm = 0; c.Damage = 0; c.Stops++; c.BoxRequest = false; P.Phase = "out"; if (c.IsPlayer) Say("Go, go, go! " + Config.Tyres[(int)c.Tyre].Name + "s on.", "green"); }
            }
            else
            {
                float vt = P.P < T.PX0 ? Config.PitSpeed : 42;
                P.V += MathX.Clamp(vt - P.V, -10 * dt, (P.P < T.PX0 ? 8 : 12) * dt);
                if (P.P >= T.PX1)
                {
                    float x2, y2; T.PosF(P.P + .5f, PitLatAt(P.P + .5f, P.Lat0), out x2, out y2);
                    c.H = (float)Math.Atan2(y2 - c.Y, x2 - c.X); c.VX = (float)Math.Cos(c.H) * P.V; c.VY = (float)Math.Sin(c.H) * P.V; c.VF = P.V;
                    c.Pit = null; c.Recover = .8f; c.Lane = 0; Locate(c);
                    return;
                }
            }
            P.P += P.V * dt / Config.Step;
            float lat = PitLatAt(P.P, P.Lat0), x, y, xa, ya; T.PosF(P.P, lat, out x, out y); T.PosF(P.P + .6f, PitLatAt(P.P + .6f, P.Lat0), out xa, out ya);
            c.X = x; c.Y = y; if (P.V > .2f) c.H = (float)Math.Atan2(ya - y, xa - x);
            c.VX = (float)Math.Cos(c.H) * P.V; c.VY = (float)Math.Sin(c.H) * P.V; c.VF = P.V; c.VL = 0; c.Thr = .3f; c.Brk = 0; c.Steer = 0;
            Locate(c);
        }
        void MaybeEnterPit(Car c)
        {
            if (!c.BoxRequest || c.Pit != null || c.Retired || c.Finished || !Started) return;
            int rel = MathX.Mod(c.Idx - (T.N + T.PE0), T.N);
            if (rel < 4 && c.MaxLaps >= 0 && !(Mode == SessionMode.Race && c.MaxLaps >= Laps)) EnterPit(c, rel);
        }
        Compound ChooseTyre(Car c)
        {
            float left = 1 - MathX.Clamp(c.PF / (Laps * T.N), 0, 1);
            var t = left > .55f ? Compound.Hard : left > .28f ? Compound.Medium : Compound.Soft;
            if (c.Used.Count < 2 && t == c.Tyre) t = c.Tyre == Compound.Hard ? Compound.Medium : Compound.Hard;
            return t;
        }
        void AiStrategy(Car c)
        {
            if (c.Retired || c.Finished || c.Pit != null || c.BoxRequest || Mode != SessionMode.Race) return;
            int lapNow = c.MaxLaps + 1; if (lapNow < 1 || lapNow >= Laps) return;
            if (MathX.Mod(c.Idx - (T.N - 230), T.N) > 6 || c.StratLap == lapNow) return;
            c.StratLap = lapNow; bool need = c.Used.Count < 2; int left = Laps - lapNow; bool box = false;
            if (need && lapNow >= c.PlanLap) box = true;
            if (need && left <= 1) box = true;
            if (c.Wear > .82f && left >= 1) box = true;
            if (Neutral != null && Neutral.Type == "SC" && need && left >= 1) box = true;
            if (Neutral != null && Neutral.Type == "VSC" && need && left >= 1 && MathX.Rnd01() < .6f) box = true;
            if (c.Damage > .3f && left >= 1) box = true;
            if (box) { c.BoxRequest = true; c.NextTyre = ChooseTyre(c); }
        }
        public void ToggleBox()
        {
            var p = Player; if (p == null || Mode != SessionMode.Race || p.Pit != null || p.Finished) return;
            p.BoxRequest = !p.BoxRequest;
            Say(p.BoxRequest ? "Box this lap. " + Config.Tyres[(int)p.NextTyre].Name + "s ready." : "Stay out, stay out.", p.BoxRequest ? "" : "yellow");
        }

        // ---------------- neutralisations & incidents ----------------
        public void DeployVSC()
        {
            if (Neutral != null || Over) return;
            Neutral = new Neutral { Type = "VSC", Phase = "on", Clear = MathX.Rnd(14, 22) };
            foreach (var c in Cars) { c.VscDelta = .4f; c.OtActive = false; c.StraightMode = false; }
            Say("Virtual Safety Car. Slow down and keep the delta positive.", "yellow");
        }
        public void DeploySC()
        {
            if (Over || (Neutral != null && Neutral.Type == "SC")) return;
            Neutral = new Neutral { Type = "SC", Phase = "on", Clear = MathX.Rnd(28, 40) };
            Car lead = null; foreach (var c in Order()) if (!c.Retired && !c.Finished) { lead = c; break; }
            if (lead == null) return;
            SC = new SafetyCar { PF = lead.PF + 60, V = Math.Max(30, MathX.Hypot(lead.VX, lead.VY) * .8f) };
            foreach (var c in Cars) { c.OtActive = false; c.StraightMode = false; }
            Say("Safety Car, Safety Car. No overtaking. Box if you need tyres.", "yellow");
        }
        void EndNeutral()
        {
            Neutral = null; SC = null; GreenUntil = Time + 4;
            foreach (var c in Cars) if (c.Retired && c.Parked) c.Hidden = true;
            Say("Track is clear. Green flag.", "green");
        }
        void NeutralStep(float dt)
        {
            var N = Neutral; if (N == null) return; N.T += dt; int n = T.N;
            if (N.Type == "VSC")
            {
                if (N.Phase == "on" && N.T > N.Clear) { N.Phase = "ending"; N.EndAt = N.T + MathX.Rnd(10, 15); Say("VSC ending. Green in 10 to 15 seconds.", "yellow"); }
                else if (N.Phase == "ending" && N.T > N.EndAt) EndNeutral();
                return;
            }
            var S = SC; if (S == null) return;
            Car lead = null; foreach (var c in Order()) if (!c.Retired && !c.Finished && c.Pit == null) { lead = c; break; }
            if (N.Phase == "on" && N.T > N.Clear) { N.Phase = "in"; S.Lights = false; Say("Safety Car in this lap.", "yellow"); }
            if (!S.InPit)
            {
                int i = T.Wrap((int)Math.Floor(S.PF)); float v = MathX.Clamp(T.VP[i] * .72f, 22, 62);
                if (lead != null) { float gap = (S.PF - lead.PF) * Config.Step; if (gap > 140) v *= .6f; else if (gap < 25) v = Math.Min(v * 1.15f, 55); }
                S.V += MathX.Clamp(v - S.V, -10 * dt, 6 * dt);
                float rel = (((S.PF - (n + T.PE0)) % n) + n) % n;
                if (N.Phase == "in" && rel < 6 && N.T > N.Clear + 3) { S.InPit = true; S.PP = T.PE0 + rel; }
            }
            if (S.InPit)
            {
                S.V = Math.Max(Config.PitSpeed, S.V - 12 * dt); S.PP += S.V * dt / Config.Step; S.PF += S.V * dt / Config.Step;
                S.Lat = S.PP < T.PE1 ? MathX.Lerp(T.RL[T.Wrap((int)Math.Floor(S.PP))], T.PitLat, MathX.Smooth((S.PP - T.PE0) / (float)(T.PE1 - T.PE0))) : T.PitLat;
                if (S.PP > T.PE1 + 12 && !S.Gone) { S.Gone = true; N.Phase = "restart"; Say("Safety Car is in. Leader controls the restart.", "yellow"); }
            }
            else { S.PF += S.V * dt / Config.Step; S.Lat = T.RL[T.Wrap((int)Math.Floor(S.PF))]; }
            float x, y, x2, y2; T.PosF(S.PF, S.Lat, out x, out y); T.PosF(S.PF + .5f, S.Lat, out x2, out y2);
            S.X = x; S.Y = y; S.H = (float)Math.Atan2(y2 - y, x2 - x);
            if (N.Phase == "restart" && lead != null) { if (N.RestartLap == -999) N.RestartLap = lead.MaxLaps; if (lead.MaxLaps > N.RestartLap) EndNeutral(); }
        }
        void StartIncident(Car c)
        {
            float r = MathX.Rnd01(), corner = T.KC[c.Idx];
            if (r < .58f) { c.Spin = new SpinState { Rate = MathX.Rnd(3.5f, 6) * (MathX.Rnd01() < .5f ? -1 : 1) }; Say(c.Code + " has spun.", "yellow"); }
            else if (r < .84f)
            {
                c.Spin = new SpinState { Rate = MathX.Rnd(2, 4) * (corner > 0 ? 1 : -1), Crash = true };
                int s = corner > 0 ? -1 : 1; c.VX += T.NX[c.Idx] * s * c.VF * .45f; c.VY += T.NY[c.Idx] * s * c.VF * .45f;
                Say(c.Code + " is in the barrier.", "red");
            }
            else { c.MechT = 0; Say(c.Code + " reports a power unit problem.", "yellow"); }
            SetYellow(c.Idx, 1, 10);
        }
        void SpinStep(Car c, float dt)
        {
            var S = c.Spin; S.T += dt; float v = MathX.Hypot(c.VX, c.VY);
            if (v > .3f) { float k = Math.Max(0, v - 16 * dt) / v; c.VX *= k; c.VY *= k; } else { c.VX = c.VY = 0; }
            c.H = MathX.WrapAngle(c.H + S.Rate * dt * MathX.Clamp(v / 20, .15f, 1));
            c.X += c.VX * dt; c.Y += c.VY * dt; c.VF = v; c.VL = 0; c.Thr = 0; c.Brk = 1;
            Locate(c); Walls(c);
            bool onTrack = Math.Abs(c.D) < T.W / 2 + .5f; SetYellow(c.Idx, onTrack ? 2 : 1, 6);
            if (v < .4f)
            {
                S.StopT += dt;
                if (S.Crash && S.StopT > 1)
                {
                    c.Retired = true; c.RetiredAt = Time; c.Dnf = "Accident"; c.Parked = true; c.Spin = null; c.VX = c.VY = 0;
                    if (onTrack || T.StreetCircuit || MathX.Rnd01() < .55f) DeploySC(); else DeployVSC();
                    if (Neutral == null) SetYellow(c.Idx, 2, 25);
                }
                else if (!S.Crash && S.StopT > MathX.Rnd(1.8f, 3.5f)) { c.Spin = null; RecoverCar(c); c.Recover = 1.5f; }
            }
        }
        void MechStep(Car c, float dt)
        {
            c.MechT += dt; c.Thr = Math.Min(c.Thr, .25f);
            float side = c.D >= 0 ? 1 : -1; c.Lane = side * (T.W / 2 + 3) - T.RL[c.Idx];
            SetYellow(c.Idx, 1, 5);
            if (c.MechT > 5 && c.VF < .5f)
            {
                c.Retired = true; c.RetiredAt = Time; c.Dnf = "Power unit"; c.Parked = true; c.MechT = -1; c.VX = c.VY = 0;
                if (MathX.Rnd01() < .6f) DeployVSC(); else SetYellow(c.Idx, 2, 20);
            }
        }

        // ---------------- laps, detection, rules ----------------
        void Laps_(Car c)
        {
            int n = T.N, done = (int)Math.Floor((double)c.Prog / n);
            if (done > c.MaxLaps)
            {
                bool first = c.MaxLaps < 0; c.MaxLaps = done;
                if (!first)
                {
                    c.Sec[2] = Time - c.SecStart; float lt = Time - c.LapStart; c.LastLap = lt;
                    if (!c.Invalid && (c.BestLap == null || lt < c.BestLap)) c.BestLap = lt;
                    if (Mode == SessionMode.Race && done >= Laps && !c.Finished)
                    {
                        c.Finished = true; c.FinishTime = Time - RaceStart;
                        if (c.IsPlayer) { Over = true; c.IsAI = true; c.Skill = .8f; if (PlayerFinished != null) PlayerFinished(c); }
                    }
                }
                float start = Mode == SessionMode.Race && done == 0 ? RaceStart : Time;
                c.LapStart = start; c.SecStart = start; c.SecIdx = 0; c.Invalid = false;
                if (c.IsPlayer && Mode == SessionMode.Race && done == Laps - 1 && Laps > 1)
                    Say(c.Used.Count < 2 ? "Final lap. You have not used two compounds: you will be disqualified!" : "Final lap. Bring it home.", c.Used.Count < 2 ? "red" : "yellow");
            }
            if (c.MaxLaps >= 0)
            {
                int pos = MathX.Mod(c.Prog, n), s = pos < n / 3 ? 0 : pos < 2 * n / 3 ? 1 : 2;
                if (s > c.SecIdx) { c.Sec[c.SecIdx] = Time - c.SecStart; c.SecStart = Time; c.SecIdx = s; }
            }
        }
        void Detection(Car c)
        {
            int n = T.N, rel = MathX.Mod(MathX.Mod(c.Idx, n) - T.Det, n);
            if (rel > 4 || c.LastDet == c.MaxLaps) return;
            c.LastDet = c.MaxLaps; c.OtActive = false;
            if (Mode != SessionMode.Race || Neutral != null || c.MaxLaps < 1 || c.Pit != null) { c.OtArmed = false; return; }
            float dist; var a = CarAhead(c, out dist); float gap = a != null ? dist / Math.Max(15, Math.Abs(c.VF)) : 99;
            bool was = c.OtArmed; c.OtArmed = gap <= 1.0f && a != null && !a.Retired;
            if (c.IsPlayer && c.OtArmed && !was) Say("Within one second. Overtake available on the straight.", "green");
        }
        public void AddPenalty(Car c, float s, string why) { c.Penalty += s; if (c.IsPlayer) Say(s + " second time penalty: " + why + ".", "red"); }
        void PlayerRules(Car c, float dt)
        {
            if (Mode != SessionMode.Race || c.Pit != null || c.Finished || !Started) return;
            int n = T.N; var N = Neutral;
            if (!T.StreetCircuit)
            {
                bool off = Math.Abs(c.D) > T.W / 2 + 1f;
                if (off && !c.OffTrack)
                {
                    c.OffTrack = true; c.TrackLimitStrikes++;
                    if (c.TrackLimitStrikes <= 2) Say("Track limits. Warning " + c.TrackLimitStrikes + ".", "yellow");
                    else if (c.TrackLimitStrikes == 3) { Say("Black and white flag. Next one is a penalty.", "yellow"); BlackWhiteUntil = Time + 4; }
                    else AddPenalty(c, 5, "track limits");
                }
                else if (!off && Math.Abs(c.D) < T.W / 2) c.OffTrack = false;
            }
            bool caution = N != null || SectorYellow(c.Idx) > 0;
            foreach (var o in Cars)
            {
                if (o == c) continue;
                float rel = c.PF - o.PF; rel = ((rel % n) + n + n / 2f) % n - n / 2f;
                float prev; bool had = relMe.TryGetValue(o, out prev); relMe[o] = rel; if (!had) continue;
                if (prev < 0 && rel >= 0 && Math.Abs(rel) < 20)
                {
                    bool exempt = o.Pit != null || o.Retired || o.Spin != null || o.MechT >= 0 || o.Recover > 0;
                    if (caution && !exempt && c.PassedUnderCaution == null) { c.PassedUnderCaution = o; c.PassDeadline = Time + 8; Say("You passed " + o.Code + " under caution. Give the position back.", "red"); }
                }
                if (c.PassedUnderCaution == o && prev >= 0 && rel < 0) { c.PassedUnderCaution = null; Say("Position returned. Thanks.", "green"); }
            }
            if (c.PassedUnderCaution != null && Time > c.PassDeadline) { AddPenalty(c, 5, "overtaking under caution"); c.PassedUnderCaution = null; }
            if (N != null && N.Type == "VSC")
            {
                c.VscDelta += dt * (1 - Math.Max(0, c.VF) / VscRef(c.Idx)); c.VscDelta = Math.Min(c.VscDelta, 3);
                if (c.VscDelta < -1 && !N.PenaltyGiven) { N.PenaltyGiven = true; AddPenalty(c, 5, "VSC delta"); }
            }
        }
        readonly Dictionary<Car, float> relMe = new Dictionary<Car, float>();
        void BlueFlags()
        {
            int n = T.N; foreach (var o in Cars) o.Blue = false;
            if (Mode != SessionMode.Race) return;
            foreach (var c in Cars)
            {
                if (c.Retired || c.Pit != null) continue;
                foreach (var o in Cars) { if (o == c || o.Retired || o.Pit != null) continue; if (c.PF - o.PF > n * .6f) { float phys = (o.PF + n - c.PF) * Config.Step; if (phys > 0 && phys < 60) o.Blue = true; } }
            }
        }

        // ---------------- main update ----------------
        public void Update(float dt)
        {
            Time += dt;
            if (Mode == SessionMode.Race && !Started && Time >= LightsHold) { Started = true; RaceStart = Authority ? Time : LightsHold; Say("Lights out and away we go!", "green"); }
            if (Authority) NeutralStep(dt);
            float rate = Mode == SessionMode.TimeTrial || !Authority ? 0 : Mode == SessionMode.Attract ? Config.IncidentRate["real"] : Config.IncidentRate[Incidents];
            if ((int)(Time * 4) != (int)((Time - dt) * 4)) BlueFlags();
            foreach (var c in Cars)
            {
                if (c.Hidden || c.Kinematic) continue;
                if (c.Retired) { c.VX = c.VY = 0; if (c.Parked && Neutral == null && Time - c.RetiredAt > 25) c.Hidden = true; continue; }
                if (!Started) { if (c.IsAI) AiDrive(c, dt); else PlayerControl(c, dt); c.VX = c.VY = 0; continue; }
                if (c.Pit != null) { PitStep(c, dt); Laps_(c); continue; }
                if (c.Spin != null) { SpinStep(c, dt); continue; }
                if (c.IsAI)
                {
                    AiDrive(c, dt);
                    if (c.MechT >= 0) MechStep(c, dt);
                    else if (rate > 0 && !Over && !c.Finished && c.MaxLaps >= 0 && Math.Abs(T.KC[c.Idx]) > 1f / 150 && c.VF > 20)
                    {
                        float pPerM = rate / (T.N * Config.Step * T.CornerFrac);
                        if (MathX.Rnd01() < pPerM * c.VF * dt) StartIncident(c);
                    }
                }
                else PlayerControl(c, dt);
                AeroStep(c); Physics(c, dt); Locate(c);
                float hit = Walls(c);
                if (hit > 16 && c.IsPlayer) { c.Damage = Math.Min(1, c.Damage + hit / 40); SetYellow(c.Idx, 1, 8); Say(hit > 30 ? "Big impact! Front wing damage, box for a new nose." : "Contact with the wall. Front wing damaged.", "red"); }
                if (c.Recover > 0) c.Recover -= dt;
                if (!T.StreetCircuit && Math.Abs(c.D) > T.W / 2 + 45 && c.Pit == null) RecoverCar(c);
                if (c.IsAI) { float r2 = (c.PF - c.PrevPF) * Config.Step / dt; if (r2 < 2.5f && Neutral == null) c.Stuck += dt; else c.Stuck = Math.Max(0, c.Stuck - dt); if (c.Stuck > 3) RecoverCar(c); }
                c.PrevPF = c.PF;
                if (c.IsPlayer && Mode == SessionMode.TimeTrial && !c.Invalid && c.MaxLaps >= 0 && !T.StreetCircuit && Math.Abs(c.D) > T.W / 2 + 1.2f) { c.Invalid = true; Say("Track limits: lap deleted.", "red"); }
                if (c.IsPlayer) { c.Wrong = Math.Cos(c.H - T.Heading(c.Idx)) < -.3f && MathX.Hypot(c.VX, c.VY) > 4 && c.VF > 0; PlayerRules(c, dt); }
                Laps_(c); Detection(c); AiStrategy(c); MaybeEnterPit(c);
            }
            if (Started) for (int i = 0; i < Cars.Count; i++) for (int j = i + 1; j < Cars.Count; j++) Collide(Cars[i], Cars[j]);
        }

        // ---------------- classification ----------------
        public class ResultRow { public Car Car; public float Time; public string Status = ""; public int Pos; public int Points; }
        public List<ResultRow> Classification()
        {
            int n = T.N; float now = Time - RaceStart; var rows = new List<ResultRow>();
            foreach (var c in Cars)
            {
                var r = new ResultRow { Car = c };
                if (c.Retired) { r.Status = "DNF"; r.Time = 1e6f - c.PF; }
                else if (c.Finished) r.Time = c.FinishTime.Value + c.Penalty;
                else r.Time = now + (Laps * n - c.PF) * Config.Step / Config.GapSpeed + c.Penalty;
                if (!c.Retired && c.Used.Count < 2 && Laps >= 3) { r.Status = "DSQ"; r.Time = 2e6f - c.PF; }
                rows.Add(r);
            }
            rows.Sort((a, b) => a.Time.CompareTo(b.Time));
            int pos = 0; foreach (var r in rows) if (r.Status == "") { pos++; r.Pos = pos; r.Points = pos <= 10 ? Config.Points[pos - 1] : 0; }
            return rows;
        }
    }
}
