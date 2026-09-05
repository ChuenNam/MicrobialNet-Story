using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>子图克隆工具：深克隆节点 + 重生成 ID + 重映射连线，供复制/粘贴与重复复用。</summary>
    internal static class GraphCommandHelper
    {
        /// <summary>
        /// 将一组节点及其内部连线克隆并加入模型，返回新建节点。
        /// 节点 ID 全部重生成，连线端点按旧 ID→新 ID 映射；位置整体偏移 offset。
        /// </summary>
        public static List<StoryNodeData> CloneSubgraph(StoryGraphModel model, IEnumerable<StoryNodeData> nodes, IEnumerable<StoryEdge> edges, Vector2 offset)
        {
            var idMap = new Dictionary<string, string>();
            var created = new List<StoryNodeData>();
            foreach (var src in nodes)
            {
                if (src == null) continue;
                var copy = ReflectionUtil.DeepClone(src);
                copy.id = System.Guid.NewGuid().ToString("N");
                copy.position += offset;
                idMap[src.id] = copy.id;
                model.Asset.nodes.Add(copy);
                created.Add(copy);
            }
            foreach (var e in edges)
            {
                if (e == null) continue;
                if (idMap.TryGetValue(e.fromNodeId, out var nf) && idMap.TryGetValue(e.toNodeId, out var nt))
                    model.Asset.edges.Add(new StoryEdge { fromNodeId = nf, fromPortId = e.fromPortId, toNodeId = nt, toPortId = e.toPortId });
            }
            return created;
        }
    }
}
