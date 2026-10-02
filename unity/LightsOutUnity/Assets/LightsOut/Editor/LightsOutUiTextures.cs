// The UI's gradient and shadow textures (Assets/LightsOut/UI) are stretched or 9-sliced, so they must not repeat,
// mip or compress.
using UnityEditor;
using UnityEngine;

namespace LightsOut.EditorTools
{
    class LightsOutUiTextures : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/LightsOut/UI/")) return;
            var t = (TextureImporter)assetImporter;
            t.mipmapEnabled = false; t.wrapMode = TextureWrapMode.Clamp; t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Uncompressed; t.alphaIsTransparency = true;
        }
    }
}
