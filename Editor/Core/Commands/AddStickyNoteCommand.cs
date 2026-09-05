using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>新建便签：在指定画布坐标创建一个默认大小的便签。</summary>
    internal sealed class AddStickyNoteCommand : IGraphCommand
    {
        private readonly Rect _rect;
        private readonly string _title;
        private readonly string _text;
        private string _createdId;

        public string Description => "新建便签";
        public GraphChange Change => new GraphChange(GraphChangeType.Reset);

        public AddStickyNoteCommand(Rect rect, string title = "便签", string text = "")
        {
            _rect = rect;
            _title = title;
            _text = text;
        }

        public void Execute(StoryGraphModel model)
        {
            Undo.RecordObject(model.Asset, Description);
            var n = new StoryStickyNote
            {
                id = "n_" + System.Guid.NewGuid().ToString("N").Substring(0, 10),
                title = _title,
                text = _text,
                rect = _rect,
                theme = 0,
            };
            model.Asset.stickyNotes.Add(n);
            _createdId = n.id;
        }
    }
}
