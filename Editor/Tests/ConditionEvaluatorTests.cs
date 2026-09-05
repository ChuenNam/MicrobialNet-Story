using System.Collections.Generic;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;

namespace MicrobialNet.Story.Tests
{
    /// <summary>条件求值器测试：All/Any 组合、各类型比较、未定义变量、空条件组语义。运行时与编辑器（校验/试跑）共用此逻辑。</summary>
    public class ConditionEvaluatorTests
    {
        private static IStoryVariableProvider Vars(params (string id, VariableType type, string raw)[] defs)
        {
            var list = new List<StoryVariableDef>();
            foreach (var (id, type, raw) in defs)
                list.Add(new StoryVariableDef { id = id, type = type, defaultValue = raw });
            return new InMemoryVariableProvider(list);
        }

        private static ConditionClause Clause(string varId, CompareOp op, string value)
            => new ConditionClause { variableId = varId, op = op, value = value };

        [TestCase(100, true)]
        [TestCase(10, false)]
        [TestCase(9, false)]
        [TestCase(0, false)]
        public void IntComparison_Greater(int hp, bool expected)
        {
            var ok = ConditionEvaluator.Evaluate(
                new[] { Clause("hp", CompareOp.Greater, "10") }, ConditionCombine.All, Vars(("hp", VariableType.Int, hp.ToString())));
            Assert.AreEqual(expected, ok);
        }

        [Test]
        public void AllCombine_RequiresEveryClause()
        {
            var provider = Vars(("hp", VariableType.Int, "100"), ("mp", VariableType.Int, "0"));
            var both = new[] { Clause("hp", CompareOp.Greater, "0"), Clause("mp", CompareOp.Greater, "0") };
            Assert.IsFalse(ConditionEvaluator.Evaluate(both, ConditionCombine.All, provider), "mp=0 → All 不满足");
            Assert.IsTrue(ConditionEvaluator.Evaluate(both, ConditionCombine.Any, provider), "hp>0 → Any 满足");
        }

        [Test]
        public void AnyCombine_SatisfiedBySingleClause()
        {
            var provider = Vars(("hp", VariableType.Int, "0"), ("key", VariableType.Int, "1"));
            var clauses = new[] { Clause("hp", CompareOp.Greater, "0"), Clause("key", CompareOp.Equal, "1") };
            Assert.IsTrue(ConditionEvaluator.Evaluate(clauses, ConditionCombine.Any, provider));
            Assert.IsFalse(ConditionEvaluator.Evaluate(clauses, ConditionCombine.All, provider));
        }

        [Test]
        public void EmptyClauses_AlwaysSatisfied()
        {
            var provider = Vars(("hp", VariableType.Int, "1"));
            Assert.IsTrue(ConditionEvaluator.Evaluate(new ConditionClause[0], ConditionCombine.All, provider), "空条件组=无约束即放行");
            Assert.IsTrue(ConditionEvaluator.Evaluate(null, ConditionCombine.Any, provider));
        }

        [Test]
        public void UndefinedVariable_ClauseFails()
        {
            var provider = Vars(); // 空黑板
            Assert.IsFalse(ConditionEvaluator.Evaluate(new[] { Clause("ghost", CompareOp.Equal, "1") }, ConditionCombine.All, provider));
        }

        [TestCase(VariableType.Float, "1.5", "Greater", "1.4", true)]
        [TestCase(VariableType.Float, "1.5", "LessEqual", "1.5", true)]
        [TestCase(VariableType.Bool, "true", "Equal", "true", true)]
        [TestCase(VariableType.Bool, "true", "NotEqual", "false", true)]
        [TestCase(VariableType.String, "勇者", "Equal", "勇者", true)]
        [TestCase(VariableType.String, "勇者", "NotEqual", "魔王", true)]
        public void TypeAwareComparisons(VariableType type, string raw, string opStr, string value, bool expected)
        {
            var op = (CompareOp)System.Enum.Parse(typeof(CompareOp), opStr);
            var ok = ConditionEvaluator.Evaluate(new[] { Clause("v", op, value) }, ConditionCombine.All, Vars(("v", type, raw)));
            Assert.AreEqual(expected, ok);
        }

        [TestCase(VariableType.Bool, "Greater", false)]
        [TestCase(VariableType.String, "Greater", false)]
        public void BoolAndString_OnlySupportEquality(VariableType type, string opStr, bool expected)
        {
            var op = (CompareOp)System.Enum.Parse(typeof(CompareOp), opStr);
            var ok = ConditionEvaluator.Evaluate(new[] { Clause("v", op, "x") }, ConditionCombine.All, Vars(("v", type, "x")));
            Assert.AreEqual(expected, ok, $"{type} 不支持 {op}，应判 false 而非抛异常");
        }

        [Test]
        public void FloatCompares_WithDoublePrecision()
        {
            var provider = Vars(("f", VariableType.Float, "0.1"));
            // 0.1 * 3 浮点误差场景：GreaterEqual 边界仍应稳定判真。
            Assert.IsTrue(ConditionEvaluator.Evaluate(new[] { Clause("f", CompareOp.GreaterEqual, "0.1") }, ConditionCombine.All, provider));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new[] { Clause("f", CompareOp.Greater, "0.1000001") }, ConditionCombine.All, provider));
        }
    }
}
