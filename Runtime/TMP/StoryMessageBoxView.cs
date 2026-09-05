using MicrobialNet.Story;
using MicrobialNet.Story.UI;
using TMPro;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 消息框内容视图（story-end 样式）。显示标题与正文（仅 story-end 使用；错误已不再以对话框呈现，改走控制台日志）。
    /// </summary>
    public sealed class StoryMessageBoxView : MonoBehaviour, IDialogueBoxView, IDialogueBoxRecyclable
    {
        /// <summary>由 StoryView 传入的数据包。</summary>
        internal sealed class Payload
        {
            public string title;
            public string body;
            public bool isError;
        }

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;

        internal void InitRefs(TextMeshProUGUI title, TextMeshProUGUI body)
        {
            _title = title;
            _body = body;
        }

        // 同 StoryLineBoxView：克隆体的私有引用不会被序列化转移，必须在本实例上按名称找回。
        private void ResolveRefs()
        {
            if (_title != null && _body != null) return;
            foreach (var t in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t.name == "Title") _title = t;
                else if (t.name == "Body") _body = t;
            }
        }

        public void Setup(DialogueBoxHandle handle, object payload)
        {
            ResolveRefs();
            var p = payload as Payload;
            if (p == null) return;
            _title.text = p.isError ? "⚠ 剧情错误" : "—— 剧情结束 ——";
            _title.color = p.isError ? Color.red : Color.white;
            _body.text = p.body ?? string.Empty;
        }

        /// <summary>回收前由管理器回调：清空文本，供池化复用。</summary>
        public void OnRecycle()
        {
            if (_title != null) _title.text = string.Empty;
            if (_body != null) _body.text = string.Empty;
        }
    }
}
