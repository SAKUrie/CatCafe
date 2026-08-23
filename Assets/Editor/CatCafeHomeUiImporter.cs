#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// 大厅纸艺 UI 的导入规则。
    ///
    /// 和 InGameUI 那批不同，这里的源图是按 alpha 包围盒裁紧过的（见
    /// Tools/CatCafeConfig/import_home_ui.py），所以不需要 2048 的上限来兜底
    /// 全画布尺寸；摆放位置由 HomeLayout 表给出，与贴图尺寸解耦。
    /// </summary>
    public sealed class CatCafeHomeUiImporter : AssetPostprocessor
    {
        private const string UiFolder = "Assets/Resources/CatCafe/HomeUI/";
        // 开始界面那批是同样的纸艺整层，导入要求一致（未压缩、透明、不缩放），
        // 顺手一起管，免得再复制一个只差目录名的 importer。
        private const string StartUiFolder = "Assets/Resources/CatCafe/StartUI/";

        private void OnPreprocessTexture()
        {
            bool managed =
                assetPath.StartsWith(UiFolder, System.StringComparison.OrdinalIgnoreCase) ||
                assetPath.StartsWith(StartUiFolder, System.StringComparison.OrdinalIgnoreCase);
            if (!managed) return;

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
                throw new System.InvalidOperationException("Cannot read source dimensions: " + assetPath);
            importer.maxTextureSize = Mathf.Clamp(
                Mathf.NextPowerOfTwo(sourceMaxDimension), 32, 16384);
            TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
            platform.maxTextureSize = importer.maxTextureSize;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform);
        }

        [MenuItem("Tools/Cat Cafe/Reimport Paper Home UI")]
        private static void ReimportHomeUi()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { UiFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guids[i]),
                    ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Cat Cafe paper home UI reimported: " + guids.Length + " textures.");
        }
    }
}
#endif
