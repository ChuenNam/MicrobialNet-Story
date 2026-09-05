using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 反射工具：支持「字段路径」读写（如 "text"、"options[0].text"）与多态对象深克隆。
    /// 供属性面板自动生成与复制/粘贴复用，避免命令层耦合具体节点类型。
    /// </summary>
    public static class ReflectionUtil
    {
        private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>仅公开字段的绑定标志（供模型做反向引用扫描）。</summary>
        public const BindingFlags BF_PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>按路径读取字段值。路径示例："text"、"options[0].text"。</summary>
        public static object GetValue(object target, string path)
        {
            if (target == null) return null;
            object cur = target;
            foreach (var seg in path.Split('.'))
            {
                if (cur == null) return null;
                ParseSegment(seg, out var name, out var index);
                var f = cur.GetType().GetField(name, BF);
                if (f == null) return null;
                cur = f.GetValue(cur);
                if (index >= 0 && cur is IList list)
                    cur = index < list.Count ? list[index] : null;
            }
            return cur;
        }

        /// <summary>按路径写入字段值。自动做枚举/布尔/数值类型转换。</summary>
        public static void SetValue(object target, string path, object value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            object cur = target;
            var segments = path.Split('.');
            for (int i = 0; i < segments.Length; i++)
            {
                ParseSegment(segments[i], out var name, out var index);
                var f = cur.GetType().GetField(name, BF);
                if (f == null) throw new ArgumentException($"字段不存在: {name}（路径 {path}）");
                if (i == segments.Length - 1)
                {
                    f.SetValue(cur, Convert(value, f.FieldType));
                    return;
                }
                cur = f.GetValue(cur);
                if (index >= 0 && cur is IList list)
                    cur = index < list.Count ? list[index] : null;
            }
        }

        private static void ParseSegment(string seg, out string name, out int index)
        {
            int b = seg.IndexOf('[');
            if (b >= 0 && seg.EndsWith("]"))
            {
                name = seg.Substring(0, b);
                index = int.Parse(seg.Substring(b + 1, seg.Length - b - 2));
            }
            else
            {
                name = seg;
                index = -1;
            }
        }

        private static object Convert(object value, Type target)
        {
            if (value == null) return null;
            if (target.IsInstanceOfType(value)) return value;
            if (target.IsEnum) return Enum.Parse(target, value.ToString());
            if (target == typeof(string)) return value.ToString();
            if (target == typeof(bool) && value is string sb) return bool.Parse(sb);
            try { return System.Convert.ChangeType(value, target); }
            catch { return value; }
        }

        /// <summary>深克隆一个多态可序列化对象（含嵌套 List / 类）。用于复制/粘贴。</summary>
        public static T DeepClone<T>(T source) where T : class => source == null ? null : (T)Clone(source);

        private static object Clone(object obj)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            if (t.IsValueType || t == typeof(string)) return obj;
            if (obj is IList list)
            {
                if (t.IsArray)
                {
                    var elem = t.GetElementType();
                    var arr = Array.CreateInstance(elem, list.Count);
                    for (int i = 0; i < list.Count; i++) arr.SetValue(Clone(list[i]), i);
                    return arr;
                }
                var clone = (IList)Activator.CreateInstance(t);
                foreach (var item in list) clone.Add(Clone(item));
                return clone;
            }
            var copy = Activator.CreateInstance(t);
            foreach (var f in t.GetFields(BF)) f.SetValue(copy, Clone(f.GetValue(obj)));
            return copy;
        }
    }
}
