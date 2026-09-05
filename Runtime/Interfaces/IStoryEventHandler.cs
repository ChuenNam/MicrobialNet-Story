using System;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情事件处理器（调用面）。剧情系统只通过它派发事件，具体业务由宿主经
    /// <see cref="StoryFlowConfig.Events"/> 注入。区分两种语义：
    /// <list type="bullet">
    /// <item><description>挂起型 <see cref="Raise(string,string,Action)"/>：事件节点用——剧情等你调 onComplete 才续走（协程式流程控制）。</description></item>
    /// <item><description>瞬时型 <see cref="Raise(string,string)"/>：voice 等用——派发即忘，不挂起流程。</description></item>
    /// </list>
    /// 默认实现见 <see cref="StoryEventBus"/>。
    /// </summary>
    public interface IStoryEventHandler
    {
        /// <summary>
        /// 挂起型事件（事件节点用）。查表调用业务处理器；业务完成逻辑后调 <paramref name="onComplete"/>，剧情续走。
        /// </summary>
        /// <param name="eventName">事件名（事件节点的 eventName 字段）。</param>
        /// <param name="payloadJson">事件参数（事件节点的 eventPayload 字段，JSON 字符串，可能为空）。</param>
        /// <param name="onComplete">业务完成时回调；剧情系统据此续走。未注册事件时由实现决定是否直接回调（StoryEventBus 默认不卡死）。</param>
        void Raise(string eventName, string payloadJson, Action onComplete);

        /// <summary>
        /// 瞬时型事件（voice 等用）。派发即忘，不挂起流程。
        /// </summary>
        /// <param name="eventName">事件名（如 "voice:{key}"）。</param>
        /// <param name="payloadJson">事件参数（可能为空）。</param>
        void Raise(string eventName, string payloadJson);
    }
}
