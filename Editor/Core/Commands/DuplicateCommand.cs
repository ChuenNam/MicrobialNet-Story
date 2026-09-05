using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using UnityEngine;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>复制并粘贴选中节点（Ctrl+D）：复制选中节点及其内部连线，偏移后克隆。</summary>
    internal sealed class DuplicateCommand : IGraphCommand
    {
        private readonly List<string> _nodeIds;
        private readonly Vector2 _offset;
        private List<StoryNodeData> _created;

        public string Description => "复制节点";
        public GraphChange Change => new GraphChange(GraphChangeType.NodesAdded, _created?.ConvertAll(n => n.id));

        public DuplicateCommand(IEnumerable<string> nodeIds, Vector2 offset)
        {
            _nodeIds = new List<string>(nodeIds);
            _offset = offset;
        }

        public void Execute(StoryGraphModel model)
        {
            var idSet = new HashSet<string>(_nodeIds);
            var nodes = _nodeIds.Select(model.GetNode).Where(n => n != null).ToList();
            if (nodes.Count == 0) return;
            var edges = model.Asset.edges
                .Where(e => idSet.Contains(e.fromNodeId) && idSet.Contains(e.toNodeId)).ToList();

            Undo.RecordObject(model.Asset, Description);
            _created = GraphCommandHelper.CloneSubgraph(model, nodes, edges, _offset);
        }
    }
}
