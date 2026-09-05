using System;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 一条连线（端口到端口）。
    /// 连线与节点分离存储，便于做「删除节点自动接续上下游」「反向引用统计」等操作。
    /// </summary>
    [Serializable]
    internal sealed class StoryEdge
    {
        /// <summary>起点节点 ID（输出端口所在节点）。</summary>
        public string fromNodeId;

        /// <summary>起点端口 ID（见各节点 GetOutputPorts 返回的 id）。</summary>
        public string fromPortId;

        /// <summary>终点节点 ID（输入端口所在节点）。</summary>
        public string toNodeId;

        /// <summary>终点端口 ID（一般为 "in"）。</summary>
        public string toPortId;
    }
}
