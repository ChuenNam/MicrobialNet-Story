using System;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 文本来源提供者（本地化解析接缝）。播放器在抛出对白 / 选项文本前，会按「节点 ID 派生的本地化 key」
    /// （对话正文 = "{nodeId}.text"，选项 = "{choiceId}.opt.{optionId}"，与编辑器 CSV/Excel 导出规则一致）
    /// 调用 <see cref="ResolveText"/>；命中则返回译文，未命中（返回 null 或空）播放器回退显示原文（identity）。
    ///
    /// 未注入（传 null）时播放器直接用原文。宿主可注入 <see cref="LocalizationTextProvider"/>（基于主表 <see cref="StoryLocalizationTable"/>）
    /// 实现运行时多语言切换；也可注入自定义实现做全局文本变换。
    /// </summary>
    public interface IStoryTextProvider
    {
        /// <summary>
        /// 按本地化 key 解析展示文本。返回值：译文文本；或 null/空表示「无译文」，播放器将回退显示原文。
        /// 注意：传入的是本地化 key 而非原文，实现内部应据 key 查表，而非简单透传。
        /// </summary>
        string ResolveText(string key);
    }
}
