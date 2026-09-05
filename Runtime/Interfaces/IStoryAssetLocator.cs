using System.Threading.Tasks;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情相关资产的加载定位器接缝（资产空间的端口-适配器）。默认实现走 UnityEngine.Resources；
    /// 宿主可整体替换（如 Addressables / 远程热更适配器）以支持分包、按需下载与热更。
    /// 框架内全部运行时资产发现统一经此获取：StoryDialogueBoxes（对话框模板 Prefab）、
    /// StoryDialogueBoxStyles（样式资产）、StorySpawnStrategies（生成策略资产）、
    /// Story/Graphs（剧情图批量扫描）、Story/Characters（角色资产兜底）。
    ///
    /// <para><b>热更契约（适配器实现须遵守）：</b></para>
    /// <list type="bullet">
    /// <item><b>path 是逻辑键，不是物理路径承诺</b>：形如 "Story/Graphs/main"（无扩展名、/ 分隔），
    /// 与 Resources 相对路径同形。内容物理位置可迁移（包内 → AB/远程），键保持稳定；
    /// 内容更新不得改键，同键新版本覆盖旧版本（避免热更后新旧并存错乱）。</item>
    /// <item><b>同步/异步分工</b>：同步成员只服务本地或已就绪资产；远程适配器对「未就绪」资产
    /// 返回 null/空数组（禁止阻塞等网络——WebGL 上同步等待不可用），需要网络的内容一律走异步成员。</item>
    /// <item><b>失败不抛异常</b>：缺失/下载失败/校验不过 → null 或空数组；坏资源由适配器记日志后过滤，
    /// 剧情系统按「未注册/未配置」降级，绝不因单个资产失败中断。</item>
    /// <item><b>资产生命周期归定位器所有</b>：经本接缝加载的资产视为常驻有效（剧情元数据量级小），
    /// 接口不提供 Release；GB 级美术资产不经此通道（走宿主自己的加载系统）。</item>
    /// <item><b>Current 引导期一次性设置</b>：资产空间是应用级单全局（不同于每 StoryFlow 实例的
    /// 角色解析器），应在启动引导阶段、首次剧情加载前设置完成；运行中替换的正确性由宿主自证，非线程安全。</item>
    /// </list>
    ///
    /// <para><b>Addressables 适配映射（推荐口径，完整实现见包 Samples~/AddressablesAdapter）：</b></para>
    /// <list type="bullet">
    /// <item><b>单资产 → 地址同名</b>：LoadAsset(path) 映射 Addressables.LoadAssetAsync&lt;T&gt;(address: path)。
    /// 资产入组时 address 必须等于逻辑键（如 "StoryDialogueBoxes/story-line"）。</item>
    /// <item><b>批量 → Label 同名约定</b>：LoadAllAssets(path) 映射 Addressables.LoadAssetsAsync&lt;T&gt;(label: path)。
    /// Addressables 没有「按目录枚举」API，目录语义靠 Label 约定补齐：键空间（如 "Story/Graphs"）下全部资产
    /// 打同名 Label。标签纪律由宿主构建管线保证（打包前批处理打标/校验），拼错的后果 = 该空间批量加载返回空。</item>
    /// <item><b>双包陷阱（迁移必避）</b>：资产留在 Assets/Resources/ 目录又标 Addressable = 构建打两份
    /// （resources.assets 一份 + bundle 一份），且热更只替换 bundle 份 → 新旧版本并存、Resources 通道永远读旧值，
    /// 表现为「改了没生效」。迁移第一步永远是先搬出 Resources/ 目录。</item>
    /// <item><b>迁移顺序三步</b>：① 资产搬出 Assets/Resources/（防双包）；② 标 Addressable（address = 逻辑键；
    /// 需要批量加载的空间再加同名 Label）；③ 启动引导期换 Current（含远程内容的工程同步成员传
    /// allowSyncBlocking:false，远程内容一律异步预载）。</item>
    /// </list>
    /// </summary>
    public interface IStoryAssetLocator
    {
        /// <summary>按逻辑键加载单个资产（同步：仅本地/已就绪资产）。找不到返回 null，不抛异常。</summary>
        T LoadAsset<T>(string path) where T : Object;

        /// <summary>加载键空间下全部指定类型资产（同步）。无则返回空数组（不为 null）。</summary>
        T[] LoadAllAssets<T>(string path) where T : Object;

        /// <summary>
        /// 按逻辑键异步加载单个资产。热更适配器可在此覆盖「下载 → 校验 → 加载」全流程；
        /// 缺失/失败完成于 null 结果（不抛异常）。本地实现（Resources）为立即完成的同步包装。
        /// </summary>
        Task<T> LoadAssetAsync<T>(string path) where T : Object;

        /// <summary>
        /// 异步加载键空间下全部指定类型资产（热更适配器可用目录/标签批量拉取）。
        /// 无则完成于空数组（不为 null）。本地实现为立即完成的同步包装。
        /// </summary>
        Task<T[]> LoadAllAssetsAsync<T>(string path) where T : Object;
    }

    /// <summary>
    /// 默认实现：UnityEngine.Resources（同步语义；异步成员为立即完成的同步包装——本地无网络语义，
    /// 仅占住契约）。宿主替换 <see cref="StoryAssetLocator.Current"/> 时整体换掉。
    /// </summary>
    public sealed class ResourcesStoryAssetLocator : IStoryAssetLocator
    {
        public T LoadAsset<T>(string path) where T : Object => Resources.Load<T>(path);

        public T[] LoadAllAssets<T>(string path) where T : Object
        {
            var loaded = Resources.LoadAll<T>(path);
            return loaded ?? System.Array.Empty<T>();
        }

        public Task<T> LoadAssetAsync<T>(string path) where T : Object
            => Task.FromResult(LoadAsset<T>(path));

        public Task<T[]> LoadAllAssetsAsync<T>(string path) where T : Object
            => Task.FromResult(LoadAllAssets<T>(path));
    }

    /// <summary>
    /// 定位器全局默认（可替换，置 null 回落 Resources）。框架不缓存实例，替换即时生效；
    /// 推荐在启动引导期一次性设置（见 <see cref="IStoryAssetLocator"/> 热更契约）。
    /// </summary>
    public static class StoryAssetLocator
    {
        private static IStoryAssetLocator _current = new ResourcesStoryAssetLocator();

        /// <summary>当前定位器。赋 null 时回落默认 Resources 实现。</summary>
        public static IStoryAssetLocator Current
        {
            get => _current;
            set => _current = value ?? new ResourcesStoryAssetLocator();
        }
    }
}
