using System.Collections.Generic;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 局外目录的兼容门面。
    /// 数据来自 CatCafeGameConfig.xlsx 导出的统一 JSON，不再单独解析 CSV。
    /// </summary>
    public static class CatCatalog
    {
        public sealed class CatRow
        {
            public string Key;
            public string Name;
            public string Kind;        // cat / kitten / guest / staff / item
            public string Rarity;      // common / uncommon / rare / special
            public string ColorGene;
            public string Asset;       // 空 = 无美术（文字卡）
            public string Unlock;      // base / breed / mutation / recipe
            public string PoolRarity;  // 解锁后进奖励池的档位；空 = 不进池
            public string GrownForm;   // 幼崽长大对应的成年品种
            public string RuleText;
            public string DexHint;
            public string DexFlavor;
        }

        /// <summary>
        /// 局内育儿窝的配方。只服务棋盘上的繁育，局外解锁不再走这张表——
        /// 那条线已改为「呼朋唤友」（<see cref="InviteRow"/>）。
        /// Breeding 表里的 craft_fur / craft_cans 两列因此作废，这里不再读取。
        /// </summary>
        public sealed class BreedRow
        {
            public string ParentA;     // 字典序
            public string ParentB;
            public string Child;       // 幼崽 key
            public string ResultMode;  // fixed / rarity_random
            public string RarityContext;
            public string Tier;        // 1 / 2 / special
            public string MutationChild;
            public float MutationRate;
        }

        /// <summary>
        /// 呼朋唤友的一条邀请：让 InviterA（可选再加 InviterB）带着绒毛和罐头出门，
        /// 把 Child 请进猫咖。和局内育儿窝的配方（<see cref="BreedRow"/>）互不相干。
        /// </summary>
        public sealed class InviteRow
        {
            public string Child;
            public string InviterA;
            public int FurA;
            public string InviterB;    // 空 = 一位邀请者就够
            public int FurB;
            public int Cans;

            public bool HasSecondInviter { get { return !string.IsNullOrEmpty(InviterB); } }
        }

        public sealed class LevelRow
        {
            public string CatKey;
            public int Level;
            public int CostCans;
            public int CostFur;
            public string PerkId;      // board_weight / mutation_up / material_bonus
            public float PerkValue;
            public string Desc;
        }

        private static bool loaded;
        private static readonly Dictionary<string, CatRow> cats = new Dictionary<string, CatRow>();
        private static readonly List<CatRow> catsOrdered = new List<CatRow>();
        private static readonly List<BreedRow> breeds = new List<BreedRow>();
        private static readonly List<InviteRow> invites = new List<InviteRow>();
        private static readonly List<LevelRow> levels = new List<LevelRow>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            loaded = false;
            cats.Clear();
            catsOrdered.Clear();
            breeds.Clear();
            invites.Clear();
            levels.Clear();
        }

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            CatCafeConfigDatabase.EnsureLoaded();
            CatCafeConfigDatabase.Root config = CatCafeConfigDatabase.Data;
            for (int i = 0; i < config.elements.Length; i++)
            {
                CatCafeConfigDatabase.ElementRow source = config.elements[i];
                if (!source.enabled || string.IsNullOrEmpty(source.key)) continue;
                CatRow row = new CatRow
                {
                    Key = source.key,
                    Name = source.name,
                    Kind = source.kind,
                    Rarity = source.rarity,
                    ColorGene = source.color_gene,
                    Asset = source.asset,
                    Unlock = source.unlock,
                    PoolRarity = source.pool_rarity,
                    GrownForm = source.grown_form,
                    RuleText = (source.rule_text ?? string.Empty).Replace("\\n", "\n"),
                    DexHint = source.dex_hint,
                    DexFlavor = source.dex_flavor
                };
                cats[row.Key] = row;
                catsOrdered.Add(row);
            }

            for (int i = 0; i < config.breeding.Length; i++)
            {
                CatCafeConfigDatabase.BreedRow source = config.breeding[i];
                if (!source.enabled || string.IsNullOrEmpty(source.parent_a)) continue;
                BreedRow row = new BreedRow
                {
                    ParentA = source.parent_a,
                    ParentB = source.parent_b,
                    Child = source.child,
                    ResultMode = source.result_mode,
                    RarityContext = source.rarity_context,
                    Tier = source.tier,
                    MutationChild = source.mutation_child,
                    MutationRate = source.mutation_rate
                };
                breeds.Add(row);
            }

            for (int i = 0; i < config.invites.Length; i++)
            {
                CatCafeConfigDatabase.InviteRow source = config.invites[i];
                if (!source.enabled || string.IsNullOrEmpty(source.child)) continue;
                InviteRow row = new InviteRow
                {
                    Child = source.child,
                    InviterA = source.inviter_a,
                    FurA = source.fur_a,
                    InviterB = source.inviter_b,
                    FurB = source.fur_b,
                    Cans = source.cans
                };
                invites.Add(row);
            }

            for (int i = 0; i < config.levels.Length; i++)
            {
                CatCafeConfigDatabase.LevelRow source = config.levels[i];
                if (!source.enabled || string.IsNullOrEmpty(source.cat_key)) continue;
                LevelRow row = new LevelRow
                {
                    CatKey = source.cat_key,
                    Level = source.level,
                    CostCans = source.cost_cans,
                    CostFur = source.cost_fur,
                    PerkId = source.perk_id,
                    PerkValue = source.perk_value,
                    Desc = source.desc
                };
                levels.Add(row);
            }

            // 跨表校验：合成表引用的 key 必须存在
            foreach (BreedRow b in breeds)
            {
                bool wildcard = b.ParentA == "*" && b.ParentB == "*";
                bool randomResult = b.ResultMode == "rarity_random";
                bool missingParents = !wildcard && (!cats.ContainsKey(b.ParentA) || !cats.ContainsKey(b.ParentB));
                bool missingChild = !randomResult && !cats.ContainsKey(b.Child);
                if (missingParents || missingChild)
                {
                    UnityEngine.Debug.LogError("[CatCatalog] Breeding 表引用了 Elements 表不存在的 key：" +
                        b.ParentA + " × " + b.ParentB + " → " + b.Child);
                }
                if (randomResult && CatCafeConfigDatabase.GetWeight(b.RarityContext) == null)
                {
                    UnityEngine.Debug.LogError("[CatCatalog] Breeding rarity_random 缺少 Weights 上下文：" +
                        b.RarityContext);
                }
            }

            // 呼朋唤友只解锁图鉴，不产幼崽，所以 child 必须是成年品种（kind=cat），
            // 写成幼崽 key 会点亮一个图鉴里根本不存在的条目。
            foreach (InviteRow invite in invites)
            {
                if (!cats.ContainsKey(invite.Child) || !cats.ContainsKey(invite.InviterA) ||
                    (invite.HasSecondInviter && !cats.ContainsKey(invite.InviterB)))
                {
                    UnityEngine.Debug.LogError("[CatCatalog] Invite 表引用了 Elements 表不存在的 key：" +
                        invite.InviterA + " / " + invite.InviterB + " → " + invite.Child);
                    continue;
                }
                if (cats[invite.Child].Kind != "cat")
                {
                    UnityEngine.Debug.LogError("[CatCatalog] Invite 的 child 必须是成年品种（kind=cat）：" + invite.Child);
                }
            }
        }

        public static CatRow Get(string key)
        {
            EnsureLoaded();
            CatRow row;
            return cats.TryGetValue(key, out row) ? row : null;
        }

        /// <summary>图鉴条目（kind=cat，按表内顺序）。</summary>
        public static List<CatRow> DexBreeds()
        {
            EnsureLoaded();
            List<CatRow> result = new List<CatRow>();
            for (int i = 0; i < catsOrdered.Count; i++)
            {
                if (catsOrdered[i].Kind == "cat") result.Add(catsOrdered[i]);
            }
            return result;
        }

        /// <summary>幼崽 → 成年品种；非幼崽返回自身。</summary>
        public static string GrownForm(string key)
        {
            CatRow row = Get(key);
            return row != null && !string.IsNullOrEmpty(row.GrownForm) ? row.GrownForm : key;
        }

        /// <summary>无序配对查配方；未配置返回 null（= 不产仔）。</summary>
        public static BreedRow LookupBreed(string a, string b)
        {
            EnsureLoaded();
            string x = string.CompareOrdinal(a, b) <= 0 ? a : b;
            string y = string.CompareOrdinal(a, b) <= 0 ? b : a;
            BreedRow fallback = null;
            for (int i = 0; i < breeds.Count; i++)
            {
                if (breeds[i].ParentA == x && breeds[i].ParentB == y) return breeds[i];
                if (breeds[i].ParentA == "*" && breeds[i].ParentB == "*") fallback = breeds[i];
            }
            return fallback;
        }

        /// <summary>呼朋唤友的全部邀请（按表内顺序）。</summary>
        public static List<InviteRow> AllInvites()
        {
            EnsureLoaded();
            return invites;
        }

        public static LevelRow GetLevel(string catKey, int level)
        {
            EnsureLoaded();
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].CatKey == catKey && levels[i].Level == level) return levels[i];
            }
            return null;
        }

    }
}
