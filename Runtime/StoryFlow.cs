using System;
using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情系统对外门面（facade）与控制组件。一个 MonoBehaviour 同时承担两职责：
    /// <list type="bullet">
    ///   <item>控制组件：可独立挂载、驱动剧情运行（需同物体上有实现了 <see cref="IStoryPresenter"/> 的视图，如 StoryView）。</item>
    ///   <item>使用门面：对外只暴露 Play / Stop / Advance / Choose + 配置（<see cref="StoryFlowConfig"/>）+ 事件订阅，
    ///   内部藏起 <see cref="StoryPlayer"/> / 具体视图（IStoryPresenter）与一切底层数据模型。</item>
    /// </list>
    ///
        /// 这是「不依赖宿主框架也能在编辑器 Play 模式跑通一张剧情图」的最小闭环示例与默认装配：
        /// 进 Play 模式时（<see cref="Start"/>）默认自动装配 <see cref="StoryPlayer"/> + 变量提供者 + 默认事件处理器
        /// + 可选文本/本地化提供者 + 视图（如 StoryView）并开始播放；取消 <see cref="autoStart"/> 则由宿主在合适时机调 <see cref="Play"/>。
        /// 控制方法 <see cref="Play"/>/<see cref="Stop"/>/<see cref="Advance"/>/<see cref="Choose"/>/<see cref="Restart"/> 与状态查询 <see cref="IsRunning"/>/<see cref="IsWaiting"/> 构成完整对外控制面。
    ///
    /// 接入正式项目时，由宿主（Assets/Scripts/Game/Story/StoryBridge.cs 之类）以同样方式装配，
    /// 只是把示例 Provider 换成真实存档/事件/文本/角色/图加载实现（统一经 <see cref="Configure(StoryFlowConfig)"/> 注入）。
    /// </summary>
    [AddComponentMenu("MicrobialNet/Story/Story Flow", 0)]
    public class StoryFlow : MonoBehaviour
    {
        // ===== 公共事件参数（不引用任何 internal 类型，可被 public 事件安全暴露）=====

        /// <summary>一句对白（交给视图渲染 / 宿主订阅）。</summary>
        public sealed class Line
        {
            /// <summary>讲述者 ID（数据标识，一般不显示）。</summary>
            public string SpeakerId;
            /// <summary>讲述者显示名（已含本地化解析）。</summary>
            public string SpeakerName;
            /// <summary>讲述者视图模型（显示名 + 主题色 + 立绘）。视图据此外观着色 / 显示头像。</summary>
            public StoryConstants.CharacterViewModel Speaker;
            /// <summary>对白正文（已含本地化解析）。</summary>
            public string Text;
            /// <summary>打字速度（字/秒，>0 有效）。</summary>
            public float Speed;
            /// <summary>打字机节奏模式（见 TypingMode）。</summary>
            public TypingMode TypingMode;
            /// <summary>形式三手K逐字符延迟（秒）；仅 TypingMode.Custom 且长度匹配可见字符数时使用。按可见字符索引。</summary>
            public float[] TypingDelays;
            /// <summary>节点级立绘 Key（轻量 [Future]：仅暴露数据，视图默认沿用角色默认立绘）。</summary>
            public string PortraitKey;
            /// <summary>节点级语音 Key（轻量 [Future]：经事件 "voice:{key}" 派发，由宿主播放）。</summary>
            public string VoiceKey;

            /// <summary>节点级外观覆盖提示（样式/位置/策略/保留），由视图按「节点 > 全局」应用。</summary>
            public DialogueAppearanceHint appearance;
        }

        /// <summary>一个玩家选项（交给视图渲染为按钮）。</summary>
        public sealed class Choice
        {
            /// <summary>选项 ID（对应输出端口 "opt_{OptionId}"），<see cref="Choose(string)"/> 用此值。</summary>
            public string OptionId;
            /// <summary>选项显示文本（已含本地化解析）。</summary>
            public string Text;
            /// <summary>节点级外观覆盖提示（样式/位置/策略），由视图应用。</summary>
            public DialogueAppearanceHint appearance;
            /// <summary>选项框顶部说明文字（「带文字」选择节点的行内对白，可空=不显示），由视图渲染在选项上方。</summary>
            public string Prompt;
            /// <summary>Prompt 的打字机参数（与对白节点同语义）：视图据此生成打字 schedule，选项在文字打完后才出现。</summary>
            public float PromptSpeed = 0.5f;
            public TypingMode PromptTypingMode = TypingMode.GlobalSpeed;
        }

        /// <summary>一个剧情事件（交给业务代码处理）。</summary>
        public sealed class StoryEventInfo
        {
            /// <summary>事件名（如 "confirm:battle_start"）。</summary>
            public string Name;
            /// <summary>事件负载（JSON 字符串）。</summary>
            public string PayloadJson;
        }

        // ===== 对外事件（视图 / 宿主订阅）=====
        /// <summary>一句对白已呈现（视图渲染 / 宿主可同时订阅做自定义逻辑）。</summary>
        public event Action<Line> OnLine;
        /// <summary>需要玩家做选择（传入可见选项列表）。</summary>
        public event Action<IReadOnlyList<Choice>> OnChoices;
        /// <summary>剧情事件节点派发（业务侧据此触发逻辑；挂起节点需调 onComplete 才续走）。</summary>
        public event Action<StoryEventInfo> OnEvent;
        /// <summary>进入某节点（参数为节点 ID；编辑器同步高亮 / 调试用）。</summary>
        public event Action<string> OnNodeEnter;
        /// <summary>剧情结束（到达 End 节点或遇不可恢复错误后）。</summary>
        public event Action OnEnd;
        /// <summary>章节 / 图切换（JumpChapter 触发），参数为目标图 storyId。</summary>
        public event Action<string> OnChapterChanged;
        /// <summary>剧情中断 / 结构错误（死路 / 缺节点 / 异常结构）。</summary>
        public event Action<string> OnError;

        // ===== Inspector 配置 =====
        [Header("播放控制")]
        [SerializeField] private bool autoStart = true; // 进 Play 模式是否自动开始；false 时由宿主调 Play() 驱动

        [Header("剧情图")]
        [SerializeField] private StoryGraphAsset storyGraphAsset;

        [Header("全局变量（可选）")]
        [SerializeField] private StoryGlobalVariableAsset globalVariables;

        [Header("视图（可选；实现了 IStoryPresenter 的组件，如 StoryView。可经 Configure 注入或同物体 GetComponent 兜底）")]
        private IStoryPresenter _presenter;

        [Header("存档（示例：PlayerPrefs + JsonUtility）")]
        [SerializeField] private bool autoSaveOnExit = true;
        [SerializeField] private bool autoLoadOnStart = false;

        [Header("本地化（可选，运行时多语言）")]
        [Tooltip("可选兜底本地化主表。优先使用每张剧情图自带的 localizationTable（已绑定到 StoryGraphAsset）；此字段仅在某张图没有自带表时作为兜底。显示语言由下方 activeLanguage 统一控制，无需在此拖入与图同名的表。")]
        [SerializeField] private StoryLocalizationTable localizationTable;
        [Tooltip("全局显示语言（如 zh-CN / en-US）。为空则用主表/首图的 defaultLanguage。切换章节时保持不变，全局统一由此值控制；运行时亦可经 StoryFlow.ActiveLanguage 设置。")]
        [SerializeField] private string activeLanguage = string.Empty; // 为空则用主表的 defaultLanguage

        // ===== 运行时内部状态 =====
        private StoryPlayer _player;
        private RuntimeStoryGraph _graph;
        private IStoryVariableProvider _variables;

        /// <summary>进度存档落地（可插拔）。默认 PlayerPrefs；正式宿主经 Configure 传入自家实现。</summary>
        private IStorySaveStore _save = new PlayerPrefsSaveStore("MicrobialNet.Story.Progress");

        /// <summary>宿主装配配置（可选）。传入后 Start 优先使用其中的实现。</summary>
        private StoryFlowConfig _config;

        /// <summary>最近一次 OnChoices 转发的可见选项（供 <see cref="Choose(int)"/> 按序号映射）。</summary>
        private readonly List<Choice> _lastChoices = new List<Choice>();

        private bool _initialized;

        /// <summary>图加载器（跳转章节用）。EnsureInitialized 中确定后缓存；JumpChapter 时由 StoryPlayer 直接把解析到的目标图资产经 OnChapterChanged 传出，本类据此切换本地化表（不依赖 storyId 二次解析）。</summary>
        private Func<string, StoryGraphAsset> _graphResolver;

        /// <summary>图绑定本地化提供者（跳转章节时切换当前图）。若宿主经 Config.Text 注入了自定义文本提供者则为 null。</summary>
        private StoryGraphLocalizationProvider _graphLocalization;

        /// <summary>实例级变量名解析（本图 + 全局变量映射）。多 StoryFlow 并存时互不覆盖，也不污染编辑器绑定的全局静态。</summary>
        private Func<string, string> _variableNameResolver;

        // ===== 装配入口（即插即用接缝）=====

        /// <summary>由编辑器工具 / 桥接层在装配时显式注入「视图」（实现了 <see cref="IStoryPresenter"/> 的组件，如 StoryView）。运行时 Start 也会按 GetComponent&lt;IStoryPresenter&gt; 兜底。</summary>
        public void Configure(IStoryPresenter presenter) => _presenter = presenter;

        /// <summary>
        /// 统一装配入口（即插即用接缝）。宿主传入填好的 <see cref="StoryFlowConfig"/>，
        /// 即可把真实变量 / 事件 / 文本 / 角色 / 存档 / 图加载系统接入，剧情逻辑零改动。
        /// </summary>
        public void Configure(StoryFlowConfig cfg) => _config = cfg;

        // ===== 控制方法（对外门面）=====

        /// <summary>是否正在运行（已开始且未结束 / 未出错）。供宿主 UI 判断按钮态等。</summary>
        public bool IsRunning => _player != null && _player.IsRunning;

        /// <summary>是否正等待用户推进（对白等 Advance / 选项等 Choose）。供宿主 UI 判断当前交互态。</summary>
        public bool IsWaiting => _player != null && _player.IsWaiting;

        /// <summary>
        /// 全局显示语言（如 "zh-CN" / "en-US"）。决定所有剧情图统一显示的语言，切换章节时保持不变。
        /// <list type="bullet">
        ///   <item>为空：回落到当前剧情图本地化表的 <see cref="StoryLocalizationTable.defaultLanguage"/>（切换章节后自然跟随新图）。</item>
        ///   <item>设置后：立即对所有后续文本生效。底层 provider 在每次 <see cref="IStoryTextProvider.ResolveText"/> 时实时读取此值，
        ///   因此运行时改语言即时生效、切换章节也始终沿用此值，绝不回落默认语言。</item>
        /// </list>
        /// 运行时可由宿主随时设置（如玩家在设置页切语言），无需重建播放器。
        /// </summary>
        public string ActiveLanguage
        {
            get => activeLanguage;
            set => activeLanguage = value ?? string.Empty; // 语言由 provider 实时读取，无需手动同步
        }

        /// <summary>
        /// 开始 / 继续播放。
        /// <list type="bullet">
        ///   <item>尚未装配：先装配（构建播放器 + 绑定视图），再从入口节点开始播放。</item>
        ///   <item>已装配且未运行（如 Stop 之后）：从入口节点重新开始播放（变量状态保持）。</item>
        ///   <item>已装配且正在运行：幂等，忽略。</item>
        /// </list>
        /// </summary>
        public void Play()
        {
            if (!_initialized) { InitializeAndPlay(); return; }
            if (_player != null && !_player.IsRunning) _player.Start();
        }

        /// <summary>结束播放（对白 / 任意等待态均可调用）。</summary>
        public void Stop() => _player?.Stop();

        /// <summary>推进一句对白（在收到 <see cref="OnLine"/> 后由视图 / 宿主调用）。非对白等待态调用会被忽略。</summary>
        public void Advance() => _player?.Advance();

        /// <summary>选择一个选项（在收到 <see cref="OnChoices"/> 后由视图 / 宿主调用）。</summary>
        /// <param name="optionId">选项的 OptionId（来自 <see cref="Choice.OptionId"/>）。</param>
        public void Choose(string optionId) => _player?.Choose(optionId);

        /// <summary>
        /// 按选项序号选择（<paramref name="index"/> 为最近一次 <see cref="OnChoices"/> 可见列表的下标）。
        /// 越界或不在选项等待态时忽略并报错。
        /// </summary>
        public void Choose(int index)
        {
            if (_player == null || !_player.IsRunning || !_player.IsWaiting) return;
            if (index < 0 || index >= _lastChoices.Count)
            {
                Debug.LogWarning($"[StoryFlow] Choose(index={index}) 越界（可见选项数={_lastChoices.Count}），已忽略。");
                return;
            }
            _player.Choose(_lastChoices[index].OptionId);
        }

        /// <summary>
        /// 从入口节点重新播放（变量归零、清掉旧进度）。
        /// <list type="bullet">
        ///   <item>简单用法（未注入 <see cref="StoryFlowConfig.Variables"/>）：重建 <see cref="InMemoryVariableProvider"/>，变量回到初始值。</item>
        ///   <item>宿主注入了 <see cref="StoryFlowConfig.Variables"/>：变量提供者由宿主持有，是否归零取决于宿主；本方法只重置剧情遍历并清进度存档。</item>
        /// </list>
        /// </summary>
        public void Restart()
        {
            _player?.Stop();
            if (_presenter != null)
            {
                _presenter.OnAdvanceRequested -= Advance;
                _presenter.OnChoiceSelected -= Choose;
            }
            _initialized = false;
            _lastChoices.Clear();
            _save.Clear();
            InitializeAndPlay(forceFresh: true);
        }

        /// <summary>
        /// 当前变量值的调试字符串（示例场景的变量监视面板用）。
        /// 变量数据运行期活在本组件的 <see cref="_variables"/>（纯 C# 对象，Inspector 不可见），
        /// 此只读视图用于让玩家 / 开发者在 Game 视图里直观看到变量变化。
        /// </summary>
        public string FormatVariables()
        {
            if (_variables == null) return "(无变量)";
            var dict = _variables.Snapshot();
            if (dict == null || dict.Count == 0) return "(无变量)";
            var sb = new System.Text.StringBuilder();
            foreach (var kv in dict)
                sb.AppendLine($"{ResolveVariableName(kv.Key)} = {kv.Value}");
            return sb.ToString().TrimEnd();
        }

        // ===== 生命周期 =====

        private void Start()
        {
            if (autoStart) InitializeAndPlay();
            else EnsureInitialized();
        }

        /// <summary>确保播放器已装配（构建 StoryPlayer + 绑定视图 + 注册解析器）。幂等；不自动开始播放（由 Play / autoStart 决定）。</summary>
        private void EnsureInitialized()
        {
            if (_initialized) return;

            if (storyGraphAsset != null)
            {
                _graph = RuntimeStoryGraph.FromAsset(storyGraphAsset);
            }
            else
            {
                // 未指定资产时回退到内置示例图，示例场景零手工资产即可 Play。
                _graph = StoryDemoGraph.Build();
                Debug.Log("[StoryFlow] 未指定 StoryGraphAsset，使用内置示例剧情图。");
            }
            if (_graph == null) return;

            if (_config != null)
            {
                // 变量提供者：显式配置优先，否则回退本图 + 全局变量黑板（与无 config 时一致）。
                _variables = _config.Variables
                    ?? new InMemoryVariableProvider(
                        _graph.variables,
                        globalVariables != null ? globalVariables.variables : null);
                // 角色解析器改为实例级注入（见下方 StoryPlayer 构造处）：不再写回全局静态，
                // 多 StoryFlow 并存（分屏/多存档同屏）时各自互不覆盖，Play 结束后也不污染编辑器绑定器。
                if (_config.Save != null)
                    _save = _config.Save;
            }
            else
            {
                _variables = new InMemoryVariableProvider(
                    _graph.variables,
                    globalVariables != null ? globalVariables.variables : null);
            }

            // 注册变量名解析器：变量名来自本图 / 全局变量黑板，使运行时 UI 显示「HP」而非「hp」。
            // 讲述者名 / 立绘 / 主题色由角色解析器解析（编辑器注入 或 宿主经 Config.Characters 注入）。
            RegisterResolvers(_graph);

            // 默认事件处理器：仅打印；默认文本提供者：identity（本地化未实现）。
            // 二者均可被 StoryFlowConfig 覆盖（正式接入时换成转发宿主系统的实现）。
            var events = _config?.Events ?? new LambdaEventHandler((name, payload) =>
                Debug.Log($"[Story] 事件：{name} {payload}"));

            // 文本提供者优先级：
            //   1) StoryFlowConfig.Text（正式宿主注入的自定义 IStoryTextProvider，完全自定义）；
            //   2) 当前图的 localizationTable（每张图自带表，跳转章节自动跟随切换）；
            //   3) Inspector 拖入的兜底本地化主表（单表，不随跳转切换）；
            //   4) 都为空 → 播放器用原文（identity）。
            IStoryTextProvider text = _config?.Text;
            if (text == null)
            {
                // 语言由 ActiveLanguage 实时决定：两个 provider 内部每次 ResolveText 都经 () => activeLanguage 读取，
                // 因此无需在初始化时预先算语言，也无需在切换章节时手动同步——语言永远等于当前 ActiveLanguage，
                // 切换章节只切图、绝不重置语言，根除「跳转后回落默认语言」的问题。
                if (storyGraphAsset != null && storyGraphAsset.localizationTable != null)
                {
                    _graphLocalization = new StoryGraphLocalizationProvider(storyGraphAsset, () => activeLanguage, localizationTable);
                    text = _graphLocalization;
                }
                else if (localizationTable != null)
                {
                    text = new LocalizationTextProvider(localizationTable, () => activeLanguage);
                }
            }

            // 图加载器：优先用显式配置的 GraphResolver，否则用引导组件（StoryGraphRegistry）注册的静态委托。
            _graphResolver = _config?.GraphResolver ?? StoryConstants.GraphResolver;
            // 角色解析器：实例级优先（Config.Characters）；未配置时播放器回落全局静态
            // （编辑器绑定器 / 宿主 BindCharacterResolver 注入的兼容默认），行为与原先一致。
            Func<string, StoryConstants.CharacterViewModel> characterResolver =
                _config != null && _config.Characters != null ? _config.Characters.Resolve : null;
            _player = new StoryPlayer(_graph, _variables, events, text, _graphResolver, characterResolver);

            // 订阅内部事件 → 转发为公开事件（供宿主订阅，与 StoryView 渲染并存）。
            _player.OnLine += ForwardLine;
            _player.OnChoices += ForwardChoices;
            _player.OnEvent += ForwardEvent;
            _player.OnNodeEnter += n => OnNodeEnter?.Invoke(n);
            _player.OnEnd += (showText, text) => { OnEnd?.Invoke(); _presenter?.ShowEnd(showText, text); };
            _player.OnChapterChanged += asset =>
            {
                // 对外事件仍传图标识：优先 storyId，为空时退化为资产名，保证宿主回调能拿到目标图标识。
                string id = asset != null
                    ? (asset.meta != null && !string.IsNullOrEmpty(asset.meta.storyId) ? asset.meta.storyId : asset.name)
                    : null;
                OnChapterChanged?.Invoke(id);
                // 本地化跟随切换：直接把图绑定提供者切到「事件带来的目标图资产」，
                // 不再经 storyId 二次解析（旧实现因目标图 storyId 为空导致解析失败、切换不发生，
                // 新图文本误查旧表 / 回落源语言（默认语言））。语言由 ActiveLanguage 实时决定，与切图解耦。
                if (_graphLocalization != null && asset != null)
                    _graphLocalization.SetCurrentGraph(asset);
            };
            // 错误信息不对玩家展示：仅对外派发 OnError（宿主可订阅做诊断），并在控制台打印，不弹任何错误框。
            _player.OnError += e => { OnError?.Invoke(e); Debug.LogError($"[Story] 剧情异常：{e}"); };

            if (_presenter == null) _presenter = GetComponent<IStoryPresenter>();
            if (_presenter != null)
            {
                _presenter.OnAdvanceRequested += Advance;
                _presenter.OnChoiceSelected += Choose;
            }

            _initialized = true;
        }

        /// <summary>装配并开始播放（= EnsureInitialized + BeginPlay）。forceFresh=true 时跳过存档恢复、从入口重播并清旧进度（用于 Restart）。</summary>
        private void InitializeAndPlay(bool forceFresh = false)
        {
            EnsureInitialized();
            BeginPlay(!forceFresh);
        }

        /// <summary>开始播放：若允许恢复且存在「有效」存档（快照节点仍存在于当前剧情）则从该节点续玩；否则从入口重播并清掉失效存档。</summary>
        private void BeginPlay(bool allowRestore)
        {
            if (allowRestore && autoLoadOnStart && TryLoadSnapshot(out var snap)
                && snap != null && !string.IsNullOrEmpty(snap.currentNodeId)
                && _graph.GetNode(snap.currentNodeId) != null)
            {
                _player.Restore(snap);
                Debug.Log("[StoryFlow] 已从存档恢复进度。");
            }
            else
            {
                _player.Start();
                if (!allowRestore || (autoLoadOnStart && _save.HasSave()))
                {
                    _save.Clear();
                }
            }
        }

        private void OnDestroy()
        {
            if (_presenter != null)
            {
                _presenter.OnAdvanceRequested -= Advance;
                _presenter.OnChoiceSelected -= Choose;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && autoSaveOnExit) SaveProgress();
        }

        private void OnApplicationQuit()
        {
            if (autoSaveOnExit) SaveProgress();
        }

        // ===== 内部事件转发（internal 类型 → public 类型）=====

        private void ForwardLine(StoryPlayer.Line line)
        {
            var l = new Line
            {
                SpeakerId = line.SpeakerId,
                SpeakerName = line.SpeakerName,
                Speaker = line.Speaker,
                Text = line.Text,
                Speed = line.Speed,
                TypingMode = line.TypingMode,
                TypingDelays = line.TypingDelays,
                PortraitKey = line.PortraitKey,
                VoiceKey = line.VoiceKey,
                appearance = line.appearance,
            };
            OnLine?.Invoke(l);
            _presenter?.ShowLine(l);
        }

        private void ForwardChoices(IReadOnlyList<StoryPlayer.Choice> choices)
        {
            _lastChoices.Clear();
            var visible = new List<Choice>(choices.Count);
            foreach (var c in choices)
            {
                var fc = new Choice
                {
                    OptionId = c.OptionId,
                    Text = c.Text,
                    appearance = c.appearance,
                    Prompt = c.Prompt,
                    PromptSpeed = c.PromptSpeed,
                    PromptTypingMode = c.PromptTypingMode,
                };
                visible.Add(fc);
                _lastChoices.Add(fc);
            }
            OnChoices?.Invoke(visible);
            _presenter?.ShowChoices(visible);
        }

        private void ForwardEvent(StoryPlayer.StoryEvent e)
        {
            OnEvent?.Invoke(new StoryEventInfo { Name = e.Name, PayloadJson = e.PayloadJson });
        }

        // ===== 存档 =====

        /// <summary>把当前进度存到 PlayerPrefs（示例用；正式接入由宿主桥接层替换落地方式）。</summary>
        public void SaveProgress()
        {
            if (_player == null) return;
            if (!_player.IsRunning)
            {
                // 剧情已结束（到达 End 节点）或尚未开始：无可续玩进度。
                // 清掉旧存档，避免下次加载时 Restore 因 currentNodeId 为空而报「快照节点不存在」。
                if (_save.HasSave()) _save.Clear();
                return;
            }
            var snap = _player.CaptureState();
            _save.Save(JsonUtility.ToJson(snap));
            Debug.Log("[StoryFlow] 进度已保存。");
        }

        /// <summary>从 PlayerPrefs 读取并恢复进度（若存在）。</summary>
        public void LoadProgress()
        {
            if (_player == null) return;
            if (TryLoadSnapshot(out var snap)) _player.Restore(snap);
        }

        private bool TryLoadSnapshot(out StorySnapshot snap)
        {
            snap = null;
            if (!_save.HasSave()) return false;
            var json = _save.Load();
            if (string.IsNullOrEmpty(json)) return false;
            snap = JsonUtility.FromJson<StorySnapshot>(json);
            return snap != null;
        }

        // ===== 轻量默认实现（示例用；正式桥接层替换为真实实现）=====

        /// <summary>
        /// 注册实例级变量名解析器：变量名解析自本图 / 全局变量黑板（显示「HP」而非「hp」）。
        /// 讲述者名 / 立绘 / 主题色由角色解析器解析（实例级注入进 StoryPlayer），运行时无需、也不能引用角色资产列表。
        /// </summary>
        private void RegisterResolvers(RuntimeStoryGraph graph)
        {
            var varMap = new Dictionary<string, string>();
            if (graph.variables != null)
                foreach (var v in graph.variables)
                    if (!string.IsNullOrEmpty(v.id)) varMap[v.id] = v.name;
            if (globalVariables != null)
                foreach (var v in globalVariables.variables)
                    if (!string.IsNullOrEmpty(v.id) && !varMap.ContainsKey(v.id)) varMap[v.id] = v.name;
            if (varMap.Count > 0)
                // 实例级：不写全局静态（多实例互不覆盖，也避免 Play 结束后残留覆盖编辑器绑定）。
                _variableNameResolver = id => varMap.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n) ? n : null;
        }

        /// <summary>变量 id → 可读名：实例映射优先，回落全局静态（编辑器绑定 / 宿主注入），最终 [未配置] 占位（与旧行为一致）。</summary>
        private string ResolveVariableName(string id)
        {
            if (_variableNameResolver != null)
            {
                var n = _variableNameResolver(id);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return StoryConstants.VariableName(id);
        }

        private sealed class LambdaEventHandler : IStoryEventHandler
        {
            private readonly System.Action<string, string> _handler;
            public LambdaEventHandler(System.Action<string, string> h) => _handler = h;
            // 挂起型：默认仅打印，不挂起流程（直接调 onComplete 让剧情不卡死）
            public void Raise(string eventName, string payloadJson, System.Action onComplete)
            {
                _handler?.Invoke(eventName, payloadJson);
                onComplete?.Invoke();
            }
            // 瞬时型：打印即忘
            public void Raise(string eventName, string payloadJson)
                => _handler?.Invoke(eventName, payloadJson);
        }
    }
}
