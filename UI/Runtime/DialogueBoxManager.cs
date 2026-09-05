using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 对话框管理系统核心（编排器）。
    /// 职责：生成（对象池）/ 关闭 / 层级（sibling 顺序）/ 模态输入拦截 / 生命周期回调 / 场景卸载清理。
    /// 以懒加载单例形式存在；也可手动挂到场景里（Add Component 菜单：MicrobialNet / Story / Dialogue Box Manager）。
    /// </summary>
    [AddComponentMenu("MicrobialNet/Story/Dialogue Box Manager", 40)]
    [RequireComponent(typeof(Canvas))]
    public sealed class DialogueBoxManager : MonoBehaviour
    {
        /// <summary>懒加载单例。</summary>
        public static DialogueBoxManager Instance => _instance;

        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private int poolCapacityPerStyle = 8;

        private static DialogueBoxManager _instance;
        private bool _eventSystemEnsured;

        private readonly Dictionary<string, DialogueBoxStyle> _styles =
            new Dictionary<string, DialogueBoxStyle>();
        private readonly Dictionary<string, Stack<DialogueBox>> _pool =
            new Dictionary<string, Stack<DialogueBox>>();
        private readonly List<DialogueBox> _active = new List<DialogueBox>();

        /// <summary>上一框非立即关闭时，其离场动画应结束的时刻（Time.time 尺度）。Open 据此延迟新框出现（留存过渡）。</summary>
        private float _retainUntil;
        private readonly Dictionary<string, IDialogueBoxSpawnStrategy> _spawnStrategies =
            new Dictionary<string, IDialogueBoxSpawnStrategy>();
        // 节点级策略键 → 策略（与按样式键注册的 _spawnStrategies 分离，避免键空间冲突）
        private readonly Dictionary<string, IDialogueBoxSpawnStrategy> _spawnStrategiesByKey =
            new Dictionary<string, IDialogueBoxSpawnStrategy>();
        private bool _strategiesByKeyLoaded;
        private bool _stylesByKeyLoaded;
        private int _spawnCounter;

        private Canvas _canvas;
        private RectTransform _canvasRT;

        // ── 单例 / 生命周期 ──
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // 重复实例：销毁策略必须保护宿主物体上的其他脚本（如 StoryFlow / StoryView）。
                // 若本物体上除自身外没有任何其他组件，才连同物体一起销毁（避免留下空物体）；
                // 否则只移除本组件。否则在「StoryView.Awake 先于本组件触发 Ensure() 创建竞争实例」的时序下，
                // Destroy(gameObject) 会误删承载 StoryFlow 的整个物体。
                bool onlySelf = true;
                foreach (var c in gameObject.GetComponents<MonoBehaviour>())
                    if (c != (MonoBehaviour)this) { onlySelf = false; break; }
                if (onlySelf) Destroy(gameObject);
                else Destroy(this);
                return;
            }
            _instance = this;
            if (rootCanvas == null) rootCanvas = GetComponent<Canvas>();
            EnsureCanvas();
            EnsureEventSystem();
            _eventSystemEnsured = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var b = _active[i];
                if (b.spec != null && b.spec.position != null &&
                    b.spec.position.mode == DialogueBoxPositionMode.WorldFollow)
                    b.UpdateWorldFollow();
            }
        }

        /// <summary>确保存在一个管理器实例（场景里没有时自动创建）。</summary>
        public static DialogueBoxManager Ensure()
        {
            if (_instance == null)
            {
                var go = new GameObject("DialogueBoxManager");
                _instance = go.AddComponent<DialogueBoxManager>();
            }
            return _instance;
        }

        private void EnsureCanvas()
        {
            _canvas = rootCanvas != null ? rootCanvas : GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            EnsureRaycaster();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasRT = (RectTransform)_canvas.transform;
        }

        // uGUI 指针事件（IPointerDownHandler 等）要被命中，承载对话框的 Canvas 必须挂 GraphicRaycaster，
        // 否则射线打不到任何子物体，点击会静默失效且无任何报错。
        private void EnsureRaycaster()
        {
            if (_canvas != null && _canvas.GetComponent<GraphicRaycaster>() == null)
                _canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        // ── 输入系统（uGUI 指针事件必须由 EventSystem 派发，否则 IPointerDownHandler 永远收不到点击）──
        private static void EnsureEventSystem()
        {
            var es = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
            // 选择与项目输入设置匹配的输入模块：
            // Active Input Handling = New(1)/Both(2) 时，StandaloneInputModule 收不到任何点击，必须换成 InputSystemUIInputModule。
            // 仅 Old(0) 才用 StandaloneInputModule。读不到设置（如打包后）则按“存在新输入模块就优先”兜底。
            var newInputType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            int mode = ReadActiveInputHandler();
            bool preferNew = newInputType != null && (mode == 1 || mode == 2 || mode == -1);

            var existing = es.GetComponent<BaseInputModule>();
            if (preferNew)
            {
                if (existing == null || existing.GetType() != newInputType)
                {
                    if (existing != null) UnityEngine.Object.Destroy(existing);
                    es.gameObject.AddComponent(newInputType);
                }
            }
            else
            {
                if (existing == null)
                    es.gameObject.AddComponent<StandaloneInputModule>();
                else if (newInputType != null && existing.GetType() == newInputType)
                {
                    UnityEngine.Object.Destroy(existing);
                    es.gameObject.AddComponent<StandaloneInputModule>();
                }
            }
        }

        // 读取 ProjectSettings 的 activeInputHandler（编辑器 / Play 模式可读取文件；打包后读不到返回 -1）。
        // 0=Old, 1=New, 2=Both。
        private static int ReadActiveInputHandler()
        {
            try
            {
                var parent = Directory.GetParent(Application.dataPath);
                if (parent != null)
                {
                    var path = Path.Combine(parent.FullName, "ProjectSettings", "ProjectSettings.asset");
                    if (File.Exists(path))
                    {
                        foreach (var line in File.ReadAllLines(path))
                        {
                            if (line.Contains("activeInputHandler:"))
                            {
                                var v = line.Substring(line.IndexOf(':') + 1).Trim();
                                if (int.TryParse(v, out var i)) return i;
                            }
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        // ── 样式注册 ──
        public void RegisterStyle(string key, GameObject template, float intro = 0.2f, float outro = 0.2f, float retain = 0.8f)
        {
            if (string.IsNullOrEmpty(key) || template == null)
            {
                Debug.LogWarning("[DialogueBoxManager] 注册样式失败：key 或 template 为空");
                return;
            }
            _styles[key] = new DialogueBoxStyle(template, intro, outro, retain);
        }

        /// <summary>按样式资产注册（节点级 / StoryView 级样式覆盖的统一入口）。键 = <see cref="DialogueBoxStyleAsset.styleKey"/>（资产未设则用资产名）。
        /// template 为 null 时经 <see cref="StoryAssetLocator"/> 回退 StoryDialogueBoxes/{key}，仍无则忽略并告警。</summary>
        public void RegisterStyle(DialogueBoxStyleAsset asset)
        {
            if (asset == null) return;
            var key = string.IsNullOrEmpty(asset.styleKey) ? asset.name : asset.styleKey;
            if (string.IsNullOrEmpty(key)) return;
            var tmpl = asset.template ?? StoryAssetLocator.Current.LoadAsset<GameObject>("StoryDialogueBoxes/" + key);
            if (tmpl == null)
            {
                Debug.LogWarning($"[DialogueBoxManager] 样式资产 '{key}' 缺少 template，且未找到 Resources/StoryDialogueBoxes/{key}.prefab，已忽略");
                return;
            }
            _styles[key] = new DialogueBoxStyle(tmpl, asset.introDuration, asset.outroDuration, asset.retainRatio);
        }

        public bool HasStyle(string key) => _styles.ContainsKey(key);

        /// <summary>
        /// 为某样式注册默认生成策略（优先级低于 spec.spawnStrategy 的逐次覆盖）。
        /// 业务可在初始化时把自定义策略注册到 story-line / story-choice 等样式，实现整类对话框的统一出现逻辑。
        /// </summary>
        public void RegisterSpawnStrategy(string styleKey, IDialogueBoxSpawnStrategy strategy)
        {
            if (string.IsNullOrEmpty(styleKey) || strategy == null) return;
            _spawnStrategies[styleKey] = strategy;
        }

        /// <summary>
        /// 按策略键解析生成策略（用于节点级策略分配）。键对应 <see cref="DialogueBoxSpawnStrategyAsset.strategyKey"/>（资产未设则用资产名）。
        /// 首次调用时经 <see cref="StoryAssetLocator"/> 加载 <c>StorySpawnStrategies</c> 目录并注册全部策略资产，
        /// 保证编辑器 [SpawnStrategyPicker] 下拉选项与运行时解析结果一致。
        /// 找不到键时返回 null（调用方回退到全局策略 / 静态定位）。
        /// </summary>
        public IDialogueBoxSpawnStrategy GetSpawnStrategy(string key)
        {
            EnsureStrategiesByKey();
            if (string.IsNullOrEmpty(key)) return null;
            return _spawnStrategiesByKey.TryGetValue(key, out var s) ? s : null;
        }

        private void EnsureStrategiesByKey()
        {
            if (_strategiesByKeyLoaded) return;
            _strategiesByKeyLoaded = true;
            // ① 全 Resources 兜底扫：业务可把策略资产放任意 Resources 子目录（编辑器下拉已全局列出），运行时可解析命中。
            //    自定义定位器（Addressables）对空路径返回空数组，无副作用。
            foreach (var a in StoryAssetLocator.Current.LoadAllAssets<DialogueBoxSpawnStrategyAsset>(""))
                RegisterStrategyByKey(a);
            // ② 约定键空间（StorySpawnStrategies，Addressables 按同名 Label 批量加载）后注册 → 优先级更高，
            //    避免业务目录与键空间同名 key 时被后者覆盖。
            foreach (var a in StoryAssetLocator.Current.LoadAllAssets<DialogueBoxSpawnStrategyAsset>("StorySpawnStrategies"))
                RegisterStrategyByKey(a);
        }

        private void RegisterStrategyByKey(DialogueBoxSpawnStrategyAsset a)
        {
            if (a == null) return;
            var key = string.IsNullOrEmpty(a.strategyKey) ? a.name : a.strategyKey;
            if (!string.IsNullOrEmpty(key)) _spawnStrategiesByKey[key] = a;
        }

        /// <summary>首次调用时经 <see cref="StoryAssetLocator"/> 加载 <c>StoryDialogueBoxStyles</c> 并注册全部样式资产（与节点/StoryView 显式注册互补）。</summary>
        private void EnsureStylesByKey()
        {
            if (_stylesByKeyLoaded) return;
            _stylesByKeyLoaded = true;
            foreach (var a in StoryAssetLocator.Current.LoadAllAssets<DialogueBoxStyleAsset>("StoryDialogueBoxStyles"))
            {
                if (a == null) continue;
                RegisterStyle(a);
            }
        }

        // ── 弹出 ──
        public DialogueBoxHandle Show(DialogueBoxSpec spec)
        {
            if (_canvasRT == null) EnsureCanvas();
            EnsureStrategiesByKey();
            EnsureStylesByKey();
            if (!_eventSystemEnsured) { EnsureEventSystem(); _eventSystemEnsured = true; }
            if (spec == null) { Debug.LogWarning("[DialogueBoxManager] spec 为 null，已忽略"); return null; }
            if (string.IsNullOrEmpty(spec.styleKey) || !_styles.TryGetValue(spec.styleKey, out var style))
            {
                Debug.LogWarning($"[DialogueBoxManager] 未注册样式：{spec.styleKey}，已忽略");
                return null;
            }

            var handle = new DialogueBoxHandle { _manager = this, _instanceId = ++_spawnCounter };

            // ── 生成策略（核心扩展点）──
            // 优先级：spec.spawnStrategy（逐次覆盖）> 样式注册的默认策略 > 静态 position。
            var strategy = spec.spawnStrategy
                ?? (_spawnStrategies.TryGetValue(spec.styleKey, out var reg) ? reg : null);
            float delay = 0f;
            if (strategy != null)
            {
                var ctx = new DialogueBoxSpawnContext
                {
                    styleKey = spec.styleKey,
                    spec = spec,
                    payload = spec.payload,
                    activeBoxes = _active,
                    totalActive = _active.Count,
                };
                DialogueBoxSpawnResolution resolution = null;
                try { resolution = strategy.Resolve(ctx); }
                catch (Exception e) { Debug.LogException(e); resolution = null; }

                if (resolution != null)
                {
                    if (resolution.cancel)
                    {
                        Debug.Log($"[DialogueBoxManager] 生成策略取消弹出：style={spec.styleKey}");
                        return null;
                    }
                    if (resolution.position != null) spec.position = resolution.position;
                    if (resolution.layerOverride.HasValue) spec.layer = resolution.layerOverride.Value;
                    if (resolution.persistent.HasValue) spec.persistent = resolution.persistent.Value;
                    delay = resolution.delay;
                }
            }

            // 留存过渡（样式资产特有能力）：等上一框离场进行到该样式 retainRatio 占比后再显示本框——
            // 避免「下一框立即出现覆盖正在淡出的旧框」造成的离场被跳过观感（与生成策略 delay 取较大者）
            if (style.retainRatio > 0f && _retainUntil > Time.time)
                delay = Mathf.Max(delay, (_retainUntil - Time.time) * style.retainRatio);

            var box = Acquire(spec.styleKey, style);
            box.handle = handle;
            box.manager = this;
            box.spec = spec;
            box.styleKey = spec.styleKey;
            handle._box = box;
            handle.Tag = spec.tag;
            handle.Layer = spec.layer;

            var view = box.GetComponent<IDialogueBoxView>();
            view?.Setup(handle, spec.payload);

            if (!_active.Contains(box)) _active.Add(box);
            ApplyOrder();
            box.Configure(style.introDuration, style.outroDuration);
            if (delay > 0f)
            {
                box.gameObject.SetActive(false);
                StartCoroutine(DeferredOpen(box, delay));
            }
            else
            {
                box.BeginOpen();
            }
            return handle;
        }

        private DialogueBox Acquire(string key, DialogueBoxStyle style)
        {
            if (_pool.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                var b = stack.Pop();
                b.gameObject.SetActive(true);
                return b;
            }
            var go = Instantiate(style.template, _canvasRT);
            go.SetActive(true);
            var box = go.GetComponent<DialogueBox>() ?? go.AddComponent<DialogueBox>();
            return box;
        }

        // 延迟打开：等待期间隐藏该框（不占用交互/视觉），到点再 BeginOpen（含定位与入场上文）。
        private IEnumerator DeferredOpen(DialogueBox box, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (box == null || box.State != DialogueBoxState.Pooled) yield break;
            box.gameObject.SetActive(true);
            box.BeginOpen();
        }

        // ── 关闭 ──
        public void RequestClose(DialogueBoxHandle handle, bool immediate)
        {
            if (handle == null || handle._box == null) return;
            // 非立即关闭：记录该框离场应结束的时刻，供后续 Open 计算留存过渡（下一框等旧框淡出到指定占比再出现）
            if (!immediate) _retainUntil = Time.time + handle._box.OutroDuration;
            handle._box.BeginClose(immediate);
        }

        internal void NotifyOpened(DialogueBoxHandle handle)
        {
            RefreshInteraction();
            Try(() => handle._box?.spec?.onOpened?.Invoke(handle));
        }

        internal void NotifyClosed(DialogueBoxHandle handle)
        {
            var box = handle._box;
            Try(() => box.spec?.onClosed?.Invoke(handle));

            if (box != null)
            {
                var recyclable = box.GetComponent<IDialogueBoxRecyclable>();
                if (recyclable != null) Try(recyclable.OnRecycle);
                _active.Remove(box);
                if (_pool.TryGetValue(box.styleKey, out var stack) && stack.Count < poolCapacityPerStyle)
                {
                    box.gameObject.SetActive(false);
                    box.State = DialogueBoxState.Pooled;
                    stack.Push(box);
                }
                else
                {
                    box.State = DialogueBoxState.Destroyed;
                    Destroy(box.gameObject);
                }
            }
            handle._box = null;
            ApplyOrder();
            RefreshInteraction();
        }

        /// <summary>关闭最顶层（层级最高、同层最后弹出）的对话框。</summary>
        public void CloseTop()
        {
            if (_active.Count == 0) return;
            RequestClose(_active[_active.Count - 1].handle, immediate: false);
        }

        /// <summary>关闭全部。immediate=true 跳过退场动画（场景卸载时用）。</summary>
        public void CloseAll(bool immediate = true)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                RequestClose(_active[i].handle, immediate);
        }

        /// <summary>按分组标签批量关闭。</summary>
        public void CloseByTag(string tag, bool immediate = false)
        {
            if (string.IsNullOrEmpty(tag)) return;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].spec != null && _active[i].spec.tag == tag)
                    RequestClose(_active[i].handle, immediate);
            }
        }

        /// <summary>当前活动对话框数量。</summary>
        public int ActiveCount => _active.Count;

        // ── 层级 / 交互 ──
        private void ApplyOrder()
        {
            _active.Sort((a, b) =>
            {
                int l = a.spec.layer.CompareTo(b.spec.layer);
                return l != 0 ? l : a.handle._instanceId.CompareTo(b.handle._instanceId);
            });
            for (int i = 0; i < _active.Count; i++)
                _active[i].transform.SetSiblingIndex(i);
        }

        private void RefreshInteraction()
        {
            int topModal = int.MinValue;
            foreach (var b in _active)
                if (b.spec != null && b.spec.modal) topModal = Math.Max(topModal, b.spec.layer);
            bool anyModal = topModal != int.MinValue;
            foreach (var b in _active)
            {
                bool interactive = !anyModal || b.spec.layer >= topModal;
                b.SetInteractive(interactive);
            }
        }

        private void OnSceneUnloaded(Scene scene) => CloseAll(true);

        private static void Try(Action a)
        {
            try { a?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
