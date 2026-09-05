using MicrobialNet.Story;
using System;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>
    /// 批量导入包装命令：把「直接 Undo.RecordObject 直改资产」的导入操作统一收口到命令层，
    /// 使导入也经模型标脏 / 重建索引 / 广播 Changed（与交互编辑一致的撤销·刷新路径，见 02 §4.3）。
    /// 实际的数据改写仍由既有静态导入器完成（其内部已做 Undo.RecordObject），本命令只负责把调用
    /// 接入统一的命令抽象，避免导入绕过 IGraphCommand。
    /// </summary>
    internal sealed class ImportCommand : IGraphCommand
    {
        private readonly Action<StoryGraphAsset> _mutate;
        private readonly string _desc;

        public string Description => _desc;

        /// <summary>导入会整体替换图内容，视作整体重建。</summary>
        public GraphChange Change => new GraphChange(GraphChangeType.Reset);

        public ImportCommand(string description, Action<StoryGraphAsset> mutate)
        {
            _desc = description;
            _mutate = mutate;
        }

        public void Execute(StoryGraphModel model)
        {
            _mutate(model.Asset);
        }
    }
}
