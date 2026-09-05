using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情图资产（ScriptableObject）。编辑期的唯一真相来源。
    /// 发布时由 StoryJsonExporter 导出精简 JSON 供运行时加载。
    ///
    /// 多态序列化关键：nodes 使用 [SerializeReference]，使 List&lt;StoryNodeData&gt; 能正确保留子类字段。
    /// edges / variables / usedCharacterIds 为普通（非多态）可序列化列表。
    /// </summary>
    [CreateAssetMenu(menuName = "MicrobialNet/Story/剧情图", fileName = "StoryGraph")]
    public sealed class StoryGraphAsset : ScriptableObject
    {
        /// <summary>元信息（ID / 章节 / 标签 / 描述）。</summary>
        // 注：下列字段为剧情底层数据模型，对外（宿主 Assembly-CSharp）不可见，仅经 [SerializeField]
        // 保留 Unity 序列化；Editor 经 [InternalsVisibleTo] 可读写，运行时经 RuntimeStoryGraph.FromAsset 只读引用。
        [SerializeField] internal StoryMeta meta = new StoryMeta();

        /// <summary>剧情节点（多态，[SerializeReference] 序列化子类）。</summary>
        [SerializeField] [SerializeReference] internal List<StoryNodeData> nodes = new List<StoryNodeData>();

        /// <summary>连线集合（端口到端口）。</summary>
        [SerializeField] internal List<StoryEdge> edges = new List<StoryEdge>();

        /// <summary>本图变量黑板（局部与全局作用域定义）。</summary>
        [SerializeField] internal List<StoryVariableDef> variables = new List<StoryVariableDef>();

        /// <summary>本图被引用的角色 ID 集合（实际角色资产为独立 SO，按需引用）。</summary>
        [SerializeField] internal List<string> usedCharacterIds = new List<string>();

        /// <summary>本图本地化主表（可选）。运行时经 StoryGraphLocalizationProvider 从「当前图」取表，
        /// 跳转章节后自动切换到目标图的表；为空则回落 StoryFlow 指定的兜底表 / 原文。
        /// 由编辑器「从图同步 Key」自动创建并回写。</summary>
        [SerializeField] internal StoryLocalizationTable localizationTable;

#if UNITY_EDITOR
        /// <summary>分组框集合（章节区块，仅编辑器视觉组织，运行时忽略）——仅编辑器构建存在该字段，
        /// 玩家构建中不参与编译 → Unity 序列化打包时不写入包体（P9：编辑态不进游戏包）。</summary>
        [SerializeField] internal List<StoryGroup> groups = new List<StoryGroup>();

        /// <summary>便签集合（写给同事的说明，不参与剧情执行）——同上，仅编辑器构建存在。</summary>
        [SerializeField] internal List<StoryStickyNote> stickyNotes = new List<StoryStickyNote>();
#endif

        /// <summary>内联剧情表行（仅 JSON 发布路径使用）。构建期无 .asset，剧情表节点引用的表资产无法解析，
        /// 故由 StoryJsonExporter 把所引用表的内容内联进 JSON；运行时经 RuntimeStoryGraph.FromAsset 合并进 tableRows。
        /// 编辑期（表资产可解析）此列表为空，不重复存储。</summary>
        [SerializeField] internal List<StoryTableRow> inlinedTableRows = new List<StoryTableRow>();

        /// <summary>按 ID 查找节点（线性查找，节点量级下足够；编辑期索引由 StoryGraphModel 维护）。</summary>
        internal StoryNodeData GetNode(string id)
            => nodes.FirstOrDefault(n => n.id == id);

        /// <summary>取入口节点（IsEntry 为 true 的节点，约定至多一个）。</summary>
        internal StoryNodeData GetEntryNode()
            => nodes.FirstOrDefault(n => n.IsEntry);
    }
}
