using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 零依赖的最小 OOXML（.xlsx）读写器。
    /// 仅依赖 <see cref="System.IO.Compression.ZipArchive"/> 与 <see cref="System.Xml.Linq.XElement"/>，
    /// 不引入任何第三方 Excel 库，规避 Unity 工程引入 DLL 的兼容风险。
    ///
    /// 设计要点：
    /// - 单元格统一使用 <c>inlineStr</c>（内联字符串），无需维护 sharedStrings 共享表，最简单稳健。
    /// - 仅当单元格文本可被稳定解析为数值时才写成数值单元格（&lt;v&gt;），其余一律内联字符串。
    /// - 支持多工作表读写（<see cref="XlsSheet"/>），按名字索引，跨软件（Excel / WPS / LibreOffice）兼容。
    /// </summary>
    public sealed class XlsSheet
    {
        /// <summary>工作表显示名（≤31 字符，不含 : \ / ? * [ ]）。</summary>
        public string Name;

    /// <summary>行数据：Rows[0] 为表头，其余为数据行；每格为字符串（数值单元格读回亦为字符串）。</summary>
    public List<string[]> Rows = new List<string[]>();
    }

    /// <summary>导入结果诊断信息：便于在编辑器内定位「0 匹配」等问题（文件读回正常但资产侧找不到节点）。</summary>
    public struct ImportReport
    {
        /// <summary>成功写回资产的条目数。</summary>
        public int changed;
        /// <summary>数据行数（不含表头）。</summary>
        public int dataRows;
        /// <summary>格式正确的 key 数（本地化：以 .text 结尾或含 .opt.；节点：存在 NodeId）。</summary>
        public int wellFormed;
        /// <summary>在资产中成功定位到目标字段的条目数。</summary>
        public int resolved;
        /// <summary>是否找到主键列（本地化 Key / 节点 NodeId）。</summary>
        public bool hasKeyCol;
        /// <summary>是否找到目标值列（本地化语言列或 Original / 节点 Value）。</summary>
        public bool hasValueCol;
        /// <summary>人类可读摘要，可直接展示给用户。</summary>
        public string message;
    }

    public static class StoryXlsx
    {
        private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string NsRels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string NsPkgRels = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string NsContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly XNamespace XNS = XNamespace.Get(NsMain);
        private static readonly XNamespace RNS = XNamespace.Get(NsRels);
        private static readonly XNamespace PNS = XNamespace.Get(NsPkgRels);
        private static readonly XNamespace CNS = XNamespace.Get(NsContentTypes);
        private static readonly XNamespace XmlNs = XNamespace.Get("http://www.w3.org/XML/1998/namespace");

        // ══ 写 ══

        /// <summary>把多张工作表写入 .xlsx 文件（覆盖式创建）。</summary>
        public static void WriteWorkbook(string path, List<XlsSheet> sheets)
        {
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            var names = MakeSafeSheetNames(sheets);

            using (var fs = File.Create(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false))
            {
                // [Content_Types].xml
                var ct = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(CNS + "Types",
                        new XElement(CNS + "Default", new XAttribute("Extension", "rels"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                        new XElement(CNS + "Default", new XAttribute("Extension", "xml"),
                            new XAttribute("ContentType", "application/xml")),
                        new XElement(CNS + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                        from i in Enumerable.Range(0, sheets.Count)
                        select new XElement(CNS + "Override",
                            new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                        new XElement(CNS + "Override", new XAttribute("PartName", "/xl/styles.xml"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))
                    ));
                AddEntry(zip, "[Content_Types].xml", ct);

                // _rels/.rels
                var rootRels = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(PNS + "Relationships",
                        new XElement(PNS + "Relationship", new XAttribute("Id", "rIdWb"),
                            new XAttribute("Type", NsRels + "/officeDocument"),
                            new XAttribute("Target", "xl/workbook.xml"))));
                AddEntry(zip, "_rels/.rels", rootRels);

                // xl/workbook.xml
                var wb = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(XNS + "workbook",
                        new XAttribute(XNamespace.Xmlns + "r", NsRels),
                        new XElement(XNS + "sheets",
                            from i in Enumerable.Range(0, sheets.Count)
                            select new XElement(XNS + "sheet",
                                new XAttribute("name", names[i]),
                                new XAttribute("sheetId", i + 1),
                                new XAttribute(RNS + "id", "rId" + (i + 1))))));
                AddEntry(zip, "xl/workbook.xml", wb);

                // xl/_rels/workbook.xml.rels
                var wbRels = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(PNS + "Relationships",
                        from i in Enumerable.Range(0, sheets.Count)
                        select new XElement(PNS + "Relationship",
                            new XAttribute("Id", "rId" + (i + 1)),
                            new XAttribute("Type", NsRels + "/worksheet"),
                            new XAttribute("Target", $"worksheets/sheet{i + 1}.xml")),
                        new XElement(PNS + "Relationship",
                            new XAttribute("Id", "rIdStyles"),
                            new XAttribute("Type", NsRels + "/styles"),
                            new XAttribute("Target", "styles.xml"))));
                AddEntry(zip, "xl/_rels/workbook.xml.rels", wbRels);

                // 各工作表
                for (int i = 0; i < sheets.Count; i++)
                    AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheet(sheets[i].Rows));

                // 极简样式表（保证跨软件兼容，所有单元格默认格式）
                AddEntry(zip, "xl/styles.xml", BuildStyles());
            }
        }

        /// <summary>
        /// 把内存中的 zip 部件（XML 解析为 XDocument，其余存原始字节）缓存起来，
        /// 以便「就地更新某工作表」时，只改目标表、其余部件原样写回，从而保留原文件格式。
        /// </summary>
        private struct ZipPart
        {
            public bool IsXml;
            public XDocument Doc;
            public byte[] Raw;
        }

        /// <summary>
        /// 就地更新指定工作表的单元格数据，保留文件其余所有格式
        /// （样式表、列宽 &lt;cols&gt;、冻结窗格、行高、其它工作表、图表 / 绘图等部件）。
        /// 用于「剧情表写回 Excel」时不再破坏用户原有的间距 / 表头 / 字号排版。
        /// <para>· 表头行（首行）优先沿用其原有样式索引（如原有加粗 / 底色则保留）；若原表头无任何样式则套用加粗样式。</para>
        /// <para>· 若工作表缺少列宽则按默认列宽（16）写入；已存在列宽则原样保留。</para>
        /// </summary>
        public static void UpdateSheetData(string path, string sheetName, List<string[]> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0) return;
            if (!File.Exists(path)) throw new FileNotFoundException("找不到要写回的 Excel 文件", path);

            // 1) 把整个 zip 读入内存（XML 部件解析为 XDocument，其余存原始字节）
            var parts = new Dictionary<string, ZipPart>(StringComparer.OrdinalIgnoreCase);
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read, false))
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue; // 跳过纯目录项
                    try
                    {
                        var doc = XDocument.Load(e.Open());
                        parts[e.FullName] = new ZipPart { IsXml = true, Doc = doc };
                    }
                    catch
                    {
                        using (var ms = new MemoryStream())
                        {
                            e.Open().CopyTo(ms);
                            parts[e.FullName] = new ZipPart { IsXml = false, Raw = ms.ToArray() };
                        }
                    }
                }
            }

            // 2) 解析 workbook.xml：工作表名 -> rId -> 目标部件路径
            if (!parts.TryGetValue("xl/workbook.xml", out var wbPart) || wbPart.Doc == null)
                throw new InvalidDataException("不是有效的 xlsx 文件：缺少 workbook.xml");
            var sheetEls = wbPart.Doc.Root.Element(XNS + "sheets")?.Elements(XNS + "sheet").ToList()
                           ?? new List<XElement>();
            string targetRid = null;
            foreach (var s in sheetEls)
            {
                if (string.Equals(s.Attribute("name")?.Value, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    targetRid = s.Attribute(RNS + "id")?.Value;
                    break;
                }
            }
            if (targetRid == null) throw new InvalidDataException($"工作簿中找不到名为「{sheetName}」的工作表");

            string targetPart = null;
            if (parts.TryGetValue("xl/_rels/workbook.xml.rels", out var relsPart) && relsPart.Doc != null)
            {
                foreach (var rel in relsPart.Doc.Root.Elements(PNS + "Relationship"))
                {
                    if (rel.Attribute("Id")?.Value == targetRid)
                    {
                        var t = rel.Attribute("Target")?.Value;
                        targetPart = t.StartsWith("/") ? t.Substring(1) : "xl/" + t;
                        break;
                    }
                }
            }
            if (targetPart == null || !parts.TryGetValue(targetPart, out var sheetPart) || sheetPart.Doc == null)
                throw new InvalidDataException("找不到目标工作表部件");

            // 3) 准备样式：确保有一个加粗样式（供表头在无样式时套用）
            ZipPart styles = default;
            foreach (var kv in parts)
            {
                if (kv.Key.EndsWith("styles.xml", StringComparison.OrdinalIgnoreCase) && kv.Value.IsXml)
                {
                    styles = kv.Value;
                    break;
                }
            }
            int boldStyleIndex = 0;
            if (styles.Doc != null)
                boldStyleIndex = EnsureBoldStyle(styles.Doc);

            // 4) 读取原 sheetData 中每个单元格的样式索引（按 "A1" 引用做映射），以便重建时沿用，
            //    最大程度保留用户原有的表头/数据单元格排版（加粗、底色、对齐、字号等）。
            var sdOld = sheetPart.Doc.Root.Element(XNS + "sheetData");
            var origStyleByRef = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sdOld != null)
            {
                foreach (var rowEl in sdOld.Elements(XNS + "row"))
                    foreach (var c in rowEl.Elements(XNS + "c"))
                    {
                        var sAttr = c.Attribute("s")?.Value;
                        var refA = c.Attribute("r")?.Value;
                        if (refA != null && sAttr != null && sAttr != "0")
                            origStyleByRef[refA] = sAttr;
                    }
            }
            // 表头首格原有样式（用于「原表头无任何样式时」才套加粗）
            int origHeaderStyle = -1;
            var firstCellRef = sdOld?.Elements(XNS + "row").FirstOrDefault()?
                .Elements(XNS + "c").FirstOrDefault()?.Attribute("r")?.Value;
            if (firstCellRef != null && origStyleByRef.TryGetValue(firstCellRef, out var hs)) int.TryParse(hs, out origHeaderStyle);
            int headerStyle = origHeaderStyle >= 0 ? origHeaderStyle : boldStyleIndex;

            // 5) 重建 sheetData（保留 cols / sheetViews / 冻结窗格等其余子元素与各自位置）
            int colCount = rows[0].Length;
            var newSheetData = new XElement(XNS + "sheetData");
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                int len = Math.Max(row.Length, colCount);
                var cells = new List<XElement>();
                for (int c = 0; c < len; c++)
                {
                    var val = c < row.Length ? (row[c] ?? string.Empty) : string.Empty;
                    var refAddr = ColumnLetter(c) + (r + 1);
                    XElement cEl;
                    if (IsNumericLiteral(val))
                        cEl = new XElement(XNS + "c", new XAttribute("r", refAddr),
                            new XElement(XNS + "v", val));
                    else
                        cEl = new XElement(XNS + "c", new XAttribute("r", refAddr),
                            new XAttribute("t", "inlineStr"),
                            new XElement(XNS + "is", new XElement(XNS + "t", new XAttribute(XmlNs + "space", "preserve"), val)));
                    // 沿用原单元格样式（数据格保留用户排版；表头格优先原样式、无则加粗）
                    if (origStyleByRef.TryGetValue(refAddr, out var st))
                        cEl.Add(new XAttribute("s", st));
                    else if (r == 0)
                        cEl.Add(new XAttribute("s", headerStyle));
                    cells.Add(cEl);
                }
                newSheetData.Add(new XElement(XNS + "row", new XAttribute("r", r + 1), cells));
            }

            var sheetDoc = sheetPart.Doc;
            // 保留 sheetData 在原工作表里的位置：OOXML 要求特定子元素顺序，直接 Add 到末尾会破坏顺序、
            // 导致 Excel 打开提示「修复」。以原 sheetData 之后的第一个兄弟元素为锚点插回原位。
            var anchor = sdOld != null ? sdOld.ElementsAfterSelf().FirstOrDefault() : null;
            sdOld?.Remove();
            // 列宽：缺失才补默认列宽（保留用户原有列宽）
            if (sheetDoc.Root.Element(XNS + "cols") == null)
            {
                var cols = new XElement(XNS + "cols",
                    new XElement(XNS + "col", new XAttribute("min", 1), new XAttribute("max", colCount),
                        new XAttribute("width", 16), new XAttribute("customWidth", 1)));
                if (anchor != null) { anchor.AddBeforeSelf(cols); anchor.AddBeforeSelf(newSheetData); }
                else { sheetDoc.Root.Add(cols); sheetDoc.Root.Add(newSheetData); }
            }
            else
            {
                if (anchor != null) anchor.AddBeforeSelf(newSheetData);
                else sheetDoc.Root.Add(newSheetData);
            }
            // 移除过期的 dimension（Excel 打开时会重算）
            sheetDoc.Root.Element(XNS + "dimension")?.Remove();

            // 6) 把整个 zip 写回（其余部件原样，目标表与 styles 用修改后的 XDocument）
            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = File.Create(tmp))
            using (var outZip = new ZipArchive(fs, ZipArchiveMode.Create, false))
            {
                foreach (var kv in parts)
                {
                    var entry = outZip.CreateEntry(kv.Key);
                    using (var s = entry.Open())
                    {
                        if (kv.Value.IsXml) kv.Value.Doc.Save(s);
                        else s.Write(kv.Value.Raw, 0, kv.Value.Raw.Length);
                    }
                }
            }
            File.Replace(tmp, path, null);
        }

        /// <summary>确保样式表中存在一个「加粗」单元格样式，返回其索引（已存在则复用）。</summary>
        private static int EnsureBoldStyle(XDocument styles)
        {
            var fonts = styles.Root.Element(XNS + "fonts");
            var cellXfs = styles.Root.Element(XNS + "cellXfs");
            if (fonts == null || cellXfs == null) return 0;

            int fontCount = int.Parse(fonts.Attribute("count").Value);
            int boldFontId = -1, idx = 0;
            foreach (var f in fonts.Elements(XNS + "font"))
            {
                if (f.Element(XNS + "b") != null) { boldFontId = idx; break; }
                idx++;
            }
            if (boldFontId < 0)
            {
                var baseFont = fonts.Elements(XNS + "font").FirstOrDefault()
                               ?? new XElement(XNS + "font");
                var newFont = new XElement(baseFont); // 克隆（保留原字号 / 字体名）
                newFont.Add(new XElement(XNS + "b"));
                fonts.Add(newFont);
                boldFontId = fontCount;
                fonts.Attribute("count").Value = (fontCount + 1).ToString();
            }

            int xfCount = int.Parse(cellXfs.Attribute("count").Value);
            int boldXf = -1, i2 = 0;
            foreach (var xf in cellXfs.Elements(XNS + "xf"))
            {
                if ((xf.Attribute("fontId")?.Value == boldFontId.ToString())
                    && (xf.Attribute("fillId")?.Value == "0")
                    && (xf.Attribute("borderId")?.Value == "0"))
                {
                    boldXf = i2;
                    break;
                }
                i2++;
            }
            if (boldXf < 0)
            {
                cellXfs.Add(new XElement(XNS + "xf",
                    new XAttribute("numFmtId", 0), new XAttribute("fontId", boldFontId),
                    new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0)));
                cellXfs.Attribute("count").Value = (xfCount + 1).ToString();
                boldXf = xfCount;
            }
            return boldXf;
        }

        private static XDocument BuildWorksheet(List<string[]> rows)
        {
            var sheetData = new XElement(XNS + "sheetData");
            for (int r = 0; r < rows.Count; r++)
            {
                var cells = new List<XElement>();
                var row = rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    var val = row[c] ?? string.Empty;
                    var refAddr = ColumnLetter(c) + (r + 1);
                    if (IsNumericLiteral(val))
                        cells.Add(new XElement(XNS + "c", new XAttribute("r", refAddr),
                            new XElement(XNS + "v", val)));
                    else
                        cells.Add(new XElement(XNS + "c", new XAttribute("r", refAddr),
                            new XAttribute("t", "inlineStr"),
                            new XElement(XNS + "is",
                                new XElement(XNS + "t", new XAttribute(XmlNs + "space", "preserve"), val))));
                }
                sheetData.Add(new XElement(XNS + "row", new XAttribute("r", r + 1), cells));
            }
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(XNS + "worksheet", sheetData));
        }

        private static XDocument BuildStyles()
        {
            // 规范最小样式表（与 Excel 默认结构一致）：保证 Excel / WPS / LibreOffice / openpyxl 均兼容。
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(XNS + "styleSheet",
                    new XElement(XNS + "fonts", new XAttribute("count", 1),
                        new XElement(XNS + "font",
                            new XElement(XNS + "sz", new XAttribute("val", 11)),
                            new XElement(XNS + "name", new XAttribute("val", "Calibri")))),
                    new XElement(XNS + "fills", new XAttribute("count", 2),
                        new XElement(XNS + "fill", new XElement(XNS + "patternFill", new XAttribute("patternType", "none"))),
                        new XElement(XNS + "fill", new XElement(XNS + "patternFill", new XAttribute("patternType", "gray125")))),
                    new XElement(XNS + "borders", new XAttribute("count", 1), new XElement(XNS + "border")),
                    new XElement(XNS + "cellStyleXfs", new XAttribute("count", 1),
                        new XElement(XNS + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0),
                            new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
                    new XElement(XNS + "cellXfs", new XAttribute("count", 1),
                        new XElement(XNS + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0),
                            new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0))),
                    new XElement(XNS + "cellStyles", new XAttribute("count", 1),
                        new XElement(XNS + "cellStyle", new XAttribute("name", "Normal"),
                            new XAttribute("xfId", 0), new XAttribute("builtinId", 0)))));
        }

        // ══ 读 ══

        /// <summary>读取 .xlsx 文件，返回按工作簿顺序排列的工作表列表。</summary>
        public static List<XlsSheet> ReadWorkbook(string path)
        {
            var result = new List<XlsSheet>();
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read, false))
            {
                var wbEntry = zip.GetEntry("xl/workbook.xml");
                if (wbEntry == null) throw new InvalidDataException("不是有效的 xlsx 文件：缺少 workbook.xml");

                // 1) 解析工作表顺序与 rId
                var wbDoc = XDocument.Load(wbEntry.Open());
                var sheetEls = wbDoc.Root.Element(XNS + "sheets")?.Elements(XNS + "sheet").ToList()
                               ?? new List<XElement>();
                var sheetRefs = new List<(string name, string rid)>();
                foreach (var s in sheetEls)
                {
                    var name = s.Attribute("name")?.Value ?? "Sheet";
                    var rid = s.Attribute(RNS + "id")?.Value ?? string.Empty;
                    sheetRefs.Add((name, rid));
                }

                // 0.5) 读取共享字符串表：Excel / WPS 重新保存时往往把 inlineStr 改写为 sharedStrings，
                // 不读此表会导致所有文本单元格读成 "[s0]" 占位，导入静默失败。兼容富文本 <r><t> 形式。
                var sharedStrings = ReadSharedStrings(zip);

                // 2) 解析 workbook rels：rId -> 目标部件
                var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
                var relMap = new Dictionary<string, string>();
                if (relsEntry != null)
                {
                    var relDoc = XDocument.Load(relsEntry.Open());
                    foreach (var rel in relDoc.Root.Elements(PNS + "Relationship"))
                    {
                        var id = rel.Attribute("Id")?.Value;
                        var target = rel.Attribute("Target")?.Value;
                        if (id != null && target != null) relMap[id] = target;
                    }
                }

                // 3) 逐个读取工作表
                foreach (var (name, rid) in sheetRefs)
                {
                    if (!relMap.TryGetValue(rid, out var target)) continue;
                    var full = target.StartsWith("/") ? target.Substring(1) : "xl/" + target;
                    var entry = zip.GetEntry(full);
                    if (entry == null) continue;
                    result.Add(new XlsSheet { Name = name, Rows = ReadSheet(entry.Open(), sharedStrings) });
                }
            }
            return result;
        }

        private static List<string[]> ReadSheet(Stream stream, List<string> sharedStrings)
        {
            var doc = XDocument.Load(stream);
            var rowsOut = new List<string[]>();
            var sd = doc.Root?.Element(XNS + "sheetData");
            if (sd == null) return rowsOut;

            foreach (var rowEl in sd.Elements(XNS + "row"))
            {
                var cells = new Dictionary<int, string>();
                int maxCol = -1;
                foreach (var c in rowEl.Elements(XNS + "c"))
                {
                    var refA = c.Attribute("r")?.Value ?? string.Empty;
                    int col = ParseColumn(refA);
                    string val = ReadCell(c, sharedStrings);
                    cells[col] = val;
                    if (col > maxCol) maxCol = col;
                }
                var arr = new string[maxCol + 1];
                for (int i = 0; i <= maxCol; i++) arr[i] = cells.TryGetValue(i, out var v) ? v : string.Empty;
                rowsOut.Add(arr);
            }
            return rowsOut;
        }

        private static string ReadCell(XElement c, List<string> sharedStrings)
        {
            var t = c.Attribute("t")?.Value;
            if (t == "inlineStr")
            {
                var isEl = c.Element(XNS + "is");
                var tx = isEl?.Element(XNS + "t");
                return tx?.Value ?? string.Empty;
            }
            if (t == "s")
            {
                // 共享字符串（Excel/WPS 另存后常见）：按索引查 sharedStrings 表
                var v = c.Element(XNS + "v")?.Value;
                if (v != null && int.TryParse(v, out var idx) && idx >= 0 && idx < sharedStrings.Count)
                    return sharedStrings[idx];
                return string.Empty;
            }
            // 数值 / 公式结果
            var vv = c.Element(XNS + "v")?.Value;
            return vv ?? string.Empty;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;
            try
            {
                var doc = XDocument.Load(entry.Open());
                if (doc.Root == null) return list;
                foreach (var si in doc.Root.Elements(XNS + "si"))
                {
                    // 纯文本 <si><t>..</t></si> 与富文本 <si><r><t>..</t></r>.. 统一拼接所有 <t>
                    var parts = si.Descendants(XNS + "t").Select(t => t.Value).ToArray();
                    list.Add(string.Concat(parts));
                }
            }
            catch
            {
                // 读不到共享字符串表时退回 inlineStr 逻辑，不影响其它单元格
            }
            return list;
        }

        // ══ 工具 ══

        private static bool IsNumericLiteral(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out _);
        }

        private static string ColumnLetter(int zeroBasedCol)
        {
            int c = zeroBasedCol;
            string s = string.Empty;
            while (c >= 0)
            {
                s = (char)('A' + (c % 26)) + s;
                c = c / 26 - 1;
            }
            return s;
        }

        private static int ParseColumn(string refAddr)
        {
            int i = 0;
            while (i < refAddr.Length && char.IsLetter(refAddr[i])) i++;
            string letters = refAddr.Substring(0, i);
            int n = 0;
            foreach (char ch in letters) n = n * 26 + (ch - 'A' + 1);
            return n - 1;
        }

        private static List<string> MakeSafeSheetNames(List<XlsSheet> sheets)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outNames = new List<string>();
            foreach (var s in sheets)
            {
                var name = s.Name ?? "Sheet";
                var sb = new System.Text.StringBuilder();
                foreach (var ch in name)
                {
                    if (":\\/?*[]".IndexOf(ch) >= 0) sb.Append('_');
                    else sb.Append(ch);
                }
                name = sb.ToString().Trim();
                if (name.Length > 31) name = name.Substring(0, 31);
                if (string.IsNullOrEmpty(name)) name = "Sheet";
                // 去重
                string candidate = name;
                int k = 1;
                while (used.Contains(candidate))
                {
                    string suffix = k.ToString();
                    int cut = Math.Min(name.Length, 31 - suffix.Length);
                    candidate = name.Substring(0, cut) + suffix;
                    k++;
                }
                used.Add(candidate);
                outNames.Add(candidate);
            }
            return outNames;
        }

        private static void AddEntry(ZipArchive zip, string name, XDocument doc)
        {
            var entry = zip.CreateEntry(name);
            using (var s = entry.Open())
                doc.Save(s);
        }
    }
}
