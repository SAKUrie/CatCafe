#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// Keeps the supplied full-canvas paper UI pieces crisp and loadable through Resources.Load<Sprite>.
    /// </summary>
    public sealed class CatCafeInGameUiImporter : AssetPostprocessor
    {
        private const string UiFolder = "Assets/Resources/CatCafe/InGameUI/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiFolder, System.StringComparison.OrdinalIgnoreCase)) return;

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
        }

        [MenuItem("Tools/Cat Cafe/Reimport Paper In-Game UI")]
        private static void ReimportPaperUi()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { UiFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Cat Cafe paper in-game UI reimported: " + guids.Length + " textures.");
        }
    }
}
#endif
