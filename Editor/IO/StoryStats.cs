using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>剧情统计结果。</summary>
    public sealed class StoryStatsResult
    {
        public int totalNodes;
        public int edgeCount;
        public int choiceCount;
        public int variableCount;
        public int characterRefCount;
        public int reachableCount;
        public int unreachableCount;
        public int deadEndCount;
        public int textChars;
        public Dictionary<string, int> byType = new Dictionary<string, int>();
        public List<string> unreachableTitles = new List<string>();
        public List<string> deadEndTitles = new List<string>();
    }

    /// <summary>
    /// 剧情统计：节点/连线/选项/变量计数、各类型分布、可达与不可达、死路、文本体量。
    /// 纯逻辑，依赖 StoryGraphModel 的索引。
    /// </summary>
    internal static class StoryStats
    {
        public static StoryStatsResult Compute(StoryGraphModel model)
        {
            var r = new StoryStatsResult();
            if (model == null) return r;

            var nodes = model.Nodes.ToList();
            r.totalNodes = nodes.Count;
            r.edgeCount = model.Asset.edges.Count;
            r.variableCount = model.Asset.variables.Count;
            r.characterRefCount = model.Asset.usedCharacterIds.Count;

            foreach (var n in nodes)
            {
                var title = NodeRegistry.GetAttr(n.GetType())?.Title ?? n.GetType().Name;
                if (!r.byType.ContainsKey(title)) r.byType[title] = 0;
                r.byType[title]++;

                if (n is DialogueNodeData d)
                {
                    // 表驱动：文本在行（唯一真相源），统计从行取
                    string dText = d.text;
                    if (d.IsTableBound) dText = StoryTableResolver.ResolveRow(d.tableBinding)?.text ?? "";
                    r.textChars += dText?.Length ?? 0;
                }
                else if (n is ChoiceNodeData c)
                {
                    r.choiceCount += c.options.Count;
                    if (c.IsTableBound)
                    {
                        var row = StoryTableResolver.ResolveRow(c.tableBinding);
                        var tbl = StoryTableResolver.ResolveTable(c.tableBinding.tableAssetGuid);
                        for (int i = 0; i < c.options.Count; i++)
                        {
                            var chChoice = StoryTableBaker.GetChoiceForOption(row, tbl, i);
                            r.textChars += chChoice?.text?.Length ?? 0;
                        }
                    }
                    else
                        foreach (var o in c.options) r.textChars += o.text?.Length ?? 0;
                }
                else if (n is StoryTableNodeData tn && tn.tableAsset != null && tn.tableAsset.rows != null)
                {
                    // 剧情表节点：文本体量来自所引用的表（唯一真相源），不单独存储
                    foreach (var row in tn.tableAsset.rows)
                    {
                        if (row == null) continue;
                        r.textChars += row.text?.Length ?? 0;
                        if (row.choices != null)
                            foreach (var ch in row.choices)
                                if (ch != null)
                                    r.textChars += ch.text?.Length ?? 0;
                    }
                }
            }

            // 从入口 BFS 求可达集合（沿出边遍历，Comment 若被连入也会计入）
            var reachable = new HashSet<string>();
            var entry = model.GetEntryNode();
            if (entry != null)
            {
                var q = new Queue<string>();
                q.Enqueue(entry.id);
                reachable.Add(entry.id);
                while (q.Count > 0)
                {
                    var id = q.Dequeue();
                    foreach (var e in model.GetOutgoing(id))
                        if (!reachable.Contains(e.toNodeId)) { reachable.Add(e.toNodeId); q.Enqueue(e.toNodeId); }
                }
            }
            r.reachableCount = reachable.Count;
            r.unreachableCount = nodes.Count - reachable.Count;

            foreach (var n in nodes)
            {
                if (!reachable.Contains(n.id)) r.unreachableTitles.Add(n.DisplayTitle());
                bool isDeadEnd = n.IsExecutable && !(n is EndNodeData) && model.GetOutgoing(n.id).Count == 0;
                if (isDeadEnd) { r.deadEndCount++; r.deadEndTitles.Add(n.DisplayTitle()); }
            }
            return r;
        }
    }
}
