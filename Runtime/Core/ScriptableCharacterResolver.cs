using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 基于角色资产（ScriptableObject）<see cref="StoryCharacterAsset"/> 的运行时角色解析器默认实现。
    ///
    /// 适用于「角色资产随包发布」的场景：宿主 / 示例直接把可用角色资产列表注入即可，
    /// 无需编辑器 AssetDatabase 扫描（编辑器态扫描由 Editor 侧 CharacterLibrary 负责）。
    /// 也提供 <see cref="FromResources"/> 兜底：在指定 Resources 子目录下自动加载全部角色资产。
    ///
    /// 解析不到时返回 default（isValid=false），由 StoryConstants.ResolveCharacter 落到 [未配置] 占位符，
    /// 绝不回退裸 ID。
    /// </summary>
    public sealed class ScriptableCharacterResolver : IStoryCharacterResolver
    {
        private readonly Dictionary<string, StoryConstants.CharacterViewModel> _map
            = new Dictionary<string, StoryConstants.CharacterViewModel>();

        /// <summary>用显式角色资产列表构造（推荐：宿主在装配时把已加载的资产传入）。</summary>
        public ScriptableCharacterResolver(IEnumerable<StoryCharacterAsset> assets)
        {
            if (assets == null) return;
            foreach (var a in assets)
            {
                if (a == null || string.IsNullOrEmpty(a.characterId)) continue;
                _map[a.characterId] = ToViewModel(a);
            }
        }

        /// <summary>
        /// 从资产键空间加载全部 StoryCharacterAsset 作为兜底（经 <see cref="StoryAssetLocator"/> 接缝，
        /// 默认即 Resources：资产须位于 Resources 下才会被打包加载）。<paramref name="relativePath"/>
        /// 为逻辑键路径，默认 "Story/Characters"。远程/异步交付场景：先
        /// <c>await LoadAllAssetsAsync</c> 预载，再用列表构造器组装（构造器签名已就绪）。
        /// </summary>
        public static ScriptableCharacterResolver FromResources(string relativePath = "Story/Characters")
        {
            var assets = StoryAssetLocator.Current.LoadAllAssets<StoryCharacterAsset>(relativePath);
            return new ScriptableCharacterResolver(assets);
        }

        public StoryConstants.CharacterViewModel Resolve(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return default;
            return _map.TryGetValue(characterId, out var vm) ? vm : default;
        }

        private static StoryConstants.CharacterViewModel ToViewModel(StoryCharacterAsset a)
            => new StoryConstants.CharacterViewModel
            {
                displayName = a.displayName,
                colorHex = a.colorHex,
                avatar = a.avatar,
                isValid = true,
            };
    }
}
