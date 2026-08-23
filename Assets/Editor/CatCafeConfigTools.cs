#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// 猫咖配置表的 Unity 菜单入口。
    /// Excel 是唯一策划源；菜单调用项目内导出脚本，校验后刷新运行时 JSON。
    /// </summary>
    public static class CatCafeConfigTools
    {
        private const string MenuRoot = "Tools/Cat Cafe/配置表/";
        private const string WorkbookRelativePath = "GameDesign/CatCafeGameConfig.xlsx";
        private const string RuntimeJsonAssetPath = "Assets/Resources/GameData/cat_cafe_config.json";

        [MenuItem(MenuRoot + "打开 Excel", false, 10)]
        private static void OpenWorkbook()
        {
            string workbook = ProjectPath(WorkbookRelativePath);
            if (!File.Exists(workbook))
            {
                ShowError("找不到配置表：\n" + workbook);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = workbook,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                ShowError("无法打开 Excel：\n" + exception.Message);
            }
        }

        [MenuItem(MenuRoot + "导出 Excel 到 JSON", false, 20)]
        private static void ExportWorkbook()
        {
            RunExporter(false);
        }

        [MenuItem(MenuRoot + "仅校验 Excel", false, 21)]
        private static void ValidateWorkbook()
        {
            RunExporter(true);
        }

        [MenuItem(MenuRoot + "打开 Excel", true)]
        [MenuItem(MenuRoot + "导出 Excel 到 JSON", true)]
        [MenuItem(MenuRoot + "仅校验 Excel", true)]
        private static bool CanUseConfigTools()
        {
            // Play Mode 中静态配置已经载入内存；禁止此时导出，避免误以为能热更新当前对局。
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorApplication.isCompiling && !EditorApplication.isUpdating;
        }

        private static void RunExporter(bool checkOnly)
        {
            string workbook = ProjectPath(WorkbookRelativePath);
            if (!File.Exists(workbook))
            {
                ShowError("找不到配置表：\n" + workbook);
                return;
            }

            try
            {
                string outputPath = ProjectPath(RuntimeJsonAssetPath);
                string summary = CatCafeExcelExporter.Export(workbook, outputPath, checkOnly);
                if (!checkOnly)
                {
                    AssetDatabase.ImportAsset(RuntimeJsonAssetPath, ImportAssetOptions.ForceUpdate);
                    AssetDatabase.Refresh();
                }

                string action = checkOnly ? "校验通过" : "导出成功，Unity JSON 已刷新";
                Debug.Log("[CatCafeConfig] " + action + "\n" + summary);
                EditorUtility.DisplayDialog("猫咖配置表", action + "\n\n" + summary, "确定");
            }
            catch (Exception exception)
            {
                string action = checkOnly ? "校验" : "导出";
                ShowError("配置表" + action + "失败：\n\n" + exception.Message);
            }
        }

        private static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        private static string ProjectPath(string relativePath)
        {
            return Path.Combine(ProjectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void ShowError(string message)
        {
            Debug.LogError("[CatCafeConfig] " + message);
            EditorUtility.DisplayDialog("猫咖配置表", message, "确定");
        }
    }
}
#endif
