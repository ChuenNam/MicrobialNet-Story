using System;
using System.Collections.Generic;
using MicrobialNet.Story;
using MicrobialNet.Story.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 选项框内容视图（story-choice 样式）。把可见选项渲染为可点击按钮；点击回传 OptionId。
    /// 「带文字」选择节点：顶部对白（Prompt）走与对白节点一致的打字机（逐字符 schedule），
    /// 选项按钮在 Prompt 打字结束后才生成；打字中点按可跳过（立即全显 + 出选项）。
    /// </summary>
    public sealed class StoryChoiceBoxView : MonoBehaviour, IDialogueBoxView, IDialogueBoxRecyclable, IPointerDownHandler
    {
        /// <summary>由 StoryView 传入的数据包。</summary>
        internal sealed class Payload
        {
            public IReadOnlyList<StoryFlow.Choice> choices;
            public Action<string> onChoose;
            /// <summary>Prompt 逐字符延迟序列（秒）。null/空 = 即时全显（选项立即出现，旧行为）。</summary>
            public float[] promptSchedule;
        }

        private Transform _choicesRoot;
        private readonly List<Button> _buttons = new List<Button>();
        private GameObject _prompt;
        private TextMeshProUGUI _promptTmp;
        private DialogueBoxHandle _handle;
        private Payload _payload;

        // ── Prompt 打字机状态（同 StoryLineBoxView 推进逻辑）──
        private float[] _schedule;
        private int _visTotal;
        private float _elapsed;
        private int _shown;
        private bool _typing;

        /// <summary>选项按钮模板：拖入后按钮外观完全由模板控制（仅填文本）。为空时回落代码生成（兼容旧场景）。</summary>
        [Header("模板（为空时回落代码生成）")]
        [Tooltip("选项按钮模板：需含 Button 组件 + 一个 TextMeshProUGUI 子物体（文本会被自动填充）。")]
        [SerializeField] private Transform choiceButtonTemplate;

        /// <summary>讲述对白（Prompt）模板：渲染在选项上方的说明。为空时回落代码生成。</summary>
        [Tooltip("讲述对白模板：需含一个 TextMeshProUGUI 子物体（文本会被自动填充）。")]
        [SerializeField] private Transform promptTemplate;

        internal void InitRefs(Transform choicesRoot) => _choicesRoot = choicesRoot;

        // 同 StoryLineBoxView：克隆体的私有引用不会被序列化转移，必须在本实例上按名称找回。
        private void ResolveRefs()
        {
            if (_choicesRoot != null) return;
            var c = transform.Find("Choices");
            if (c != null) _choicesRoot = c;
        }

        public void Setup(DialogueBoxHandle handle, object payload)
        {
            ResolveRefs();
            var p = payload as Payload;
            if (p == null) return;
            _handle = handle;
            _payload = p;
            Clear();
            _schedule = p.promptSchedule;
            _visTotal = _schedule != null ? _schedule.Length : 0;
            _elapsed = 0f;
            _shown = 0;

            // 「带文字」选择节点：行内对白渲染在选项上方（取第一个非空 Prompt），带打字机则逐字揭示
            if (p.choices != null)
                foreach (var c in p.choices)
                    if (c != null && !string.IsNullOrEmpty(c.Prompt))
                    {
                        AddPromptLabel(c.Prompt);
                        break;
                    }

            // 仅当「确实渲染了可打字文字（Prompt 非空且视图已挂 TMP）」才启动打字——选项在文字打完后出现；
            // 无文字（未勾选显示文字 / 无 Prompt / 无 TMP 可挂）一律立即生成选项，避免无文字也延迟出选项。
            _typing = _promptTmp != null && _visTotal > 0;
            if (_typing)
                _promptTmp.maxVisibleCharacters = 0; // 打字开始：从 0 逐字揭示
            else
                BuildButtons();
        }

        /// <summary>生成全部选项按钮（Prompt 打字结束后调用；无打字时 Setup 立即调用）。</summary>
        private void BuildButtons()
        {
            if (_payload?.choices == null) return;
            foreach (var c in _payload.choices)
            {
                var btn = MakeButton(c.Text);
                var id = c.OptionId;
                btn.onClick.AddListener(() =>
                {
                    Clear();
                    // 先关选项框（更新管理器 _retainUntil），再回传选择触发下一节点——
                    // 保证下一框 Open 时能按留存过渡延迟出现，不覆盖正在淡出的选项框
                    _handle?.Close();
                    _payload?.onChoose?.Invoke(id);
                });
                _buttons.Add(btn);
            }
        }

        // ── Prompt 打字推进（与对白框同 schedule 语义）──

        private void Update()
        {
            if (!_typing || _promptTmp == null) return;
            _elapsed += Time.deltaTime;
            while (_shown < _visTotal && _elapsed >= _schedule[_shown])
            {
                _elapsed -= _schedule[_shown];
                _shown++;
                _promptTmp.maxVisibleCharacters = _shown;
            }
            if (_shown >= _visTotal)
            {
                _typing = false;
                _promptTmp.maxVisibleCharacters = _visTotal;
                BuildButtons(); // 文字打完 → 选项出现
            }
        }

        /// <summary>打字中任意点按 = 跳过打字：立即全显 Prompt 并生成选项（与对白框「点击跳过打字」一致）。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_typing) return;
            _typing = false;
            if (_promptTmp != null) _promptTmp.maxVisibleCharacters = _visTotal;
            BuildButtons();
        }

        /// <summary>在选项列表上方渲染一段说明文字（与按钮同参与父级自动布局）。
        /// 配置了 promptTemplate 时克隆模板并填充文本，否则回落代码生成。返回 TMP 供打字机推进 maxVisibleCharacters。</summary>
        private void AddPromptLabel(string text)
        {
            TextMeshProUGUI tmp = null;
            if (promptTemplate != null)
            {
                var go = Instantiate(promptTemplate, _choicesRoot, false);
                go.name = "Prompt";
                go.gameObject.SetActive(true); // 模板常设为 inactive 防自身显示；克隆必须激活
                tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) tmp.text = text;
                _prompt = go.gameObject; // Instantiate(Transform) 返回 Transform，转 GameObject 供 Clear 销毁
            }
            else
            {
                var go2 = new GameObject("Prompt");
                go2.transform.SetParent(_choicesRoot, false);
                _prompt = go2;
                var le = go2.AddComponent<LayoutElement>();
                le.minHeight = 48;
                le.preferredHeight = 48;
                var t2 = go2.AddComponent<TextMeshProUGUI>();
                t2.font = StoryFontResolver.Resolve();
                t2.alignment = TextAlignmentOptions.Left;
                t2.raycastTarget = false;
                t2.fontSize = 22;
                t2.color = new Color(1f, 1f, 1f, 0.95f);
                t2.text = text;
                t2.enableWordWrapping = true;
                tmp = t2;
            }
            if (tmp != null)
            {
                tmp.maxVisibleCharacters = int.MaxValue; // 默认全显（打字模式由 Setup 置 0 启动）
                tmp.enableWordWrapping = true;
                _promptTmp = tmp;
            }
        }

        private Button MakeButton(string text)
        {
            // 模板路径：克隆按钮模板并填充文本——按钮外观（底色/圆角/字号/高度/图标等）完全由模板控制
            if (choiceButtonTemplate != null)
            {
                var go = Instantiate(choiceButtonTemplate, _choicesRoot, false);
                go.name = "Choice";
                go.gameObject.SetActive(true); // 模板常设为 inactive 防自身显示；克隆必须激活
                var t = go.GetComponentInChildren<TextMeshProUGUI>(true);
                if (t != null) t.text = text;
                var btn = go.GetComponent<Button>();
                if (btn == null)
                {
                    // 模板缺失 Button 组件：销毁并回落代码生成（避免后续 onClick 空引用）
                    Destroy(go);
                }
                else
                {
                    return btn;
                }
            }
            var go2 = new GameObject("Choice");
            go2.transform.SetParent(_choicesRoot, false);
            var btn2 = go2.AddComponent<Button>();
            var img = go2.AddComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.7f, 1f);
            var le = go2.AddComponent<LayoutElement>();
            le.minHeight = 44;
            le.preferredHeight = 44;

            var label = new GameObject("Label");
            label.transform.SetParent(go2.transform, false);
            var t2 = label.AddComponent<TextMeshProUGUI>();
            t2.font = StoryFontResolver.Resolve();
            t2.alignment = TextAlignmentOptions.Center;
            t2.raycastTarget = false;
            t2.fontSize = 20;
            t2.text = text;
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            return btn2;
        }

        private void Clear()
        {
            _typing = false;
            _promptTmp = null;
            _schedule = null;
            _visTotal = 0;
            if (_prompt != null) { Destroy(_prompt); _prompt = null; }
            foreach (var b in _buttons)
                if (b != null) Destroy(b.gameObject);
            _buttons.Clear();
        }

        /// <summary>回收前由管理器回调：销毁动态生成的选项按钮，避免池化复用残留。</summary>
        public void OnRecycle()
        {
            Clear();
            _payload = null;
        }
    }
}
