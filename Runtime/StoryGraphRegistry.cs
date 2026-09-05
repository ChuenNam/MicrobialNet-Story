using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情图注册引导组件（Runtime，纯运行时、零编辑器依赖，打包后同样生效）。
    ///
    /// 解决「图加载器必须用代码注册」的繁琐：挂载本组件后，只需在 Inspector 拖入若干
    /// <see cref="StoryGraphAsset"/>（或指定一个 Resources 子目录让其自动扫描），即可把
    /// 「跳转标识 → 图」的映射自动注册到 <see cref="StoryConstants.GraphResolver"/>，供
    /// <see cref="StoryPlayer"/> 在 JumpChapter 时解析目标图。宿主几乎零代码接入。
    ///
    /// <para><b>key 约定</b>：每个图资产同时以「storyId」和「资产文件名」注册（命中任一即可），
    /// 避免编辑者在结束节点 <c>jumpToChapter</c> 字段纠结该填哪个。可在 Inspector 关掉
    /// <see cref="useStoryIdAsKey"/> 仅用文件名。</para>
    ///
    /// <para><b>执行时机</b>：Awake 注册（早于 StoryFlow.Start 读取），保证跳转可用；
    /// 用 <see cref="DefaultExecutionOrder"/>(-100) 进一步确保早于其它 Start 逻辑。</para>
    ///
    /// <para><b>资产通道</b>：批量扫描经 <see cref="StoryAssetLocator"/>（<see cref="IStoryAssetLocator"/> 接缝）
    /// 执行——宿主替换为 Addressables / 热更适配器后，本组件与运行时其余加载点（模板/样式/策略/角色）
    /// 一并切换，无需改代码。Awake 为同步语义：远程交付场景不依赖本组件扫描，应在引导期经
    /// <c>LoadAllAssetsAsync</c> 预载图资产，再以 <see cref="StoryGraphCollection"/> +
    /// <see cref="StoryConstants.BindGraphResolver"/> 自行装配，或把预载结果直接拖入本组件的 graphs 列表。</para>
    /// </summary>
    [AddComponentMenu("MicrobialNet/Story/Story Graph Registry", 10)]
    [DefaultExecutionOrder(-100)]
    public sealed class StoryGraphRegistry : MonoBehaviour
    {
        [Header("显式注册：直接拖入图资产")]
        [SerializeField] private StoryGraphAsset[] graphs;

        [Header("批量注册：Resources 子目录（留空则跳过）")]
        [SerializeField] private string resourcesSubPath = "Story/Graphs";

        [Header("key 约定")]
        [SerializeField] private bool useStoryIdAsKey = true;

        private void Awake()
        {
            var collection = new StoryGraphCollection();

            if (graphs != null)
                foreach (var g in graphs)
                    if (g != null) RegisterOne(collection, g);

            if (!string.IsNullOrEmpty(resourcesSubPath))
            {
                // 经资产定位接缝扫描（默认 Resources；宿主替换为 Addressables/热更适配器后整体切换，无需改本组件）。
                var loaded = StoryAssetLocator.Current.LoadAllAssets<StoryGraphAsset>(resourcesSubPath);
                if (loaded != null)
                    foreach (var g in loaded)
                        if (g != null) RegisterOne(collection, g);
            }

            if (collection.Count > 0)
                StoryConstants.BindGraphResolver(collection.Resolver);
        }

        private void RegisterOne(StoryGraphCollection collection, StoryGraphAsset asset)
        {
            collection.Add(asset.name, asset); // 资产文件名兜底 key
            if (useStoryIdAsKey && asset.meta != null && !string.IsNullOrEmpty(asset.meta.storyId))
                collection.Add(asset.meta.storyId, asset); // storyId 语义 key；与文件名相同则字典自动覆盖为同一图
        }
    }
}
