#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// 每次 Player 构建前将启动页整屏美术拆成独立、无损的 AssetBundle；构建结束后
    /// 清理 Assets/StreamingAssets 中的临时副本。这样不会把大图塞进 WebGL 的 data 文件，
    /// 也不会在项目内遗留构建缓存。
    /// </summary>
    internal sealed class CatCafeStartUiBundleBuildProcessor :
        IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string SourceFolder = "Assets/CatCafeBundled/StartUI";
        private const string OutputFolder = "Assets/StreamingAssets/CatCafeStartUiBundles";
        private const string CatalogFileName = "catalog.json";

        [Serializable]
        private sealed class Catalog
        {
            public string[] bundles;
        }

        public int callbackOrder { get { return -1000; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            BuildBundles(report.summary.platform);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            CleanupGeneratedBundles();
        }

        [MenuItem("Tools/Cat Cafe/清理启动页资源包临时文件", false, 140)]
        private static void CleanupFromMenu()
        {
            CleanupGeneratedBundles();
        }

        private static void BuildBundles(BuildTarget target)
        {
            CleanupGeneratedBundles();
            Directory.CreateDirectory(OutputFolder);

            string[] assetPaths = Directory.GetFiles(SourceFolder, "*.png", SearchOption.TopDirectoryOnly)
                .Select(path => path.Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (assetPaths.Length == 0)
            {
                throw new BuildFailedException("[CatCafe] 未找到启动页资源：" + SourceFolder);
            }

            AssetBundleBuild[] builds = assetPaths.Select(path => new AssetBundleBuild
            {
                assetBundleName = "startui_" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant() + ".bundle",
                assetNames = new[] { path }
            }).ToArray();

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                OutputFolder,
                builds,
                BuildAssetBundleOptions.ChunkBasedCompression,
                target);
            if (manifest == null)
            {
                throw new BuildFailedException("[CatCafe] 启动页资源包构建失败。");
            }

            Catalog catalog = new Catalog { bundles = builds.Select(build => build.assetBundleName).ToArray() };
            File.WriteAllText(Path.Combine(OutputFolder, CatalogFileName), JsonUtility.ToJson(catalog));
            AssetDatabase.Refresh();
        }

        private static void CleanupGeneratedBundles()
        {
            if (!Directory.Exists(OutputFolder)) return;
            FileUtil.DeleteFileOrDirectory(OutputFolder);
            FileUtil.DeleteFileOrDirectory(OutputFolder + ".meta");
            AssetDatabase.Refresh();
        }
    }
}
#endif
