using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 剧情表烘焙相关工具。
    /// 早期实现曾把表资产「烘焙」成图里的一坨节点（StoryTableBaker.Bake），现已废弃：
    /// 当前架构改为「单剧情表节点 + 虚拟子图」——表节点经 <see cref="StoryTableSubGraph"/> 按数据派生虚拟内部子图，
    /// 不冗余存储内容、不做写资产级烘焙。本类仅保留把「节点选项下标」（= 行内原始下标，含无连接编号的选项）映射到「行内对应选项」的辅助方法。
    /// </summary>
    public static class StoryTableBaker
    {
        /// <summary>
        /// 把「节点选项下标」映射到「剧情表行内对应 StoryTableChoice」。
        /// 自「单剧情表节点」架构起，选项节点已按行内原始下标（含无连接编号的选项）编号，二者下标一致，
        /// 故此处直接按 optionIndexInNode 取 <see cref="StoryTableRow.choices"/> 对应项。
        /// </summary>
        internal static StoryTableChoice GetChoiceForOption(StoryTableRow row, StoryTableAsset table, int optionIndexInNode)
        {
            if (row == null || optionIndexInNode < 0) return null;
            var choices = row.choices;
            if (choices == null || optionIndexInNode >= choices.Count) return null;
            return choices[optionIndexInNode];
        }
    }
}
