using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story.Nodes;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 条件求值器。对一组「变量 op 值」子句按组合方式（All=全部满足 / Any=任一满足）求值，
    /// 供条件节点与「带条件的选项」复用。所有变量读写经 <see cref="IStoryVariableProvider"/>。
    /// </summary>
    internal static class ConditionEvaluator
    {
        /// <summary>
        /// 求值条件组。空条件组视为「恒满足」（无约束即放行），与编辑器侧语义一致。
        /// </summary>
        /// <param name="clauses">条件子句列表（可为 null / 空）。</param>
        /// <param name="combine">组合方式。</param>
        /// <param name="variables">变量提供者。</param>
        /// <param name="portValue">可选的子句端口取值回调（比较值来自「获取变量」节点连线）；返回 null 视为未连线 → 用子句常量。</param>
        public static bool Evaluate(IReadOnlyList<ConditionClause> clauses, ConditionCombine combine, IStoryVariableProvider variables,
            Func<ConditionClause, object> portValue = null)
        {
            if (clauses == null || clauses.Count == 0) return true;

            if (combine == ConditionCombine.Any)
            {
                foreach (var c in clauses)
                    if (EvalClause(c, variables, portValue)) return true;
                return false;
            }
            // All
            foreach (var c in clauses)
                if (!EvalClause(c, variables, portValue)) return false;
            return true;
        }

        private static bool EvalClause(ConditionClause c, IStoryVariableProvider variables, Func<ConditionClause, object> portValue)
        {
            if (c == null || string.IsNullOrEmpty(c.variableId)) return false;
            if (!variables.HasVariable(c.variableId)) return false;

            var type = variables.GetVariableType(c.variableId);
            variables.TryGetValue(c.variableId, out var current);
            // 比较值：端口（获取变量连线）优先，未连线回落子句常量
            object target;
            if (portValue != null && portValue(c) is object pv && pv != null)
                target = pv;
            else
                target = ValueParser.Parse(c.value, type);
            return Compare(current, target, c.op, type);
        }

        private static bool Compare(object a, object b, CompareOp op, VariableType type)
        {
            switch (type)
            {
                case VariableType.Int:
                case VariableType.Float:
                    double da = ToDouble(a), db = ToDouble(b);
                    switch (op)
                    {
                        case CompareOp.Equal: return da == db;
                        case CompareOp.NotEqual: return da != db;
                        case CompareOp.Greater: return da > db;
                        case CompareOp.GreaterEqual: return da >= db;
                        case CompareOp.Less: return da < db;
                        case CompareOp.LessEqual: return da <= db;
                        default: return false;
                    }
                case VariableType.Bool:
                    bool ba = ToBool(a), bb = ToBool(b);
                    if (op == CompareOp.Equal) return ba == bb;
                    if (op == CompareOp.NotEqual) return ba != bb;
                    return false; // 布尔仅支持相等比较
                case VariableType.String:
                default:
                    string sa = a == null ? string.Empty : a.ToString();
                    string sb = b == null ? string.Empty : b.ToString();
                    if (op == CompareOp.Equal) return sa == sb;
                    if (op == CompareOp.NotEqual) return sa != sb;
                    return false; // 字符串仅支持相等比较
            }
        }

        private static double ToDouble(object v)
            => v is double d ? d
             : v is float f ? f
             : v is int i ? i
             : (double.TryParse(v?.ToString(), out var r) ? r : 0d);

        private static bool ToBool(object v)
            => v is bool b ? b : (bool.TryParse(v?.ToString(), out var r) && r);
    }
}
