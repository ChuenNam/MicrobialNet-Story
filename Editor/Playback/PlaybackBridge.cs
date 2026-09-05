using System;
using System.Collections.Generic;

namespace MicrobialNet.Story.EditorTools.Playback
{
    /// <summary>试跑运行时快照：当前节点 + 变量实时值（名称/值文本），供主窗口「运行时监视区」展示。
    /// 以纯数据形式解耦，主窗口不感知 StorySimulator 内部类型。</summary>
    public sealed class RuntimeSnapshot
    {
        public bool active;                                   // 是否处于预览态（false = 灰显）
        public string nodeId;                                 // 当前节点 ID
        public string nodeTypeLabel;                          // 当前节点类型中文标签
        public IReadOnlyDictionary<string, string> vars;      // 变量显示名 -> 实时值文本
    }

    /// <summary>
    /// 试跑窗口与主编辑器之间的解耦桥。
    /// 试跑窗口只负责「请求」高亮/清除/路径、以及「广播」运行时状态，由主编辑器（StoryGraphWindow）自己订阅并操作画布 / 监视区，
    /// 避免试跑窗口反向 GetWindow 主窗口导致主窗口抢占焦点 / 被置顶盖住试跑窗口。
    /// </summary>
    public static class PlaybackBridge
    {
        public static event Action<string> HighlightRequested;
        public static event Action ClearRequested;
        public static event Action<List<string>> PathRequested;
        public static event Action<RuntimeSnapshot> StateUpdated;   // 运行时状态广播（供主窗口运行时监视区）

        public static void RequestHighlight(string nodeId) => HighlightRequested?.Invoke(nodeId);
        public static void RequestClear() => ClearRequested?.Invoke();
        public static void RequestPath(List<string> path) => PathRequested?.Invoke(path);
        public static void PushState(RuntimeSnapshot snap) => StateUpdated?.Invoke(snap);
    }
}
