using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 把 <see cref="StoryTableAsset"/>（真相源 SO）写回其源文件（.xlsx/.csv），
    /// 使在编辑器面板里修改的对白/讲述者/选项同步回到「人维护的 Excel 表」。
    ///
    /// 列布局与 <see cref="StoryTableAssetImporter"/> 严格一致（ID/类型(Type)/讲述者(Speaker)/正文(Text)/选项(Choice)/跳转ID(TargetId)），
    /// 以保证重新导入可原样读回：每个对白行 → 一行（类型列填「对话」，跳转ID列填该行跳转目标，空=按表内顺序接下一行，**「/」=终止输出端**）；
    /// 分支行（带选项）→ 不写独立对话行，首个选项行即「带内容的选项类型」——类型列填「选项」并承载该行的
    /// id/正文/讲述者 + 首个选项文本/目标；后续选项续行（类型留空，仅填选项文本与目标行 id）。
    ///
    /// 写回是「覆盖式」重建：读回源文件表头以保留列顺序/名称，再按 SO 当前 rows 重写数据行；
    /// 因此面板里对文字的任何增删改都会在源文件里得到反映（即用户期望的「修改文字内容后同步到 Excel」）。
    /// 注意：写回不是自动的——由面板上「同步到 Excel」按钮调用 <see cref="ExportToSource"/> 显式触发，
    /// 同步成功/失败由调用方弹窗反馈，避免静默更新。
    /// </summary>
    public static class StoryTableAssetExporter
    {
        private static readonly string[] IdCols = { "id", "ID", "行ID", "RowId", "标识" };
        private static readonly string[] SpeakerCols = { "Speaker", "讲述者", "角色" };
        private static readonly string[] TextCols = { "Text", "正文", "对白", "台词" };
        private static readonly string[] ChoiceCols = { "Choice", "选项" };
        private static readonly string[] TargetCols = { "编号", "目标", "Target", "跳转", "Goto", "目标ID", "TargetId", "跳转ID" };
        private static readonly string[] TypeCols = { "类型", "Type", "节点类型", "NodeType" };
        private static readonly string[] CanonicalHeader = { "ID", "类型(Type)", "讲述者(Speaker)", "正文(Text)", "选项(Choice)", "跳转ID(TargetId)" };

        /// <summary>该表是否配置了可写回的源文件（sourceFilePath 指向一个已存在的 .xlsx/.csv）。</summary>
        public static bool HasSource(StoryTableAsset table)
        {
            if (table == null) return false;
            return !string.IsNullOrEmpty(StoryAssetPaths.ResolveSourcePath(table.sourceFilePath));
        }

        /// <summary>把 SO 当前内容写回源文件（覆盖式）。源文件缺省/不存在时静默跳过。</summary>
        public static void ExportToSource(StoryTableAsset table)
        {
            if (table == null) return;
            string abs = StoryAssetPaths.ResolveSourcePath(table.sourceFilePath);
            if (string.IsNullOrEmpty(abs)) return;

            string sheetName = "剧情";
            var header = ReadHeader(abs, out sheetName);

            int cId = FindCol(header, IdCols);
            int cSpeaker = FindCol(header, SpeakerCols);
            int cText = FindCol(header, TextCols);
            int cChoice = FindCol(header, ChoiceCols);
            int cTarget = FindCol(header, TargetCols);
            int cType = FindCol(header, TypeCols);
            if (cText < 0)
            {
                // 源文件无「对白」列（可能是其它格式）：退回规范表头（含类型列），确保可重新导入
                header = CanonicalHeader;
                cId = 0; cType = 1; cSpeaker = 2; cText = 3; cChoice = 4; cTarget = 5;
            }
            else if (cType < 0)
            {
                // 源表头缺少类型列：末尾追加，保证导出后 Excel 带「类型」列
                var h2 = new string[header.Length + 1];
                Array.Copy(header, h2, header.Length);
                h2[header.Length] = "类型";
                header = h2;
                cType = header.Length - 1;
            }

            var rows = new List<string[]> { header };
            foreach (var row in table.rows ?? new List<StoryTableRow>())
            {
                if (row == null) continue;
                var choices = row.choices ?? new List<StoryTableChoice>();
                bool hasChoices = choices.Any(c => c != null);
                if (!hasChoices)
                {
                    // 纯对白行：一行（类型=对话；编号列可填行跳转目标，空=按表内顺序接下一行）
                    var dRow = NewRow(header.Length);
                    Set(dRow, cId, row.id);
                    Set(dRow, cText, row.text ?? "");
                    Set(dRow, cSpeaker, row.speaker ?? "");
                    Set(dRow, cType, "对话");
                    Set(dRow, cTarget, row.targetRowId ?? "");
                    rows.Add(dRow);
                    continue;
                }
                // 分支行（带选项）：不写独立对话行——首个选项行即「带内容的选项类型」，
                // 承载该行的 id/正文/讲述者（类型=选项）+ 首个选项文本/目标；后续选项续行（类型留空，以类型填写划分归属）。
                bool firstChoice = true;
                foreach (var ch in choices)
                {
                    if (ch == null) continue;
                    var cRow = NewRow(header.Length);
                    if (firstChoice)
                    {
                        Set(cRow, cId, row.id);
                        Set(cRow, cText, row.text ?? "");
                        Set(cRow, cSpeaker, row.speaker ?? "");
                        Set(cRow, cType, "选项");
                        firstChoice = false;
                    }
                    Set(cRow, cChoice, ch.text ?? "");
                    Set(cRow, cTarget, ch.targetRowId ?? "");
                    rows.Add(cRow);
                }
            }

            if (abs.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                StoryXlsx.UpdateSheetData(abs, sheetName, rows); // 就地更新，保留原文件格式（列宽/表头/字号等）
            else
                File.WriteAllText(abs, ToCsv(rows), new UTF8Encoding(false));
        }

        /// <summary>读取源文件表头（保持列顺序），并尽量沿用其工作表名（xlsx）。读不到时返回空表头。</summary>
        private static string[] ReadHeader(string abs, out string sheetName)
        {
            sheetName = "剧情";
            try
            {
                if (abs.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    var sheets = StoryXlsx.ReadWorkbook(abs);
                    var s = sheets.FirstOrDefault(x => x.Name == "剧情")
                         ?? sheets.FirstOrDefault(x => x.Name.Equals("Story", StringComparison.OrdinalIgnoreCase))
                         ?? sheets.FirstOrDefault(x => x.Rows.Count > 0);
                    if (s != null && s.Rows.Count > 0)
                    {
                        sheetName = s.Name;
                        return s.Rows[0];
                    }
                }
                else if (File.Exists(abs))
                {
                    var first = File.ReadLines(abs).FirstOrDefault();
                    if (!string.IsNullOrEmpty(first))
                        return StoryLocalizationCsv.ParseCsv(first).FirstOrDefault() ?? new string[0];
                }
            }
            catch { /* 读不到表头则退回规范表头 */ }
            return new string[0];
        }

        private static string[] NewRow(int len)
        {
            var r = new string[len];
            for (int i = 0; i < len; i++) r[i] = "";
            return r;
        }

        private static void Set(string[] row, int col, string val)
        {
            if (col >= 0 && col < row.Length) row[col] = val ?? "";
        }

        private static int FindCol(string[] header, string[] names)
        {
            if (header == null) return -1;
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] == null) continue;
                var h = header[i].Trim();
                foreach (var n in names)
                    if (h.Equals(n, StringComparison.OrdinalIgnoreCase)) return i;
                // 兼容「中文(英文)」双列表头：剥掉括号内容后再比对（如「类型(Type)」→「类型」，「ID」原样）
                var stripped = StripBracket(h);
                if (!string.IsNullOrEmpty(stripped))
                    foreach (var n in names)
                        if (stripped.Equals(n, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>去掉表头中半角/全角括号及其内容，返回括号前的部分（表头为「中文(英文)」或「英文(中文)」双列名时用于别名匹配）。</summary>
        private static string StripBracket(string s)
        {
            int i = s.IndexOf('(');
            if (i < 0) i = s.IndexOf('（');
            if (i < 0) return s;
            return s.Substring(0, i).Trim();
        }

        private static string ToCsv(List<string[]> rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                for (int c = 0; c < row.Length; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append(CsvField(row[c] ?? ""));
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        private static string CsvField(string s)
        {
            if (s.Contains('"') || s.Contains(',') || s.Contains('\r') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
