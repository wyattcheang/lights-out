// Lights Out (Unity): game flow, fixed-step simulation, rendering sync, online rooms and the HUD.
// Drop this on an empty GameObject (the editor menu "Lights Out > Set Up Scene" does it for you).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Services.Multiplayer;

namespace LightsOut
{
    public class GameController : MonoBehaviour
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
        bool busy; Vector2 menuScroll, trackScroll;

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
            _ = online.Init();
        }

        // ---------------- sessions ----------------
        void LoadTrack(int idx)
        {
            trackIdx = idx; track = Track.Build(tracks[idx]); builder.Build(track);
            bool night = track.NightRace;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = night ? 220 : 420; RenderSettings.fogEndDistance = night ? 1500 : 2800;
            RenderSettings.fogColor = night ? new Color(.12f, .16f, .25f) : new Color(.83f, .86f, .88f);
            sun.intensity = night ? .35f : 1.25f; sun.color = night ? new Color(.78f, .83f, 1f) : new Color(1f, .95f, .86f);
            RenderSettings.ambientIntensity = night ? .7f : 1f;
            rig.Cam.clearFlags = night ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox; rig.Cam.backgroundColor = new Color(.02f, .03f, .06f);
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
        void MakeView(Car c) { views[c] = CarView.Create(c.Code, Visuals.Hex(c.IsPlayer ? "#ffd21f" : c.Color), Visuals.Hex(c.IsPlayer ? "#16181c" : c.Accent), c.IsPlayer); }
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
            sun.transform.position = rig.Cam.transform.position;
            UpdateEffects(); UpdateTrackside();
        }

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
                var ep = new ParticleSystem.EmitParams { startColor = dirt ? (c.Surf == 3 ? new Color(.72f, .64f, .5f) : new Color(.42f, .5f, .3f)) : new Color(.86f, .86f, .88f), startSize = slide ? 1.4f : 1f, startLifetime = slide ? 2.2f : 1.3f };
                foreach (int s in new[] { -1, 1 }) { ep.position = pos + right * .85f * s; ep.velocity = new Vector3(c.VX * .25f, Random.Range(.3f, 1f), -c.VY * .25f); smoke.Emit(ep, 1); }
            }
        }
        void UpdateTrackside()
        {
            int lit = race.Mode == SessionMode.Race && !race.Started ? Mathf.Min(5, Mathf.FloorToInt(race.Time - .4f)) : 0;
            for (int k = 0; k < 5; k++) if (builder.GantryLamps[k]) Visuals.SetColor(builder.GantryLamps[k], k < lit ? new Color(1, .1f, .04f) : new Color(.16f, .04f, .04f));
            if (builder.PostPanels != null)
            {
                bool blink = ((int)(race.Time * 3)) % 2 == 0;
                for (int s = 0; s < builder.PostPanels.Length; s++)
                {
                    int lvl = race.Yel[s].Until > race.Time ? race.Yel[s].Level : 0; Color c = new Color(.07f, .07f, .07f);
                    if (race.Neutral != null) c = new Color(1, .82f, .12f); else if (lvl == 2) c = blink ? new Color(1, .82f, .12f) : c; else if (lvl == 1) c = new Color(1, .82f, .12f); else if (race.Time < race.GreenUntil) c = new Color(.13f, .87f, .33f);
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

        // ---------------- UI (IMGUI) ----------------
        GUIStyle panel, h1, h2, label, small, mono, btn, btnOn, big; bool stylesReady;
        void Styles()
        {
            if (stylesReady) return; stylesReady = true;
            var bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(.05f, .06f, .07f, .88f)); bg.Apply();
            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) }; panel.normal.background = bg;
            h1 = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold }; h1.normal.textColor = new Color(.95f, .93f, .89f);
            h2 = new GUIStyle(h1) { fontSize = 22 }; label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true }; label.normal.textColor = new Color(.9f, .9f, .88f);
            small = new GUIStyle(label) { fontSize = 12 }; small.normal.textColor = new Color(.6f, .62f, .66f);
            mono = new GUIStyle(label) { fontSize = 15 }; big = new GUIStyle(h1) { fontSize = 52, alignment = TextAnchor.MiddleRight };
            btn = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 32 }; btnOn = new GUIStyle(btn); btnOn.normal.textColor = new Color(1, .82f, .12f); btnOn.fontStyle = FontStyle.Bold;
        }
        bool Seg(string[] options, ref int sel)
        {
            int old = sel; GUILayout.BeginHorizontal();
            for (int i = 0; i < options.Length; i++) if (GUILayout.Button(options[i], i == sel ? btnOn : btn)) sel = i;
            GUILayout.EndHorizontal(); return old != sel;
        }

        void OnGUI()
        {
            Styles();
            switch (screen)
            {
                case Screen.Menu: DrawMenu(); break;
                case Screen.Prerace: DrawHud(); DrawPrerace(); break;
                case Screen.Race: DrawHud(); break;
                case Screen.Results: DrawHud(); DrawResults(); break;
                case Screen.Lobby: DrawLobby(); break;
            }
            if (Time.time < toastUntil) GUI.Label(new Rect(0, UnityEngine.Screen.height * .28f, UnityEngine.Screen.width, 60), toast, new GUIStyle(h2) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawMenu()
        {
            GUILayout.BeginArea(new Rect(16, 16, 400, UnityEngine.Screen.height - 32), panel);
            menuScroll = GUILayout.BeginScrollView(menuScroll);
            GUILayout.Label("LIGHTS OUT", h1);
            GUILayout.Label("Cockpit racing on 40 real circuits with measured elevation, 2026-style energy, tyre strategy, pit stops and race control.", small);
            GUILayout.Space(8);
            var t = tracks[trackIdx]; GUILayout.Label(t.city + ", " + t.country, h2);
            GUILayout.Label((t.len / 1000f).ToString("0.000") + " km · opened " + t.opened + (Config.Street.Contains(t.id) ? " · street" : "") + (Config.Night.Contains(t.id) ? " · night" : ""), small);
            GUILayout.Label("Session", small); Seg(new[] { "Race", "Time trial" }, ref modeIdx);
            int lapSel = laps == 3 ? 0 : laps == 5 ? 1 : 2; GUILayout.Label("Race distance", small); if (Seg(new[] { "3 laps", "5 laps", "10 laps" }, ref lapSel)) laps = new[] { 3, 5, 10 }[lapSel];
            int fieldSel = field == 6 ? 0 : field == 8 ? 1 : 2; GUILayout.Label("Grid size", small); if (Seg(new[] { "6", "8", "10" }, ref fieldSel)) field = new[] { 6, 8, 10 }[fieldSel];
            GUILayout.Label("Opponents", small); Seg(new[] { "Rookie", "Pro", "Ace" }, ref diffIdx);
            GUILayout.Label("Incidents", small); Seg(new[] { "Off", "Realistic", "Chaotic" }, ref incIdx);
            int cam = (int)rig.Mode; GUILayout.Label("Camera", small); if (Seg(new[] { "Cockpit", "Chase", "Top-down" }, ref cam)) rig.Mode = (CamMode)cam;
            GUILayout.Space(6);
            if (GUILayout.Button(modeIdx == 0 ? "START RACE" : "START TIME TRIAL", new GUIStyle(btnOn) { fixedHeight = 44, fontSize = 20 })) StartSingle();
            GUILayout.Space(10); GUILayout.Label("RACE ONLINE", small);
            GUILayout.BeginHorizontal(); GUILayout.Label("Name", small, GUILayout.Width(50)); nick = GUILayout.TextField(nick, 16); GUILayout.EndHorizontal();
            GUI.enabled = !busy;
            GUILayout.BeginHorizontal(); if (GUILayout.Button("Host public room", btn)) HostRoom(false); if (GUILayout.Button("Host private room", btn)) HostRoom(true); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); joinCode = GUILayout.TextField(joinCode, 8, GUILayout.Width(150)); if (GUILayout.Button("Join code", btn)) JoinRoom(joinCode, null); if (GUILayout.Button("Refresh", btn)) RefreshRooms(); GUILayout.EndHorizontal();
            foreach (var r in publicRooms) { GUILayout.BeginHorizontal(); GUILayout.Label(r.Name + "  (" + (r.MaxPlayers - r.AvailableSlots) + "/" + r.MaxPlayers + ")", label); if (GUILayout.Button("Join", btn, GUILayout.Width(70))) JoinRoom(null, r.Id); GUILayout.EndHorizontal(); }
            GUI.enabled = true;
            if (online.Status != "") GUILayout.Label(online.Status, small);
            GUILayout.Space(8);
            GUILayout.Label("Keys: arrows/WASD drive · B boost · O overtake · E energy map · P box · 1/2/3 next tyre · C camera · Esc pause. Gamepad: triggers, left stick, X boost.", small);
            GUILayout.Label("Circuit outlines: bacinger/f1-circuits (MIT). Elevation from public F1 timing telemetry. Unofficial fan project; not affiliated with Formula One Licensing B.V. or the FIA.", small);
            GUILayout.EndScrollView(); GUILayout.EndArea();
            // circuit list
            GUILayout.BeginArea(new Rect(UnityEngine.Screen.width - 316, 16, 300, UnityEngine.Screen.height - 32), panel);
            trackScroll = GUILayout.BeginScrollView(trackScroll);
            for (int i = 0; i < tracks.Length; i++)
            {
                if (i == 0) GUILayout.Label("CURRENT CALENDAR", small); if (i > 0 && tracks[i].cur != tracks[i - 1].cur) GUILayout.Label("PAST VENUES", small);
                if (GUILayout.Button(tracks[i].city + "  ·  " + tracks[i].country, i == trackIdx ? btnOn : btn)) { trackIdx = i; StartAttract(); }
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawPrerace()
        {
            float w = 620, h = 260; GUILayout.BeginArea(new Rect((UnityEngine.Screen.width - w) / 2, (UnityEngine.Screen.height - h) / 2, w, h), panel);
            var p = race.Player; bool isRace = race.Mode == SessionMode.Race;
            GUILayout.Label(track.Src.city + (isRace ? " · Grid" : " · Time trial"), h2);
            GUILayout.Label(isRace ? "You start P" + (race.Cars.IndexOf(p) + 1) + " of " + race.Cars.Count + " · " + race.Laps + " laps · two compounds required" : "Pick tyres for a flying lap", small);
            int lapsN = Mathf.Max(3, isRace ? race.Laps : 6); GUILayout.BeginHorizontal();
            for (int k = 0; k < 3; k++)
            {
                var ts = Config.Tyres[k];
                if (GUILayout.Button(ts.Name + "\n≈ " + (ts.Life * lapsN).ToString("0.0") + " laps · grip " + Mathf.RoundToInt(ts.Grip * 100) + "%", (int)startTyre == k ? new GUIStyle(btnOn) { fixedHeight = 70 } : new GUIStyle(btn) { fixedHeight = 70 })) startTyre = (Compound)k;
            }
            GUILayout.EndHorizontal(); GUILayout.Space(10);
            if (GUILayout.Button("Go to the grid", new GUIStyle(btnOn) { fixedHeight = 40 }))
            {
                p.Used.Clear(); p.Stints.Clear(); p.SetTyre(startTyre); p.NextTyre = startTyre == Compound.Hard ? Compound.Medium : Compound.Hard; screen = Screen.Race;
            }
            GUILayout.EndArea();
        }

        void DrawLobby()
        {
            float w = 640; GUILayout.BeginArea(new Rect((UnityEngine.Screen.width - w) / 2, 60, w, UnityEngine.Screen.height - 120), panel);
            var s = online.Session;
            GUILayout.Label("Room " + (s != null ? s.Code : "…"), h2);
            GUILayout.Label((net.IsHost ? "You are the host. " : "") + "Share the code with friends; public rooms also appear in everyone's room list.", small);
            if (s != null && GUILayout.Button("Copy code", btn, GUILayout.Width(120))) GUIUtility.systemCopyBuffer = s.Code;
            GUILayout.Space(6); GUILayout.Label("Drivers", small);
            foreach (var p in net.Players) GUILayout.Label(p.Name + (p.Id == net.LocalId ? " (you)" : "") + "   " + Config.Tyres[p.Tyre].Name + "   " + (p.Ready ? "Ready" : "Not ready"), label);
            GUILayout.Label("Your starting tyre", small); int ty = net.MyTyre; if (Seg(new[] { "Soft", "Medium", "Hard" }, ref ty)) { net.MyTyre = (byte)ty; net.SendHello(); }
            if (net.IsHost)
            {
                GUILayout.Label("Circuit: " + tracks[net.Cfg.Track].city, small);
                GUILayout.BeginHorizontal(); if (GUILayout.Button("◀", btn, GUILayout.Width(40))) { net.Cfg.Track = (net.Cfg.Track + tracks.Length - 1) % tracks.Length; net.BroadcastLobby(); } if (GUILayout.Button("▶", btn, GUILayout.Width(40))) { net.Cfg.Track = (net.Cfg.Track + 1) % tracks.Length; net.BroadcastLobby(); } GUILayout.EndHorizontal();
                int ls = net.Cfg.Laps == 3 ? 0 : net.Cfg.Laps == 5 ? 1 : 2; GUILayout.Label("Laps", small); if (Seg(new[] { "3", "5", "10" }, ref ls)) { net.Cfg.Laps = new[] { 3, 5, 10 }[ls]; net.BroadcastLobby(); }
                int ai = net.Cfg.Ai == 0 ? 0 : net.Cfg.Ai == 3 ? 1 : 2; GUILayout.Label("AI cars", small); if (Seg(new[] { "0", "3", "6" }, ref ai)) { net.Cfg.Ai = new[] { 0, 3, 6 }[ai]; net.BroadcastLobby(); }
                int inc = System.Array.IndexOf(Incs, net.Cfg.Incidents); GUILayout.Label("Incidents", small); if (Seg(new[] { "Off", "Realistic", "Chaotic" }, ref inc)) { net.Cfg.Incidents = Incs[inc]; net.BroadcastLobby(); }
                GUILayout.Space(8); if (GUILayout.Button("START RACE", new GUIStyle(btnOn) { fixedHeight = 42 })) net.HostStart();
            }
            else
            {
                GUILayout.Label("Circuit: " + tracks[net.Cfg.Track].city + " · " + net.Cfg.Laps + " laps · " + net.Cfg.Ai + " AI", small);
                if (GUILayout.Button(net.MyReady ? "Not ready" : "Ready", btnOn)) { net.MyReady = !net.MyReady; net.SendHello(); }
            }
            if (GUILayout.Button("Leave room", btn)) LeaveOnline();
            GUILayout.EndArea();
        }

        void DrawResults()
        {
            float w = 640; GUILayout.BeginArea(new Rect((UnityEngine.Screen.width - w) / 2, 80, w, UnityEngine.Screen.height - 160), panel);
            var rows = race.Classification(); var me = rows.Find(r => r.Car.IsPlayer);
            GUILayout.Label(me != null && me.Status == "DSQ" ? "DISQUALIFIED" : me != null && me.Pos == 1 ? "RACE WINNER" : "P" + (me != null ? me.Pos : 0) + " FINISH", h2);
            float win = rows[0].Time;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; string time = r.Status != "" ? r.Status + (r.Car.Dnf != null ? " · " + r.Car.Dnf : "") : i == 0 ? MathX.FormatTime(r.Time) : "+" + (r.Time - win).ToString("0.000");
                GUILayout.Label((r.Pos > 0 ? r.Pos.ToString() : "–").PadRight(4) + r.Car.Code.PadRight(6) + time.PadRight(22) + string.Join(">", r.Car.Stints.Select(x => x.ToString().Substring(0, 1))).PadRight(10) + MathX.FormatTime(r.Car.BestLap).PadRight(12) + (r.Points > 0 ? r.Points + " pts" : ""), mono);
            }
            if (net.InRace) { if (net.IsHost && GUILayout.Button("Back to room", btnOn)) net.HostBackToLobby(); if (GUILayout.Button("Leave room", btn)) LeaveOnline(); }
            else { if (GUILayout.Button("Race again", btnOn)) StartSingle(); if (GUILayout.Button("Menu", btn)) { screen = Screen.Menu; StartAttract(); } }
            GUILayout.EndArea();
        }

        void DrawHud()
        {
            var p = race.Player; float W = UnityEngine.Screen.width, H = UnityEngine.Screen.height;
            // timing tower
            var order = race.Order(); int n = track.N;
            GUILayout.BeginArea(new Rect(12, 12, 250, 46 + order.Count * 27), panel);
            var lead = order[0]; GUILayout.Label("LAP " + Mathf.Min(race.Laps, Mathf.Max(1, lead.MaxLaps + 1)) + "/" + race.Laps, small);
            for (int i = 0; i < order.Count; i++)
            {
                var c = order[i]; string gap = i == 0 ? "Leader" : c.Retired ? "DNF" : "+" + ((order[i - 1].PF - c.PF) * Config.Step / Config.GapSpeed).ToString("0.0");
                string tag = c.Pit != null ? " PIT" : c.Penalty > 0 ? " +" + c.Penalty : c.OtActive ? " OT" : "";
                GUILayout.Label((i + 1).ToString().PadRight(3) + c.Code.PadRight(5) + gap.PadRight(8) + Config.Tyres[(int)c.Tyre].Name.Substring(0, 1) + tag, c.IsPlayer ? new GUIStyle(mono) { normal = { textColor = new Color(1, .82f, .12f) } } : mono);
            }
            GUILayout.EndArea();
            // flags
            string flag = race.Neutral != null ? (race.Neutral.Type == "SC" ? (race.Neutral.Phase == "in" ? "SAFETY CAR IN THIS LAP" : "SAFETY CAR") : (race.Neutral.Phase == "ending" ? "VSC ENDING" : "VIRTUAL SAFETY CAR")) : p != null && p.Blue ? "BLUE FLAG" : p != null && race.SectorYellow(p.Idx) == 2 ? "DOUBLE YELLOW" : p != null && race.SectorYellow(p.Idx) == 1 ? "YELLOW FLAG" : race.Time < race.BlackWhiteUntil ? "BLACK AND WHITE FLAG" : race.Time < race.GreenUntil ? "GREEN FLAG" : "";
            if (flag != "") GUI.Label(new Rect(W / 2 - 220, 14, 440, 40), flag, new GUIStyle(h2) { alignment = TextAnchor.MiddleCenter, normal = { textColor = flag.StartsWith("GREEN") ? new Color(.2f, .85f, .45f) : flag.StartsWith("BLUE") ? new Color(.3f, .55f, 1f) : new Color(1, .82f, .12f) } });
            if (race.Mode == SessionMode.Race && !race.Started) { int lit = Mathf.Min(5, Mathf.FloorToInt(race.Time - .4f)); GUI.Label(new Rect(W / 2 - 150, 60, 300, 40), new string('●', Mathf.Max(0, lit)) + new string('○', 5 - Mathf.Max(0, lit)), new GUIStyle(h2) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, .2f, .15f) } }); }
            if (p == null) return;
            // lap box
            GUILayout.BeginArea(new Rect(W - 212, 12, 200, 100), panel);
            GUILayout.Label("LAP " + (race.Mode == SessionMode.Race ? Mathf.Min(race.Laps, Mathf.Max(1, p.MaxLaps + 1)) + "/" + race.Laps : Mathf.Max(1, p.MaxLaps + 1).ToString()), h2);
            GUILayout.Label(MathX.FormatTime(race.Mode == SessionMode.Race ? (p.Finished ? p.FinishTime : (race.Started ? race.Time - race.RaceStart : 0)) : (p.MaxLaps >= 0 ? race.Time - p.LapStart : 0)), mono);
            GUILayout.Label("Last " + MathX.FormatTime(p.LastLap) + (race.Neutral != null && race.Neutral.Type == "VSC" ? "   VSC Δ " + p.VscDelta.ToString("+0.0;-0.0") : ""), small);
            GUILayout.EndArea();
            // dash
            GUILayout.BeginArea(new Rect(W / 2 - 220, H - 120, 440, 108), panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Mathf.RoundToInt(Mathf.Abs(p.VF) * 3.6f).ToString(), big, GUILayout.Width(130));
            GUILayout.Label(Gear(p), new GUIStyle(h1) { normal = { textColor = new Color(1, .82f, .12f) } }, GUILayout.Width(40));
            GUILayout.BeginVertical();
            GUILayout.Label("Battery · " + (p.RechargeMode ? "Recharge" : "Balanced") + "   " + p.Soc.ToString("0.0") + " MJ", small);
            var r = GUILayoutUtility.GetRect(200, 10); GUI.DrawTexture(r, Texture2D.grayTexture); GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(p.Soc / Config.ErsCap), r.height), Bar(p.OtActive ? new Color(.18f, .84f, .45f) : p.Boost && p.Dep > 0 ? new Color(1, .82f, .12f) : new Color(.23f, .63f, 1f)));
            GUILayout.Label((p.StraightMode ? "STRAIGHT MODE" : "Corner mode") + "   " + (p.Boost && p.Dep > 0 ? "BOOST" : "") + "   " + (p.OtActive ? "OVERTAKE ON" : p.OtArmed ? "OVERTAKE READY" : ""), small);
            GUILayout.EndVertical(); GUILayout.EndHorizontal(); GUILayout.EndArea();
            // tyres & pit
            GUILayout.BeginArea(new Rect(12, H - 120, 260, 108), panel);
            GUILayout.Label(Config.Tyres[(int)p.Tyre].Name + " · life " + Mathf.RoundToInt(Mathf.Max(0, 1 - p.Wear) * 100) + "%", label);
            GUILayout.Label("Next: " + Config.Tyres[(int)p.NextTyre].Name + "   " + (p.Pit != null ? (p.Pit.Phase == "stop" ? "Stationary " + Mathf.Max(0, p.Pit.StopTime - p.Pit.Stop).ToString("0.0") + " s" : "Pit limiter 80") : p.BoxRequest ? "BOX THIS LAP" : "Box: P"), small);
            GUILayout.Label(race.Mode == SessionMode.Race && p.Used.Count < 2 ? "Mandatory stop: use a 2nd compound" : p.Damage > .05f ? "Front wing damage " + Mathf.RoundToInt(p.Damage * 100) + "%" : "Stops " + p.Stops, small);
            GUILayout.EndArea();
            // radio
            float ry = H - 140 - radio.Count * 26;
            foreach (var m in radio) { if (Time.time - m.Key < 7) GUI.Label(new Rect(12, ry, 420, 24), "RADIO  " + m.Value, label); ry += 26; }
            // minimap
            if (builder.MapTexture)
            {
                var mr = new Rect(W - 172, H - 172, 160, 160); GUI.DrawTexture(mr, builder.MapTexture);
                float k = 160f / builder.MapSize;
                foreach (var c in race.Cars) { if (c.Hidden) continue; var mp = builder.MapPoint(c.X, c.Y); GUI.DrawTexture(new Rect(mr.x + mp.x * k - 3, mr.y + mp.y * k - 3, 6, 6), Bar(c.IsPlayer ? new Color(1, .82f, .12f) : Visuals.Hex(c.Color))); }
            }
            if (paused) GUI.Label(new Rect(0, H / 2 - 30, W, 60), "PAUSED  (Esc)", new GUIStyle(h1) { alignment = TextAnchor.MiddleCenter });
        }
        static string Gear(Car p)
        {
            if (p.Pit != null) return "P"; if (p.VF < -.5f) return "R"; if (p.VF < .5f) return "N";
            for (int g = 1; g < Config.GearTops.Length; g++) if (p.VF < Config.GearTops[g] || g == Config.GearTops.Length - 1) return g.ToString();
            return "8";
        }
        readonly Dictionary<Color, Texture2D> bars = new Dictionary<Color, Texture2D>();
        Texture2D Bar(Color c) { Texture2D t; if (!bars.TryGetValue(c, out t)) { t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); bars[c] = t; } return t; }
    }
}
