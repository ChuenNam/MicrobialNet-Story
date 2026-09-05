using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 播放内核（StoryPlayer）测试：线性遍历、选项分支、条件、赋值、事件挂起、
    /// 错误路径（死路/缺入口/不可执行/熔断）、快照捕获与恢复、跨图恢复与 JumpChapter。
    /// 全部纯逻辑（无 UI / 无 MonoBehaviour），直接构造 internal 运行时图。
    /// </summary>
    public class StoryPlayerTests
    {
        // ── 线性遍历与等待态 ──────────────────────────────────

        [Test]
        public void LinearPlay_PresentLinesInOrder_ThenEnd()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "第一句"), GraphFactory.Dialogue("d2", StoryConstants.NarrationId, "第二句"), GraphFactory.End("e") },
                edges: new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "d2"), GraphFactory.Edge("d2", "out", "e") });

            var lines = new List<StoryPlayer.Line>();
            var ends = new List<(bool, string)>();
            player.OnLine += lines.Add;
            player.OnEnd += (show, text) => ends.Add((show, text));

            player.Start();
            Assert.IsTrue(player.IsRunning);
            Assert.IsTrue(player.IsWaiting, "对白节点应进入等待态");
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("第一句", lines[0].Text);

            player.Advance();
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("第二句", lines[1].Text);
            Assert.IsTrue(player.IsWaiting);

            player.Advance();
            Assert.AreEqual(1, ends.Count, "应到达 End 节点并终结");
            Assert.IsFalse(player.IsRunning);
            Assert.IsFalse(player.IsWaiting);
        }

        [Test]
        public void Choice_PresentsOptions_AndChooseFollowsBranch()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Choice("c", ("a", "选项A"), ("b", "选项B")),
                    GraphFactory.Dialogue("da", StoryConstants.NarrationId, "走了A"),
                    GraphFactory.Dialogue("db", StoryConstants.NarrationId, "走了B"),
                },
                edges: new[]
                {
                    GraphFactory.Edge("s", "out", "c"),
                    GraphFactory.Edge("c", "opt_a", "da"),
                    GraphFactory.Edge("c", "opt_b", "db"),
                });

            var choices = new List<IReadOnlyList<StoryPlayer.Choice>>();
            var lines = new List<StoryPlayer.Line>();
            player.OnChoices += choices.Add;
            player.OnLine += lines.Add;

            player.Start();
            Assert.AreEqual(1, choices.Count);
            Assert.AreEqual(2, choices[0].Count, "两个选项都应呈现");
            CollectionAssert.AreEqual(new[] { "选项A", "选项B" }, choices[0].Select(c => c.Text));

            player.Choose("b");
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("走了B", lines[0].Text, "应沿 opt_b 分支跳转");
            Assert.IsTrue(player.IsWaiting);
        }

        [Test]
        public void Choose_InvalidOptionId_RaisesErrorAndStops()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.Choice("c", ("a", "A")), GraphFactory.End("e") },
                edges: new[] { GraphFactory.Edge("s", "out", "c"), GraphFactory.Edge("c", "opt_a", "e") });

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();

            player.Choose("not_exist");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("选项不存在", errors[0]);
            Assert.IsFalse(player.IsRunning);
        }

        // ── 条件分支与变量赋值 ────────────────────────────────

        [Test]
        public void Condition_TrueAndFalseBranches_FollowVariableValue()
        {
            for (int hp = 0; hp <= 100; hp += 100)
            {
                var player = GraphFactory.MakePlayer(
                    nodes: new StoryNodeData[]
                    {
                        GraphFactory.Node<StartNodeData>("s"),
                        GraphFactory.Cond("cond", "hp", CompareOp.Greater, "0"),
                        GraphFactory.Dialogue("alive", StoryConstants.NarrationId, "存活"),
                        GraphFactory.Dialogue("dead", StoryConstants.NarrationId, "倒下"),
                    },
                    edges: new[] { GraphFactory.Edge("s", "out", "cond"), GraphFactory.Edge("cond", "true", "alive"), GraphFactory.Edge("cond", "false", "dead") },
                    variables: new[] { GraphFactory.Var("hp", "HP", VariableType.Int, hp.ToString()) });

                var lines = new List<StoryPlayer.Line>();
                player.OnLine += lines.Add;
                player.Start();

                Assert.AreEqual(1, lines.Count);
                Assert.AreEqual(hp > 0 ? "存活" : "倒下", lines[0].Text, $"hp={hp} 应走 {(hp > 0 ? "true" : "false")} 分支");
            }
        }

        [TestCase("Add", "5", 105)]
        [TestCase("Sub", "5", 95)]
        [TestCase("Mul", "2", 200)]
        [TestCase("Set", "7", 7)]
        public void SetVariable_IntOps_ApplyArithmetic(string opStr, string value, int expected)
        {
            var op = (AssignOp)System.Enum.Parse(typeof(AssignOp), opStr);
            var provider = new InMemoryVariableProvider(new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });
            var g = new RuntimeStoryGraph
            {
                meta = new StoryMeta { storyId = "t" },
                nodes = new List<StoryNodeData> { GraphFactory.Node<StartNodeData>("s"), GraphFactory.SetVar("set", "hp", op, value), GraphFactory.End("e") },
                edges = new List<StoryEdge> { GraphFactory.Edge("s", "out", "set"), GraphFactory.Edge("set", "out", "e") },
            };
            var player = new StoryPlayer(g, provider, new StoryEventBus());
            player.Start();

            Assert.IsTrue(provider.TryGetValue("hp", out var v));
            Assert.AreEqual(expected, System.Convert.ToInt64(v), "Int 赋值节点写回 long（产品契约）");
        }

        [Test]
        public void SetVariable_DivideByZero_KeepsOriginalValue()
        {
            var provider = new InMemoryVariableProvider(new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });
            var g = new RuntimeStoryGraph
            {
                meta = new StoryMeta { storyId = "t" },
                nodes = new List<StoryNodeData> { GraphFactory.Node<StartNodeData>("s"), GraphFactory.SetVar("set", "hp", AssignOp.Div, "0"), GraphFactory.End("e") },
                edges = new List<StoryEdge> { GraphFactory.Edge("s", "out", "set"), GraphFactory.Edge("set", "out", "e") },
            };
            new StoryPlayer(g, provider, new StoryEventBus()).Start();

            provider.TryGetValue("hp", out var v);
            Assert.AreEqual(100, System.Convert.ToInt64(v), "除零保护：保持原值");
        }

        [Test]
        public void SetVariable_StringAdd_Concatenates()
        {
            var provider = new InMemoryVariableProvider(new[] { GraphFactory.Var("name", "名字", VariableType.String, "勇者") });
            var g = new RuntimeStoryGraph
            {
                meta = new StoryMeta { storyId = "t" },
                nodes = new List<StoryNodeData> { GraphFactory.Node<StartNodeData>("s"), GraphFactory.SetVar("set", "name", AssignOp.Add, "·改"), GraphFactory.End("e") },
                edges = new List<StoryEdge> { GraphFactory.Edge("s", "out", "set"), GraphFactory.Edge("set", "out", "e") },
            };
            new StoryPlayer(g, provider, new StoryEventBus()).Start();

            provider.TryGetValue("name", out var v);
            Assert.AreEqual("勇者·改", (string)v);
        }

        // ── 事件挂起（协程式流程控制点）──────────────────────

        [Test]
        public void EventNode_SuspendsUntilOnComplete()
        {
            var handler = new CapturingEventHandler();
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    new EventNodeData { id = "evt", eventName = "confirm:battle_start", eventPayload = "{\"enemy\":\"slime\"}" },
                    GraphFactory.Dialogue("after", StoryConstants.NarrationId, "战斗后"),
                },
                edges: new[] { GraphFactory.Edge("s", "out", "evt"), GraphFactory.Edge("evt", "out", "after") },
                events: handler);

            var lines = new List<StoryPlayer.Line>();
            player.OnLine += lines.Add;

            player.Start();
            Assert.IsTrue(player.IsWaiting, "事件节点应挂起等待业务回调");
            Assert.AreEqual(0, lines.Count);
            Assert.AreEqual(1, handler.Raised.Count);
            Assert.AreEqual("confirm:battle_start", handler.Raised[0].name);
            StringAssert.Contains("slime", handler.Raised[0].payload);

            handler.onComplete.Invoke(); // 业务完成 → 剧情续走
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("战斗后", lines[0].Text);
            Assert.IsTrue(player.IsWaiting);
        }

        [Test]
        public void DialogueVoiceKey_DispatchedAsTransientEvent()
        {
            var handler = new CapturingEventHandler();
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), new DialogueNodeData { id = "d", speakerId = StoryConstants.NarrationId, text = "配音句", voiceKey = "v_boss_1" }, GraphFactory.End("e") },
                edges: new[] { GraphFactory.Edge("s", "out", "d"), GraphFactory.Edge("d", "out", "e") },
                events: handler);

            player.Start();
            Assert.IsTrue(handler.Raised.Any(r => r.name == "voice:v_boss_1"), "节点级语音 key 应经事件处理器派发（瞬时型）");
            Assert.IsNull(handler.onComplete, "瞬时型派发不应挂起流程");
            Assert.IsTrue(player.IsWaiting, "语音派发不改变对白等待态");
        }

        // ── 错误路径（不崩溃，明确报错）─────────────────────

        [Test]
        public void NoEntryNode_RaisesError()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Dialogue("d", StoryConstants.NarrationId, "无入口"), GraphFactory.End("e") },
                edges: new[] { GraphFactory.Edge("d", "out", "e") });

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("入口", errors[0]);
        }

        [Test]
        public void DeadEnd_AdvanceRaisesError()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.Dialogue("d", StoryConstants.NarrationId, "没有出边") },
                edges: new[] { GraphFactory.Edge("s", "out", "d") });

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();
            player.Advance();
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("未找到出口", errors[0]);
        }

        [Test]
        public void NonExecutableNode_RaisesError()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), new CommentNodeData { id = "cm", note = "批注" } },
                edges: new[] { GraphFactory.Edge("s", "out", "cm") });

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("不可执行", errors[0]);
        }

        [Test]
        public void EdgeToMissingNode_RaisesError()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.End("e") },
                edges: new[] { GraphFactory.Edge("s", "out", "ghost") });

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("为空", errors[0]);
        }

        [Test]
        public void InfiniteLoop_StepGuardFuses()
        {
            // start → cond(true/false 都指回 start)：无等待节点的纯环路，步数守卫应在 10000 步熔断。
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.Cond("cond", "hp", CompareOp.Greater, "0") },
                edges: new[] { GraphFactory.Edge("s", "out", "cond"), GraphFactory.Edge("cond", "true", "s"), GraphFactory.Edge("cond", "false", "s") },
                variables: new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("步数超限", errors[0]);
            Assert.IsFalse(player.IsRunning);
        }

        // ── 快照捕获与恢复（断点续玩）───────────────────────

        [Test]
        public void CaptureAndRestore_SameGraph_RePresentsCurrentLine()
        {
            var vars = new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") };
            StoryPlayer MakePlayer() => GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "断点句"), GraphFactory.End("e") },
                edges: new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "e") },
                variables: vars);

            var p1 = MakePlayer();
            p1.Start();
            var snap = p1.CaptureState();
            Assert.AreEqual("d1", snap.currentNodeId);
            Assert.AreEqual("test_graph", snap.graphId);
            Assert.AreEqual(1, snap.variables.Count);
            Assert.AreEqual("100", snap.variables[0].raw);
            Assert.AreEqual("1", snap.version);

            // 换一个全新播放器 + 全新变量实例，恢复后应重抛当前对白、变量还原。
            var p2 = MakePlayer();
            var lines = new List<StoryPlayer.Line>();
            p2.OnLine += lines.Add;
            p2.Restore(snap);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("断点句", lines[0].Text);
            Assert.IsTrue(p2.IsWaiting);
            p2.Advance();
            Assert.IsFalse(p2.IsRunning, "恢复后继续推进应能到 End");
        }

        [Test]
        public void Restore_MissingNode_RaisesError()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.End("e") },
                edges: new[] { GraphFactory.Edge("s", "out", "e") });
            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Restore(new StorySnapshot { currentNodeId = "ghost", graphId = "test_graph" });
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("不存在", errors[0]);
        }

        // ── JumpChapter 章节跳转与跨图恢复 ──────────────────

        [Test]
        public void JumpChapter_SwitchesGraph_AndContinuesWithVariables()
        {
            var assetB = GraphFactory.ToAsset(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("sB"), GraphFactory.Dialogue("dB", StoryConstants.NarrationId, "第二章内容"), GraphFactory.End("eB") },
                edges: new[] { GraphFactory.Edge("sB", "out", "dB"), GraphFactory.Edge("dB", "out", "eB") },
                storyId: "graph_b");

            var provider = new InMemoryVariableProvider(new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });
            var g = new RuntimeStoryGraph
            {
                meta = new StoryMeta { storyId = "graph_a" },
                nodes = new List<StoryNodeData> { GraphFactory.Node<StartNodeData>("sA"), GraphFactory.End("jump", EndType.JumpChapter, "graph_b") },
                edges = new List<StoryEdge> { GraphFactory.Edge("sA", "out", "jump") },
            };
            var player = new StoryPlayer(g, provider, new StoryEventBus(), null, key => key == "graph_b" ? assetB : null);

            var lines = new List<StoryPlayer.Line>();
            var chapterChanged = new List<StoryGraphAsset>();
            player.OnLine += lines.Add;
            player.OnChapterChanged += chapterChanged.Add;

            player.Start();
            Assert.AreEqual(1, chapterChanged.Count, "应触发章节切换事件");
            Assert.AreSame(assetB, chapterChanged[0]);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("第二章内容", lines[0].Text, "跳章后应从新图入口继续播放");
            provider.TryGetValue("hp", out var hp);
            Assert.AreEqual(100, (int)hp, "变量黑板跨图保留");
        }

        [Test]
        public void JumpChapter_WithoutResolver_RaisesError()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.End("jump", EndType.JumpChapter, "graph_b") },
                edges: new[] { GraphFactory.Edge("s", "out", "jump") });

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("图加载器", errors[0]);
        }

        [Test]
        public void JumpChapter_MissingTargetGraph_RaisesError()
        {
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.End("jump", EndType.JumpChapter, "graph_b") },
                edges: new[] { GraphFactory.Edge("s", "out", "jump") },
                graphResolver: _ => null);

            var errors = new List<string>();
            player.OnError += errors.Add;
            player.Start();
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("找不到目标图", errors[0]);
        }

        [Test]
        public void Restore_CrossGraph_SwitchesBackToSnapshotGraph()
        {
            var assetB = GraphFactory.ToAsset(
                nodes: new StoryNodeData[] { GraphFactory.Node<StartNodeData>("sB"), GraphFactory.Dialogue("dB", StoryConstants.NarrationId, "B图断点"), GraphFactory.End("eB") },
                edges: new[] { GraphFactory.Edge("sB", "out", "dB"), GraphFactory.Edge("dB", "out", "eB") },
                storyId: "graph_b");

            // 播放器 A 图 + 解析器可回 B 图；存档记录在 B 图的 dB 节点 → 恢复时应先切图再续播。
            var provider = new InMemoryVariableProvider(new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "50") });
            var g = new RuntimeStoryGraph
            {
                meta = new StoryMeta { storyId = "graph_a" },
                nodes = new List<StoryNodeData> { GraphFactory.Node<StartNodeData>("sA"), GraphFactory.End("eA") },
                edges = new List<StoryEdge> { GraphFactory.Edge("sA", "out", "eA") },
            };
            var player = new StoryPlayer(g, provider, new StoryEventBus(), null, key => key == "graph_b" ? assetB : null);

            var lines = new List<StoryPlayer.Line>();
            player.OnLine += lines.Add;
            player.Restore(new StorySnapshot
            {
                version = "1",
                graphId = "graph_b",
                currentNodeId = "dB",
                variables = new List<StorySnapshot.VarEntry> { new StorySnapshot.VarEntry { id = "hp", type = VariableType.Int, raw = "50" } },
            });
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("B图断点", lines[0].Text);
            provider.TryGetValue("hp", out var hp);
            Assert.AreEqual(50, (int)hp);
        }

        // ── 呈现数据（讲述者/外观提示/本地化）───────────────

        [Test]
        public void PresentLine_UsesInstanceCharacterResolver_NotGlobalStatic()
        {
            // 7.3-1 回归：播放器应消费构造时注入的实例级角色解析器，而非全局静态。
            var globalBefore = StoryConstants.CharacterViewModelResolver;
            try
            {
                StoryConstants.CharacterViewModelResolver = id => new StoryConstants.CharacterViewModel { displayName = "全局错误值", isValid = true };
                var player = new StoryPlayer(
                    new RuntimeStoryGraph
                    {
                        meta = new StoryMeta { storyId = "t" },
                        nodes = new List<StoryNodeData> { GraphFactory.Node<StartNodeData>("s"), GraphFactory.Dialogue("d", "hero", "台词"), GraphFactory.End("e") },
                        edges = new List<StoryEdge> { GraphFactory.Edge("s", "out", "d"), GraphFactory.Edge("d", "out", "e") },
                    },
                    new InMemoryVariableProvider(null), new StoryEventBus(), null, null,
                    characterResolver: new StubCharacterResolver("实例甲").Resolve);

                var lines = new List<StoryPlayer.Line>();
                player.OnLine += lines.Add;
                player.Start();
                Assert.AreEqual("实例甲", lines[0].SpeakerName, "应使用实例级解析器");
                Assert.AreEqual("全局错误值", StoryConstants.CharacterViewModelResolver("hero").displayName, "全局静态不应被播放器改写");
            }
            finally
            {
                StoryConstants.CharacterViewModelResolver = globalBefore;
            }
        }
    }
}
