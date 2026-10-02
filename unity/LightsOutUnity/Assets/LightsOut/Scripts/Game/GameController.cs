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
            UpdateEffects(); UpdateTrackside();
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

        // ---------------- UI (IMGUI, drawn with UiKit) ----------------
        GUIStyle h2, label, small, note, btn, btnOn, btnPrimary, btnList, btnListOn, card, cardOn, textBox; bool stylesReady;
        // the UI is laid out on a virtual 1080-pixel-high canvas and scaled to the window
        const float UiH = 1080f; static float UiW { get { return UnityEngine.Screen.width * UiH / UnityEngine.Screen.height; } }
        static void States(GUIStyle s, Texture2D idle, Texture2D hot, Color text, Color textHot)
        {
            s.normal.background = idle; s.hover.background = hot; s.active.background = hot; s.focused.background = hot;
            s.normal.textColor = text; s.hover.textColor = textHot; s.active.textColor = textHot; s.focused.textColor = textHot;
        }
        void Styles()
        {
            if (stylesReady) return; stylesReady = true;
            Color cy = Look.Cyan, mg = Look.Magenta, text = UiKit.Text, none = new Color(0, 0, 0, 0);
            h2 = new GUIStyle { font = UiKit.Head, fontSize = 30, fontStyle = FontStyle.Bold }; h2.normal.textColor = text;
            label = new GUIStyle { font = UiKit.Head, fontSize = 18, wordWrap = true, richText = true }; label.normal.textColor = text;
            small = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold, margin = new RectOffset(2, 0, 12, 4) }; small.normal.textColor = UiKit.Dim;
            note = new GUIStyle(label) { fontSize = 14 }; note.normal.textColor = UiKit.Faint;
            btn = new GUIStyle { font = UiKit.Head, fontSize = 18, fixedHeight = 38, alignment = TextAnchor.MiddleCenter, border = new RectOffset(11, 11, 11, 11), margin = new RectOffset(3, 3, 3, 3), padding = new RectOffset(12, 12, 0, 0), richText = true };
            States(btn, UiKit.Round9(new Color(1, 1, 1, .075f), none), UiKit.Round9(new Color(1, 1, 1, .17f), none), text, Color.white);
            btnOn = new GUIStyle(btn) { fontStyle = FontStyle.Bold };
            States(btnOn, UiKit.Round9(new Color(cy.r, cy.g, cy.b, .95f), none), UiKit.Round9(new Color(.55f, .95f, 1f, 1f), none), UiKit.Ink, UiKit.Ink);
            btnPrimary = new GUIStyle(btn) { fontSize = 26, fixedHeight = 60, fontStyle = FontStyle.Bold, margin = new RectOffset(3, 3, 14, 6) };
            States(btnPrimary, UiKit.Round9(mg, none, 12, new Color(.48f, .26f, 1f)), UiKit.Round9(new Color(1f, .42f, .88f), none, 12, new Color(.62f, .42f, 1f)), Color.white, Color.white);
            btnList = new GUIStyle(btn) { alignment = TextAnchor.MiddleLeft, fixedHeight = 34, fontSize = 17, margin = new RectOffset(0, 6, 1, 1), padding = new RectOffset(14, 8, 0, 0) };
            States(btnList, UiKit.Round9(new Color(1, 1, 1, .025f), none, 7), UiKit.Round9(new Color(1, 1, 1, .13f), none, 7), text, Color.white);
            btnListOn = new GUIStyle(btnList) { fontStyle = FontStyle.Bold };
            var sel = UiKit.Round9(new Color(mg.r, mg.g, mg.b, .5f), none, 7, new Color(mg.r, mg.g, mg.b, .08f)); States(btnListOn, sel, sel, Color.white, Color.white);
            card = new GUIStyle(btn) { fixedHeight = 132, alignment = TextAnchor.LowerCenter, padding = new RectOffset(8, 8, 8, 14), fontSize = 17 };
            States(card, UiKit.Round9(new Color(1, 1, 1, .06f), new Color(1, 1, 1, .1f), 12), UiKit.Round9(new Color(1, 1, 1, .14f), new Color(1, 1, 1, .2f), 12), text, Color.white);
            cardOn = new GUIStyle(card); var con = UiKit.Round9(new Color(cy.r, cy.g, cy.b, .16f), cy, 12); States(cardOn, con, con, Color.white, Color.white);
            textBox = new GUIStyle(GUI.skin.textField) { font = UiKit.Head, fontSize = 18, fixedHeight = 36, border = new RectOffset(11, 11, 11, 11), padding = new RectOffset(12, 12, 0, 0), margin = new RectOffset(3, 3, 3, 3), alignment = TextAnchor.MiddleLeft };
            States(textBox, UiKit.Round9(new Color(0, 0, 0, .35f), new Color(1, 1, 1, .16f)), UiKit.Round9(new Color(0, 0, 0, .45f), cy), text, Color.white);
            // slim scrollbars without arrow buttons
            var sb = GUI.skin.verticalScrollbar; sb.fixedWidth = 6; sb.normal.background = UiKit.Round9(new Color(1, 1, 1, .05f), none, 3); sb.border = new RectOffset(3, 3, 3, 3);
            var th = GUI.skin.verticalScrollbarThumb; th.fixedWidth = 6; th.border = new RectOffset(3, 3, 3, 3); States(th, UiKit.Round9(new Color(1, 1, 1, .28f), none, 3), UiKit.Round9(new Color(1, 1, 1, .5f), none, 3), text, text);
            GUI.skin.verticalScrollbarUpButton.fixedHeight = 0; GUI.skin.verticalScrollbarDownButton.fixedHeight = 0;
        }
        bool Seg(string[] options, ref int sel)
        {
            int old = sel; GUILayout.BeginHorizontal();
            for (int i = 0; i < options.Length; i++) if (GUILayout.Button(options[i], i == sel ? btnOn : btn)) sel = i;
            GUILayout.EndHorizontal(); return old != sel;
        }
        static Color TyreColor(Compound c) { var t = Config.Tyres[(int)c]; return new Color(t.R, t.G, t.B); }
        static string TyreLetter(Compound c) { return Config.Tyres[(int)c].Name.Substring(0, 1); }

        void OnGUI()
        {
            Styles();
            float ui = UnityEngine.Screen.height / UiH; GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1));
            switch (screen)
            {
                case Screen.Menu: DrawMenu(); break;
                case Screen.Prerace: DrawHud(); DrawPrerace(); break;
                case Screen.Race: DrawHud(); break;
                case Screen.Results: DrawHud(); DrawResults(); break;
                case Screen.Lobby: DrawLobby(); break;
            }
            if (Time.time < toastUntil) { var tr = new Rect(UiW / 2 - 200, UiH * .26f, 400, 48); UiKit.Fill(tr, UiKit.Glass, 24); UiKit.Label(tr, toast.ToUpperInvariant(), 20, UiKit.Text, TextAnchor.MiddleCenter, false, true); }
        }

        void DrawMenu()
        {
            var left = new Rect(36, 36, 470, UiH - 72); UiKit.Panel(left, 18, .88f);
            GUILayout.BeginArea(new Rect(left.x + 28, left.y + 20, left.width - 44, left.height - 40));
            menuScroll = GUILayout.BeginScrollView(menuScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(left.width - 66));
            var wr = GUILayoutUtility.GetRect(380, 96);
            UiKit.Label(new Rect(wr.x, wr.y, wr.width, 78), "LIGHTS <color=#FF2BD6>OUT</color>", 74, UiKit.Text, TextAnchor.MiddleLeft, true, true);
            UiKit.Accent(new Rect(wr.x, wr.y + 82, 320, 3), Look.Cyan);
            GUILayout.Label("Cockpit racing on 40 real circuits with measured elevation, 2026-style energy, tyre strategy, pit stops and race control.", note);
            GUILayout.Space(10);
            // circuit card with the track outline
            var t = tracks[trackIdx]; var cr = GUILayoutUtility.GetRect(380, 132);
            UiKit.Fill(cr, new Color(1, 1, 1, .055f), 12); UiKit.Fill(new Rect(cr.x, cr.y + 16, 3, cr.height - 32), Look.Cyan, 1.5f);
            UiKit.Label(new Rect(cr.x + 20, cr.y + 14, cr.width - 150, 38), t.city.ToUpperInvariant(), 32, UiKit.Text, TextAnchor.MiddleLeft, false, true);
            UiKit.Label(new Rect(cr.x + 20, cr.y + 50, cr.width - 150, 22), t.country.ToUpperInvariant(), 15, UiKit.Dim, TextAnchor.MiddleLeft, false, true);
            UiKit.Label(new Rect(cr.x + 20, cr.y + 78, 120, 34), (t.len / 1000f).ToString("0.000"), 28, UiKit.Text, TextAnchor.MiddleLeft, true);
            UiKit.Label(new Rect(cr.x + 104, cr.y + 86, 40, 22), "KM", 13, UiKit.Dim, TextAnchor.MiddleLeft, false, true);
            float px = cr.x + 146; UiKit.Pill(new Rect(px, cr.y + 84, 58, 22), t.opened.ToString(), new Color(1, 1, 1, .1f), UiKit.Text); px += 64;
            if (Config.Street.Contains(t.id)) { UiKit.Pill(new Rect(px, cr.y + 84, 66, 22), "STREET", new Color(1, 1, 1, .1f), UiKit.Text); px += 72; }
            if (Config.Night.Contains(t.id)) UiKit.Pill(new Rect(px, cr.y + 84, 58, 22), "NIGHT", Look.Magenta, Color.white);
            if (builder.MapTexture) UiKit.Tex(new Rect(cr.xMax - 136, cr.y + 2, 128, 128), builder.MapTexture, Color.white);

            GUILayout.Label("SESSION", small); Seg(new[] { "Race", "Time trial" }, ref modeIdx);
            int lapSel = laps == 3 ? 0 : laps == 5 ? 1 : 2; GUILayout.Label("RACE DISTANCE", small); if (Seg(new[] { "3 laps", "5 laps", "10 laps" }, ref lapSel)) laps = new[] { 3, 5, 10 }[lapSel];
            int fieldSel = field == 6 ? 0 : field == 8 ? 1 : 2; GUILayout.Label("GRID SIZE", small); if (Seg(new[] { "6", "8", "10" }, ref fieldSel)) field = new[] { 6, 8, 10 }[fieldSel];
            GUILayout.Label("OPPONENTS", small); Seg(new[] { "Rookie", "Pro", "Ace" }, ref diffIdx);
            GUILayout.Label("INCIDENTS", small); Seg(new[] { "Off", "Realistic", "Chaotic" }, ref incIdx);
            int cam = (int)rig.Mode; GUILayout.Label("CAMERA", small); if (Seg(new[] { "Cockpit", "Chase", "Top-down" }, ref cam)) rig.Mode = (CamMode)cam;
            if (GUILayout.Button(modeIdx == 0 ? "START RACE" : "START TIME TRIAL", btnPrimary)) StartSingle();

            GUILayout.Label("RACE ONLINE", small);
            GUILayout.BeginHorizontal(); nick = GUILayout.TextField(nick, 16, textBox); GUILayout.EndHorizontal();
            GUI.enabled = !busy;
            GUILayout.BeginHorizontal(); if (GUILayout.Button("Host public room", btn)) HostRoom(false); if (GUILayout.Button("Host private room", btn)) HostRoom(true); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); joinCode = GUILayout.TextField(joinCode, 8, textBox, GUILayout.Width(150)); if (GUILayout.Button("Join code", btn)) JoinRoom(joinCode, null); if (GUILayout.Button("Refresh", btn)) RefreshRooms(); GUILayout.EndHorizontal();
            foreach (var r in publicRooms) { GUILayout.BeginHorizontal(); GUILayout.Label(r.Name + "  (" + (r.MaxPlayers - r.AvailableSlots) + "/" + r.MaxPlayers + ")", label); if (GUILayout.Button("Join", btn, GUILayout.Width(70))) JoinRoom(null, r.Id); GUILayout.EndHorizontal(); }
            GUI.enabled = true;
            if (online.Status != "") GUILayout.Label(online.Status, note);
            GUILayout.Space(10);
            GUILayout.Label("Keys: arrows/WASD drive · B boost · O overtake · E energy map · P box · 1/2/3 next tyre · C camera · Esc pause. Gamepad: triggers, left stick, X boost.", note);
            GUILayout.Label("Circuit outlines: bacinger/f1-circuits (MIT). Car model: \"F1 2026 concept\" by Qvist_designs (CC BY 4.0), simplified. Elevation from public F1 timing telemetry. Unofficial fan project; not affiliated with Formula One Licensing B.V. or the FIA.", note);
            GUILayout.EndVertical(); GUILayout.EndScrollView(); GUILayout.EndArea();

            // circuit list
            var right = new Rect(UiW - 36 - 340, 36, 340, UiH - 72); UiKit.Panel(right, 18, .88f);
            GUILayout.BeginArea(new Rect(right.x + 16, right.y + 12, right.width - 26, right.height - 24));
            trackScroll = GUILayout.BeginScrollView(trackScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            for (int i = 0; i < tracks.Length; i++)
            {
                if (i == 0) GUILayout.Label("CURRENT CALENDAR", small); if (i > 0 && tracks[i].cur != tracks[i - 1].cur) GUILayout.Label("PAST VENUES", small);
                if (GUILayout.Button(tracks[i].city + "  <color=#8a96a6><size=14>" + tracks[i].country.ToUpperInvariant() + "</size></color>", i == trackIdx ? btnListOn : btnList)) { trackIdx = i; StartAttract(); }
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawPrerace()
        {
            float w = 760, h = 336; var box = new Rect((UiW - w) / 2, (UiH - h) / 2 - 60, w, h); UiKit.Panel(box, 18, .93f);
            var p = race.Player; bool isRace = race.Mode == SessionMode.Race;
            UiKit.Label(new Rect(box.x + 32, box.y + 22, w - 64, 40), (track.Src.city + (isRace ? "  ·  GRID" : "  ·  TIME TRIAL")).ToUpperInvariant(), 32, UiKit.Text, TextAnchor.MiddleLeft, false, true);
            UiKit.Accent(new Rect(box.x + 32, box.y + 66, 260, 3), Look.Cyan);
            UiKit.Label(new Rect(box.x + 32, box.y + 76, w - 64, 26), isRace ? "You start P" + (race.Cars.IndexOf(p) + 1) + " of " + race.Cars.Count + "  ·  " + race.Laps + " laps  ·  two compounds required" : "Pick tyres for a flying lap", 17, UiKit.Dim);
            GUILayout.BeginArea(new Rect(box.x + 26, box.y + 112, w - 52, h - 124));
            int lapsN = Mathf.Max(3, isRace ? race.Laps : 6); GUILayout.BeginHorizontal();
            for (int k = 0; k < 3; k++)
            {
                var ts = Config.Tyres[k];
                if (GUILayout.Button("<b><size=22>" + ts.Name.ToUpperInvariant() + "</size></b>\n<color=#8a96a6>≈ " + (ts.Life * lapsN).ToString("0.0") + " laps  ·  grip " + Mathf.RoundToInt(ts.Grip * 100) + "%</color>", (int)startTyre == k ? cardOn : card)) startTyre = (Compound)k;
                var lr = GUILayoutUtility.GetLastRect(); UiKit.TyreRing(new Rect(lr.center.x - 24, lr.y + 14, 48, 48), TyreColor((Compound)k), TyreLetter((Compound)k));
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("GO TO THE GRID", btnPrimary))
            {
                p.Used.Clear(); p.Stints.Clear(); p.SetTyre(startTyre); p.NextTyre = startTyre == Compound.Hard ? Compound.Medium : Compound.Hard; screen = Screen.Race;
            }
            GUILayout.EndArea();
        }

        void DrawLobby()
        {
            float w = 680; var box = new Rect((UiW - w) / 2, 60, w, UiH - 120); UiKit.Panel(box, 18, .93f);
            GUILayout.BeginArea(new Rect(box.x + 30, box.y + 24, w - 60, box.height - 48));
            var s = online.Session;
            GUILayout.Label("ROOM " + (s != null ? s.Code : "…"), h2);
            GUILayout.Label((net.IsHost ? "You are the host. " : "") + "Share the code with friends; public rooms also appear in everyone's room list.", note);
            if (s != null && GUILayout.Button("Copy code", btn, GUILayout.Width(140))) GUIUtility.systemCopyBuffer = s.Code;
            GUILayout.Label("DRIVERS", small);
            foreach (var p in net.Players) GUILayout.Label(p.Name + (p.Id == net.LocalId ? " (you)" : "") + "   <color=#8a96a6>" + Config.Tyres[p.Tyre].Name + "</color>   " + (p.Ready ? "<color=#39FF88>Ready</color>" : "<color=#8a96a6>Not ready</color>"), label);
            GUILayout.Label("YOUR STARTING TYRE", small); int ty = net.MyTyre; if (Seg(new[] { "Soft", "Medium", "Hard" }, ref ty)) { net.MyTyre = (byte)ty; net.SendHello(); }
            if (net.IsHost)
            {
                GUILayout.Label("CIRCUIT", small);
                GUILayout.BeginHorizontal(); if (GUILayout.Button("◀", btn, GUILayout.Width(48))) { net.Cfg.Track = (net.Cfg.Track + tracks.Length - 1) % tracks.Length; net.BroadcastLobby(); } GUILayout.Label(tracks[net.Cfg.Track].city.ToUpperInvariant(), h2); if (GUILayout.Button("▶", btn, GUILayout.Width(48))) { net.Cfg.Track = (net.Cfg.Track + 1) % tracks.Length; net.BroadcastLobby(); } GUILayout.EndHorizontal();
                int ls = net.Cfg.Laps == 3 ? 0 : net.Cfg.Laps == 5 ? 1 : 2; GUILayout.Label("LAPS", small); if (Seg(new[] { "3", "5", "10" }, ref ls)) { net.Cfg.Laps = new[] { 3, 5, 10 }[ls]; net.BroadcastLobby(); }
                int ai = net.Cfg.Ai == 0 ? 0 : net.Cfg.Ai == 3 ? 1 : 2; GUILayout.Label("AI CARS", small); if (Seg(new[] { "0", "3", "6" }, ref ai)) { net.Cfg.Ai = new[] { 0, 3, 6 }[ai]; net.BroadcastLobby(); }
                int inc = System.Array.IndexOf(Incs, net.Cfg.Incidents); GUILayout.Label("INCIDENTS", small); if (Seg(new[] { "Off", "Realistic", "Chaotic" }, ref inc)) { net.Cfg.Incidents = Incs[inc]; net.BroadcastLobby(); }
                if (GUILayout.Button("START RACE", btnPrimary)) net.HostStart();
            }
            else
            {
                GUILayout.Label("Circuit: " + tracks[net.Cfg.Track].city + " · " + net.Cfg.Laps + " laps · " + net.Cfg.Ai + " AI", note);
                if (GUILayout.Button(net.MyReady ? "NOT READY" : "READY", btnPrimary)) { net.MyReady = !net.MyReady; net.SendHello(); }
            }
            if (GUILayout.Button("Leave room", btn)) LeaveOnline();
            GUILayout.EndArea();
        }

        void DrawResults()
        {
            var rows = race.Classification(); var me = rows.Find(r => r.Car.IsPlayer);
            float w = 820, rowH = 36, h = 150 + rows.Count * rowH + 70; var box = new Rect((UiW - w) / 2, Mathf.Max(40, (UiH - h) / 2), w, h); UiKit.Panel(box, 18, .93f);
            string title = me != null && me.Status == "DSQ" ? "DISQUALIFIED" : me != null && me.Pos == 1 ? "RACE WINNER" : "P" + (me != null ? me.Pos : 0) + " FINISH";
            UiKit.Label(new Rect(box.x + 32, box.y + 22, w - 64, 48), title, 40, me != null && me.Pos == 1 ? Look.Amber : UiKit.Text, TextAnchor.MiddleLeft, false, true);
            UiKit.Label(new Rect(box.x + 32, box.y + 22, w - 64, 48), track.Src.city.ToUpperInvariant() + "  ·  " + race.Laps + " LAPS", 16, UiKit.Dim, TextAnchor.MiddleRight, false, true);
            UiKit.Accent(new Rect(box.x + 32, box.y + 74, 300, 3), Look.Cyan);
            float y = box.y + 92, x = box.x + 24;
            string[] heads = { "POS", "DRIVER", "TIME", "TYRES", "BEST LAP", "PTS" }; float[] cx = { 12, 76, 190, 430, 560, 700 };
            for (int k = 0; k < heads.Length; k++) UiKit.Label(new Rect(x + cx[k], y, 140, 22), heads[k], 13, UiKit.Faint, TextAnchor.MiddleLeft, false, true);
            y += 26; float win = rows[0].Time;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; var rr = new Rect(x, y + i * rowH, w - 48, rowH - 4); bool mine = r.Car.IsPlayer;
                if (mine) UiKit.Tex(rr, UiKit.FadeRight, new Color(Look.Magenta.r, Look.Magenta.g, Look.Magenta.b, .45f)); else UiKit.Fill(rr, new Color(1, 1, 1, i % 2 == 0 ? .05f : .025f), 5);
                UiKit.Label(new Rect(rr.x + cx[0], rr.y, 40, rr.height), r.Pos > 0 ? r.Pos.ToString() : "–", 20, UiKit.Text, TextAnchor.MiddleLeft, true, true);
                UiKit.Fill(new Rect(rr.x + 58, rr.y + 6, 4, rr.height - 12), Visuals.Hex(mine ? "#ffd21f" : r.Car.Color), 2);
                UiKit.Label(new Rect(rr.x + cx[1], rr.y, 100, rr.height), r.Car.Code, 20, UiKit.Text, TextAnchor.MiddleLeft, false, true);
                string time = r.Status != "" ? r.Status + (r.Car.Dnf != null ? " · " + r.Car.Dnf : "") : i == 0 ? MathX.FormatTime(r.Time) : "+" + (r.Time - win).ToString("0.000");
                UiKit.Label(new Rect(rr.x + cx[2], rr.y, 230, rr.height), time, 19, r.Status != "" ? UiKit.Dim : UiKit.Text, TextAnchor.MiddleLeft, true);
                for (int k = 0; k < r.Car.Stints.Count && k < 5; k++) UiKit.TyreRing(new Rect(rr.x + cx[3] + k * 24, rr.y + 5, 22, 22), TyreColor(r.Car.Stints[k]), TyreLetter(r.Car.Stints[k]));
                UiKit.Label(new Rect(rr.x + cx[4], rr.y, 130, rr.height), MathX.FormatTime(r.Car.BestLap), 19, UiKit.Text, TextAnchor.MiddleLeft, true);
                if (r.Points > 0) UiKit.Label(new Rect(rr.x + cx[5], rr.y, 60, rr.height), r.Points.ToString(), 20, Look.Cyan, TextAnchor.MiddleLeft, true, true);
            }
            GUILayout.BeginArea(new Rect(box.x + 24, box.yMax - 62, w - 48, 50)); GUILayout.BeginHorizontal();
            if (net.InRace) { if (net.IsHost && GUILayout.Button("Back to room", btnOn)) net.HostBackToLobby(); if (GUILayout.Button("Leave room", btn)) LeaveOnline(); }
            else { if (GUILayout.Button("Race again", btnOn)) StartSingle(); if (GUILayout.Button("Menu", btn)) { screen = Screen.Menu; StartAttract(); } }
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }

        void DrawHud()
        {
            var p = race.Player; float W = UiW, H = UiH; Color mg = Look.Magenta, cy = Look.Cyan;
            // ---- timing tower ----
            var order = race.Order(); float tx = 28, ty = 28, tw = 304, rh = 32;
            var lead = order[0]; var hr = new Rect(tx, ty, tw, 38); UiKit.Fill(hr, UiKit.Glass, 8); UiKit.Fill(new Rect(tx, ty + 9, 3, 20), cy, 1.5f);
            UiKit.Label(new Rect(tx + 14, ty, 80, 38), "LAP", 15, UiKit.Dim, TextAnchor.MiddleLeft, false, true);
            UiKit.Label(new Rect(tx + 52, ty, 120, 38), Mathf.Min(race.Laps, Mathf.Max(1, lead.MaxLaps + 1)) + "<color=#8a96a6> / " + race.Laps + "</color>", 22, UiKit.Text, TextAnchor.MiddleLeft, true, true);
            UiKit.Label(new Rect(tx, ty, tw - 12, 38), track.Src.city.ToUpperInvariant(), 14, UiKit.Dim, TextAnchor.MiddleRight, false, true);
            for (int i = 0; i < order.Count; i++)
            {
                var c = order[i]; var rr = new Rect(tx, ty + 42 + i * rh, tw, rh - 2);
                UiKit.Fill(rr, new Color(.025f, .035f, .065f, c.IsPlayer ? .9f : .68f), 5);
                if (c.IsPlayer) UiKit.Tex(rr, UiKit.FadeRight, new Color(mg.r, mg.g, mg.b, .55f));
                UiKit.Label(new Rect(rr.x, rr.y, 34, rr.height), (i + 1).ToString(), 18, UiKit.Text, TextAnchor.MiddleCenter, true, true);
                UiKit.Fill(new Rect(rr.x + 36, rr.y + 5, 4, rr.height - 10), Visuals.Hex(c.IsPlayer ? "#ffd21f" : c.Color), 2);
                UiKit.Label(new Rect(rr.x + 50, rr.y, 60, rr.height), c.Code, 19, UiKit.Text, TextAnchor.MiddleLeft, false, true);
                if (c.Pit != null) UiKit.Pill(new Rect(rr.x + 104, rr.y + 6, 36, rr.height - 12), "PIT", Look.Amber, UiKit.Ink, 11);
                else if (c.Penalty > 0) UiKit.Pill(new Rect(rr.x + 104, rr.y + 6, 36, rr.height - 12), "+" + c.Penalty, new Color(1f, .25f, .25f), Color.white, 11);
                else if (c.OtActive) UiKit.Pill(new Rect(rr.x + 104, rr.y + 6, 36, rr.height - 12), "OT", new Color(.22f, 1f, .53f), UiKit.Ink, 11);
                string gap = i == 0 ? "LEADER" : c.Retired ? "OUT" : "+" + ((order[i - 1].PF - c.PF) * Config.Step / Config.GapSpeed).ToString("0.0");
                UiKit.Label(new Rect(rr.x + 150, rr.y, tw - 150 - 40, rr.height), gap, i == 0 ? 13 : 17, i == 0 || c.Retired ? UiKit.Dim : UiKit.Text, TextAnchor.MiddleRight, i != 0, i == 0);
                UiKit.TyreRing(new Rect(rr.xMax - 31, rr.y + 4, 22, 22), TyreColor(c.Tyre), TyreLetter(c.Tyre));
            }
            // ---- flags and start lights ----
            string flag = race.Neutral != null ? (race.Neutral.Type == "SC" ? (race.Neutral.Phase == "in" ? "SAFETY CAR IN THIS LAP" : "SAFETY CAR") : (race.Neutral.Phase == "ending" ? "VSC ENDING" : "VIRTUAL SAFETY CAR")) : p != null && p.Blue ? "BLUE FLAG" : p != null && race.SectorYellow(p.Idx) == 2 ? "DOUBLE YELLOW" : p != null && race.SectorYellow(p.Idx) == 1 ? "YELLOW FLAG" : race.Time < race.BlackWhiteUntil ? "BLACK AND WHITE FLAG" : race.Time < race.GreenUntil ? "GREEN FLAG" : "";
            if (flag != "")
            {
                Color fc = flag.StartsWith("GREEN") ? new Color(.2f, .9f, .45f) : flag.StartsWith("BLUE") ? new Color(.25f, .55f, 1f) : flag.StartsWith("BLACK") ? new Color(.9f, .92f, .95f) : Look.Amber;
                var fr = new Rect(W / 2 - 250, 28, 500, 46); UiKit.Fill(fr, new Color(fc.r, fc.g, fc.b, .94f), 8); UiKit.Label(fr, flag, 24, UiKit.Ink, TextAnchor.MiddleCenter, false, true);
            }
            if (race.Mode == SessionMode.Race && !race.Started)
            {
                int lit = Mathf.Clamp(Mathf.FloorToInt(race.Time - .4f), 0, 5); var lr = new Rect(W / 2 - 190, 88, 380, 76); UiKit.Fill(lr, new Color(.02f, .02f, .03f, .9f), 16); UiKit.Outline(lr, new Color(1, 1, 1, .1f), 1, 16);
                for (int k = 0; k < 5; k++)
                {
                    var c0 = new Vector2(lr.x + 50 + k * 70, lr.center.y);
                    if (k < lit) { UiKit.Dot(c0, 96, new Color(1f, .1f, .08f, .35f)); UiKit.Dot(c0, 46, new Color(1f, .16f, .1f)); UiKit.Dot(c0 + new Vector2(-6, -7), 14, new Color(1, .8f, .75f, .7f)); }
                    else UiKit.Dot(c0, 46, new Color(.16f, .04f, .04f));
                }
            }
            if (p == null) return;
            // ---- lap and time ----
            var lb = new Rect(W - 28 - 280, 28, 280, 112); UiKit.Panel(lb, 12);
            UiKit.Label(new Rect(lb.x + 20, lb.y + 12, 60, 24), "LAP", 15, UiKit.Dim, TextAnchor.MiddleLeft, false, true);
            UiKit.Label(new Rect(lb.x + 20, lb.y + 12, lb.width - 40, 24), race.Mode == SessionMode.Race ? Mathf.Min(race.Laps, Mathf.Max(1, p.MaxLaps + 1)) + "<color=#8a96a6> / " + race.Laps + "</color>" : Mathf.Max(1, p.MaxLaps + 1).ToString(), 22, UiKit.Text, TextAnchor.MiddleRight, true, true);
            UiKit.Label(new Rect(lb.x + 20, lb.y + 36, lb.width - 40, 46), MathX.FormatTime(race.Mode == SessionMode.Race ? (p.Finished ? p.FinishTime : (race.Started ? race.Time - race.RaceStart : 0)) : (p.MaxLaps >= 0 ? race.Time - p.LapStart : 0)), 40, UiKit.Text, TextAnchor.MiddleLeft, true, true);
            UiKit.Label(new Rect(lb.x + 20, lb.y + 82, lb.width - 40, 22), "LAST  <color=#edf4fa>" + MathX.FormatTime(p.LastLap) + "</color>" + (race.Neutral != null && race.Neutral.Type == "VSC" ? "    VSC Δ <color=#FFB000>" + p.VscDelta.ToString("+0.0;-0.0") + "</color>" : ""), 15, UiKit.Dim, TextAnchor.MiddleLeft, true);
            // ---- dash: shift bar, gear, speed, energy ----
            var d = new Rect(W / 2 - 340, H - 28 - 158, 680, 158); UiKit.Panel(d, 16);
            float kmh = Mathf.Abs(p.VF) * 3.6f;
            UiKit.SegBar(new Rect(d.x + 26, d.y + 18, d.width - 60, 12), 34, kmh / 340f, i => i < 20 ? cy : i < 29 ? Look.Amber : mg);
            var gr = new Rect(d.x + 26, d.y + 46, 92, 92); UiKit.Fill(gr, new Color(1, 1, 1, .06f), 14); UiKit.Outline(gr, new Color(cy.r, cy.g, cy.b, .7f), 2, 14);
            UiKit.Label(gr, Gear(p), 70, cy, TextAnchor.MiddleCenter, true, true);
            UiKit.Label(new Rect(d.x + 126, d.y + 40, 200, 86), Mathf.RoundToInt(kmh).ToString(), 92, UiKit.Text, TextAnchor.MiddleRight, true, true);
            UiKit.Label(new Rect(d.x + 126, d.y + 122, 200, 20), "KM/H", 13, UiKit.Dim, TextAnchor.MiddleRight, false, true);
            float ex = d.x + 362, ew = d.xMax - 26 - ex; bool boosting = p.Boost && p.Dep > 0;
            UiKit.Label(new Rect(ex, d.y + 44, 160, 22), "ENERGY  <color=#8a96a6>" + (p.RechargeMode ? "RECHARGE" : "BALANCED") + "</color>", 14, UiKit.Text, TextAnchor.MiddleLeft, false, true);
            UiKit.Label(new Rect(ex, d.y + 40, ew, 28), p.Soc.ToString("0.0") + "<size=13><color=#8a96a6>  MJ</color></size>", 24, UiKit.Text, TextAnchor.MiddleRight, true, true);
            Color ec = p.OtActive ? new Color(.22f, 1f, .53f) : boosting ? Look.Amber : cy;
            UiKit.SegBar(new Rect(ex, d.y + 74, ew - 6, 16), 18, p.Soc / Config.ErsCap, i => ec);
            float pw = (ew - 16) / 3f;
            UiKit.Lamp(new Rect(ex, d.y + 106, pw, 28), "STRAIGHT", p.StraightMode, cy);
            UiKit.Lamp(new Rect(ex + pw + 8, d.y + 106, pw, 28), "BOOST", boosting, Look.Amber);
            UiKit.Lamp(new Rect(ex + (pw + 8) * 2, d.y + 106, pw, 28), p.OtActive ? "OVERTAKE" : p.OtArmed ? "OT READY" : "OVERTAKE", p.OtActive || p.OtArmed, p.OtActive ? new Color(.22f, 1f, .53f) : new Color(.7f, .75f, .8f));
            // ---- tyres and pit ----
            var tp = new Rect(28, H - 28 - 158, 320, 158); UiKit.Panel(tp, 16);
            float life = Mathf.Max(0, 1 - p.Wear); Color tc = TyreColor(p.Tyre);
            UiKit.TyreRing(new Rect(tp.x + 22, tp.y + 22, 84, 84), tc, TyreLetter(p.Tyre));
            UiKit.Label(new Rect(tp.x + 122, tp.y + 20, 180, 28), Config.Tyres[(int)p.Tyre].Name.ToUpperInvariant(), 22, UiKit.Text, TextAnchor.MiddleLeft, false, true);
            UiKit.Label(new Rect(tp.x + 122, tp.y + 46, 180, 36), Mathf.RoundToInt(life * 100) + "<size=14><color=#8a96a6> % LIFE</color></size>", 30, life < .25f ? new Color(1f, .3f, .3f) : UiKit.Text, TextAnchor.MiddleLeft, true, true);
            UiKit.SegBar(new Rect(tp.x + 122, tp.y + 88, 170, 10), 10, life, i => life < .25f ? new Color(1f, .3f, .3f) : life < .5f ? Look.Amber : tc);
            UiKit.Label(new Rect(tp.x + 22, tp.y + 118, 50, 26), "NEXT", 13, UiKit.Dim, TextAnchor.MiddleLeft, false, true);
            UiKit.TyreRing(new Rect(tp.x + 62, tp.y + 120, 22, 22), TyreColor(p.NextTyre), TyreLetter(p.NextTyre));
            string pit = p.Pit != null ? (p.Pit.Phase == "stop" ? "STOPPED " + Mathf.Max(0, p.Pit.StopTime - p.Pit.Stop).ToString("0.0") : "PIT LIMITER 80") : p.BoxRequest ? "BOX THIS LAP" : race.Mode == SessionMode.Race && p.Used.Count < 2 ? "STOP REQUIRED" : p.Damage > .05f ? "WING DAMAGE " + Mathf.RoundToInt(p.Damage * 100) + "%" : "STOPS " + p.Stops;
            bool pitHot = p.Pit != null || p.BoxRequest; UiKit.Lamp(new Rect(tp.x + 100, tp.y + 118, tp.width - 122, 26), pit, pitHot || p.Damage > .05f, pitHot ? Look.Amber : new Color(1f, .35f, .3f));
            // ---- radio ----
            float ry = tp.y - 12 - radio.Count * 36;
            foreach (var m in radio)
            {
                if (Time.time - m.Key < 7) { var rr = new Rect(28, ry, 520, 32); UiKit.Fill(rr, UiKit.Glass, 8); UiKit.Pill(new Rect(rr.x + 8, rr.y + 6, 58, 20), "RADIO", cy, UiKit.Ink, 11); UiKit.Label(new Rect(rr.x + 78, rr.y, rr.width - 88, rr.height), m.Value, 16, UiKit.Text); }
                ry += 36;
            }
            // ---- minimap ----
            if (builder.MapTexture)
            {
                var mp0 = new Rect(W - 28 - 230, H - 28 - 230, 230, 230); UiKit.Panel(mp0, 16);
                var mr = new Rect(mp0.x + 15, mp0.y + 15, 200, 200); UiKit.Tex(mr, builder.MapTexture, Color.white);
                float k = mr.width / builder.MapSize;
                foreach (var c in race.Cars)
                {
                    if (c.Hidden || c.IsPlayer) continue; var mp = builder.MapPoint(c.X, c.Y); var at = new Vector2(mr.x + mp.x * k, mr.y + mp.y * k);
                    UiKit.Dot(at, 11, UiKit.Ink); UiKit.Dot(at, 8, Visuals.Hex(c.Color));
                }
                var me = builder.MapPoint(p.X, p.Y); var pm = new Vector2(mr.x + me.x * k, mr.y + me.y * k); float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 5f);
                UiKit.Dot(pm, 22 + pulse * 10, new Color(mg.r, mg.g, mg.b, .3f)); UiKit.Dot(pm, 15, Color.white); UiKit.Dot(pm, 11, mg);
            }
            if (paused) { UiKit.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .45f)); UiKit.Label(new Rect(0, H / 2 - 50, W, 70), "PAUSED", 64, UiKit.Text, TextAnchor.MiddleCenter, false, true); UiKit.Label(new Rect(0, H / 2 + 20, W, 30), "PRESS ESC TO RESUME", 16, UiKit.Dim, TextAnchor.MiddleCenter, false, true); }
        }
        static string Gear(Car p)
        {
            if (p.Pit != null) return "P"; if (p.VF < -.5f) return "R"; if (p.VF < .5f) return "N";
            for (int g = 1; g < Config.GearTops.Length; g++) if (p.VF < Config.GearTops[g] || g == Config.GearTops.Length - 1) return g.ToString();
            return "8";
        }
    }
}
