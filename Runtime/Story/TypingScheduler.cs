using System;
using System.Text.RegularExpressions;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 打字机节奏调度：把文本映射为逐「可见字符」延迟数组 float[] D[i]（单位：秒）。
    /// 三种模式共用此入口，引擎（StoryLineBoxView）零侵入——只替换「揭示节奏数据源」。
    ///
    /// 延迟数组按「可见字符」索引（与 TMP <c>maxVisibleCharacters</c> 一致，已剔除富文本标签），
    /// 故形式三的 hand-key 数组（节点 typingDelays）亦按可见字符索引，未来时间编辑器直接读写它即可。
    /// </summary>
    internal static class TypingScheduler
    {
        // 富文本标签 <...> 不计入可见字符，剔除后再生长停顿序列。
        // 注意：极罕见情形（如正文里出现裸 "<" ">"、或 <sprite> 这类被 TMP 算作可见字符的标签）会与索引产生偏差，
        // 对话正文通常不含此类内容，故暂不特殊处理。
        private static readonly Regex RichTag = new Regex("<.*?>", RegexOptions.Compiled);

        /// <summary>剔除富文本标签，得到与 TMP 可见字符一致的纯文本。</summary>
        public static string StripRichText(string text)
            => string.IsNullOrEmpty(text) ? string.Empty : RichTag.Replace(text, string.Empty);

        /// <summary>
        /// 构建逐可见字符延迟序列（长度恒等于可见字符数，便于引擎按索引推进）：
        ///  - <see cref="TypingMode.GlobalSpeed"/>：全为常数 baseInterval；
        ///  - <see cref="TypingMode.Punctuation"/>：baseInterval × 标点倍率（profile 缺失则用内置默认）；
        ///  - <see cref="TypingMode.Custom"/>：优先采用 customDelays（长度须等于可见字符数），否则回退 baseInterval。
        /// </summary>
        public static float[] BuildSchedule(string fullText, TypingMode mode, float baseInterval, DialogueTypingProfile profile, float[] customDelays)
        {
            var visible = StripRichText(fullText);
            int n = visible.Length;
            if (n == 0) return Array.Empty<float>();
            if (baseInterval <= 0f) baseInterval = 0.02f;

            var d = new float[n];
            if (mode == TypingMode.Custom && customDelays != null && customDelays.Length == n)
            {
                // 直接采用手K序列：未来时间编辑器即读写此数组，零额外交互、零破坏。
                Array.Copy(customDelays, d, n);
            }
            else
            {
                bool punct = mode == TypingMode.Punctuation;
                for (int i = 0; i < n; i++)
                {
                    float m = punct ? Multiplier(profile, visible[i]) : 1f;
                    d[i] = baseInterval * m;
                }
            }
            return d;
        }

        private static float Multiplier(DialogueTypingProfile profile, char c)
        {
            if (profile != null) return profile.MultiplierFor(c);
            // 内置默认（与 DialogueTypingProfile 默认值一致，确保未指定资产也有合理节奏）。
            switch (c)
            {
                case '，': case '。': case '！': case '？': case '；': case '…': return 3f;
                case '、': return 1.8f;
                case '：': return 1.5f;
                case '\n': return 4f;
                default: return 1f;
            }
        }
    }
}
