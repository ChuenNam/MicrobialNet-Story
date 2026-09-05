using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>核心支撑测试：节点类型注册表（反射扫描/零注册扩展）、图模型脏通知（TouchData）、图集合（跳章解析器）、资产定位器默认实现、内存变量提供者。</summary>
    public class CoreSupportTests
    {
        // ── NodeRegistry ─────────────────────────────────────

        [Test]
        public void Registry_ContainsAllBuiltInNodeTypes()
        {
            var types = NodeRegistry.Entries.Select(e => e.Type).ToList();
            Assert.IsTrue(types.Contains(typeof(StartNodeData)));
            Assert.IsTrue(types.Contains(typeof(DialogueNodeData)));
            Assert.IsTrue(types.Contains(typeof(ChoiceNodeData)));
            Assert.IsTrue(types.Contains(typeof(ConditionNodeData)));
            Assert.IsTrue(types.Contains(typeof(SetVariableNodeData)));
            Assert.IsTrue(types.Contains(typeof(EventNodeData)));
            Assert.IsTrue(types.Contains(typeof(EndNodeData)));
            Assert.IsTrue(types.Contains(typeof(StoryTableNodeData)));
            Assert.IsTrue(types.Contains(typeof(CommentNodeData)));
        }

        [Test]
        public void Registry_AttributesProvideMenuMetadata()
        {
            var attr = NodeRegistry.GetAttr(typeof(DialogueNodeData));
            Assert.IsNotNull(attr);
            StringAssert.AreEqualIgnoringCase("对话", attr.Title);
            Assert.IsNotEmpty(attr.Category);
            Assert.IsFalse(string.IsNullOrEmpty(attr.ColorHex));
        }

        [Test]
        public void Registry_Create_AssignsFreshGuidIds()
        {
            var n1 = NodeRegistry.Create(typeof(DialogueNodeData));
            var n2 = NodeRegistry.Create(typeof(DialogueNodeData));
            Assert.IsInstanceOf<DialogueNodeData>(n1);
            Assert.AreNotEqual(n1.id, n2.id);
            Assert.AreEqual(32, n1.id.Length, "Guid N 格式（32 位十六进制）");

            var byName = NodeRegistry.Create("ChoiceNodeData"); // 简名
            Assert.IsInstanceOf<ChoiceNodeData>(byName);
            Assert.AreEqual(32, byName.id.Length);
        }

        [Test]
        public void Registry_ByCategory_Filters()
        {
            Assert.IsNotEmpty(NodeRegistry.ByCategory("基础"));
            Assert.IsNotEmpty(NodeRegistry.ByCategory("逻辑"));
            Assert.IsEmpty(NodeRegistry.ByCategory("不存在的分类"));
        }

        // ── StoryGraphModel 脏通知（数值字段原生绑定路径的未保存感知）──

        [Test]
        public void TouchData_SetsDirtyAndBroadcastsFieldChanged()
        {
            // 契约：数值滑块走原生序列化绑定（绕过命令管线）→ 值变化时经 TouchData
            // 置脏 + 广播 FieldChanged，窗口据此显示「未保存*」并启用关闭/切换确认。
            var asset = GraphFactory.ToAsset(
                new StoryNodeData[] { GraphFactory.Node<StartNodeData>("s"), GraphFactory.End("e") },
                new[] { GraphFactory.Edge("s", "out", "e") });
            using (var model = new StoryGraphModel(asset))
            {
                Assert.IsFalse(model.IsDirty);
                var changes = new List<GraphChange>();
                model.Changed += c => changes.Add(c);

                model.TouchData();
                Assert.IsTrue(model.IsDirty, "TouchData 应置脏");
                Assert.AreEqual(1, changes.Count, "TouchData 应广播一次变更");
                Assert.AreEqual(GraphChangeType.FieldChanged, changes[0].Type, "语义 = 字段数据变更（驱动状态栏/未保存确认）");

                model.MarkSaved();
                Assert.IsFalse(model.IsDirty, "MarkSaved 后脏标记清零");
            }
        }

        // ── StoryGraphCollection ────────────────────────────

        [Test]
        public void GraphCollection_RegisterAndResolve()
        {
            var c = new StoryGraphCollection();
            var a = GraphFactory.NewAsset("g1");
            var b = GraphFactory.NewAsset("g2");

            c.Add("chapter1", a);
            c.Add("story_id_2", b);

            Assert.AreEqual(2, c.Count);
            Assert.AreSame(a, c.Resolve("chapter1"));
            Assert.AreSame(b, c.Resolve("story_id_2"));
            Assert.IsNull(c.Resolve("missing"), "找不到返回 null（触发 JumpChapter 明确错误）");
            Assert.IsTrue(c.TryGet("chapter1", out var got) && got == a);
            Assert.IsFalse(c.TryGet("missing", out _));
        }

        [Test]
        public void GraphCollection_IgnoresInvalidEntries()
        {
            var c = new StoryGraphCollection();
            c.Add(null, GraphFactory.NewAsset());
            c.Add("", GraphFactory.NewAsset());
            c.Add("k", null);
            Assert.AreEqual(0, c.Count);
        }

        // ── IStoryAssetLocator（Resources 默认实现）─────────

        [Test]
        public void Locator_DefaultIsResources_AndMissingLoadReturnsNull()
        {
            Assert.IsNotNull(StoryAssetLocator.Current);
            Assert.IsInstanceOf<ResourcesStoryAssetLocator>(StoryAssetLocator.Current);

            Assert.IsNull(StoryAssetLocator.Current.LoadAsset<GameObject>("Story/肯定不存在的路径/none"));
            var all = StoryAssetLocator.Current.LoadAllAssets<DialogueBoxStyleAsset>("Story/肯定不存在的目录");
            Assert.IsNotNull(all);
            Assert.AreEqual(0, all.Length, "目录缺失返回空数组而非 null");
        }

        [Test]
        public void Locator_CanBeReplacedAndReset()
        {
            var replaced = new FakeLocator();
            var before = StoryAssetLocator.Current;
            try
            {
                StoryAssetLocator.Current = replaced;
                Assert.AreSame(replaced, StoryAssetLocator.Current);

                StoryAssetLocator.Current = null; // 置 null 回落 Resources 默认
                Assert.IsInstanceOf<ResourcesStoryAssetLocator>(StoryAssetLocator.Current);
            }
            finally
            {
                StoryAssetLocator.Current = before; // 还原测试前状态（不污染其它用例）
            }
        }

        private sealed class FakeLocator : IStoryAssetLocator
        {
            public T LoadAsset<T>(string path) where T : Object => null;
            public T[] LoadAllAssets<T>(string path) where T : Object => System.Array.Empty<T>();
            public System.Threading.Tasks.Task<T> LoadAssetAsync<T>(string path) where T : Object
                => System.Threading.Tasks.Task.FromResult<T>(null);
            public System.Threading.Tasks.Task<T[]> LoadAllAssetsAsync<T>(string path) where T : Object
                => System.Threading.Tasks.Task.FromResult(System.Array.Empty<T>());
        }

        // ── InMemoryVariableProvider ────────────────────────

        [Test]
        public void InMemoryVariables_SeedParseAndScope()
        {
            var p = new InMemoryVariableProvider(
                new[]
                {
                    GraphFactory.Var("hp", "HP", VariableType.Int, "100"),
                    GraphFactory.Var("f", "系数", VariableType.Float, "1.5"),
                    GraphFactory.Var("ok", "标记", VariableType.Bool, "true"),
                    GraphFactory.Var("name", "名字", VariableType.String, "勇者"),
                },
                globalVariables: new[] { GraphFactory.Var("hp", "全局HP", VariableType.Int, "999") }); // 同名：局部优先

            Assert.AreEqual(4, p.Snapshot().Count);
            p.TryGetValue("hp", out var hp);
            Assert.AreEqual(100, (int)hp, "局部同名覆盖全局兜底");
            p.TryGetValue("f", out var f);
            Assert.AreEqual(1.5f, (float)f, 1e-6, "默认值按类型解析");
            p.TryGetValue("ok", out var ok);
            Assert.AreEqual(true, ok);
            Assert.AreEqual(VariableType.Int, p.GetVariableType("hp"));
            Assert.IsTrue(p.HasVariable("hp"));
            Assert.IsFalse(p.HasVariable("ghost"));
        }

        [Test]
        public void InMemoryVariables_SetValueCreatesAndSnapshots()
        {
            var p = new InMemoryVariableProvider(null);
            p.SetValue("dynamic", 42); // 未定义变量：实现可自行创建
            Assert.IsTrue(p.HasVariable("dynamic"));
            p.TryGetValue("dynamic", out var v);
            Assert.AreEqual(42, v);
            Assert.AreEqual(1, p.Snapshot().Count);
        }

        [Test]
        public void ValueParser_DirtyDataFallsBackToZero_NeverThrows()
        {
            Assert.AreEqual(0, (int)ValueParser.Parse("abc", VariableType.Int));
            Assert.AreEqual(0f, (float)ValueParser.Parse(null, VariableType.Float));
            Assert.AreEqual(false, ValueParser.Parse("yes?", VariableType.Bool));
            Assert.AreEqual("原文", ValueParser.Parse("原文", VariableType.String));
            Assert.AreEqual("", ValueParser.Parse(null, VariableType.String) as string);
        }
    }
}
