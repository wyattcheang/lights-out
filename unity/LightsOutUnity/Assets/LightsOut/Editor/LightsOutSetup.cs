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
        static readonly string[] CodeShaders = { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit" };

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
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int level = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = pipeline; }
            QualitySettings.SetQualityLevel(level, false);
        }

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
