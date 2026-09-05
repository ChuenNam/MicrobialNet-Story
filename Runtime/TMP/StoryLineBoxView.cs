using System;
using MicrobialNet.Story;
using MicrobialNet.Story.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情对白框内容视图（story-line 样式）。承载讲述者名 / 正文 / 立绘 / 继续指示，
    /// 打字机播放，点击面板：打字中→全显，待推进→请求 Advance。
    /// </summary>
    public sealed class StoryLineBoxView : MonoBehaviour, IDialogueBoxView, IPointerDownHandler, IDialogueBoxRecyclable
    {
        /// <summary>由 StoryView 传入的数据包。</summary>
        internal sealed class Payload
        {
            public StoryFlow.Line line;
            public Action onAdvance;
            /// <summary>打字机逐可见字符延迟序列（秒）。null/空表示即时全显。</summary>
            public float[] schedule;
        }

        private TextMeshProUGUI _speaker;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _hint;
        private Image _portrait;
        private string _fullText = string.Empty;
        private float[] _schedule;          // 逐可见字符延迟（秒），长度 == 可见字符数
        private int _visTotal;               // 可见字符总数（= _schedule.Length）
        private float _elapsed;
        private int _shown;                  // 已揭示的可见字符数（= TMP maxVisibleCharacters）
        private bool _typing;
        private Action _onAdvance;
        private DialogueBoxHandle _handle;

        internal void InitRefs(TextMeshProUGUI speaker, TextMeshProUGUI body, TextMeshProUGUI hint, Image portrait)
        {
            _speaker = speaker;
            _body = body;
            _hint = hint;
            _portrait = portrait;
        }

        // 运行时由 DialogueBoxManager 克隆模板弹出；克隆体的私有引用不会被序列化转移，
        // 不能在模板构建期靠 InitRefs 一劳永逸，必须在本实例（克隆）上按名称找回子物体引用。
        private void ResolveRefs()
        {
            if (_speaker != null && _body != null && _hint != null && _portrait != null) return;
            foreach (var t in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t.name == "Speaker") _speaker = t;
                else if (t.name == "Body") _body = t;
                else if (t.name == "Hint") _hint = t;
            }
            var portrait = transform.Find("Portrait");
            if (portrait != null) _portrait = portrait.GetComponent<Image>();
        }

        public void Setup(DialogueBoxHandle handle, object payload)
        {
            ResolveRefs();
            var p = payload as Payload;
            if (p == null) return;
            _handle = handle;
            _onAdvance = p.onAdvance;

            var line = p.line;
            _speaker.text = line.SpeakerName;
            if (StoryConstants.TryParseColor(line.Speaker.colorHex, out var nameColor))
                _speaker.color = nameColor;
            else
                _speaker.color = Color.white;
            ApplyPortrait(line.Speaker);

            _fullText = line.Text ?? string.Empty;
            _schedule = p.schedule;
            _visTotal = _schedule != null ? _schedule.Length : 0;
            _shown = 0;
            _elapsed = 0f;
            _typing = _visTotal > 0;
            _body.text = _fullText;
            _body.maxVisibleCharacters = 0;
            // 空文本（无可见字符）无需打字，直接可继续（与原行为一致）。
            _hint.gameObject.SetActive(!_typing);
        }

        private void ApplyPortrait(StoryConstants.CharacterViewModel vm)
        {
            if (_portrait == null) return;
            if (vm.avatar != null)
            {
                _portrait.sprite = vm.avatar;
                _portrait.color = Color.white;
                _portrait.enabled = true;
            }
            else if (StoryConstants.TryParseColor(vm.colorHex, out var col))
            {
                _portrait.sprite = null;
                _portrait.color = col;
                _portrait.enabled = true;
            }
            else
            {
                _portrait.enabled = false;
            }
        }

        private void Update()
        {
            if (!_typing) return;
            _elapsed += Time.deltaTime;
            // 按逐可见字符延迟推进；_schedule[_shown] 即「揭示第 _shown 个可见字符前应等待的时长」。
            while (_shown < _visTotal && _elapsed >= _schedule[_shown])
            {
                _elapsed -= _schedule[_shown];
                _shown++;
                _body.maxVisibleCharacters = _shown;
            }
            if (_shown >= _visTotal)
            {
                _typing = false;
                _body.maxVisibleCharacters = _visTotal; // 全部揭示
                _hint.gameObject.SetActive(true);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_typing)
            {
                _typing = false;
                _shown = _visTotal;
                _body.maxVisibleCharacters = _visTotal; // 跳过打字，立即全显
                _hint.gameObject.SetActive(true);
                return;
            }
            // 先关旧框、再推进：BeginClose 会更新管理器的 _retainUntil（离场结束时刻），
            // 新框 Open 时据此延迟出现（留存过渡），否则新框会立即出现覆盖正在淡出的旧框。
            // persistent = true 时（由生成策略或调用方设置），点击继续只推进剧情、不关闭自身，
            // 从而实现「一串对话保留显示」；默认行为仍会关闭当前框。
            if (!(_handle?.Spec?.persistent ?? false))
                _handle?.Close();
            _onAdvance?.Invoke();
        }

        /// <summary>回收前由管理器回调：重置打字机状态与文本，供池化复用。</summary>
        public void OnRecycle()
        {
            _typing = false;
            _shown = 0;
            _elapsed = 0f;
            _fullText = string.Empty;
            _schedule = null;
            _visTotal = 0;
            _onAdvance = null;
            _handle = null;
            if (_body != null) { _body.text = string.Empty; _body.maxVisibleCharacters = int.MaxValue; }
            if (_speaker != null) _speaker.text = string.Empty;
            if (_hint != null) _hint.gameObject.SetActive(false);
        }
    }
}
