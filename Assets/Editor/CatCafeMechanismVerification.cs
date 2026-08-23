#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// 可由 CI/本地批处理直接执行的机制回归。失败时抛异常并让 Unity 返回非零退出码。
    /// 这里只验证通用原子和配置契约，不复制任何业务数值。
    /// </summary>
    public static class CatCafeMechanismVerification
    {
        [MenuItem("Tools/Cat Cafe/验证/机制闭包回归")]
        public static void RunBatch()
        {
            VerifyCycleReduction();
            VerifyCardinalDirections();
            VerifyConfigContracts();
            VerifyControllerProtocols();
            Debug.Log("[CatCafeMechanismVerification] 机制闭包回归通过。");
        }

        private static void VerifyCycleReduction()
        {
            Equal(19, CatCafeMechanicMath.EffectiveCycleAge(18, 1),
                "缩短1轮必须只改变阈值一次，不能每轮叠加");
            Equal(19, CatCafeMechanicMath.EffectiveCycleAge(14, 5),
                "缩短5轮必须把20轮周期改为15轮");
            Equal(8, CatCafeMechanicMath.EffectiveCycleAge(6, 2),
                "储值卡缩短2轮");
            Equal(3, CatCafeMechanicMath.ExternalBonusTriggerCount(
                "adjacent_key", 3, "none", 0), "三个外部来源应触发三次");
            Equal(1, CatCafeMechanicMath.ExternalBonusTriggerCount(
                "none", 0, "none", 0), "无关系计数的外部修正按一个来源计算");
        }

        private static void VerifyCardinalDirections()
        {
            Equal(0, CatCafeMechanicMath.CardinalRay(0, 0, 4, 4).Count,
                "左上角向上没有有效方向");
            Equal(3, CatCafeMechanicMath.CardinalRay(0, 1, 4, 4).Count,
                "左上角向下应有三个格子");
            Equal(3, CatCafeMechanicMath.CardinalRay(0, 3, 4, 4).Count,
                "左上角向右应有三个格子");
            if (!CatCafeMechanicMath.IsNegativeRule(
                    "rarity_weights", "multiply", 0, 0, 0, 0, 0.75f))
                throw new InvalidOperationException("降低稀有权重必须识别为负面规则。");
            if (CatCafeMechanicMath.IsNegativeRule(
                    "round", "generate", 0, 0, 0, 0, 0f))
                throw new InvalidOperationException("正面生成规则不能被事故手册误伤。");
        }

        private static void VerifyConfigContracts()
        {
            CatCafeConfigDatabase.EnsureLoaded();
            CatCafeConfigDatabase.Root data = CatCafeConfigDatabase.Data;
            if (data == null || data.elements == null || data.items == null || data.rules == null)
                throw new InvalidOperationException("运行时配置未完整加载。");

            for (int i = 0; i < data.weights.Length; i++)
            {
                CatCafeConfigDatabase.WeightRow row = data.weights[i];
                if (!row.enabled) continue;
                Equal(100, row.common + row.uncommon + row.rare + row.special,
                    "Weights." + row.context + " 总和必须为100");
            }

            CatCafeConfigDatabase.RuleRow suppressor = null;
            for (int i = 0; i < data.rules.Length; i++)
            {
                if (data.rules[i].rule_id == "closure_item_082_suppress_accidents")
                    suppressor = data.rules[i];
                if (data.rules[i].operation == "cycle_reduce" &&
                    (data.rules[i].trigger != "on_any_dismiss" ||
                     data.rules[i].base_value <= 0 ||
                     string.IsNullOrEmpty(data.rules[i].source_keys)))
                    throw new InvalidOperationException(
                        "cycle_reduce 必须由 on_any_dismiss 触发，并配置正数缩短值与来源。");
            }
            if (suppressor == null || suppressor.target_value_mode != "negative_only")
                throw new InvalidOperationException(
                    "营业事故处理手册必须以 negative_only 模式抑制负面规则。");
        }

        private static void VerifyControllerProtocols()
        {
            GameObject root = new GameObject("CatCafe Mechanism Verification");
            try
            {
                CatCafeGameController controller = root.AddComponent<CatCafeGameController>();
                Type type = typeof(CatCafeGameController);
                List<string> owned = (List<string>)type.GetField(
                    "ownedItems", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);

                owned.Add("v3Item084");
                CatCafeConfigDatabase.RuleRow bagRule = FindRule("target_inspiration_bag_base");
                Equal(2, (int)Invoke(controller, "RuleRepeatCount", bagRule),
                    "礼袋复印机必须让礼袋规则执行两次");

                owned.Clear();
                owned.Add("v3Item094");
                CatCafeConfigDatabase.RuleRow rouletteRule = FindRule("closure_137_random_income");
                Equal(3, (int)Invoke(controller, "ApplyRandomIncomeModifiers", rouletteRule, 1, 1, 3),
                    "满点转盘芯片必须把三格转盘固定到3");

                owned.Clear();
                owned.Add("v3Item108");
                type.GetField("round", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, 1);
                Equal(1, (int)Invoke(controller, "ConfiguredExtraPieceChoices"),
                    "双日补货冰柜必须在偶数次营业增加一次选择");

                CatCafeConfigDatabase.RuleRow chooseRule = FindRule("target_item_002_choose");
                IList candidates = (IList)Invoke(controller, "ConfiguredChoiceCandidates", chooseRule);
                if (candidates.Count < 3)
                    throw new InvalidOperationException("猫咪领养名册至少需要3个可选小猫。");

                owned.Clear();
                owned.Add("v3Item113");
                type.GetField("round", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, 2);
                bool global = (bool)Invoke(controller, "UsesGlobalAdjacency", null, 0, string.Empty);
                if (!global)
                    throw new InvalidOperationException("全景店内监控第三次营业必须启用全局相邻。");

                owned.Clear();
                owned.Add("v3Item079");
                CatCafeConfigDatabase.RuleRow blindBox = FindRule("closure_105_blind_box_reward");
                for (int i = 0; i < 20; i++)
                {
                    string key = (string)Invoke(controller, "ChooseRuleResultKey", blindBox);
                    string rarity = CatCafeConfigDatabase.GetElement(key).rarity;
                    if (rarity != "rare" && rarity != "special")
                        throw new InvalidOperationException("盲盒透视机生成了非史诗/传奇对象：" + key);
                }

                owned.Clear();
                owned.Add("v3Item062");
                CatCafeConfigDatabase.RuleRow seed = FindRule("closure_125_grow");
                for (int i = 0; i < 20; i++)
                {
                    string key = (string)Invoke(controller, "ModifiedTransformResult", seed);
                    string rarity = CatCafeConfigDatabase.GetElement(key).rarity;
                    if (rarity != "rare" && rarity != "special")
                        throw new InvalidOperationException("猫草营养液变形到非史诗/传奇对象：" + key);
                }

                IList allChoices = (IList)Invoke(controller, "AllConfiguredPoolKeys", null);
                if (allChoices.Count < 100)
                    throw new InvalidOperationException("全对象采购选择池异常偏小：" + allChoices.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static object Invoke(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) throw new MissingMethodException(target.GetType().Name, method);
            return info.Invoke(target, args);
        }

        private static CatCafeConfigDatabase.RuleRow FindRule(string ruleId)
        {
            CatCafeConfigDatabase.RuleRow[] rules = CatCafeConfigDatabase.Data.rules;
            for (int i = 0; i < rules.Length; i++) if (rules[i].rule_id == ruleId) return rules[i];
            throw new InvalidOperationException("缺少回归规则：" + ruleId);
        }

        private static void Equal(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new InvalidOperationException(
                    message + "；expected=" + expected + " actual=" + actual);
        }
    }
}
#endif
