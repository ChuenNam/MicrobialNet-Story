using System;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>变量数据类型。</summary>
    public enum VariableType
    {
        Int,
        Float,
        Bool,
        String,
    }

    /// <summary>变量作用域。Local 仅在单张剧情图实例内有效；Global 跨图持久（由变量提供者管理）。</summary>
    public enum VariableScope
    {
        Local,
        Global,
    }

    /// <summary>
    /// 变量定义（变量黑板中的一行）。存储于 StoryGraphAsset.variables。
    /// 默认值以字符串保存，运行时按 type 解析，便于 JSON 序列化与多格式兼容。
    /// </summary>
    [Serializable]
    public sealed class StoryVariableDef
    {
        /// <summary>变量稳定 ID（条件/赋值节点引用它，而非显示名，改名不影响配置）。</summary>
        public string id;

        /// <summary>显示名。</summary>
        public string name;

        public VariableType type = VariableType.Int;
        public VariableScope scope = VariableScope.Local;

        /// <summary>以字符串存储的默认值，运行时按 type 解析。</summary>
        public string defaultValue;

        /// <summary>说明（不参与执行）。</summary>
        public string description;
    }
}
