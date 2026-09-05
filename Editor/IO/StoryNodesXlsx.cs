using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 节点属性批量表 Excel：把每个节点的（含基类）标量字段与列表元素的标量字段拍平为长表，
    /// 列：NodeId, NodeType, Field, Value。导出可在 Excel 中批量编辑，导入按 NodeId 把 Value 写回对应字段。
    ///
    /// 路径规则（与 ReflectionUtil 一致）：标量字段直接用字段名；列表元素用 "listName[index].elemField"，
    /// 如 "options[0].text"、"clauses[2].variableId"。optionId 等稳定 ID 不参与导出，避免误改破坏连线。
    /// 导入采用不变文化（InvariantCulture）做数值/枚举转换，跨机器安全。
    /// </summary>
    public static class StoryNodesXlsx
    {
        public const string SheetName = "节点属性";
        private static readonly BindingFlags BF = BindingFlags.Public | BindingFlags.Instance;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly HashSet<Type> ScalarTypes = new HashSet<Type>
        {
            typeof(string), typeof(bool), typeof(int), typeof(long),
            typeof(float), typeof(double), typeof(Vector2),
        };

        /// <summary>构建节点属性扁平表（表头 + 数据行）。</summary>
        public static XlsSheet BuildSheet(StoryGraphAsset asset)
        {
            var sheet = new XlsSheet { Name = SheetName };
            sheet.Rows.Add(new[] { "NodeId", "NodeType", "Field", "Value" });
            if (asset == null) return sheet;

            foreach (var node in asset.nodes)
            {
                if (node == null) continue;
                string typeName = NodeRegistry.GetAttr(node.GetType())?.Title ?? node.GetType().Name;

                foreach (var f in node.GetType().GetFields(BF))
                {
                    if (f.Name == "id") continue; // id 是匹配键，不在表中编辑
                    var val = f.GetValue(node);
                    if (IsScalar(f.FieldType))
                    {
                        sheet.Rows.Add(new[] { node.id, typeName, f.Name, Serialize(val, f.FieldType) });
                    }
                    else if (IsListOfClass(f.FieldType, out var elemType))
                    {
                        var list = val as IList;
                        if (list == null) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var elem = list[i];
                            if (elem == null) continue;
                            foreach (var ef in elemType.GetFields(BF))
                            {
                                if (ef.Name == "optionId") continue; // 稳定 ID，不参与批量编辑
                                if (!IsScalar(ef.FieldType)) continue;
                                var eval = ef.GetValue(elem);
                                sheet.Rows.Add(new[] { node.id, typeName,
                                    $"{f.Name}[{i}].{ef.Name}", Serialize(eval, ef.FieldType) });
                            }
                        }
                    }
                }
            }
            return sheet;
        }

        /// <summary>按 NodeId 把 Value 写回对应字段（原地修改）。带 Undo。返回诊断信息。</summary>
        public static ImportReport ImportFromRows(StoryGraphAsset asset, List<string[]> rows)
        {
            var report = new ImportReport();
            if (asset == null || rows == null || rows.Count < 2)
            {
                report.message = "文件行数不足（需要表头 + 至少 1 行数据）。";
                return report;
            }

            // 表头定位列：去首尾空白、大小写不敏感
            var header = rows[0];
            int cId = FindCol(header, "NodeId");
            int cField = FindCol(header, "Field");
            int cVal = FindCol(header, "Value");
            report.hasKeyCol = cId >= 0;
            report.hasValueCol = cVal >= 0;
            if (cId < 0 || cField < 0 || cVal < 0)
            {
                report.message = $"未找到「NodeId/Field/Value」列。表头为：{string.Join(",", header)}";
                return report;
            }

            Undo.RecordObject(asset, "导入节点属性 Excel");
            int changed = 0, wellFormed = 0, resolved = 0;

            // 按 NodeId 分组，减少重复查找
            var byNode = new Dictionary<string, List<string[]>>();
            for (int r = 1; r < rows.Count; r++)
            {
                var fields = rows[r];
                if (fields.Length <= cId) continue;
                string id = fields[cId];
                if (string.IsNullOrEmpty(id)) continue;
                wellFormed++;
                if (!byNode.TryGetValue(id, out var list)) { list = new List<string[]>(); byNode[id] = list; }
                list.Add(fields);
            }

            foreach (var kv in byNode)
            {
                var node = asset.GetNode(kv.Key);
                if (node == null) continue;
                resolved++;
                foreach (var fields in kv.Value)
                {
                    string path = fields.Length > cField ? fields[cField] : string.Empty;
                    string value = fields.Length > cVal ? fields[cVal] : string.Empty;
                    if (string.IsNullOrEmpty(path) || path == "id") continue;
                    if (SetByPath(node, path, value)) changed++;
                }
            }

            report.dataRows = rows.Count - 1;
            report.wellFormed = wellFormed;
            report.resolved = resolved;
            report.changed = changed;
            if (changed > 0) EditorUtility.SetDirty(asset);

            if (changed == 0)
            {
                if (wellFormed == 0)
                    report.message = $"数据行 {report.dataRows} 条，但没有有效的 NodeId。可能表头错位或文件不是本工具导出的节点属性表。";
                else
                    report.message = $"数据行 {report.dataRows} 条、含 NodeId {wellFormed} 条，但 0 条命中当前资产节点。最常见原因：导入的资产与导出时不是同一份（节点 ID 对不上），或导出后图被改动/重建导致 ID 变化。请确认「导出」与「导入」作用于同一份资产。";
            }
            else
            {
                report.message = $"已写回 {changed} 条（数据行 {report.dataRows}，命中节点 {resolved}）。";
            }
            Debug.Log($"[Story] 节点属性导入：{report.message}");
            return report;
        }

        private static int FindCol(string[] header, string col)
        {
            for (int i = 0; i < header.Length; i++)
                if (!string.IsNullOrEmpty(header[i]) && header[i].Trim().Equals(col, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        // ── 内部 ──

        private static bool SetByPath(StoryNodeData node, string path, string raw)
        {
            var segs = path.Split('.');
            object cur = node;
            FieldInfo leafField = null;
            object leafParent = null;

            for (int i = 0; i < segs.Length; i++)
            {
                ParseSeg(segs[i], out var name, out var idx);
                var f = cur.GetType().GetField(name, BF);
                if (f == null) return false;
                if (i == segs.Length - 1) { leafField = f; leafParent = cur; break; }
                cur = f.GetValue(cur);
                if (idx >= 0 && cur is IList list)
                {
                    if (idx < 0 || idx >= list.Count) return false;
                    cur = list[idx];
                }
            }
            if (leafField == null || leafParent == null) return false;

            var converted = ConvertScalar(raw, leafField.FieldType);
            if (converted == null)
            {
                if (leafField.FieldType.IsValueType) return false; // 值类型无法置空，跳过
                try { leafField.SetValue(leafParent, null); return true; }
                catch { return false; }
            }
            try { leafField.SetValue(leafParent, converted); return true; }
            catch { return false; }
        }

        private static object ConvertScalar(string raw, Type type)
        {
            if (type == typeof(string)) return raw ?? string.Empty;
            if (type.IsEnum)
                return Enum.TryParse(type, raw ?? "", out var e) ? e : (object)null;
            if (type == typeof(bool))
                return bool.TryParse(raw ?? "", out var b) ? b : (object)null;
            if (type == typeof(int))
                return int.TryParse(raw, NumberStyles.Integer, Inv, out var i) ? i : (object)null;
            if (type == typeof(long))
                return long.TryParse(raw, NumberStyles.Integer, Inv, out var l) ? l : (object)null;
            if (type == typeof(float))
                return float.TryParse(raw, NumberStyles.Float, Inv, out var f) ? f : (object)null;
            if (type == typeof(double))
                return double.TryParse(raw, NumberStyles.Float, Inv, out var d) ? d : (object)null;
            if (type == typeof(Vector2))
            {
                if (string.IsNullOrEmpty(raw)) return null;
                var parts = raw.Split(';');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], NumberStyles.Float, Inv, out var x) &&
                    float.TryParse(parts[1], NumberStyles.Float, Inv, out var y))
                    return new Vector2(x, y);
                return null;
            }
            return raw; // 未知类型按原串存储（通常不会发生）
        }

        private static string Serialize(object val, Type type)
        {
            if (val == null) return string.Empty;
            if (type == typeof(string)) return (string)val;
            if (type.IsEnum) return val.ToString();
            if (type == typeof(bool)) return ((bool)val).ToString();
            if (type == typeof(float)) return ((float)val).ToString(Inv);
            if (type == typeof(double)) return ((double)val).ToString(Inv);
            if (type == typeof(int)) return ((int)val).ToString(Inv);
            if (type == typeof(long)) return ((long)val).ToString(Inv);
            if (type == typeof(Vector2)) { var v = (Vector2)val; return v.x.ToString(Inv) + ";" + v.y.ToString(Inv); }
            return val.ToString();
        }

        private static bool IsScalar(Type t) => ScalarTypes.Contains(t) || t.IsEnum;

        private static bool IsListOfClass(Type t, out Type elemType)
        {
            elemType = null;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var e = t.GetGenericArguments()[0];
                if (e.IsClass && e != typeof(string)) { elemType = e; return true; }
            }
            return false;
        }

        private static void ParseSeg(string seg, out string name, out int index)
        {
            int b = seg.IndexOf('[');
            if (b >= 0 && seg.EndsWith("]"))
            {
                name = seg.Substring(0, b);
                index = int.Parse(seg.Substring(b + 1, seg.Length - b - 2));
            }
            else { name = seg; index = -1; }
        }

        private static int IndexOf(string[] arr, string col)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == col) return i;
            return -1;
        }
    }
}

