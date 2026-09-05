using MicrobialNet.Story;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>断开一条连线（按端口四元组精确匹配）。</summary>
    internal sealed class DisconnectCommand : IGraphCommand
    {
        private readonly string _fromNodeId;
        private readonly string _fromPortId;
        private readonly string _toNodeId;
        private readonly string _toPortId;

        public string Description => "断开连接";
        public GraphChange Change => new GraphChange(GraphChangeType.EdgesChanged);

        public DisconnectCommand(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
        {
            _fromNodeId = fromNodeId;
            _fromPortId = fromPortId;
            _toNodeId = toNodeId;
            _toPortId = toPortId;
        }

        public void Execute(StoryGraphModel model)
        {
            Undo.RecordObject(model.Asset, Description);
            model.Asset.edges.RemoveAll(e =>
                e.fromNodeId == _fromNodeId && e.fromPortId == _fromPortId &&
                e.toNodeId == _toNodeId && e.toPortId == _toPortId);
        }
    }
}
