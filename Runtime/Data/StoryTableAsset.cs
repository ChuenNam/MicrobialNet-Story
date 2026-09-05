using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情表资产（ScriptableObject）——表格驱动剧情流程的**唯一真相源**。
    /// 内容（讲述者/对白/选项/目标）只存在于 <see cref="rows"/>；烘焙产物 <see cref="StoryGraphAsset"/>
    /// 只是它的派生投影，表一改即可重烘焙。CSV/Excel 仅作导入/导出通道，不参与实时存储。
    /// 与本地化主表 <see cref="StoryLocalizationTable"/> 同构（SO 真相源 + 文件导入导出）。
    /// </summary>
    [CreateAssetMenu(menuName = "MicrobialNet/Story/剧情表", fileName = "StoryTable")]
    public sealed class StoryTableAsset : ScriptableObject
    {
        /// <summary>全部对白行（含各自选项）。顺序即表内书写顺序。</summary>
        public List<StoryTableRow> rows = new List<StoryTableRow>();

        /// <summary>导入来源文件路径（csv/xlsx）。「重新导入」时默认读此路径覆盖 <see cref="rows"/>；可为空（手动创建的 SO 无源文件，只能从 SO 自身恢复节点）。</summary>
        public string sourceFilePath = "";

        /// <summary>是否存在已写回 SO、但尚未「同步到 Excel」的修改（仅编辑器用：属性面板据此显示「未同步到表格」）。重导入/成功同步后清零。</summary>
        [HideInInspector] public bool unsyncedToExcel;

        /// <summary>按稳定行键取行（线性查找，表规模下足够）。</summary>
        public StoryTableRow GetRow(string id)
            => rows.FirstOrDefault(r => r != null && r.id == id);

        /// <summary>取行在列表中的下标（-1 表示不存在）。</summary>
        public int IndexOf(string id)
            => rows.FindIndex(r => r != null && r.id == id);

        /// <summary>生成与现有行不冲突的稳定唯一 id（"r" + GUID 短码）。</summary>
        public string NewId()
        {
            string id;
            do { id = "r" + System.Guid.NewGuid().ToString("N").Substring(0, 8); }
            while (rows.Any(r => r != null && r.id == id));
            return id;
        }
    }
}
