using System.Collections.Generic;
using System.Reflection;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// StoryFlow 门面端到端测试（EditMode，无 Play 依赖）：
    /// 内置示例图零资产播放闭环（Presenter 回环）、事件转发、Restart、FormatVariables 实例级变量名、
    /// 多 StoryFlow 并存互不覆盖角色解析器（7.3-1 回归）。
    /// </summary>
    public class StoryFlowFacadeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private StoryFlow NewFlow(out StubPresenter presenter)
        {
            var go = new GameObject("StoryFlow_UT");
            _gos.Add(go);
            var flow = go.AddComponent<StoryFlow>();
            presenter = new StubPresenter();
            flow.Configure(presenter);
            return flow;
        }

        private static void SetGraphAsset(StoryFlow flow, StoryGraphAsset asset)
        {
            // storyGraphAsset 为私有序列化字段：测试经反射注入（不为此改产品可见性）。
            typeof(StoryFlow).GetField("storyGraphAsset", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(flow, asset);
        }

        [Test]
        public void DemoGraph_ZeroAsset_PlaybackLoopThroughPresenter()
        {
            var flow = NewFlow(out var presenter);

            flow.Play(); // 未配置资产时应回退内置示例图并开播
            Assert.AreEqual(1, presenter.Lines.Count);
            StringAssert.Contains("欢迎来到示例剧情", presenter.Lines[0].Text);
            Assert.AreEqual("旁白", presenter.Lines[0].SpeakerName);
            Assert.IsTrue(flow.IsRunning);
            Assert.IsTrue(flow.IsWaiting);

            presenter.RaiseAdvance(); // 玩家点继续
            Assert.AreEqual(1, presenter.ChoicesShown.Count, "应抛出选项");
            Assert.AreEqual(2, presenter.ChoicesShown[0].Count);

            presenter.RaiseChoose("b"); // 撤退 → cond(hp=100>0) → alive
            Assert.AreEqual(2, presenter.Lines.Count);
            StringAssert.Contains("成功撤离", presenter.Lines[1].Text);

            presenter.RaiseAdvance();
            Assert.AreEqual(1, presenter.Ends.Count, "到达 End 应通知视图");
            Assert.IsFalse(flow.IsRunning);
        }

        [Test]
        public void PublicEvents_ForwardedAlongsidePresenter()
        {
            var flow = NewFlow(out _);
            var lines = new List<StoryFlow.Line>();
            var choices = new List<IReadOnlyList<StoryFlow.Choice>>();
            var nodeEnters = new List<string>();
            var ended = false;
            flow.OnLine += lines.Add;
            flow.OnChoices += choices.Add;
            flow.OnNodeEnter += nodeEnters.Add;
            flow.OnEnd += () => ended = true;

            flow.Play();
            flow.Advance();
            flow.Choose("b");
            flow.Advance();

            Assert.AreEqual(2, lines.Count, "OnLine 与视图渲染并存转发");
            Assert.AreEqual(1, choices.Count);
            Assert.IsNotEmpty(nodeEnters);
            Assert.IsTrue(nodeEnters.Contains("hello"), "OnNodeEnter 应携带节点 id");
            Assert.IsTrue(ended);
        }

        [Test]
        public void Restart_ReplaysFromEntry()
        {
            var flow = NewFlow(out var presenter);
            flow.Play();
            presenter.RaiseAdvance();
            presenter.RaiseChoose("b");
            presenter.RaiseAdvance();
            Assert.AreEqual(1, presenter.Ends.Count);

            flow.Restart();
            Assert.AreEqual(3, presenter.Lines.Count, "重播应从入口重新呈现首句");
            StringAssert.Contains("欢迎来到示例剧情", presenter.Lines[2].Text);
            Assert.IsTrue(flow.IsWaiting);
        }

        [Test]
        public void FormatVariables_UsesInstanceResolver_ShowsReadableName()
        {
            var flow = NewFlow(out _);
            flow.Play();
            var text = flow.FormatVariables();
            StringAssert.Contains("HP = 100", text, "变量应显示可读名（实例级映射，非裸 id 也非全局静态）");
        }

        [Test]
        public void ActiveLanguage_CanBeSetAtRuntime()
        {
            var flow = NewFlow(out _);
            flow.ActiveLanguage = "en-US";
            Assert.AreEqual("en-US", flow.ActiveLanguage);
        }

        [Test]
        public void MultipleFlows_InstanceResolversDoNotClobberEachOther()
        {
            // 7.3-1 回归：两个 StoryFlow 各注入不同角色解析器，讲述者名互不覆盖，全局静态不被运行时改写。
            var asset = GraphFactory.ToAsset(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d", "hero", "台词"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "d"), GraphFactory.Edge("d", "out", "e") });

            var globalBefore = StoryConstants.CharacterViewModelResolver;

            var flowA = NewFlow(out var presenterA);
            SetGraphAsset(flowA, asset);
            flowA.Configure(new StoryFlowConfig { Characters = new StubCharacterResolver("实例甲") });

            var flowB = NewFlow(out var presenterB);
            SetGraphAsset(flowB, asset);
            flowB.Configure(new StoryFlowConfig { Characters = new StubCharacterResolver("实例乙") });

            try
            {
                flowA.Play();
                flowB.Play();

                Assert.AreEqual("实例甲", presenterA.Lines[0].SpeakerName, "FlowA 使用自己的解析器");
                Assert.AreEqual("实例乙", presenterB.Lines[0].SpeakerName, "FlowB 不被 FlowA 覆盖");
                Assert.IsTrue(ReferenceEquals(StoryConstants.CharacterViewModelResolver, globalBefore),
                    "运行时不得改写全局静态解析器（多实例解耦的契约）");
            }
            finally
            {
                // 双保险还原静态（即使断言失败也不污染后续测试）。
                StoryConstants.CharacterViewModelResolver = globalBefore;
            }
        }

        [Test]
        public void Configure_WithCustomVariables_UsedByPlayback()
        {
            // 宿主注入自定义变量提供者：剧情赋值直接写进宿主对象。
            var asset = GraphFactory.ToAsset(
                new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.SetVar("set", "hp", AssignOp.Sub, "30"),
                    GraphFactory.End("e"),
                },
                new[] { GraphFactory.Edge("s", "out", "set"), GraphFactory.Edge("set", "out", "e") });

            var provider = new InMemoryVariableProvider(new[] { GraphFactory.Var("hp", "HP", VariableType.Int, "100") });
            var flow = NewFlow(out _);
            SetGraphAsset(flow, asset);
            flow.Configure(new StoryFlowConfig { Variables = provider });

            flow.Play();
            Assert.IsFalse(flow.IsRunning, "直通节点图应一路播完到 End");
            provider.TryGetValue("hp", out var hp);
            Assert.AreEqual(70, System.Convert.ToInt64(hp), "宿主变量提供者承接剧情赋值（Int 写回 long）");
        }
    }
}
