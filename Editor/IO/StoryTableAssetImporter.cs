using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 把 CSV/Excel 剧情表解析进 <see cref="StoryTableAsset"/>（真相源 SO），不生成任何节点。
    /// 列规则（表头大小写/空白不敏感，支持中英文别名与「中文(英文)」双列表头）：
    ///   - ID（可选，别名：id/行ID/RowId/标识）：本行稳定键；缺省时自动生成 "row_N"。选项目标引用此 id。
    ///   - 类型(Type)（可选，别名：节点类型/NodeType）：节点类型标记——对白行填「对话」；选项行仅**首个选项**填「选项」、
    ///     后续同组选项留空（以类型填写划分选项归属）。
    ///   - 讲述者(Speaker)（可选，别名：角色）：讲述者 ID。
    ///   - 正文(Text)（必填，别名：对白/台词）：对白文本；空则整行跳过。
    ///   - 选项(Choice)（可选）：**选项独占一行**——单个选项占一行、多选项向下排列。
    ///   - 跳转ID(TargetId)（可选，别名：编号/目标/Target/跳转/Goto/目标ID）：**对白行与选项行通用**——对白行填该行「跳转目标」
    ///     （播完本句直接跳到目标行；留空=按表内顺序接下一行）；选项行填该选项要跳转到的**目标行 id**（非 Excel 行号）。
    ///     **填「/」= 终止标识**：该行/该选项无后继（输出端——对白行得 exit_ 出口端口、选项得 optexit_ 出口端口）。
    ///     兼容旧写法：若填的是整数且能匹配到某行的 Excel 行号，则回退解析为该行 id。
    ///   - **分支行（带选项）推荐写法**：不写独立的「对话」行；首个选项行即「带内容的选项类型」——同填
    ///     正文/讲述者/id 与首个选项文本/目标（类型=选项），后续选项续行（类型留空）。
    ///     兼容旧写法：对白行（选项空）+ 下方选项续行仍可解析（选项归属其所在对白行）。
    ///
    /// 解析结果直接覆盖 <see cref="StoryTableAsset.rows"/>（导入语义）。增删行/编辑内容请在编辑器或重导入完成。
    /// 复用 <see cref="StoryLocalizationCsv.ParseCsv"/>（CSV）与 <see cref="StoryXlsx.ReadWorkbook"/>（Excel）。
    /// </summary>
    public static class StoryTableAssetImporter
    {
        private static readonly string[] IdCols = { "id", "ID", "行ID", "RowId", "标识" };
        private static readonly string[] SpeakerCols = { "Speaker", "讲述者", "角色" };
        private static readonly string[] TextCols = { "Text", "正文", "对白", "台词" };
        private static readonly string[] ChoiceCols = { "Choice", "选项" };
        private static readonly string[] TargetCols = { "编号", "目标", "Target", "跳转", "Goto", "目标ID", "TargetId", "跳转ID" };
        private static readonly string[] TypeCols = { "类型", "Type", "节点类型", "NodeType" };

        /// <summary>从文件（.csv/.xlsx）解析并填充 <paramref name="table"/> 的 rows。失败抛异常由调用方处理。</summary>
        public static void ImportFromFile(StoryTableAsset table, string path, out List<string> warnings)
        {
            warnings = new List<string>();
            if (table == null || string.IsNullOrEmpty(path)) return;

            List<string[]> rows;
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                var sheets = StoryXlsx.ReadWorkbook(path);
                var sheet = sheets.FirstOrDefault(s => s.Name == "剧情")
                         ?? sheets.FirstOrDefault(s => s.Name.Equals("Story", StringComparison.OrdinalIgnoreCase))
                         ?? sheets.FirstOrDefault(s => s.Rows.Count > 0 && HasTextHeader(s.Rows[0]));
                if (sheet == null)
                    throw new InvalidOperationException("文件中未找到剧情工作表（需含「对白/Text」列的表头）。");
                rows = sheet.Rows;
            }
            else
            {
                rows = StoryLocalizationCsv.ParseCsv(File.ReadAllText(path));
            }

            ParseRows(rows, out var outRows, warnings);
            Undo.RecordObject(table, "导入表格到剧情表");
            table.rows = outRows;
            EditorUtility.SetDirty(table);
        }

        /// <summary>遍历图中所有剧情表节点，从各自源文件重新导入并同步（Excel → SO）。返回 (导入数, 跳过数)。
        /// 供主窗口菜单「表格驱动/重新导入并同步全部」与子画布窗口「从 Excel 表还原」共用。</summary>
        public static (int imported, int skipped) ReimportAllTables(StoryGraphAsset graph)
        {
            int n = 0, skip = 0;
            if (graph == null || graph.nodes == null) return (0, 0);
            var seen = new HashSet<StoryTableAsset>();
            foreach (var node in graph.nodes)
            {
                if (node is StoryTableNodeData tn && tn.tableAsset != null && seen.Add(tn.tableAsset))
                {
                    string src = StoryAssetPaths.ResolveSourcePath(tn.tableAsset.sourceFilePath);
                    if (!string.IsNullOrEmpty(src))
                    {
                        ImportFromFile(tn.tableAsset, src, out _);
                        tn.tableAsset.unsyncedToExcel = false;
                        EditorUtility.SetDirty(tn.tableAsset);
                        n++;
                    }
                    else skip++;
                }
            }
            return (n, skip);
        }

        /// <summary>把已解析的表格行（含表头 rows[0]）转为 <see cref="StoryTableRow"/> 列表，并解析选项的 targetRowId。</summary>
        public static void ParseRows(List<string[]> rows, out List<StoryTableRow> outRows, List<string> warnings)
        {
            outRows = new List<StoryTableRow>();
            warnings ??= new List<string>();
            if (rows == null || rows.Count < 2)
            {
                warnings.Add("表格行数不足（需要表头 + 至少 1 行数据）。");
                return;
            }

            var header = rows[0];
            int cId = FindCol(header, IdCols);
            int cSpeaker = FindCol(header, SpeakerCols);
            int cText = FindCol(header, TextCols);
            int cChoice = FindCol(header, ChoiceCols);
            int cTarget = FindCol(header, TargetCols);
            int cType = FindCol(header, TypeCols);
            if (cText < 0)
            {
                warnings.Add($"未找到「对白/Text」列。表头为：{string.Join(",", header)}");
                return;
            }

            var mapById = new Dictionary<string, StoryTableRow>(StringComparer.Ordinal);
            var mapByExcelRow = new Dictionary<int, StoryTableRow>();
            // 选项行暂存（owner 引用 + 原始目标串），待所有对白行建完后第二遍解析目标 id，
            // 以支持「目标行在选项行之后」的前跳/后跳引用（单遍顺序解析会因 mapById 尚未建立而查不到）。
            var pendingChoices = new List<(StoryTableRow owner, int excelRow, string choice, string rawTarget)>();
            // 对白行跳转目标暂存（目标行可能在后文，待所有行建完后第二遍解析）
            var pendingRowTargets = new List<(StoryTableRow row, int excelRow, string rawTarget)>();
            StoryTableRow current = null;
            // 当前选项组归属行：首个选项填「类型」开启新组（归属 current），未填类型的选项继续该组
            StoryTableRow optionOwner = null;

            // 第一遍：建所有对白行 + 暂存选项行
            for (int r = 1; r < rows.Count; r++)
            {
                var fields = rows[r];
                if (fields == null) continue;
                int excelRow = r + 1; // 表头为第 1 行
                string text = Cell(fields, cText).Trim();
                string speaker = Cell(fields, cSpeaker).Trim();
                string choice = Cell(fields, cChoice).Trim();

                // 分支行带内容的首选项（新格式）：正文 + 讲述者 + 首个选项同在一行（类型=选项）。
                // 本行即「分支行文字 + 首个选项」，编号列是选项目标（非行跳转目标）。
                if (!string.IsNullOrEmpty(choice) && !string.IsNullOrEmpty(text))
                {
                    string bid = cId >= 0 ? Cell(fields, cId).Trim() : "";
                    if (string.IsNullOrEmpty(bid)) bid = "row_" + outRows.Count;
                    if (mapById.ContainsKey(bid))
                    {
                        warnings.Add($"第 {excelRow} 行 id「{bid}」重复，已自动重命名为 {bid}_{r}。");
                        bid = $"{bid}_{r}";
                    }
                    var bRow = new StoryTableRow { id = bid, speaker = speaker, text = text };
                    outRows.Add(bRow);
                    mapById[bid] = bRow;
                    mapByExcelRow[excelRow] = bRow;
                    current = bRow;
                    optionOwner = bRow;
                    var rawT0 = cTarget >= 0 ? Cell(fields, cTarget).Trim() : "";
                    pendingChoices.Add((bRow, excelRow, choice, rawT0));
                    continue;
                }

                // 含对白的纯对白行 → 新建一行归属（选项续行会追加到 current.choices）
                if (!string.IsNullOrEmpty(text))
                {
                    string id = cId >= 0 ? Cell(fields, cId).Trim() : "";
                    if (string.IsNullOrEmpty(id)) id = "row_" + outRows.Count;
                    if (mapById.ContainsKey(id))
                    {
                        warnings.Add($"第 {excelRow} 行 id「{id}」重复，已自动重命名为 {id}_{r}。");
                        id = $"{id}_{r}";
                    }
                    var row = new StoryTableRow { id = id, speaker = speaker, text = text };
                    outRows.Add(row);
                    mapById[id] = row;
                    mapByExcelRow[excelRow] = row;
                    current = row;
                    optionOwner = null; // 新对白行：其后选项组重新归属
                    // 对白行跳转目标（「编号」列）：暂存，第二遍解析（目标行可能在后文）
                    string rawRowT = cTarget >= 0 ? Cell(fields, cTarget).Trim() : "";
                    if (!string.IsNullOrEmpty(rawRowT))
                        pendingRowTargets.Add((row, excelRow, rawRowT));
                    continue;
                }

                // 选项续行（正文空、选项非空）：类型列划分归属——首个选项填「选项」开启新组（归属当前对白行），
                // 未填类型的后续选项继续该组；旧表无类型列时，未填类型也回退归属当前对白行。
                if (string.IsNullOrEmpty(choice)) continue;
                string rowType = cType >= 0 ? Cell(fields, cType).Trim() : "";
                if (current == null)
                {
                    warnings.Add($"第 {excelRow} 行是选项行，但前面没有对白行，已跳过：{choice}");
                    continue;
                }
                if (!string.IsNullOrEmpty(rowType) || optionOwner == null)
                    optionOwner = current;
                var rawT = cTarget >= 0 ? Cell(fields, cTarget).Trim() : "";
                pendingChoices.Add((optionOwner, excelRow, choice, rawT));
            }

            // 第二遍：解析选项 targetRowId（此时 mapById 已完整，前跳/后跳/循环均可命中）。
            // 【单剧情表节点架构】选项「不一定要有连接编号」——无连接编号的选项是表节点的合法出口端口（optexit_…），
            // 因此无论是否有 targetRowId 都保留该选项；仅当「填了但无效」时才告警并降级为空（出口语义）。
            foreach (var (owner, excelRow, choice, rawT) in pendingChoices)
            {
                var opt = new StoryTableChoice { text = choice };
                if (!string.IsNullOrEmpty(rawT))
                {
                    if (rawT == "/")
                        opt.targetRowId = "/"; // 终止标识：该选项无内部目标，作为表节点出口端口（optexit_…）
                    else if (mapById.TryGetValue(rawT, out var byId))
                        opt.targetRowId = byId.id;
                    else if (int.TryParse(rawT, out int tr) && mapByExcelRow.TryGetValue(tr, out var byRow))
                        opt.targetRowId = byRow.id; // 兼容旧「编号=Excel 行号」写法
                    else
                        warnings.Add($"第 {excelRow} 行选项「{choice}」的目标「{rawT}」不是有效的行 id，已忽略该目标（选项保留为表节点出口）。");
                }
                // 始终保留选项：有连接编号→内部跳转；无连接编号→表节点出口端口。
                owner.choices.Add(opt);
            }

            // 对白行跳转目标第二遍解析（此时 mapById 已完整，前跳/后跳/循环均可命中）
            foreach (var (rRow, excelRow, rawT) in pendingRowTargets)
            {
                if (rawT == "/") { rRow.targetRowId = "/"; continue; } // 终止标识：本行是输出端（无后继，表节点出口）
                if (mapById.TryGetValue(rawT, out var byId))
                    rRow.targetRowId = byId.id;
                else if (int.TryParse(rawT, out int tr) && mapByExcelRow.TryGetValue(tr, out var byRow))
                    rRow.targetRowId = byRow.id; // 兼容旧「编号=Excel 行号」写法
                else
                    warnings.Add($"第 {excelRow} 行对白的跳转目标「{rawT}」不是有效的行 id，已忽略（该行按表内顺序接下一行）。");
            }
        }

        private static bool HasTextHeader(string[] header)
            => FindCol(header, TextCols) >= 0;

        private static string Cell(string[] fields, int col)
            => (col >= 0 && col < fields.Length) ? (fields[col] ?? "") : "";

        private static int FindCol(string[] header, string[] names)
        {
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
    }
}
