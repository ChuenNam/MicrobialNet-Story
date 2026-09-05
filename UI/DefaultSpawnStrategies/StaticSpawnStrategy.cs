using System.Collections.Generic;
using UnityEngine;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 静态策略：把资产上配置的 position（缺省时回退 spec.position）原样返回。
    /// 等价于「不使用策略」时的默认行为；作为 <see cref="StoryView"/> 各 position 字段的等价物，
    /// 可拖到 Inspector 替代逐框配置，从而把「逐框静态位置」升级为「一组可复用的定位策略」。
    /// </summary>
    [CreateAssetMenu(fileName = "StaticSpawnStrategy", menuName = "MicrobialNet/Story/对话框策略/静态定位", order = 0)]
    public sealed class StaticSpawnStrategy : DialogueBoxSpawnStrategyAsset
    {
        [SerializeField] private DialogueBoxPosition position;

        /// <inheritdoc />
        public override DialogueBoxSpawnResolution Resolve(DialogueBoxSpawnContext context)
        {
            var pos = position ?? context.spec.position ?? DialogueBoxPosition.BottomCenter();
            return new DialogueBoxSpawnResolution { position = pos };
        }
    }
}
