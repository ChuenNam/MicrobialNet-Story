using MicrobialNet.Story;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>
    /// 编辑节点字段。字段路径支持嵌套，如 "text"、"options[0].text"。
    /// 通过反射设值，从而属性面板无需为每个节点类型写专门代码。
    /// </summary>
    internal sealed class EditFieldCommand : IGraphCommand
    {
        private readonly string _nodeId;
        private readonly string _fieldPath;
        private readonly object _value;

        public string Description => "编辑字段";
        public GraphChange Change => new GraphChange(GraphChangeType.FieldChanged, new[] { _nodeId });

        public EditFieldCommand(string nodeId, string fieldPath, object value)
        {
            _nodeId = nodeId;
            _fieldPath = fieldPath;
            _value = value;
        }

        public void Execute(StoryGraphModel model)
        {
            var node = model.GetNode(_nodeId);
            if (node == null) return;
            Undo.RecordObject(model.Asset, Description);
            ReflectionUtil.SetValue(node, _fieldPath, _value);
        }
    }
}
