using System;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 对话框句柄（令牌）。调用方只持有它，不直接持有 GameObject，
    /// 从而避免对话框关闭/回收后产生悬空引用。句柄可安全判等。
    /// </summary>
    public sealed class DialogueBoxHandle
    {
        internal DialogueBox _box;
        internal DialogueBoxManager _manager;
        internal int _instanceId;

        /// <summary>当前生命周期状态。</summary>
        public DialogueBoxState State => _box != null ? _box.State : DialogueBoxState.Destroyed;

        /// <summary>是否已关闭（回收或销毁）。关闭后本句柄即失效。</summary>
        public bool IsClosed => State == DialogueBoxState.Destroyed || State == DialogueBoxState.Pooled;

        /// <summary>分组标签（来自 spec.tag）。</summary>
        public string Tag { get; internal set; }

        /// <summary>层级（来自 spec.layer）。</summary>
        public int Layer { get; internal set; }

        /// <summary>只读暴露本框的弹出规格（含 persistent 等标志），供内容视图按业务标志调整交互（如是否点击自关）。</summary>
        public DialogueBoxSpec Spec => _box?.spec;

        /// <summary>请求关闭：播放退场动画后回收/销毁。重复调用安全（Closing 态为空操作）。</summary>
        public void Close() => _manager?.RequestClose(this, immediate: false);

        /// <summary>立即强制关闭：跳过动画，直接回收/销毁。</summary>
        public void ForceClose() => _manager?.RequestClose(this, immediate: true);

        public override bool Equals(object obj) => obj is DialogueBoxHandle h && h._instanceId == _instanceId;

        public override int GetHashCode() => _instanceId;

        public override string ToString() => $"DialogueBoxHandle(#{_instanceId}, {State})";
    }
}
