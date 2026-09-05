using System;
using System.Collections.Generic;
using System.Reflection;

namespace MicrobialNet.Story
{
    /// <summary>
    /// <see cref="IStoryEventHandler"/> 的默认实现：按事件名注册单个处理器的注册表（Registry 模式）。
    /// 业务侧**细粒度、分散**注册自己关心的事件；剧情系统按需查表分发，不持有全量事件清单。
    /// 满足「自定义事件业务侧编写注入、创建方便易管理、避免一下子注册/获取所有事件」。
    /// </summary>
    public sealed class StoryEventBus : IStoryEventHandler
    {
        private readonly Dictionary<string, Action<string, Action>> _handlers
            = new Dictionary<string, Action<string, Action>>();

        /// <summary>注册一个事件处理器（委托式，最方便）。各业务模块只注册自己关心的事件。</summary>
        public void Register(string eventName, Action<string, Action> handler)
        {
            if (string.IsNullOrEmpty(eventName)) throw new ArgumentException("eventName 不能为空", nameof(eventName));
            _handlers[eventName] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>注册一个事件处理器（类式，实现 <see cref="IStoryEvent"/>）。</summary>
        public void Register(IStoryEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            Register(e.EventName, (payload, done) => e.Execute(payload, done));
        }

        /// <summary>移除一个事件（按名）。</summary>
        public void Unregister(string eventName) => _handlers.Remove(eventName);

        /// <summary>是否存在某事件的注册。</summary>
        public bool Contains(string eventName) => !string.IsNullOrEmpty(eventName) && _handlers.ContainsKey(eventName);

        /// <summary>
        /// 挂起型：查表调用业务处理器；业务调 onComplete 时剧情续走。
        /// 未注册事件不卡死剧情——直接调用 onComplete（宿主可借 OnEvent 监听调试）。
        /// </summary>
        public void Raise(string eventName, string payloadJson, Action onComplete)
        {
            if (_handlers.TryGetValue(eventName, out var h))
                h(payloadJson, onComplete);
            else
                onComplete?.Invoke();
        }

        /// <summary>瞬时型：查表调用，忽略 onComplete 语义（派发即忘，不挂起流程）。</summary>
        public void Raise(string eventName, string payloadJson)
        {
            if (_handlers.TryGetValue(eventName, out var h))
                h(payloadJson, () => { });
        }

        /// <summary>
        /// 可选：扫描给定程序集中带 <see cref="StoryEventAttribute"/> 且实现 <see cref="IStoryEvent"/> 的类，自动注册其实例。
        /// 仍是分散/按需的（只登记显式标记的类），剧情系统不持有全量清单。
        /// </summary>
        public void AutoRegister(Assembly assembly)
        {
            if (assembly == null) return;
            foreach (var t in assembly.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IStoryEvent).IsAssignableFrom(t)) continue;
                var attr = t.GetCustomAttribute<StoryEventAttribute>();
                if (attr == null) continue;
                if (Activator.CreateInstance(t) is IStoryEvent e)
                    Register(e);
            }
        }
    }
}
