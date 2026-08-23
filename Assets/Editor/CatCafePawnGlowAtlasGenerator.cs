#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe
{
    public static class CatCafePawnGlowAtlasGenerator
    {
        private const string SourceFolder =
            "Assets/Resources/CatCafe/Pawns";
        private const string OutputFolder =
            "Assets/Resources/CatCafe/PawnGlow";
        private const string AtlasPrefix =
            "CatCafePawnGlowMaskAtlas_";
        private const string MapAssetPath =
            OutputFolder + "/CatCafePawnGlowAtlasMap.asset";

        private const int MaskSize = 128;
        private const int MaxAtlasSize = 2048;
        private const int SlotsPerAxis = MaxAtlasSize / MaskSize;
        private const int AtlasCapacity = SlotsPerAxis * SlotsPerAxis;
        private const int BackgroundColorQuantizationShift = 4;
        private const float BackgroundFloodDistance = 48f;
        private const float BackgroundFeatherStart = 4f;

        [MenuItem("CatCafe/Tools/Generate Pawn Glow RGBA Atlas")]
        [MenuItem("CatCafe/Tools/Rebuild Pawn Glow Alpha From RGB")]
        public static void Generate()
        {
            List<string> assetPaths = FindPawnSpritePaths();
            if (assetPaths.Count == 0)
            {
                Debug.LogError(
                    "[CatCafe] No active pawn sprites found under " +
                    SourceFolder);
                return;
            }

            HashSet<string> names = new HashSet<string>(
                StringComparer.Ordinal);
            for (int i = 0; i < assetPaths.Count; i++)
            {
                string spriteName =
                    Path.GetFileNameWithoutExtension(assetPaths[i]);
                if (!names.Add(spriteName))
                {
                    Debug.LogError(
                        "[CatCafe] Duplicate pawn sprite name: " +
                        spriteName);
                    return;
                }
            }

            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            string outputAbsolute = Path.Combine(
                projectRoot,
                OutputFolder.Replace(
                    '/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(outputAbsolute);
            AssetDatabase.Refresh();

            List<Texture2D> importedAtlases =
                new List<Texture2D>();
            List<CatCafePawnGlowAtlasMap.Entry> entries =
                new List<CatCafePawnGlowAtlasMap.Entry>(
                    assetPaths.Count);
            HashSet<string> expectedAtlasPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            int atlasCount = Mathf.CeilToInt(
                assetPaths.Count / (float)AtlasCapacity);

            try
            {
                for (int atlasIndex = 0;
                    atlasIndex < atlasCount;
                    atlasIndex++)
                {
                    int first = atlasIndex * AtlasCapacity;
                    int count = Mathf.Min(
                        AtlasCapacity,
                        assetPaths.Count - first);
                    int columns = Mathf.Min(
                        SlotsPerAxis,
                        Mathf.CeilToInt(Mathf.Sqrt(count)));
                    int rows = Mathf.CeilToInt(
                        count / (float)columns);
                    int atlasWidth = columns * MaskSize;
                    int atlasHeight = rows * MaskSize;

                    Color32[] atlasPixels =
                        CreateTransparentAtlasPixels(
                            atlasWidth,
                            atlasHeight);

                    for (int localIndex = 0;
                        localIndex < count;
                        localIndex++)
                    {
                        int assetIndex = first + localIndex;
                        string assetPath = assetPaths[assetIndex];
                        EditorUtility.DisplayProgressBar(
                            "Cat Cafe",
                            "Building RGBA pawn atlas " +
                            (assetIndex + 1) + "/" +
                            assetPaths.Count + ": " +
                            Path.GetFileNameWithoutExtension(
                                assetPath),
                            assetIndex /
                            (float)assetPaths.Count);

                        Color32[] rgbaSprite = ReadRgbaSprite(
                            projectRoot,
                            assetPath);
                        if (rgbaSprite == null)
                        {
                            continue;
                        }

                        int slotX =
                            (localIndex % columns) * MaskSize;
                        int slotY =
                            (localIndex / columns) * MaskSize;
                        WriteSprite(
                            atlasPixels,
                            atlasWidth,
                            slotX,
                            slotY,
                            rgbaSprite);

                        entries.Add(
                            new CatCafePawnGlowAtlasMap.Entry
                            {
                                spriteName =
                                    Path.GetFileNameWithoutExtension(
                                        assetPath),
                                atlasIndex = atlasIndex,
                                uvRect = new Rect(
                                    slotX / (float)atlasWidth,
                                    slotY / (float)atlasHeight,
                                    MaskSize /
                                    (float)atlasWidth,
                                    MaskSize /
                                    (float)atlasHeight)
                            });
                    }

                    string atlasAssetPath =
                        OutputFolder + "/" + AtlasPrefix +
                        atlasIndex + ".png";
                    expectedAtlasPaths.Add(atlasAssetPath);
                    SaveAtlas(
                        projectRoot,
                        atlasAssetPath,
                        atlasWidth,
                        atlasHeight,
                        atlasPixels);
                    ConfigureAtlasImporter(atlasAssetPath);

                    Texture2D imported =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            atlasAssetPath);
                    if (imported == null)
                    {
                        Debug.LogError(
                            "[CatCafe] Pawn RGBA atlas failed " +
                            "to import: " + atlasAssetPath);
                        return;
                    }

                    importedAtlases.Add(imported);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (entries.Count != assetPaths.Count)
            {
                Debug.LogError(
                    "[CatCafe] Pawn RGBA atlas generation was " +
                    "incomplete. Expected " +
                    assetPaths.Count + " sprites, produced " +
                    entries.Count + ".");
                return;
            }

            DeleteStaleAtlases(expectedAtlasPaths);

            CatCafePawnGlowAtlasMap map =
                AssetDatabase.LoadAssetAtPath<
                    CatCafePawnGlowAtlasMap>(
                    MapAssetPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<
                    CatCafePawnGlowAtlasMap>();
                AssetDatabase.CreateAsset(
                    map,
                    MapAssetPath);
            }

            map.SetGeneratedData(
                MaskSize,
                importedAtlases,
                entries);
            RemoveLegacyMaterialProperties();
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            long atlasBytes = 0;
            for (int i = 0;
                i < importedAtlases.Count;
                i++)
            {
                Texture2D atlas = importedAtlases[i];
                atlasBytes +=
                    (long)atlas.width *
                    atlas.height * 4L;
            }

            Debug.Log(
                "[CatCafe] Generated " +
                entries.Count +
                " RGB sprites with clean RGB-derived alpha in " +
                importedAtlases.Count +
                " RGBA32 atlas(es), " +
                (atlasBytes / (1024f * 1024f)).
                    ToString("0.00") +
                " MiB uncompressed GPU memory.");
        }

        private static List<string> FindPawnSpritePaths()
        {
            return AssetDatabase.FindAssets(
                    "t:Texture2D",
                    new[] { SourceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    path.EndsWith(
                        ".png",
                        StringComparison.OrdinalIgnoreCase) &&
                    AssetDatabase.LoadAssetAtPath<Sprite>(
                        path) != null)
                .OrderBy(
                    path =>
                        Path.GetFileNameWithoutExtension(path),
                    StringComparer.Ordinal)
                .ToList();
        }

        private static Color32[]
            CreateTransparentAtlasPixels(
                int width,
                int height)
        {
            return new Color32[width * height];
        }

        private static Color32[] ReadRgbaSprite(
            string projectRoot,
            string assetPath)
        {
            string absolutePath = Path.Combine(
                projectRoot,
                assetPath.Replace(
                    '/', Path.DirectorySeparatorChar));
            Texture2D source = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false);
            try
            {
                if (!ImageConversion.LoadImage(
                    source,
                    File.ReadAllBytes(absolutePath),
                    false))
                {
                    Debug.LogError(
                        "[CatCafe] Failed to decode pawn: " +
                        assetPath);
                    return null;
                }

                return BuildRgbaFromRgb(source);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static Color32[] BuildRgbaFromRgb(
            Texture2D source)
        {
            int width = source.width;
            int height = source.height;
            Color32[] pixels = source.GetPixels32();
            Color32 background = EstimateBackgroundColor(
                pixels,
                width,
                height);
            float[] backgroundDistances =
                new float[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                backgroundDistances[i] = ColorDistance(
                    pixels[i],
                    background);
            }

            bool[] backgroundConnected =
                FindBackgroundConnectedPixels(
                backgroundDistances,
                width,
                height);
            return ResampleRgba(
                pixels,
                backgroundDistances,
                backgroundConnected,
                width,
                height,
                MaskSize);
        }

        private static Color32 EstimateBackgroundColor(
            Color32[] pixels,
            int width,
            int height)
        {
            int stride = Mathf.Max(
                1,
                Mathf.Min(width, height) / 128);
            Dictionary<int, BackgroundColorBucket> buckets =
                new Dictionary<int, BackgroundColorBucket>();

            for (int x = 0; x < width; x += stride)
            {
                AddBackgroundSample(
                    pixels[x],
                    buckets);
                AddBackgroundSample(
                    pixels[(height - 1) * width + x],
                    buckets);
            }

            for (int y = stride; y < height - 1; y += stride)
            {
                AddBackgroundSample(
                    pixels[y * width],
                    buckets);
                AddBackgroundSample(
                    pixels[y * width + width - 1],
                    buckets);
            }

            BackgroundColorBucket best = default;
            foreach (BackgroundColorBucket bucket in buckets.Values)
            {
                if (bucket.count > best.count)
                {
                    best = bucket;
                }
            }

            if (best.count == 0)
            {
                return new Color32(0, 0, 0, 0);
            }

            return new Color32(
                (byte)Mathf.RoundToInt(
                    best.red / (float)best.count),
                (byte)Mathf.RoundToInt(
                    best.green / (float)best.count),
                (byte)Mathf.RoundToInt(
                    best.blue / (float)best.count),
                255);
        }

        private static void AddBackgroundSample(
            Color32 pixel,
            Dictionary<int, BackgroundColorBucket> buckets)
        {
            int shift = BackgroundColorQuantizationShift;
            int key =
                (pixel.r >> shift) << 16 |
                (pixel.g >> shift) << 8 |
                pixel.b >> shift;
            BackgroundColorBucket bucket;
            if (!buckets.TryGetValue(key, out bucket))
            {
                bucket = default;
            }

            bucket.count++;
            bucket.red += pixel.r;
            bucket.green += pixel.g;
            bucket.blue += pixel.b;
            buckets[key] = bucket;
        }

        private static float ColorDistance(
            Color32 pixel,
            Color32 background)
        {
            return Mathf.Max(
                Mathf.Abs(pixel.r - background.r),
                Mathf.Max(
                    Mathf.Abs(pixel.g - background.g),
                    Mathf.Abs(pixel.b - background.b)));
        }

        private static bool[] FindBackgroundConnectedPixels(
            float[] backgroundDistances,
            int width,
            int height)
        {
            bool[] connected =
                new bool[backgroundDistances.Length];
            Queue<int> open = new Queue<int>();

            for (int x = 0; x < width; x++)
            {
                EnqueueBackground(
                    x,
                    0,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    x,
                    height - 1,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
            }

            for (int y = 1; y < height - 1; y++)
            {
                EnqueueBackground(
                    0,
                    y,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    width - 1,
                    y,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
            }

            while (open.Count > 0)
            {
                int index = open.Dequeue();
                int x = index % width;
                int y = index / width;

                EnqueueBackground(
                    x - 1,
                    y,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    x + 1,
                    y,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    x,
                    y - 1,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    x,
                    y + 1,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);

                EnqueueBackground(
                    x - 1,
                    y - 1,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    x + 1,
                    y - 1,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    x - 1,
                    y + 1,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
                EnqueueBackground(
                    x + 1,
                    y + 1,
                    width,
                    height,
                    backgroundDistances,
                    connected,
                    open);
            }

            return connected;
        }

        private static void EnqueueBackground(
            int x,
            int y,
            int width,
            int height,
            float[] backgroundDistances,
            bool[] connected,
            Queue<int> open)
        {
            if (x < 0 || y < 0 ||
                x >= width || y >= height)
            {
                return;
            }

            int index = y * width + x;
            if (connected[index] ||
                backgroundDistances[index] >
                BackgroundFloodDistance)
            {
                return;
            }

            connected[index] = true;
            open.Enqueue(index);
        }

        private static float AlphaFromBackgroundDistance(
            float distance)
        {
            return Mathf.InverseLerp(
                BackgroundFeatherStart,
                BackgroundFloodDistance,
                distance);
        }

        private static Color32[] ResampleRgba(
            Color32[] sourcePixels,
            float[] backgroundDistances,
            bool[] backgroundConnected,
            int sourceWidth,
            int sourceHeight,
            int targetSize)
        {
            Color32[] result =
                new Color32[targetSize * targetSize];

            for (int y = 0; y < targetSize; y++)
            {
                int yMin = Mathf.FloorToInt(
                    y * sourceHeight /
                    (float)targetSize);
                int yMax = Mathf.CeilToInt(
                    (y + 1) * sourceHeight /
                    (float)targetSize);
                yMax = Mathf.Min(
                    sourceHeight,
                    Mathf.Max(yMin + 1, yMax));

                for (int x = 0; x < targetSize; x++)
                {
                    int xMin = Mathf.FloorToInt(
                        x * sourceWidth /
                        (float)targetSize);
                    int xMax = Mathf.CeilToInt(
                        (x + 1) * sourceWidth /
                        (float)targetSize);
                    xMax = Mathf.Min(
                        sourceWidth,
                        Mathf.Max(xMin + 1, xMax));

                    float covered = 0f;
                    float red = 0f;
                    float green = 0f;
                    float blue = 0f;

                    for (int sourceY = yMin;
                        sourceY < yMax;
                        sourceY++)
                    {
                        for (int sourceX = xMin;
                            sourceX < xMax;
                            sourceX++)
                        {
                            int sourceIndex =
                                sourceY * sourceWidth +
                                sourceX;
                            Color32 sourcePixel =
                                sourcePixels[sourceIndex];
                            float sampleAlpha = backgroundConnected[
                                sourceIndex]
                                ? AlphaFromBackgroundDistance(
                                    backgroundDistances[sourceIndex])
                                : 1f;
                            if (sampleAlpha <= 0f)
                            {
                                continue;
                            }

                            covered += sampleAlpha;
                            red += sourcePixel.r * sampleAlpha;
                            green += sourcePixel.g * sampleAlpha;
                            blue += sourcePixel.b * sampleAlpha;
                        }
                    }

                    int sampleCount =
                        (xMax - xMin) * (yMax - yMin);
                    if (covered <= 0f || sampleCount <= 0)
                    {
                        result[y * targetSize + x] =
                            new Color32(0, 0, 0, 0);
                        continue;
                    }

                    byte alpha = (byte)Mathf.RoundToInt(
                        covered * 255f / sampleCount);
                    result[y * targetSize + x] =
                        new Color32(
                            (byte)Mathf.RoundToInt(
                                red / covered),
                            (byte)Mathf.RoundToInt(
                                green / covered),
                            (byte)Mathf.RoundToInt(
                                blue / covered),
                            alpha);
                }
            }

            return result;
        }

        private struct BackgroundColorBucket
        {
            public int count;
            public int red;
            public int green;
            public int blue;
        }

        private static void WriteSprite(
            Color32[] atlasPixels,
            int atlasWidth,
            int slotX,
            int slotY,
            Color32[] rgbaSprite)
        {
            for (int y = 0; y < MaskSize; y++)
            {
                for (int x = 0; x < MaskSize; x++)
                {
                    atlasPixels[
                        (slotY + y) * atlasWidth +
                        slotX + x] =
                        rgbaSprite[
                            y * MaskSize + x];
                }
            }
        }

        private static void SaveAtlas(
            string projectRoot,
            string atlasAssetPath,
            int width,
            int height,
            Color32[] pixels)
        {
            Texture2D atlas = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false);
            try
            {
                atlas.SetPixels32(pixels);
                atlas.Apply(false, false);
                string absolutePath = Path.Combine(
                    projectRoot,
                    atlasAssetPath.Replace(
                        '/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(
                    absolutePath,
                    atlas.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atlas);
            }

            AssetDatabase.ImportAsset(
                atlasAssetPath,
                ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureAtlasImporter(
            string atlasAssetPath)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(
                    atlasAssetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType =
                TextureImporterType.Default;
            importer.spriteImportMode =
                SpriteImportMode.Single;
#pragma warning disable 0618
            importer.spritesheet =
                Array.Empty<SpriteMetaData>();
#pragma warning restore 0618
            importer.textureShape =
                TextureImporterShape.Texture2D;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.alphaSource =
                TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.npotScale =
                TextureImporterNPOTScale.None;
            importer.maxTextureSize = MaxAtlasSize;

            TextureImporterPlatformSettings settings =
                importer.GetDefaultPlatformTextureSettings();
            settings.maxTextureSize = MaxAtlasSize;
            settings.format =
                TextureImporterFormat.RGBA32;
            settings.textureCompression =
                TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);

            ConfigurePlatformSettings(
                importer,
                "Standalone");
            ConfigurePlatformSettings(
                importer,
                "Android");
            ConfigurePlatformSettings(
                importer,
                "iPhone");
            ConfigurePlatformSettings(
                importer,
                "WebGL");
            ConfigurePlatformSettings(
                importer,
                "Windows Store Apps");
            importer.SaveAndReimport();
        }

        private static void ConfigurePlatformSettings(
            TextureImporter importer,
            string platformName)
        {
            TextureImporterPlatformSettings settings =
                importer.GetPlatformTextureSettings(
                    platformName);
            settings.name = platformName;
            settings.overridden = true;
            settings.maxTextureSize = MaxAtlasSize;
            settings.format = TextureImporterFormat.RGBA32;
            settings.textureCompression =
                TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
        }

        private static void DeleteStaleAtlases(
            HashSet<string> expectedPaths)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { OutputFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName =
                    Path.GetFileNameWithoutExtension(path);
                if (!fileName.StartsWith(
                        AtlasPrefix,
                        StringComparison.Ordinal) ||
                    expectedPaths.Contains(path))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(path);
            }
        }
    

private static void RemoveSavedProperties(
            SerializedProperty entries,
            HashSet<string> names)
        {
            if (entries == null || !entries.isArray)
            {
                return;
            }

            for (int i = entries.arraySize - 1;
                i >= 0;
                i--)
            {
                SerializedProperty entry =
                    entries.GetArrayElementAtIndex(i);
                SerializedProperty key =
                    entry.FindPropertyRelative("first");
                if (key != null &&
                    names.Contains(key.stringValue))
                {
                    entries.DeleteArrayElementAtIndex(i);
                }
            }
        }


        private static void RemoveLegacyMaterialProperties()
        {
            const string materialPath =
                "Assets/Materials/CatCafePieceSoftGlow.mat";
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    materialPath);
            if (material == null)
            {
                return;
            }

            SerializedObject serialized =
                new SerializedObject(material);
            RemoveSavedProperties(
                serialized.FindProperty(
                    "m_SavedProperties.m_TexEnvs"),
                new HashSet<string>(
                    new[] { "_SdfTex", "_MaskTex" },
                    StringComparer.Ordinal));
            RemoveSavedProperties(
                serialized.FindProperty(
                    "m_SavedProperties.m_Floats"),
                new HashSet<string>(
                    new[]
                    {
                        "_SdfContentSize",
                        "_SdfPadding",
                        "_SdfRange",
                        "_SdfSlotSize",
                        "_MaskContentSize"
                    },
                    StringComparer.Ordinal));
            RemoveSavedProperties(
                serialized.FindProperty(
                    "m_SavedProperties.m_Colors"),
                new HashSet<string>(
                    new[] { "_SdfUvRect", "_MaskUvRect" },
                    StringComparer.Ordinal));

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(material);
        }
}
}
#endif
