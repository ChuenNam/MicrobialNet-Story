using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 样式定义：样式键 → 预制体模板 + 入场/退场时长。
    /// 由管理器持有（也可由 ScriptableObject 资产 <see cref="DialogueBoxStyleAsset"/> 批量导入）。
    /// 原位于 UI 模块，为支持节点直接引用样式资产（避免 Runtime→UI 反向依赖）已迁至运行时数据层。
    /// </summary>
    public sealed class DialogueBoxStyle
    {
        /// <summary>预制体或运行时模板 GameObject（克隆源）。</summary>
        public GameObject template;

        /// <summary>入场动画时长（秒）。</summary>
        public float introDuration = 0.2f;

        /// <summary>退场动画时长（秒）。</summary>
        public float outroDuration = 0.2f;

        /// <summary>留存过渡占比（0~1）：新框等上一框离场进行到该占比再出现（样式资产特有能力）。</summary>
        public float retainRatio = 0.8f;

        public DialogueBoxStyle() { }

        public DialogueBoxStyle(GameObject template, float intro = 0.2f, float outro = 0.2f, float retain = 0.8f)
        {
            this.template = template;
            this.introDuration = intro;
            this.outroDuration = outro;
            this.retainRatio = retain;
        }
    }
}
