using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 角色资产（ScriptableObject）。讲述者的唯一真相来源。
    /// 节点中的 speakerId 保存本资产的 characterId，从而改名/换图只改一处。
    /// 左侧栏「角色」标签列出全工程此类资产，支持双击编辑与新建。
    /// </summary>
    [CreateAssetMenu(menuName = "MicrobialNet/Story/角色", fileName = "Character")]
    public sealed class StoryCharacterAsset : ScriptableObject
    {
        /// <summary>角色稳定 ID（节点 speakerId 引用它，而非显示名）。</summary>
        public string characterId;

        /// <summary>显示名（用于节点展示与运行时 UI）。</summary>
        public string displayName;

        /// <summary>主题色（十六进制），用于节点标题栏与立绘占位底色。</summary>
        public string colorHex = "#378ADD";

        /// <summary>头像（可选）。为空时以 colorHex 色块占位。</summary>
        public Sprite avatar;

        /// <summary>角色简介（不参与执行）。</summary>
        [TextArea] public string description;
    }
}
