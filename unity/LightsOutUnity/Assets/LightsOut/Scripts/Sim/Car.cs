using System.Collections.Generic;

namespace LightsOut
{
    public class PitState
    {
        public float P;          // position along the pit path (index units, relative to the line)
        public string Phase;     // in, lane, stop, out
        public float Lat0, V, Stop, StopTime, Served;
    }

    public class SpinState { public float T, StopT, Rate; public bool Crash; }

    /// One car in the simulation. Plane coordinates x (east), y (south); heading h in radians.
    public class Car
    {
        public string Code = "YOU", Name = "", Color = "#ffd21f", Accent = "#16181c";
        public bool IsPlayer, IsAI, Kinematic, Remote;
        public ulong NetId;      // network client id for remote humans
        public int AiIndex = -1, Garage;
        public float Skill = 1f;
        // dynamics
        public float X, Y, H, VX, VY, VF, VL, Steer, Thr, Brk, Load = 1f;
        public bool Handbrake;
        public int Idx, Prog; public float PF, D; public int Surf;
        public float Lane, LaneBase;
        // timing
        public int MaxLaps = -1, SecIdx, StratLap = -9, LastDet = -100, PlanLap = 2;
        public float LapStart, SecStart; public float?[] Sec = new float?[3];
        public float? LastLap, BestLap, FinishTime;
        public bool Finished, Invalid;
        // energy & aero (2026)
        public float Soc = Config.ErsCap * .9f, SocCap = Config.ErsCap, Dep, Harv;
        public bool RechargeMode, Boost, OtArmed, OtActive, StraightMode; public int AeroZone = -1;
        // tyres, pit, damage, rules
        public Compound Tyre = Compound.Medium, NextTyre = Compound.Hard;
        public float Wear, Warm = 1f, Damage;
        public HashSet<Compound> Used = new HashSet<Compound>();
        public List<Compound> Stints = new List<Compound>();
        public int Stops, TrackLimitStrikes; public bool OffTrack;
        public float Penalty, PenaltyServed;
        public PitState Pit; public bool BoxRequest;
        // incidents
        public bool Retired, Parked, Hidden, Blue; public string Dnf;
        public SpinState Spin; public float MechT = -1f, RetiredAt;
        public float Recover, Stuck, PrevPF; public bool Blocked, Wrong;
        public float VscDelta;
        public Car PassedUnderCaution; public float PassDeadline;

        public void SetTyre(Compound c) { Tyre = c; Used.Add(c); Stints.Add(c); Wear = 0f; }
    }
}
