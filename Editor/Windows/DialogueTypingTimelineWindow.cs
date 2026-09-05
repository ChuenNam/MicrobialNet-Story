using System;
using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools.Window;
using MicrobialNet.Story.Nodes;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 打字机形式三（手K逐字时序）的专用编辑窗口。
    /// 以「一维时间轴」呈现某对话节点的逐字停顿：x 轴 = 累计时间，每个点 = 一个可见字符，
    /// 相邻点之间的水平间距 = 该字显示前的停顿时长（秒）。可拖动点改停顿、带 Play 播放预览
    /// （playhead 扫动 + 文本按节奏逐字揭示）。数据写回节点内联 float[]（typingDelays），
    /// 与 StoryLineBoxView / TypingScheduler 共用同一条数据通路，未来正式时间编辑器可无缝接管。
    ///
    /// 增强能力：
    ///  - 目标总时长：工具栏右侧直接编辑总时长，提交后按比例自动缩放当前整段时序。
    ///  - 时间标尺：顶部刻度尺，随缩放/平移同步，标出绝对时间。
    ///  - 缩放/平移：滚轮横向平移；Ctrl/⌘+滚轮以光标处为锚点缩放；工具栏 −/适应/+ 按钮。
    ///  - 底部固定栏：底部为播放预览，固定高度且始终贴底，不随内容滚动。
    ///  - 打开方式：选中对话节点或菜单打开时均为默认浮窗（Unity 决定位置），已存在则复用实例、刷新目标。
    /// </summary>
    public sealed class DialogueTypingTimelineWindow : EditorWindow
    {
        private StoryGraphModel _model;
        private string _nodeId;
        private DialogueNodeData _node;

        private string _visibleText;   // 剔除富文本标签后的纯文本（与引擎可见字符一致）
        private float[] _delays;       // 工作副本，长度 = 可见字符数；_delays[i] = 第 i 字显示前的停顿

        // 播放预览
        private bool _playing;
        private double _playStart;
        private float _playPos;

        // 拖拽
        private int _dragIndex = -1;   // 正在拖的点 k（k>=1），对应修改 _delays[k-1]
        private float _dragOffset;

        // 视图（缩放 / 平移）
        private float _zoom = 1f;      // 1 = 适应窗口
        private float _scrollX = 0f;   // 像素横向偏移

        // 总时长 FloatField 拖拽/输入批处理：缩放时只改本地 _delays，结束后再写回节点，避免 Undo 碎化
        private bool _totalDirty;
        private bool _wasEditingTextField;

        private const float PadL = 30f;
        private const float PadR = 30f;
        private const float RulerH = 24f;
        private const float TrackH = 66f;
        private const float BottomH = 80f;
        private const float HitR = 9f;
        private const float MinZoom = 0.1f;
        private const float MaxZoom = 50f;

        // ── 入口 ──
        [MenuItem("MicrobialNet/Story/打字机时间轴")]
        private static void MenuOpen() => OpenFor(null, null);

        /// <summary>由对话节点属性面板的「时间轴」按钮调用：以默认浮窗形式打开（若未开）并刷新目标。
        /// 仅当打字机模式为手K时序时才会暴露该按钮，故不再随节点选中自动弹出。</summary>
        internal static void OpenForNode(StoryGraphModel model, DialogueNodeData node)
            => OpenFor(model, node);

        private static void OpenFor(StoryGraphModel model, DialogueNodeData node)
        {
            if (node == null)
            {
                // 菜单打开：默认浮窗
                GetWindow<DialogueTypingTimelineWindow>("打字机时间轴");
                return;
            }
            // 选中对话节点：默认浮窗打开（Unity 决定位置），已存在则复用实例、刷新目标
            GetWindow<DialogueTypingTimelineWindow>("打字机时间轴").SetTarget(model, node);
        }

        private void SetTarget(StoryGraphModel model, DialogueNodeData node)
        {
            _model = model;
            _nodeId = node?.id;
            Reload();
            Repaint();
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            // 补抓：若窗口打开前就已选中了对话节点，OnSelectionChanged 已错过，这里自行获取
            var dlg = StoryGraphWindow.GetSelectedDialogueNode(out var m);
            if (dlg != null) SetTarget(m, dlg);
        }
        private void OnDisable() => EditorApplication.update -= Tick;

        private DialogueNodeData ResolveNode()
            => (_model != null && _nodeId != null)
                ? _model.Asset.nodes.FirstOrDefault(n => n.id == _nodeId) as DialogueNodeData
                : null;

        private void Reload()
        {
            _node = ResolveNode();
            _playing = false;
            _playPos = 0f;
            _zoom = 1f;
            _scrollX = 0f;
            if (_node == null) { _visibleText = string.Empty; _delays = null; return; }
            _visibleText = TypingScheduler.StripRichText(_node.text ?? string.Empty);
            int n = _visibleText.Length;
            if (n == 0) { _delays = null; return; }
            if (_node.typingDelays != null && _node.typingDelays.Length == n)
                _delays = (float[])_node.typingDelays.Clone();
            else
                _delays = BaselineDelays(n);
        }

        private float[] BaselineDelays(int n)
        {
            float baseInterval = _node != null && _node.speed > 0.01f ? 1f / (_node.speed * 50f) : 0.02f;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = baseInterval;
            return d;
        }

        private float TotalDuration()
        {
            if (_delays == null) return 0f;
            float s = 0f;
            for (int i = 0; i < _delays.Length; i++) s += _delays[i];
            return s;
        }

        /// <summary>把当前整段时序按比例缩放到 newTotal 秒（保留各停顿相对占比）。</summary>
        private void ScaleTo(float newTotal)
        {
            if (_delays == null || _delays.Length == 0) return;
            float old = TotalDuration();
            if (old <= 0.0001f) return;
            float s = newTotal / old;
            for (int i = 0; i < _delays.Length; i++) _delays[i] *= s;
            _totalDirty = true; // 延迟到交互结束再 WriteBack，避免拖拽/输入产生大量 Undo
        }

        // ── 播放 ──
        private void Tick()
        {
            if (!_playing) return;
            _playPos = (float)(EditorApplication.timeSinceStartup - _playStart);
            float total = TotalDuration();
            if (_playPos >= total) { _playPos = total; _playing = false; }
            Repaint();
        }

        private void WriteBack()
        {
            if (_node == null || _model == null || _delays == null) return;
            Undo.RecordObject(_model.Asset, "编辑打字机时序");
            _node.typingDelays = (float[])_delays.Clone();
            _node.typingMode = TypingMode.Custom; // 手K时序即启用 Custom 模式
            EditorUtility.SetDirty(_model.Asset);
        }

        private int RevealedCount(float pos)
        {
            int n = _visibleText.Length;
            if (_delays == null || n == 0) return 0;
            int rev = 0;
            float acc = 0f;
            for (int k = 0; k < n; k++)
            {
                if (acc <= pos) rev++; else break;
                acc += _delays[k];
            }
            return rev;
        }

        // ── 界面 ──
        private void OnGUI()
        {
            DrawToolbar();
            float tbH = Mathf.Max(GUILayoutUtility.GetLastRect().height, 18f);

            if (_node == null)
            {
                EditorGUILayout.HelpBox("请在剧情编辑器中选中一个对话节点，即可在此编辑其打字机逐字时序。\n（菜单 MicrobialNet/Story/打字机时间轴 打开本窗口。）", MessageType.Info);
                return;
            }

            // 文本长度与延迟数组不一致（通常因正文被外部修改）→ 提示重新生成，避免静默丢编辑
            if (_delays == null || _visibleText.Length != _delays.Length)
            {
                EditorGUILayout.HelpBox($"文本可见字符数（{_visibleText.Length}）与时序数组长度（{(_delays?.Length ?? -1)}）不一致。", MessageType.Warning);
            if (GUILayout.Button("按当前语速重新生成时序"))
            {
                _delays = BaselineDelays(_visibleText.Length);
                _playPos = 0f;
                WriteBack();
            }
                return;
            }

            // 时间轴占据中间区域；底部固定栏贴底
            Rect tlRegion = new Rect(0, tbH, position.width, position.height - tbH - BottomH);
            DrawTimeline(tlRegion);
            DrawBottom(new Rect(0, position.height - BottomH, position.width, BottomH));

            // 总时长 FloatField 的拖拽/输入结束后统一写回
            if (_totalDirty)
            {
                bool justFinishedEdit = _wasEditingTextField && !EditorGUIUtility.editingTextField;
                bool mouseUp = Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseLeaveWindow;
                if (justFinishedEdit || mouseUp)
                {
                    WriteBack();
                    _totalDirty = false;
                }
            }
            _wasEditingTextField = EditorGUIUtility.editingTextField;
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(_playing ? "暂停" : "播放", EditorStyles.toolbarButton))
            {
                if (_playing) _playing = false;
                else
                {
                    if (_playPos >= TotalDuration()) _playPos = 0f;
                    _playStart = EditorApplication.timeSinceStartup - _playPos;
                    _playing = true;
                }
            }
            if (GUILayout.Button("停止", EditorStyles.toolbarButton))
            {
                _playing = false;
                _playPos = 0f;
            }
            if (GUILayout.Button("重置为语速", EditorStyles.toolbarButton) && _node != null)
            {
                _delays = BaselineDelays(_visibleText.Length);
                _playPos = 0f;
                WriteBack();
            }
            GUILayout.FlexibleSpace();
            // 缩放控制
            if (GUILayout.Button("−", EditorStyles.toolbarButton))
            {
                _zoom = Mathf.Max(MinZoom, _zoom / 1.25f);
            }
            if (GUILayout.Button("适应", EditorStyles.toolbarButton))
            {
                _zoom = 1f; _scrollX = 0f;
            }
            if (GUILayout.Button("+", EditorStyles.toolbarButton))
            {
                _zoom = Mathf.Min(MaxZoom, _zoom * 1.25f);
            }
            GUILayout.Space(8f);
            string speaker = StoryConstants.SpeakerDisplayName(_node != null ? _node.speakerId : StoryConstants.NarrationId);
            EditorGUILayout.LabelField($"讲述者：{speaker}  ·  字：{(_visibleText?.Length ?? 0)}", GUILayout.MaxWidth(150f));

            EditorGUI.BeginChangeCheck();
            float oldWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 62f; // 根据你的字体微调
            float nv = EditorGUILayout.FloatField("总时长(s)", TotalDuration(), GUILayout.Width(150f));
            nv = Mathf.Round(nv * 100) / 100;
            EditorGUIUtility.labelWidth = oldWidth;
            if (EditorGUI.EndChangeCheck())
            {
                ScaleTo(Mathf.Max(0.01f, nv));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTimeline(Rect region)
        {
            int n = _visibleText.Length;
            float total = Mathf.Max(TotalDuration(), 0.0001f);

            float x0 = region.x + PadL;
            float x1 = region.x + region.width - PadR;
            float span = Mathf.Max(x1 - x0, 1f);
            float pps = span / total * _zoom;                       // 当前缩放下 像素/秒
            float contentW = total * pps;

            float TimeToX(float t) => x0 + t * pps - _scrollX;
            float XToTime(float x) => (x - x0 + _scrollX) / pps;

            // 拖拽/缩放/平移交互
            var evt = Event.current;
            float blockH;
            float blockY;
            float[] reveal;
            if (evt.type == EventType.ScrollWheel && region.Contains(evt.mousePosition))
            {
                float curPps = span / total * _zoom;
                if (evt.control || evt.command)
                {
                    float tAt = XToTime(evt.mousePosition.x);
                    _zoom = Mathf.Clamp(_zoom * (evt.delta.y < 0 ? 1.1f : 0.9f), MinZoom, MaxZoom);
                    float newPps = span / total * _zoom;
                    _scrollX = tAt * newPps - (evt.mousePosition.x - x0);
                }
                else
                {
                    _scrollX += evt.delta.y * curPps + evt.delta.x * curPps;
                }
                _scrollX = Mathf.Clamp(_scrollX, 0f, Mathf.Max(0f, total * (span / total * _zoom) - span));
                evt.Use();
                Repaint();
                return;
            }
            else if (evt.type == EventType.MouseDown)
            {
                // 轨道中心（与下方绘制保持一致）
                blockH = RulerH + TrackH;
                blockY = region.y + (region.height - blockH) * 0.5f;
                float cyTrack = blockY + RulerH + TrackH * 0.5f;
                reveal = BuildReveal();
                for (int k = 1; k <= n; k++)
                {
                    float px = TimeToX(reveal[k]);
                    if (Mathf.Abs(evt.mousePosition.x - px) <= HitR && Mathf.Abs(evt.mousePosition.y - cyTrack) <= HitR * 2f)
                    {
                        _dragIndex = k;
                        _dragOffset = evt.mousePosition.x - px;
                        GUI.FocusControl(null);
                        evt.Use();
                        break;
                    }
                }
            }
            else if (evt.type == EventType.MouseDrag && _dragIndex >= 1)
            {
                float newTime = XToTime(evt.mousePosition.x - _dragOffset);
                reveal = BuildReveal();
                _delays[_dragIndex - 1] = Mathf.Max(0f, newTime - reveal[_dragIndex - 1]);
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp && _dragIndex >= 1)
            {
                _dragIndex = -1;
                WriteBack(); // 松手写回 + 置 Custom 模式
                evt.Use();
            }

            float[] BuildReveal()
            {
                var r = new float[n + 1];
                r[0] = 0f;
                for (int i = 0; i < n; i++) r[i + 1] = r[i] + _delays[i];
                return r;
            }

            // 重新 clamp 偏移（pps 可能已变）
            _scrollX = Mathf.Clamp(_scrollX, 0f, Mathf.Max(0f, contentW - span));

            reveal = BuildReveal();

            // 轨道块布局
            blockH = RulerH + TrackH;
            blockY = region.y + (region.height - blockH) * 0.5f;
            Rect rulerRect = new Rect(region.x, blockY, region.width, RulerH);
            Rect trackRect = new Rect(region.x, blockY + RulerH, region.width, TrackH);
            float cy = trackRect.y + trackRect.height * 0.5f;

            // 标尺背景
            EditorGUI.DrawRect(rulerRect, new Color(0.22f, 0.22f, 0.25f));

            // 标尺刻度（随缩放/平移）
            float pxTarget = 72f;
            float rawStep = pxTarget / pps;
            float[] steps = { 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };
            float step = steps[steps.Length - 1];
            foreach (var s in steps) { if (s >= rawStep) { step = s; break; } }
            float visStart = XToTime(x0);
            float visEnd = XToTime(x1);
            float t0 = Mathf.Ceil(visStart / step) * step;
            for (float t = t0; t <= visEnd + 1e-6f; t += step)
            {
                float x = TimeToX(t);
                EditorGUI.DrawRect(new Rect(x - 0.5f, rulerRect.y + rulerRect.height - 8f, 1f, 8f), new Color(0.6f, 0.6f, 0.65f));
                GUI.Label(new Rect(x - 24f, rulerRect.y + 2f, 48f, 14f), t.ToString("F2") + "s", EditorStyles.centeredGreyMiniLabel);
                // 轨道内淡网格
                EditorGUI.DrawRect(new Rect(x - 0.5f, trackRect.y, 1f, trackRect.height), new Color(0.3f, 0.3f, 0.35f, 0.45f));
            }

            // 基线
            EditorGUI.DrawRect(new Rect(x0, cy - 1f, span, 2f), new Color(0.4f, 0.4f, 0.45f));

            // 段（间隔）标注
            bool showLabels = n <= 24 && pps >= 8f;
            for (int k = 1; k <= n; k++)
            {
                float xa = TimeToX(reveal[k - 1]);
                float xb = TimeToX(reveal[k]);
                EditorGUI.DrawRect(new Rect(xa, cy - 1f, Mathf.Max(xb - xa, 1f), 2f), new Color(0.55f, 0.55f, 0.6f));
                if (showLabels && xb > x0 - 30f && xa < x1 + 30f)
                {
                    var mid = new Vector2((xa + xb) * 0.5f, cy - 18f);
                    GUI.Label(new Rect(mid.x - 22f, mid.y - 9f, 44f, 18f), _delays[k - 1].ToString("F2"), EditorStyles.centeredGreyMiniLabel);
                }
            }

            // 点（可见字符）
            for (int k = 0; k <= n; k++)
            {
                float px = TimeToX(reveal[k]);
                bool dragged = k == _dragIndex;
                Color c = dragged ? Color.yellow : (k == 0 ? Color.green : Color.cyan);
                float r = dragged ? 7f : 5f;
                EditorGUI.DrawRect(new Rect(px - r, cy - r, r * 2f, r * 2f), c);
                if (k < n && n <= 30 && pps >= 6f && px > x0 - 20f && px < x1 + 20f)
                {
                    char ch = _visibleText[k];
                    GUI.Label(new Rect(px - 6f, cy + 10f, 14f, 16f), ch.ToString(), EditorStyles.centeredGreyMiniLabel);
                }
            }

            // 播放头
            if (_playPos > 0f || _playing)
            {
                float phx = TimeToX(_playPos);
                EditorGUI.DrawRect(new Rect(phx - 1f, rulerRect.y, 2f, rulerRect.height + trackRect.height), Color.red);
            }
        }

        private void DrawBottom(Rect bottom)
        {
            // 背景 + 顶部分隔线
            EditorGUI.DrawRect(bottom, new Color(0.3f, 0.3f, 0.3f));
            EditorGUI.DrawRect(new Rect(0, bottom.y, bottom.width, 1f), new Color(0.5f, 0.5f, 0.55f));

            float total = TotalDuration();
            int rev = RevealedCount(_playPos);

            // 预览（按节奏逐字揭示），占满底部整栏
            Rect pbox = new Rect(10, bottom.y + 8, bottom.width - 20, bottom.height - 40);
            GUI.Box(pbox, GUIContent.none);
            var pstyle = new GUIStyle(EditorStyles.wordWrappedLabel) { normal = { textColor = Color.white } };
            string shown = _visibleText.Substring(0, rev);
            GUI.Label(new Rect(pbox.x + 8, pbox.y + 6, pbox.width - 16, pbox.height - 12),
                string.IsNullOrEmpty(shown) ? "（点击播放）" : shown, pstyle);
            EditorGUI.LabelField(new Rect(10, bottom.y + bottom.height - 26, bottom.width - 20, 18),
                $"已显示 {rev}/{_visibleText.Length}  ·  播放位置 {_playPos:F2}s / {total:F2}s");
        }
    }
}
