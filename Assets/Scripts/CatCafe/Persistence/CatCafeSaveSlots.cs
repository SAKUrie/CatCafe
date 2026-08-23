using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 多档存档（世界观里叫「小店」）。<see cref="CatCafeMeta"/> 只认「当前档」，
    /// 档位的选择、迁移、摘要和删除全部收在这里，Meta 那边只需要问 <see cref="CurrentPath"/>。
    ///
    /// 落盘布局：persistentDataPath/catcafe_save_{1..N}.json（各自带 .bak）。
    /// 单档时代的 catcafe_save.json 首次启动时自动搬进 1 号店，老玩家不掉档。
    /// 当前档号存在 PlayerPrefs，和存档文件本身分开——删掉某个档不影响别的档。
    /// </summary>
    public static class CatCafeSaveSlots
    {
        private const string ActiveSlotKey = "catcafe_active_slot";
        private const string LegacyFileName = "catcafe_save.json";

        /// <summary>只读摘要：不惊动 <see cref="CatCafeMeta"/> 的内存状态，纯粹为了画列表。</summary>
        public struct Summary
        {
            public bool Exists;
            public int Runs;
            public int Wins;
            public int Discovered;
        }

        // JsonUtility 会忽略 JSON 里多出来的字段，所以这里只声明画列表要用的那几个。
        [Serializable]
        private sealed class SummaryDto
        {
            public int runs;
            public int wins;
            public List<DexKeyDto> dex = new List<DexKeyDto>();
        }

        [Serializable]
        private sealed class DexKeyDto { public string key; }

        private static bool migrated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            migrated = false;
        }

        public static int SlotCount
        {
            get { return Mathf.Clamp(CatCafeConfigDatabase.GetInt("meta_save_slot_count", 3), 1, 9); }
        }

        /// <summary>当前档号（1-based）。越界或没设过时回落到 1 号店。</summary>
        public static int Current
        {
            get
            {
                EnsureMigrated();
                int slot = PlayerPrefs.GetInt(ActiveSlotKey, 1);
                return slot >= 1 && slot <= SlotCount ? slot : 1;
            }
        }

        public static string CurrentPath { get { return PathOf(Current); } }

        public static string PathOf(int slot)
        {
            return Path.Combine(Application.persistentDataPath,
                "catcafe_save_" + Mathf.Clamp(slot, 1, 9) + ".json");
        }

        private static string LegacyRunPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath,
                "catcafe_run_" + Mathf.Clamp(slot, 1, 9) + ".json");
        }

        /// <summary>换一家小店。切档必须清掉 Meta 的内存状态，否则会把上一家的数据写进这一家。</summary>
        public static void Select(int slot)
        {
            EnsureMigrated();
            if (slot < 1 || slot > SlotCount) return;
            if (slot == Current) return;
            CatCafeMeta.Unload();
            PlayerPrefs.SetInt(ActiveSlotKey, slot);
            PlayerPrefs.Save();
        }

        public static bool Exists(int slot)
        {
            EnsureMigrated();
            return File.Exists(PathOf(slot));
        }

        /// <summary>
        /// 开一家全新的小店：换档 + 把教程状态清成"从零开始"，并立刻落盘建档。
        ///
        /// 教程开关和已读位现在都在存档里，新档本来就会落到配置默认；这里再显式清
        /// 一次是为了覆盖「收摊删档后又开同一号」这类内存里可能还留着上一家状态的
        /// 路径，不依赖调用顺序。
        ///
        /// 立刻落盘是为了让玩家进去转一圈就退出时，列表里这家已经是"开张"状态。
        /// </summary>
        public static void BeginNewShop(int slot)
        {
            EnsureMigrated();
            if (slot < 1 || slot > SlotCount) return;
            Select(slot);
            CatCafeMeta.ResetTutorials();       // 清已读 + 开关归默认，内部 SaveNow 建档
        }

        /// <summary>收摊：删掉这家店的存档。删的是当前档时顺手清内存，避免下一次写盘又把它复活。</summary>
        public static void Delete(int slot)
        {
            EnsureMigrated();
            if (slot < 1 || slot > SlotCount) return;
            if (slot == Current) CatCafeMeta.Unload();
            TryDelete(PathOf(slot));
            TryDelete(PathOf(slot) + ".bak");
            // 清理旧版本留下的跨场景局内快照；当前版本不会再创建它。
            TryDelete(LegacyRunPath(slot));
            TryDelete(LegacyRunPath(slot) + ".tmp");
        }

        public static Summary Read(int slot)
        {
            EnsureMigrated();
            Summary summary = new Summary();
            string path = PathOf(slot);
            if (!File.Exists(path)) return summary;
            try
            {
                SummaryDto dto = JsonUtility.FromJson<SummaryDto>(File.ReadAllText(path));
                if (dto == null) return summary;
                summary.Exists = true;
                summary.Runs = dto.runs;
                summary.Wins = dto.wins;
                summary.Discovered = dto.dex == null ? 0 : dto.dex.Count;
            }
            catch (Exception e)
            {
                // 坏档也要能在列表里显示出来，否则玩家只会看到一个空位，没法删。
                Debug.LogWarning("[CatCafeSaveSlots] 第 " + slot + " 家小店的存档读不出来：" + e.Message);
                summary.Exists = true;
            }
            return summary;
        }

        /// <summary>单档时代的存档搬进 1 号店。只在进程内做一次；搬完删掉旧文件，避免下次又搬一遍。</summary>
        private static void EnsureMigrated()
        {
            if (migrated) return;
            migrated = true;
            string legacy = Path.Combine(Application.persistentDataPath, LegacyFileName);
            if (!File.Exists(legacy)) return;
            string target = PathOf(1);
            try
            {
                // 1 号店已经有档就不覆盖：多半是迁移过一次之后旧文件又被还原出来了。
                if (!File.Exists(target)) File.Copy(legacy, target);
                TryDelete(legacy);
                TryDelete(legacy + ".bak");
                Debug.Log("[CatCafeSaveSlots] 旧存档已搬进 1 号小店。");
            }
            catch (Exception e)
            {
                Debug.LogError("[CatCafeSaveSlots] 旧存档迁移失败，仍按 1 号店新开：" + e.Message);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("[CatCafeSaveSlots] 删除失败 " + path + "：" + e.Message); }
        }
    }
}
