using System.Collections;
using UnityEngine;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 对话框运行时实例（挂在预制体根上）。负责自身生命周期状态机、
    /// CanvasGroup 淡入淡出、定位应用、自动关闭计时。编排由 DialogueBoxManager 完成。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(RectTransform))]
    public sealed class DialogueBox : MonoBehaviour
    {
        /// <summary>当前生命周期状态。</summary>
        public DialogueBoxState State { get; internal set; } = DialogueBoxState.Pooled;

        internal DialogueBoxHandle handle;
        internal DialogueBoxManager manager;
        internal DialogueBoxSpec spec;
        internal string styleKey;

        private CanvasGroup _cg;
        private RectTransform _rt;
        private float _introDuration;
        private float _outroDuration;
        private Coroutine _anim;

        private void Awake()
        {
            _cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _rt = (RectTransform)transform;
        }

        internal void Configure(float introDuration, float outroDuration)
        {
            _introDuration = introDuration;
            _outroDuration = outroDuration;
        }

        /// <summary>本次配置的离场动画时长（供管理器计算留存过渡：下一框延迟 = 离场时长 × 留存占比）。</summary>
        internal float OutroDuration => _outroDuration;
        // ── 打开 ──
        internal void BeginOpen()
        {
            State = DialogueBoxState.Spawning;
            ApplyPosition();
            _cg.alpha = 0f;
            SetInteractive(false);
            State = DialogueBoxState.Opening;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(PlayIntro());
        }

        private IEnumerator PlayIntro()
        {
            float t = 0f;
            while (t < _introDuration)
            {
                t += Time.deltaTime;
                _cg.alpha = Mathf.Clamp01(t / _introDuration);
                yield return null;
            }
            _cg.alpha = 1f;
            State = DialogueBoxState.Open;
            manager.NotifyOpened(handle);
            if (spec != null && spec.autoCloseSeconds > 0f)
                StartCoroutine(AutoCloseCountdown());
        }

        private IEnumerator AutoCloseCountdown()
        {
            yield return new WaitForSeconds(spec.autoCloseSeconds);
            if (State == DialogueBoxState.Open)
                manager.RequestClose(handle, immediate: false);
        }

        // ── 关闭 ──
        internal void BeginClose(bool immediate)
        {
            if (State == DialogueBoxState.Closing ||
                State == DialogueBoxState.Pooled ||
                State == DialogueBoxState.Destroyed) return;
            State = DialogueBoxState.Closing;
            SetInteractive(false);
            if (_anim != null) StopCoroutine(_anim);
            if (immediate || _outroDuration <= 0f)
                FinishClose();
            else
                _anim = StartCoroutine(PlayOut());
        }

        private IEnumerator PlayOut()
        {
            float t = 0f;
            while (t < _outroDuration)
            {
                t += Time.deltaTime;
                _cg.alpha = 1f - Mathf.Clamp01(t / _outroDuration);
                yield return null;
            }
            FinishClose();
        }

        private void FinishClose()
        {
            _cg.alpha = 0f;
            manager.NotifyClosed(handle);
        }

        // ── 交互开关（尊重当前状态）──
        internal void SetInteractive(bool v)
        {
            if (_cg == null) return;
            bool on = v && State == DialogueBoxState.Open;
            _cg.interactable = on;
            _cg.blocksRaycasts = on;
        }

        // ── 定位 ──
        internal void ApplyPosition()
        {
            if (spec == null || spec.position == null || spec.position.mode == DialogueBoxPositionMode.Free) return;
            if (spec.position.mode == DialogueBoxPositionMode.ScreenAnchor)
            {
                var a = AnchorToVector(spec.position.anchor);
                _rt.anchorMin = a;
                _rt.anchorMax = a;
                _rt.pivot = a;
                _rt.anchoredPosition = spec.position.offset;
            }
            else if (spec.position.mode == DialogueBoxPositionMode.WorldFollow)
            {
                UpdateWorldFollow();
            }
        }

        internal void UpdateWorldFollow()
        {
            if (spec == null || spec.position == null ||
                spec.position.mode != DialogueBoxPositionMode.WorldFollow) return;
            var cam = spec.position.camera ? spec.position.camera : Camera.main;
            if (cam == null || spec.position.followTarget == null) return;
            Vector3 screen = cam.WorldToScreenPoint(spec.position.followTarget.position);
            _rt.anchorMin = new Vector2(0.5f, 0.5f);
            _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.anchoredPosition = (Vector2)screen + spec.position.offset;
        }

        private static Vector2 AnchorToVector(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return new Vector2(0f, 1f);
                case TextAnchor.UpperCenter: return new Vector2(0.5f, 1f);
                case TextAnchor.UpperRight: return new Vector2(1f, 1f);
                case TextAnchor.MiddleLeft: return new Vector2(0f, 0.5f);
                case TextAnchor.MiddleCenter: return new Vector2(0.5f, 0.5f);
                case TextAnchor.MiddleRight: return new Vector2(1f, 0.5f);
                case TextAnchor.LowerLeft: return new Vector2(0f, 0f);
                case TextAnchor.LowerCenter: return new Vector2(0.5f, 0f);
                case TextAnchor.LowerRight: return new Vector2(1f, 0f);
                default: return new Vector2(0.5f, 0f);
            }
        }
    }
}
