using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools.Inspector;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 属性面板纯逻辑测试（P4/L1）：混合态判定、表绑定值路由与行写回、变量 op/value 类型归一化决策、
    /// 外观字段显隐谓词。这些函数原先内嵌在 FieldDrawerRegistry（不可单测），抽出后在此建立回归网，
    /// 为 L2 结构拆分提供护栏。
    /// </summary>
    public class FieldPanelLogicTests
    {
        // ── ① 混合态判定（多选批量编辑的显示语义）──────────

        [Test]
        public void AllEqual_UniformValues_True()
        {
            Assert.IsTrue(FieldPanelLogic.AllEqual(new List<object> { "a", "a", "a" }));
            Assert.IsTrue(FieldPanelLogic.AllEqual(new List<object> { 1, 1 }));
            Assert.IsTrue(FieldPanelLogic.AllEqual(new List<object>()));
        }

        [Test]
        public void AllEqual_MixedValues_False()
        {
            Assert.IsFalse(FieldPanelLogic.AllEqual(new List<object> { "a", "b" }));
            Assert.IsFalse(FieldPanelLogic.AllEqual(new List<object> { 1, 2, 1 }));
        }

        [Test]
        public void AllEqual_NullSemantics()
        {
            Assert.IsTrue(FieldPanelLogic.AllEqual(new List<object> { null, null }), "全 null 视为相等");
            Assert.IsFalse(FieldPanelLogic.AllEqual(new List<object> { null, "x" }), "null 与非 null 不等");
            Assert.IsFalse(FieldPanelLogic.AllEqual(new List<object> { "x", null }));
        }

        [Test]
        public void AllEqual_UnityObjectReferenceIdentity()
        {
            var a = ScriptableObject.CreateInstance<StoryTableAsset>();
            var b = ScriptableObject.CreateInstance<StoryTableAsset>();
            try
            {
                Assert.IsTrue(FieldPanelLogic.AllEqual(new List<object> { a, a }), "同一实例相等");
                Assert.IsFalse(FieldPanelLogic.AllEqual(new List<object> { a, b }), "不同实例不等（Unity 对象按实例判等）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void EvaluateMixedState_MixedFlagAndFirstValue()
        {
            var (mixed, display) = FieldPanelLogic.EvaluateMixedState(new List<object> { "甲", "乙" });
            Assert.IsTrue(mixed, "值不同 → 混合态");
            Assert.AreEqual("甲", display, "显示值取首个");

            var (mixed2, display2) = FieldPanelLogic.EvaluateMixedState(new List<object> { "同", "同" });
            Assert.IsFalse(mixed2);
            Assert.AreEqual("同", display2);

            var (mixed3, display3) = FieldPanelLogic.EvaluateMixedState(new List<object>());
            Assert.IsFalse(mixed3);
            Assert.IsNull(display3);
        }

        // ── ② 表绑定值路由（表是唯一内容真相源）────────────

        [TestCase("text", true)]
        [TestCase("speakerId", true)]
        [TestCase("showText", true)]
        [TestCase("options[0].text", true)]
        [TestCase("options[12].text", true)]
        [TestCase("speed", false)]
        [TestCase("typingMode", false)]
        [TestCase("options[0].conditionGroup", false)]
        [TestCase("appearanceStyle", false)]
        [TestCase("textContent", false)]
        [TestCase("", false)]
        public void IsTableContentField_RoutesContentOnly(string path, bool expected)
            => Assert.AreEqual(expected, FieldPanelLogic.IsTableContentField(path));

        [Test]
        public void RouteTableBoundDisplay_ContentFieldsFromRow_OthersFromNode()
        {
            var row = new StoryTableRow { id = "r1", speaker = "老者", text = "行内正文", showText = true };
            Assert.AreEqual("行内正文", FieldPanelLogic.RouteTableBoundDisplay("text", row, "节点正文"));
            Assert.AreEqual("老者", FieldPanelLogic.RouteTableBoundDisplay("speakerId", row, "节点讲述者"));
            Assert.AreEqual(true, FieldPanelLogic.RouteTableBoundDisplay("showText", row, false));
            Assert.AreEqual("节点值", FieldPanelLogic.RouteTableBoundDisplay("speed", row, "节点值"), "非内容字段不路由");
            Assert.AreEqual("节点正文", FieldPanelLogic.RouteTableBoundDisplay("text", null, "节点正文"), "行空 = 不路由");
        }

        [Test]
        public void RouteTableBoundOptionText_ByIndexFromRow()
        {
            var row = new StoryTableRow
            {
                choices = new List<StoryTableChoice>
                {
                    new StoryTableChoice { text = "选项零" },
                    new StoryTableChoice { text = "选项壹" },
                },
            };
            Assert.AreEqual("选项零", FieldPanelLogic.RouteTableBoundOptionText(row, null, 0, "节点文本"));
            Assert.AreEqual("选项壹", FieldPanelLogic.RouteTableBoundOptionText(row, null, 1, "节点文本"));
            Assert.AreEqual("节点文本", FieldPanelLogic.RouteTableBoundOptionText(row, null, 9, "节点文本"), "下标越界用节点文本");
            Assert.AreEqual("节点文本", FieldPanelLogic.RouteTableBoundOptionText(null, null, 0, "节点文本"), "行空用节点文本");
        }

        [Test]
        public void ApplyTableRowEdit_WritesContentFieldsToRow()
        {
            var row = new StoryTableRow
            {
                id = "r1",
                speaker = "旧",
                text = "旧文本",
                showText = false,
                choices = new List<StoryTableChoice> { new StoryTableChoice { text = "旧选项" } },
            };

            Assert.IsTrue(FieldPanelLogic.ApplyTableRowEdit(row, null, "text", "新文本"));
            Assert.AreEqual("新文本", row.text);

            Assert.IsTrue(FieldPanelLogic.ApplyTableRowEdit(row, null, "speakerId", "新讲述者"));
            Assert.AreEqual("新讲述者", row.speaker);

            Assert.IsTrue(FieldPanelLogic.ApplyTableRowEdit(row, null, "showText", true));
            Assert.IsTrue(row.showText);

            Assert.IsTrue(FieldPanelLogic.ApplyTableRowEdit(row, null, "options[0].text", "新选项"));
            Assert.AreEqual("新选项", row.choices[0].text);

            // null 值写 text/speaker 落为空串（与 ApplyTableBoundEdit 原语义一致）。
            Assert.IsTrue(FieldPanelLogic.ApplyTableRowEdit(row, null, "text", null));
            Assert.AreEqual("", row.text);
        }

        [Test]
        public void ApplyTableRowEdit_NonContentOrInvalid_DoesNotWrite()
        {
            var row = new StoryTableRow { text = "保持", choices = new List<StoryTableChoice> { new StoryTableChoice { text = "选项" } } };

            Assert.IsFalse(FieldPanelLogic.ApplyTableRowEdit(row, null, "speed", 0.5f), "非内容字段返回 false（走节点自身）");
            Assert.AreEqual("保持", row.text);
            Assert.IsFalse(FieldPanelLogic.ApplyTableRowEdit(null, null, "text", "x"), "行空返回 false");

            // 选项下标越界：命中内容字段（true）但无处可写，值保持。
            Assert.IsTrue(FieldPanelLogic.ApplyTableRowEdit(row, null, "options[5].text", "越界"));
            Assert.AreEqual("选项", row.choices[0].text);
        }

        // ── ③ 变量 op/value 类型归一化 ─────────────────────

        [Test]
        public void ResolveVarType_FindsDefinition()
        {
            var vars = new List<StoryVariableDef>
            {
                new StoryVariableDef { id = "hp", type = VariableType.Int },
                new StoryVariableDef { id = "ok", type = VariableType.Bool },
            };
            Assert.AreEqual(VariableType.Int, FieldPanelLogic.ResolveVarType(vars, "hp"));
            Assert.AreEqual(VariableType.Bool, FieldPanelLogic.ResolveVarType(vars, "ok"));
            Assert.IsNull(FieldPanelLogic.ResolveVarType(vars, "ghost"), "未定义返回 null");
            Assert.IsNull(FieldPanelLogic.ResolveVarType(null, "hp"));
            Assert.IsNull(FieldPanelLogic.ResolveVarType(vars, null));
            Assert.IsNull(FieldPanelLogic.ResolveVarType(vars, ""), "空 id 返回 null");
        }

        [TestCase(VariableType.Int, 5)]
        [TestCase(VariableType.Float, 5)]
        [TestCase(VariableType.Bool, 1)]
        [TestCase(VariableType.String, 1)]
        [TestCase(null, 1)]
        public void ValidAssignOps_ByVariableType(VariableType? type, int expectedCount)
            => Assert.AreEqual(expectedCount, FieldPanelLogic.ValidAssignOps(type).Count, "布尔/字符串/未定义仅 Set，数值含加减乘除");

        [TestCase(VariableType.Int, 6)]
        [TestCase(VariableType.Float, 6)]
        [TestCase(VariableType.Bool, 2)]
        [TestCase(VariableType.String, 2)]
        [TestCase(null, 2)]
        public void ValidCompareOps_ByVariableType(VariableType? type, int expectedCount)
            => Assert.AreEqual(expectedCount, FieldPanelLogic.ValidCompareOps(type).Count, "布尔/字符串/未定义仅 ==/!=，数值含大小比较");

        [Test]
        public void ValidOps_ContainExpectedOps()
        {
            Assert.IsTrue(FieldPanelLogic.ValidAssignOps(VariableType.Int).Any(x => Equals(x.op, AssignOp.Div)));
            Assert.IsFalse(FieldPanelLogic.ValidAssignOps(VariableType.Bool).Any(x => Equals(x.op, AssignOp.Add)));
            Assert.IsTrue(FieldPanelLogic.ValidCompareOps(VariableType.String).Any(x => Equals(x.op, CompareOp.Equal)));
            Assert.IsFalse(FieldPanelLogic.ValidCompareOps(VariableType.Bool).Any(x => Equals(x.op, CompareOp.Greater)));
        }

        [TestCase(VariableType.Int, false, "Add", "5", null, null)]
        [TestCase(VariableType.Int, false, "Add", "true", null, "0")]
        [TestCase(VariableType.Float, false, "Set", "False", null, "0")]
        [TestCase(VariableType.String, false, "Set", "true", null, "")]
        [TestCase(VariableType.String, false, "Set", "普通文本", null, null)]
        [TestCase(VariableType.Int, false, "Set", "42", null, null)]
        [TestCase(VariableType.Bool, false, "Add", "1", "Set", null)]
        [TestCase(VariableType.Bool, false, "Set", "hello", null, "false")]
        [TestCase(VariableType.Bool, false, "Set", "true", null, null)]
        [TestCase(VariableType.Bool, false, "Set", "True", null, null)]
        [TestCase(null, false, "Add", "5", "Set", null)]
        [TestCase(null, false, "Set", "true", null, "")]
        [TestCase(VariableType.Int, true, "Greater", "10", null, null)]
        [TestCase(VariableType.Bool, true, "Greater", "x", "Equal", "false")]
        [TestCase(VariableType.String, true, "Greater", "a", "Equal", null)]
        public void NormalizeOpValue_TypeDrivenCorrection(
            VariableType? vt, bool isCondition, string opStr, string currentVal, string expectOpStr, string expectVal)
        {
            // op 用字符串传参再解析（internal 枚举不作 public 测试方法参数，规避 CS0051）。
            var currentOp = ParseOp(opStr, isCondition);
            var expectOp = expectOpStr != null ? ParseOp(expectOpStr, isCondition) : null;
            var (fixedOp, fixedVal) = FieldPanelLogic.NormalizeOpValue(vt, isCondition, currentOp, currentVal);
            Assert.AreEqual(expectOp, fixedOp, $"op 修正期望 {expectOpStr ?? "(无)"}");
            Assert.AreEqual(expectVal, fixedVal, $"value 修正期望 {expectVal ?? "(无)"}");
        }

        private static Enum ParseOp(string s, bool isCondition)
            => isCondition ? (Enum)System.Enum.Parse(typeof(CompareOp), s) : (Enum)System.Enum.Parse(typeof(AssignOp), s);

        [Test]
        public void NormalizeOpValue_BoolRecognizesTruthyLiterals()
        {
            // 布尔变量认 1/true/True 及一切可解析为 true 的文本（大小写不敏感）→ 不修正。
            foreach (var truthy in new[] { "1", "true", "True", "TRUE" })
            {
                var (_, fixedVal) = FieldPanelLogic.NormalizeOpValue(VariableType.Bool, false, AssignOp.Set, truthy);
                Assert.IsNull(fixedVal, $"布尔值 {truthy} 应视为合法，不修正");
            }
            // 数值 "0" 不是真值 → 修正为 "false"（bool.TryParse("0")=true 但 b=false → boolish false）。
            var (_, fixed0) = FieldPanelLogic.NormalizeOpValue(VariableType.Bool, false, AssignOp.Set, "0");
            Assert.AreEqual("false", fixed0);
        }

        [Test]
        public void NormalizeOpValue_NumericOneIsNotBoolResidueForNonBool()
        {
            // "1"/"0" 是合法数值，切到 Int 不应被当布尔残留清掉（注释明确的防误伤契约）。
            var (_, fixedVal) = FieldPanelLogic.NormalizeOpValue(VariableType.Int, false, AssignOp.Set, "1");
            Assert.IsNull(fixedVal);
            var (_, fixedVal0) = FieldPanelLogic.NormalizeOpValue(VariableType.Int, false, AssignOp.Set, "0");
            Assert.IsNull(fixedVal0);
        }

        // ── ④ 外观字段显隐谓词与条件子句文本 ────────────────

        [TestCase("appearancePositionMode", true)]
        [TestCase("appearancePositionAnchor", true)]
        [TestCase("appearancePositionOffset", true)]
        [TestCase("appearanceOverridePosition", false)]
        [TestCase("appearanceSpawnStrategyKey", false)]
        [TestCase("text", false)]
        public void IsAppearancePositionField_Predicate(string name, bool expected)
            => Assert.AreEqual(expected, FieldPanelLogic.IsAppearancePositionField(name));

        [TestCase("appearanceSpawnStrategyKey", true)]
        [TestCase("appearancePositionMode", false)]
        [TestCase("appearanceStyle", false)]
        public void IsAppearanceSpawnStrategyField_Predicate(string name, bool expected)
            => Assert.AreEqual(expected, FieldPanelLogic.IsAppearanceSpawnStrategyField(name));

        [TestCase("Equal", "==")]
        [TestCase("NotEqual", "!=")]
        [TestCase("Greater", ">")]
        [TestCase("GreaterEqual", ">=")]
        [TestCase("Less", "<")]
        [TestCase("LessEqual", "<=")]
        public void ClauseOpText_Symbols(string opStr, string expected)
            => Assert.AreEqual(expected, FieldPanelLogic.ClauseOpText((CompareOp)System.Enum.Parse(typeof(CompareOp), opStr)));
    }
}
