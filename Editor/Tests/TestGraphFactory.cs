using System;
using System.Collections.Generic;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 测试共享的图构建工厂：在内存中构造节点/边/资产/运行时图，避免依赖工程资产。
    /// 仅测试程序集可用（依赖 InternalsVisibleTo 访问 internal 数据模型）。
    /// </summary>
    internal static class GraphFactory
    {
        public static StoryGraphAsset NewAsset(string storyId = "test_graph")
        {
            var a = ScriptableObject.CreateInstance<StoryGraphAsset>();
            a.meta = new StoryMeta { storyId = storyId };
            return a;
        }

        /// <summary>new 一个指定类型节点并指定 id（内部类型经 InternalsVisibleTo 可访问）。</summary>
        public static T Node<T>(string id) where T : StoryNodeData, new() => new T { id = id };

        public static StoryEdge Edge(string from, string port, string to)
            => new StoryEdge { fromNodeId = from, fromPortId = port, toNodeId = to, toPortId = "in" };

        public static DialogueNodeData Dialogue(string id, string speaker, string text)
            => new DialogueNodeData { id = id, speakerId = speaker, text = text };

        public static ChoiceNodeData Choice(string id, params (string optionId, string text)[] options)
        {
            var c = new ChoiceNodeData { id = id };
            foreach (var (oid, txt) in options)
                c.options.Add(new ChoiceOption { optionId = oid, text = txt });
            return c;
        }

        public static SetVariableNodeData SetVar(string id, string varId, AssignOp op, string value)
            => new SetVariableNodeData { id = id, variableId = varId, op = op, value = value };

        public static ConditionNodeData Cond(string id, string varId, CompareOp op, string value, ConditionCombine combine = ConditionCombine.All)
            => new ConditionNodeData
            {
                id = id,
                combine = combine,
                clauses = new List<ConditionClause> { new ConditionClause { variableId = varId, op = op, value = value } },
            };

        public static EventNodeData Event(string id, string eventName, string eventPayload = null)
            => new EventNodeData { id = id, eventName = eventName, eventPayload = eventPayload };

        public static EndNodeData End(string id, EndType type = EndType.Normal, string jump = null)
            => new EndNodeData { id = id, endType = type, jumpToChapter = jump };

        public static StoryVariableDef Var(string id, string name, VariableType type, string defaultValue)
            => new StoryVariableDef { id = id, name = name, type = type, defaultValue = defaultValue };

        /// <summary>直接构造运行时图（不经 FromAsset），并配好内存变量提供者。</summary>
        public static StoryPlayer MakePlayer(
            IEnumerable<StoryNodeData> nodes,
            IEnumerable<StoryEdge> edges,
            IEnumerable<StoryVariableDef> variables = null,
            IStoryEventHandler events = null,
            IStoryTextProvider text = null,
            Func<string, StoryGraphAsset> graphResolver = null)
        {
            var g = new RuntimeStoryGraph
            {
                meta = new StoryMeta { storyId = "test_graph" },
                nodes = new List<StoryNodeData>(nodes),
                edges = new List<StoryEdge>(edges),
                variables = variables != null ? new List<StoryVariableDef>(variables) : new List<StoryVariableDef>(),
            };
            return new StoryPlayer(g, new InMemoryVariableProvider(g.variables), events ?? new StoryEventBus(), text, graphResolver);
        }

        /// <summary>把节点/边装进一个（未保存到 AssetDatabase 的）内存 SO 资产。</summary>
        public static StoryGraphAsset ToAsset(IEnumerable<StoryNodeData> nodes, IEnumerable<StoryEdge> edges,
            IEnumerable<StoryVariableDef> variables = null, string storyId = "test_graph")
        {
            var a = NewAsset(storyId);
            a.nodes = new List<StoryNodeData>(nodes);
            a.edges = new List<StoryEdge>(edges);
            a.variables = variables != null ? new List<StoryVariableDef>(variables) : new List<StoryVariableDef>();
            return a;
        }
    }

    /// <summary>IStoryPresenter 桩：捕获引擎抛出的一切呈现事件，并可反向触发玩家输入。不依赖任何 UI。</summary>
    internal sealed class StubPresenter : IStoryPresenter
    {
        public readonly List<StoryFlow.Line> Lines = new List<StoryFlow.Line>();
        public readonly List<IReadOnlyList<StoryFlow.Choice>> ChoicesShown = new List<IReadOnlyList<StoryFlow.Choice>>();
        public readonly List<(bool showText, string text)> Ends = new List<(bool, string)>();

        public event Action OnAdvanceRequested;
        public event Action<string> OnChoiceSelected;

        public void ShowLine(StoryFlow.Line line) => Lines.Add(line);
        public void ShowChoices(IReadOnlyList<StoryFlow.Choice> choices) => ChoicesShown.Add(choices);
        public void ShowEnd(bool showText, string text) => Ends.Add((showText, text));

        /// <summary>模拟玩家点击「继续」。</summary>
        public void RaiseAdvance() => OnAdvanceRequested?.Invoke();
        /// <summary>模拟玩家点选某选项。</summary>
        public void RaiseChoose(string optionId) => OnChoiceSelected?.Invoke(optionId);
    }

    /// <summary>IStoryEventHandler 捕获桩：记录全部派发（含瞬时型），挂起型不自动续走（由测试显式触发）。</summary>
    internal sealed class CapturingEventHandler : IStoryEventHandler
    {
        public readonly List<(string name, string payload)> Raised = new List<(string, string)>();
        public Action onComplete;

        public void Raise(string eventName, string payloadJson, Action onComplete)
        {
            Raised.Add((eventName, payloadJson));
            this.onComplete = onComplete; // 挂起语义：不立即回调，由测试决定何时续走
        }

        public void Raise(string eventName, string payloadJson) => Raised.Add((eventName, payloadJson));
    }

    /// <summary>固定映射的文本提供者（本地化桩）：key → 译文；未命中返回 null（引擎回退原文）。</summary>
    internal sealed class StubTextProvider : IStoryTextProvider
    {
        private readonly Dictionary<string, string> _map;
        public StubTextProvider(Dictionary<string, string> map) => _map = map;
        public string ResolveText(string key) => _map != null && _map.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>固定映射的角色解析器：characterId → 显示名。多实例回归测试用（互不相同的两份实例）。</summary>
    internal sealed class StubCharacterResolver : IStoryCharacterResolver
    {
        private readonly string _displayName;
        public StubCharacterResolver(string displayName) => _displayName = displayName;
        public StoryConstants.CharacterViewModel Resolve(string characterId)
            => new StoryConstants.CharacterViewModel { displayName = _displayName, isValid = true };
    }
}
