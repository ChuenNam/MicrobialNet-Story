using System;
using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 运行时本地化资产（单语言）。存储「本地化 Key → 该语言文本」的映射，供
    /// <see cref="LocalizationTextProvider"/> 在播放时按节点 ID 派生的 key 查表解析。
    /// <para>key 规则与编辑器 CSV/Excel 导出完全一致：对话正文 = "{nodeId}.text"，选项 = "{choiceId}.opt.{optionId}"。</para>
    /// <para>运行时多语言 = 为每种语言各建一个资产，切换时把对应语言的资产注入 <see cref="StoryFlowConfig.Text"/>。</para>
    /// </summary>
    [System.Obsolete("使用 StoryLocalizationTable 作为本地化主数据源（多语言、持久化于资产文件夹、进包）。")]
    public sealed class StoryLocalizationAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string key;
            public string text;
        }

        [Tooltip("语言标识（zh-CN / en-US 等），仅供阅读与调试；查表以 key 为准。")]
        public string language;

        [Tooltip("本地化条目：key（节点ID派生）→ 该语言文本。")]
        public List<Entry> entries = new List<Entry>();

        [NonSerialized] private Dictionary<string, string> _lookup;

        private void Rebuild()
        {
            _lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entries == null) return;
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.key) && !_lookup.ContainsKey(e.key))
                    _lookup[e.key] = e.text;
        }

        /// <summary>按 key 取该语言文本；不存在返回 false（播放器回退原文）。</summary>
        public bool TryGet(string key, out string text)
        {
            if (_lookup == null) Rebuild();
            if (key != null && _lookup.TryGetValue(key, out text)) return true;
            text = null;
            return false;
        }
    }
}
