using System;
using UnityEditor;
using UnityEngine;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools.Tests
{
    /// <summary>
    /// 业务端事件定义示例（演示「自定义事件业务侧编写、易管理」）。
    /// 标记 [StoryEvent] 后，既能被编辑器事件名下拉收集，也能被 StoryEventBus.AutoRegister 自动发现。
    /// </summary>
    [StoryEvent("test:confirm")]
    internal sealed class ConfirmEvent : IStoryEvent
    {
        public string EventName => "test:confirm";

        /// <summary>业务侧存起完成回调——模拟「弹界面等玩家操作」的挂起语义。</summary>
        public Action Pending { get; private set; }

        public void Execute(string payloadJson, Action onComplete)
        {
            // 真实业务：这里打开一个 UI 界面，玩家点击「确认」后调 PlayerConfirmed()。
            Pending = onComplete;
        }

        /// <summary>模拟玩家在界面上的操作完成后，业务主动通知剧情续走。</summary>
        public void PlayerConfirmed() => Pending?.Invoke();
    }

    [StoryEvent("test:log")]
    internal sealed class LogEvent : IStoryEvent
    {
        public string EventName => "test:log";

        public void Execute(string payloadJson, Action onComplete)
        {
            Debug.Log($"[业务事件] {EventName} payload={payloadJson}");
            onComplete?.Invoke(); // 瞬时式：立即续走
        }
    }

    /// <summary>
    /// 可挂起事件系统 · 业务端验证脚本。
    /// 菜单：Story/测试/验证事件系统
    /// 运行后在 Console 打印 PASS/FAIL 汇总并弹窗。
    /// </summary>
    public static class StoryEventSystemTest
    {
        [MenuItem("MicrobialNet/Story/测试/验证事件系统")]
        public static void Run()
        {
            int pass = 0, fail = 0;
            void Check(string name, bool ok)
            {
                if (ok) { pass++; Debug.Log($"PASS  {name}"); }
                else { fail++; Debug.LogError($"FAIL  {name}"); }
            }

            var bus = new StoryEventBus();

            // 1) 类式注册（业务侧注入 IStoryEvent 实现）
            var confirm = new ConfirmEvent();
            bus.Register(confirm);

            // 2) 委托式注册（最方便，各业务模块只注册自己关心的事件）
            bool logRan = false;
            bus.Register("test:log", (p, done) => { logRan = true; done?.Invoke(); });

            // 3) 注册可见性
            Check("注册后 Contains 命中", bus.Contains("test:confirm") && bus.Contains("test:log"));

            // 4) 挂起语义：Raise 后流程「暂停」，必须等业务调 onComplete 才续走
            bool continued = false;
            bus.Raise("test:confirm", "{\"ok\":true}", () => { continued = true; });
            Check("挂起事件：Raise 后未立即续走（流程已暂停）", !continued && confirm.Pending != null);
            confirm.PlayerConfirmed(); // 模拟玩家在界面点击
            Check("挂起事件：业务回调后流程续走", continued);

            // 5) 瞬时语义 + 委托式：派发即续走
            bool continued2 = false;
            bus.Raise("test:log", "hello", () => { continued2 = true; });
            Check("瞬时事件：委托式立即续走", logRan && continued2);

            // 6) 未注册事件：不卡死，直接回调 onComplete
            bool fallback = false;
            bus.Raise("not:registered", "", () => { fallback = true; });
            Check("未注册事件：直接回调不卡死", fallback);

            // 7) 覆盖注册：后注册者替换前注册者
            bus.Register("test:log", (p, done) => { done?.Invoke(); });
            bool c3 = false;
            bus.Raise("test:log", "", () => { c3 = true; });
            Check("覆盖注册后旧处理器被替换", c3);

            // 8) 移除注册
            bus.Unregister("test:log");
            Check("Unregister 后 Contains 失效", !bus.Contains("test:log"));

            // 9) AutoRegister：自动发现同程序集带 [StoryEvent] 的类（分散、按需，不持全量清单）
            var bus2 = new StoryEventBus();
            bus2.AutoRegister(typeof(ConfirmEvent).Assembly);
            Check("AutoRegister 自动发现 test:confirm", bus2.Contains("test:confirm"));

            // 10) 注入接缝：业务事件总线可作为 StoryFlowConfig.Events 注入剧情系统
            var cfg = new StoryFlowConfig { Events = bus };
            Check("业务事件总线可注入 StoryFlowConfig.Events", ReferenceEquals(cfg.Events, bus));

            string summary = $"事件系统测试\n通过 {pass} / 失败 {fail}";
            Debug.Log(summary);
            EditorUtility.DisplayDialog("事件系统测试", summary, "OK");
        }
    }
}
