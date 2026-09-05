using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Commands;
using MicrobialNet.Story.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Inspector
{
    /// <summary>
    /// 属性面板控件工厂（P4/L2 自 FieldDrawerRegistry 拆出）：只负责「为字段创建控件并注册其交互回调」，
    /// 不做门控 / 行组装 / 列表展开 / 表绑定路由（那些归 FieldDrawerRegistry 编排与 FieldPanelLogic 纯逻辑）。
    ///
    /// 入口 <see cref="CreateControl"/>：按 <see cref="FieldMeta"/> 的特性与字段类型分派——
    /// [CharacterPicker]/[StoryEventPicker]/[VariablePicker]/[SpawnStrategyPicker]、DialogueBoxStyleAsset、
    /// 通用 Object、string([MultilineText])、int/float([RangeSlider]，原生绑定/广播)、bool（结构性重建钩子）、
    /// TypingMode 友好中文下拉、枚举、Vector2(Int)，以及变量相关节点的 op/value 自适应控件。
    /// </summary>
    internal static class FieldWidgetFactory
    {
        /// <summary>多选混合态占位的统一常量。Unity 的 DropdownField(choices, defaultValue) 要求 defaultValue 必须存在于 choices，
        /// 否则构造抛 ArgumentException；故混合态把本占位追加进 choices 副本再构造，并在 change 处理里忽略、不写回模型。</summary>
        internal const string MixedPlaceholder = "— 各节点不同 —";

        /// <summary>给控件打上「混合」提示：tooltip 说明修改将广播到全部选中节点。</summary>
        internal static void MarkMixed(VisualElement control, bool isMixed)
        {
            if (!isMixed) return;
            control.tooltip = "多选：部分节点该值不同，修改将应用到全部选中节点";
        }

        /// <summary>多选修改后，延迟重建面板以重算混合态（FocusOut / 离散控件提交后触发，避免编辑中抢焦点）。</summary>
        internal static void ScheduleRefresh(VisualElement root, bool isMulti, Action onStructuralChange)
        {
            if (isMulti) root.schedule.Execute(() => onStructuralChange?.Invoke());
        }

        /// <summary>
        /// 递归把控件及其所有子元素的最小宽度清零并允许收缩（flexShrink=1）。
        /// 关键：TextField/EnumField/DropdownField 等内部含 TextInput 等子元素，其默认 minWidth=Auto
        /// 会让控件按内容撑开、外层设 minWidth=0 无法传递进去，导致输入框超出面板宽度。
        /// 必须逐层清零才能真正收缩进面板。FieldDrawerRegistry 以 public 转发对外供角色面板等其它 UI 复用。
        /// </summary>
        internal static void ForceShrink(VisualElement e)
        {
            e.style.flexShrink = 1;
            e.style.minWidth = 0;
            foreach (var c in e.Children())
                ForceShrink(c);
        }

        // ══ 控件创建入口（原 AddLeaf 的类型分派链）════════════════

        /// <summary>
        /// 为叶子字段创建控件并注册交互回调。统一收尾：MarkMixed + fd-control 类 + ForceShrink（幂等）。
        /// 返回控件的 <paramref name="timelineTarget"/>：仅打字机「手K时序」且单选对话节点时非空，
        /// 调用方（Registry）据此在行尾追加「时间轴」按钮。
        /// </summary>
        /// <param name="onRefresh">多选提交后的延迟刷新（调用方包装 ScheduleRefresh(row)，保持行级语义）。</param>
        internal static VisualElement CreateControl(
            FieldMeta meta, System.Reflection.FieldInfo f, StoryGraphModel model, StoryNodeData node, IReadOnlyList<StoryNodeData> nodes,
            object owner, object displayValue, bool isMixed, string path,
            Action<string, object> apply, Action onStructuralChange, Action onRefresh, SerializedObject so,
            out DialogueNodeData timelineTarget)
        {
            timelineTarget = null;
            bool isMulti = nodes.Count > 1;
            var sf = meta?.StoryField;
            var t = f.FieldType;
            VisualElement control;

            // 变量相关节点：操作(op)与值(value)随所选变量类型自适应
            // （如布尔无加减、无大小比较；布尔的值用 true/false 下拉而非输入框）
            bool isAssign = node is SetVariableNodeData && ReferenceEquals(owner, node);
            bool isClause = owner is ConditionClause;
            if (isAssign || isClause)
            {
                string varId = isAssign ? ((SetVariableNodeData)node).variableId : ((ConditionClause)owner).variableId;
                var vt = ResolveVarTypeIncludingGlobal(model, varId);
                if (f.Name == "op")
                {
                    control = MakeOpField(apply, path, (Enum)displayValue, isAssign ? FieldPanelLogic.ValidAssignOps(vt) : FieldPanelLogic.ValidCompareOps(vt));
                    control.RegisterCallback<ChangeEvent<string>>(_ => onRefresh?.Invoke());
                    return Finish(control, isMixed);
                }
                if (f.Name == "value")
                {
                    // 「变量输入」端口连线（获取变量节点）→ 操作数/比较值固定为端口变量：置灰只读框显示变量名，表示不可编辑
                    string portId = isAssign ? "var_in" : "var_in_" + ((ConditionClause)owner).clauseId;
                    string portVar = ResolvePortVarName(model, node, portId);
                    if (portVar != null)
                    {
                        var tf = new TextField { value = "← " + portVar, isReadOnly = true };
                        tf.SetEnabled(false);
                        tf.style.opacity = 0.75f;
                        tf.tooltip = "操作数来自「获取变量」节点连线；取消连线后可编辑常量";
                        control = tf;
                        return Finish(control, isMixed);
                    }
                    control = MakeValueField(apply, path, vt, displayValue as string);
                    control.RegisterCallback<ChangeEvent<string>>(_ => onRefresh?.Invoke());
                    return Finish(control, isMixed);
                }
            }

            if (sf != null && sf.Future)
            {
                // 预留字段（未实现功能）：灰显占位，不可编辑
                var tf = new TextField { value = displayValue as string ?? "", isReadOnly = true };
                tf.SetEnabled(false);
                control = tf;
            }
            else if (meta?.CharacterPicker != null)
            {
                control = MakeCharacterField(model, node, apply, path, displayValue as string, isMixed);
                control.RegisterCallback<FocusOutEvent>(_ => onRefresh?.Invoke());
            }
            else if (meta?.EventPicker != null)
            {
                // 可编辑组合框：TextField 自由输入（兼容动态/运行时注册事件）+ 右侧下拉按钮（点击弹 GenericDropdownMenu，
                // 内置定位与点击外部关闭，无内联浮层遮挡下方字段）。DropdownField 不可自由输入→动态事件无法配置；
                // 之前的 ScrollView 内联建议列表会盖住下方兄弟——本质是 inline 设计错误，此处用 popover 根治。
                var known = StoryEventCatalog.GetKnownEventNames();
                var cur = displayValue as string ?? "";
                var tf = new TextField { value = isMixed ? "" : cur };
                tf.style.flexGrow = 1;
                tf.RegisterValueChangedCallback(e => apply(path, e.newValue));
                tf.RegisterCallback<FocusOutEvent>(_ => onRefresh?.Invoke());

                var arrow = new Button { text = "▾", name = "event-arrow" };
                arrow.style.marginLeft = 2;
                arrow.style.width = 22;
                arrow.clicked += () =>
                {
                    var menu = new GenericDropdownMenu();
                    if (known.Count == 0)
                        menu.AddDisabledItem("（无已知事件）", false);
                    else
                        foreach (var k in known)
                            menu.AddItem(k, false, () => { tf.value = k; }); // 触发 ValueChanged → apply
                    menu.DropDown(arrow.worldBound, arrow);
                };

                var box = new VisualElement { name = "event-combobox" };
                box.style.flexDirection = FlexDirection.Row;
                box.style.alignItems = Align.Center;
                box.Add(tf);
                box.Add(arrow);
                control = box;
            }
            else if (node is EndNodeData && f.Name == "jumpToChapter")
            {
                // 结束节点「跳转章节」：复用事件节点选事件的下拉样式/交互——可编辑组合框
                // （TextField 自由输入，兼容手填/运行时动态章节 key）+ ▾ 下拉。下拉内容 = 全部剧情图
                // 按分组（StoryMeta.chapter，空归「未分组」且排最后）平铺；点选把该图 storyId（空则资产名）写入。
                control = MakeGraphJumpField(apply, path, displayValue as string, isMixed, onRefresh);
            }
            else if (meta?.VariablePicker != null)
            {
                control = MakeVariableField(model, node, apply, path, displayValue as string, owner, onStructuralChange, isMixed);
            }
            else if (meta?.SpawnStrategyPicker != null)
            {
                control = MakeSpawnStrategyField(model, node, apply, path, displayValue as string, isMulti, isMixed, onStructuralChange);
            }
            else if (t == typeof(DialogueBoxStyleAsset))
            {
                control = MakeStyleAssetField(model, nodes, apply, path, displayValue as DialogueBoxStyleAsset, isMulti, isMixed, onStructuralChange);
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                // 通用 SO 资产引用（如剧情表节点的 tableAsset）：ObjectField 拖槽直接指定资产，避免把引用渲染成字符串文本框。
                // ObjectField 自身 label 置空（外层行标签已显示字段名）；换绑后重建面板（摘要/端口/源文件区随之刷新）。
                var objField = new ObjectField("")
                {
                    objectType = t,
                    value = isMixed ? null : displayValue as UnityEngine.Object,
                };
                objField.AddToClassList("fd-control");
                objField.RegisterValueChangedCallback(e =>
                {
                    apply(path, e.newValue);
                    if (node is StoryTableNodeData && f.Name == "tableAsset")
                        onStructuralChange?.Invoke(); // 换表：重算端口/摘要/源文件区
                    onRefresh?.Invoke();
                });
                control = objField;
            }
            else if (t == typeof(string))
            {
                var mt = meta?.MultilineText;
                // 多选混合：字符串清空，明确提示「各节点不同」，而非显示某一节点的值冒充统一值。
                var tf = new TextField { multiline = true, value = isMixed ? "" : displayValue as string ?? "" };
                if (mt != null) tf.style.height = mt.Lines * 18;
                tf.RegisterValueChangedCallback(e => apply(path, e.newValue));
                tf.RegisterCallback<FocusOutEvent>(_ => onRefresh?.Invoke());
                control = tf;
            }
            else if (t == typeof(int))
            {
                var rs = meta?.RangeSlider;
                if (rs != null)
                {
                    var sl = new SliderInt((int)rs.Min, (int)rs.Max, SliderDirection.Horizontal, displayValue is int vi ? vi : 0);
                    sl.showInputField = true;
                    sl.AddToClassList("fd-control");
                    BindOrBroadcastNumeric(sl, so, model, nodes, isMulti, path, sf, f, owner);
                    control = sl;
                }
                else
                {
                    var nf = new IntegerField { value = displayValue is int vi ? vi : 0 };
                    BindOrBroadcastNumeric(nf, so, model, nodes, isMulti, path, sf, f, owner);
                    control = nf;
                }
            }
            else if (t == typeof(float))
            {
                var rs = meta?.RangeSlider;
                if (rs != null)
                {
                    var sl = new Slider(rs.Min, rs.Max, SliderDirection.Horizontal, displayValue is float vf ? vf : 0f);
                    sl.showInputField = true;
                    sl.AddToClassList("fd-control");
                    BindOrBroadcastNumeric(sl, so, model, nodes, isMulti, path, sf, f, owner);
                    control = sl;
                }
                else
                {
                    var ff = new FloatField { value = displayValue is float vf ? vf : 0f };
                    BindOrBroadcastNumeric(ff, so, model, nodes, isMulti, path, sf, f, owner);
                    control = ff;
                }
            }
            else if (t == typeof(bool))
            {
                var tg = new Toggle { value = displayValue is bool b && b };
                tg.RegisterValueChangedCallback(e =>
                {
                    apply(path, e.newValue);
                    // 剧情表节点「表内默认」总开关（统一语速与打字机 / 统一样式与外观）：切换时重建面板以显隐对应字段组
                    if (owner is StoryTableNodeData
                        && (f.Name == "overrideTyping" || f.Name == "overrideAppearance"))
                        onStructuralChange?.Invoke();
                    // 选择节点的「带条件」开关变化时，需要重建面板以显隐条件细节字段
                    if (owner is ChoiceOption && f.Name == "hasCondition")
                        onStructuralChange?.Invoke();
                    // 节点「覆盖位置」开关变化时，重建面板以显隐定位子字段与生成策略（二者互斥）
                    // ——含剧情表节点「表内默认」下的外观覆盖位置（FieldDrawerRegistry 门控对其同样生效）
                    if ((owner is DialogueNodeData || owner is ChoiceNodeData || owner is StoryTableNodeData)
                        && f.Name == "appearanceOverridePosition")
                        onStructuralChange?.Invoke();
                    // 结束节点「显示结束文本」开关变化时，重建面板以显隐「结束文本」输入框
                    if (owner is EndNodeData && f.Name == "showEndText")
                        onStructuralChange?.Invoke();
                    // 选择节点「显示文字」开关变化时，重建面板以显隐讲述者/正文字段（序列化持久 + 显示切换）
                    if (owner is ChoiceNodeData && f.Name == "showText")
                        onStructuralChange?.Invoke();
                    onRefresh?.Invoke();
                });
                control = tg;
            }
            else if (f.FieldType == typeof(TypingMode))
            {
                // 打字机模式：友好中文标签下拉（避免直接显示枚举名 GlobalSpeed/Punctuation/Custom）。
                var labels = new List<string> { "全局语速", "标点节奏", "手K时序" };
                var values = new List<TypingMode> { TypingMode.GlobalSpeed, TypingMode.Punctuation, TypingMode.Custom };
                var cur = displayValue is TypingMode tm ? tm : TypingMode.GlobalSpeed;
                var ddChoices = new List<string>(labels);
                string ddDefault = labels[values.IndexOf(cur)];
                if (isMixed) { ddChoices.Add(MixedPlaceholder); ddDefault = MixedPlaceholder; }
                var dd = new DropdownField(ddChoices, ddDefault); dd.AddToClassList("fd-control");
                dd.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue == MixedPlaceholder) return;   // 占位项不可写回
                    int idx = labels.IndexOf(e.newValue);
                    if (idx >= 0) apply(path, values[idx]);
                    onRefresh?.Invoke();
                });
                control = dd;

                // 仅手K时序：out 目标节点，由调用方在该行追加「时间轴」按钮（打开逐字时序可视化编辑窗口，不随节点选中自动弹出）
                if (cur == TypingMode.Custom && !isMulti && nodes[0] is DialogueNodeData dlgNode)
                    timelineTarget = dlgNode;
            }
            else if (t.IsEnum)
            {
                var ef = new EnumField((Enum)displayValue); ef.AddToClassList("fd-control");
                ef.RegisterValueChangedCallback(e =>
                {
                    apply(path, e.newValue);
                    // 结束节点「结束类型」在 Normal ⇄ JumpChapter 间切换时，重建面板以显隐「跳转章节」字段（仅 JumpChapter 显示）
                    if (owner is EndNodeData && f.Name == "endType")
                        onStructuralChange?.Invoke();
                    onRefresh?.Invoke();
                });
                control = ef;
            }
            else if (t == typeof(Vector2Int))
            {
                var v2i = new Vector2IntField { value = displayValue is Vector2Int vi ? vi : Vector2Int.zero };
                v2i.RegisterValueChangedCallback(e => { apply(path, e.newValue); onRefresh?.Invoke(); });
                control = v2i;
            }
            else if (t == typeof(Vector2))
            {
                var v2 = new Vector2Field { value = displayValue is Vector2 v ? v : Vector2.zero };
                v2.RegisterValueChangedCallback(e => { apply(path, e.newValue); onRefresh?.Invoke(); });
                control = v2;
            }
            else
            {
                var tf = new TextField { value = displayValue?.ToString() ?? "" };
                tf.RegisterValueChangedCallback(e => apply(path, e.newValue));
                tf.RegisterCallback<FocusOutEvent>(_ => onRefresh?.Invoke());
                control = tf;
            }

            return Finish(control, isMixed);
        }

        /// <summary>统一收尾（幂等）：混合提示 + fd-control 类 + 收缩。个别分支已做过的操作重复执行无副作用。</summary>
        private static VisualElement Finish(VisualElement control, bool isMixed)
        {
            MarkMixed(control, isMixed);
            control.AddToClassList("fd-control");
            ForceShrink(control);
            return control;
        }

        // ══ 数值字段的原生绑定 / 广播 ═════════════════════════

        /// <summary>
        /// 数值字段（int/float，含 RangeSlider）的原生序列化绑定 / 广播写值。
        /// - 单选：用 Unity 自带 slider / 输入域绑定（Slider/SliderInt/IntegerField/FloatField 的 bindingPath + Bind），
        ///   拖拽自动写回 SerializedProperty、自动 Undo、控件自身跟手，且绝不重建面板（从根上消除滑块不同步 bug）。
        /// - 多选：节点是 [SerializeReference] 数组元素、index 各异，无法用单一 SerializedObject 多目标绑定，
        ///   故在拖拽 / 输入框提交时遍历所有选中节点的 SerializedProperty 广播写值（同样不重建面板、跟手），每次拖拽 / 提交前记一个 Undo 步。
        /// </summary>
        private static void BindOrBroadcastNumeric(VisualElement control, SerializedObject so, StoryGraphModel model, IReadOnlyList<StoryNodeData> nodes, bool isMulti, string path, StoryFieldAttribute sf, FieldInfo f, object owner)
        {
            // 单选：原生绑定（拖拽自动写回 + Undo + 重绘，无重建）
            if (!isMulti)
            {
                int idx = model.Asset.nodes.IndexOf(nodes[0]);
                if (control is BindableElement bindable)
                {
                    bindable.bindingPath = $"nodes.Array.data[{idx}].{path}";
                    bindable.Bind(so);
                }
                // 原生绑定直接写 SerializedObject、绕过命令管线 → 模型脏标记感知不到改动。
                // 值变化时补 TouchData：置脏 + 广播 FieldChanged（状态栏「未保存*」/ 关闭与切换确认 / 自动保存快照）。
                // 数据锚定方案：判定依据是节点字段值本身（控件值不可信，见 RegisterNumericDirtyTracker 注释）。
                RegisterNumericDirtyTracker(control, model, f, owner);
                return;
            }

            // 多选：广播写值（跟手、不重建面板）
            string label = sf?.Label ?? path;
            control.RegisterCallback<PointerDownEvent>(_ => Undo.RecordObject(model.Asset, "批量编辑 " + label));
            control.RegisterCallback<FocusOutEvent>(_ =>
            {
                Undo.RecordObject(model.Asset, "批量编辑 " + label);
                so.ApplyModifiedProperties();
            });
            void WriteAll(float v)
            {
                foreach (var n in nodes)
                {
                    var sp = so.FindProperty($"nodes.Array.data[{model.Asset.nodes.IndexOf(n)}].{path}");
                    if (sp == null) continue;
                    if (control is SliderInt || control is IntegerField) sp.intValue = Mathf.RoundToInt(v);
                    else sp.floatValue = v;
                }
                so.ApplyModifiedProperties();
                model.TouchData(); // 广播写值同样绕过命令管线 → 补置脏+通知（与单选绑定路径对齐）
            }
            if (control is SliderInt si) si.RegisterValueChangedCallback(e => WriteAll(e.newValue));
            else if (control is IntegerField inf) inf.RegisterValueChangedCallback(e => WriteAll(e.newValue));
            else if (control is Slider sl) sl.RegisterValueChangedCallback(e => WriteAll(e.newValue));
            else if (control is FloatField ff) ff.RegisterValueChangedCallback(e => WriteAll(e.newValue));
        }

        /// <summary>为原生绑定数值控件注册「脏跟踪」（**数据锚定**方案：只看节点字段值，不看控件值）。
        /// 基线 = 注册时反射读到的节点字段值；值变化回调里再读节点字段值，**数据确实变了才 TouchData**。
        ///
        /// 为什么不看控件值（前两版踩坑的根因）：控件值会被 Unity 绑定随时改写——「初值同步」把属性值推进控件、
        /// 滑块钳制、混合态占位等都可能让控件值在注册后变化（≠用户编辑）。与控件值比对必然在
        /// 「选中误报」（第一版）与「拖动漏报」（第三版）之间摇摆，取决于绑定写回与回调的时序。
        /// 节点字段值不受任何 UI 时序影响：初值同步是 属性→控件 单向（不改数据），真实拖动则绑定先写回数据
        /// （第二版实测：回调时节点值已==控件新值）再触发回调 → 回调里读数据必然能看到变化。
        ///
        /// 双保险：回调里同步判一次（覆盖「先写回后回调」）；再 schedule 下一拍补判一次（覆盖「先回调后写回」的
        /// 时序变体，元素仍在面板上时必然执行）。补判无数据变化时是空操作，不会重复 TouchData。</summary>
        private static void RegisterNumericDirtyTracker(VisualElement control, StoryGraphModel model, FieldInfo f, object owner)
        {
            var last = new double[1]; // 闭包箱：基线锚定在节点数据（非控件值）
            last[0] = ToDoubleLoose(f?.GetValue(owner));
            void Check()
            {
                double cur = ToDoubleLoose(f?.GetValue(owner));
                if (double.IsNaN(cur) || double.IsNaN(last[0]))
                {
                    // 读不到（异常路径）：保守置脏并刷新基线，宁可误报不漏报。
                    last[0] = cur;
                    model.TouchData();
                    return;
                }
                if (System.Math.Abs(cur - last[0]) < 1e-9) return; // 数据没变（初值同步回放等）：忽略
                last[0] = cur; // 数据真变了：更新基线并置脏
                model.TouchData();
            }
            void OnChanged()
            {
                Check();                                  // 立即判一次（绑定先写回的场景）
                control.schedule.Execute(Check);           // 下一拍补判一次（绑定后写回的场景；无变化=空操作）
            }

            switch (control)
            {
                case SliderInt si: si.RegisterValueChangedCallback(_ => OnChanged()); break;
                case IntegerField inf: inf.RegisterValueChangedCallback(_ => OnChanged()); break;
                case Slider sl: sl.RegisterValueChangedCallback(_ => OnChanged()); break;
                case FloatField ff: ff.RegisterValueChangedCallback(_ => OnChanged()); break;
            }
        }

        /// <summary>装箱数值归一为 double（int/long/float/double）；非数值或 null → NaN。
        /// 基线与判定走同一转换路径，float→double 精度差异两侧一致，精确相等。</summary>
        private static double ToDoubleLoose(object v)
        {
            switch (v)
            {
                case null: return double.NaN;
                case int i: return i;
                case long l: return l;
                case float fl: return fl;
                case double d: return d;
                default: return double.NaN;
            }
        }

        // ══ 下拉 / 输入类控件 ═════════════════════════════════

        private static string TypeLabel(VariableType? t) => t switch
        {
            VariableType.Int => "Int",
            VariableType.Float => "Float",
            VariableType.Bool => "布尔",
            VariableType.String => "String",
            _ => "?",
        };

        private static VisualElement MakeOpField(Action<string, object> apply, string path, Enum current, IReadOnlyList<(Enum op, string label)> ops)
        {
            var labels = ops.Select(o => o.label).ToList();
            var labelToOp = ops.ToDictionary(o => o.label, o => o.op);
            var cur = ops.FirstOrDefault(o => Equals(o.op, current));
            string currentLabel = cur.label ?? labels[0]; // 当前值非法（如切换类型后）→ 回退首项显示
            var dd = new DropdownField(labels, currentLabel); dd.AddToClassList("fd-control");
            dd.RegisterValueChangedCallback(e =>
            {
                if (labelToOp.TryGetValue(e.newValue, out var op))
                    apply(path, op);
            });
            return dd;
        }

        private static VisualElement MakeValueField(Action<string, object> apply, string path, VariableType? type, string current)
        {
            if (type == VariableType.Bool)
            {
                // 布尔值用下拉选择 true/false（不用勾选框）
                bool b = current == "1" || current == "True" || current == "true" || (bool.TryParse(current, out var tb) && tb);
                var choices = new List<string> { "true", "false" };
                var dd = new DropdownField(choices, b ? "true" : "false"); dd.AddToClassList("fd-control");
                dd.RegisterValueChangedCallback(e =>
                    apply(path, e.newValue));
                return dd;
            }
            if (type == VariableType.Int)
            {
                // Int 类型：实时过滤，只允许「可选负号 + 数字」，其余字符丢弃。
                var tf = new TextField { value = current ?? "" }; tf.AddToClassList("fd-control");
                tf.RegisterCallback<InputEvent>(evt =>
                {
                    var filtered = SanitizeInt(evt.newData);
                    if (filtered != evt.newData)
                    {
                        tf.value = filtered;
                        tf.cursorIndex = filtered.Length;
                        tf.selectIndex = filtered.Length;
                    }
                });
                tf.RegisterValueChangedCallback(e => apply(path, e.newValue));
                return tf;
            }
            var tf2 = new TextField { value = current ?? "" }; tf2.AddToClassList("fd-control");
                tf2.RegisterValueChangedCallback(e => apply(path, e.newValue));
            return tf2;
        }

        /// <summary>保留「可选负号 + 数字」，去掉其它所有字符（用于 Int 输入框实时校验）。</summary>
        private static string SanitizeInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var chars = new char[s.Length];
            int n = 0;
            bool minusSeen = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '-')
                {
                    if (i == 0 && !minusSeen) { chars[n++] = '-'; minusSeen = true; }
                }
                else if (c >= '0' && c <= '9')
                {
                    chars[n++] = c;
                }
            }
            return new string(chars, 0, n);
        }

        /// <summary>结束节点「跳转章节」：可编辑组合框（交互/样式与事件节点选事件一致——TextField 自由输入
        /// + ▾ 弹 GenericDropdownMenu）。下拉 = 全部剧情图按分组（StoryMeta.chapter，空归「未分组」排最后）展示，
        /// 点选把该图 storyId（空则资产名，与 StoryGraphRegistry 双键注册语义对齐）写入字段。</summary>
        private static VisualElement MakeGraphJumpField(Action<string, object> apply, string path, string current, bool isMixed, Action onRefresh)
        {
            var tf = new TextField { value = isMixed ? "" : (current ?? "") };
            tf.style.flexGrow = 1;
            tf.RegisterValueChangedCallback(e => apply(path, e.newValue));
            tf.RegisterCallback<FocusOutEvent>(_ => onRefresh?.Invoke());

            var arrow = new Button { text = "▾", name = "graph-arrow" };
            arrow.style.marginLeft = 2;
            arrow.style.width = 22;
            arrow.clicked += () =>
            {
                var menu = new GenericDropdownMenu();
                var byGroup = AssetDatabase.FindAssets("t:StoryGraphAsset")
                    .Select(g => AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(a => a != null)
                    .GroupBy(a => string.IsNullOrEmpty(a.meta?.chapter) ? "未分组" : a.meta.chapter)
                    .OrderBy(g => g.Key == "未分组" ? 1 : 0)
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .ToList();
                if (byGroup.Count == 0)
                {
                    menu.AddDisabledItem("（无剧情图）", false);
                }
                else
                {
                    // 组头用禁用项呈现（分组即章节/子目录名），组内图资产按名排序；未分组组头排在最后。
                    foreach (var g in byGroup)
                    {
                        menu.AddDisabledItem($"── {g.Key} ──", false);
                        foreach (var a in g.OrderBy(a => a.name, StringComparer.Ordinal))
                        {
                            string key = string.IsNullOrEmpty(a.meta?.storyId) ? a.name : a.meta.storyId;
                            menu.AddItem(a.name, tf.value == key, () => { tf.value = key; }); // 触发 ValueChanged → apply；勾选态以输入框实时值为准
                        }
                    }
                }
                menu.DropDown(arrow.worldBound, arrow);
            };

            var box = new VisualElement { name = "graph-combobox" };
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            box.Add(tf);
            box.Add(arrow);
            return box;
        }

        private static VisualElement MakeCharacterField(StoryGraphModel model, StoryNodeData node, Action<string, object> apply, string path, string current, bool isMixed)
        {
            // 选项来源：角色库（StoryCharacterAsset）+ 已引用 ID 兜底 + 旁白/未知。
            // 显示「可读名字」，回写 characterId（保证准确性：落库仍是稳定 ID，改名不影响引用）。
            var assets = CharacterLibrary.All();
            var nameById = new Dictionary<string, string>();
            foreach (var a in assets)
                if (!string.IsNullOrEmpty(a.characterId))
                    nameById[a.characterId] = string.IsNullOrEmpty(a.displayName) ? a.characterId : a.displayName;

            string NameOf(string id)
            {
                if (id == StoryConstants.NarrationId) return "旁白";
                if (id == StoryConstants.UnknownId) return "???";
                if (id == StoryConstants.SelfId) return "玩家自己";
                if (string.IsNullOrEmpty(id)) return "旁白";
                return nameById.TryGetValue(id, out var n) ? n : id;
            }
            // 兜底默认值：current 为空（新节点未选讲述者）时解析为旁白，避免向 Dictionary 传入 null 作 key。
            string SafeCurrent(string cur) => string.IsNullOrEmpty(cur) ? StoryConstants.NarrationId : cur;

            var idToName = new Dictionary<string, string>();
            var nameToId = new Dictionary<string, string>();
            bool Add(string id)
            {
                if (string.IsNullOrEmpty(id) || idToName.ContainsKey(id)) return false;
                var name = NameOf(id);
                string key = name;
                int dup = 1;
                while (nameToId.ContainsKey(key)) key = $"{name} ({++dup})";
                idToName[id] = key;
                nameToId[key] = id;
                return true;
            }
            Add(StoryConstants.NarrationId);
            Add(StoryConstants.UnknownId);
            Add(StoryConstants.SelfId);
            foreach (var a in assets)
                if (!string.IsNullOrEmpty(a.characterId)) Add(a.characterId);
            foreach (var id in model.Asset.usedCharacterIds)
                Add(id);
            // 兜底：当前值若不在选项内（如引用了已删除资产），仍补入以免下拉丢失选中
            if (!string.IsNullOrEmpty(current)) Add(current);

            var choices = idToName.Values.ToList();
            idToName.TryGetValue(SafeCurrent(current), out var currentName);
            if (string.IsNullOrEmpty(currentName)) currentName = NameOf(current);
            // 多选混合态：Placeholder 必须进 choices 副本，否则 DropdownField 构造抛 ArgumentException；占位仅作初始显示、不可写回。
            var ddChoices = new List<string>(choices);
            string ddDefault = currentName;
            if (isMixed) { ddChoices.Add(MixedPlaceholder); ddDefault = MixedPlaceholder; }
            var dd = new DropdownField(ddChoices, ddDefault); dd.AddToClassList("fd-control");
            dd.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == MixedPlaceholder) return;   // 占位项不可写回
                if (nameToId.TryGetValue(e.newValue, out var id))
                    apply(path, id);
            });
            return dd;
        }

        private static VisualElement MakeVariableField(StoryGraphModel model, StoryNodeData node, Action<string, object> apply, string path, string current, object owner, Action refresh, bool isMixed)
        {
            // 选项来源：本剧情图的变量黑板（asset.variables）。显示「可读名字(类型)」，回写变量 id（保证准确性：改名不影响引用）。
            // 始终包含一个「未选择」占位项，对应 current 为空；这样 default 值恒在 choices 内，
            // 且绝不会把 null 作为 Dictionary 的 key（新建节点 variableId 默认即 null）。
            const string NoneLabel = "(未选择变量)";
            // 选项来源：本图局部变量 + 全局变量资产。全局变量加 [全局] 前缀以便区分。
            var vars = new List<StoryVariableDef>();
            if (model.Asset.variables != null) vars.AddRange(model.Asset.variables);
            var globalAsset = GlobalVariableLookup.GetAsset();
            if (globalAsset != null && globalAsset.variables != null) vars.AddRange(globalAsset.variables);
            var idToName = new Dictionary<string, string>();
            var nameToId = new Dictionary<string, string>();
            idToName[string.Empty] = NoneLabel;   // 空变量 → 占位项
            nameToId[NoneLabel] = string.Empty;
            foreach (var v in vars)
            {
                if (string.IsNullOrEmpty(v.id)) continue;
                bool isGlobal = globalAsset != null && globalAsset.variables != null && globalAsset.variables.Contains(v);
                var name = string.IsNullOrEmpty(v.name) ? v.id : v.name;
                // 显示名：全局变量加 [全局] 前缀，后接括号标注变量类型，如「[全局] 血量 (Int)」
                string prefix = isGlobal ? "[全局] " : "";
                string key = $"{prefix}{name} ({TypeLabel(v.type)})";
                int dup = 1;
                while (nameToId.ContainsKey(key)) key = $"{prefix}{name} ({TypeLabel(v.type)}) ({++dup})";
                idToName[v.id] = key;
                nameToId[key] = v.id;
            }
            // 兜底：当前值若不在选项内（如引用了已删除的变量），仍补入以免下拉丢失选中
            if (!string.IsNullOrEmpty(current) && !idToName.ContainsKey(current))
            {
                idToName[current] = current;
                nameToId[current] = current;
            }
            var choices = idToName.Values.ToList();
            idToName.TryGetValue(string.IsNullOrEmpty(current) ? string.Empty : current, out var currentName);
            if (string.IsNullOrEmpty(currentName)) currentName = NoneLabel;
            // 多选混合态：Placeholder 必须进 choices 副本，否则 DropdownField 构造抛 ArgumentException；占位仅作初始显示、不可写回。
            var ddChoices = new List<string>(choices);
            string ddDefault = currentName;
            if (isMixed) { ddChoices.Add(MixedPlaceholder); ddDefault = MixedPlaceholder; }
            var dd = new DropdownField(ddChoices, ddDefault); dd.AddToClassList("fd-control");
            dd.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == MixedPlaceholder) return;   // 占位项不可写回
                if (nameToId.TryGetValue(e.newValue, out var id))
                {
                    apply(path, id);
                    // 变量类型变化时，把 op/value 校正到该类型合法范围（如布尔复位为「设为」+ 开关值）
                    // 经 apply 广播，多选时也应用到全部选中节点。
                    NormalizeVarOpValue(model, owner, id, path, apply);
                    // 重建属性面板：op/value 控件随新变量类型实时更新（如布尔→true/false 下拉、无加减）
                    refresh?.Invoke();
                }
            });
            return dd;
        }

        /// <summary>解析变量类型：本图局部变量 + 全局变量资产（与变量下拉同一数据源）。
        /// 只查本图会让全局变量类型解析为 null → op 下拉退化（赋值只剩 Set、条件只剩 ==/!=），即「全局变量只能 Set」缺陷。</summary>
        private static VariableType? ResolveVarTypeIncludingGlobal(StoryGraphModel model, string varId)
        {
            var vars = new List<StoryVariableDef>();
            if (model?.Asset?.variables != null) vars.AddRange(model.Asset.variables);
            var globalAsset = GlobalVariableLookup.GetAsset();
            if (globalAsset != null && globalAsset.variables != null) vars.AddRange(globalAsset.variables);
            return FieldPanelLogic.ResolveVarType(vars, varId);
        }

        /// <summary>端口连线 → 获取变量节点显示名；无连线 / 源头不是获取变量节点返回 null（调用方回落常量）。</summary>
        private static string ResolvePortVarName(StoryGraphModel model, StoryNodeData node, string portId)
        {
            if (model == null || node == null) return null;
            foreach (var e in model.GetIncoming(node.id))
            {
                if (e.toPortId != portId) continue;
                var from = model.GetNode(e.fromNodeId);
                if (from is GetVariableNodeData gv && !string.IsNullOrEmpty(gv.variableId))
                    return StoryConstants.VariableName(gv.variableId);
            }
            return null;
        }

        /// <summary>切换变量后，把 op/value 校正到新变量类型合法范围（布尔复位为「设为」+ 开关值）。
        /// 决策逻辑在 <see cref="FieldPanelLogic.NormalizeOpValue"/>（纯函数，可单测）；本壳只负责经 apply 广播
        /// （多选时同样应用到全部选中节点）。</summary>
        private static void NormalizeVarOpValue(StoryGraphModel model, object owner, string varId, string varIdPath, Action<string, object> apply)
        {
            if (!(owner is SetVariableNodeData || owner is ConditionClause)) return;
            var vt = ResolveVarTypeIncludingGlobal(model, varId);
            string prefix = varIdPath.Substring(0, varIdPath.Length - "variableId".Length);

            var opField = owner.GetType().GetField("op", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var valField = owner.GetType().GetField("value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var (fixedOp, fixedVal) = FieldPanelLogic.NormalizeOpValue(
                vt,
                owner is ConditionClause,
                opField != null ? opField.GetValue(owner) as Enum : null,
                valField != null ? valField.GetValue(owner) as string : null);
            if (opField != null && fixedOp != null)
                apply(prefix + "op", fixedOp);
            if (valField != null && fixedVal != null)
                apply(prefix + "value", fixedVal);
        }

        /// <summary>生成策略键空间判定：资产位于任意名为 "StorySpawnStrategies" 的目录下即属于该键空间。
        /// 对齐运行时口径（Resources.LoadAll 无斜杠按名搜索整个树，故 Resources/Story/StorySpawnStrategies
        /// 的摆放偏差运行时能命中），并覆盖资产迁移工具的目标侧 AddressableStory/StorySpawnStrategies
        /// （迁移后策略不在 Resources 下，运行时经定位器 Addressables 通道解析）。
        /// 旧过滤 Contains("/Resources/StorySpawnStrategies/") 对这两种布局失明（下拉看不到策略）。</summary>
        internal static bool IsInStrategyKeySpace(string assetPath)
            => assetPath != null && assetPath.Contains("/StorySpawnStrategies/");

        private static VisualElement MakeSpawnStrategyField(StoryGraphModel model, StoryNodeData node, Action<string, object> apply, string path, string current, bool isMulti, bool isMixed, Action onStructuralChange)
        {
            // 下拉选项 = 全局搜索全部 DialogueBoxSpawnStrategyAsset（任意目录，业务侧无需把策略资产放进
            // 固定键空间目录也可被选中；资产未设 strategyKey 时用资产名作键；回写 strategyKey）。
            var assets = AssetDatabase.FindAssets("t:DialogueBoxSpawnStrategyAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => AssetDatabase.LoadAssetAtPath<DialogueBoxSpawnStrategyAsset>(p))
                .Where(a => a != null)
                .ToList();
            var keyToLabel = new Dictionary<string, string>();
            keyToLabel[string.Empty] = "(全局默认)";
            var choices = new List<string> { "(全局默认)" };
            foreach (var a in assets)
            {
                string key = string.IsNullOrEmpty(a.strategyKey) ? a.name : a.strategyKey;
                if (string.IsNullOrEmpty(key)) key = a.name;
                string label = key;
                int dup = 1;
                while (keyToLabel.ContainsKey(label)) label = $"{key} ({++dup})";
                keyToLabel[label] = key;
                choices.Add(label);
            }
            // 多选混合态：Unity 原生会把此类字段显示成「--」占位，选任意值都应用。
            // 若这里直接显示代表节点的当前值，想「统一成代表节点已是的值」（如全局默认）时控件值不变、不触发 change，广播不会发生。
            // 故混合态用占位文本，确保用户选任意真实项都触发 apply 广播到全部选中节点。
            string currentLabel;
            if (isMixed)
            {
                // 混合态：Placeholder 必须进 choices，否则 DropdownField 构造抛 ArgumentException；占位仅作初始显示、不可写回。
                choices.Add(MixedPlaceholder);
                currentLabel = MixedPlaceholder;
            }
            else
            {
                currentLabel = "(全局默认)";
                foreach (var kv in keyToLabel)
                    if (kv.Value == current) { currentLabel = kv.Key; break; }
            }
            var dd = new DropdownField(choices, currentLabel); dd.AddToClassList("fd-control");
            dd.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == MixedPlaceholder) return;   // 占位项不可写回
                // 用 choices 索引定位，确保「全局默认」(choices[0]) 一定走到 apply("")，
                // 不依赖 keyToLabel.TryGetValue（空字符串映射易漏，漏调 apply 会导致空值不落库、失焦回弹原策略）。
                int idx = choices.IndexOf(e.newValue);
                if (idx == 0)
                    apply(path, "");
                else if (idx > 0 && keyToLabel.TryGetValue(e.newValue, out var key))
                    apply(path, key);
                onStructuralChange?.Invoke(); // 重建面板，使显示与已写入的模型值（含空=全局默认）一致
            });
            MarkMixed(dd, isMixed);
            dd.RegisterCallback<FocusOutEvent>(_ => ScheduleRefresh(dd, isMulti, onStructuralChange));
            return dd;
        }

        /// <summary>样式资产字段：拖拽/选择已有 DialogueBoxStyleAsset，下方内联展开该资产的子字段（styleKey/template/intro/outro）可直接改，
        /// 并提供「在此节点新建样式资产」按钮（节点编辑器即资产组装器）。
        /// 内联子字段编辑仅在「选中节点都引用同一样式资产」时启用：此时改共享资产即天然批量到所有引用节点，无歧义。
        /// 多选且引用不同资产时禁用内联编辑（无单一值可广播），仅做引用批量设置（拖入/清空样式资产广播到全部选中节点）。</summary>
        private static VisualElement MakeStyleAssetField(StoryGraphModel model, IReadOnlyList<StoryNodeData> nodes, Action<string, object> apply, string path, DialogueBoxStyleAsset current, bool isMulti, bool isMixed, Action onStructuralChange)
        {
            var wrap = new VisualElement { name = "style-asset-field" };
            wrap.AddToClassList("fd-style-asset");
            wrap.style.flexGrow = 1;
            wrap.style.width = Length.Percent(100);

            // 1) 资产引用（拖拽 / 选择已有样式资产）：多选时经 apply 广播到全部选中节点。
            // 行标签已显示「样式」，故 ObjectField 自身标签置空，避免重复并把宽度留给内容。
            // 多选混合态：ObjectField 显示 null 占位（而非代表节点的值），tooltip 说明；并额外提供「统一清空」按钮，
            // 否则想「统一成全局默认（清空）」时控件值不变不会触发 change，其他节点不会被广播到。
            var objField = new ObjectField("") { objectType = typeof(DialogueBoxStyleAsset), value = isMixed ? null : current };
            objField.AddToClassList("fd-control");
            objField.style.flexGrow = 1;
            if (isMixed)
                objField.tooltip = "各节点样式不同；拖入资产将统一应用到全部选中节点；下方按钮可统一清空为全局默认";
            objField.RegisterValueChangedCallback(e =>
            {
                apply(path, e.newValue);
                onStructuralChange?.Invoke(); // 重建面板以显隐内联编辑区
            });
            objField.RegisterCallback<FocusOutEvent>(_ => ScheduleRefresh(wrap, isMulti, onStructuralChange));
            wrap.Add(objField);

            if (isMixed)
            {
                var clearBtn = new Button(() =>
                {
                    apply(path, null);              // 统一清空为全局默认（空引用），广播到全部选中节点
                    onStructuralChange?.Invoke();
                }) { text = "统一为全局默认（清空样式引用）" };
                clearBtn.AddToClassList("fd-apply-btn");
                wrap.Add(clearBtn);
            }

            // 2) 内联可编辑（已选资产且引用无歧义时）：单节点直接改；多选但所有节点引用同一资产时也启用，
            //    因为改的是共享资产、对所有引用节点的样式生效，等同于批量；仅「多选且引用不同资产」(isMixed) 时禁用。
            if (current != null && (!isMulti || !isMixed))
            {
                var fold = new Foldout { text = "样式数据（内联编辑）", value = true };
                fold.AddToClassList("fd-fold");
                fold.style.flexGrow = 1;
                fold.style.width = Length.Percent(100);
                fold.contentContainer.style.flexGrow = 1;
                fold.contentContainer.style.width = Length.Percent(100);
                // 去掉默认缩进，避免内联字段被挤到右侧
                fold.contentContainer.style.marginLeft = 0;
                fold.contentContainer.style.paddingLeft = 0;

                var so = new SerializedObject(current);
                foreach (var propName in new[] { "styleKey", "template", "introDuration", "outroDuration", "retainRatio" })
                {
                    var sp = so.FindProperty(propName);
                    if (sp == null) continue;
                    var pf = new UnityEditor.UIElements.PropertyField(sp, sp.displayName);
                    pf.AddToClassList("fd-control");
                    pf.style.flexGrow = 1;
                    pf.style.width = Length.Percent(100);
                    pf.Bind(so);
                    fold.Add(pf);
                }
                wrap.Add(fold);
            }
            else if (isMulti)
            {
                var note = new Label("（多选：内联编辑已禁用，修改引用将应用到全部选中节点）") { name = "style-multi-note" };
                note.AddToClassList("fd-summary");
                wrap.Add(note);
            }

            // 3) 一键新建样式资产（节点编辑器即资产组装器）：放到 Resources/StoryDialogueBoxStyles 以便运行时自动注册。
            // 多选时新建后广播引用到全部选中节点。
            var createBtn = new Button(() =>
            {
                var dir = "Assets/Resources/StoryDialogueBoxStyles";
                if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Resources", "StoryDialogueBoxStyles");
                var uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/Style_{nodes[0].id}.asset");
                var asset = ScriptableObject.CreateInstance<DialogueBoxStyleAsset>();
                asset.styleKey = System.IO.Path.GetFileNameWithoutExtension(uniquePath);
                AssetDatabase.CreateAsset(asset, uniquePath);
                AssetDatabase.SaveAssets();
                apply(path, asset);
                onStructuralChange?.Invoke();
            }) { text = "在此节点新建样式资产" };
            createBtn.AddToClassList("fd-create-btn");
            wrap.Add(createBtn);

            return wrap;
        }
    }
}
