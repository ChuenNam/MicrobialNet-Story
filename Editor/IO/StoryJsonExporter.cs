using System.Collections.Generic;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 剧情图 JSON 导入/导出。
    /// 采用 Newtonsoft.Json（项目已引入 com.unity.nuget.newtonsoft-json），
    /// 以 TypeNameHandling.Auto 保留 StoryNodeData 子树的多态类型（[SerializeReference] 在 JsonUtility 中无法跨文件/字典友好处理）。
    /// 仅序列化数据子集（meta/nodes/edges/variables/usedCharacterIds），避免 ScriptableObject 引擎字段污染。
    /// </summary>
    internal static class StoryJsonExporter
    {
        private const string FormatTag = "MicrobialNet.StoryGraph";
        private const string Version = "1.0";

        // 节点列表走多态：$type 记录具体子类，反序列化时还原正确类型。
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            // 关键：UnityEngine.Object 引用（含节点 appearanceStyle 样式资产）改以资产 GUID 序列化，
            // 避免 Newtonsoft 下钻 GameObject 时触碰已废弃的 rigidbody 等属性 getter（Unity 2022.3 抛 NotSupportedException）。
            Converters = { new UnityObjectRefConverter() },
        };

        [System.Serializable]
        private sealed class StoryGraphDto
        {
            public string format = FormatTag;
            public string version = Version;
            public StoryMeta meta;
            public List<StoryNodeData> nodes;
            public List<StoryEdge> edges;
            public List<StoryVariableDef> variables;
            public List<string> usedCharacterIds;
            public List<StoryGroup> groups;
            public List<StoryStickyNote> stickyNotes;
            public List<StoryTableRow> tableRows;
        }

        /// <summary>将资产序列化为可读 JSON 字符串。JSON 是**备份/交换通道**（编辑器内部往返、
        /// 自动保存、基线回滚、手工备份），导出**完整数据含编辑器布局态**（position/groups/stickyNotes）——
        /// 玩家包体的编辑态剥离不在这里做，而是由数据模型的 `#if UNITY_EDITOR` 条件字段实现
        /// （玩家构建中字段不存在 → Unity 序列化不写入包体；见 StoryNodeData/StoryGraphAsset）。</summary>
        public static string Export(StoryGraphAsset asset)
        {
            if (asset == null) return string.Empty;
            // 内联剧情表行：构建期无 .asset，剧情表节点引用的表资产无法解析，故把内容写进 JSON 随包发布
            var tableRows = new List<StoryTableRow>();
            var seenRow = new HashSet<string>();
            if (asset.nodes != null)
                foreach (var n in asset.nodes)
                    if (n is StoryTableNodeData tn && tn.tableAsset != null && tn.tableAsset.rows != null)
                        foreach (var row in tn.tableAsset.rows)
                            if (row != null && !string.IsNullOrEmpty(row.id) && seenRow.Add(row.id))
                                tableRows.Add(row);

            var dto = new StoryGraphDto
            {
                meta = asset.meta,
                nodes = asset.nodes,
                edges = asset.edges,
                variables = asset.variables,
                usedCharacterIds = asset.usedCharacterIds,
                groups = asset.groups,
                stickyNotes = asset.stickyNotes,
                tableRows = tableRows,
            };
            return JsonConvert.SerializeObject(dto, Settings);
        }

        /// <summary>
        /// 反序列化并写回资产（带 Undo）。反序列化前先经 <see cref="StoryJsonMigrator.Migrate"/> 归一：
        /// 按注册的版本迁移链升级旧格式、按类型别名表改写 $type（节点类改名/换命名空间后旧文件仍可导入）。
        /// 调用方应在写回后重建视图（如 window.Load(asset)）。
        /// </summary>
        public static void Import(StoryGraphAsset asset, string json)
        {
            if (asset == null) return;
            json = StoryJsonMigrator.Migrate(json, Version);
            var dto = JsonConvert.DeserializeObject<StoryGraphDto>(json, Settings);
            if (dto == null)
                throw new System.InvalidOperationException("JSON 解析失败：根对象为空。");

            Undo.RecordObject(asset, "导入剧情图 JSON");
            asset.meta = dto.meta ?? new StoryMeta();
            asset.nodes = dto.nodes ?? new List<StoryNodeData>();
            asset.edges = dto.edges ?? new List<StoryEdge>();
            asset.variables = dto.variables ?? new List<StoryVariableDef>();
            asset.usedCharacterIds = dto.usedCharacterIds ?? new List<string>();
            asset.groups = dto.groups ?? new List<StoryGroup>();
            asset.stickyNotes = dto.stickyNotes ?? new List<StoryStickyNote>();
            asset.inlinedTableRows = dto.tableRows ?? new List<StoryTableRow>();
            EditorUtility.SetDirty(asset);
        }
    }
}
