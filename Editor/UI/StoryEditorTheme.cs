using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.UI
{
    /// <summary>
    /// 编辑器主题：集中语义颜色令牌，按 <see cref="EditorGUIUtility.isProSkin"/> 自动切换 Light/Dark。
    /// 作为 02 §4.1「主题层」的第一阶段——先把分散在 C# 里的语义颜色收口到此处，
    /// 后续可进一步抽成 .uss（CSS 变量）实现「换配色不改 C#」。
    /// Dark 取值刻意与既有硬编码保持一致，避免视觉回退；Light 提供配套浅色。
    /// </summary>
    public static class StoryEditorTheme
    {
        public static bool IsDark => EditorGUIUtility.isProSkin;

        // ── 校验（节点左边框高亮）──
        public static Color ValidationError => IsDark
            ? new Color(0.85f, 0.20f, 0.20f)
            : new Color(0.80f, 0.08f, 0.08f);

        public static Color ValidationWarning => IsDark
            ? new Color(0.95f, 0.75f, 0.15f)
            : new Color(0.85f, 0.58f, 0.0f);

        // ── 校验问题面板文字 ──
        public static Color ValidationErrorText => IsDark
            ? new Color(0.90f, 0.35f, 0.35f)
            : new Color(0.82f, 0.12f, 0.12f);

        public static Color ValidationWarningText => IsDark
            ? new Color(0.95f, 0.78f, 0.25f)
            : new Color(0.88f, 0.62f, 0.0f);

        public static Color ValidationOk => new Color(0.40f, 0.80f, 0.40f);

        // ── 试跑高亮（节点右边框）/ 路径流动青色 ──
        public static Color PlaybackHighlight => new Color(0.25f, 0.55f, 0.95f);

        public static Color Flow => new Color(0.20f, 0.85f, 1f);

        // ── 面板分隔线 ──
        public static Color Divider => IsDark
            ? new Color(0.20f, 0.20f, 0.20f)
            : new Color(0.70f, 0.70f, 0.70f);
    }
}
