using System;
using System.Collections.Generic;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情进度快照（可序列化）。包含恢复播放所需的全部状态：
    /// 当前所在节点 ID + 全部变量的值快照。
    ///
    /// 设计要点：
    /// - 变量值统一以「类型 + 原始字符串」保存，恢复时经 <see cref="ValueParser.Parse"/> 还原，
    ///   避免在快照里携带 object / 装箱类型导致反序列化歧义。
    /// - 用 System.Serializable（即 Unity 序列化所用的 [Serializable]），以便 <see cref="JsonUtility"/> 正确序列化。
    /// </summary>
    [Serializable]
    public sealed class StorySnapshot
    {
        /// <summary>存档格式版本（迁移钩子：旧档缺此字段 = 旧格式，Restore 时告警；后续格式演进在此挂迁移）。</summary>
        public string version = "1";

        /// <summary>剧情图标识（用于校验存档与当前剧情是否匹配；兼容旧档）。</summary>
        public string storyId;

        /// <summary>当前所在图的标识（JumpChapter 跨图流程后记录真实所在图）。恢复时据此切回正确的图。</summary>
        public string graphId;

        /// <summary>恢复后应进入的节点 ID（对白节点会重新抛出 OnLine；选项节点重新抛出 OnChoices）。</summary>
        public string currentNodeId;

        /// <summary>变量值快照。</summary>
        public List<VarEntry> variables = new List<VarEntry>();

        [Serializable]
        public sealed class VarEntry
        {
            public string id;
            public VariableType type;
            public string raw;
        }
    }
}
