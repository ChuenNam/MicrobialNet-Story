using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using MicrobialNet.Story.EditorTools.Commands;
using MicrobialNet.Story.EditorTools.Window;
using UnityEditor;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Inspector
{
    /// <summary>
    /// 属性面板自动生成器（P4/L2 后的组装入口）。扫描节点的 [StoryField]（及配套特性）生成控件面板，
    /// 任何改动都经 EditFieldCommand / MultiEditFieldCommand 回写模型。编辑器主干无需为每个节点类型写面板。
    ///
    /// 职责分工（L2 拆分后本类只做「编排」）：
    ///  - <see cref="FieldMetaCache"/>：反射元数据（类型级缓存，L0）；
    ///  - <see cref="FieldPanelLogic"/>：可单测的纯逻辑（混合态/表绑定路由/变量归一化，L1）；
    ///  - <see cref="FieldWidgetFactory"/>：控件创建与交互回调注册（L2）；
    ///  - <see cref="TableBoundEditor"/>：表格驱动节点的徽标头部/行维护/写回壳（L2）；
    ///  - 本类：字段迭代、显隐门控、列表展开（选项/条件折叠）、行组装、apply 路由（单选/多选/表绑定）。
    ///
    /// 多选语义参照 Unity 原生「同时选中多个物体改组件数值」：
    ///  - 标量字段：选中节点值全部相同时正常显示；部分不同时显示「混合」态（左侧高亮 + 提示），
    ///    且任何修改都广播到全部选中节点（一个 Undo 步）。
    ///  - 列表类字段（选项 / 条件）：Unity 不做数组多编，仅只读汇总，避免「只改首个节点」的歧义。
    /// </summary>
    internal static class FieldDrawerRegistry
    {
        // 选项折叠状态缓存（按稳定 optionId），使面板重建后折叠状态不复位
        private static readonly Dictionary<string, bool> _optionFoldState = new Dictionary<string, bool>();

        // 条件子句折叠状态缓存（按稳定 clauseId），使面板重建后折叠状态不复位
        private static readonly Dictionary<string, bool> _clauseFoldState = new Dictionary<string, bool>();

        /// <summary>节点的「覆盖位置」状态（控制外观·定位子字段门控）：Dialogue/Choice 直读自身字段；
        /// 剧情表节点用「表内默认」下的 appearanceOverridePosition；其它类型 = false。</summary>
        private static bool GetAppearanceOverridePosition(StoryNodeData n)
            => n switch
            {
                DialogueNodeData d => d.appearanceOverridePosition,
                ChoiceNodeData c => c.appearanceOverridePosition,
                StoryTableNodeData st => st.appearanceOverridePosition,
                _ => false,
            };

        /// <summary>剧情表节点「统一样式与外观」组的字段名：未勾选 overrideAppearance 时整组隐藏。</summary>
        private static bool IsTableAppearanceField(string name)
            => name == "appearanceStyle" || name == "appearanceOverridePosition"
                || name == "appearancePositionMode" || name == "appearancePositionAnchor" || name == "appearancePositionOffset"
                || name == "appearanceSpawnStrategyKey" || name == "appearancePersistent";

        public static VisualElement Build(StoryGraphModel model, IReadOnlyList<StoryNodeData> nodes, Action onStructuralChange, Action onTableCommit = null)
        {
            var root = new VisualElement { name = "inspector-fields" };
            root.AddToClassList("fd-fields");
            if (nodes == null || nodes.Count == 0) return root;
            bool isMulti = nodes.Count > 1;
            VisualElement unsyncBadge = null; // 表格驱动节点专用：编辑后即时显示「未同步到表格」

            // 原生序列化来源：数值字段（int/float，含 RangeSlider）走 SerializedProperty 绑定 / 广播写值，
            // 以获得 Unity 自带 slider 序列化效果（拖拽自动写回资产 + 自动 Undo + 控件自身跟手、不重建面板）。
            var so = new SerializedObject(model.Asset);

            // 标量字段编辑：单节点直接改自身；多选则广播到所有选中节点（一个 Undo 步）。
            Action<string, object> normalApply = (path, val) =>
            {
                if (nodes.Count == 1)
                    model.ExecuteCommand(new EditFieldCommand(nodes[0].id, path, val));
                else
                    model.ExecuteCommand(new MultiEditFieldCommand(nodes.Select(n => n.id).ToList(), path, val));
            };

            // 表格驱动节点：内容字段（对白文本 / 讲述者 / 选项文本）编辑时只写回 StoryTableAsset 对应行（唯一真相源），
            // 节点本身在烘焙后不冗余存内容（见 StoryTableBaker / 方案A），不再双写节点字段。其余字段（外观/语速比/打字机等）仅写节点。
            Action<string, object> apply = normalApply;
            bool isTableBound = nodes.Count == 1 && nodes[0].IsTableBound;
            // 表驱动节点：内容真相源在 StoryTableAsset 的行，面板显示/编辑都针对该行（节点不冗余存内容）
            StoryTableAsset table = null;
            StoryTableRow boundRow = null;
            if (isTableBound)
            {
                table = TableBoundEditor.ResolveTable(nodes[0].tableBinding.tableAssetGuid);
                boundRow = table?.GetRow(nodes[0].tableBinding.rowId);
                apply = (path, val) => TableBoundEditor.ApplyEdit(model, nodes[0], normalApply, path, val, unsyncBadge);
            }

            // 表格绑定徽标 + 行编辑按钮（仅单节点且由剧情表驱动时显示，置于字段最上方）
            if (isTableBound)
                unsyncBadge = TableBoundEditor.AddHeader(root, nodes[0], onTableCommit);

            // 反射元数据走 FieldMetaCache（类型级缓存）：面板每次重建不再对每字段 GetFields+GetCustomAttribute。
            var fields = FieldMetaCache.GetFields(nodes[0].GetType());

            foreach (var fm in fields)
            {
                var f = fm.Field;
                var sec = fm.Section;

                // 表驱动虚拟节点（剧情表行展开/子画布）：只显示「编辑会真正生效」的内容字段——语速/打字机/外观/
                // 生成策略等仅节点字段在表驱动下随行重建被丢弃、不生效，整段隐藏（连同其 section 头）避免误导。
                if (isTableBound && !FieldPanelLogic.IsTableDrivenEffectiveField(nodes[0].GetType(), f.Name))
                    continue;

                if (sec != null)
                {
                    var h = new Label(sec.Title) { name = "section-header" };
                    h.AddToClassList("fd-group-label");
                    root.Add(h);
                }

                // 多选：预先算出该字段在选中节点间是否「混合」（部分值不同），用于显示态与高亮。
                // 列表类字段不在此比较（多选时直接只读汇总）。
                object displayValue = f.GetValue(nodes[0]);
                bool isMixed = false;
                if (isMulti && !fm.IsListOfEditable)
                {
                    var vals = nodes.Select(n => f.GetValue(n)).ToList();
                    (isMixed, displayValue) = FieldPanelLogic.EvaluateMixedState(vals);
                }

                // 表驱动节点：内容字段（正文 / 讲述者 / 显示文字）的显示值取自行（唯一真相源）；
                // 节点本身在烘焙后不冗余存内容，故不读节点字段。选项文本在该字段的列表递归里处理。
                displayValue = FieldPanelLogic.RouteTableBoundDisplay(f.Name, boundRow, displayValue);

                AddField(root, model, nodes, f, f.Name, displayValue, isMixed, onStructuralChange, apply, so, table, boundRow);
            }
            // 剧情表节点（主图 StoryTableNodeData）：底部追加「源文件」区——只读显示 SO 中的 Source File Path + 「打开源文件表格」按钮
            if (nodes.Count == 1 && nodes[0] is StoryTableNodeData tableNode)
                TableBoundEditor.AddSourceSection(root, tableNode);
            return root;
        }

        /// <summary>递归把控件及其所有子元素的最小宽度清零并允许收缩（公共工具，角色面板等其它 UI 复用）。
        /// 实现在 <see cref="FieldWidgetFactory"/>（保持外部 FieldDrawerRegistry.ForceShrink 调用点不变）。</summary>
        public static void ForceShrink(VisualElement e) => FieldWidgetFactory.ForceShrink(e);

        private static void AddField(VisualElement root, StoryGraphModel model, IReadOnlyList<StoryNodeData> nodes, FieldInfo f, string path, object displayValue, bool isMixed, Action onStructuralChange, Action<string, object> apply, SerializedObject so, StoryTableAsset table, StoryTableRow boundRow)
        {
            // 反射元数据复用 Build 处已缓存的 FieldMeta（FieldInfo → 元数据查缓存，热路径零重复反射）。
            var meta = FieldMetaCache.GetFields(f.DeclaringType).FirstOrDefault(m => m.Field == f)
                       ?? FieldMetaCache.GetFields(f.ReflectedType ?? f.DeclaringType).FirstOrDefault(m => m.Field == f);
            var sf = meta?.StoryField;
            bool isMulti = nodes.Count > 1;
            bool isListOfEditable = meta?.IsListOfEditable ?? FieldMetaCache.IsListOfEditable(f.FieldType);

            // 多选：列表类字段（选项 / 条件）Unity 不做数组多编，仅只读汇总，避免「只改首个节点」的歧义。
            if (isMulti && isListOfEditable && displayValue is IList listSum)
            {
                AddReadOnlyListSummary(root, sf?.Label ?? f.Name, listSum);
                return;
            }

            // 列表类型（如选项列表、条件组）：展开每个元素的可编辑成员，并提供增 / 删（仅单节点编辑时进入）。
            if (isListOfEditable && displayValue is IList list)
            {
                var elemType = f.FieldType.GetGenericArguments()[0];
                var members = FieldMetaCache.GetFields(elemType);

                var group = new VisualElement { name = "field-group" };
                group.AddToClassList("fd-group");
                var groupLabel = new Label(sf?.Label ?? f.Name) { name = "field-group-label" };
                groupLabel.AddToClassList("fd-group-label");
                group.Add(groupLabel);

                var addBtn = new Button(() =>
                {
                    model.ExecuteCommand(new EditListCommand(nodes[0].id, path, -1));
                    onStructuralChange?.Invoke();
                }) { text = $"添加{sf?.Label ?? f.Name}" };
                // 表驱动：选项列表由剧情表行驱动，禁止面板增删（改的是临时虚拟节点，刷新即被行重建覆盖）
                if (boundRow != null)
                {
                    addBtn.SetEnabled(false);
                    addBtn.tooltip = "选项数量由剧情表行决定，请在表格编辑器中增删选项";
                }
                group.Add(addBtn);

                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i; // 捕获副本，避免 for 循环变量按引用捕获
                    var elem = list[idx];

                    // 玩家选项：做成可收纳（Foldout）显示，折叠状态按稳定 optionId 持久化，避免面板重建后复位
                    if (elemType == typeof(ChoiceOption) && elem is ChoiceOption co)
                    {
                        bool cond = co.hasCondition || !string.IsNullOrEmpty(co.conditionVariable);
                        var header = $"选项 {idx + 1}" + (string.IsNullOrEmpty(co.text) ? "" : $"：{co.text}") + (cond ? " 〔条件〕" : "");
                        bool expanded = true;
                        if (!string.IsNullOrEmpty(co.optionId)) _optionFoldState.TryGetValue(co.optionId, out expanded);
                        var fold = new Foldout { text = header, value = expanded };
                        fold.name = "option-fold";
                        fold.AddToClassList("fd-fold");
                        if (!string.IsNullOrEmpty(co.optionId))
                            fold.RegisterValueChangedCallback(e => _optionFoldState[co.optionId] = e.newValue);
                        foreach (var m in members)
                        {
                            var fi = m.Field;
                            // 表驱动选项：仅文本可编辑（写回行内选项）；条件/外观等成员无行列映射、不生效 → 隐藏
                            if (boundRow != null && fi.Name != "text") continue;
                            object memberVal = fi.GetValue(elem);
                            // 表驱动选项：选项文本取自行内对应选项（按行内原始下标，含无连接编号的选项）；下标映射复用 GetChoiceForOption
                            if (boundRow != null && fi.Name == "text")
                                memberVal = FieldPanelLogic.RouteTableBoundOptionText(boundRow, table, idx, memberVal);
                            AddLeaf(fold, model, nodes, fi, $"{path}[{idx}].{fi.Name}", memberVal, false, elem, onStructuralChange, apply, so, table, boundRow);
                        }
                        var delOptionBtn = new Button(() =>
                        {
                            model.ExecuteCommand(new EditListCommand(nodes[0].id, path, idx));
                            onStructuralChange?.Invoke();
                        }) { text = "删除选项" };
                        delOptionBtn.AddToClassList("fd-del-btn");
                        // 表驱动选项数量由剧情表行决定（节点每次随行重建）——禁止在此增删，避免「改了没反应」的误导
                        if (boundRow != null)
                        {
                            delOptionBtn.SetEnabled(false);
                            delOptionBtn.tooltip = "选项数量由剧情表行决定，请在表格编辑器中增删选项";
                        }
                        fold.Add(delOptionBtn);
                        // 选项之间加分隔线（首个选项除外，避免与「添加选项」按钮之间也出现线）
                        if (idx > 0)
                        {
                            var sep = new VisualElement { name = "option-sep" };
                            sep.AddToClassList("fd-sep");
                            group.Add(sep);
                        }
                        group.Add(fold);
                        continue;
                    }

                    // 条件子句（条件节点的「选项」）：做成可收纳 Foldout，折叠状态按稳定 clauseId 持久化（与选项一致）
                    if (elemType == typeof(ConditionClause) && elem is ConditionClause cc)
                    {
                        string varName = string.IsNullOrEmpty(cc.variableId) ? "" : StoryConstants.VariableName(cc.variableId);
                        string opText = FieldPanelLogic.ClauseOpText(cc.op);
                        string summary = string.IsNullOrEmpty(cc.variableId) ? "" : $"：{varName} {opText} {cc.value}";
                        string header = $"条件 {idx + 1}{summary}";
                        bool expanded = true;
                        if (!string.IsNullOrEmpty(cc.clauseId)) _clauseFoldState.TryGetValue(cc.clauseId, out expanded);
                        var fold = new Foldout { text = header, value = expanded };
                        fold.name = "clause-fold";
                        fold.AddToClassList("fd-fold");
                        if (!string.IsNullOrEmpty(cc.clauseId))
                            fold.RegisterValueChangedCallback(e => _clauseFoldState[cc.clauseId] = e.newValue);
                        foreach (var m in members)
                            AddLeaf(fold, model, nodes, m.Field, $"{path}[{idx}].{m.Field.Name}", m.Field.GetValue(elem), false, elem, onStructuralChange, apply, so, table, boundRow);
                        var delClauseBtn = new Button(() =>
                        {
                            model.ExecuteCommand(new EditListCommand(nodes[0].id, path, idx));
                            onStructuralChange?.Invoke();
                        }) { text = "删除条件" };
                        delClauseBtn.AddToClassList("fd-del-btn");
                        fold.Add(delClauseBtn);
                        // 子句之间加分隔线（首条除外，与选项的视觉风格一致）
                        if (idx > 0)
                        {
                            var sep = new VisualElement { name = "clause-sep" };
                            sep.AddToClassList("fd-sep");
                            group.Add(sep);
                        }
                        group.Add(fold);
                        continue;
                    }

                    // 其他列表元素包成一个内嵌框，彼此分隔
                    var item = new VisualElement { name = "field-item" };
                    item.AddToClassList("fd-item");
                    var itemLabel = new Label($"  {sf?.Label ?? f.Name} {idx + 1}") { name = "field-item-label" };
                    itemLabel.AddToClassList("fd-item-label");
                    item.Add(itemLabel);
                    foreach (var m in members)
                        AddLeaf(item, model, nodes, m.Field, $"{path}[{idx}].{m.Field.Name}", m.Field.GetValue(elem), false, elem, onStructuralChange, apply, so, table, boundRow);
                    var delBtn = new Button(() =>
                    {
                        model.ExecuteCommand(new EditListCommand(nodes[0].id, path, idx));
                        onStructuralChange?.Invoke();
                    }) { text = "删除" };
                    delBtn.AddToClassList("fd-del-btn");
                    item.Add(delBtn);
                    group.Add(item);
                }
                root.Add(group);
                return;
            }

            // 顶层标量字段：owner 即节点本身（变量赋值 / 条件子句位于节点层时使用）。
            AddLeaf(root, model, nodes, f, path, displayValue, isMixed, nodes[0], onStructuralChange, apply, so, table, boundRow);
        }

        /// <summary>多选时列表类字段的只读汇总行：明确告知各节点独立、不批量编辑。</summary>
        private static void AddReadOnlyListSummary(VisualElement root, string label, IList list)
        {
            var row = new VisualElement { name = "field-row" };
            row.AddToClassList("fd-row");
            row.AddToClassList("fd-row-readonly");
            var rowLabel = new Label(label) { name = "field-row-label" };
            rowLabel.AddToClassList("fd-label");
            row.Add(rowLabel);
            var summary = new Label($"{list.Count} 项（各节点独立，多选时不批量编辑）") { name = "field-summary" };
            summary.AddToClassList("fd-summary");
            summary.SetEnabled(false);
            row.Add(summary);
            root.Add(row);
        }

        private static void AddLeaf(VisualElement root, StoryGraphModel model, IReadOnlyList<StoryNodeData> nodes, FieldInfo f, string path, object displayValue, bool isMixed, object owner, Action onStructuralChange, Action<string, object> apply, SerializedObject so, StoryTableAsset table, StoryTableRow boundRow)
        {
            // 反射元数据查缓存（Owner 类型级；owner 为列表元素如 ChoiceOption 时其类型已随列表字段一并缓存）。
            var ownerType = owner?.GetType() ?? f.DeclaringType;
            var meta = FieldMetaCache.GetFields(ownerType).FirstOrDefault(m => m.Field == f);
            var sf = meta?.StoryField;
            var node = nodes[0];
            bool isMulti = nodes.Count > 1;

            // 剧情表节点「表内默认」总开关门控（勾选才显示对应内容）：
            // overrideTyping=false → 隐藏 语速/打字机；overrideAppearance=false → 隐藏整个外观组（样式/位置/策略/保留）。
            if (node is StoryTableNodeData stTable)
            {
                if (!stTable.overrideTyping && (f.Name == "typingSpeed" || f.Name == "typingMode")) return;
                if (!stTable.overrideAppearance && IsTableAppearanceField(f.Name)) return;
            }

            // 节点级外观·位置子字段的显隐：仅当「覆盖位置」勾选时才显示（与选择节点条件字段门控同构）。
            if (FieldPanelLogic.IsAppearancePositionField(f.Name))
            {
                bool show = GetAppearanceOverridePosition(node);
                if (!show) return;
            }

            // 节点级外观·生成策略的显隐：仅当「覆盖位置」未勾选时才显示。
            // 设计意图：勾选了「覆盖位置」意味着用具体定位模式/锚点/偏移精确控制位置，
            // 此时生成策略（决定出现位置/层级/保留）不再参与，避免两者同时配置造成歧义。
            if (FieldPanelLogic.IsAppearanceSpawnStrategyField(f.Name))
            {
                bool hide = GetAppearanceOverridePosition(node);
                if (hide) return;
            }

            // 结束节点：跳转章节仅在「结束类型 = 跳转章节(JumpChapter)」时才显示（正常结束无跳转语义，不暴露该字段）。
            if (f.Name == "jumpToChapter" && node is EndNodeData jumpEnd && jumpEnd.endType != EndType.JumpChapter) return;

            // 结束节点：结束文本仅在「显示结束文本」勾选时才显示（默认不勾选 → 不暴露该字段）。
            if (f.Name == "endText" && node is EndNodeData endNode && !endNode.showEndText) return;

            // 选择节点：讲述者 / 正文仅在「显示文字」勾选时才显示（未勾选=纯选项节点，不暴露文字字段）。
            // 注意必须限定 owner 是节点本身：递归展开选项列表时 ChoiceOption.text（选项文本）也会命
            // 中 f.Name=="text"，若不加限定会被误隐藏——「显示文字」只应门控节点级对白（讲述者/正文）。
            if (node is ChoiceNodeData chcShow && owner is not ChoiceOption
                && (f.Name == "speakerId" || f.Name == "text"))
            {
                bool showTextVal = chcShow.IsTableBound
                    ? (boundRow?.showText ?? true)
                    : chcShow.showText;
                if (!showTextVal) return;
            }

            // 选择节点：条件相关字段的显隐。判定「受条件门控」= 勾选了「带条件」，或旧档残留单条件变量（二者任一才显示条件字段）。
            // 注意：必须在嵌套列表递归之前判定，否则 conditionGroup 会先于门控被递归展开（标题+添加按钮始终显示）。
            if (owner is ChoiceOption co)
            {
                bool showCond = co.hasCondition || !string.IsNullOrEmpty(co.conditionVariable);
                if (!showCond)
                {
                    // 不受条件门控：隐藏所有条件相关字段（含条件组及其按钮）
                    var nm0 = f.Name;
                    if (nm0 == "conditionVariable" || nm0 == "conditionOp" || nm0 == "conditionValue"
                        || nm0 == "conditionCombine" || nm0 == "conditionGroup")
                        return;
                }
                else
                {
                    co.EnsureMigrated();
                    // 仅当确实勾选了「带条件」时，旧四字段才被条件组接管而隐藏；
                    // 旧档仅残留单条件变量（hasCondition 未勾选）时保留旧字段可见，便于查看/编辑。
                    if (co.hasCondition)
                    {
                        var nm1 = f.Name;
                        if (nm1 == "conditionVariable" || nm1 == "conditionOp" || nm1 == "conditionValue")
                            return;
                    }
                }
            }

            // 支持嵌套列表（如选项内的条件组）：交给 AddField 递归展开，避免被当成普通字符串
            if ((meta?.IsListOfEditable ?? FieldMetaCache.IsListOfEditable(f.FieldType)) && displayValue is IList)
            {
                AddField(root, model, nodes, f, path, displayValue, false, onStructuralChange, apply, so, table, boundRow);
                return;
            }

            var row = new VisualElement { name = "field-row" };
            row.AddToClassList("fd-row");
            if (isMixed) row.AddToClassList("fd-row-mixed");
            var rowLabel = new Label(sf?.Label ?? f.Name) { name = "field-row-label" };
            rowLabel.AddToClassList("fd-label");
            // 单行显示 + 过长省略 + 硬截断：长字段 label（6+ 字）在 50px 紧凑标签内既不换行也不溢出到控件区。
            // 只在此处内联（不动 USS fd-label 宽度，避免影响其它节点既有紧凑布局）。
            rowLabel.style.whiteSpace = WhiteSpace.NoWrap;
            rowLabel.style.textOverflow = TextOverflow.Ellipsis;
            rowLabel.style.unityTextOverflowPosition = TextOverflowPosition.End;
            rowLabel.style.overflow = Overflow.Hidden;
            row.Add(rowLabel);

            // 控件创建 + 交互回调注册全部在 FieldWidgetFactory（变量 op/value 自适应、混合态占位、
            // 数值原生绑定/广播、结构性重建钩子等细节见工厂注释）。
            var control = FieldWidgetFactory.CreateControl(
                meta, f, model, node, nodes, owner, displayValue, isMixed, path,
                apply, onStructuralChange,
                onRefresh: () => FieldWidgetFactory.ScheduleRefresh(row, isMulti, onStructuralChange),
                so, out var timelineTarget);

            row.Add(control);
            root.Add(row);

            // 打字机「手K时序」模式：在打字机模式行内、下拉右侧追加「时间轴」按钮，打开逐字时序编辑窗口
            if (timelineTarget != null)
            {
                var tlBtn = new Button(() => DialogueTypingTimelineWindow.OpenForNode(model, timelineTarget))
                {
                    text = "时间轴"
                };
                tlBtn.AddToClassList("fd-timeline-btn");
                row.Add(tlBtn);
            }
        }
    }
}
