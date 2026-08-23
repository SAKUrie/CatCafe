using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 猫咖唯一运行时配置入口。
    ///
    /// 策划只编辑 GameDesign/CatCafeGameConfig.xlsx，导出工具会生成
    /// Resources/GameData/cat_cafe_config.json。游戏代码只解释配置，不再保存玩法数值。
    /// </summary>
    public static class CatCafeConfigDatabase
    {
        [Serializable]
        public sealed class Root
        {
            public SettingRow[] settings = new SettingRow[0];
            public RarityRow[] rarities = new RarityRow[0];
            public ElementRow[] elements = new ElementRow[0];
            public ItemRow[] items = new ItemRow[0];
            public StageRow[] stages = new StageRow[0];
            public WeightRow[] weights = new WeightRow[0];
            public InitialDeckRow[] initialDeck = new InitialDeckRow[0];
            public RuleRow[] rules = new RuleRow[0];
            public BreedRow[] breeding = new BreedRow[0];
            public LevelRow[] levels = new LevelRow[0];
            public TutorialRow[] tutorials = new TutorialRow[0];
            public IntimacyRow[] intimacy = new IntimacyRow[0];
            public InviteRow[] invites = new InviteRow[0];
            public ArchetypeRow[] archetypes = new ArchetypeRow[0];
        }

        [Serializable]
        public sealed class SettingRow
        {
            public string key;
            public string value;
            public string value_type;
            public string design_note;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class RarityRow
        {
            public string key;
            public int index;
            public string label;
            public string color;
            /// <summary>纸艺稀有度徽章素材名，位于 Resources/CatCafe/PaperSkin/ 下。</summary>
            public string badge;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class ElementRow
        {
            public string key;
            public string name;
            public string kind;
            public string type_label;
            public string rarity;
            public string color_gene;
            public string asset;
            public string short_icon;
            public string unlock;
            public string pool_rarity;
            public string grown_form;
            public bool special_presentation;
            public string rule_text;
            public string dex_hint;
            public string dex_flavor;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class ItemRow
        {
            public string key;
            public string name;
            public string rarity;
            public string asset;
            public string short_icon;
            public string rule_text;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class StageRow
        {
            public int id;
            public string name;
            public int rounds;
            public int target;
            public string rarity_context;
            public int clear_item_tier;
            public string clear_reward_min_rarity;
            public bool is_final;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class WeightRow
        {
            public string context;
            public int common;
            public int uncommon;
            public int rare;
            public int special;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class InitialDeckRow
        {
            public string element_key;
            public int count;
            public bool enabled = true;
        }

        /// <summary>
        /// 通用规则行。scope/comparator/operation 使用稳定字符串协议，详见 Excel 的“说明”页。
        /// 新数值、新组合只加行；只有增加全新的原子操作时才需要扩展执行器。
        /// </summary>
        [Serializable]
        public sealed class RuleRow
        {
            public string rule_id;
            public string owner_type;
            public string owner_key;
            public string trigger;
            public int priority;
            public string source_kinds;
            public string source_keys;
            public string operation;
            public string primary_scope;
            public string primary_filter;
            public string primary_comparator;
            public int primary_threshold;
            public string secondary_scope;
            public string secondary_filter;
            public string secondary_comparator;
            public int secondary_threshold;
            public int base_value;
            public int primary_factor;
            public int secondary_factor;
            public int cross_factor;
            public int divisor = 1;
            public float multiplier;
            public bool consume_self;
            public bool once_per_round;
            public string reason;
            public bool enabled = true;

            // ── 移除类原子（2026-08-16 新增）──
            // 规则第一次获得「写权限」：以前只能读棋盘算钱，现在能把对象从名册里拿掉。
            // remove_scope 决定拿谁：pool_key / pool_kind 从名册拿棋子，owned_item 拿别的道具。
            // 实际移除数量会当作 primary 代入 base_value/primary_factor 那套公式结算收益。
            public string remove_scope;
            public string remove_filter;
            public int remove_limit;

            // 通用动作目标与概率协议。代码只解释这些原子字段；具体卡牌数据来自 Rules 表。
            public string target_scope;
            public string target_filter;
            public int target_limit;
            public float chance = 1f;
            public bool repeat_on_success;
            public int max_triggers = 1;
            public string result_key;
            public int result_count = 1;
            public string target_value_mode;
        }

        /// <summary>
        /// 局内育儿窝配方。craft_fur / craft_cans 是旧「局外两只猫合成一只猫」的遗留列，
        /// 已随呼朋唤友改造作废；保留字段只为和表结构对齐，运行时不再有人读。
        /// </summary>
        [Serializable]
        public sealed class BreedRow
        {
            public string parent_a;
            public string parent_b;
            public string child;
            public string result_mode;
            public string rarity_context;
            public string source_url;
            public string tier;
            public string requires;
            public string mutation_child;
            public float mutation_rate;
            public int craft_fur;
            public int craft_cans;
            public string notes;
            public bool enabled = true;
        }

        /// <summary>
        /// 呼朋唤友（局外解锁）。和 Breeding 是两套东西：Breeding 只管局内育儿窝生幼崽，
        /// 这张表管的是"让已经住下的猫出门，把新朋友请回来"——邀请者不是父母，
        /// 产物也不落到棋盘上，只点亮图鉴。inviter_b 留空表示一位邀请者就够。
        /// </summary>
        [Serializable]
        public sealed class InviteRow
        {
            public string child;
            public string inviter_a;
            public int fur_a;
            public string inviter_b;
            public int fur_b;
            public int cans;
            public string notes;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class LevelRow
        {
            public string cat_key;
            public int level;
            public int cost_cans;
            public int cost_fur;
            public string perk_id;
            public float perk_value;
            public string desc;
            public bool enabled = true;
        }

        /// <summary>构筑倾向与局末账本的流派映射。成员棋子键完全由配置表定义。</summary>
        [Serializable]
        public sealed class ArchetypeRow
        {
            public string key;
            public string label;
            public string color;
            public string element_keys;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class TutorialRow
        {
            public string id;
            public string trigger_key;
            public string copy;
            /// <summary>这条字条会在什么时候出现（玩家可读文案，局外字条回看列表直接展示）。</summary>
            public string appear_note = "";
            public string spotlight_target;
            public bool once = true;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class IntimacyRow
        {
            public int milestone;
            public int required_points;
            public string unlock_id;
            public string label;
            public bool enabled = true;
        }

        private const string ResourcePath = "GameData/cat_cafe_config";
        private static bool loaded;
        private static Root data;
        private static Dictionary<string, SettingRow> settingMap;
        private static Dictionary<string, RarityRow> rarityMap;
        private static Dictionary<string, ElementRow> elementMap;
        private static Dictionary<string, ItemRow> itemMap;
        private static Dictionary<string, WeightRow> weightMap;
        private static Dictionary<string, TutorialRow> tutorialById;
        private static Dictionary<string, TutorialRow> tutorialByTrigger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            // SubsystemRegistration also runs when Enter Play Mode has domain reload disabled.
            // Always read the latest exported table instead of retaining paths from a prior run.
            loaded = false;
            data = null;
            settingMap = null;
            rarityMap = null;
            elementMap = null;
            itemMap = null;
            weightMap = null;
            tutorialById = null;
            tutorialByTrigger = null;
        }

        public static Root Data
        {
            get { EnsureLoaded(); return data; }
        }

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                data = new Root();
                BuildIndexes();
                Debug.LogError("[CatCafeConfig] 缺少 Resources/" + ResourcePath + ".json。请运行 Tools/CatCafeConfig/export_config.py。");
                return;
            }

            try
            {
                data = JsonUtility.FromJson<Root>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError("[CatCafeConfig] JSON 解析失败：" + exception.Message);
            }

            if (data == null) data = new Root();
            NormalizeArrays();
            BuildIndexes();
            Validate();
        }

        public static string GetString(string key, string fallback = "")
        {
            EnsureLoaded();
            SettingRow row;
            return settingMap.TryGetValue(key, out row) && !string.IsNullOrEmpty(row.value) ? row.value : fallback;
        }

        public static string GetRequiredString(string key)
        {
            EnsureLoaded();
            SettingRow row;
            if (settingMap.TryGetValue(key, out row) && !string.IsNullOrEmpty(row.value)) return row.value;
            throw new InvalidOperationException("[CatCafeConfig] 缺少必填 string 设置：" + key);
        }

        public static int GetInt(string key, int fallback = 0)
        {
            int result;
            return int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result : fallback;
        }

        public static int GetRequiredInt(string key)
        {
            EnsureLoaded();
            SettingRow row;
            int result;
            if (settingMap.TryGetValue(key, out row) &&
                int.TryParse(row.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            throw new InvalidOperationException("[CatCafeConfig] 缺少或无法解析必填 int 设置：" + key);
        }

        public static float GetFloat(string key, float fallback = 0f)
        {
            float result;
            return float.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result : fallback;
        }

        public static float GetRequiredFloat(string key)
        {
            EnsureLoaded();
            SettingRow row;
            float result;
            if (settingMap.TryGetValue(key, out row) &&
                float.TryParse(row.value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            throw new InvalidOperationException("[CatCafeConfig] 缺少或无法解析必填 float 设置：" + key);
        }

        public static bool GetBool(string key, bool fallback = false)
        {
            string value = GetString(key);
            bool parsed;
            if (bool.TryParse(value, out parsed)) return parsed;
            if (value == "1") return true;
            if (value == "0") return false;
            return fallback;
        }

        public static bool GetRequiredBool(string key)
        {
            string value = GetRequiredString(key);
            bool parsed;
            if (bool.TryParse(value, out parsed)) return parsed;
            if (value == "1") return true;
            if (value == "0") return false;
            throw new InvalidOperationException("[CatCafeConfig] 无法解析必填 bool 设置：" + key);
        }

        public static ElementRow GetElement(string key)
        {
            EnsureLoaded();
            ElementRow row;
            return elementMap.TryGetValue(key, out row) ? row : null;
        }

        public static ItemRow GetItem(string key)
        {
            EnsureLoaded();
            ItemRow row;
            return itemMap.TryGetValue(key, out row) ? row : null;
        }

        public static WeightRow GetWeight(string context)
        {
            EnsureLoaded();
            WeightRow row;
            return weightMap.TryGetValue(context, out row) ? row : null;
        }

        public static TutorialRow GetTutorialById(string id)
        {
            EnsureLoaded();
            TutorialRow row;
            return tutorialById.TryGetValue(id, out row) ? row : null;
        }

        public static TutorialRow GetTutorialByTrigger(string triggerKey)
        {
            EnsureLoaded();
            TutorialRow row;
            return tutorialByTrigger.TryGetValue(triggerKey, out row) ? row : null;
        }

        public static string RarityLabel(string key)
        {
            EnsureLoaded();
            RarityRow row;
            return rarityMap.TryGetValue(key, out row) ? row.label : key;
        }

        public static string RarityColor(string key, string fallback)
        {
            EnsureLoaded();
            RarityRow row;
            return rarityMap.TryGetValue(key, out row) && !string.IsNullOrEmpty(row.color) ? row.color : fallback;
        }

        /// <summary>稀有度对应的纸艺徽章素材名；没配就返回空串，由表现层回退到程序化色带。</summary>
        public static string RarityBadge(string key)
        {
            EnsureLoaded();
            RarityRow row;
            return rarityMap.TryGetValue(key, out row) && !string.IsNullOrEmpty(row.badge) ? row.badge : string.Empty;
        }

        private static void NormalizeArrays()
        {
            if (data.settings == null) data.settings = new SettingRow[0];
            if (data.rarities == null) data.rarities = new RarityRow[0];
            if (data.elements == null) data.elements = new ElementRow[0];
            if (data.items == null) data.items = new ItemRow[0];
            if (data.stages == null) data.stages = new StageRow[0];
            if (data.weights == null) data.weights = new WeightRow[0];
            if (data.initialDeck == null) data.initialDeck = new InitialDeckRow[0];
            if (data.rules == null) data.rules = new RuleRow[0];
            if (data.breeding == null) data.breeding = new BreedRow[0];
            if (data.levels == null) data.levels = new LevelRow[0];
            if (data.tutorials == null) data.tutorials = new TutorialRow[0];
            if (data.intimacy == null) data.intimacy = new IntimacyRow[0];
            if (data.invites == null) data.invites = new InviteRow[0];
            if (data.archetypes == null) data.archetypes = new ArchetypeRow[0];
        }

        private static void BuildIndexes()
        {
            settingMap = new Dictionary<string, SettingRow>(StringComparer.Ordinal);
            rarityMap = new Dictionary<string, RarityRow>(StringComparer.OrdinalIgnoreCase);
            elementMap = new Dictionary<string, ElementRow>(StringComparer.Ordinal);
            itemMap = new Dictionary<string, ItemRow>(StringComparer.Ordinal);
            weightMap = new Dictionary<string, WeightRow>(StringComparer.Ordinal);
            tutorialById = new Dictionary<string, TutorialRow>(StringComparer.Ordinal);
            tutorialByTrigger = new Dictionary<string, TutorialRow>(StringComparer.Ordinal);

            for (int i = 0; i < data.settings.Length; i++)
                if (data.settings[i].enabled && !string.IsNullOrEmpty(data.settings[i].key)) settingMap[data.settings[i].key] = data.settings[i];
            for (int i = 0; i < data.rarities.Length; i++)
                if (data.rarities[i].enabled && !string.IsNullOrEmpty(data.rarities[i].key)) rarityMap[data.rarities[i].key] = data.rarities[i];
            for (int i = 0; i < data.elements.Length; i++)
                if (data.elements[i].enabled && !string.IsNullOrEmpty(data.elements[i].key)) elementMap[data.elements[i].key] = data.elements[i];
            for (int i = 0; i < data.items.Length; i++)
                if (data.items[i].enabled && !string.IsNullOrEmpty(data.items[i].key)) itemMap[data.items[i].key] = data.items[i];
            for (int i = 0; i < data.weights.Length; i++)
                if (data.weights[i].enabled && !string.IsNullOrEmpty(data.weights[i].context)) weightMap[data.weights[i].context] = data.weights[i];
            for (int i = 0; i < data.tutorials.Length; i++)
            {
                TutorialRow row = data.tutorials[i];
                if (!row.enabled || string.IsNullOrEmpty(row.id)) continue;
                tutorialById[row.id] = row;
                if (!string.IsNullOrEmpty(row.trigger_key)) tutorialByTrigger[row.trigger_key] = row;
            }
        }

        private static void Validate()
        {
            if (data.stages.Length == 0) Debug.LogError("[CatCafeConfig] Stages 表没有启用数据。");
            if (data.initialDeck.Length == 0) Debug.LogError("[CatCafeConfig] InitialDeck 表没有启用数据。");

            for (int i = 0; i < data.initialDeck.Length; i++)
            {
                InitialDeckRow row = data.initialDeck[i];
                if (row.enabled && !elementMap.ContainsKey(row.element_key))
                    Debug.LogError("[CatCafeConfig] InitialDeck 引用了不存在的棋子：" + row.element_key);
            }

            for (int i = 0; i < data.rules.Length; i++)
            {
                RuleRow row = data.rules[i];
                if (!row.enabled) continue;
                if (row.owner_type == "element" && row.owner_key != "*" && !elementMap.ContainsKey(row.owner_key))
                    Debug.LogError("[CatCafeConfig] Rules " + row.rule_id + " 引用了不存在的棋子：" + row.owner_key);
                if (row.owner_type == "item" && !itemMap.ContainsKey(row.owner_key))
                    Debug.LogError("[CatCafeConfig] Rules " + row.rule_id + " 引用了不存在的物品：" + row.owner_key);
            }

            for (int i = 0; i < data.breeding.Length; i++)
            {
                BreedRow row = data.breeding[i];
                if (!row.enabled) continue;

                bool parentAWildcard = row.parent_a == "*";
                bool parentBWildcard = row.parent_b == "*";
                bool wildcard = parentAWildcard && parentBWildcard;
                bool randomResult = row.result_mode == "rarity_random";

                if (parentAWildcard != parentBWildcard)
                {
                    Debug.LogError("[CatCafeConfig] Breeding 通配配方必须同时使用 parent_a=*、parent_b=*：" +
                        row.parent_a + " × " + row.parent_b);
                }
                else if (wildcard)
                {
                    if (!randomResult)
                        Debug.LogError("[CatCafeConfig] Breeding 通配配方必须配置 result_mode=rarity_random");
                    if (!string.IsNullOrEmpty(row.child))
                        Debug.LogError("[CatCafeConfig] Breeding rarity_random 通配配方的 child 必须留空：" + row.child);
                    if (string.IsNullOrEmpty(row.rarity_context) || !weightMap.ContainsKey(row.rarity_context))
                        Debug.LogError("[CatCafeConfig] Breeding rarity_random 缺少 Weights 上下文：" + row.rarity_context);
                }
                else
                {
                    if (!elementMap.ContainsKey(row.parent_a) || !elementMap.ContainsKey(row.parent_b) ||
                        !elementMap.ContainsKey(row.child))
                    {
                        Debug.LogError("[CatCafeConfig] Breeding 引用了不存在的棋子：" +
                            row.parent_a + " × " + row.parent_b + " → " + row.child);
                    }
                    if (randomResult)
                        Debug.LogError("[CatCafeConfig] Breeding 精确配方不能使用 result_mode=rarity_random：" +
                            row.parent_a + " × " + row.parent_b);
                }

                if (!string.IsNullOrEmpty(row.mutation_child) && !elementMap.ContainsKey(row.mutation_child))
                    Debug.LogError("[CatCafeConfig] Breeding 引用了不存在的突变幼崽：" + row.mutation_child);
            }

            for (int i = 0; i < data.invites.Length; i++)
            {
                InviteRow row = data.invites[i];
                if (!row.enabled) continue;
                if (!elementMap.ContainsKey(row.child) || !elementMap.ContainsKey(row.inviter_a))
                    Debug.LogError("[CatCafeConfig] Invite 引用了不存在的棋子：" + row.inviter_a + " → " + row.child);
                if (!string.IsNullOrEmpty(row.inviter_b) && !elementMap.ContainsKey(row.inviter_b))
                    Debug.LogError("[CatCafeConfig] Invite 引用了不存在的第二位邀请者：" + row.inviter_b);
                if (row.fur_a <= 0)
                    Debug.LogError("[CatCafeConfig] Invite 的 fur_a 必须大于 0：" + row.child);
                if (!string.IsNullOrEmpty(row.inviter_b) && row.fur_b <= 0)
                    Debug.LogError("[CatCafeConfig] Invite 配了第二位邀请者却没配 fur_b：" + row.child);
            }
        }
    }
}
