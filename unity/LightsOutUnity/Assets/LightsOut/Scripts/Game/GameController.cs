// Lights Out (Unity): game flow, fixed-step simulation, rendering sync and online rooms. The menus and HUD are in GameController.Ui.cs.
// Drop this on an empty GameObject (the editor menu "Lights Out > Set Up Scene" does it for you).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Services.Multiplayer;

namespace LightsOut
{
    public partial class GameController : MonoBehaviour
    {
        enum Screen { Menu, Prerace, Race, Results, Lobby }
        Screen screen = Screen.Menu;
        TrackJson[] tracks;
        int trackIdx = 2, laps = 5, field = 8, diffIdx = 1, incIdx = 1, modeIdx = 0;
        Compound startTyre = Compound.Medium;
        static readonly string[] Diffs = { "rookie", "pro", "ace" }, Incs = { "off", "real", "chaos" };

        RaceSession race; Track track; readonly TrackBuilder builder = new TrackBuilder();
        readonly Dictionary<Car, CarView> views = new Dictionary<Car, CarView>();
        CameraRig rig; readonly InputReader input = new InputReader();
        Light sun; ParticleSystem smoke; GameObject scCar; Material[] scLamps;
        float acc; readonly List<KeyValuePair<float, string>> radio = new List<KeyValuePair<float, string>>();
        float toastUntil; string toast = "";

        // online
        readonly OnlineService online = new OnlineService(); readonly NetSync net = new NetSync();
        List<ISessionInfo> publicRooms = new List<ISessionInfo>(); string joinCode = "", nick = "";
        bool busy;

        void Start()
        {
            var txt = Resources.Load<TextAsset>("tracks");
            tracks = JsonUtility.FromJson<TrackList>(txt.text).tracks;
            var cam = Camera.main; if (cam == null) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
            rig = new CameraRig(cam);
            sun = FindAnyObjectByType<Light>(); if (sun == null) { var lg = new GameObject("Sun"); sun = lg.AddComponent<Light>(); sun.type = LightType.Directional; }
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            BuildSmoke();
            nick = "Driver" + Random.Range(100, 999);
            net.RaceStarted += OnNetRaceStarted; net.BackToLobby += () => { screen = Screen.Lobby; StartAttract(); }; net.HostLeft += msg => { Toast(msg); LeaveOnline(); };
            StartAttract();
            BuildUi();
            _ = online.Init();
        }

        // ---------------- sessions ----------------
        void LoadTrack(int idx)
        {
            trackIdx = idx; track = Track.Build(tracks[idx]);
            Look.Apply(track.NightRace, sun, rig.Cam); builder.Build(track);
        }
        void NewRace(SessionMode mode, int field_, Compound tyre)
        {
            foreach (var v in views.Values) if (v) Destroy(v.gameObject); views.Clear();
            race = new RaceSession { Laps = laps, Difficulty = Diffs[diffIdx], Incidents = Incs[incIdx] };
            race.Radio += (m, tone) => { radio.Add(new KeyValuePair<float, string>(Time.time, m)); if (radio.Count > 3) radio.RemoveAt(0); };
            race.PlayerFinished += c => Invoke("ShowResults", 1.8f);
            race.Setup(track, mode, field_, tyre);
            foreach (var c in race.Cars) MakeView(c);
        }
        static readonly int[] RivalNumbers = { 5, 11, 23, 31, 44, 63, 77, 81, 16 };
        void MakeView(Car c)
        {
            int rival = System.Array.FindIndex(Config.Rivals, r => r[0] == c.Code);
            int number = c.IsPlayer ? 1 : rival >= 0 ? RivalNumbers[rival] : 20 + race.Cars.IndexOf(c);
            views[c] = CarView.Create(c.Code, Visuals.Hex(c.IsPlayer ? "#ffd21f" : c.Color), Visuals.Hex(c.IsPlayer ? "#16181c" : c.Accent), c.IsPlayer, number);
        }
        void StartAttract() { if (track == null || track.Src != tracks[trackIdx]) LoadTrack(trackIdx); NewRace(SessionMode.Attract, 8, Compound.Medium); }
        void StartSingle()
        {
            if (track.Src != tracks[trackIdx]) LoadTrack(trackIdx);
            NewRace(modeIdx == 0 ? SessionMode.Race : SessionMode.TimeTrial, field, startTyre);
            screen = Screen.Prerace;
        }
        void ShowResults() { if (race != null && race.Mode == SessionMode.Race) screen = Screen.Results; }

        void OnNetRaceStarted(RoomConfig cfg, List<GridEntry> grid, float lightsHold)
        {
            laps = cfg.Laps; diffIdx = System.Array.IndexOf(Diffs, cfg.Difficulty); incIdx = System.Array.IndexOf(Incs, cfg.Incidents);
            LoadTrack(cfg.Track);
            foreach (var v in views.Values) if (v) Destroy(v.gameObject); views.Clear();
            race = new RaceSession { Laps = laps, Difficulty = cfg.Difficulty, Incidents = cfg.Incidents, Authority = net.IsHost };
            race.Radio += (m, tone) => { radio.Add(new KeyValuePair<float, string>(Time.time, m)); if (radio.Count > 3) radio.RemoveAt(0); };
            race.PlayerFinished += c => Invoke("ShowResults", 1.8f);
            race.Setup(track, SessionMode.Race, 0, Compound.Medium);
            race.Cars.Clear(); race.LightsHold = lightsHold;
            for (int s = 0; s < grid.Count; s++)
            {
                var g = grid[s]; var c = race.NewCar(race.GridPose(s)); c.Garage = s;
                if (g.Ai)
                {
                    var riv = Config.Rivals[(int)g.Id % Config.Rivals.Length]; c.IsAI = true; c.Code = riv[0]; c.Color = riv[1]; c.Accent = riv[2]; c.AiIndex = (int)g.Id;
                    c.Skill = Mathf.Clamp(Config.Difficulty[cfg.Difficulty] - s * .004f, .7f, 1f); c.SetTyre(new[] { Compound.Medium, Compound.Soft, Compound.Medium, Compound.Hard }[(int)g.Id % 4]);
                    c.NextTyre = c.Tyre == Compound.Hard ? Compound.Medium : Compound.Hard; if (!net.IsHost) c.Kinematic = true;
                }
                else if (g.Id == net.LocalId) { c.IsPlayer = true; c.SetTyre((Compound)net.MyTyre); c.NextTyre = net.MyTyre == 2 ? Compound.Medium : Compound.Hard; race.Player = c; }
                else
                {
                    var p = net.Players.Find(x => x.Id == g.Id); var pal = Config.Rivals[(s + 3) % Config.Rivals.Length];
                    c.Kinematic = true; c.Remote = true; c.NetId = g.Id; c.Name = p != null ? p.Name : "Driver"; c.Code = (c.Name.ToUpperInvariant() + "XXX").Substring(0, 3); c.Color = pal[1]; c.Accent = pal[2];
                    c.SetTyre(p != null ? (Compound)p.Tyre : Compound.Medium);
                }
                race.Cars.Add(c);
            }
            foreach (var c in race.Cars) MakeView(c);
            screen = Screen.Race;
        }

        // ---------------- frame ----------------
        void Update()
        {
            net.Poll();
            if (race == null) return;
            HandleKeys();
            if (screen != Screen.Prerace && !paused)
            {
                if (race.Player != null && !race.Player.IsAI) race.Input = input.Read(Time.deltaTime);
                if (net.InRace && !net.IsHost) SyncGuestClock();
                acc += Mathf.Min(net.InRace ? 1f : .05f, Time.deltaTime);
                while (acc >= 1f / 120) { foreach (var c in race.Cars) if (c.Kinematic) Kin(c); race.Update(1f / 120); acc -= 1f / 120; }
                net.Tick(race, Time.deltaTime);
            }
            var focus = race.Player ?? race.Cars.Where(c => !c.Retired).OrderByDescending(c => c.PF).FirstOrDefault() ?? race.Cars[0];
            foreach (var kv in views) kv.Value.Sync(kv.Key, track, Time.deltaTime, kv.Key == focus && rig.Mode == CamMode.Cockpit && race.Mode != SessionMode.Attract, race.Time);
            rig.Follow(focus, track, Time.deltaTime, race.Mode == SessionMode.Attract);
            CarView fv; Look.FollowCar(views.TryGetValue(focus, out fv) && fv ? fv.transform : null, focus.IsPlayer);
            nearCars.Clear();
            if (Look.Night)
            {
                var camPos = rig.Cam.transform.position;
                foreach (var kv in views.Where(kv => kv.Key != focus && kv.Value && kv.Value.gameObject.activeSelf).OrderBy(kv => (kv.Value.transform.position - camPos).sqrMagnitude).Take(3)) nearCars.Add(kv.Value.transform);
            }
            Look.LightNearbyCars(nearCars);
            sun.transform.position = rig.Cam.transform.position;
            UpdateEffects(); UpdateTrackside(); UpdateUi();
        }

        readonly List<Transform> nearCars = new List<Transform>();
        bool paused;
        void HandleKeys()
        {
            var kb = Keyboard.current; if (kb == null || screen != Screen.Race) return;
            if (kb.escapeKey.wasPressedThisFrame && !net.InRace) paused = !paused;
            if (kb.cKey.wasPressedThisFrame) { rig.Mode = (CamMode)(((int)rig.Mode + 1) % 3); Toast(rig.Mode.ToString()); }
            var p = race.Player; if (p == null) return;
            if (kb.pKey.wasPressedThisFrame) race.ToggleBox();
            if (kb.oKey.wasPressedThisFrame && !race.ActivateOvertake(p)) Toast("Overtake not available");
            if (kb.eKey.wasPressedThisFrame) { p.RechargeMode = !p.RechargeMode; Toast(p.RechargeMode ? "Recharge mode" : "Balanced mode"); }
            if (kb.digit1Key.wasPressedThisFrame) p.NextTyre = Compound.Soft;
            if (kb.digit2Key.wasPressedThisFrame) p.NextTyre = Compound.Medium;
            if (kb.digit3Key.wasPressedThisFrame) p.NextTyre = Compound.Hard;
            if (kb.rKey.wasPressedThisFrame && p.Pit == null) race.RecoverCar(p);
        }

        void SyncGuestClock()
        {
            float est = net.HostTime + (Time.realtimeSinceStartup - net.HostTimeAt), drift = est - race.Time;
            race.Time += Mathf.Abs(drift) > 2 ? drift : Mathf.Clamp(drift, -.03f, .03f);
            if (net.NeutralType != "") { if (race.Neutral == null || race.Neutral.Type != net.NeutralType) { race.Neutral = new Neutral { Type = net.NeutralType }; foreach (var c in race.Cars) c.VscDelta = .4f; } race.Neutral.Phase = net.NeutralPhase; }
            else if (race.Neutral != null) { race.Neutral = null; race.GreenUntil = race.Time + 4; }
            if (net.ScOn) { if (race.SC == null) race.SC = new SafetyCar(); race.SC.X = net.ScX; race.SC.Y = net.ScY; race.SC.H = net.ScH; race.SC.Lights = net.ScLights; race.SC.PF = net.ScPF; race.SC.Gone = false; }
            else if (race.SC != null) race.SC.Gone = true;
            for (int i = 0; i < net.Yellow.Length && i < race.Yel.Length; i++) if (net.Yellow[i] > 0) { race.Yel[i].Level = net.Yellow[i]; race.Yel[i].Until = race.Time + 1; }
        }
        void Kin(Car c)
        {
            int slot = race.Cars.IndexOf(c); NetCar a, b; float f;
            if (!net.Sample(slot, out a, out b, out f)) return;
            c.X = Mathf.Lerp(a.X, b.X, f); c.Y = Mathf.Lerp(a.Y, b.Y, f); c.H = a.H + MathX.WrapAngle(b.H - a.H) * f; c.VF = Mathf.Lerp(a.VF, b.VF, f); c.Steer = b.Steer;
            c.VX = Mathf.Cos(c.H) * c.VF; c.VY = Mathf.Sin(c.H) * c.VF;
            c.Brk = (b.Flags & 1) != 0 ? 1 : 0; c.Thr = 1 - c.Brk; c.StraightMode = (b.Flags & 2) != 0; c.OtActive = (b.Flags & 64) != 0; c.Harv = (b.Flags & 32) != 0 ? .2f : 0;
            c.Pit = (b.Flags & 4) != 0 ? (c.Pit ?? new PitState { Phase = "lane" }) : null;
            c.Tyre = (Compound)b.Tyre; c.PF = b.PF; c.Prog = Mathf.FloorToInt(b.PF); c.Idx = track.Wrap(c.Prog);
            c.D = (c.X - track.X[c.Idx]) * track.NX[c.Idx] + (c.Y - track.Y[c.Idx]) * track.NY[c.Idx];
            c.MaxLaps = b.MaxLaps; c.Finished = (b.Flags & 16) != 0; c.FinishTime = b.FinishTime < 0 ? (float?)null : b.FinishTime; c.Penalty = b.Penalty; c.BestLap = b.BestLap < 0 ? (float?)null : b.BestLap; c.Stops = b.Stops;
            c.Used.Add(c.Tyre); if (c.Stints.Count == 0 || c.Stints[c.Stints.Count - 1] != c.Tyre) c.Stints.Add(c.Tyre);
            if ((b.Flags & 8) != 0 && !c.Retired) { c.Retired = true; c.Parked = true; c.Dnf = "Retired"; c.RetiredAt = race.Time; }
        }

        // ---------------- effects & trackside ----------------
        void BuildSmoke()
        {
            var go = new GameObject("TyreSmoke"); smoke = go.AddComponent<ParticleSystem>();
            var main = smoke.main; main.loop = false; main.playOnAwake = false; main.maxParticles = 800; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = 1.6f;
            var em = smoke.emission; em.enabled = false;
            var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .6f, 1, 3f));
            var col = smoke.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.45f, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
            var r = smoke.GetComponent<ParticleSystemRenderer>(); var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit"));
            // soft alpha-blended puffs; the shader's default is an opaque, untextured quad
            m.mainTexture = Visuals.Puff; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Visuals.Puff);
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_Mode", 2); m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", 1); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
            m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000;
            r.material = m;
        }
        void UpdateEffects()
        {
            foreach (var c in race.Cars)
            {
                if (c.Hidden || (c.Retired && c.Spin == null)) continue;
                float v = Mathf.Abs(c.VF); bool lockup = c.Brk > .9f && v > 26 && c.Pit == null, slide = Mathf.Abs(c.VL) > 3.2f || c.Spin != null;
                bool dirt = c.Surf >= 2 && v > 7;
                if (!(lockup || slide || dirt) || Random.value > .5f) continue;
                var y = track.ElevAt(c.PF); var fwd = new Vector3(Mathf.Cos(c.H), 0, -Mathf.Sin(c.H)); var right = new Vector3(-fwd.z, 0, fwd.x);
                var pos = Visuals.World(c.X, c.Y, y + .25f) + fwd * (lockup && !slide ? 1.55f : -1.8f);
                float sk = Look.Pick(1f, .2f); var ep = new ParticleSystem.EmitParams { startColor = (dirt ? (c.Surf == 3 ? new Color(.72f, .64f, .5f) : new Color(.42f, .5f, .3f)) : new Color(.86f, .86f, .88f)) * new Color(sk, sk, sk * 1.3f, 1), startSize = slide ? 1.4f : 1f, startLifetime = slide ? 2.2f : 1.3f };
                foreach (int s in new[] { -1, 1 }) { ep.position = pos + right * .85f * s; ep.velocity = new Vector3(c.VX * .25f, Random.Range(.3f, 1f), -c.VY * .25f); smoke.Emit(ep, 1); }
            }
        }
        void UpdateTrackside()
        {
            int lit = race.Mode == SessionMode.Race && !race.Started ? Mathf.Min(5, Mathf.FloorToInt(race.Time - .4f)) : 0;
            for (int k = 0; k < 5; k++) if (builder.GantryLamps[k]) Visuals.SetColor(builder.GantryLamps[k], k < lit ? Look.Hdr(new Color(1, .1f, .04f), 3.5f / Look.Glow) : new Color(.16f, .04f, .04f));
            if (builder.PostPanels != null)
            {
                bool blink = ((int)(race.Time * 3)) % 2 == 0;
                for (int s = 0; s < builder.PostPanels.Length; s++)
                {
                    int lvl = race.Yel[s].Until > race.Time ? race.Yel[s].Level : 0; Color c = new Color(.07f, .07f, .07f);
                    if (race.Neutral != null) c = Look.Amber; else if (lvl == 2) c = blink ? Look.Amber : c; else if (lvl == 1) c = Look.Amber; else if (race.Time < race.GreenUntil) c = new Color(.13f, .87f, .33f);
                    Visuals.SetColor(builder.PostPanels[s], c);
                }
            }
            if (race.SC != null && !race.SC.Gone)
            {
                if (!scCar) BuildScCar();
                scCar.SetActive(true); scCar.transform.position = Visuals.World(race.SC.X, race.SC.Y, track.ElevAt(race.SC.PF)); scCar.transform.rotation = Visuals.Yaw(race.SC.H);
                bool on = race.SC.Lights && ((int)(race.Time * 5)) % 2 == 0; Visuals.SetColor(scLamps[0], race.SC.Lights ? (on ? new Color(1, .7f, 0) : new Color(.3f, .2f, 0)) : new Color(.1f, .1f, .1f)); Visuals.SetColor(scLamps[1], race.SC.Lights ? new Color(.1f, .1f, .1f) : new Color(.13f, .8f, .4f));
            }
            else if (scCar) scCar.SetActive(false);
        }
        void BuildScCar()
        {
            scCar = new GameObject("SafetyCar"); var silver = Visuals.Mat("scsilver", new Color(.78f, .8f, .82f), null, .85f, .7f);
            System.Func<Vector3, Vector3, Material, GameObject> box = (p, s, m) => { var g = new GameObject("p"); g.transform.SetParent(scCar.transform, false); g.transform.localPosition = p; g.transform.localScale = s; g.AddComponent<MeshFilter>().sharedMesh = Visuals.Cube; g.AddComponent<MeshRenderer>().sharedMaterial = m; return g; };
            box(new Vector3(0, .55f, 0), new Vector3(1.9f, .55f, 4.6f), silver); box(new Vector3(0, 1.0f, -.4f), new Vector3(1.7f, .45f, 2.2f), Visuals.Mat("glass", new Color(.05f, .06f, .08f), null, .95f, .6f));
            scLamps = new[] { new Material(Visuals.Unlit("scA", Color.yellow)), new Material(Visuals.Unlit("scB", Color.green)) };
            box(new Vector3(-.3f, 1.3f, -.4f), new Vector3(.55f, .1f, .3f), scLamps[0]); box(new Vector3(.3f, 1.3f, -.4f), new Vector3(.55f, .1f, .3f), scLamps[1]);
            foreach (float z in new[] { 1.45f, -1.45f }) foreach (float x in new[] { -.86f, .86f }) { var w = box(new Vector3(x, .34f, z), new Vector3(.68f, .14f, .68f), Visuals.Mat("tyre", new Color(.07f, .07f, .07f))); w.GetComponent<MeshFilter>().sharedMesh = Visuals.Cylinder; w.transform.localRotation = Quaternion.Euler(0, 0, 90); }
        }

        // ---------------- online ----------------
        async void HostRoom(bool priv)
        {
            busy = true; net.MyName = nick;
            var name = nick + " · " + tracks[trackIdx].city + " · " + laps + " laps";
            if (await online.Host(priv, name)) { net.Cfg = new RoomConfig { Track = trackIdx, Laps = laps, Ai = 3, Difficulty = Diffs[diffIdx], Incidents = Incs[incIdx] }; screen = Screen.Lobby; }
            busy = false;
        }
        async void JoinRoom(string code, string id)
        {
            busy = true; net.MyName = nick;
            bool ok = id != null ? await online.JoinById(id) : await online.JoinByCode(code);
            if (ok) screen = Screen.Lobby; busy = false;
        }
        async void RefreshRooms() { busy = true; publicRooms = await online.ListPublic(); busy = false; }
        async void LeaveOnline()
        {
            net.Shutdown(); await online.Leave(); screen = Screen.Menu; StartAttract();
        }

        void Toast(string s) { toast = s; toastUntil = Time.time + 1.4f; }

        static string Gear(Car p)
        {
            if (p.Pit != null) return "P"; if (p.VF < -.5f) return "R"; if (p.VF < .5f) return "N";
            for (int g = 1; g < Config.GearTops.Length; g++) if (p.VF < Config.GearTops[g] || g == Config.GearTops.Length - 1) return g.ToString();
            return "8";
        }
    }
}
