using System;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 单个业务事件的契约。事件节点（EventNodeData）派发时，剧情系统只认这个名字 + 参数 + 完成回调，
    /// 语义全在业务侧。业务实现 <see cref="Execute"/> 做自己的事，做完（含异步 UI 交互）调
    /// <paramref name="onComplete"/>，剧情才续走。剧情系统零业务知识。
    /// </summary>
    public interface IStoryEvent
    {
        /// <summary>事件名（与剧情图里 EventNodeData.eventName 对应）。</summary>
        string EventName { get; }

        /// <summary>
        /// 执行事件。
        /// </summary>
        /// <param name="payloadJson">事件参数（EventNodeData.eventPayload，JSON 字符串，可能为空）。</param>
        /// <param name="onComplete">业务完成时回调——剧情系统据此续走后续流程。务必调用，否则剧情永久挂起。</param>
        void Execute(string payloadJson, Action onComplete);
    }

    /// <summary>
    /// 标记业务事件类，供编辑器事件名下拉收集（[StoryEventPicker]）与可选自动发现（StoryEventBus.AutoRegister）。
    /// 实现 <see cref="IStoryEvent"/> 即可被注册表识别。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class StoryEventAttribute : Attribute
    {
        public string Name { get; }
        public StoryEventAttribute(string name) => Name = name;
    }
}
