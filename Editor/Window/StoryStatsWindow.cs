using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Window
{
    /// <summary>剧情统计面板：展示节点/连线/选项/变量计数、类型分布、可达-不可达、死路、文本体量。</summary>
    internal sealed class StoryStatsWindow : EditorWindow
    {
        private StoryGraphModel _model;

        internal static void Show(StoryGraphModel model)
        {
            var w = GetWindow<StoryStatsWindow>("剧情统计");
            w._model = model;
            w.Rebuild();
        }

        private void OnEnable() => Rebuild();

        private void Rebuild()
        {
            rootVisualElement.Clear();
            StoryStyle.Apply(rootVisualElement);
            rootVisualElement.AddToClassList("stats-root");

            if (_model == null)
            {
                rootVisualElement.Add(new Label("未加载资产"));
                return;
            }

            var r = StoryStats.Compute(_model);
            var root = rootVisualElement;

            AddRow(root, "节点总数", r.totalNodes.ToString());
            AddRow(root, "连线总数", r.edgeCount.ToString());
            AddRow(root, "玩家选项数", r.choiceCount.ToString());
            AddRow(root, "变量定义数", r.variableCount.ToString());
            AddRow(root, "角色引用数", r.characterRefCount.ToString());
            AddRow(root, "可达节点", r.reachableCount.ToString());
            AddRow(root, "不可达节点", r.unreachableCount.ToString(), r.unreachableCount > 0 ? "#E0B341" : null);
            AddRow(root, "死路节点", r.deadEndCount.ToString(), r.deadEndCount > 0 ? "#E0533D" : null);
            AddRow(root, "文本总量(字符)", r.textChars.ToString());

            var typeTitle = new Label("节点类型分布") { name = "stats-type-title" };
            typeTitle.AddToClassList("stats-section-title");
            root.Add(typeTitle);
            var typePane = new VisualElement { name = "stats-type-pane" };
            typePane.AddToClassList("stats-indent");
            foreach (var kv in r.byType.OrderBy(kv => kv.Key))
                AddRow(typePane, kv.Key, kv.Value.ToString());
            root.Add(typePane);

            if (r.unreachableTitles.Count > 0)
            {
                var upTitle = new Label("不可达节点清单") { name = "stats-up-title" };
                upTitle.AddToClassList("stats-section-title");
                root.Add(upTitle);
                var up = new VisualElement { name = "stats-up-pane" };
                up.AddToClassList("stats-indent");
                foreach (var t in r.unreachableTitles) up.Add(new Label("• " + t));
                root.Add(up);
            }

            if (r.deadEndTitles.Count > 0)
            {
                var dpTitle = new Label("死路节点清单") { name = "stats-dp-title" };
                dpTitle.AddToClassList("stats-section-title");
                root.Add(dpTitle);
                var dp = new VisualElement { name = "stats-dp-pane" };
                dp.AddToClassList("stats-indent");
                foreach (var t in r.deadEndTitles) dp.Add(new Label("• " + t));
                root.Add(dp);
            }
        }

        private static void AddRow(VisualElement parent, string k, string v, string color = null)
        {
            var row = new VisualElement { name = "stats-row" };
            row.AddToClassList("stats-row");
            var kl = new Label(k) { name = "stats-key" };
            kl.AddToClassList("stats-key");
            var vl = new Label(v);
            if (color != null && ColorUtility.TryParseHtmlString(color, out var c))
                vl.style.color = new StyleColor(c);
            row.Add(kl);
            row.Add(vl);
            parent.Add(row);
        }
    }
}
