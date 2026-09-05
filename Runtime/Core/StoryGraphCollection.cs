using System;
using System.Collections.Generic;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 运行时图集合（JumpChapter 默认图加载器，零编辑器 / 路径依赖）。
    ///
    /// 宿主（或示例）在装配时把所有图资产 <see cref="StoryGraphAsset"/> 按「标识 → 图」注册进来；
    /// EndNodeData.jumpToChapter 携带的跳转目标标识（章节名或 storyId，语义由宿主决定）经
    /// <see cref="Resolver"/> 查得下一张图资产，由播放器内部经 RuntimeStoryGraph.FromAsset 编译为运行时图。
    /// 框架不预设「章节 → 图」的映射关系，完全交给装配决定，因此单个章节目录下多张图、或跨 storyId 跳转都可由宿主灵活注册。
    ///
    /// 用法：
    /// <code>
    /// var graphs = new StoryGraphCollection();
    /// graphs.Add("Test", testAsset);   // 直接传 StoryGraphAsset
    /// graphs.Add("AAA", aaaAsset);
    /// host.Configure(new StoryFlowConfig { Variables = ..., GraphResolver = graphs.Resolver });
    /// </code>
    /// </summary>
    public sealed class StoryGraphCollection
    {
        private readonly Dictionary<string, StoryGraphAsset> _map = new Dictionary<string, StoryGraphAsset>();

        /// <summary>注册一张图资产（标识建议用 storyId 或章节名）。</summary>
        public void Add(string key, StoryGraphAsset asset)
        {
            if (!string.IsNullOrEmpty(key) && asset != null) _map[key] = asset;
        }

        /// <summary>尝试按标识取图资产。</summary>
        public bool TryGet(string key, out StoryGraphAsset asset)
            => _map.TryGetValue(key, out asset);

        /// <summary>按标识取图资产；找不到返回 null（→ 触发 JumpChapter 明确错误）。</summary>
        public StoryGraphAsset Resolve(string key)
            => _map.TryGetValue(key, out var g) ? g : null;

        /// <summary>可直接作为 StoryFlowConfig.GraphResolver 使用。</summary>
        public Func<string, StoryGraphAsset> Resolver => Resolve;

        /// <summary>已注册图数量（便于引导组件判断是否已注册）。</summary>
        public int Count => _map.Count;
    }
}
