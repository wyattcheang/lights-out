// Drawing kit for the IMGUI menus and HUD: glass panels, rounded fills, pills, segmented bars, rings and text.
// Everything is drawn into rects on the 1080-high virtual canvas; shapes are anti-aliased textures made at runtime.
using System.Collections.Generic;
using UnityEngine;

namespace LightsOut
{
    public static class UiKit
    {
        public static readonly Color Text = new Color(.93f, .96f, .98f), Dim = new Color(.56f, .63f, .71f), Faint = new Color(.4f, .46f, .54f);
        public static readonly Color Glass = new Color(.025f, .035f, .065f, .76f), Ink = new Color(.02f, .04f, .07f);

        static Font head;
        /// Condensed sans for labels and headings.
        public static Font Head { get { return head ? head : (head = Font.CreateDynamicFontFromOSFont(new[] { "Avenir Next Condensed", "Bahnschrift", "Arial Narrow", "Segoe UI", "Arial" }, 18)); } }
        /// Numbers use the same condensed family, always in its bold weight, so timing and speed read like a dash display.
        public static Font Num { get { return Head; } }

        // ---- shapes ----
        public static void Fill(Rect r, Color c, float radius = 0) { GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, 0, radius); }
        public static void Outline(Rect r, Color c, float width, float radius) { GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, width, radius); }
        public static void Tex(Rect r, Texture t, Color c) { GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true, 0, c, 0, 0); }

        /// Dark translucent glass with a soft top sheen and a hairline edge.
        public static void Panel(Rect r, float radius = 12, float opacity = .76f)
        {
            Fill(r, new Color(Glass.r, Glass.g, Glass.b, opacity), radius);
            Tex(new Rect(r.x + radius, r.y + 1, r.width - radius * 2, Mathf.Min(48, r.height)), FadeDown, new Color(1, 1, 1, .045f));
            Outline(r, new Color(1, 1, 1, .09f), 1, radius);
        }
        /// A short coloured line that fades out to the right: the accent under headings.
        public static void Accent(Rect r, Color c) { Tex(r, FadeRight, c); }
        public static void Pill(Rect r, string s, Color bg, Color fg, int size = 12) { Fill(r, bg, r.height / 2); Label(r, s, size, fg, TextAnchor.MiddleCenter, false, true); }
        /// A pill that is filled when active and a dim outline when not.
        public static void Lamp(Rect r, string s, bool on, Color c) { if (on) Pill(r, s, c, Ink); else { Outline(r, new Color(1, 1, 1, .16f), 1, r.height / 2); Label(r, s, 12, Faint, TextAnchor.MiddleCenter, false, true); } }

        /// A row of slanted segments, lit from the left up to 'frac'.
        public static void SegBar(Rect r, int n, float frac, System.Func<int, Color> on)
        {
            float gap = 3, w = (r.width - gap * (n - 1)) / n; int lit = Mathf.RoundToInt(Mathf.Clamp01(frac) * n);
            for (int i = 0; i < n; i++) Tex(new Rect(r.x + i * (w + gap), r.y, w + r.height * .3f, r.height), Slant, i < lit ? on(i) : new Color(1, 1, 1, .1f));
        }
        public static void Dot(Vector2 centre, float size, Color c) { Tex(new Rect(centre.x - size / 2, centre.y - size / 2, size, size), Disc, c); }
        /// Tyre compound marker: a coloured ring with the compound letter inside.
        public static void TyreRing(Rect r, Color c, string letter)
        {
            Tex(r, Ring, c); Label(r, letter, Mathf.RoundToInt(r.height * .5f), Text, TextAnchor.MiddleCenter, true, true);
        }

        // ---- text ----
        static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        public static void Label(Rect r, string s, int size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool number = false, bool bold = false)
        {
            int key = size | ((int)a << 8) | (number ? 1 << 16 : 0) | (bold ? 1 << 17 : 0); GUIStyle st;
            if (!styles.TryGetValue(key, out st)) { st = new GUIStyle { font = number ? Num : Head, fontSize = size, alignment = a, fontStyle = bold || number ? FontStyle.Bold : FontStyle.Normal, wordWrap = false, clipping = TextClipping.Overflow, richText = true }; styles[key] = st; }
            st.normal.textColor = c; GUI.Label(r, s, st);
        }

        // ---- generated textures (white, with the shape in the alpha channel) ----
        static Texture2D fadeDown, fadeRight, slant, disc, ring, glow;
        static Texture2D Make(int w, int h, System.Func<float, float, float> alpha)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(alpha(x + .5f, y + .5f)));
            t.SetPixels(px); t.Apply(); return t;
        }
        public static Texture2D FadeDown { get { return fadeDown ? fadeDown : (fadeDown = Make(1, 32, (x, y) => y / 32f)); } }
        public static Texture2D FadeRight { get { return fadeRight ? fadeRight : (fadeRight = Make(64, 1, (x, y) => 1 - x / 64f)); } }
        public static Texture2D Slant { get { return slant ? slant : (slant = Make(32, 32, (x, y) => { float lean = 9f * (1 - y / 32f); return Mathf.Min(x - lean, (23 + lean) - x) + .5f; })); } }
        public static Texture2D Disc { get { return disc ? disc : (disc = Make(64, 64, (x, y) => 30.5f - Mathf.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32)))); } }
        public static Texture2D Ring { get { return ring ? ring : (ring = Make(64, 64, (x, y) => { float d = Mathf.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32)); return Mathf.Min(30.5f - d, d - 22.5f); })); } }
        public static Texture2D Glow { get { return glow ? glow : (glow = Make(64, 64, (x, y) => { float d = Mathf.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32)) / 32f; return (1 - d) * (1 - d); })); } }

        /// A 9-slice rounded rectangle for button and field backgrounds. 'to' gives a left-to-right gradient.
        public static Texture2D Round9(Color fill, Color edge, int radius = 9, Color? to = null)
        {
            int w = to.HasValue ? 96 : 32, h = 32; var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float qx = Mathf.Abs(x + .5f - w / 2f) - (w / 2f - radius), qy = Mathf.Abs(y + .5f - h / 2f) - (h / 2f - radius);
                    float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;   // signed distance to the rounded rect
                    var c = to.HasValue ? Color.Lerp(fill, to.Value, x / (float)(w - 1)) : fill;
                    if (edge.a > 0) c = Color.Lerp(c, edge, Mathf.Clamp01(d + 1.6f));
                    c.a *= Mathf.Clamp01(.5f - d); px[y * w + x] = c;
                }
            t.SetPixels(px); t.Apply(); return t;
        }
    }
}
