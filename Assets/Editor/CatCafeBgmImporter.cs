#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// 猫咖 BGM 的统一导入规则。默认导入设置对长循环配乐是错的：
    /// 3D 会让音乐被空间化（离摄像机远就变小声），DecompressOnLoad 会把每首曲子
    /// 整段解压进内存。这里统一改成 2D + 流式加载。
    /// </summary>
    public sealed class CatCafeBgmImporter : AssetPostprocessor
    {
        private const string BgmFolder = "Assets/Resources/CatCafe/Bgm/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(BgmFolder, System.StringComparison.OrdinalIgnoreCase)) return;

            AudioImporter importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            importer.ambisonic = false;
            // 本类的规则一直写着"统一改成 2D"，但这一行以前漏了：新导入的曲子会拿到
            // Unity 默认的 3D=1，和既有 11 首（都是 3D=0）不一致。播放端 CatCafeMusicPlayer
            // 把 spatialBlend 写死成 0，所以听感上没差别；补上是为了让资产设置自洽，
            // 也防止将来有人用默认 AudioSource 播它时被空间化。
            importer.threeD = false;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            // 用 CompressedInMemory 而不是 Streaming：
            // 这些是 30s~1min 的循环曲，压缩后每首约 1MB，常驻内存完全可接受；
            // 而 Streaming 配合 preloadAudioData=false + 显式 LoadAudioData()
            // 会在打包后的播放器里让音频后端原生崩溃（0.2.0 首个构建就是这样起不来）。
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
        }

        [MenuItem("Tools/Cat Cafe/Reimport BGM")]
        private static void ReimportBgm()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/CatCafe/Bgm" });
            for (int i = 0; i < guids.Length; i++)
            {
                AssetDatabase.ImportAsset(
                    AssetDatabase.GUIDToAssetPath(guids[i]), ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Cat Cafe BGM reimported: " + guids.Length + " clips.");
        }
    }
}
#endif
