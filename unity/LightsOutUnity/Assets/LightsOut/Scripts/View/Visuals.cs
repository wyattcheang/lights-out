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

        public static Material Mat(string key, Color color, Texture2D tex = null, float smooth = .2f, float metal = 0f, bool cutout = false, Vector2? tiling = null)
        {
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            EnsureShader();
            m = new Material(lit) { name = key };
            if (urp) { m.SetColor("_BaseColor", color); if (tex) m.SetTexture("_BaseMap", tex); m.SetFloat("_Smoothness", smooth); }
            else { m.SetColor("_Color", color); if (tex) m.SetTexture("_MainTex", tex); m.SetFloat("_Glossiness", smooth); }
            m.SetFloat("_Metallic", metal);
            if (tiling.HasValue) m.mainTextureScale = tiling.Value;
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

        static Texture2D asphalt, grass, gravel, kerb, barrier, fence, crowd, garage, check, city;
        public static Texture2D Asphalt { get { if (!asphalt) { var r = new System.Random(1); var b = Hex("#55585e"); asphalt = Tex(256, 256, (x, y) => Speckle(b, .35f, r)); } return asphalt; } }
        public static Texture2D Grass { get { if (!grass) { var r = new System.Random(2); var b = Hex("#3c7136"); grass = Tex(256, 256, (x, y) => { var c = Speckle(b, .3f, r); return x < 128 ? c * 1.06f : c; }); } return grass; } }
        public static Texture2D City { get { if (!city) { var r = new System.Random(7); var b = Hex("#6c6f74"); city = Tex(256, 256, (x, y) => (x % 128 < 2 || y % 128 < 2) ? b * .8f : Speckle(b, .2f, r)); } return city; } }
        public static Texture2D Gravel { get { if (!gravel) { var r = new System.Random(3); var b = Hex("#b6a785"); gravel = Tex(256, 256, (x, y) => Speckle(b, .5f, r)); } return gravel; } }
        public static Texture2D Kerb { get { if (!kerb) { var red = Hex("#d8322a"); var wht = Hex("#f3f0ea"); kerb = Tex(16, 64, (x, y) => y < 32 ? wht : red); } return kerb; } }
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
        static readonly string[][] Brands = { new[] { "#0d2a6b", "#ffffff" }, new[] { "#d8322a", "#ffffff" }, new[] { "#111111", "#ffd21f" }, new[] { "#ffffff", "#d8322a" }, new[] { "#0a6e5c", "#ffffff" }, new[] { "#ff8a1f", "#111111" } };
        public static Texture2D Barrier
        {
            get
            {
                if (!barrier) barrier = Tex(512, 64, (x, y) =>
                {
                    var br = Brands[(x / 128) % Brands.Length];
                    if (y < 18) return ((x / 16) % 2 == 0) ? Hex("#d8322a") : Hex("#eeeae2");
                    int lx = x % 128;
                    if (y > 24 && y < 58 && lx > 3 && lx < 124) return (lx > 20 && lx < 108 && y > 34 && y < 48 && (lx / 6) % 2 == 0) ? Hex(br[1]) : Hex(br[0]);
                    return Hex("#24272c");
                });
                return barrier;
            }
        }
        public static Texture2D Crowd { get { if (!crowd) { var r = new System.Random(5); string[] cs = { "#dd3333", "#ffdd22", "#2288ff", "#eeeeee", "#ff8800", "#22cc66", "#aa44ff", "#555555" }; crowd = Tex(256, 128, (x, y) => y % 16 < 3 ? Hex("#202329") : Hex(cs[r.Next(cs.Length)])); } return crowd; } }
        public static Texture2D Garage
        {
            get
            {
                if (!garage) { string[] cs = { "#e8322e", "#2f6bff", "#ff8a1f", "#13c4a3", "#c9ced6", "#7a3cff", "#ff4f9a", "#0b6b3a" }; garage = Tex(512, 128, (x, y) => { int i = x / 64, lx = x % 64; if (y > 108) return Hex("#1a1f27"); if (lx > 6 && lx < 58 && y < 86) return y > 78 ? Hex(cs[i % cs.Length]) : Hex("#2a2f37"); return Hex("#e9e7e1"); }); }
                return garage;
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
