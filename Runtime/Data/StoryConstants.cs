using System;
using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>剧情系统的内置常量与特殊讲述者。</summary>
    public static class StoryConstants
    {
        /// <summary>旁白讲述者 ID（无需角色资产即可使用）。</summary>
        public const string NarrationId = "__narration__";

        /// <summary>未知讲述者 ID（显示为 ???，常见于尚未揭露身份的角色）。</summary>
        public const string UnknownId = "__unknown__";

        /// <summary>玩家自己讲述者 ID（显示为「玩家自己」，指代当前操控的玩家角色）。</summary>
        public const string SelfId = "__self__";

        /// <summary>判断给定讲述者 ID 是否为内置特殊讲述者（旁白/未知/玩家自己）。内置特殊讲述者不引用角色资产，不应被「引用缺失」类校验当作缺失角色报错。</summary>
        public static bool IsBuiltInSpeaker(string id)
            => id == NarrationId || id == UnknownId || id == SelfId;

        /// <summary>
        /// 讲述者视图模型（P2）。解析产物，携带运行时 UI 直接可用的全部展示信息：
        /// 显示名、主题色（十六进制）、立绘（Sprite，可空）。
        /// 角色 ID 仅作数据标识，绝不进入玩家视线；本结构只承载「该显示什么」。
        /// </summary>
        public struct CharacterViewModel
        {
            /// <summary>显示名（讲述者真正显示的名字）。</summary>
            public string displayName;
            /// <summary>主题色（如 "#378ADD"）。空表示无主题色（UI 用默认）。</summary>
            public string colorHex;
            /// <summary>头像（可选）。为空时 UI 以 colorHex 色块占位。</summary>
            public Sprite avatar;
            /// <summary>是否解析成功。false 表示未找到对应角色（缺失配置）。</summary>
            public bool isValid;
        }

        /// <summary>
        /// 缺失展示名的兜底占位符（角色未注册 / 变量未定义时显示）。
        /// 刻意用「方括号 + 中文」且不可能是真实姓名，便于一眼识别为配置错误；
        /// 与 <see cref="UnknownId"/> 刻意保留的「???」（叙事性未知身份）区分开。
        /// </summary>
        public const string MissingPlaceholder = "[未配置]";

        /// <summary>
        /// 讲述者视图模型解析器（Editor 侧注入）。Runtime 不知道角色库，由 Editor 启动 / 进 Play 前注册
        /// （如 CharacterLibrary.ResolveViewModel），把 characterId 解析为完整视图模型（名字 + 颜色 + 立绘）。
        /// 未注册或解析不到时回退 [未配置] 占位符（不再回退裸 ID），并输出一次告警。
        /// </summary>
        public static Func<string, CharacterViewModel> CharacterViewModelResolver;

        /// <summary>
        /// 把运行时角色解析器接口实现绑定到全局解析委托（即插即用接缝）。
        /// 宿主适配层 / 示例在运行时调用它，即可让 <see cref="ResolveCharacter"/> 解析到角色真名 / 立绘 / 颜色，
        /// 不再依赖编辑器注入的 CharacterLibrary（编辑器 Play 仍由 StoryCharacterResolverBinder 覆盖）。
        /// 传 null 时不改动现有委托。
        /// </summary>
        public static void BindCharacterResolver(IStoryCharacterResolver resolver)
        {
            if (resolver == null) return;
            CharacterViewModelResolver = resolver.Resolve;
        }

        /// <summary>
        /// 图加载器（JumpChapter 章节跳转 / 多图加载）。Runtime 不知道有哪些图，由宿主 / 引导组件
        /// （如 <see cref="StoryGraphRegistry"/>）在启动时注册，把跳转标识解析为下一张 <see cref="StoryGraphAsset"/>。
        /// 未注册时为 null（<see cref="StoryPlayer"/> 遇 JumpChapter 报明确错误）。
        /// </summary>
        public static Func<string, StoryGraphAsset> GraphResolver;

        /// <summary>
        /// 绑定运行时图加载器（即插即用接缝）。宿主适配层 / 引导组件在启动时调用，
        /// 即可让 <see cref="StoryPlayer"/> 经 <see cref="StoryFlowConfig.GraphResolver"/> 或本静态委托解析跳转目标图，
        /// 不必在 new StoryPlayer 时显式传参。传 null 时不改动现有委托。
        /// </summary>
        public static void BindGraphResolver(Func<string, StoryGraphAsset> resolver)
        {
            if (resolver == null) return;
            GraphResolver = resolver;
        }

        /// <summary>
        /// 将讲述者 ID 解析为视图模型（含显示名 / 主题色 / 立绘）。
        /// 运行时 UI（StoryView）据此渲染讲述者名颜色与立绘；编辑器图谱摘要 / 试跑窗口也复用它。
        /// </summary>
        public static CharacterViewModel ResolveCharacter(string id)
        {
            if (id == NarrationId) return BuiltIn("旁白");
            if (id == UnknownId) return BuiltIn("???");
            if (id == SelfId) return BuiltIn("玩家自己");
            if (string.IsNullOrEmpty(id)) return BuiltIn("旁白");
            if (CharacterViewModelResolver != null)
            {
                var vm = CharacterViewModelResolver(id);
                if (vm.isValid) return vm;
            }
            WarnMissing(id);
            return new CharacterViewModel { displayName = MissingPlaceholder, isValid = false };
        }

        private static CharacterViewModel BuiltIn(string name)
            => new CharacterViewModel { displayName = name, isValid = true };

        /// <summary>将讲述者 ID 转换为可读显示名（兼容旧调用；P2 起建议直接用 <see cref="ResolveCharacter"/> 取完整视图模型）。</summary>
        public static string SpeakerDisplayName(string id) => ResolveCharacter(id).displayName;

        /// <summary>
        /// 变量名解析器（Editor 侧注入）。Runtime 不知道变量黑板，由 Editor 启动时注册
        /// （如当前剧情图资产的 variables 列表），把 variableId 解析为可读名字。
        /// 未注册时回退 <see cref="MissingPlaceholder"/> 占位符（不再回退裸 ID），并输出一次告警。
        /// </summary>
        public static Func<string, string> VariableNameResolver;

        /// <summary>将变量 ID 转换为可读显示名（节点摘要、试跑预览等共用，避免只显示难辨认的变量 id）。</summary>
        public static string VariableName(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (VariableNameResolver != null)
            {
                var resolved = VariableNameResolver(id);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }
            WarnMissing(id);
            return MissingPlaceholder;
        }

        /// <summary>判断给定显示名是否为缺失占位符（视图可做红色高亮等提示）。</summary>
        public static bool IsMissing(string displayName) => displayName == MissingPlaceholder;

        /// <summary>
        /// 尝试解析 "#RRGGBB" / "#RRGGBBAA" 十六进制颜色（运行时 UI 上色用）。无效格式返回 false。
        /// 解析失败时 color 置为 white。
        /// </summary>
        public static bool TryParseColor(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(hex)) return false;
            var h = hex.TrimStart('#');
            if (h.Length != 6 && h.Length != 8) return false;
            if (!uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
            if (h.Length == 6)
            {
                color = new Color(
                    ((v >> 16) & 0xFF) / 255f,
                    ((v >> 8) & 0xFF) / 255f,
                    (v & 0xFF) / 255f,
                    1f);
            }
            else
            {
                color = new Color(
                    ((v >> 24) & 0xFF) / 255f,
                    ((v >> 16) & 0xFF) / 255f,
                    ((v >> 8) & 0xFF) / 255f,
                    (v & 0xFF) / 255f);
            }
            return true;
        }

        private static readonly HashSet<string> _warnedMissing = new HashSet<string>();
        private static void WarnMissing(string id)
        {
            if (_warnedMissing.Add(id))
                Debug.LogWarning($"[Story] 展示名解析失败：ID=\"{id}\" 未在角色/变量注册表找到，已显示占位符 \"{MissingPlaceholder}\"。请检查角色资产是否加载 / 变量是否定义。");
        }
    }
}
