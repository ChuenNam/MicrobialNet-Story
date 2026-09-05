using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Validation;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;

namespace MicrobialNet.Story.Tests
{
    /// <summary>编辑期静态校验器测试：各规则触发（入口/出边/可达性/环/引用缺失/类型不匹配/重复选项），干净图零问题。</summary>
    public class ValidatorTests
    {
        private static List<ValidationIssue> Validate(IEnumerable<StoryNodeData> nodes, IEnumerable<StoryEdge> edges,
            IEnumerable<StoryVariableDef> vars = null, IEnumerable<StoryVariableDef> globalVars = null,
            IEnumerable<string> eventNameMismatches = null)
        {
            var asset = GraphFactory.ToAsset(nodes, edges, vars);
            using (var model = new StoryGraphModel(asset))
                // 事件名不一致清单默认注入空集（非 null）：隔离编辑器全工程 TypeCache 域缓存，
                // 测试不随工程里新增/修改事件类而顺序耦合（沿用 globalVars 注入先例）。
                return StoryValidator.Validate(model, globalVars, eventNameMismatches ?? new string[0]);
        }

        private static bool HasRule(List<ValidationIssue> issues, string ruleId, string nodeId = null)
            => issues.Any(i => i.RuleId == ruleId && (nodeId == null || i.NodeId == nodeId));

        [Test]
        public void CleanNarrationGraph_NoIssues()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "内容"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "e") });
            Assert.IsEmpty(issues, $"干净图应零问题，实际：{string.Join(";", issues.Select(i => i.RuleId))}");
        }

        [Test]
        public void NoEntry_IsError()
        {
            var issues = Validate(
                new StoryNodeData[] { GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "内容"), GraphFactory.End("e") },
                new[] { GraphFactory.Edge("d1", "out", "e") });
            Assert.IsTrue(HasRule(issues, "NoEntry"));
            Assert.AreEqual(ValidationSeverity.Error, issues.First(i => i.RuleId == "NoEntry").Severity);
        }

        [Test]
        public void MultipleEntries_IsWarning()
        {
            var issues = Validate(
                new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s1"), GraphFactory.Node<StartNodeData>("s2"), GraphFactory.End("e") },
                new[] { GraphFactory.Edge("s1", "out", "e"), GraphFactory.Edge("s2", "out", "e") });
            Assert.IsTrue(HasRule(issues, "MultiEntry", "s2"));
        }

        [TestCase("Dialogue")]
        [TestCase("Start")]
        [TestCase("Event")]
        [TestCase("SetVariable")]
        public void MissingOutEdge_IsError(string nodeKind)
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    nodeKind switch
                    {
                        "Dialogue" => (StoryNodeData)GraphFactory.Dialogue("x", StoryConstants.NarrationId, "内容"),
                        "Start" => GraphFactory.Node<StartNodeData>("x"),
                        "Event" => new EventNodeData { id = "x", eventName = "evt" },
                        _ => GraphFactory.SetVar("x", "hp", AssignOp.Set, "1"),
                    },
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "x") }); // x 自身无出边
            Assert.IsTrue(HasRule(issues, "NoOut", "x"), $"{nodeKind} 缺出边应报 NoOut");
        }

        [Test]
        public void UnreachableNode_IsWarning()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "可达"),
                    GraphFactory.Dialogue("orphan", StoryConstants.NarrationId, "孤立"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "e") });
            Assert.IsTrue(HasRule(issues, "Unreachable", "orphan"));
        }

        [Test]
        public void Cycle_IsWarning()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("a", StoryConstants.NarrationId, "A"),
                    GraphFactory.Dialogue("b", StoryConstants.NarrationId, "B"),
                },
                new[] { GraphFactory.Edge("s", "out", "a"), GraphFactory.Edge("a", "out", "b"), GraphFactory.Edge("b", "out", "a") });
            Assert.IsTrue(HasRule(issues, "Cycle"), "a→b→a 环应被三色 DFS 检出");
        }

        [Test]
        public void MissingVariableInCondition_IsError()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Cond("cond", "ghost_missing", CompareOp.Equal, "1"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "cond"), GraphFactory.Edge("cond", "true", "e"), GraphFactory.Edge("cond", "false", "e") });
            Assert.IsTrue(HasRule(issues, "MissingVar", "cond"));
        }

        // ── 全局变量「已定义」域（修复：校验域与运行时一致）──────────

        /// <summary>赋值/条件引用**全局变量**（GlobalVariables.asset 域）不算未定义——
        /// 修复前 varIds 只收本图变量，引用全局变量的赋值节点被误报 MissingVar Error 并阻塞构建门禁，
        /// 而运行时（StoryFlow 两级 seed）与试跑（模拟器并入全局表）都正常。</summary>
        [Test]
        public void GlobalVariableReference_IsNotMissing()
        {
            var globals = new[] { GraphFactory.Var("gv_flag", "全局标记", VariableType.Bool, "false") };
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.SetVar("set", "gv_flag", AssignOp.Set, "true"),
                    GraphFactory.Cond("cond", "gv_flag", CompareOp.Equal, "true"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "set"), GraphFactory.Edge("set", "out", "cond"), GraphFactory.Edge("cond", "true", "e"), GraphFactory.Edge("cond", "false", "e") },
                globalVars: globals);
            Assert.IsFalse(HasRule(issues, "MissingVar"), $"引用全局变量不应报 MissingVar，实际：{string.Join(";", issues.Select(i => i.RuleId))}");
        }

        /// <summary>全局变量赋值同样获得类型检查（def 查找：本图优先，其次全局表）。</summary>
        [Test]
        public void GlobalVariableAssignment_TypeCheck()
        {
            var globals = new[] { GraphFactory.Var("gv_count", "全局计数", VariableType.Int, "0") };
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.SetVar("set", "gv_count", AssignOp.Set, "不是数字"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "set"), GraphFactory.Edge("set", "out", "e") },
                globalVars: globals);
            Assert.IsTrue(HasRule(issues, "VarTypeMismatch", "set"), "全局变量赋值类型不匹配应告警");
        }

        [Test]
        public void ConditionWithoutBranches_IsWarning()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Cond("cond", "hp", CompareOp.Equal, "1"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "cond"), GraphFactory.Edge("cond", "true", "e") }, // false 分支悬空
                new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });
            Assert.IsTrue(HasRule(issues, "CondNoBranch", "cond"));
        }

        [Test]
        public void VariableDefaultTypeMismatch_IsWarning()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "内容"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "e") },
                new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "不是数字") });
            Assert.IsTrue(HasRule(issues, "VarDefaultMismatch"));
        }

        [Test]
        public void SetVariableTypeMismatch_IsWarning()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.SetVar("set", "hp", AssignOp.Set, "abc"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "set"), GraphFactory.Edge("set", "out", "e") },
                new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });
            Assert.IsTrue(HasRule(issues, "VarTypeMismatch", "set"));
        }

        [Test]
        public void EmptyTextAndMissingSpeaker_AreWarnings()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    new DialogueNodeData { id = "d1", speakerId = "", text = "" },
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "e") });
            Assert.IsTrue(HasRule(issues, "EmptyText", "d1"));
            Assert.IsTrue(HasRule(issues, "NoSpeaker", "d1"));
        }

        [Test]
        public void MissingCharacterReference_IsWarning_BuiltInSpeakersExempt()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d1", "char_ghost_zzz", "内容"),
                    GraphFactory.Dialogue("d2", StoryConstants.NarrationId, "内容"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "d2"), GraphFactory.Edge("d2", "out", "e") });
            Assert.IsTrue(HasRule(issues, "MissingChar", "d1"), "角色库中不存在的讲述者应告警");
            Assert.IsFalse(HasRule(issues, "MissingChar", "d2"), "内置旁白不报缺失");
        }

        [Test]
        public void ChoiceDefects_NoOptions_DuplicateText_OptionNoTarget()
        {
            var emptyChoice = GraphFactory.Choice("c_empty");
            var dupChoice = GraphFactory.Choice("c_dup", ("a", "重复文本"), ("b", "重复文本"));
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    emptyChoice,
                    GraphFactory.Choice("c_ok", ("x", "有效选项")),
                    dupChoice,
                    GraphFactory.End("e"),
                },
                new[]
                {
                    GraphFactory.Edge("s", "out", "c_empty"),
                    GraphFactory.Edge("c_empty", "opt_none", "e"),
                    GraphFactory.Edge("c_ok", "opt_x", "e"),
                    // c_dup 的选项均未连线
                });
            Assert.IsTrue(HasRule(issues, "NoOptions", "c_empty"));
            Assert.IsTrue(HasRule(issues, "DupOptionText", "c_dup"), "选项文本重复应告警");
            Assert.IsTrue(HasRule(issues, "OptNoTarget", "c_dup"), "未连线的选项应报错误");
        }

        [Test]
        public void ChoiceOptionWithUndefinedConditionVariable_IsError()
        {
            var c = GraphFactory.Choice("c1", ("a", "选项"));
            c.options[0].hasCondition = true;
            c.options[0].conditionGroup.Add(new ConditionClause { variableId = "ghost_missing", op = CompareOp.Equal, value = "1" });

            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    c,
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "c1"), GraphFactory.Edge("c1", "opt_a", "e") });
            Assert.IsTrue(HasRule(issues, "MissingVar", "c1"), "选项条件引用未定义变量应报错");
        }

        [Test]
        public void JumpChapterWithoutTarget_IsWarning()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.End("jump", EndType.JumpChapter, ""),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "jump") });
            Assert.IsTrue(HasRule(issues, "EmptyJump", "jump"));
        }

        // ── P5/L0+L1：事件 payload 语法与事件名双写一致性 ──────────

        /// <summary>payload 手写 JSON 语法错误在编辑期拦截为 Error——
        /// 错误不再右移到「运行时业务侧反序列化才炸」；Error 级同时被构建门禁（StoryBuildValidator）拦截。</summary>
        [Test]
        public void EventPayload_InvalidJson_IsError()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Event("ev", "evt:a", "{\"enemy\":"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "ev"), GraphFactory.Edge("ev", "out", "e") });
            Assert.IsTrue(HasRule(issues, "BadPayloadJson", "ev"));
            Assert.AreEqual(ValidationSeverity.Error, issues.First(i => i.RuleId == "BadPayloadJson").Severity,
                "Error 级：构建门禁按 Error 阻断打包");
        }

        /// <summary>合法 JSON（对象/数组/标量）与空 payload 均直通——只验语法，不限 schema。</summary>
        [TestCase("{\"enemy\":\"slime\"}")]
        [TestCase("[1,2,3]")]
        [TestCase("123")]
        [TestCase("true")]
        [TestCase(null)]
        [TestCase("   ")]
        public void EventPayload_ValidJsonOrEmpty_NoIssue(string payload)
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Event("ev", "evt:a", payload),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "ev"), GraphFactory.Edge("ev", "out", "e") });
            Assert.IsFalse(HasRule(issues, "BadPayloadJson"),
                $"payload「{payload ?? "(null)"}」不应报 BadPayloadJson，实际：{string.Join(";", issues.Select(i => i.RuleId))}");
        }

        /// <summary>「[StoryEvent] 特性名 ≠ EventName」为图级黄条，且仅含事件节点的图提示（无关图不噪音）。</summary>
        [Test]
        public void EventNameMismatch_GraphLevelWarning_OnlyForGraphsWithEventNodes()
        {
            var mismatch = new[] { "事件类 Foo 的 [StoryEvent(\"a\")] 与 EventName「b」不一致（测试注入）" };
            var withEvent = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Event("ev", "evt:a"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "ev"), GraphFactory.Edge("ev", "out", "e") },
                eventNameMismatches: mismatch);
            Assert.IsTrue(HasRule(withEvent, "EventNameMismatch"));
            Assert.AreEqual(ValidationSeverity.Warning, withEvent.First(i => i.RuleId == "EventNameMismatch").Severity,
                "黄条级别，不阻断构建");

            var withoutEvent = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "内容"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "e") },
                eventNameMismatches: mismatch);
            Assert.IsFalse(HasRule(withoutEvent, "EventNameMismatch"), "无事件节点的图不应提示工程级事件名不一致");
        }

        [Test]
        public void CleanEventGraph_NoIssues()
        {
            var issues = Validate(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Event("ev", "evt:a", "{\"enemy\":\"slime\"}"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "ev"), GraphFactory.Edge("ev", "out", "e") });
            Assert.IsEmpty(issues, $"干净事件图应零问题，实际：{string.Join(";", issues.Select(i => i.RuleId))}");
        }

        [Test]
        public void Reachability_BfsFromEntry()
        {
            var asset = GraphFactory.ToAsset(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "A"),
                    GraphFactory.Dialogue("orphan", StoryConstants.NarrationId, "B"),
                },
                new[] { GraphFactory.Edge("s", "out", "d1") });
            using (var model = new StoryGraphModel(asset))
            {
                var reachable = StoryValidator.GetReachableNodeIds(model);
                CollectionAssert.AreEquivalent(new[] { "s", "d1" }, reachable);
                var unreachable = StoryValidator.GetUnreachableNodeIds(model);
                CollectionAssert.AreEquivalent(new[] { "orphan" }, unreachable);
            }
        }
    }
}
