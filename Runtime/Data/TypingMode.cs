namespace MicrobialNet.Story
{
    /// <summary>
    /// 打字机（逐字显示）节奏模式：
    ///  - <see cref="GlobalSpeed"/>：沿用节点「语速」作全局均匀间隔（现状默认行为，控制大部分对话节奏）；
    ///  - <see cref="Punctuation"/>：在语速基础上，按标点解析预设倍率（局部节奏，避免为个别字改整段）；
    ///  - <see cref="Custom"/>：整段手K逐字符延迟（精确控制）。逐字符延迟写入节点 typingDelays 数组，
    ///    未来由时间编辑器可视化编辑（具体样式未定），本数组即其读写目标，保证后续兼容。
    /// </summary>
    public enum TypingMode
    {
        /// <summary>全局语速：沿用语速作均匀间隔（形式一）。</summary>
        GlobalSpeed = 0,
        /// <summary>标点节奏：语速基础间隔 × 标点倍率（形式二）。</summary>
        Punctuation = 1,
        /// <summary>手K时序：节点 typingDelays 逐字符延迟（形式三），由打字机时间轴窗口可视化编辑。</summary>
        Custom = 2,
    }
}
