using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>节点类型注册表的一条记录：类型 + 其 [StoryNode] 特性。</summary>
    public sealed class NodeTypeEntry
    {
        public Type Type;
        public StoryNodeAttribute Attr;
    }

    /// <summary>
    /// 节点类型注册中心。
    /// 通过反射扫描所有已加载程序集中带 [StoryNode] 的 StoryNodeData 子类，
    /// 构建「创建菜单」数据源与「按类型取特性」缓存。
    ///
    /// 设计要点（扩展性地基）：
    /// - 新增节点类型无需改动此处代码，只需新增一个带 [StoryNode] 的数据类。
    /// - 反射结果缓存，避免每次取菜单都全量扫描。
    /// - 运行时反序列化后如需按类型实例化新节点，也走 Create()。
    /// </summary>
    internal static class NodeRegistry
    {
        private static List<NodeTypeEntry> _entries;
        private static Dictionary<Type, StoryNodeAttribute> _attrCache;
        private static readonly object _lock = new object();

        /// <summary>所有已注册节点类型（按 Order 升序）。首次访问触发构建。</summary>
        public static IReadOnlyList<NodeTypeEntry> Entries
        {
            get { Ensure(); return _entries; }
        }

        /// <summary>强制重新扫描程序集（例如在热重载或动态加载程序集后调用）。</summary>
        public static void Refresh()
        {
            lock (_lock)
            {
                _entries = null;
                _attrCache = null;
            }
            Ensure();
        }

        private static void Ensure()
        {
            if (_entries != null) return;
            lock (_lock)
            {
                if (_entries != null) return;
                Build();
            }
        }

        private static void Build()
        {
            var entries = new List<NodeTypeEntry>();
            var attrCache = new Dictionary<Type, StoryNodeAttribute>();
            var baseType = typeof(StoryNodeData);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || !baseType.IsAssignableFrom(t)) continue;
                    var attr = t.GetCustomAttribute<StoryNodeAttribute>();
                    if (attr == null) continue;
                    entries.Add(new NodeTypeEntry { Type = t, Attr = attr });
                    attrCache[t] = attr;
                }
            }

            entries.Sort((a, b) => a.Attr.Order.CompareTo(b.Attr.Order));
            _entries = entries;
            _attrCache = attrCache;
        }

        /// <summary>取某节点类型的 [StoryNode] 特性（缓存命中）。</summary>
        public static StoryNodeAttribute GetAttr(Type t)
        {
            Ensure();
            return _attrCache.TryGetValue(t, out var a) ? a : null;
        }

        /// <summary>实例化一个节点数据对象，并分配唯一 ID。</summary>
        public static StoryNodeData Create(Type t)
        {
            if (t == null || !typeof(StoryNodeData).IsAssignableFrom(t) || t.IsAbstract)
                throw new ArgumentException($"无法创建节点类型：{t?.FullName}");

            var inst = (StoryNodeData)Activator.CreateInstance(t);
            inst.id = Guid.NewGuid().ToString("N");
            return inst;
        }

        /// <summary>按类型全名或简名实例化节点。</summary>
        public static StoryNodeData Create(string typeName)
        {
            Ensure();
            var entry = _entries.FirstOrDefault(e =>
                e.Type.FullName == typeName || e.Type.Name == typeName);
            if (entry == null)
                throw new ArgumentException($"未知节点类型：{typeName}");
            return Create(entry.Type);
        }

        /// <summary>按分类取节点类型（用于创建菜单分组）。</summary>
        public static IReadOnlyList<NodeTypeEntry> ByCategory(string category)
            => Entries.Where(e => e.Attr.Category == category).ToList();
    }
}
