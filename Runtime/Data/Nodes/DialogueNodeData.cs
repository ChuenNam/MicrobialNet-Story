using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>
    /// 对话节点：讲述者说一段台词，单一后继。
    /// 讲述者以 speakerId 引用角色资产；portraitKey（节点级立绘 Key）/ voiceKey（节点级语音 Key）
    /// 经轻量打通已可在属性面板编辑，并暴露给运行时（portraitKey 默认沿用角色立绘；voiceKey 经事件派发交由宿主播放）。
    /// </summary>
    [System.Serializable]
    [StoryNode("对话", ColorHex = "#378ADD", Category = "基础", Order = 0)]
    internal sealed class DialogueNodeData : StoryNodeData
    {
        [StorySection("对话")]
        [StoryField("讲述者", Order = 0)]
        [CharacterPicker]
        public string speakerId = StoryConstants.NarrationId; // 默认显式设为「旁白」，避免新建节点留空被校验误报「未设置讲述者」（空值在显示层也按旁白处理，二者语义需一致）

        [StoryField("正文", Order = 1)]
        [MultilineText(Lines = 5, RichTextToolbar = true)]
        public string text = "";

        [StoryField("语速", Order = 2)]
        [RangeSlider(0.1f, 1f)]
        public float speed = 0.5f;

        // —— 打字机节奏：形式一/二/三 的控制开关（见 TypingMode）——
        [StoryField("打字机", Order = 3)]
        [Tooltip("全局语速=沿用语速均匀间隔（形式一）；标点节奏=按标点预设停顿（形式二）；手K时序=节点 typingDelays 逐字符延迟，由打字机时间轴窗口可视化编辑（形式三）。")]
        public TypingMode typingMode = TypingMode.GlobalSpeed;
        // 形式三（手K时序）逐字符延迟（秒）。按「可见字符」索引，长度应与正文可见字符数一致；
        // 未来时间编辑器直接读写此数组（即其持久化目标）。无 [StoryField]：不参与反射自动面板，避免无编辑器时显示噪声。
        // 长度不符时 TypingScheduler 自动回退基础间隔，不崩溃。
        public float[] typingDelays;
        // —— 轻量 [Future] 打通：解除灰显，字段可编辑并暴露给运行时（视图默认沿用角色默认立绘）——
        [StoryField("立绘", Order = 10)]
        public string portraitKey;
        [StoryField("语音", Order = 11)]
        [LocalizedKey]
        public string voiceKey;

        // —— 节点级对话框外观覆盖：样式 ——
        [StorySection("外观")]
        [StoryField("样式", Order = 20)]
        [Tooltip("拖入 DialogueBoxStyleAsset 资产即可让该对白节点用其样式呈现（含模板 + 入场/退场时长）；留空=用 StoryView 全局样式（对白 story-line）。")]
        public DialogueBoxStyleAsset appearanceStyle;

        // —— 节点级位置/生成策略覆盖：勾选「覆盖位置」用显式定位，否则用生成策略 ——
        [StorySection("生成策略")]
        [StoryField("覆盖位置", Order = 21)]
        [Tooltip("勾选后下方「定位模式/锚点/偏移」生效；否则沿用 StoryView 全局 position。")]
        public bool appearanceOverridePosition;

        [StoryField("定位模式", Order = 22)]
        public DialogueBoxPositionMode appearancePositionMode;

        [StoryField("锚点", Order = 23)]
        public TextAnchor appearancePositionAnchor = TextAnchor.LowerCenter;

        [StoryField("偏移", Order = 24)]
        public Vector2Int appearancePositionOffset;

        [StoryField("生成策略", Order = 25)]
        [SpawnStrategyPicker]
        [Tooltip("未勾选「覆盖位置」时显示。选中后该节点用对应生成策略决定出现位置/层级/保留。留空=用 StoryView 全局策略。")]
        public string appearanceSpawnStrategyKey;

        [StoryField("保留自身", Order = 26)]
        [Tooltip("Inherit=继承全局；Persistent=点击继续保留该框（一串对话保留显示）；Transient=点击继续即关闭。")]
        public DialogueBoxPersistentSetting appearancePersistent = DialogueBoxPersistentSetting.Inherit;

        public override IEnumerable<NodePort> GetInputPorts() => new[] { new NodePort { id = "in" } };
        public override IEnumerable<NodePort> GetOutputPorts() => new[] { new NodePort { id = "out", label = "下一句" } };

        public override string GetSummary()
        {
            var sp = StoryConstants.SpeakerDisplayName(speakerId);
            var preview = string.IsNullOrEmpty(text) ? "<空>" : text.Replace("\n", " ");
            return $"{sp}：{preview}";
        }

        public override string SearchSpeaker => StoryConstants.SpeakerDisplayName(speakerId);
    }
}
