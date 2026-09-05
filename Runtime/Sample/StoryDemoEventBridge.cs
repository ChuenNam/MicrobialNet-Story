using System;
using System.Collections;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 示例桥接：在 Play 模式把「真实业务事件总线」注入示例宿主，取代默认的打印型 handler。
    /// 目的——证明事件节点（EventNodeData）确实经 <see cref="IStoryEventHandler"/> 分发到业务事件，而非内部直接打印。
    /// 注意：本组件必须放在 Runtime 程序集（不能放 Editor/ 子树），否则 Editor-only 的 MonoBehaviour 无法挂载/序列化。
    /// 仅用于示例场景，不依赖任何 UnityEditor API；正式构建若不包含示例场景则不会带它。
    /// </summary>
    [RequireComponent(typeof(StoryFlow))]
    public sealed class StoryDemoEventBridge : MonoBehaviour
    {
        private void Awake()
        {
            var host = GetComponent<StoryFlow>();
            if (host == null) return;

            var bus = new StoryEventBus();
            // 注册一个示例业务事件，命中内置示例图事件节点的 eventName="confirm:battle_start"。
            bus.Register(new SampleBattleEvent(this));

            // 注入事件系统；宿主 Start 时会读取 _config.Events 装配播放器，从而走真实业务事件而非默认打印。
            host.Configure(new StoryFlowConfig { Events = bus });
            Debug.Log("[StoryDemoEventBridge] 已注入真实 StoryEventBus（示例业务事件 confirm:battle_start）。");
        }

        /// <summary>
        /// 示例业务事件：演示「事件节点 → 事件系统 → 业务事件（挂起，等业务完成才续走）」。
        /// 真实项目里这里会触发战斗系统，等战斗结束再调 onComplete 让剧情续走。
        /// </summary>
        [StoryEvent("confirm:battle_start")]
        private sealed class SampleBattleEvent : IStoryEvent
        {
            private readonly StoryDemoEventBridge _bridge;
            public SampleBattleEvent(StoryDemoEventBridge bridge) => _bridge = bridge;

            public string EventName => "confirm:battle_start";

            public void Execute(string payloadJson, Action onComplete)
            {
                Debug.Log($"[示例业务事件] confirm:battle_start 被调用 ▶ payload={payloadJson}（挂起演示：1.5 秒后自动续走）");
                _bridge.StartCoroutine(CompleteAfterDelay(1.5f, onComplete));
            }

            private IEnumerator CompleteAfterDelay(float seconds, Action done)
            {
                yield return new WaitForSeconds(seconds);
                Debug.Log("[示例业务事件] confirm:battle_start 完成，剧情续走。");
                done?.Invoke();
            }
        }
    }
}
