using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 剧情表导入/导出测试：CSV/Excel 列别名解析、两遍解析（前跳/后跳/循环）、终止标识、旧格式兼容、
    /// SO→CSV 往返、xlsx 写回后格式保留（UpdateSheetData 就地替换 sheetData）。
    /// </summary>
    public class TableImportExportTests
    {
        private static readonly string[] Header = { "ID", "类型(Type)", "讲述者(Speaker)", "正文(Text)", "选项(Choice)", "跳转ID(TargetId)" };

        private static List<string[]> Sheet(params string[][] rows)
        {
            var list = new List<string[]> { Header };
            list.AddRange(rows);
            return list;
        }

        private static string TempPath(string ext)
            => Path.Combine(Path.GetTempPath(), "story_test_" + Path.GetRandomFileName().Replace(".", "") + ext);

        /// <summary>ParseRows 包装：warnings 为普通参数（非 out），统一收集返回。</summary>
        private static (List<StoryTableRow> rows, List<string> warnings) Parse(params string[][] rows)
        {
            var warnings = new List<string>();
            StoryTableAssetImporter.ParseRows(Sheet(rows), out var parsed, warnings);
            return (parsed, warnings);
        }

        // ── 导入：ParseRows（不落盘）────────────────────────

        [Test]
        public void Parse_BasicDialogueRows_AutoIdWhenBlank()
        {
            var (rows, warnings) = Parse(
                new[] { "", "", "旁白", "第一句", "", "" },
                new[] { "r4", "对话", "旁白", "结束了", "", "/" });

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("row_0", rows[0].id, "id 列留空时自动生成 row_N");
            Assert.AreEqual("旁白", rows[0].speaker);
            Assert.AreEqual("第一句", rows[0].text);
            Assert.IsNull(rows[0].targetRowId, "无跳转目标 → 后续线性接下一行");
            Assert.AreEqual("/", rows[1].targetRowId, "「/」终止标识");
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Parse_BranchRowWithContinuation_TwoPassResolvesForwardAndBackward()
        {
            var (rows, warnings) = Parse(
                new[] { "r1", "对话", "旁白", "开头", "", "" },
                new[] { "r2", "选项", "老者", "选择吧", "听从", "r4" },   // 前跳：目标 r4 在选项行之后
                new[] { "", "", "", "", "自行", "r1" },                  // 续行：后跳回 r1（循环）
                new[] { "r4", "对话", "旁白", "结尾", "", "" });

            Assert.AreEqual(3, rows.Count, "分支行+两个选项只占一个 StoryTableRow");
            var branch = rows.First(r => r.id == "r2");
            Assert.AreEqual(2, branch.choices.Count, "类型=选项 开启选项组，续行追加");
            Assert.AreEqual("r4", branch.choices[0].targetRowId, "前跳：两遍解析可命中后文目标");
            Assert.AreEqual("r1", branch.choices[1].targetRowId, "后跳/循环同样命中");
            Assert.AreEqual("选择吧", branch.text, "分支行正文归并进「带文字」行");
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Parse_LegacyFormat_DialogueRowFollowedByUntypedOptions()
        {
            var (rows, _) = Parse(
                new[] { "r1", "对话", "旁白", "带选项的对白", "", "" },
                new[] { "", "", "", "", "甲", "r1" },
                new[] { "", "", "", "", "乙", "" });

            Assert.AreEqual(1, rows.Count);
            var r = rows[0];
            Assert.AreEqual(2, r.choices.Count, "旧格式：无类型列的选项行归属其上方对话行");
            Assert.AreEqual("r1", r.choices[0].targetRowId);
            Assert.IsNull(r.choices[1].targetRowId, "目标留空 → null（表节点出口端口，合法语义）");
        }

        [Test]
        public void Parse_LegacyIntegerTarget_ResolvesToExcelRowNumber()
        {
            var (rows, warnings) = Parse(
                new[] { "r1", "对话", "旁白", "第一句", "", "" },
                new[] { "r2", "对话", "旁白", "第二句", "", "2" },   // 旧写法：目标=Excel 行号（表头第1行，r1=第2行）
                new[] { "r3", "对话", "旁白", "第三句", "", "" });

            Assert.AreEqual("r1", rows[1].targetRowId, "整数行号兼容解析回行 id");
            Assert.IsEmpty(warnings, "兼容解析不告警");
        }

        [Test]
        public void Parse_InvalidTarget_WarnsAndKeepsOptionAsExit()
        {
            var (rows, warnings) = Parse(
                new[] { "r1", "选项", "旁白", "选择", "甲", "ghost_row" });

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(1, rows[0].choices.Count, "目标无效时选项仍保留（降级为表节点出口）");
            Assert.IsTrue(string.IsNullOrEmpty(rows[0].choices[0].targetRowId));
            Assert.IsNotEmpty(warnings, "应产生告警");
            StringAssert.Contains("ghost_row", warnings[0]);
        }

        [Test]
        public void Parse_DuplicateId_RenamesWithWarning()
        {
            var (rows, warnings) = Parse(
                new[] { "dup", "对话", "旁白", "一", "", "" },
                new[] { "dup", "对话", "旁白", "二", "", "" });

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("dup", rows[0].id);
            StringAssert.StartsWith("dup_", rows[1].id, "重复 id 自动重命名");
            Assert.IsNotEmpty(warnings);
        }

        [Test]
        public void Parse_OptionRowWithoutPrecedingDialogue_SkipsWithWarning()
        {
            var (rows, warnings) = Parse(new[] { "", "", "", "", "孤选项", "r1" });
            Assert.AreEqual(0, rows.Count);
            Assert.IsNotEmpty(warnings);
        }

        [TestCase("ID|类型|讲述者|正文|选项|编号")]
        [TestCase("id|Type|Speaker|Text|Choice|TargetId")]
        [TestCase("ID|类型(Type)|讲述者(Speaker)|正文(Text)|选项(Choice)|跳转ID(TargetId)")]
        public void Parse_HeaderAliases_ChineseEnglishAndBracketed(string headerJoined)
        {
            var header = headerJoined.Split('|');
            var warnings = new List<string>();
            var sheet = new List<string[]> { header, new[] { "r1", "对话", "旁白", "内容", "", "" } };
            StoryTableAssetImporter.ParseRows(sheet, out var parsed, warnings);

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("内容", parsed[0].text);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Parse_MissingTextColumn_Warns()
        {
            var warnings = new List<string>();
            var sheet = new List<string[]> { new[] { "ID", "讲述者" }, new[] { "r1", "旁白" } };
            StoryTableAssetImporter.ParseRows(sheet, out var parsed, warnings);
            Assert.AreEqual(0, parsed.Count);
            Assert.IsNotEmpty(warnings);
        }

        // ── 导入：ImportFromFile（真实临时文件）─────────────

        [Test]
        public void ImportFromCsv_QuotedFieldsWithComma()
        {
            string path = null;
            var table = ScriptableObject.CreateInstance<StoryTableAsset>();
            try
            {
                path = TempPath(".csv");
                File.WriteAllText(path, "ID,类型(Type),讲述者(Speaker),正文(Text),选项(Choice),跳转ID(TargetId)\r\nr1,对话,旁白,\"含,逗号的正文\",,\r\n", new UTF8Encoding(false));
                StoryTableAssetImporter.ImportFromFile(table, path, out _);

                Assert.AreEqual(1, table.rows.Count);
                Assert.AreEqual("含,逗号的正文", table.rows[0].text, "CSV 引号转义字段应完整解析");
            }
            finally
            {
                if (path != null && File.Exists(path)) File.Delete(path);
                Undo.ClearUndo(table);
            }
        }

        [Test]
        public void ImportFromXlsx_RoundtripViaWriteWorkbook()
        {
            string path = null;
            var table = ScriptableObject.CreateInstance<StoryTableAsset>();
            try
            {
                path = TempPath(".xlsx");
                StoryXlsx.WriteWorkbook(path, new List<XlsSheet>
                {
                    new XlsSheet
                    {
                        Name = "剧情",
                        Rows = Sheet(
                            new[] { "r1", "对话", "旁白", "表格第一句", "", "" },
                            new[] { "r2", "选项", "老者", "选吗", "好", "r1" }),
                    },
                });
                StoryTableAssetImporter.ImportFromFile(table, path, out _);

                Assert.AreEqual(2, table.rows.Count);
                Assert.AreEqual("表格第一句", table.rows[0].text);
                Assert.AreEqual(1, table.rows[1].choices.Count);
                Assert.AreEqual("r1", table.rows[1].choices[0].targetRowId, "xlsx 跳转目标同样按行 id 解析");
            }
            finally
            {
                if (path != null && File.Exists(path)) File.Delete(path);
                Undo.ClearUndo(table);
            }
        }

        // ── 导出：SO → CSV 往返 ─────────────────────────────

        [Test]
        public void ExportToCsv_RoundtripPreservesStructure()
        {
            string path = null;
            var src = ScriptableObject.CreateInstance<StoryTableAsset>();
            var dst = ScriptableObject.CreateInstance<StoryTableAsset>();
            try
            {
                path = TempPath(".csv");
                // 源文件须已存在（导出语义=写回既有源表；不存在时静默跳过），先铺规范表头。
                File.WriteAllText(path, string.Join(",", Header) + "\r\n", new UTF8Encoding(false));
                src.sourceFilePath = path;
                src.rows.Add(new StoryTableRow { id = "r1", speaker = "旁白", text = "第一句" });
                src.rows.Add(new StoryTableRow
                {
                    id = "r2",
                    speaker = "老者",
                    text = "选择吧",
                    choices = new List<StoryTableChoice>
                    {
                        new StoryTableChoice { text = "听从", targetRowId = "r3" },
                        new StoryTableChoice { text = "终止项", targetRowId = "/" },
                    },
                });
                src.rows.Add(new StoryTableRow { id = "r3", speaker = "旁白", text = "结尾", targetRowId = "/" });

                StoryTableAssetExporter.ExportToSource(src);
                Assert.IsTrue(File.Exists(path), "导出应写回源文件");

                StoryTableAssetImporter.ImportFromFile(dst, path, out _);
                Assert.AreEqual(3, dst.rows.Count);
                Assert.AreEqual("第一句", dst.rows[0].text);
                Assert.AreEqual("旁白", dst.rows[0].speaker);
                var branch = dst.rows.First(r => r.id == "r2");
                Assert.AreEqual(2, branch.choices.Count);
                Assert.AreEqual("听从", branch.choices[0].text);
                Assert.AreEqual("r3", branch.choices[0].targetRowId);
                Assert.AreEqual("/", branch.choices[1].targetRowId, "「/」终止标识往返保持");
                Assert.AreEqual("/", dst.rows[2].targetRowId);
            }
            finally
            {
                if (path != null && File.Exists(path)) File.Delete(path);
                Undo.ClearUndo(src);
                Undo.ClearUndo(dst);
            }
        }

        // ── 导出：xlsx 就地写回保格式 ───────────────────────

        [Test]
        public void UpdateSheetData_ReplacesValues_PreservesColsAndOtherParts()
        {
            string path = null;
            try
            {
                path = TempPath(".xlsx");
                StoryXlsx.WriteWorkbook(path, new List<XlsSheet>
                {
                    new XlsSheet { Name = "剧情", Rows = Sheet(new[] { "r1", "对话", "旁白", "旧内容", "", "" }) },
                });

                // 人为注入 Excel 原生排版部件：列宽 <cols>（模拟用户在 Excel 里调过的格式）。
                InjectCols(path, 42f);

                // 就地写回新数据行。
                StoryXlsx.UpdateSheetData(path, "剧情", Sheet(new[] { "r9", "对话", "旁白", "新内容", "", "" }));

                // ① 数据已更新且可读回。
                var sheets = StoryXlsx.ReadWorkbook(path);
                var sheet = sheets.First(s => s.Name == "剧情");
                Assert.AreEqual("r9", sheet.Rows[1][0]);
                Assert.AreEqual("新内容", sheet.Rows[1][3]);

                // ② 用户排版（<cols width=42>）原样保留——这正是「同步不毁排版」的核心契约。
                string xml = ReadZipEntry(path, "xl/worksheets/sheet1.xml");
                StringAssert.Contains("<cols", xml);
                StringAssert.Contains("width=\"42\"", xml);
            }
            finally
            {
                if (path != null && File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ExportToXlsx_ModifiedRowsWrittenBackToSource()
        {
            string path = null;
            var table = ScriptableObject.CreateInstance<StoryTableAsset>();
            try
            {
                path = TempPath(".xlsx");
                StoryXlsx.WriteWorkbook(path, new List<XlsSheet> { new XlsSheet { Name = "剧情", Rows = Sheet(new[] { "r1", "对话", "旁白", "原始", "", "" }) } });

                table.sourceFilePath = path;
                table.rows.Add(new StoryTableRow { id = "r1", speaker = "旁白", text = "面板改过的" });
                table.rows.Add(new StoryTableRow { id = "r2", speaker = "旁白", text = "新增行" });

                StoryTableAssetExporter.ExportToSource(table);

                var sheet = StoryXlsx.ReadWorkbook(path).First(s => s.Name == "剧情");
                Assert.AreEqual(3, sheet.Rows.Count, "表头+2 数据行");
                Assert.AreEqual("面板改过的", sheet.Rows[1][3]);
                Assert.AreEqual("新增行", sheet.Rows[2][3]);
            }
            finally
            {
                if (path != null && File.Exists(path)) File.Delete(path);
                Undo.ClearUndo(table);
            }
        }

        // ── 工具 ────────────────────────────────────────────

        private static void InjectCols(string xlsxPath, float width)
        {
            using (var fs = File.Open(xlsxPath, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("xl/worksheets/sheet1.xml");
                string xml;
                using (var sr = new StreamReader(entry.Open()))
                    xml = sr.ReadToEnd();
                xml = xml.Replace("<sheetData>", $"<cols><col min=\"1\" max=\"1\" width=\"{width}\" customWidth=\"1\"/></cols><sheetData>");
                entry.Delete();
                var newEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
                using (var sw = new StreamWriter(newEntry.Open()))
                    sw.Write(xml);
            }
        }

        private static string ReadZipEntry(string xlsxPath, string entryName)
        {
            using (var fs = File.OpenRead(xlsxPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            using (var sr = new StreamReader(zip.GetEntry(entryName).Open()))
                return sr.ReadToEnd();
        }
    }
}
