using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 便签（写给同事的说明）。纯展示性文本块，不参与剧情执行。
    /// 几何 + 文本由画布持久化；theme 仅 0~3 的整数映射，避免依赖具体枚举名。
    /// </summary>
    [System.Serializable]
    internal sealed class StoryStickyNote
    {
        /// <summary>稳定 ID。</summary>
        public string id;

        /// <summary>便签标题。</summary>
        public string title = "便签";

        /// <summary>便签正文。</summary>
        [Multiline] public string text = "";

        /// <summary>画布坐标下的矩形（位置 + 尺寸）。</summary>
        public Rect rect;

        /// <summary>配色主题（0~3），映射到 StickyNoteTheme 枚举。</summary>
        public int theme;
    }
}
