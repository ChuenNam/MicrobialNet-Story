using System.Collections.Generic;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Graph
{
    /// <summary>
    /// 画布上的分组框视图。自绘（不用 Unity 内置 Group），以避免内置 Group 把成员节点作为子节点导致的
    /// 坐标嵌套换算坑：本视图只是一个「衬在节点后面的矩形 + 可拖拽标题栏」，成员节点仍是画布顶层独立元素。
    /// 外框矩形始终由成员节点位置自动计算（包围盒 + 内边距），因此节点移动 / 分组整体拖拽时外框都会跟随。
    /// body 设 PickingMode.Ignore，使框内的节点仍可正常点击/拖拽。
    /// </summary>
    public sealed class StoryGroupView : GraphElement
    {
        private const float HeaderH = 22f;
        private const float Pad = 30f;
        private const float NodeW = 220f;   // 节点视觉尺寸固定（StoryNodeView 构造函数 SetPosition 用 220×120）
        private const float NodeH = 120f;

        private readonly StoryGraphModel _model;
        private readonly StoryGraphView _view;
        private readonly StoryGroup _data;
        private readonly Label _titleLabel;
        private readonly TextField _titleField;
        private readonly VisualElement _header;
        private readonly VisualElement _body;

        public string GroupId => _data.id;

        /// <summary>底层数据（供画布按父子关系计算嵌套框）。</summary>
        internal StoryGroup Data => _data;

        internal StoryGroupView(StoryGraphModel model, StoryGraphView view, StoryGroup data)
        {
            _model = model;
            _view = view;
            _data = data;
            // 仅可选中 / 可删除；移动由标题栏自定义拖拽处理（避免与 GraphView 内置拖拽冲突）。
            capabilities = Capabilities.Selectable | Capabilities.Deletable;
            // 分组框是「衬在节点后的背景盒 + 可拖拽标题栏」：整块容器设为点击穿透(Ignore)，
            // 否则容器会在其整个矩形范围内拦截拾取，导致框内的连线(Edge 命中区仅细线)/便签/节点无法正常点选与拖拽。
            // 仅标题栏(_header)保持可拾取，用于拖拽与改名；分组选中改由标题栏单击显式处理。
            pickingMode = PickingMode.Ignore;
            this.AddToClassList("story-group"); // 定位/边框/背景由 StoryGroupView.uss 的 .story-group 提供

            _header = new VisualElement { name = "group-header" };
            _header.AddToClassList("group-header"); // 高度/布局/配色由 .group-header 提供
            _header.pickingMode = PickingMode.Position; // 仅标题栏可拾取（拖拽/改名/选中），body 已由容器 Ignore 穿透
            _titleLabel = new Label(_data.title) { name = "group-title-label" };
            _titleLabel.AddToClassList("group-title"); // 字号/对齐由 .group-title 提供
            _header.Add(_titleLabel);
            _titleField = new TextField { name = "group-title-field", value = _data.title, isDelayed = true };
            _titleField.AddToClassList("group-title"); // 字号由 .group-title 提供
            _titleField.style.display = DisplayStyle.None; // 改名态显隐（动态态，保留内联）
            // 改名收口统一走 FocusOut：失焦（点别处 / 回车后失焦）/ Esc 取消都触发，保证输入框一定回到标签态。
            _titleField.RegisterCallback<FocusOutEvent>(_ => CommitRename());
            _titleField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Escape)
                {
                    e.StopPropagation();
                    if (e.keyCode == KeyCode.Escape) _titleField.value = _data.title; // 取消编辑，恢复原值
                    _titleField.Blur(); // 触发 FocusOut → CommitRename 收口
                }
            });
            _header.Add(_titleField);
            _header.AddManipulator(new HeaderDragger(this));
            _header.RegisterCallback<MouseDownEvent>(OnHeaderMouseDown);
            this.Add(_header);

            _body = new VisualElement { name = "group-body" };
            _body.AddToClassList("group-body");
            _body.pickingMode = PickingMode.Ignore; // 不拦截框内节点的拾取
            this.Add(_body);

            base.SetPosition(_data.rect);
            RefitToMembers(); // 载入即按成员位置包围，外框贴合节点
        }

        public bool ContainsNode(string id) => _data.nodeIds.Contains(id);

        /// <summary>根据成员节点当前位置重算分组矩形（包围盒 + 内边距），使外框始终跟随节点。无成员则保持现状。
        /// 嵌套：同时包裹直接子分组的当前框，使父框把子框整体兜住、不再与子框重叠。</summary>
        public void RefitToMembers()
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            foreach (var id in _data.nodeIds)
            {
                var nv = _view.GetNodeView(id);
                if (nv == null) continue;
                var p = nv.Data.position;             // 位置用数据坐标（节点 SetPosition 即用此值，恒定，不依赖 layout 是否完成）
                // 尺寸优先用节点已布局的真实几何（GetPosition().size）；布局完成前回退到固定常量。
                // 既避免未布局时 size=0 使包围盒塌缩，又能贴合节点真实渲染宽度——若真实宽 < 固定值，
                // 用真实值可消除此前「按 220 估算导致右侧多出一截空白」的问题。Populate 末尾 schedule 重算时即处于已布局状态。
                var gs = nv.GetPosition();
                var s = (gs.width > 1f && gs.height > 1f) ? gs.size : new Vector2(NodeW, NodeH);
                minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x + s.x); maxY = Mathf.Max(maxY, p.y + s.y);
                any = true;
            }
            // 嵌套：把直接子组的当前框也并入包围盒（子框先于父框 refit 时，此处取到的是已更新的子框）。
            foreach (var cgv in _view.GetChildGroupViews(_data.id))
            {
                var r = cgv.Data.rect;
                if (r.width > 1f && r.height > 1f)
                {
                    minX = Mathf.Min(minX, r.x); minY = Mathf.Min(minY, r.y);
                    maxX = Mathf.Max(maxX, r.x + r.width); maxY = Mathf.Max(maxY, r.y + r.height);
                    any = true;
                }
            }
            if (!any) return; // 无成员（均被删除）则保持当前矩形，等重建自然消失
            // 框体上方需容纳标题栏，故上内边距 = HeaderH + Pad；节点到标题栏下沿与到框底均为 Pad，视觉上居中。
            var nr = new Rect(minX - Pad, minY - HeaderH - Pad, (maxX - minX) + 2 * Pad, (maxY - minY) + HeaderH + 2 * Pad);
            _data.rect = nr;
            base.SetPosition(nr);
        }

        private void OnHeaderMouseDown(MouseDownEvent evt)
        {
            // 双击标题进入改名；其余交给 HeaderDragger 拖拽。
            if (evt.clickCount >= 2 && evt.button == 0)
            {
                evt.StopPropagation();
                EnterRename();
                return;
            }
            // 单击标题栏：选中本分组（容器已设为点击穿透，故分组选中只能由标题栏显式处理，
            // 保证 Delete 键 / 菜单对分组生效）。ClearSelection + AddToSelection 行为与普通节点一致。
            if (evt.button == 0)
            {
                _view.ClearSelection();
                _view.AddToSelection(this);
            }
        }

        private void EnterRename()
        {
            _titleLabel.style.display = DisplayStyle.None;
            _titleField.style.display = DisplayStyle.Flex;
            _titleField.value = _data.title;
            _titleField.Focus();
            _titleField.SelectAll();
        }

        private void CommitRename()
        {
            if (_titleField.style.display == DisplayStyle.None) return; // 已收口，避免重复提交
            _data.title = _titleField.value ?? "";
            _titleLabel.text = _data.title;
            _titleField.style.display = DisplayStyle.None;
            _titleLabel.style.display = DisplayStyle.Flex;
            _model.Touch();
            EditorUtility.SetDirty(_model.Asset);
        }

        // ── 标题栏拖拽（联动成员节点；用内容坐标系计算位移，避免标题随框移动导致 localMousePosition 失准而卡顿）──
        private sealed class HeaderDragger : Manipulator
        {
            private readonly StoryGroupView _g;
            private bool _active;
            private Vector2 _startContent;
            private readonly Dictionary<string, Vector2> _memberStart = new Dictionary<string, Vector2>();

            public HeaderDragger(StoryGroupView g) => _g = g;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<MouseDownEvent>(OnDown);
                target.RegisterCallback<MouseMoveEvent>(OnMove);
                target.RegisterCallback<MouseUpEvent>(OnUp);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<MouseDownEvent>(OnDown);
                target.UnregisterCallback<MouseMoveEvent>(OnMove);
                target.UnregisterCallback<MouseUpEvent>(OnUp);
            }

            private void OnDown(MouseDownEvent e)
            {
                if (e.button != 0 || e.clickCount >= 2) return;
                _active = true;
                // 用内容容器的「世界→本地」变换求鼠标在内容坐标系下的位置：内容容器不随框移动，位移量稳定无抖动。
                _startContent = _g._view.contentViewContainer.WorldToLocal(e.mousePosition);
                _memberStart.Clear();
                Undo.RecordObject(_g._model.Asset, "移动分组");
                // 移动分组 = 移动其全部后代节点（含子分组里的节点），保证嵌套整体平移。
                foreach (var id in _g._view.GetAllDescendantNodeIds(_g._data.id))
                {
                    var nv = _g._view.GetNodeView(id);
                    if (nv != null) _memberStart[id] = nv.Data.position;
                }
                target.CaptureMouse();
                e.StopPropagation();
            }

            private void OnMove(MouseMoveEvent e)
            {
                if (!_active) return;
                var cur = _g._view.contentViewContainer.WorldToLocal(e.mousePosition);
                var delta = cur - _startContent;
                foreach (var kv in _memberStart)
                {
                    var nv = _g._view.GetNodeView(kv.Key);
                    if (nv != null) nv.SetPosition(new Rect(kv.Value + delta, nv.GetPosition().size));
                }
                // 重算本组子树（自底向上）+ 向上冒泡到所有祖先，使父框始终包裹子框、整体跟随。
                _g._view.RefitGroupTree(_g._data.id);
            }

            private void OnUp(MouseUpEvent e)
            {
                if (!_active) return;
                _active = false;
                target.ReleaseMouse();
                e.StopPropagation();
            }
        }
    }
}
