using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 本地化 Excel 工具（表驱动，主表 <see cref="StoryLocalizationTable"/> 为唯一真相源）。
    /// 与 <see cref="StoryLocalizationCsv"/> 语义一致：从主表导出镜像、按 key 合并回主表（增量、不删条目）。
    /// 单张工作表「本地化」，列：Key,Original,然后依次为 table.languages 中各语言。
    /// 依赖 StoryXlsx 的零依赖 OOXML 读写器。
    /// </summary>
    public static class StoryLocalizationXlsx
    {
        public const string SheetName = "本地化";

        private const string ColKey = "Key";
        private const string ColOriginal = "Original";

        // ── 从主表导出（镜像）──

        public static XlsSheet BuildSheet(StoryLocalizationTable table)
        {
            var sheet = new XlsSheet { Name = SheetName };
            var header = new List<string> { ColKey, ColOriginal };
            if (table != null && table.languages != null) header.AddRange(table.languages);
            sheet.Rows.Add(header.ToArray());

            if (table == null || table.entries == null) return sheet;
            int n = table.languages != null ? table.languages.Count : 0;
            foreach (var e in table.entries)
            {
                var row = new List<string> { e.key, e.original };
                for (int i = 0; i < n; i++)
                    row.Add(e.translations != null && i < e.translations.Count ? e.translations[i] : string.Empty);
                sheet.Rows.Add(row.ToArray());
            }
            return sheet;
        }

        // ── 从 Excel 合并回主表（增量）──

        public static ImportReport ImportFromRowsToTable(List<string[]> rows, StoryLocalizationTable table)
        {
            var report = new ImportReport();
            if (table == null || rows == null || rows.Count < 2)
            { report.message = "文件行数不足（需要表头 + 至少 1 行数据）。"; return report; }

            var header = rows[0];
            int idxKey = FindCol(header, ColKey);
            int idxOrig = FindCol(header, ColOriginal);
            report.hasKeyCol = idxKey >= 0;
            report.hasValueCol = idxOrig >= 0;
            if (idxKey < 0) { report.message = $"未找到「Key」列。表头为：{string.Join(",", header)}"; return report; }

            var langCols = new List<(int col, int langIndex)>();
            if (table.languages != null)
                for (int li = 0; li < table.languages.Count; li++)
                {
                    int c = FindCol(header, table.languages[li]);
                    if (c >= 0) langCols.Add((c, li));
                }

            Undo.RecordObject(table, "导入本地化表格→主表");
            int changed = 0, wellFormed = 0;
            for (int r = 1; r < rows.Count; r++)
            {
                var fields = rows[r];
                if (fields.Length <= idxKey) continue;
                string key = fields[idxKey];
                if (string.IsNullOrEmpty(key)) continue;
                wellFormed++;
                if (idxOrig >= 0 && fields.Length > idxOrig && !string.IsNullOrEmpty(fields[idxOrig]))
                { table.SetOriginal(key, fields[idxOrig]); changed++; }
                foreach (var (c, li) in langCols)
                {
                    string val = fields.Length > c ? fields[c] : string.Empty;
                    if (!string.IsNullOrEmpty(val)) { table.SetTranslation(key, li, val); changed++; }
                }
            }
            report.dataRows = rows.Count - 1;
            report.wellFormed = wellFormed;
            report.changed = changed;
            if (changed > 0) EditorUtility.SetDirty(table);
            report.message = changed > 0
                ? $"已合并 {changed} 处（数据行 {report.dataRows}，key 格式正确 {wellFormed}）。"
                : "0 处更新（文件中无非空单元格命中）。";
            Debug.Log($"[Story] 本地化主表导入：{report.message}");
            return report;
        }

        private static int FindCol(string[] header, string name)
        {
            for (int i = 0; i < header.Length; i++)
                if (!string.IsNullOrEmpty(header[i]) && header[i].Trim().Equals(name, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}
