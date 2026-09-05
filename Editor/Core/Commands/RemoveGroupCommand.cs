using MicrobialNet.Story;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>删除分组框：仅移除分组本身，保留其成员节点（解除成员关系，不删节点）。</summary>
    internal sealed class RemoveGroupCommand : IGraphCommand
    {
        private readonly string _groupId;

        public string Description => "删除分组";
        public GraphChange Change => new GraphChange(GraphChangeType.Reset);

        public RemoveGroupCommand(string groupId) => _groupId = groupId;

        public void Execute(StoryGraphModel model)
        {
            var g = model.Asset.groups.Find(x => x.id == _groupId);
            if (g == null) return;
            Undo.RecordObject(model.Asset, Description);
            // 子分组提升为被删组的父组，保留嵌套结构（不再孤立）。
            foreach (var child in model.Asset.groups)
                if (child.parentGroupId == _groupId) child.parentGroupId = g.parentGroupId ?? "";
            model.Asset.groups.Remove(g);
        }
    }
}
