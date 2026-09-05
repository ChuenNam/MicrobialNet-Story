using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.Graph
{
    /// <summary>
    /// 节点创建搜索窗（文档 02 §界面布局三大建节点入口之二：端口拖线松手 / Space）。
    /// 复用 NodeRegistry 按 [StoryNode].Category 分组列出所有节点类型，选中后在指定画布坐标创建节点；
    /// 可选携带源端口 / 源节点，创建后自动连线。
    /// </summary>
    internal sealed class StoryNodeSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        public StoryGraphView graphView;
        public Port sourcePort;            // 端口拖拽创建时非空 → 新节点自动连到该端口
        public StoryNodeView fromNode;     // Space 选中节点创建时非空 → 从选中节点主输出自动连入新节点
        public Vector2 contentLocal;       // 画布 contentViewContainer 本地坐标，新建节点落点

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("创建节点"), 0)
            };
            foreach (var grp in NodeRegistry.Entries.GroupBy(e => e.Attr.Category))
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent(grp.Key), 1));
                foreach (var entry in grp)
                    tree.Add(new SearchTreeEntry(new GUIContent(entry.Attr.Title))
                    {
                        level = 2,
                        userData = entry.Type
                    });
            }
            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is System.Type t && graphView != null)
            {
                graphView.SpawnNodeWithConnection(t, contentLocal, sourcePort, fromNode);
                return true;
            }
            return false;
        }
    }
}
