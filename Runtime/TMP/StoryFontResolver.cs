using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 运行时解析一个"覆盖中文"的 TMP 字体资产，供代码创建的 TextMeshProUGUI 使用。
    /// 解析优先级：
    ///   ① 已缓存；
    ///   ② TMP Settings 的 fallback 字体表（推荐把中文字体注册到这里，最稳、打包后也生效）；
    ///   ③ 扫描已加载的 TMP_FontAsset，排除 LiberationSans / TMP 默认字体，取第一个。
    /// 找不到返回 null（此时 TextMeshProUGUI 走 TMP 全局默认字体，仍可由 TMP Settings fallback 回退到中文）。
    /// 设计意图：复用旧版 StoryDemoSceneCreator.FindCjkFontAsset 的"找非 LiberationSans 的中文字体"思路，
    /// 但改为运行时可用（旧版依赖 Editor 的 AssetDatabase，无法在 Play 时调用）。
    /// </summary>
    internal static class StoryFontResolver
    {
        private static TMP_FontAsset _cached;

        public static TMP_FontAsset Resolve()
        {
            if (_cached != null) return _cached;

            // ② TMP Settings 的 fallback 表（最稳，引导用户在 TMP Settings 注册中文字体）
            var settings = TMP_Settings.instance;
            if (settings != null)
            {
                var fb = TMP_Settings.fallbackFontAssets;
                if (fb != null)
                {
                    foreach (var fa in fb)
                    {
                        if (fa != null && !IsExcluded(fa)) { _cached = fa; return _cached; }
                    }
                }
            }

            // ③ 扫描已加载字体资产（兜底）
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var fa in all)
            {
                if (fa == null) continue;
                if (IsExcluded(fa)) continue;
                _cached = fa; return _cached;
            }

            return null;
        }

        private static bool IsExcluded(TMP_FontAsset fa)
        {
            var n = fa != null ? (fa.name ?? string.Empty) : string.Empty;
            return n.Contains("LiberationSans");
        }
    }
}
