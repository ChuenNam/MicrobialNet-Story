using System;
using MicrobialNet.Story;
using UnityEngine;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>新增节点：经 NodeRegistry 创建并分配稳定 ID，置于指定画布坐标。</summary>
    internal sealed class AddNodeCommand : IGraphCommand
    {
        private readonly Type _nodeType;
        private readonly Vector2 _position;
        private StoryNodeData _created;

        public string Description => "添加节点";
        public GraphChange Change => new GraphChange(GraphChangeType.NodesAdded, new[] { _created?.id });

        /// <summary>执行后暴露新建节点的 ID，便于随后自动连线（如端口拖拽创建）。</summary>
        public string CreatedNodeId => _created?.id;

        public AddNodeCommand(Type nodeType, Vector2 position)
        {
            _nodeType = nodeType;
            _position = position;
        }

        public void Execute(StoryGraphModel model)
        {
            Undo.RecordObject(model.Asset, Description);
            _created = NodeRegistry.Create(_nodeType);
            _created.position = _position;
            model.Asset.nodes.Add(_created);
        }
    }
}
