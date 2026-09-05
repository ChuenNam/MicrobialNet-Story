using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>节点级「点击继续是否保留自身」的设置（三态）。</summary>
    public enum DialogueBoxPersistentSetting
    {
        /// <summary>继承全局默认（对白默认点击继续即关闭）。</summary>
        Inherit,
        /// <summary>保留：点击继续只推进剧情，不关闭该框（用于一串对话保留显示）。</summary>
        Persistent,
        /// <summary>瞬关：点击继续即关闭（与全局默认行为一致，显式声明）。</summary>
        Transient,
    }

    /// <summary>
    /// 节点级对话框外观覆盖提示（纯运行期数据，不序列化）。
    /// 由 <see cref="StoryPlayer"/> 从节点数据读取后透传给 <see cref="StoryView"/>，
    /// 使其按「节点覆盖 &gt; 全局」的优先级决定样式 / 定位 / 生成策略 / 保留行为。
    /// </summary>
    public sealed class DialogueAppearanceHint
    {
        /// <summary>样式键覆盖；空 = 用全局默认（对白 story-line / 选项 story-choice）。由 BuildAppearance 从样式资产解析。</summary>
        public string styleKeyOverride;

        /// <summary>样式资产引用（节点直接引用 DialogueBoxStyleAsset）。非空时 ShowLine/ShowChoices 会即时把它注册进管理器，
        /// 使其按 styleKey 接入样式表——节点级样式因此"拖个资产即可生效"，无需预先全局注册。</summary>
        public DialogueBoxStyleAsset styleAsset;

        /// <summary>是否覆盖定位（false 时用 StoryView 的全局 position 字段）。</summary>
        public bool overridePosition;

        /// <summary>覆盖定位时的具体定位（overridePosition 为真时有效）。</summary>
        public DialogueBoxPosition position;

        /// <summary>生成策略键覆盖；空 = 不指定（用全局 Strategy 或静态定位）。运行时经 DialogueBoxManager 解析。</summary>
        public string spawnStrategyKey;

        /// <summary>保留行为覆盖；null = 不覆盖（继承全局默认）。</summary>
        public bool? persistentOverride;
    }
}
