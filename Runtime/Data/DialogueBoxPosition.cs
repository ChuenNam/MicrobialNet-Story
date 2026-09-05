using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>定位模式：决定对话框出现在屏幕的什么位置，实现「不受位置限制」。</summary>
    public enum DialogueBoxPositionMode
    {
        /// <summary>屏幕锚点：相对画布按 TextAnchor 对齐（如底部居中）。</summary>
        ScreenAnchor,
        /// <summary>跟随 3D/2D 物体：经相机投影到屏幕（如 NPC 头顶气泡）。</summary>
        WorldFollow,
        /// <summary>自由矩形：由样式/视图自行布局，管理器不干预（适合 HUD 式固定面板）。</summary>
        Free
    }

    /// <summary>对话框定位描述（纯数据）。
    /// 定义在 Runtime 层（仅依赖 UnityEngine 类型），使节点数据与运行时外观提示可直接持有，
    /// 无需反向依赖 UI 模块——这是「节点级外观覆盖」能下沉到剧情编辑器的基础。</summary>
    [System.Serializable]
    public sealed class DialogueBoxPosition
    {
        public DialogueBoxPositionMode mode = DialogueBoxPositionMode.ScreenAnchor;
        public TextAnchor anchor = TextAnchor.LowerCenter;
        public Vector2 offset = Vector2.zero;
        public Transform followTarget;
        public Camera camera;

        public static DialogueBoxPosition BottomCenter() =>
            new DialogueBoxPosition { mode = DialogueBoxPositionMode.ScreenAnchor, anchor = TextAnchor.LowerCenter };

        public static DialogueBoxPosition TopCenter() =>
            new DialogueBoxPosition { mode = DialogueBoxPositionMode.ScreenAnchor, anchor = TextAnchor.UpperCenter };

        public static DialogueBoxPosition TopLeft() =>
            new DialogueBoxPosition { mode = DialogueBoxPositionMode.ScreenAnchor, anchor = TextAnchor.UpperLeft };

        public static DialogueBoxPosition TopRight() =>
            new DialogueBoxPosition { mode = DialogueBoxPositionMode.ScreenAnchor, anchor = TextAnchor.UpperRight };

        public static DialogueBoxPosition Follow(Transform target, Camera cam = null) =>
            new DialogueBoxPosition { mode = DialogueBoxPositionMode.WorldFollow, followTarget = target, camera = cam };

        public static DialogueBoxPosition Free() =>
            new DialogueBoxPosition { mode = DialogueBoxPositionMode.Free };
    }
}
