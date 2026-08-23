using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 局外存档（MetaState）：图鉴、等级、罐头、绒毛、收钱罐、统计。
    /// JSON 明文存 persistentDataPath，原子写（tmp → Replace），坏档自动备份重建。
    /// 数值规则见 Docs/Design/MetaGameDesign.md §5–§7。
    /// </summary>
    public static class CatCafeMeta
    {
        [Serializable]
        private class DexDto { public string key; public int n; public int lv; public string src; }

        [Serializable]
        private class FurDto { public string key; public int v; }
        [Serializable]
        private class IntimacyDto { public string key; public int v; }

        [Serializable]
        private class SaveDto
        {
            public int version = 3;
            public int cans;
            public long jarLastMs;
            public long furLastMs;
            public int runs;
            public int wins;
            public List<DexDto> dex = new List<DexDto>();
            public List<FurDto> fur = new List<FurDto>();
            public List<string> tutorialRead = new List<string>();
            public List<IntimacyDto> intimacy = new List<IntimacyDto>();
            public int homeSeenDexCount;

            // 新手教程开关。刻意用三态 int 而不是 bool：JsonUtility 读老存档时
            // 缺字段的 bool 一律变成 false，那会把所有老档的教程静默关掉。
            // 0=没记过（用配置默认）、1=开、2=关。
            public int tutorialMode;
        }

        // 收钱罐数值来自 Excel Settings；这里只保留存档与计时算法。
        private static float JarRatePerMinutePerCat
        {
            get { return CatCafeConfigDatabase.GetFloat("meta_jar_rate_per_minute_per_cat", 0.2f); }
        }

        private static int JarCap
        {
            get { return CatCafeConfigDatabase.GetInt("meta_jar_cap", 50); }
        }

        private static int NaturalFurIntervalMinutes
        {
            get { return CatCafeConfigDatabase.GetRequiredInt("meta_fur_natural_interval_minutes"); }
        }

        private static int NaturalFurAmountPerInterval
        {
            get { return CatCafeConfigDatabase.GetRequiredInt("meta_fur_natural_amount_per_interval"); }
        }

        private static int NaturalFurCapPerBreed
        {
            get { return CatCafeConfigDatabase.GetRequiredInt("meta_fur_natural_cap_per_breed"); }
        }

        private static bool loaded;
        private static SaveDto data;
        private static Dictionary<string, DexDto> dexMap;
        private static Dictionary<string, int> furMap;
        private static Dictionary<string, int> intimacyMap;

        // 档位（世界观里的"哪一家小店"）由 CatCafeSaveSlots 管；这里只认它给的路径。
        private static string SavePath
        {
            get { return CatCafeSaveSlots.CurrentPath; }
        }

        // 关闭 Domain Reload 时静态状态会跨播放会话残留；多档之后残留的后果是
        // 上一次运行选中的那家店被写进这一次选中的店，所以这里必须显式清一次。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Unload();
        }

        /// <summary>
        /// 丢掉内存里的存档状态，下次 EnsureLoaded 从盘上重读。
        /// 换档和删档必须先调它，否则上一家小店的数据会被写进新选的那家。
        /// </summary>
        public static void Unload()
        {
            loaded = false;
            data = null;
            dexMap = null;
            furMap = null;
            intimacyMap = null;
        }

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            data = null;
            try
            {
                if (File.Exists(SavePath))
                {
                    data = JsonUtility.FromJson<SaveDto>(File.ReadAllText(SavePath));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[CatCafeMeta] 存档解析失败，已备份重建：" + e.Message);
                try { File.Copy(SavePath, SavePath + ".corrupt." + NowMs(), true); } catch { }
            }
            if (data == null) data = new SaveDto { jarLastMs = NowMs() };
            if (data.jarLastMs == 0) data.jarLastMs = NowMs();
            if (data.dex == null) data.dex = new List<DexDto>();
            if (data.fur == null) data.fur = new List<FurDto>();
            if (data.tutorialRead == null) data.tutorialRead = new List<string>();
            if (data.intimacy == null) data.intimacy = new List<IntimacyDto>();
            data.version = 3;

            dexMap = new Dictionary<string, DexDto>();
            for (int i = 0; i < data.dex.Count; i++) dexMap[data.dex[i].key] = data.dex[i];
            furMap = new Dictionary<string, int>();
            for (int i = 0; i < data.fur.Count; i++) furMap[data.fur[i].key] = data.fur[i].v;
            intimacyMap = new Dictionary<string, int>();
            for (int i = 0; i < data.intimacy.Count; i++) intimacyMap[data.intimacy[i].key] = data.intimacy[i].v;

            // 初始猫（unlock=base）自动点亮图鉴
            CatCatalog.EnsureLoaded();
            List<CatCatalog.CatRow> breedsAll = CatCatalog.DexBreeds();
            for (int i = 0; i < breedsAll.Count; i++)
            {
                if (breedsAll[i].Unlock == "base" && !dexMap.ContainsKey(breedsAll[i].Key))
                {
                    DexDto entry = new DexDto { key = breedsAll[i].Key, n = 1, lv = 1, src = "base" };
                    dexMap[entry.key] = entry;
                    data.dex.Add(entry);
                    intimacyMap[entry.key] = Mathf.Max(0, intimacyMap.ContainsKey(entry.key) ? intimacyMap[entry.key] : 0);
                }
            }
            // 旧存档没有自然绒毛时间戳：从本次加载开始计时，不追溯补发，避免升级版本时突然灌满。
            if (data.furLastMs <= 0) data.furLastMs = NowMs();
        }

        public static void SaveNow()
        {
            EnsureLoaded();
            data.fur.Clear();
            foreach (KeyValuePair<string, int> pair in furMap)
            {
                if (pair.Value > 0) data.fur.Add(new FurDto { key = pair.Key, v = pair.Value });
            }
            data.intimacy.Clear();
            foreach (KeyValuePair<string, int> pair in intimacyMap)
                if (pair.Value > 0) data.intimacy.Add(new IntimacyDto { key = pair.Key, v = pair.Value });
            string json = JsonUtility.ToJson(data);
            string tmp = SavePath + ".tmp";
            try
            {
                File.WriteAllText(tmp, json);
                if (File.Exists(SavePath)) File.Replace(tmp, SavePath, SavePath + ".bak");
                else File.Move(tmp, SavePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[CatCafeMeta] 写盘失败：" + e.Message);
            }
        }

        /* ── 罐头 ── */

        public static int Cans { get { EnsureLoaded(); return data.cans; } }

        public static void AddCans(int value)
        {
            EnsureLoaded();
            data.cans += Mathf.Max(0, value);
        }

        public static bool TrySpendCans(int value)
        {
            EnsureLoaded();
            if (data.cans < value) return false;
            data.cans -= value;
            return true;
        }

        /* ── 图鉴 ── */

        public static bool IsDiscovered(string breedKey)
        {
            EnsureLoaded();
            return dexMap.ContainsKey(breedKey);
        }

        public static int LevelOf(string breedKey)
        {
            EnsureLoaded();
            DexDto entry;
            return dexMap.TryGetValue(breedKey, out entry) ? entry.lv : 0;
        }

        public static int CountOf(string breedKey)
        {
            EnsureLoaded();
            DexDto entry;
            return dexMap.TryGetValue(breedKey, out entry) ? entry.n : 0;
        }

        /* ── 房东奶奶字条 ── */

        public static bool HasReadTutorial(string id)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(id) && data.tutorialRead.Contains(id);
        }

        public static void MarkTutorialRead(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id) || data.tutorialRead.Contains(id)) return;
            data.tutorialRead.Add(id);
            SaveNow();
        }

        public static void ResetTutorials()
        {
            EnsureLoaded();
            data.tutorialRead.Clear();
            data.tutorialMode = 0;          // 回到「按配置默认」，即开
            SaveNow();
        }

        /// <summary>
        /// 这家小店要不要显示房东奶奶的字条。
        ///
        /// 存在**存档里**，不是 PlayerPrefs——这是刻意的。放设备级会出两种事故：
        /// 一是「确定收起」按一次，这台机器上之后新开的每一家店都零教程；二是
        /// Windows 的 PlayerPrefs 在注册表里，卸载重装都不清，所谓「全新安装」
        /// 照样继承上一次的关闭状态。挪进存档之后，新档天然没有这个字段，
        /// 落到配置默认，结构上不可能继承别人的选择。
        /// </summary>
        public static bool TutorialEnabled
        {
            get
            {
                EnsureLoaded();
                if (data.tutorialMode == 1) return true;
                if (data.tutorialMode == 2) return false;
                return CatCafeConfigDatabase.GetRequiredBool("tutorial_enabled_default");
            }
            set
            {
                EnsureLoaded();
                int mode = value ? 1 : 2;
                if (data.tutorialMode == mode) return;
                data.tutorialMode = mode;
                SaveNow();
            }
        }

        public static int IntimacyOf(string breedKey)
        {
            EnsureLoaded();
            int value;
            return intimacyMap.TryGetValue(breedKey, out value) ? value : 0;
        }

        public static void AddIntimacy(string breedKey, int value)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(breedKey) || value <= 0 || !IsDiscovered(breedKey)) return;
            intimacyMap[breedKey] = IntimacyOf(breedKey) + value;
        }

        public static int IntimacyMilestone(string breedKey)
        {
            EnsureLoaded();
            int points = IntimacyOf(breedKey);
            int milestone = 1;
            CatCafeConfigDatabase.IntimacyRow[] rows = CatCafeConfigDatabase.Data.intimacy;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].enabled && points >= rows[i].required_points) milestone = Mathf.Max(milestone, rows[i].milestone);
            return milestone;
        }

        public static int NextIntimacyTarget(string breedKey)
        {
            int current = IntimacyMilestone(breedKey);
            CatCafeConfigDatabase.IntimacyRow[] rows = CatCafeConfigDatabase.Data.intimacy;
            for (int i = 0; i < rows.Length; i++) if (rows[i].enabled && rows[i].milestone == current + 1) return rows[i].required_points;
            return IntimacyOf(breedKey);
        }

        public static int DiscoveredCount()
        {
            EnsureLoaded();
            List<CatCatalog.CatRow> breeds = CatCatalog.DexBreeds();
            int count = 0;
            for (int i = 0; i < breeds.Count; i++) if (dexMap.ContainsKey(breeds[i].Key)) count++;
            return count;
        }

        /// <summary>回到大厅时消费一次“有新猫住下”提示，避免只用固定数量猜测。</summary>
        public static bool ConsumeNewCatHomeArrival()
        {
            EnsureLoaded();
            int discovered = DiscoveredCount();
            int baseCount = 0;
            List<CatCatalog.CatRow> breeds = CatCatalog.DexBreeds();
            for (int i = 0; i < breeds.Count; i++) if (breeds[i].Unlock == "base") baseCount++;
            if (data.homeSeenDexCount <= 0) data.homeSeenDexCount = baseCount;
            bool hasNew = discovered > data.homeSeenDexCount;
            data.homeSeenDexCount = Mathf.Max(data.homeSeenDexCount, discovered);
            if (hasNew) SaveNow();
            return hasNew;
        }

        /// <summary>
        /// 记录一次收集（传成年品种 key）。首次点亮图鉴，重复只累计遇见次数。
        /// 绒毛不在这里产出——它按波次从盘面上的猫身上掉（见 CatCafeGameController.SettleFurDrops），
        /// 与"发现"是两条独立的账。首次发现立即写盘。
        /// </summary>
        public static int Discover(string breedKey, string source, out bool first)
        {
            EnsureLoaded();
            DexDto entry;
            if (!dexMap.TryGetValue(breedKey, out entry))
            {
                // 先把老猫从上次时间点到现在的自然增长结清，再加入新猫。
                // 这样新认识的品种不会获得认识之前的离线绒毛。
                RefreshNaturalFur();
                entry = new DexDto { key = breedKey, n = 1, lv = 1, src = source };
                dexMap[breedKey] = entry;
                data.dex.Add(entry);
                first = true;
                SaveNow();
                return 0;
            }
            entry.n++;
            first = false;
            return 0;
        }

        /* ── 绒毛 ── */

        public static int FurOf(string breedKey)
        {
            EnsureLoaded();
            int value;
            return furMap.TryGetValue(breedKey, out value) ? value : 0;
        }

        public static void AddFur(string breedKey, int value)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(breedKey) || value <= 0) return;
            furMap[breedKey] = FurOf(breedKey) + value;
        }

        /// <summary>手里有没有任何绒毛。只用于"该讲绒毛这件事了吗"的判断。</summary>
        public static bool HasAnyFur()
        {
            EnsureLoaded();
            foreach (KeyValuePair<string, int> pair in furMap) if (pair.Value > 0) return true;
            return false;
        }

        public static bool TrySpendFur(string breedKey, int value)
        {
            EnsureLoaded();
            if (FurOf(breedKey) < value) return false;
            furMap[breedKey] = FurOf(breedKey) - value;
            return true;
        }

        /// <summary>
        /// 按现实时间为每个已认识品种结算自然绒毛。间隔、单次产量和自然储存上限均来自 Settings。
        /// 营业中掉落的绒毛不受这个上限截断；自然增长只负责把低于上限的库存补到上限。
        /// </summary>
        public static int RefreshNaturalFur()
        {
            EnsureLoaded();
            long now = NowMs();
            long intervalMs = Math.Max(1L, (long)NaturalFurIntervalMinutes * 60L * 1000L);
            if (now <= data.furLastMs || now - data.furLastMs < intervalMs) return 0;

            long intervals = (now - data.furLastMs) / intervalMs;
            long growthPerBreed = intervals * NaturalFurAmountPerInterval;
            int totalGain = 0;
            foreach (KeyValuePair<string, DexDto> pair in dexMap)
            {
                int current = FurOf(pair.Key);
                int room = Mathf.Max(0, NaturalFurCapPerBreed - current);
                int gain = (int)Math.Min(room, growthPerBreed);
                if (gain <= 0) continue;
                furMap[pair.Key] = current + gain;
                totalGain += gain;
            }

            // 保留未满一个间隔的余数，防止频繁进出大厅损失计时进度。
            data.furLastMs += intervals * intervalMs;
            SaveNow();
            return totalGain;
        }

        /* ── 亲密度里程碑与 perk ── */

        /// <summary>按亲密度里程碑解锁原 Levels 表中的横向 perk；购买式等级不再参与。</summary>
        public static float PerkValue(string breedKey, string perkId)
        {
            EnsureLoaded();
            int level = IntimacyMilestone(breedKey);
            float total = 0f;
            for (int l = 2; l <= level; l++)
            {
                CatCatalog.LevelRow row = CatCatalog.GetLevel(breedKey, l);
                if (row != null && row.PerkId == perkId) total += row.PerkValue;
            }
            return total;
        }

        public static bool TryLevelUp(string breedKey, out string error)
        {
            EnsureLoaded();
            int current = LevelOf(breedKey);
            if (current <= 0) { error = "尚未发现该品种"; return false; }
            CatCatalog.LevelRow next = CatCatalog.GetLevel(breedKey, current + 1);
            if (next == null) { error = "已满级"; return false; }
            if (Cans < next.CostCans) { error = "罐头不足"; return false; }
            if (FurOf(breedKey) < next.CostFur) { error = "绒毛不足"; return false; }
            TrySpendCans(next.CostCans);
            TrySpendFur(breedKey, next.CostFur);
            dexMap[breedKey].lv = current + 1;
            SaveNow();
            error = null;
            return true;
        }

        /* ── 奖励池门控 ── */

        /// <summary>已解锁、按 pool_rarity 应进入指定档位奖励池的非初始品种。</summary>
        public static List<string> UnlockedPoolBreeds(string poolRarity)
        {
            EnsureLoaded();
            List<string> result = new List<string>();
            List<CatCatalog.CatRow> breeds = CatCatalog.DexBreeds();
            for (int i = 0; i < breeds.Count; i++)
            {
                CatCatalog.CatRow row = breeds[i];
                if (row.Unlock == "base") continue;
                if (row.PoolRarity != poolRarity) continue;
                if (dexMap.ContainsKey(row.Key)) result.Add(row.Key);
            }
            return result;
        }

        /* ── 收钱罐 ── */

        public static int JarAccrued()
        {
            EnsureLoaded();
            float minutes = (NowMs() - data.jarLastMs) / 60000f;
            return Mathf.Min(JarCap, Mathf.FloorToInt(minutes * JarRatePerMinutePerCat * DiscoveredCount()));
        }

        public static float JarFillRatio()
        {
            return Mathf.Clamp01(JarAccrued() / (float)JarCap);
        }

        public static int CollectJar()
        {
            EnsureLoaded();
            int gain = JarAccrued();
            if (gain <= 0) return 0;
            data.cans += gain;
            data.jarLastMs = NowMs();
            SaveNow();
            return gain;
        }

        /* ── 统计 ── */

        public static int Runs { get { EnsureLoaded(); return data.runs; } }
        public static int Wins { get { EnsureLoaded(); return data.wins; } }

        public static void RecordRunEnd(bool win)
        {
            EnsureLoaded();
            data.runs++;
            if (win) data.wins++;
        }

        private static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
