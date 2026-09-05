using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// P1：让剧情播放宿主（StoryFlow）在进 Play 前自动获得「角色库 → 视图模型」解析器，
    /// 无需在 Inspector 手工挂载角色资产列表。
    ///
    /// 约束：运行时程序集（com.microbialnet.story）不能引用 Editor 程序集（com.microbialnet.story.Editor），因此运行时无法直接扫描
    /// CharacterLibrary（它依赖 AssetDatabase）。做法是由本编辑器工具在 EnteredPlayMode 时把
    /// CharacterLibrary.ResolveViewModel 注册到 StoryConstants.CharacterViewModelResolver（静态委托，
    /// 运行时可调用，委托目标在编辑器 Play 态下仍可达）。
    ///
    /// 编辑态的解析由 StoryGraphWindow 打开时注册（覆盖编辑器内图谱摘要 / 试跑窗口）；此处覆盖
    /// 「未打开剧情窗口直接按 Play」的场景，保证运行时讲述者名 / 颜色 / 立绘解析始终可用。
    ///
    /// 注：正式构建（非编辑器 Play）不包含 Editor 程序集，届时该解析器为 null，运行时回退 [未配置]。
    /// 正式接入需改由 StoryBridge 在构建期把角色数据以 Resources / Addressables 等运行时可达方式提供
    /// （属桥接层范围，本期不实现）。
    /// </summary>
    [InitializeOnLoad]
    public static class StoryCharacterResolverBinder
    {
        static StoryCharacterResolverBinder()
        {
            // 尽早注册：原先等到 EnteredPlayMode 才注册，但 autoStart 的 StoryFlow 会在 Awake 中
            // 同步呈现首句对白（PresentLine→ResolveCharacter），此时委托尚未就绪 → 首帧角色名/立绘解析为 [未配置]，
            // 第二次 Play 才正常（委托已就绪）。改为在编辑器加载/域重载后的静态构造里立即注册，
            // 保证任何 Play 的首帧之前 CharacterViewModelResolver 已可用。
            StoryConstants.CharacterViewModelResolver = CharacterLibrary.ResolveViewModel;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // 进 Play 时再设一次，覆盖「域重载前已有其他解析器」的边缘情况（幂等赋值）。
            if (change == PlayModeStateChange.EnteredPlayMode)
                StoryConstants.CharacterViewModelResolver = CharacterLibrary.ResolveViewModel;
        }
    }
}
