using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEngine;

namespace MicrobialNet.Story.Nodes
{
    /// <summary>
    /// 剧情表节点：以单个节点承载一张剧情表（真相源 SO），主图不再散落由表烘焙的大量节点。
    /// 其输入/输出端口由表内部子图的「头/尾空缺」动态派生（<c>entry_{rowId}</c> / <c>exit_{rowId}</c>），
    /// 双击在子画布中展开表的内部流程（纯渲染，不单独存储）。
    /// 运行时由 <see cref="RuntimeStoryGraph.FromAsset"/> 展开为虚拟内部子图 + 边界边，播放器零改动。
    /// </summary>
    [System.Serializable]
    [StoryNode("剧情表", ColorHex = "#8E44AD", Category = "表格驱动", Order = 5)]
    internal sealed class StoryTableNodeData : StoryNodeData
    {
        /// <summary>剧情表资产（真相源）。直接引用以便运行时零 GUID 解析；编辑器侧据此取行与写回。</summary>
        [StoryField("剧情表", Order = 0)]
        public StoryTableAsset tableAsset;

        // —— 表内对白全局默认（开关式覆盖）：勾选后展开内部子图时把下列参数注入每个虚拟
        //    对白/选项节点（表内统一语速与样式，无需逐句配置）；不勾 = 完全保持现状（默认打字/全局样式）。
        [StorySection("节点效果")]
        
        [StoryField("语速与打字机", Order = 1)]
        [Tooltip("勾选后，本表展开的全部对白统一用下方「语速与打字机」渲染；不勾=用默认打字（现状）。")]
        public bool overrideTyping;

        [StoryField("语速", Order = 2)]
        [RangeSlider(0.1f, 1f)]
        [Tooltip("仅「语速与打字机」勾选时生效：表内全部对白的打字速度。")]
        public float typingSpeed = 0.5f;
       
        [StoryField("打字机", Order = 3)]
        [Tooltip("仅「语速与打字机」勾选时生效。全局语速=均匀间隔；标点节奏=按标点停顿。")]
        public TypingMode typingMode = TypingMode.GlobalSpeed;
        
        
        [StoryField("样式与外观", Order = 4)]
        [Tooltip("勾选后，本表展开的全部对白/选项统一用下方外观参数（样式/位置/策略/保留）呈现；不勾=用 StoryView 全局样式与定位（现状）。")]
        public bool overrideAppearance;
        
        [StoryField("样式", Order = 5)]
        [Tooltip("仅「样式与外观」勾选时生效。拖入 DialogueBoxStyleAsset 即可让表内全部对话框用其模板与入场/退场/留存时长。")]
        public DialogueBoxStyleAsset appearanceStyle;

        [StoryField("覆盖位置", Order = 6)]
        [Tooltip("仅「样式与外观」勾选时生效。勾选后下方「定位模式/锚点/偏移」生效；否则沿用 StoryView 全局 position。")]
        public bool appearanceOverridePosition;

        [StoryField("定位模式", Order = 7)]
        public DialogueBoxPositionMode appearancePositionMode;

        [StoryField("锚点", Order = 8)]
        public TextAnchor appearancePositionAnchor = TextAnchor.LowerCenter;

        [StoryField("偏移", Order = 9)]
        public Vector2Int appearancePositionOffset;

        [StoryField("生成策略", Order = 10)]
        [SpawnStrategyPicker]
        [Tooltip("仅「样式与外观」勾选时生效。选中后表内对话框用对应生成策略决定出现位置/层级。留空=用 StoryView 全局策略。")]
        public string appearanceSpawnStrategyKey;

        [StoryField("保留自身", Order = 11)]
        [Tooltip("仅「样式与外观」勾选时生效，作用于表内对白。Inherit=继承全局；Persistent=点击继续保留该框（一串对话保留显示）；Transient=点击继续即关闭。")]
        public DialogueBoxPersistentSetting appearancePersistent = DialogueBoxPersistentSetting.Inherit;

        public override IEnumerable<NodePort> GetInputPorts()
            => StoryTableSubGraph.GetEntryPorts(tableAsset, id);

        public override IEnumerable<NodePort> GetOutputPorts()
            => StoryTableSubGraph.GetExitPorts(tableAsset, id);

        public override string GetSummary()
        {
            if (tableAsset == null) return "<未绑定剧情表>";
            StoryTableSubGraph.ComputeHeadsTails(tableAsset, id, out var heads, out _);
            var exits = StoryTableSubGraph.GetExitPorts(tableAsset, id).Count;
            return $"剧情表：{tableAsset.name}\n{heads.Count} 入口 / {exits} 出口";
        }

        public override bool IsExecutable => true;
    }
}
