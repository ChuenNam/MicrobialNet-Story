using System.Collections.Generic;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 本地化测试：主表查询（TryGetTranslation/UpsertOriginal）、LocalizationTextProvider 语言实时切换、
    /// StoryGraphLocalizationProvider 图绑定与跳章跟随、播放器侧「未命中回退原文（绝不显示裸 key）」。
    /// </summary>
    public class LocalizationTests
    {
        private static StoryLocalizationTable NewTable(string defaultLang, params (string key, string original, string[] translations)[] entries)
        {
            var t = ScriptableObject.CreateInstance<StoryLocalizationTable>();
            t.defaultLanguage = defaultLang;
            t.languages = new List<string> { "zh-CN", "en-US" };
            foreach (var (key, original, translations) in entries)
            {
                var e = new StoryLocalizationTable.Entry { key = key, original = original, translations = new List<string>(translations) };
                t.entries.Add(e);
            }
            return t;
        }

        // ── 主表查询 ─────────────────────────────────────────

        [Test]
        public void Table_TryGetTranslation_ByLanguageIndex()
        {
            var t = NewTable("zh-CN", ("k1", "原文", new[] { "", "Hello" }));
            Assert.IsTrue(t.TryGetTranslation("k1", "en-US", out var text));
            Assert.AreEqual("Hello", text);
            Assert.IsFalse(t.TryGetTranslation("k1", "zh-CN", out _), "空译文=未翻译，应返回 false 触发回退");
            Assert.IsFalse(t.TryGetTranslation("k_missing", "en-US", out _), "key 不存在返回 false");
            Assert.IsFalse(t.TryGetTranslation("k1", "fr-FR", out _), "未注册语言返回 false");
        }

        [Test]
        public void Table_UpsertOriginal_KeepsExistingTranslations()
        {
            var t = NewTable("zh-CN", ("k1", "旧原文", new[] { "", "Hello" }));
            t.UpsertOriginal("k1", "新原文");
            Assert.AreEqual("新原文", t.GetOriginal("k1"));
            Assert.IsTrue(t.TryGetTranslation("k1", "en-US", out var text));
            Assert.AreEqual("Hello", text, "同步 original 不应丢失已有译文");

            t.UpsertOriginal("k_new", "新增");
            Assert.IsTrue(t.ContainsKey("k_new"));
            Assert.AreEqual(2, t.entries.Count);
        }

        [Test]
        public void Table_SetTranslation_CreatesEntryWhenMissing()
        {
            var t = NewTable("zh-CN");
            t.SetTranslation("k2", t.LangIndex("en-US"), "World");
            Assert.IsTrue(t.TryGetTranslation("k2", "en-US", out var text));
            Assert.AreEqual("World", text);
        }

        // ── LocalizationTextProvider ────────────────────────

        [Test]
        public void TextProvider_RealTimeLanguageSwitch()
        {
            var t = NewTable("zh-CN", ("k1", "原文", new[] { "", "Hello" }));
            string lang = "en-US";
            var p = new LocalizationTextProvider(t, () => lang);

            Assert.AreEqual("Hello", p.ResolveText("k1"), "英文命中译文");

            lang = "zh-CN"; // 运行时改语言 → 委托实时读取，无需重建 provider
            Assert.IsNull(p.ResolveText("k1"), "中文无译文 → 返回 null（播放器回退原文）");

            lang = null; // 空 → 回落 defaultLanguage（zh-CN）
            Assert.IsNull(p.ResolveText("k1"));
        }

        [Test]
        public void TextProvider_NullTable_ReturnsNull()
        {
            var p = new LocalizationTextProvider(null, () => "en-US");
            Assert.IsNull(p.ResolveText("any"));
        }

        // ── StoryGraphLocalizationProvider（图绑定 + 跳章跟随）──

        [Test]
        public void GraphProvider_ResolvesFromCurrentGraphTable()
        {
            var tableA = NewTable("en-US", ("k", "A原文", new[] { "", "A译" }));
            var graphA = GraphFactory.NewAsset("a");
            graphA.localizationTable = tableA;

            var p = new StoryGraphLocalizationProvider(graphA, () => "en-US", null);
            Assert.AreEqual("A译", p.ResolveText("k"));
        }

        [Test]
        public void GraphProvider_FallsBackToFallbackTable_WhenGraphHasNoTable()
        {
            var tableA = NewTable("en-US", ("k", "A原文", new[] { "", "A译" }));
            var graphA = GraphFactory.NewAsset("a");
            graphA.localizationTable = tableA;
            var tableF = NewTable("en-US", ("k", "F原文", new[] { "", "F译" }));
            var graphB = GraphFactory.NewAsset("b"); // 无表

            var p = new StoryGraphLocalizationProvider(graphA, () => "en-US", tableF);
            Assert.AreEqual("A译", p.ResolveText("k"), "当前图有表 → 用图自己的表");

            p.SetCurrentGraph(graphB); // 模拟 JumpChapter 后切图
            Assert.AreEqual("F译", p.ResolveText("k"), "新图无表 → 回落兜底表（不误查旧表）");
        }

        [Test]
        public void GraphProvider_EmptyLanguage_FallsBackToTableDefault()
        {
            // defaultLanguage=en-US 的表；语言委托返回空 → 用表默认语言命中的译文。
            var tableA = NewTable("en-US", ("k", "原文", new[] { "", "A译" }));
            var graphA = GraphFactory.NewAsset("a");
            graphA.localizationTable = tableA;

            var p = new StoryGraphLocalizationProvider(graphA, () => null, null);
            Assert.AreEqual("A译", p.ResolveText("k"), "语言为空回落当前图表的 defaultLanguage");
        }

        // ── 播放器侧回退（key 规则 + 未命中显示原文）────────

        [Test]
        public void Player_MissingTranslation_FallsBackToOriginalText_NeverRawKey()
        {
            // provider 只命中 d1.text；d2.text 未命中 → 应显示原文而非裸 key。
            var map = new Dictionary<string, string> { ["d1.text"] = "第一句译文" };
            var player = GraphFactory.MakePlayer(
                nodes: new StoryNodeData[]
                {
                    GraphFactory.Node<StartNodeData>("s"),
                    GraphFactory.Dialogue("d1", StoryConstants.NarrationId, "第一句"),
                    GraphFactory.Dialogue("d2", StoryConstants.NarrationId, "第二句"),
                    GraphFactory.End("e"),
                },
                edges: new[] { GraphFactory.Edge("s", "out", "d1"), GraphFactory.Edge("d1", "out", "d2"), GraphFactory.Edge("d2", "out", "e") },
                text: new StubTextProvider(map));

            var lines = new List<StoryPlayer.Line>();
            player.OnLine += lines.Add;
            player.Start();
            player.Advance();

            Assert.AreEqual("第一句译文", lines[0].Text, "命中译文（key=节点id.text）");
            Assert.AreEqual("第二句", lines[1].Text, "未命中回退原文，绝不显示裸 key");
        }
    }
}
