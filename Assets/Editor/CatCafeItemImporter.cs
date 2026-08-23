#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// Imports long-term item artwork as transparent, single sprites so Resources.Load&lt;Sprite&gt; works.
    /// </summary>
    public sealed class CatCafeItemImporter : AssetPostprocessor
    {
        private const string ItemFolder = "Assets/Resources/CatCafe/Items/";
        private const string V3ItemFolder = "Assets/Resources/CatCafe/ItemsV3/";

        private void OnPreprocessTexture()
        {
            bool isItem = assetPath.StartsWith(ItemFolder, System.StringComparison.OrdinalIgnoreCase) ||
                          assetPath.StartsWith(V3ItemFolder, System.StringComparison.OrdinalIgnoreCase);
            if (!isItem) return;

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
        }
    }
}
#endif
