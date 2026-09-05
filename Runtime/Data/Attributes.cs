using System;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 标记一个剧情节点数据类型。编辑器据此自动生成创建菜单、节点颜色与属性面板。
    /// 新增节点类型 = 写一个带此特性的数据类，无需改动编辑器主干代码。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class StoryNodeAttribute : Attribute
    {
        public StoryNodeAttribute(string title) { Title = title; }

        /// <summary>节点类型显示名（显示在创建菜单与节点标题栏）。</summary>
        public string Title { get; }

        /// <summary>分类（基础 / 逻辑 / 演出 / 辅助），用于创建菜单分组。</summary>
        public string Category { get; set; } = "基础";

        /// <summary>节点标题栏主色（十六进制，如 #378ADD）。</summary>
        public string ColorHex { get; set; } = "#888780";

        /// <summary>同级排序权重，越小越靠前。</summary>
        public int Order { get; set; } = 100;
    }

    /// <summary>
    /// 标记节点数据类中的可编辑字段，属性面板据此生成对应控件并按 Order 排序。
    /// 未标记 [StoryField] 的字段不会被面板暴露。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class StoryFieldAttribute : Attribute
    {
        public StoryFieldAttribute(string label) { Label = label; }

        /// <summary>字段显示名。</summary>
        public string Label { get; }

        /// <summary>悬停提示。</summary>
        public string Tooltip { get; set; }

        /// <summary>排序权重，越小越靠前。</summary>
        public int Order { get; set; } = 0;

        /// <summary>
        /// 标记为预留字段：本期未实现。属性面板以灰显占位呈现，
        /// 让使用者知道「这里以后会有」，也提前占好版面，避免后期重排。
        /// </summary>
        public bool Future { get; set; } = false;
    }

    /// <summary>字段应渲染为「角色下拉」（选项来自项目内 StoryCharacterAsset）。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class CharacterPickerAttribute : Attribute { }

    /// <summary>
    /// 多行文本字段。可启用 TMP 富文本工具条与字数限制提示。
    /// CountLimitFrom 指向同一节点类上返回 int 的方法名（如对话框容量）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class MultilineTextAttribute : Attribute
    {
        public int Lines { get; set; } = 4;
        public bool RichTextToolbar { get; set; } = false;
        public string CountLimitFrom { get; set; }
    }

    /// <summary>浮点滑块字段。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class RangeSliderAttribute : Attribute
    {
        public RangeSliderAttribute(float min, float max) { Min = min; Max = max; }
        public float Current { get; set; }
        public float Min { get; }
        public float Max { get; }
    }

    /// <summary>字段应渲染为「事件名下拉」（事件表由运行时/项目注册提供）。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class StoryEventPickerAttribute : Attribute { }

    /// <summary>字段应渲染为「变量下拉」（选项来自本剧情图的变量黑板 asset.variables，显示 name 回写 id）。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class VariablePickerAttribute : Attribute { }

    /// <summary>字段为本地化 Key（本期未实现多语言，灰显占位）。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class LocalizedKeyAttribute : Attribute { }

    /// <summary>字段应渲染为「生成策略下拉」：列出 Resources/StorySpawnStrategies 下的策略资产，回写其 strategyKey。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SpawnStrategyPickerAttribute : Attribute { }

    /// <summary>
    /// 字段分组标题：属性面板在带此特性的字段之前渲染一个分组标签（如「对话框外观」），
    /// 纯视觉分组，不引入新的数据结构。与 [StoryField] 配合，把一组相关字段在节点面板上归拢。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class StorySectionAttribute : Attribute
    {
        public StorySectionAttribute(string title) { Title = title; }
        /// <summary>分组显示名。</summary>
        public string Title { get; }
    }
}
