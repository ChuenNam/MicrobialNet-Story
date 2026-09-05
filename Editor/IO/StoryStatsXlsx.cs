using System.Collections.Generic;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 剧情统计报表 Excel：导出专用（统计是只读报表，不支持导入回填）。
    /// 三张工作表：
    /// - 概览：指标 / 数值 两列，含节点、连线、选项、变量、可达/不可达、死路、文本体量等。
    /// - 类型分布：节点类型 / 数量。
    /// - 问题节点：类别（不可达 / 死路）/ 节点显示名，便于排查断头路。
    /// </summary>
    public static class StoryStatsXlsx
    {
        public const string SheetOverview = "概览";
        public const string SheetByType = "类型分布";
        public const string SheetIssues = "问题节点";

        public static List<XlsSheet> BuildSheets(StoryStatsResult r)
        {
            var sheets = new List<XlsSheet>();
            sheets.Add(BuildOverview(r));
            sheets.Add(BuildByType(r));
            sheets.Add(BuildIssues(r));
            return sheets;
        }

        private static XlsSheet BuildOverview(StoryStatsResult r)
        {
            var s = new XlsSheet { Name = SheetOverview };
            s.Rows.Add(new[] { "指标", "数值" });
            s.Rows.Add(new[] { "总节点数", r.totalNodes.ToString() });
            s.Rows.Add(new[] { "连线数", r.edgeCount.ToString() });
            s.Rows.Add(new[] { "选项数", r.choiceCount.ToString() });
            s.Rows.Add(new[] { "变量数", r.variableCount.ToString() });
            s.Rows.Add(new[] { "角色引用数", r.characterRefCount.ToString() });
            s.Rows.Add(new[] { "可达节点数", r.reachableCount.ToString() });
            s.Rows.Add(new[] { "不可达节点数", r.unreachableCount.ToString() });
            s.Rows.Add(new[] { "死路节点数", r.deadEndCount.ToString() });
            s.Rows.Add(new[] { "文本字符数", r.textChars.ToString() });
            return s;
        }

        private static XlsSheet BuildByType(StoryStatsResult r)
        {
            var s = new XlsSheet { Name = SheetByType };
            s.Rows.Add(new[] { "节点类型", "数量" });
            foreach (var kv in r.byType)
                s.Rows.Add(new[] { kv.Key, kv.Value.ToString() });
            return s;
        }

        private static XlsSheet BuildIssues(StoryStatsResult r)
        {
            var s = new XlsSheet { Name = SheetIssues };
            s.Rows.Add(new[] { "类别", "节点" });
            foreach (var t in r.unreachableTitles)
                s.Rows.Add(new[] { "不可达", t });
            foreach (var t in r.deadEndTitles)
                s.Rows.Add(new[] { "死路", t });
            return s;
        }
    }
}
