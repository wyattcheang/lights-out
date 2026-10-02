// Pressing Play always starts the game scene, even when Unity opened an empty "Untitled" scene,
// and the game scene is opened on load when nothing else is.
using UnityEditor;
using UnityEditor.SceneManagement;

namespace LightsOut.EditorTools
{
    [InitializeOnLoad]
    static class LightsOutStartScene
    {
        const string ScenePath = "Assets/LightsOut/LightsOut.unity";

        static LightsOutStartScene()
        {
            EditorApplication.delayCall += () =>
            {
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                if (scene == null) return;
                EditorSceneManager.playModeStartScene = scene;
                if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEditorInternal.InternalEditorUtility.inBatchMode) return;
                var open = EditorSceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(open.path) && !open.isDirty) EditorSceneManager.OpenScene(ScenePath);
            };
        }
    }
}
