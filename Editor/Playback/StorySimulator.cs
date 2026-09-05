using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.Nodes;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Playback
{
    /// <summary>试跑状态机：Ready=停在普通节点可前进；AtChoice=停在选项节点；Finished=结束；Blocked=无后继死路。</summary>
    public enum SimState { Ready, AtChoice, Finished, Blocked }

    /// <summary>运行时变量值（按类型装箱，不可变）。</summary>
    public readonly struct SimVar
    {
        public readonly VariableType Type;
        public readonly object Value;
        public SimVar(VariableType type, object value) { Type = type; Value = value; }
        public int AsInt => Convert.ToInt32(Value);
        public float AsFloat => Convert.ToSingle(Value);
        public bool AsBool => Value is bool b ? b : Convert.ToInt32(Value) != 0;
        public string AsString => Value?.ToString() ?? "";
    }

    /// <summary>选项节点的一个可选项（含可见性，受显示条件约束）。</summary>
    public sealed class SimChoiceOption
    {
        public string OptionId;
        public string Text;
        public bool Visible;
        /// <summary>不可见时的原因说明（如 "HP >= 10"），用于试跑窗口提示玩家。</summary>
        public string ConditionText;
    }

    /// <summary>试跑路径中的一帧：到达某节点时的快照（变量为到达时的值，即本节点生效前）。</summary>
    internal sealed class SimFrame
    {
        public StoryNodeData Node;
        public string EffectText;
        public IReadOnlyDictionary<string, SimVar> Vars;
        public List<SimChoiceOption> Choices; // 仅选项节点非空
        public string ChosenOptionId; // 选项节点：玩家实际选择的 optionId（未选则为 null）
    }

    /// <summary>
    /// 编辑器内剧情试跑模拟器（纯逻辑，不进入 Play 模式）。
    /// 从指定节点沿连线遍历，维护变量表，支持前进 / 选项 / 回退 / 重置。
    /// 语义为运行时的「简化版」，用于编辑期快速验证逻辑与分支是否可达、变量是否按预期变化。
    /// </summary>
    internal sealed class StorySimulator
    {
        private readonly StoryGraphModel _model;
        private readonly Dictionary<string, SimVar> _vars = new Dictionary<string, SimVar>();
        private readonly List<SimFrame> _stack = new List<SimFrame>();
        private SimState _state = SimState.Ready;
        private string _pendingPort;
        private StoryNodeData _startNode;

        // 展开后的遍历视图：把每个「剧情表节点」按其虚拟子图（与 RuntimeStoryGraph.FromAsset 同构）替换，
        // 表节点本体不入表，其边界边（entry_/exit_）映射到头/尾虚拟对白，使试跑能逐行演练表内剧情。
        private Dictionary<string, StoryNodeData> _nodes;
        private Dictionary<string, List<StoryEdge>> _outgoing;

        public SimState State => _state;
        public StoryNodeData Current => _stack.Count > 0 ? _stack[_stack.Count - 1].Node : null;
        public SimFrame CurrentFrame => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public IReadOnlyList<SimFrame> Frames => _stack;
        public IReadOnlyDictionary<string, SimVar> Variables => _vars;

        public StorySimulator(StoryGraphModel model) => _model = model;

        public void Load(StoryNodeData startNode)
        {
            _startNode = startNode;
            _vars.Clear();
            _stack.Clear();
            // 本图局部变量先入（作为默认），全局变量资产随后补充（同 id 时本图覆盖全局）
            foreach (var v in _model.Asset.variables)
            {
                if (v == null || string.IsNullOrEmpty(v.id)) continue;
                _vars[v.id] = ParseVar(v);
            }
            var g = GlobalVariableLookup.GetAsset();
            if (g != null && g.variables != null)
                foreach (var v in g.variables)
                {
                    if (v == null || string.IsNullOrEmpty(v.id)) continue;
                    if (!_vars.ContainsKey(v.id)) _vars[v.id] = ParseVar(v);
                }
            _state = SimState.Ready;
            _pendingPort = null;
            BuildExpansion();
            // 入口若为「剧情表节点」（罕见，通常入口是 Start），从首条 head 行进入，演练表内剧情。
            var entry = startNode;
            if (entry is StoryTableNodeData tn && tn.tableAsset != null)
            {
                StoryTableSubGraph.ComputeHeadsTails(tn.tableAsset, tn.id, out var heads, out _);
                if (heads.Count > 0)
                {
                    var first = GetNode(StoryTableSubGraph.RowVirtualId(tn.tableAsset, tn.id, heads[0]));
                    if (first != null) entry = first;
                }
            }
            if (entry != null) Enter(entry);
        }

        public void Reset() => Load(_startNode);

        /// <summary>连续前进，直到停在选项 / 结束 / 死路（不自动替用户做选择）。</summary>
        public void Advance()
        {
            while (_state == SimState.Ready) Step();
        }

        /// <summary>从当前普通节点前进一步：先应用本节点副作用（如赋值），再沿 pending 端口连线进入下一节点。</summary>
        public void Step()
        {
            if (_state != SimState.Ready) return;
            if (string.IsNullOrEmpty(_pendingPort)) { _state = SimState.Blocked; return; }
            ApplyNodeEffect(Current);
            var edge = GetOutgoing(Current.id).FirstOrDefault(e => e.fromPortId == _pendingPort);
            if (edge == null) { _state = SimState.Blocked; return; }
            var next = GetNode(edge.toNodeId);
            if (next == null) { _state = SimState.Blocked; return; }
            Enter(next);
        }

        /// <summary>在选项节点选择一个可见选项（visIndex 为「可见选项」中的序号）。</summary>
        public void ChooseOption(int visIndex)
        {
            if (_state != SimState.AtChoice || CurrentFrame?.Choices == null) return;
            var vis = CurrentFrame.Choices.Where(c => c.Visible).ToList();
            if (visIndex < 0 || visIndex >= vis.Count) return;
            var opt = vis[visIndex];
            var edge = GetOutgoing(Current.id).FirstOrDefault(e => e.fromPortId == "opt_" + opt.OptionId);
            if (edge == null) { _state = SimState.Blocked; return; }
            var next = GetNode(edge.toNodeId);
            if (next == null) { _state = SimState.Blocked; return; }
            CurrentFrame.ChosenOptionId = opt.OptionId;
            Enter(next);
        }

        /// <summary>回退一步：恢复到达上一节点时的变量快照，并据此重算状态与后续端口。</summary>
        public void Back()
        {
            if (_stack.Count <= 1) return;
            _stack.RemoveAt(_stack.Count - 1);
            var top = _stack[_stack.Count - 1];
            _vars.Clear();
            foreach (var kv in top.Vars) _vars[kv.Key] = kv.Value;
            switch (top.Node)
            {
                case ChoiceNodeData _:
                    _state = SimState.AtChoice; _pendingPort = null; break;
                case EndNodeData _:
                    _state = SimState.Finished; _pendingPort = null; break;
                default:
                    _state = SimState.Ready;
                    _pendingPort = top.Node is ConditionNodeData c
                        ? (EvalCondition(c).result ? "true" : "false")
                        : top.Node.GetOutputPorts().Select(p => p.id).FirstOrDefault();
                    break;
            }
        }

        // ── 展开：把「剧情表节点」替换为虚拟子图 + 边界边（与 RuntimeStoryGraph.FromAsset 同构），
        //    使试跑能逐行演练表内对白与选项，而非把整张表当黑盒跳过。表节点本体不入 _nodes。──
        private void BuildExpansion()
        {
            _nodes = new Dictionary<string, StoryNodeData>();
            _outgoing = new Dictionary<string, List<StoryEdge>>();
            var tableNodeIds = new HashSet<string>();
            var tableNodes = new List<StoryTableNodeData>();
            foreach (var node in _model.Asset.nodes)
            {
                if (node is StoryTableNodeData tn)
                {
                    tableNodeIds.Add(tn.id);
                    tableNodes.Add(tn);
                }
                else if (node != null)
                {
                    _nodes[node.id] = node;
                }
            }
            // 主图边：两端均普通节点→原样；触表节点→映射为边界边（entry_/exit_ 端口 → 头/尾虚拟对白）
            foreach (var e in _model.Asset.edges)
            {
                if (e == null) continue;
                bool fromTable = tableNodeIds.Contains(e.fromNodeId);
                bool toTable = tableNodeIds.Contains(e.toNodeId);
                if (!fromTable && !toTable) { AddOutgoing(_outgoing, e); continue; }
                if (fromTable && toTable) continue; // 表内边不应出现在资产层
                if (toTable)
                {
                    var rowId = StripPrefix(e.toPortId, StoryTableSubGraph.EntryPrefix);
                    if (rowId == null) continue;
                    // 入口行映射：分支行 → 其「带文字」选择节点；纯对白行 → 对白节点（1 节点模型统一走 RowVirtualId）
                    var tn = tableNodes.FirstOrDefault(n => n.id == e.toNodeId);
                    var vid = tn?.tableAsset != null
                        ? StoryTableSubGraph.RowVirtualId(tn.tableAsset, e.toNodeId, rowId)
                        : StoryTableSubGraph.DialogueVirtualId(e.toNodeId, rowId);
                    AddOutgoing(_outgoing, new StoryEdge
                    {
                        fromNodeId = e.fromNodeId,
                        fromPortId = e.fromPortId,
                        toNodeId = vid,
                        toPortId = "in",
                    });
                }
                else
                {
                    if (e.fromPortId != null && e.fromPortId.StartsWith(StoryTableSubGraph.OptExitPrefix))
                    {
                        // 无连接编号的选项出口：optexit_{rowId}_{optionIndex} → Choice 虚拟节点对应 opt 端口 → 外部
                        var rest = e.fromPortId.Substring(StoryTableSubGraph.OptExitPrefix.Length);
                        int sep = rest.LastIndexOf('_');
                        if (sep <= 0) continue;
                        var rowId = rest.Substring(0, sep);
                        var optIdx = rest.Substring(sep + 1);
                        AddOutgoing(_outgoing, new StoryEdge
                        {
                            fromNodeId = StoryTableSubGraph.ChoiceVirtualId(e.fromNodeId, rowId),
                            fromPortId = "opt_" + optIdx,
                            toNodeId = e.toNodeId,
                            toPortId = e.toPortId,
                        });
                    }
                    else
                    {
                        var rowId = StripPrefix(e.fromPortId, StoryTableSubGraph.ExitPrefix);
                        if (rowId == null) continue;
                        AddOutgoing(_outgoing, new StoryEdge
                        {
                            fromNodeId = StoryTableSubGraph.DialogueVirtualId(e.fromNodeId, rowId),
                            fromPortId = "out",
                            toNodeId = e.toNodeId,
                            toPortId = e.toPortId,
                        });
                    }
                }
            }
            // 展开每个表节点为虚拟子图（虚拟对白/选项节点 + 内部边）
            foreach (var tn in tableNodes)
            {
                if (tn.tableAsset == null || tn.tableAsset.rows == null) continue;
                var sub = StoryTableSubGraph.Build(tn.tableAsset, tn.id, GuidOf(tn.tableAsset), tn);
                foreach (var vn in sub.nodes) _nodes[vn.id] = vn;
                foreach (var ve in sub.edges) AddOutgoing(_outgoing, ve);
            }
        }

        private static void AddOutgoing(Dictionary<string, List<StoryEdge>> map, StoryEdge e)
        {
            if (!map.TryGetValue(e.fromNodeId, out var list)) { list = new List<StoryEdge>(); map[e.fromNodeId] = list; }
            list.Add(e);
        }

        private static string StripPrefix(string s, string prefix)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith(prefix)) return null;
            var r = s.Substring(prefix.Length);
            return string.IsNullOrEmpty(r) ? null : r;
        }

        private static string GuidOf(StoryTableAsset t)
        {
            if (t == null) return null;
            var path = AssetDatabase.GetAssetPath(t);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        private StoryNodeData GetNode(string id) => _nodes != null && _nodes.TryGetValue(id, out var n) ? n : null;
        private IReadOnlyList<StoryEdge> GetOutgoing(string id) => _outgoing != null && _outgoing.TryGetValue(id, out var l) ? l : Array.Empty<StoryEdge>();

        // ── 内部：进入节点（仅做展示计算，不修改变量；变量修改统一在 Step 中进行）──
        private void Enter(StoryNodeData node)
        {
            var snapshot = new Dictionary<string, SimVar>(_vars);
            string effect = "";
            List<SimChoiceOption> choices = null;
            _pendingPort = null;

            switch (node)
            {
                case StartNodeData _:
                    effect = "剧情开始";
                    _pendingPort = "out";
                    break;
                case DialogueNodeData d:
                    // 表驱动：内容在行（唯一真相源），试跑预览从行取文本
                    string dlgSpk = d.speakerId;
                    string dlgText = d.text;
                    if (d.IsTableBound)
                    {
                        var row = StoryTableResolver.ResolveRow(d.tableBinding);
                        dlgSpk = row?.speaker ?? "";
                        dlgText = row?.text ?? "";
                    }
                    effect = $"{StoryConstants.SpeakerDisplayName(dlgSpk)}：{(string.IsNullOrEmpty(dlgText) ? "<空>" : dlgText)}";
                    _pendingPort = "out";
                    break;
                case EventNodeData ev:
                    effect = string.IsNullOrEmpty(ev.eventName) ? "<未命名事件>" : $"触发事件：{ev.eventName}";
                    _pendingPort = "out";
                    break;
                case SetVariableNodeData sv:
                    if (_vars.TryGetValue(sv.variableId ?? "", out var cur))
                    {
                        // 操作数：连线到「变量」输入端口（获取变量节点）时用端口值；未连线回落面板常量
                        var operand = TryGetPortSimVar(sv, "var_in", out var portVar)
                            ? portVar
                            : MakeSimVar(cur.Type, sv.value);
                        var nv = ApplyAssign(cur, sv.op, operand);
                        effect = $"{StoryConstants.VariableName(sv.variableId)} {AssignOpText(sv.op)} {ValueText(operand)} → {ValueText(nv)}（预览）";
                    }
                    else
                        effect = $"⚠ 变量未定义：{sv.variableId}（将跳过）";
                    _pendingPort = "out";
                    break;
                case ConditionNodeData c:
                    {
                        var (res, detail) = EvalCondition(c);
                        effect = detail; // detail 已是多行格式：首行「条件满足/不满足（All/Any）」，随后逐子句缩进 + ✓/×
                        _pendingPort = res ? "true" : "false";
                        break;
                    }
                case ChoiceNodeData ch:
                    // 带文字的选择节点：先展示所承载的对白（讲述者+正文），再展示选项
                    string chHeader = "";
                    if (ch.showText)
                    {
                        string cspk, ctxt;
                        if (ch.IsTableBound)
                        {
                            var crow = StoryTableResolver.ResolveRow(ch.tableBinding);
                            cspk = crow?.speaker ?? "";
                            ctxt = crow?.text ?? "";
                        }
                        else { cspk = ch.speakerId; ctxt = ch.text; }
                        chHeader = $"{StoryConstants.SpeakerDisplayName(string.IsNullOrEmpty(cspk) ? StoryConstants.NarrationId : cspk)}：{(string.IsNullOrEmpty(ctxt) ? "<空>" : ctxt.Replace("\n", " "))}\n";
                    }
                    choices = new List<SimChoiceOption>();
                    foreach (var o in ch.options)
                    {
                    bool vis = EvalOptionVisible(o);
                    string condText = null;
                    // 「受条件门控」= 带条件勾选 / 旧档残留单条件变量（取消勾选即视为无条件，忽略残留子句）
                    bool conditional = o.hasCondition || !string.IsNullOrEmpty(o.conditionVariable);
                    if (conditional && !vis)
                    {
                        o.EnsureMigrated();
                        condText = ConditionGroupText(o);
                    }
                        string optText;
                        if (ch.IsTableBound)
                        {
                            int oi = ch.options.IndexOf(o);
                            var row = StoryTableResolver.ResolveRow(ch.tableBinding);
                            var tbl = StoryTableResolver.ResolveTable(ch.tableBinding.tableAssetGuid);
                            var chChoice = StoryTableBaker.GetChoiceForOption(row, tbl, oi);
                            optText = chChoice?.text ?? "";
                        }
                        else
                            optText = o.text ?? "";
                        choices.Add(new SimChoiceOption
                        {
                            OptionId = o.optionId,
                            Text = string.IsNullOrEmpty(optText) ? "<选项>" : optText,
                            Visible = vis,
                            ConditionText = condText,
                        });
                    }
                    effect = chHeader + "请选择分支：";
                    _pendingPort = null;
                    break;
                case EndNodeData en:
                    effect = en.endType == EndType.JumpChapter
                        ? $"结束 → 跳转章节 {en.jumpToChapter}"
                        : "剧情结束";
                    _pendingPort = null;
                    break;
                case StoryTableNodeData _:
                    // 正常遍历由边界边直接落到虚拟子图，不会走到这里；仅作入口为表节点时的兜底。
                    effect = node.GetSummary();
                    _pendingPort = null;
                    break;
                default:
                    effect = node.GetSummary();
                    _pendingPort = node.GetOutputPorts().Select(p => p.id).FirstOrDefault();
                    break;
            }

            _state = node switch
            {
                ChoiceNodeData _ => SimState.AtChoice,
                EndNodeData _ => SimState.Finished,
                _ => SimState.Ready,
            };
            _stack.Add(new SimFrame { Node = node, EffectText = effect, Vars = snapshot, Choices = choices });
        }

        /// <summary>在 Step 中应用当前节点的副作用（目前只有赋值节点会改变量）。</summary>
        private void ApplyNodeEffect(StoryNodeData node)
        {
            if (node is SetVariableNodeData sv && _vars.TryGetValue(sv.variableId ?? "", out var cur))
                _vars[sv.variableId] = ApplyAssign(cur, sv.op,
                    TryGetPortSimVar(sv, "var_in", out var portVar) ? portVar : MakeSimVar(cur.Type, sv.value));
        }

        // ── 变量 / 条件 / 赋值 求值 ──
        private static SimVar ParseVar(StoryVariableDef v)
        {
            switch (v.type)
            {
                case VariableType.Int: return new SimVar(VariableType.Int, ParseInt(v.defaultValue));
                case VariableType.Float: return new SimVar(VariableType.Float, ParseDouble(v.defaultValue));
                case VariableType.Bool: return new SimVar(VariableType.Bool, ParseBool(v.defaultValue));
                default: return new SimVar(VariableType.String, v.defaultValue ?? "");
            }
        }

        private SimVar ApplyAssign(SimVar cur, AssignOp op, SimVar operand)
        {
            switch (cur.Type)
            {
                case VariableType.Int:
                    long a = operand.AsInt, v = cur.AsInt;
                    v = op switch
                    {
                        AssignOp.Add => v + a, AssignOp.Sub => v - a,
                        AssignOp.Mul => v * a, AssignOp.Div => a == 0 ? v : v / a,
                        _ => a
                    };
                    return new SimVar(VariableType.Int, (int)v);
                case VariableType.Float:
                    double fa = operand.AsFloat, fv = cur.AsFloat;
                    fv = op switch
                    {
                        AssignOp.Add => fv + fa, AssignOp.Sub => fv - fa,
                        AssignOp.Mul => fv * fa, AssignOp.Div => Math.Abs(fa) < 1e-9 ? fv : fv / fa,
                        _ => fa
                    };
                    return new SimVar(VariableType.Float, (float)fv);
                case VariableType.Bool:
                    return new SimVar(VariableType.Bool, op == AssignOp.Set ? operand.AsBool : cur.AsBool);
                default:
                    return new SimVar(VariableType.String, op == AssignOp.Set ? operand.AsString : cur.AsString);
            }
        }

        private (bool result, string detail) EvalCondition(ConditionNodeData c)
        {
            if (c.clauses == null || c.clauses.Count == 0)
                return (false, "条件不满足（无子句）");
            var combineText = c.combine == ConditionCombine.All ? "All" : "Any";
            bool combined;
            var clauseLines = new List<string>();
            if (c.combine == ConditionCombine.All)
            {
                combined = true;
                foreach (var cl in c.clauses)
                {
                    bool ok = _vars.TryGetValue(cl.variableId ?? "", out var v) && Compare(v, cl.op, ClauseOperand(c, cl, v));
                    clauseLines.Add($"  {StoryConstants.VariableName(cl.variableId)} {CompareOpText(cl.op)} {ClauseOperandText(c, cl, v)} {(ok ? "✓" : "×")}");
                    combined = combined && ok;
                }
            }
            else
            {
                combined = false;
                foreach (var cl in c.clauses)
                {
                    bool ok = _vars.TryGetValue(cl.variableId ?? "", out var v) && Compare(v, cl.op, ClauseOperand(c, cl, v));
                    clauseLines.Add($"  {StoryConstants.VariableName(cl.variableId)} {CompareOpText(cl.op)} {ClauseOperandText(c, cl, v)} {(ok ? "✓" : "×")}");
                    combined = combined || ok;
                }
            }
            var lines = new List<string>
            {
                $"条件{(combined ? "满足" : "不满足")}（{combineText}）",
            };
            lines.AddRange(clauseLines);
            return (combined, string.Join("\n", lines));
        }

        /// <summary>子句比较值 SimVar：端口（var_in_{clauseId} 接获取变量节点）优先，未连线回落子句常量（按左值类型解析）。</summary>
        private SimVar ClauseOperand(ConditionNodeData c, ConditionClause cl, SimVar left)
            => TryGetPortSimVar(c, "var_in_" + cl.clauseId, out var pv) ? pv : MakeSimVar(left.Type, cl.value);

        /// <summary>子句比较值显示文本（端口连线时显示来源变量名，否则常量）。</summary>
        private string ClauseOperandText(ConditionNodeData c, ConditionClause cl, SimVar left)
        {
            if (TryGetPortSimVar(c, "var_in_" + cl.clauseId, out var pv))
            {
                var from = FindPortSource(c, "var_in_" + cl.clauseId);
                return from is GetVariableNodeData gv && !string.IsNullOrEmpty(gv.variableId)
                    ? StoryConstants.VariableName(gv.variableId) + " → " + ValueText(pv)
                    : ValueText(pv);
            }
            return cl.value;
        }

        /// <summary>找连到 node.toPortId 输入端的源头节点（供显示/诊断）。</summary>
        private StoryNodeData FindPortSource(StoryNodeData node, string portId)
        {
            if (_outgoing == null || node == null) return null;
            foreach (var kv in _outgoing)
                foreach (var e in kv.Value)
                    if (e.toNodeId == node.id && e.toPortId == portId)
                        return GetNode(e.fromNodeId);
            return null;
        }

        private bool EvalOptionVisible(ChoiceOption o)
        {
            o.EnsureMigrated();
            // 「受条件门控」= 勾选了「带条件」，或旧档残留单条件变量；取消勾选即视为无条件、始终显示（忽略条件组里可能残留的子句）。
            bool conditional = o.hasCondition || !string.IsNullOrEmpty(o.conditionVariable);
            if (!conditional) return true;
            if (o.conditionGroup != null && o.conditionGroup.Count > 0)
                return EvaluateClauses(o.conditionGroup, o.conditionCombine).result;
            if (string.IsNullOrEmpty(o.conditionVariable)) return true;
            if (!_vars.TryGetValue(o.conditionVariable ?? "", out var v)) return false;
            return Compare(v, o.conditionOp, MakeSimVar(v.Type, o.conditionValue));
        }

        /// <summary>未满足时给试跑窗口的「未满足原因」文本：多条件组合（钥匙 且 HP>=10），旧档回退单条件。</summary>
        private string ConditionGroupText(ChoiceOption o)
        {
            if (o.conditionGroup != null && o.conditionGroup.Count > 0)
            {
                var join = o.conditionCombine == ConditionCombine.All ? " 且 " : " 或 ";
                return string.Join(join, o.conditionGroup.Select(cl =>
                    $"{StoryConstants.VariableName(cl.variableId)} {CompareOpText(cl.op)} {cl.value}"));
            }
            if (!string.IsNullOrEmpty(o.conditionVariable))
                return $"{StoryConstants.VariableName(o.conditionVariable)} {CompareOpText(o.conditionOp)} {o.conditionValue}";
            return "条件未满足";
        }

        /// <summary>通用：评估条件组（条件节点与选项门控共用同一套多条件逻辑）。</summary>
        private (bool result, string detail) EvaluateClauses(List<ConditionClause> clauses, ConditionCombine combine)
        {
            if (clauses == null || clauses.Count == 0)
                return (false, "条件不满足（无子句）");
            var combineText = combine == ConditionCombine.All ? "All" : "Any";
            bool combined;
            var clauseLines = new List<string>();
            if (combine == ConditionCombine.All)
            {
                combined = true;
                foreach (var cl in clauses)
                {
                    bool ok = _vars.TryGetValue(cl.variableId ?? "", out var v) && Compare(v, cl.op, MakeSimVar(v.Type, cl.value));
                    clauseLines.Add($"  {StoryConstants.VariableName(cl.variableId)} {CompareOpText(cl.op)} {cl.value} {(ok ? "✓" : "×")}");
                    combined = combined && ok;
                }
            }
            else
            {
                combined = false;
                foreach (var cl in clauses)
                {
                    bool ok = _vars.TryGetValue(cl.variableId ?? "", out var v) && Compare(v, cl.op, MakeSimVar(v.Type, cl.value));
                    clauseLines.Add($"  {StoryConstants.VariableName(cl.variableId)} {CompareOpText(cl.op)} {cl.value} {(ok ? "✓" : "×")}");
                    combined = combined || ok;
                }
            }
            var lines = new List<string>
            {
                $"条件{(combined ? "满足" : "不满足")}（{combineText}）",
            };
            lines.AddRange(clauseLines);
            return (combined, string.Join("\n", lines));
        }

        private bool Compare(SimVar left, CompareOp op, SimVar right)
        {
            switch (left.Type)
            {
                case VariableType.String:
                    var rs = right.AsString;
                    return op == CompareOp.Equal ? left.AsString == rs
                         : op == CompareOp.NotEqual ? left.AsString != rs
                         : false;
                case VariableType.Bool:
                    bool lb = left.AsBool, rb = right.AsBool;
                    return op == CompareOp.Equal ? lb == rb
                         : op == CompareOp.NotEqual ? lb != rb
                         : false;
                default:
                    double lv = left.Type == VariableType.Int ? left.AsInt : left.AsFloat;
                    double rv = right.Type == VariableType.Int ? right.AsInt : right.AsFloat;
                    return op switch
                    {
                        CompareOp.Equal => lv == rv,
                        CompareOp.NotEqual => lv != rv,
                        CompareOp.Greater => lv > rv,
                        CompareOp.GreaterEqual => lv >= rv,
                        CompareOp.Less => lv < rv,
                        CompareOp.LessEqual => lv <= rv,
                        _ => false
                    };
            }
        }

        /// <summary>读取「数据线」端口值：找到连到 node 的 toPortId 输入端的边，若源头是「获取变量」节点则返回其变量当前 SimVar。
        /// 无连线 / 源头不是获取变量节点 → 返回 false（调用方回落常量）。</summary>
        private bool TryGetPortSimVar(StoryNodeData node, string portId, out SimVar value)
        {
            value = default;
            if (_outgoing == null || node == null || string.IsNullOrEmpty(node.id)) return false;
            foreach (var kv in _outgoing)
                foreach (var e in kv.Value)
                    if (e.toNodeId == node.id && e.toPortId == portId)
                    {
                        var from = GetNode(e.fromNodeId);
                        if (from is GetVariableNodeData gv)
                            return _vars.TryGetValue(gv.variableId ?? "", out value);
                        return false; // 连了非获取变量节点：视为未连线
                    }
            return false;
        }

        /// <summary>按类型把常量字符串构造为 SimVar（与运行时 ValueParser.Parse 语义一致）。</summary>
        private static SimVar MakeSimVar(VariableType type, string raw)
        {
            switch (type)
            {
                case VariableType.Int: return new SimVar(VariableType.Int, ParseInt(raw));
                case VariableType.Float: return new SimVar(VariableType.Float, (float)ParseDouble(raw));
                case VariableType.Bool: return new SimVar(VariableType.Bool, ParseBool(raw));
                default: return new SimVar(VariableType.String, raw ?? "");
            }
        }

        private static string AssignOpText(AssignOp op) => op switch
        {
            AssignOp.Add => "+=", AssignOp.Sub => "-=",
            AssignOp.Mul => "*=", AssignOp.Div => "/=",
            _ => "=",
        };

        private static string CompareOpText(CompareOp op) => op switch
        {
            CompareOp.Equal => "==", CompareOp.NotEqual => "!=",
            CompareOp.Greater => ">", CompareOp.GreaterEqual => ">=",
            CompareOp.Less => "<", CompareOp.LessEqual => "<=",
            _ => "?",
        };

        private static string ValueText(SimVar v) => v.Type switch
        {
            VariableType.Bool => v.AsBool ? "true" : "false",
            VariableType.String => v.AsString,
            _ => v.Value.ToString(),
        };

        private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;
        private static double ParseDouble(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        private static bool ParseBool(string s)
        {
            if (bool.TryParse(s, out var b)) return b;
            return s == "1" || s == "true" || s == "True";
        }
    }
}
