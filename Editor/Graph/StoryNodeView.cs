using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.Nodes;
using MicrobialNet.Story.EditorTools.Validation;
using MicrobialNet.Story.EditorTools.Playback;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Graph
{
    /// <summary>
    /// 画布上的节点视图。只负责「渲染」模型中的一个 StoryNodeData 与它的端口，
    /// 不包含任何修改逻辑——所有改动都通过 StoryGraphView 走命令。
    /// </summary>
    public sealed class StoryNodeView : Node
    {
        /// <summary>
        /// 节点类型色兜底字典。GraphView 节点的标题/边框被引擎内置 USS 以高优先级锁死，
        /// 用户 USS（含 !important）无法稳定覆盖，故标题区域采用内联样式直接上色。
        /// 换节点配色只改此字典一处即可；其余编辑器配色仍走 StoryEditorTheme.uss 的 CSS 变量。
        /// 颜色值与 StoryEditorTheme.uss 的 --c-node-* 保持一致。
        /// </summary>
        private static readonly Dictionary<string, Color> NodeTypeColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "start",       new Color(99f / 255, 153f / 255, 34f / 255) },
            { "end",         new Color(163f / 255, 45f / 255, 45f / 255) },
            { "choice",      new Color(239f / 255, 159f / 255, 39f / 255) },
            { "dialogue",    new Color(55f / 255, 138f / 255, 221f / 255) },
            { "event",       new Color(136f / 255, 135f / 255, 128f / 255) },
            { "setvariable", new Color(29f / 255, 158f / 255, 117f / 255) },
            { "condition",   new Color(127f / 255, 119f / 255, 221f / 255) },
            { "comment",     new Color(136f / 255, 135f / 255, 128f / 255) },
        };

        private readonly StoryNodeData _data;
        private readonly Dictionary<string, Port> _ports = new Dictionary<string, Port>();
        private Label _summaryLabel;
        private Label _getVarLabel;
        private StoryGraphModel _model;
        private Vector2 _nodeDownPos;
        /// <summary>双击剧情表节点打开的误拖抑制锁：双击第二击常被当作拖拽开始，锁定期忽略 SetPosition（视图与数据都不动）。</summary>
        private bool _suppressDragWrite;

        /// <summary>节点位置被拖拽改变时触发，供画布标脏并落盘（不直接走命令，避免污染 Undo 栈）。</summary>
        public event Action OnPositionChanged;

        /// <summary>双击「剧情表」节点时触发，请求在主窗口之外打开其剧情表的子画布（纯渲染 + 编辑写回）。</summary>
        internal static event Action<StoryTableNodeData, StoryGraphModel> RequestOpenTableSubGraph;

        /// <summary>对应模型节点的稳定 ID。</summary>
        public string NodeId => _data.id;

        internal StoryNodeView(StoryNodeData data)
        {
            _data = data;

            // ── 标题与配色（节点类型色由 CSS 变量提供，按类型名加 sgw-node-* 类）──
            title = _data.DisplayTitle();
            var typeKey = _data.GetType().Name.Replace("NodeData", "").ToLowerInvariant();
            var nodeTypeClass = "sgw-node-" + typeKey;

            // GraphView 节点的可视边框实际渲染在 .mainContainer，标题背景渲染在 .titleContainer。
            // 把类型类同时加到根、mainContainer、titleContainer，USS 用高优先级 AND 选择器覆盖默认样式。
            AddToClassList(nodeTypeClass);
            mainContainer.AddToClassList("sgw-node-main");
            mainContainer.AddToClassList(nodeTypeClass);
            if (titleContainer != null)
            {
                titleContainer.AddToClassList("sgw-node-title");
                titleContainer.AddToClassList(nodeTypeClass);
            }

            AddToClassList("story-node-accent"); // 顶部强调边框宽度由 StoryNodeView.uss 提供

            // GraphView 内置 USS 以高优先级锁死节点标题/边框默认色，用户 USS（含 !important）无法
            // 稳定覆盖；故节点类型色改用内联样式直接上色。颜色集中在类级 NodeTypeColors 字典，
            // 换节点配色只改这一处即可。
            if (NodeTypeColors.TryGetValue(typeKey, out var accent))
            {
                if (titleContainer != null)
                {
                    titleContainer.style.backgroundColor = accent;
                    // 标题文字配色同样被 GraphView 引擎 USS 锁死，USS 路线（含 !important）无法稳定生效，
                    // 故直接内联设置白字 + 加粗，保证在彩色标题栏上清晰可读。
                    var titleLabel = titleContainer.Q<Label>(className: "title-label") ?? titleContainer.Q<Label>();
                    if (titleLabel != null)
                    {
                        titleLabel.style.color = Color.white;
                        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    }
                }
            }

            // ── 端口 ──
            foreach (var p in _data.GetInputPorts())
                AddPort(p, Direction.Input);
            foreach (var p in _data.GetOutputPorts())
                AddPort(p, Direction.Output);

            // ── 摘要 / 获取变量节点专属渲染 ──
            if (!(_data is GetVariableNodeData))
            {
                _summaryLabel = new Label(ResolveSummary(_data)) { name = "summary" };
                _summaryLabel.AddToClassList("story-node-summary"); // 换行/字号由 StoryNodeView.uss 提供
                mainContainer.Add(_summaryLabel);
            }
            else
            {
                // 获取变量节点：Shader Graph 风格——隐藏标题栏与 input/output 容器，输出端口移入主体
                // 与变量名横向排列（「| 变量名 端口 |」一行），整节点仅变量名一处文字；颜色按变量类型区分。
                if (titleContainer != null) titleContainer.style.display = DisplayStyle.None;
                if (inputContainer != null) inputContainer.style.display = DisplayStyle.None;
                // 输出端口（唯一，AddPort 已加入 outputContainer）移到 mainContainer 右侧同行
                // 先建 label 再放端口 → Row 内 label 在左、端口在右（「变量名  端口」）
                _getVarLabel = new Label(GetVarDisplayText()) { name = "getvar-label" };
                _getVarLabel.style.fontSize = 12;
                _getVarLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _getVarLabel.style.color = Color.white;
                _getVarLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                _getVarLabel.style.marginRight = 4;
                mainContainer.Add(_getVarLabel);
                if (outputContainer != null && outputContainer.childCount > 0)
                {
                    var p = outputContainer[0];
                    outputContainer.Remove(p);
                    mainContainer.Add(p);
                }
                if (outputContainer != null) outputContainer.style.display = DisplayStyle.None;
                ApplyGetVarStyle();
            }

            // 非可执行节点（如 Comment）视觉弱化；获取变量节点除外（数据源节点用专属样式）
            if (!_data.IsExecutable && !(_data is GetVariableNodeData))
                AddToClassList("comment-node");

            SetPosition(new Rect(_data.position, new Vector2(220, 120)));

            // 多选细节语义③：单击已处于多选集中的节点 → 折叠为单选该节点（访问点击的节点）。
            // MouseDown 仅记录位置；MouseUp 用 TrickleDown 注册，命中节点自身或任一子级都能触发；
            // 拖拽（位移 ≥ 阈值）不折叠，保证节点可正常拖动，且不干预 GraphView 默认 ClickSelector 的
            // Shift/Ctrl 增选与端口连线。
            this.RegisterCallback<MouseDownEvent>(OnNodeMouseDown);
            this.RegisterCallback<MouseUpEvent>(OnNodeMouseUp, TrickleDown.TrickleDown);
            this.RegisterCallback<MouseDownEvent>(OnNodeDoubleClick);
        }

        /// <summary>双击剧情表节点 → 打开子画布（MouseDown 的 clickCount 判定可靠）。
        /// 第二击按下会同时被当作拖拽起点：抑制本节点位置写回（_suppressDragWrite），
        /// 锁定期内 SetPosition 被忽略（节点视图与数据都不动），直到本次交互 MouseUp 解锁——双击不会把节点拖走。</summary>
        private void OnNodeDoubleClick(MouseDownEvent evt)
        {
            if (evt.clickCount < 2) return;
            if (!(_data is StoryTableNodeData tn)) return;
            _suppressDragWrite = true; // 双击第二击区间：抑制本节点位置写回（防误拖）
            RequestOpenTableSubGraph?.Invoke(tn, _model);
        }

        private void AddPort(NodePort np, Direction dir)
        {
            var port = Port.Create<Edge>(Orientation.Horizontal, dir, Port.Capacity.Multi, typeof(object));
            port.portName = ResolvePortLabel(np);
            port.userData = np.id;        // 端口 ID，连线时取用（拖拽连线在 StoryGraphView.Populate 中统一挂 PortDragConnector）
            _ports[np.id] = port;
            if (dir == Direction.Input) inputContainer.Add(port);
            else outputContainer.Add(port);
        }

        /// <summary>端口标签：表驱动选项节点的端口名从行内对应选项（按行内原始下标，含无连接编号的选项）取文本。</summary>
        private string ResolvePortLabel(NodePort np)
        {
            if (_data is ChoiceNodeData c && c.IsTableBound && np.id.StartsWith("opt_"))
            {
                var row = StoryTableResolver.ResolveRow(c.tableBinding);
                if (row != null)
                {
                    string optId = np.id.Substring("opt_".Length);
                    int idx = c.options.FindIndex(o => o.optionId == optId);
                    if (idx >= 0)
                    {
                        var table = StoryTableResolver.ResolveTable(c.tableBinding.tableAssetGuid);
                        var ch = StoryTableBaker.GetChoiceForOption(row, table, idx);
                        if (ch != null) return string.IsNullOrEmpty(ch.text) ? "<选项>" : ch.text;
                    }
                }
            }
            return np.label;
        }

        /// <summary>节点摘要：表驱动节点从绑定的源行取内容（方案A 下节点不冗余存内容）；
        /// 赋值/条件节点在「变量输入」端口连线时，把操作数/比较值显示为获取的变量名（常量值回落）；其余走原 GetSummary。</summary>
        private string ResolveSummary(StoryNodeData n)
        {
            if (n is SetVariableNodeData sv)
            {
                var opText = sv.op switch
                {
                    AssignOp.Add => "+=", AssignOp.Sub => "-=",
                    AssignOp.Mul => "*=", AssignOp.Div => "/=", _ => "=",
                };
                string operand = ResolvePortVarName(sv, "var_in") ?? sv.value;
                return $"{StoryConstants.VariableName(sv.variableId)} {opText} {operand}";
            }
            if (n is ConditionNodeData cond)
            {
                if (cond.clauses == null || cond.clauses.Count == 0) return "<无条件>";
                var join = cond.combine == ConditionCombine.All ? " 且 " : " 或 ";
                return string.Join(join, cond.clauses.Select(cl =>
                    $"{StoryConstants.VariableName(cl.variableId)} {CompareOpText(cl.op)} {ResolvePortVarName(cond, "var_in_" + cl.clauseId) ?? cl.value}"));
            }
            if (n is DialogueNodeData d && d.IsTableBound)
            {
                var row = StoryTableResolver.ResolveRow(d.tableBinding);
                var sp = StoryConstants.SpeakerDisplayName(string.IsNullOrEmpty(row?.speaker) ? StoryConstants.NarrationId : row?.speaker);
                var preview = string.IsNullOrEmpty(row?.text) ? "<空>" : row.text.Replace("\n", " ");
                return $"{sp}：{preview}";
            }
            if (n is ChoiceNodeData c && c.IsTableBound)
            {
                var row = StoryTableResolver.ResolveRow(c.tableBinding);
                if (row == null) return "<表格驱动选项>";
                var table = StoryTableResolver.ResolveTable(c.tableBinding.tableAssetGuid);
                var lines = new List<string>();
                // 分支行=「带文字」选择节点（1 节点模型）：先展示行内对白，再列选项
                if (c.showText)
                {
                    var sp = StoryConstants.SpeakerDisplayName(string.IsNullOrEmpty(row.speaker) ? StoryConstants.NarrationId : row.speaker);
                    lines.Add($"{sp}：{(string.IsNullOrEmpty(row.text) ? "<空>" : row.text.Replace("\n", " "))}");
                }
                for (int i = 0; i < c.options.Count; i++)
                {
                    var ch = StoryTableBaker.GetChoiceForOption(row, table, i);
                    if (ch == null) continue;
                    lines.Add("(选项) " + (string.IsNullOrEmpty(ch.text) ? "<选项>" : ch.text));
                }
                return lines.Count > 0 ? string.Join("\n", lines) : "<表格驱动选项>";
            }
            return n.GetSummary();
        }

        /// <summary>端口连线 → 获取变量节点显示名；无连线 / 源头不是获取变量节点返回 null（调用方回落常量）。</summary>
        private string ResolvePortVarName(StoryNodeData node, string portId)
        {
            if (_model == null || node == null) return null;
            foreach (var e in _model.GetIncoming(node.id))
            {
                if (e.toPortId != portId) continue;
                var from = _model.GetNode(e.fromNodeId);
                if (from is GetVariableNodeData gv && !string.IsNullOrEmpty(gv.variableId))
                    return StoryConstants.VariableName(gv.variableId);
            }
            return null;
        }

        /// <summary>获取变量节点主体显示文本：全局变量加 [全局] 前缀，未选择占位。</summary>
        private string GetVarDisplayText()
        {
            var gv = _data as GetVariableNodeData;
            if (gv == null || string.IsNullOrEmpty(gv.variableId)) return "<选择变量>";
            var (type, isGlobal) = GetVarInfo(gv.variableId);
            var name = StoryConstants.VariableName(gv.variableId);
            return isGlobal ? $"[全局] {name}" : name;
        }

        /// <summary>查变量的类型与是否全局（本图优先 → 全局资产）。无 _model/未定义 → (String, false)。</summary>
        private (VariableType type, bool isGlobal) GetVarInfo(string variableId)
        {
            if (string.IsNullOrEmpty(variableId) || _model?.Asset == null) return (VariableType.String, false);
            var local = _model.Asset.variables?.FirstOrDefault(v => v.id == variableId);
            if (local != null) return (local.type, false);
            var g = GlobalVariableLookup.GetAsset();
            var gv = g?.variables?.FirstOrDefault(v => v.id == variableId);
            if (gv != null) return (gv.type, true);
            return (VariableType.String, false);
        }

        /// <summary>类型→节点背景色（紧凑数据节点配色；未定义/未选择用灰）。</summary>
        private static Color GetVarNodeColor(VariableType type) => type switch
        {
            VariableType.Int => new Color(0.13f, 0.25f, 0.42f),
            VariableType.Float => new Color(0.13f, 0.40f, 0.45f),
            VariableType.Bool => new Color(0.35f, 0.20f, 0.45f),
            VariableType.String => new Color(0.18f, 0.40f, 0.22f),
            _ => new Color(0.30f, 0.30f, 0.30f),
        };

        /// <summary>获取变量节点样式应用：圆角/紧凑 padding + 按类型着色 + 文本更新。
        /// 构造时 _model 尚未注入也会安全调用（默认灰色 + 仅变量名），SetModel/Refresh 后再应用真实类型色与 [全局] 标记。</summary>
        private void ApplyGetVarStyle()
        {
            var gv = _data as GetVariableNodeData;
            if (gv == null) return;
            var (type, _) = GetVarInfo(gv.variableId ?? "");
            var color = GetVarNodeColor(type);
            mainContainer.style.backgroundColor = color;
            mainContainer.style.borderTopLeftRadius = 8;
            mainContainer.style.borderTopRightRadius = 8;
            mainContainer.style.borderBottomLeftRadius = 8;
            mainContainer.style.borderBottomRightRadius = 8;
            mainContainer.style.paddingTop = 4;
            mainContainer.style.paddingBottom = 4;
            mainContainer.style.paddingLeft = 8;
            mainContainer.style.paddingRight = 8;
            mainContainer.style.minWidth = 0;
            // 横向排列：变量名 + 输出端口同一行
            mainContainer.style.flexDirection = FlexDirection.Row;
            mainContainer.style.alignItems = Align.Center;
            SyncPortLabels(); // 构造时也清掉端口 label（Get 节点 out 端口隐藏文字仅留圆点）
            if (_getVarLabel != null) _getVarLabel.text = GetVarDisplayText();
        }

        private static string CompareOpText(CompareOp op) => op switch
        {
            CompareOp.Equal => "==", CompareOp.NotEqual => "!=",
            CompareOp.Greater => ">", CompareOp.GreaterEqual => ">=",
            CompareOp.Less => "<", CompareOp.LessEqual => "<=", _ => "?",
        };

        public Port GetPort(string portId) => _ports.TryGetValue(portId, out var p) ? p : null;

        /// <summary>节点数据被外部改动（如属性面板编辑）后，刷新标题、摘要与端口名。
        /// 端口集合的结构性变化（如选项增删）由 <see cref="HasPortSetChanged"/> 检测，交给画布重建。</summary>
        public void Refresh()
        {
            title = _data.DisplayTitle();
            if (_getVarLabel != null && _data is GetVariableNodeData)
                ApplyGetVarStyle(); // 文本 + 类型色（变量改了类型/全局标志同步）
            if (_summaryLabel != null)
                _summaryLabel.text = ResolveSummary(_data);
            SyncPortLabels();
        }

        /// <summary>把当前端口名同步为模型端口的 label（选项文本等改动后实时刷新，端口 ID 不变故连线不受影响）。</summary>
        private void SyncPortLabels()
        {
            foreach (var p in _data.GetInputPorts())
                if (_ports.TryGetValue(p.id, out var port))
                    port.portName = IsHiddenLabel(p) ? string.Empty : p.label;
            foreach (var p in _data.GetOutputPorts())
                if (_ports.TryGetValue(p.id, out var port))
                    port.portName = IsHiddenLabel(p) ? string.Empty : p.label;
        }

        /// <summary>获取变量节点紧凑样式——隐藏端口 label（只显示端口圆点），其他节点保留 label。</summary>
        private bool IsHiddenLabel(NodePort p) => _data is GetVariableNodeData && p.id == "out";

        /// <summary>检测端口集合是否与模型不一致（如选项增删导致输出端口数量/ID 变化）。
        /// 若变化，画布应重建该节点端口以同步「可接入节点」。</summary>
        public bool HasPortSetChanged()
        {
            int count = 0;
            foreach (var p in _data.GetInputPorts())
            {
                if (!_ports.ContainsKey(p.id)) return true;
                count++;
            }
            foreach (var p in _data.GetOutputPorts())
            {
                if (!_ports.ContainsKey(p.id)) return true;
                count++;
            }
            return count != _ports.Count;
        }

        internal StoryNodeData Data => _data;

        /// <summary>由画布在构建时注入模型，供右键「从此处试跑」打开试跑窗口。</summary>
        internal void SetModel(StoryGraphModel m)
        {
            _model = m;
            // Populate 顺序是 new StoryNodeView → SetModel：构造时 _model 未注入，摘要里变量端口查询回落常量。
            // 注入模型后统一 Refresh 重算——赋值/条件节点摘要显示「HP += 攻击力」、Get 节点应用类型色与 [全局] 标记。
            Refresh();
        }

        /// <summary>
        /// GraphView 在初始布局与拖拽结束时都会调用本方法。
        /// 拖拽产生的位置变化写回 _data.position，保证后续重建/保存后位置不丢；
        /// 仅当位置确实变化才触发 OnPositionChanged，避免载入时误标脏。
        /// </summary>
        public override void SetPosition(Rect newPos)
        {
            // 双击剧情表节点打开的误拖抑制：第二击 MouseDown 常被 SelectionDragger 当作拖拽开始，
            // 双击间鼠标微抖会让节点跟随鼠标移动——锁定期内忽略位置写回（视图与数据都不动），
            // 由单击（新交互）或 MouseUp（该次拖拽结束）解锁。
            if (_suppressDragWrite) return;
            base.SetPosition(newPos);
            if (_data.position != newPos.position)
            {
                _data.position = newPos.position;
                OnPositionChanged?.Invoke();
            }
        }

        public override void OnSelected()
        {
            base.OnSelected();
        }

        public override void OnUnselected()
        {
            base.OnUnselected();
        }

        /// <summary>记录左键按下位置，供 OnNodeMouseUp 区分「单击」与「拖拽」；clickCount==1 的新交互解除双击锁。</summary>
        private void OnNodeMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0) return;
            if (evt.clickCount == 1) _suppressDragWrite = false; // 新的单击交互：解除双击误拖锁定
            _nodeDownPos = evt.mousePosition;
        }

        /// <summary>多选细节语义③：单击已处于多选集中的节点 → 折叠为单选该节点（访问点击的节点）。
        /// 仅在「单击」（位移 &lt; 阈值、无修饰键）且本节点当前正处于多选集中时折叠；拖拽不折叠（保持节点可拖动），
        /// Shift/Ctrl/⌘ 增选与端口连线交给 GraphView 默认行为。</summary>
        private void OnNodeMouseUp(MouseUpEvent evt)
        {
            if (evt.button != 0) return;
            _suppressDragWrite = false; // 本次交互（含双击第二击）结束：解锁，后续拖动恢复正常
            if (evt.shiftKey || evt.ctrlKey || evt.commandKey) return; // 修饰键增选：保留默认行为
            if (Vector2.Distance(_nodeDownPos, evt.mousePosition) >= 6f) return; // 拖拽 → 不折叠

            var gv = this.GetFirstAncestorOfType<StoryGraphView>();
            if (gv == null) return;

            // 本节点当前处于多选集中（含自身的选择数 &gt; 1）→ 折叠为单选本节点，访问点击的节点
            int selectedNodeCount = 0;
            foreach (var e in gv.selection)
                if (e is StoryNodeView) selectedNodeCount++;
            if (this.selected && selectedNodeCount > 1)
            {
                gv.ClearSelection();
                gv.AddToSelection(this);
                evt.StopPropagation();
            }
        }

        /// <summary>右键菜单：在选中节点处启动编辑器内试跑（Play From Here）。</summary>
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            if (_model != null)
                evt.menu.AppendAction("从此处试跑", _ => StoryPlaybackWindow.Open(_model, _data));
        }

        /// <summary>校验高亮：左侧边框红(错误)/黄(警告)。不影响顶部类型配色。</summary>
        public void MarkValidation(ValidationSeverity severity)
        {
            ClearValidation();
            AddToClassList(severity == ValidationSeverity.Error ? "node-invalid-error" : "node-invalid-warning");
        }

        /// <summary>清除校验高亮（含左侧边框）。</summary>
        public void ClearValidation()
        {
            RemoveFromClassList("node-invalid-error");
            RemoveFromClassList("node-invalid-warning");
        }

        /// <summary>试跑高亮：右侧蓝色边框（与校验的左侧红/黄高亮区分）。</summary>
        public void MarkPlayback()
        {
            ClearPlayback();
            AddToClassList("node-playback");
        }

        public void ClearPlayback()
        {
            RemoveFromClassList("node-playback");
        }

        private bool _unreachable;

        /// <summary>不可达节点视觉弱化：整体 50% 透明（02 §二 节点状态叠加样式）。</summary>
        public void SetUnreachable(bool unreachable)
        {
            if (_unreachable == unreachable) return;
            _unreachable = unreachable;
            if (unreachable) AddToClassList("node-unreachable");
            else RemoveFromClassList("node-unreachable");
        }
    }
}
