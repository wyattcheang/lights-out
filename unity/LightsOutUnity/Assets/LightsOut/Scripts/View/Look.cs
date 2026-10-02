// The visual direction in one place: palette, day/night lighting, fog, reflections and post-processing.
// Materials read Look.Night and Look.Glow when they are created, so Apply() runs before a circuit is built.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LightsOut
{
    public static class Look
    {
        /// Forces the neon night look on every circuit. Off by default: each circuit's own day/night setting decides.
        public static bool CyberNight = false;

        public static readonly Color Base = Visuals.Hex("#05060d"), Cyan = Visuals.Hex("#00E5FF"), Magenta = Visuals.Hex("#FF2BD6"), Amber = Visuals.Hex("#FFB000");
        public static bool Night { get; private set; }
        /// Emission multiplier: full at night, a subtle glow in daylight.
        public static float Glow { get; private set; } = 1f;
        public static float Pick(float day, float night) { return Night ? night : day; }
        public static Color Pick(Color day, Color night) { return Night ? night : day; }
        /// HDR colour for emissive surfaces, already scaled for the time of day.
        public static Color Hdr(Color c, float intensity) { float k = intensity * Glow; return new Color(c.r * k, c.g * k, c.b * k, 1); }

        static bool applied; static Material daySky, nightSky; static Cubemap nightCube;
        static Bloom bloom; static ColorAdjustments grade; static Vignette vignette; static ChromaticAberration aberration; static FilmGrain grain;

        public static void Apply(bool nightRace, Light sun, Camera cam)
        {
            bool night = nightRace || CyberNight;
            if (!applied) daySky = RenderSettings.skybox;
            if (!applied || night != Night) Visuals.ResetMaterials();
            applied = true; Night = night; Glow = night ? 1f : .35f;
            EnsureVolume(cam);

            sun.color = night ? new Color(.5f, .66f, 1f) : new Color(1f, .97f, .93f); sun.intensity = night ? .85f : 1.2f;
            RenderSettings.fog = true; RenderSettings.fogMode = night ? FogMode.ExponentialSquared : FogMode.Linear;
            RenderSettings.fogDensity = .0018f; RenderSettings.fogStartDistance = 380; RenderSettings.fogEndDistance = 2600;
            RenderSettings.fogColor = night ? new Color(.1f, .05f, .2f) : new Color(.76f, .83f, .9f);
            RenderSettings.skybox = night ? NightSky() : daySky;
            RenderSettings.ambientMode = night ? AmbientMode.Trilight : AmbientMode.Skybox; RenderSettings.ambientIntensity = 1f;
            RenderSettings.ambientSkyColor = new Color(.12f, .16f, .36f); RenderSettings.ambientEquatorColor = new Color(.26f, .1f, .3f); RenderSettings.ambientGroundColor = new Color(.02f, .02f, .045f);
            RenderSettings.defaultReflectionMode = night ? DefaultReflectionMode.Custom : DefaultReflectionMode.Skybox;
            if (night) RenderSettings.customReflectionTexture = NightCube();
            RenderSettings.reflectionIntensity = night ? 1f : .8f;
            cam.clearFlags = CameraClearFlags.Skybox; cam.backgroundColor = Base; cam.allowHDR = true;
            if (!night) DynamicGI.UpdateEnvironment();

            bloom.threshold.Override(night ? .9f : 1.1f); bloom.intensity.Override(night ? 1.2f : .5f); bloom.scatter.Override(.7f);
            grade.contrast.Override(15f); grade.saturation.Override(night ? 10f : 4f); grade.postExposure.Override(night ? .3f : 0f);
            grade.colorFilter.Override(night ? Color.white : new Color(.96f, .98f, 1f));
            vignette.intensity.Override(night ? .25f : .16f); aberration.intensity.Override(night ? .08f : .04f); grain.intensity.Override(night ? .22f : .1f);
        }

        static void EnsureVolume(Camera cam)
        {
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            if (bloom != null) return;
            var go = new GameObject("Look Volume"); Object.DontDestroyOnLoad(go);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>(); vol.sharedProfile = p;
            p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            bloom = p.Add<Bloom>(true); grade = p.Add<ColorAdjustments>(true); vignette = p.Add<Vignette>(true); aberration = p.Add<ChromaticAberration>(true);
            grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1);
        }

        // Night sky: deep indigo overhead with a smooth violet city glow along the horizon. The same cubemap is the
        // reflection source, so glossy paint and wet asphalt pick up a soft gradient instead of hard colour bands.
        static Material NightSky()
        {
            if (nightSky) return nightSky;
            var sh = Shader.Find("Skybox/Cubemap"); if (sh == null) return null;
            nightSky = new Material(sh); nightSky.SetTexture("_Tex", NightCube()); nightSky.SetColor("_Tint", new Color(.5f, .5f, .5f, 1)); nightSky.SetFloat("_Exposure", 1f);
            return nightSky;
        }
        static Cubemap NightCube()
        {
            if (nightCube) return nightCube;
            const int S = 64; nightCube = new Cubemap(S, TextureFormat.RGBAHalf, true);
            Color top = new Color(.012f, .018f, .06f), ground = new Color(.004f, .004f, .012f), warm = new Color(.5f, .12f, .55f), cool = new Color(.1f, .3f, .6f);
            for (int f = 0; f < 6; f++)
            {
                var px = new Color[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                    {
                        float u = (x + .5f) / S * 2 - 1, v = (y + .5f) / S * 2 - 1; Vector3 d;
                        switch ((CubemapFace)f)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1, -v, -u); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1, -v, u); break;
                            case CubemapFace.PositiveY: d = new Vector3(u, 1, v); break;
                            case CubemapFace.NegativeY: d = new Vector3(u, -1, -v); break;
                            case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1); break;
                            default: d = new Vector3(-u, -v, -1); break;
                        }
                        d.Normalize(); float az = Mathf.Atan2(d.z, d.x), e = d.y;
                        var hue = Color.Lerp(warm, cool, .5f + .5f * Mathf.Sin(az + .6f));
                        float glow = Mathf.Exp(-e * e * (e > 0 ? 28f : 220f)) * (.75f + .25f * Mathf.Sin(az * 2f + 1.3f));
                        var c = (e > 0 ? Color.Lerp(top * 2.2f, top, Mathf.SmoothStep(0, .6f, e)) : ground) + hue * (glow * .5f);
                        c.a = 1; px[y * S + x] = c;
                    }
                nightCube.SetPixels(px, (CubemapFace)f);
            }
            nightCube.Apply(true); return nightCube;
        }
    }
}
