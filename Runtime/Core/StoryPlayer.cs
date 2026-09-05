using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story.Nodes;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情播放器：纯逻辑状态机，不依赖任何 UI / 宿主框架。
    ///
    /// 遍历模型：从入口节点（Start）进入，按节点类型执行并沿 <see cref="StoryEdge"/> 跳转，
    /// 直到 End 或死路。对外只通过 C# event 通信（OnLine / OnChoices / OnEvent / OnEnd / OnNodeEnter / OnError），
    /// 所有变量读写经 <see cref="IStoryVariableProvider"/>、事件派发经 <see cref="IStoryEventHandler"/>、文本经 <see cref="IStoryTextProvider"/>。
    ///
    /// 视图与数据严格分离：本类只决定「下一步播什么、抛出什么」，具体怎么显示交给 StoryView（或宿主 UI）。
    /// </summary>
    internal sealed class StoryPlayer
    {
        /// <summary>一句对白（交给视图渲染）。</summary>
        public sealed class Line
        {
            public string SpeakerId;
            public string SpeakerName;
            /// <summary>讲述者视图模型（P2）：显示名 + 主题色 + 立绘。视图据此外观着色/显示头像。</summary>
            public StoryConstants.CharacterViewModel Speaker;
            public string Text;
            public float Speed;
            /// <summary>打字机节奏模式（见 TypingMode）。驱动视图选择揭示节奏源。</summary>
            public TypingMode TypingMode;
            /// <summary>形式三手K逐字符延迟（秒）；仅 TypingMode.Custom 且长度匹配可见字符数时使用。按可见字符索引。</summary>
            public float[] TypingDelays;
            /// <summary>节点级立绘 Key（轻量 [Future] 打通：仅暴露数据，视图默认沿用角色默认立绘）。</summary>
            public string PortraitKey;
            /// <summary>节点级语音 Key（轻量 [Future] 打通：经 IStoryEventHandler 派发 "voice:{key}" 交由宿主播放）。</summary>
            public string VoiceKey;

            /// <summary>节点级外观覆盖提示（样式/位置/策略/保留），透传给视图。</summary>
            public DialogueAppearanceHint appearance;
        }

        /// <summary>一个玩家选项（交给视图渲染为按钮）。</summary>
        public sealed class Choice
        {
            public string OptionId;
            public string Text;
            /// <summary>节点级外观覆盖提示（样式/位置/策略），透传给视图。</summary>
            public DialogueAppearanceHint appearance;
            /// <summary>选项框顶部说明文字（「带文字」选择节点的行内对白，可空=不显示）。由视图渲染在选项上方。</summary>
            public string Prompt;
            /// <summary>Prompt 的打字机参数（与对白节点同语义）：视图据此生成打字 schedule，选项在文字打完后才出现。</summary>
            public float PromptSpeed = 0.5f;
            public TypingMode PromptTypingMode = TypingMode.GlobalSpeed;
        }

        /// <summary>一个剧情事件（交给业务代码处理）。</summary>
        public sealed class StoryEvent
        {
            public string Name;
            public string PayloadJson;
        }

        // —— 对外事件（视图 / 宿主订阅）——
        public event Action<Line> OnLine;
        public event Action<IReadOnlyList<Choice>> OnChoices;
        public event Action<StoryEvent> OnEvent;
        public event Action<string> OnNodeEnter;   // 当前节点 ID（编辑器同步高亮 / 调试用）
        public event Action<bool, string> OnEnd;   // (showEndText, endText)：剧情正常终结；showEndText=false 时视图不应弹任何框
        public event Action<StoryGraphAsset> OnChapterChanged; // 章节/图切换（JumpChapter 触发），直接传解析到的目标图资产（不依赖 meta.storyId 二次解析，避免 storyId 为空时本地化表不切换）
        public event Action<string> OnError;        // 死路 / 缺节点 / 异常结构

        private RuntimeStoryGraph _graph;
        private readonly IStoryVariableProvider _variables;
        private readonly IStoryEventHandler _events;
        private readonly IStoryTextProvider _text;
        private Func<string, StoryGraphAsset> _graphResolver; // 章节跳转/多图加载：跳转标识→目标图资产（内部编译）

        /// <summary>实例级讲述者视图模型解析器（宿主经 StoryFlowConfig.Characters 注入；多 StoryFlow 并存时互不覆盖）。
        /// null = 回落全局静态 StoryConstants.ResolveCharacter（编辑器绑定器 / BindCharacterResolver 的兼容默认）。</summary>
        private readonly Func<string, StoryConstants.CharacterViewModel> _resolveCharacter;

        private StoryNodeData _current;
        private bool _waiting;   // 等待 Advance / Choose
        private bool _running;
        private int _stepGuard;  // 防环：异常结构导致无限跳转时熔断
        private const int MaxSteps = 10000;
        private int _chapterGuard; // 防章节环路：JumpChapter 每次换图计数（迭代遍历下换图仍是嵌套调用，需独立熔断）

        /// <summary>
        /// 构造播放器。
        /// </summary>
        /// <param name="graph">运行时剧情图（由 RuntimeStoryGraph.FromAsset 或 JSON 装载得到）。</param>
        /// <param name="variables">变量提供者（条件求值 / 赋值）。</param>
        /// <param name="events">事件处理器（事件节点派发）。</param>
        /// <param name="text">文本提供者（本地化解析）；传 null 时按 identity 处理。</param>
        /// <param name="graphResolver">图加载器（JumpChapter 章节跳转 / 多图加载）；可空 → 遇 JumpChapter 报错。</param>
        /// <param name="characterResolver">实例级讲述者解析器（宿主经 StoryFlowConfig.Characters 注入）；可空 → 回落全局静态（兼容编辑器绑定与 BindCharacterResolver）。</param>
        public StoryPlayer(RuntimeStoryGraph graph, IStoryVariableProvider variables, IStoryEventHandler events, IStoryTextProvider text = null, Func<string, StoryGraphAsset> graphResolver = null, Func<string, StoryConstants.CharacterViewModel> characterResolver = null)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _text = text;
            _graphResolver = graphResolver;
            _resolveCharacter = characterResolver ?? StoryConstants.ResolveCharacter;
        }

        /// <summary>是否正在运行（已开始且未结束 / 未出错）。</summary>
        public bool IsRunning => _running;

        /// <summary>是否正等待用户推进（对白等 Advance / 选项等 Choose）。</summary>
        public bool IsWaiting => _waiting;

        /// <summary>从入口节点开始播放。</summary>
        public void Start()
        {
            if (_running) return;
            var entry = _graph.GetEntryNode();
            if (entry == null)
            {
                RaiseError("未找到入口节点（Start）。");
                return;
            }
            _running = true;
            _stepGuard = 0;
            _chapterGuard = 0;
            Enter(entry);
        }

        /// <summary>结束播放（对白 / 任意等待态均可调用）。</summary>
        public void Stop()
        {
            _running = false;
            _waiting = false;
            _current = null;
        }

        /// <summary>
        /// 抓取当前进度快照（当前所在图 ID + 当前节点 ID + 全部变量值），供存档。
        /// 跨图流程（JumpChapter）会自动记录当前所在图的 storyId（graphId 字段），恢复时据此切回正确的图。
        /// </summary>
        public StorySnapshot CaptureState()
        {
            var snap = new StorySnapshot
            {
                storyId = _graph.meta.storyId,
                graphId = _graph.meta.storyId,
                currentNodeId = _current?.id,
            };
            var dict = _variables.Snapshot();
            if (dict != null)
            {
                foreach (var kv in dict)
                {
                    snap.variables.Add(new StorySnapshot.VarEntry
                    {
                        id = kv.Key,
                        type = _variables.GetVariableType(kv.Key),
                        raw = kv.Value?.ToString(),
                    });
                }
            }
            return snap;
        }

        /// <summary>
        /// 从快照恢复并继续播放：先还原变量，再从 <see cref="StorySnapshot.currentNodeId"/> 进入该节点
        /// （对白节点会重新抛出 OnLine、选项节点重新抛出 OnChoices，呈现「断点续玩」）。
        /// 快照为 null 或节点不存在时报错中断；storyId 不匹配时仅告警并仍尝试恢复。
        /// </summary>
        public void Restore(StorySnapshot snap)
        {
            if (snap == null) return;
            if (string.IsNullOrEmpty(snap.version))
                Debug.LogWarning("[StoryPlayer] 存档缺少版本号（旧格式存档），按当前格式尝试恢复。");
            if (_running) _running = false; // 允许在已 Start 后重载（测试/热覆盖场景）
            _running = true;
            _waiting = false;
            _stepGuard = 0;
            _chapterGuard = 0;

            // 跨图恢复：存档可能记录在另一张图（JumpChapter 之后的进度），先切图再找节点。
            string snapGraphId = !string.IsNullOrEmpty(snap.graphId) ? snap.graphId : snap.storyId;
            if (!string.IsNullOrEmpty(snapGraphId) && _graph.meta.storyId != snapGraphId)
            {
                if (_graphResolver != null)
                {
                    var target = RuntimeStoryGraph.FromAsset(_graphResolver(snapGraphId));
                    if (target == null) { RaiseError($"恢复失败：找不到存档所在图「{snapGraphId}」。"); return; }
                    _graph = target;
                }
                else
                {
                    RaiseError($"恢复失败：存档属于图「{snapGraphId}」，但未配置图加载器（StoryFlowConfig.GraphResolver）。");
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(snap.storyId) && !string.IsNullOrEmpty(_graph.meta.storyId)
                && !string.Equals(snap.storyId, _graph.meta.storyId, System.StringComparison.Ordinal))
            {
                Debug.LogWarning($"[StoryPlayer] 存档 storyId({snap.storyId}) 与当前剧情({_graph.meta.storyId}) 不匹配，仍尝试恢复。");
            }

            if (snap.variables != null)
            {
                foreach (var v in snap.variables)
                    _variables.SetValue(v.id, ValueParser.Parse(v.raw, v.type));
            }

            var node = _graph.GetNode(snap.currentNodeId);
            if (node == null)
            {
                RaiseError($"恢复失败：快照节点不存在（{snap.currentNodeId}）。");
                return;
            }
            Enter(node);
        }

        /// <summary>推进一句对白（在收到 OnLine 后由视图调用）。非对白等待态调用会被忽略。</summary>
        public void Advance()
        {
            if (!_running || !_waiting) return;
            if (_current is DialogueNodeData d)
            {
                _waiting = false;
                Traverse(d.id, "out");
            }
        }

        /// <summary>选择一个选项（在收到 OnChoices 后由视图调用）。非法调用会被忽略并报错。</summary>
        /// <param name="optionId">选项的 optionId（对应输出端口 "opt_{optionId}"）。</param>
        public void Choose(string optionId)
        {
            if (!_running || !_waiting) return;
            if (!(_current is ChoiceNodeData c))
            {
                RaiseError("当前不在选项等待态，Choose 被忽略。");
                return;
            }
            var opt = c.options.FirstOrDefault(o => o.optionId == optionId);
            if (opt == null)
            {
                RaiseError($"选项不存在：{optionId}。");
                return;
            }
            _waiting = false;
            Traverse(c.id, "opt_" + opt.optionId);
        }

        // —— 内部：进入并执行一个节点（迭代式遍历）——
        // Enter↔Traverse 相互递归改为显式循环：直通节点（Start/Condition/SetVariable）在同一循环内
        // 推进到后继，超长线性图（数千连跳）不再累积调用栈；等待/终结类节点照旧返回。
        // 唯一剩余的嵌套调用是 JumpToChapter→Enter（每次章节跳转 +1 层），由 _chapterGuard 独立熔断。
        private void Enter(StoryNodeData node)
        {
            while (_running)
            {
                if (++_stepGuard > MaxSteps)
                {
                    RaiseError("步数超限，疑似存在环路 / 异常结构，已熔断。");
                    return;
                }
                if (node == null)
                {
                    RaiseError("跳转目标节点为空。");
                    return;
                }
                if (!node.IsExecutable)
                {
                    RaiseError($"节点不可执行（{node.GetType().Name}，可能为批注节点）。");
                    return;
                }

                OnNodeEnter?.Invoke(node.id);

                string exitPort; // 直通节点的出口端口；null = 等待/终结类，本次调用即返回
                switch (node)
                {
                    case StartNodeData _:
                        exitPort = "out";
                        break;
                    case DialogueNodeData d:
                        PresentLine(d);
                        return;
                    case ChoiceNodeData c:
                        PresentChoices(c);
                        return;
                    case ConditionNodeData cond:
                        exitPort = EvaluateCondition(cond) ? "true" : "false";
                        break;
                    case SetVariableNodeData sv:
                        ApplySet(sv);
                        exitPort = "out";
                        break;
                    case EventNodeData e:
                        HandleEvent(e);
                        return;
                    case EndNodeData end:
                        HandleEnd(end);
                        return;
                    default:
                        RaiseError($"未知节点类型：{node.GetType().Name}。");
                        return;
                }

                // —— 内联 Traverse：按出口端口找边，命中后在本循环内推进（不再相互递归）——
                var edge = _graph.edges
                    .FirstOrDefault(e => e.fromNodeId == node.id && e.fromPortId == exitPort);
                if (edge == null)
                {
                    RaiseError($"未找到出口（{node.id}:{exitPort}），剧情在此中断。");
                    return;
                }
                node = _graph.GetNode(edge.toNodeId);
            }
        }

        // —— 内部：事件节点挂起（协程式流程控制点，等业务 onComplete 才续走）——
        private void HandleEvent(EventNodeData e)
        {
            _current = e;
            _waiting = true;   // 挂起：协程式流程控制点，等业务回调才续走
            OnEvent?.Invoke(new StoryEvent { Name = e.eventName, PayloadJson = e.eventPayload }); // 视图/调试
            _events.Raise(e.eventName, e.eventPayload, () =>
            {
                _waiting = false;
                Traverse(e.id, "out");
            });
        }

        // —— 内部：沿出口端口找边跳转（Advance / Choose / 事件完成回调的续走入口）——
        private void Traverse(string fromNodeId, string fromPortId)
        {
            var edge = _graph.edges
                .FirstOrDefault(e => e.fromNodeId == fromNodeId && e.fromPortId == fromPortId);
            if (edge == null)
            {
                RaiseError($"未找到出口（{fromNodeId}:{fromPortId}），剧情在此中断。");
                return;
            }
            var next = _graph.GetNode(edge.toNodeId);
            Enter(next);
        }

        /// <summary>取表驱动节点绑定的源行（运行时内容真相源）。非表驱动或找不到返回 null。</summary>
        private StoryTableRow GetBoundRow(StoryNodeData n)
        {
            if (n == null || !n.IsTableBound) return null;
            _graph.tableRows.TryGetValue(n.tableBinding.rowId, out var row);
            return row;
        }

        /// <summary>呈现一句对白（或旁白）：内容来自节点自身或绑定的表行（唯一真相源），并派发本地化 key。</summary>
        private void PresentLine(DialogueNodeData d)
        {
            _waiting = true;
            _current = d;
            // 表驱动节点：内容来自绑定的源行（唯一真相源），本地化 key 绑稳定的 rowId；
            // 手搭节点：内容在节点自身，本地化 key 绑 nodeId。
            StoryTableRow row = GetBoundRow(d);
            string speakerId = row != null
                ? (string.IsNullOrEmpty(row.speaker) ? StoryConstants.NarrationId : row.speaker)
                : d.speakerId;
            string text = row != null ? (row.text ?? string.Empty) : d.text;
            string locKey = row != null ? (d.tableBinding.rowId + ".text") : (d.id + ".text");
            var vm = _resolveCharacter(speakerId);
            OnLine?.Invoke(new Line
            {
                SpeakerId = speakerId,
                Speaker = vm,
                SpeakerName = string.IsNullOrEmpty(speakerId)
                    ? vm.displayName
                    : ResolveText("character." + speakerId + ".name", vm.displayName),
                Text = ResolveText(locKey, text),
                Speed = d.speed,
                TypingMode = d.typingMode,
                TypingDelays = d.typingDelays,
                PortraitKey = d.portraitKey,
                VoiceKey = d.voiceKey,
                appearance = BuildAppearance(d),
            });
            // 轻量 [Future] 打通：节点级语音 Key 经事件处理器派发，交由宿主播放（运行时不自带音频）。
            if (!string.IsNullOrEmpty(d.voiceKey))
                _events.Raise("voice:" + d.voiceKey, string.Empty);
        }

        private void PresentChoices(ChoiceNodeData c)
        {
            // 带文字的选择节点：文字不再单独弹对白面板，而是并入选项框顶部（Prompt，视图渲染在选项上方）
            string prompt = null;
            if (c.showText)
            {
                StoryTableRow txRow = GetBoundRow(c);
                string txSpeaker = txRow != null
                    ? (string.IsNullOrEmpty(txRow.speaker) ? StoryConstants.NarrationId : txRow.speaker)
                    : c.speakerId;
                string txText = txRow != null ? (txRow.text ?? string.Empty) : c.text;
                string txLocKey = txRow != null ? (c.tableBinding.rowId + ".text") : (c.id + ".text");
                var txVm = _resolveCharacter(txSpeaker);
                string speakerName = string.IsNullOrEmpty(txSpeaker)
                    ? txVm.displayName
                    : ResolveText("character." + txSpeaker + ".name", txVm.displayName);
                string resolved = ResolveText(txLocKey, txText);
                // 正文为空则不显示说明（纯选项框）
                prompt = string.IsNullOrEmpty(resolved) ? null : $"{speakerName}：{resolved}";
            }
            var visible = new List<Choice>();
            StoryTableRow row = GetBoundRow(c);
            // 表驱动：选项文本按行内原始下标（= 节点选项下标，含无连接编号的选项）从行取；手搭：用节点选项自身文本
            for (int i = 0; i < c.options.Count; i++)
            {
                var o = c.options[i];
                if (!OptionVisible(o)) continue;
                string optText = row != null && row.choices != null && i < row.choices.Count
                    ? (row.choices[i]?.text ?? "")
                    : o.text;
                // 表驱动：选项 key 绑稳定的 rowId + 选项下标（与编辑器 CSV/Excel 导出规则一致，且不随虚拟节点 id 变化）；手搭：绑 choice 节点 id
                string choiceLocKey = row != null ? (c.tableBinding.rowId + ".opt." + o.optionId) : (c.id + ".opt." + o.optionId);
                visible.Add(new Choice
                {
                    OptionId = o.optionId,
                    Text = ResolveText(choiceLocKey, optText),
                    appearance = BuildAppearance(c),
                    Prompt = prompt,
                    // 带文字选择节点的对白打字机参数（Prompt 逐字 + 选项延迟出现）
                    PromptSpeed = c.speed,
                    PromptTypingMode = c.typingMode,
                });
            }
            if (visible.Count == 0)
            {
                RaiseError("所有选项的显示条件均不满足，无可用分支，剧情中断。");
                return;
            }
            _waiting = true;
            _current = c;
            OnChoices?.Invoke(visible);
        }

        // —— 节点级外观提示构造：把节点上扁平的外观字段组装成 DialogueAppearanceHint（运行期透传，不序列化）——
        private DialogueAppearanceHint BuildAppearance(DialogueNodeData d)
        {
            return new DialogueAppearanceHint
            {
                styleAsset = d.appearanceStyle,
                styleKeyOverride = d.appearanceStyle != null ? d.appearanceStyle.styleKey : null,
                overridePosition = d.appearanceOverridePosition,
                position = d.appearanceOverridePosition
                    ? new DialogueBoxPosition { mode = d.appearancePositionMode, anchor = d.appearancePositionAnchor, offset = (Vector2)d.appearancePositionOffset }
                    : null,
                spawnStrategyKey = d.appearanceOverridePosition ? null : d.appearanceSpawnStrategyKey,
                persistentOverride = d.appearancePersistent == DialogueBoxPersistentSetting.Inherit ? (bool?)null
                    : d.appearancePersistent == DialogueBoxPersistentSetting.Persistent,
            };
        }

        private DialogueAppearanceHint BuildAppearance(ChoiceNodeData c)
        {
            return new DialogueAppearanceHint
            {
                styleAsset = c.appearanceStyle,
                styleKeyOverride = c.appearanceStyle != null ? c.appearanceStyle.styleKey : null,
                overridePosition = c.appearanceOverridePosition,
                position = c.appearanceOverridePosition
                    ? new DialogueBoxPosition { mode = c.appearancePositionMode, anchor = c.appearancePositionAnchor, offset = (Vector2)c.appearancePositionOffset }
                    : null,
                spawnStrategyKey = c.appearanceOverridePosition ? null : c.appearanceSpawnStrategyKey,
                persistentOverride = null,
            };
        }

        private void ApplySet(SetVariableNodeData sv)
        {
            if (string.IsNullOrEmpty(sv.variableId)) return;
            var type = _variables.GetVariableType(sv.variableId);
            _variables.TryGetValue(sv.variableId, out var current);
            // 操作数：连线到「变量」输入端口（获取变量节点）时用端口变量当前值；未连线回落面板常量
            object operand;
            if (!TryGetPortVariable(sv, "var_in", out operand))
                operand = ValueParser.Parse(sv.value, type);
            object result = operand;

            if (type == VariableType.Int || type == VariableType.Float)
            {
                double dc = ToDouble(current), dv = ToDouble(operand);
                double r = sv.op switch
                {
                    AssignOp.Set => dv,
                    AssignOp.Add => dc + dv,
                    AssignOp.Sub => dc - dv,
                    AssignOp.Mul => dc * dv,
                    AssignOp.Div => dv == 0 ? dc : dc / dv, // 除零保护：保持原值
                    _ => dv,
                };
                result = type == VariableType.Int ? (object)Convert.ToInt64(Math.Round(r)) : (object)(float)r;
            }
            else if (type == VariableType.String && sv.op == AssignOp.Add)
            {
                result = (current?.ToString() ?? string.Empty) + (operand?.ToString() ?? string.Empty);
            }
            // Bool：仅 Set 有意义，其余保持原值
            _variables.SetValue(sv.variableId, result);
        }

        /// <summary>读取「数据线」端口值：找到连到 node 的 toPortId 输入端的边，若源头是「获取变量」节点则返回其变量当前值。
        /// 无连线 / 源头不是获取变量节点 → 返回 false（调用方回落常量）。</summary>
        private bool TryGetPortVariable(StoryNodeData node, string portId, out object value)
        {
            value = null;
            if (_graph == null || node == null || string.IsNullOrEmpty(node.id)) return false;
            foreach (var e in _graph.edges)
            {
                if (e.toNodeId == node.id && e.toPortId == portId)
                {
                    var from = _graph.nodes != null ? _graph.nodes.FirstOrDefault(n => n.id == e.fromNodeId) : null;
                    if (from is GetVariableNodeData gv)
                        return _variables.TryGetValue(gv.variableId, out value);
                    return false; // 连了非获取变量节点：视为未连线
                }
            }
            return false;
        }

        private void HandleEnd(EndNodeData end)
        {
            if (end.endType == EndType.JumpChapter)
            {
                JumpToChapter(end.jumpToChapter);
                return; // JumpToChapter 内部已决定后续（继续播放 or 报错中断）
            }
            _running = false;
            _waiting = false;
            _current = null;
            OnEnd?.Invoke(end.showEndText, end.endText);
        }

        /// <summary>
        /// 跳转章节 / 切换剧情图（JumpChapter 结束节点的核心能力）。
        /// 经 _graphResolver 把跳转标识解析为下一张 RuntimeStoryGraph；变量黑板跨图保留（共享进度），
        /// 仅切换 _graph 数据并从其入口节点继续播放。解析器为空或目标图缺失 / 无入口则报明确错误（不崩溃）。
        /// </summary>
        private void JumpToChapter(string targetKey)
        {
            if (_graphResolver == null)
            {
                RaiseError($"结束节点要求跳转章节「{targetKey}」，但未配置图加载器（StoryFlowConfig.GraphResolver）。");
                return;
            }
            // 章节熔断：换图是本类唯一残留的嵌套 Enter 调用（每次 +1 层调用栈），章节间循环
            // （A→B→A…）靠步数守卫发现不了（换图时 _stepGuard 清零），须独立计数兜底。
            if (++_chapterGuard > MaxSteps)
            {
                RaiseError("章节跳转次数超限，疑似章节间循环，已熔断。");
                return;
            }
            var nextAsset = _graphResolver(targetKey);
            if (nextAsset == null)
            {
                RaiseError($"章节跳转失败：找不到目标图「{targetKey}」。");
                return;
            }
            _graph = RuntimeStoryGraph.FromAsset(nextAsset);
            _stepGuard = 0; // 新图重新计数（单图内防环仍生效）
            // 直接把解析到的图资产交给订阅者（如 StoryFlow 的本地化提供者），
            // 不再依赖 meta.storyId 二次解析——即便 storyId 为空也能正确切到目标图的本地化表，
            // 杜绝「跳转后文本误查旧表 / 回落源语言（默认语言）」。
            OnChapterChanged?.Invoke(nextAsset);
            var entry = _graph.GetEntryNode();
            if (entry == null)
            {
                RaiseError($"目标图「{targetKey}」缺少入口节点（Start），无法继续。");
                return;
            }
            Enter(entry);
        }

        // —— 条件求值（条件节点）——
        // 子句比较值：端口（var_in_{clauseId} 接「获取变量」节点）优先，未连线回落子句常量。
        private bool EvaluateCondition(ConditionNodeData cond)
            => ConditionEvaluator.Evaluate(cond.clauses, cond.combine, _variables,
                cl => TryGetPortVariable(cond, "var_in_" + cl.clauseId, out var v) ? v : null);

        // —— 选项可见性（带条件的选项，兼容旧档单条件字段，不修改节点）——
        private bool OptionVisible(ChoiceOption o)
        {
            if (!o.hasCondition) return true;
            var clauses = o.conditionGroup;
            if ((clauses == null || clauses.Count == 0) && !string.IsNullOrEmpty(o.conditionVariable))
            {
                // 旧档单条件回退（构造临时子句，不改动共享节点）
                var legacy = new List<ConditionClause>
                {
                    new ConditionClause { variableId = o.conditionVariable, op = o.conditionOp, value = o.conditionValue },
                };
                return ConditionEvaluator.Evaluate(legacy, o.conditionCombine, _variables);
            }
            return ConditionEvaluator.Evaluate(clauses, o.conditionCombine, _variables);
        }

        /// <summary>
        /// 本地化解析：以节点 ID 派生的 key 查 <see cref="_text"/>（IStoryTextProvider），
        /// 命中译文则用译文，未注入或查不到（返回 null/空）则回退 fallback（原文），避免显示裸 key。
        /// </summary>
        private string ResolveText(string key, string fallback)
        {
            if (_text != null)
            {
                var resolved = _text.ResolveText(key);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }
            return fallback ?? string.Empty;
        }

        private static double ToDouble(object v)
            => v is double d ? d
             : v is float f ? f
             : v is int i ? i
             : (double.TryParse(v?.ToString(), out var r) ? r : 0d);

        private void RaiseError(string msg)
        {
            _running = false;
            _waiting = false;
            _current = null;
            OnError?.Invoke(msg);
        }
    }
}
