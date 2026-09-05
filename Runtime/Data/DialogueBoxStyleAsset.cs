using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 对话框样式资产（ScriptableObject）。把"样式键 + 模板 + 入场/退场时长"封装为可创建、可拖拽、可复用的资产，
    /// 替代原先手动敲字符串 styleKey 的方式——节点与 StoryView 都能直接引用本资产
    /// （节点编辑器内可内联编辑并一键新建，呼应"节点编辑器即资产组装器"）。
    /// 内部仍以 <see cref="styleKey"/> 注册进 DialogueBoxManager 的样式表（复用现有机制）。
    /// 本类型位于运行时数据层，使节点（Runtime）可直接引用而不破坏分层。
    /// </summary>
    [CreateAssetMenu(fileName = "DialogueBoxStyle", menuName = "MicrobialNet/Story/对话框样式/样式资产")]
    public sealed class DialogueBoxStyleAsset : ScriptableObject
    {
        /// <summary>注册进管理器的样式键；留空则用资产名。ShowLine 等按此键查样式表。</summary>
        public string styleKey;

        /// <summary>预制体模板（克隆源）；留空时管理器尝试 Resources/StoryDialogueBoxes/{styleKey}.prefab，仍无则忽略并告警。</summary>
        public GameObject template;

        /// <summary>入场动画时长（秒）。</summary>
        public float introDuration = 0.18f;

        /// <summary>退场动画时长（秒）。</summary>
        public float outroDuration = 0.18f;

        /// <summary>留存过渡占比（0~1，本资产特有能力）：下一框出现前，等上一框离场进行到该占比，
        /// 避免「新框立即覆盖正在淡出的旧框」导致离场看着像被跳过。0=立即出现；1=等完全离场再出现。</summary>
        [Range(0f, 1f)] public float retainRatio = 0.8f;
    }
}
