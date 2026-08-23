using System;
using System.Collections.Generic;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 棋子规则执行器使用的纯计算原子。这里只处理通用数学与网格关系，
    /// 不保存任何棋子、道具、数值或文案；业务数据仍全部来自配置表。
    /// </summary>
    public static class CatCafeMechanicMath
    {
        public static int EffectiveCycleAge(int lifetimeRounds, int configuredReduction)
        {
            return Math.Max(0, lifetimeRounds) + Math.Max(0, configuredReduction);
        }

        public static int ExternalBonusTriggerCount(
            string primaryScope, int primary, string secondaryScope, int secondary)
        {
            if (IsRelationshipScope(primaryScope) && primary > 0) return primary;
            if (IsRelationshipScope(secondaryScope) && secondary > 0) return secondary;
            return 1;
        }

        public static List<int> CardinalRay(
            int sourceIndex, int direction, int rows, int columns)
        {
            List<int> result = new List<int>();
            if (rows <= 0 || columns <= 0 || sourceIndex < 0 || sourceIndex >= rows * columns)
                return result;

            int rowStep = direction == 0 ? -1 : direction == 1 ? 1 : 0;
            int columnStep = direction == 2 ? -1 : direction == 3 ? 1 : 0;
            if (rowStep == 0 && columnStep == 0) return result;

            int row = sourceIndex / columns + rowStep;
            int column = sourceIndex % columns + columnStep;
            while (row >= 0 && row < rows && column >= 0 && column < columns)
            {
                result.Add(row * columns + column);
                row += rowStep;
                column += columnStep;
            }
            return result;
        }

        public static bool IsNegativeRule(
            string trigger, string operation, int baseValue, int primaryFactor,
            int secondaryFactor, int crossFactor, float multiplier)
        {
            if (operation == "remove_targets" || operation == "set_targets_zero" ||
                operation == "force_skip" || operation == "force_choose") return true;
            if (operation == "chance_income" || operation == "income")
                return baseValue < 0 || primaryFactor < 0 ||
                    secondaryFactor < 0 || crossFactor < 0;
            if (operation == "multiply" || operation == "multiply_income" ||
                operation == "multiply_targets")
                return multiplier > 0f && multiplier < 1f;
            if (trigger == "rarity_weights")
                return operation == "multiply" && multiplier > 0f && multiplier < 1f;
            return false;
        }

        private static bool IsRelationshipScope(string scope)
        {
            return !string.IsNullOrEmpty(scope) &&
                (scope.StartsWith("adjacent", StringComparison.Ordinal) ||
                 scope == "same_row_key");
        }
    }
}
