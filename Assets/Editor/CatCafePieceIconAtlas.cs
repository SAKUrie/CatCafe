#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// 把棋子／道具的立绘打成一张 TMP 图集，供规则文案里的 &lt;sprite name="key"&gt; 内联显示。
    ///
    /// 为什么要自己生成：TMP 显示内联图标必须有 TMP_SpriteAsset（图集 + 字形表），
    /// 而这些立绘是 310 张各自独立的 1024² 图，没法手工维护。
    ///
    /// 两个刻意的实现选择：
    ///   1 读像素走 RenderTexture + ReadPixels，不去翻 TextureImporter.isReadable。
    ///     改导入设置会重写 .meta，310 个文件的改动会淹掉真正的提交。
    ///   2 每张图先按 alpha 裁掉透明边再缩放。源图四周留白很多，直接缩到 64px
    ///     的话主体只占中间一小块，内联显示时基本看不清是什么。
    /// </summary>
    public static class CatCafePieceIconAtlas
    {
        private const string ConfigPath = "Assets/Resources/GameData/cat_cafe_config.json";
        private const string OutputPath = "Assets/Resources/CatCafe/UI/PieceIcons.asset";
        // 图集贴图刻意放在 Resources 之外：它由上面那个资产引用着，打包时会被带上，
        // 放进 Resources 反而会被当成独立资源再收一份。
        private const string AtlasPath = "Assets/Art/CatCafe/Generated/PieceIconsAtlas.png";
        private const string SpriteRoot = "Assets/Resources/CatCafe/";

        private const int Cell = 64;        // 图集里每格的边长
        private const int Padding = 4;      // 格与格之间留白，避免采样串色
        private const int WorkSize = 256;   // 裁剪前的工作分辨率
        // 图标显示倍率。1.0 时图标高度等于文字 ascent，看着偏小；1.6 大约是
        // 一个汉字见方再大一点，和「喝掉相邻的[图]和[图]」这种夹在文字里的排版最搭。
        private const float IconScale = 1.6f;

        private sealed class Entry
        {
            public string Key;
            public string Asset;
        }

        [MenuItem("Tools/Cat Cafe/生成棋子图标图集")]
        public static void Build()
        {
            List<Entry> entries = CollectEntries();
            if (entries.Count == 0)
            {
                Debug.LogError("[PieceIconAtlas] 配置里没有可用条目，图集未生成。");
                return;
            }

            int columns = Mathf.Max(1, (2048 + Padding) / (Cell + Padding));
            int rows = Mathf.CeilToInt(entries.Count / (float)columns);
            int width = Mathf.NextPowerOfTwo(columns * (Cell + Padding));
            int height = Mathf.NextPowerOfTwo(rows * (Cell + Padding));

            Texture2D atlas = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32[] clear = new Color32[width * height];
            atlas.SetPixels32(clear);

            List<TMP_SpriteCharacter> characters = new List<TMP_SpriteCharacter>();
            List<TMP_SpriteGlyph> glyphs = new List<TMP_SpriteGlyph>();
            List<string> missing = new List<string>();

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                EditorUtility.DisplayProgressBar("生成棋子图标图集",
                    entry.Key + "（" + (i + 1) + "/" + entries.Count + "）",
                    (i + 1) / (float)entries.Count);

                Texture2D icon = RenderIcon(entry.Asset);
                if (icon == null)
                {
                    missing.Add(entry.Key + " ← " + entry.Asset);
                    continue;
                }

                int column = glyphs.Count % columns;
                int row = glyphs.Count / columns;
                int x = column * (Cell + Padding);
                // TMP 的 glyphRect 和 Texture2D 一样是左下原点，这里保持一致，
                // 否则图标会整体上下翻转错位。
                int y = height - (row + 1) * (Cell + Padding);
                atlas.SetPixels(x, y, Cell, Cell, icon.GetPixels());
                UnityEngine.Object.DestroyImmediate(icon);

                uint index = (uint)glyphs.Count;
                TMP_SpriteGlyph glyph = new TMP_SpriteGlyph
                {
                    index = index,
                    // bearingY 抬到 0.8 倍高度，图标才会坐在基线上而不是沉下去半格。
                    metrics = new GlyphMetrics(Cell, Cell, 0f, Cell * 0.8f, Cell),
                    glyphRect = new GlyphRect(x, y, Cell, Cell),
                    scale = 1f,
                    atlasIndex = 0,
                };
                glyphs.Add(glyph);

                TMP_SpriteCharacter character = new TMP_SpriteCharacter(0u, glyph)
                {
                    name = entry.Key,
                    // 图集没有自己的 faceInfo.pointSize，TMP 会走「图标高度 = 字体
                    // ascent 高度」那条分支，于是图标和文字一样高、看着偏小。
                    // sprite.scale 是这条分支上唯一干净的放大杠杆。
                    scale = IconScale,
                };
                characters.Add(character);
            }
            EditorUtility.ClearProgressBar();

            atlas.Apply(false, false);
            atlas.name = "PieceIcons Atlas";

            // 图集单独存成 PNG，不用 AddObjectToAsset 内嵌。
            // 内嵌会把裸 RGBA32 序列化进 .asset 的 YAML 里：2048×1024×4 = 8 MiB
            // 裸像素，文本编码后翻倍到 16 MiB，而且 *.asset 在 .gitattributes 里是
            // 文本不走 LFS，每次重新生成都是全量 diff。存成 PNG 之后走 LFS，
            // 导入时压成 BC7，磁盘和显存都降到约四分之一。
            Texture2D atlasAsset = WriteAtlasTexture(atlas);
            UnityEngine.Object.DestroyImmediate(atlas);
            if (atlasAsset == null)
            {
                Debug.LogError("[PieceIconAtlas] 图集 PNG 写入失败，未生成资产。");
                return;
            }

            TMP_SpriteAsset spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            spriteAsset.name = "PieceIcons";
            spriteAsset.spriteSheet = atlasAsset;
            // spriteInfoList 声明时没有初始化，新建实例上是 null。TMP 的旧版迁移
            // 会直接遍历它，不给个空表就是 NullReferenceException。
            spriteAsset.spriteInfoList = new List<TMP_Sprite>();

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            AssetDatabase.DeleteAsset(OutputPath);
            AssetDatabase.CreateAsset(spriteAsset, OutputPath);

            // 顺序在这里是关键，不是洁癖。
            // TMP 的判定是「material != null 且 version 为空 → 跑旧版迁移」，
            // 而迁移干的第一件事就是 m_SpriteCharacterTable.Clear()。所以必须
            // 赶在挂 material 之前把版本号写死，否则表刚填完就被清空；更糟的是
            // 运行时 Awake() 还会再触发一次，把落盘的资产也洗掉。
            // version 的 setter 是 internal，外部程序集只能走 SerializedObject。
            SerializedObject serialized = new SerializedObject(spriteAsset);
            SerializedProperty versionProperty = serialized.FindProperty("m_Version");
            if (versionProperty != null)
            {
                versionProperty.stringValue = "1.1.0";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[PieceIconAtlas] 找不到 m_Version 字段，TMP 版本可能变了；"
                    + "若图集显示为空，先查是不是被旧版迁移清过表。");
            }

            Material material = new Material(Shader.Find("TextMeshPro/Sprite"));
            material.SetTexture(ShaderUtilities.ID_MainTex, atlasAsset);
            material.name = "PieceIcons Material";
            spriteAsset.material = material;
            AssetDatabase.AddObjectToAsset(material, spriteAsset);

            // 这两张表是只读属性（只有 getter），只能原地增删，不能整体赋值。
            spriteAsset.spriteGlyphTable.Clear();
            spriteAsset.spriteGlyphTable.AddRange(glyphs);
            spriteAsset.spriteCharacterTable.Clear();
            spriteAsset.spriteCharacterTable.AddRange(characters);
            spriteAsset.UpdateLookupTables();

            EditorUtility.SetDirty(spriteAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 落盘之后回读一次，确认表没有在中途被清空——这个坑踩过一次，值得留个哨兵。
            TMP_SpriteAsset verify = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(OutputPath);
            if (verify == null || verify.spriteCharacterTable.Count != characters.Count)
            {
                Debug.LogError(string.Format(
                    "[PieceIconAtlas] 落盘校验失败：期望 {0} 个图标，实际 {1} 个。",
                    characters.Count, verify == null ? 0 : verify.spriteCharacterTable.Count));
                return;
            }

            Debug.Log(string.Format(
                "[PieceIconAtlas] 已生成 {0}：{1} 个图标，图集 {2}×{3}，缺图 {4} 个。\n"
                + "  资产 {5:N0} 字节｜图集 PNG {6} = {7:N0} 字节",
                OutputPath, characters.Count, width, height, missing.Count,
                new FileInfo(OutputPath).Length, AtlasPath, new FileInfo(AtlasPath).Length));
            if (missing.Count > 0)
            {
                Debug.LogWarning("[PieceIconAtlas] 以下条目找不到立绘，规则文案里会退回显示名字：\n  "
                    + string.Join("\n  ", missing.ToArray()));
            }
        }

        /// <summary>
        /// 把内存里的图集写成 PNG 并按 TMP 图集该有的方式导入，返回导入后的贴图资产。
        ///
        /// 关键几项：不生成 mipmap（内联图标永远 1:1 显示，mip 只会让它发糊）、
        /// Clamp 防止边缘采样串到对面、BC7 在带 alpha 的图集上比 DXT5 干净得多。
        /// </summary>
        private static Texture2D WriteAtlasTexture(Texture2D atlas)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AtlasPath));
            File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[PieceIconAtlas] 拿不到 TextureImporter：" + AtlasPath);
                return null;
            }
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = 2048,
                format = TextureImporterFormat.BC7,
                textureCompression = TextureImporterCompression.Compressed,
            });
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        }

        /// <summary>读配置里的 elements + items，按 asset 去重。</summary>
        private static List<Entry> CollectEntries()
        {
            List<Entry> result = new List<Entry>();
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(ConfigPath);
            if (json == null)
            {
                Debug.LogError("[PieceIconAtlas] 读不到配置：" + ConfigPath);
                return result;
            }

            CatCafeConfigDatabase.Root root =
                JsonUtility.FromJson<CatCafeConfigDatabase.Root>(json.text);
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < root.elements.Length; i++)
            {
                CatCafeConfigDatabase.ElementRow row = root.elements[i];
                if (!row.enabled || string.IsNullOrEmpty(row.asset) || !seen.Add(row.key)) continue;
                result.Add(new Entry { Key = row.key, Asset = row.asset });
            }
            for (int i = 0; i < root.items.Length; i++)
            {
                CatCafeConfigDatabase.ItemRow row = root.items[i];
                if (!row.enabled || string.IsNullOrEmpty(row.asset) || !seen.Add(row.key)) continue;
                result.Add(new Entry { Key = row.key, Asset = row.asset });
            }
            return result;
        }

        /// <summary>
        /// 把一张立绘渲染成 Cell×Cell 的图标：先 Blit 到 RenderTexture 拿到可读像素，
        /// 再按 alpha 裁掉透明边，最后等比缩放居中。
        /// </summary>
        private static Texture2D RenderIcon(string asset)
        {
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteRoot + asset + ".png");
            if (source == null) return null;

            RenderTexture work = RenderTexture.GetTemporary(
                WorkSize, WorkSize, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, work);
            RenderTexture.active = work;
            Texture2D readable = new Texture2D(WorkSize, WorkSize, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, WorkSize, WorkSize), 0, 0);
            readable.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(work);

            RectInt bounds = AlphaBounds(readable);
            Texture2D icon = Downscale(readable, bounds);
            UnityEngine.Object.DestroyImmediate(readable);
            return icon;
        }

        /// <summary>不透明像素的包围盒。全透明时退回整张图，避免除零。</summary>
        private static RectInt AlphaBounds(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a < 8) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < minX || maxY < minY) return new RectInt(0, 0, width, height);
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// 把 bounds 区域等比缩进 Cell×Cell 并居中。
        ///
        /// 用 alpha 加权的面积平均，不能用 GetPixelBilinear：这批立绘的透明区域
        /// RGB 是 (255,253,249) 近白色，四通道独立插值会把这层白渗进图标边缘，
        /// 缩到内联尺寸后就是一圈明显的白边。按 alpha 加权等价于在预乘空间里滤波，
        /// 完全透明的像素不贡献颜色，白边自然消失。
        /// </summary>
        private static Texture2D Downscale(Texture2D source, RectInt bounds)
        {
            Color32[] pixels = source.GetPixels32();
            int sourceWidth = source.width;

            Texture2D result = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false);
            Color[] output = new Color[Cell * Cell];
            for (int i = 0; i < output.Length; i++) output[i] = Color.clear;

            float scale = Mathf.Min(Cell / (float)bounds.width, Cell / (float)bounds.height);
            int drawWidth = Mathf.Max(1, Mathf.RoundToInt(bounds.width * scale));
            int drawHeight = Mathf.Max(1, Mathf.RoundToInt(bounds.height * scale));
            int offsetX = (Cell - drawWidth) / 2;
            int offsetY = (Cell - drawHeight) / 2;

            for (int y = 0; y < drawHeight; y++)
            {
                int sourceY0 = bounds.y + Mathf.FloorToInt(y * bounds.height / (float)drawHeight);
                int sourceY1 = bounds.y + Mathf.CeilToInt((y + 1) * bounds.height / (float)drawHeight);
                sourceY1 = Mathf.Max(sourceY1, sourceY0 + 1);

                for (int x = 0; x < drawWidth; x++)
                {
                    int sourceX0 = bounds.x + Mathf.FloorToInt(x * bounds.width / (float)drawWidth);
                    int sourceX1 = bounds.x + Mathf.CeilToInt((x + 1) * bounds.width / (float)drawWidth);
                    sourceX1 = Mathf.Max(sourceX1, sourceX0 + 1);

                    float weight = 0f, r = 0f, g = 0f, b = 0f, a = 0f;
                    int count = 0;
                    for (int sy = sourceY0; sy < sourceY1; sy++)
                    {
                        for (int sx = sourceX0; sx < sourceX1; sx++)
                        {
                            Color32 texel = pixels[sy * sourceWidth + sx];
                            float alpha = texel.a / 255f;
                            r += texel.r * alpha;
                            g += texel.g * alpha;
                            b += texel.b * alpha;
                            weight += alpha;
                            a += alpha;
                            count++;
                        }
                    }
                    if (count == 0) continue;
                    Color value = weight > 0.0001f
                        ? new Color(r / weight / 255f, g / weight / 255f, b / weight / 255f, a / count)
                        : Color.clear;
                    output[(offsetY + y) * Cell + offsetX + x] = value;
                }
            }

            Dilate(output);
            result.SetPixels(output);
            result.Apply(false, false);
            return result;
        }

        /// <summary>
        /// 把不透明像素的颜色往外扩散几圈，只改透明像素的 RGB、不动它们的 alpha。
        ///
        /// 图标在图集里会被双线性采样和 BC7 压缩，采样点落在主体边界外时会取到相邻
        /// 透明像素的 RGB。让那圈透明像素带上邻居的颜色，边缘就不会再泛出底色。
        /// </summary>
        private static void Dilate(Color[] pixels)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                Color[] snapshot = (Color[])pixels.Clone();
                for (int y = 0; y < Cell; y++)
                {
                    for (int x = 0; x < Cell; x++)
                    {
                        int index = y * Cell + x;
                        if (snapshot[index].a > 0.004f) continue;

                        float r = 0f, g = 0f, b = 0f;
                        int found = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= Cell || ny >= Cell) continue;
                                Color neighbour = snapshot[ny * Cell + nx];
                                if (neighbour.a <= 0.004f) continue;
                                r += neighbour.r;
                                g += neighbour.g;
                                b += neighbour.b;
                                found++;
                            }
                        }
                        if (found == 0) continue;
                        // alpha 保持 0，只借颜色。
                        pixels[index] = new Color(r / found, g / found, b / found, pixels[index].a);
                    }
                }
            }
        }
    }
}
#endif
