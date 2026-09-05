namespace MicrobialNet.Story
{
    /// <summary>
    /// 运行时角色解析器接口（即插即用接缝）。
    ///
    /// 把 characterId 解析为 <see cref="StoryConstants.CharacterViewModel"/>（显示名 + 主题色 + 立绘）。
    /// 框架不内置角色库；运行时程序集不引用编辑器程序集，因此角色资产必须由「运行时可达」的方式提供
    /// （Resources 加载 / 显式列表 / Addressables），不能依赖编辑器 AssetDatabase 扫描
    /// （编辑器态扫描由 Editor 侧的 CharacterLibrary 负责，并经 BindCharacterResolver 注入）。
    ///
    /// 宿主适配层（或示例）实现此接口后，经 <see cref="StoryConstants.BindCharacterResolver"/>
    /// 注入即可让打包后的正式客户端也能解析讲述者真名/立绘/颜色，不再回退 [未配置] 占位符。
    /// </summary>
    public interface IStoryCharacterResolver
    {
        /// <summary>
        /// 解析角色视图模型。解析不到时必须返回 <c>default</c>（isValid=false），
        /// 绝不能返回裸 ID——角色 ID 只是数据标识，绝不能泄漏到玩家画面。
        /// </summary>
        StoryConstants.CharacterViewModel Resolve(string characterId);
    }
}
