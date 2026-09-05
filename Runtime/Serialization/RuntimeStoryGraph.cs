using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story.Nodes;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 运行时剧情图（纯数据 POCO）。播放器只消费此模型，彻底与 ScriptableObject 资产加载解耦。
    ///
    /// 两种来源：
    /// - 编辑器 / 示例场景：<see cref="FromAsset"/> 直接从 <see cref="StoryGraphAsset"/> 构建（引用其节点/边/变量，播放器只读不写）。
    /// - 发布期：后续由 JSON 装载器（Newtonsoft，复用导出格式）反序列化为本模型，避免随包发布 .asset。
    ///
    /// 节点列表为多态（List&lt;StoryNodeData&gt; 子类），来源保证类型正确（SO 直接持有 / JSON 用 $type）。
    /// </summary>
    internal sealed class RuntimeStoryGraph
    {
        /// <summary>元信息（ID / 章节 / 标签 / 描述）。</summary>
        public StoryMeta meta;

        /// <summary>剧情节点（多态）。</summary>
        public List<StoryNodeData> nodes = new List<StoryNodeData>();

        /// <summary>连线集合（端口到端口）。</summary>
        public List<StoryEdge> edges = new List<StoryEdge>();

        /// <summary>本图变量黑板定义（局部）。</summary>
        public List<StoryVariableDef> variables = new List<StoryVariableDef>();

        /// <summary>本图被引用的角色 ID 集合。</summary>
        public List<string> usedCharacterIds = new List<string>();

        /// <summary>剧情表行索引（rowId → 行），由 <see cref="FromAsset"/> 从所有表格驱动组的表资产收集。
        /// 表驱动节点的内容（对白文本 / 讲述者 / 选项）运行时统一自此取，节点本身不冗余存储内容，
        /// 保证「表是唯一内容真相源」。key 为行稳定 id（同图内由 NewId 保证唯一）。</summary>
        public Dictionary<string, StoryTableRow> tableRows = new Dictionary<string, StoryTableRow>();

        /// <summary>按 ID 查找节点（线性查找，剧情节点量级下足够）。</summary>
        public StoryNodeData GetNode(string id)
            => nodes.FirstOrDefault(n => n.id == id);

        /// <summary>取入口节点（IsEntry 为 true 的节点，约定至多一个）。</summary>
        public StoryNodeData GetEntryNode()
            => nodes.FirstOrDefault(n => n.IsEntry);

        /// <summary>
        /// 从编辑期资产构建运行时图。直接引用资产的节点/边/变量实例（播放器只读，不修改），
        /// 不走深拷贝，避免 [SerializeReference] 子树复制开销与类型丢失。
        /// </summary>
        public static RuntimeStoryGraph FromAsset(StoryGraphAsset asset)
        {
            if (asset == null) return null;
            var g = new RuntimeStoryGraph
            {
                meta = asset.meta,
                nodes = new List<StoryNodeData>(),
                edges = new List<StoryEdge>(),
                variables = new List<StoryVariableDef>(asset.variables),
                usedCharacterIds = new List<string>(asset.usedCharacterIds),
            };

            // 1) 展开：普通节点直接纳入；表节点展开为虚拟内部子图（含内容索引收集），表节点本身不入运行时图。
            var tableNodeIds = new HashSet<string>();
            foreach (var node in asset.nodes)
            {
                if (node is StoryTableNodeData tn)
                {
                    tableNodeIds.Add(tn.id);
                    if (tn.tableAsset != null && tn.tableAsset.rows != null)
                    {
                        var sub = StoryTableSubGraph.Build(tn.tableAsset, tn.id, null, tn);
                        g.nodes.AddRange(sub.nodes);
                        g.edges.AddRange(sub.edges);
                        foreach (var row in tn.tableAsset.rows)
                        {
                            if (row != null && !string.IsNullOrEmpty(row.id) && !g.tableRows.ContainsKey(row.id))
                                g.tableRows[row.id] = row;
                            if (row != null && !string.IsNullOrEmpty(row.speaker) && !g.usedCharacterIds.Contains(row.speaker))
                                g.usedCharacterIds.Add(row.speaker);
                        }
                    }
                    continue; // 表节点本身被虚拟子图取代
                }
                g.nodes.Add(node);
            }

            // 2) 边：不触碰表节点的边转为边界边；其余原样复制。
            //    外部 → 表节点入口（entry_{rowId}）：映射为「外部节点 → 头虚拟对白节点 in」。
            //    表节点出口（exit_{rowId}）→ 外部：映射为「尾虚拟对白节点 out → 外部节点」。
            foreach (var e in asset.edges)
            {
                if (e == null) continue;
                bool fromTable = tableNodeIds.Contains(e.fromNodeId);
                bool toTable = tableNodeIds.Contains(e.toNodeId);
                if (!fromTable && !toTable) { g.edges.Add(e); continue; }
                if (fromTable && toTable) continue; // 表节点内部边不应出现在资产层

                if (toTable)
                {
                    var rowId = e.toPortId != null && e.toPortId.StartsWith(StoryTableSubGraph.EntryPrefix)
                        ? e.toPortId.Substring(StoryTableSubGraph.EntryPrefix.Length) : null;
                    if (rowId == null) continue;
                    // 入口行映射：分支行 → 其「带文字」选择节点；纯对白行 → 对白节点（1 节点模型统一走 RowVirtualId）
                    var tn = asset.nodes.FirstOrDefault(n => n.id == e.toNodeId) as StoryTableNodeData;
                    var vid = tn?.tableAsset != null
                        ? StoryTableSubGraph.RowVirtualId(tn.tableAsset, e.toNodeId, rowId)
                        : StoryTableSubGraph.DialogueVirtualId(e.toNodeId, rowId);
                    g.edges.Add(new StoryEdge
                    {
                        fromNodeId = e.fromNodeId,
                        fromPortId = e.fromPortId,
                        toNodeId = vid,
                        toPortId = "in",
                    });
                }
                else // fromTable
                {
                    if (e.fromPortId != null && e.fromPortId.StartsWith(StoryTableSubGraph.OptExitPrefix))
                    {
                        // 无连接编号的选项出口：optexit_{rowId}_{optionIndex} → Choice 虚拟节点对应 opt 端口 → 外部
                        var rest = e.fromPortId.Substring(StoryTableSubGraph.OptExitPrefix.Length);
                        int sep = rest.LastIndexOf('_');
                        if (sep <= 0) continue;
                        var rowId = rest.Substring(0, sep);
                        var optIdx = rest.Substring(sep + 1);
                        g.edges.Add(new StoryEdge
                        {
                            fromNodeId = StoryTableSubGraph.ChoiceVirtualId(e.fromNodeId, rowId),
                            fromPortId = "opt_" + optIdx,
                            toNodeId = e.toNodeId,
                            toPortId = e.toPortId,
                        });
                    }
                    else
                    {
                        var rowId = e.fromPortId != null && e.fromPortId.StartsWith(StoryTableSubGraph.ExitPrefix)
                            ? e.fromPortId.Substring(StoryTableSubGraph.ExitPrefix.Length) : null;
                        if (rowId == null) continue;
                        g.edges.Add(new StoryEdge
                        {
                            fromNodeId = StoryTableSubGraph.DialogueVirtualId(e.fromNodeId, rowId),
                            fromPortId = "out",
                            toNodeId = e.toNodeId,
                            toPortId = e.toPortId,
                        });
                    }
                }
            }

            // 3) JSON 发布路径：内联表行（构建期 tableAsset 不可解析），按 rowId 合并进内容索引（已收集的同 id 不覆盖）。
            if (asset.inlinedTableRows != null)
            {
                foreach (var row in asset.inlinedTableRows)
                    if (row != null && !string.IsNullOrEmpty(row.id) && !g.tableRows.ContainsKey(row.id))
                        g.tableRows[row.id] = row;
            }
            return g;
        }
    }
}
