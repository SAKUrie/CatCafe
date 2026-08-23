using System;
using TMPro;
using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Single table-driven font source for both TextMeshPro and Legacy Text UI.
    /// </summary>
    internal static class CatCafeUiFontProvider
    {
        private const string FontSettingKey = "ui_font_resource";
        private const string FontSizeScaleSettingKey = "ui_font_size_scale";

        private static Font legacyFont;
        private static TMP_FontAsset tmpFont;

        public static Font LegacyFont
        {
            get
            {
                if (legacyFont == null) legacyFont = LoadConfiguredFont();
                return legacyFont;
            }
        }

        public static TMP_FontAsset TmpFont
        {
            get
            {
                if (tmpFont != null) return tmpFont;

                Font source = LegacyFont;
                tmpFont = TMP_FontAsset.CreateFontAsset(source);
                if (tmpFont == null)
                {
                    throw new InvalidOperationException(
                        "[CatCafeUI] 无法从配置字体创建 TextMeshPro 字体：" + source.name);
                }

                tmpFont.name = source.name + " Runtime SDF";
                tmpFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                tmpFont.isMultiAtlasTexturesEnabled = true;
                return tmpFont;
            }
        }

        /// <summary>
        /// 运行时 UI 的统一字号倍率。字体家族保持不变，具体倍率来自 Settings 表。
        /// 所有页面共用这一个入口，避免各控制器各自硬编码放大比例。
        /// </summary>
        public static float FontSizeScale
        {
            get { return Mathf.Max(0.5f, CatCafeConfigDatabase.GetRequiredFloat(FontSizeScaleSettingKey)); }
        }

        public static float ScaleSize(float baseSize)
        {
            return Mathf.Max(1f, baseSize * FontSizeScale);
        }

        public static int ScaleSize(int baseSize)
        {
            return Mathf.Max(1, Mathf.RoundToInt(baseSize * FontSizeScale));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            // Enter Play Mode may keep the AppDomain alive. Never retain the prior run's
            // generated TMP atlas or a font path from an older exported table.
            legacyFont = null;
            tmpFont = null;
        }

        private static Font LoadConfiguredFont()
        {
            string resourcePath = CatCafeConfigDatabase.GetString(FontSettingKey);
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                throw new InvalidOperationException(
                    "[CatCafeUI] Settings 表缺少已启用的字体资源路径：" + FontSettingKey);
            }

            Font configuredFont = Resources.Load<Font>(resourcePath);
            if (configuredFont == null)
            {
                throw new InvalidOperationException(
                    "[CatCafeUI] 字体资源加载失败：Resources/" + resourcePath);
            }

            return configuredFont;
        }
    }
}
