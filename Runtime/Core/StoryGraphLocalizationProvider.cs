using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 图绑定本地化文本提供者（实现 <see cref="IStoryTextProvider"/>）。
    /// 持有「当前图」引用：<see cref="ResolveText"/> 时从当前图的 <see cref="StoryGraphAsset.localizationTable"/> 取译文；
    /// 当前图未设置表时回落到 <paramref name="fallback"/>（StoryFlow Inspector 指定的兜底表）。
    ///
    /// <para><b>语言实时性</b>：显示语言不内存在本类，而是经 <paramref name="getLanguage"/> 实时读取
    /// <see cref="StoryFlow.ActiveLanguage"/>。这样无论何时设置语言、何时切换章节，文本都始终使用
    /// 当前的全局语言，绝不回落默认语言（旧实现把语言“拷贝”进 _language 字段，切换/改值时易过期）。</para>
    ///
    /// <para><b>跳转章节跟随</b>：剧情经 JumpChapter 切换到目标图后，由 <see cref="StoryFlow"/> 调用
    /// <see cref="SetCurrentGraph"/> 把当前图切到目标图，使本地化表随剧情图一起切换，新图文本不再误查旧表。
    /// 语言完全由 ActiveLanguage 实时决定，与切图解耦。</para>
    /// </summary>
    public sealed class StoryGraphLocalizationProvider : IStoryTextProvider
    {
        private StoryGraphAsset _currentGraph;
        private readonly StoryLocalizationTable _fallback;
        private readonly System.Func<string> _getLanguage;

        public StoryGraphLocalizationProvider(StoryGraphAsset initialGraph, System.Func<string> getLanguage, StoryLocalizationTable fallback)
        {
            _currentGraph = initialGraph;
            _getLanguage = getLanguage;
            _fallback = fallback;
        }

        /// <summary>切换当前图（跳转章节时由 StoryFlow 调用），后续 ResolveText 从该图的本地化表取译文。</summary>
        public void SetCurrentGraph(StoryGraphAsset graph) => _currentGraph = graph;

        public string ResolveText(string key)
        {
            var table = (_currentGraph != null ? _currentGraph.localizationTable : null) ?? _fallback;
            if (table == null) return null;
            // 语言实时来自 ActiveLanguage；为空则回落当前表的 defaultLanguage（按当前图，切换章节后自然跟随新图）。
            string lang = _getLanguage != null ? _getLanguage() : null;
            if (string.IsNullOrEmpty(lang)) lang = table.defaultLanguage;
            if (string.IsNullOrEmpty(lang)) return null;
            return table.TryGetTranslation(key, lang, out var text) ? text : null;
        }
    }
}
