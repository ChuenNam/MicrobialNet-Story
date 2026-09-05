using UnityEngine;
using UnityEditor;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// StoryView 自定义 Inspector：
    /// 1) 顶部「出现逻辑来源」下拉，二选一：
    ///    - 用户配置数据：显示三个 Position 字段（每个按 mode 仅展示所需参数，见 DialogueBoxPositionDrawer）
    ///    - 生成策略：仅显示 defaultSpawnStrategy 资产字段
    ///    未选中的那一类序列化数据保留（不清除），仅隐藏且不参与运行时应用。
    /// 2) 下方始终显示：三个可选的样式资产覆盖字段，以及一个可选的打字机标点节奏配置。
    /// </summary>
    [CustomEditor(typeof(StoryView))]
    public sealed class StoryViewEditor : Editor
    {
        private SerializedProperty _spawnMode;
        private SerializedProperty _linePosition;
        private SerializedProperty _choicePosition;
        private SerializedProperty _endPosition;
        private SerializedProperty _defaultSpawnStrategy;

        private SerializedProperty _lineStyleAsset;
        private SerializedProperty _choiceStyleAsset;
        private SerializedProperty _endStyleAsset;
        private SerializedProperty _typingProfile;

        private static readonly string[] ModeLabels =
        {
            "用户配置（Position 字段）",
            "生成策略（Strategy 资产）"
        };

        private void OnEnable()
        {
            _spawnMode = serializedObject.FindProperty("spawnMode");
            _linePosition = serializedObject.FindProperty("linePosition");
            _choicePosition = serializedObject.FindProperty("choicePosition");
            _endPosition = serializedObject.FindProperty("endPosition");
            _defaultSpawnStrategy = serializedObject.FindProperty("defaultSpawnStrategy");

            _lineStyleAsset = serializedObject.FindProperty("lineStyleAsset");
            _choiceStyleAsset = serializedObject.FindProperty("choiceStyleAsset");
            _endStyleAsset = serializedObject.FindProperty("endStyleAsset");
            _typingProfile = serializedObject.FindProperty("typingProfile");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            int idx = EditorGUILayout.Popup("出现逻辑来源", _spawnMode.enumValueIndex, ModeLabels);
            if (idx != _spawnMode.enumValueIndex) _spawnMode.enumValueIndex = idx;

            EditorGUILayout.Space();

            if (_spawnMode.enumValueIndex == (int)StoryViewSpawnMode.Strategy)
            {
                EditorGUILayout.HelpBox("生成策略模式：四类对话框统一使用下方策略资产决定出现位置 / 时机（优先级最高）。", MessageType.Info);
                EditorGUILayout.PropertyField(_defaultSpawnStrategy, new GUIContent("Default Spawn Strategy", "生成策略资产（仅此模式生效）"));
            }
            else
            {
                EditorGUILayout.HelpBox("用户配置模式：使用下方四个 Position 字段；每个字段按所选 Mode 仅显示所需参数。", MessageType.Info);
                EditorGUILayout.PropertyField(_linePosition, new GUIContent("Line Position（对白框）"));
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(_choicePosition, new GUIContent("Choice Position（选项框）"));
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(_endPosition, new GUIContent("End Position（结束/提示框）"));
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(_lineStyleAsset, new GUIContent("Line Style（对白）", "覆盖内置 story-line 样式；留空使用默认。"));
            EditorGUILayout.PropertyField(_choiceStyleAsset, new GUIContent("Choice Style（选项）", "覆盖内置 story-choice 样式；留空使用默认。"));
            EditorGUILayout.PropertyField(_endStyleAsset, new GUIContent("End Style（结束）", "覆盖内置 story-end 样式；留空使用默认。"));

            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(_typingProfile, new GUIContent("Typing Profile", "标点节奏配置；仅在对话节点使用「标点节奏」模式时生效。"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
