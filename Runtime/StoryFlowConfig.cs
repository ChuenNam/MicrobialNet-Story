using System;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情系统统一装配入口（即插即用接缝）。
    ///
    /// 宿主（或示例）只需填好下列实现，交给 <see cref="StoryFlow.Configure(StoryFlowConfig)"/>
    /// （或自行 new StoryPlayer + 绑定），剧情逻辑即零改动接入真实系统。
    ///
    /// 任何字段留空时，宿主 / 框架会提供合理的默认实现，因此空配置也能跑 demo / 单元测试：
    /// - <see cref="Variables"/> 为空 → 示例用 InMemoryVariableProvider；
    /// - <see cref="Events"/> / <see cref="Text"/> 为空 → 默认事件打印 / 文本透传；
    /// - <see cref="Characters"/> 为空 → 运行时回退 [未配置] 占位符（编辑器 Play 仍可经编辑器注入解析）；
    /// - <see cref="Save"/> 为空 → 示例用 PlayerPrefsSaveStore。
    /// </summary>
    public sealed class StoryFlowConfig
    {
        /// <summary>变量读写提供者（条件求值 / 赋值节点）。必填（无合理全局默认）。</summary>
        public IStoryVariableProvider Variables;

        /// <summary>事件处理器（事件节点派发）。可空 → 默认仅打印。</summary>
        public IStoryEventHandler Events;

        /// <summary>文本 / 本地化提供者。可空 → 默认透传（identity）。</summary>
        public IStoryTextProvider Text;

        /// <summary>
        /// 运行时角色解析器。可空 → 运行时解析不到讲述者（回退 [未配置]），
        /// 但提供后打包客户端也能解析讲述者真名 / 立绘 / 颜色，无需编辑器注入。
        /// </summary>
        public IStoryCharacterResolver Characters;

        /// <summary>进度存档落地。可空 → 默认 PlayerPrefsSaveStore。</summary>
        public IStorySaveStore Save;

        /// <summary>
        /// 图加载器（JumpChapter 章节跳转 / 多图加载）。可空 → 运行时遇 JumpChapter 报明确错误（不崩溃）。
        /// 给定跳转目标标识（章节名或 storyId，语义由宿主决定），返回目标 StoryGraphAsset；找不到返回 null。
        /// 推荐用 <see cref="StoryGraphCollection"/> 在装配时注册所有图，零路径约定、零编辑器依赖。
        /// </summary>
        public Func<string, StoryGraphAsset> GraphResolver;
    }
}
