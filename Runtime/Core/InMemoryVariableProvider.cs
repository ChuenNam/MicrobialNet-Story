using System.Collections.Generic;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 内存变量提供者：用剧情图的变量定义（局部）+ 可选全局变量播种默认值，
    /// 在内存中维护一份变量快照。供独立验证（编辑器 Play 无需宿主存档系统即可跑）。
    ///
    /// 接入宿主时由 StoryBridge 用真实存档 / 变量系统实现 <see cref="IStoryVariableProvider"/> 替换本类，
    /// 剧情逻辑无需改动。
    /// </summary>
    public sealed class InMemoryVariableProvider : IStoryVariableProvider
    {
        private readonly Dictionary<string, VariableType> _types = new Dictionary<string, VariableType>();
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        /// <summary>
        /// 构造。局部与全局变量定义都传入时，全局作为兜底（不覆盖已播种的局部同名变量）。
        /// </summary>
        /// <param name="localVariables">本图变量黑板定义（必填）。</param>
        /// <param name="globalVariables">跨章节全局变量定义（可选）。</param>
        public InMemoryVariableProvider(IEnumerable<StoryVariableDef> localVariables, IEnumerable<StoryVariableDef> globalVariables = null)
        {
            if (localVariables != null) Seed(localVariables);
            if (globalVariables != null) Seed(globalVariables);
        }

        private void Seed(IEnumerable<StoryVariableDef> defs)
        {
            foreach (var d in defs)
            {
                if (string.IsNullOrEmpty(d.id)) continue;
                _types[d.id] = d.type;
                // 局部优先：已播种则不覆盖（保证本图定义覆盖全局同名）
                if (!_values.ContainsKey(d.id))
                    _values[d.id] = ValueParser.Parse(d.defaultValue, d.type);
            }
        }

        public bool HasVariable(string variableId) => _types.ContainsKey(variableId);

        public VariableType GetVariableType(string variableId)
            => _types.TryGetValue(variableId, out var t) ? t : VariableType.String;

        public bool TryGetValue(string variableId, out object value)
            => _values.TryGetValue(variableId, out value);

        public void SetValue(string variableId, object value)
        {
            if (!_types.ContainsKey(variableId)) _types[variableId] = VariableType.String;
            _values[variableId] = value;
        }

        /// <summary>调试用：导出当前全部变量快照（变量快照 / 存档雏形）。</summary>
        public IReadOnlyDictionary<string, object> Snapshot() => new Dictionary<string, object>(_values);
    }
}
