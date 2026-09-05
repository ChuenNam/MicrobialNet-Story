using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Window;
using MicrobialNet.Story.EditorTools.UI;
using MicrobialNet.Story.Nodes;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Playback
{
    /// <summary>
    /// 编辑器内试跑窗口（独立 EditorWindow，不进入 Play 模式）。
    /// 显示当前节点摘要、变量监视、路径历史，并提供 下一步 / 上一步 / 重置 / 退出 控制。
    /// 选项节点列出选项按钮；点击后进入所选分支的第一个节点，由用户手动「下一步」单步前进，便于逐个观察对话与变量变化。
    /// </summary>
    public sealed class StoryPlaybackWindow : EditorWindow
    {
        private StoryGraphModel _model;
        private StorySimulator _sim;

        private Label _headerLabel;
        private Label _stageType;
        private Label _stageTitle;
        private ScrollView _stageBody;
        private ScrollView _varPane;
        private ScrollView _historyPane;
        private Button _stepBtn;
        private Button _backBtn;
        private Label _statusLabel;

        internal static void Open(StoryGraphModel model, StoryNodeData startNode)
        {
            var w = GetWindow<StoryPlaybackWindow>("剧情试跑");
            w.Initialize(model, startNode);
        }

        private void OnDisable()
        {
            // 关闭（或域重载）时清除主画布上的试跑高亮，并通知主窗口运行时监视区灰显（未预览）。
            PlaybackBridge.RequestClear();
            PlaybackBridge.PushState(new RuntimeSnapshot { active = false });
        }

        private void Initialize(StoryGraphModel model, StoryNodeData startNode)
        {
            _model = model;
            _sim = new StorySimulator(model);
            _sim.Load(startNode);
            BuildUI();
            Refresh();
        }

        private void OnEnable()
        {
            if (_sim != null) { BuildUI(); Refresh(); }
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            StoryStyle.Apply(root);
            root.AddToClassList("pb-root");

            // 顶部状态条（固定高度，不被挤压）
            _headerLabel = new Label("剧情试跑") { name = "pb-header" };
            _headerLabel.AddToClassList("pb-header");
            root.Add(_headerLabel);

            // 当前节点「舞台」卡片：独立深色标题栏（与下方滚轮区区分）+ 可滚动叙事日志（下沉框）
            var stage = new VisualElement { name = "stage" };
            stage.AddToClassList("pb-stage");

            // 当前节点标题栏：深色背景，避免与下方滚轮区底板颜色重合
            var stageHeader = new VisualElement { name = "stage-header" };
            stageHeader.AddToClassList("pb-stage-header");
            _stageType = new Label("") { name = "stage-type" };
            _stageType.AddToClassList("pb-stage-type");
            _stageTitle = new Label("") { name = "stage-title" };
            _stageTitle.AddToClassList("pb-stage-title");
            stageHeader.Add(_stageType);
            stageHeader.Add(_stageTitle);
            stage.Add(stageHeader);

            // 滚轮叙事区：下沉框（暗底 + 边框）
            _stageBody = new ScrollView { name = "stage-body" };
            _stageBody.AddToClassList("pb-inset");
            _stageBody.AddToClassList("pb-stage-body");
            _stageBody.contentContainer.AddToClassList("pb-history-content");
            stage.Add(_stageBody);
            // 内容变化（推进/回退）自动滚动到最底端（新节点处）
            _stageBody.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => ScrollStageToBottom());
            root.Add(stage);

            // 变量监视：子栏目标题（深色）+ 下沉框内容区
            root.Add(MakeSectionTitle("变量监视"));
            _varPane = new ScrollView { name = "vars" };
            _varPane.AddToClassList("pb-inset");
            _varPane.AddToClassList("pb-var-pane");
            root.Add(_varPane);

            // 路径历史：子栏目标题（深色）+ 下沉框内容区
            root.Add(MakeSectionTitle("路径历史"));
            _historyPane = new ScrollView { name = "history" };
            _historyPane.AddToClassList("pb-inset");
            _historyPane.AddToClassList("pb-history-pane");
            _historyPane.contentContainer.AddToClassList("pb-history-content");
            // 内容变化（推进/回退）自动滚动到最底端（当前节点处），与舞台区一致
            _historyPane.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => ScrollHistoryToBottom());
            root.Add(_historyPane);

            // 控制按钮（固定高度，不被挤压）
            var btnRow = new VisualElement { name = "pb-btn-row" };
            btnRow.AddToClassList("pb-btn-row");
            _stepBtn = new Button(OnStep) { text = "下一步" };
            _backBtn = new Button(OnBack) { text = "上一步" };
            var resetBtn = new Button(OnReset) { text = "重置" };
            var closeBtn = new Button(Close) { text = "退出" };
            btnRow.Add(_stepBtn);
            btnRow.Add(_backBtn);
            btnRow.Add(resetBtn);
            btnRow.Add(closeBtn);
            root.Add(btnRow);

            _statusLabel = new Label("") { name = "pb-status" };
            _statusLabel.AddToClassList("pb-status");
            root.Add(_statusLabel);
        }

        /// <summary>子栏目标题：深色背景条，与内容区明显区分。</summary>
        private static Label MakeSectionTitle(string text)
        {
            var label = new Label(text) { name = "section-title" };
            label.AddToClassList("pb-section-title");
            return label;
        }

        /// <summary>把叙事日志滚动到最底端（最新节点处）。</summary>
        private void ScrollStageToBottom()
        {
            _stageBody.verticalScroller.value = _stageBody.verticalScroller.highValue;
        }

        /// <summary>把路径历史滚动到最底端（当前节点处）。顺序本身为起始帧在顶、当前帧在底，与置底不冲突。</summary>
        private void ScrollHistoryToBottom()
        {
            _historyPane.verticalScroller.value = _historyPane.verticalScroller.highValue;
        }

        private void OnStep() { _sim.Step(); AfterChange(); }
        private void OnBack() { _sim.Back(); AfterChange(); }
        private void OnReset() { _sim.Reset(); AfterChange(); }
        // 选择选项后只进入所选分支的第一个节点（点击「下一步」可单步观察后续对话与变量变化），不再自动快进。
        private void OnChoose(int visIndex) { _sim.ChooseOption(visIndex); AfterChange(); }

        private void AfterChange()
        {
            // 通过事件桥请求主窗口高亮当前节点；试跑窗口不反向 GetWindow 主窗口，避免主窗口置顶盖住本窗口。
            if (_sim.Current != null)
                PlaybackBridge.RequestHighlight(_sim.Current.id);
            // 同步当前试跑路径（走过的节点序列），驱动画布连线流动效果。
            var path = _sim.Frames.Select(f => f.Node.id).ToList();
            PlaybackBridge.RequestPath(path);
            Refresh();
        }

        private void Refresh()
        {
            if (_sim == null) return;

            var cur = _sim.Current;

            // 顶部状态条：第 N 步 + 中文语义状态（不用 SimState 调试枚举名）
            var stateText = _sim.State switch
            {
                SimState.AtChoice => "等待选择",
                SimState.Finished => "已结束",
                SimState.Blocked => "死路（无后继）",
                _ => "进行中",
            };
            _headerLabel.text = $"剧情试跑 · 第 {_sim.Frames.Count} 步 · {stateText}";

            // 当前节点标题区：类型标签 + 不重复的语境标题（修复「对话 / 对话」重复）
            _stageType.text = cur == null ? "" : NodeTypeLabel(cur);
            _stageTitle.text = cur == null ? "未开始" : StageTitleFor(cur);

            // 舞台日志：滚轮式叙事回溯，按帧顺序列出，当前帧在底部，可向上滑动查看之前内容
            _stageBody.Clear();
            for (int i = 0; i < _sim.Frames.Count; i++)
            {
                var f = _sim.Frames[i];
                bool isCur = i == _sim.Frames.Count - 1;
                AppendStageEntry(f, isCur, _stageBody);
            }
            _stageBody.schedule.Execute(ScrollStageToBottom);

            // 变量监视（去 id 后缀，仅 名称: 值）
            _varPane.Clear();
            if (_sim.Variables.Count == 0)
            {
                var empty = new Label("（无变量）") { name = "pb-var-empty" };
                empty.AddToClassList("pb-var-line");
                empty.AddToClassList("pb-muted");
                _varPane.Add(empty);
            }
            foreach (var kv in _sim.Variables)
            {
                var name = _model?.Asset.variables.FirstOrDefault(v => v.id == kv.Key)?.name ?? kv.Key;
                var line = new Label($"{name}: {VarText(kv.Value)}") { name = "pb-var-line" };
                line.AddToClassList("pb-var-line");
                _varPane.Add(line);
            }

            // 路径历史：节点 + 实际文字/玩家选项（如「选择：A」），当前高亮
            _historyPane.Clear();
            for (int i = 0; i < _sim.Frames.Count; i++)
            {
                var f = _sim.Frames[i];
                var isCur = i == _sim.Frames.Count - 1;
                var row = new VisualElement { name = "pb-history-row" };
                row.AddToClassList("pb-history-row");

                var marker = new Label(isCur ? "▶ " : "  ") { name = "pb-history-marker" };
                marker.AddToClassList(isCur ? "pb-accent" : "pb-muted");
                row.Add(marker);

                var text = new Label(FrameHistoryText(f)) { name = "pb-history-text" };
                text.AddToClassList(isCur ? "pb-accent" : "pb-muted");
                row.Add(text);

                _historyPane.Add(row);
            }
            _historyPane.schedule.Execute(ScrollHistoryToBottom);

            // 按钮可用性
            _stepBtn.SetEnabled(_sim.State == SimState.Ready);
            _backBtn.SetEnabled(_sim.Frames.Count > 1 && _sim.State != SimState.Finished);

            _statusLabel.text = $"路径长度 {_sim.Frames.Count}";

            // 广播运行时状态给主窗口「运行时监视区」（02 §②）：当前节点 ID + 变量实时值（仅名称/值文本，解耦）。
            var varDict = new Dictionary<string, string>();
            foreach (var kv in _sim.Variables)
            {
                var name = _model?.Asset.variables.FirstOrDefault(v => v.id == kv.Key)?.name ?? kv.Key;
                varDict[kv.Key] = VarText(kv.Value);
            }
            PlaybackBridge.PushState(new RuntimeSnapshot
            {
                active = true,
                nodeId = _sim.Current?.id ?? "",
                nodeTypeLabel = _sim.Current != null ? NodeTypeLabel(_sim.Current) : "",
                vars = varDict,
            });
        }

        private void AppendStageEntry(SimFrame f, bool isCur, VisualElement container)
        {
            var entry = new VisualElement { name = "pb-entry" };
            entry.AddToClassList("pb-entry");
            entry.style.opacity = isCur ? 1f : 0.55f;

            // 类型行
            var typeRow = new VisualElement { name = "pb-entry-type" };
            typeRow.AddToClassList("pb-entry-type");

            var typeMarker = new Label(isCur ? "▶ " : "  ") { name = "pb-entry-marker" };
            typeMarker.AddToClassList(isCur ? "pb-accent" : "pb-muted");
            typeRow.Add(typeMarker);

            var typeLabel = new Label(NodeTypeLabel(f.Node)) { name = "pb-entry-type-label" };
            typeLabel.AddToClassList("pb-accent");
            typeRow.Add(typeLabel);
            entry.Add(typeRow);

            var body = new VisualElement { name = "pb-entry-body" };
            body.AddToClassList("pb-entry-body");
            switch (f.Node)
            {
                // 对话：演讲者 + 正文大字号
                case DialogueNodeData d:
                    var speaker = new Label(StoryConstants.SpeakerDisplayName(d.speakerId)) { name = "pb-speaker" };
                    speaker.AddToClassList("pb-speaker");
                    body.Add(speaker);
                    string pbText = d.text;
                    if (d.IsTableBound)
                    {
                        var row = StoryTableResolver.ResolveRow(d.tableBinding);
                        pbText = row?.text ?? "";
                    }
                    var dlg = new Label(string.IsNullOrEmpty(pbText) ? "（空台词）" : pbText) { name = "pb-dialogue" };
                    dlg.AddToClassList("pb-dialogue-text");
                    body.Add(dlg);
                    break;

                // 选择：分支行=「带文字」选择节点（showText=true）时先渲染对白文字，再渲染选项；
                // 可见项为按钮（当前帧可点，历史帧只读）；不可见项为灰色禁用按钮并标注条件
                case ChoiceNodeData ch:
                    if (ch.showText)
                    {
                        // 与对话节点一致：讲述者 + 正文（表驱动从绑定行取）
                        var row0 = ch.IsTableBound ? StoryTableResolver.ResolveRow(ch.tableBinding) : null;
                        var sp0 = StoryConstants.SpeakerDisplayName(
                            row0 != null && !string.IsNullOrEmpty(row0.speaker) ? row0.speaker : ch.speakerId);
                        var spk = new Label(sp0) { name = "pb-speaker" };
                        spk.AddToClassList("pb-speaker");
                        body.Add(spk);
                        string cText = row0 != null ? (row0.text ?? "") : ch.text;
                        var cd = new Label(string.IsNullOrEmpty(cText) ? "（空台词）" : cText) { name = "pb-dialogue" };
                        cd.AddToClassList("pb-dialogue-text");
                        body.Add(cd);
                    }
                    var choices = f.Choices ?? new List<SimChoiceOption>();
                    int visIndex = 0;
                    foreach (var c in choices)
                    {
                        if (c.Visible)
                        {
                            if (isCur)
                            {
                                int captured = visIndex;
                                body.Add(new Button(() => OnChoose(captured)) { text = c.Text });
                            }
                            else
                            {
                                var cl = new Label($"• {c.Text}") { name = "pb-choice" };
                                cl.AddToClassList("pb-choice-label");
                                body.Add(cl);
                            }
                            visIndex++;
                        }
                        else
                        {
                            var reason = string.IsNullOrEmpty(c.ConditionText) ? "条件未满足" : $"未满足：{c.ConditionText}";
                            if (isCur)
                            {
                                var disabledBtn = new Button(() => { }) { text = $"{c.Text}（{reason}）" };
                                disabledBtn.SetEnabled(false);
                                body.Add(disabledBtn);
                            }
                            else
                            {
                                var dl = new Label($"• {c.Text}（{reason}）") { name = "pb-choice-disabled" };
                                dl.AddToClassList("pb-choice-label");
                                dl.AddToClassList("pb-muted");
                                body.Add(dl);
                            }
                        }
                    }
                    break;

                // 条件 / 赋值 / 事件 / 其它：human 可读说明，按 \n 逐行（不依赖 PreWrap）
                default:
                    var eff = f.EffectText ?? "";
                    foreach (var ln in eff.Split('\n'))
                    {
                        if (string.IsNullOrEmpty(ln)) continue;
                        var el = new Label(ln) { name = "pb-effect-line" };
                        el.AddToClassList("pb-effect-line");
                        body.Add(el);
                    }
                    break;
            }
            entry.Add(body);
            container.Add(entry);
        }

        private static string StageTitleFor(StoryNodeData n) => n switch
        {
            DialogueNodeData d => string.IsNullOrEmpty(d.title)
                ? StoryConstants.SpeakerDisplayName(d.speakerId)
                : d.title,
            ChoiceNodeData _ => "请选择分支",
            ConditionNodeData _ => "条件判断",
            SetVariableNodeData _ => "变量赋值",
            EventNodeData ev => string.IsNullOrEmpty(ev.eventName) ? "触发事件" : ev.eventName,
            StartNodeData _ => "剧情开始",
            EndNodeData en => en.endType == EndType.JumpChapter ? $"跳转章节 {en.jumpToChapter}" : "剧情结束",
            _ => n.DisplayTitle(),
        };

        private static string FrameHistoryText(SimFrame f)
        {
            var node = f.Node;
            if (node is StartNodeData) return "开始";
            if (node is EndNodeData en) return en.endType == EndType.JumpChapter ? $"结束 → 跳转章节 {en.jumpToChapter}" : "剧情结束";
            if (node is DialogueNodeData) return node.GetSummary();
            if (node is ChoiceNodeData ch)
            {
                // 分支行=「带文字」选择节点：showText=true 时历史条目也前置「讲述者：正文」
                string prefix = "";
                if (ch.showText)
                {
                    var row0 = ch.IsTableBound ? StoryTableResolver.ResolveRow(ch.tableBinding) : null;
                    var sp0 = StoryConstants.SpeakerDisplayName(
                        row0 != null && !string.IsNullOrEmpty(row0.speaker) ? row0.speaker : ch.speakerId);
                    var txt0 = row0 != null ? (row0.text ?? "") : ch.text;
                    prefix = $"{sp0}：{(string.IsNullOrEmpty(txt0) ? "<空>" : txt0.Replace("\n", " "))}";
                }
                string choiceText;
                if (!string.IsNullOrEmpty(f.ChosenOptionId))
                {
                    var chosen = ch.options.FirstOrDefault(o => o.optionId == f.ChosenOptionId);
                    string txt;
                    if (chosen == null) txt = "<选项>";
                    else
                    {
                        txt = chosen.text;
                        if (ch.IsTableBound)
                        {
                            int oi = ch.options.IndexOf(chosen);
                            var row = StoryTableResolver.ResolveRow(ch.tableBinding);
                            var tbl = StoryTableResolver.ResolveTable(ch.tableBinding.tableAssetGuid);
                            var chChoice = StoryTableBaker.GetChoiceForOption(row, tbl, oi);
                            if (chChoice != null) txt = chChoice.text ?? "";
                        }
                        if (string.IsNullOrEmpty(txt)) txt = "<选项>";
                    }
                    choiceText = $"选择：{txt}";
                }
                else choiceText = "选择：等待玩家选择";
                return string.IsNullOrEmpty(prefix) ? choiceText : prefix + "\n" + choiceText;
            }
            if (node is ConditionNodeData) return f.EffectText?.Split('\n').FirstOrDefault() ?? "条件";
            if (node is SetVariableNodeData) return f.EffectText ?? "赋值";
            if (node is EventNodeData) return f.EffectText ?? "事件";
            return f.EffectText ?? node.DisplayTitle();
        }

        private static string VarText(SimVar v) => v.Type switch
        {
            VariableType.Bool => v.AsBool ? "true" : "false",
            VariableType.String => $"\"{v.AsString}\"",
            _ => v.Value.ToString(),
        };

        private static string NodeTypeLabel(StoryNodeData n) => n switch
        {
            StartNodeData _ => "开始",
            DialogueNodeData _ => "对话",
            EventNodeData _ => "事件",
            SetVariableNodeData _ => "赋值",
            ConditionNodeData _ => "条件",
            ChoiceNodeData _ => "选择",
            EndNodeData _ => "结束",
            _ => n.GetType().Name,
        };
    }
}
