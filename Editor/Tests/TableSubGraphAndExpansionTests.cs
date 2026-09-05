using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 表格驱动核心测试：StoryTableSubGraph 派生（虚拟节点/内部边/头尾端口）、
    /// RuntimeStoryGraph.FromAsset 展开（边界边重映射、tableRows 内容索引、usedCharacterIds、
    /// inlinedTableRows JSON 发布路径），以及「表是唯一内容真相源」的运行时行为与 rowId 本地化 key。
    /// </summary>
    public class TableSubGraphAndExpansionTests
    {
        private const string TableNodeId = "tn1";

        /// <summary>标准测试表：r1 纯对白 → r2 分支（选项0→r3，选项1 无目标=出口）→ r3 终止（/）。speaker 留空=内置旁白。</summary>
        private static StoryTableAsset NewTable()
        {
            var t = ScriptableObject.CreateInstance<StoryTableAsset>();
            t.rows.Add(new StoryTableRow { id = "r1", speaker = "", text = "表格第一句" });
            t.rows.Add(new StoryTableRow
            {
                id = "r2",
                speaker = "char_a",
                text = "要听从指引吗",
                choices = new List<StoryTableChoice>
                {
                    new StoryTableChoice { text = "听从", targetRowId = "r3" },
                    new StoryTableChoice { text = "出口选项", targetRowId = "" },  // 无目标 → optexit 出口端口
                },
            });
            t.rows.Add(new StoryTableRow { id = "r3", speaker = "", text = "表格结尾", targetRowId = "/" });
            return t;
        }

        // ── StoryTableSubGraph.Build 派生 ────────────────────

        [Test]
        public void Build_CreatesVirtualNodes_WithDeterministicIds()
        {
            var result = StoryTableSubGraph.Build(NewTable(), TableNodeId);

            Assert.AreEqual(3, result.nodes.Count, "3 行 → 3 个虚拟节点");
            Assert.IsTrue(result.nodes.Any(n => n.id == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r1")));
            Assert.IsTrue(result.nodes.Any(n => n.id == StoryTableSubGraph.ChoiceVirtualId(TableNodeId, "r2")), "分支行 → 单个「带文字」选择节点（1 节点模型）");
            Assert.IsTrue(result.nodes.Any(n => n.id == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r3")));

            var r1 = result.nodes.First(n => n.id == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r1"));
            Assert.AreEqual("r1", r1.tableBinding.rowId, "虚拟节点 TableBinding 指回行（内容真相源）");
            Assert.AreEqual("表格第一句", ((DialogueNodeData)r1).text, "虚拟节点冗余填入行内容供显示");
        }

        [Test]
        public void Build_InternalEdges_FollowTargetsAndTerminator()
        {
            var result = StoryTableSubGraph.Build(NewTable(), TableNodeId);

            // r1 无目标 → 线性接 r2（RowVirtualId：分支行 → Choice 虚拟节点）。
            Assert.IsTrue(result.edges.Any(e =>
                e.fromNodeId == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r1") && e.fromPortId == "out" &&
                e.toNodeId == StoryTableSubGraph.ChoiceVirtualId(TableNodeId, "r2")));

            // r2 选项0（有目标）→ 内部边连 r3；选项1（无目标）→ 无内部边（出口端口）。
            var choice = (ChoiceNodeData)result.nodes.First(n => n.id == StoryTableSubGraph.ChoiceVirtualId(TableNodeId, "r2"));
            Assert.AreEqual(2, choice.options.Count, "选项含无连接编号项，按行内原始下标编号");
            Assert.AreEqual("0", choice.options[0].optionId);
            Assert.AreEqual("1", choice.options[1].optionId);
            Assert.IsTrue(result.edges.Any(e => e.fromPortId == "opt_0" && e.toNodeId == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r3")));
            Assert.IsFalse(result.edges.Any(e => e.fromPortId == "opt_1"), "无目标选项不连内部边（由边界映射接主图）");

            // r3「/」终止 → 无任何后继。
            Assert.IsFalse(result.edges.Any(e => e.fromNodeId == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r3")));
        }

        [Test]
        public void Build_HeadsAndTails_FirstRowAlwaysEntry_TerminatorRowIsTail()
        {
            var table = NewTable();
            var result = StoryTableSubGraph.Build(table, TableNodeId);

            CollectionAssert.AreEqual(new[] { "r1" }, result.headRowIds, "仅首行（恒为入口）是头");
            CollectionAssert.AreEqual(new[] { "r3" }, result.tailRowIds, "「/」行是尾（输出端口）");

            var entries = StoryTableSubGraph.GetEntryPorts(table, TableNodeId);
            CollectionAssert.AreEqual(new[] { StoryTableSubGraph.EntryPortId("r1") }, entries.Select(p => p.id));

            var exits = StoryTableSubGraph.GetExitPorts(table, TableNodeId);
            Assert.IsTrue(exits.Any(p => p.id == StoryTableSubGraph.ExitPortId("r3")), "终止行 → exit_ 出口端口");
            Assert.IsTrue(exits.Any(p => p.id == StoryTableSubGraph.OptExitPortId("r2", 1)), "无目标选项 → optexit_ 独立出口端口");
        }

        [Test]
        public void Build_DialogueRowWithExplicitTarget_JumpsInsteadOfLinear()
        {
            var table = ScriptableObject.CreateInstance<StoryTableAsset>();
            table.rows.Add(new StoryTableRow { id = "a", text = "甲" });
            table.rows.Add(new StoryTableRow { id = "b", text = "乙", targetRowId = "a" }); // 回跳
            table.rows.Add(new StoryTableRow { id = "c", text = "丙" });

            var result = StoryTableSubGraph.Build(table, "tn");
            Assert.IsTrue(result.edges.Any(e =>
                e.fromNodeId == StoryTableSubGraph.DialogueVirtualId("tn", "b") &&
                e.toNodeId == StoryTableSubGraph.DialogueVirtualId("tn", "a")),
                "显式跳转目标优先于线性下一行");
        }

        // ── RuntimeStoryGraph.FromAsset 展开 ────────────────

        [Test]
        public void FromAsset_ReplacesTableNodeWithVirtualSubgraph_AndRemapsBoundaryEdges()
        {
            var table = NewTable();
            var asset = GraphFactory.ToAsset(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    new StoryTableNodeData { id = TableNodeId, tableAsset = table },
                    GraphFactory.End("e"),
                },
                edges: new[]
                {
                    new StoryEdge { fromNodeId = "s", fromPortId = "out", toNodeId = TableNodeId, toPortId = StoryTableSubGraph.EntryPortId("r1") },
                    new StoryEdge { fromNodeId = TableNodeId, fromPortId = StoryTableSubGraph.ExitPortId("r3"), toNodeId = "e", toPortId = "in" },
                });

            var g = RuntimeStoryGraph.FromAsset(asset);

            Assert.IsFalse(g.nodes.Any(n => n.id == TableNodeId), "表节点本体被虚拟子图取代（不入运行时图）");
            Assert.AreEqual(5, g.nodes.Count, "s + r1/r2/r3 虚拟 + e");
            Assert.AreEqual(3, g.tableRows.Count, "tableRows 内容索引收集全部行");
            Assert.AreEqual("表格第一句", g.tableRows["r1"].text);
            Assert.IsTrue(g.usedCharacterIds.Contains("char_a"), "行内讲述者并入 usedCharacterIds");
            Assert.IsFalse(g.usedCharacterIds.Contains("旁白"), "内置旁白不计入角色引用");

            // 边界边重映射：外部→表入口 端到虚拟头；表出口→外部 改自虚拟尾。
            Assert.IsTrue(g.edges.Any(e => e.fromNodeId == "s" && e.toNodeId == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r1") && e.toPortId == "in"));
            Assert.IsTrue(g.edges.Any(e => e.fromNodeId == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "r3") && e.fromPortId == "out" && e.toNodeId == "e"));
        }

        [Test]
        public void FromAsset_OptExitBoundary_MapsChoiceVirtualPortToExternal()
        {
            var table = NewTable();
            var asset = GraphFactory.ToAsset(
                nodes: new StoryNodeData[]
                {
                    new StoryTableNodeData { id = TableNodeId, tableAsset = table },
                    GraphFactory.End("e2"),
                },
                edges: new[]
                {
                    // 表节点「无目标选项」出口 → 外部 End（每个选项独立出口端口）。
                    new StoryEdge { fromNodeId = TableNodeId, fromPortId = StoryTableSubGraph.OptExitPortId("r2", 1), toNodeId = "e2", toPortId = "in" },
                });

            var g = RuntimeStoryGraph.FromAsset(asset);
            Assert.IsTrue(g.edges.Any(e =>
                e.fromNodeId == StoryTableSubGraph.ChoiceVirtualId(TableNodeId, "r2") &&
                e.fromPortId == "opt_1" &&
                e.toNodeId == "e2"),
                "optexit_{rowId}_{optionIndex} 端口应映射到 Choice 虚拟节点的对应 opt 端口");
        }

        [Test]
        public void FromAsset_InlinedTableRows_MergedWithoutTableAsset()
        {
            // JSON 发布路径：构建期表资产不可解析 → 行内联进资产；表节点无 tableAsset 引用。
            var asset = GraphFactory.ToAsset(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    new StoryTableNodeData { id = TableNodeId }, // 未绑定表资产
                    GraphFactory.End("e"),
                },
                edges: new[]
                {
                    new StoryEdge { fromNodeId = "s", fromPortId = "out", toNodeId = TableNodeId, toPortId = StoryTableSubGraph.EntryPortId("inline_r1") },
                    new StoryEdge { fromNodeId = TableNodeId, fromPortId = StoryTableSubGraph.ExitPortId("inline_r1"), toNodeId = "e", toPortId = "in" },
                });
            asset.inlinedTableRows = new List<StoryTableRow> { new StoryTableRow { id = "inline_r1", text = "内联行内容" } };

            var g = RuntimeStoryGraph.FromAsset(asset);
            Assert.AreEqual("内联行内容", g.tableRows["inline_r1"].text, "内联行合并进内容索引");
            Assert.IsTrue(g.edges.Any(e => e.fromNodeId == "s" && e.toNodeId == StoryTableSubGraph.DialogueVirtualId(TableNodeId, "inline_r1")),
                "无表资产时入口边界仍映射到对白虚拟节点");
        }

        // ── 运行时行为：表是唯一内容真相源 ──────────────────

        [Test]
        public void Player_OverExpandedGraph_TraversesTableContent()
        {
            var table = NewTable();
            var asset = GraphFactory.ToAsset(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    new StoryTableNodeData { id = TableNodeId, tableAsset = table },
                    GraphFactory.End("e"),
                },
                edges: new[]
                {
                    new StoryEdge { fromNodeId = "s", fromPortId = "out", toNodeId = TableNodeId, toPortId = StoryTableSubGraph.EntryPortId("r1") },
                    new StoryEdge { fromNodeId = TableNodeId, fromPortId = StoryTableSubGraph.ExitPortId("r3"), toNodeId = "e", toPortId = "in" },
                });

            var g = RuntimeStoryGraph.FromAsset(asset);
            var player = new StoryPlayer(g, new InMemoryVariableProvider(null), new StoryEventBus());
            var lines = new List<StoryPlayer.Line>();
            var choices = new List<IReadOnlyList<StoryPlayer.Choice>>();
            var ends = new List<(bool, string)>();
            player.OnLine += lines.Add;
            player.OnChoices += choices.Add;
            player.OnEnd += (show, text) => ends.Add((show, text));

            player.Start();
            Assert.AreEqual("表格第一句", lines[0].Text, "虚拟对白节点内容来自绑定行");

            player.Advance();
            Assert.AreEqual(1, choices.Count);
            CollectionAssert.AreEqual(new[] { "听从", "出口选项" }, choices[0].Select(c => c.Text), "选项文本来自行（真相源）");
            StringAssert.Contains("要听从指引吗", choices[0][0].Prompt, "分支行正文并入选项框顶部 Prompt");

            player.Choose("0");
            Assert.AreEqual("表格结尾", lines[1].Text);

            player.Advance();
            Assert.AreEqual(1, ends.Count, "经表尾出口端口走出表、到达主图 End");
        }

        [Test]
        public void Player_TableIsSingleSourceOfTruth_MutatingRowChangesPlayback()
        {
            var table = NewTable();
            var asset = GraphFactory.ToAsset(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    new StoryTableNodeData { id = TableNodeId, tableAsset = table },
                    GraphFactory.End("e"),
                },
                edges: new[]
                {
                    new StoryEdge { fromNodeId = "s", fromPortId = "out", toNodeId = TableNodeId, toPortId = StoryTableSubGraph.EntryPortId("r1") },
                    new StoryEdge { fromNodeId = TableNodeId, fromPortId = StoryTableSubGraph.ExitPortId("r3"), toNodeId = "e", toPortId = "in" },
                });

            var g = RuntimeStoryGraph.FromAsset(asset);
            table.rows[0].text = "改过之后的真相源文本"; // FromAsset 之后改表

            var player = new StoryPlayer(g, new InMemoryVariableProvider(null), new StoryEventBus());
            var lines = new List<StoryPlayer.Line>();
            player.OnLine += lines.Add;
            player.Start();
            Assert.AreEqual("改过之后的真相源文本", lines[0].Text, "运行时内容实时取自表行（tableRows 引用同一对象）");
        }

        [Test]
        public void Player_TableDrivenLocalizationKey_BindsStableRowId()
        {
            var table = NewTable();
            var asset = GraphFactory.ToAsset(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    new StoryTableNodeData { id = TableNodeId, tableAsset = table },
                    GraphFactory.End("e"),
                },
                edges: new[]
                {
                    new StoryEdge { fromNodeId = "s", fromPortId = "out", toNodeId = TableNodeId, toPortId = StoryTableSubGraph.EntryPortId("r1") },
                    new StoryEdge { fromNodeId = TableNodeId, fromPortId = StoryTableSubGraph.ExitPortId("r3"), toNodeId = "e", toPortId = "in" },
                });

            var g = RuntimeStoryGraph.FromAsset(asset);
            var map = new Dictionary<string, string> { ["r1.text"] = "表格译文" };
            var player = new StoryPlayer(g, new InMemoryVariableProvider(null), new StoryEventBus(), new StubTextProvider(map));

            var lines = new List<StoryPlayer.Line>();
            player.OnLine += lines.Add;
            player.Start();
            Assert.AreEqual("表格译文", lines[0].Text, "表驱动本地化 key 绑稳定 rowId（而非虚拟节点 id）");
        }
    }
}
