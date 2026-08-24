#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// Windows 独立包出包入口。
    ///
    /// 输出目录为 &lt;用户&gt;/Downloads/&lt;productName&gt;_&lt;版本号&gt;（当前即 CatCafe_x.y.z）。
    /// 包名取 Player Settings 的 productName；版本号以 Project Settings →
    /// Player → Version（PlayerSettings.bundleVersion）为准，包内版本和目录名同出一源。
    /// 出新版本在 Player Settings 里改即可，不用动代码。
    ///
    /// 命令行：Unity -quit -batchmode -projectPath &lt;项目&gt;
    ///         -executeMethod ManyFace.CatCafe.Editor.CatCafeBuild.BuildWindows
    /// </summary>
    internal static class CatCafeBuild
    {
        /// <summary>
        /// 包名。仓库名 many_face 是上一代「变脸解谜」概念的遗留，游戏早就叫猫咖了，
        /// Player Settings 的 productName 也已经是 CatCafe——这里跟着它走，
        /// 免得再出现「目录叫 many_face、产品叫 CatCafe」两套名字。
        /// productName 意外为空时退回常量，不让出包卡在一个设置项上。
        /// </summary>
        private const string FallbackProductName = "CatCafe";

        private static string ProductName
        {
            get
            {
                string name = PlayerSettings.productName;
                return string.IsNullOrWhiteSpace(name) ? FallbackProductName : name.Trim();
            }
        }

        private static string ExecutableName { get { return ProductName + ".exe"; } }

        [MenuItem("Tools/Cat Cafe/构建 Windows 包", false, 100)]
        private static void BuildFromMenu()
        {
            BuildFromMenu(false);
        }

        /// <summary>WebGL 包：itch.io 网页版发布用。输出是一整个目录（index.html + Build/），
        /// 没有可执行文件名的概念，直接拿目录本身当 locationPathName。</summary>
        [MenuItem("Tools/Cat Cafe/构建 WebGL 包", false, 102)]
        private static void BuildWebGLFromMenu()
        {
            string error;
            if (!ValidateVersion(out error))
            {
                EditorUtility.DisplayDialog("构建 WebGL 包", "失败：\n" + error, "知道了");
                return;
            }

            string output = ResolveOutputPath("_WebGL");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "目标目录已存在",
                    output + "\n\n继续会覆盖里面的同名文件。\n" +
                    "想留着旧包就先取消，去 Project Settings → Player → Version 改版本号。",
                    "覆盖", "取消");
                if (!overwrite) return;
            }

            bool ok = RunWebGL(output, out error);
            EditorUtility.DisplayDialog("构建 WebGL 包",
                ok ? "成功：\n" + output : "失败：\n" + error, "知道了");
        }

        /// <summary>批处理入口，供 itch 发布脚本调用：
        /// Unity -quit -batchmode -projectPath &lt;项目&gt;
        ///       -executeMethod ManyFace.CatCafe.Editor.CatCafeBuild.BuildWebGLCli</summary>
        private static void BuildWebGLCli()
        {
            string output = ResolveOutputPath("_WebGL");
            string error;
            if (!RunWebGL(output, out error))
            {
                Debug.LogError("[CatCafeBuild] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 开发版包：带完整托管堆栈和 Debug.Log 输出。正式包崩了但看不出原因时用这个，
        /// Player.log 会给出真正的调用栈而不是一串没有符号的地址。
        /// </summary>
        [MenuItem("Tools/Cat Cafe/构建 Windows 包（开发版·带堆栈）", false, 101)]
        private static void BuildDevelopmentFromMenu()
        {
            BuildFromMenu(true);
        }

        private static void BuildFromMenu(bool development)
        {
            string error;
            if (!ValidateVersion(out error))
            {
                EditorUtility.DisplayDialog("构建 Windows 包", "失败：\n" + error, "知道了");
                return;
            }

            string output = ResolveOutputPath(development);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "目标目录已存在",
                    output + "\n\n继续会覆盖里面的同名文件。\n" +
                    "想留着旧包就先取消，去 Project Settings → Player → Version 改版本号。",
                    "覆盖", "取消");
                if (!overwrite) return;
            }

            bool ok = Run(output, development, out error);
            EditorUtility.DisplayDialog("构建 Windows 包",
                ok ? "成功：\n" + output : "失败：\n" + error, "知道了");
        }

        /// <summary>批处理入口。失败时以非零码退出，方便 CI 判定。</summary>
        private static void BuildWindows()
        {
            string output = ResolveOutputPath(false);
            string error;
            if (!Run(output, false, out error))
            {
                Debug.LogError("[CatCafeBuild] " + error);
                EditorApplication.Exit(1);
            }
        }

        private static string ResolveOutputPath(bool development)
        {
            return ResolveOutputPath(development ? "_dev" : string.Empty);
        }

        private static string ResolveOutputPath(string suffix)
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Path.Combine(downloads,
                ProductName + "_" + SanitizeVersion(PlayerSettings.bundleVersion) + suffix);
        }

        /// <summary>版本号空着会拼出 CatCafe_ 这种目录名，出包前先拦下来。</summary>
        private static bool ValidateVersion(out string error)
        {
            if (!string.IsNullOrEmpty(PlayerSettings.bundleVersion) &&
                PlayerSettings.bundleVersion.Trim().Length > 0)
            {
                error = string.Empty;
                return true;
            }

            error = "Project Settings → Player → Version 是空的，先填个版本号再出包。";
            return false;
        }

        /// <summary>
        /// Player Settings 的版本号是自由文本，可能带空格或斜杠这类不能进路径的字符，
        /// 统一换成下划线，免得拼出来的目录建不出来。
        /// </summary>
        private static string SanitizeVersion(string version)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string trimmed = (version ?? string.Empty).Trim();
            StringBuilder safe = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                safe.Append(c == ' ' || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return safe.ToString();
        }

        /// <summary>
        /// 闪屏必须保持关闭。ce9250a：ProjectSettings 被多个编辑器版本先后序列化后，
        /// 闪屏渲染路径（DrawSplashScreenBackground → Material::SetTextureInternal）
        /// 在独立包中解引用坏 PPtr 导致启动即崩；编辑器不渲染玩家闪屏，问题只在包里暴露。
        /// YAML 层无可修字段（与可正常启动的历史状态逐字段一致），根源在烹制后的二进制
        /// 数据，故由构建入口强制关闭，防止勾选回归。
        /// </summary>
        private static void EnforceSplashDisabled()
        {
            if (!PlayerSettings.SplashScreen.show && !PlayerSettings.SplashScreen.showUnityLogo)
            {
                return;
            }

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            AssetDatabase.SaveAssets();
            Debug.LogWarning("[CatCafeBuild] 检测到 Unity 闪屏被重新启用，已自动关闭。" +
                "该项目开启闪屏会导致独立包启动即崩（详见提交 ce9250a），请勿再勾选。");
        }

        /// <summary>
        /// 运行时用 Shader.Find 取的自制 Shader 必须进 Always Included Shaders。
        ///
        /// 这类 Shader 没有任何场景或预制体引用它——材质是运行时 new 出来的——
        /// 于是打包时会被整个剥掉。编辑器里资源都在，Shader.Find 照常返回，
        /// 只有独立包里返回 null，画面退化成缺图占位（大厅三只序列帧猫变成洋红方块）。
        /// 这种只在包里暴露的问题和闪屏那条同类，同样由构建入口兜住。
        ///
        /// 做法是扫源码里的 Shader.Find("…")，缺哪个补哪个，而不是钉死一张名单——
        /// 以后谁再加一个运行时 Shader，不用记得回来改这里。
        /// </summary>
        private static void EnforceRuntimeShadersIncluded()
        {
            List<string> wanted = new List<string>();
            string scripts = Path.Combine(Application.dataPath, "Scripts");
            if (Directory.Exists(scripts))
            {
                foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(file),
                        "Shader\\.Find\\(\\s*\"([^\"]+)\"\\s*\\)"))
                    {
                        string name = match.Groups[1].Value;
                        if (!wanted.Contains(name)) wanted.Add(name);
                    }
                }
            }
            if (wanted.Count == 0) return;

            SerializedObject settings = new SerializedObject(
                UnityEngine.Rendering.GraphicsSettings.GetGraphicsSettings());
            SerializedProperty list = settings.FindProperty("m_AlwaysIncludedShaders");
            HashSet<Shader> included = new HashSet<Shader>();
            for (int i = 0; i < list.arraySize; i++)
            {
                Shader entry = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (entry != null) included.Add(entry);
            }

            List<string> added = new List<string>();
            foreach (string name in wanted)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning("[CatCafeBuild] 源码里 Shader.Find(\"" + name +
                        "\") 找不到对应 Shader，包里也一定取不到。");
                    continue;
                }
                // 内置 Shader 不会被剥掉，也不该往名单里塞。
                if (!AssetDatabase.GetAssetPath(shader).StartsWith("Assets/")) continue;
                if (!included.Add(shader)) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                added.Add(name);
            }
            if (added.Count == 0) return;

            settings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.LogWarning("[CatCafeBuild] 以下运行时 Shader 不在 Always Included Shaders 里，" +
                "已自动补上（否则独立包里 Shader.Find 会返回 null）：\n  " +
                string.Join("\n  ", added.ToArray()));
        }

        private static bool Run(string output, bool development, out string error)
        {
            if (!ValidateVersion(out error)) return false;
            EnforceSplashDisabled();
            EnforceRuntimeShadersIncluded();

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                error = "Build Settings 里没有启用任何场景。";
                return false;
            }

            Directory.CreateDirectory(output);

            StringBuilder log = new StringBuilder();
            log.AppendLine("[CatCafeBuild] 开始构建");
            log.AppendLine("  版本 " + PlayerSettings.bundleVersion +
                "（" + PlayerSettings.companyName + " / " + PlayerSettings.productName + "）");
            log.AppendLine("  输出 " + output + (development ? "（开发版）" : string.Empty));
            for (int i = 0; i < scenes.Length; i++)
            {
                log.AppendLine("  场景 " + i + "  " + scenes[i]);
            }
            Debug.Log(log.ToString());

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(output, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                error = string.Format("构建{0}，{1} 个错误。详见 Console。",
                    summary.result == BuildResult.Cancelled ? "被取消" : "失败",
                    summary.totalErrors);
                return false;
            }

            // Burst 调试符号目录名字就叫 DoNotShip，发包前总得有人记着删——改成自动删。
            string burstDebug = Path.Combine(output,
                Path.GetFileNameWithoutExtension(ExecutableName) + "_BurstDebugInformation_DoNotShip");
            if (Directory.Exists(burstDebug))
            {
                Directory.Delete(burstDebug, true);
                Debug.Log("[CatCafeBuild] 已移除 " + Path.GetFileName(burstDebug));
            }

            Debug.Log(string.Format(
                "[CatCafeBuild] 构建成功：{0}\n  产物 {1:F1} MB，用时 {2:F0} 秒",
                output, summary.totalSize / (1024f * 1024f), summary.totalTime.TotalSeconds));
            return true;
        }

        /// <summary>WebGL 版构建逻辑，与 <see cref="Run"/> 平行：同样的场景收集、闪屏/
        /// Shader 兜底，唯独没有 Burst 调试符号目录要清（那是独立包才有的产物）。</summary>
        private static bool RunWebGL(string output, out string error)
        {
            if (!ValidateVersion(out error)) return false;
            EnforceSplashDisabled();
            EnforceRuntimeShadersIncluded();

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                error = "Build Settings 里没有启用任何场景。";
                return false;
            }

            Directory.CreateDirectory(output);

            StringBuilder log = new StringBuilder();
            log.AppendLine("[CatCafeBuild] 开始构建（WebGL）");
            log.AppendLine("  版本 " + PlayerSettings.bundleVersion +
                "（" + PlayerSettings.companyName + " / " + PlayerSettings.productName + "）");
            log.AppendLine("  输出 " + output);
            for (int i = 0; i < scenes.Length; i++)
            {
                log.AppendLine("  场景 " + i + "  " + scenes[i]);
            }
            Debug.Log(log.ToString());

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                error = string.Format("构建{0}，{1} 个错误。详见 Console。",
                    summary.result == BuildResult.Cancelled ? "被取消" : "失败",
                    summary.totalErrors);
                return false;
            }

            Debug.Log(string.Format(
                "[CatCafeBuild] 构建成功：{0}\n  产物 {1:F1} MB，用时 {2:F0} 秒",
                output, summary.totalSize / (1024f * 1024f), summary.totalTime.TotalSeconds));
            return true;
        }
    }
}
#endif
