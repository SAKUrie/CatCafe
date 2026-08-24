using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 启动页整屏美术的运行时资源缓存。
    ///
    /// 这些美术由构建处理器分成多个独立 AssetBundle，避免 WebGL 主数据文件超过
    /// itch.io 的单文件上限。资源键仍沿用既有的 CatCafe/StartUI/… 形式，玩法配置
    /// 与调用侧不需要知道资源的物理存储方式。
    /// </summary>
    internal static class CatCafeStartUiBundleCache
    {
        private const string ResourceRoot = "CatCafe/StartUI/";
        private const string BundleFolder = "CatCafeStartUiBundles";
        private const string CatalogFileName = "catalog.json";

        [Serializable]
        private sealed class Catalog
        {
            public string[] bundles;
        }

        private static readonly Dictionary<string, Sprite> sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly List<AssetBundle> loadedBundles = new List<AssetBundle>();
        private static bool initialized;
        private static bool initializationFailed;

        internal static bool IsReady
        {
            get { return initialized && !initializationFailed; }
        }

        internal static IEnumerator Initialize()
        {
            if (initialized) yield break;
            initialized = true;

#if UNITY_EDITOR
            if (Application.isEditor)
            {
                LoadEditorAssets();
                yield break;
            }
#endif

            string catalogUrl = CombineUrl(Application.streamingAssetsPath, BundleFolder + "/" + CatalogFileName);
            using (UnityWebRequest catalogRequest = UnityWebRequest.Get(catalogUrl))
            {
                yield return catalogRequest.SendWebRequest();
                if (catalogRequest.result != UnityWebRequest.Result.Success)
                {
                    Fail("无法读取启动页资源目录：" + catalogRequest.error);
                    yield break;
                }

                Catalog catalog = JsonUtility.FromJson<Catalog>(catalogRequest.downloadHandler.text);
                if (catalog == null || catalog.bundles == null || catalog.bundles.Length == 0)
                {
                    Fail("启动页资源目录为空或格式无效。");
                    yield break;
                }

                for (int i = 0; i < catalog.bundles.Length; i++)
                {
                    string bundleUrl = CombineUrl(Application.streamingAssetsPath,
                        BundleFolder + "/" + catalog.bundles[i]);
                    using (UnityWebRequest bundleRequest = UnityWebRequestAssetBundle.GetAssetBundle(bundleUrl))
                    {
                        yield return bundleRequest.SendWebRequest();
                        if (bundleRequest.result != UnityWebRequest.Result.Success)
                        {
                            Fail("无法载入启动页资源包 " + catalog.bundles[i] + "：" + bundleRequest.error);
                            yield break;
                        }

                        AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(bundleRequest);
                        if (bundle == null)
                        {
                            Fail("启动页资源包为空：" + catalog.bundles[i]);
                            yield break;
                        }

                        loadedBundles.Add(bundle);
                        Sprite[] bundleSprites = bundle.LoadAllAssets<Sprite>();
                        for (int j = 0; j < bundleSprites.Length; j++)
                        {
                            Sprite sprite = bundleSprites[j];
                            if (sprite != null)
                            {
                                sprites[ResourceRoot + sprite.name] = sprite;
                            }
                        }
                    }
                }
            }
        }

        internal static Sprite LoadSprite(string resourcePath)
        {
            Sprite sprite;
            return sprites.TryGetValue(resourcePath, out sprite) ? sprite : null;
        }

        private static string CombineUrl(string root, string relativePath)
        {
            if (root.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                return root.TrimEnd('/') + "/" + relativePath;
            }

            return new Uri(Path.Combine(root, relativePath)).AbsoluteUri;
        }

        private static void Fail(string message)
        {
            initializationFailed = true;
            Debug.LogError("[CatCafeStartUiBundleCache] " + message);
        }

#if UNITY_EDITOR
        private static void LoadEditorAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/CatCafeBundled/StartUI" });
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null)
                {
                    sprites[ResourceRoot + sprite.name] = sprite;
                }
            }
        }
#endif
    }
}
