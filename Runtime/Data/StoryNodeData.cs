using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 所有剧情节点数据类的抽象基类。
    /// 节点是「纯数据」，不含任何 UnityEditor / 视图逻辑，可安全进入 ScriptableObject 与 JSON 序列化。
    /// 多态序列化的关键：StoryGraphAsset 中以 [SerializeReference] 持有 List&lt;StoryNodeData&gt;，
    /// 从而正确保留各子类的字段。
    /// </summary>
    [System.Serializable]
    internal abstract class StoryNodeData
    {
        /// <summary>节点稳定唯一 ID，作为连线、反向引用、存档的依据。创建后永不改变（即便节点改名）。</summary>
        public string id;

        /// <summary>作者自定义的标题，覆盖类型名显示在节点顶部。可空。</summary>
        public string title;

#if UNITY_EDITOR
        /// <summary>画布坐标（持久化以保持布局）。编辑器视图状态——仅编辑器构建存在该字段，
        /// 玩家构建中字段不参与编译，Unity 序列化打包时自然不写入包体（P9：编辑态不进游戏包）。
        /// JSON 备份通道（StoryJsonExporter）在编辑器下运行，字段存在 → 备份完整保留布局。</summary>
        public Vector2 position;
#endif

        /// <summary>作者备注（不参与执行）。</summary>
        public string authorNote;

        /// <summary>输入端口定义。Start / Comment 返回空集合。</summary>
        public abstract IEnumerable<NodePort> GetInputPorts();

        /// <summary>输出端口定义。End / Comment 返回空集合；Choice 按选项动态生成。</summary>
        public abstract IEnumerable<NodePort> GetOutputPorts();

        /// <summary>节点正文摘要，显示在节点体内，做到不看属性面板也能读懂整张图。</summary>
        public virtual string GetSummary() => string.Empty;

        /// <summary>用于节点搜索的讲述者显示名（仅对话类节点返回具体讲述者），其余返回 null。B8 搜索「按讲述者」定位用。</summary>
        public virtual string SearchSpeaker => null;

        /// <summary>该节点是否参与流程执行（Comment 为 false，仅作画布批注）。</summary>
        public virtual bool IsExecutable => true;

        /// <summary>该节点是否可作为图的入口（仅 Start 为 true）。</summary>
        public virtual bool IsEntry => false;

        /// <summary>用于属性面板/节点标题的显示名：优先自定义标题，否则取 [StoryNode] 的 Title。</summary>
        public string DisplayTitle()
        {
            if (!string.IsNullOrEmpty(title)) return title;
            var attr = NodeRegistry.GetAttr(GetType());
            return attr != null ? attr.Title : GetType().Name;
        }

        /// <summary>表格驱动绑定：非空 <see cref="TableBinding.rowId"/> 表示该节点由剧情表烘焙生成。
        /// 内容在烘焙/渲染时由表解析，改表并重烘焙即更新，节点本身不长期持有内容副本。</summary>
        public TableBinding tableBinding;

        /// <summary>该节点是否由剧情表驱动（内容随表更新）。</summary>
        public bool IsTableBound => !string.IsNullOrEmpty(tableBinding.rowId);
    }

    /// <summary>
    /// 节点 → 剧情表的绑定（轻量、可序列化、无 Object 引用，避免运行时耦合表资产）。
    /// 仅存「表资产 GUID + 行 id」，渲染时经 GUID 解析资产并取对应行。
    /// </summary>
    [System.Serializable]
    public struct TableBinding
    {
        /// <summary>剧情表资产在 Project 中的 GUID（AssetDatabase.AssetPathToGUID）。</summary>
        public string tableAssetGuid;

        /// <summary>目标行稳定 id（<see cref="StoryTableRow.id"/>）。空字符串表示未绑定。</summary>
        public string rowId;
    }

    /// <summary>节点的端口描述。端口 ID 在同一节点内唯一；Choice 的输出端口 ID 形如 "opt_{optionId}"。</summary>
    [System.Serializable]
    public sealed class NodePort
    {
        public string id;
        public string label;
    }
}
