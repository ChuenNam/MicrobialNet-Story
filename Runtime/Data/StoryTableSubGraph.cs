using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story.Nodes;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 把 <see cref="StoryTableAsset"/>（真相源）派生为「虚拟内部子图」：
    /// 纯内存节点/边，不写入 <see cref="StoryGraphAsset"/>，满足「不单独存储数据」。
    ///
    /// 同一套逻辑供三处复用：
    /// ① 表节点端口聚合——<see cref="GetEntryPorts"/>/<see cref="GetExitPorts"/> 按内部「头/尾空缺」与「无连接编号选项」生成
    ///    <c>entry_{rowId}</c> / <c>exit_{rowId}</c> / <c>optexit_{rowId}_{optionIndex}</c> 端口；
    /// ② 运行时 <see cref="RuntimeStoryGraph.FromAsset"/> 展开为虚拟节点 + 边界边；
    /// ③ 编辑器子画布渲染（双击表节点）。
    ///
    /// 虚拟节点 id 确定性（基于表节点 id + 行 id），保证跨重载快照可恢复，且多表节点互不冲突。
    /// 选项按行内原始下标（含无连接编号的选项）编号，故 optionId = 行内下标，与编辑器/本地化 key 对齐。
    /// 内部边连法：纯对白行无选项→优先按行跳转目标（row.targetRowId，自由连线写回）连到目标行，否则线性连下一行；
    /// **跳转目标填「/」= 终止标识**：该行不连任何后继（输出端，出口由 GetExitPorts 暴露 exit_{rowId}）；
    /// 分支行（行内有选项）→ 合并为单个「带文字」玩家选择节点（承载行内对白 showText=row.showText + 选项），无独立对白节点、无结构边；
    /// 选项的 targetRowId 指向存在的行→连到该行（内部边），填「/」或留空→该选项是表节点的出口端口（optexit_…），由边界映射接主图后继。
    /// 虚拟节点位置由 <see cref="ApplyFlowLayout"/> 统一计算（流程式布局）：主链横向、并列/分支纵向，供子画布/预览使用。
    /// </summary>
    internal static class StoryTableSubGraph
    {
        public const string EntryPrefix = "entry_";
        public const string ExitPrefix = "exit_";
        /// <summary>「无连接编号的选项」出口端口前缀：optexit_{rowId}_{optionIndex}。每个选项一个独立出口端口，可分别接主图不同后继。</summary>
        public const string OptExitPrefix = "optexit_";
        private const string DlgPrefix = "::dlg::";
        private const string ChcPrefix = "::chc::";

        /// <summary>对白行对应的虚拟节点 id。</summary>
        public static string DialogueVirtualId(string tableNodeId, string rowId) => tableNodeId + DlgPrefix + rowId;

        /// <summary>选项行对应的虚拟 Choice 节点 id。</summary>
        public static string ChoiceVirtualId(string tableNodeId, string rowId) => tableNodeId + ChcPrefix + rowId;

        /// <summary>某行的「虚拟节点 id」：分支行（带选项）用 ChoiceVirtualId（其内容归并到带文字的选择节点），
        /// 纯对白行用 DialogueVirtualId。内部边/头尾/边界映射统一走本方法，保证 1 节点模型处处一致。</summary>
        internal static string RowVirtualId(StoryTableAsset table, string tableNodeId, string rowId)
        {
            var row = table != null ? table.GetRow(rowId) : null;
            bool isBranch = row != null && row.choices != null && row.choices.Any(o => o != null);
            return isBranch ? ChoiceVirtualId(tableNodeId, rowId) : DialogueVirtualId(tableNodeId, rowId);
        }

        /// <summary>表节点输入端口 id（对应一个 head 行）。</summary>
        public static string EntryPortId(string rowId) => EntryPrefix + rowId;

        /// <summary>表节点输出端口 id（对应一个 tail 行）。</summary>
        public static string ExitPortId(string rowId) => ExitPrefix + rowId;

        /// <summary>表节点输出端口 id（对应一个「无连接编号的选项」）。每个选项一个独立出口端口。</summary>
        public static string OptExitPortId(string rowId, int optionIndex) => OptExitPrefix + rowId + "_" + optionIndex;

        /// <summary>派生结果。nodes/edges 为虚拟节点与内部边；headRowIds/tailRowIds 为无入边/无出边的对白行（对应表节点输入/输出端口）。</summary>
        public sealed class Result
        {
            public List<StoryNodeData> nodes = new List<StoryNodeData>();
            public List<StoryEdge> edges = new List<StoryEdge>();
            public List<string> headRowIds = new List<string>();
            public List<string> tailRowIds = new List<string>();
        }

        /// <summary>从剧情表派生虚拟内部子图。tableAssetGuid 仅用于编辑器侧写回（运行时传 null 即可，内容索引按 rowId 查）。
        /// source 可选：传入表节点（StoryTableNodeData）时，若其开了「表内默认」覆盖（语速/打字机/样式），
        /// 则把参数注入每个虚拟对白/选项节点——表内统一语速与样式，运行时/子画布零感知（读的仍是虚拟节点字段）。</summary>
        public static Result Build(StoryTableAsset table, string tableNodeId, string tableAssetGuid = null, StoryTableNodeData source = null)
        {
            var result = new Result();
            if (table == null || table.rows == null || string.IsNullOrEmpty(tableNodeId)) return result;

            var rowIds = new HashSet<string>(table.rows.Where(r => r != null).Select(r => r.id));

            // 1) 对白虚拟节点（分支行不建对白节点——其内容归并到下方的「带文字」玩家选择节点，单节点表示）
            for (int i = 0; i < table.rows.Count; i++)
            {
                var row = table.rows[i];
                if (row == null) continue;
                var opts0 = row.choices ?? new List<StoryTableChoice>();
                if (opts0.Any(o => o != null)) continue; // 分支行 → 下方单 Choice 节点
                var dlg = new DialogueNodeData
                {
                    id = DialogueVirtualId(tableNodeId, row.id),
                    tableBinding = new TableBinding { tableAssetGuid = tableAssetGuid, rowId = row.id },
                    // 冗余填入行内容：子画布虚拟节点是临时对象（每次刷新重建），让 Runtime 的 GetSummary/GetOutputPorts
                    // 直接读到节点字段即可正确显示，无需依赖 Editor 侧 ResolveSummary（构造时未调，只 Refresh 用）。
                    speakerId = string.IsNullOrEmpty(row.speaker) ? StoryConstants.NarrationId : row.speaker,
                    text = row.text ?? "",
                };
                ApplyTableDefaults(dlg, source);
                result.nodes.Add(dlg);
            }

            // 2) 内部边 + 选项虚拟节点。
            //    行内只要有选项（含「无连接编号」的选项）就建 Choice 节点；选项按行内原始下标 i 编号（含无连接编号的），
            //    保证虚拟子图每次重建端口 id 与本地化 key 稳定。
            //    - 有有效 targetRowId → 连到目标对白（内部边）
            //    - 无有效 targetRowId（连接编号缺失）→ 该选项即表节点的出口端口（optexit_{rowId}_{i}），由边界映射接外部，不连内部边
            for (int i = 0; i < table.rows.Count; i++)
            {
                var row = table.rows[i];
                if (row == null) continue;
                var dlgId = DialogueVirtualId(tableNodeId, row.id);

                var options = row.choices ?? new List<StoryTableChoice>();
                bool hasOptions = options.Any(o => o != null);
                if (!hasOptions)
                {
                    // 纯对白行：优先按「行跳转目标」连到目标对白（自由连线写回），否则线性连下一行；
                    // 终止标识「/」→ 不连任何后继（本行是输出端，出口由 GetExitPorts 暴露 exit_{rowId}）；
                    // 无后继则本行为尾部（出口由 GetExitPorts 暴露 exit_{rowId}）
                    string nxt = null;
                    if (row.targetRowId == "/")
                        nxt = null; // 终止跳转
                    else if (!string.IsNullOrEmpty(row.targetRowId) && rowIds.Contains(row.targetRowId))
                        nxt = row.targetRowId;
                    else if (i + 1 < table.rows.Count && table.rows[i + 1] != null)
                        nxt = table.rows[i + 1].id;
                    if (!string.IsNullOrEmpty(nxt))
                    {
                        result.edges.Add(new StoryEdge
                        {
                            fromNodeId = dlgId,
                            fromPortId = "out",
                            toNodeId = RowVirtualId(table, tableNodeId, nxt),
                            toPortId = "in",
                        });
                    }
                    continue;
                }

                // 分支行（带选项）：合并为单个「带文字」的玩家选择节点——承载行内对白（showText=row.showText）+ 选项。
                // 不再产生独立的对白节点与结构边；前驱经内部/外部边直接连本选择节点的 in 入口。
                var choiceId = ChoiceVirtualId(tableNodeId, row.id);
                var choice = new ChoiceNodeData
                {
                    id = choiceId,
                    tableBinding = new TableBinding { tableAssetGuid = tableAssetGuid, rowId = row.id },
                    showText = row.showText, // 可经面板「显示文字」开关持久化（表驱动也生效）
                    // 冗余填入行内容（speaker/text）：同 DialogueNodeData 注释；GetSummary/showText 分支读到即正确显示。
                    speakerId = string.IsNullOrEmpty(row.speaker) ? StoryConstants.NarrationId : row.speaker,
                    text = row.text ?? "",
                };
                ApplyTableDefaults(choice, source);
                var choiceOpts = new List<ChoiceOption>();
                // 选项 id 用行内原始下标（含无连接编号的选项），保证端口 id / 本地化 key 稳定且与编辑器一致；
                // 同时把选项文本填入 ChoiceOption.text，让 GetOutputPorts/GetSummary 读节点字段即可正确显示。
                for (int vi = 0; vi < options.Count; vi++)
                {
                    var o = options[vi];
                    if (o == null) continue;
                    var opt = new ChoiceOption
                    {
                        optionId = vi.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        text = o.text ?? "",
                    };
                    choiceOpts.Add(opt);
                    bool valid = !string.IsNullOrEmpty(o.targetRowId) && rowIds.Contains(o.targetRowId);
                    if (valid)
                    {
                        result.edges.Add(new StoryEdge
                        {
                            fromNodeId = choiceId,
                            fromPortId = "opt_" + opt.optionId,
                            toNodeId = RowVirtualId(table, tableNodeId, o.targetRowId),
                            toPortId = "in",
                        });
                    }
                    // 无效 / 无连接编号 → 不连内部边，作为出口端口待边界映射
                }
                choice.options = choiceOpts;
                result.nodes.Add(choice);
            }

            // 3) 头/尾：某行的虚拟 id 从未作为内部边 toNodeId（头）/ fromNodeId（尾）
            ComputeTargetsSources(table, tableNodeId, out var targets, out var sources);
            foreach (var row in table.rows)
            {
                if (row == null) continue;
                var vid = RowVirtualId(table, tableNodeId, row.id);
                if (!targets.Contains(vid)) result.headRowIds.Add(row.id);
                if (!sources.Contains(vid)) result.tailRowIds.Add(row.id);
            }

            // 4) 流程式布局：主链横向、并列/分支纵向（从入口 DFS 分配位置）。
            //    布局纯编辑器视觉（position 仅编辑器构建存在，见 P9 编辑态剥离）→ 玩家构建跳过。
#if UNITY_EDITOR
            ApplyFlowLayout(result, table);
#endif
            return result;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 流程式自动布局：主链（沿「离入口的跳数」横向延伸）x 递增；同一后继层的并列/分支节点沿纵向堆叠（y 递增）。
        /// 规则：
        /// ① 从首行（恒为入口）沿「主线后继」（非分支行 out → 目标行）DFS，深度即横向列；
        /// ② 分支行（带选项）无主线后继，其每个选项目标放下一列、同列纵向依次排开（并列语义）；
        /// ③ 选项目标若已被放置（如回跳到主链）则跳过；
        /// ④ 其余未放置行（其它 head 入口段 / 分支后未接续行 / 环内行）统一放最右兜底列纵向排开。
        /// 位置仅用于渲染/预览，运行时与试跑逻辑不依赖（同构展开只看节点/边）。
        /// 仅编辑器构建编译（玩家构建中 position 字段不存在）。
        /// </summary>
        private static void ApplyFlowLayout(Result result, StoryTableAsset table)
        {
            if (result == null || result.nodes == null || table == null || table.rows == null) return;

            const float H = 340f; // 横向列距（主链 / 跳转层级）
            const float V = 200f; // 纵向行距（并列堆叠）

            var nodeByRow = new Dictionary<string, StoryNodeData>();
            foreach (var n in result.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.tableBinding.rowId)) continue;
                if (!nodeByRow.ContainsKey(n.tableBinding.rowId))
                    nodeByRow[n.tableBinding.rowId] = n;
            }

            // 由内部边推导后继：非分支行 out → 主线后继；分支行 opt_X → 并列目标（按选项序）
            var nextOf = new Dictionary<string, string>();
            var optTargets = new Dictionary<string, List<string>>();
            foreach (var e in result.edges)
            {
                if (e == null) continue;
                var from = result.nodes.FirstOrDefault(n => n != null && n.id == e.fromNodeId);
                var to = result.nodes.FirstOrDefault(n => n != null && n.id == e.toNodeId);
                if (from == null || to == null) continue;
                if (from is ChoiceNodeData && e.fromPortId != null && e.fromPortId.StartsWith("opt_"))
                {
                    if (!optTargets.TryGetValue(from.tableBinding.rowId, out var list))
                        optTargets[from.tableBinding.rowId] = list = new List<string>();
                    if (!list.Contains(to.tableBinding.rowId)) list.Add(to.tableBinding.rowId);
                }
                else if (from is DialogueNodeData && e.fromPortId == "out")
                    nextOf[from.tableBinding.rowId] = to.tableBinding.rowId;
            }

            var placed = new HashSet<string>();
            var depthCounter = new Dictionary<int, int>();

            void Place(string rowId, int depth)
            {
                if (rowId == null || !nodeByRow.TryGetValue(rowId, out var node) || !placed.Add(rowId)) return;
                depthCounter.TryGetValue(depth, out int k);
                depthCounter[depth] = k + 1;
                node.position = new Vector2(depth * H, k * V);
                if (nextOf.TryGetValue(rowId, out var nxt))
                    Place(nxt, depth + 1); // 主线后继 → 横向延伸
                else if (optTargets.TryGetValue(rowId, out var opts))
                    foreach (var t in opts) Place(t, depth + 1); // 并列/分支 → 下一列纵向堆叠
            }

            // 入口：首行恒为入口（主链从它开始，横向延伸）
            var first = table.rows.FirstOrDefault(r => r != null);
            if (first != null) Place(first.id, 0);

            // 兜底：其余未放置行（其它 head 独立入口段 / 分支后未接续行 / 环内行）统一放最右列，纵向排开，
            // 不占用第 0 列以免干扰主链阅读；其出口仍有 entry_/exit_ 端口可接入。
            int maxDepth = depthCounter.Count > 0 ? depthCounter.Keys.Max() : 0;
            float fx = (maxDepth + 1) * H;
            int fi = 0;
            foreach (var n in result.nodes)
                if (n != null && !string.IsNullOrEmpty(n.tableBinding.rowId) && !placed.Contains(n.tableBinding.rowId))
                {
                    n.position = new Vector2(fx, fi * V);
                    fi++;
                }
        }
#endif

        /// <summary>仅算头/尾行（供端口与摘要，不实例化节点、不分配边）。</summary>
        public static void ComputeHeadsTails(StoryTableAsset table, string tableNodeId, out List<string> heads, out List<string> tails)
        {
            if (table == null || table.rows == null)
            {
                heads = new List<string>();
                tails = new List<string>();
                return;
            }
            ComputeTargetsSources(table, tableNodeId, out var targets, out var sources);
            heads = new List<string>();
            tails = new List<string>();
            foreach (var row in table.rows)
            {
                if (row == null) continue;
                var vid = RowVirtualId(table, tableNodeId, row.id);
                if (!targets.Contains(vid)) heads.Add(row.id);
                if (!sources.Contains(vid)) tails.Add(row.id);
            }
        }

        /// <summary>遍历行，统计「作为内部边 toNodeId 的集合」(targets) 与「作为 fromNodeId 的集合」(sources)。</summary>
        private static void ComputeTargetsSources(StoryTableAsset table, string tableNodeId, out HashSet<string> targets, out HashSet<string> sources)
        {
            targets = new HashSet<string>();
            sources = new HashSet<string>();
            var rowIds = new HashSet<string>(table.rows.Where(r => r != null).Select(r => r.id));
            for (int i = 0; i < table.rows.Count; i++)
            {
                var row = table.rows[i];
                if (row == null) continue;
                var dlgId = DialogueVirtualId(tableNodeId, row.id);
                var options = row.choices ?? new List<StoryTableChoice>();
                bool hasOptions = options.Any(o => o != null);
                if (!hasOptions)
                {
                    // 与 Build 同构：终止标识「/」→ 无后继（输出端）；否则优先行跳转目标，其次线性下一行
                    string nxt = null;
                    if (row.targetRowId == "/")
                        nxt = null; // 终止跳转
                    else if (!string.IsNullOrEmpty(row.targetRowId) && rowIds.Contains(row.targetRowId))
                        nxt = row.targetRowId;
                    else if (i + 1 < table.rows.Count && table.rows[i + 1] != null)
                        nxt = table.rows[i + 1].id;
                    if (!string.IsNullOrEmpty(nxt))
                    {
                        targets.Add(RowVirtualId(table, tableNodeId, nxt));
                        sources.Add(dlgId);
                    }
                    continue;
                }
                // 分支行：单「带文字」选择节点即该行（1 节点模型，无结构边）。
                // 其选项即出口 → 恒为 source（永不作为线性 tail）；选项有目标 → 目标成为 target。
                var choiceId = ChoiceVirtualId(tableNodeId, row.id);
                sources.Add(choiceId);
                foreach (var o in options)
                {
                    if (o == null || string.IsNullOrEmpty(o.targetRowId) || !rowIds.Contains(o.targetRowId)) continue;
                    targets.Add(RowVirtualId(table, tableNodeId, o.targetRowId));
                    sources.Add(choiceId);
                }
            }
        }

        /// <summary>表节点输入端口：首行恒为入口 + 其余 head 行各一个 <c>entry_{rowId}</c>，label 取该行讲述者+正文摘要。
        /// 首行恒入口：跳转语义下第一行可能被内部循环引用（有入边不再是 head），但仍是表的自然起点，不应失去对外入口。</summary>
        public static List<NodePort> GetEntryPorts(StoryTableAsset table, string tableNodeId)
        {
            var ports = new List<NodePort>();
            if (table == null || table.rows == null) return ports;
            ComputeHeadsTails(table, tableNodeId, out var heads, out _);
            var first = table.rows.FirstOrDefault(r => r != null);
            if (first != null)
                ports.Add(new NodePort { id = EntryPortId(first.id), label = DialoguePortLabel(first) });
            foreach (var rowId in heads)
            {
                if (first != null && rowId == first.id) continue;
                ports.Add(new NodePort { id = EntryPortId(rowId), label = DialoguePortLabel(table.GetRow(rowId)) });
            }
            return ports;
        }

        /// <summary>
        /// 表节点输出端口，两类：
        /// ① 纯对白尾部行（无选项且无内部后继）→ <c>exit_{rowId}</c>；
        /// ② 每个「无连接编号的选项」→ <c>optexit_{rowId}_{optionIndex}</c>（每个选项独立出口端口，可分别接主图不同后继）。
        /// </summary>
        public static List<NodePort> GetExitPorts(StoryTableAsset table, string tableNodeId)
        {
            var ports = new List<NodePort>();
            if (table == null || table.rows == null) return ports;
            var rowIds = new HashSet<string>(table.rows.Where(r => r != null).Select(r => r.id));
            ComputeTargetsSources(table, tableNodeId, out _, out var sources);
            for (int i = 0; i < table.rows.Count; i++)
            {
                var row = table.rows[i];
                if (row == null) continue;
                var dlgId = DialogueVirtualId(tableNodeId, row.id);
                bool hasOptions = row.choices != null && row.choices.Any(o => o != null);
                if (!hasOptions && !sources.Contains(dlgId))
                    ports.Add(new NodePort { id = ExitPortId(row.id), label = DialoguePortLabel(row) });
                if (row.choices != null)
                {
                    for (int vi = 0; vi < row.choices.Count; vi++)
                    {
                        var o = row.choices[vi];
                        if (o == null) continue;
                        bool valid = !string.IsNullOrEmpty(o.targetRowId) && rowIds.Contains(o.targetRowId);
                        if (!valid)
                            ports.Add(new NodePort { id = OptExitPortId(row.id, vi), label = ChoicePortLabel(o) });
                    }
                }
            }
            return ports;
        }

        /// <summary>对白类端口文本：带「(对话)」节点类型前缀 + 讲述者 + 正文摘要（用于 entry_/exit_ 端口）。</summary>
        private static string DialoguePortLabel(StoryTableRow row)
        {
            if (row == null) return "(对话)<空行>";
            var sp = StoryConstants.SpeakerDisplayName(string.IsNullOrEmpty(row.speaker) ? StoryConstants.NarrationId : row.speaker);
            var preview = string.IsNullOrEmpty(row.text) ? "<空>" : row.text.Replace("\n", " ");
            return $"(对话) {sp}：{preview}";
        }

        /// <summary>选项类端口文本：带「(选项)」节点类型前缀 + 选项文本（用于 optexit_ 出口端口）。</summary>
        private static string ChoicePortLabel(StoryTableChoice opt)
        {
            if (opt == null) return "(选项)<空>";
            var preview = string.IsNullOrEmpty(opt.text) ? "<空选项>" : opt.text.Replace("\n", " ");
            return $"(选项) {preview}";
        }

        /// <summary>把表节点（StoryTableNodeData）的「表内默认」覆盖注入虚拟节点：
        /// 统一语速/打字机作用于对白与「带文字」选项（其行内对白也走打字机）；统一样式与外观作用于对白与选项。
        /// source 为 null 或未开覆盖 = 不注入（保持现状）。</summary>
        private static void ApplyTableDefaults(StoryNodeData v, StoryTableNodeData source)
        {
            if (source == null) return;
            if (source.overrideTyping)
            {
                switch (v)
                {
                    case DialogueNodeData d:
                        d.speed = source.typingSpeed;
                        d.typingMode = source.typingMode;
                        break;
                    case ChoiceNodeData cc:
                        cc.speed = source.typingSpeed;
                        cc.typingMode = source.typingMode;
                        break;
                }
            }
            if (!source.overrideAppearance) return;
            switch (v)
            {
                case DialogueNodeData dd:
                    dd.appearanceStyle = source.appearanceStyle;
                    dd.appearanceOverridePosition = source.appearanceOverridePosition;
                    dd.appearancePositionMode = source.appearancePositionMode;
                    dd.appearancePositionAnchor = source.appearancePositionAnchor;
                    dd.appearancePositionOffset = source.appearancePositionOffset;
                    dd.appearanceSpawnStrategyKey = source.appearanceSpawnStrategyKey;
                    dd.appearancePersistent = source.appearancePersistent;
                    break;
                case ChoiceNodeData c2:
                    c2.appearanceStyle = source.appearanceStyle;
                    c2.appearanceOverridePosition = source.appearanceOverridePosition;
                    c2.appearancePositionMode = source.appearancePositionMode;
                    c2.appearancePositionAnchor = source.appearancePositionAnchor;
                    c2.appearancePositionOffset = source.appearancePositionOffset;
                    c2.appearanceSpawnStrategyKey = source.appearanceSpawnStrategyKey;
                    break;
            }
        }
    }
}
