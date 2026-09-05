using System;
using System.Collections.Generic;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 变量读写提供者。剧情系统不直接依赖具体的存档 / 背包 / UI 实现，
    /// 所有变量读写（条件求值、赋值节点）一律经此接口注入。
    ///
    /// 运行时（Runtime）只认这个接口，不认宿主工程的存档系统；
    /// 宿主在桥接层（StoryBridge）把真实存档/变量系统包装成此接口注入。
    /// </summary>
    public interface IStoryVariableProvider
    {
        /// <summary>是否存在该变量（含类型信息）。</summary>
        bool HasVariable(string variableId);

        /// <summary>取变量类型（条件比较 / 赋值解析依赖它做类型感知）。</summary>
        VariableType GetVariableType(string variableId);

        /// <summary>读取当前值；不存在时返回 false 且 value 为 null。</summary>
        bool TryGetValue(string variableId, out object value);

        /// <summary>写回值（赋值节点调用）。变量不存在时实现可自行创建或忽略。</summary>
        void SetValue(string variableId, object value);

        /// <summary>
        /// 导出当前全部变量的只读快照（变量名 → 当前值），供进度存档使用。
        /// 键集合即「当前所有已知变量」，值的类型由 <see cref="GetVariableType"/> 决定。
        /// </summary>
        IReadOnlyDictionary<string, object> Snapshot();
    }
}
