using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MicrobialNet.Story;
using MicrobialNet.Story.Nodes;
using MicrobialNet.Story.EditorTools;
using MicrobialNet.Story.EditorTools.Commands;
using MicrobialNet.Story.EditorTools.Graph;
using MicrobialNet.Story.EditorTools.Inspector;
using MicrobialNet.Story.EditorTools.UI;
using MicrobialNet.Story.EditorTools.Validation;
using MicrobialNet.Story.EditorTools.Playback;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MicrobialNet.Story.EditorTools.Window
{
    /// <summary>
    /// 剧情编辑器主窗口（五区：工具栏 / 资源树 / 画布 / 属性面板 / 状态栏）。
    /// 双击 Project 中的剧情图资产，或经菜单打开。所有写操作经 StoryGraphModel 命令。
    /// </summary>
    public sealed class StoryGraphWindow : EditorWindow
    {
        private StoryGraphAsset _asset;
        private StoryGraphModel _model;
        private StoryGraphView _graphView;
        private ScrollView _inspectorPane;
        private VisualElement _leftPane;
        private enum LeftTab { Resource, Characters, Variables }
        private LeftTab _leftTab = LeftTab.Resource;
        private Button _tabResource;
        private Button _tabChars;
        private Button _tabVars;
        private ScrollView _leftContent;
        private VisualElement _resourceContent;
        private VisualElement _characterContent;
        private VisualElement _variableContent;
        // 分栏宽度持久化键 + 去抖调度（拖动分隔条后经 GeometryChanged + 300ms 去抖写 EditorPrefs）
        private const string PrefsLeftPaneWidth = "com.microbialnet.story.window.leftPaneWidth.v1";
        private const string PrefsRightPaneWidth = "com.microbialnet.story.window.rightPaneWidth.v1";
        private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> _widthDebounce = new Dictionary<VisualElement, IVisualElementScheduledItem>();

        /// <summary>拖动分隔条（TwoPaneSplitView fixed pane 宽度变化）后去抖持久化宽度：300ms 内只写一次 EditorPrefs。</summary>
        private static void PersistWidthDebounced(VisualElement pane, string prefsKey)
        {
            pane.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_widthDebounce.TryGetValue(pane, out var running)) running.Pause();
                var item = pane.schedule.Execute(() =>
                {
                    _widthDebounce.Remove(pane);
                    float w = pane.resolvedStyle.width;
                    if (w > 1f) EditorPrefs.SetInt(prefsKey, Mathf.RoundToInt(w));
                });
                item.ExecuteLater(300);
                _widthDebounce[pane] = item;
            });
        }
        // 三个左栏面板的「动态列表容器」：静态骨架（搜索框/按钮/批量条）只建一次，搜索刷新只重建列表容器本身，
        // 避免每次刷新重建 TextField 导致输入时失焦打断（A3 搜索焦点丢失 bug 修复）。
        private VisualElement _resourceList;
        private VisualElement _charList;
        private VisualElement _varList;
        // 左侧栏底部常驻「运行时监视区」（02 §②）：预览播放中显示当前节点 ID 与变量实时值，非预览态灰显。
        private VisualElement _monitor;
        private Label _monitorState;
        private Label _monitorNode;
        private ScrollView _monitorVars;
        private StoryCharacterAsset _colorPickerTarget;
        private Label _statusLabel;
        private Label _dirtyLabel;
        // B8 节点搜索（文本/讲述者/ID + Enter 跳转）：搜索框引用、命中结果列表与当前跳转索引、结果计数标签。
        private TextField _searchField;
        private Label _searchResultLabel;
        private readonly List<StoryNodeView> _searchMatches = new List<StoryNodeView>();
        private int _searchIndex = -1;
        private bool _miniMapOn;

        /// <summary>当前图的章节缓存，用于侦测章节变化并自动移动文件到对应分组子文件夹（固定布局行为）。</summary>
        private string _currentChapter;

        /// <summary>资源树多选：选中的剧情图资产 GUID 集合（跨刷新持久）。</summary>
        private HashSet<string> _selectedGraphGuids = new HashSet<string>();
        private VisualElement _bulkBar;
        private Label _bulkCountLabel;

        /// <summary>角色多选：选中的角色资产 GUID 集合（跨刷新持久）。</summary>
        private HashSet<string> _selectedCharGuids = new HashSet<string>();
        private VisualElement _charBulkBar;
        private Label _charCountLabel;

        /// <summary>变量多选：选中项的复合键集合（{global|local}:变量id），跨刷新持久。</summary>
        private HashSet<string> _selectedVarKeys = new HashSet<string>();
        /// <summary>变量行折叠状态缓存（按 全局/本图 + 变量 id），使面板重建后折叠状态不复位。默认折叠（收纳）。</summary>
        private static readonly Dictionary<string, bool> _varFoldState = new Dictionary<string, bool>();
        private VisualElement _varBulkBar;
        private Label _varCountLabel;

        /// <summary>左栏搜索过滤关键字（资源树 / 角色 / 变量三面板各自独立，对应需求 A3 部分）。</summary>
        private string _resourceSearch = "";
        private string _charSearch = "";
        private string _varSearch = "";

        /// <summary>自动保存快照计时：每隔 60s 把脏图序列化到 Library/StoryEditorAutosave/（对应需求 A5）。</summary>
        private double _lastAutosaveTime;

        /// <summary>构建一段纵向批量操作条（统一风格）：第一行「已选 N」左 + 「清空」右；第二行「删除选中」等宽按钮。</summary>
        private VisualElement BuildBulkBar(out Label countLabel, System.Action onClear, System.Action onDelete, string deleteText)
        {
            var bar = new VisualElement { name = "bulk-bar" };
            bar.AddToClassList("sgw-bulk-bar");
            var topRow = new VisualElement { name = "bulk-top-row" };
            topRow.AddToClassList("sgw-bulk-top-row");
            countLabel = new Label("已选 0") { name = "bulk-count" };
            countLabel.AddToClassList("sgw-bulk-count");
            topRow.Add(countLabel);
            var spacer = new VisualElement { name = "bulk-spacer" };
            spacer.AddToClassList("sgw-spacer");
            topRow.Add(spacer);
            var clearBtn = new Button(onClear) { text = "清空" };
            clearBtn.AddToClassList("sgw-btn");
            topRow.Add(clearBtn);
            bar.Add(topRow);

            var btnRow = new VisualElement { name = "bulk-btn-row" };
            btnRow.AddToClassList("sgw-row");
            var delBtn = new Button(onDelete) { text = deleteText };
            delBtn.AddToClassList("sgw-bulk-del-btn");
            delBtn.AddToClassList("sgw-btn");
            btnRow.Add(delBtn);
            bar.Add(btnRow);
            return bar;
        }

        private Foldout _validationFoldout;
        private ScrollView _validationPane;
        private List<ValidationIssue> _issues;

        private List<StoryNodeData> _clipboardNodes;
        private List<StoryEdge> _clipboardEdges;
        /// <summary>「打开/上次保存」时的资产基线（JSON）。关闭且有未保存改动时，
        /// 若用户选择「放弃修改」，用它回滚资产并落盘，确保磁盘恢复到未保存状态。</summary>
        private string _baselineJson;

        [MenuItem("Window/MicrobialNet/剧情编辑器")]
        private static void OpenFromMenu()
        {
            var asset = Selection.activeObject as StoryGraphAsset;
            Open(asset);
        }

        [MenuItem("Window/MicrobialNet/整理剧情资产")]
        private static void MenuOrganize()
        {
            if (EditorUtility.DisplayDialog("整理剧情资产",
                "将工程内散落的剧情图 / 角色 / 全局变量移动到 Assets/Story 固定结构？\n已就位的会跳过。", "整理", "取消"))
            {
                var msg = StoryAssetOrganizer.OrganizeAll();
                Debug.Log("[Story] " + msg);
                EditorUtility.DisplayDialog("整理完成", msg, "确定");
            }
        }

        public static void Open(StoryGraphAsset asset)
        {
            var w = GetWindow<StoryGraphWindow>("剧情编辑器");
            w.Load(asset);
        }

        /// <summary>A2 双击资产打开编辑器：拦截双击 StoryGraphAsset / StoryCharacterAsset / StoryGlobalVariableAsset，
        /// 直接打开剧情编辑器窗口（角色/全局变量切到对应左栏标签）。返回 true 表示已接管，不再走默认 Inspector。</summary>
        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool OnOpenStoryAsset(int instanceID, int line)
        {
            var path = AssetDatabase.GetAssetPath(instanceID);
            if (string.IsNullOrEmpty(path)) return false;

            var graph = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(path);
            if (graph != null) { Open(graph); return true; }

            var character = AssetDatabase.LoadAssetAtPath<StoryCharacterAsset>(path);
            if (character != null)
            {
                var w = GetWindow<StoryGraphWindow>("剧情编辑器");
                w.SwitchLeftTab(LeftTab.Characters);
                return true;
            }

            var gvar = AssetDatabase.LoadAssetAtPath<StoryGlobalVariableAsset>(path);
            if (gvar != null)
            {
                var w = GetWindow<StoryGraphWindow>("剧情编辑器");
                w.SwitchLeftTab(LeftTab.Variables);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            // 自动保存快照计时（A5）：每 60s 把脏图写入 Library/StoryEditorAutosave/
            EditorApplication.update -= OnAutosaveTick;
            EditorApplication.update += OnAutosaveTick;
            _lastAutosaveTime = EditorApplication.timeSinceStartup;

            // 订阅试跑桥：由主窗口自己高亮/清除，避免试跑窗口反向 GetWindow 导致主窗口置顶盖住试跑窗口。
            PlaybackBridge.HighlightRequested -= OnPlaybackHighlight;
            PlaybackBridge.HighlightRequested += OnPlaybackHighlight;
            PlaybackBridge.ClearRequested -= OnPlaybackClear;
            PlaybackBridge.ClearRequested += OnPlaybackClear;
            PlaybackBridge.PathRequested -= OnPlaybackPath;
            PlaybackBridge.PathRequested += OnPlaybackPath;
            // 运行时监视区（02 §②）：订阅试跑状态广播。
            PlaybackBridge.StateUpdated -= OnPlaybackState;
            PlaybackBridge.StateUpdated += OnPlaybackState;
            // 注入讲述者视图模型解析：让 Runtime 的 ResolveCharacter / SpeakerDisplayName（图谱摘要/试跑窗口）
            // 也能解析出角色真名/主题色/立绘而非 ID。运行时程序集不引用 Editor，故此处（编辑态）注册；
            // 进 Play 前另有 StoryCharacterResolverBinder 再次注册，覆盖「未开剧情窗口直接 Play」的场景。
            StoryConstants.CharacterViewModelResolver = CharacterLibrary.ResolveViewModel;
            // 注入变量名解析：让节点摘要 / 试跑预览显示变量名而非难辨认的 id（落库仍是稳定 id）。
            StoryConstants.VariableNameResolver = ResolveVariableName;
            BuildUI();
            hasUnsavedChanges = false;
            saveChangesMessage = "剧情图有未保存的修改，关闭前是否保存？";
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnAutosaveTick;
            PlaybackBridge.HighlightRequested -= OnPlaybackHighlight;
            PlaybackBridge.ClearRequested -= OnPlaybackClear;
            PlaybackBridge.PathRequested -= OnPlaybackPath;
            PlaybackBridge.StateUpdated -= OnPlaybackState;
            if (_model != null)
            {
                // 未保存改动的关闭确认改由 hasUnsavedChanges 接入的 Unity 原生「保存/放弃/取消」三选框处理
                // （在 OnDisable 之前弹出，选「取消」可真正保留窗口）。此处仅负责收尾释放。
                _model.Dispose();
                _model = null;
            }
        }

        /// <summary>放弃修改：用「打开/上次保存」时的基线覆盖资产并立即落盘，确保即使 Unity 曾自动保存过
        /// 中间编辑（Auto Save、失焦序列化等），磁盘也恢复到未保存状态，而非残留改动。</summary>
        private void RollbackToBaseline()
        {
            if (_asset == null) return;
            if (_baselineJson != null)
            {
                try
                {
                    StoryJsonExporter.Import(_asset, _baselineJson);
                    EditorUtility.SetDirty(_asset);
                    AssetDatabase.SaveAssets();
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Story] 放弃修改时回滚基线失败：{ex.Message}"); }
            }
            else
            {
                var path = AssetDatabase.GetAssetPath(_asset);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>经 hasUnsavedChanges 接入 Unity 原生「保存 / 放弃 / 取消」关闭确认：
        /// 用户点关闭且存在未保存改动时，Unity 弹三选框；选「取消」窗口保持打开（不再误关编辑器）。</summary>
        public override void SaveChanges()
        {
            Save();
            hasUnsavedChanges = false;
            base.SaveChanges();
        }

        /// <summary>用户选择「放弃」时回滚到打开/上次保存时的资产基线，再关闭。</summary>
        public override void DiscardChanges()
        {
            RollbackToBaseline();
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }

        private void OnPlaybackHighlight(string id) => HighlightInGraph(id);
        private void OnPlaybackPath(List<string> path) => _graphView?.SetPlaybackPath(path);
        private void OnPlaybackClear() => _graphView?.ClearPlaybackHighlight();

        // ── 左侧栏底部「运行时监视区」（02 §②）──
        /// <summary>构建左侧栏底部常驻监视区：标题 + 状态 + 当前节点 + 变量实时值面板；非预览态默认灰显。</summary>
        private VisualElement BuildRuntimeMonitor()
        {
            _monitor = new VisualElement { name = "runtime-monitor" };
            _monitor.AddToClassList("sgw-monitor");

            var title = new Label("运行时监视") { name = "monitor-title" };
            title.AddToClassList("sgw-monitor-title");
            _monitor.Add(title);

            _monitorState = new Label("未预览") { name = "monitor-state" };
            _monitorState.AddToClassList("sgw-monitor-state");
            _monitor.Add(_monitorState);

            _monitorNode = new Label("当前节点：—") { name = "monitor-node" };
            _monitorNode.AddToClassList("sgw-monitor-line");
            _monitor.Add(_monitorNode);

            _monitorVars = new ScrollView { name = "monitor-vars" };
            _monitorVars.AddToClassList("sgw-monitor-vars");
            _monitor.Add(_monitorVars);

            SetMonitorActive(false);
            return _monitor;
        }

        /// <summary>切换监视区激活态：激活=不透明亮显；非激活=0.4 灰显并占位「（未预览）」。</summary>
        private void SetMonitorActive(bool active)
        {
            if (_monitor == null) return;
            if (active) _monitor.RemoveFromClassList("dim");
            else _monitor.AddToClassList("dim");
            _monitorState.text = active ? "预览中" : "未预览";
            if (!active)
            {
                _monitorNode.text = "当前节点：—";
                _monitorVars.Clear();
                var emptyLbl = new Label("（未预览）") { name = "monitor-empty" };
                emptyLbl.AddToClassList("sgw-monitor-empty");
                _monitorVars.Add(emptyLbl);
            }
        }

        /// <summary>试跑桥状态回调：实时刷新监视区的当前节点与变量值。</summary>
        private void OnPlaybackState(RuntimeSnapshot snap)
        {
            if (snap == null || !snap.active)
            {
                SetMonitorActive(false);
                return;
            }
            SetMonitorActive(true);
            _monitorNode.text = $"当前节点：{snap.nodeTypeLabel} · {snap.nodeId}";
            _monitorVars.Clear();
            if (snap.vars == null || snap.vars.Count == 0)
            {
                var noVarLbl = new Label("（无变量）") { name = "monitor-empty" };
                noVarLbl.AddToClassList("sgw-monitor-empty");
                _monitorVars.Add(noVarLbl);
            }
            else
            {
                foreach (var kv in snap.vars)
                {
                    var varLbl = new Label($"{kv.Key}: {kv.Value}") { name = "monitor-var" };
                    varLbl.AddToClassList("sgw-monitor-var");
                    _monitorVars.Add(varLbl);
                }
            }
        }


        private void BuildUI()
        {
            var root = rootVisualElement;
            StoryStyle.Apply(root);
            root.AddToClassList("sgw-root");

            // ── 工具栏 ──
            var toolbar = new Toolbar();

            var addMenu = new ToolbarMenu { text = "添加节点" };
            foreach (var grp in NodeRegistry.Entries.GroupBy(e => e.Attr.Category))
            {
                foreach (var entry in grp)
                {
                    var e = entry;
                    addMenu.menu.AppendAction($"{grp.Key}/{e.Attr.Title}",
                        _ => _graphView.RequestAddNode(e.Type));
                }
            }
            toolbar.Add(addMenu);

            var saveBtn = new ToolbarButton(Save) { text = "保存" };
            toolbar.Add(saveBtn);

            _dirtyLabel = new Label("") { name = "dirty" };
            _dirtyLabel.AddToClassList("sgw-tool-btn");
            toolbar.Add(_dirtyLabel);

            _searchField = new TextField { name = "search" };
            _searchField.AddToClassList("sgw-search");
            _searchField.RegisterValueChangedCallback(evt => ApplySearch(evt.newValue));
            _searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown);
            _searchField.tooltip = "按文本 / 讲述者 / 节点 ID 搜索；Enter 跳到下一个匹配，Shift+Enter 上一个，Esc 清空";
            toolbar.Add(_searchField);

            _searchResultLabel = new Label("") { name = "search-result" };
            _searchResultLabel.AddToClassList("sgw-search-result");
            toolbar.Add(_searchResultLabel);

            var validateBtn = new ToolbarButton(RunValidation) { text = "校验" };
            validateBtn.AddToClassList("sgw-tool-btn");
            toolbar.Add(validateBtn);

            var playBtn = new ToolbarButton(StartPlayback) { text = "试跑" };
            playBtn.AddToClassList("sgw-tool-btn");
            toolbar.Add(playBtn);

            // ── 数据流转菜单（重排）──
            var dataMenu = new ToolbarMenu { text = "数据" };
            dataMenu.AddToClassList("sgw-tool-btn");
            // 顶层：从图同步 Key / 剧情统计
            dataMenu.menu.AppendAction("本地化/从图同步 Key（SO）", _ => SyncLocalization());
            // 本地化：两个并列子菜单「本地化 Excel ▾」「本地化 CSV ▾」
            dataMenu.menu.AppendAction("本地化/Excel/导出本地化 Excel（从主表）", _ => ExportLocalizationXlsx());
            dataMenu.menu.AppendAction("本地化/Excel/导入本地化 Excel → 主表", _ => ImportLocalizationXlsxToTable());
            dataMenu.menu.AppendAction("本地化/CSV/导出表格 CSV（从主表）", _ => ExportCsv());
            dataMenu.menu.AppendAction("本地化/CSV/导入表格 CSV → 主表", _ => ImportCsvToTable());
            
            // 剧情表：剧情表节点 + 虚拟子图（表即真相源，节点为派生投影）
            dataMenu.menu.AppendAction("剧情表/新建剧情表节点（导入文件）", _ => CreateTableNode());
            dataMenu.menu.AppendAction("剧情表/从已有SO新建节点", _ => CreateTableNodeFromSo());
            dataMenu.menu.AppendAction("剧情表/全部读取源表格", _ => ReimportAndSyncAllTables());
            
            // 节点：导出/导入 节点结构JSON，分隔，导出/导入 节点属性JSON
            dataMenu.menu.AppendAction("节点/导出 节点结构JSON", _ => ExportJson());
            dataMenu.menu.AppendAction("节点/导入 节点结构JSON", _ => ImportJson());
            dataMenu.menu.AppendSeparator("节点/");
            dataMenu.menu.AppendAction("节点/导出 节点属性Xlsx", _ => ExportNodesXlsx());
            dataMenu.menu.AppendAction("节点/导入 节点属性Xlsx", _ => ImportNodesXlsx());
            
            dataMenu.menu.AppendAction("剧情统计", _ => ShowStats());
            toolbar.Add(dataMenu);

            // ── 视图菜单（B9 MiniMap）── 迷你地图默认关闭，可在视图菜单开启。
            var viewMenu = new ToolbarMenu { text = "视图" };
            viewMenu.AddToClassList("sgw-tool-btn");
            viewMenu.menu.AppendAction("迷你地图",
                _ => { _miniMapOn = !_miniMapOn; _graphView?.SetMiniMap(_miniMapOn); },
                _ => _miniMapOn ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            toolbar.Add(viewMenu);

            root.Add(toolbar);

            // ── 主体三栏 ──
            var body = new VisualElement { name = "body" };
            body.AddToClassList("sgw-body");

            // 左栏：资源 / 角色 / 变量 三标签
            _leftPane = new VisualElement { name = "left-pane" };
            _leftPane.AddToClassList("sgw-left-pane");

            var tabRow = new VisualElement { name = "tab-row" };
            tabRow.AddToClassList("sgw-tab-row");
            _tabResource = new Button(() => SwitchLeftTab(LeftTab.Resource)) { text = "资源" };
            _tabResource.AddToClassList("sgw-left-tab");
            _tabChars = new Button(() => SwitchLeftTab(LeftTab.Characters)) { text = "角色" };
            _tabChars.AddToClassList("sgw-left-tab");
            _tabVars = new Button(() => SwitchLeftTab(LeftTab.Variables)) { text = "变量" };
            _tabVars.AddToClassList("sgw-left-tab");
            tabRow.Add(_tabResource);
            tabRow.Add(_tabChars);
            tabRow.Add(_tabVars);
            _leftPane.Add(tabRow);

            _leftContent = new ScrollView { name = "left-content" };
            _leftContent.AddToClassList("sgw-left-content");
            _leftPane.Add(_leftContent);

            _resourceContent = new VisualElement { name = "resource" };
            _characterContent = new VisualElement { name = "characters" };
            _variableContent = new VisualElement { name = "variables" };
            _characterContent.style.display = DisplayStyle.None;
            _variableContent.style.display = DisplayStyle.None;
            _leftContent.Add(_resourceContent);
            _leftContent.Add(_characterContent);
            _leftContent.Add(_variableContent);
            SwitchLeftTab(LeftTab.Resource);

            // 左侧栏底部常驻「运行时监视区」（02 §②）：预览播放中显示当前节点 ID 与变量实时值，非预览态灰显。
            _leftPane.Add(BuildRuntimeMonitor());

            // 给 GraphView 套一层父容器（绝对填充）。
            // 规避 Unity 官方 GraphView bug：GraphView 不在窗口 (0,0) 时框选矩形偏移（偏移量=左侧面板宽）。
            var graphContainer = new VisualElement { name = "graph-container" };
            graphContainer.AddToClassList("sgw-graph-container");

            _graphView = new StoryGraphView();
            _graphView.AddToClassList("sgw-graph-view");
            graphContainer.Add(_graphView);
            _graphView.SelectionChanged += () => OnSelectionChanged();
            // 双击「剧情表」节点 → 打开其剧情表子画布（独立窗口，纯渲染 + 编辑写回）
            StoryNodeView.RequestOpenTableSubGraph += (node, model) => StoryTableSubGraphWindow.Open(node, _graphView);

            _inspectorPane = new ScrollView { name = "inspector" };
            _inspectorPane.AddToClassList("sgw-inspector-pane");
            // 属性面板：保留 300px 原始宽度 + 默认滚动条；输入框溢出由 FieldDrawerRegistry.ForceShrink 递归清零子元素最小宽度解决。

            // 左右栏宽度可拖：TwoPaneSplitView 嵌套（左栏 fixed | 画布 flexible | 右栏 fixed）。
            // 拖动分隔条调整宽度，松开后经 GeometryChanged + 去抖写入 EditorPrefs，下次打开恢复。
            int leftW = EditorPrefs.GetInt(PrefsLeftPaneWidth, 200);
            int rightW = EditorPrefs.GetInt(PrefsRightPaneWidth, 300);
            var innerSplit = new TwoPaneSplitView(1, rightW, TwoPaneSplitViewOrientation.Horizontal);
            innerSplit.Add(graphContainer);
            innerSplit.Add(_inspectorPane);
            var outerSplit = new TwoPaneSplitView(0, leftW, TwoPaneSplitViewOrientation.Horizontal);
            outerSplit.Add(_leftPane);
            outerSplit.Add(innerSplit);
            outerSplit.style.flexGrow = 1;
            outerSplit.style.minWidth = 0;
            body.Add(outerSplit);
            root.Add(body);

            // 拖动持久化：宽度变化经 schedule 去抖（300ms）写 EditorPrefs，避免拖动中高频 IO。
            PersistWidthDebounced(_leftPane, PrefsLeftPaneWidth);
            PersistWidthDebounced(_inspectorPane, PrefsRightPaneWidth);

            // ── 校验问题面板 ──
            _validationFoldout = new Foldout { text = "校验问题（未运行）", value = false };
            _validationFoldout.AddToClassList("sgw-validation-foldout");
            _validationPane = new ScrollView();
            _validationFoldout.Add(_validationPane);
            root.Add(_validationFoldout);

            // ── 状态栏 ──
            _statusLabel = new Label("就绪") { name = "status" };
            _statusLabel.AddToClassList("sgw-status");
            root.Add(_statusLabel);

            // 快捷键：复制 / 粘贴 / 重复
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            root.RegisterCallback<KeyUpEvent>(OnKeyUp);

            if (_asset != null) Load(_asset);
            else ScheduleResourceTreeRefresh(); // 无剧情图时也构建资源栏（搜索框 / 新建剧情图 / 空态提示），否则无法新建第一张图
        }

        public void Load(StoryGraphAsset asset, bool captureBaseline = true)
        {
            // 切换到**不同**剧情图且当前图有未保存修改：先弹三选确认，避免改动被静默保留/丢失。
            // 同资产重载（导入/移动/重命名后的 Load(asset, false)）与 Load(null)（删除当前图）不弹窗。
            // 按钮映射（DisplayDialogComplex 返回值 0/1/2 = 参数顺序 ok/cancel/alt）：
            // 0=保存并切换；1=取消切换（Esc/关窗同此，安全默认=留在当前图）；2=放弃修改并切换（回滚到打开时基线）。
            if (_model != null && _model.IsDirty && asset != null && !ReferenceEquals(_asset, asset))
            {
                string curName = _asset != null ? _asset.name : "当前图";
                int option = EditorUtility.DisplayDialogComplex("切换剧情图",
                    $"「{curName}」有未保存的修改。切换到「{asset.name}」前如何处理？",
                    "保存并切换", "取消切换", "放弃修改并切换");
                switch (option)
                {
                    case 0:
                        Save(); // 含基线刷新与自动保存快照清理，之后继续切换
                        break;
                    case 1:
                        return; // 取消切换：保留当前图与未保存修改
                    case 2:
                        RollbackToBaseline(); // 回滚磁盘到打开/上次保存状态（含 Unity 中途自动保存的中间态一并撤销）
                        break;
                }
            }
            _graphView?.Unbind();
            if (_model != null) { _model.Dispose(); _model.Changed -= OnModelChanged; _model = null; }
            _asset = asset;
            _currentChapter = asset != null && asset.meta != null ? asset.meta.chapter : "";
            if (asset == null)
            {
                _inspectorPane?.Clear();
                _resourceContent?.Clear();
                _variableContent?.Clear();
                _baselineJson = null;
                // 资产清空时一并重置校验面板，避免底部残留被删图的旧错误行
                _issues = null;
                RefreshValidation();
                UpdateStatus();
                return;
            }
            // 崩溃恢复（A5）：若该图存在未清掉的自动保存快照（来自上次未正常保存的会话），提示恢复
            if (StoryAutosave.HasSnapshot(asset))
            {
                bool recover = EditorUtility.DisplayDialog("发现自动保存快照",
                    $"检测到「{asset.name}」的自动保存副本（可能来自上次未正常保存的会话）。\n是否用快照恢复未保存的改动？",
                    "恢复快照", "丢弃快照");
                if (recover)
                {
                    try { StoryJsonExporter.Import(asset, StoryAutosave.Read(asset)); AssetDatabase.SaveAssets(); }
                    catch (System.Exception ex) { Debug.LogWarning($"[Story] 恢复快照失败：{ex.Message}"); }
                }
                StoryAutosave.Clear(asset);
            }

            _model = new StoryGraphModel(asset);
            _model.Changed += OnModelChanged;
            _graphView.Bind(_model);
            _model.SyncUsedCharacters();
            // 切换/打开新图时清空校验（显示「未运行」），避免沿用上一图的旧错误行
            _issues = null;
            RefreshValidation();
            RefreshResourceTree();
            RefreshInspector(null);
            // 记录「打开/上次保存」时的资产基线，供关闭时「放弃修改」回滚，确保磁盘恢复到未保存状态。
            if (captureBaseline) _baselineJson = StoryJsonExporter.Export(asset);
            _lastAutosaveTime = EditorApplication.timeSinceStartup;
            UpdateStatus();
        }

        private void OnModelChanged(GraphChange change)
        {
            // 章节变化 → 自动把文件移动到对应分组子文件夹（固定布局：三 tab=三文件夹）。
            // 不依赖用户手动在 Project 里挪文件，编辑器在此强制收口。
            // 迁移防护：图已被搬离标准树（如迁往 Addressables 目录做热更）时尊重当前位置不拉回，
            // 仅更新章节记录——拉回会触发「移入 Resources 自动清 entry」逆转迁移（同 StoryAssetOrganizer）。
            if (_asset != null)
            {
                string ch = _asset.meta != null ? _asset.meta.chapter : "";
                if (ch != _currentChapter)
                {
                    string before = AssetDatabase.GetAssetPath(_asset);
                    if (!StoryAssetPaths.IsUnderStoryRoot(before))
                    {
                        _currentChapter = ch; // 已迁移：仅记录，不移动
                    }
                    else
                    {
                        string dir = StoryAssetPaths.GetGroupDir(ch);
                        StoryAssetPaths.EnsureFolder(dir);
                        string after = StoryAssetPaths.MoveAssetToDir(_asset, dir);
                        if (before != after)
                        {
                            AssetDatabase.SaveAssets();
                            ScheduleResourceTreeRefresh();
                            StoryAssetPaths.PruneEmptyGroupFolders(); // 源分组可能已空，清理残留
                            _statusLabel.text = $"已按分组「{(string.IsNullOrEmpty(ch) ? StoryAssetPaths.Ungrouped : ch)}」移动剧情图文件";
                        }
                        _currentChapter = ch;
                    }
                }
            }

            // 已运行过校验则实时重算，保持问题列表最新（高亮不自动重绘，用户重新双击定位即可）
            if (_issues != null)
            {
                _issues = StoryValidator.Validate(_model);
                RefreshValidation();
            }
            if (_leftTab == LeftTab.Characters) RefreshCharacters();
            if (_leftTab == LeftTab.Variables) RefreshVariables();
            UpdateStatus();
            if (change.Type == GraphChangeType.Reset)
            {
                var sel = _graphView.SelectedNodeViews().FirstOrDefault();
                RefreshInspector(sel?.Data == null ? null : new List<StoryNodeData> { sel.Data });
            }
        }

        private void Save()
        {
            if (_model == null || _asset == null) return;
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            _model.MarkSaved();
            _baselineJson = StoryJsonExporter.Export(_asset);
            StoryAutosave.Clear(_asset); // 已保存，清除崩溃恢复快照避免误报
            _lastAutosaveTime = EditorApplication.timeSinceStartup;
            UpdateStatus();
            hasUnsavedChanges = false;
        }

        /// <summary>A5 自动保存：仅当当前图处于脏状态时，每隔 60s 写一次快照到 Library/StoryEditorAutosave/。</summary>
        private void OnAutosaveTick()
        {
            if (_model == null || _asset == null) return;
            hasUnsavedChanges = _model.IsDirty; // 同步未保存标记，使 Unity 关闭确认框的「取消」可真正保留窗口
            if (!_model.IsDirty) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastAutosaveTime >= 60.0)
            {
                _lastAutosaveTime = now;
                StoryAutosave.Write(_asset);
                if (_statusLabel != null) _statusLabel.text = $"已自动保存快照（{System.DateTime.Now:HH:mm:ss}）";
            }
        }

        /// <summary>A3 左栏通用搜索框：输入即触发对应面板的刷新过滤（按名称实时匹配）。</summary>
        private static TextField MakeSearchField(string placeholder, string cur, System.Action<string> onChanged)
        {
            var f = new TextField { value = cur };
            f.AddToClassList("sgw-search-field");
            f.tooltip = placeholder;
            f.RegisterValueChangedCallback(e => onChanged(e.newValue));
            return f;
        }

        // ── 资源树（文件资源管理器式工作台）──
        // 资源树重建合并：reimport 未完成时扫描会命中半加载资产而报空错，且 delayCall 直接累加会泄漏回调。
        private bool _resourceTreePending;
        private void ScheduleResourceTreeRefresh()
        {
            if (_resourceTreePending) return;
            _resourceTreePending = true;
            EditorApplication.delayCall += OnResourceTreeDue;
        }
        private void OnResourceTreeDue()
        {
            _resourceTreePending = false;
            RefreshResourceTree();
        }

        private void RefreshResourceTree()
        {
            if (_resourceContent == null) return;
            // 静态骨架（整理按钮/批量条/搜索框/新建按钮）只建一次，避免每次刷新重建 TextField 导致输入时失焦打断（A3 搜索焦点 bug）。
            if (_resourceList == null || _resourceList.parent != _resourceContent)
            {
                _resourceContent.Clear();
                var organizeBtn = new Button(() =>
                {
                    if (EditorUtility.DisplayDialog("整理剧情资产",
                        "将工程内散落的剧情图 / 角色 / 全局变量移动到 Assets/Story 固定结构？\n已就位的会跳过。", "整理", "取消"))
                    {
                        _statusLabel.text = StoryAssetOrganizer.OrganizeAll();
                        RefreshResourceTree();
                        RefreshCharacters();
                        RefreshVariables();
                    }
                }) { text = "整理资产" };
                organizeBtn.AddToClassList("sgw-pane-btn");
                organizeBtn.AddToClassList("sgw-mt4");
                _resourceContent.Add(organizeBtn);

                // 批量操作条（选中≥1 时显示）
                _bulkBar = BuildBulkBar();
                _resourceContent.Add(_bulkBar);

                // 搜索框：按图名 / storyId 实时过滤（A3）。持久保留，搜索刷新只重建下方列表，输入不丢焦点。
                _resourceContent.Add(MakeSearchField("搜索剧情图…", _resourceSearch, v =>
                {
                    _resourceSearch = v;
                    ScheduleResourceTreeRefresh();
                }));

                _resourceList = new VisualElement();
                _resourceContent.Add(_resourceList);

                var newBtn = new Button(CreateStoryGraph) { text = "+ 新建剧情图" };
                newBtn.AddToClassList("sgw-pane-btn");
                newBtn.AddToClassList("sgw-mt6");
                _resourceContent.Add(newBtn);
            }

            // 仅重建列表内容（搜索框/按钮等保持不变）
            _resourceList.Clear();

            // 扫描全工程剧情图资产
            var assets = AssetDatabase.FindAssets("t:StoryGraphAsset")
                .Select(g => AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null)
                .ToList();
            int totalCount = assets.Count;
            if (!string.IsNullOrEmpty(_resourceSearch))
            {
                var q = _resourceSearch.Trim().ToLowerInvariant();
                assets = assets.Where(a =>
                    (a.name ?? "").ToLowerInvariant().Contains(q) ||
                    (a.meta != null && (a.meta.storyId ?? "").ToLowerInvariant().Contains(q))).ToList();
            }

            if (assets.Count == 0)
            {
                var emptyLbl = new Label(totalCount == 0 ? "（暂无剧情图，点击下方新建）" : "（无匹配）") { name = "resource-empty" };
                emptyLbl.AddToClassList("sgw-pane-empty");
                _resourceList.Add(emptyLbl);
            }
            else
            {
                // 按章节分组（空章节归入「未分组」并排到最后）
                var groups = assets
                    .GroupBy(a => string.IsNullOrEmpty(a.meta.chapter) ? "未分组" : a.meta.chapter)
                    .OrderBy(g => g.Key == "未分组" ? 1 : 0)
                    .ThenBy(g => g.Key, StringComparer.Ordinal);

                foreach (var grp in groups)
                {
                    var fold = new Foldout { text = grp.Key, value = true };
                    fold.AddToClassList("sgw-tree-fold");
                    foreach (var asset in grp.OrderBy(a => a.name, StringComparer.Ordinal))
                    {
                        var a = asset;
                        // reimport 期间资产可能半加载/已销毁，访问 meta/name 会抛异常；跳过该资产避免控制台空错
                        string label;
                        try { label = string.IsNullOrEmpty(a.name) ? a.meta.storyId : a.name; }
                        catch (System.Exception) { continue; }
                        bool isCurrent = _asset != null && a == _asset;

                        // 行：勾选框 + 加载按钮
                        var row = new VisualElement { name = "resource-row" };
                        row.AddToClassList("sgw-row-center");
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a));
                        var toggle = new Toggle { value = _selectedGraphGuids.Contains(guid) };
                        toggle.AddToClassList("sgw-tree-toggle");
                        toggle.RegisterValueChangedCallback(ev =>
                        {
                            if (ev.newValue) _selectedGraphGuids.Add(guid);
                            else _selectedGraphGuids.Remove(guid);
                            UpdateBulkBar();
                        });
                        row.Add(toggle);

                        var btn = new Button(() => Load(a))
                        {
                            text = isCurrent ? $"▸ {label}" : label,
                        };
                        btn.AddToClassList("sgw-tree-btn");
                        if (isCurrent) btn.AddToClassList("sgw-tree-btn-active");
                        btn.AddManipulator(new ContextualMenuManipulator(evt =>
                        {
                            evt.menu.AppendAction("重命名", _ => RenameStoryGraph(a));
                            // 「移动到分组」子菜单：现有分组 + 新建分组
                            foreach (var g in StoryAssetPaths.GetExistingGroups())
                                evt.menu.AppendAction($"移动到分组/{g}", _ => ApplyGroupToAssets(new List<StoryGraphAsset> { a }, g));
                            evt.menu.AppendAction("移动到分组/新建分组…", _ =>
                            {
                                RenameDialog.Show("新建分组名", "", name =>
                                {
                                    if (!string.IsNullOrWhiteSpace(name)) ApplyGroupToAssets(new List<StoryGraphAsset> { a }, name.Trim());
                                });
                            });
                            evt.menu.AppendAction("删除", _ => DeleteStoryGraph(a));
                        }));
                        row.Add(btn);
                        fold.Add(row);
                    }
                    _resourceList.Add(fold);
                }
            }

            UpdateBulkBar();
        }

        // ── 资源树多选 / 批量操作 ──

        private VisualElement BuildBulkBar()
        {
            var bar = new VisualElement { name = "bulk-bar" };
            bar.AddToClassList("sgw-bulk-bar");

            // 第一行：已选 N（左） + 清空（右）
            var topRow = new VisualElement { name = "bulk-top-row" };
            topRow.AddToClassList("sgw-bulk-top-row");
            _bulkCountLabel = new Label("已选 0") { name = "bulk-count" };
            _bulkCountLabel.AddToClassList("sgw-bulk-count");
            topRow.Add(_bulkCountLabel);
            // 弹性占位：把「清空」推到右侧（避免 StyleLength.Auto 在部分 Unity 版本不可用）
            var spacer = new VisualElement { name = "bulk-spacer" };
            spacer.AddToClassList("sgw-spacer");
            topRow.Add(spacer);
            var clearBtn = new Button(() =>
            {
                _selectedGraphGuids.Clear();
                UpdateBulkBar();
                ScheduleResourceTreeRefresh();
            }) { text = "清空" };
            clearBtn.AddToClassList("sgw-btn");
            topRow.Add(clearBtn);
            bar.Add(topRow);

            // 第二行：移动到分组 + 删除选中，等宽并排
            var btnRow = new VisualElement { name = "bulk-btn-row" };
            btnRow.AddToClassList("sgw-row");
            var moveBtn = new Button(ShowMoveGroupMenuForSelected) { text = "移动到分组 ▾" };
            moveBtn.AddToClassList("sgw-bulk-del-btn");
            moveBtn.AddToClassList("sgw-btn");
            moveBtn.AddToClassList("sgw-mr4");
            btnRow.Add(moveBtn);
            var delBtn = new Button(DeleteSelectedGraphs) { text = "删除选中" };
            delBtn.AddToClassList("sgw-bulk-del-btn");
            delBtn.AddToClassList("sgw-btn");
            btnRow.Add(delBtn);
            bar.Add(btnRow);

            return bar;
        }

        private void UpdateBulkBar()
        {
            if (_bulkBar == null || _bulkCountLabel == null) return;
            int n = _selectedGraphGuids.Count;
            _bulkBar.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _bulkCountLabel.text = $"已选 {n}";
        }

        private void ShowMoveGroupMenuForSelected()
        {
            var assets = ResolveSelectedAssets();
            if (assets.Count == 0) return;
            var menu = new GenericMenu();
            foreach (var g in StoryAssetPaths.GetExistingGroups())
                menu.AddItem(new GUIContent(g), false, () => ApplyGroupToAssets(assets, g));
            menu.AddItem(new GUIContent("新建分组…"), false, () =>
            {
                RenameDialog.Show("新建分组名", "", name =>
                {
                    if (!string.IsNullOrWhiteSpace(name)) ApplyGroupToAssets(assets, name.Trim());
                });
            });
            menu.ShowAsContext();
        }

        private List<StoryGraphAsset> ResolveSelectedAssets()
        {
            var list = new List<StoryGraphAsset>();
            foreach (var guid in _selectedGraphGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var a = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(path);
                if (a != null) list.Add(a);
            }
            return list;
        }

        /// <summary>把若干剧情图移动到指定分组：写回 meta.chapter 并移动文件，清理空源分组。</summary>
        private void ApplyGroupToAssets(List<StoryGraphAsset> assets, string group)
        {
            if (assets == null || assets.Count == 0) return;
            string g = StoryAssetPaths.Sanitize(group);
            bool reloadCurrent = false;
            foreach (var asset in assets)
            {
                if (asset == null) continue;
                asset.meta.chapter = g; // 空→未分组文件夹
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets(); // 先把 chapter 写回旧路径文件
                StoryAssetPaths.MoveAssetToDir(asset, StoryAssetPaths.GetGroupDir(g)); // 移动文件到新分组
                if (asset == _asset) reloadCurrent = true;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            StoryAssetPaths.PruneEmptyGroupFolders();
            if (reloadCurrent)
            {
                var reloaded = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(AssetDatabase.GetAssetPath(_asset));
                if (reloaded != null) Load(reloaded);
            }
            _selectedGraphGuids.Clear();
            ScheduleResourceTreeRefresh();
            string grpLabel = string.IsNullOrEmpty(g) ? StoryAssetPaths.Ungrouped : g;
            _statusLabel.text = $"已移动 {assets.Count} 个剧情图到分组「{grpLabel}」";
        }

        private void DeleteSelectedGraphs()
        {
            var assets = ResolveSelectedAssets();
            if (assets.Count == 0) return;
            bool ok = EditorUtility.DisplayDialog("删除选中的剧情图",
                $"确定删除选中的 {assets.Count} 个剧情图？此操作不可撤销。", "删除", "取消");
            if (!ok) return;
            foreach (var a in assets)
            {
                if (a == null) continue;
                var path = AssetDatabase.GetAssetPath(a);
                if (string.IsNullOrEmpty(path)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (_asset == a) Load(null);
                AssetDatabase.DeleteAsset(path);
                _selectedGraphGuids.Remove(guid);
            }
            AssetDatabase.Refresh();
            StoryAssetPaths.PruneEmptyGroupFolders();
            _selectedGraphGuids.Clear();
            ScheduleResourceTreeRefresh();
            _statusLabel.text = $"已删除 {assets.Count} 个剧情图";
        }

        private void CreateStoryGraph()
        {
            // 一步式对话框：直接定名称 + 分组（可编辑下拉：选已有组或直接输入新组名）
            string defaultGroup = (_asset != null && !string.IsNullOrEmpty(_asset.meta.chapter)) ? _asset.meta.chapter : "";
            NewStoryGraphDialog.Show(defaultGroup, (name, group) =>
            {
                string safeName = StoryAssetPaths.Sanitize(name);
                if (string.IsNullOrEmpty(safeName)) safeName = "StoryGraph";
                string dir = StoryAssetPaths.GetGroupDir(group);
                StoryAssetPaths.EnsureFolder(dir);
                var asset = ScriptableObject.CreateInstance<StoryGraphAsset>();
                asset.meta.storyId = "graph_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                asset.meta.chapter = StoryAssetPaths.Sanitize(group); // 空→""，落到「未分组」文件夹
                var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{safeName}.asset");
                AssetDatabase.CreateAsset(asset, path);
                // 新建图默认自带「开始 / 结束」节点（位于画布左侧与右侧，留出中间布线空间）
                var start = NodeRegistry.Create(typeof(StartNodeData));
                start.position = new Vector2(80, 160);
                var end = NodeRegistry.Create(typeof(EndNodeData));
                end.position = new Vector2(360, 160);
                asset.nodes = new List<StoryNodeData> { start, end };
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                Load(asset);
                _currentChapter = asset.meta.chapter; // 初始化章节追踪
                ScheduleResourceTreeRefresh();
                string grpLabel = string.IsNullOrEmpty(asset.meta.chapter) ? StoryAssetPaths.Ungrouped : asset.meta.chapter;
                _statusLabel.text = $"已新建剧情图「{safeName}」（分组：{grpLabel}）";
            });
        }

        private void RenameStoryGraph(StoryGraphAsset a)
        {
            if (a == null) return;
            RenameDialog.Show("重命名剧情图", a.name, newName =>
            {
                if (string.IsNullOrWhiteSpace(newName)) return;
                var path = AssetDatabase.GetAssetPath(a);
                if (string.IsNullOrEmpty(path)) return;
                // 仅用 RenameAsset 改名：导入器会自动把主对象名同步为文件名，
                // 切勿手动 a.name = newName（会导致「对象名≠文件名」警告且文件名未真正变更）。
                AssetDatabase.RenameAsset(path, newName);
                AssetDatabase.SaveAssets();
                // 重命名后 Unity 会重新导入并可能替换资产实例，重新加载并重新绑定当前图，保持高亮正确
                string dir = Path.GetDirectoryName(path);
                var reloaded = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>($"{dir}/{newName}.asset");
                if (reloaded != null) Load(reloaded);
                ScheduleResourceTreeRefresh();
                _statusLabel.text = $"已重命名剧情图为「{newName}」";
            });
        }

        private void DeleteStoryGraph(StoryGraphAsset a)
        {
            if (a == null) return;
            var path = AssetDatabase.GetAssetPath(a);
            if (string.IsNullOrEmpty(path)) return;
            string deletedName = a.name; // 删除前缓存名称，避免访问已销毁对象
            bool ok = EditorUtility.DisplayDialog("删除剧情图",
                $"确定删除剧情图「{deletedName}」？此操作不可撤销。", "删除", "取消");
            if (!ok) return;
            AssetDatabase.DeleteAsset(path);
            if (_asset == a) Load(null);
            AssetDatabase.Refresh();
            StoryAssetPaths.PruneEmptyGroupFolders(); // 源分组可能已空，清理残留
            // 延迟一帧再重建资源树：等 Unity 完成 reimport，避免扫描到半加载资产而报空错
            ScheduleResourceTreeRefresh();
            _statusLabel.text = $"已删除剧情图「{deletedName}」";
        }

        // ── 左栏标签切换 ──
        private void SwitchLeftTab(LeftTab tab)
        {
            _leftTab = tab;
            _resourceContent.style.display = tab == LeftTab.Resource ? DisplayStyle.Flex : DisplayStyle.None;
            _characterContent.style.display = tab == LeftTab.Characters ? DisplayStyle.Flex : DisplayStyle.None;
            _variableContent.style.display = tab == LeftTab.Variables ? DisplayStyle.Flex : DisplayStyle.None;
            if (_tabResource != null) _tabResource.EnableInClassList("selected", tab == LeftTab.Resource);
            if (_tabChars != null) _tabChars.EnableInClassList("selected", tab == LeftTab.Characters);
            if (_tabVars != null) _tabVars.EnableInClassList("selected", tab == LeftTab.Variables);
            if (tab == LeftTab.Characters) RefreshCharacters();
            if (tab == LeftTab.Variables) RefreshVariables();
        }

        // ── 角色库面板 ──
        private void RefreshCharacters()
        {
            if (_characterContent == null) return;
            // 静态骨架（标题/搜索框/新建按钮/批量条）只建一次，搜索刷新只重建下方列表，输入不丢焦点（A3 搜索焦点 bug）。
            if (_charList == null || _charList.parent != _characterContent)
            {
                _characterContent.Clear();
                var charTitle = new Label("角色库") { name = "char-title" };
                charTitle.AddToClassList("sgw-pane-title");
                _characterContent.Add(charTitle);

                // 搜索框：按角色显示名实时过滤（A3）。持久保留。
                _characterContent.Add(MakeSearchField("搜索角色…", _charSearch, v =>
                {
                    _charSearch = v;
                    RefreshCharacters();
                }));

                var newBtn = new Button(CreateCharacter) { text = "+ 新建角色" };
                newBtn.AddToClassList("sgw-pane-btn");
                newBtn.AddToClassList("sgw-mt4");
                _characterContent.Add(newBtn);

                // 批量操作条（选中≥1 时显示）
                _charBulkBar = BuildBulkBar(out _charCountLabel,
                    () => { _selectedCharGuids.Clear(); RefreshCharacters(); },
                    DeleteSelectedCharacters, "删除选中");
                _characterContent.Add(_charBulkBar);

                _charList = new VisualElement();
                _characterContent.Add(_charList);
            }

            // 仅重建列表内容（搜索框/按钮等保持不变）
            _charList.Clear();

            var chars = CharacterLibrary.All();
            if (!string.IsNullOrEmpty(_charSearch))
            {
                var q = _charSearch.Trim().ToLowerInvariant();
                chars = chars.Where(c => (c.displayName ?? "").ToLowerInvariant().Contains(q)).ToList();
            }
            if (chars.Count == 0)
            {
                var charEmpty = new Label("（暂无角色，点击上方新建）") { name = "char-empty" };
                charEmpty.AddToClassList("sgw-pane-empty");
                _charList.Add(charEmpty);
            }
            else
            {
                foreach (var c in chars)
                {
                    int usage = _model != null ? _model.CountCharacterUsage(c.characterId) : 0;
                    var row = new VisualElement { name = "char-row" };
                    row.AddToClassList("sgw-char-row");

                    // 行勾选框（多选）
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c));
                    var toggle = new Toggle { value = _selectedCharGuids.Contains(guid) };
                    toggle.AddToClassList("sgw-tree-toggle");
                    toggle.RegisterValueChangedCallback(ev =>
                    {
                        if (ev.newValue) _selectedCharGuids.Add(guid);
                        else _selectedCharGuids.Remove(guid);
                        UpdateCharBulkBar();
                    });
                    row.Add(toggle);

                    // 代表色色块：直接显示颜色，点击打开调色板设置（不使用内联吸色控件）
                    var swatch = new VisualElement { name = "char-swatch" };
                    swatch.AddToClassList("sgw-char-swatch");
                    swatch.style.backgroundColor = ParseColor(c.colorHex);
                    swatch.tooltip = "点击打开调色板设置代表色";
                    var capturedChar = c;
                    swatch.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        evt.StopPropagation(); // 避免冒泡到整行的双击编辑逻辑
                        OpenColorPicker(capturedChar, swatch);
                    });
                    row.Add(swatch);

                    // 内联改名（直接在此编辑，无需跳转到 Inspector）
                    var nameField = new TextField { value = c.displayName, name = "char-name" };
                    nameField.AddToClassList("sgw-inline-field");
                    nameField.RegisterValueChangedCallback(e =>
                    {
                        c.displayName = e.newValue;
                        EditorUtility.SetDirty(c);
                        // 名字变化后，刷新属性面板的讲述者下拉（让已选该角色的下拉立即显示新名）
                        var sel = _graphView.SelectedNodeViews().FirstOrDefault();
                        if (sel != null) RefreshInspector(new List<StoryNodeData> { sel.Data });
                    });
                    nameField.RegisterCallback<FocusOutEvent>(_ => AssetDatabase.SaveAssets());
                    FieldDrawerRegistry.ForceShrink(nameField);
                    row.Add(nameField);

                    var usageLbl = new Label($"台词 {usage}") { name = "char-usage" };
                    usageLbl.AddToClassList("sgw-char-usage");
                    row.Add(usageLbl);

                    var captured = c;
                    row.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        if (evt.clickCount >= 2) EditCharacter(captured); // 双击：在 Inspector 编辑头像/描述
                    });
                    _charList.Add(row);
                }
            }
            UpdateCharBulkBar();
        }

        private void UpdateCharBulkBar()
        {
            if (_charBulkBar == null || _charCountLabel == null) return;
            int n = _selectedCharGuids.Count;
            _charBulkBar.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _charCountLabel.text = $"已选 {n}";
        }

        private List<StoryCharacterAsset> ResolveSelectedCharacters()
        {
            var list = new List<StoryCharacterAsset>();
            foreach (var guid in _selectedCharGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var c = AssetDatabase.LoadAssetAtPath<StoryCharacterAsset>(path);
                if (c != null) list.Add(c);
            }
            return list;
        }

        private void DeleteSelectedCharacters()
        {
            var assets = ResolveSelectedCharacters();
            if (assets.Count == 0) return;

            // 统计被引用情况：若仍有台词讲述者指向这些角色，删除后会变成悬挂引用（游戏中显示 [未配置]）。
            int totalRefs = 0;
            var refDetail = new System.Text.StringBuilder();
            foreach (var c in assets)
            {
                int usage = _model != null ? _model.CountCharacterUsage(c.characterId) : 0;
                if (usage > 0)
                {
                    totalRefs += usage;
                    refDetail.AppendLine($"· {c.displayName}（{usage} 处台词）");
                }
            }
            string msg = $"确定删除选中的 {assets.Count} 个角色？此操作不可撤销。";
            if (totalRefs > 0)
                msg += $"\n\n注意：其中 {totalRefs} 处台词仍引用这些角色，删除后相关对话将显示为「[未配置]」，请在剧情图中重新指派讲述者。\n" + refDetail;

            bool ok = EditorUtility.DisplayDialog("删除选中的角色", msg, "删除", "取消");
            if (!ok) return;
            foreach (var c in assets)
            {
                var path = AssetDatabase.GetAssetPath(c);
                if (string.IsNullOrEmpty(path)) continue;
                if (_colorPickerTarget == c) _colorPickerTarget = null;
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.Refresh();
            _selectedCharGuids.Clear();
            RefreshCharacters();
            _statusLabel.text = $"已删除 {assets.Count} 个角色";
        }

        private void CreateCharacter()
        {
            // 角色统一落在 Assets/Story/Characters 文件夹（固定布局）
            StoryAssetPaths.EnsureFolder(StoryAssetPaths.CharactersDir);
            var asset = ScriptableObject.CreateInstance<StoryCharacterAsset>();
            asset.characterId = "char_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            asset.displayName = "新角色";
            asset.colorHex = "#378ADD";
            var path = AssetDatabase.GenerateUniqueAssetPath($"{StoryAssetPaths.CharactersDir}/Character_{asset.characterId}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SwitchLeftTab(LeftTab.Characters); // 切到「角色」页并刷新，新建后可直接在面板内改名/改色
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            _statusLabel.text = "已新建角色：可在左侧「角色」面板直接改名 / 改色，双击行在 Inspector 编辑头像与描述";
        }

        private void EditCharacter(StoryCharacterAsset c)
        {
            Selection.activeObject = c;
            EditorGUIUtility.PingObject(c);
        }

        // ── 变量黑板面板 ──
        private void RefreshVariables()
        {
            if (_variableContent == null) return;
            // 搜索框持久保留（只建一次），搜索刷新只重建下方列表，输入不丢焦点（A3 搜索焦点 bug）。
            if (_varList == null || _varList.parent != _variableContent)
            {
                _variableContent.Clear();
                // 搜索框：按变量名实时过滤（A3，跨全局/本图两段）。持久保留。
                _variableContent.Add(MakeSearchField("搜索变量…", _varSearch, v =>
                {
                    _varSearch = v;
                    RefreshVariables();
                }));
                _varList = new VisualElement();
                _variableContent.Add(_varList);
            }

            // 仅重建列表内容（搜索框保持不变）
            _varList.Clear();

            // 批量操作条（选中≥1 时显示；跨全局/本图两段统一选择）
            _varBulkBar = BuildBulkBar(out _varCountLabel,
                () => { _selectedVarKeys.Clear(); RefreshVariables(); },
                DeleteSelectedVariables, "删除选中");
            _varList.Add(_varBulkBar);

            // ── 全局变量段（跨章节共享）──
            var globalTitle = new Label("全局变量（跨章节共享）") { name = "var-global-title" };
            globalTitle.AddToClassList("sgw-pane-title");
            _varList.Add(globalTitle);
            var global = GlobalVariableLookup.GetAsset();
            if (global == null)
            {
                var gEmpty = new Label("（尚未创建全局变量资产）") { name = "var-global-empty" };
                gEmpty.AddToClassList("sgw-pane-empty");
                _varList.Add(gEmpty);
                var createGlobalBtn = new Button(() =>
                {
                    var g = GlobalVariableLookup.GetOrCreate();
                    if (g != null) { EditorGUIUtility.PingObject(g); RefreshVariables(); _statusLabel.text = "已创建全局变量资产：可在「变量」面板编辑全局变量"; }
                }) { text = "+ 创建全局变量资产" };
                createGlobalBtn.AddToClassList("sgw-pane-btn");
                createGlobalBtn.AddToClassList("sgw-mt4");
                _varList.Add(createGlobalBtn);
            }
            else
            {
                var gNewBtn = new Button(() => CreateGlobalVariable(global)) { text = "+ 新建全局变量" };
                gNewBtn.AddToClassList("sgw-pane-btn");
                gNewBtn.AddToClassList("sgw-mt4");
                _varList.Add(gNewBtn);
                var gList = global.variables ?? new List<StoryVariableDef>();
                List<StoryVariableDef> gDisp = gList;
                if (!string.IsNullOrEmpty(_varSearch))
                {
                    var q = _varSearch.Trim().ToLowerInvariant();
                    gDisp = gList.Where(v => (v.name ?? "").ToLowerInvariant().Contains(q)).ToList();
                }
                if (gDisp.Count == 0)
                {
                    var gDispEmpty = new Label(gList.Count == 0 ? "（暂无全局变量，点击上方新建）" : "（无匹配）") { name = "var-global-disp-empty" };
                    gDispEmpty.AddToClassList("sgw-pane-empty");
                    _varList.Add(gDispEmpty);
                }
                else
                {
                    foreach (var vref in gDisp)
                        _varList.Add(BuildVariableRow(vref, global, () => DeleteVariable(vref, global.variables, global)));
                }
            }

            // 两段之间分隔线
            var sep = new VisualElement { name = "var-sep" };
            sep.AddToClassList("sgw-pane-sep");
            _varList.Add(sep);

            // ── 本图变量段（局部）──
            var localTitle = new Label("本图变量（局部）") { name = "var-local-title" };
            localTitle.AddToClassList("sgw-pane-title");
            _varList.Add(localTitle);
            var newBtn = new Button(CreateVariable) { text = "+ 新建变量" };
            newBtn.AddToClassList("sgw-pane-btn");
            newBtn.AddToClassList("sgw-mt4");
            _varList.Add(newBtn);

            var vars = _model != null ? _model.Asset.variables : null;
            var lList = vars ?? new List<StoryVariableDef>();
            var lDisp = lList;
            if (!string.IsNullOrEmpty(_varSearch))
            {
                var q = _varSearch.Trim().ToLowerInvariant();
                lDisp = lList.Where(v => (v.name ?? "").ToLowerInvariant().Contains(q)).ToList();
            }
            if (lDisp.Count == 0)
            {
                var lDispEmpty = new Label(lList.Count == 0 ? "（暂无变量，点击上方新建）" : "（无匹配）") { name = "var-local-empty" };
                lDispEmpty.AddToClassList("sgw-pane-empty");
                _varList.Add(lDispEmpty);
            }
            else
            {
                foreach (var vref in lDisp)
                    _varList.Add(BuildVariableRow(vref, _asset, () => DeleteVariable(vref, vars, _asset)));
            }
            UpdateVarBulkBar();
        }

        /// <summary>构建单个变量行（全局/本图共用），做成可收纳抽屉：用 Unity 内置 Foldout（自带系统折叠箭头），把默认文本 Label 替换为自定义头行（勾选框+类型色点+名字+删除，折叠后仍可见），折叠体为 类型/作用域/默认。ownerAsset 用于标记脏数据；onDelete 执行删除。</summary>
        private VisualElement BuildVariableRow(StoryVariableDef v, ScriptableObject ownerAsset, System.Action onDelete)
        {
            // 行多选键 = {global|local}:变量id
            string varKey = (ownerAsset is StoryGlobalVariableAsset ? "global:" : "local:") + v.id;

            bool expanded = false; // 默认折叠（收纳）；已展开的按缓存
            _varFoldState.TryGetValue(varKey, out expanded);

            var fold = new Foldout { text = v.name, value = expanded };
            fold.name = "var-fold";
            fold.AddToClassList("sgw-var-fold");
            // 折叠态持久化（原生箭头驱动 fold.value 变化）
            fold.RegisterValueChangedCallback(e => _varFoldState[varKey] = e.newValue);

            // 自定义头行（折叠后仍可见）：勾选框 + 类型色点 + 名字 + 删除
            var headerRow = new VisualElement { name = "var-header" };
            headerRow.AddToClassList("sgw-var-header");

            // 行勾选框（多选）
            var toggle = new Toggle { value = _selectedVarKeys.Contains(varKey) };
            toggle.AddToClassList("sgw-var-toggle");
            toggle.RegisterValueChangedCallback(ev =>
            {
                if (ev.newValue) _selectedVarKeys.Add(varKey);
                else _selectedVarKeys.Remove(varKey);
                UpdateVarBulkBar();
            });
            headerRow.Add(toggle);

            var typeDot = new VisualElement { name = "var-type-dot" };
            typeDot.AddToClassList("sgw-type-dot");
            typeDot.AddToClassList("sgw-type-" + v.type.ToString().ToLowerInvariant());
            headerRow.Add(typeDot);

            var nameField = new TextField { value = v.name, name = "var-name" };
            nameField.AddToClassList("sgw-inline-field");
            nameField.RegisterValueChangedCallback(e =>
            {
                v.name = e.newValue;
                EditorUtility.SetDirty(ownerAsset);
                // 名字变化后刷新属性面板的变量下拉（让已选该变量的下拉立即显示新名）
                var sel = _graphView.SelectedNodeViews().FirstOrDefault();
                if (sel != null) RefreshInspector(new List<StoryNodeData> { sel.Data });
                // 同步刷新画布上各节点的摘要（节点摘要显示变量名，改名后需即时更新）
                _graphView.RefreshNodeSummaries();
            });
            nameField.RegisterCallback<FocusOutEvent>(_ => AssetDatabase.SaveAssets());
            FieldDrawerRegistry.ForceShrink(nameField);
            headerRow.Add(nameField);

            var delBtn = new Button(onDelete) { text = "删除", name = "var-del" };
            delBtn.AddToClassList("sgw-var-del");
            headerRow.Add(delBtn);

            // 阻止头行内的交互（改名/勾选/删除）触发 Foldout 头部的默认点击折叠行为，仅内置三角 Toggle 负责展开/收起
            headerRow.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            headerRow.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
            headerRow.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            // 用自定义头行替换 Foldout 默认的文本 Label（保留内置三角 Toggle）
            var label = fold.Q<Label>();
            if (label != null && label.parent != null)
            {
                var parent = label.parent;
                int idx = parent.IndexOf(label);
                parent.Remove(label);
                parent.Insert(idx, headerRow);
            }
            else
            {
                fold.Add(headerRow);
            }

            // ── 折叠体：类型 / 作用域 / 默认 ──
            var typeRow = new VisualElement { name = "var-type-row" };
            typeRow.AddToClassList("sgw-var-field-row");
            var typeLbl = new Label("类型") { name = "var-type-label" };
            typeLbl.AddToClassList("sgw-var-field-label");
            typeRow.Add(typeLbl);
            var typeField = new EnumField(v.type) { name = "var-type-field" };
            typeField.AddToClassList("sgw-var-field-ctrl");
            typeField.RegisterValueChangedCallback(e =>
            {
                v.type = (VariableType)e.newValue;
                EditorUtility.SetDirty(ownerAsset);
                // 类型色点实时更新：换 sgw-type-* 类（颜色由 StoryEditorTheme.uss 的 --c-var-* 提供）
                typeDot.RemoveFromClassList("sgw-type-" + ((VariableType)e.previousValue).ToString().ToLowerInvariant());
                typeDot.AddToClassList("sgw-type-" + v.type.ToString().ToLowerInvariant());
            });
            typeField.RegisterCallback<FocusOutEvent>(_ => AssetDatabase.SaveAssets());
            FieldDrawerRegistry.ForceShrink(typeField);
            typeRow.Add(typeField);
            fold.Add(typeRow);

            var scopeRow = new VisualElement { name = "var-scope-row" };
            scopeRow.AddToClassList("sgw-var-field-row");
            var scopeLbl = new Label("作用域") { name = "var-scope-label" };
            scopeLbl.AddToClassList("sgw-var-field-label");
            scopeRow.Add(scopeLbl);
            var scopeField = new EnumField(v.scope) { name = "var-scope-field" };
            scopeField.AddToClassList("sgw-var-field-ctrl");
            scopeField.RegisterValueChangedCallback(e => { v.scope = (VariableScope)e.newValue; EditorUtility.SetDirty(ownerAsset); });
            scopeField.RegisterCallback<FocusOutEvent>(_ => AssetDatabase.SaveAssets());
            FieldDrawerRegistry.ForceShrink(scopeField);
            scopeRow.Add(scopeField);
            fold.Add(scopeRow);

            var defRow = new VisualElement { name = "var-def-row" };
            defRow.AddToClassList("sgw-var-field-row");
            var defLbl = new Label("默认") { name = "var-def-label" };
            defLbl.AddToClassList("sgw-var-field-label");
            defRow.Add(defLbl);
            var defField = new TextField { value = v.defaultValue ?? "", name = "var-def-field" };
            defField.AddToClassList("sgw-var-field-ctrl");
            defField.RegisterValueChangedCallback(e => { v.defaultValue = e.newValue; EditorUtility.SetDirty(ownerAsset); });
            defField.RegisterCallback<FocusOutEvent>(_ => AssetDatabase.SaveAssets());
            FieldDrawerRegistry.ForceShrink(defField);
            defRow.Add(defField);
            fold.Add(defRow);

            return fold;
        }

        private void CreateVariable()
        {
            if (_model == null || _asset == null) return;
            if (_model.Asset.variables == null) _model.Asset.variables = new List<StoryVariableDef>();
            var def = new StoryVariableDef
            {
                id = "var_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "新变量",
                type = VariableType.Int,
                scope = VariableScope.Local,
                defaultValue = "0",
            };
            _model.Asset.variables.Add(def);
            _varFoldState["local:" + def.id] = true; // 新建即展开，便于直接编辑
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            SwitchLeftTab(LeftTab.Variables); // 切到「变量」页，新建后可直接在面板内编辑
            OnModelChanged(new GraphChange(GraphChangeType.Reset));
            _statusLabel.text = "已新建变量：可在左侧「变量」面板直接改名 / 改类型 / 改作用域 / 改默认值";
        }

        private void CreateGlobalVariable(StoryGlobalVariableAsset g)
        {
            if (g == null) return;
            if (g.variables == null) g.variables = new List<StoryVariableDef>();
            var def = new StoryVariableDef
            {
                id = "var_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "新全局变量",
                type = VariableType.Int,
                scope = VariableScope.Global,
                defaultValue = "0",
            };
            g.variables.Add(def);
            _varFoldState["global:" + def.id] = true; // 新建即展开，便于直接编辑
            EditorUtility.SetDirty(g);
            AssetDatabase.SaveAssets();
            SwitchLeftTab(LeftTab.Variables);
            OnModelChanged(new GraphChange(GraphChangeType.Reset));
            _statusLabel.text = "已新建全局变量：跨章节共享，可被任意剧情图引用";
        }

        /// <summary>删除变量（全局/本图通用）：按变量引用从所属列表移除，并清理节点里的空引用。owner 用于标记脏数据。</summary>
        private void DeleteVariable(StoryVariableDef def, List<StoryVariableDef> list, ScriptableObject owner)
        {
            if (def == null || list == null || owner == null) return;
            int usage = _model.CountVariableUsage(def.id);
            if (usage > 0)
            {
                bool ok = EditorUtility.DisplayDialog("删除变量",
                    $"变量「{def.name}」被 {usage} 处引用。删除后这些引用将变为空（未定义），可能导致条件/赋值节点报错。\n仍要删除吗？",
                    "删除", "取消");
                if (!ok) return;
            }
            // 清理指向该变量的节点引用（含 Choice.options / Condition.clauses 内嵌引用），避免遗留空引用
            CleanVariableReferences(def.id);
            _varFoldState.Remove((owner is StoryGlobalVariableAsset ? "global:" : "local:") + def.id);
            list.Remove(def);
            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssets();
            _model.RebuildIndex();
            RefreshVariables();
            OnModelChanged(new GraphChange(GraphChangeType.Reset));
            _statusLabel.text = usage > 0 ? $"已删除变量「{def.name}」并清理 {usage} 处引用" : $"已删除变量「{def.name}」";
        }

        /// <summary>清理所有节点中指向该变量 id 的引用（variableId / conditionVariable，含 Choice.options 与 Condition.clauses 内嵌），避免遗留空引用。供单删与批量删共用。</summary>
        private void CleanVariableReferences(string id)
        {
            if (string.IsNullOrEmpty(id) || _model == null) return;
            foreach (var n in _model.Nodes)
            {
                foreach (var f in n.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if ((f.Name == "variableId" || f.Name == "conditionVariable") && f.GetValue(n) is string s && s == id)
                        f.SetValue(n, "");
                    else if (f.GetValue(n) is System.Collections.IList list2)
                    {
                        foreach (var elem in list2)
                        {
                            if (elem == null) continue;
                            foreach (var mf in elem.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                                if ((mf.Name == "variableId" || mf.Name == "conditionVariable") && mf.GetValue(elem) is string ms && ms == id)
                                    mf.SetValue(elem, "");
                            // 第二层：选项内嵌的条件组（ChoiceOption.conditionGroup -> ConditionClause.variableId）
                            var cg = elem.GetType().GetField("conditionGroup", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (cg != null && cg.GetValue(elem) is System.Collections.IList cgList)
                                foreach (var ce in cgList)
                                {
                                    if (ce == null) continue;
                                    foreach (var cf in ce.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                                        if ((cf.Name == "variableId" || cf.Name == "conditionVariable") && cf.GetValue(ce) is string cs && cs == id)
                                            cf.SetValue(ce, "");
                                }
                        }
                    }
                }
            }
        }

        // ── 变量多选 / 批量操作 ──

        private void UpdateVarBulkBar()
        {
            if (_varBulkBar == null || _varCountLabel == null) return;
            int n = _selectedVarKeys.Count;
            _varBulkBar.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _varCountLabel.text = $"已选 {n}";
        }

        private List<(ScriptableObject owner, List<StoryVariableDef> list, StoryVariableDef def)> ResolveSelectedVariables()
        {
            var result = new List<(ScriptableObject, List<StoryVariableDef>, StoryVariableDef)>();
            foreach (var key in _selectedVarKeys)
            {
                var parts = key.Split(':');
                if (parts.Length < 2) continue;
                string id = parts[1];
                if (parts[0] == "global")
                {
                    var g = GlobalVariableLookup.GetAsset();
                    if (g != null && g.variables != null)
                    {
                        var def = g.variables.FirstOrDefault(x => x.id == id);
                        if (def != null) result.Add((g, g.variables, def));
                    }
                }
                else
                {
                    var vars = _model != null ? _model.Asset.variables : null;
                    if (vars != null)
                    {
                        var def = vars.FirstOrDefault(x => x.id == id);
                        if (def != null) result.Add((_asset, vars, def));
                    }
                }
            }
            return result;
        }

        private void DeleteSelectedVariables()
        {
            var sel = ResolveSelectedVariables();
            if (sel.Count == 0) return;
            int totalUsage = 0;
            foreach (var (owner, list, def) in sel)
                if (_model != null) totalUsage += _model.CountVariableUsage(def.id);
            string msg = $"确定删除选中的 {sel.Count} 个变量？";
            if (totalUsage > 0) msg += $"\n其中有 {totalUsage} 处引用将变为空（可能导致条件/赋值节点报错）。";
            bool ok = EditorUtility.DisplayDialog("删除选中的变量", msg, "删除", "取消");
            if (!ok) return;
            foreach (var (owner, list, def) in sel)
            {
                CleanVariableReferences(def.id);
                _varFoldState.Remove((owner is StoryGlobalVariableAsset ? "global:" : "local:") + def.id);
                list.Remove(def);
                EditorUtility.SetDirty(owner);
            }
            AssetDatabase.SaveAssets();
            _model.RebuildIndex();
            _selectedVarKeys.Clear();
            RefreshVariables();
            OnModelChanged(new GraphChange(GraphChangeType.Reset));
            _statusLabel.text = totalUsage > 0 ? $"已删除 {sel.Count} 个变量并清理 {totalUsage} 处引用" : $"已删除 {sel.Count} 个变量";
        }

        /// <summary>把 variableId 解析为变量名（注入给 StoryConstants.VariableNameResolver，供节点摘要显示真名）。先查本图局部变量，再查全局变量资产。</summary>
        private string ResolveVariableName(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            if (_asset != null && _asset.variables != null)
            {
                var local = _asset.variables.FirstOrDefault(v => v.id == id);
                if (local != null) return string.IsNullOrEmpty(local.name) ? local.id : local.name;
            }
            var g = GlobalVariableLookup.GetAsset();
            if (g != null && g.variables != null)
            {
                var globalDef = g.variables.FirstOrDefault(v => v.id == id);
                if (globalDef != null) return string.IsNullOrEmpty(globalDef.name) ? globalDef.id : globalDef.name;
            }
            return id;
        }

        /// <summary>
        /// 点击代表色色块：打开 Unity 系统调色板设置角色代表色。
        /// 拖拽过程中实时落库到 colorHex 并刷新色块；调色板关闭后统一 SaveAssets 一次。
        /// </summary>
        private void OpenColorPicker(StoryCharacterAsset c, VisualElement swatch)
        {
            _colorPickerTarget = c;
            EditorApplication.update -= OnColorPickerTick;
            EditorApplication.update += OnColorPickerTick;
            OpenSystemColorPicker(ParseColor(c.colorHex), col =>
            {
                c.colorHex = "#" + ColorUtility.ToHtmlStringRGB(col);
                EditorUtility.SetDirty(c);
                swatch.style.backgroundColor = col;
            });
        }

        /// <summary>
        /// 打开 Unity 系统调色板。当前 Unity 版本的 EditorGUIUtility 未暴露 ShowColorPicker，
        /// 改用反射调用内部 UnityEditor.ColorPicker.Show(Action&lt;Color&gt;)。成功打开后才会在关闭后统一落盘。
        /// </summary>
        private static void OpenSystemColorPicker(Color initial, System.Action<Color> onChanged)
        {
            var pickerType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ColorPicker");
            if (pickerType == null) return;
            // 尽量把初始色写入调色板当前色（部分版本通过静态 color 属性）
            var colorProp = pickerType.GetProperty("color",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (colorProp != null && colorProp.CanWrite)
            {
                try { colorProp.SetValue(null, initial); } catch (System.Exception) { /* 忽略，使用默认初始色 */ }
            }
            // 按方法名 + 首参为 Action<Color> 定位静态 Show（兼容不同参数签名/默认值），避免精确类型匹配失败。
            System.Reflection.MethodInfo show = null;
            foreach (var m in pickerType.GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "Show") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(System.Action<Color>)) { show = m; break; }
            }
            if (show == null) return;
            var ps2 = show.GetParameters();
            var args = new object[ps2.Length];
            args[0] = onChanged;
            for (int i = 1; i < ps2.Length; i++)
            {
                var p = ps2[i];
                if (p.HasDefaultValue) args[i] = p.DefaultValue;
                else if (p.ParameterType == typeof(bool)) args[i] = true;
                else if (p.ParameterType == typeof(Color)) args[i] = initial;
                else args[i] = null;
            }
            show.Invoke(null, args);
        }

        private void OnColorPickerTick()
        {
            if (IsColorPickerOpen()) return;
            EditorApplication.update -= OnColorPickerTick;
            if (_colorPickerTarget != null)
            {
                EditorUtility.SetDirty(_colorPickerTarget);
                AssetDatabase.SaveAssets();
                _colorPickerTarget = null;
            }
        }

        private static bool IsColorPickerOpen()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w.GetType().Name == "ColorPicker") return true;
            return false;
        }

        private static Color ParseColor(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return new Color(0.22f, 0.54f, 0.86f);
        }

        // ── 属性面板 ──
        private void RefreshInspector(IReadOnlyList<StoryNodeData> nodes)
        {
            if (_inspectorPane == null) return;
            _inspectorPane.Clear();
            if (nodes == null || nodes.Count == 0)
            {
                var emptyLbl = new Label("未选择节点") { name = "inspector-empty" };
                emptyLbl.AddToClassList("sgw-inspector-empty");
                _inspectorPane.Add(emptyLbl);
                return;
            }

            if (nodes.Count > 1)
            {
                RefreshInspectorMulti(nodes);
                return;
            }

            // 单节点
            var node = nodes[0];
            var header = new Label(node.DisplayTitle()) { name = "inspector-header" };
            header.AddToClassList("sgw-inspector-header");
            _inspectorPane.Add(header);

            var typeLabel = new Label($"类型：{node.GetType().Name}") { name = "inspector-type" };
            typeLabel.AddToClassList("sgw-node-type-label");
            _inspectorPane.Add(typeLabel);

            _inspectorPane.Add(FieldDrawerRegistry.Build(_model, nodes, () => RefreshInspectorForSelection(),
                () => _graphView?.Populate(false)));

            _inspectorPane.Add(MakeDeleteNodeButton(node.id));
        }

        /// <summary>多选：仅当全部同类型时进入批量编辑（含删除全部）；不同类型则只显示「节点类型不同」提示，不渲染任何属性。</summary>
        private void RefreshInspectorMulti(IReadOnlyList<StoryNodeData> nodes)
        {
            var type0 = nodes[0].GetType();
            bool sameType = nodes.All(n => n.GetType() == type0);

            if (!sameType)
            {
                // 不同类型：不渲染任何属性，仅提示节点类型不同（避免批量误改）
                var hint = new Label($"节点类型不同（已选 {nodes.Count} 个不同类型的节点，无法批量编辑）") { name = "inspector-type-mixed" };
                hint.AddToClassList("sgw-inspector-note");
                _inspectorPane.Add(hint);
                return;
            }

            // 同类型批量
            var batchHeader = new Label($"批量编辑（{nodes.Count} 个「{type0.Name}」节点）") { name = "inspector-header" };
            batchHeader.AddToClassList("sgw-inspector-header");
            _inspectorPane.Add(batchHeader);
            var note = new Label("修改将应用到全部选中节点；值不同的字段以高亮标出。列表类字段（选项/条件）在各节点间独立，不批量编辑。") { name = "inspector-batch-note" };
            note.AddToClassList("sgw-inspector-note");
            _inspectorPane.Add(note);

            _inspectorPane.Add(FieldDrawerRegistry.Build(_model, nodes, () => RefreshInspectorForSelection()));

            var delAll = new Button(() =>
            {
                foreach (var n in nodes)
                    _model.ExecuteCommand(new RemoveNodeCommand(n.id));
            }) { text = $"删除选中的 {nodes.Count} 个节点" };
            delAll.AddToClassList("sgw-del-btn");
            _inspectorPane.Add(delAll);
        }

        private void RefreshInspectorForSelection() =>
            RefreshInspector(_graphView.SelectedNodeViews().Select(v => v.Data).ToList());

        private Button MakeDeleteNodeButton(string id)
        {
            var b = new Button(() => _model.ExecuteCommand(new RemoveNodeCommand(id)))
            {
                text = "删除节点",
                name = "inspector-del",
            };
            b.AddToClassList("sgw-del-btn");
            return b;
        }

        private void OnSelectionChanged()
        {
            if (_graphView == null) return;
            var nodes = _graphView.SelectedNodeViews().Select(v => v.Data).ToList();
            RefreshInspector(nodes);
            UpdateStatus();
        }

        /// <summary>供打字机时间轴窗口在自身 OnEnable 时补抓当前选中节点（解决「先选中、后开窗口」的空白问题）。
        /// 最近打开的剧情编辑器窗口中，若恰好选中一个对话节点则返回它，并通过 out 返回其模型；否则返回 null。</summary>
        internal static DialogueNodeData GetSelectedDialogueNode(out StoryGraphModel model)
        {
            model = null;
            var w = Resources.FindObjectsOfTypeAll<StoryGraphWindow>().FirstOrDefault();
            if (w == null || w._graphView == null || w._model == null) return null;
            var nodes = w._graphView.SelectedNodeViews().Select(v => v.Data).ToList();
            if (nodes.Count == 1 && nodes[0] is DialogueNodeData dlg) { model = w._model; return dlg; }
            return null;
        }

        // ── 搜索高亮 ──
        private void ApplySearch(string query)
        {
            if (_graphView == null) return;
            var q = query?.Trim().ToLowerInvariant() ?? "";
            _searchMatches.Clear();
            foreach (var nv in _graphView.AllNodeViews())
            {
                bool hit = string.IsNullOrEmpty(q) ||
                           (nv.Data.DisplayTitle()?.ToLowerInvariant().Contains(q) ?? false) ||
                           (nv.Data.GetSummary()?.ToLowerInvariant().Contains(q) ?? false) ||
                           (nv.Data.id?.ToLowerInvariant().Contains(q) ?? false) ||
                           (nv.Data.SearchSpeaker?.ToLowerInvariant().Contains(q) ?? false);
                nv.EnableInClassList("search-hit", !string.IsNullOrEmpty(q) && hit);
                nv.style.unityFontStyleAndWeight = (!string.IsNullOrEmpty(q) && hit) ? FontStyle.Bold : FontStyle.Normal;
                if (!string.IsNullOrEmpty(q) && hit) _searchMatches.Add(nv);
            }
            _searchIndex = -1; // 下次 Enter 从第 1 个匹配开始
            UpdateSearchResultLabel();
        }

        private void UpdateSearchResultLabel()
        {
            if (_searchResultLabel == null) return;
            _searchResultLabel.text = _searchMatches.Count == 0 ? "" : $"命中 {_searchMatches.Count}";
        }

        /// <summary>搜索框键盘事件：Enter 跳到下一个匹配、Shift+Enter 上一个；Esc 清空搜索并失焦。</summary>
        private void OnSearchKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                JumpToSearchMatch(!evt.shiftKey);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                _searchField.value = "";
                ApplySearch("");
                _searchField.Blur();
            }
        }

        /// <summary>跳转：选中并居中目标节点（B8 搜索定位核心）。首次 Enter 定位第 1 个匹配，循环切换。</summary>
        private void JumpToSearchMatch(bool forward)
        {
            if (_graphView == null || _searchMatches.Count == 0) return;
            if (_searchIndex < 0) _searchIndex = 0; // 首次跳转直接定位第一个
            else if (forward) _searchIndex = (_searchIndex + 1) % _searchMatches.Count;
            else _searchIndex = (_searchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
            var nv = _searchMatches[_searchIndex];
            _graphView.SelectNode(nv);
            _graphView.FrameNode(nv);
            if (_searchResultLabel != null) _searchResultLabel.text = $"{_searchIndex + 1}/{_searchMatches.Count}";
        }

        // ── 快捷键 ──
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (_model == null) return;
            if (evt.actionKey && evt.keyCode == KeyCode.S) { Save(); evt.StopPropagation(); }
            else if (evt.actionKey && evt.keyCode == KeyCode.C) { CopySelection(); evt.StopPropagation(); }
            else if (evt.actionKey && evt.keyCode == KeyCode.V) { Paste(); evt.StopPropagation(); }
            else if (evt.actionKey && evt.keyCode == KeyCode.D) { Duplicate(); evt.StopPropagation(); }
            else if (evt.actionKey && evt.keyCode == KeyCode.F) { _searchField?.Focus(); _searchField?.SelectAll(); evt.StopPropagation(); }
            else if (evt.actionKey && evt.keyCode == KeyCode.P) { StartPlayback(); evt.StopPropagation(); } // 从此处预览（选中节点时生效）
            else if (evt.actionKey && evt.keyCode == KeyCode.G) { _graphView?.GroupSelection(); evt.StopPropagation(); } // 将选中节点打包为分组
            else if (!evt.actionKey && evt.keyCode == KeyCode.F) { FrameView(); evt.StopPropagation(); } // 适应视图：有选中聚焦选中，否则全图
            else if (!evt.actionKey && (evt.keyCode == KeyCode.Space || StoryGraphView.IsQuickCreateKey(evt.keyCode)))
            {
                // 「修饰键 + 左键点击」快捷建节点：此处仅记录按住的修饰键（Space / D / O / V / C / E），
                // 真正的创建发生在画布左键按下时（StoryGraphView.OnCanvasMouseDown）。
                // 编辑文本（节点标题 / 搜索框等 TextField）时不拦截，避免打断输入。
                if (evt.target is TextField) return;
                if (_graphView == null) return;
                _graphView.SetCreateModifier(evt.keyCode, true);
                evt.StopPropagation();
            }
            // 注：Ctrl+Z / Ctrl+Y（撤销/重做）不在此显式绑定——交由 Unity 原生 Undo 栈处理；
            // 若在此额外调用 Undo.PerformUndo/Redo 会与编辑器全局快捷键「双重撤销」，故刻意不绑。
        }

        /// <summary>键抬起时清除建节点修饰键状态，使「修饰键 + 左键」的按住判定复位。</summary>
        private void OnKeyUp(KeyUpEvent evt)
        {
            if (_graphView == null) return;
            if (evt.keyCode == KeyCode.Space || StoryGraphView.IsQuickCreateKey(evt.keyCode))
                _graphView.SetCreateModifier(evt.keyCode, false);
        }

        /// <summary>适应视图：无选中＝全图取景；有选中＝聚焦首个选中节点。</summary>
        private void FrameView()
        {
            if (_graphView == null) return;
            var sel = _graphView.SelectedNodeViews().FirstOrDefault();
            if (sel != null) _graphView.FrameNode(sel);
            else _graphView.FrameAll();
        }

        private void CopySelection()
        {
            var sel = _graphView.SelectedNodeViews().Select(v => v.Data).ToList();
            if (sel.Count == 0) return;
            var ids = new HashSet<string>(sel.Select(n => n.id));
            _clipboardNodes = sel.Select(n => ReflectionUtil.DeepClone(n)).ToList();
            _clipboardEdges = _model.Asset.edges
                .Where(e => ids.Contains(e.fromNodeId) && ids.Contains(e.toNodeId))
                .Select(e => ReflectionUtil.DeepClone(e)).ToList();
            _statusLabel.text = $"已复制 {sel.Count} 个节点";
        }

        private void Paste()
        {
            if (_clipboardNodes == null || _clipboardNodes.Count == 0) return;
            _model.ExecuteCommand(new PasteCommand(_clipboardNodes, _clipboardEdges, new Vector2(40, 40)));
            _statusLabel.text = "已粘贴";
        }

        private void Duplicate()
        {
            var ids = _graphView.SelectedNodeViews().Select(v => v.NodeId).ToList();
            if (ids.Count == 0) return;
            _model.ExecuteCommand(new DuplicateCommand(ids, new Vector2(40, 40)));
            _statusLabel.text = "已复制";
        }

        private void UpdateStatus()
        {
            if (_model == null) { _statusLabel.text = "未加载资产"; return; }
            int nodes = _model.Nodes.Count();
            int edges = _model.Asset.edges.Count;
            var status = $"节点 {nodes} · 连线 {edges}";
            if (_issues != null)
            {
                int err = _issues.Count(i => i.Severity == ValidationSeverity.Error);
                int warn = _issues.Count(i => i.Severity == ValidationSeverity.Warning);
                status += $" · 校验 {err}错误/{warn}警告";
            }
            status += $" · {(_model.IsDirty ? "未保存*" : "已保存")}";
            _statusLabel.text = status;
            if (_dirtyLabel != null)
                _dirtyLabel.text = _model.IsDirty ? "● 未保存" : "✓ 已保存";
        }

        // ── 校验 ──
        private void RunValidation()
        {
            if (_model == null) { _issues = new List<ValidationIssue>(); RefreshValidation(); return; }
            _issues = StoryValidator.Validate(_model);
            RefreshValidation();
            UpdateStatus();
        }

        private void RefreshValidation()
        {
            if (_validationFoldout == null || _validationPane == null) return;
            _validationPane.Clear();
            if (_issues == null)
            {
                _validationFoldout.text = "校验问题（未运行）";
                _validationFoldout.value = false;
                return;
            }
            int err = _issues.Count(i => i.Severity == ValidationSeverity.Error);
            int warn = _issues.Count(i => i.Severity == ValidationSeverity.Warning);
            _validationFoldout.text = $"校验问题（{err} 错误 / {warn} 警告）";
            if (_issues.Count == 0)
            {
                var okLbl = new Label("✓ 没有发现问题") { name = "validation-ok" };
                okLbl.AddToClassList("sgw-validation-ok");
                _validationPane.Add(okLbl);
                return;
            }
            foreach (var issue in _issues)
            {
                var severityClass = issue.Severity == ValidationSeverity.Error
                    ? "sgw-validation-error"
                    : issue.Severity == ValidationSeverity.Warning
                        ? "sgw-validation-warning"
                        : "sgw-validation-info";
                var icon = issue.Severity == ValidationSeverity.Error ? "✕" : issue.Severity == ValidationSeverity.Warning ? "!" : "i";
                var row = new VisualElement { name = "validation-row" };
                row.AddToClassList("sgw-validation-row");
                row.AddToClassList(severityClass);
                var iconLbl = new Label(icon) { name = "validation-icon" };
                iconLbl.AddToClassList("sgw-validation-icon");
                row.Add(iconLbl);
                var msgLbl = new Label(issue.Message) { name = "validation-msg" };
                msgLbl.AddToClassList("sgw-validation-msg");
                row.Add(msgLbl);
                if (!string.IsNullOrEmpty(issue.NodeId))
                {
                    var t = _model?.GetNode(issue.NodeId);
                    if (t != null)
                    {
                        var nodeLbl = new Label($"[{t.DisplayTitle()}]") { name = "validation-node" };
                        nodeLbl.AddToClassList("sgw-validation-node");
                        row.Add(nodeLbl);
                    }
                }
                var captured = issue;
                // G7 偏离修复：规范 02 §⑤ 要求「双击行→画布平移并高亮问题节点」，原为单击。
                row.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount >= 2) LocateIssue(captured);
                });
                _validationPane.Add(row);
            }
        }

        private void LocateIssue(ValidationIssue issue)
        {
            if (_graphView == null) return;
            foreach (var nodeView in _graphView.AllNodeViews()) nodeView.ClearValidation();
            if (string.IsNullOrEmpty(issue.NodeId))
            {
                _statusLabel.text = "该问题为图级问题，无对应节点。";
                return;
            }
            var targetView = _graphView.GetNodeView(issue.NodeId);
            if (targetView == null) { _statusLabel.text = "找不到对应节点。"; return; }
            _graphView.SelectNode(targetView);
            _graphView.FrameNode(targetView);
            targetView.MarkValidation(issue.Severity);
            _statusLabel.text = $"已定位：{issue.Message}";
        }

        // ── 试跑 ──
        private void StartPlayback()
        {
            if (_model == null) return;
            var sel = _graphView.SelectedNodeViews().FirstOrDefault();
            var start = sel?.Data ?? _model.GetEntryNode();
            if (start == null) { _statusLabel.text = "没有可用入口或选中节点。"; return; }
            StoryPlaybackWindow.Open(_model, start);
        }

        /// <summary>供试跑窗口回调：在画布中高亮当前执行节点（选中 + 滚动 + 蓝色边框）。</summary>
        public void HighlightInGraph(string nodeId)
        {
            if (_graphView == null) return;
            _graphView.HighlightPlayback(nodeId);
        }

        // ══ 数据流转 ══

        private static string _lastIoDir;

        private void ExportJson()
        {
            if (_asset == null) return;
            var path = EditorUtility.SaveFilePanel("导出剧情 JSON", _lastIoDir, _asset.name, "json");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            // JSON 是备份/交换通道：完整导出（含布局态 position/groups/stickyNotes），玩家包剥离由数据模型 #if UNITY_EDITOR 字段实现。
            File.WriteAllText(path, StoryJsonExporter.Export(_asset));
            _statusLabel.text = $"已导出 JSON：{Path.GetFileName(path)}";
        }

        private void ImportJson()
        {
            if (_asset == null) return;
            var path = EditorUtility.OpenFilePanel("导入剧情 JSON", _lastIoDir, "json");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            try
            {
                _model.ExecuteCommand(new ImportCommand("导入剧情图 JSON", a => StoryJsonExporter.Import(a, File.ReadAllText(path))));
                Load(_asset, false); // 重建视图（不刷新基线：导入的未保存改动可被「放弃」回滚到导入前）
                _statusLabel.text = $"已导入 JSON：{Path.GetFileName(path)}";
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("导入失败", ex.Message, "确定");
            }
        }

        private void ExportCsv()
        {
            if (_asset == null) return;
            var table = EnsureLocalizationTable();
            var path = EditorUtility.SaveFilePanel("导出本地化表格 CSV", _lastIoDir, _asset.name + ".l10n", "csv");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            try
            {
                File.WriteAllText(path, StoryLocalizationCsv.ExportCsv(table), Encoding.UTF8);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 0x20 || (ex.HResult & 0xFFFF) == 0x21)
            {
                // Windows 文件锁：Excel 占用 CSV 时拒绝其他进程写入
                _statusLabel.text = "写入失败：CSV 正被其他程序（如 Excel）占用，请先关闭该文件再导出。";
                return;
            }
            int count = table.entries != null ? table.entries.Count : 0;
            _statusLabel.text = $"已导出本地化表格 CSV（{count} 条）：{Path.GetFileName(path)}";
        }

        // ── 本地化：主表（StoryLocalizationTable）为唯一真相源 ──

        /// <summary>从图把缺失的 key 增量同步进主表（保留已有译文）。</summary>
        private void SyncLocalization()
        {
            if (_asset == null) return;
            var table = EnsureLocalizationTable();
            try
            {
                int added = StoryLocalizationCsv.SyncFromGraph(_asset, table);
                AssetDatabase.SaveAssets();
                int total = table.entries != null ? table.entries.Count : 0;
                _statusLabel.text = $"已从图同步本地化 Key：新增 {added} 条，主表共 {total} 条";
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("同步失败", ex.Message, "确定");
            }
        }

        private void ImportCsvToTable()
        {
            if (_asset == null) return;
            var path = EditorUtility.OpenFilePanel("导入本地化表格 CSV → 主表", _lastIoDir, "csv");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            try
            {
                var table = EnsureLocalizationTable();
                var rep = StoryLocalizationCsv.ImportCsvToTable(File.ReadAllText(path), table);
                AssetDatabase.SaveAssets();
                _statusLabel.text = $"已合并本地化表格 CSV：{rep.message}";
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("导入失败", ex.Message, "确定");
            }
        }

        private void ImportLocalizationXlsxToTable()
        {
            if (_asset == null) return;
            var path = EditorUtility.OpenFilePanel("导入本地化 Excel → 主表", _lastIoDir, "xlsx");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            try
            {
                var table = EnsureLocalizationTable();
                var sheets = StoryXlsx.ReadWorkbook(path);
                var sheet = sheets.FirstOrDefault(s => s.Name == StoryLocalizationXlsx.SheetName)
                         ?? sheets.FirstOrDefault(s => s.Rows.Count > 0 && s.Rows[0].Any(c => c != null && c.Trim().Equals("Key", System.StringComparison.OrdinalIgnoreCase)));
                if (sheet == null) throw new System.Exception("文件中未找到工作表。");
                var rep = StoryLocalizationXlsx.ImportFromRowsToTable(sheet.Rows, table);
                AssetDatabase.SaveAssets();
                _statusLabel.text = $"已合并本地化 Excel：{rep.message}";
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("导入失败", ex.Message, "确定");
            }
        }

        /// <summary>确保与当前图对应的主表资产存在：Resources/Story/Localization/&lt;图名&gt;/&lt;图名&gt;.l10ntable.asset（无则新建 Localization/&lt;图名&gt; 子文件夹）。
        /// 主表持久化于 Resources（进包），与图同名自成一文件夹；并回写当前图的 localizationTable 字段，使运行时跳转章节后能跟随切换。</summary>
        private StoryLocalizationTable EnsureLocalizationTable()
        {
            var locPath = StoryAssetPaths.GetLocalizationTablePath(_asset.name);
            bool createdDir = StoryAssetPaths.EnsureFolder(StoryAssetPaths.GetLocalizationDir(_asset.name));
            if (createdDir) AssetDatabase.Refresh();
            var existing = AssetDatabase.LoadAssetAtPath<StoryLocalizationTable>(locPath);
            if (existing != null)
            {
                BindGraphLocalization(existing);
                return existing;
            }
            var created = ScriptableObject.CreateInstance<StoryLocalizationTable>();
            AssetDatabase.CreateAsset(created, locPath);
            AssetDatabase.SaveAssets();
            BindGraphLocalization(created);
            return created;
        }

        /// <summary>把本地化主表回写到当前图的 localizationTable 字段（internal，编辑器经 InternalsVisibleTo 可写），供运行时按当前图取表。</summary>
        private void BindGraphLocalization(StoryLocalizationTable table)
        {
            if (_asset != null && _asset.localizationTable != table)
            {
                _asset.localizationTable = table;
                EditorUtility.SetDirty(_asset);
            }
        }

        /// <summary>
        /// 新建「剧情表节点」：选一个剧情表文件（csv/xlsx）→ 建 StoryTableAsset(SO) → 建一个 StoryTableNodeData 引用它。
        /// 表即真相源；双击该节点在子画布里按数据派生虚拟子图，主图只显示其头/尾端口。
        /// </summary>
        private void CreateTableNode()
        {
            if (_asset == null) return;
            var path = EditorUtility.OpenFilePanel("新建剧情表节点（选剧情表格文件）", _lastIoDir, "csv,xlsx");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            string tableName = Path.GetFileNameWithoutExtension(path);
            try
            {
                _model.ExecuteCommand(new ImportCommand("新建剧情表节点", a =>
                {
                    StoryAssetPaths.EnsureFolder(StoryAssetPaths.TablesDir);
                    var table = ScriptableObject.CreateInstance<StoryTableAsset>();
                    StoryTableAssetImporter.ImportFromFile(table, path, out _);
                    table.sourceFilePath = StoryAssetPaths.ToProjectRelative(path); // 存项目相对路径，工程移动后仍可解析
                    var safe = StoryAssetPaths.Sanitize(tableName);
                    if (string.IsNullOrEmpty(safe)) safe = "StoryTable";
                    var tpath = AssetDatabase.GenerateUniqueAssetPath($"{StoryAssetPaths.TablesDir}/Table_{safe}.asset");
                    AssetDatabase.CreateAsset(table, tpath);

                    var node = (StoryTableNodeData)NodeRegistry.Create(typeof(StoryTableNodeData));
                    node.position = new Vector2(60, 60);
                    node.tableAsset = table;
                    a.nodes.Add(node);
                }));
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Load(_asset, false);
                _statusLabel.text = $"已新建剧情表节点：{tableName}（表资产已建在 Story/Tables/）";
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("创建失败", ex.Message, "确定");
            }
        }

        /// <summary>从已有 SO 新建「剧情表节点」：选一个 StoryTableAsset，建一个 StoryTableNodeData 引用它（不读 Excel，以 SO 现有 rows 为准）。</summary>
        private void CreateTableNodeFromSo()
        {
            if (_asset == null) return;
            var path = EditorUtility.OpenFilePanel("选择已有剧情表资产（SO）以新建剧情表节点", StoryAssetPaths.TablesDir, "asset");
            if (string.IsNullOrEmpty(path)) return;
            string normPath = path.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (!normPath.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("创建失败", "所选文件不在当前项目的 Assets 目录内。", "确定");
                return;
            }
            string assetPath = "Assets" + normPath.Substring(dataPath.Length);
            var so = AssetDatabase.LoadAssetAtPath<StoryTableAsset>(assetPath);
            if (so == null) { EditorUtility.DisplayDialog("创建失败", "所选文件不是 StoryTableAsset。", "确定"); return; }
            try
            {
                _model.ExecuteCommand(new ImportCommand("从 SO 新建剧情表节点", a =>
                {
                    var node = (StoryTableNodeData)NodeRegistry.Create(typeof(StoryTableNodeData));
                    node.position = new Vector2(60, 60);
                    node.tableAsset = so;
                    a.nodes.Add(node);
                }));
                Load(_asset, false);
                _statusLabel.text = $"已从 SO「{so.name}」新建剧情表节点";
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("创建失败", ex.Message, "确定");
            }
        }

        /// <summary>重新导入并同步：对每个剧情表节点，从其 SO 的 sourceFilePath 重新读 Excel/csv 覆盖 rows（虚拟子图随之刷新，无需重烘焙）。用于「改了 Excel」后的同步。</summary>
        private void ReimportAndSyncAllTables()
        {
            if (_asset == null) return;
            int n = 0, skip = 0;
            try
            {
                _model.ExecuteCommand(new ImportCommand("重新导入并同步剧情表", a =>
                {
                    (n, skip) = StoryTableAssetImporter.ReimportAllTables(a);
                }));
                Load(_asset, false);
                _statusLabel.text = $"已重新导入并同步 {n} 个剧情表" + (skip > 0 ? $"（{skip} 个未找到源文件，已跳过）" : "");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("同步失败", ex.Message, "确定");
            }
        }

        private static bool HasTextHeader(string[] header)
        {
            if (header == null) return false;
            foreach (var h in header)
            {
                if (h == null) continue;
                var t = h.Trim();
                // 兼容「中文(英文)」双列表头：剥掉括号内容再比对（如「正文(Text)」→「正文」）
                int i = t.IndexOf('(');
                if (i < 0) i = t.IndexOf('（');
                if (i >= 0) t = t.Substring(0, i).Trim();
                if (t.Equals("Text", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("正文", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("对白", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("台词", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void ShowStats()
        {
            if (_model == null) return;
            StoryStatsWindow.Show(_model);
        }

        // ══ Excel 数据流转（扩展）══

        private void ExportLocalizationXlsx()
        {
            if (_asset == null) return;
            var table = EnsureLocalizationTable();
            var path = EditorUtility.SaveFilePanel("导出本地化 Excel（从主表）", _lastIoDir, _asset.name + ".l10n", "xlsx");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            var sheets = new List<XlsSheet> { StoryLocalizationXlsx.BuildSheet(table) };
            StoryXlsx.WriteWorkbook(path, sheets);
            int count = table.entries != null ? table.entries.Count : 0;
            _statusLabel.text = $"已导出本地化 Excel（{count} 条）：{Path.GetFileName(path)}";
        }


        private void ExportNodesXlsx()
        {
            if (_asset == null) return;
            var path = EditorUtility.SaveFilePanel("导出节点属性 Excel", _lastIoDir, _asset.name + ".nodes", "xlsx");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            var sheets = new List<XlsSheet> { StoryNodesXlsx.BuildSheet(_asset) };
            StoryXlsx.WriteWorkbook(path, sheets);
            _statusLabel.text = $"已导出节点属性 Excel：{Path.GetFileName(path)}";
        }

        private void ImportNodesXlsx()
        {
            if (_asset == null) return;
            var path = EditorUtility.OpenFilePanel("导入节点属性 Excel", _lastIoDir, "xlsx");
            if (string.IsNullOrEmpty(path)) return;
            _lastIoDir = Path.GetDirectoryName(path);
            try
            {
                var sheets = StoryXlsx.ReadWorkbook(path);
                var sheet = sheets.FirstOrDefault(s => s.Name == StoryNodesXlsx.SheetName)
                         ?? sheets.FirstOrDefault(s => s.Rows.Count > 0 && s.Rows[0].Any(c => c != null && c.Trim().Equals("NodeId", System.StringComparison.OrdinalIgnoreCase)));
                if (sheet == null) throw new System.Exception("文件中未找到工作表。");
                ImportReport rep = default;
                _model.ExecuteCommand(new ImportCommand("导入节点属性 Excel", a => rep = StoryNodesXlsx.ImportFromRows(a, sheet.Rows)));
                if (rep.changed == 0)
                {
                    EditorUtility.DisplayDialog("导入完成", rep.message, "确定");
                    return;
                }
                Load(_asset, false);
                _statusLabel.text = $"已导入节点属性 Excel：{rep.message}";
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("导入失败", ex.Message, "确定");
            }
        }
    }

    /// <summary>轻量重命名输入窗口（IMGUI）。供资源树重命名剧情图使用。</summary>
    public sealed class RenameDialog : EditorWindow
    {
        private string _initial;
        private System.Action<string> _onOk;
        private string _value;

        public static void Show(string title, string initial, System.Action<string> onOk)
        {
            var w = ScriptableObject.CreateInstance<RenameDialog>();
            w.titleContent = new GUIContent(title);
            w._initial = initial ?? "";
            w._onOk = onOk;
            w.position = new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 40f, 360, 86);
            w.ShowAuxWindow();
        }

        private void OnEnable() => _value = _initial;

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            _value = EditorGUILayout.TextField("名称", _value);
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("确定", GUILayout.Height(24)))
            {
                var v = _value?.Trim();
                _onOk?.Invoke(v);
                Close();
            }
            if (GUILayout.Button("取消", GUILayout.Height(24)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
