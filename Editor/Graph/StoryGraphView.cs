using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Commands;
using MicrobialNet.Story.EditorTools.Validation;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Graph
{
    /// <summary>
    /// 剧情画布。只读渲染 StoryGraphModel，并通过命令把用户操作（连/断/删）回写模型。
    /// 所有结构变更走命令，画布本身不直接改数据。
    /// </summary>
    public sealed class StoryGraphView : GraphView
    {
        private StoryGraphModel _model;
        private readonly Dictionary<string, StoryNodeView> _nodeViews = new Dictionary<string, StoryNodeView>();
        private readonly List<StoryGroupView> _groupViews = new List<StoryGroupView>();
        private readonly List<StickyNote> _stickyNotes = new List<StickyNote>();
        private bool _rebuilding;
        private MiniMap _miniMap;

        /// <summary>可选拦截器：子画布（剧情表）用。连接两端口前调用；返回 true 表示已自行处理（如写回剧情表），跳过默认 ConnectCommand。</summary>
        internal Func<Port, Port, bool> ConnectInterceptor;

        /// <summary>可选拦截器：子画布（剧情表）用。删除元素前调用；返回 true 表示已处理（如写回剧情表），该元素从删除列表剔除、不被默认命令处理。</summary>
        internal Func<GraphElement, bool> DeleteInterceptor;

        // 「修饰键 + 左键点击」快捷建节点：记录当前按住的建节点修饰键（Space / D / O / V / C / E / T）。
        // 键事件由 StoryGraphWindow 转发，真正创建发生在画布左键按下时（OnCanvasMouseDown）。
        private KeyCode? _createModifier;

        // 「单击选择」语义增强：记录按下位置，用于区分「单击」与「拖拽（平移/框选/移动节点）」。
        private Vector2 _pointerDownPos;

        // 选择操作嵌套深度（如 ClearSelection 内部会递归调用 RemoveFromSelection）。
        // 用于抑制嵌套过程中的重复 SelectionChanged 事件，只在最外层操作完成后触发一次刷新。
        private int _selectionChangeDepth;

        // 字母键 → 节点类型（直接创建，跳过搜索窗）。与文档 02 及工具栏「添加节点」菜单节点类型一一对应。
        private static readonly Dictionary<KeyCode, Type> QuickNodeTypes = new Dictionary<KeyCode, Type>
        {
            { KeyCode.D, typeof(DialogueNodeData) },    // 对话
            { KeyCode.O, typeof(ChoiceNodeData) },      // 玩家选项
            { KeyCode.V, typeof(SetVariableNodeData) }, // 变量赋值
            { KeyCode.C, typeof(ConditionNodeData) },   // 条件
            { KeyCode.E, typeof(EventNodeData) },       // 事件
            { KeyCode.T, typeof(StoryTableNodeData) },  // 剧情表（表格驱动；创建后面板拖入 SO 绑定）
        };

        public static bool IsQuickCreateKey(KeyCode k) => QuickNodeTypes.ContainsKey(k);

        /// <summary>设置/清除当前按住的建节点修饰键（由 StoryGraphWindow 的键事件转发）。</summary>
        public void SetCreateModifier(KeyCode k, bool down)
        {
            if (k == KeyCode.Space || QuickNodeTypes.ContainsKey(k))
                _createModifier = down ? k : (_createModifier == k ? (KeyCode?)null : _createModifier);
        }

        /// <summary>选择变更时触发（节点选中/取消），用于刷新属性面板。</summary>
        public event Action SelectionChanged;

        public StoryGraphView()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());

            // 「修饰键 + 左键点击」快捷建节点：在冒泡阶段之前拦截左键，若正按住建节点修饰键则创建节点。
            // 普通左键点击不在此拦截，交给 GraphView 内置 ClickSelector 处理默认选择/空白清除。
            this.RegisterCallback<MouseDownEvent>(OnCanvasMouseDown, TrickleDown.TrickleDown);

            // 多选细节语义增强：
            // ① 单击空白画布 → 取消全部选择（画布级 MouseUp，TrickleDown，命中测试含 target 自身）；
            // ② 单击已处于多选集中的节点 → 折叠为单选该节点（节点级处理在 StoryNodeView，保证命中节点自身、拖拽不折叠）。
            this.RegisterCallback<MouseDownEvent>(OnGraphPointerDown);
            this.RegisterCallback<MouseUpEvent>(OnCanvasMouseUp, TrickleDown.TrickleDown);

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged = OnGraphViewChanged;
        }

        internal void Bind(StoryGraphModel model, bool frame = true)
        {
            _model = model;
            _model.Changed += OnModelChanged;
            Populate(frame);
        }

        internal void Unbind()
        {
            if (_model != null) _model.Changed -= OnModelChanged;
            _model = null;
        }

        private void OnModelChanged(GraphChange change)
        {
            if (change.Type == GraphChangeType.FieldChanged)
            {
                if (change.NodeIds != null)
                {
                    var toRefresh = new HashSet<string>(change.NodeIds);
                    // 数据源（获取变量节点）字段变化 → 其数据线消费方（赋值/条件节点的变量输入端口）摘要需同步刷新
                    foreach (var id in change.NodeIds)
                    {
                        foreach (var e in _model.GetOutgoing(id))
                        {
                            if (e.toPortId == "var_in" || (e.toPortId ?? "").StartsWith("var_in_", StringComparison.Ordinal))
                                toRefresh.Add(e.toNodeId);
                        }
                    }
                    foreach (var id in toRefresh)
                        if (_nodeViews.TryGetValue(id, out var nv))
                        {
                            nv.Refresh(); // 标题/摘要/端口名刷新
                            // 端口集合变化（如选项增删）→ 重建该节点端口与连线，使「可接入节点」同步
                            if (nv.HasPortSetChanged()) ScheduleRebuild();
                        }
                }
                return;
            }
            ScheduleRebuild();
        }

        private bool _rebuildPending;
        private void ScheduleRebuild()
        {
            if (_rebuildPending) return;
            _rebuildPending = true;
            this.schedule.Execute(() => { _rebuildPending = false; Populate(false); }).ExecuteLater(1);
        }

        /// <summary>重建全部节点与连线。frame=true 时整体取景（仅首次加载/导入后用）；
        /// frame=false 时保留当前缩放与平移（增删节点、选项增删等结构微调，避免视图复位）。</summary>
        public void Populate(bool frame = true)
        {
            if (_model == null) return;
            // 重建前记录当前选中的节点，重建后恢复，避免选项增删等重建导致选中丢失、属性面板被清空。
            var selIds = this.selection.OfType<StoryNodeView>().Select(v => v.NodeId).ToList();
            _rebuilding = true;
            var toRemove = new List<GraphElement>();
            foreach (var n in nodes) toRemove.Add(n);
            foreach (var e in edges) toRemove.Add(e);
            // 上一图残留的分组框(StoryGroupView)与便签(StickyNote)是直接 AddElement 的 GraphElement，
            // 不在 nodes/edges 枚举里，须显式收集删除，否则切换剧情图时残留。
            foreach (var g in _groupViews) toRemove.Add(g);
            foreach (var sn in _stickyNotes) toRemove.Add(sn);
            if (toRemove.Count > 0) DeleteElements(toRemove);
            _rebuilding = false;
            _nodeViews.Clear();
            _groupViews.Clear();
            _stickyNotes.Clear();

            foreach (var node in _model.Nodes)
            {
                var nv = new StoryNodeView(node);
                nv.OnPositionChanged += () => { _model.Touch(); RefitGroupsForNode(nv.NodeId); };
                nv.SetModel(_model);
                AddElement(nv);
                _nodeViews[node.id] = nv;
                // 端口拖拽连线：自包含 PortDragConnector（纯公开 API，规避本版本 EdgeConnectorListener 不可见 / EdgeConnector 抽象的问题）。
                foreach (var p in nv.Query<Port>().ToList())
                    p.AddManipulator(new PortDragConnector(this, GetAllPorts, OnPortsConnected));
            }

            foreach (var edge in _model.Asset.edges)
            {
                if (!_nodeViews.TryGetValue(edge.fromNodeId, out var from) ||
                    !_nodeViews.TryGetValue(edge.toNodeId, out var to)) continue;
                var op = from.GetPort(edge.fromPortId);
                var ip = to.GetPort(edge.toPortId);
                if (op == null || ip == null) continue;
                var ve = new Edge { output = op, input = ip };
                ve.userData = edge;
                AddElement(ve);
                ve.input.Connect(ve);
                ve.output.Connect(ve);
            }

            // ── 分组框（衬在节点之后，SendToBack 使其处于节点底层，不遮挡点击）──
            foreach (var g in _model.Asset.groups)
            {
                if (g == null) continue;
                var gv = new StoryGroupView(_model, this, g);
                gv.userData = g.id;
                AddElement(gv);
                _groupViews.Add(gv);
            }
            // 嵌套 z 序：子组在前、父组衬后。按深度降序 SendToBack —— 最深的子组先入底，
            // 最浅的父组最后入底，最终父组处于子组之后（同一坐标系下父框包裹子框，渲染上父在底层）。
            foreach (var gv in _groupViews.OrderByDescending(g => GetGroupDepth(g.Data)))
                gv.SendToBack();

            // ── 便签（自带拖拽/缩放/编辑，画布仅负责持久化）──
            foreach (var sn in _model.Asset.stickyNotes)
            {
                if (sn == null) continue;
                var note = new StickyNote { title = sn.title, contents = sn.text };
                note.theme = (StickyNoteTheme)Mathf.Clamp(sn.theme, 0, 3);
                note.SetPosition(sn.rect);
                note.userData = sn.id;
                // 拖拽前记录 Undo，使便签移动可撤销（避免与 GraphView 默认拖拽重复录制）
                note.RegisterCallback<MouseDownEvent>(e => { if (e.button == 0) Undo.RecordObject(_model.Asset, "移动便签"); });
                // 编辑（标题/正文/主题）完成后持久化
                note.RegisterCallback<FocusOutEvent>(_ => PersistStickyNote(note));
                AddElement(note);
                _stickyNotes.Add(note);
            }

            // 恢复选中（仅当对应节点仍存在）
            if (selIds.Count > 0)
            {
                ClearSelection();
                foreach (var id in selIds)
                    if (_nodeViews.TryGetValue(id, out var nv)) AddToSelection(nv);
            }

            // 试跑中若发生重建（如结构微调），按已记录路径重敷流动连线，避免染色的 EdgeControl 实例丢失。
            ApplyFlowColors();

            // 不可达节点整体 50% 透明（02 §二 节点状态叠加样式），每次重建后按可达性重算。
            ApplyUnreachable();

            // 分组外框在元素挂载并完成布局后再统一重算一次：构造函数里的首算已用固定节点尺寸（不依赖 layout），
            // 此处再保险一次，确保载入/切换图后外框立即贴合，无需等用户移动节点才出现。
            this.schedule.Execute(() =>
            {
                // 自底向上重算：子组框先确定，父组框才能正确包裹子组框（避免重叠）。
                foreach (var gv in _groupViews.OrderByDescending(g => GetGroupDepth(g.Data)))
                    gv.RefitToMembers();
            });

            // 仅首次加载/导入后整体取景；结构微调（增删/选项同步）保留当前缩放与平移，避免视图复位。
            if (frame) this.schedule.Execute(() => { FrameAll(); }).ExecuteLater(2);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_rebuilding || _model == null) return change;

            // 连线创建现由 PortDragConnector 直接走 ConnectCommand（不经过 edgesToCreate），
            // 故此处不再处理 edgesToCreate。

            // 删除：节点/连线走命令，视觉由重建统一处理
            if (change.elementsToRemove != null)
            {
                var remaining = new List<GraphElement>(change.elementsToRemove.Count);
                foreach (var el in change.elementsToRemove)
                {
                    // 子画布（剧情表）拦截：被拦截的元素已自行写回表，从删除列表剔除，避免默认命令再处理。
                    if (DeleteInterceptor != null && DeleteInterceptor(el)) continue;
                    if (el is Edge edge)
                    {
                        var se = BuildStoryEdge(edge) ?? edge.userData as StoryEdge;
                        if (se != null)
                            _model.ExecuteCommand(new DisconnectCommand(se.fromNodeId, se.fromPortId, se.toNodeId, se.toPortId));
                    }
                    else if (el is StoryNodeView nv)
                    {
                        _model.ExecuteCommand(new RemoveNodeCommand(nv.NodeId));
                    }
                    else if (el is StoryGroupView gv)
                    {
                        // 删除分组：仅移除分组本身，保留成员节点（RemoveGroupCommand 不解成员关系以外的数据）。
                        _model.ExecuteCommand(new RemoveGroupCommand(gv.GroupId));
                    }
                    else if (el is StickyNote sn)
                    {
                        _model.ExecuteCommand(new RemoveStickyNoteCommand(sn.userData as string));
                    }
                    remaining.Add(el);
                }
                change.elementsToRemove = remaining.Count > 0 ? remaining : null;
            }

            // 便签移动/缩放：由 GraphView 默认拖拽完成位移，此处仅把最终几何持久化回资产。
            // （分组移动由 StoryGroupView 自定义拖拽处理；节点移动由 StoryNodeView.SetPosition 已写回数据，二者无需在此处理。）
            if (change.movedElements != null && !_rebuilding)
            {
                foreach (var el in change.movedElements)
                    if (el is StickyNote sn) PersistStickyNote(sn);
            }

            return change;
        }

        private static StoryEdge BuildStoryEdge(Edge e)
        {
            if (e.output == null || e.input == null) return null;
            if (!(e.output.node is StoryNodeView fromView) || !(e.input.node is StoryNodeView toView)) return null;
            return new StoryEdge
            {
                fromNodeId = fromView.NodeId,
                fromPortId = (string)e.output.userData,
                toNodeId = toView.NodeId,
                toPortId = (string)e.input.userData,
            };
        }

        public string RequestAddNode(Type t, Vector2? pos = null)
        {
            if (_model == null || t == null) return null;
            // 未指定位置（如工具栏菜单）时按已有节点数错开摆放；右键等指定位置时直接用。
            Vector2 p = pos ?? new Vector2(60 + (_nodeViews.Count % 6) * 36, 60 + (_nodeViews.Count % 6) * 36);
            var cmd = new AddNodeCommand(t, p);
            _model.ExecuteCommand(cmd);
            return cmd.CreatedNodeId;
        }

        /// <summary>画布左键按下时，若正按住建节点修饰键，则创建节点：
        /// Space+左键 → 弹搜索窗（若已选中节点则自动连线）；字母键(D/O/V/C/E/T)+左键 → 直接创建对应类型。
        /// 用 TrickleDown 注册，先于 GraphView 内置的框选/拖拽/平移 Manipulator 拦截；消费后 StopPropagation。</summary>
        private void OnCanvasMouseDown(MouseDownEvent evt)
        {
            if (_createModifier == null) return;
            if (evt.button != 0) return; // 仅响应左键
            if (_model == null) return;

            var contentLocal = contentViewContainer.WorldToLocal(evt.mousePosition);
            if (_createModifier == KeyCode.Space)
            {
                // Space + 左键：弹搜索窗；若已选中节点则自动连线（保留文档「选中节点按 Space」语义）。
                // 立刻取消空格读取（_createModifier 置空），并延后一帧再打开窗口——
                // 否则用户仍按着空格、搜索窗的搜索框一获得焦点就把空格（或其自动重复）吃进输入框。
                _createModifier = null;
                var sel = SelectedNodeViews().FirstOrDefault();
                var sp = evt.mousePosition;
                var cl = contentLocal;
                this.schedule.Execute(() => OpenNodeSearch(sp, cl, null, sel));
            }
            else if (QuickNodeTypes.TryGetValue(_createModifier.Value, out var t))
            {
                // 字母键 + 左键：直接创建对应类型节点（跳过搜索窗）。
                RequestAddNode(t, contentLocal);
            }
            evt.StopPropagation(); // 拦截，避免触发默认框选/拖拽/平移
        }

        /// <summary>记录左键按下位置，供 OnCanvasMouseUp 区分「单击」与「拖拽」。</summary>
        private void OnGraphPointerDown(MouseDownEvent evt)
        {
            if (evt.button != 0) return;
            _pointerDownPos = evt.mousePosition;
        }

        /// <summary>单击空白画布 → 取消全部选择（多选细节语义②）。MouseUp 用 TrickleDown 注册（先于节点级折叠处理）。
        /// 仅在「单击」（位移 &lt; 阈值、无修饰键、非建节点修饰键）且命中画布/网格背景/contentViewContainer 时清除选择；
        /// 命中节点/端口/连线/分组/便签的交给默认行为（节点折叠在 StoryNodeView 处理）。</summary>
        private void OnCanvasMouseUp(MouseUpEvent evt)
        {
            if (evt.button != 0) return;
            if (_createModifier != null) return;            // 建节点修饰键点击：交给创建流程
            if (evt.shiftKey || evt.ctrlKey || evt.commandKey) return; // 修饰键增选：保留默认行为
            if (Vector2.Distance(_pointerDownPos, evt.mousePosition) >= 6f) return; // 拖拽（平移/框选/移动）→ 不处理

            var ve = evt.target as VisualElement;
            if (ve == null) return;

            // 命中可交互元素 → 不是空白，交给默认行为（节点折叠在 StoryNodeView 处理）。注意用「自身 is / 祖先」双判定，
            // 否则 evt.target 就是节点自身时 GetFirstAncestorOfType 返回 null，会误判成空白。
            if (ve is StoryNodeView || ve.GetFirstAncestorOfType<StoryNodeView>() != null) return;
            if (ve.GetFirstAncestorOfType<Port>() != null) return;
            if (ve.GetFirstAncestorOfType<Edge>() != null) return;
            if (ve.GetFirstAncestorOfType<StoryGroupView>() != null) return;
            if (ve.GetFirstAncestorOfType<StickyNote>() != null) return;

            // 命中画布本身 / 网格背景 / contentViewContainer → 视为点击空白，取消全部选择
            if (ve is GridBackground || ve == contentViewContainer || ve == this ||
                ve.GetFirstAncestorOfType<GridBackground>() != null)
            {
                ClearSelection();
                evt.StopPropagation();
            }
        }

        /// <summary>在指定画布坐标创建节点；若 sourcePort / fromNode 非空则自动连线（端口拖拽 / 选中节点 Space 两种入口）。</summary>
        public void SpawnNodeWithConnection(Type t, Vector2 contentLocal, Port sourcePort, StoryNodeView fromNode)
        {
            var newId = RequestAddNode(t, contentLocal);
            if (string.IsNullOrEmpty(newId)) return;

            // 节点视图在 OnModelChanged → ScheduleRebuild 中延迟到下一帧 Populate，故连线也顺延到重建之后；
            // 且重建会重建所有端口视图，不能持有旧的 sourcePort 引用 —— 这里只捕获「身份」（节点/端口 ID），
            // 待重建完成后再按 ID 重新解析出最新端口视图。
            string srcNodeId = null, srcPortId = null;
            Direction srcDir = Direction.Input;
            if (sourcePort != null)
            {
                var sv = sourcePort.node as StoryNodeView;
                srcNodeId = sv?.NodeId;
                srcPortId = (string)sourcePort.userData;
                srcDir = sourcePort.direction;
            }
            string fromNodeId = fromNode?.NodeId;

            this.schedule.Execute(() =>
            {
                var nv = GetNodeView(newId);
                if (nv == null) return;
                if (srcNodeId != null && srcPortId != null)
                {
                    // 端口拖拽创建：新节点用反向端口接住源端口
                    var sv = GetNodeView(srcNodeId);
                    var sp = sv?.GetPort(srcPortId);
                    if (sp != null)
                    {
                        var wantDir = srcDir == Direction.Output ? Direction.Input : Direction.Output;
                        var target = PickConnectPort(nv, wantDir);
                        if (target != null) OnPortsConnected(sp, target);
                    }
                }
                else if (fromNodeId != null)
                {
                    // 选中节点按 Space：从选中节点主输出连入新节点主输入（两端都有合适端口才连）
                    var fv = GetNodeView(fromNodeId);
                    var outPort = fv != null ? PickConnectPort(fv, Direction.Output) : null;
                    var inPort = PickConnectPort(nv, Direction.Input);
                    if (outPort != null && inPort != null) OnPortsConnected(outPort, inPort);
                }
            }).ExecuteLater(3);
        }

        private static Port PickConnectPort(StoryNodeView nv, Direction dir)
            => nv.Query<Port>().ToList().FirstOrDefault(p => p.direction == dir);

        /// <summary>弹出节点创建搜索窗（文档 02 三大建节点入口：端口拖拽松手 / Space）。</summary>
        public void OpenNodeSearch(Vector2 screenPos, Vector2 contentLocal, Port sourcePort = null, StoryNodeView fromNode = null)
        {
            var provider = ScriptableObject.CreateInstance<StoryNodeSearchProvider>();
            provider.graphView = this;
            provider.sourcePort = sourcePort;
            provider.fromNode = fromNode;
            provider.contentLocal = contentLocal;
            SearchWindow.Open(new SearchWindowContext(screenPos), provider);
        }

        public void SelectNode(StoryNodeView nv)
        {
            ClearSelection();
            AddToSelection(nv);
            nv.BringToFront();
        }

        public IEnumerable<StoryNodeView> SelectedNodeViews() => selection.OfType<StoryNodeView>();

        /// <summary>统一在选择操作后触发 SelectionChanged，避免依赖各版本 GraphView / ClickSelector 对
        /// OnSelected/OnUnselected 的调用时机。无论内置选择器、代码调用还是快捷键清空/增选，都会刷新面板。
        /// 使用 _selectionChangeDepth 抑制 ClearSelection 内部递归 RemoveFromSelection 造成的重复事件。</summary>
        public override void ClearSelection()
        {
            _selectionChangeDepth++;
            base.ClearSelection();
            _selectionChangeDepth--;
            NotifySelectionChanged();
        }

        public override void AddToSelection(ISelectable selectable)
        {
            if (selection.Contains(selectable)) return;   // 已选中：基类无操作，避免重复刷新
            _selectionChangeDepth++;
            base.AddToSelection(selectable);
            _selectionChangeDepth--;
            NotifySelectionChanged();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            if (!selection.Contains(selectable)) return;  // 未选中：基类无操作，避免重复刷新
            _selectionChangeDepth++;
            base.RemoveFromSelection(selectable);
            _selectionChangeDepth--;
            NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            if (_selectionChangeDepth == 0)
                SelectionChanged?.Invoke();
        }

        public StoryNodeView GetNodeView(string id) => _nodeViews.TryGetValue(id, out var nv) ? nv : null;

        public StoryGroupView GetGroupView(string id) => _groupViews.FirstOrDefault(g => g.GroupId == id);

        public IEnumerable<StoryGroupView> GetChildGroupViews(string id) => _groupViews.Where(g => g.Data.parentGroupId == id);

        /// <summary>分组嵌套深度（顶层为 0）。</summary>
        internal int GetGroupDepth(StoryGroup g)
        {
            int d = 0;
            int guard = 0;
            var cur = g;
            while (!string.IsNullOrEmpty(cur.parentGroupId) && guard++ < 64)
            {
                var p = _model?.Asset.groups.Find(x => x.id == cur.parentGroupId);
                if (p == null) break;
                d++;
                cur = p;
            }
            return d;
        }

        /// <summary>收集某组及其所有后代分组里的全部节点 ID（含子分组里的节点），用于拖拽时整体平移。</summary>
        public List<string> GetAllDescendantNodeIds(string groupId)
        {
            var result = new List<string>();
            var stack = new Stack<string>();
            stack.Push(groupId);
            int guard = 0;
            while (stack.Count > 0 && guard++ < 256)
            {
                var id = stack.Pop();
                var gv = GetGroupView(id);
                if (gv == null) continue;
                result.AddRange(gv.Data.nodeIds);
                foreach (var c in GetChildGroupViews(id)) stack.Push(c.GroupId);
            }
            return result;
        }

        /// <summary>重算某组子树（自底向上）再向上冒泡到所有祖先，保证嵌套框整体跟随节点/子框移动。</summary>
        public void RefitGroupTree(string groupId)
        {
            var sub = new List<StoryGroupView>();
            var stack = new Stack<string>();
            stack.Push(groupId);
            int guard = 0;
            while (stack.Count > 0 && guard++ < 256)
            {
                var id = stack.Pop();
                var gv = GetGroupView(id);
                if (gv == null) continue;
                sub.Add(gv);
                foreach (var c in GetChildGroupViews(id)) stack.Push(c.GroupId);
            }
            // 自底向上：后代先于祖先 refit（父框依赖已更新的子框）。
            sub.Sort((a, b) => GetGroupDepth(b.Data).CompareTo(GetGroupDepth(a.Data)));
            foreach (var gv in sub) gv.RefitToMembers();
            // 向上冒泡到根。
            var pid = GetGroupView(groupId)?.Data.parentGroupId;
            guard = 0;
            while (!string.IsNullOrEmpty(pid) && guard++ < 64)
            {
                var pg = GetGroupView(pid);
                if (pg == null) break;
                pg.RefitToMembers();
                pid = pg.Data.parentGroupId;
            }
        }

        /// <summary>刷新全部节点视图的标题/摘要（如变量改名后，节点摘要需同步显示新变量名）。不重建连线，保留选中与视图变换。</summary>
        public void RefreshNodeSummaries()
        {
            foreach (var nv in _nodeViews.Values) nv.Refresh();
        }

        public IEnumerable<StoryNodeView> AllNodeViews() => _nodeViews.Values;

        /// <summary>不可达节点整体 50% 透明（02 §二 节点状态叠加样式）。每次重建后按可达性重算。</summary>
        public void ApplyUnreachable()
        {
            if (_model == null) return;
            var unreachable = StoryValidator.GetUnreachableNodeIds(_model);
            foreach (var kv in _nodeViews)
                kv.Value.SetUnreachable(unreachable.Contains(kv.Key));
        }

        /// <summary>试跑高亮：清除全部播放标记后，选中并滚动到目标节点、加蓝色播放边框。</summary>
        public void HighlightPlayback(string id)
        {
            foreach (var nodeView in _nodeViews.Values) nodeView.ClearPlayback();
            var target = GetNodeView(id);
            if (target != null)
            {
                SelectNode(target);
                FrameNode(target);
                target.MarkPlayback();
            }
        }

        /// <summary>清除全部试跑高亮（试跑窗口关闭时调用）。</summary>
        public void ClearPlaybackHighlight()
        {
            foreach (var nodeView in _nodeViews.Values) nodeView.ClearPlayback();
            RestoreFlowEdges();
            _playbackPath = null;
            StopFlow();
        }

        /// <summary>试跑路径连线流动：把走过的连线染青色，并在原 EdgeControl 上叠加沿线流动的亮点，表示剧情流向。
        /// 青色常量统一用 FlowLineDraw.FlowColor（handler 内直接描边，不依赖 EdgeControl.inputColor/outputColor 重绘）。</summary>
        private sealed class FlowEntry
        {
            public Edge edge;
            public Action<MeshGenerationContext> handler;
            public Color inColor;
            public Color outColor;
        }
        private readonly List<FlowEntry> _flowEdges = new List<FlowEntry>();
        private IVisualElementScheduledItem _flowTicker;
        private float _flowTime;
        /// <summary>最近一次试跑路径（节点序列）。选中/重建会让 GraphView 重建 EdgeControl 实例，
        /// 故保留路径以便在落定后重新敷色，避免最新（含最后两条）连线丢失染色。</summary>
        private List<string> _playbackPath;

        /// <summary>根据走过的节点序列，把相邻节点之间的连线染青色，并在原 EdgeControl 上挂流动亮点绘制。
        /// 选中/取景新节点会令 GraphView 重建所连 EdgeControl 实例，导致刚染色的连线丢失；故记录路径并
        /// 在下一帧延迟重敷一次，确保最新（含最后两条）连线也变青。</summary>
        public void SetPlaybackPath(List<string> nodeIds)
        {
            _playbackPath = nodeIds != null ? new List<string>(nodeIds) : null;
            ApplyFlowColors();
            if (_playbackPath != null && _playbackPath.Count >= 2)
                this.schedule.Execute(ApplyFlowColors).ExecuteLater(30);
        }

        /// <summary>按记录路径重敷流动连线（与选中/重建后的新 EdgeControl 实例对齐）。</summary>
        private void ApplyFlowColors()
        {
            RestoreFlowEdges();

            if (_playbackPath == null || _playbackPath.Count < 2) { StopFlow(); return; }

            for (int i = 0; i < _playbackPath.Count - 1; i++)
            {
                var from = _playbackPath[i];
                var to = _playbackPath[i + 1];
                foreach (var e in edges)
                {
                    if (e.userData is StoryEdge se && se.fromNodeId == from && se.toNodeId == to)
                    {
                        // 默认 EdgeControl 染青色（inputColor/outputColor 在 2022.3 为可读写属性，作为兜底；
                        // 主青色由 FlowLineDraw.DrawDots 在本 handler 内直接描边，确保任何 2022.3 小版本都可见）
                        var origIn = e.edgeControl.inputColor;
                        var origOut = e.edgeControl.outputColor;
                        e.edgeControl.inputColor = FlowLineDraw.FlowColor;
                        e.edgeControl.outputColor = FlowLineDraw.FlowColor;

                        // 直接在「原连线自己的 EdgeControl」上叠加流动亮点：
                        // 订阅其 generateVisualContent 事件（2022.3 该成员是事件，非虚方法），
                        // EdgeControl 先画完青色线，本 handler 在同一坐标系接着画白点，与默认线完全重合。
                        Action<MeshGenerationContext> handler = ctx => FlowLineDraw.DrawDots(ctx, e, _flowTime);
                        e.edgeControl.generateVisualContent += handler;
                        e.edgeControl.MarkDirtyRepaint();

                        _flowEdges.Add(new FlowEntry { edge = e, handler = handler, inColor = origIn, outColor = origOut });
                        break;
                    }
                }
            }

            if (_flowEdges.Count > 0) StartFlow();
            else StopFlow();
        }

        /// <summary>取消流动绘制订阅并还原连线默认颜色。</summary>
        private void RestoreFlowEdges()
        {
            foreach (var fe in _flowEdges)
            {
                if (fe.edge != null && fe.edge.edgeControl != null)
                {
                    if (fe.handler != null) fe.edge.edgeControl.generateVisualContent -= fe.handler;
                    fe.edge.edgeControl.inputColor = fe.inColor;
                    fe.edge.edgeControl.outputColor = fe.outColor;
                    fe.edge.edgeControl.MarkDirtyRepaint();
                }
            }
            _flowEdges.Clear();
        }

        private void StartFlow()
        {
            if (_flowTicker == null)
                _flowTicker = this.schedule.Execute(FlowTick).Every(33);
            else
                _flowTicker.Resume();
        }

        private void StopFlow()
        {
            _flowTicker?.Pause();
        }

        private void FlowTick()
        {
            _flowTime += 0.05f;
            foreach (var fe in _flowEdges)
            {
                // 重绘原连线 EdgeControl，触发其 generateVisualContent（含我们的流动白点）重新生成
                if (fe.edge?.edgeControl?.panel == null) continue;
                if (fe.handler != null) fe.edge.edgeControl.MarkDirtyRepaint();
            }
        }

        /// <summary>将指定节点滚动到视图中心，保持当前缩放。本版本 GraphView 未暴露 ScrollTo/FrameSelected，
        /// 改用其受保护底层 API UpdateViewTransform 自实现（FrameSelected 内部亦基于此）。</summary>
        public void FrameNode(GraphElement element)
        {
            if (element == null || contentViewContainer == null) return;
            var zoom = contentViewContainer.transform.scale.x;
            if (zoom <= 0f) zoom = 1f;
            var center = element.GetPosition().center; // content-local 坐标
            var view = localBound.size;
            var pan = new Vector3(view.x * 0.5f - center.x * zoom, view.y * 0.5f - center.y * zoom, 0f);
            UpdateViewTransform(pan, new Vector3(zoom, zoom, 1f));
        }

        /// <summary>显示/隐藏迷你地图（B9）。anchored=true 将其锚定在画布角落，不随平移/缩放滚动；
        /// 用户可在画布内拖动它切换为浮动模式。默认不创建，首次开启时懒实例化。</summary>
        public void SetMiniMap(bool on)
        {
            if (on)
            {
                if (_miniMap == null)
                {
                    _miniMap = new MiniMap { maxWidth = 200, maxHeight = 150 };
                    _miniMap.anchored = true; // 锚定：钉在画布角落、不随平移缩放滚动
                }
                if (_miniMap.parent == null) Add(_miniMap);
            }
            else if (_miniMap != null && _miniMap.parent != null)
            {
                Remove(_miniMap);
            }
        }

        // ── 分组框 / 便签（B7）────────────────────────────────
        /// <summary>将当前选中节点打包为一个分组框（Ctrl+G）。未选中节点时不操作。</summary>
        public void GroupSelection()
        {
            if (_model == null) return;
            var sel = selection.OfType<StoryNodeView>().ToList();
            if (sel.Count == 0) return;
            var bounds = ComputeSelectionBounds(sel);
            _model.ExecuteCommand(new AddGroupCommand(sel.Select(v => v.NodeId).ToList(), bounds));
        }

        /// <summary>某节点位置变化后，重算包含它的分组外框（自底向上 + 向上冒泡），使嵌套框始终跟随（B7 外框跟随）。</summary>
        private void RefitGroupsForNode(string nodeId)
        {
            // 节点只属于最内层组：找到该组后，重算其整棵子树并向上冒泡到所有祖先。
            var g = _groupViews.FirstOrDefault(gv => gv.ContainsNode(nodeId));
            if (g != null) RefitGroupTree(g.GroupId);
        }

        /// <summary>在指定画布坐标创建一个默认大小的便签（右键菜单调用）。</summary>
        public void CreateStickyNote(Vector2 contentPos)
        {
            if (_model == null) return;
            var rect = new Rect(contentPos - new Vector2(100, 70), new Vector2(200, 140));
            _model.ExecuteCommand(new AddStickyNoteCommand(rect));
        }

        private static Rect ComputeSelectionBounds(IEnumerable<StoryNodeView> views)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in views)
            {
                var p = v.Data.position;
                minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x + 220f); maxY = Mathf.Max(maxY, p.y + 120f);
            }
            const float m = 30f;
            return new Rect(minX - m, minY - m, (maxX - minX) + 2 * m, (maxY - minY) + 2 * m);
        }

        private void PersistStickyNote(StickyNote note)
        {
            if (_rebuilding || _model == null || !(note.userData is string id)) return;
            var sn = _model.Asset.stickyNotes.Find(x => x.id == id);
            if (sn == null) return;
            sn.title = note.title;
            sn.text = note.contents;
            sn.theme = (int)note.theme;
            sn.rect = note.GetPosition();
            _model.Touch();
        }

        /// <summary>画布右键菜单：创建节点（置顶，按分类分组列出所有 [StoryNode] 类型）、新建便签（置顶）、将选中节点打包为分组。</summary>
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            var content = contentViewContainer.WorldToLocal(evt.mousePosition);
            // 节点生成在鼠标附近：以右键处作为节点左上角，向右下展开（不精确压住光标）。
            var nodeSpawn = content;

            // 置顶插入（前两项）：先用 InsertAction(0) 放「新建便签」，再把「创建节点」各子项反向 Insert(0)，
            // 使扁平顺序为「创建节点(组) / 新建便签 / 其余默认项」，且子菜单内分类/类型顺序不乱。
            evt.menu.InsertAction(0, "新建便签", _ => CreateStickyNote(content));

            var groups = NodeRegistry.Entries.GroupBy(e => e.Attr.Category).ToList();
            for (int gi = groups.Count - 1; gi >= 0; gi--)
            {
                var grp = groups[gi];
                var entries = grp.ToList();
                for (int ei = entries.Count - 1; ei >= 0; ei--)
                {
                    var e = entries[ei];
                    evt.menu.InsertAction(0, $"创建节点/{grp.Key}/{e.Attr.Title}",
                        _ => RequestAddNode(e.Type, nodeSpawn));
                }
            }

            if (selection.OfType<StoryNodeView>().Any())
                evt.menu.AppendAction("将选中节点打包为分组", _ => GroupSelection());
        }

        #region 端口拖拽连线（不依赖 EdgeConnector / EdgeDragHelper / EdgeConnectorListener，纯公开 API 实现）

        /// <summary>返回画布内所有端口，供落点命中测试。</summary>
        private IEnumerable<Port> GetAllPorts() => this.Query<Port>().ToList();

        /// <summary>两端口成功连接：经 CanConnect 校验后走 ConnectCommand（带 Undo、与数据一致）。</summary>
        private void OnPortsConnected(Port a, Port b)
        {
            var aView = a.node as StoryNodeView;
            var bView = b.node as StoryNodeView;
            if (aView == null || bView == null) return;

            // 归一化方向：a/b 谁为 out/in 不固定，按 Direction 区分；
            // NodeId 必须与端口归属一致（a 不一定是输出端口）。
            var (outPort, inPort) = a.direction == Direction.Output ? (a, b) : (b, a);

            // 子画布（剧情表）拦截：连线交由剧情表写回逻辑处理（选项跳转 / 行顺序），不落默认命令。
            if (ConnectInterceptor != null && ConnectInterceptor(outPort, inPort)) return;

            var fromView = outPort == a ? aView : bView;
            var toView = inPort == a ? aView : bView;
            var se = new StoryEdge
            {
                fromNodeId = fromView.NodeId,
                fromPortId = (string)outPort.userData,
                toNodeId = toView.NodeId,
                toPortId = (string)inPort.userData
            };
            // 单端口容量：落点前先断开该端口已有的连线（走命令，保证可撤销、数据一致）
            DisconnectIfSingle(outPort);
            DisconnectIfSingle(inPort);
            if (_model.CanConnect(se, out _))
                _model.ExecuteCommand(new ConnectCommand(se));
        }

        private void DisconnectIfSingle(Port port)
        {
            if (port.capacity != Port.Capacity.Single) return;
            foreach (var c in port.connections.ToList())
                if (c.userData is StoryEdge se)
                    _model.ExecuteCommand(new DisconnectCommand(se.fromNodeId, se.fromPortId, se.toNodeId, se.toPortId));
        }

        /// <summary>自包含端口拖拽连线 manipulator：仅用 UIToolkit 公开 API（Mouse 事件 + 端口 worldBound 命中测试），
        /// 不依赖本版本不可见的 EdgeConnectorListener / 抽象 EdgeConnector。</summary>
        private sealed class PortDragConnector : Manipulator
        {
            private readonly StoryGraphView _owner;
            private readonly Func<IEnumerable<Port>> _portProvider;
            private readonly Action<Port, Port> _onConnect;
            private Port _source;
            private bool _active;

            public PortDragConnector(StoryGraphView owner, Func<IEnumerable<Port>> portProvider, Action<Port, Port> onConnect)
            {
                _owner = owner;
                _portProvider = portProvider;
                _onConnect = onConnect;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<MouseDownEvent>(OnMouseDown);
                target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
                target.RegisterCallback<MouseUpEvent>(OnMouseUp);
                target.RegisterCallback<MouseCaptureOutEvent>(OnCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
                target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
                target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
                target.UnregisterCallback<MouseCaptureOutEvent>(OnCaptureOut);
            }

            private void OnMouseDown(MouseDownEvent e)
            {
                if (e.button != 0 || !(target is Port port)) return;
                _source = port;
                _active = true;
                port.CaptureMouse();
                e.StopPropagation();
            }

            private void OnMouseMove(MouseMoveEvent e)
            {
                if (!_active) return;
                // 暂不做橡皮筋预览；落点后由重建统一生成连线视觉
            }

            private void OnMouseUp(MouseUpEvent e)
            {
                if (!_active) return;
                _active = false;
                _source.ReleaseMouse();
                // localMousePosition 是相对源端口（捕获后 target 即源端口）的本地坐标，
                // 经 LocalToWorld 得到与 worldBound 同坐标系的世界坐标；
                // 切勿再对 e.mousePosition（已是世界坐标）做 LocalToWorld，否则会二次叠加 zoom/pan 而命中失效。
                var world = _source.LocalToWorld(e.localMousePosition);
                var targetPort = HitTestPort(world);
                if (targetPort != null)
                {
                    _onConnect?.Invoke(_source, targetPort);
                }
                else
                {
                    // 拖到空白处松手：弹搜索窗创建节点并自动连到源端口（文档 02 三大建节点入口之一）。
                    var screenPos = e.mousePosition;
                    var contentLocal = _owner.contentViewContainer.WorldToLocal(world);
                    _owner.OpenNodeSearch(screenPos, contentLocal, _source, null);
                }
                _source = null;
                e.StopPropagation();
            }

            /// <summary>命中测试：先精确命中端口圆点，未命中再放宽到端口所在节点（便于「拖到节点入口」即连）。</summary>
            private Port HitTestPort(Vector2 world)
            {
                Port hit = null;
                foreach (var p in _portProvider())
                {
                    if (p == _source || p.direction == _source.direction) continue;
                    if (p.worldBound.Contains(world)) { hit = p; break; }
                }
                if (hit != null) return hit;
                // 宽容判定：鼠标落在目标节点任一区域即可连到该节点第一个反向端口
                foreach (var p in _portProvider())
                {
                    if (p == _source || p.direction == _source.direction) continue;
                    if (p.node != null && p.node.worldBound.Contains(world)) { hit = p; break; }
                }
                return hit;
            }

            private void OnCaptureOut(MouseCaptureOutEvent e)
            {
                _active = false;
                _source = null;
            }
        }

        #endregion
    }
}
