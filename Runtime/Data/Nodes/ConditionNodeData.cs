using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>单个条件子句：变量 {op} 值。</summary>
    [System.Serializable]
    internal sealed class ConditionClause
    {
        /// <summary>子句稳定 ID（折叠状态持久化用，重排/改名不影响）。</summary>
        public string clauseId = System.Guid.NewGuid().ToString("N");

        [VariablePicker]
        [StoryField("变量", Order = 0)]
        public string variableId;

        [StoryField("比较", Order = 1)]
        public CompareOp op = CompareOp.Equal;

        [StoryField("值", Order = 2)]
        public string value;
    }

    /// <summary>条件节点：按 combine 组合多个子句，输出「满足 / 不满足」两个分支。</summary>
    [System.Serializable]
    [StoryNode("条件", ColorHex = "#7F77DD", Category = "逻辑", Order = 5)]
    internal sealed class ConditionNodeData : StoryNodeData
    {
        [StoryField("组合方式", Order = 0)]
        public ConditionCombine combine = ConditionCombine.All;

        [StoryField("条件组", Order = 1)]
        public List<ConditionClause> clauses = new List<ConditionClause>();

        // 输入端口：in（流程入口）+ 每子句一个「比较值」端口（var_in_{clauseId}，接获取变量节点）。
        // 连线后该子句的比较值取端口变量当前值；未连线回落子句常量 value（原单变量运算不变）。
        public override IEnumerable<NodePort> GetInputPorts()
        {
            var ports = new List<NodePort> { new NodePort { id = "in" } };
            if (clauses != null)
                foreach (var cl in clauses)
                    if (cl != null && !string.IsNullOrEmpty(cl.clauseId))
                        ports.Add(new NodePort { id = "var_in_" + cl.clauseId, label = "比较值" });
            return ports;
        }

        public override IEnumerable<NodePort> GetOutputPorts() => new[]
        {
            new NodePort { id = "true", label = "满足" },
            new NodePort { id = "false", label = "不满足" },
        };

        public override string GetSummary()
        {
            if (clauses.Count == 0) return "<无条件>";
            var join = combine == ConditionCombine.All ? " 且 " : " 或 ";
            return string.Join(join, clauses.Select(c => $"{StoryConstants.VariableName(c.variableId)} {OpText(c.op)} {c.value}"));
        }

        private static string OpText(CompareOp op) => op switch
        {
            CompareOp.Equal => "==",
            CompareOp.NotEqual => "!=",
            CompareOp.Greater => ">",
            CompareOp.GreaterEqual => ">=",
            CompareOp.Less => "<",
            CompareOp.LessEqual => "<=",
            _ => "?",
        };
    }
}
