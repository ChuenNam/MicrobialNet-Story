using System;
using MicrobialNet.Story;
using NUnit.Framework;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 事件总线测试（把既有菜单式手动验证脚本固化为自动化）：
    /// 注册/查询/移除、挂起语义（等 onComplete 才续走）、未注册不卡死、覆盖注册、
    /// 瞬时型派发、[StoryEvent] 程序集自动发现、作为 StoryFlowConfig.Events 注入。
    /// </summary>
    public class EventBusTests
    {
        [StoryEvent("test:auto_discovers")]
        internal sealed class AutoDiscoveredEvent : IStoryEvent
        {
            public string EventName => "test:auto_discovers";
            public void Execute(string payloadJson, Action onComplete) => onComplete?.Invoke();
        }

        [Test]
        public void RegisterAndContains()
        {
            var bus = new StoryEventBus();
            bus.Register("evt:a", (p, done) => done?.Invoke());
            Assert.IsTrue(bus.Contains("evt:a"));
            Assert.IsFalse(bus.Contains("evt:missing"));
            Assert.IsFalse(bus.Contains(null));
            Assert.IsFalse(bus.Contains(""));
        }

        [Test]
        public void SuspendSemantics_RaisesThenWaitsForOnComplete()
        {
            var bus = new StoryEventBus();
            Action pending = null;
            bus.Register("suspend", (payload, done) => pending = done);

            bool continued = false;
            bus.Raise("suspend", "{}", () => continued = true);
            Assert.IsFalse(continued, "挂起型：Raise 后未立即续走");
            Assert.IsNotNull(pending, "处理器收到完成回调");

            pending.Invoke();
            Assert.IsTrue(continued, "业务回调后流程续走");
        }

        [Test]
        public void UnregisteredEvent_DoesNotDeadlock_InvokesOnComplete()
        {
            var bus = new StoryEventBus();
            bool continued = false;
            bus.Raise("not:registered", "", () => continued = true);
            Assert.IsTrue(continued, "未注册事件直接回调（不卡死剧情）");
        }

        [Test]
        public void TransientRaise_IgnoresCompletion()
        {
            var bus = new StoryEventBus();
            string seenPayload = null;
            bus.Register("transient", (payload, done) => seenPayload = payload);

            bus.Raise("transient", "hello", () => Assert.Fail("瞬时型不应携带续走语义"));
            Assert.AreEqual("hello", seenPayload);
        }

        [Test]
        public void OverwriteRegistration_ReplacesHandler()
        {
            var bus = new StoryEventBus();
            int first = 0, second = 0;
            bus.Register("evt", (p, d) => { first++; d?.Invoke(); });
            bus.Register("evt", (p, d) => { second++; d?.Invoke(); });

            bus.Raise("evt", "", null);
            Assert.AreEqual(0, first, "后注册者替换前注册者");
            Assert.AreEqual(1, second);
        }

        [Test]
        public void Unregister_RemovesRegistration()
        {
            var bus = new StoryEventBus();
            bus.Register("evt", (p, d) => d?.Invoke());
            bus.Unregister("evt");
            Assert.IsFalse(bus.Contains("evt"));

            bool continued = false;
            bus.Raise("evt", "", () => continued = true);
            Assert.IsTrue(continued, "移除后按未注册处理（直接回调）");
        }

        [Test]
        public void ClassRegistration_WrapsIStoryEvent()
        {
            var bus = new StoryEventBus();
            bus.Register(new AutoDiscoveredEvent());
            Assert.IsTrue(bus.Contains("test:auto_discovers"));

            bool continued = false;
            bus.Raise("test:auto_discovers", "", () => continued = true);
            Assert.IsTrue(continued);
        }

        [Test]
        public void AutoRegister_DiscoversAttributedEventsInAssembly()
        {
            var bus = new StoryEventBus();
            bus.AutoRegister(typeof(AutoDiscoveredEvent).Assembly);
            Assert.IsTrue(bus.Contains("test:auto_discovers"), "扫描本测试程序集应发现 [StoryEvent] 类");
        }

        [Test]
        public void Register_InvalidArguments_Throw()
        {
            var bus = new StoryEventBus();
            Assert.Throws<ArgumentException>(() => bus.Register(null, (p, d) => { }));
            Assert.Throws<ArgumentException>(() => bus.Register("", (p, d) => { }));
            Assert.Throws<ArgumentNullException>(() => bus.Register("evt", null));
            Assert.Throws<ArgumentNullException>(() => bus.Register(null));
        }

        [Test]
        public void Bus_IsInjectableIntoFlowConfig()
        {
            var bus = new StoryEventBus();
            var cfg = new StoryFlowConfig { Events = bus };
            Assert.AreSame(bus, cfg.Events, "事件总线可作为接缝注入（宿主桥接层零改动契约）");
        }
    }
}
