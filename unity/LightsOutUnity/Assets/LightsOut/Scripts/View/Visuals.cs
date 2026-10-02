// Procedural textures, materials and mesh helpers. Works with URP (Lit) or the built-in pipeline (Standard).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightsOut
{
    public static class Visuals
    {
        static Shader lit;
        static bool urp;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Vector3 World(float x, float y, float elev) { return new Vector3(x, elev, -y); }
        public static Quaternion Yaw(float h) { return Quaternion.Euler(0f, 90f + h * Mathf.Rad2Deg, 0f); }

        public static Color Hex(string hex) { Color c; return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta; }

        static void EnsureShader()
        {
            if (lit != null) return;
            lit = Shader.Find("Universal Render Pipeline/Lit");
            urp = lit != null;
            if (!urp) lit = Shader.Find("Standard");
        }

        /// Drops cached materials so the next circuit is built for the current day/night look.
        public static void ResetMaterials() { foreach (var m in cache.Values) if (m) Object.Destroy(m); cache.Clear(); }

        /// 'emission' is an HDR colour (see Look.Hdr); it is multiplied by 'emissionMap', or by 'tex' when no map is given.
        public static Material Mat(string key, Color color, Texture2D tex = null, float smooth = .2f, float metal = 0f, bool cutout = false, Vector2? tiling = null, Color? emission = null, Texture2D emissionMap = null)
        {
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            EnsureShader();
            m = new Material(lit) { name = key };
            if (urp) { m.SetColor("_BaseColor", color); if (tex) m.SetTexture("_BaseMap", tex); m.SetFloat("_Smoothness", smooth); }
            else { m.SetColor("_Color", color); if (tex) m.SetTexture("_MainTex", tex); m.SetFloat("_Glossiness", smooth); }
            m.SetFloat("_Metallic", metal);
            if (tiling.HasValue) m.mainTextureScale = tiling.Value;
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                var map = emissionMap ? emissionMap : tex; if (map) m.SetTexture("_EmissionMap", map);
            }
            if (cutout)
            {
                if (urp) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", .3f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 0); }
                else { m.SetFloat("_Mode", 1); m.SetFloat("_Cutoff", .3f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
            }
            cache[key] = m;
            return m;
        }

        public static Material Unlit(string key, Color color)
        {
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            m = new Material(sh) { name = key };
            m.SetColor("_BaseColor", color); m.SetColor("_Color", color);
            cache[key] = m;
            return m;
        }

        /// Unlit neon: bright enough to bloom at night, toned down in daylight.
        public static Material Neon(string key, Color color, float intensity) { float k = Mathf.Max(1f, intensity * Look.Glow); return Unlit(key, new Color(color.r * k, color.g * k, color.b * k, 1)); }

        public static void SetColor(Material m, Color c) { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c); }

        // ---- textures ----
        static Texture2D Tex(int w, int h, System.Func<int, int, Color> f, bool repeat = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            t.SetPixels(px); t.Apply(true);
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.anisoLevel = 8; t.filterMode = FilterMode.Trilinear;
            return t;
        }
        static Color Speckle(Color a, float amt, System.Random r) { float k = 1f + ((float)r.NextDouble() - .5f) * amt; return new Color(a.r * k, a.g * k, a.b * k, 1); }

        static Texture2D asphalt, grass, gravel, kerb, barrier, fence, crowd, garage, check, city, puff, windows, facade, astro;
        public static Texture2D Asphalt { get { if (!asphalt) { var r = new System.Random(1); var b = Hex("#55585e"); asphalt = Tex(256, 256, (x, y) => Speckle(b, .2f, r)); } return asphalt; } }
        // groomed grass: mowing stripes, soft large-scale patches and only a light speckle
        public static Texture2D Grass
        {
            get
            {
                if (!grass)
                {
                    var r = new System.Random(2); var b = Hex("#3c7136"); const float T = Mathf.PI * 2 / 512f;
                    grass = Tex(512, 512, (x, y) =>
                    {
                        float stripe = (x / 64) % 2 == 0 ? 1.07f : .95f;
                        float patch = 1f + .07f * Mathf.Sin(x * T * 2 + 1.3f) * Mathf.Sin(y * T * 3 + .4f) + .05f * Mathf.Sin((x + y) * T * 5);
                        return Speckle(b, .12f, r) * (stripe * patch);
                    });
                }
                return grass;
            }
        }
        // artificial turf beside the track: darker green with fine lengthwise ribs
        public static Texture2D Astro { get { if (!astro) { var r = new System.Random(4); var b = Hex("#2c5a33"); astro = Tex(64, 64, (x, y) => Speckle(b, .1f, r) * (x % 8 < 4 ? 1.06f : .94f)); } return astro; } }
        public static Texture2D City { get { if (!city) { var r = new System.Random(7); var b = Hex("#6c6f74"); city = Tex(256, 256, (x, y) => (x % 128 < 2 || y % 128 < 2) ? b * .8f : Speckle(b, .2f, r)); } return city; } }
        public static Texture2D Gravel { get { if (!gravel) { var r = new System.Random(3); var b = Hex("#b6a785"); gravel = Tex(256, 256, (x, y) => Speckle(b, .5f, r)); } return gravel; } }
        // neon kerbs: cyan and magenta blocks separated by dark gaps (v runs along the track)
        public static Texture2D Kerb { get { if (!kerb) { var dark = Hex("#0b0d14"); kerb = Tex(16, 64, (x, y) => y < 16 ? Look.Cyan : y < 32 ? dark : y < 48 ? Look.Magenta : dark); } return kerb; } }
        // 2x2 twill carbon weave: tows alternate direction every cell and are shaded across their width
        public static Texture2D Carbon
        {
            get
            {
                if (!carbonTex) carbonTex = Tex(64, 64, (x, y) =>
                {
                    int cx = x / 16, cy = y / 16; bool along = ((cx + cy) & 1) == 0;
                    float u = ((along ? y : x) % 16) / 15f, sheen = .55f + .45f * Mathf.Sin(u * Mathf.PI);
                    float k = along ? .8f + .35f * sheen : .45f + .3f * sheen;
                    return new Color(.13f * k, .135f * k, .145f * k, 1);
                });
                return carbonTex;
            }
        }
        static Texture2D carbonTex;
        /// A race number in the UI's condensed bold face: the ink colour everywhere, with the digits in the alpha channel.
        public static Texture2D NumberTexture(int number, Color ink)
        {
            const int W = 256, H = 192, Size = 150; string s = number.ToString(); var font = UiKit.Head;
            font.RequestCharactersInTexture(s, Size, FontStyle.Bold);
            float width = 0; CharacterInfo ci; foreach (char ch in s) if (font.GetCharacterInfo(ch, out ci, Size, FontStyle.Bold)) width += ci.advance;
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32); var prev = RenderTexture.active; RenderTexture.active = rt;
            GL.Clear(true, true, new Color(ink.r, ink.g, ink.b, 0));
            GL.PushMatrix(); GL.LoadPixelMatrix(0, W, 0, H); font.material.SetPass(0); GL.Begin(GL.QUADS); GL.Color(ink);
            float x = (W - width) / 2, y = H / 2 - Size * .36f;
            foreach (char ch in s)
            {
                if (!font.GetCharacterInfo(ch, out ci, Size, FontStyle.Bold)) continue;
                GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(x + ci.minX, y + ci.minY, 0); GL.TexCoord(ci.uvTopLeft); GL.Vertex3(x + ci.minX, y + ci.maxY, 0);
                GL.TexCoord(ci.uvTopRight); GL.Vertex3(x + ci.maxX, y + ci.maxY, 0); GL.TexCoord(ci.uvBottomRight); GL.Vertex3(x + ci.maxX, y + ci.minY, 0);
                x += ci.advance;
            }
            GL.End(); GL.PopMatrix();
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            t.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            // the text shader blends alpha with itself; take the square root back and keep the colour solid
            var px = t.GetPixels(); for (int i = 0; i < px.Length; i++) px[i] = new Color(ink.r, ink.g, ink.b, Mathf.Sqrt(px[i].a));
            t.SetPixels(px); t.Apply(true); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            return t;
        }
        public static Texture2D Puff { get { if (!puff) puff = Tex(32, 32, (x, y) => { float d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 15.5f; return new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)); }, false); return puff; } }
        public static Texture2D Check { get { if (!check) check = Tex(64, 8, (x, y) => ((x / 4 + y / 4) % 2 == 0) ? Hex("#f2f0ea") : Hex("#111111")); return check; } }
        public static Texture2D Fence
        {
            get
            {
                if (!fence) fence = Tex(64, 64, (x, y) =>
                {
                    bool wire = ((x + y) % 8 == 0) || ((x - y + 64) % 8 == 0); bool post = x < 3 || y > 61;
                    return post ? Hex("#6b7077") : wire ? new Color(.8f, .82f, .84f, .8f) : new Color(0, 0, 0, 0);
                });
                return fence;
            }
        }
        // dark panels with neon sponsor blocks; u runs up the wall, v along it. The top band is a continuous light strip.
        static readonly string[][] Brands = { new[] { "#0a0d1a", "#00E5FF" }, new[] { "#140818", "#FF2BD6" }, new[] { "#0d0d10", "#FFB000" }, new[] { "#060a14", "#7CFFEA" }, new[] { "#12071c", "#B56BFF" }, new[] { "#05060d", "#FF2BD6" } };
        public static Texture2D Barrier
        {
            get
            {
                if (!barrier) barrier = Tex(64, 384, (x, y) =>
                {
                    var br = Brands[(y / 64) % Brands.Length]; int ly = y % 64;
                    if (x >= 59) return Look.Cyan;
                    if (x < 6 || x > 54 || ly < 2 || ly > 61) return Hex("#07080d");
                    return (x > 18 && x < 44 && ly > 10 && ly < 54 && (ly / 6) % 2 == 0) ? Hex(br[1]) : Hex(br[0]);
                });
                return barrier;
            }
        }
        // a calm crowd: mostly dark seats and clothing with a few bright accents that glow at night. Each person is a
        // 4x4 block so the texture holds together at a distance instead of shimmering.
        public static Texture2D Crowd
        {
            get
            {
                if (!crowd)
                {
                    string[] dark = { "#1d2027", "#262a33", "#30343d", "#3b3f48" }, acc = { "#00E5FF", "#FF2BD6", "#FFB000", "#e9edf2", "#7a8089" }; var cell = new Color[64 * 32]; var r = new System.Random(5);
                    for (int i = 0; i < cell.Length; i++) cell[i] = r.NextDouble() < .86 ? Hex(dark[r.Next(dark.Length)]) : Hex(acc[r.Next(acc.Length)]);
                    crowd = Tex(256, 128, (x, y) => y % 16 < 3 ? Hex("#14161b") : cell[(y / 4) * 64 + x / 4]);
                }
                return crowd;
            }
        }
        // pit garages: dark wall, darker door openings with a team-coloured light bar, and a cyan strip under the roof (u runs up, v along)
        public static Texture2D Garage
        {
            get
            {
                if (!garage) { string[] cs = { "#FF2BD6", "#00E5FF", "#FFB000", "#7CFFEA", "#B56BFF", "#FF5A3C", "#3D7BFF", "#39FF88" }; garage = Tex(128, 256, (x, y) => { int i = y / 32, ly = y % 32; if (x > 118) return Look.Cyan; if (ly > 3 && ly < 29 && x < 84) return x > 76 ? Hex(cs[i % cs.Length]) : Hex("#04050a"); return Hex("#12141b"); }); }
                return garage;
            }
        }
        // modern facade: pale panels with horizontal bands of dark glass and thin mullions
        public static Texture2D Facade { get { if (!facade) facade = Tex(64, 64, (x, y) => (y % 16 < 9 && x % 16 > 0) ? Hex("#2a3948") : Hex("#dde2e6")); return facade; } }
        // building windows for the emission map: mostly dark, a few lit in warm white, cyan or magenta
        public static Texture2D Windows
        {
            get
            {
                if (!windows)
                {
                    var r = new System.Random(11); var lit = new Color[64];
                    for (int i = 0; i < 64; i++) { double d = r.NextDouble(); lit[i] = d > .27 ? Color.black : d > .07 ? new Color(1f, .82f, .58f) : d > .035 ? Look.Cyan : Look.Magenta; }
                    windows = Tex(64, 64, (x, y) => (x % 8 > 1 && x % 8 < 6 && y % 8 > 1 && y % 8 < 7) ? lit[(y / 8) * 8 + x / 8] : Color.black);
                }
                return windows;
            }
        }

        // ---- mesh helpers ----
        /// Adds a quad (a,b,c,d in order around the face); if 'up' is set the winding is fixed so the face points upward.
        public static void Quad(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, bool up, bool twoSided)
        {
            int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            Vector3 n = Vector3.Cross(b - a, c - a);
            bool flip = up && n.y < 0;
            if (!flip) { tri.Add(k); tri.Add(k + 1); tri.Add(k + 2); tri.Add(k); tri.Add(k + 2); tri.Add(k + 3); }
            else { tri.Add(k); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k); tri.Add(k + 3); tri.Add(k + 2); }
            if (twoSided) { tri.Add(k); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k); tri.Add(k + 3); tri.Add(k + 2); }
        }

        public static GameObject MeshObject(string name, Transform parent, List<Vector3> v, List<Vector2> uv, List<int> tri, Material m, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; mr.receiveShadows = true;
            return go;
        }

        static Mesh cube, cyl, sphere;
        public static Mesh Cube { get { if (!cube) cube = PrimMesh(PrimitiveType.Cube); return cube; } }
        public static Mesh Cylinder { get { if (!cyl) cyl = PrimMesh(PrimitiveType.Cylinder); return cyl; } }
        public static Mesh Sphere { get { if (!sphere) sphere = PrimMesh(PrimitiveType.Sphere); return sphere; } }
        static Mesh PrimMesh(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(g); return m; }
    }
}
