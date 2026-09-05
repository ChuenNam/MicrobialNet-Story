using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>结束节点：无输出。可正常结束或跳转到指定章节。</summary>
    [System.Serializable]
    [StoryNode("结束", ColorHex = "#A32D2D", Category = "基础", Order = 4)]
    internal sealed class EndNodeData : StoryNodeData
    {
        [StoryField("结束类型", Order = 0)]
        public EndType endType = EndType.Normal;

        [StoryField("跳转章节", Order = 1)]
        public string jumpToChapter;

        [StorySection("结束展示")]
        [StoryField("显示文本", Order = 2, Tooltip = "勾选后，剧情走到本节点时弹出结束文本对话框；默认不勾选（不弹任何框，自然结束）。")]
        public bool showEndText = false;

        [StoryField("结束文本", Order = 3)]
        [MultilineText(Lines = 3)]
        public string endText = string.Empty;

        public override IEnumerable<NodePort> GetInputPorts() => new[] { new NodePort { id = "in" } };
        public override IEnumerable<NodePort> GetOutputPorts() => System.Array.Empty<NodePort>();

        public override string GetSummary()
            => endType == EndType.JumpChapter
                ? $"结束 → 跳转 {jumpToChapter}"
                : "剧情结束";
    }
}
