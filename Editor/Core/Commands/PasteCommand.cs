using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>粘贴节点：深克隆并加入模型，重生成 ID、重映射内部连线、整体偏移。</summary>
    internal sealed class PasteCommand : IGraphCommand
    {
        private readonly List<StoryNodeData> _srcNodes;
        private readonly List<StoryEdge> _srcEdges;
        private readonly Vector2 _offset;
        private List<StoryNodeData> _created;

        public string Description => "粘贴节点";
        public GraphChange Change => new GraphChange(GraphChangeType.NodesAdded, _created?.ConvertAll(n => n.id));

        public PasteCommand(IEnumerable<StoryNodeData> nodes, IEnumerable<StoryEdge> edges, Vector2 offset)
        {
            _srcNodes = new List<StoryNodeData>(nodes);
            _srcEdges = new List<StoryEdge>(edges);
            _offset = offset;
        }

        public void Execute(StoryGraphModel model)
        {
            Undo.RecordObject(model.Asset, Description);
            _created = GraphCommandHelper.CloneSubgraph(model, _srcNodes, _srcEdges, _offset);
        }
    }
}
