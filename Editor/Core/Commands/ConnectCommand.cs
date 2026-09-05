using MicrobialNet.Story;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>连接两个端口。合法性校验在模型 CanConnect 中统一处理。</summary>
    internal sealed class ConnectCommand : IGraphCommand
    {
        private readonly StoryEdge _edge;

        public string Description => "连接节点";
        public GraphChange Change => new GraphChange(GraphChangeType.EdgesChanged);

        public ConnectCommand(StoryEdge edge) => _edge = edge;

        public void Execute(StoryGraphModel model)
        {
            if (!model.CanConnect(_edge, out _)) return;
            Undo.RecordObject(model.Asset, Description);
            model.Asset.edges.Add(_edge);
        }
    }
}
