using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 编辑期内存模型：持有 StoryGraphAsset，维护脏标记、节点/连线索引与变更事件，
    /// 并通过 IGraphCommand 统一执行修改、接入 Unity 原生 Undo。
    /// 视图层只读消费本模型，所有写操作走 Command。
    /// </summary>
    internal sealed class StoryGraphModel : IDisposable
    {
        public StoryGraphAsset Asset { get; }

        /// <summary>是否有未保存改动（保存后清零；撤销/重做后置位）。</summary>
        public bool IsDirty { get; private set; }

        /// <summary>图发生变更时触发（含命令执行与撤销/重做后）。</summary>
        public event Action<GraphChange> Changed;

        private readonly Dictionary<string, StoryNodeData> _nodeById = new Dictionary<string, StoryNodeData>();
        private readonly Dictionary<string, List<StoryEdge>> _outgoing = new Dictionary<string, List<StoryEdge>>();
        private readonly Dictionary<string, List<StoryEdge>> _incoming = new Dictionary<string, List<StoryEdge>>();

        public StoryGraphModel(StoryGraphAsset asset)
        {
            Asset = asset ? asset : throw new ArgumentNullException(nameof(asset));
            RebuildIndex();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public void Dispose() => Undo.undoRedoPerformed -= OnUndoRedo;

        // ── 索引 ──────────────────────────────────────────────
        public StoryNodeData GetNode(string id) => _nodeById.TryGetValue(id, out var n) ? n : null;
        public IReadOnlyCollection<StoryNodeData> Nodes => _nodeById.Values;
        public IReadOnlyList<StoryEdge> GetOutgoing(string id) => _outgoing.TryGetValue(id, out var l) ? l : Array.Empty<StoryEdge>();
        public IReadOnlyList<StoryEdge> GetIncoming(string id) => _incoming.TryGetValue(id, out var l) ? l : Array.Empty<StoryEdge>();

        public void RebuildIndex()
        {
            _nodeById.Clear();
            _outgoing.Clear();
            _incoming.Clear();
            foreach (var n in Asset.nodes)
                if (n != null) _nodeById[n.id] = n;
            foreach (var e in Asset.edges)
            {
                if (e == null) continue;
                if (!_outgoing.TryGetValue(e.fromNodeId, out var ol)) { ol = new List<StoryEdge>(); _outgoing[e.fromNodeId] = ol; }
                ol.Add(e);
                if (!_incoming.TryGetValue(e.toNodeId, out var il)) { il = new List<StoryEdge>(); _incoming[e.toNodeId] = il; }
                il.Add(e);
            }
        }

        // ── 入口/执行 ────────────────────────────────────────
        public StoryNodeData GetEntryNode() => Nodes.FirstOrDefault(n => n.IsEntry);

        /// <summary>执行一条命令：记录 Undo 由命令内部完成；这里标脏、按变更影响集维护索引并广播变更。</summary>
        public void ExecuteCommand(IGraphCommand cmd)
        {
            if (cmd == null) return;
            cmd.Execute(this);
            IsDirty = true;
            // 索引只关心 nodes/edges 的成员增减。已核验全部命令的不变量：
            // FieldChanged（字段编辑/列表项增删/批量编辑）不改成员 → 跳过全量重建（这是属性面板
            // 逐字段提交的最高频命令）；结构级（NodesAdded/Removed、EdgesChanged、Reset）与撤销重做仍全量重建。
            // （GraphChange 为 readonly struct，非空安全。）
            if (cmd.Change.Type != GraphChangeType.FieldChanged)
                RebuildIndex();
            SyncUsedCharacters();
            Changed?.Invoke(cmd.Change);
        }

        /// <summary>保存后置位脏标记（落盘由调用方显式完成，这里不再 SetDirty，避免触发 Unity 自动保存）。</summary>
        public void MarkSaved()
        {
            IsDirty = false;
        }

        /// <summary>轻量标脏（位置等高频拖拽改动，不进 Undo 栈）。仅置内存脏标记，不调用 SetDirty，
        /// 由显式 Save 才落盘，避免编辑中途被 Unity 自动保存（Auto Save/失焦序列化）写盘。</summary>
        public void Touch()
        {
            IsDirty = true;
        }

        /// <summary>数据经**非命令路径**（数值字段的原生序列化绑定 / 多选广播写值）被修改后调用：
        /// 置脏并广播 FieldChanged，使状态栏「未保存*」、hasUnsavedChanges 关闭确认与切换确认、
        /// 自动保存快照全部感知到改动。与 Touch 的区别：Touch 只置脏不广播（节点拖拽每帧高频），
        /// 本方法语义 = 一次字段数据变更完成（频率等同命令路径的键盘输入）。</summary>
        public void TouchData()
        {
            IsDirty = true;
            Changed?.Invoke(new GraphChange(GraphChangeType.FieldChanged));
        }

        // ── 连线合法性（供命令与拖线校验共用）────────────
        public bool CanConnect(StoryEdge e, out string reason)
        {
            reason = null;
            if (e.fromNodeId == e.toNodeId) { reason = "不能自连"; return false; }
            var from = GetNode(e.fromNodeId);
            var to = GetNode(e.toNodeId);
            if (from == null || to == null) { reason = "节点不存在"; return false; }
            if (!from.GetOutputPorts().Any(p => p.id == e.fromPortId)) { reason = "起点端口不是输出端口"; return false; }
            if (!to.GetInputPorts().Any(p => p.id == e.toPortId)) { reason = "终点端口不是输入端口"; return false; }
            // 变量数据线：赋值节点「变量」输入 / 条件子句「比较值」输入，只能接「获取变量」节点输出
            if (e.toPortId == "var_in" || e.toPortId.StartsWith("var_in_", StringComparison.Ordinal))
            {
                if (!(from is GetVariableNodeData)) { reason = "「变量」输入端口只能连接「获取变量」节点"; return false; }
            }
            if (GetOutgoing(e.fromNodeId).Any(x => x.fromPortId == e.fromPortId && x.toNodeId == e.toNodeId && x.toPortId == e.toPortId))
            { reason = "连线已存在"; return false; }
            return true;
        }

        // ── 反向引用统计（供左侧栏删除确认 / 校验）────────
        public int CountCharacterUsage(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return 0;
            int c = 0;
            foreach (var n in Nodes)
                if (n.GetType().GetFields(ReflectionUtil.BF_PublicInstance).Any(f => f.Name == "speakerId" && f.GetValue(n) is string s && s == characterId))
                    c++;
            return c;
        }

        public int CountVariableUsage(string variableId)
        {
            if (string.IsNullOrEmpty(variableId)) return 0;
            int c = 0;
            foreach (var n in Nodes)
                foreach (var f in n.GetType().GetFields(ReflectionUtil.BF_PublicInstance))
                {
                    if ((f.Name == "variableId" || f.Name == "conditionVariable") && f.GetValue(n) is string s && s == variableId)
                        c++;
                    // 递归进一层列表（覆盖 Choice.options、Condition.clauses 内的 variableId/conditionVariable）
                    else if (f.GetValue(n) is System.Collections.IList list)
                    {
                        foreach (var elem in list)
                        {
                            if (elem == null) continue;
                            foreach (var mf in elem.GetType().GetFields(ReflectionUtil.BF_PublicInstance))
                                if ((mf.Name == "variableId" || mf.Name == "conditionVariable") && mf.GetValue(elem) is string ms && ms == variableId)
                                    c++;
                            // 第二层：选项内嵌的条件组（ChoiceOption.conditionGroup -> ConditionClause.variableId）
                            var cg = elem.GetType().GetField("conditionGroup", ReflectionUtil.BF_PublicInstance);
                            if (cg != null && cg.GetValue(elem) is System.Collections.IList cgList)
                                foreach (var ce in cgList)
                                {
                                    if (ce == null) continue;
                                    foreach (var cf in ce.GetType().GetFields(ReflectionUtil.BF_PublicInstance))
                                        if ((cf.Name == "variableId" || cf.Name == "conditionVariable") && cf.GetValue(ce) is string cs && cs == variableId)
                                            c++;
                                }
                        }
                    }
                }
            return c;
        }

        // speakerId 字段反射缓存（按节点类型）：本方法每条命令后都会调用，原实现逐节点 GetFields
        // 全量反射是命令热路径的主要开销；缓存后同类型仅首次扫描。
        private static readonly Dictionary<Type, FieldInfo> _speakerFieldCache = new Dictionary<Type, FieldInfo>();

        private static FieldInfo GetSpeakerField(Type t)
        {
            if (!_speakerFieldCache.TryGetValue(t, out var f))
            {
                f = t.GetField("speakerId", ReflectionUtil.BF_PublicInstance);
                _speakerFieldCache[t] = f;
            }
            return f;
        }

        /// <summary>从所有节点的 speakerId 重算 Asset.usedCharacterIds（供导出/校验/反查）。
        /// 讲述者以 speakerId 引用角色资产；内置特殊讲述者（旁白/未知者/玩家自己）不计入。
        /// 每次命令与撤销重做后调用，保证该缓存始终准确。</summary>
        public void SyncUsedCharacters()
        {
            // 只计入「真实存在」的角色，剔除已删除/不存在的悬挂引用。
            // 否则 usedCharacterIds 会一直挂着已删角色的 ID（数据卫生问题），并让校验/导出带上无效引用。
            var libraryIds = new HashSet<string>(CharacterLibrary.All().Select(c => c.characterId));
            var set = new HashSet<string>();
            foreach (var n in Nodes)
            {
                var f = GetSpeakerField(n.GetType());
                if (f == null) continue;
                if (f.GetValue(n) is string s
                    && !string.IsNullOrEmpty(s) && s != StoryConstants.NarrationId
                    && s != StoryConstants.UnknownId && s != StoryConstants.SelfId
                    && libraryIds.Contains(s))
                    set.Add(s);
            }
            Asset.usedCharacterIds = set.ToList();
        }

        private void OnUndoRedo()
        {
            IsDirty = true;
            RebuildIndex();
            SyncUsedCharacters();
            Changed?.Invoke(new GraphChange(GraphChangeType.Reset));
        }
    }
}
