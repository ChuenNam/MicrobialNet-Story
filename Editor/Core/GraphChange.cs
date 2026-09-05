using System.Collections.Generic;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>变更类型，供视图层决定如何增量刷新。</summary>
    public enum GraphChangeType
    {
        NodesAdded,
        NodesRemoved,
        EdgesChanged,
        FieldChanged,
        /// <summary>整体重建（如撤销/重做后）。</summary>
        Reset,
    }

    /// <summary>一次编辑操作对图造成的影响描述。</summary>
    public readonly struct GraphChange
    {
        public GraphChangeType Type { get; }
        /// <summary>受影响的节点 ID 列表（Reset 时为 null）。</summary>
        public IReadOnlyList<string> NodeIds { get; }

        public GraphChange(GraphChangeType type, IReadOnlyList<string> nodeIds = null)
        {
            Type = type;
            NodeIds = nodeIds;
        }
    }
}
