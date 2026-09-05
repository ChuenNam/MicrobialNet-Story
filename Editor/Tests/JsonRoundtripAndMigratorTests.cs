using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.Nodes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// JSON 序列化双轨测试：Export/Import 往返（多态 $type、节点字段、Unity 对象引用转 GUID）、
    /// StoryJsonMigrator（版本迁移链 + 节点类型别名，含端到端「改名旧文件仍可导入」）。
    /// </summary>
    public class JsonRoundtripAndMigratorTests
    {
        private static (List<StoryNodeData> nodes, List<StoryEdge> edges, List<StoryVariableDef> vars) BuildSampleGraph()
        {
            var nodes = new List<StoryNodeData>
            {
                GraphFactory.Node<StartNodeData>("s"),
                GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "往返第一句"),
                GraphFactory.Choice("c1", ("a", "甲"), ("b", "乙")),
                GraphFactory.SetVar("set", "hp", AssignOp.Add, "5"),
                GraphFactory.End("e"),
            };
            var edges = new List<StoryEdge>
            {
                GraphFactory.Edge("s", "out", "d1"),
                GraphFactory.Edge("d1", "out", "c1"),
                GraphFactory.Edge("c1", "opt_a", "set"),
                GraphFactory.Edge("c1", "opt_b", "e"),
                GraphFactory.Edge("set", "out", "e"),
            };
            var vars = new List<StoryVariableDef> { GraphFactory.Var("hp", "HP", VariableType.Int, "100") };
            return (nodes, edges, vars);
        }

        // ── 基本往返 ─────────────────────────────────────────

        [Test]
        public void ExportImport_RoundtripPreservesStructureAndFields()
        {
            var (nodes, edges, vars) = BuildSampleGraph();
            var src = GraphFactory.ToAsset(nodes, edges, vars, storyId: "rt_graph");
            var json = StoryJsonExporter.Export(src);

            var dst = ScriptableObject.CreateInstance<StoryGraphAsset>();
            try
            {
                StoryJsonExporter.Import(dst, json);

                Assert.AreEqual("rt_graph", dst.meta.storyId);
                Assert.AreEqual(src.nodes.Count, dst.nodes.Count);
                Assert.AreEqual(src.edges.Count, dst.edges.Count);
                Assert.AreEqual(src.variables.Count, dst.variables.Count);

                // 多态 $type：节点类型逐个还原。
                for (int i = 0; i < src.nodes.Count; i++)
                    Assert.AreEqual(src.nodes[i].GetType(), dst.nodes[i].GetType(), $"节点 {i} 类型应经 $type 还原");

                var d1 = (DialogueNodeData)dst.nodes.First(n => n.id == "d1");
                Assert.AreEqual("往返第一句", d1.text);
                Assert.AreEqual(StoryConstants.NarrationId, d1.speakerId);

                var c1 = (ChoiceNodeData)dst.nodes.First(n => n.id == "c1");
                Assert.AreEqual(2, c1.options.Count);
                Assert.AreEqual("a", c1.options[0].optionId, "选项稳定 id 往返保持");

                Assert.IsTrue(dst.edges.Any(e => e.fromNodeId == "c1" && e.fromPortId == "opt_a" && e.toNodeId == "set"));
                Assert.AreEqual("hp", dst.variables[0].id);
            }
            finally
            {
                Undo.ClearUndo(dst);
            }
        }

        // ── JSON 备份通道完整性（P9：编辑态剥离在玩家构建，不在 JSON）──

        /// <summary>JSON 是备份/交换通道：**完整保留**编辑器布局态（position/groups/stickyNotes）且往返还原。
        /// 玩家包体的编辑态剥离由数据模型的 #if UNITY_EDITOR 条件字段实现（玩家构建中字段不存在），
        /// 与 JSON 备份无关——两个通道职责分离的契约固化。</summary>
        [Test]
        public void Export_BackupChannel_PreservesEditorLayoutState()
        {
            var (nodes, edges, vars) = BuildSampleGraph();
            // 布局态非默认值：非零坐标 + 一组分组 + 一条便签。
            ((DialogueNodeData)nodes.First(n => n.id == "d1")).position = new Vector2(123f, 456f);
            var src = GraphFactory.ToAsset(nodes, edges, vars, storyId: "backup_graph");
            src.groups = new List<StoryGroup> { new StoryGroup { id = "g1", title = "内部评审分组" } };
            src.stickyNotes = new List<StoryStickyNote> { new StoryStickyNote { id = "n1", title = "备注", text = "TODO：这段要改" } };

            var json = StoryJsonExporter.Export(src);

            // 备份完整性：三处编辑态全部在场。
            StringAssert.Contains("\"position\"", json, "备份导出应含节点画布坐标");
            StringAssert.Contains("\"groups\"", json, "备份导出应含分组");
            StringAssert.Contains("\"stickyNotes\"", json, "备份导出应含便签");
            StringAssert.Contains("TODO：这段要改", json, "备份导出应含便签内容");

            // 往返还原：坐标/分组/便签无损（备份可恢复）。
            var dst = ScriptableObject.CreateInstance<StoryGraphAsset>();
            try
            {
                StoryJsonExporter.Import(dst, json);
                var d1 = (DialogueNodeData)dst.nodes.First(n => n.id == "d1");
                Assert.AreEqual(123f, d1.position.x, 0.001f, "备份往返应还原节点坐标");
                Assert.AreEqual(456f, d1.position.y, 0.001f);
                Assert.AreEqual(1, dst.groups.Count, "备份往返应还原分组");
                Assert.AreEqual("内部评审分组", dst.groups[0].title);
                Assert.AreEqual(1, dst.stickyNotes.Count, "备份往返应还原便签");
                Assert.AreEqual("TODO：这段要改", dst.stickyNotes[0].text);
            }
            finally
            {
                Undo.ClearUndo(dst);
            }
        }

        // ── UnityObjectRefConverter（对象引用 → GUID）────────

        private const string TempDir = "Assets/StoryJsonTestTmp";

        [Test]
        public void ExportImport_StyleAssetReference_RoundtripsViaGuid()
        {
            StoryGraphAsset src = null, dst = null;
            GameObject tmpl = null;
            try
            {
                if (!AssetDatabase.IsValidFolder(TempDir))
                    AssetDatabase.CreateFolder("Assets", "StoryJsonTestTmp");

                // 模板 Prefab + 样式资产（获得真实 GUID）。
                tmpl = new GameObject("Tmpl");
                var prefab = PrefabUtility.SaveAsPrefabAsset(tmpl, $"{TempDir}/tmpl.prefab");
                var style = ScriptableObject.CreateInstance<DialogueBoxStyleAsset>();
                style.styleKey = "test-style";
                style.template = prefab;
                AssetDatabase.CreateAsset(style, $"{TempDir}/style.asset");
                AssetDatabase.SaveAssets();

                var (nodes, edges, _) = BuildSampleGraph();
                ((DialogueNodeData)nodes.First(n => n.id == "d1")).appearanceStyle = style;
                src = GraphFactory.ToAsset(nodes, edges);
                var json = StoryJsonExporter.Export(src);

                // GUID 形式落盘（而非下钻 GameObject 的废弃属性——那会抛 NotSupportedException）。
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(style));
                StringAssert.Contains(guid, json, "Unity 对象引用应序列化为资产 GUID");

                dst = ScriptableObject.CreateInstance<StoryGraphAsset>();
                StoryJsonExporter.Import(dst, json);
                var d1 = (DialogueNodeData)dst.nodes.First(n => n.id == "d1");
                Assert.AreSame(style, d1.appearanceStyle, "反序列化按 GUID 还原同一资产引用");
            }
            finally
            {
                if (src != null) Undo.ClearUndo(src);
                if (dst != null) Undo.ClearUndo(dst);
                if (tmpl != null) Object.DestroyImmediate(tmpl);
                if (AssetDatabase.IsValidFolder(TempDir))
                {
                    AssetDatabase.DeleteAsset(TempDir);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }
        }

        // ── StoryJsonMigrator ────────────────────────────────

        [Test]
        public void Migrate_NoRegistrations_Passthrough()
        {
            // 未注册任何别名/步骤时零开销直通；一旦其它用例注册（迁移器设计为进程级常驻、不可注销）则退避。
            if (StoryJsonMigrator.HasRegistrations) Assert.Inconclusive("迁移器已有注册项，跳过直通断言");
            var json = "{\"version\":\"1.0\",\"nodes\":[]}";
            Assert.AreEqual(json, StoryJsonMigrator.Migrate(json, "1.0"));
        }

        [Test]
        public void Migrate_TypeAlias_RewritesDollarType()
        {
            StoryJsonMigrator.RegisterTypeAlias(
                "MicrobialNet.Story.Tests.FakeOldNode",
                "MicrobialNet.Story.Tests.FakeNewNode");

            var json = "{\"nodes\":[{\"$type\":\"MicrobialNet.Story.Tests.FakeOldNode, com.microbialnet.story.Tests\",\"id\":\"n1\"}]}";
            var migrated = StoryJsonMigrator.Migrate(json, "1.0");
            StringAssert.DoesNotContain("FakeOldNode", migrated);
            StringAssert.Contains("MicrobialNet.Story.Tests.FakeNewNode, com.microbialnet.story.Tests", migrated, "别名改写保留程序集名");
        }

        [Test]
        public void Migrate_VersionChain_AppliesStepsInOrder()
        {
            var step2 = new TestMigrator("0.9", "1.0", root => { root["version"] = "1.0"; root["step"] = "last"; });
            var step1 = new TestMigrator("0.8", "0.9", root => { root["version"] = "0.9"; root["step"] = "first"; });
            StoryJsonMigrator.RegisterStep(step1);
            StoryJsonMigrator.RegisterStep(step2);

            var json = "{\"version\":\"0.8\",\"nodes\":[]}";
            var migrated = StoryJsonMigrator.Migrate(json, "1.0");
            var root = JObject.Parse(migrated);
            Assert.AreEqual("1.0", (string)root["version"], "链式升级到目标版本");
            Assert.AreEqual("last", (string)root["step"], "多步按版本顺序衔接，后执行者覆盖");
        }

        [Test]
        public void Migrate_UnknownVersion_PassthroughWithNoStep()
        {
            var json = "{\"version\":\"0.5\",\"keep\":\"me\"}";
            // 防御性告警本身是被测行为的一部分：声明期望后 LogAssert 消费该条日志（控制台不再出现裸告警）。
            LogAssert.Expect(LogType.Warning, "[StoryJsonMigrator] 未注册从版本「0.5」到「1.0」的迁移步骤，按当前格式直接尝试解析。");
            var migrated = StoryJsonMigrator.Migrate(json, "1.0");
            var root = JObject.Parse(migrated);
            Assert.AreEqual("0.5", (string)root["version"], "无迁移步骤时按当前格式直通（交由上层报明确错误）");
            Assert.AreEqual("me", (string)root["keep"]);
        }

        [Test]
        public void Migrate_MissingVersion_SkipsChain_ButAppliesAliases()
        {
            // 自注册独占别名，避免与其它用例的执行顺序耦合（注册表为进程级常驻）。
            StoryJsonMigrator.RegisterTypeAlias(
                "MicrobialNet.Story.Tests.MvOldNode",
                "MicrobialNet.Story.Tests.MvNewNode");
            var json = "{\"nodes\":[{\"$type\":\"MicrobialNet.Story.Tests.MvOldNode, asm\",\"id\":\"n\"}]}";
            var migrated = StoryJsonMigrator.Migrate(json, "1.0");
            StringAssert.Contains("MvNewNode", migrated, "缺 version 跳过版本链，别名仍生效");
        }

        [Test]
        public void Import_RenamedNodeType_EndToEndViaAlias()
        {
            // 端到端：导出 → 手工把 $type 改成「旧类名」 → 注册别名 → 导入成功且类型正确。
            var (nodes, edges, _) = BuildSampleGraph();
            var src = GraphFactory.ToAsset(nodes, edges);
            var json = StoryJsonExporter.Export(src);

            const string oldFull = "MicrobialNet.Story.Nodes.DialogueNodeData";
            const string legacyFull = "MicrobialNet.Story.Nodes.LegacyDialogueNode";
            var legacyJson = json.Replace(oldFull, legacyFull);

            StoryJsonMigrator.RegisterTypeAlias(legacyFull, oldFull);

            var dst = ScriptableObject.CreateInstance<StoryGraphAsset>();
            try
            {
                Assert.DoesNotThrow(() => StoryJsonExporter.Import(dst, legacyJson));
                Assert.AreEqual(src.nodes.Count, dst.nodes.Count);
                Assert.IsInstanceOf<DialogueNodeData>(dst.nodes.First(n => n.id == "d1"), "经别名还原为正确节点类型");
            }
            finally
            {
                Undo.ClearUndo(dst);
                Undo.ClearUndo(src);
            }
        }

        private sealed class TestMigrator : ISerializationMigrator
        {
            private readonly string _from;
            private readonly string _to;
            private readonly System.Action<JObject> _apply;
            public TestMigrator(string from, string to, System.Action<JObject> apply)
            {
                _from = from; _to = to; _apply = apply;
            }
            public string FromVersion => _from;
            public string ToVersion => _to;
            public void Apply(JObject root) => _apply(root);
        }
    }
}
