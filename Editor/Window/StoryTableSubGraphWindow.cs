using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Commands;
using MicrobialNet.Story.EditorTools.Graph;
using MicrobialNet.Story.EditorTools.Inspector;
using MicrobialNet.Story.EditorTools.UI;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MicrobialNet.Story.EditorTools.Window
{
    /// <summary>
    /// 剧情表子画布窗口：双击主图「剧情表」节点打开。渲染该表的内部剧情流程（由 <see cref="StoryTableSubGraph"/> 从
    /// <see cref="StoryTableAsset"/> 派生虚拟节点）。连线作为「草稿」只存于工作副本（_subAsset.edges），不触碰 SO；
    /// 连线语义=跳转（不受表内顺序限制），点「同步到 Excel」才把连线 reconcile 回 StoryTableAsset（对白行 targetRowId + 选项 targetRowId）
    /// 并写回 Excel 源表。
    /// 结构变更（增/删行）经 onTableCommit 重建子画布并刷新主图画布的表节点端口。
    /// </summary>
    public class StoryTableSubGraphWindow : EditorWindow
    {
        private StoryTableAsset _table;
        private string _nodeId;
        private StoryTableNodeData _sourceNode; // 主图表节点引用：供展开时注入「表内默认」语速/样式（节点可能被删，防御性判空）
        private StoryGraphView _view;
        private ScrollView _inspectorPane;
        private StoryGraphModel _subModel;
        private StoryGraphAsset _subAsset;
        private StoryGraphView _parentView;
        private bool _hasPendingSync;            // 子画布连线是否为「未提交到剧情表」的草稿
        private Label _syncStatus;               // 头部「未同步」状态标签（配合窗口内「同步到 Excel」按钮）

        internal static void Open(StoryTableNodeData node, StoryGraphView parentView)
        {
            if (node == null || node.tableAsset == null) return;
            var w = GetWindow<StoryTableSubGraphWindow>("剧情表子画布");
            w.minSize = new Vector2(820, 520);
            w.Init(node, parentView);
        }

        private void Init(StoryTableNodeData node, StoryGraphView parentView)
        {
            _table = node.tableAsset;
            _nodeId = node.id;
            _sourceNode = node;
            _parentView = parentView;
            titleContent = new GUIContent($"剧情表：{_table.name}");
            BuildUI();
            BuildSubModelAndBind(frame: true);
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            StoryStyle.Apply(root);
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;

            var header = new Label($"剧情表「{_table.name}」· 子画布（连线=跳转，不受表内顺序限制；点「同步到 Excel」按连线写回剧情表与 Excel 源表）")
            {
                name = "sub-header",
            };
            header.AddToClassList("sgw-status");
            root.Add(header);

            // 工具栏：常用按钮（打开源表 / 同步到 Excel）+ 未同步状态
            var toolbar = new VisualElement { name = "sub-toolbar" };
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.FlexStart;
            toolbar.style.marginTop = 4;

            var openBtn = new Button(OpenSourceTable) { text = "在 Excel 中打开源表", name = "sub-open-btn" };
            toolbar.Add(openBtn);

            var syncBtn = new Button(SyncToExcel) { text = "同步到 Excel", name = "sub-sync-btn" };
            syncBtn.style.marginLeft = 4;
            toolbar.Add(syncBtn);

            var reimportBtn = new Button(ReimportFromExcel) { text = "从 Excel 表还原", name = "sub-reimport-btn" };
            reimportBtn.style.marginLeft = 4;
            toolbar.Add(reimportBtn);

            _syncStatus = new Label("") { name = "sync-status" };
            _syncStatus.AddToClassList("sgw-status");
            _syncStatus.style.marginLeft = 8;
            toolbar.Add(_syncStatus);

            root.Add(toolbar);
            UpdateSyncStatus();

            var body = new VisualElement { name = "sub-body" };
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;

            var graphContainer = new VisualElement { name = "graph-container" };
            graphContainer.AddToClassList("sgw-graph-container");
            _view = new StoryGraphView();
            _view.AddToClassList("sgw-graph-view");
            // 子画布连线/删除交由本窗口写回剧情表（选项跳转编号、行顺序），不走默认命令。
            _view.ConnectInterceptor = OnSubConnect;
            _view.DeleteInterceptor = OnSubDelete;
            graphContainer.Add(_view);
            body.Add(graphContainer);

            _inspectorPane = new ScrollView { name = "inspector" };
            _inspectorPane.AddToClassList("sgw-inspector-pane");
            _inspectorPane.style.width = 320;
            body.Add(_inspectorPane);

            root.Add(body);

            _view.SelectionChanged += OnSelectionChanged;
        }

        private void BuildSubModelAndBind(bool frame = false)
        {
            if (_subModel != null) { _subModel.Dispose(); _subModel = null; }
            if (_subAsset != null) { Object.DestroyImmediate(_subAsset); _subAsset = null; }

            _subAsset = ScriptableObject.CreateInstance<StoryGraphAsset>();
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_table));
            var sub = StoryTableSubGraph.Build(_table, _nodeId, guid, _sourceNode);
            _subAsset.nodes = sub.nodes;
            _subAsset.edges = sub.edges;

            _subModel = new StoryGraphModel(_subAsset);
            _view.Unbind();
            _view.Bind(_subModel, frame);
            RefreshInspectorForSelection();
        }

        private void OnSelectionChanged() => RefreshInspectorForSelection();

        private void RefreshInspectorForSelection()
            => RefreshInspector(_view.SelectedNodeViews().Select(v => v.Data).ToList());

        private void RefreshInspector(IReadOnlyList<StoryNodeData> nodes)
        {
            _inspectorPane.Clear();
            if (nodes == null || nodes.Count == 0)
            {
                _inspectorPane.Add(new Label("未选择节点") { name = "inspector-empty" });
                return;
            }
            if (nodes.Count > 1)
            {
                _inspectorPane.Add(new Label("多选暂不支持，请单选一个节点编辑") { name = "inspector-mixed" });
                return;
            }

            var node = nodes[0];
            var header = new Label(node.DisplayTitle()) { name = "inspector-header" };
            header.AddToClassList("sgw-inspector-header");
            _inspectorPane.Add(header);

            Action onStructuralChange = RefreshInspectorForSelection;
            Action onTableCommit = OnTableStructureChanged;
            _inspectorPane.Add(FieldDrawerRegistry.Build(_subModel, nodes, onStructuralChange, onTableCommit));
        }

        /// <summary>表结构变更（增/删行）后：重建子画布，并刷新主图画布的表节点端口（头/尾可能变化）。</summary>
        private void OnTableStructureChanged()
        {
            BuildSubModelAndBind();
            _parentView?.Populate(false);
        }

        #region 子画布连线（草稿工作副本 + 同步到表）

        /// <summary>
        /// 子画布连线：只写入工作副本（_subModel / _subAsset.edges），不触碰剧情表 SO，也不每次重建画布。
        /// 连线语义 = **跳转**（不受表内顺序限制，高行号可连低行号）：点「同步到 Excel」才把连线 reconcile 回
        /// StoryTableAsset（对白行 targetRowId + 选项 targetRowId）并写回 Excel 源表。
        /// 允许三类连线：① 对白 out → 对白 in（对白行跳转目标）；② 选项端口 opt_X → 对白行（选项跳转编号）；
        /// ③ 任意输出 → 选择节点 in（分支行以单个「带文字」选择节点表示，连线到它=跳转到该分支行并进入其选项）。
        /// 其余（自连等）为非法，吞掉不落默认命令。
        /// </summary>
        private bool OnSubConnect(Port outPort, Port inPort)
        {
            var fromView = outPort.node as StoryNodeView;
            var toView = inPort.node as StoryNodeView;
            var fromData = fromView?.Data as StoryNodeData;
            var toData = toView?.Data as StoryNodeData;
            if (fromView == null || toView == null || fromData == null || toData == null) return true;
            if (fromView == toView) return true; // 禁止自连
            var outId = (string)outPort.userData;
            var inId = (string)inPort.userData;
            if (string.IsNullOrEmpty(outId) || string.IsNullOrEmpty(inId)) return true;

            // 目标为选项节点（经其 in 入口）：「跳到该选项所属的分支行」——源（对白行 / 选项）的 targetRowId 指向该分支行。
            // 分支行自己的结构性边（B.out→Choice(B).in）除外：已在派生图且禁改，避免自跳。
            if (toData is ChoiceNodeData && inId == "in")
            {
                if (fromData is DialogueNodeData && outId == "out"
                    && fromData.tableBinding.rowId != toData.tableBinding.rowId)
                {
                    // 分支行的 out 结构性连到自身选项，不可再跳他处
                    var srcRowObj = _table.GetRow(fromData.tableBinding.rowId);
                    if (srcRowObj?.choices != null && srcRowObj.choices.Any(o => o != null)) return true;
                    CommitEdge(fromView, outId, toView, inId);
                    return true;
                }
                if (fromData is ChoiceNodeData && outId.StartsWith("opt_")
                    && fromData.tableBinding.rowId != toData.tableBinding.rowId)
                {
                    CommitEdge(fromView, outId, toView, inId);
                    return true;
                }
                return true; // 结构性边 / 同行自跳 / 其余：吞掉
            }
            // 仅允许：对白 out → 对白 in（跳转目标），或 选项 opt_X → 对白 in（选项跳转）
            if (fromData is DialogueNodeData && outId == "out" && toData is DialogueNodeData && inId == "in")
            {
                // 分支行的 out 端口结构性连到自身选项节点，不可改作「跳转目标」——它的后继由各选项 targetRowId 决定
                var fromRowObj = _table.GetRow(fromData.tableBinding.rowId);
                if (fromRowObj?.choices != null && fromRowObj.choices.Any(o => o != null)) return true;
                CommitEdge(fromView, outId, toView, inId);
                return true;
            }
            if (fromData is ChoiceNodeData && outId.StartsWith("opt_") && toData is DialogueNodeData && inId == "in")
            {
                CommitEdge(fromView, outId, toView, inId);
                return true;
            }
            return true; // 其余非法连接吞掉
        }

        /// <summary>把一条连线写入工作副本：先移除同源端口已有边（替换语义，避免连线叠加），再添加新边；
        /// 经 ConnectCommand 落到 _subModel.Asset.edges 并触发视图重绘，不写剧情表。</summary>
        private void CommitEdge(StoryNodeView fromView, string fromPortId, StoryNodeView toView, string toPortId)
        {
            var fromNodeId = fromView.NodeId;
            foreach (var old in _subModel.Asset.edges
                .Where(e => e.fromNodeId == fromNodeId && e.fromPortId == fromPortId).ToList())
                _subModel.ExecuteCommand(new DisconnectCommand(old.fromNodeId, old.fromPortId, old.toNodeId, old.toPortId));
            _subModel.ExecuteCommand(new ConnectCommand(new StoryEdge
            {
                fromNodeId = fromNodeId,
                fromPortId = fromPortId,
                toNodeId = toView.NodeId,
                toPortId = toPortId,
            }));
            _hasPendingSync = true;
            UpdateSyncStatus();
        }

        /// <summary>
        /// 子画布删除：只从工作副本移除边（不写剧情表）。节点/分组/便签不可在子画布删除（吞掉）。
        /// 点「同步到 Excel」时，工作副本里已删除的边不会再写回表（删除的跳转会被清空）。
        /// </summary>
        private bool OnSubDelete(GraphElement el)
        {
            if (el is Edge edge)
            {
                var se = BuildEdge(edge);
                if (se != null)
                {
                    _subModel.ExecuteCommand(new DisconnectCommand(se.fromNodeId, se.fromPortId, se.toNodeId, se.toPortId));
                    _hasPendingSync = true;
                    UpdateSyncStatus();
                }
                return true; // 边删除只改工作副本，不写表
            }
            return true; // 节点/分组/便签：子画布不可删
        }

        private StoryEdge BuildEdge(Edge e)
        {
            if (e.output == null || e.input == null) return null;
            if (!(e.output.node is StoryNodeView from) || !(e.input.node is StoryNodeView to)) return null;
            return new StoryEdge
            {
                fromNodeId = from.NodeId,
                fromPortId = (string)e.output.userData,
                toNodeId = to.NodeId,
                toPortId = (string)e.input.userData,
            };
        }

        /// <summary>把工作副本的连线按「跳转语义」提交到剧情表 SO（不写 Excel——Excel 由细节面板「同步到 Excel」按钮统一触发）：
        /// ① 用排序算法按连线推导行的物理顺序（链式跟随 + 剩余原序追加，分支行无线性后继；**终止行「/」是硬链尾**）；
        /// ② 对白 out→对白 写回该行 targetRowId（目标是新顺序下的自然下一行则留空=线性回退；**终止行不作自然下一行**，
        ///    接入终止行须显式写其行 id）；
        /// ③ 选项 opt_X→对白 写回该选项 targetRowId；输出→选项节点 写回源行/源选项 targetRowId = 该选项所属分支行；
        /// ④ **终止推导**：无 out 连线的非分支对话行（表尾/被断开/分支目标终点）= 终止输出端 → 写「/」（避免留空隐式接下一行）。
        /// 先清空全部跳转（**保留终止行「/」**，使被删除的边能正确清空且终止标记不丢）。提交后从表重新派生子画布并刷新主图端口。</summary>
        internal void CommitDraftToTable()
        {
            if (_subAsset == null || _subModel == null || _table == null) return;

            // 1) 排序算法：按连线推导行的物理顺序（行顺序随连线同步变更）
            var newOrder = ComputeFlowOrder();
            if (newOrder.Count > 0)
            {
                var reordered = new List<StoryTableRow>(newOrder.Count);
                foreach (var id in newOrder)
                {
                    var r = _table.GetRow(id);
                    if (r != null) reordered.Add(r);
                }
                _table.rows = reordered;
            }

            // 2) 清空所有行跳转与选项跳转（被删除的边据此清空）；
            //    保留终止标识「/」（输出端），除非用户给该行重新连了线（下方写回阶段会覆盖）
            foreach (var row in _table.rows)
            {
                if (row == null) continue;
                if (row.targetRowId != "/") row.targetRowId = "";
                if (row.choices != null)
                    foreach (var c in row.choices)
                        if (c != null && c.targetRowId != "/") c.targetRowId = "";
            }

            // 3) 新物理顺序下的自然下一行映射（非分支行的线性后继；连线目标与之一致时无需写跳转）。
            //    终止行「/」不参与：它不可作为「隐式自然下一行」——某行要接入终止行必须显式连线（写回其行 id），
            //    避免"留空=线性回退"悄悄终止流程而不被察觉。
            var nextInList = new Dictionary<string, string>();
            for (int i = 0; i < _table.rows.Count; i++)
            {
                var row = _table.rows[i];
                if (row == null) continue;
                if (IsBranchRow(row.id)) continue; // 分支行的后继由各选项 targetRowId 决定
                if (i + 1 < _table.rows.Count && _table.rows[i + 1] != null)
                {
                    var nxt = _table.rows[i + 1];
                    if (nxt.targetRowId == "/") continue; // 终止行不可作自然下一行
                    nextInList[row.id] = nxt.id;
                }
            }

            // 4) 按连线写跳转目标（自由连线：任意方向都可连，连线=跳转）
            foreach (var e in _subAsset.edges)
            {
                var from = _subModel.GetNode(e.fromNodeId) as StoryNodeData;
                var to = _subModel.GetNode(e.toNodeId) as StoryNodeData;
                if (from == null || to == null) continue;
                if (from is DialogueNodeData && e.fromPortId == "out" && to is DialogueNodeData && e.toPortId == "in")
                {
                    var row = _table.GetRow(from.tableBinding.rowId);
                    if (row == null) continue;
                    // 目标是「自然下一行」→ 留空（走线性回退）；否则写跳转目标
                    row.targetRowId = nextInList.TryGetValue(row.id, out var nxt) && nxt == to.tableBinding.rowId
                        ? "" : to.tableBinding.rowId;
                }
                else if (from is ChoiceNodeData && e.fromPortId != null && e.fromPortId.StartsWith("opt_")
                         && to is DialogueNodeData && e.toPortId == "in"
                         && int.TryParse(e.fromPortId.Substring(4), out int idx))
                {
                    var row = _table.GetRow(from.tableBinding.rowId);
                    if (row?.choices != null && idx >= 0 && idx < row.choices.Count && row.choices[idx] != null)
                        row.choices[idx].targetRowId = to.tableBinding.rowId;
                }
                else if (to is ChoiceNodeData && e.toPortId == "in")
                {
                    // 目标为选项节点 → 「跳到该选项所属的分支行」：源行/源选项 targetRowId = 分支行 id。
                    // 分支行自己的结构边（from 行 == 选项所属行）跳过，避免自跳。
                    if (from is DialogueNodeData && e.fromPortId == "out"
                        && from.tableBinding.rowId != to.tableBinding.rowId)
                    {
                        var row = _table.GetRow(from.tableBinding.rowId);
                        if (row == null) continue;
                        // 该分支行若本就是自然下一行 → 留空（线性回退），否则写跳转
                        row.targetRowId = nextInList.TryGetValue(row.id, out var nxt) && nxt == to.tableBinding.rowId
                            ? "" : to.tableBinding.rowId;
                    }
                    else if (from is ChoiceNodeData && e.fromPortId != null && e.fromPortId.StartsWith("opt_")
                             && from.tableBinding.rowId != to.tableBinding.rowId
                             && int.TryParse(e.fromPortId.Substring(4), out int ci))
                    {
                        var row = _table.GetRow(from.tableBinding.rowId);
                        if (row?.choices != null && ci >= 0 && ci < row.choices.Count && row.choices[ci] != null)
                            row.choices[ci].targetRowId = to.tableBinding.rowId;
                    }
                }
            }

            // 5) 终止推导：无 out 连线（无后继）的非分支对话行 = 终止输出端 → 写「/」。
            //    否则留空=线性回退会把终止节点隐式接到列表下一行，使其成为上一节点的 target，
            //    「终止」意图在 SO/Excel 里丢失（表尾行、被断开的节点、分支目标终点都走这里）。
            var connectedFrom = new HashSet<string>();
            foreach (var e in _subAsset.edges)
            {
                var from = _subModel.GetNode(e.fromNodeId) as StoryNodeData;
                if (from is DialogueNodeData && e.fromPortId == "out"
                    && !string.IsNullOrEmpty(from.tableBinding.rowId))
                    connectedFrom.Add(from.tableBinding.rowId);
            }
            foreach (var row in _table.rows)
            {
                if (row == null) continue;
                if (IsBranchRow(row.id)) continue; // 分支行后继由各选项 targetRowId 决定
                if (!connectedFrom.Contains(row.id))
                    row.targetRowId = "/"; // 无出边 → 终止输出端（表节点暴露 exit_ 端口）
            }

            _table.unsyncedToExcel = true;
            EditorUtility.SetDirty(_table);
            _hasPendingSync = false;
            BuildSubModelAndBind(false);
            _parentView?.Populate(false);
            UpdateSyncStatus();
        }

        /// <summary>排序算法：按连线推导行的物理顺序。
        /// 线性骨架 = 非分支行的 out→对白 / out→选项所属分支 后继链（链式跟随）；
        /// 分支行无线性后继（其后续由选项决定），作为链中节点被前驱连到；
        /// 选项边（opt_X→对白）只贡献「目标有前驱」，目标在链结束后按原顺序补位；
        /// **终止行「/」是硬链尾**——Walk 到它即停（流程终点），其后继不产生；
        /// 无任何连线的行 / 不可达行按原顺序追加。确定性、稳定（不依赖历史重排状态）。</summary>
        private List<string> ComputeFlowOrder()
        {
            var order = new List<string>();
            var visited = new HashSet<string>();
            var hasIncoming = new HashSet<string>();
            var succ = new Dictionary<string, string>();

            foreach (var e in _subAsset.edges)
            {
                var from = _subModel.GetNode(e.fromNodeId) as StoryNodeData;
                var to = _subModel.GetNode(e.toNodeId) as StoryNodeData;
                if (from == null || to == null) continue;
                string f = from.tableBinding.rowId, t = to.tableBinding.rowId;
                if (string.IsNullOrEmpty(f) || string.IsNullOrEmpty(t)) continue;

                if (from is DialogueNodeData && e.fromPortId == "out" && to is DialogueNodeData && e.toPortId == "in")
                {
                    if (IsBranchRow(f)) continue; // 分支行 out 只结构连自己选项，不产生线性后继
                    if (!succ.ContainsKey(f)) succ[f] = t;
                    hasIncoming.Add(t);
                }
                else if (from is DialogueNodeData && e.fromPortId == "out" && to is ChoiceNodeData && e.toPortId == "in")
                {
                    if (f == t || IsBranchRow(f)) continue; // 结构边 / 分支行不跳他处
                    if (!succ.ContainsKey(f)) succ[f] = t;
                    hasIncoming.Add(t);
                }
                else if (from is ChoiceNodeData && e.fromPortId != null && e.fromPortId.StartsWith("opt_")
                         && to is DialogueNodeData && e.toPortId == "in")
                {
                    hasIncoming.Add(t); // 选项目标有前驱（经分支行选项进入）
                }
            }

            // 头 = 无入边的行；若全表无头（如纯环）从首行开始
            var heads = _table.rows.Where(r => r != null && !hasIncoming.Contains(r.id)).Select(r => r.id).ToList();
            if (heads.Count == 0)
            {
                var first = _table.rows.FirstOrDefault(r => r != null);
                if (first != null) heads.Add(first.id);
            }
            heads.Sort((a, b) => _table.IndexOf(a).CompareTo(_table.IndexOf(b)));

            void Walk(string id)
            {
                if (!visited.Add(id)) return;
                order.Add(id);
                // 终止行「/」是硬链尾：流程在此结束，不再产生任何后继（写回算法显式包含跳转终止）
                if (_table.GetRow(id)?.targetRowId == "/") return;
                if (succ.TryGetValue(id, out var nxt) && !string.IsNullOrEmpty(nxt) && _table.GetRow(nxt) != null)
                    Walk(nxt);
            }
            foreach (var h in heads) Walk(h);

            // 未访问（不可达 / 分支行选项目标 / 环内剩余）：按原顺序追加
            foreach (var r in _table.rows)
                if (r != null && !visited.Contains(r.id)) order.Add(r.id);
            return order;
        }

        private bool IsBranchRow(string rowId)
        {
            var row = _table.GetRow(rowId);
            return row != null && row.choices != null && row.choices.Any(o => o != null);
        }

        /// <summary>若该表存在打开的、且含未提交草稿连线的子画布窗口，则先把草稿提交到 SO（排序 + 跳转写回）。
        /// 供细节面板「同步到 Excel」按钮在导出前调用，实现「一次点击同时写 SO 与 Excel」。</summary>
        internal static bool TryCommitDraft(StoryTableAsset table)
        {
            if (table == null) return false;
            bool committed = false;
            foreach (var w in Resources.FindObjectsOfTypeAll<StoryTableSubGraphWindow>())
            {
                if (w == null || w._table != table || !w._hasPendingSync) continue;
                w.CommitDraftToTable();
                committed = true;
            }
            return committed;
        }

        /// <summary>「在 Excel 中打开源表」：有源文件用系统默认程序打开；无源文件则定位并选中 SO。</summary>
        private void OpenSourceTable()
        {
            if (_table == null) return;
            string abs = StoryAssetPaths.ResolveSourcePath(_table.sourceFilePath);
            if (!string.IsNullOrEmpty(abs))
                EditorUtility.OpenWithDefaultApp(abs);
            else
            {
                Selection.activeObject = _table;
                EditorGUIUtility.PingObject(_table);
            }
        }

        /// <summary>「同步到 Excel」：先把本窗口未提交的连线草稿按规则写入 SO（排序 + 跳转 + 终止推导），
        /// 再覆盖式写回 Excel 源文件；无源文件仅落 SO 并弹窗提示。成功后清「未同步」标记并刷新状态。</summary>
        private void SyncToExcel()
        {
            if (_table == null) return;
            if (_hasPendingSync) CommitDraftToTable(); // 草稿先提交 SO（排序/跳转/终止），再导出
            if (!StoryTableAssetExporter.HasSource(_table))
            {
                EditorUtility.DisplayDialog("无法同步",
                    "该剧情表未配置源文件（sourceFilePath 为空），连线改动已写入剧情表（SO），但无法写回 Excel。\n可在「重新导入并同步全部」前为其指定源文件。", "确定");
                return;
            }
            try
            {
                StoryTableAssetExporter.ExportToSource(_table);
                _table.unsyncedToExcel = false;
                EditorUtility.SetDirty(_table);
                UpdateSyncStatus();
                EditorUtility.DisplayDialog("同步成功",
                    "已把连线改动写入剧情表，并将行顺序 / 跳转（含终止标识「/」）写回 Excel 源表。", "确定");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("同步失败", ex.Message, "确定");
            }
        }

        /// <summary>「从 Excel 表还原」：仅把当前剧情表（本子画布关联的 <see cref="_table"/>）从其源文件重新导入（Excel → SO）。
        /// 注意与主窗口菜单「数据 → 表格驱动 → 重新导入并同步全部」（批量全部表）区分；随后刷新本子画布与主图端口。</summary>
        private void ReimportFromExcel()
        {
            if (_table == null) return;
            string src = StoryAssetPaths.ResolveSourcePath(_table.sourceFilePath);
            if (string.IsNullOrEmpty(src))
            {
                EditorUtility.DisplayDialog("无法还原", "该剧情表未配置源文件（sourceFilePath 为空），无法从 Excel 还原。", "确定");
                return;
            }
            try
            {
                StoryTableAssetImporter.ImportFromFile(_table, src, out _);
                _table.unsyncedToExcel = false;
                EditorUtility.SetDirty(_table);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("还原失败", ex.Message, "确定");
                return;
            }
            BuildSubModelAndBind(false);
            _parentView?.Populate(false);
            UpdateSyncStatus();
            EditorUtility.DisplayDialog("还原完成", $"已从 Excel 重新导入当前剧情表「{_table.name}」。", "确定");
        }

        private void UpdateSyncStatus()
        {
            if (_syncStatus == null) return;
            _syncStatus.text = _hasPendingSync
                ? "● 有未同步到剧情表的连线改动（请点「同步到 Excel」）"
                : "已与剧情表一致";
        }

        #endregion

        private void OnDestroy()
        {
            if (_view != null) _view.Unbind();
            if (_subModel != null) { _subModel.Dispose(); _subModel = null; }
            if (_subAsset != null) { Object.DestroyImmediate(_subAsset); _subAsset = null; }
        }
    }
}
