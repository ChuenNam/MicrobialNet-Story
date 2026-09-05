using System;
using System.Collections.Generic;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>单条标点节奏规则：命中的字符集合 + 相对基础间隔的倍率。</summary>
    [Serializable]
    public struct TypingPunctuationRule
    {
        /// <summary>触发该倍率的字符集合（任一命中即应用，按列表顺序取首个匹配）。</summary>
        public string chars;
        /// <summary>相对基础间隔的倍率（>1 表示停顿更久；<=0 视为 1）。</summary>
        public float multiplier;
    }

    /// <summary>
    /// 打字机标点节奏配置（可选 ScriptableObject）。仅作用于 <see cref="TypingMode.Punctuation"/>：
    /// 在节点「语速」决定的基础间隔上，对命中标点的可见字符施加倍率停顿。
    /// 留空时 <see cref="TypingScheduler"/> 使用内置默认规则（与下方默认值一致）。
    /// 若需全局统一节奏，可在 StoryView 指定一份共用的本资产；节点级 Custom 手K不受此影响。
    /// </summary>
    [CreateAssetMenu(fileName = "DialogueTypingProfile", menuName = "MicrobialNet/Story/打字机节奏配置")]
    public class DialogueTypingProfile : ScriptableObject
    {
        [Tooltip("基础间隔倍率规则（按列表顺序命中首个匹配的字符）。常用 CJK 标点已给默认倍率。")]
        public List<TypingPunctuationRule> rules = new List<TypingPunctuationRule>
        {
            new TypingPunctuationRule { chars = "，。！？；…", multiplier = 3f },
            new TypingPunctuationRule { chars = "、", multiplier = 1.8f },
            new TypingPunctuationRule { chars = "：", multiplier = 1.5f },
            //new TypingPunctuationRule { chars = "\n", multiplier = 4f },
        };

        /// <summary>返回给定字符的停顿倍率（无规则命中或资产为空时返回 1）。</summary>
        public float MultiplierFor(char c)
        {
            if (rules == null) return 1f;
            foreach (var r in rules)
            {
                if (!string.IsNullOrEmpty(r.chars) && r.chars.IndexOf(c) >= 0)
                    return r.multiplier > 0 ? r.multiplier : 1f;
            }
            return 1f;
        }
    }
}
