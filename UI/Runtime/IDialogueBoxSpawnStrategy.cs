using System;
using System.Collections.Generic;
using UnityEngine;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 对话框「出现决策」的上下文。在每次 <see cref="DialogueBoxManager.Show"/> 真正落地前由管理器构建并传给策略。
    /// 提供策略所需的全部运行期信息，使策略无需反向依赖管理器即可做出业务相关的定位 / 时机 / 去重决策。
    /// </summary>
    public sealed class DialogueBoxSpawnContext
    {
        /// <summary>本次弹出的样式键（对应 StyleRegistry）。</summary>
        public string styleKey;

        /// <summary>原始请求（含静态 position 作为兜底、layer、tag 等）。策略可读取，但不应长期持有该引用。</summary>
        public DialogueBoxSpec spec;

        /// <summary>业务数据载荷（如 StoryFlow.Line），供策略按剧情上下文决策。</summary>
        public object payload;

        /// <summary>
        /// 当前活动（含正在开 / 关）的对话框列表，已按层级 + 弹出顺序排好。
        /// 可用于避让、级联偏移、去重等业务逻辑——每个元素的 <c>spec.position</c> 即其已解析的最终定位。
        /// </summary>
        public IReadOnlyList<DialogueBox> activeBoxes;

        /// <summary>当前活动框总数（等于 activeBoxes.Count，冗余提供以便使用）。</summary>
        public int totalActive;
    }

    /// <summary>
    /// 策略解析出的「出现决策」。管理器以它覆盖 spec 的静态字段。
    /// <see cref="position"/> 为实际定位（必填）；其余为可选控制面。
    /// </summary>
    public sealed class DialogueBoxSpawnResolution
    {
        /// <summary>最终定位（必填）。传 null 表示沿用 spec.position 静态值。</summary>
        public DialogueBoxPosition position;

        /// <summary>打开延迟（秒）。0 = 立即打开。用于错峰、排队、节拍等「时机」需求。</summary>
        public float delay;

        /// <summary>true = 取消本次弹出（如业务去重 / 条件不满足）。管理器将不显示该框、直接返回 null 句柄。</summary>
        public bool cancel;

        /// <summary>可选层级覆盖；非 null 时覆盖 spec.layer（影响堆叠顺序与模态输入拦截）。</summary>
        public int? layerOverride;

        /// <summary>可选「点击继续时不关闭自身」标记；非 null 时覆盖 spec.persistent。
        /// true = 该框被点击「继续」后只推进剧情、不关闭自己（用于「一串对话保留显示」的级联场景）。</summary>
        public bool? persistent;
    }

    /// <summary>
    /// 对话框生成策略接缝（核心扩展点）。
    /// 把「对话框出现在哪 / 何时出现 / 是否出现」从框架的静态数据，提升为可由业务注入的运行时决策函数。
    ///
    /// 实现可持有状态（记住已用位置、轮换序号、随机区域、分组上下文等），且跨多次 Show 保持同一实例，
    /// 因此能表达「一串对话共享某区域随机出现」「按说话者轮换位置」「去重不重复提示」等序列行为。
    ///
    /// 框架提供 <see cref="DialogueBoxSpawnStrategyAsset"/> 基类（ScriptableObject，可 Inspector 拖拽配置）；
    /// 纯代码策略亦可直接实现本接口。无策略（null）时，管理器沿用 spec 的静态字段，行为完全等价于此前。
    /// </summary>
    public interface IDialogueBoxSpawnStrategy
    {
        /// <summary>
        /// 解析本次弹出的决策。context 提供样式 / 业务数据 / 活动框；返回的决策用于覆盖 spec 静态字段。
        /// 实现应保持对 context 的只读；需要序列 / 历史信息时，请在实现内部用字段自管理
        /// （如 invocation 计数、已用点集合、分组哈希），因为 context 每次都是新构建的。
        /// </summary>
        /// <param name="context">本次弹出的上下文（只读）。</param>
        /// <returns>出现决策；不应返回 null（返回 null 时管理器按「无策略」处理）。</returns>
        DialogueBoxSpawnResolution Resolve(DialogueBoxSpawnContext context);
    }

    /// <summary>
    /// 生成策略的资产基类（ScriptableObject）。供策划 / 美术在 Inspector 创建并配置策略，
    /// 再拖到 <see cref="StoryView"/> 的 defaultSpawnStrategy，或经
    /// <see cref="DialogueBoxManager.RegisterSpawnStrategy"/> 注册到某样式作为默认。
    /// 内置实现见 <see cref="StaticSpawnStrategy"/> / <see cref="RandomRectSpawnStrategy"/>。
    /// 自定义业务策略：继承本类并覆写 <see cref="Resolve"/> 即可，无需改动管理器或剧情代码。
    /// </summary>
    public abstract class DialogueBoxSpawnStrategyAsset : ScriptableObject, IDialogueBoxSpawnStrategy
    {
        /// <summary>策略注册键。节点经 [SpawnStrategyPicker] 选中的即此值；运行时 DialogueBoxManager 按此键解析为策略资产。
        /// 留空则用品资产名作为键。编辑器与运行时须一致（都从 Resources/StorySpawnStrategies 读取）。</summary>
        [SerializeField] public string strategyKey;

        /// <inheritdoc />
        public abstract DialogueBoxSpawnResolution Resolve(DialogueBoxSpawnContext context);
    }
}
