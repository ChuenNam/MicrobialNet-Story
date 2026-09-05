using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools.Inspector
{
    /// <summary>
    /// 属性面板的纯逻辑（无 UIToolkit / Undo / AssetDatabase 依赖），从 FieldDrawerRegistry 抽出（P4/L1）：
    /// ① 多选混合态判定；② 表绑定值路由与行写回（表是唯一内容真相源）；
    /// ③ 变量 op/value 的类型归一化决策（切换变量后校正残留）；④ 外观字段显隐谓词与条件子句文本。
    /// 全部为静态纯函数、可独立单测（见 FieldPanelLogicTests），为后续 L2 结构拆分先建回归网。
    /// </summary>
    internal static class FieldPanelLogic
    {
        // ── ① 多选混合态判定 ───────────────────────────────

        /// <summary>判断一组（已装箱）值是否全部相等，兼容 null 与 UnityEngine.Object 引用比较。</summary>
        public static bool AllEqual(IList<object> vals)
        {
            if (vals.Count == 0) return true;
            var first = vals[0];
            for (int i = 1; i < vals.Count; i++)
            {
                var v = vals[i];
                if (first == null && v == null) continue;
                if (first == null || v == null) return false;
                if (!first.Equals(v)) return false;
            }
            return true;
        }

        /// <summary>多选字段的混合态：值部分不同 → isMixed=true（高亮提示「修改将应用到全部」），显示值取首个。
        /// 空列表返回 (false, null)（调用方先按节点字段取过显示值，仅多选时才走此处）。</summary>
        public static (bool isMixed, object displayValue) EvaluateMixedState(IList<object> values)
        {
            if (values == null || values.Count == 0) return (false, null);
            return (!AllEqual(values), values[0]);
        }

        // ── ② 表绑定值路由（内容真相源在 StoryTableAsset 的行）────

        /// <summary>表驱动虚拟节点上「编辑会真正生效」的字段白名单：内容字段（speakerId/text/showText → 写回表格行）
        /// + 选项列表（文本与行 choices 对应，元素级仅允许改文本）。语速/打字机/外观/生成策略等仅节点字段在表驱动下
        /// 随每次行重建被丢弃、不生效——面板应隐藏，避免误导用户以为可配。</summary>
        public static bool IsTableDrivenEffectiveField(Type nodeType, string fieldName)
            => fieldName == "speakerId" || fieldName == "text" || fieldName == "showText" || fieldName == "options";

        /// <summary>判断字段路径是否为「剧情表内容字段」（编辑应回写 SO 行）：对白文本 / 讲述者 / 选项文本 / 显示文字。</summary>
        public static bool IsTableContentField(string path)
            => path == "text" || path == "speakerId" || path == "showText"
               || (path.StartsWith("options[") && path.EndsWith("].text"));

        /// <summary>表驱动节点内容字段的显示值路由：text/speakerId/showText 取自行（唯一真相源），其余用节点值。行空 = 不路由。</summary>
        public static object RouteTableBoundDisplay(string fieldName, StoryTableRow row, object nodeValue)
        {
            if (row == null) return nodeValue;
            switch (fieldName)
            {
                case "text": return row.text;
                case "speakerId": return row.speaker;
                case "showText": return row.showText;
                default: return nodeValue;
            }
        }

        /// <summary>表驱动选项文本的显示值路由：按行内原始下标取行内选项文本；行空或下标越界用节点文本。</summary>
        public static object RouteTableBoundOptionText(StoryTableRow row, StoryTableAsset table, int index, object nodeText)
        {
            if (row == null) return nodeText;
            var ch = StoryTableBaker.GetChoiceForOption(row, table, index);
            return ch != null ? (object)ch.text : nodeText;
        }

        /// <summary>
        /// 把内容字段值写入绑定行（唯一真相源；调用方负责 Undo.RecordObject / SetDirty / 徽标等编辑器副作用）。
        /// 选项文本路径形如 "options[3].text"，下标映射复用 StoryTableBaker.GetChoiceForOption。
        /// 返回是否命中内容字段（false = 非内容路径，调用方应走节点自身）。
        /// </summary>
        public static bool ApplyTableRowEdit(StoryTableRow row, StoryTableAsset table, string path, object val)
        {
            if (row == null || !IsTableContentField(path)) return false;
            if (path == "text")
                row.text = val as string ?? "";
            else if (path == "speakerId")
                row.speaker = val as string ?? "";
            else if (path == "showText")
                row.showText = val is bool bv && bv;
            else // options[i].text（IsTableContentField 已保证形态）
            {
                int ob = "options[".Length;
                int cb = path.IndexOf(']');
                if (cb > ob && int.TryParse(path.Substring(ob, cb - ob), out var oi))
                {
                    var choice = StoryTableBaker.GetChoiceForOption(row, table, oi);
                    if (choice != null) choice.text = val as string ?? "";
                }
            }
            return true;
        }

        // ── ③ 变量 op/value 的类型归一化 ────────────────────

        /// <summary>从变量定义列表解析变量类型；未定义 / 空表 / 空 id 返回 null。</summary>
        public static VariableType? ResolveVarType(IReadOnlyList<StoryVariableDef> variables, string varId)
        {
            if (string.IsNullOrEmpty(varId) || variables == null) return null;
            foreach (var v in variables)
                if (v != null && v.id == varId) return v.type;
            return null;
        }

        /// <summary>赋值节点当前变量类型下合法的运算符集合：布尔 / 字符串 / 未定义仅 Set，数值含加减乘除。</summary>
        public static IReadOnlyList<(Enum op, string label)> ValidAssignOps(VariableType? type)
        {
            if (type == VariableType.Bool || type == VariableType.String || type == null)
                return new List<(Enum, string)> { ((Enum)AssignOp.Set, "Set") };
            return new List<(Enum, string)>
            {
                ((Enum)AssignOp.Set, "Set"),
                ((Enum)AssignOp.Add, "Add"),
                ((Enum)AssignOp.Sub, "Subtract"),
                ((Enum)AssignOp.Mul, "Multiply"),
                ((Enum)AssignOp.Div, "Divide"),
            };
        }

        /// <summary>条件子句当前变量类型下合法的比较运算符集合：布尔 / 字符串 / 未定义仅 ==/!=，数值含大小比较。</summary>
        public static IReadOnlyList<(Enum op, string label)> ValidCompareOps(VariableType? type)
        {
            if (type == VariableType.Bool || type == VariableType.String || type == null)
                return new List<(Enum, string)> { ((Enum)CompareOp.Equal, "Equal"), ((Enum)CompareOp.NotEqual, "Not Equal") };
            return new List<(Enum, string)>
            {
                ((Enum)CompareOp.Equal, "Equal"),
                ((Enum)CompareOp.NotEqual, "Not Equal"),
                ((Enum)CompareOp.Greater, "Greater"),
                ((Enum)CompareOp.GreaterEqual, "Greater or Equal"),
                ((Enum)CompareOp.Less, "Less"),
                ((Enum)CompareOp.LessEqual, "Less or Equal"),
            };
        }

        /// <summary>
        /// 切换变量后 op/value 的校正决策（纯计算，不改对象）：运算符对新类型非法 → 复位默认（条件 Equal / 赋值 Set）；
        /// 值残留布尔文本 → 按新类型归零（Int/Float→"0"、String→""）；布尔变量值非布尔字面 → "false"。
        /// 返回 (fixedOp, fixedVal)，null 项 = 无需修正（由调用方经 apply 广播，多选同样生效）。
        /// </summary>
        public static (Enum fixedOp, string fixedVal) NormalizeOpValue(VariableType? vt, bool isCondition, Enum currentOp, string currentVal)
        {
            Enum fixedOp = null;
            string fixedVal = null;

            // op：对新类型非法则复位默认（cur 为 null 时视为非法，与「字段不存在」的调用方守卫配合）。
            bool opValid = isCondition
                ? ValidCompareOps(vt).Any(x => Equals(x.op, currentOp))
                : ValidAssignOps(vt).Any(x => Equals(x.op, currentOp));
            if (!opValid)
                fixedOp = isCondition ? (Enum)CompareOp.Equal : (Enum)AssignOp.Set;

            // value：布尔变量认 1/true/True（及一切可解析为 true 的文本）；其它类型清掉残留的布尔文本。
            if (vt == VariableType.Bool)
            {
                bool boolish = currentVal == "1" || currentVal == "True" || currentVal == "true"
                    || (bool.TryParse(currentVal, out var b) && b);
                if (!boolish)
                    fixedVal = "false";
            }
            else
            {
                // 从布尔切到其它类型时，残留的 true/false 不是合法数值/字符串初值，需清掉
                // （只认 true/false/True/False，避免误伤数值本身的 "1"/"0"）。
                bool isBoolText = currentVal == "true" || currentVal == "True" || currentVal == "false" || currentVal == "False";
                if (isBoolText)
                    fixedVal = (vt == VariableType.Int || vt == VariableType.Float) ? "0" : "";
            }
            return (fixedOp, fixedVal);
        }

        // ── ④ 外观字段显隐谓词与条件子句文本 ────────────────

        /// <summary>节点级外观·定位子字段（仅「覆盖位置」勾选时显示）。</summary>
        public static bool IsAppearancePositionField(string name) =>
            name == "appearancePositionMode" || name == "appearancePositionAnchor" || name == "appearancePositionOffset";

        /// <summary>节点级外观·生成策略键（仅「覆盖位置」未勾选时显示，与定位覆盖互斥）。</summary>
        public static bool IsAppearanceSpawnStrategyField(string name) =>
            name == "appearanceSpawnStrategyKey";

        /// <summary>条件子句的比较运算符显示文本（折叠头摘要用）。</summary>
        public static string ClauseOpText(CompareOp op) => op switch
        {
            CompareOp.Equal => "==",
            CompareOp.NotEqual => "!=",
            CompareOp.Greater => ">",
            CompareOp.GreaterEqual => ">=",
            CompareOp.Less => "<",
            CompareOp.LessEqual => "<=",
            _ => "?",
        };
    }
}
