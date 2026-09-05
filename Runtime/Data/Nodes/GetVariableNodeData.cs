using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>
    /// 获取变量节点：只读地把一个变量「当前值」暴露到输出端口（数据线），
    /// 供赋值节点「变量输入」/ 条件子句「比较值」端口连线取值。
    /// 本身不驱动流程（无输入端口，不在主流程中执行），仅被数据连线引用。
    /// 未连线时，赋值/条件节点回落面板常量字段——原单变量运算行为不变。
    /// </summary>
    [System.Serializable]
    [StoryNode("获取变量", ColorHex = "#2E86C1", Category = "逻辑", Order = 7)]
    internal sealed class GetVariableNodeData : StoryNodeData
    {
        [VariablePicker]
        [StoryField("变量", Order = 0)]
        public string variableId;

        public override IEnumerable<NodePort> GetInputPorts() => System.Array.Empty<NodePort>();

        public override IEnumerable<NodePort> GetOutputPorts() => new[] { new NodePort { id = "out", label = "变量值" } };

        /// <summary>数据源节点：不参与流程执行（无入口、不被遍历），也不计入「不可达/孤立」判定与半透明弱化。</summary>
        public override bool IsExecutable => false;

        public override string GetSummary()
            => string.IsNullOrEmpty(variableId)
                ? "<未选择变量>"
                : $"读取 {StoryConstants.VariableName(variableId)}";
    }
}
