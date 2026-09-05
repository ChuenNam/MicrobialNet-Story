namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情表中的一个选项：文本 + 跳转目标（目标行的稳定 <see cref="StoryTableRow.id"/>）。
    /// </summary>
    [System.Serializable]
    public sealed class StoryTableChoice
    {
        /// <summary>选项文本。</summary>
        public string text;

        /// <summary>跳转目标行 id（<see cref="StoryTableRow.id"/>）；空或「/」表示未指定（该选项是表节点出口端口 optexit_…）。
        /// 「/」为显式终止标识（语义同空：无内部目标，作为输出端）。</summary>
        public string targetRowId;
    }
}
