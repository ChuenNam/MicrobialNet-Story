namespace MicrobialNet.Story
{
    /// <summary>结束节点类型。</summary>
    internal enum EndType
    {
        /// <summary>正常结束当前剧情。</summary>
        Normal,
        /// <summary>结束并跳转到指定章节（配合 jumpToChapter 字段）。</summary>
        JumpChapter,
    }

    /// <summary>条件比较运算符。</summary>
    internal enum CompareOp
    {
        Equal,
        NotEqual,
        Greater,
        GreaterEqual,
        Less,
        LessEqual,
    }

    /// <summary>赋值运算符。</summary>
    internal enum AssignOp
    {
        Set,
        Add,
        Sub,
        Mul,
        Div,
    }

    /// <summary>多条件组合方式。</summary>
    internal enum ConditionCombine
    {
        /// <summary>全部满足（AND）。</summary>
        All,
        /// <summary>任一满足（OR）。</summary>
        Any,
    }
}
