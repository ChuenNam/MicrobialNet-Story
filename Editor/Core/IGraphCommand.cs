namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 图编辑命令。所有对 StoryGraphAsset 的修改都必须经由命令执行，
    /// 命令内部负责记录 Unity 原生 Undo。视图层禁止直接改数据。
    /// </summary>
    internal interface IGraphCommand
    {
        /// <summary>人类可读描述（Undo 栈显示用）。</summary>
        string Description { get; }

        /// <summary>本次操作造成的变更（用于视图增量刷新）。</summary>
        GraphChange Change { get; }

        /// <summary>在给定模型上执行。应在内部调用 Undo.RecordObject 后再改数据。</summary>
        void Execute(StoryGraphModel model);
    }
}
