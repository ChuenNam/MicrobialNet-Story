using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 「新建剧情图」一步式对话框：同时收集名称与分组。
    /// 分组使用可编辑下拉——既可直接输入新分组名（确认即建子文件夹），
    /// 也可从 ▼ 菜单点选 Graphs/ 下现有分组。
    /// </summary>
    public sealed class NewStoryGraphDialog : EditorWindow
    {
        private string _name;
        private string _group;
        private System.Action<string, string> _onOk;
        private List<string> _groups;

        public static void Show(string defaultGroup, System.Action<string, string> onOk)
        {
            var w = ScriptableObject.CreateInstance<NewStoryGraphDialog>();
            w.titleContent = new GUIContent("新建剧情图");
            w._group = defaultGroup ?? "";
            w._onOk = onOk;
            w._name = "";
            w._groups = StoryAssetPaths.GetExistingGroups();
            w.position = new Rect(Screen.width * 0.5f - 190f, Screen.height * 0.5f - 60f, 380, 132);
            w.ShowAuxWindow();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("剧情图名称", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField(_name);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("分组", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _group = EditorGUILayout.TextField(_group);
            if (GUILayout.Button("▼", GUILayout.Width(24), GUILayout.Height(18)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(StoryAssetPaths.Ungrouped),
                    _group == StoryAssetPaths.Ungrouped,
                    () => { _group = StoryAssetPaths.Ungrouped; Repaint(); });
                foreach (var g in _groups)
                {
                    if (g == StoryAssetPaths.Ungrouped) continue;
                    menu.AddItem(new GUIContent(g), _group == g,
                        () => { _group = g; Repaint(); });
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_name));
            if (GUILayout.Button("确定", GUILayout.Height(24)))
            {
                var n = _name.Trim();
                var g = (_group ?? "").Trim();
                _onOk?.Invoke(n, g);
                Close();
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("取消", GUILayout.Height(24)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
