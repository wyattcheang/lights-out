// Editor helper: "Lights Out > Set Up Scene" builds a ready-to-play scene with the game controller,
// a NetworkManager + UnityTransport (used by Multiplayer Services / Relay for online rooms), sun and camera.
// It also creates and assigns a URP pipeline asset and keeps the shaders the game creates from code in player builds.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace LightsOut.EditorTools
{
    public static class LightsOutSetup
    {
        const string ScenePath = "Assets/LightsOut/LightsOut.unity";
        const string SettingsDir = "Assets/LightsOut/Settings";
        const string RendererPath = SettingsDir + "/LightsOut_Renderer.asset", PipelinePath = SettingsDir + "/LightsOut_URP.asset";
        static readonly string[] CodeShaders = { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit", "Skybox/Cubemap", "Universal Render Pipeline/Complex Lit", "Shader Graphs/Decal" };

        [MenuItem("Lights Out/Set Up Scene")]
        public static void SetUpScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SetUpRenderPipeline();
            IncludeCodeShaders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null) { sun.name = "Sun"; sun.shadows = LightShadows.Soft; sun.shadowStrength = .85f; }
            var cam = Camera.main;
            if (cam != null) { cam.nearClipPlane = .05f; cam.farClipPlane = 6000f; cam.allowHDR = true; }

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.ConnectionApproval = false;
            nm.NetworkConfig.EnableSceneManagement = false;   // everything is built procedurally; no scene sync needed

            new GameObject("LightsOut").AddComponent<GameController>();

            PlayerSettings.runInBackground = true;            // the host keeps simulating when the window loses focus
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Lights Out: scene saved to " + ScenePath + " and added to Build Settings. Press Play.");
        }

        // Without a pipeline asset Unity falls back to the built-in pipeline, which draws the URP materials magenta.
        static void SetUpRenderPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                if (!AssetDatabase.IsValidFolder(SettingsDir)) AssetDatabase.CreateFolder("Assets/LightsOut", "Settings");
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.supportsHDR = true; pipeline.shadowDistance = 400f; pipeline.msaaSampleCount = 4;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            // bloom, film grain and the other effects in Look.cs need the package's post-processing resources on the renderer
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData != null && rendererData.postProcessData == null)
            {
                rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                EditorUtility.SetDirty(rendererData);
            }
            if (!pipeline.supportsHDR) { pipeline.supportsHDR = true; EditorUtility.SetDirty(pipeline); }
            // the road is one mesh lit by the headlamp, underglow and up to three nearby cars' lamps
            if (pipeline.maxAdditionalLightsCount < 8) { pipeline.maxAdditionalLightsCount = 8; EditorUtility.SetDirty(pipeline); }
            // sharp, soft-edged sun shadows near the camera, and a 64-bit HDR buffer so the night sky does not band
            var ps = new SerializedObject(pipeline);
            ps.FindProperty("m_ShadowDistance").floatValue = 260f; ps.FindProperty("m_ShadowCascadeCount").intValue = 4; ps.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            ps.FindProperty("m_SoftShadowsSupported").boolValue = true; ps.FindProperty("m_HDRColorBufferPrecision").intValue = 1;
            ps.ApplyModifiedPropertiesWithoutUndo();
            if (rendererData != null)
            {
                // contact shadows under the cars and in panel gaps
                var ao = Feature<ScreenSpaceAmbientOcclusion>(rendererData, "Ambient Occlusion");
                { var s = new SerializedObject(ao); s.FindProperty("m_Settings.Radius").floatValue = .3f; s.FindProperty("m_Settings.Intensity").floatValue = .7f; s.FindProperty("m_Settings.DirectLightingStrength").floatValue = .2f; s.FindProperty("m_Settings.Falloff").floatValue = 120f; s.FindProperty("m_Settings.Samples").enumValueIndex = 0; s.FindProperty("m_Settings.AOMethod").enumValueIndex = 1; s.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(ao); }
                Feature<DecalRendererFeature>(rendererData, "Decals");   // race numbers (CarView.Numbers)
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int level = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = pipeline; }
            QualitySettings.SetQualityLevel(level, false);
        }

        /// The renderer asset's feature of this type, added first when it is missing.
        static T Feature<T>(UniversalRendererData data, string name) where T : ScriptableRendererFeature
        {
            var have = data.rendererFeatures.Find(f => f is T) as T; if (have != null) return have;
            var feature = ScriptableObject.CreateInstance<T>(); feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, data);
            string guid; long id; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out guid, out id);
            var so = new SerializedObject(data); SerializedProperty list = so.FindProperty("m_RendererFeatures"), map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(data);
            return feature;
        }

        /// Batch entry point: refreshes the pipeline assets without rebuilding the scene.
        public static void UpgradeRendering() { SetUpRenderPipeline(); IncludeCodeShaders(); AssetDatabase.SaveAssets(); }

        // These shaders are only referenced from code, so a player build would strip them.
        static void IncludeCodeShaders()
        {
            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in CodeShaders)
            {
                var shader = Shader.Find(name); if (shader == null) continue;
                bool present = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
