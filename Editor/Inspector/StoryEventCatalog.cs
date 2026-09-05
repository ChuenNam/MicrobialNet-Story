using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 编辑器侧收集已知业务事件名（供 [StoryEventPicker] 下拉），不要求剧情系统掌握全量清单。
    /// 通过 Unity TypeCache 扫描带 [StoryEvent] 且实现 IStoryEvent 的类。
    ///
    /// P5/L1 单一事实源：事件名取 IStoryEvent.EventName（实例化读取）——与运行时注册口径
    /// （StoryEventBus.Register(IStoryEvent) 按 EventName 建表）同源；[StoryEvent] 特性名仅承担
    /// TypeCache 快速发现（与类无法实例化时的回退）职责。修复前取特性名，双名漂移会让
    /// 「下拉能选到、运行时查无」——未注册事件经 Raise 静默直通，事件被吞零日志。
    /// </summary>
    public static class StoryEventCatalog
    {
        private static List<string> _names;             // 事件名（EventName 口径，排序去重）
        private static List<string> _mismatchWarnings;  // 「特性名≠EventName」黄条文案

        public static IReadOnlyList<string> GetKnownEventNames()
        {
            EnsureBuilt();
            return _names;
        }

        /// <summary>特性名与 EventName 不一致的事件类（格式化文案；空 = 全一致）。
        /// 供 StoryValidator 产出 EventNameMismatch 图级黄条（仅含事件节点的图提示）。</summary>
        public static IReadOnlyList<string> GetAttributeNameMismatches()
        {
            EnsureBuilt();
            return _mismatchWarnings;
        }

        /// <summary>
        /// 解析事件类的目录事件名与一致性警告（纯函数、可单测——特性名以参数传入，
        /// 测试类无需带 [StoryEvent]，避免被 TypeCache 扫进真实下拉造成工程污染）。
        /// 规则：能实例化 → 取 EventName（运行时口径），与 attributeName 不一致时产出黄条文案；
        /// 不能实例化（无无参构造/构造抛异常/EventName 为空）→ 静默退回 attributeName（保持可发现，
        /// 且不判一致性——读不到运行时名，判了也是噪音）。两者皆空 → (null, null)，调用方跳过。
        /// </summary>
        internal static (string eventName, string mismatchWarning) ResolveEventName(Type t, string attributeName)
        {
            string runtimeName = null;
            try { if (Activator.CreateInstance(t) is IStoryEvent e) runtimeName = e.EventName; }
            catch { /* 无无参构造或构造抛异常：读不到运行时名，走特性名回退 */ }
            if (string.IsNullOrEmpty(runtimeName))
                return (attributeName, null);
            if (!string.IsNullOrEmpty(attributeName) && attributeName != runtimeName)
                return (runtimeName,
                    $"事件类 {t.FullName} 的 [StoryEvent(\"{attributeName}\")] 特性名与 EventName 属性「{runtimeName}」不一致："
                    + $"运行时按「{runtimeName}」注册（事件名下拉同源取此值），请统一两者，否则会出现「下拉可选、运行时查无、事件被静默跳过」。");
            return (runtimeName, null);
        }

        private static void EnsureBuilt()
        {
            if (_names != null) return; // 事件类集合随域重载重置；编辑器会话内不变，缓存无失效问题
            var names = new List<string>();
            var warnings = new List<string>();
            foreach (var t in TypeCache.GetTypesWithAttribute<StoryEventAttribute>()
                .Where(t => typeof(IStoryEvent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                var attrName = t.GetCustomAttribute<StoryEventAttribute>()?.Name;
                var (eventName, warning) = ResolveEventName(t, attrName);
                if (string.IsNullOrEmpty(eventName)) continue;
                if (warning != null) warnings.Add(warning);
                if (!names.Contains(eventName)) names.Add(eventName);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            _names = names;
            _mismatchWarnings = warnings;
        }
    }
}
