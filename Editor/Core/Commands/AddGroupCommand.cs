using System;
using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>新建分组框：将一组节点打包进一个分组（仅记录成员关系与几何，不移动节点）。</summary>
    internal sealed class AddGroupCommand : IGraphCommand
    {
        private readonly List<string> _nodeIds;
        private readonly Rect _rect;
        private readonly string _parentGroupId;
        private string _createdId;

        public string Description => "新建分组";
        // 结构变化（增删分组）→ 整体重建画布以渲染分组视图。
        public GraphChange Change => new GraphChange(GraphChangeType.Reset);

        /// <param name="parentGroupId">可选。非空则新分组挂到该父组下；为空则自动识别「完整包含选区且层级最深」的已有分组作为父组。</param>
        public AddGroupCommand(List<string> nodeIds, Rect rect, string parentGroupId = null)
        {
            _nodeIds = nodeIds ?? new List<string>();
            _rect = rect;
            _parentGroupId = parentGroupId;
        }

        public void Execute(StoryGraphModel model)
        {
            Undo.RecordObject(model.Asset, Description);

            // 1) 确定父组：显式指定优先；否则取「完整包含选区（bounds 四角都在其内）且层级最深」的已有分组。
            var parent = string.IsNullOrEmpty(_parentGroupId) ? FindEnclosingGroupId(model.Asset, _rect) : _parentGroupId;

            // 2) 节点只属于最内层分组：从所有已有分组里移除本次选中的节点，避免父子两组重复包含同一节点导致框重叠。
            foreach (var g in model.Asset.groups)
                g.nodeIds.RemoveAll(id => _nodeIds.Contains(id));

            // 3) 新建分组
            var ng = new StoryGroup
            {
                id = "g_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                title = "分组",
                rect = _rect,
                nodeIds = new List<string>(_nodeIds),
                parentGroupId = parent ?? "",
            };
            model.Asset.groups.Add(ng);
            _createdId = ng.id;
        }

        private static string FindEnclosingGroupId(StoryGraphAsset asset, Rect bounds)
        {
            string best = null;
            int bestDepth = -1;
            foreach (var g in asset.groups)
            {
                if (g.rect.Contains(bounds.min) && g.rect.Contains(bounds.max))
                {
                    int d = DepthOf(asset, g);
                    if (d > bestDepth) { bestDepth = d; best = g.id; }
                }
            }
            return best;
        }

        private static int DepthOf(StoryGraphAsset asset, StoryGroup g)
        {
            int d = 0;
            int guard = 0;
            var cur = g;
            while (!string.IsNullOrEmpty(cur.parentGroupId) && guard++ < 64)
            {
                var p = asset.groups.Find(x => x.id == cur.parentGroupId);
                if (p == null) break;
                d++;
                cur = p;
            }
            return d;
        }
    }
}
