using MicrobialNet.Story;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>打字机调度测试：三模式统一归约为「逐可见字符 float[]」，富文本剔除，长度不符自动回退。</summary>
    public class TypingSchedulerTests
    {
        private const float Base = 0.1f;

        [Test]
        public void GlobalSpeed_UniformInterval_PerVisibleChar()
        {
            var d = TypingScheduler.BuildSchedule("你好abc", TypingMode.GlobalSpeed, Base, null, null);
            Assert.AreEqual(5, d.Length, "长度=可见字符数");
            CollectionAssert.AreEqual(new[] { Base, Base, Base, Base, Base }, d);
        }

        [Test]
        public void StripRichText_RemovesTags_FromVisibleCount()
        {
            Assert.AreEqual("你好", TypingScheduler.StripRichText("你<b>好</b>"));
            var d = TypingScheduler.BuildSchedule("你<b>好</b>", TypingMode.GlobalSpeed, Base, null, null);
            Assert.AreEqual(2, d.Length, "富文本标签不计入可见字符");
        }

        [Test]
        public void Punctuation_WithDefaultProfile_AppliesMultipliers()
        {
            var profile = ScriptableObject.CreateInstance<DialogueTypingProfile>();
            var d = TypingScheduler.BuildSchedule("好。", TypingMode.Punctuation, Base, profile, null);
            CollectionAssert.AreEqual(new[] { Base, Base * 3f }, d, "默认规则：句号 ×3");

            var d2 = TypingScheduler.BuildSchedule("好、", TypingMode.Punctuation, Base, profile, null);
            Assert.AreEqual(Base * 1.8f, d2[1], 1e-5, "顿号 ×1.8");

            var d3 = TypingScheduler.BuildSchedule("好：", TypingMode.Punctuation, Base, profile, null);
            Assert.AreEqual(Base * 1.5f, d3[1], 1e-5, "冒号 ×1.5");

            var d4 = TypingScheduler.BuildSchedule("好a", TypingMode.Punctuation, Base, profile, null);
            Assert.AreEqual(Base, d4[1], "非标点字符倍率 1");
        }

        [Test]
        public void Punctuation_NullProfile_UsesBuiltInRules()
        {
            // 内置默认（profile==null）与资产默认一致，另含换行 ×4。
            var d = TypingScheduler.BuildSchedule("好。\n", TypingMode.Punctuation, Base, null, null);
            CollectionAssert.AreEqual(new[] { Base, Base * 3f, Base * 4f }, d);
        }

        [Test]
        public void Punctuation_CustomProfile_RulesOverrideDefaults()
        {
            var profile = ScriptableObject.CreateInstance<DialogueTypingProfile>();
            profile.rules.Clear();
            profile.rules.Add(new TypingPunctuationRule { chars = "！", multiplier = 9f });
            var d = TypingScheduler.BuildSchedule("好！", TypingMode.Punctuation, Base, profile, null);
            CollectionAssert.AreEqual(new[] { Base, Base * 9f }, d, "自定义规则优先，且不再命中内置标点");
        }

        [Test]
        public void Custom_UsesDelays_WhenLengthMatchesVisibleChars()
        {
            var delays = new[] { 0.05f, 0.2f, 0.01f };
            var d = TypingScheduler.BuildSchedule("三个字", TypingMode.Custom, Base, null, delays);
            CollectionAssert.AreEqual(delays, d, "长度匹配时手K序列原样采用");
        }

        [Test]
        public void Custom_FallsBackToUniform_WhenLengthMismatch()
        {
            var delays = new[] { 0.05f, 0.2f }; // 长度 2 ≠ 可见字符 3
            var d = TypingScheduler.BuildSchedule("三个字", TypingMode.Custom, Base, null, delays);
            CollectionAssert.AreEqual(new[] { Base, Base, Base }, d, "长度不符自动回退均匀间隔（不崩溃）");
        }

        [Test]
        public void Custom_NullDelays_FallsBackToUniform()
        {
            var d = TypingScheduler.BuildSchedule("ab", TypingMode.Custom, Base, null, null);
            CollectionAssert.AreEqual(new[] { Base, Base }, d);
        }

        [Test]
        public void EmptyText_ProducesEmptySchedule()
        {
            Assert.AreEqual(0, TypingScheduler.BuildSchedule("", TypingMode.GlobalSpeed, Base, null, null).Length);
        }

        [Test]
        public void InvalidBaseInterval_ClampedToMinimum()
        {
            var d = TypingScheduler.BuildSchedule("a", TypingMode.GlobalSpeed, 0f, null, null);
            Assert.AreEqual(0.02f, d[0], "非正基础间隔钳制到 0.02s");
        }
    }
}
