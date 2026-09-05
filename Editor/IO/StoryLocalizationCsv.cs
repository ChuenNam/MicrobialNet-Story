using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 本地化 CSV 工具（表驱动，主表 <see cref="StoryLocalizationTable"/> 为唯一真相源）。
    /// 三件事全部增量、互不破坏：
    /// ① <see cref="SyncFromGraph"/> 从图把缺失 key 追加进主表（保留已有译文）；
    /// ② <see cref="ExportCsv"/> 从主表导出给翻译（含已有译文）；
    /// ③ <see cref="ImportCsvToTable"/> 把 CSV 按 key 合并回主表（只更新出现的 key/语言单元格，不删条目）。
    /// 列：Key,Original,然后依次为 table.languages 中各语言。自带极简 CSV 转义/解析，不依赖第三方库。
    /// </summary>
    public static class StoryLocalizationCsv
    {
        public const string ColKey = "Key";
        public const string ColOriginal = "Original";

        // ── ① 从图同步 Key（增量）──

        /// <summary>扫描图，把缺失的 key 追加进主表（original=源文本），已存在则刷新 original；译文始终保留。返回新增条数。</summary>
        public static int SyncFromGraph(StoryGraphAsset asset, StoryLocalizationTable table)
        {
            if (asset == null || table == null) return 0;

            var keys = new List<(string key, string original)>();
            var speakerIds = new HashSet<string>();
            foreach (var node in asset.nodes)
            {
                if (node is DialogueNodeData dlg)
                {
                    // 表驱动：内容在行（唯一真相源），key 绑稳定的 rowId（重烘焙换 nodeId 不丢译文）；节点本身不冗余存文本
                    string text = dlg.text;
                    string speakerId = dlg.speakerId;
                    if (dlg.IsTableBound)
                    {
                        var row = StoryTableResolver.ResolveRow(dlg.tableBinding);
                        text = row?.text ?? "";
                        speakerId = row?.speaker ?? "";
                    }
                    if (!string.IsNullOrEmpty(text))
                    {
                        string key = dlg.IsTableBound ? $"{dlg.tableBinding.rowId}.text" : $"{dlg.id}.text";
                        keys.Add((key, text));
                        if (!string.IsNullOrEmpty(speakerId)) speakerIds.Add(speakerId);
                    }
                }
                else if (node is ChoiceNodeData choice)
                {
                    bool tableBound = choice.IsTableBound;
                    var row = tableBound ? StoryTableResolver.ResolveRow(choice.tableBinding) : null;
                    var tbl = tableBound ? StoryTableResolver.ResolveTable(choice.tableBinding.tableAssetGuid) : null;
                    for (int i = 0; i < choice.options.Count; i++)
                    {
                        string optText = tableBound
                            ? (StoryTableBaker.GetChoiceForOption(row, tbl, i)?.text ?? "")
                            : (choice.options[i].text ?? "");
                        if (!string.IsNullOrEmpty(optText))
                            keys.Add(($"{choice.id}.opt.{choice.options[i].optionId}", optText));
                    }
                }
                else if (node is StoryTableNodeData tn && tn.tableAsset != null && tn.tableAsset.rows != null)
                {
                    // 剧情表节点：内容在表（唯一真相源），key 绑稳定的 rowId（与运行时 StoryPlayer 派生规则一致）
                    foreach (var row in tn.tableAsset.rows)
                    {
                        if (row == null || string.IsNullOrEmpty(row.id)) continue;
                        if (!string.IsNullOrEmpty(row.text))
                        {
                            keys.Add((row.id + ".text", row.text));
                            if (!string.IsNullOrEmpty(row.speaker)) speakerIds.Add(row.speaker);
                        }
                        // 选项按行内原始下标编号（含无连接编号的选项，与运行时 StoryPlayer 派生规则一致）
                        if (row.choices != null)
                        {
                            for (int vi = 0; vi < row.choices.Count; vi++)
                            {
                                var ch = row.choices[vi];
                                if (ch != null) keys.Add((row.id + ".opt." + vi, ch.text ?? ""));
                            }
                        }
                    }
                }
            }
            foreach (var sid in speakerIds)
                keys.Add(("character." + sid + ".name", CharacterLibrary.ResolveViewModel(sid).displayName));

            Undo.RecordObject(table, "从图同步本地化 Key");
            int added = 0;
            foreach (var (key, original) in keys)
            {
                bool existed = table.ContainsKey(key);
                table.UpsertOriginal(key, original);
                if (!existed) added++;
            }
            if (added > 0 || keys.Count > 0) EditorUtility.SetDirty(table);
            return added;
        }

        // ── ② 从主表导出（镜像）──

        public static string ExportCsv(StoryLocalizationTable table)
        {
            var sb = new StringBuilder();
            sb.Append(ColKey).Append(',').Append(ColOriginal);
            if (table != null && table.languages != null)
                foreach (var lang in table.languages) sb.Append(',').Append(lang);
            sb.Append('\n');

            if (table != null && table.entries != null)
            {
                int n = table.languages != null ? table.languages.Count : 0;
                foreach (var e in table.entries)
                {
                    sb.Append(Escape(e.key)).Append(',').Append(Escape(e.original));
                    for (int i = 0; i < n; i++)
                        sb.Append(',').Append(Escape(e.translations != null && i < e.translations.Count ? e.translations[i] : string.Empty));
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        // ── ②b 同步主表到外部 CSV（增量，供「同步到表格」按钮）──

        /// <summary>同步主表到外部 CSV（增量）：若 targetCsv 已有内容，保留其现有行（译文不丢），仅把主表中缺失的 key 追加为「original + 空译文」新行；targetCsv 为空或无 Key 列则全量导出。用于「同步到表格」按钮（graph→SO→CSV 流水线的最终一步）。</summary>
        public static string SyncToCsv(StoryLocalizationTable table, string targetCsv)
        {
            if (table == null) return string.Empty;
            if (string.IsNullOrWhiteSpace(targetCsv)) return ExportCsv(table);

            var rows = ParseCsv(targetCsv);
            if (rows.Count < 1) return ExportCsv(table);

            var header = rows[0];
            int idxKey = System.Array.IndexOf(header, ColKey);
            if (idxKey < 0) return ExportCsv(table); // 现有表格无 Key 列，无法增量合并 → 全量导出

            // 现有 CSV 的语言列数（按现有表格对齐，新增行补同样多的空列）
            int langCols = rows[0].Length - 2;
            if (langCols < 0) langCols = 0;
            var existingKeys = new HashSet<string>();
            for (int r = 1; r < rows.Count; r++)
            {
                var f = rows[r];
                if (f.Length > idxKey && !string.IsNullOrEmpty(f[idxKey])) existingKeys.Add(f[idxKey]);
            }

            var sb = new StringBuilder();
            sb.Append(string.Join(",", System.Array.ConvertAll(header, Escape))).Append('\n');
            for (int r = 1; r < rows.Count; r++)
                sb.Append(string.Join(",", System.Array.ConvertAll(rows[r], Escape))).Append('\n');

            if (table.entries != null)
            {
                foreach (var e in table.entries)
                {
                    if (string.IsNullOrEmpty(e.key) || existingKeys.Contains(e.key)) continue;
                    sb.Append(Escape(e.key)).Append(',').Append(Escape(e.original));
                    for (int i = 0; i < langCols; i++) sb.Append(',');
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        // ── ③ 从 CSV 合并回主表（增量）──

        public static ImportReport ImportCsvToTable(string csv, StoryLocalizationTable table)
        {
            var report = new ImportReport();
            if (table == null || string.IsNullOrWhiteSpace(csv)) { report.message = "资产或文件为空。"; return report; }

            var rows = ParseCsv(csv);
            if (rows.Count < 2) { report.message = "文件行数不足（需要表头 + 至少 1 行数据）。"; return report; }

            var header = rows[0];
            int idxKey = System.Array.IndexOf(header, ColKey);
            int idxOrig = System.Array.IndexOf(header, ColOriginal);
            if (idxKey < 0) { report.message = $"未找到「Key」列。表头为：{string.Join(",", header)}"; return report; }

            // 仅当文件中存在对应语言列时才合并该语言（按下标对齐 table.languages）
            var langCols = new List<(int col, int langIndex)>();
            if (table.languages != null)
                for (int li = 0; li < table.languages.Count; li++)
                {
                    int c = System.Array.IndexOf(header, table.languages[li]);
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
                // 原文列非空才更新（空单元格 = 未提供，保留现有）
                if (idxOrig >= 0 && fields.Length > idxOrig && !string.IsNullOrEmpty(fields[idxOrig]))
                { table.SetOriginal(key, fields[idxOrig]); changed++; }
                // 仅非空译文单元格写入（避免部分导入时清空已有译文）
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

        // ── 内部 ──

        private static string Escape(string s)
        {
            if (s == null) return string.Empty;
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>稳健解析 CSV 文本（支持引号包裹、字段内逗号/换行）。返回逐行字段数组。</summary>
        public static List<string[]> ParseCsv(string csv)
        {
            var result = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0, n = csv.Length;
            while (i < n)
            {
                char c = csv[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && csv[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                        inQuotes = false; i++; continue;
                    }
                    field.Append(c); i++; continue;
                }
                if (c == '"') { inQuotes = true; i++; continue; }
                if (c == ',') { row.Add(field.ToString()); field.Clear(); i++; continue; }
                if (c == '\r') { i++; continue; }
                if (c == '\n') { row.Add(field.ToString()); result.Add(row.ToArray()); row.Clear(); field.Clear(); i++; continue; }
                field.Append(c); i++;
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); result.Add(row.ToArray()); }
            return result;
        }
    }
}
