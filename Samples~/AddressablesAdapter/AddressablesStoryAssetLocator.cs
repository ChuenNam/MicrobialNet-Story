using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
// using System(供 Exception/Type/Array) 与 UnityEngine 均导出 Object，约束里的裸 Object 会 CS0104 歧义；
// 别名统一锚定 UnityEngine.Object（与 IStoryAssetLocator 的 where T : Object 同一类型）。
using Object = UnityEngine.Object;

namespace MicrobialNet.Story
{
    /// <summary>
    /// <see cref="IStoryAssetLocator"/> 的 Addressables 适配器（示例代码，随包 Samples~ 分发、按需导入，
    /// 不构成包对 com.unity.addressables 的硬依赖）。导入本示例后，启动引导期替换全局定位器即可整体切换通道：
    /// <code>
    /// StoryAssetLocator.Current = new AddressablesStoryAssetLocator(allowSyncBlocking: true);
    /// </code>
    ///
    /// <para><b>映射约定（与接缝契约对齐，详见 IStoryAssetLocator 文档「Addressables 适配映射」）：</b></para>
    /// <list type="bullet">
    /// <item>LoadAsset(path) → address 同名：资产入组时 address = 逻辑键（如 "StoryDialogueBoxes/story-line"）。</item>
    /// <item>LoadAllAssets(path) → Label 同名：键空间（如 "Story/Graphs"）下全部资产打同名 Label——
    /// Addressables 无「按目录枚举」API，目录语义靠 Label 约定补齐；标签纪律由宿主构建管线保证，
    /// 拼错的后果 = 该空间批量加载返回空（记一条 Warning）。</item>
    /// </list>
    ///
    /// <para><b>同步语义（契约②）</b>：<paramref name="allowSyncBlocking"/> 为 true（默认，本地随包内容）时，
    /// 同步成员对未就绪句柄经 WaitForCompletion 等待（本地 bundle 毫秒级）；WebGL 不支持同步等待，异常被捕获、
    /// 返回 null/空数组，操作继续后台进行、完成后经句柄缓存命中。为 false（含远程热更内容的工程）时，
    /// 同步成员对未就绪资产立即返回 null/空数组、绝不阻塞——远程内容一律走异步成员并建议引导期预载
    /// （图批量预载示例见 StoryGraphRegistry 文档注释的组合路径）。</para>
    ///
    /// <para><b>失败语义（契约③）</b>：地址/标签不存在、下载失败 → 完成于 null/空数组并记一条 Warning，不抛异常；
    /// 失败句柄不缓存（下次调用重试），in-flight 句柄保留（其 await 方仍会拿到结果）。
    /// **键缺失走预检**——加载前先查 catalog（<c>Locate(key, typeof(T))</c>，与加载路径同口径），
    /// 不存在则直接按「未配置」返回，不发起失败操作（Addressables 对缺失键的每次加载都打一条
    /// InvalidKeyException 错误日志，异常拦得住、日志拦不住；组合定位器的回落场景尤其需要静默）。</para>
    ///
    /// <para><b>生命周期（契约④）</b>：已完成句柄按（类型+键）缓存、全部持有不释放——经本接缝加载的资产视为
    /// 常驻有效，重复加载命中同一句柄、不产生 Addressables 引用计数累积。非线程安全（主线程调用；
    /// Addressables 的 Task 完成于主循环，异步延续不会跨界）。</para>
    ///
    /// <para><b>迁移三步（避免双包陷阱）</b>：① 资产搬出 Assets/Resources/ 目录（Resources 又标 Addressable =
    /// 构建打两份且热更后版本漂移）；② 标 Addressable（address = 逻辑键；批量空间加同名 Label）；
    /// ③ 启动引导期换 Current。</para>
    /// </summary>
    public sealed class AddressablesStoryAssetLocator : IStoryAssetLocator
    {
        private readonly bool _allowSyncBlocking;

        // 句柄缓存：key = (加载形态|类型|逻辑键)。常驻持有（契约④），失败项移除、in-flight 保留。
        private readonly Dictionary<string, AsyncOperationHandle> _handles
            = new Dictionary<string, AsyncOperationHandle>();

        // 缺失键告警去重：同一键只提醒一次（「未配置」是部署态而非每次调用的错误）。
        private readonly HashSet<string> _warnedMissing = new HashSet<string>();

        /// <param name="allowSyncBlocking">
        /// 同步成员是否允许阻塞等待未就绪资产。本地随包内容（默认 true）＝ WaitForCompletion 毫秒级返回；
        /// 含远程内容的工程建议 false——同步调用对未就绪资产返回 null/空数组（契约②），远程内容一律走异步成员
        /// 并在引导期预载。
        /// </param>
        public AddressablesStoryAssetLocator(bool allowSyncBlocking = true)
        {
            _allowSyncBlocking = allowSyncBlocking;
        }

        // ══ 同步成员（契约②：仅本地/已就绪资产）══════════════════

        public T LoadAsset<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = AssetKey(typeof(T), path);

            if (!_handles.TryGetValue(key, out var op))
            {
                if (!KeyExists<T>(path))
                {
                    WarnMissing(path);
                    return null; // 键不在 catalog：契约③按「未配置」完成，不发起失败操作
                }
                op = Addressables.LoadAssetAsync<T>(path); // 地址同名映射
                _handles[key] = op;
            }
            return SyncComplete(op, key, path) is T asset ? asset : null;
        }

        public T[] LoadAllAssets<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<T>();
            string key = ListKey(typeof(T), path);

            if (!_handles.TryGetValue(key, out var op))
            {
                if (!KeyExists<T>(path))
                {
                    WarnMissing(path);
                    return Array.Empty<T>(); // 键不在 catalog：契约③按「未配置」完成，不发起失败操作
                }
                op = Addressables.LoadAssetsAsync<T>(path, null); // Label 同名映射（目录语义）
                _handles[key] = op;
            }
            return SyncList<T>(op, key, path);
        }

        // ══ 异步成员（远程/下载内容的主通道）════════════════

        public async Task<T> LoadAssetAsync<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = AssetKey(typeof(T), path);

            if (!_handles.TryGetValue(key, out var op))
            {
                if (!KeyExists<T>(path))
                {
                    WarnMissing(path);
                    return null; // 键不在 catalog：契约③按「未配置」完成，不发起失败操作
                }
                op = Addressables.LoadAssetAsync<T>(path);
                _handles[key] = op;
            }
            try
            {
                await op.Task; // 失败时抛异常 → catch（契约③：完成于 null）
                return op.Status == AsyncOperationStatus.Succeeded && op.Result is T asset ? asset : null;
            }
            catch (Exception e)
            {
                if (op.IsDone) _handles.Remove(key); // 失败不缓存（下次重试）；in-flight 保留
                Warn(path, e);
                return null;
            }
        }

        public async Task<T[]> LoadAllAssetsAsync<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<T>();
            string key = ListKey(typeof(T), path);

            if (!_handles.TryGetValue(key, out var op))
            {
                if (!KeyExists<T>(path))
                {
                    WarnMissing(path);
                    return Array.Empty<T>(); // 键不在 catalog：契约③按「未配置」完成，不发起失败操作
                }
                op = Addressables.LoadAssetsAsync<T>(path, null); // Label 同名映射（目录语义）
                _handles[key] = op;
            }
            try
            {
                await op.Task;
                return op.Status == AsyncOperationStatus.Succeeded
                    ? ToArray<T>(op.Result)
                    : Array.Empty<T>();
            }
            catch (Exception e)
            {
                if (op.IsDone) _handles.Remove(key);
                Warn(path, e);
                return Array.Empty<T>();
            }
        }

        // ══ 内部工具 ═════════════════════════════════════

        /// <summary>
        /// 预检（消噪）：键（address/Label）在当前 catalog 是否存在 <typeparamref name="T"/> 可用的位置。
        /// 组合定位器「回落」场景下，未迁移的键若直接发起加载，Addressables 会为**每个失败操作**打一条
        /// InvalidKeyException 错误日志（异常我们拦得住，日志拦不住——加载路径内部在抛出前就已 LogException）。
        /// 预检让缺失键按契约③「未配置 → null/空 + 一条 Warning」静默完成，不产生失败操作。
        /// 实现逐 locator 调 <c>Locate(key, typeof(T))</c>——与加载路径的键查找同一口径（含类型过滤）。
        /// 懒初始化未完成（尚无任何 locator）时放行 true，走原加载路径由 Addressables 自行初始化，
        /// 预检不改变任何既有行为语义。
        /// </summary>
        private static bool KeyExists<T>(string path)
        {
            bool anyLocator = false;
            foreach (var locator in Addressables.ResourceLocators)
            {
                anyLocator = true;
                if (locator.Locate(path, typeof(T), out var locations)
                    && locations != null && locations.Count > 0)
                    return true;
            }
            return !anyLocator; // 无 locator（未初始化）：放行
        }

        /// <summary>缺失键告警（同一键只提醒一次——「未配置」是部署态，不该每次调用刷屏）。</summary>
        private void WarnMissing(string path)
        {
            if (_warnedMissing.Add(path))
                Debug.LogWarning($"[AddressablesStoryAssetLocator] 键不在 catalog（未标注或未迁移）：{path} —— " +
                                 "按「未配置」返回 null/空数组（组合定位器场景由回落通道兜底，重标注后 New Build 即可命中）");
        }

        private object SyncComplete(AsyncOperationHandle op, string key, string path)
        {
            if (op.IsDone)
            {
                if (op.Status == AsyncOperationStatus.Succeeded) return op.Result;
                _handles.Remove(key); // 失败句柄不缓存：下次调用重试
                return null;
            }
            if (!_allowSyncBlocking) return null; // 未就绪且禁阻塞：契约②（不抛、不等）

            try
            {
                op.WaitForCompletion(); // 本地 bundle 毫秒级；WebGL 抛异常走 catch
            }
            catch (Exception e)
            {
                // WebGL 无同步等待（或等待异常）：操作仍在后台进行，句柄保留供后续命中/await。
                Warn(path, e);
                return null;
            }
            if (op.Status == AsyncOperationStatus.Succeeded) return op.Result;
            _handles.Remove(key);
            return null;
        }

        private T[] SyncList<T>(AsyncOperationHandle op, string key, string path) where T : Object
        {
            var result = SyncComplete(op, key, path);
            return result is T single ? new[] { single } : ToArray<T>(result);
        }

        private static T[] ToArray<T>(object result) where T : Object
        {
            if (result is IList<T> list)
            {
                var arr = new T[list.Count];
                for (int i = 0; i < list.Count; i++) arr[i] = list[i];
                return arr;
            }
            return result is T single ? new[] { single } : Array.Empty<T>();
        }

        private static string AssetKey(Type t, string path) => "a|" + t.FullName + "|" + path;
        private static string ListKey(Type t, string path) => "l|" + t.FullName + "|" + path;

        private static void Warn(string path, Exception e)
            => Debug.LogWarning($"[AddressablesStoryAssetLocator] 加载失败（地址/标签缺失或未就绪）：{path} —— {e.Message}");
    }
}
