using MicrobialNet.Story;
using UnityEditor;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>删除便签。</summary>
    internal sealed class RemoveStickyNoteCommand : IGraphCommand
    {
        private readonly string _noteId;

        public string Description => "删除便签";
        public GraphChange Change => new GraphChange(GraphChangeType.Reset);

        public RemoveStickyNoteCommand(string noteId) => _noteId = noteId;

        public void Execute(StoryGraphModel model)
        {
            var n = model.Asset.stickyNotes.Find(x => x.id == _noteId);
            if (n == null) return;
            Undo.RecordObject(model.Asset, Description);
            model.Asset.stickyNotes.Remove(n);
        }
    }
}
