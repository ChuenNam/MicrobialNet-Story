using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>赋值节点：对变量黑板中的某个变量执行运算并写回。单一后继。</summary>
    [System.Serializable]
    [StoryNode("变量赋值", ColorHex = "#1D9E75", Category = "逻辑", Order = 6)]
    internal sealed class SetVariableNodeData : StoryNodeData
    {
        [VariablePicker]
        [StoryField("变量", Order = 0)]
        public string variableId;

        [StoryField("操作", Order = 1)]
        public AssignOp op = AssignOp.Set;

        [StoryField("值", Order = 2, Tooltip = "操作数优先级：连线到「变量」输入端口（获取变量节点）时用端口值；未连线用此处常量。")]
        public string value;

        public override IEnumerable<NodePort> GetInputPorts()
            => new[] { new NodePort { id = "in" }, new NodePort { id = "var_in", label = "变量" } };
        public override IEnumerable<NodePort> GetOutputPorts() => new[] { new NodePort { id = "out" } };

        public override string GetSummary()
        {
            var opText = op switch
            {
                AssignOp.Set => "=",
                AssignOp.Add => "+=",
                AssignOp.Sub => "-=",
                AssignOp.Mul => "*=",
                AssignOp.Div => "/=",
                _ => "=",
            };
            return $"{StoryConstants.VariableName(variableId)} {opText} {value}";
        }
    }
}
