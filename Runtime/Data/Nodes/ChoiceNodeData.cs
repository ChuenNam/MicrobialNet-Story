using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>玩家选项：一个选项 = 一个输出端口。端口 ID 形如 "opt_{optionId}"，随选项稳定。</summary>
    [System.Serializable]
    internal sealed class ChoiceOption
    {
        /// <summary>选项稳定 ID（端口 ID 来源，重排/改名不影响连线）。</summary>
        public string optionId = System.Guid.NewGuid().ToString("N");

        /// <summary>选项文本。</summary>
        [TextArea]
        [StoryField("选项文本", Order = 0)]
        public string text = "";

        /// <summary>是否带显示条件（满足条件组才显示该选项）。</summary>
        [StoryField("带条件", Order = 1)]
        public bool hasCondition;

        /// <summary>多条件组合方式（All=全部满足 / Any=任一满足）。仅「带条件」勾选时用于条件组。</summary>
        [StoryField("组合方式", Order = 2)]
        public ConditionCombine conditionCombine = ConditionCombine.All;

        /// <summary>条件组：多个「变量 op 值」子句，按 conditionCombine 组合；全部/任一满足才显示该选项。</summary>
        [StoryField("条件组", Order = 3)]
        public List<ConditionClause> conditionGroup = new List<ConditionClause>();

        /// <summary>【兼容旧档】单条件变量 ID。新格式用 conditionGroup；本字段仅在未迁移时作单条件回退。</summary>
        [VariablePicker]
        [StoryField("条件变量", Order = 4)]
        public string conditionVariable;

        /// <summary>【兼容旧档】单条件比较运算符。</summary>
        [StoryField("比较", Order = 5)]
        public CompareOp conditionOp = CompareOp.Equal;

        /// <summary>【兼容旧档】单条件比较值。</summary>
        [StoryField("比较值", Order = 6)]
        public string conditionValue;

        /// <summary>把旧版单条件四字段一次性迁移进 conditionGroup（幂等）。迁移后清空旧字段，避免重复判定。</summary>
        public void EnsureMigrated()
        {
            if (conditionGroup == null) conditionGroup = new List<ConditionClause>();
            if (conditionGroup.Count == 0 && hasCondition && !string.IsNullOrEmpty(conditionVariable))
            {
                conditionGroup.Add(new ConditionClause
                {
                    variableId = conditionVariable,
                    op = conditionOp,
                    value = conditionValue,
                });
                conditionVariable = null;
                conditionValue = null;
                conditionOp = CompareOp.Equal;
            }
        }
    }

    /// <summary>玩家选项节点：每个可选分支一个输出端口，端口随选项列表动态生成。</summary>
    [System.Serializable]
    [StoryNode("玩家选项", ColorHex = "#EF9F27", Category = "基础", Order = 3)]
    internal sealed class ChoiceNodeData : StoryNodeData
    {
        // —— 承载文字（可选）：勾选「显示文字」后本节点先呈现一句对白（讲述者+正文）再呈现选项。
        //    用于「带分支的对白合并成一个玩家选择节点」（剧情表分支行即此形态：Build 时 showText=true）。——
        [StorySection("对话（可选）")]
        [StoryField("显示文字", Order = -10)]
        [Tooltip("勾选后本节点承载一句对白（讲述者+正文）后再呈现选项；适用于「带分支的对白」合并成一个节点。")]
        public bool showText;

        [StoryField("讲述者", Order = -9)]
        [CharacterPicker]
        public string speakerId = StoryConstants.NarrationId;

        [StoryField("正文", Order = -8)]
        [MultilineText(Lines = 3)]
        public string text = "";

        // 「显示文字」所承载的对白也走打字机（与对白节点同一套语速/打字机语义），
        // 选项在该对白打字结束后才出现。字段名 speed/typingMode 与 DialogueNodeData 一致，便于表节点统一注入。
        [StoryField("语速", Order = -7)]
        [RangeSlider(0.1f, 1f)]
        public float speed = 0.5f;

        [StoryField("打字机", Order = -6)]
        [Tooltip("全局语速=均匀间隔；标点节奏=按标点停顿。用于「显示文字」承载的对白。")]
        public TypingMode typingMode = TypingMode.GlobalSpeed;

        [StoryField("选项列表", Order = 0)]
        public List<ChoiceOption> options = new List<ChoiceOption>();

        // —— 节点级对话框外观覆盖：选项框同样可独立配置（无「保留自身」，选项选中即关闭）——
        [StorySection("对话框外观")]
        [StoryField("样式", Order = 20)]
        [Tooltip("拖入 DialogueBoxStyleAsset 资产即可让该选项框用其样式呈现；留空=用 StoryView 全局样式（选项 story-choice）。")]
        public DialogueBoxStyleAsset appearanceStyle;

        [StoryField("覆盖位置", Order = 21)]
        [Tooltip("勾选后下方「定位模式/锚点/偏移」生效；否则沿用 StoryView 全局 position。")]
        public bool appearanceOverridePosition;

        [StoryField("定位模式", Order = 22)]
        public DialogueBoxPositionMode appearancePositionMode;

        [StoryField("锚点", Order = 23)]
        public TextAnchor appearancePositionAnchor = TextAnchor.LowerCenter;

        [StoryField("偏移", Order = 24)]
        public Vector2Int appearancePositionOffset;

        [StoryField("生成策略", Order = 25)]
        [SpawnStrategyPicker]
        [Tooltip("未勾选「覆盖位置」时显示。选中后该选项框用对应生成策略决定出现位置/层级。留空=用 StoryView 全局策略。")]
        public string appearanceSpawnStrategyKey;

        public override IEnumerable<NodePort> GetInputPorts() => new[] { new NodePort { id = "in" } };

        public override IEnumerable<NodePort> GetOutputPorts()
            => options.Select(o => new NodePort
            {
                id = "opt_" + o.optionId,
                label = string.IsNullOrEmpty(o.text) ? "<选项>" : o.text,
            });

        public override string GetSummary()
        {
            var lines = new List<string>();
            if (showText)
                lines.Add($"{StoryConstants.SpeakerDisplayName(speakerId)}：{(string.IsNullOrEmpty(text) ? "<空>" : text.Replace("\n", " "))}");
            if (options.Count == 0) lines.Add("<无选项>");
            else foreach (var o in options) lines.Add("• " + (string.IsNullOrEmpty(o.text) ? "<选项>" : o.text));
            return string.Join("\n", lines);
        }

        public override string SearchSpeaker => showText ? StoryConstants.SpeakerDisplayName(speakerId) : null;
    }
}
