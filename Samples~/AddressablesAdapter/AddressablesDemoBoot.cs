using System;
using System.Linq;
using System.Threading.Tasks;
using MicrobialNet.Story.UI; // DialogueBoxManager 注册样式用（示例自洽：勿删）
using UnityEngine;
using UnityEngine.AddressableAssets;
// System(Task/Exception) 与 UnityEngine 均导出 Object，裸 Object 会 CS0104；别名锚定 UnityEngine.Object。
using Object = UnityEngine.Object;

namespace MicrobialNet.Story
{
#if STORY_HOTUPDATE_DEMO
    /// <summary>
    /// 热更演示引导（**静态引导形态**，不挂场景、不碰场景文件）。
    /// 启停由编译宏 STORY_HOTUPDATE_DEMO 控制（一键配置加宏 / 撤销配置移除宏，见 StoryHotUpdateDemoSetup）。
    ///
    /// <para>执行时机：<see cref="RuntimeInitializeOnLoadMethod"/>(<see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>)
    /// 在**首个场景加载之前**执行——早于一切 Awake（含 <see cref="StoryGraphRegistry"/>(-100) 的批量扫描与
    /// <see cref="StoryFlow"/> 启动），天然满足热更契约⑤「Current 引导期一次性设置」。
    /// 相比场景挂 MonoBehaviour 引导：无需修改任何场景资产、不依赖「哪个场景被打开」、Play 任意场景都生效。</para>
    ///
    /// <para><b>初始化策略（Editor Play 的已知坑）</b>：Addressables 的懒初始化 + 同步 WaitForCompletion
    /// 组合在 Editor Play（Use Existing Build）下不可靠——本引导把初始化显式提前：
    /// 先同步 <c>InitializeAsync().WaitForCompletion()</c>（只读本地 catalog，轻量），
    /// 让后续 Registry/StoryView 的同步加载命中已初始化的句柄缓存；失败再由 <see cref="BootAsync"/>
    /// 异步兜底（预热模板重注册 + 图预载重装配）。</para>
    ///
    /// <para><b>诊断日志口径（诚实分通道）</b>：图/模板各自打印「Addressables N + Resources 回落 M」。
    /// 回落数字非零 = 该资产**尚未迁移**（改了立即生效、不参与热更）；Addressables 数字才吃 New Build 快照。
    /// 场景直接引用的入口图（StoryFlow.graph 拖的）不经任何通道——改了必然立即生效，与热更无关。</para>
    ///
    /// <para>组合形态说明：<see cref="ChainedAssetLocator"/> 是**渐进迁移的标准姿势**——
    /// 已搬出 Resources 并标注 Addressable 的资产走 Addressables，其余回落旧通道。每个资产只有一个真实来源，
    /// 不存在双包；迁移完成后想整体切换时，把 fallback 去掉、全量标注即可（见 IStoryAssetLocator 契约的迁移三步）。</para>
    /// </summary>
    public static class AddressablesDemoBoot
    {
        private static readonly string[] BoxKeys = { "story-line", "story-choice", "story-end" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            var primary = new AddressablesStoryAssetLocator(allowSyncBlocking: true); // 本地随包内容：同步毫秒级
            var fallback = new ResourcesStoryAssetLocator();                          // 未标注资产回落
            StoryAssetLocator.Current = new ChainedAssetLocator(primary, fallback);

            // 显式提前初始化（把懒初始化从 Registry/StoryView 的首次同步加载里抽出来）
            bool initOk = TrySyncInitialize();

            // 诊断快照（分通道计数——回落数字非零 = 该部分资产未迁移，热更不覆盖它）
            var abGraphs = primary.LoadAllAssets<StoryGraphAsset>("Story/Graphs");
            var resGraphs = fallback.LoadAllAssets<StoryGraphAsset>("Story/Graphs");
            var abChars = primary.LoadAllAssets<StoryCharacterAsset>("Story/Characters");
            var resChars = fallback.LoadAllAssets<StoryCharacterAsset>("Story/Characters");
            int abBoxes = 0, resBoxes = 0;
            foreach (var k in BoxKeys)
            {
                if (primary.LoadAsset<Object>("StoryDialogueBoxes/" + k) != null) abBoxes++;
                else if (fallback.LoadAsset<Object>("StoryDialogueBoxes/" + k) != null) resBoxes++;
            }
            int abStrats = primary.LoadAllAssets<Object>("StorySpawnStrategies").Length;
            int resStrats = fallback.LoadAllAssets<Object>("StorySpawnStrategies").Length;
            Debug.Log($"[热更演示] 资产通道已切换（Addressables 优先 → Resources 回落）。\n" +
                      $"  初始化：{(initOk ? "同步成功" : "同步失败(转异步兜底)")}\n" +
                      $"  图：Addressables {abGraphs?.Length ?? 0} 张 + Resources 回落 {resGraphs?.Length ?? 0} 张\n" +
                      $"  角色：Addressables {abChars?.Length ?? 0} 个 + Resources 回落 {resChars?.Length ?? 0} 个\n" +
                      $"  对话框模板：Addressables {abBoxes}/{BoxKeys.Length} + Resources 回落 {resBoxes}/{BoxKeys.Length}\n" +
                      $"  生成策略：Addressables {abStrats} 个 + Resources 回落 {resStrats} 个\n" +
                      "  ※ 回落非零 = 该资产未迁移（改了立即生效、不参与热更）；Addressables 数字才吃 New Build 快照。\n" +
                      "  ※ 场景直接引用的入口图不经任何通道——改了必然立即生效。\n" +
                      "  验证：Groups→Build→New Build → Play Mode Script = Use Existing Build → Play。");

            // 异步兜底（幂等）：同步已命中的全部走缓存，无副作用。
            _ = BootAsync(primary, fallback);
        }

        /// <summary>同步初始化 Addressables（读本地 catalog，轻量）。失败不致命——BootAsync 异步兜底。</summary>
        private static bool TrySyncInitialize()
        {
            try
            {
                var init = Addressables.InitializeAsync();
                init.WaitForCompletion();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[热更演示] Addressables 同步初始化异常（转异步兜底）：" + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 异步兜底：预热对话框模板并重注册（覆盖 StoryView 同步失败时落的代码模板），预载图并
        /// BindGraphResolver——这正是 IStoryAssetLocator 契约文档写的「远程宿主引导形态」
        /// （组合既有公开 API，框架零新代码），此处顺带示范真实宿主姿势。分通道计数与同步快照同一口径。
        /// </summary>
        private static async Task BootAsync(IStoryAssetLocator primary, IStoryAssetLocator fallback)
        {
            try { await Addressables.InitializeAsync().Task; }
            catch (Exception e) { Debug.LogWarning("[热更演示] Addressables 异步初始化异常：" + e.Message); }

            int abBox = 0, resBox = 0;
            foreach (var key in BoxKeys)
            {
                var prefab = await primary.LoadAssetAsync<Object>("StoryDialogueBoxes/" + key);
                bool fromAb = prefab != null;
                if (!fromAb)
                    prefab = await fallback.LoadAssetAsync<Object>("StoryDialogueBoxes/" + key);
                if (prefab is GameObject go)
                {
                    DialogueBoxManager.Ensure().RegisterStyle(key, go, 0.18f, 0.18f);
                    if (fromAb) abBox++; else resBox++;
                }
                else
                    Debug.LogWarning($"[热更演示] 模板两通道均缺失：StoryDialogueBoxes/{key}（检查标注与构建顺序）");
            }
            Debug.Log($"[热更演示] 模板注册：Addressables {abBox}/{BoxKeys.Length} + Resources 回落 {resBox}/{BoxKeys.Length}。");

            var abGraphs = await primary.LoadAllAssetsAsync<StoryGraphAsset>("Story/Graphs");
            var resGraphs = await fallback.LoadAllAssetsAsync<StoryGraphAsset>("Story/Graphs");
            var graphs = (abGraphs ?? Array.Empty<StoryGraphAsset>())
                .Concat(resGraphs ?? Array.Empty<StoryGraphAsset>())
                .Where(g => g != null)
                .ToList();
            if (graphs.Count > 0)
            {
                var collection = new StoryGraphCollection();
                foreach (var g in graphs)
                    collection.Add(g.name, g);
                StoryConstants.BindGraphResolver(collection.Resolver);
                Debug.Log($"[热更演示] 图通道就绪：Addressables {abGraphs?.Length ?? 0} 张 + Resources 回落 {resGraphs?.Length ?? 0} 张（JumpChapter 可用）。");
            }
            else
            {
                Debug.LogWarning("[热更演示] 图两通道均为空：0 张。检查 Label \"Story/Graphs\" 标注与 New Build 顺序。");
            }
        }
    }
#endif

    /// <summary>
    /// 组合定位器（迁移过渡形态）：先问主定位器（Addressables 适配器），null / 空数组再回落备定位器（Resources）。
    ///
    /// <para>语义要点：主定位器按契约「失败/未标注 → null / 空数组不抛异常」，正好作为「该键不在新通道」
    /// 的判定信号；回退只在结果为空时发生，已在新通道的资产绝不会被双读（各资产单一来源，无双包、无漂移）。
    /// 「Label 存在但恰好 0 个资产」的边缘情形回退后结果同样为空数组——语义不变。</para>
    /// </summary>
    public sealed class ChainedAssetLocator : IStoryAssetLocator
    {
        private readonly IStoryAssetLocator _primary;
        private readonly IStoryAssetLocator _fallback;

        public ChainedAssetLocator(IStoryAssetLocator primary, IStoryAssetLocator fallback)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        public T LoadAsset<T>(string path) where T : Object
            => _primary.LoadAsset<T>(path) ?? _fallback.LoadAsset<T>(path);

        public T[] LoadAllAssets<T>(string path) where T : Object
        {
            var primary = _primary.LoadAllAssets<T>(path);
            return primary is { Length: > 0 } ? primary : _fallback.LoadAllAssets<T>(path);
        }

        public async Task<T> LoadAssetAsync<T>(string path) where T : Object
        {
            var primary = await _primary.LoadAssetAsync<T>(path);
            return primary != null ? primary : await _fallback.LoadAssetAsync<T>(path);
        }

        public async Task<T[]> LoadAllAssetsAsync<T>(string path) where T : Object
        {
            var primary = await _primary.LoadAllAssetsAsync<T>(path);
            return primary is { Length: > 0 } ? primary : await _fallback.LoadAllAssetsAsync<T>(path);
        }
    }
}
