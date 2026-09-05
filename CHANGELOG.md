# Changelog

## \[v1.3] - 2026-08-28

- **架构优化（多实例 / 栈深 / 热路径 / 迁移链）**：① 实例级解析器——`StoryFlowConfig.Characters` 与本图变量名不再写回全局静态，改经 `StoryPlayer` 构造参数实例级注入，多 `StoryFlow` 并存（分屏/多存档同屏）不再互覆；② 播放器迭代式遍历——`Enter↔Traverse` 相互递归改显式循环，超长线性图不累积栈帧，`JumpToChapter` 换图由独立 `_chapterGuard` 熔断兜底；③ 编辑器命令热路径——`FieldChanged`（最高频命令）跳过全量 `RebuildIndex`，`SyncUsedCharacters` 反射按节点类型缓存 `FieldInfo`；④ JSON 迁移链——`StoryJsonMigrator`（`ISerializationMigrator` 版本步骤链 + `$type` 别名表，节点类改名/换命名空间后旧导出物、快照、基线仍可导入），`StorySnapshot` 增 `version` 字段。
- **Addressables 热更通道（`IStoryAssetLocator` 接缝收敛，P7 收束）**：
  - 运行时全部 6 个资产加载点（对话框模板 ×2 / 策略 / 样式 / 图批量扫描 / 角色兜底）统一经 `StoryAssetLocator.Current`，替换通道不再出现「UI 走新通道、图与角色仍走 Resources」的混合态；预置异步成员并定形**热更五契约**（path=逻辑键 / 同步仅服务已就绪资产 / 失败不抛异常 / 资产常驻无 Release / Current 引导期一次性设置）。破坏性变更：接口新增两个异步成员，自定义适配器需补实现。
  - `AddressablesStoryAssetLocator` 适配器（随包 `Samples~/AddressablesAdapter` 独立 asmdef，不构成包硬依赖，已注册进 package.json samples）：句柄按（类型+键）缓存常驻、失败句柄不缓存可重试、`allowSyncBlocking` 构造分档（本地随包 true / 含远程 false——未就绪即返空绝不阻塞网络）、WebGL 同步等待异常兜底；加载前 catalog 预检，缺失键按契约直接返回，消除 InvalidKeyException 日志噪音。
  - `ChainedAssetLocator` 渐进迁移组合定位器（Addressables 优先、空结果回落 Resources，每资产单一来源无双包，迁移完成后去 fallback 即整体切换）；`AddressablesDemoBoot` 静态引导（`RuntimeInitializeOnLoadMethod` + 编译宏启停，不碰场景文件；启动期显式预热 Addressables 初始化，同步快照 + 异步兜底双通道装配）。
  - **资产迁移小工具**（菜单 `MicrobialNet/Story/资产迁移`）：Resources 下 Story 资产七类目（图/角色/表格/本地化/模板/样式/策略）一键迁往 `Assets/AddressableStory` 并按契约标注（批量类目 Label=键空间 / 单资产类目 address=逻辑键）；`AssetDatabase.MoveAsset` 搬移（GUID 不变、场景与资产引用无缝跟随）；**合并式搬移幂等可重跑**（新项增量补迁、已迁不动）；多候选源目录兼容摆放偏差；撤销迁移原路搬回并归正到包约定位置；失败项弹窗显式列出；刻意不迁场景直连资产（全局变量/打字机配置——构建冻结进场景，迁移无热更价值）。
  - 编辑器策略下拉过滤升级为键空间判定 `IsInStrategyKeySpace`（目录名=键名）——迁移后与子目录摆放的策略均可见，与运行时「键是逻辑键不是物理路径」口径一致；`StoryAssetOrganizer` 等收口路径对已迁出标准树的资产跳过自动拉回（防迁移被编辑器域名重载逆转）。
- **事件节点边界收束（P5 自缺陷清单除名）**：事件节点定位为「具名闸门」，参数按语义三通道分流（叙事值→变量 / 引用凭证→事件名 / 业务杂项→payload 逃生口），平衡数值与业务配置不进图；校验器新增 `BadPayloadJson`（payload 非空必须为合法 JSON，Error 级并入构建门禁）与 `EventNameMismatch`（`[StoryEvent]` 特性名与 `EventName` 双写漂移图级黄条）；事件下拉口径改实例化读 `EventName`，与运行时注册同源。
- **属性面板子系统拆分（原 `FieldDrawerRegistry` 巨石 1217 行 → 五协作类）**：`FieldMetaCache`（类型级反射元数据缓存，面板构建从逐字段多次反射降为一次性反射 + 查表）/ `FieldPanelLogic`（可单测纯逻辑：多选混合态判定 / 表绑定值路由 / 变量类型归一化）/ `FieldWidgetFactory`（控件工厂与交互回调注册）/ `TableBoundEditor`（表驱动节点徽标与行维护）/ `FieldDrawerRegistry`（组装入口）；消除 `_timelineTarget` 静态 hack，公共 API 不变、行为零变化。
- **编辑态数据不进玩家包（P9）**：`StoryNodeData.position` 与 `StoryGraphAsset.groups/stickyNotes` 改 `#if UNITY_EDITOR` 条件字段——玩家构建中字段不参与编译、SO 序列化零编辑态字节；JSON 备份通道（导出/自动保存/基线回滚）保留完整编辑态；存量 `.asset` 无需迁移，`ApplyFlowLayout` 条件化。
- **编辑器修复**：
  - 校验器把全局变量误报「未定义」并阻塞打包——`varIds` 判定域并入全局变量表（与运行时「本图+全局」口径一致），赋值节点类型检查对全局变量同步生效，既有 4 个调用点零改动。
  - 数值控件脏跟踪（拖语速不触发未保存 / 选中即误报脏）——最终方案**数据锚定**：注册时反射读节点字段记基线，值变化回调（含下一帧双保险）重读节点数据，确实变化才 `TouchData()`；控件值的一切被动变化（初值同步/钳制/占位替换）与判定无关。`TouchData()` 置脏并广播 FieldChanged，未保存指示、关闭确认、自动保存全部感知。
  - 切换剧情图时对未保存修改弹三选确认（保存并切换 / 取消 / 放弃并回滚基线）；同资产内部重载与删除流程不误伤。
- **自动化测试补齐（P12）**：新建 EditMode 测试程序集（`InternalsVisibleTo` 开放 internal 数据模型），约 250 用例覆盖十二个功能域（播放内核 / 条件求值 / 表格导入导出 / 虚拟子图展开 / 本地化 / 打字机调度 / JSON 双轨 / 校验器 / 试跑模拟器 / StoryFlow 门面 / 事件总线 / 生成策略）并全绿；测试资产内存构造、无持久副作用，全局静态测试均在 finally 还原避免顺序耦合。

## \[v1.2] - 2026-08-28

- **表格驱动创作（真相源方案全链路）**：
  - `StoryTableAsset`(SO) 为剧情表唯一内容真相源（稳定行 id）；「新建表格驱动组（导入文件）」把 CSV/Excel 导入建 SO+组，改表重烘焙即同步；退休旧「一次性导入」菜单（生成节点后与表格失同步）。示例 `Samples~/StoryTableImport/` 同步 id 格式。
  - **两遍解析**：先建全部行（id 映射完整）再解析跳转目标——前跳/后跳/循环任意方向命中、选项不再丢失；跳转填**行 id**（兼容旧整数行号写法），`/`=终止标识。
  - 手动布局保留：`StoryTableGroupOverlay`（SO，按行 id 索引）记录表外扩展位置，重烘焙不再重置手拖布局，删行同步清理。
  - Inspector 写回：表驱动节点面板带「● 有修改尚未同步到表格」徽标与行维护按钮（在 Excel 中打开 / 同步到 Excel / 在此行后新增 / 删除此行）；编辑只写 SO 对应行（真相源），写回 Excel 改为手动触发；徽标即时切换不重建面板（逐字符输入不丢焦点）。
  - **Excel 就地写回保排版**：`StoryXlsx.UpdateSheetData` 把整包读入内存、仅就地替换目标工作表 `<sheetData>`（按 OOXML 子元素顺序插回原位置）——列宽/表头样式/行高/冻结窗格/其它工作表原样保留。
  - `sourceFilePath` 存**项目相对路径**（存时归一化、读时兼容绝对/相对）——换机/移动工程后「重新导入并同步」不失效。
- **运行时错误不弹框**：移除「错误框」（story-error）——死路/缺节点/异常结构改经 `StoryFlow.OnError` 事件 + LogError；`IStoryPresenter.ShowEnd` 签名改为 `ShowEnd(bool showText, string text)`，End 节点勾选「显示结束文本」才弹结束框。
- **编辑器关闭保护**：未保存关闭走 Unity 原生 `hasUnsavedChanges` 三选确认（保存/放弃/取消）——「取消」可真正保留窗口，「放弃」回滚到打开时基线（连中途自动保存的中间态一并回滚）。
- 修复「从已有 SO 恢复节点」误报：文件面板返回的绝对路径转 `Assets/` 项目相对路径后再加载，并加「文件不在 Assets 内」守卫。

## \[v1.1] - 2026-08-15

- **打字机演出三模式**：对话节点「打字机」——全局语速 / 标点节奏（`DialogueTypingProfile` SO 可配标点倍率）/ 手K逐字时序（`typingDelays` 数组）；`TypingScheduler.BuildSchedule` 把三模式统一归约为逐**可见**字符 `float[]`（富文本标签剔除）；揭示机制改 TMP `maxVisibleCharacters`（根治富文本标签被截断的崩溃隐患，点击跳过立即全显）；引擎零侵入，换节奏模式不碰播放逻辑。
- **打字机时间轴编辑器**（`DialogueTypingTimelineWindow`）：一维时间轴逐可见字符拖点改停顿、Play/Pause/Stop 播放预览、目标总时长等比缩放、时间标尺与淡网格、滚轮平移 / Ctrl+滚轮缩放（0.1×–50×）、底部固定播放预览栏；由对话节点面板「时间轴」按钮打开。
- **对话框模板 Prefab 化**：`StoryView` 注册样式时优先加载 `Resources/StoryDialogueBoxes/{key}.prefab`（美术定制模板）、找不到回退内置代码模板；「生成对话框模板 Prefab」菜单导出 4 个模板；视图组件经 `ResolveRefs()` 按子物体名（Speaker/Body/Hint/Portrait）自找引用——美术改 Prefab 即生效、无需改代码。
- **生成策略接缝**（`IDialogueBoxSpawnStrategy`）：把「出现在哪/何时/是否出现」提升为可注入的运行时决策——`Resolve(context)` 返回 `{position, delay, cancel, layerOverride, persistent}`；内置 Static/RandomRect/CascadeRandom 三个资产化策略（级联随机：围绕最近框中心随机半径、`layerStep` 层级递增天然最上层、persistent 点击继续不关自身）；策略资产按 `Resources/StorySpawnStrategies` 目录以键注册。无策略时完全等价静态行为，向后兼容。
- **对话框位置与出现逻辑 Inspector 可配**：`StoryView` 新增三类框位置字段（ScreenAnchor 九宫格 + 偏移 / WorldFollow / Free 交 Prefab）与「出现逻辑来源」`Config`/`Strategy` 二选一下拉（只显示/只应用选中那类）；`DialogueBoxPositionDrawer` 按定位模式仅绘制所需参数。
- **节点级对话框外观覆盖**：对话/选项节点「对话框外观」分组字段（样式资产 / 覆盖定位（模式/锚点/整数偏移）/ 策略键 / 保留行为）经 `DialogueAppearanceHint`（纯运行期不序列化）透传到视图，优先级节点 > 全局；`DialogueBoxPosition`/`DialogueBoxStyle` 从 UI 模块下沉 Runtime 数据层，解除 Runtime→UI 反向依赖；对话节点面板按「对话 / 对话框 / 外观 / 生成策略」四分组带间距。
- **样式赋值改资产形式**：`DialogueBoxStyleAsset`（styleKey/template/入退场时长）拖拽引用替代手敲样式键（易拼错、静默失败）；节点面板内联 Foldout 编辑 +「在此节点新建样式资产」；`Resources/StoryDialogueBoxStyles` 目录 Show 时兜底自动注册。
- **属性面板多选批量编辑**：框选同类型节点批量修改任一标量字段——`MultiEditFieldCommand` 一个 Undo 步广播到全部选中节点；混合值整行高亮 + 占位「— 各节点不同 —」（占位项追加进 choices 副本规避 `DropdownField` 构造异常，绝不写回模型）；列表字段（选项/条件）只读汇总；不同类型多选仅提示不可编辑；点空白清空选择、单击多集中节点折叠为单选；同资产多选开放样式内联编辑（改共享资产即批量生效）。
- **数值字段原生序列化绑定**：单选 `bindingPath + Bind(so)` / 多选遍历 `SerializedProperty` 广播写值——拖拽跟手、自动 Undo、不重建面板（根治拖滑块时面板重建抢焦点）。
- **修复**：① Newtonsoft 序列化样式资产触发废弃 `rigidbody` getter 抛 `NotSupportedException`——新增 `UnityObjectRefConverter`（任意 Unity 对象 ↔ 资产 GUID 字符串），导入导出/自动保存/崩溃恢复正确往返；② 首次 Play 首帧角色解析 `[未配置]`——角色 Binder 提前至 `[InitializeOnLoad]` 静态构造注册；③ 单选生成策略选「全局默认」失焦回弹——下拉按索引定位空键并触发重建；④ `StoryView` 自定义 Inspector 漏绘后续新增的样式/打字机字段——对应区块无条件显示。

## \[v1.0] - 2026-08-10

- 新增通用对话框管理系统（独立模块 `com.microbialnet.story.UI`，UGUI + Canvas）：
  - `DialogueBoxManager` 编排核心——生成/关闭/层级（sibling 顺序）/模态输入拦截/生命周期回调/场景卸载清理，懒加载单例可经 `Add Component`（MicrobialNet / Story / Dialogue Box Manager）挂载。
  - `DialogueBox` 实例 + 明确生命周期状态机（Pooled→Spawning→Opening→Open→Closing→Pooled/Destroyed），CanvasGroup 淡入淡出，状态守卫杜绝重复关闭。
  - `DialogueBoxSpec`（声明式请求）/ `DialogueBoxHandle`（令牌）/ `DialogueBoxStyle`（样式注册）/ `DialogueBoxPosition`（ScreenAnchor / WorldFollow / Free 定位策略）/ `IDialogueBoxView` 内容视图契约。
  - 对象池按样式键复用；`CloseTop` / `CloseAll` / `CloseByTag` / `autoCloseSeconds` 配额关闭。
- `StoryView`(TMP) 重构为 `DialogueBoxManager` 适配器：对白/选项/结束/错误翻译为 story-line / story-choice(模态) / story-end / story-error 样式弹出；保留 `IStoryPresenter` 契约与 `OnAdvanceRequested` / `OnChoiceSelected` 回传。新增剧情对话样式视图组件（StoryLineBoxView / StoryChoiceBoxView / StoryMessageBoxView）与运行时模板构建器（StoryBoxTemplates，免 .prefab 资产）。

## \[v0.2] - 2026-08-10

- 对外组件接入 Inspector「Add Component」菜单：为 `StoryFlow`、`StoryGraphRegistry`、`StoryView`(TMP 模块)、`VariableDebugView`(TMP 模块) 显式添加 `[AddComponentMenu("MicrobialNet/Story/...")]`，统一归入 `MicrobialNet / Story` 分组，搜索「story」即可定位。示例用的 `StoryDemoEventBridge`（属 Sample）未加入。
- 文档对外发布化：移除包内记录开发过程的内部文档（需求梳理、界面规范、开发文档、工具级定位决策），仅保留 `README.md` 与两份对外使用指南（事件节点使用指南、系统接口使用指南），并清理其中对内部文档的悬空引用；`README.md` 改写为面向使用者的快速接入说明。
- 代码对外发布化：清除源码与用户可见字符串（MenuItem、Inspector `[Header]`、编辑器弹窗）中的内部里程碑标签（M1/M3/M4/M5/M6-\*、Phase B 等），保留技术含义、不改变行为。

## \[v0.1] - 2026-08-09

- UPM 包骨架搭建：`package.json` + `Packages/com.microbialnet.story` 目录 + asmdef 改名。
- Runtime 程序集 `MicrobialNet.Story.Runtime` → `com.microbialnet.story`；Editor `MicrobialNet.Story.Editor` → `com.microbialnet.story.Editor`。
- 示例资产移入 `Samples~`；示例 C# 仍编译于 `Runtime/Sample`、`Editor/Sample`。
