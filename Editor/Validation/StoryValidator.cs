using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Validation;
using MicrobialNet.Story.Nodes;
using Newtonsoft.Json.Linq;

namespace MicrobialNet.Story.EditorTools.Validation
{
    /// <summary>
    /// 剧情图静态校验器（编辑期）。输入 StoryGraphModel，产出问题列表。
    /// 规则覆盖文档规划：无入口/多入口、无出口、不可达、选项无跳转、引用缺失（变量/角色）、
    /// 空文本、条件缺分支、变量默认值类型不匹配、循环（无终点）。
    /// 纯逻辑、无 UI 依赖，便于单元复用与未来命令行校验。
    /// </summary>
    internal static class StoryValidator
    {
        /// <summary>校验剧情图。
        /// 变量「已定义」域 = 本图变量 + 全局变量（GlobalVariables.asset）——与运行时一致
        /// （StoryFlow 构造变量 provider 时两者都 seed，见 StoryFlow.LoadGraph；试跑模拟器同口径并入全局表）。
        /// <param name="globalVars">全局变量定义注入（测试用）；null = 自动查找工程标准全局表。</param>
        /// <param name="eventNameMismatches">「[StoryEvent] 特性名 ≠ IStoryEvent.EventName」黄条文案注入（测试用）；
        /// null = 自动取 StoryEventCatalog 全工程扫描。图内无事件节点时不消费（见 3.5 图级规则）。</param>
        public static List<ValidationIssue> Validate(StoryGraphModel model, IEnumerable<StoryVariableDef> globalVars = null,
            IEnumerable<string> eventNameMismatches = null)
        {
            var issues = new List<ValidationIssue>();
            if (model == null || model.Asset == null) return issues;

            var nodes = model.Nodes.ToList();
            var asset = model.Asset;
            if (globalVars == null)
            {
                var g = GlobalVariableLookup.GetAsset();
                globalVars = g != null ? g.variables : null;
            }
            var globalList = (globalVars as List<StoryVariableDef>) ?? (globalVars ?? Enumerable.Empty<StoryVariableDef>()).ToList();
            // 同 id 时本图优先（赋值类型检查的 def 查找顺序体现：先本图后全局）。
            var varIds = new HashSet<string>(asset.variables.Where(v => !string.IsNullOrEmpty(v.id)).Select(v => v.id));
            foreach (var gv in globalList)
                if (!string.IsNullOrEmpty(gv.id)) varIds.Add(gv.id);
            // 角色存在性以「角色库」为权威来源，而非 usedCharacterIds（后者由节点 speakerId 反推，
            // 会自洽地包含已删角色的悬挂引用，导致下面的 MissingChar 永远触发不了）。
            var existingCharIds = new HashSet<string>(CharacterLibrary.All().Select(c => c.characterId));

            // 1. 入口检查
            var entries = nodes.Where(n => n.IsEntry).ToList();
            if (entries.Count == 0)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "NoEntry", "缺少开始(Start)节点，剧情无法运行。"));
            else if (entries.Count > 1)
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "MultiEntry", "存在多个开始节点，仅第一个会被执行。", entries[1].id));

            // 2. 变量默认值类型不匹配（图级）
            foreach (var v in asset.variables)
            {
                if (string.IsNullOrWhiteSpace(v.defaultValue)) continue;
                switch (v.type)
                {
                    case VariableType.Int:
                        if (!int.TryParse(v.defaultValue, out _))
                            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "VarDefaultMismatch", $"变量「{v.name}」(整数)的默认值「{v.defaultValue}」不是合法整数。"));
                        break;
                    case VariableType.Float:
                        if (!float.TryParse(v.defaultValue, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "VarDefaultMismatch", $"变量「{v.name}」(浮点)的默认值「{v.defaultValue}」不是合法数字。"));
                        break;
                    case VariableType.Bool:
                        if (!bool.TryParse(v.defaultValue, out _))
                            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "VarDefaultMismatch", $"变量「{v.name}」(布尔)的默认值「{v.defaultValue}」不是 true/false。"));
                        break;
                }
            }

            // 3. 节点级校验
            foreach (var n in nodes)
                ValidateNode(model, n, varIds, existingCharIds, asset, globalList, issues);

            // 3.7 变量数据线约束：赋值「变量」输入 / 条件子句「比较值」输入只接「获取变量」节点输出
            foreach (var e in asset.edges)
            {
                if (e.toPortId == "var_in" || (e.toPortId ?? "").StartsWith("var_in_", StringComparison.Ordinal))
                {
                    var from = nodes.FirstOrDefault(n => n.id == e.fromNodeId);
                    if (!(from is GetVariableNodeData))
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, "BadVarEdge",
                            $"「变量」输入端口只能连接「获取变量」节点（当前连接：{from?.GetType().Name ?? "?"}）。", e.toNodeId));
                }
            }

            // 3.5 事件名双写一致性（图级，P5/L1）：[StoryEvent] 特性名与 IStoryEvent.EventName 漂移会让
            // 「下拉能选到、运行时查无」——未注册事件经 StoryEventBus.Raise 静默直通（设计上不卡死剧情），
            // 事件被吞且零日志。仅含事件节点的图提示（避免无关图噪音）；null = 自动取 StoryEventCatalog。
            if (nodes.Any(n => n is EventNodeData))
            {
                var mismatches = eventNameMismatches ?? StoryEventCatalog.GetAttributeNameMismatches();
                foreach (var m in mismatches)
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EventNameMismatch", m));
            }

            // 4. 可达性（从入口 BFS）
            if (entries.Count > 0)
            {
                var reachable = GetReachableNodeIds(model);
                foreach (var n in nodes)
                    if (n.IsExecutable && !n.IsEntry && !reachable.Contains(n.id))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "Unreachable", $"节点「{n.DisplayTitle()}」从入口无法到达（可能是死分支或孤立节点）。", n.id));
            }

            // 5. 循环检测（无终点循环）
            DetectCycles(model, nodes, issues);

            issues.Sort((a, b) => SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity)));
            return issues;
        }

        /// <summary>从入口 BFS 求可达节点集合（含入口）。无入口时返回空集。</summary>
        public static HashSet<string> GetReachableNodeIds(StoryGraphModel model)
        {
            var reachable = new HashSet<string>();
            if (model == null) return reachable;
            var entries = model.Nodes.Where(n => n.IsEntry).ToList();
            if (entries.Count == 0) return reachable;
            var queue = new Queue<string>();
            foreach (var e in entries) { reachable.Add(e.id); queue.Enqueue(e.id); }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var edge in model.GetOutgoing(cur))
                    if (edge != null && !string.IsNullOrEmpty(edge.toNodeId) && reachable.Add(edge.toNodeId))
                        queue.Enqueue(edge.toNodeId);
            }
            return reachable;
        }

        /// <summary>返回从入口不可达的可执行节点集合（用于节点视图 50% 透明等视觉反馈）。
        /// 无入口时不标记（交给 NoEntry 错误），避免把整图节点都淡化。</summary>
        public static HashSet<string> GetUnreachableNodeIds(StoryGraphModel model)
        {
            var result = new HashSet<string>();
            if (model == null) return result;
            var entries = model.Nodes.Where(n => n.IsEntry).ToList();
            if (entries.Count == 0) return result;
            var reachable = GetReachableNodeIds(model);
            foreach (var n in model.Nodes)
                if (n.IsExecutable && !n.IsEntry && !reachable.Contains(n.id))
                    result.Add(n.id);
            return result;
        }

        private static int SeverityRank(ValidationSeverity s)
        {
            switch (s)
            {
                case ValidationSeverity.Error: return 0;
                case ValidationSeverity.Warning: return 1;
                default: return 2;
            }
        }

        private static bool HasOutEdge(StoryGraphModel model, string nodeId, string portId)
            => model.GetOutgoing(nodeId).Any(e => e != null && e.fromPortId == portId);

        private static void ValidateNode(StoryGraphModel model, StoryNodeData n, HashSet<string> varIds,
            HashSet<string> charIds, StoryGraphAsset asset, List<StoryVariableDef> globalVars, List<ValidationIssue> issues)
        {
            if (n is StartNodeData)
            {
                if (!HasOutEdge(model, n.id, "out"))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "NoOut", "开始节点没有后续连线。", n.id));
                return;
            }
            if (n is DialogueNodeData d)
            {
                // 表驱动：内容在 StoryTableAsset 行（唯一真相源），从行取文本/讲述者校验；节点本身不冗余存内容
                string text = d.text;
                string speakerId = d.speakerId;
                if (d.IsTableBound)
                {
                    var row = StoryTableResolver.ResolveRow(d.tableBinding);
                    text = row?.text ?? "";
                    speakerId = row?.speaker ?? "";
                }
                if (string.IsNullOrWhiteSpace(text))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EmptyText", "对话正文为空。", n.id));
                // G3：未设置讲述者（空白对话框风险）
                if (string.IsNullOrWhiteSpace(speakerId))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NoSpeaker", "对话未设置讲述者。", n.id));
                // 引用缺失(角色)：内置特殊讲述者（旁白/未知/玩家自己）不引用角色资产，跳过，避免误报"不在已用角色列表"。
                if (!string.IsNullOrEmpty(speakerId) && !StoryConstants.IsBuiltInSpeaker(speakerId) && !charIds.Contains(speakerId))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "MissingChar", $"讲述者引用的角色「{speakerId}」不在已用角色列表。", n.id));
                if (!HasOutEdge(model, n.id, "out"))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "NoOut", "对话节点没有后续连线。", n.id));
                return;
            }
            if (n is ChoiceNodeData c)
            {
                if (c.options == null || c.options.Count == 0)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NoOptions", "玩家选项节点没有任何选项。", n.id));
                    return;
                }
                // 表驱动：选项文本在行内（节点本身不冗余存文本），按行内原始下标取全部选项文本做空/重复校验
                bool tableBound = c.IsTableBound;
                string OptText(int i)
                {
                    if (!tableBound) return c.options[i].text ?? "";
                    var row = StoryTableResolver.ResolveRow(c.tableBinding);
                    var table = StoryTableResolver.ResolveTable(c.tableBinding.tableAssetGuid);
                    var ch = StoryTableBaker.GetChoiceForOption(row, table, i);
                    return ch != null ? (ch.text ?? "") : "";
                }
                // G4：选项文本重复（忽略空文本，空文本由 EmptyText 单独报）
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < c.options.Count; i++)
                {
                    string t = OptText(i);
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    string key = t.Trim();
                    if (!seen.Add(key))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "DupOptionText", $"选项存在重复文本：「{key}」。", n.id));
                }
                for (int i = 0; i < c.options.Count; i++)
                {
                    var opt = c.options[i];
                    string t = OptText(i);
                    var label = string.IsNullOrWhiteSpace(t) ? $"第{i + 1}项" : t;
                    if (string.IsNullOrWhiteSpace(t))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EmptyText", $"选项「{label}」文本为空。", n.id));
                    bool optConditional = opt.hasCondition
                        || (opt.conditionGroup != null && opt.conditionGroup.Count > 0)
                        || !string.IsNullOrEmpty(opt.conditionVariable);
                    if (optConditional)
                    {
                        var referenced = new List<string>();
                        if (!string.IsNullOrEmpty(opt.conditionVariable) && !referenced.Contains(opt.conditionVariable)) referenced.Add(opt.conditionVariable);
                        if (opt.conditionGroup != null)
                            foreach (var cl in opt.conditionGroup)
                                if (!string.IsNullOrEmpty(cl.variableId) && !referenced.Contains(cl.variableId)) referenced.Add(cl.variableId);
                        foreach (var vid in referenced)
                            if (!varIds.Contains(vid))
                                issues.Add(new ValidationIssue(ValidationSeverity.Error, "MissingVar", $"选项「{label}」的条件引用了未定义的变量：{vid}。", n.id));
                    }
                    var portId = "opt_" + opt.optionId;
                    // 表驱动选项：无连接编号是「出口端口」的合法语义，不在此报缺连线（连线在表节点边界层）
                    if (!tableBound && !HasOutEdge(model, n.id, portId))
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, "OptNoTarget", $"选项「{label}」没有跳转目标（缺少连线）。", n.id));
                }
                return;
            }
            if (n is StoryTableNodeData tn && tn.tableAsset != null && tn.tableAsset.rows != null)
            {
                // 剧情表节点：内容在表（唯一真相源），逐行校验文本/讲述者/选项（与手搭 Dialogue/Choice 同口径）
                int idx = 0;
                foreach (var row in tn.tableAsset.rows)
                {
                    idx++;
                    if (row == null) continue;
                    string rowLabel = string.IsNullOrEmpty(row.id) ? $"第{idx}行" : row.id;
                    if (string.IsNullOrWhiteSpace(row.text))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EmptyText", $"剧情表行「{rowLabel}」正文为空。", n.id));
                    if (string.IsNullOrWhiteSpace(row.speaker))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NoSpeaker", $"剧情表行「{rowLabel}」未设置讲述者。", n.id));
                    else if (!StoryConstants.IsBuiltInSpeaker(row.speaker) && !charIds.Contains(row.speaker))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "MissingChar", $"剧情表行「{rowLabel}」引用的角色「{row.speaker}」不在已用角色列表。", n.id));
                    // 全部选项（含无连接编号的选项，它们作为出口端口，文本同样需非空且不重复）文本空/重复校验
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (row.choices != null)
                        foreach (var ch in row.choices)
                        {
                            if (ch == null) continue;
                            string t = ch.text ?? "";
                            if (string.IsNullOrWhiteSpace(t))
                                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EmptyText", $"剧情表行「{rowLabel}」的某选项文本为空。", n.id));
                            else
                            {
                                string key = t.Trim();
                                if (!seen.Add(key))
                                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "DupOptionText", $"剧情表行「{rowLabel}」存在重复选项文本：「{key}」。", n.id));
                            }
                        }
                }
                return;
            }
            if (n is ConditionNodeData cond)
            {
                if (cond.clauses == null || cond.clauses.Count == 0)
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NoClauses", "条件节点没有条件子句。", n.id));
                else
                    foreach (var cl in cond.clauses)
                        if (!string.IsNullOrEmpty(cl.variableId) && !varIds.Contains(cl.variableId))
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, "MissingVar", $"条件引用了未定义的变量：{cl.variableId}。", n.id));
                if (!HasOutEdge(model, n.id, "true"))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "CondNoBranch", "条件「满足」分支没有后续连线。", n.id));
                if (!HasOutEdge(model, n.id, "false"))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "CondNoBranch", "条件「不满足」分支没有后续连线。", n.id));
                return;
            }
            if (n is GetVariableNodeData gv)
            {
                if (string.IsNullOrEmpty(gv.variableId))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "MissingVar", "获取变量节点未指定变量。", n.id));
                else if (!varIds.Contains(gv.variableId))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "MissingVar", $"获取变量节点引用了未定义的变量：{gv.variableId}。", n.id));
                if (!HasOutEdge(model, n.id, "out"))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NoOut", "获取变量节点没有连线（其值未供任何节点使用）。", n.id));
                return;
            }
            if (n is SetVariableNodeData sv)
            {
                if (string.IsNullOrEmpty(sv.variableId))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "MissingVar", "赋值节点未指定变量。", n.id));
                else if (!varIds.Contains(sv.variableId))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "MissingVar", $"赋值节点引用了未定义的变量：{sv.variableId}。", n.id));
                else
                {
                    // def 查找：本图优先，其次全局表（全局变量赋值同样获得类型检查）。
                    var def = asset.variables.FirstOrDefault(v => v.id == sv.variableId)
                              ?? globalVars.FirstOrDefault(v => v.id == sv.variableId);
                    if (def != null) CheckValueType(sv.value, def, n.id, issues);
                }
                if (!HasOutEdge(model, n.id, "out"))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "NoOut", "赋值节点没有后续连线。", n.id));
                return;
            }
            if (n is EventNodeData ev)
            {
                if (string.IsNullOrWhiteSpace(ev.eventName))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EmptyEvent", "事件节点未指定事件名。", n.id));
                // P5/L0：payload 语法校验——非空参数必须是合法 JSON（只验语法；业务字段 schema 属 L2 范畴）。
                // Error 级：窗口红条 + 构建门禁（StoryBuildValidator 按 Error 阻断打包）自动拦截，
                // 错误不再右移到「运行时业务侧反序列化才炸」——那是离引入点最远的位置。
                if (!string.IsNullOrWhiteSpace(ev.eventPayload) && !IsJsonSyntax(ev.eventPayload, out var jsonReason))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "BadPayloadJson",
                        $"事件参数(JSON)语法非法（「{PayloadPreview(ev.eventPayload)}」）：{jsonReason}", n.id));
                if (!HasOutEdge(model, n.id, "out"))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "NoOut", "事件节点没有后续连线。", n.id));
                return;
            }
            if (n is EndNodeData end)
            {
                if (end.endType == EndType.JumpChapter && string.IsNullOrWhiteSpace(end.jumpToChapter))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EmptyJump", "结束类型为「跳转章节」但未填写目标章节。", n.id));
                return;
            }
            // Comment 等不参与执行的节点：不校验
        }

        /// <summary>JSON 语法检查（Newtonsoft；JToken 接受任意合法 JSON 值：对象/数组/标量均通过）。</summary>
        private static bool IsJsonSyntax(string json, out string reason)
        {
            try { JToken.Parse(json); reason = null; return true; }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        /// <summary>payload 摘要（去首尾空白、超长截断）——错误信息里帮策划定位是哪段写错。</summary>
        private static string PayloadPreview(string s)
        {
            var t = s.Trim();
            return t.Length <= 32 ? t : t.Substring(0, 32) + "…";
        }

        private static void CheckValueType(string value, StoryVariableDef def, string nodeId, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value)) return; // 空值允许：运行时按变量默认值
            switch (def.type)
            {
                case VariableType.Int:
                    if (!int.TryParse(value, out _))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "VarTypeMismatch", $"变量「{def.name}」(整数)的赋值「{value}」不是合法整数。", nodeId));
                    break;
                case VariableType.Float:
                    if (!float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "VarTypeMismatch", $"变量「{def.name}」(浮点)的赋值「{value}」不是合法数字。", nodeId));
                    break;
                case VariableType.Bool:
                    if (!bool.TryParse(value, out _))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "VarTypeMismatch", $"变量「{def.name}」(布尔)的赋值「{value}」不是 true/false。", nodeId));
                    break;
            }
        }

        private static void DetectCycles(StoryGraphModel model, List<StoryNodeData> nodes, List<ValidationIssue> issues)
        {
            var color = new Dictionary<string, int>(); // 0=white,1=gray,2=black
            foreach (var n in nodes) color[n.id] = 0;
            var reported = new HashSet<string>();
            foreach (var n in nodes)
                if (color[n.id] == 0)
                    DfsCycle(model, n.id, color, reported, issues);
        }

        private static void DfsCycle(StoryGraphModel model, string u, Dictionary<string, int> color,
            HashSet<string> reported, List<ValidationIssue> issues)
        {
            color[u] = 1;
            foreach (var edge in model.GetOutgoing(u))
            {
                if (edge == null || string.IsNullOrEmpty(edge.toNodeId)) continue;
                var v = edge.toNodeId;
                if (!color.ContainsKey(v)) color[v] = 0;
                if (color[v] == 1)
                {
                    if (reported.Add(v))
                    {
                        var title = model.GetNode(v)?.DisplayTitle() ?? v;
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "Cycle",
                            $"检测到循环：节点「{title}」处于一个可能无法结束的循环分支中。", v));
                    }
                }
                else if (color[v] == 0)
                    DfsCycle(model, v, color, reported, issues);
            }
            color[u] = 2;
        }
    }
}
