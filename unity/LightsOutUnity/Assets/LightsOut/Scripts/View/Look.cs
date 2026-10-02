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

        static bool applied; static Material daySky, nightSky; static Cubemap nightCube; static Light headlamp, underglow; static readonly Light[] nearLamps = new Light[3];
        static Bloom bloom; static ColorAdjustments grade; static Vignette vignette; static ChromaticAberration aberration; static FilmGrain grain;
        static WhiteBalance balance; static ShadowsMidtonesHighlights tones; static MotionBlur motion;

        public static void Apply(bool nightRace, Light sun, Camera cam)
        {
            bool night = nightRace || CyberNight;
            if (!applied) daySky = DaySky(RenderSettings.skybox);
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

            // Filmic grade: ACES curve, a warm key and cool shadows by day, cooler and punchier at night, with a soft
            // highlight bloom, light vignette and grain. Motion blur follows the camera only, so the HUD-side car stays sharp.
            bloom.threshold.Override(night ? .9f : 1.05f); bloom.intensity.Override(night ? 1.1f : .35f); bloom.scatter.Override(night ? .72f : .6f);
            grade.contrast.Override(night ? 14f : 8f); grade.saturation.Override(night ? 8f : 6f); grade.postExposure.Override(night ? .3f : .3f);
            grade.colorFilter.Override(Color.white);
            balance.temperature.Override(night ? -8f : 3f); balance.tint.Override(night ? 4f : 0f);
            tones.shadows.Override(night ? new Vector4(.94f, .98f, 1.1f, 0f) : new Vector4(.95f, 1f, 1.07f, -.02f));
            tones.highlights.Override(night ? new Vector4(1f, 1f, 1f, 0f) : new Vector4(1.03f, 1.01f, .97f, 0f));
            vignette.intensity.Override(night ? .26f : .2f); vignette.smoothness.Override(.45f);
            aberration.intensity.Override(night ? .05f : .025f); grain.intensity.Override(night ? .16f : .08f);
            motion.intensity.Override(.3f);
        }

        // Daylight: a slightly thicker, less saturated atmosphere with a pale haze at the horizon.
        static Material DaySky(Material source)
        {
            if (source == null || !source.HasProperty("_AtmosphereThickness")) return source;
            var m = new Material(source);
            m.SetFloat("_AtmosphereThickness", 1.25f); m.SetFloat("_Exposure", 1.25f); m.SetColor("_SkyTint", new Color(.56f, .57f, .6f)); m.SetColor("_GroundColor", new Color(.62f, .66f, .7f));
            return m;
        }

        /// A headlamp beam and a neon underglow on the car the camera follows.
        public static void FollowCar(Transform car, bool player)
        {
            if (headlamp == null)
            {
                headlamp = new GameObject("Headlamp").AddComponent<Light>(); headlamp.type = LightType.Spot; headlamp.range = 110; headlamp.spotAngle = 70; headlamp.innerSpotAngle = 24;
                headlamp.color = new Color(.78f, .9f, 1f); headlamp.intensity = 70; headlamp.shadows = LightShadows.None;
                underglow = new GameObject("Underglow").AddComponent<Light>(); underglow.type = LightType.Spot; underglow.spotAngle = 160; underglow.innerSpotAngle = 100; underglow.range = 2.4f; underglow.intensity = 14; underglow.shadows = LightShadows.None;
            }
            headlamp.enabled = underglow.enabled = Night && car != null;
            if (!headlamp.enabled) return;
            headlamp.transform.position = car.position + car.forward * 3.2f + car.up * .9f; headlamp.transform.rotation = car.rotation * Quaternion.Euler(3f, 0, 0);
            underglow.transform.position = car.position + car.up * .7f - car.forward * .4f; underglow.transform.rotation = car.rotation * Quaternion.Euler(90f, 0, 0);   // points down, so it only tints the road underglow.color = player ? Magenta : Cyan;
        }

        /// Up to three pooled headlamps for the cars closest to the camera; every other car stays emissive only.
        public static void LightNearbyCars(System.Collections.Generic.IList<Transform> cars)
        {
            for (int i = 0; i < nearLamps.Length; i++)
            {
                if (nearLamps[i] == null)
                {
                    var l = nearLamps[i] = new GameObject("NearLamp" + i).AddComponent<Light>(); l.type = LightType.Spot; l.range = 60; l.spotAngle = 60; l.innerSpotAngle = 22;
                    l.color = new Color(.78f, .9f, 1f); l.intensity = 34; l.shadows = LightShadows.None;
                }
                bool on = Night && cars != null && i < cars.Count && cars[i] != null; nearLamps[i].enabled = on;
                if (on) { var car = cars[i]; nearLamps[i].transform.position = car.position + car.forward * 3.2f + car.up * .9f; nearLamps[i].transform.rotation = car.rotation * Quaternion.Euler(3f, 0, 0); }
            }
        }

        static void EnsureVolume(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
            // MSAA handles geometry edges; SMAA on top settles the fences, crowd and other alpha-tested detail
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High; data.dithering = true;
            if (bloom != null) return;
            var go = new GameObject("Look Volume"); Object.DontDestroyOnLoad(go);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>(); vol.sharedProfile = p;
            p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            bloom = p.Add<Bloom>(true); bloom.highQualityFiltering.Override(true); grade = p.Add<ColorAdjustments>(true); vignette = p.Add<Vignette>(true); aberration = p.Add<ChromaticAberration>(true);
            grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1);
            balance = p.Add<WhiteBalance>(true); tones = p.Add<ShadowsMidtonesHighlights>(true);
            motion = p.Add<MotionBlur>(true); motion.mode.Override(MotionBlurMode.CameraOnly); motion.quality.Override(MotionBlurQuality.Medium); motion.clamp.Override(.03f);
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
            Color top = new Color(.012f, .018f, .07f), haze = new Color(.09f, .06f, .24f), ground = new Color(.004f, .004f, .012f), warm = new Color(.5f, .12f, .55f), cool = new Color(.1f, .3f, .6f);
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
                        float glow = Mathf.Exp(-e * e * (e > 0 ? 7f : 220f)) * (.75f + .25f * Mathf.Sin(az * 2f + 1.3f));
                        var c = (e > 0 ? Color.Lerp(haze, top, Mathf.SmoothStep(0, .85f, e)) : ground) + hue * (glow * .6f);
                        c.a = 1; px[y * S + x] = c;
                    }
                nightCube.SetPixels(px, (CubemapFace)f);
            }
            nightCube.Apply(true); return nightCube;
        }
    }
}
