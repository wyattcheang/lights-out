// Lights Out (Unity): menus and HUD in UI Toolkit, following docs/ui-design. The tree is built from code and styled
// by Resources/LightsOutUi.uss; the panel is laid out at 1280x720 and scales with the window height.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightsOut
{
    public partial class GameController
    {
        VisualElement ui, menu, hud, prerace, results, lobby, pauseVeil, toastBox; Label toastText;
        Screen shownScreen = (Screen)(-1); int tab; string lobbySig = "", roomsSig = "";
        readonly List<Action> refresh = new List<Action>();   // controls that re-read the game state they show

        // ---------------- small builders ----------------
        static T With<T>(T e, string classes, params VisualElement[] kids) where T : VisualElement
        {
            foreach (var c in classes.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
            foreach (var k in kids) if (k != null) e.Add(k);
            return e;
        }
        static VisualElement Box(string classes, params VisualElement[] kids) { return With(new VisualElement(), classes, kids); }
        static Label Txt(string classes, string s = "") { var l = new Label(s) { pickingMode = PickingMode.Ignore }; return With(l, "t " + classes); }
        static VisualElement Btn(string classes, Action click, params VisualElement[] kids) { var b = Box("btn " + classes, kids); b.AddManipulator(new Clickable(click)); return b; }
        /// A shaded panel with its drop shadow.
        static VisualElement Panel(string classes, params VisualElement[] kids)
        {
            var p = Box("panel " + classes); p.Add(new UiShadow());
            foreach (var k in kids) if (k != null) p.Add(k);
            return p;
        }
        static VisualElement Primary(string text, Action click, out Label label) { label = Txt("", text); return Btn("primary", click, label, new UiIcon("arrow", 24)); }
        static VisualElement Primary(string text, Action click) { Label l; return Primary(text, click, out l); }
        static VisualElement Tyre(int size, Compound c) { var e = Box("tyre tyre" + size, Txt("")); SetTyre(e, c); return e; }
        static void SetTyre(VisualElement e, Compound c)
        {
            var col = TyreColor(c); e.style.borderTopColor = col; e.style.borderBottomColor = col; e.style.borderLeftColor = col; e.style.borderRightColor = col;
            var l = (Label)e[0]; l.text = TyreLetter(c); l.style.color = col;
        }
        static VisualElement TeamBar(Color c, float height) { var b = Box("team"); b.style.height = height; b.style.backgroundColor = c; return b; }
        static VisualElement KeyValue(string key, string value) { return Box("kv", Txt("label", key), Txt("f7 s18", value)); }
        Color TeamColor(Car c) { return Visuals.Hex(c.IsPlayer ? "#ffd21f" : c.Color); }
        static Color TyreColor(Compound c) { var t = Config.Tyres[(int)c]; return new Color(t.R, t.G, t.B); }
        static string TyreLetter(Compound c) { return Config.Tyres[(int)c].Name.Substring(0, 1); }

        VisualElement Seg(string label, string[] opts, Func<int> get, Action<int> set)
        {
            var segs = Box("segs"); var btns = new VisualElement[opts.Length];
            for (int i = 0; i < opts.Length; i++)
            {
                int k = i; btns[i] = Btn("seg f7" + (i == opts.Length - 1 ? " seg--last" : ""), () => { set(k); RefreshUi(); }, Txt("", opts[i])); segs.Add(btns[i]);
            }
            refresh.Add(() => { int s = get(); for (int i = 0; i < btns.Length; i++) { btns[i].EnableInClassList("lit", i == s); btns[i].EnableInClassList("raised", i != s); } });
            return Box("gap4", Txt("label", label.ToUpperInvariant()), segs);
        }
        static VisualElement TopBar(params VisualElement[] rest)
        {
            var dots = Box("row"); for (int i = 0; i < 5; i++) dots.Add(Box("dot5"));
            var bar = Box("topbar", dots, Txt("fi s22 brand", "LIGHTS OUT")); foreach (var r in rest) bar.Add(r);
            return bar;
        }
        Action preraceSelection;
        void RefreshUi() { foreach (var a in refresh) a(); if (preraceSelection != null) preraceSelection(); }

        // ---------------- set-up ----------------
        void BuildUi()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("LightsOutTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize; settings.referenceResolution = new Vector2Int(1280, 720);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; settings.match = 1f;
            var doc = gameObject.AddComponent<UIDocument>(); doc.panelSettings = settings;
            ui = Box("root"); doc.rootVisualElement.Add(ui); ui.pickingMode = PickingMode.Ignore;
            menu = Box("screen scrim"); hud = Box("screen hud"); prerace = Box("screen veil"); results = Box("screen scrim"); lobby = Box("screen scrim");
            hud.pickingMode = PickingMode.Ignore;
            BuildMenu(); BuildHud();
            pauseVeil = Box("screen veil hidden", Txt("fi s64", "PAUSED"), Txt("label", "PRESS ESC TO RESUME")); pauseVeil.pickingMode = PickingMode.Ignore;
            toastText = Txt("fi s16"); toastBox = Panel("panel--hud toast hidden", toastText);
            var toastLayer = Box("screen"); toastLayer.pickingMode = PickingMode.Ignore; toastLayer.style.alignItems = Align.Center; toastLayer.style.paddingTop = 150; toastLayer.Add(toastBox);
            foreach (var s in new[] { hud, menu, prerace, results, lobby, pauseVeil, toastLayer }) ui.Add(s);
            RefreshUi(); UpdateUi();
        }

        void UpdateUi()
        {
            if (ui == null) return;
            if (screen != shownScreen)
            {
                shownScreen = screen;
                if (screen == Screen.Prerace) BuildPrerace();
                if (screen == Screen.Results) BuildResults();
                if (screen == Screen.Lobby) lobbySig = "";
                menu.EnableInClassList("hidden", screen != Screen.Menu);
                hud.EnableInClassList("hidden", screen != Screen.Race);
                prerace.EnableInClassList("hidden", screen != Screen.Prerace);
                results.EnableInClassList("hidden", screen != Screen.Results);
                lobby.EnableInClassList("hidden", screen != Screen.Lobby);
                RefreshUi();
            }
            if (screen == Screen.Menu) UpdateRooms();
            if (screen == Screen.Lobby) UpdateLobby();
            if (screen == Screen.Race) UpdateHud();
            pauseVeil.EnableInClassList("hidden", !(paused && screen == Screen.Race));
            bool toasting = Time.time < toastUntil; toastBox.EnableInClassList("hidden", !toasting); if (toasting) toastText.text = toast.ToUpperInvariant();
        }

        // ---------------- main menu ----------------
        VisualElement roomsBox, onlinePanel, cardPanel, controlsPanel; TrackMap menuMap; TextField nickField, codeField;
        void BuildMenu()
        {
            string[] tabNames = { "SINGLE PLAYER", "ONLINE", "CONTROLS" }; var tabs = new VisualElement[3];
            for (int i = 0; i < 3; i++) { int k = i; tabs[i] = Btn("tab f7", () => { tab = k; RefreshUi(); }, Txt("", tabNames[i])); }
            refresh.Add(() => { for (int i = 0; i < 3; i++) tabs[i].EnableInClassList("tab--on", i == tab); });
            menu.Add(TopBar(tabs[0], tabs[1], tabs[2], Box("spacer"), Txt("s12 dim", "Unofficial fan project")));

            Label startLabel;
            var left = Panel("panel--primary col-left gap12",
                Txt("heading", "SESSION SETUP"),
                Seg("Session", new[] { "Race", "Time trial" }, () => modeIdx, v => modeIdx = v),
                Seg("Race distance", new[] { "3 laps", "5 laps", "10 laps" }, () => laps == 3 ? 0 : laps == 5 ? 1 : 2, v => laps = new[] { 3, 5, 10 }[v]),
                Seg("Grid size", new[] { "6", "8", "10" }, () => field == 6 ? 0 : field == 8 ? 1 : 2, v => field = new[] { 6, 8, 10 }[v]),
                Seg("Opponents", new[] { "Rookie", "Pro", "Ace" }, () => diffIdx, v => diffIdx = v),
                Seg("Incidents", new[] { "Off", "Realistic", "Chaotic" }, () => incIdx, v => incIdx = v),
                Seg("Camera", new[] { "Cockpit", "Chase", "Top-down" }, () => (int)rig.Mode, v => rig.Mode = (CamMode)v),
                Box("spacer"),
                Primary("START RACE", StartSingle, out startLabel));
            refresh.Add(() => startLabel.text = modeIdx == 0 ? "START RACE" : "START TIME TRIAL");

            // circuit card
            Label caption = Txt("label"), city = Txt("fi s60 mt10"), country = Txt("s20 dim mt6"), length = Txt("f7 s26"), distance = Txt("f7 s26"), opened = Txt("f7 s26");
            var tags = Box("row mt10", Txt("tag tag--line f7", "STREET"), Txt("tag tag--red f7", "NIGHT RACE"));
            menuMap = new TrackMap { Width = 4.5f, StartMark = true }; menuMap.AddToClassList("menu-map");
            Func<string, Label, string, VisualElement> stat = (name, value, unit) => Box("", Txt("label", name), Box("row base", value, unit != null ? Txt("f6 s14 dim unit", unit) : null));
            cardPanel = Panel("card",
                Box("grow", caption, city, country, tags, Box("spacer"), Box("row", stat("LENGTH", length, "km"), Box("vrule"), stat("RACE", distance, "km"), Box("vrule"), stat("OPENED", opened, null))),
                menuMap);
            refresh.Add(() =>
            {
                var t = tracks[trackIdx]; int section = tracks.Count(x => x.cur == t.cur), nth = tracks.Take(trackIdx + 1).Count(x => x.cur == t.cur);
                caption.text = "CIRCUIT " + nth.ToString("00") + " / " + section + " · " + (t.cur ? "CURRENT CALENDAR" : "PAST VENUES");
                city.text = t.city.ToUpperInvariant(); country.text = t.country;
                length.text = (t.len / 1000f).ToString("0.000"); distance.text = modeIdx == 0 ? (t.len * laps / 1000f).ToString("0.00") : "–"; opened.text = t.opened.ToString();
                tags[0].EnableInClassList("hidden", !Config.Street.Contains(t.id)); tags[1].EnableInClassList("hidden", !Config.Night.Contains(t.id));
                menuMap.Show(track);
            });

            // race online
            nickField = new TextField { value = nick, maxLength = 16 }; nickField.AddToClassList("field"); nickField.textEdition.placeholder = "Your name";
            nickField.RegisterValueChangedCallback(e => nick = e.newValue);
            codeField = new TextField { value = joinCode, maxLength = 8 }; With(codeField, "field field--code grow mr8"); codeField.textEdition.placeholder = "Enter code";
            codeField.RegisterValueChangedCallback(e => joinCode = e.newValue);
            roomsBox = Box("rule-top");
            onlinePanel = Panel("pad gap12",
                Box("row base between", Txt("heading", "RACE ONLINE"), Txt("s12 dim", "Up to 10 drivers per room")),
                Box("row",
                    Box("grow gap8 mr16", Txt("label", "DRIVER NAME"), nickField,
                        Box("row", Btn("small-btn lit f7 grow mr8", () => { if (!busy) HostRoom(false); }, Txt("", "Host public")), Btn("small-btn ghost f7 grow", () => { if (!busy) HostRoom(true); }, Txt("", "Host private")))),
                    Box("grow gap8", Txt("label", "ROOM CODE"),
                        Box("row", codeField, Btn("small-btn ghost f7", () => { if (!busy) JoinRoom(joinCode, null); }, Txt("", "Join"))),
                        roomsBox)));

            // controls and credits
            controlsPanel = Panel("pad grow");
            controlsPanel.Add(Txt("heading", "CONTROLS"));
            string[][] keys = { new[] { "STEER, THROTTLE, BRAKE", "Arrows or WASD · gamepad left stick and triggers" }, new[] { "BOOST", "B · gamepad X" }, new[] { "OVERTAKE MODE", "O" }, new[] { "ENERGY MAP", "E" },
                new[] { "BOX THIS LAP", "P" }, new[] { "NEXT TYRE", "1 soft · 2 medium · 3 hard" }, new[] { "CAMERA", "C" }, new[] { "RECOVER CAR", "R" }, new[] { "PAUSE", "Esc" } };
            foreach (var k in keys) controlsPanel.Add(Box("kv", Txt("label", k[0]), Txt("f7 s15", k[1])));
            controlsPanel.Add(Box("spacer"));
            controlsPanel.Add(Txt("s12 dim wrap", "Circuit outlines: bacinger/f1-circuits (MIT). Car model: \"F1 2026 concept\" by Qvist_designs (CC BY 4.0), simplified. Elevation from public F1 timing telemetry. Typeface: Titillium Web (SIL OFL). Unofficial fan project; not affiliated with Formula One Licensing B.V. or the FIA."));
            refresh.Add(() =>
            {
                cardPanel.EnableInClassList("hidden", tab != 0); onlinePanel.EnableInClassList("hidden", tab == 2); controlsPanel.EnableInClassList("hidden", tab != 2);
                onlinePanel.EnableInClassList("grow", tab == 1);
            });

            // circuit list
            var list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden }; list.AddToClassList("grow");
            var rows = new VisualElement[tracks.Length];
            for (int i = 0; i < tracks.Length; i++)
            {
                if (i == 0 || tracks[i].cur != tracks[i - 1].cur) list.Add(Txt("label list-head", tracks[i].cur ? "CURRENT CALENDAR" : "PAST VENUES"));
                int k = i, nth = tracks.Take(i + 1).Count(x => x.cur == tracks[k].cur);
                rows[i] = Btn("list-row", () => { trackIdx = k; StartAttract(); RefreshUi(); }, Txt("f6 s13 dim w20", nth.ToString("00")), Txt("f7 s15 spacer", tracks[i].city), Txt("s13 dim", tracks[i].country));
                list.Add(rows[i]);
            }
            refresh.Add(() => { for (int i = 0; i < rows.Length; i++) rows[i].EnableInClassList("lit", i == trackIdx); });
            var right = Panel("col-right", Box("clip", Box("row base between list-head", Txt("heading", "CIRCUITS"), Txt("s12 dim", tracks.Length + " venues")), list));

            menu.Add(Box("body", left, Box("col-mid", cardPanel, onlinePanel, controlsPanel), right));
        }

        void UpdateRooms()
        {
            string sig = online.Status + "|" + busy + "|" + string.Join(",", publicRooms.Select(r => r.Id + r.AvailableSlots));
            if (sig == roomsSig) return; roomsSig = sig; roomsBox.Clear();
            foreach (var r in publicRooms.Take(tab == 1 ? 8 : 2))
            {
                var room = r;
                roomsBox.Add(Box("kv", Txt("f7 s14", r.Name + "  (" + (r.MaxPlayers - r.AvailableSlots) + "/" + r.MaxPlayers + ")"), Btn("link f7", () => { if (!busy) JoinRoom(null, room.Id); }, Txt("", "Join"))));
            }
            roomsBox.Add(Box("row middle between", Txt("s13 dim wrap grow", busy ? "Working…" : online.Status != "" ? online.Status.Split(':', '.')[0] : publicRooms.Count == 0 ? "No public rooms open" : publicRooms.Count + " public rooms"),
                Btn("link f7", () => { if (!busy) RefreshRooms(); }, Txt("", "Refresh"))));
        }

        // ---------------- grid and tyre choice ----------------
        void BuildPrerace()
        {
            prerace.Clear(); var p = race.Player; bool isRace = race.Mode == SessionMode.Race; int lapsN = Mathf.Max(3, isRace ? race.Laps : 6);
            var cards = new VisualElement[3]; var tagLabels = new Label[3];
            var cardRow = Box("row");
            for (int k = 0; k < 3; k++)
            {
                var ts = Config.Tyres[k]; int kk = k; tagLabels[k] = Txt("f7 s11");
                cards[k] = Btn("tyre-card" + (k == 2 ? " tyre-card--last" : ""), () => { startTyre = (Compound)kk; RefreshUi(); },
                    Box("row middle", Tyre(52, (Compound)k), Txt("fi s22 spacer ml12", ts.Name), tagLabels[k]),
                    Box("spacer"),
                    Box("tyre-stats", Box("mr24", Txt("label", "LIFE"), Txt("f7 s22", "≈ " + (ts.Life * lapsN).ToString("0.0") + " laps")), Box("", Txt("label", "GRIP"), Txt("f7 s22", Mathf.RoundToInt(ts.Grip * 100) + "%"))));
                cardRow.Add(cards[k]);
            }
            Action sel = () => { for (int k = 0; k < 3; k++) { bool on = (int)startTyre == k; cards[k].EnableInClassList("lit", on); cards[k].EnableInClassList("raised", !on); tagLabels[k].text = on ? "SELECTED" : ""; } };
            preraceSelection = sel;
            var dialog = Panel("panel--primary panel--dialog dialog",
                Box("row base between",
                    Box("", Txt("label", track.Src.city.ToUpperInvariant() + (isRace ? " · GRID" : " · TIME TRIAL")), Txt("fi s40 mt10", "CHOOSE YOUR START TYRE"), Txt("s15 dim mt10", isRace ? race.Laps + " laps · two compounds required" : "Pick tyres for a flying lap")),
                    isRace ? Box("row middle", Box("badge badge64 lit mr12", Txt("f9 s30", "P" + (race.Cars.IndexOf(p) + 1))), Txt("s14 dim", "Grid slot\nof " + race.Cars.Count + " cars")) : null),
                With(cardRow, "mt24"),
                With(Box("row middle between",
                    Txt("s14 dim", "Change your next compound in the race with 1 / 2 / 3, box with P."),
                    With(Primary("GO TO THE GRID", () =>
                    {
                        p.Used.Clear(); p.Stints.Clear(); p.SetTyre(startTyre); p.NextTyre = startTyre == Compound.Hard ? Compound.Medium : Compound.Hard; screen = Screen.Race;
                    }), "w280")), "mt24"));
            prerace.Add(dialog);
        }

        // ---------------- results ----------------
        void BuildResults()
        {
            results.Clear(); var rows = race.Classification(); var me = rows.Find(r => r.Car.IsPlayer); float win = rows[0].Time;
            results.Add(TopBar(Txt("f7 s14 dim", "RACE CLASSIFICATION"), Box("spacer"), Txt("s12 dim", track.Src.city + " · " + race.Laps + " laps")));
            float? fastest = rows.Where(r => r.Car.BestLap != null).Select(r => r.Car.BestLap).Min();
            string title = me == null ? "FINISH" : me.Status == "DSQ" ? "DISQUALIFIED" : me.Status != "" ? me.Status : me.Pos == 1 ? "WINNER" : "FINISH";
            string sub = me == null ? "" : me.Status != "" ? (me.Car.Dnf ?? "Not classified") : me.Pos == 1 ? "Race winner" : "+" + (me.Time - win).ToString("0.000") + " to the winner";
            var buttons = Box("gap8");
            if (net.InRace) { if (net.IsHost) buttons.Add(Primary("BACK TO ROOM", () => net.HostBackToLobby())); buttons.Add(Btn("small-btn ghost f7", LeaveOnline, Txt("", "Leave room"))); }
            else { buttons.Add(Primary("RACE AGAIN", StartSingle)); buttons.Add(Btn("small-btn ghost f7", () => { screen = Screen.Menu; StartAttract(); }, Txt("", "Back to menu"))); }
            var left = Panel("panel--primary col-left w380 gap16",
                Txt("label", "YOUR RESULT"),
                Box("row middle", Box("badge badge112 lit mr16", Txt("f9 s64", me != null && me.Pos > 0 ? "P" + me.Pos : "–")), Box("", Txt("fi s34", title), Txt("s16 dim mt10", sub))),
                me == null ? null : Box("",
                    KeyValue("BEST LAP", MathX.FormatTime(me.Car.BestLap)), KeyValue("STARTED", "P" + (race.Cars.IndexOf(me.Car) + 1)), KeyValue("PIT STOPS", me.Car.Stops.ToString()),
                    KeyValue("PENALTIES", me.Car.Penalty > 0 ? "+" + me.Car.Penalty.ToString("0") + " s" : "None"), KeyValue("POINTS", me.Points.ToString())),
                Box("spacer"), buttons);
            var table = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden }; table.AddToClassList("grow");
            foreach (var r in rows)
            {
                bool mine = r.Car.IsPlayer; var stints = Box("row middle w120"); for (int k = 0; k < r.Car.Stints.Count && k < 4; k++) stints.Add(Tyre(24, r.Car.Stints[k]));
                string note = r.Car.BestLap != null && r.Car.BestLap == fastest ? "FASTEST LAP" : r.Car.Penalty > 0 ? "+" + r.Car.Penalty.ToString("0") + " S PENALTY" : r.Car.Dnf != null ? r.Car.Dnf.ToUpperInvariant() : "";
                string time = r.Status != "" ? r.Status : r == rows[0] ? MathX.FormatTime(r.Time) : "+" + (r.Time - win).ToString("0.000");
                table.Add(Box("result-row" + (mine ? " lit" : ""),
                    Txt("f7 s22 center w56", r.Pos > 0 ? r.Pos.ToString() : "–"), With(TeamBar(TeamColor(r.Car), 28), "mr16"),
                    Box("row base spacer", Txt("f7 s20 mr12", r.Car.Code), Txt("f7 s11 dim", note)),
                    stints, Txt("f6 s16 dim right w110", MathX.FormatTime(r.Car.BestLap)), Txt("f7 s18 right w130", time)));
            }
            var right = Panel("grow", Box("clip",
                Box("result-row result-head", Txt("label center w56", "POS"), Box("team mr16"), Txt("label spacer", "DRIVER"), Txt("label w120", "STINTS"), Txt("label right w110", "BEST LAP"), Txt("label right w130", "TIME / GAP")),
                table));
            results.Add(Box("body", left, right));
        }

        // ---------------- online room ----------------
        void UpdateLobby()
        {
            var s = online.Session; var cfg = net.Cfg;
            string sig = (s != null ? s.Code : "") + net.IsHost + cfg.Track + cfg.Laps + cfg.Ai + cfg.Incidents + net.MyTyre + net.MyReady + string.Join(",", net.Players.Select(p => p.Id + p.Name + p.Tyre + p.Ready));
            if (sig == lobbySig) return; lobbySig = sig; lobby.Clear();
            lobby.Add(TopBar(Txt("f7 s14 dim", "ONLINE ROOM"), Box("spacer"), Btn("small-btn ghost f7", LeaveOnline, Txt("", "Leave room"))));
            var drivers = Box("clip", Box("row base between list-head pad24", Txt("heading", "DRIVERS"), Txt("s12 dim", net.Players.Count + " of 10")));
            for (int i = 0; i < net.Players.Count; i++)
            {
                var p = net.Players[i]; bool mine = p.Id == net.LocalId; var tyre = (Compound)p.Tyre;
                var status = Txt("status f7 " + (p.Ready ? "lit" : "status--off"), p.Ready ? "READY" : "NOT READY");
                drivers.Add(Box("driver-row", Txt("f6 s14 dim w20", (i + 1).ToString()), With(TeamBar(Visuals.Hex(mine ? "#ffd21f" : Config.Rivals[(i + 3) % Config.Rivals.Length][1]), 24), "mr12"),
                    Txt("f7 s17 mr12", p.Name), Txt("f7 s12 dim", mine ? (net.IsHost ? "YOU · HOST" : "YOU") : ""), Box("spacer"), Tyre(24, tyre), Txt("s14 dim w64", Config.Tyres[p.Tyre].Name), status));
            }
            var t = tracks[cfg.Track];
            var setup = Panel("pad w400 gap12", Box("row base between", Txt("heading", "RACE SETUP"), Txt("s12 dim", net.IsHost ? "Host controls" : "Set by the host")));
            var local = new List<Action>();
            Func<string, string[], int, Action<int>, VisualElement> seg = (label, opts, selected, set) =>
            {
                var segs = Box("segs");
                for (int i = 0; i < opts.Length; i++) { int k = i; segs.Add(Btn("seg f7 " + (i == selected ? "lit" : "raised") + (i == opts.Length - 1 ? " seg--last" : ""), () => set(k), Txt("", opts[i]))); }
                return Box("gap4", Txt("label", label), segs);
            };
            Action<int> stepTrack = d => { net.Cfg.Track = (net.Cfg.Track + tracks.Length + d) % tracks.Length; net.BroadcastLobby(); };
            var circuit = Box("well grow circuit-well", Txt("fi s18", t.city.ToUpperInvariant()), Txt("s12 dim", t.country + " · " + (t.len / 1000f).ToString("0.000") + " km"));
            setup.Add(Box("gap4", Txt("label", "CIRCUIT"), net.IsHost
                ? Box("segs", Btn("seg seg--tall raised", () => stepTrack(-1), new UiIcon("left", 20)), circuit, Btn("seg seg--tall seg--last raised", () => stepTrack(1), new UiIcon("right", 20)))
                : Box("segs", circuit)));
            if (net.IsHost)
            {
                setup.Add(seg("LAPS", new[] { "3", "5", "10" }, cfg.Laps == 3 ? 0 : cfg.Laps == 5 ? 1 : 2, v => { net.Cfg.Laps = new[] { 3, 5, 10 }[v]; net.BroadcastLobby(); }));
                setup.Add(seg("AI CARS", new[] { "0", "3", "6" }, cfg.Ai == 0 ? 0 : cfg.Ai == 3 ? 1 : 2, v => { net.Cfg.Ai = new[] { 0, 3, 6 }[v]; net.BroadcastLobby(); }));
                setup.Add(seg("INCIDENTS", new[] { "Off", "Realistic", "Chaotic" }, Array.IndexOf(Incs, cfg.Incidents), v => { net.Cfg.Incidents = Incs[v]; net.BroadcastLobby(); }));
            }
            else setup.Add(Txt("s14 dim", cfg.Laps + " laps · " + cfg.Ai + " AI cars · incidents " + cfg.Incidents));
            setup.Add(seg("YOUR STARTING TYRE", new[] { "Soft", "Medium", "Hard" }, net.MyTyre, v => { net.MyTyre = (byte)v; net.SendHello(); }));
            setup.Add(Box("spacer"));
            int waiting = net.Players.Count(p => !p.Ready && p.Id != net.LocalId);
            if (net.IsHost)
            {
                setup.Add(Txt("s13 dim wrap", waiting > 0 ? "Waiting for " + waiting + (waiting == 1 ? " driver" : " drivers") + " to ready up. You can start anyway." : "Everyone is ready."));
                setup.Add(Primary("START RACE", () => net.HostStart()));
            }
            else setup.Add(Primary(net.MyReady ? "NOT READY" : "READY", () => { net.MyReady = !net.MyReady; net.SendHello(); }));
            var code = Panel("panel--primary code-panel",
                Box("spacer", Txt("label", "ROOM CODE"), Txt("f7 s52 code mt10", s != null ? s.Code : "…"),
                    Txt("s14 dim mt10", (net.IsHost ? "You are the host. " : "") + "Share the code with friends to let them join.")),
                Btn("small-btn lit f7", () => { if (s != null) GUIUtility.systemCopyBuffer = s.Code; }, With(new UiIcon("copy", 18), "mr8"), Txt("", "Copy code")));
            lobby.Add(Box("body", Box("col-mid", code, Panel("grow", drivers)), setup));
        }

        // ---------------- HUD ----------------
        class TowerRow { public VisualElement Root, Team, Tyre; public Label Pos, Code, Gap, Tag; }
        readonly List<TowerRow> tower = new List<TowerRow>();
        Label towerLap, towerCity, flagText, timeLabel, timePos, timeValue, lastLap, vscDelta, tyreName, tyrePct, nextTyre, pitNote, speed, gear, batteryLabel, batteryValue;
        VisualElement flagBox, flagIcon, lightsBox, timeBox, vscRow, tyreBox, tyreRing, wearFill, dash, mapBox, towerBox, radioBox; UiIcon flagGlyph;
        readonly VisualElement[] lights = new VisualElement[5], cells = new VisualElement[8], radioRows = new VisualElement[3], chips = new VisualElement[3];
        readonly List<VisualElement> mapDots = new List<VisualElement>(); TrackMap hudMap; int yellowMask = -1; Track hudMapTrack;

        void BuildHud()
        {
            towerLap = Txt("fi s14 tracked"); towerCity = Txt("label");
            towerBox = Panel("panel--primary panel--hud tower", Box("tower-head", towerLap, towerCity));
            flagText = Txt("fi s16 tracked"); flagGlyph = new UiIcon("flag", 20); flagIcon = Box("flag-icon", flagGlyph); flagBox = Box("flag hidden", flagIcon, flagText);
            lightsBox = Panel("panel--hud lights hidden"); for (int k = 0; k < 5; k++) lightsBox.Add(lights[k] = Box("light"));
            var centre = Box("middle", flagBox, lightsBox); centre.pickingMode = PickingMode.Ignore;
            timeLabel = Txt("label"); timePos = Txt("label"); timeValue = Txt("f7 s34 mt4"); lastLap = Txt("f7 s14"); vscDelta = Txt("f7 s14 yellow");
            vscRow = Box("row between hidden", Txt("s14 dim", "VSC delta"), vscDelta);
            timeBox = Panel("panel--primary panel--hud timebox", Box("row between", timeLabel, timePos), timeValue, Box("row between rule-top mt6 pt6", Txt("s14 dim", "Last lap"), lastLap), vscRow);

            radioBox = Box("");
            for (int k = 0; k < 3; k++) radioBox.Add(radioRows[k] = Panel("radio hidden", Txt("f7 s12 radio-tag", "RADIO"), Txt("s14 wrap grow")));
            tyreName = Txt("f7 s15"); tyrePct = Txt("f7 s15"); nextTyre = Txt("f7 s14"); pitNote = Txt("s13 mt6"); tyreRing = Tyre(36, Compound.Medium); wearFill = Box("bar-fill");
            tyreBox = Panel("panel--hud tyrebox",
                Box("row middle", With(tyreRing, "mr10"), Box("grow", Box("row between", tyreName, tyrePct), Box("well bar", wearFill))),
                Box("row between rule-top mt8 pt8", Txt("s14 dim", "Next stop"), nextTyre), pitNote);

            speed = Txt("fi s60 right"); gear = Txt("f9 s34"); batteryLabel = Txt("label"); batteryValue = Txt("f7 s15");
            var cellRow = Box("well cells"); for (int k = 0; k < 8; k++) cellRow.Add(cells[k] = Box("cell"));
            var chipRow = Box("row"); for (int k = 0; k < 3; k++) chipRow.Add(chips[k] = Txt("chip f7"));
            dash = Panel("dash",
                Box("speed", speed, Txt("label tracked", "KM/H")),
                Box("gear lit", gear, Txt("f7 s10 tracked", "GEAR")),
                Box("grow", Box("row base between", batteryLabel, batteryValue), cellRow, chipRow));

            hudMap = new TrackMap { Width = 3f, Line = Visuals.Hex("#A7ABB8"), HighlightColor = Visuals.Hex("#FFD12E") }; hudMap.style.flexGrow = 1;
            mapBox = Panel("panel--hud mapbox", Box("well mapwell", hudMap));

            hud.Add(With(Box("row between", towerBox, centre, timeBox), "top")); hud[0].style.alignItems = Align.FlexStart; hud[0].pickingMode = PickingMode.Ignore;
            var bottom = Box("row between base", Box("w280", radioBox, tyreBox), dash, With(Box("w280", mapBox), "end")); bottom.pickingMode = PickingMode.Ignore;
            hud.Add(bottom);
        }

        TowerRow NewTowerRow()
        {
            var r = new TowerRow { Pos = Txt("f7 s14 center tower-pos"), Team = Box("team mr8"), Code = Txt("f7 s15 w40"), Tag = Txt("tower-tag f7 center"), Gap = Txt("f6 s14 right soft spacer mr8"), Tyre = Tyre(20, Compound.Medium) };
            r.Team.style.height = 16; r.Root = Box("tower-row", r.Pos, r.Team, r.Code, r.Tag, r.Gap, r.Tyre); towerBox.Add(r.Root); tower.Add(r);
            return r;
        }

        void UpdateHud()
        {
            var p = race.Player; var order = race.Order(); var lead = order[0]; bool isRace = race.Mode == SessionMode.Race;
            // timing tower
            towerLap.text = isRace ? "LAP " + Mathf.Min(race.Laps, Mathf.Max(1, lead.MaxLaps + 1)) + " / " + race.Laps : "TIME TRIAL"; towerCity.text = track.Src.city.ToUpperInvariant();
            while (tower.Count < order.Count) NewTowerRow();
            for (int i = 0; i < tower.Count; i++)
            {
                var r = tower[i]; bool on = i < order.Count; r.Root.EnableInClassList("hidden", !on); if (!on) continue;
                var c = order[i]; r.Root.EnableInClassList("lit", c.IsPlayer); r.Root.EnableInClassList("tower-row--last", i == order.Count - 1);
                r.Pos.text = (i + 1).ToString(); r.Team.style.backgroundColor = TeamColor(c); r.Code.text = c.Code; SetTyre(r.Tyre, c.Tyre);
                r.Gap.text = i == 0 ? "Leader" : c.Retired ? "OUT" : "+" + ((order[i - 1].PF - c.PF) * Config.Step / Config.GapSpeed).ToString("0.0");
                string tag = c.Pit != null ? "PIT" : c.Penalty > 0 ? "+" + c.Penalty.ToString("0") : c.OtActive ? "OT" : "";
                r.Tag.text = tag; r.Tag.EnableInClassList("hidden", tag == ""); r.Tag.EnableInClassList("tag--yellow", c.Pit != null); r.Tag.EnableInClassList("tag--red", c.Pit == null && c.Penalty > 0); r.Tag.EnableInClassList("tag--ready", c.Pit == null && c.Penalty <= 0);
            }
            // flags and start lights
            int yel = p != null ? race.SectorYellow(p.Idx) : 0;
            string flag = race.Neutral != null ? (race.Neutral.Type == "SC" ? (race.Neutral.Phase == "in" ? "SAFETY CAR IN THIS LAP" : "SAFETY CAR") : (race.Neutral.Phase == "ending" ? "VSC ENDING" : "VIRTUAL SAFETY CAR"))
                : p != null && p.Blue ? "BLUE FLAG" : yel == 2 ? "DOUBLE YELLOW · SECTOR " + (track.SectorOf(p.Idx) + 1) : yel == 1 ? "YELLOW FLAG · SECTOR " + (track.SectorOf(p.Idx) + 1)
                : race.Time < race.BlackWhiteUntil ? "BLACK AND WHITE FLAG" : race.Time < race.GreenUntil ? "GREEN FLAG" : "";
            flagBox.EnableInClassList("hidden", flag == "");
            if (flag != "")
            {
                Color fc = flag.StartsWith("GREEN") ? Visuals.Hex("#2FBF5B") : flag.StartsWith("BLUE") ? Visuals.Hex("#2F6BFF") : flag.StartsWith("BLACK") ? Color.white : Visuals.Hex("#FFD12E");
                flagBox.style.backgroundColor = fc; flagText.text = flag; flagText.style.color = flag.StartsWith("BLUE") ? Color.white : Visuals.Hex("#14151A"); flagGlyph.Tint = fc;
            }
            bool waiting = isRace && !race.Started && screen == Screen.Race; lightsBox.EnableInClassList("hidden", !waiting);
            if (waiting) { int lit = Mathf.Clamp(Mathf.FloorToInt(race.Time - .4f), 0, 5); for (int k = 0; k < 5; k++) lights[k].EnableInClassList("light--on", k < lit); }
            foreach (var e in new[] { timeBox, tyreBox, dash, mapBox, radioBox }) e.EnableInClassList("hidden", p == null);
            if (p == null) return;
            // race time
            timeLabel.text = isRace ? "RACE TIME" : "LAP " + Mathf.Max(1, p.MaxLaps + 1); timePos.text = isRace ? "P" + (order.IndexOf(p) + 1) : "";
            timeValue.text = MathX.FormatTime(isRace ? (p.Finished ? p.FinishTime : (race.Started ? race.Time - race.RaceStart : 0)) : (p.MaxLaps >= 0 ? race.Time - p.LapStart : 0));
            lastLap.text = MathX.FormatTime(p.LastLap);
            bool vsc = race.Neutral != null && race.Neutral.Type == "VSC"; vscRow.EnableInClassList("hidden", !vsc); if (vsc) vscDelta.text = p.VscDelta.ToString("+0.0;-0.0");
            // radio
            for (int k = 0; k < 3; k++)
            {
                bool on = k < radio.Count && Time.time - radio[k].Key < 7; radioRows[k].EnableInClassList("hidden", !on);
                if (on) ((Label)radioRows[k][2]).text = radio[k].Value;
            }
            // tyres and pit
            float life = Mathf.Max(0, 1 - p.Wear); SetTyre(tyreRing, p.Tyre); tyreName.text = Config.Tyres[(int)p.Tyre].Name; tyrePct.text = Mathf.RoundToInt(life * 100) + "%";
            wearFill.style.width = Length.Percent(life * 100); wearFill.style.backgroundColor = life < .25f ? Visuals.Hex("#E0241B") : life < .5f ? Visuals.Hex("#FFD12E") : Color.white;
            nextTyre.text = Config.Tyres[(int)p.NextTyre].Name;
            bool pitHot = p.Pit != null || p.BoxRequest, mustStop = isRace && p.Used.Count < 2;
            pitNote.text = p.Pit != null ? (p.Pit.Phase == "stop" ? "Stopped " + Mathf.Max(0, p.Pit.StopTime - p.Pit.Stop).ToString("0.0") + " s" : "Pit lane: limiter 80") : p.BoxRequest ? "Box this lap"
                : mustStop ? "Mandatory stop: use a 2nd compound" : p.Damage > .05f ? "Wing damage " + Mathf.RoundToInt(p.Damage * 100) + "%" : "Stops: " + p.Stops;
            pitNote.EnableInClassList("yellow", pitHot || mustStop || p.Damage > .05f); pitNote.EnableInClassList("dim", !(pitHot || mustStop || p.Damage > .05f));
            // dash
            speed.text = Mathf.RoundToInt(Mathf.Abs(p.VF) * 3.6f).ToString(); gear.text = Gear(p);
            bool boosting = p.Boost && p.Dep > 0; batteryLabel.text = "BATTERY · " + (p.RechargeMode ? "RECHARGE" : "BALANCED"); batteryValue.text = p.Soc.ToString("0.0") + " MJ";
            int full = Mathf.RoundToInt(Mathf.Clamp01(p.Soc / Config.ErsCap) * 8);
            for (int k = 0; k < 8; k++) { cells[k].EnableInClassList("cell--on", k < full && !boosting); cells[k].EnableInClassList("cell--boost", k < full && boosting); }
            Chip(0, "STRAIGHT MODE", p.StraightMode ? "chip--white" : null); Chip(1, "BOOST", boosting ? "chip--red" : null);
            Chip(2, p.OtActive ? "OVERTAKE" : p.OtArmed ? "OT READY" : "OVERTAKE", p.OtActive ? "chip--white" : p.OtArmed ? "chip--ready" : null);
            // minimap
            if (hudMapTrack != track) { hudMapTrack = track; hudMap.Highlight = i => race != null && race.Yel[track.SectorOf(i)].Until > race.Time; hudMap.Show(track); yellowMask = -1; }
            int mask = 0; for (int s = 0; s < race.Yel.Length && s < 30; s++) if (race.Yel[s].Until > race.Time) mask |= 1 << s;
            if (mask != yellowMask) { yellowMask = mask; hudMap.MarkDirtyRepaint(); }
            while (mapDots.Count < race.Cars.Count) { var d = Box("map-dot"); d.pickingMode = PickingMode.Ignore; hudMap.Add(d); mapDots.Add(d); }
            for (int i = 0; i < mapDots.Count; i++)
            {
                var d = mapDots[i]; bool on = i < race.Cars.Count && !race.Cars[i].Hidden; d.EnableInClassList("hidden", !on); if (!on) continue;
                var c = race.Cars[i]; var at = hudMap.Project(c.X, c.Y); d.style.left = at.x; d.style.top = at.y;
                d.EnableInClassList("map-dot--me", c.IsPlayer); if (c.IsPlayer) { d.style.backgroundColor = StyleKeyword.Null; d.BringToFront(); } else d.style.backgroundColor = TeamColor(c);
            }
        }
        void Chip(int k, string text, string state)
        {
            var c = (Label)chips[k]; c.text = text;
            c.EnableInClassList("chip--white", state == "chip--white"); c.EnableInClassList("chip--red", state == "chip--red"); c.EnableInClassList("chip--ready", state == "chip--ready");
        }
    }

    /// The circuit outline drawn as a vector path, with optional highlighted sectors and a start/finish mark.
    public class TrackMap : VisualElement
    {
        public Color Line = Color.white, HighlightColor = Color.yellow; public float Width = 3f; public bool StartMark; public Func<int, bool> Highlight;
        Track track;
        public TrackMap() { generateVisualContent += Draw; pickingMode = PickingMode.Ignore; }
        public void Show(Track t) { track = t; MarkDirtyRepaint(); }
        public Vector2 Project(float x, float y)
        {
            if (track == null) return Vector2.zero;
            var r = contentRect; float k = Mathf.Min((r.width - Width * 2) / Mathf.Max(1, track.MaxX - track.MinX), (r.height - Width * 2) / Mathf.Max(1, track.MaxY - track.MinY));
            return new Vector2(r.width / 2 + (x - (track.MinX + track.MaxX) / 2) * k, r.height / 2 + (y - (track.MinY + track.MaxY) / 2) * k);
        }
        void Draw(MeshGenerationContext ctx)
        {
            if (track == null || contentRect.width < 4 || contentRect.height < 4) return;
            var p = ctx.painter2D; int step = Mathf.Max(1, track.N / 400);
            p.lineWidth = Width; p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round; p.strokeColor = Line; p.BeginPath();
            for (int i = 0; i < track.N; i += step) { var q = Project(track.X[i], track.Y[i]); if (i == 0) p.MoveTo(q); else p.LineTo(q); }
            p.ClosePath(); p.Stroke();
            if (Highlight != null)
            {
                p.strokeColor = HighlightColor; bool open = false;
                for (int i = 0; i < track.N; i += step)
                {
                    var q = Project(track.X[i], track.Y[i]);
                    if (Highlight(i)) { if (!open) { p.BeginPath(); p.MoveTo(q); open = true; } else p.LineTo(q); }
                    else if (open) { p.Stroke(); open = false; }
                }
                if (open) p.Stroke();
            }
            if (StartMark)
            {
                p.strokeColor = new Color(.88f, .14f, .1f); p.lineCap = LineCap.Butt; p.BeginPath(); p.MoveTo(Project(track.X[0], track.Y[0]));
                for (int i = step; i < track.N / 28; i += step) p.LineTo(Project(track.X[i], track.Y[i]));
                p.Stroke();
            }
        }
    }

    /// A soft drop shadow around its parent panel: stacked translucent rings outside the panel's outline, heavier below.
    public class UiShadow : VisualElement
    {
        const float Reach = 22f, Drop = 4f; const int Steps = 11;
        public UiShadow() { AddToClassList("shadow"); pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        static void Outline(Painter2D p, float x0, float y0, float x1, float y1, float corner, float round)
        {
            p.MoveTo(new Vector2(x0 + round, y0)); p.LineTo(new Vector2(x1 - round, y0)); if (round > 0) p.ArcTo(new Vector2(x1, y0), new Vector2(x1, y0 + round), round);
            p.LineTo(new Vector2(x1, y1 - corner)); if (corner > 0) p.ArcTo(new Vector2(x1, y1), new Vector2(x1 - corner, y1), corner);
            p.LineTo(new Vector2(x0 + round, y1)); if (round > 0) p.ArcTo(new Vector2(x0, y1), new Vector2(x0, y1 - round), round);
            p.LineTo(new Vector2(x0, y0 + round)); if (round > 0) p.ArcTo(new Vector2(x0, y0), new Vector2(x0 + round, y0), round);
            p.ClosePath();
        }
        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect; float x0 = Reach, y0 = Reach, x1 = r.width - Reach, y1 = r.height - Reach; if (x1 - x0 < 2 || y1 - y0 < 2) return;
            float corner = parent != null ? parent.resolvedStyle.borderBottomRightRadius : 0; var p = ctx.painter2D; p.fillColor = new Color(0, 0, 0, .05f);
            for (int k = 1; k <= Steps; k++)
            {
                float e = (Reach - Drop) * k / Steps;
                p.BeginPath(); Outline(p, x0 - e, y0 - Mathf.Max(0, e - Drop), x1 + e, y1 + e + Drop, corner + e, e); Outline(p, x0, y0, x1, y1, corner, 0); p.Fill(FillRule.OddEven);
            }
        }
    }

    /// Small line icons drawn on a 24-unit grid: arrow, left, right, flag, copy.
    public class UiIcon : VisualElement
    {
        readonly string kind; Color? tint;
        public Color Tint { set { tint = value; MarkDirtyRepaint(); } }
        public UiIcon(string kind, float size)
        {
            this.kind = kind; style.width = size; style.height = size; style.flexShrink = 0; pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e => MarkDirtyRepaint());
        }
        void Draw(MeshGenerationContext ctx)
        {
            float k = contentRect.width / 24f; if (k <= 0) return; var p = ctx.painter2D; Func<float, float, Vector2> at = (x, y) => new Vector2(x * k, y * k);
            p.strokeColor = tint ?? resolvedStyle.color; p.fillColor = p.strokeColor; p.lineWidth = 2.4f * k; p.lineCap = LineCap.Butt; p.lineJoin = LineJoin.Miter;
            Action<float[]> line = pts => { p.BeginPath(); p.MoveTo(at(pts[0], pts[1])); for (int i = 2; i < pts.Length; i += 2) p.LineTo(at(pts[i], pts[i + 1])); p.Stroke(); };
            switch (kind)
            {
                case "arrow": line(new[] { 5f, 12, 19, 12 }); line(new[] { 13f, 6, 19, 12, 13, 18 }); break;
                case "left": line(new[] { 15f, 5, 8, 12, 15, 19 }); break;
                case "right": line(new[] { 9f, 5, 16, 12, 9, 19 }); break;
                case "copy": line(new[] { 9f, 9, 20, 9, 20, 20, 9, 20, 9, 9 }); line(new[] { 5f, 15, 4, 15, 4, 4, 15, 4, 15, 5 }); break;
                case "flag":
                    line(new[] { 5f, 3, 5, 21 });
                    p.BeginPath(); p.MoveTo(at(5, 4)); p.LineTo(at(19, 4)); p.LineTo(at(16, 9)); p.LineTo(at(19, 14)); p.LineTo(at(5, 14)); p.ClosePath(); p.Fill();
                    break;
            }
        }
    }
}
