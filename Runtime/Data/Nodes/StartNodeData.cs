using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>开始节点：图入口，无输入，单一输出。整张图至多一个。</summary>
    [System.Serializable]
    [StoryNode("开始", ColorHex = "#639922", Category = "基础", Order = -100)]
    internal sealed class StartNodeData : StoryNodeData
    {
        public override IEnumerable<NodePort> GetInputPorts() => System.Array.Empty<NodePort>();
        public override IEnumerable<NodePort> GetOutputPorts() => new[] { new NodePort { id = "out", label = "开始" } };
        public override bool IsEntry => true;
        public override string GetSummary() => "剧情入口";
    }
}
