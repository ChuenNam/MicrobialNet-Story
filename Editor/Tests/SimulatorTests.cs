using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Playback;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 编辑器试跑模拟器测试：免 Play 演练（前进/选择/回退/死路）、变量副作用、
    /// 选项条件门控（不可见与原因文本）、表格驱动展开演练（与运行时同构）。
    /// </summary>
    public class SimulatorTests
    {
        private static (StoryGraphModel model, StorySimulator sim) MakeSim(
            IEnumerable<StoryNodeData> nodes, IEnumerable<StoryEdge> edges, IEnumerable<StoryVariableDef> vars = null)
        {
            var asset = GraphFactory.ToAsset(nodes, edges, vars);
            var model = new StoryGraphModel(asset);
            return (model, new StorySimulator(model));
        }

        private static (StoryGraphModel model, StorySimulator sim) DemoSim()
            => MakeSim(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("hello", StoryConstants.NarrationId, "开场白"),
                    GraphFactory.Choice("c", ("a", "迎战"), ("b", "撤退")),
                    GraphFactory.SetVar("set", "hp", AssignOp.Sub, "10"),
                    new EventNodeData { id = "evt", eventName = "battle" },
                    GraphFactory.End("endA"),
                    GraphFactory.Cond("cond", "hp", CompareOp.Greater, "0"),
                    GraphFactory.Dialogue("alive", StoryConstants.NarrationId, "成功撤离"),
                    GraphFactory.Dialogue("dead", StoryConstants.NarrationId, "无力再战"),
                    GraphFactory.End("endB"),
                },
                new[]
                {
                    GraphFactory.Edge("s", "out", "hello"),
                    GraphFactory.Edge("hello", "out", "c"),
                    GraphFactory.Edge("c", "opt_a", "set"),
                    GraphFactory.Edge("c", "opt_b", "cond"),
                    GraphFactory.Edge("set", "out", "evt"),
                    GraphFactory.Edge("evt", "out", "endA"),
                    GraphFactory.Edge("cond", "true", "alive"),
                    GraphFactory.Edge("cond", "false", "dead"),
                    GraphFactory.Edge("alive", "out", "endB"),
                    GraphFactory.Edge("dead", "out", "endB"),
                },
                new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });

        [Test]
        public void Advance_StopsAtChoice_VisibleOptions()
        {
            var (model, sim) = DemoSim();
            sim.Load(model.GetEntryNode()); // Start
            sim.Advance(); // 连续前进直到选项

            Assert.AreEqual(SimState.AtChoice, sim.State);
            Assert.AreEqual("c", sim.Current.id);
            Assert.AreEqual(2, sim.CurrentFrame.Choices.Count);
            Assert.IsTrue(sim.CurrentFrame.Choices.All(o => o.Visible));
        }

        [Test]
        public void ChooseOption_AppliesEffectsAlongPath_AndReachesEnd()
        {
            var (model, sim) = DemoSim();
            sim.Load(model.GetEntryNode());
            sim.Advance();

            var frameCountAtChoice = sim.Frames.Count;
            sim.ChooseOption(0); // 选「迎战」→ set(hp-10) → evt → endA
            sim.Advance();

            Assert.AreEqual(SimState.Finished, sim.State);
            Assert.AreEqual("endA", sim.Current.id);
            Assert.AreEqual(90, sim.Variables["hp"].AsInt, "赋值副作用应沿路径生效");
            Assert.AreEqual(frameCountAtChoice + 3, sim.Frames.Count, "set/evt/endA 三帧入栈");
        }

        [Test]
        public void Back_RestoresVariableSnapshot_PerFrame()
        {
            var (model, sim) = DemoSim();
            sim.Load(model.GetEntryNode());
            sim.Advance();
            sim.ChooseOption(0); // → 停在 set（到达快照 hp=100，副作用尚未应用）
            sim.Advance();       // set 生效 hp=90 → evt → endA
            Assert.AreEqual(SimState.Finished, sim.State);
            Assert.AreEqual(90, sim.Variables["hp"].AsInt);

            sim.Back(); // 回 evt 帧（到达 evt 时快照：hp=90）
            Assert.AreEqual(SimState.Ready, sim.State);
            Assert.AreEqual(90, sim.Variables["hp"].AsInt);

            sim.Back(); // 回 set 帧（到达 set 时快照：hp=100，效果未应用）
            Assert.AreEqual(SimState.Ready, sim.State);
            Assert.AreEqual(100, sim.Variables["hp"].AsInt, "回退恢复「到达该帧时」的变量快照");

            sim.Back(); // 回选项帧
            Assert.AreEqual(SimState.AtChoice, sim.State);
            Assert.AreEqual(100, sim.Variables["hp"].AsInt);

            sim.ChooseOption(1); // 撤退 → cond(hp>0)=true → alive → endB
            sim.Advance();
            Assert.AreEqual(SimState.Finished, sim.State);
            Assert.AreEqual(100, sim.Variables["hp"].AsInt, "B 支线不赋值");
        }

        [Test]
        public void DeadEnd_BecomesBlocked()
        {
            var (model, sim) = MakeSim(
                new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.Dialogue("d", StoryConstants.NarrationId, "没有后继") },
                new[] { GraphFactory.Edge("s", "out", "d") });
            sim.Load(model.GetEntryNode());
            sim.Advance(); // 前进到无出边的对白 → 下一次推进发现死路
            Assert.AreEqual(SimState.Blocked, sim.State);
        }

        [Test]
        public void OptionCondition_GatesVisibility_WithReasonText()
        {
            var gated = GraphFactory.Choice("c", ("open", "开放选项"), ("locked", "锁定选项"));
            gated.options[1].hasCondition = true;
            gated.options[1].conditionGroup.Add(new ConditionClause { variableId = "key", op = CompareOp.Equal, value = "1" });

            var (model, sim) = MakeSim(
                new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), gated, GraphFactory.End("e") },
                new[]
                {
                    GraphFactory.Edge("s", "out", "c"),
                    GraphFactory.Edge("c", "opt_open", "e"),
                    GraphFactory.Edge("c", "opt_locked", "e"),
                },
                new[] { GraphFactory.Var("key", "钥匙", VariableType.Int, "0") });

            // 临时注册变量名解析器（试跑摘要依赖全局静态；测试环境默认无人注册 → [未配置] 占位）。
            var before = StoryConstants.VariableNameResolver;
            StoryConstants.VariableNameResolver = id => id == "key" ? "钥匙" : null;
            try
            {
                sim.Load(model.GetEntryNode());
                sim.Advance();
                var opts = sim.CurrentFrame.Choices;
                Assert.IsTrue(opts[0].Visible);
                Assert.IsFalse(opts[1].Visible, "条件不满足的选项应隐藏");
                StringAssert.Contains("钥匙", opts[1].ConditionText, "隐藏原因应给出可读的条件文本");
                StringAssert.Contains("== 1", opts[1].ConditionText);

                sim.ChooseOption(0); // 只能选可见选项
                Assert.AreEqual(SimState.Finished, sim.State);
            }
            finally
            {
                StoryConstants.VariableNameResolver = before;
            }
        }

        [Test]
        public void TableDriven_ExpandsVirtualSubgraph_ForSimulation()
        {
            // 表驱动内容经 GUID 解析（StoryTableResolver）：表资产须真实落盘，模拟编辑器工作流。
            const string dir = "Assets/StorySimTableTmp";
            StoryGraphAsset asset = null;
            StoryTableAsset table = null;
            var model = (StoryGraphModel)null;
            try
            {
                if (!AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.CreateFolder("Assets", "StorySimTableTmp");

                table = ScriptableObject.CreateInstance<StoryTableAsset>();
                table.rows.Add(new StoryTableRow { id = "r1", text = "表内第一句" });
                table.rows.Add(new StoryTableRow
                {
                    id = "r2",
                    text = "表内分支",
                    choices = new List<StoryTableChoice> { new StoryTableChoice { text = "走", targetRowId = "r3" } },
                });
                table.rows.Add(new StoryTableRow { id = "r3", text = "表内结尾", targetRowId = "/" });
                AssetDatabase.CreateAsset(table, $"{dir}/table.asset");
                AssetDatabase.SaveAssets();

                asset = GraphFactory.ToAsset(
                    new StoryNodeData[]
                    {
                        GraphFactory.Node<StartNodeData>("s"),
                        new StoryTableNodeData { id = "tn", tableAsset = table },
                        GraphFactory.End("e"),
                    },
                    new[]
                    {
                        new StoryEdge { fromNodeId = "s", fromPortId = "out", toNodeId = "tn", toPortId = StoryTableSubGraph.EntryPortId("r1") },
                        new StoryEdge { fromNodeId = "tn", fromPortId = StoryTableSubGraph.ExitPortId("r3"), toNodeId = "e", toPortId = "in" },
                    });
                model = new StoryGraphModel(asset);
                var sim = new StorySimulator(model);

                sim.Load(model.GetEntryNode());
                sim.Advance(); // 走到表内分支（选项节点）

                Assert.AreEqual(SimState.AtChoice, sim.State);
                StringAssert.Contains("tn::chc::r2", sim.Current.id, "试跑应展开表虚拟子图逐行演练（与运行时同构）");
                Assert.AreEqual(1, sim.CurrentFrame.Choices.Count);
                Assert.AreEqual("走", sim.CurrentFrame.Choices[0].Text, "选项文本经 GUID 解析自表行（真相源）");

                sim.ChooseOption(0);
                sim.Advance();
                Assert.AreEqual(SimState.Finished, sim.State, "经表尾出口端口走出、到达主图 End");
                StringAssert.Contains("表内结尾", sim.Frames[sim.Frames.Count - 2].EffectText);
            }
            finally
            {
                if (model != null) model.Dispose();
                if (AssetDatabase.IsValidFolder(dir))
                {
                    AssetDatabase.DeleteAsset(dir);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
                if (asset != null) Undo.ClearUndo(asset);
            }
        }
    }
}
