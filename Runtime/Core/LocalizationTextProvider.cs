namespace MicrobialNet.Story
{
    /// <summary>
    /// 真实本地化文本提供者（实现 <see cref="IStoryTextProvider"/>）。
    /// 构造时传入主表 <see cref="StoryLocalizationTable"/> 与语言获取委托；
    /// <see cref="ResolveText"/> 按节点 ID 派生的 key（对话 "{nodeId}.text" / 选项 "{choiceId}.opt.{optionId}" / 讲述者 "character.{id}.name"）查表，
    /// 命中该语言译文则返回、未命中返回 null（播放器回退原文，避免显示裸 key）。
    ///
    /// <para>语言实时性：显示语言经 <paramref name="getLanguage"/> 实时读取（如 StoryFlow.ActiveLanguage），
    /// 因此运行时改语言立即对所有后续文本生效，切换章节也不重置；为空回落主表 defaultLanguage。</para>
    /// </summary>
    public sealed class LocalizationTextProvider : IStoryTextProvider
    {
        private readonly StoryLocalizationTable _table;
        private readonly System.Func<string> _getLanguage;

        public LocalizationTextProvider(StoryLocalizationTable table, System.Func<string> getLanguage)
        {
            _table = table;
            _getLanguage = getLanguage;
        }

        public string ResolveText(string key)
        {
            if (_table == null) return null;
            string lang = _getLanguage != null ? _getLanguage() : null;
            if (string.IsNullOrEmpty(lang)) lang = _table.defaultLanguage;
            if (string.IsNullOrEmpty(lang)) return null;
            return _table.TryGetTranslation(key, lang, out var text) ? text : null;
        }
    }
}
