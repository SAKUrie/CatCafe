#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// 校验 BGM 配置与资源是否对得上：Settings 表里每个 bgm_tracks_&lt;场景&gt; 引用的曲目
    /// 都要能被 Resources.Load 找到，导入设置也要适合长循环音乐。
    /// 策划改完表跑一次就知道有没有写错曲目名，不用进 Play 听。
    /// </summary>
    internal static class CatCafeAudioVerify
    {
        private const string TrackKeyPrefix = "bgm_tracks_";

        [MenuItem("Tools/Cat Cafe/校验 BGM 配置")]
        private static void VerifyFromMenu()
        {
            string report;
            bool passed = Run(out report);
            Debug.Log(report);
            EditorUtility.DisplayDialog("BGM 配置校验",
                passed ? "全部通过。\n\n详情见 Console。" : "发现问题，详情见 Console。", "知道了");
        }

        /// <summary>批处理入口：Unity -batchmode -executeMethod ...CatCafeAudioVerify.Verify</summary>
        private static void Verify()
        {
            string report;
            bool passed = Run(out report);
            Debug.Log(report);
            if (!passed) EditorApplication.Exit(1);
        }

        private static bool Run(out string report)
        {
            CatCafeConfigDatabase.EnsureLoaded();

            StringBuilder log = new StringBuilder();
            log.AppendLine("[CatCafeBGM] 配置校验");

            string folder = CatCafeConfigDatabase.GetString("bgm_resource_folder", "CatCafe/Bgm");
            log.AppendLine("  资源目录 Resources/" + folder);
            log.AppendLine("  基准音量 " + CatCafeConfigDatabase.GetFloat("bgm_base_volume", 0.45f) +
                "，交叉淡入 " + CatCafeConfigDatabase.GetFloat("bgm_crossfade_seconds", 2f) + "s" +
                "，乱序 " + CatCafeConfigDatabase.GetBool("bgm_shuffle", true));

            AudioClip[] onDisk = Resources.LoadAll<AudioClip>(folder);
            HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
            bool passed = true;

            CatCafeConfigDatabase.SettingRow[] settings = CatCafeConfigDatabase.Data.settings;
            int playlists = 0;
            for (int i = 0; i < settings.Length; i++)
            {
                CatCafeConfigDatabase.SettingRow row = settings[i];
                if (!row.enabled || row.key == null || !row.key.StartsWith(TrackKeyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                playlists++;
                string scene = row.key.Substring(TrackKeyPrefix.Length);
                string[] tracks = (row.value ?? string.Empty).Split(',');
                log.AppendLine("  场景 " + scene + "：" + tracks.Length + " 首");

                for (int t = 0; t < tracks.Length; t++)
                {
                    string track = tracks[t].Trim();
                    if (track.Length == 0) continue;

                    referenced.Add(track);
                    AudioClip clip = Resources.Load<AudioClip>(folder + "/" + track);
                    if (clip == null)
                    {
                        log.AppendLine("    × " + track + " —— Resources 里找不到");
                        passed = false;
                        continue;
                    }

                    log.AppendLine(string.Format(
                        "    √ {0,-26} {1,6:0.0}s  {2}Hz  {3}声道  {4}  preload={5}",
                        track, clip.length, clip.frequency, clip.channels,
                        clip.loadType, clip.preloadAudioData));

                    if (clip.loadType != AudioClipLoadType.CompressedInMemory)
                    {
                        // Streaming 会让打包后的播放器在音频后端原生崩溃，必须拦住。
                        log.AppendLine("      ! 应为 CompressedInMemory，当前是 " + clip.loadType);
                        passed = false;
                    }
                }
            }

            if (playlists == 0)
            {
                log.AppendLine("  × Settings 表里没有任何 " + TrackKeyPrefix + "<场景名> 行");
                passed = false;
            }

            for (int i = 0; i < onDisk.Length; i++)
            {
                if (!referenced.Contains(onDisk[i].name))
                {
                    log.AppendLine("  ! " + onDisk[i].name + " 在目录里但没有任何播放列表引用它");
                }
            }

            log.AppendLine(passed ? "[CatCafeBGM] 校验通过" : "[CatCafeBGM] 校验未通过");
            report = log.ToString();
            return passed;
        }
    }
}
#endif
