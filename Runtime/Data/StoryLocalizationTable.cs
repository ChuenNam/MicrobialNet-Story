using System;
using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 本地化主表（唯一真相源，持久化于项目资产文件夹、进包）。
    /// <para>所有可本地化文本（对白正文、选项、讲述者名）以「节点 ID 派生的 key」记录于此，
    /// 含源语言原文（<see cref="Entry.original"/>）与按语言索引的译文（<see cref="Entry.translations"/>）。</para>
    /// <para>它同时是：① 运行时本地化资产（打包进游戏，<see cref="LocalizationTextProvider"/> 读取）；
    /// ② 可编辑主表（Inspector 直接编辑，或导出 CSV/Excel 给翻译后合并回写）。</para>
    /// <para>key 规则：对白正文 = "{nodeId}.text"，选项 = "{choiceId}.opt.{optionId}"，讲述者名 = "character.{speakerId}.name"。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "StoryLocalization", menuName = "MicrobialNet/Story/本地化主表", order = 0)]
    public sealed class StoryLocalizationTable : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string key;
            /// <summary>源语言（defaultLanguage）原文，作为最终回退与翻译参考。</summary>
            public string original;
            /// <summary>各语言译文，按下标与 <see cref="languages"/> 对齐；空串表示未翻译。</summary>
            public List<string> translations = new List<string>();
        }

        [Tooltip("源/默认语言（original 列对应的语言），如 zh-CN。")]
        public string defaultLanguage = "zh-CN";

        [Tooltip("注册的语言列表；Entry.translations 按下标对齐。可在 Inspector 增删语言（增删后译文列自动伸缩）。")]
        public List<string> languages = new List<string> { "zh-CN", "en-US" };

        [Tooltip("本地化条目：key（节点ID派生）→ original（源语言原文）+ 各语言译文。")]
        public List<Entry> entries = new List<Entry>();

        [NonSerialized] private Dictionary<string, Entry> _lookup;

        private void Rebuild()
        {
            _lookup = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (entries == null) return;
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.key) && !_lookup.ContainsKey(e.key))
                    _lookup[e.key] = e;
        }

        /// <summary>语言码在 <see cref="languages"/> 中的下标（大小写不敏感）；未注册返回 -1。</summary>
        public int LangIndex(string lang)
        {
            if (string.IsNullOrEmpty(lang) || languages == null) return -1;
            for (int i = 0; i < languages.Count; i++)
                if (string.Equals(languages[i], lang, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>取某语言译文；该语言无译文或 key 不存在返回 false（播放器回退原文）。仅返回译文本身，不回退 original。</summary>
        public bool TryGetTranslation(string key, string lang, out string text)
        {
            text = null;
            if (_lookup == null) Rebuild();
            if (key == null || !_lookup.TryGetValue(key, out var e)) return false;
            int idx = LangIndex(lang);
            if (idx < 0 || e.translations == null || idx >= e.translations.Count) return false;
            var t = e.translations[idx];
            if (string.IsNullOrEmpty(t)) return false;
            text = t;
            return true;
        }

        /// <summary>取源语言原文（用于导出参考或缺失译文时回退）。</summary>
        public string GetOriginal(string key)
        {
            if (_lookup == null) Rebuild();
            return _lookup.TryGetValue(key, out var e) ? e.original : null;
        }

        public bool ContainsKey(string key)
        {
            if (_lookup == null) Rebuild();
            return key != null && _lookup.ContainsKey(key);
        }

        private List<string> NewTranslations()
        {
            var list = new List<string>();
            int n = languages != null ? languages.Count : 0;
            for (int i = 0; i < n; i++) list.Add(string.Empty);
            return list;
        }

        private void EnsureTranslations(Entry e)
        {
            int n = languages != null ? languages.Count : 0;
            if (e.translations == null) e.translations = new List<string>();
            while (e.translations.Count < n) e.translations.Add(string.Empty);
        }

        /// <summary>从图同步：确保 key 存在（缺失则新增，original=源文本），已存在则刷新 original。译文始终保留，绝不因同步而丢失。</summary>
        public void UpsertOriginal(string key, string original)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_lookup == null) Rebuild();
            if (_lookup.TryGetValue(key, out var e))
            {
                e.original = original ?? string.Empty;
            }
            else
            {
                e = new Entry { key = key, original = original ?? string.Empty, translations = NewTranslations() };
                entries.Add(e);
                _lookup[key] = e;
            }
        }

        /// <summary>设置某语言译文（合并用）。key 不存在会自动创建条目。</summary>
        public void SetTranslation(string key, int langIndex, string value)
        {
            if (string.IsNullOrEmpty(key) || langIndex < 0) return;
            if (_lookup == null) Rebuild();
            if (!_lookup.TryGetValue(key, out var e))
            {
                e = new Entry { key = key, original = string.Empty, translations = NewTranslations() };
                entries.Add(e);
                _lookup[key] = e;
            }
            EnsureTranslations(e);
            if (langIndex < e.translations.Count) e.translations[langIndex] = value ?? string.Empty;
        }

        /// <summary>设置源语言原文（合并用）。key 不存在会自动创建条目。</summary>
        public void SetOriginal(string key, string original)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_lookup == null) Rebuild();
            if (!_lookup.TryGetValue(key, out var e))
            {
                e = new Entry { key = key, original = original ?? string.Empty, translations = NewTranslations() };
                entries.Add(e);
                _lookup[key] = e;
            }
            else e.original = original ?? string.Empty;
        }
    }
}
