using System;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 变量值解析工具：把节点里以字符串存储的值（默认值 / 比较值 / 设定值）按目标类型解析为强类型对象。
    /// 解析失败时按类型回退到零值，绝不抛异常（剧情运行时不允许因脏数据中断）。
    /// </summary>
    internal static class ValueParser
    {
        /// <summary>按类型把字符串解析为值对象。</summary>
        public static object Parse(string raw, VariableType type)
        {
            switch (type)
            {
                case VariableType.Int:
                    return int.TryParse(raw, out var i) ? i : 0;
                case VariableType.Float:
                    return float.TryParse(raw, out var f) ? f : 0f;
                case VariableType.Bool:
                    return bool.TryParse(raw, out var b) && b;
                case VariableType.String:
                default:
                    return raw ?? string.Empty;
            }
        }

        /// <summary>把值对象转回字符串（用于日志 / 摘要展示）。</summary>
        public static string ToString(object value)
            => value == null ? string.Empty : value.ToString();
    }
}
