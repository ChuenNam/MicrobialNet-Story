using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>便签节点：纯批注，不参与流程执行，无端口。仅用于在画布上留说明。</summary>
    [System.Serializable]
    [StoryNode("便签", ColorHex = "#888780", Category = "辅助", Order = 50)]
    internal sealed class CommentNodeData : StoryNodeData
    {
        [StoryField("内容", Order = 0)]
        [MultilineText(Lines = 4)]
        public string note;

        public override IEnumerable<NodePort> GetInputPorts() => System.Array.Empty<NodePort>();
        public override IEnumerable<NodePort> GetOutputPorts() => System.Array.Empty<NodePort>();
        public override bool IsExecutable => false;

        public override string GetSummary()
            => string.IsNullOrEmpty(note) ? "<便签>" : note;
    }
}
