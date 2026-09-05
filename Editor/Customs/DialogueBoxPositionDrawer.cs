using UnityEngine;
using UnityEditor;
using MicrobialNet.Story.UI;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 对话框定位（DialogueBoxPosition）的 Inspector 绘制：按当前 mode 只显示该模式所需的参数。
    /// - ScreenAnchor：Mode + Anchor + Offset
    /// - WorldFollow：Mode + Follow Target + Camera
    /// - Free：Mode（由 Prefab 自身布局，无额外参数）
    /// </summary>
    [CustomPropertyDrawer(typeof(DialogueBoxPosition))]
    public sealed class DialogueBoxPositionDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var modeProp = property.FindPropertyRelative("mode");
            var anchorProp = property.FindPropertyRelative("anchor");
            var offsetProp = property.FindPropertyRelative("offset");
            var followProp = property.FindPropertyRelative("followTarget");
            var camProp = property.FindPropertyRelative("camera");

            float single = EditorGUIUtility.singleLineHeight;
            float gap = EditorGUIUtility.standardVerticalSpacing;

            int line = 0;
            Rect r = new Rect(position.x, position.y, position.width, single);
            EditorGUI.LabelField(r, label);                       // 字段名（如 Line Position）
            line++;

            r = new Rect(position.x, position.y + line * (single + gap), position.width, single);
            EditorGUI.PropertyField(r, modeProp, new GUIContent("Mode", "定位模式：ScreenAnchor / WorldFollow / Free"));
            line++;

            var mode = (DialogueBoxPositionMode)modeProp.enumValueIndex;
            if (mode == DialogueBoxPositionMode.ScreenAnchor)
            {
                r = new Rect(position.x, position.y + line * (single + gap), position.width, single);
                EditorGUI.PropertyField(r, anchorProp, new GUIContent("Anchor", "屏幕 9 宫格对齐点"));
                line++;
                r = new Rect(position.x, position.y + line * (single + gap), position.width, single);
                EditorGUI.PropertyField(r, offsetProp, new GUIContent("Offset", "锚点像素偏移（y 向上）"));
                line++;
            }
            else if (mode == DialogueBoxPositionMode.WorldFollow)
            {
                r = new Rect(position.x, position.y + line * (single + gap), position.width, single);
                EditorGUI.PropertyField(r, followProp, new GUIContent("Follow Target", "要跟随的 Transform"));
                line++;
                r = new Rect(position.x, position.y + line * (single + gap), position.width, single);
                EditorGUI.PropertyField(r, camProp, new GUIContent("Camera (可选)", "投影相机；留空用主相机"));
                line++;
            }
            else // Free
            {
                r = new Rect(position.x, position.y + line * (single + gap), position.width, single);
                //EditorGUI.LabelField(r, "Free：由 Prefab 自身布局决定，无额外参数。");
                line++;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var modeProp = property.FindPropertyRelative("mode");
            var mode = (DialogueBoxPositionMode)modeProp.enumValueIndex;
            int lines = 2; // label + Mode
            if (mode == DialogueBoxPositionMode.ScreenAnchor || mode == DialogueBoxPositionMode.WorldFollow)
                lines += 2;
            else
                lines += 0; // Free 说明 - 暂时设0,有内容在改回1 (见line60)
            return lines * EditorGUIUtility.singleLineHeight
                 + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
        }
    }
}
