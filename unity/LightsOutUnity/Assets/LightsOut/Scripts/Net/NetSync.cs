// Race networking over Netcode for GameObjects named messages (no NetworkObjects or prefabs needed).
// Host-authoritative race control; each client simulates its own car and streams it to the host,
// which rebroadcasts every car plus flags / VSC / Safety Car at 30 Hz.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LightsOut
{
    public struct NetCar
    {
        public float X, Y, H, VF, Steer, PF, FinishTime, Penalty, BestLap;
        public byte Flags, Tyre, Stops; public short MaxLaps;
        public static NetCar From(Car c)
        {
            byte f = 0;
            if (c.Brk > .3f) f |= 1; if (c.StraightMode) f |= 2; if (c.Pit != null) f |= 4; if (c.Retired) f |= 8; if (c.Finished) f |= 16;
            if (c.Harv > 0 && c.Thr > .9f) f |= 32; if (c.OtActive) f |= 64; if (c.Boost && c.Dep > 0) f |= 128;
            return new NetCar { X = c.X, Y = c.Y, H = c.H, VF = c.VF, Steer = c.Steer, PF = c.PF, FinishTime = c.FinishTime ?? -1f, Penalty = c.Penalty, BestLap = c.BestLap ?? -1f, Flags = f, Tyre = (byte)c.Tyre, Stops = (byte)c.Stops, MaxLaps = (short)c.MaxLaps };
        }
        public void Write(FastBufferWriter w)
        {
            w.WriteValueSafe(X); w.WriteValueSafe(Y); w.WriteValueSafe(H); w.WriteValueSafe(VF); w.WriteValueSafe(Steer); w.WriteValueSafe(PF);
            w.WriteValueSafe(FinishTime); w.WriteValueSafe(Penalty); w.WriteValueSafe(BestLap); w.WriteValueSafe(Flags); w.WriteValueSafe(Tyre); w.WriteValueSafe(Stops); w.WriteValueSafe(MaxLaps);
        }
        public static NetCar Read(FastBufferReader r)
        {
            var c = new NetCar();
            r.ReadValueSafe(out c.X); r.ReadValueSafe(out c.Y); r.ReadValueSafe(out c.H); r.ReadValueSafe(out c.VF); r.ReadValueSafe(out c.Steer); r.ReadValueSafe(out c.PF);
            r.ReadValueSafe(out c.FinishTime); r.ReadValueSafe(out c.Penalty); r.ReadValueSafe(out c.BestLap); r.ReadValueSafe(out c.Flags); r.ReadValueSafe(out c.Tyre); r.ReadValueSafe(out c.Stops); r.ReadValueSafe(out c.MaxLaps);
            return c;
        }
    }

    [Serializable] public class RoomConfig { public int Track = 2, Laps = 5, Ai = 3; public string Difficulty = "pro", Incidents = "real"; }
    public class RoomPlayer { public ulong Id; public string Name; public byte Tyre = 1; public bool Ready; }
    public struct GridEntry { public bool Ai; public ulong Id; }

    public class NetSync
    {
        public bool Active, IsHost, InRace;
        public ulong LocalId;
        public RoomConfig Cfg = new RoomConfig();
        public List<RoomPlayer> Players = new List<RoomPlayer>();
        public List<GridEntry> Grid = new List<GridEntry>();
        public float LightsHold;
        // snapshot buffers: key = grid slot
        public readonly Dictionary<int, List<KeyValuePair<float, NetCar>>> Buffers = new Dictionary<int, List<KeyValuePair<float, NetCar>>>();
        public float HostTime, HostTimeAt; public string NeutralType = "", NeutralPhase = "";
        public bool ScOn, ScLights; public float ScX, ScY, ScH, ScPF; public byte[] Yellow = new byte[0];
        public event Action<RoomConfig, List<GridEntry>, float> RaceStarted;
        public event Action BackToLobby;
        public event Action<string> HostLeft;
        public string MyName = "Driver"; public byte MyTyre = 1; public bool MyReady;
        bool registered; float sendAcc;

        NetworkManager NM { get { return NetworkManager.Singleton; } }

        public void Poll()
        {
            if (NM == null) return;
            if (!registered && NM.IsListening)
            {
                registered = true; Active = true; IsHost = NM.IsServer; LocalId = NM.LocalClientId;
                var cm = NM.CustomMessagingManager;
                cm.RegisterNamedMessageHandler("lo.hello", OnHello); cm.RegisterNamedMessageHandler("lo.lobby", OnLobby);
                cm.RegisterNamedMessageHandler("lo.start", OnStart); cm.RegisterNamedMessageHandler("lo.car", OnCar);
                cm.RegisterNamedMessageHandler("lo.snap", OnSnap); cm.RegisterNamedMessageHandler("lo.back", OnBack);
                NM.OnClientDisconnectCallback += OnDisconnect;
                if (IsHost) { Players.Clear(); Players.Add(new RoomPlayer { Id = LocalId, Name = MyName, Tyre = MyTyre, Ready = true }); NM.OnClientConnectedCallback += id => BroadcastLobby(); }
                SendHello();
            }
            if (registered && !NM.IsListening) { Shutdown(); if (HostLeft != null) HostLeft("Disconnected from the room."); }
        }

        public void Shutdown()
        {
            if (registered && NM != null && NM.CustomMessagingManager != null)
                foreach (var n in new[] { "lo.hello", "lo.lobby", "lo.start", "lo.car", "lo.snap", "lo.back" }) NM.CustomMessagingManager.UnregisterNamedMessageHandler(n);
            registered = false; Active = false; InRace = false; Players.Clear(); Buffers.Clear();
        }

        // ---- lobby ----
        public void SendHello()
        {
            if (!registered) return;
            if (IsHost) { var me = Players.Find(p => p.Id == LocalId); if (me != null) { me.Name = MyName; me.Tyre = MyTyre; } BroadcastLobby(); return; }
            using (var w = new FastBufferWriter(256, Allocator.Temp)) { w.WriteValueSafe(MyName); w.WriteValueSafe(MyTyre); w.WriteValueSafe(MyReady); NM.CustomMessagingManager.SendNamedMessage("lo.hello", NetworkManager.ServerClientId, w, NetworkDelivery.Reliable); }
        }
        void OnHello(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            string name; byte tyre; bool ready; r.ReadValueSafe(out name); r.ReadValueSafe(out tyre); r.ReadValueSafe(out ready);
            var p = Players.Find(x => x.Id == sender); if (p == null) { p = new RoomPlayer { Id = sender }; Players.Add(p); }
            p.Name = name; p.Tyre = tyre; p.Ready = ready; BroadcastLobby();
        }
        public void BroadcastLobby()
        {
            if (!IsHost || !registered) return;
            using (var w = new FastBufferWriter(2048, Allocator.Temp))
            {
                WriteCfg(w); w.WriteValueSafe(Players.Count);
                foreach (var p in Players) { w.WriteValueSafe(p.Id); w.WriteValueSafe(p.Name ?? ""); w.WriteValueSafe(p.Tyre); w.WriteValueSafe(p.Ready); }
                NM.CustomMessagingManager.SendNamedMessageToAll("lo.lobby", w, NetworkDelivery.ReliableSequenced);
            }
        }
        void OnLobby(ulong sender, FastBufferReader r)
        {
            if (IsHost) return;
            ReadCfg(r); int n; r.ReadValueSafe(out n); Players.Clear();
            for (int i = 0; i < n; i++) { var p = new RoomPlayer(); r.ReadValueSafe(out p.Id); r.ReadValueSafe(out p.Name); r.ReadValueSafe(out p.Tyre); r.ReadValueSafe(out p.Ready); Players.Add(p); }
        }
        void WriteCfg(FastBufferWriter w) { w.WriteValueSafe(Cfg.Track); w.WriteValueSafe(Cfg.Laps); w.WriteValueSafe(Cfg.Ai); w.WriteValueSafe(Cfg.Difficulty); w.WriteValueSafe(Cfg.Incidents); }
        void ReadCfg(FastBufferReader r) { r.ReadValueSafe(out Cfg.Track); r.ReadValueSafe(out Cfg.Laps); r.ReadValueSafe(out Cfg.Ai); r.ReadValueSafe(out Cfg.Difficulty); r.ReadValueSafe(out Cfg.Incidents); }

        // ---- race start / end ----
        public void HostStart()
        {
            if (!IsHost) return;
            var humans = new List<ulong>(); foreach (var p in Players) humans.Add(p.Id);
            var rng = new System.Random(); for (int i = humans.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = humans[i]; humans[i] = humans[j]; humans[j] = t; }
            Grid.Clear(); int nAi = Math.Min(Cfg.Ai, 10 - humans.Count);
            for (int i = 0; i < nAi; i++) Grid.Add(new GridEntry { Ai = true, Id = (ulong)i });
            foreach (var h in humans) Grid.Add(new GridEntry { Ai = false, Id = h });
            LightsHold = 4.6f + (float)rng.NextDouble() * 1.2f;
            using (var w = new FastBufferWriter(1024, Allocator.Temp))
            {
                WriteCfg(w); w.WriteValueSafe(LightsHold); w.WriteValueSafe(Grid.Count);
                foreach (var g in Grid) { w.WriteValueSafe(g.Ai); w.WriteValueSafe(g.Id); }
                NM.CustomMessagingManager.SendNamedMessageToAll("lo.start", w, NetworkDelivery.ReliableSequenced);
            }
            InRace = true; Buffers.Clear();
            if (RaceStarted != null) RaceStarted(Cfg, Grid, LightsHold);
        }
        void OnStart(ulong sender, FastBufferReader r)
        {
            if (IsHost) return;
            ReadCfg(r); r.ReadValueSafe(out LightsHold); int n; r.ReadValueSafe(out n); Grid.Clear();
            for (int i = 0; i < n; i++) { var g = new GridEntry(); r.ReadValueSafe(out g.Ai); r.ReadValueSafe(out g.Id); Grid.Add(g); }
            InRace = true; Buffers.Clear(); HostTime = 0; HostTimeAt = Time.realtimeSinceStartup;
            if (RaceStarted != null) RaceStarted(Cfg, Grid, LightsHold);
        }
        public void HostBackToLobby()
        {
            if (!IsHost) return;
            using (var w = new FastBufferWriter(8, Allocator.Temp)) { w.WriteValueSafe((byte)1); NM.CustomMessagingManager.SendNamedMessageToAll("lo.back", w, NetworkDelivery.ReliableSequenced); }
            InRace = false; if (BackToLobby != null) BackToLobby();
        }
        void OnBack(ulong sender, FastBufferReader r) { if (IsHost) return; InRace = false; if (BackToLobby != null) BackToLobby(); }
        void OnDisconnect(ulong id)
        {
            if (IsHost) { Players.RemoveAll(p => p.Id == id); BroadcastLobby(); }
            else if (id == NetworkManager.ServerClientId || id == LocalId) { if (HostLeft != null) HostLeft("The host left the room."); }
        }

        // ---- in race ----
        public void Tick(RaceSession race, float dt)
        {
            if (!registered || !InRace || race == null) return;
            sendAcc += dt; if (sendAcc < 1f / 30f) return; sendAcc = 0;
            if (IsHost)
            {
                using (var w = new FastBufferWriter(1400, Allocator.Temp))
                {
                    w.WriteValueSafe(race.Time);
                    var N = race.Neutral; w.WriteValueSafe(N != null ? N.Type : ""); w.WriteValueSafe(N != null ? N.Phase : "");
                    bool sc = race.SC != null && !race.SC.Gone; w.WriteValueSafe(sc);
                    if (sc) { w.WriteValueSafe(race.SC.X); w.WriteValueSafe(race.SC.Y); w.WriteValueSafe(race.SC.H); w.WriteValueSafe(race.SC.Lights); w.WriteValueSafe(race.SC.PF); }
                    w.WriteValueSafe(race.Yel.Length); foreach (var y in race.Yel) w.WriteValueSafe((byte)(y.Until > race.Time ? y.Level : 0));
                    w.WriteValueSafe(race.Cars.Count); foreach (var c in race.Cars) NetCar.From(c).Write(w);
                    NM.CustomMessagingManager.SendNamedMessageToAll("lo.snap", w, NetworkDelivery.UnreliableSequenced);
                }
            }
            else if (race.Player != null)
            {
                using (var w = new FastBufferWriter(128, Allocator.Temp)) { NetCar.From(race.Player).Write(w); NM.CustomMessagingManager.SendNamedMessage("lo.car", NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced); }
            }
        }
        void OnCar(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            var nc = NetCar.Read(r); int slot = Grid.FindIndex(g => !g.Ai && g.Id == sender); if (slot < 0) return;
            Push(slot, nc);
        }
        void OnSnap(ulong sender, FastBufferReader r)
        {
            if (IsHost) return;
            r.ReadValueSafe(out HostTime); HostTimeAt = Time.realtimeSinceStartup;
            r.ReadValueSafe(out NeutralType); r.ReadValueSafe(out NeutralPhase);
            r.ReadValueSafe(out ScOn); if (ScOn) { r.ReadValueSafe(out ScX); r.ReadValueSafe(out ScY); r.ReadValueSafe(out ScH); r.ReadValueSafe(out ScLights); r.ReadValueSafe(out ScPF); }
            int ny; r.ReadValueSafe(out ny); if (Yellow.Length != ny) Yellow = new byte[ny]; for (int i = 0; i < ny; i++) r.ReadValueSafe(out Yellow[i]);
            int n; r.ReadValueSafe(out n); for (int i = 0; i < n; i++) Push(i, NetCar.Read(r));
        }
        void Push(int slot, NetCar c)
        {
            List<KeyValuePair<float, NetCar>> b; if (!Buffers.TryGetValue(slot, out b)) { b = new List<KeyValuePair<float, NetCar>>(); Buffers[slot] = b; }
            b.Add(new KeyValuePair<float, NetCar>(Time.realtimeSinceStartup, c)); if (b.Count > 12) b.RemoveAt(0);
        }

        /// Interpolated state ~110 ms in the past for a kinematic car.
        public bool Sample(int slot, out NetCar a, out NetCar b, out float f)
        {
            a = b = default(NetCar); f = 0; List<KeyValuePair<float, NetCar>> buf;
            if (!Buffers.TryGetValue(slot, out buf) || buf.Count == 0) return false;
            float rt = Time.realtimeSinceStartup - .11f; int i = buf.Count - 1; while (i > 0 && buf[i - 1].Key > rt) i--;
            if (i == 0) { a = b = buf[0].Value; return true; }
            var A = buf[i - 1]; var B = buf[i];
            if (B.Key <= rt) { a = b = B.Value; return true; }
            a = A.Value; b = B.Value; f = (rt - A.Key) / Mathf.Max(.001f, B.Key - A.Key); return true;
        }
    }
}
