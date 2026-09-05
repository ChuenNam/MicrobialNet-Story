using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 分组框（章节区块）。仅记录几何与成员节点 ID，真正的位置数据仍在各 StoryNodeData 上；
    /// 移动分组时由画布联动平移成员节点，保证「框」与「节点」永远对齐。
    /// 纯 [Serializable] 具体类，由 StoryGraphAsset.groups 以普通列表原生序列化（无需 [SerializeReference]）。
    /// </summary>
    [System.Serializable]
    internal sealed class StoryGroup
    {
        /// <summary>稳定 ID（生成后用，不参与运行）。</summary>
        public string id;

        /// <summary>分组标题（章节名等）。</summary>
        public string title = "分组";

        /// <summary>画布坐标下的矩形（位置 + 尺寸）。</summary>
        public Rect rect;

        /// <summary>被该分组直接包含的节点 ID 列表（仅最内层分组持有节点；外层分组通过子分组间接包含）。</summary>
        public List<string> nodeIds = new List<string>();

        /// <summary>父分组 ID；空字符串表示顶层分组。用于支持「分组里再建分组」的嵌套结构。</summary>
        public string parentGroupId = "";
    }
}
