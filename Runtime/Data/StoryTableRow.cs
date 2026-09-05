using System.Collections.Generic;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情表中的一行（一句对白 + 其选项）。可序列化，存于 <see cref="StoryTableAsset.rows"/>。
    /// 选项目标的 <see cref="StoryTableChoice.targetRowId"/> 引用本结构的 <see cref="id"/>（稳定行键），
    /// 而非 Excel 行号——插入/删除行不会让下游跳转失效。
    /// </summary>
    [System.Serializable]
    public sealed class StoryTableRow
    {
        /// <summary>稳定行键。作者可在表格「id」列手填（友好可读），缺省时由导入器自动生成。创建后保持稳定。</summary>
        public string id;

        /// <summary>讲述者 ID（角色 characterId）；空或缺失交由运行时按旁白处理。</summary>
        public string speaker;

        /// <summary>对白文本。</summary>
        public string text;

        /// <summary>跳转目标行 id（可选）。非空时本行对白播完后直接跳转到目标行（自由连线的「对白→对白」跳转写这里，
        /// 对应 Excel「跳转ID」列）；为空则按表内顺序接下一行（线性回退）；**填「/」= 终止标识**（本行无后继，
        /// 是输出端，表节点暴露 exit_ 出口端口）。选项行跳转见 <see cref="StoryTableChoice.targetRowId"/>。</summary>
        public string targetRowId;

        /// <summary>是否在派生的「带文字」选择节点上显示本行对白文字（分支行用；默认 true）。
        /// false = 仅显示选项、不显示行内正文。仅编辑器侧 SO 字段（当前不进 Excel 列）。</summary>
        public bool showText = true;

        /// <summary>该句下方的选项（独占一行书写）；可空。</summary>
        public List<StoryTableChoice> choices = new List<StoryTableChoice>();
    }
}
