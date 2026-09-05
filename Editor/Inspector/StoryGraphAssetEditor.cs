using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools.Window;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.Inspector
{
    /// <summary>
    /// 剧情图资产的 Inspector：提供「在剧情编辑器中打开」按钮。
    /// （双击资产默认打开 Inspector，点此按钮即可进入图形编辑器。）
    /// </summary>
    [CustomEditor(typeof(StoryGraphAsset))]
    public sealed class StoryGraphAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (StoryGraphAsset)target;
            EditorGUILayout.HelpBox(
                $"剧情图：{asset.nodes.Count} 节点 / {asset.edges.Count} 连线\n" +
                $"ID：{asset.meta.storyId}　章节：{asset.meta.chapter}", MessageType.Info);

            if (GUILayout.Button("在剧情编辑器中打开", GUILayout.Height(30)))
            {
                StoryGraphWindow.Open(asset);
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
