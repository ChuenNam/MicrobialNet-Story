using System;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using NUnit.Framework;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 事件目录单一事实源测试（P5/L1）：目录事件名以 IStoryEvent.EventName（运行时注册口径）为准，
    /// [StoryEvent] 特性名仅承担 TypeCache 发现与回退职责；双名漂移产出黄条文案。
    /// 测试类均**不带 [StoryEvent]**（特性名以参数显式传入），避免被 TypeCache 扫进
    /// 编辑器真实下拉/校验造成工程污染；<see cref="StoryEventCatalog.ResolveEventName"/> 为纯函数，无域缓存耦合。
    /// </summary>
    public class StoryEventCatalogTests
    {
        private sealed class ConsistentEvent : IStoryEvent
        {
            public string EventName => "cat:consistent";
            public void Execute(string payloadJson, Action onComplete) => onComplete?.Invoke();
        }

        /// <summary>构造需注入（无无参构造）的事件类——验证回退路径。</summary>
        private sealed class InjectedEvent : IStoryEvent
        {
            private readonly string _name;
            public InjectedEvent(string name) => _name = name;
            public string EventName => _name;
            public void Execute(string payloadJson, Action onComplete) => onComplete?.Invoke();
        }

        private sealed class NullNameEvent : IStoryEvent
        {
            public string EventName => null;
            public void Execute(string payloadJson, Action onComplete) => onComplete?.Invoke();
        }

        [Test]
        public void Resolve_ConsistentNames_NoWarning()
        {
            var (name, warning) = StoryEventCatalog.ResolveEventName(typeof(ConsistentEvent), "cat:consistent");
            Assert.AreEqual("cat:consistent", name);
            Assert.IsNull(warning, "特性名与 EventName 一致：无黄条");
        }

        [Test]
        public void Resolve_Mismatch_PrefersRuntimeNameAndWarns()
        {
            var (name, warning) = StoryEventCatalog.ResolveEventName(typeof(ConsistentEvent), "cat:wrong_attr");
            Assert.AreEqual("cat:consistent", name,
                "运行时按 EventName 注册，目录同源取 EventName——消灭「下拉能选到、运行时查无」");
            Assert.IsNotNull(warning, "双名漂移应产出黄条文案");
            StringAssert.Contains("cat:wrong_attr", warning);
            StringAssert.Contains("cat:consistent", warning);
        }

        [Test]
        public void Resolve_NoParameterlessCtor_FallsBackToAttributeName_Silently()
        {
            var (name, warning) = StoryEventCatalog.ResolveEventName(typeof(InjectedEvent), "cat:injected");
            Assert.AreEqual("cat:injected", name, "无法实例化：退回特性名，保持事件可发现");
            Assert.IsNull(warning, "读不到运行时名时不判一致性（对需注入状态的事件类不产噪音）");
        }

        [Test]
        public void Resolve_NullEventName_FallsBackToAttributeName()
        {
            var (name, warning) = StoryEventCatalog.ResolveEventName(typeof(NullNameEvent), "cat:nullname");
            Assert.AreEqual("cat:nullname", name, "EventName 属性返回空：等价读不到运行时名");
            Assert.IsNull(warning);
        }

        [Test]
        public void Resolve_NullAttrName_NoWarning_UsesRuntimeName()
        {
            // 类未标 [StoryEvent]（仅被直接调用）：attrName 为 null，无从比较，取运行时名不告警
            var (name, warning) = StoryEventCatalog.ResolveEventName(typeof(ConsistentEvent), null);
            Assert.AreEqual("cat:consistent", name);
            Assert.IsNull(warning);
        }
    }
}
