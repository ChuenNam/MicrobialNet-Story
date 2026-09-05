using System.Collections.Generic;
using System.Reflection;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools.Inspector;
using MicrobialNet.Story.UI;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 对话框生成策略测试（纯决策逻辑，不依赖管理器实例）：
    /// 静态回退链、矩形随机落点边界、级联随机的锚点半径/层级递增/保留语义。
    /// </summary>
    public class SpawnStrategyTests
    {
        private static T SetPrivate<T, TValue>(T obj, string field, TValue value) where T : Object
        {
            var f = typeof(T).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"私有字段 {field} 应存在（策略 Inspector 配置项）");
            f.SetValue(obj, value);
            return obj;
        }

        private static DialogueBoxSpawnContext NewContext(DialogueBoxSpec spec = null, int totalActive = 0)
            => new DialogueBoxSpawnContext
            {
                styleKey = "story-line",
                spec = spec ?? DialogueBoxSpec.Create("story-line"),
                payload = null,
                activeBoxes = new List<DialogueBox>(),
                totalActive = totalActive,
            };

        // ── StaticSpawnStrategy ──────────────────────────────

        [Test]
        public void Static_UsesConfiguredPosition_WhenSet()
        {
            var strategy = ScriptableObject.CreateInstance<StaticSpawnStrategy>();
            var configured = DialogueBoxPosition.TopRight();
            configured.offset = new Vector2(123, -45);
            SetPrivate(strategy, "position", configured);

            var r = strategy.Resolve(NewContext());
            Assert.AreSame(configured, r.position, "配置了 position 则原样返回");
        }

        [Test]
        public void Static_FallsBackToSpecPosition_ThenBottomCenter()
        {
            var strategy = ScriptableObject.CreateInstance<StaticSpawnStrategy>();
            var specPos = DialogueBoxPosition.TopCenter();
            var spec = DialogueBoxSpec.Create("story-line");
            spec.position = specPos;

            // ① 资产未配置 → 用 spec.position。
            Assert.AreSame(specPos, strategy.Resolve(NewContext(spec)).position);

            // ② spec 也没有 → 底部居中兜底。
            var spec2 = DialogueBoxSpec.Create("story-line");
            spec2.position = null;
            var r = strategy.Resolve(NewContext(spec2));
            Assert.IsNotNull(r.position);
            Assert.AreEqual(DialogueBoxPositionMode.ScreenAnchor, r.position.mode);
            Assert.AreEqual(TextAnchor.LowerCenter, r.position.anchor);
        }

        // ── RandomRectSpawnStrategy ─────────────────────────

        [Test]
        public void RandomRect_OffsetStaysWithinNormalizedRange()
        {
            var strategy = ScriptableObject.CreateInstance<RandomRectSpawnStrategy>();
            SetPrivate(strategy, "rangeNormalized", new Rect(0.25f, 0.25f, 0.5f, 0.5f));

            float w = Screen.width;
            float h = Screen.height;
            float minX = (0.25f - 0.5f) * w, maxX = (0.75f - 0.5f) * w;
            float minY = (0.25f - 0.5f) * h, maxY = (0.75f - 0.5f) * h;

            for (int i = 0; i < 50; i++)
            {
                var r = strategy.Resolve(NewContext());
                Assert.IsNotNull(r.position);
                Assert.AreEqual(DialogueBoxPositionMode.ScreenAnchor, r.position.mode);
                Assert.GreaterOrEqual(r.position.offset.x, minX - 0.01f, "随机落点应在矩形内（分辨率无关）");
                Assert.LessOrEqual(r.position.offset.x, maxX + 0.01f);
                Assert.GreaterOrEqual(r.position.offset.y, minY - 0.01f);
                Assert.LessOrEqual(r.position.offset.y, maxY + 0.01f);
            }
        }

        // ── CascadeRandomSpawnStrategy ──────────────────────

        [Test]
        public void Cascade_DefaultKeepsBoxPersistent_AndLayerScalesWithActiveCount()
        {
            var strategy = ScriptableObject.CreateInstance<CascadeRandomSpawnStrategy>();
            SetPrivate(strategy, "clampToScreen", false);
            SetPrivate(strategy, "radiusMin", 40f);
            SetPrivate(strategy, "radiusMax", 160f);
            SetPrivate(strategy, "layerStep", 2);

            var r = strategy.Resolve(NewContext(totalActive: 3));
            Assert.IsTrue(r.persistent.HasValue && r.persistent.Value, "closeOnAdvance 默认 false → 保留（级联串契约）");
            Assert.AreEqual(3 * 2, r.layerOverride.Value, "层级 = 活动数 × 步长（天然最上层）");
            Assert.IsNotNull(r.position);
        }

        [Test]
        public void Cascade_CloseOnAdvance_DisablesPersistence()
        {
            var strategy = ScriptableObject.CreateInstance<CascadeRandomSpawnStrategy>();
            SetPrivate(strategy, "clampToScreen", false);
            SetPrivate(strategy, "closeOnAdvance", true);

            var r = strategy.Resolve(NewContext());
            Assert.IsTrue(r.persistent.HasValue && !r.persistent.Value, "closeOnAdvance=true → 点击继续即关闭");
        }

        [Test]
        public void Cascade_FirstBox_AnchorsAtOriginOffset_WithinRadius()
        {
            var strategy = ScriptableObject.CreateInstance<CascadeRandomSpawnStrategy>();
            SetPrivate(strategy, "clampToScreen", false);
            SetPrivate(strategy, "originOffset", new Vector2(10, 20));
            SetPrivate(strategy, "radiusMin", 50f);
            SetPrivate(strategy, "radiusMax", 80f);

            for (int i = 0; i < 30; i++)
            {
                var r = strategy.Resolve(NewContext()); // 无历史框 → 锚点 = originOffset
                var off = r.position.offset;
                float dist = Vector2.Distance(off, new Vector2(10, 20));
                Assert.GreaterOrEqual(dist, 49.9f, "偏移半径下界");
                Assert.LessOrEqual(dist, 80.1f, "偏移半径上界");
            }
        }

        [Test]
        public void Cascade_ClampToScreen_KeepsOffsetInsideMargin()
        {
            var strategy = ScriptableObject.CreateInstance<CascadeRandomSpawnStrategy>();
            SetPrivate(strategy, "clampToScreen", true);
            SetPrivate(strategy, "screenMargin", 40f);
            SetPrivate(strategy, "radiusMin", 5000f);
            SetPrivate(strategy, "radiusMax", 5000f); // 超大半径必然越界 → 验证夹取

            var r = strategy.Resolve(NewContext());
            float halfW = Screen.width * 0.5f - 40f;
            float halfH = Screen.height * 0.5f - 40f;
            Assert.LessOrEqual(Mathf.Abs(r.position.offset.x), Mathf.Max(0f, halfW) + 0.01f, "横向夹在屏内（留边距）");
            Assert.LessOrEqual(Mathf.Abs(r.position.offset.y), Mathf.Max(0f, halfH) + 0.01f, "纵向夹在屏内（留边距）");
        }

        // ── 键空间判定（编辑器策略下拉的资产枚举口径）──────────

        /// <summary>策略下拉的键空间判定须覆盖策略资产的全部合法物理布局：
        /// 包约定位置、Resources/Story 摆放偏差（运行时靠 LoadAll 按名搜索命中）、迁移后的 AddressableStory 侧。
        /// 旧过滤 Contains("/Resources/StorySpawnStrategies/") 对后两者失明（回归锚点）。</summary>
        [Test]
        public void StrategyKeySpace_MatchesAllLegalLayouts()
        {
            Assert.IsTrue(FieldWidgetFactory.IsInStrategyKeySpace("Assets/Resources/StorySpawnStrategies/StaticSpawnStrategy.asset"), "包约定位置（Resources 根）");
            Assert.IsTrue(FieldWidgetFactory.IsInStrategyKeySpace("Assets/Resources/Story/StorySpawnStrategies/StaticSpawnStrategy.asset"), "摆放偏差（Story 子目录，运行时按名搜索可命中）");
            Assert.IsTrue(FieldWidgetFactory.IsInStrategyKeySpace("Assets/AddressableStory/StorySpawnStrategies/StaticSpawnStrategy.asset"), "迁移后（AddressableStory 侧，运行时经定位器 Addressables 通道）");
        }

        [Test]
        public void StrategyKeySpace_RejectsForeignPaths()
        {
            Assert.IsFalse(FieldWidgetFactory.IsInStrategyKeySpace("Assets/MyProject/SomeStrategy.asset"), "目录名不是键空间名");
            Assert.IsFalse(FieldWidgetFactory.IsInStrategyKeySpace("Assets/Resources/StorySpawnStrategiesBackup/Old.asset"), "仅前缀相似不算（须整段目录名匹配）");
            Assert.IsFalse(FieldWidgetFactory.IsInStrategyKeySpace(null), "空路径安全返回 false");
        }
    }
}
