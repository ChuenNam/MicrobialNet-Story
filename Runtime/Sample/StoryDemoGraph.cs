using System.Collections.Generic;
using MicrobialNet.Story.Nodes;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 内置示例剧情图。当 <see cref="StoryFlow"/> 未指定 <see cref="StoryGraphAsset"/> 时自动构建，
    /// 使示例场景无需任何手工资产即可在编辑器 Play 模式跑通。覆盖全部节点类型：
    /// 开始 → 对白 → 选项(A/B) →（A 支线：赋值血量 -10 + 触发事件 + 结束）/（B 支线：条件判断血量 &gt; 0 → 分支对白 → 结束）。
    /// </summary>
    public static class StoryDemoGraph
    {
        internal static RuntimeStoryGraph Build()
        {
            var hp = new StoryVariableDef { id = "hp", name = "HP", type = VariableType.Int, scope = VariableScope.Local, defaultValue = "100" };

            var start = new StartNodeData { id = "start" };
            var hello = new DialogueNodeData
            {
                id = "hello",
                speakerId = StoryConstants.NarrationId,
                text = "欢迎来到示例剧情。请做出你的选择。",
                speed = 1f,
            };
            var choice = new ChoiceNodeData
            {
                id = "choice",
                options = new List<ChoiceOption>
                {
                    new ChoiceOption { optionId = "a", text = "迎战" },
                    new ChoiceOption { optionId = "b", text = "撤退" },
                },
            };

            // A 支线：赋值 hp-=10，触发事件，结束
            var setHp = new SetVariableNodeData
            {
                id = "setHp",
                variableId = "hp",
                op = AssignOp.Sub,
                value = "10",
            };
            var evt = new EventNodeData
            {
                id = "evt",
                eventName = "confirm:battle_start",
                eventPayload = "{\"enemy\":\"slime\"}",
            };
            var endA = new EndNodeData { id = "endA" };

            // B 支线：条件 hp>0 → 分支对白 → 结束
            var cond = new ConditionNodeData
            {
                id = "cond",
                combine = ConditionCombine.All,
                clauses = new List<ConditionClause>
                {
                    new ConditionClause { variableId = "hp", op = CompareOp.Greater, value = "0" },
                },
            };
            var alive = new DialogueNodeData
            {
                id = "alive",
                speakerId = StoryConstants.NarrationId,
                text = "你还有兵力，成功撤离。",
                speed = 1f,
            };
            var dead = new DialogueNodeData
            {
                id = "dead",
                speakerId = StoryConstants.NarrationId,
                text = "你已无力再战……（示例：血量不足）",
                speed = 1f,
            };
            var endB = new EndNodeData { id = "endB" };

            var nodes = new List<StoryNodeData>
            {
                start, hello, choice, setHp, evt, endA, cond, alive, dead, endB,
            };

            var edges = new List<StoryEdge>
            {
                E(start, "out", hello),
                E(hello, "out", choice),
                E(choice, "opt_a", setHp),
                E(choice, "opt_b", cond),
                E(setHp, "out", evt),
                E(evt, "out", endA),
                E(cond, "true", alive),
                E(cond, "false", dead),
                E(alive, "out", endB),
                E(dead, "out", endB),
            };

            return new RuntimeStoryGraph
            {
                meta = new StoryMeta { storyId = "demo", chapter = "示例", description = "内置示例剧情：覆盖全部节点类型的自洽闭环。" },
                nodes = nodes,
                edges = edges,
                variables = new List<StoryVariableDef> { hp },
            };
        }

        private static StoryEdge E(StoryNodeData from, string fromPort, StoryNodeData to)
            => new StoryEdge { fromNodeId = from.id, fromPortId = fromPort, toNodeId = to.id, toPortId = "in" };
    }
}
