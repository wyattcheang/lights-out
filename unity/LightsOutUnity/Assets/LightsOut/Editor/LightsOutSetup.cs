// Editor helper: "Lights Out > Set Up Scene" builds a ready-to-play scene with the game controller,
// a NetworkManager + UnityTransport (used by Multiplayer Services / Relay for online rooms), sun and camera.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace LightsOut.EditorTools
{
    public static class LightsOutSetup
    {
        const string ScenePath = "Assets/LightsOut/LightsOut.unity";

        [MenuItem("Lights Out/Set Up Scene")]
        public static void SetUpScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var sun = Object.FindFirstObjectByType<Light>();
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
            Debug.Log("Lights Out: scene saved to " + ScenePath + " and added to Build Settings. Press Play.");
        }
    }
}
