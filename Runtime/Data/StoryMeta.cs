using System;
using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>剧情图的元信息：ID / 章节 / 标签 / 描述。用于左侧栏树、检索与导出时携带。</summary>
    [Serializable]
    internal sealed class StoryMeta
    {
        /// <summary>剧情图稳定 ID（区别于文件名，便于跨文件引用与统计）。</summary>
        public string storyId;

        /// <summary>所属章节，用于左侧栏「章节 → 剧情图」两级分组。</summary>
        public string chapter;

        /// <summary>标签，便于检索与批量导出。</summary>
        public List<string> tags = new List<string>();

        /// <summary>剧情简介（不参与执行）。</summary>
        [TextArea] public string description;
    }
}
