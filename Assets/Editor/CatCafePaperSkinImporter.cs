#if UNITY_EDITOR
using UnityEditor;

namespace ManyFace.CatCafe.EditorTools
{
    internal sealed class CatCafePaperSkinImporter : AssetPostprocessor
    {
        private const string PaperSkinPath = "Assets/Resources/CatCafe/PaperSkin/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PaperSkinPath, System.StringComparison.Ordinal)) return;

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.spriteBorder = UnityEngine.Vector4.zero;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (fileName.StartsWith("title-ribbon", System.StringComparison.Ordinal))
            {
                importer.spriteBorder = new UnityEngine.Vector4(170f, 32f, 170f, 32f);
            }
            else if (fileName.StartsWith("button-", System.StringComparison.Ordinal))
            {
                importer.spriteBorder = new UnityEngine.Vector4(64f, 34f, 64f, 34f);
            }
            else if (fileName == "modal-panel" || fileName == "modal-main-v2")
            {
                importer.spriteBorder = new UnityEngine.Vector4(72f, 64f, 72f, 64f);
            }
            else if (fileName.StartsWith("badge-", System.StringComparison.Ordinal))
            {
                // 稀有度徽章要按品质名长短拉伸，留出带缺角的两端不被拉变形。
                importer.spriteBorder = new UnityEngine.Vector4(30f, 14f, 30f, 14f);
            }
        }

        [MenuItem("Tools/Cat Cafe/UI/重新导入纸艺菜单皮肤")]
        private static void ReimportPaperSkin()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PaperSkinPath.TrimEnd('/') });
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.Refresh();
        }
    }
}
#endif
