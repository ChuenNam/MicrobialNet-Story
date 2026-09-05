using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>
    /// 批量编辑多个节点的同一字段路径（用于属性面板多选同类型节点时的「批量修改」）。
    /// 一次执行 = 一个 Undo 记录，应用到所有指定节点，Ctrl+Z 可一次性还原全部。
    /// </summary>
    internal sealed class MultiEditFieldCommand : IGraphCommand
    {
        private readonly IReadOnlyList<string> _nodeIds;
        private readonly string _fieldPath;
        private readonly object _value;

        public string Description => "批量编辑字段";
        public GraphChange Change => new GraphChange(GraphChangeType.FieldChanged, _nodeIds);

        public MultiEditFieldCommand(IReadOnlyList<string> nodeIds, string fieldPath, object value)
        {
            _nodeIds = nodeIds;
            _fieldPath = fieldPath;
            _value = value;
        }

        public void Execute(StoryGraphModel model)
        {
            if (_nodeIds == null || _nodeIds.Count == 0) return;
            // 同一资产只需 RecordObject 一次，整批写入共享一个 Undo 步
            Undo.RecordObject(model.Asset, Description);
            foreach (var id in _nodeIds)
            {
                var node = model.GetNode(id);
                if (node == null) continue;
                ReflectionUtil.SetValue(node, _fieldPath, _value);
            }
        }
    }
}
