using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>删除节点：同时断开其所有相连连线（自动清理，可在偏好设置中关闭）。</summary>
    internal sealed class RemoveNodeCommand : IGraphCommand
    {
        private readonly string _nodeId;
        private StoryNodeData _removed;
        private List<StoryEdge> _removedEdges;

        public string Description => "删除节点";
        public GraphChange Change => new GraphChange(GraphChangeType.NodesRemoved, new[] { _nodeId });

        public RemoveNodeCommand(string nodeId) => _nodeId = nodeId;

        public void Execute(StoryGraphModel model)
        {
            var node = model.GetNode(_nodeId);
            if (node == null) return;
            Undo.RecordObject(model.Asset, Description);
            _removed = node;
            _removedEdges = model.Asset.edges
                .Where(e => e.fromNodeId == _nodeId || e.toNodeId == _nodeId).ToList();
            model.Asset.edges.RemoveAll(e => e.fromNodeId == _nodeId || e.toNodeId == _nodeId);
            model.Asset.nodes.Remove(node);
            // 同步从任何分组的成员列表中移除该节点，避免悬空引用。
            foreach (var g in model.Asset.groups)
                g.nodeIds.Remove(_nodeId);
        }
    }
}
