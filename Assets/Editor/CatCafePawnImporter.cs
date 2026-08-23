#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// Imports cut Cat Cafe pawn art as transparent, bilinear single sprites.
    /// </summary>
    public sealed class CatCafePawnImporter : AssetPostprocessor
    {
        private static readonly string[] GameplayPieceFolders =
        {
            "Assets/Resources/CatCafe/Pawns/",
            "Assets/Resources/CatCafe/Items/",
            "Assets/Resources/CatCafe/ItemsV3/"
        };

        private void OnPreprocessTexture()
        {
            bool isGameplayPiece = false;
            for (int i = 0; i < GameplayPieceFolders.Length; i++)
            {
                if (!assetPath.StartsWith(GameplayPieceFolders[i],
                    System.StringComparison.OrdinalIgnoreCase)) continue;
                isGameplayPiece = true;
                break;
            }
            if (!isGameplayPiece) return;

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            int sourceMaxDimension = Mathf.Max(sourceWidth, sourceHeight);
            if (sourceMaxDimension <= 0)
            {
                throw new System.InvalidOperationException($"Cannot read source dimensions: {assetPath}");
            }

            importer.maxTextureSize = Mathf.Clamp(
                Mathf.NextPowerOfTwo(sourceMaxDimension),
                32,
                16384);

            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            // FullRect prevents Unity's alpha-derived tight mesh from shaving
            // the anti-aliased edge or broad sticker rim on gameplay pieces.
            textureSettings.spriteExtrude = 2u;
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!EditorApplication.isPlaying || !ContainsGameplayPiece(importedAssets)) return;

            EditorApplication.delayCall += RefreshActiveControllers;
        }

        private static bool ContainsGameplayPiece(string[] importedAssets)
        {
            if (importedAssets == null) return false;
            for (int assetIndex = 0; assetIndex < importedAssets.Length; assetIndex++)
            {
                string imported = importedAssets[assetIndex];
                for (int folderIndex = 0; folderIndex < GameplayPieceFolders.Length; folderIndex++)
                {
                    if (imported.StartsWith(GameplayPieceFolders[folderIndex],
                        System.StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static void RefreshActiveControllers()
        {
            if (!EditorApplication.isPlaying) return;

            CatCafeGameController[] controllers =
                Object.FindObjectsByType<CatCafeGameController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i].RefreshPawnSpritesAfterAssetImport();
            }
        }
    }
}
#endif
