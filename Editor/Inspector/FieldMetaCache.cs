using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MicrobialNet.Story;

namespace MicrobialNet.Story.EditorTools.Inspector
{
    /// <summary>
    /// 一个字段的反射元数据包（按 <see cref="FieldInfo"/> 缓存一次，面板每次重建直接复用）：
    /// [StoryField]（字段面板主特性）、[StorySection]（分组标题）、控件选择特性与 [MultilineText]/[RangeSlider] 等渲染参数。
    /// 特性实例为编译期定义、运行期只读，缓存安全。
    /// </summary>
    internal sealed class FieldMeta
    {
        public FieldInfo Field;
        public StoryFieldAttribute StoryField;
        public StorySectionAttribute Section;

        // —— 控件选择与渲染参数（null = 无该特性，判定逻辑与 FieldDrawerRegistry 原顺序一致）——
        public CharacterPickerAttribute CharacterPicker;
        public StoryEventPickerAttribute EventPicker;
        public VariablePickerAttribute VariablePicker;
        public SpawnStrategyPickerAttribute SpawnStrategyPicker;
        public MultilineTextAttribute MultilineText;
        public RangeSliderAttribute RangeSlider;

        /// <summary>字段类型是否为「元素含 [StoryField] 成员的 List&lt;T&gt;」（选项列表 / 条件组判定，含嵌套成员元数据）。</summary>
        public bool IsListOfEditable;
        /// <summary>元素类型为可编辑列表时的成员元数据（按 Order 排好）；否则为空列表。</summary>
        public List<FieldMeta> ListMembers = new List<FieldMeta>();

        public bool HasStoryField => StoryField != null;
    }

    /// <summary>
    /// 反射元数据缓存（类型 → 字段元数据列表，含 [StoryField] 过滤与 Order 排序）。
    ///
    /// 动机（P4 / L0）：FieldDrawerRegistry 面板每次重建对每个字段/列表成员做
    /// GetFields + GetCustomAttribute，策划高频编辑（结构变更即重建）时是可测量的热路径开销。
    /// 本类把「类型 → 带特性的字段（排序后）」「列表元素成员」「IsListOfEditable 判定」「控件选择特性」
    /// 全部收敛为一次性计算 + 静态缓存（节点数据类型在编辑器会话内不变，缓存无失效问题）。
    /// 线程模型：仅编辑器主线程访问（面板构建路径），无需加锁。
    /// </summary>
    internal static class FieldMetaCache
    {
        private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly Dictionary<Type, List<FieldMeta>> ByType =
            new Dictionary<Type, List<FieldMeta>>();

        /// <summary>
        /// 取某类型的可编辑字段元数据（含 [StoryField] 的公开实例字段，按 Order 升序；含 [StorySection] 与控件选择特性）。
        /// 首次调用触发反射构建，之后直接返回缓存列表（调用方勿修改返回列表）。
        /// </summary>
        public static List<FieldMeta> GetFields(Type t)
        {
            if (t == null) return new List<FieldMeta>();
            if (!ByType.TryGetValue(t, out var list))
            {
                list = Build(t);
                ByType[t] = list;
            }
            return list;
        }

        /// <summary>判断类型是否为「元素含 [StoryField] 成员的 List&lt;T&gt;」。判定结果随元素元数据一并缓存，热路径零反射。</summary>
        public static bool IsListOfEditable(Type t)
        {
            if (t == null || !t.IsGenericType || t.GetGenericTypeDefinition() != typeof(List<>)) return false;
            // GetFields(元素类型) 顺带完成元素元数据缓存；判定复用其结果。
            return GetFields(t.GetGenericArguments()[0]).Count > 0;
        }

        private static List<FieldMeta> Build(Type t)
        {
            var raw = t.GetFields(BF);
            var metas = new List<FieldMeta>(raw.Length);
            foreach (var f in raw)
            {
                var sf = f.GetCustomAttribute<StoryFieldAttribute>();
                if (sf == null) continue; // 无 [StoryField]：不进自动面板（与原 Where 过滤一致）

                var m = new FieldMeta
                {
                    Field = f,
                    StoryField = sf,
                    Section = f.GetCustomAttribute<StorySectionAttribute>(),
                    CharacterPicker = f.GetCustomAttribute<CharacterPickerAttribute>(),
                    EventPicker = f.GetCustomAttribute<StoryEventPickerAttribute>(),
                    VariablePicker = f.GetCustomAttribute<VariablePickerAttribute>(),
                    SpawnStrategyPicker = f.GetCustomAttribute<SpawnStrategyPickerAttribute>(),
                    MultilineText = f.GetCustomAttribute<MultilineTextAttribute>(),
                    RangeSlider = f.GetCustomAttribute<RangeSliderAttribute>(),
                };

                // 可编辑列表：递归构建元素成员元数据（元素类型为 ChoiceOption/ConditionClause 等小类，递归深度 1 层即触底）。
                if (m.Field.FieldType.IsGenericType && m.Field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    m.ListMembers = GetFields(m.Field.FieldType.GetGenericArguments()[0]);
                    m.IsListOfEditable = m.ListMembers.Count > 0;
                }
                metas.Add(m);
            }
            metas.Sort((a, b) => a.StoryField.Order.CompareTo(b.StoryField.Order));
            return metas;
        }
    }
}
