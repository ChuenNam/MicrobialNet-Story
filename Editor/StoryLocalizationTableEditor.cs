using System.Collections.Generic;
using System.IO;
using System.Text;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// <see cref="StoryLocalizationTable"/>（本地化主表 SO）的 Inspector 扩展：
    /// 在默认可编辑字段下方增加两个围绕「同目录翻译 CSV」的按钮。
    /// 表格文件固定为 SO 同目录、同名 + <c>.l10n.csv</c>（如 <c>StoryGraph.l10ntable.asset</c> → <c>StoryGraph.l10n.csv</c>），
    /// 直接定位，不弹文件选择框。
    /// <list type="bullet">
    ///   <item><b>同步到表格</b>：先把绑定本表的剧情图里「新增的节点条目」增量拉进主表（SyncFromGraph），
    ///   再<b>增量</b>合并导出到同目录 CSV（已有译文行保留，仅追加缺失 key）。</item>
    ///   <item><b>从表格更新</b>：从同目录 CSV 按 key 把译文合并回主表（ImportCsvToTable，非空单元格才覆盖）；
    ///   若同目录无 CSV 则提示先「同步到表格」生成。</item>
    /// </list>
    /// 主表 SO 作为项目内唯一真相源居中。
    /// </summary>
    [CustomEditor(typeof(StoryLocalizationTable))]
    public sealed class StoryLocalizationTableEditor : UnityEditor.Editor
    {
        private string _status = string.Empty;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("本地化表格同步（外部 CSV）", EditorStyles.boldLabel);

            var table = (StoryLocalizationTable)target;

            if (GUILayout.Button("同步到表格（导出新增节点条目 → CSV）"))
                SyncToCsv(table);

            if (GUILayout.Button("从表格更新（从 CSV 合并译文 → 主表）"))
                UpdateFromCsv(table);

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        private static string DefaultCsvPath(StoryLocalizationTable table)
        {
            string soPath = AssetDatabase.GetAssetPath(table);
            if (string.IsNullOrEmpty(soPath)) return null;
            string dir = Path.GetDirectoryName(soPath);
            string name = Path.GetFileNameWithoutExtension(soPath);
            return Path.Combine(dir, name + ".l10n.csv");
        }

        /// <summary>判断 IOException 是否由 Windows 文件锁（Excel 等占用）引起：ERROR_SHARING_VIOLATION(0x20) / ERROR_LOCK_VIOLATION(0x21)。</summary>
        private static bool IsSharingViolation(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return code == 0x20 || code == 0x21;
        }

        /// <summary>找出所有 localizationTable 指向本表的剧情图（反向查找），用于「同步到表格」先拉取新增节点条目。</summary>
        private static List<StoryGraphAsset> FindGraphsUsingTable(StoryLocalizationTable table)
        {
            var result = new List<StoryGraphAsset>();
            var guids = AssetDatabase.FindAssets("t:StoryGraphAsset");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var asset = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(path);
                if (asset != null && asset.localizationTable == table) result.Add(asset);
            }
            return result;
        }

        private void SyncToCsv(StoryLocalizationTable table)
        {
            // 1) 先从绑定本表的剧情图把新增节点条目增量拉进主表 SO
            int fromGraph = 0;
            var graphs = FindGraphsUsingTable(table);
            if (graphs.Count > 0)
            {
                foreach (var g in graphs)
                    fromGraph += StoryLocalizationCsv.SyncFromGraph(g, table);
                if (fromGraph > 0) EditorUtility.SetDirty(table);
            }

            // 2) 直接定位 SO 同目录、同名的 .l10n.csv，增量合并导出（保留已有译文行，仅追加缺失 key）
            string csvPath = DefaultCsvPath(table);
            if (string.IsNullOrEmpty(csvPath)) { _status = "无法定位资源路径（表尚未保存？），导出取消。"; return; }

            string existing = null;
            if (File.Exists(csvPath))
            {
                try { existing = File.ReadAllText(csvPath); }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                    _status = "读取失败：CSV 正被其他程序（如 Excel）占用，无法增量合并。\n请先在 Excel 中关闭该文件，再点「同步到表格」。\n" + csvPath;
                    return;
                }
            }
            try
            {
                File.WriteAllText(csvPath, StoryLocalizationCsv.SyncToCsv(table, existing), Encoding.UTF8);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                // Windows 文件锁：Excel 打开 CSV 时拒绝其他进程写入，无法在 Excel 占用期间更新文件。
                _status = "写入失败：CSV 正被其他程序（如 Excel）占用，无法写入。\n请先在 Excel 中关闭该文件，再点「同步到表格」（关闭后 Excel 会提示重新加载外部变更）。\n" + csvPath;
                return;
            }
            AssetDatabase.Refresh();

            int total = table.entries != null ? table.entries.Count : 0;
            _status = graphs.Count > 0
                ? $"已从图同步 {fromGraph} 条新 key，并增量导出到同目录 CSV（主表共 {total} 条）：{Path.GetFileName(csvPath)}"
                : $"本表未绑定任何剧情图，已直接导出主表现有 {total} 条到同目录 CSV：{Path.GetFileName(csvPath)}";
        }

        private void UpdateFromCsv(StoryLocalizationTable table)
        {
            string csvPath = DefaultCsvPath(table);
            if (string.IsNullOrEmpty(csvPath)) { _status = "无法定位资源路径（表尚未保存？），更新取消。"; return; }
            if (!File.Exists(csvPath))
            {
                _status = "同目录下未找到表格文件：" + csvPath + "\n请先点「同步到表格」生成 CSV。";
                return;
            }

            string csv;
            try { csv = File.ReadAllText(csvPath); }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                _status = "读取失败：CSV 正被其他程序（如 Excel）占用，无法读取。\n请先关闭该文件再点「从表格更新」。\n" + csvPath;
                return;
            }
            var rep = StoryLocalizationCsv.ImportCsvToTable(csv, table);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            _status = "从同目录表格更新：" + rep.message;
        }
    }
}
