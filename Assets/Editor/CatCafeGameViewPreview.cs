#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// Gives non-technical team members one menu command for the project's authored
    /// 1536x864 presentation. This changes only the editor Game view, never runtime data.
    /// </summary>
    internal static class CatCafeGameViewPreview
    {
        [MenuItem("Tools/Cat Cafe/预览/720p - 1280x720", false, 1)]
        private static void Use720p() { UsePreview(1280, 720, "猫咖 720p"); }

        [MenuItem("Tools/Cat Cafe/预览/设计基准 - 1536x864", false, 2)]
        private static void UseDesignPreview() { UsePreview(1536, 864, "猫咖设计基准"); }

        [MenuItem("Tools/Cat Cafe/预览/1080p - 1920x1080", false, 3)]
        private static void Use1080p() { UsePreview(1920, 1080, "猫咖 1080p"); }

        [MenuItem("Tools/Cat Cafe/预览/1440p - 2560x1440", false, 4)]
        private static void Use1440p() { UsePreview(2560, 1440, "猫咖 1440p"); }

        private static void UsePreview(int width, int height, string label)
        {
            EditorPrefs.SetBool("UseLowResolutionForAspectRatios", false);
            Assembly editorAssembly = typeof(EditorWindow).Assembly;
            Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
            Type sizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
            Type sizeKindType = editorAssembly.GetType("UnityEditor.GameViewSizeType");
            Type gameViewType = editorAssembly.GetType("UnityEditor.GameView");
            if (sizesType == null || sizeType == null || sizeKindType == null || gameViewType == null)
            {
                Debug.LogWarning("[CatCafe Preview] 当前 Unity 版本不支持自动设置 Game 预览，请手动选择 1536x864 Fixed Resolution。");
                return;
            }

            object sizes = sizesType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            object group = sizesType.GetMethod("GetGroup")?.Invoke(sizes, new object[] { CurrentGroupType() });
            if (group == null) return;

            int index = FindSize(group, width, height);
            if (index < 0)
            {
                object fixedResolution = Enum.Parse(sizeKindType, "FixedResolution");
                object newSize = Activator.CreateInstance(
                    sizeType,
                    fixedResolution,
                    width,
                    height,
                    label);
                group.GetType().GetMethod("AddCustomSize")?.Invoke(group, new[] { newSize });
                index = FindSize(group, width, height);
            }

            EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
            PropertyInfo selectedSizeIndex = gameViewType.GetProperty(
                "selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            selectedSizeIndex?.SetValue(gameView, index);
            gameView.Show();
            gameView.Focus();
            gameView.Repaint();
            Debug.Log($"[CatCafe Preview] 已切换到 {width}x{height}。Game 窗口若放不下会显示缩放比例；查看像素细节时将 Scale 调为 1x。");
        }

        [MenuItem("Tools/Cat Cafe/预览/预览清晰度说明", false, 20)]
        private static void ShowHelp()
        {
            EditorUtility.DisplayDialog(
                "猫咖清晰预览",
                "1. 720p/1080p/1440p 用于验证不同玩家分辨率。\n" +
                "2. 1536x864 是当前 UI 设计坐标基准，不是玩家分辨率限制。\n" +
                "3. 想看原始像素时，把 Game 页签的 Scale 调为 1x。\n" +
                "4. 只选 16:9 会随窗口尺寸缩放，小窗口发糊不代表玩家构建画面发糊。\n" +
                "5. 当前只承诺 16:9；非 16:9 需要另行设计裁切或留边规则。",
                "知道了");
        }

        private static int FindSize(object group, int targetWidth, int targetHeight)
        {
            MethodInfo getTotalCount = group.GetType().GetMethod("GetTotalCount");
            MethodInfo getGameViewSize = group.GetType().GetMethod("GetGameViewSize");
            int count = Convert.ToInt32(getTotalCount?.Invoke(group, null));
            for (int index = 0; index < count; index++)
            {
                object size = getGameViewSize?.Invoke(group, new object[] { index });
                if (size == null) continue;
                int width = Convert.ToInt32(size.GetType().GetProperty("width")?.GetValue(size));
                int height = Convert.ToInt32(size.GetType().GetProperty("height")?.GetValue(size));
                if (width == targetWidth && height == targetHeight) return index;
            }
            return -1;
        }

        private static object CurrentGroupType()
        {
            Type groupType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeGroupType");
            string value;
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                value = "Android";
            }
            else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
            {
                value = "iOS";
            }
            else
            {
                value = "Standalone";
            }
            return Enum.Parse(groupType, value);
        }
    }
}
#endif
