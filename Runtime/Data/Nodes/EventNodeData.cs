using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>事件节点：向运行时派发一个具名事件（含 JSON 参数）。单一后继。演出/系统逻辑挂载点。</summary>
    [System.Serializable]
    [StoryNode("事件", ColorHex = "#888780", Category = "逻辑", Order = 7)]
    internal sealed class EventNodeData : StoryNodeData
    {
        [StoryField("事件名", Order = 0)]
        [StoryEventPicker]
        public string eventName;

        [StoryField("参数(JSON)", Order = 1)]
        [MultilineText(Lines = 3)]
        public string eventPayload;

        public override IEnumerable<NodePort> GetInputPorts() => new[] { new NodePort { id = "in" } };
        public override IEnumerable<NodePort> GetOutputPorts() => new[] { new NodePort { id = "out" } };

        public override string GetSummary()
            => string.IsNullOrEmpty(eventName) ? "<事件>" : $"触发：{eventName}";
    }
}
