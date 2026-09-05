using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 表驱动绑定的编辑器侧解析工具：把 <see cref="TableBinding"/>（表资产 GUID + 行 id）
    /// 解析为对应的 <see cref="StoryTableAsset"/> / <see cref="StoryTableRow"/>，供属性面板、画布、校验器、试跑等
    /// 在「表是唯一内容真相源」（方案A）下读取剧情内容。
    ///
    /// Runtime 不依赖此工具——运行时走 <see cref="RuntimeStoryGraph.tableRows"/>（在 FromAsset 时已从表资产收集）。
    /// 本工具仅用于编辑器内需要按绑定反查源行显示/校验的场景。
    /// </summary>
    internal static class StoryTableResolver
    {
        /// <summary>按表资产 GUID 解析 StoryTableAsset（真相源）。</summary>
        internal static StoryTableAsset ResolveTable(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(p)) return null;
            return AssetDatabase.LoadAssetAtPath<StoryTableAsset>(p);
        }

        /// <summary>解析表驱动节点绑定的源行（内容真相源）。非表驱动或找不到返回 null。</summary>
        internal static StoryTableRow ResolveRow(TableBinding binding)
        {
            if (string.IsNullOrEmpty(binding.rowId)) return null;
            return ResolveTable(binding.tableAssetGuid)?.GetRow(binding.rowId);
        }
    }
}
