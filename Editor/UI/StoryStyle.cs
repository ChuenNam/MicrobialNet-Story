using UnityEditor;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.UI
{
    /// <summary>
    /// 集中加载 StoryEditor 的全部 USS 样式表，并按 <see cref="EditorGUIUtility.isProSkin"/>
    /// 给根元素加上 <c>theme-dark</c> / <c>theme-light</c> 根类，驱动 <c>StoryEditorTheme.uss</c> 的
    /// Light/Dark 变量切换。各 EditorWindow / GraphView 在构建 UI 时调用一次 <see cref="Apply"/>。
    /// 满足 02 §4.1「UXML / USS 组织」：样式全在 .uss 文件，C# 只加类、不写死颜色。
    /// </summary>
    public static class StoryStyle
    {
        private const string Marker = "story-styled";

        private static readonly string[] Sheets =
        {
            "Packages/com.microbialnet.story/Editor/UI/Theme/StoryEditorTheme.uss",
            "Packages/com.microbialnet.story/Editor/UI/StoryGraphWindow.uss",
            "Packages/com.microbialnet.story/Editor/UI/Nodes/StoryNodeView.uss",
            "Packages/com.microbialnet.story/Editor/UI/Groups/StoryGroupView.uss",
            "Packages/com.microbialnet.story/Editor/UI/Inspector/InspectorRow.uss",
            "Packages/com.microbialnet.story/Editor/UI/Panels/ValidationRow.uss",
            "Packages/com.microbialnet.story/Editor/UI/Playback/StoryPlaybackWindow.uss",
            "Packages/com.microbialnet.story/Editor/UI/Stats/StoryStatsWindow.uss",
            "Packages/com.microbialnet.story/Editor/UI/Inspector/FieldDrawer.uss",
        };

        /// <summary>把全部 USS 挂到 <paramref name="root"/> 并设主题根类。幂等（重复调用不会重复挂载）。</summary>
        public static void Apply(VisualElement root)
        {
            if (root == null || root.ClassListContains(Marker)) return;
            foreach (var path in Sheets)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (sheet != null) root.styleSheets.Add(sheet);
            }
            root.AddToClassList(Marker);
            root.AddToClassList(EditorGUIUtility.isProSkin ? "theme-dark" : "theme-light");
        }
    }
}
