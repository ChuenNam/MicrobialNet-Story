namespace MicrobialNet.Story.UI
{
    /// <summary>对话框生命周期状态。状态机由 DialogueBox 驱动，管理器只作观察与编排。</summary>
    public enum DialogueBoxState
    {
        /// <summary>在对象池中，未被使用。</summary>
        Pooled,
        /// <summary>已实例化，准备入场（尚未交互）。</summary>
        Spawning,
        /// <summary>入场动画播放中。</summary>
        Opening,
        /// <summary>可交互。</summary>
        Open,
        /// <summary>退场动画播放中。</summary>
        Closing,
        /// <summary>已销毁（非回收）。</summary>
        Destroyed
    }
}
