using System;
using System.Collections.Generic;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情表现层契约（Runtime 内定义，不依赖 TextMeshPro / UGUI）。
    /// 剧情引擎经此接口驱动「显示」并收集「用户输入」，与具体 UI 技术彻底解耦。
    ///
    /// TMP 版 <see cref="StoryView"/> 只是其中一种实现（位于可选模块 com.microbialnet.story.TMP）；
    /// 宿主可自实现本接口（UGUI / IMGUI / 自绘任意 UI），或传入 null 做 headless（测试 / 服务端）。
    /// 这样 Runtime 既保持「去 TMP 依赖」，又对 TMP 与非 TMP 都兼容——契约统一，接线只写一次。
    /// </summary>
    public interface IStoryPresenter
    {
        /// <summary>呈现一句对白（讲述者名 + 正文 + 速度 + 立绘视图模型）。</summary>
        void ShowLine(StoryFlow.Line line);

        /// <summary>呈现玩家可见选项列表（OptionId + 文案）。视图应渲染为可点击按钮。</summary>
        void ShowChoices(IReadOnlyList<StoryFlow.Choice> choices);

        /// <summary>剧情结束（到达 End 节点）。showText=false 时表示「不展示任何结束框」，视图应直接忽略；text 为可选的结束展示文本（showText=true 时呈现）。</summary>
        void ShowEnd(bool showText, string text);

        /// <summary>视图请求「推进一句对白」（点击对白面板时由视图触发）。</summary>
        event Action OnAdvanceRequested;

        /// <summary>视图请求「选择一个选项」（点击选项按钮时由视图触发，参数为 OptionId）。</summary>
        event Action<string> OnChoiceSelected;
    }
}
