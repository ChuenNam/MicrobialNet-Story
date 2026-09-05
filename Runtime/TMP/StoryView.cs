using System;
using System.Collections.Generic;
using UnityEngine;
using MicrobialNet.Story;
using MicrobialNet.Story.UI;

namespace MicrobialNet.Story
{
    /// <summary>对话框出现逻辑的来源：用户配置数据（四个 position 字段）或 生成策略资产。</summary>
    public enum StoryViewSpawnMode
    {
        /// <summary>用户配置数据：使用三个 position 字段（linePosition / choicePosition / endPosition）。</summary>
        Config,
        /// <summary>生成策略：使用 defaultSpawnStrategy 资产决定出现位置/时机（优先级最高）。</summary>
        Strategy
    }

    /// <summary>
    /// 剧情对话视图（TMP 实现）。实现 <see cref="IStoryPresenter"/>：把对白 / 选项 / 结束翻译成
    /// 对 <see cref="DialogueBoxManager"/> 的弹出请求（story-line / story-choice / story-end 样式），
    /// 并把玩家点击（继续 / 选项）经接口事件回传引擎。
    ///
    /// 本组件只做「翻译 + 路由」：所有呈现、层级、池化、多样式、生命周期由 DialogueBoxManager 负责，
    /// 因此剧情逻辑无需任何改动即可获得上述能力。保留 IStoryPresenter 契约不变。
    /// </summary>
    [AddComponentMenu("MicrobialNet/Story/Story View (TMP)", 20)]
    public sealed class StoryView : MonoBehaviour, IStoryPresenter
    {
        public event Action OnAdvanceRequested;
        public event Action<string> OnChoiceSelected;

        private bool _stylesReady;

        [Header("出现逻辑")]
        [Tooltip("对话框出现逻辑的来源：用户配置数据（三个 position 字段）或 生成策略资产。两者二选一；" +
                 "选择哪一类，Inspector 就只显示/只应用那一类的序列化配置。")]
        public StoryViewSpawnMode spawnMode = StoryViewSpawnMode.Config;

        [Header("对话框位置")]
        [Tooltip("对白框位置。ScreenAnchor=屏幕 9 宫格 + 偏移；Free=完全由 Prefab 自身布局决定（美术可在 Prefab 自由摆位）。" +
                 "WorldFollow 模式需跟随目标，剧情对白当前未接线（Line 不携带 Transform），暂勿使用。")]
        public DialogueBoxPosition linePosition = DialogueBoxPosition.BottomCenter();

        [Tooltip("选项框位置。默认底部居中；可改为顶部 / 自由布局（Free 模式由 Prefab 决定）。")]
        public DialogueBoxPosition choicePosition = DialogueBoxPosition.BottomCenter();

        [Tooltip("结束 / 提示框位置。默认底部居中。")]
        public DialogueBoxPosition endPosition = DialogueBoxPosition.BottomCenter();

        [Header("生成策略（可选）")]
        [Tooltip("生成策略资产（仅「出现逻辑 = 生成策略」模式生效）。赋值时四类框统一使用该策略决定出现位置/时机。" +
                 "从 Project 右键 MicrobialNet/Story/对话框策略 创建（如矩形随机 / 级联随机）。" +
                 "业务可继承 DialogueBoxSpawnStrategyAsset 自定义；设为 null 或不选该模式则忽略。")]
        public DialogueBoxSpawnStrategyAsset defaultSpawnStrategy;

        [Header("样式资产（可选，覆盖内置默认）")]
        [Tooltip("拖入 DialogueBoxStyleAsset 资产即可让对应类型对话框按该样式呈现（含模板 + 入场/退场时长）。" +
                 "留空则回退内置默认样式（story-line / story-choice / story-end）。" +
                 "从 Project 右键 MicrobialNet/Story/对话框样式 创建；节点编辑器内也可一键新建并直接引用。")]
        public DialogueBoxStyleAsset lineStyleAsset;

        [Header("打字机（可选）")]
        [Tooltip("打字机标点节奏配置（仅「标点节奏」模式生效）。拖入后，对白节点选「标点节奏」时按其中倍率停顿。" +
                 "留空则用内置默认标点倍率（，。！？；… ×3、、 ×1.8、： ×1.5、换行 ×4）。")]
        public DialogueTypingProfile typingProfile;
        public DialogueBoxStyleAsset choiceStyleAsset;
        public DialogueBoxStyleAsset endStyleAsset;

        private void Awake() => EnsureStyles();

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;
            var mgr = DialogueBoxManager.Ensure();
            // 资产字段优先；未指派时回退内置默认样式（Prefab 优先，否则代码模板）。
            RegisterStyleAssetOrFallback(mgr, lineStyleAsset, "story-line", StoryBoxTemplates.BuildLineTemplate);
            RegisterStyleAssetOrFallback(mgr, choiceStyleAsset, "story-choice", StoryBoxTemplates.BuildChoiceTemplate);
            RegisterStyleAssetOrFallback(mgr, endStyleAsset, "story-end", () => StoryBoxTemplates.BuildMessageTemplate(false));
        }

        /// <summary>资产字段优先：拖入 DialogueBoxStyleAsset 则按其注册；否则按内置 key 经 StoryAssetLocator 回退 Prefab / 代码模板。</summary>
        private void RegisterStyleAssetOrFallback(DialogueBoxManager mgr, DialogueBoxStyleAsset asset, string fallbackKey, Func<GameObject> buildFallback)
        {
            if (asset != null) { mgr.RegisterStyle(asset); return; }
            var prefab = StoryAssetLocator.Current.LoadAsset<GameObject>("StoryDialogueBoxes/" + fallbackKey);
            if (prefab != null)
            {
                mgr.RegisterStyle(fallbackKey, prefab, 0.18f, 0.18f);
                Debug.Log($"[StoryView] 对话框样式 [{fallbackKey}] 使用 Prefab 模板：StoryDialogueBoxes/{fallbackKey}");
                return;
            }
            Debug.LogWarning($"[StoryView] 未找到对话框 Prefab：StoryDialogueBoxes/{fallbackKey}.prefab，" +
                             $"回退内置代码模板。可用菜单 MicrobialNet/Story/生成对话框模板 Prefab 生成可定制 Prefab。");
            var tmpl = buildFallback();
            tmpl.transform.SetParent(mgr.transform, false);
            mgr.RegisterStyle(fallbackKey, tmpl, 0.18f, 0.18f);
        }

        public void ShowLine(StoryFlow.Line line)
        {
            EnsureStyles();
            var hint = line.appearance;
            if (hint != null && hint.styleAsset != null) DialogueBoxManager.Ensure().RegisterStyle(hint.styleAsset);
            var style = (hint != null && !string.IsNullOrEmpty(hint.styleKeyOverride)) ? hint.styleKeyOverride : lineStyleAsset?.styleKey ?? "story-line";
            var pos = (hint != null && hint.overridePosition && hint.position != null) ? hint.position : linePosition;
            IDialogueBoxSpawnStrategy strat = null;
            if (hint != null && !string.IsNullOrEmpty(hint.spawnStrategyKey))
                strat = DialogueBoxManager.Ensure().GetSpawnStrategy(hint.spawnStrategyKey);
            bool persistent = hint != null && hint.persistentOverride.HasValue && hint.persistentOverride.Value;
            float baseInterval = line.Speed > 0.01f ? 1f / (line.Speed * 50f) : 0.02f;
            float[] schedule = TypingScheduler.BuildSchedule(line.Text, line.TypingMode, baseInterval, typingProfile, line.TypingDelays);
            DialogueBoxManager.Ensure().Show(new DialogueBoxSpec
            {
                styleKey = style,
                position = pos,
                layer = 10,
                spawnStrategy = strat ?? ((spawnMode == StoryViewSpawnMode.Strategy) ? defaultSpawnStrategy : null),
                persistent = persistent,
                payload = new StoryLineBoxView.Payload { line = line, onAdvance = () => OnAdvanceRequested?.Invoke(), schedule = schedule }
            });
        }

        public void ShowChoices(IReadOnlyList<StoryFlow.Choice> choices)
        {
            EnsureStyles();
            var hint = choices != null && choices.Count > 0 ? choices[0].appearance : null;
            if (hint != null && hint.styleAsset != null) DialogueBoxManager.Ensure().RegisterStyle(hint.styleAsset);
            var style = (hint != null && !string.IsNullOrEmpty(hint.styleKeyOverride)) ? hint.styleKeyOverride : choiceStyleAsset?.styleKey ?? "story-choice";
            IDialogueBoxSpawnStrategy strat = null;
            if (hint != null && !string.IsNullOrEmpty(hint.spawnStrategyKey))
                strat = DialogueBoxManager.Ensure().GetSpawnStrategy(hint.spawnStrategyKey);
            // 覆盖位置与 ShowLine 同规则：节点（含表节点「表内默认」注入）勾选覆盖位置时用 hint.position，否则用全局 choicePosition
            var pos = (hint != null && hint.overridePosition && hint.position != null) ? hint.position : choicePosition;

            // 「带文字」选择节点的对白（Prompt）打字机 schedule：取第一个非空 Prompt 及其打字参数构建逐字符延迟；
            // 视图在 Prompt 打字结束后才生成选项按钮（选项不立即出现）。空 Prompt / 无打字配置 = 即时全显（现状）。
            string promptText = null;
            float promptSpeed = 0.5f;
            TypingMode promptMode = TypingMode.GlobalSpeed;
            if (choices != null)
                foreach (var c0 in choices)
                    if (c0 != null && !string.IsNullOrEmpty(c0.Prompt))
                    {
                        promptText = c0.Prompt;
                        promptSpeed = c0.PromptSpeed > 0.01f ? c0.PromptSpeed : 0.5f;
                        promptMode = c0.PromptTypingMode;
                        break;
                    }
            float[] promptSchedule = null;
            if (!string.IsNullOrEmpty(promptText))
            {
                float baseInterval = promptSpeed > 0.01f ? 1f / (promptSpeed * 50f) : 0.02f;
                promptSchedule = TypingScheduler.BuildSchedule(promptText, promptMode, baseInterval, typingProfile, null);
            }

            DialogueBoxManager.Ensure().Show(new DialogueBoxSpec
            {
                styleKey = style,
                position = pos,
                layer = 10,
                modal = true,
                spawnStrategy = strat ?? ((spawnMode == StoryViewSpawnMode.Strategy) ? defaultSpawnStrategy : null),
                payload = new StoryChoiceBoxView.Payload
                {
                    choices = choices,
                    promptSchedule = promptSchedule,
                    onChoose = id => OnChoiceSelected?.Invoke(id)
                }
            });
        }

        public void ShowEnd(bool showText, string text)
        {
            if (!showText) return; // 不展示结束文本：自然结束，不弹任何框
            EnsureStyles();
            DialogueBoxManager.Ensure().Show(new DialogueBoxSpec
            {
                styleKey = endStyleAsset?.styleKey ?? "story-end",
                position = endPosition,
                layer = 10,
                spawnStrategy = (spawnMode == StoryViewSpawnMode.Strategy) ? defaultSpawnStrategy : null,
                payload = new StoryMessageBoxView.Payload { title = string.Empty, body = text ?? string.Empty, isError = false }
            });
        }
    }
}
