using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools.Inspector;
using MicrobialNet.Story.Nodes;
using NUnit.Framework;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 反射元数据缓存测试（P4/L0）：元数据正确性（过滤/排序/特性齐全）与缓存契约（同类型重复取用同一实例），
    /// 以及 IsListOfEditable 判定——这些是 FieldDrawerRegistry 面板构建的正确性前提。
    /// </summary>
    public class FieldMetaCacheTests
    {
        [Test]
        public void DialogueNode_OnlyStoryFields_ListedInOrder()
        {
            var metas = FieldMetaCache.GetFields(typeof(DialogueNodeData));
            Assert.IsNotEmpty(metas);
            Assert.IsTrue(metas.All(m => m.HasStoryField), "无 [StoryField] 的字段（如 typingDelays）不应进入面板元数据");

            // Order 升序：讲述者(0) 必须在 样式(20) 之前。
            int speakerIdx = metas.FindIndex(m => m.Field.Name == "speakerId");
            int styleIdx = metas.FindIndex(m => m.Field.Name == "appearanceStyle");
            Assert.GreaterOrEqual(0, speakerIdx);
            Assert.Greater(styleIdx, speakerIdx, "字段按 [StoryField].Order 升序");

            Assert.IsFalse(metas.Any(m => m.Field.Name == "typingDelays"), "typingDelays 无 [StoryField]（刻意不进自动面板）");
        }

        [Test]
        public void DialogueNode_PicksUpControlAndRenderAttributes()
        {
            var metas = FieldMetaCache.GetFields(typeof(DialogueNodeData));
            Assert.IsNotNull(metas.First(m => m.Field.Name == "speakerId").CharacterPicker, "[CharacterPicker] 应进元数据");
            Assert.IsNotNull(metas.First(m => m.Field.Name == "text").MultilineText, "[MultilineText] 应进元数据");
            Assert.IsNotNull(metas.First(m => m.Field.Name == "speed").RangeSlider, "[RangeSlider] 应进元数据");
            Assert.IsNotNull(mets_First(metas, "appearanceSpawnStrategyKey").SpawnStrategyPicker, "[SpawnStrategyPicker] 应进元数据");

            // [StorySection] 分组标题挂在后续字段上：生成策略分组下的「覆盖位置」。
            var overridePos = metas.First(m => m.Field.Name == "appearanceOverridePosition");
            Assert.IsNotNull(overridePos.Section, "[StorySection] 应进元数据");
        }

        private static FieldMeta mets_First(List<FieldMeta> metas, string name) => metas.First(m => m.Field.Name == name);

        [Test]
        public void CachedList_IsSameInstanceOnRepeatCalls()
        {
            var a = FieldMetaCache.GetFields(typeof(DialogueNodeData));
            var b = FieldMetaCache.GetFields(typeof(DialogueNodeData));
            Assert.AreSame(a, b, "同类型重复取用应命中缓存（同一列表实例）");
        }

        [Test]
        public void IsListOfEditable_DetectsChoiceOptionsAndConditionClauses()
        {
            // ChoiceNodeData.options → List<ChoiceOption>（元素含 [StoryField] 成员）。
            var choiceMetas = FieldMetaCache.GetFields(typeof(ChoiceNodeData));
            var options = choiceMetas.First(m => m.Field.Name == "options");
            Assert.IsTrue(options.IsListOfEditable, "选项列表应判定为可编辑列表");
            Assert.IsNotEmpty(options.ListMembers, "列表元素成员元数据随列表字段一并构建");
            Assert.IsTrue(options.ListMembers.Any(m => m.Field.Name == "text"));

            // ConditionNodeData.clauses → List<ConditionClause>。
            var condMetas = FieldMetaCache.GetFields(typeof(ConditionNodeData));
            Assert.IsTrue(condMetas.First(m => m.Field.Name == "clauses").IsListOfEditable);

            // 静态判定入口与字段元数据判定一致。
            Assert.IsTrue(FieldMetaCache.IsListOfEditable(typeof(List<ChoiceOption>)));
            Assert.IsFalse(FieldMetaCache.IsListOfEditable(typeof(List<string>)), "元素无 [StoryField] 成员 → 非可编辑列表");
            Assert.IsFalse(FieldMetaCache.IsListOfEditable(typeof(string)));
            Assert.IsFalse(FieldMetaCache.IsListOfEditable(null));
        }

        [Test]
        public void NullType_ReturnsEmptyList()
        {
            Assert.IsEmpty(FieldMetaCache.GetFields(null));
        }

        [Test]
        public void ChoiceOption_MembersReflectPanelContract()
        {
            // 选项内嵌条件组（嵌套可编辑列表）：面板递归展开的契约。
            var members = FieldMetaCache.GetFields(typeof(ChoiceOption));
            var condGroup = members.FirstOrDefault(m => m.Field.Name == "conditionGroup");
            Assert.IsNotNull(condGroup, "ChoiceOption.conditionGroup 应有 [StoryField]");
            Assert.IsTrue(condGroup.IsListOfEditable, "条件组是嵌套可编辑列表（AddLeaf→AddField 递归前提）");
        }
    }
}
