using System;
using System.Collections.Generic;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using MicrobialNet.Story.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Inspector
{
    /// <summary>
    /// 表格驱动节点的面板编辑器（P4/L2 自 FieldDrawerRegistry 拆出）：徽标头部 / 行维护按钮 / 源文件区 /
    /// 内容字段写回壳。行内容变更的纯逻辑在 <see cref="FieldPanelLogic.ApplyTableRowEdit"/>；
    /// 本类只负责编辑器副作用（Undo / SetDirty / 徽标 / onTableCommit 重烘焙回调）与 UI 组装。
    /// 契约：表（StoryTableAsset）是唯一内容真相源；「同步到 Excel」由子画布窗口手动触发，此处不自动写盘。
    /// </summary>
    internal static class TableBoundEditor
    {
        /// <summary>按表资产 GUID 解析 StoryTableAsset（真相源）。</summary>
        internal static StoryTableAsset ResolveTable(string guid) => StoryTableResolver.ResolveTable(guid);

        /// <summary>
        /// 表格驱动节点的字段写入：非内容字段仅写节点；内容字段回写 SO 对应行（唯一真相源，行变更逻辑在
        /// <see cref="FieldPanelLogic.ApplyTableRowEdit"/> 纯函数）。回写路径与烘焙一致：对白文本/讲述者直接落 row；
        /// 选项文本经 <see cref="StoryTableBaker.GetChoiceForOption"/> 把「节点选项下标」（= 行内原始下标，
        /// 含无连接编号的选项）映射到「行内对应选项」。
        /// </summary>
        internal static void ApplyEdit(StoryGraphModel model, StoryNodeData node, Action<string, object> normalApply, string path, object val, VisualElement unsyncBadge)
        {
            if (!FieldPanelLogic.IsTableContentField(path))
            {
                // 非内容字段（外观 / 语速 / 打字机 / 条件等）仍走节点自身
                normalApply(path, val);
                return;
            }
            // 表驱动节点：内容字段只写回 StoryTableAsset 行（唯一真相源），**不**写节点——
            // 节点在烘焙后不冗余存内容（见 StoryTableBaker / 方案A），写节点是无意义且会失真的冗余数据。
            var table = ResolveTable(node.tableBinding.tableAssetGuid);
            if (table == null) return;
            var row = table.GetRow(node.tableBinding.rowId);
            if (row == null) return;

            Undo.RecordObject(table, "编辑剧情表行");
            FieldPanelLogic.ApplyTableRowEdit(row, table, path, val);

            // 标记「有改动尚未同步到 Excel」并即时显示徽标（不重建面板，避免逐字符输入丢焦点）
            table.unsyncedToExcel = true;
            EditorUtility.SetDirty(table);
            if (unsyncBadge != null && StoryTableAssetExporter.HasSource(table))
                unsyncBadge.style.display = DisplayStyle.Flex;
            // 注意：不在此自动写回 Excel。由子画布窗口工具栏「同步到 Excel」按钮手动触发（见 StoryTableSubGraphWindow.SyncToExcel）。
        }

        /// <summary>
        /// 表格驱动节点的属性面板头部：徽标提示 + 行维护按钮。
        /// 内容字段（对白/讲述者/选项）的任意编辑经 <see cref="ApplyEdit"/> 只写回 SO（真相源），**不**自动写回 Excel；
        /// 「在 Excel 中打开源表 / 同步到 Excel」按钮已移至子画布窗口工具栏（<see cref="Window.StoryTableSubGraphWindow"/>）；
        /// 「在此行后新增一行 / 删除此行」改 SO 后经 onTableCommit 重烘焙本组并刷新面板。
        /// 返回「未同步」徽标元素（编辑后由 ApplyEdit 即时点亮）。
        /// </summary>
        internal static VisualElement AddHeader(VisualElement root, StoryNodeData node, Action onTableCommit)
        {
            var badge = new VisualElement { name = "table-bound-badge" };
            badge.AddToClassList("fd-table-bound");
            var title = new Label("本节点由剧情表驱动") { name = "table-bound-title" };
            title.AddToClassList("fd-table-bound-title");
            badge.Add(title);
            var hint = new Label("修改对白/讲述者/选项会写回 SO（真相源）\n点子画布窗口「同步到 Excel」按钮才写回 Excel 源表\n增删行会重烘焙本组") { name = "table-bound-hint" };
            hint.AddToClassList("fd-table-bound-hint");
            badge.Add(hint);

            // 未同步指示：SO 有改动但尚未写回 Excel 时显示（仅配置了源文件的表才相关）
            var table = ResolveTable(node.tableBinding.tableAssetGuid);
            var unsync = new Label("● 有修改尚未同步到表格") { name = "table-unsynced" };
            unsync.AddToClassList("fd-unsynced");
            bool showUnsync = table != null && table.unsyncedToExcel && StoryTableAssetExporter.HasSource(table);
            unsync.style.display = showUnsync ? DisplayStyle.Flex : DisplayStyle.None;
            badge.Add(unsync);

            root.Add(badge);

            var btnRow = new VisualElement { name = "table-bound-buttons" };
            btnRow.AddToClassList("fd-table-bound-btns");

            // 「在 Excel 中打开源表」/「同步到 Excel」按钮已移至子画布窗口工具栏（StoryTableSubGraphWindow），此处不再重复。
            var addBtn = new Button(() =>
            {
                var t = ResolveTable(node.tableBinding.tableAssetGuid);
                if (t == null) return;
                int idx = t.IndexOf(node.tableBinding.rowId);
                if (idx < 0) idx = t.rows.Count - 1;
                Undo.RecordObject(t, "剧情表新增一行");
                t.rows.Insert(idx + 1, new StoryTableRow
                {
                    id = t.NewId(),
                    speaker = "",
                    text = "新对白",
                    choices = new List<StoryTableChoice>(),
                });
                t.unsyncedToExcel = true;
                EditorUtility.SetDirty(t);
                onTableCommit?.Invoke();
            }) { text = "在此行后新增一行", name = "table-add-btn" };
            addBtn.AddToClassList("fd-create-btn");
            btnRow.Add(addBtn);

            var delBtn = new Button(() =>
            {
                var t = ResolveTable(node.tableBinding.tableAssetGuid);
                if (t == null) return;
                int idx = t.IndexOf(node.tableBinding.rowId);
                if (idx < 0) return;
                if (t.rows.Count <= 1)
                {
                    EditorUtility.DisplayDialog("无法删除", "剧情表至少需保留一行。", "确定");
                    return;
                }
                Undo.RecordObject(t, "剧情表删除一行");
                t.rows.RemoveAt(idx);
                t.unsyncedToExcel = true;
                EditorUtility.SetDirty(t);
                onTableCommit?.Invoke();
            }) { text = "删除此行", name = "table-del-btn" };
            delBtn.AddToClassList("fd-del-btn");
            btnRow.Add(delBtn);

            root.Add(btnRow);
            return unsync;
        }

        /// <summary>剧情表节点面板底部：「源文件」区——只读显示 SO 中的 Source File Path（实时读 tableAsset，不冗余序列化到节点，
        /// 避免与 SO 不同步），并提供「打开源文件表格」按钮（有源文件用系统默认程序打开，无则定位并选中 SO）。</summary>
        internal static void AddSourceSection(VisualElement root, StoryTableNodeData tn)
        {
            var sec = new VisualElement { name = "table-source-section" };

            var raw = tn.tableAsset != null ? tn.tableAsset.sourceFilePath : "";
            var path = new Label($"Source File Path：\n{(string.IsNullOrEmpty(raw) ? "<未配置>" : raw)}") { name = "table-source-path" };
            path.AddToClassList("fd-group-label"); // 弱化样式：只读展示
            sec.Add(path);

            var openBtn = new Button(() =>
            {
                if (tn.tableAsset == null) return;
                string abs = StoryAssetPaths.ResolveSourcePath(tn.tableAsset.sourceFilePath);
                if (!string.IsNullOrEmpty(abs))
                    EditorUtility.OpenWithDefaultApp(abs);
                else
                {
                    Selection.activeObject = tn.tableAsset;
                    EditorGUIUtility.PingObject(tn.tableAsset);
                }
            }) { text = "打开源文件表格", name = "table-open-src-btn" };
            sec.Add(openBtn);

            root.Add(sec);
        }
    }
}
